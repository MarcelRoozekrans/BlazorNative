package io.blazornative.jni

import com.sun.jna.Memory
import com.sun.jna.Pointer
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

/**
 * Phase 3.0d: thin lifecycle wrapper for the NativeAOT BlazorNative.Runtime —
 * init → register frame callback → mount. Replaced the wasmtime/WasiHost boot
 * path in MainActivity; Phase 3.0e deleted that era, so this is the only one.
 *
 * Holds the [NativeBindings.FrameCallback] strongly for the .so's lifetime —
 * JNA callbacks are GC-eligible; if this object were collected, the native
 * side would invoke a freed trampoline. Callers must therefore keep the
 * BlazorNativeRuntime instance itself strongly referenced (e.g. an Activity
 * field) for as long as the callback is registered.
 *
 * Exception posture: a throw inside the callback would be swallowed by JNA
 * (stderr + silent frame drop), so the body is wrapped: adapter/consumer
 * errors are routed to [onError] and the frame is dropped LOUDLY. This class
 * lives in the shared main source set, so it takes a pluggable [onError]
 * instead of calling android.util.Log directly — the Activity passes Log.e.
 */
class BlazorNativeRuntime(
    // Called with every decoded frame. THREAD SET: {the start() caller thread
    // (the mount's synchronous first frame), the BlazorNative-Dispatch lane
    // (re-render frames delivered inside dispatchEvent)} — consumers must be
    // safe for both (Android: post to the main thread before touching views).
    private val onFrame: (RenderFrame) -> Unit,
    // (JVM-only default — Android callers must pass a Log-based sink: stderr
    // goes to /dev/null on Android, not logcat.)
    private val onError: (String, Throwable) -> Unit = { msg, t -> System.err.println("$msg: $t") },
) {
    private val callback = object : NativeBindings.FrameCallback {
        override fun invoke(frame: Pointer) {
            try {
                // NativeFrameAdapter.read copies everything (arena memory is
                // valid only during this invocation), so onFrame receives a
                // fully detached RenderFrame.
                onFrame(NativeFrameAdapter.read(frame))
            } catch (t: Throwable) {
                onError("frame dropped (adapter/consumer threw)", t)
            }
        }
    }

    /**
     * Phase 3.1: strong ref to the shell-bridge registrar (same GC rule as
     * [callback] — its six JNA trampolines must outlive the registration).
     * BridgeRegistrar additionally parks every registered instance in a
     * process-lifetime list, so a re-run of [start] (Activity recreation)
     * keeps the superseded registration alive too, as the re-registration
     * rule in BridgeProtocolNative.cs demands.
     */
    private var bridgeRegistrar: BridgeRegistrar? = null

    /**
     * Phase 3.2 — THE dispatch lane. Single-thread daemon executor named
     * `BlazorNative-Dispatch`; every UI event enters the .NET runtime through
     * it.
     *
     * THREADING CONTRACT: ALL post-boot .NET entry serializes through this
     * lane — UI listeners must never call the ABI directly. One lane resolves
     * both documented hazards: the renderer keeps single-threaded access
     * post-boot, and handler-triggered bridge ops (e.g. SharedPreferences
     * `commit()`) stay off the main thread — no StrictMode violation. The
     * lane is deliberately SINGLE (renderer affinity); a slow handler blocks
     * later events. RE-LEDGERED — Phase 4.2 triage item 2 (ledger of record:
     * docs/plans/2026-07-11-phase-4.2-hardening-triage.md): the single lane
     * IS the design; the real fix for slow handlers is the async-offload
     * owned by triage item 1's revisit trigger, not more lanes. Daemon
     * thread: the lane never blocks process exit.
     *
     * ONE LANE PER RUNTIME: each BlazorNativeRuntime owns its own lane thread,
     * so constructing runtimes repeatedly (Activity recreation) accumulates
     * daemon threads unless the superseded instance is [retire]d first —
     * same accumulation posture as the never-disposed component instances
     * (see the recreation contract on [start]).
     */
    private val dispatchLane: ExecutorService = Executors.newSingleThreadExecutor { r ->
        Thread(r, "BlazorNative-Dispatch").apply { isDaemon = true }
    }

    /**
     * Dispatches a UI event to the .NET handler registered under [handlerId]
     * (harvested from an AttachEvent patch), asynchronously on the
     * `BlazorNative-Dispatch` lane (see [dispatchLane]'s threading contract —
     * call this from UI listeners; never call the ABI directly).
     *
     * Args cross the ABI as FlatJson `{"name":…}` / `{"name":…,"payload":…}`
     * (the payload key is OMITTED when [payload] is null — .NET-side absent
     * key maps to null EventArgs payload). Any re-render's frame callback has
     * completed before the underlying export returns (synchronous dispatch
     * contract in Exports.cs).
     *
     * Non-zero return codes are routed to [onError] (the tap is dropped):
     * rc 1 = nothing mounted (shell bug — dispatch before start()), rc 2 =
     * dispatch faulted (handler/re-render/frame delivery threw), rc 3 =
     * malformed args (writer bug — should be impossible from this API).
     */
    fun dispatchEvent(handlerId: Int, eventName: String, payload: String? = null) {
        dispatchLane.execute {
            try {
                val rc = dispatchCore(handlerId, eventName, payload)
                if (rc != 0) {
                    onError(describeDispatchFailure(rc, handlerId, eventName), IllegalStateException("dispatch_event rc=$rc"))
                }
            } catch (t: Throwable) {
                onError("dispatch_event(handlerId=$handlerId, '$eventName') threw on the dispatch lane", t)
            }
        }
    }

    /**
     * Test seam: same marshalling as [dispatchEvent] but runs INLINE on the
     * calling thread and returns the raw rc (JVM tests assert the 0/1/2/3
     * contract directly and their calling thread IS the dispatch-discipline
     * thread). Production callers use [dispatchEvent] — the lane is the
     * threading contract.
     */
    internal fun dispatchEventBlocking(handlerId: Int, eventName: String, payload: String? = null): Int =
        dispatchCore(handlerId, eventName, payload)

    /** Builds the FlatJson args (payload key omitted when null), NUL-terminates,
     * and crosses the ABI. */
    private fun dispatchCore(handlerId: Int, eventName: String, payload: String?): Int {
        val args = if (payload == null) mapOf("name" to eventName)
                   else mapOf("name" to eventName, "payload" to payload)
        val argsJson = FlatJson.write(args).toByteArray(Charsets.UTF_8) + 0
        return NativeBindings.INSTANCE.blazornative_dispatch_event(handlerId.toLong(), argsJson)
    }

    /** Human-readable onError message per non-zero rc — rc 2 carries the
     * frozen Gate 1 wording (internal so the message contract is unit-tested). */
    internal fun describeDispatchFailure(rc: Int, handlerId: Int, eventName: String): String = when (rc) {
        1 -> "dispatch_event(handlerId=$handlerId, '$eventName') → rc 1: no session/nothing mounted"
        2 -> "dispatch_event(handlerId=$handlerId, '$eventName') → rc 2: dispatch faulted — " +
            "the handler, the resulting re-render, or frame delivery threw " +
            "(detail on native stderr — reproduce on desktop JVM to see it)"
        3 -> "dispatch_event(handlerId=$handlerId, '$eventName') → rc 3: malformed/NULL args " +
            "OR handlerId > int.MaxValue (writer bug — should be impossible from this API)"
        else -> "dispatch_event(handlerId=$handlerId, '$eventName') → undocumented rc $rc"
    }

    /**
     * Boots the runtime: init → register frame callback → mount. The first
     * frame callback fires synchronously INSIDE the mount call (sync mount
     * contract), on the calling thread.
     *
     * ACTIVITY-RECREATION CONTRACT: calling start() a second time in the same
     * process (e.g. from a recreated Activity) is safe TODAY —
     * blazornative_init is idempotent, callback re-registration is last-wins,
     * and re-mounting adds a NEW component instance on the process-global
     * session (old instances are never disposed and accumulate natively).
     * The PRIMARY recreation hazard (Phase 3.2) is CONCURRENT .NET ENTRY: a
     * dispatch queued on the OLD runtime's lane can still be executing —
     * inside the dll — while the NEW runtime's start() runs init/register/
     * mount on its own thread. That violates the renderer's single-threaded
     * access contract (two threads in the .NET session at once) and races the
     * callback re-registration against the in-flight dispatch's frame
     * delivery. Secondary hazard: an old-lane dispatch that survives the
     * window delivers its re-render frame to the OLD onFrame (a destroyed
     * Activity's views). BOTH are closed the same way: [retire] the old
     * runtime (drains its lane) BEFORE constructing/starting the replacement.
     * Frames still fire only inside host calls (mount OR dispatch_event — no
     * free-running frame source), so a retired runtime is fully quiescent.
     * Concurrent start() calls (multiple threads) are unguarded — callers
     * must serialize.
     *
     * Returns human-readable status lines for a console pane.
     * Throws [IllegalStateException] on init/registration/mount failure.
     */
    fun start(
        componentName: String = "HelloComponent",
        platformOs: String = "android",
        apiLevel: Int = 0,
        // Phase 3.1: when non-null, the six shell callbacks are registered
        // BEFORE mount (components resolving IMobileBridge need a live host).
        bridge: ShellBridgeHandlers? = null,
    ): List<String> {
        val lines = mutableListOf<String>()
        val lib = NativeBindings.INSTANCE

        // Keep the Memory allocations referenced in locals until init returns —
        // init copies the strings, it doesn't retain the pointers, but the
        // buffers must stay alive across the call (mirrors BootSmokeNativeTest).
        val osMem = utf8CString(platformOs)
        val noteMem = utf8CString("android-shell")
        val opts = BlazorNativeInitOptions.ByReference().apply {
            platformInfoOs = osMem
            platformInfoApiLevel = apiLevel
            platformInfoNote = noteMem
        }
        val init = lib.blazornative_init(opts)
        if (init.status != 0) {
            val err = init.errorMessage?.getString(0, "UTF-8") ?: "<no detail>"
            throw IllegalStateException("blazornative_init failed (status=${init.status}): $err")
        }
        val version = init.versionString?.getString(0, "UTF-8") ?: "<null>"
        lines += "[BOOT] native init ok — $version"

        check(lib.blazornative_register_frame_callback(callback) == 0) {
            "blazornative_register_frame_callback failed"
        }
        lines += "[BOOT] frame callback registered"

        if (bridge != null) {
            // Registered BEFORE mount. register() throws on non-zero status;
            // the registrar keeps the callback trampolines alive (field here
            // + the process-lifetime park list inside BridgeRegistrar).
            bridgeRegistrar = BridgeRegistrar(bridge, onError).also { it.register() }
            lines += "[BOOT] shell bridge registered"
        }

        when (val rc = lib.blazornative_mount(componentName.toByteArray(Charsets.UTF_8) + 0)) {
            0 -> lines += "[BOOT] mounted $componentName"
            1 -> throw IllegalStateException("unknown component '$componentName'")
            else -> throw IllegalStateException(
                "mount($componentName) failed with status $rc — detail went to " +
                    "native stderr, which Android discards; reproduce on the " +
                    "desktop JVM to see it"
            )
        }
        return lines
    }

    /**
     * Retires THIS runtime's dispatch lane: no new events are accepted and
     * the call blocks (up to 5 s) until any in-flight dispatch has drained
     * out of the dll. Call BEFORE constructing a replacement runtime
     * (Activity recreation) — see the recreation contract on [start]: an
     * old-lane dispatch must never execute concurrently with the
     * replacement's start(). Does NOT touch the native session (that is
     * [shutdown]'s job); idempotent.
     *
     * @return true when the lane drained in time; false on timeout — the
     *   in-flight dispatch is stuck inside the dll (log loudly; proceeding
     *   with a replacement start() risks the concurrent-entry hazard).
     */
    fun retire(): Boolean {
        dispatchLane.shutdown()
        return dispatchLane.awaitTermination(5, TimeUnit.SECONDS)
    }

    /**
     * Tears down the process-lifetime native session (retiring the dispatch
     * lane first — no event may enter the dll after the frame callback is
     * cleared). Do NOT call this from Activity teardown (onDestroy) —
     * Activity recreation re-runs start() against the same process-global
     * session (see the recreation contract on [start]); shutting down between
     * recreations would kill the session the new Activity expects. Reserved
     * for genuine process-exit paths.
     */
    fun shutdown() {
        retire()
        NativeBindings.INSTANCE.blazornative_shutdown()
    }

    /** Caller-allocated NUL-terminated UTF-8 cstring for input pointers. */
    private fun utf8CString(s: String): Memory {
        val bytes = s.toByteArray(Charsets.UTF_8) + 0
        return Memory(bytes.size.toLong()).apply { write(0, bytes, 0, bytes.size) }
    }
}
