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
    // Called with every decoded frame. THREAD SET (Phase 16.1): exactly ONE
    // thread per session, .NET's render thread, seen here as one Java thread
    // named BlazorNative-Render (see the init block). It delivers the mount's first
    // frame while start() waits, a handler's synchronous re-render while the
    // dispatch export waits, and, later, frames from a handler's continuation
    // after an await, when no host call is in progress at all. Never the
    // start() caller, never the BlazorNative-Dispatch lane, and never two
    // frames at once. A frame from a second thread is reported to [onError]
    // (see [checkFrameProducer]). Consumers still post to the main thread
    // before touching views.
    private val onFrame: (RenderFrame) -> Unit,
    // (JVM-only default — Android callers must pass a Log-based sink. This is
    // the SHELL's own Kotlin diagnostics, and it is NOT what Phase 11.4 Gate B's
    // stderr pump covers: the pump redirects process fd 2, which is where the
    // NATIVE runtime writes; ART routes a JVM `System.err.println` through its
    // own path with the tag "System.err", losing the level and the category. So
    // the pluggable sink stays, and the Activity still passes Log.e.)
    //
    // THREAD SET: ANY thread — the start() caller, the BlazorNative-Dispatch lane, and
    // since Phase 16.1 a .NET THREAD-POOL thread: a handler fault after it began a host call
    // or a fetch arrives as a FaultNotice that BridgeRegistrar hands to this sink on whatever .NET
    // thread sent it. A sink that touches UI must post to the main thread first.
    private val onError: (String, Throwable) -> Unit = { msg, t -> System.err.println("$msg: $t") },
    // Phase 16.2 (#346): .NET's BackState notice — whether a back would be handled now.
    // Sent when the value changes, before every mount, and for a navigation BEFORE the
    // frames that show the new page. THREAD SET: whichever thread .NET sent it from —
    // the start() caller for a mount, the render thread for a navigation, the dispatch
    // lane for a back. Inside .NET's hostCallBegin, which must return at once: record
    // the value and return. The Android shell buffers it in WidgetMapper and applies it
    // on main with the next frame batch (spec decision 2). A host with no system back
    // leaves the default.
    private val onBackState: (canGoBack: Boolean) -> Unit = {},
    // Phase 16.2 (#346): .NET's BackUnhandled notice — a back reached .NET at the root or
    // with no session. The Android shell hands the press to the platform's default back;
    // a host with no system back leaves the default. Same THREAD SET and the same must-
    // return-at-once rule as [onBackState]; it may also arrive on a .NET thread-pool
    // thread, for a back that yielded and later resolved unhandled.
    private val onBackUnhandled: () -> Unit = {},
) {
    private val callback = object : NativeBindings.FrameCallback {
        override fun invoke(frame: Pointer) {
            try {
                checkFrameProducer()
                // NativeFrameAdapter.read copies everything (arena memory is
                // valid only during this invocation), so onFrame receives a
                // fully detached RenderFrame.
                onFrame(NativeFrameAdapter.read(frame))
            } catch (t: Throwable) {
                onError("frame dropped (adapter/consumer threw)", t)
            }
        }
    }

    init {
        // Phase 16.1: keep the render thread ATTACHED between frames. By default JNA
        // attaches a native thread for one callback and detaches it on return, so every
        // frame from the SAME .NET render thread arrived on a NEW java.lang.Thread
        // ("Thread-4", then "Thread-5"), which made thread identity meaningless on this
        // side and paid an attach per frame. Attached once, the render thread is one
        // daemon Thread named BlazorNative-Render for its life (JNA detaches it when the
        // native thread exits), and [checkFrameProducer] can compare identities.
        com.sun.jna.Native.setCallbackThreadInitializer(
            callback,
            com.sun.jna.CallbackThreadInitializer(true, false, RENDER_THREAD_NAME),
        )
    }

    /** The thread that delivered this runtime's first frame; see [checkFrameProducer]. */
    private val frameProducer = java.util.concurrent.atomic.AtomicReference<Thread?>(null)

    /**
     * Phase 16.1, the single-producer check. The first frame records the thread
     * that delivered it; a later frame from a DIFFERENT thread is reported to
     * [onError] and still delivered. Every frame of a session comes from .NET's
     * render thread, so a report means a second producer: two sessions framing
     * into one runtime (start() again after shutdown), or a runtime change that
     * frames on another thread. Android's WidgetMapper buffers patches in an
     * unsynchronized list until CommitFrame, which is only safe with one
     * producer. The check lives here rather than in WidgetMapper because this
     * shared class is what the JVM suite can build; FrameProducerTest pins it.
     * Internal so that test can drive it directly.
     */
    internal fun checkFrameProducer(current: Thread = Thread.currentThread()) {
        if (frameProducer.compareAndSet(null, current)) return
        val first = frameProducer.get()
        if (first !== current) {
            val msg = "frame delivered on thread '${current.name}', but this runtime's frames " +
                "come from '${first?.name}': a second frame producer (single-producer contract)"
            onError(msg, IllegalStateException(msg))
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
     * key maps to null EventArgs payload). Since Phase 16.1 the export returns
     * once the handler's SYNCHRONOUS part has run: a re-render from that part
     * has been delivered by then, on .NET's render thread, but a handler that
     * awaits frees the lane at its first await that suspends, and its later re-renders
     * arrive afterwards, from a continuation, with no call in progress.
     *
     * Non-zero return codes are routed to [onError] (the tap is dropped):
     * rc 1 = nothing mounted (shell bug — dispatch before start()), rc 2 =
     * dispatch faulted (handler/re-render/frame delivery threw), rc 3 =
     * malformed args (writer bug — should be impossible from this API).
     */
    fun dispatchEvent(handlerId: Int, eventName: String, payload: String? = null) {
        dispatchEvent(handlerId, eventName, payload, onComplete = {})
    }

    /**
     * Phase 7.2 (the onScroll wire) — [dispatchEvent] WITH A COMPLETION SIGNAL:
     * [onComplete] runs after the dispatch has LEFT the lane (the ABI call
     * returned — successfully, with a non-zero rc, or by throwing), which is
     * the moment the lane is available again.
     *
     * It exists for the shell-side scroll CONFLATION (the wire contract,
     * docs/plans/2026-07-15-phase-7.2-design.md): the mapper keeps ONE pending
     * offset per scroll node and submits at most one scroll dispatch per
     * lane-availability — a new dispatch may not be submitted until the
     * previous one has completed. Fire-and-forget [dispatchEvent] cannot say
     * when that is; this overload can. No new threading surface: the SAME
     * single [dispatchLane], the same FIFO — which is also what keeps the
     * ordering rule free ("a conflated scroll dispatch must not overtake an
     * already-queued user-input event"): scroll dispatches enter the same
     * queue tail as every tap and change, and a FIFO lane never reorders.
     *
     * [onComplete] is invoked on the LANE thread — callers marshal to their
     * own thread before touching their state (WidgetMapper posts to the main
     * handler). It ALWAYS runs, including when a retired lane rejects the
     * submit (Activity recreation): a lost completion would WEDGE the caller's
     * conflation slot — the pending offset would wait forever for a lane that
     * already freed, and the list would stop following the finger.
     */
    fun dispatchEvent(handlerId: Int, eventName: String, payload: String?, onComplete: () -> Unit) {
        try {
            dispatchLane.execute {
                try {
                    val rc = dispatchCore(handlerId, eventName, payload)
                    if (rc != 0) {
                        onError(describeDispatchFailure(rc, handlerId, eventName), IllegalStateException("dispatch_event rc=$rc"))
                    }
                } catch (t: Throwable) {
                    onError("dispatch_event(handlerId=$handlerId, '$eventName') threw on the dispatch lane", t)
                } finally {
                    onComplete()
                }
            }
        } catch (e: java.util.concurrent.RejectedExecutionException) {
            // Retired lane (recreation): the event is dropped like any other
            // post-retire dispatch, but the completion still fires — see above.
            onComplete()
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

    /**
     * Phase 4.4 — the HOST-blocking dispatch (InspectorHost's POST handler):
     * marshals through the SAME single [dispatchLane] as [dispatchEvent] —
     * post-boot .NET entry stays serialized on `BlazorNative-Dispatch`, the
     * documented threading contract — but BLOCKS the caller until the
     * export has returned (the handler's synchronous part and its re-render
     * frames, delivered on .NET's render thread; since Phase 16.1 not the
     * work after an await), and returns the raw rc
     * (0/1/2/3 — [dispatchEvent]'s table; non-zero is DATA to this caller,
     * not an onError event). Safe to call from any thread EXCEPT the dispatch
     * lane itself — concurrent callers queue up behind each other on the
     * lane, but a call FROM the lane (e.g. inside an onFrame consumer during
     * a dispatch) would SELF-DEADLOCK: the single lane thread would block on
     * an untimed get() waiting for a task queued behind its own current one.
     * Unreachable from today's callers (HTTP handler threads only) — pinned
     * here because this is public API. A throw from the dispatch core (not a
     * non-zero rc) is rethrown to the caller unwrapped.
     */
    fun dispatchEventAndWait(handlerId: Int, eventName: String, payload: String? = null): Int {
        val future = dispatchLane.submit(java.util.concurrent.Callable {
            dispatchCore(handlerId, eventName, payload)
        })
        return try {
            future.get()
        } catch (e: java.util.concurrent.ExecutionException) {
            throw e.cause ?: e
        }
    }

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
            "(detail on the runtime's stderr — logcat `BlazorNative/…` on Android)"
        3 -> "dispatch_event(handlerId=$handlerId, '$eventName') → rc 3: malformed/NULL args " +
            "OR handlerId > int.MaxValue (writer bug — should be impossible from this API)"
        else -> "dispatch_event(handlerId=$handlerId, '$eventName') → undocumented rc $rc"
    }

    /**
     * Phase 5.1 (M5 DoD #5): host-INITIATED event ingress, asynchronously on the
     * `BlazorNative-Dispatch` lane (same threading contract as [dispatchEvent] —
     * never call the ABI directly). LIFECYCLE events (onPause/onResume/…) fire
     * the runtime's NativeShellBridge.NativeEvents multicast so mounted
     * components re-render; the reserved name "back" routes to navigation-back
     * (the mapping lives in .NET — see [NativeBindings.blazornative_host_event]).
     *
     * FIRE-AND-FORGET: a non-zero rc is routed to [onError], with ONE exception:
     * a BACK's rc 1. Since Phase 16.2 (#346) back is dispatched here too, never
     * blocking the main thread, and "not handled" reaches the shell as .NET's
     * BackUnhandled notice ([onBackUnhandled]), which hands the press to the
     * platform's default back. rc 1 is that normal outcome, not a failure, so it
     * is not reported a second time as an error; a back's rc 2 still is. Every
     * other event's rc 1, such as a navigate to an unknown route, still reaches
     * [onError]. [payload] is optional
     * (omitted/NULL — most host events carry none).
     */
    internal fun dispatchHostEvent(event: BnHostEvent, payload: String? = null) =
        dispatchHostEventUnchecked(event.wireName, payload)

    /**
     * Test seam: [dispatchHostEvent]'s lane + onError routing, reachable with an
     * ARBITRARY name so the rc 3 (malformed name) path stays testable. Production
     * code dispatches through [dispatchHostEvent] (the enum overload) instead —
     * that is the only production entry point, and
     * NoProductionShellSource_CallsTheHostEventSeamsDirectly
     * (BlazorNative.Runtime.Tests) enforces it by scanning production shell
     * sources for a direct call here. Kotlin itself does not stop this seam's
     * bare `String` parameter from compiling at a production call site — the
     * test is the mechanism, not the type.
     */
    internal fun dispatchHostEventUnchecked(name: String, payload: String? = null) {
        dispatchLane.execute {
            try {
                val rc = hostEventCore(name, payload)
                if (rc != 0 && !isNormalHostEventOutcome(name, rc)) {
                    onError(describeHostEventFailure(rc, name), IllegalStateException("host_event rc=$rc"))
                }
            } catch (t: Throwable) {
                onError("host_event('$name') threw on the dispatch lane", t)
            }
        }
    }

    /** Phase 16.2 (#346): a back's rc 1 is the BackUnhandled outcome, delivered through
     * [onBackUnhandled], not an error. Only back, and only rc 1. */
    private fun isNormalHostEventOutcome(name: String, rc: Int): Boolean =
        rc == 1 && name == BnHostEvent.Back.wireName

    /**
     * Test seam: same marshalling as [dispatchHostEvent] but runs INLINE on the
     * calling thread and returns the raw rc (JVM tests assert the 0/1/2/3
     * contract directly, their calling thread IS the dispatch-discipline
     * thread). Production callers use [dispatchHostEvent], fire-and-forget, for
     * every host event since Phase 16.2.
     */
    internal fun dispatchHostEventBlocking(name: String, payload: String? = null): Int =
        hostEventCore(name, payload)

    /**
     * Phase 5.1 — the HOST-blocking host-event dispatch. Until Phase 16.2 it was
     * the predictive-back and warm deep-link path on Android; both now dispatch
     * fire-and-forget through [dispatchHostEvent], because a main thread blocked
     * here waits on .NET (#346), and no Android production code calls it. It
     * marshals through the SAME single [dispatchLane]
     * (post-boot .NET entry stays serialized) but BLOCKS until the export has
     * returned — including any synchronous re-render / swap frame deliveries,
     * made on .NET's render thread — and returns the raw rc (0 handled / 1 not handled
     * → BackUnhandled is sent / 2 faulted). Safe from any thread EXCEPT the
     * dispatch lane itself (a call from the lane would self-deadlock, same as
     * [dispatchEventAndWait]). A throw from the dispatch core is rethrown
     * unwrapped.
     */
    internal fun dispatchHostEventAndWait(event: BnHostEvent, payload: String? = null): Int {
        val future = dispatchLane.submit(java.util.concurrent.Callable {
            hostEventCore(event.wireName, payload)
        })
        return try {
            future.get()
        } catch (e: java.util.concurrent.ExecutionException) {
            throw e.cause ?: e
        }
    }

    /** NUL-terminates the name (+ optional payload) and crosses the ABI via the
     * blazornative_host_event export. */
    private fun hostEventCore(name: String, payload: String?): Int {
        val nameZ = name.toByteArray(Charsets.UTF_8) + 0
        val payloadZ = payload?.let { it.toByteArray(Charsets.UTF_8) + 0 }
        return NativeBindings.INSTANCE.blazornative_host_event(nameZ, payloadZ)
    }

    /** Human-readable onError message per non-zero host_event rc (internal so
     * the message contract is unit-tested; parallels [describeDispatchFailure]). */
    internal fun describeHostEventFailure(rc: Int, name: String): String = when (rc) {
        1 -> "host_event('$name') → rc 1: not handled — no session, a navigate to an " +
            "unknown route, or a back at the origin (which is reported as BackUnhandled " +
            "instead of here)"
        2 -> "host_event('$name') → rc 2: faulted — a NativeEvents subscriber, its " +
            "re-render, or the back swap threw (detail on the runtime's stderr — " +
            "logcat `BlazorNative/…` on Android)"
        3 -> "host_event('$name') → rc 3: malformed/NULL event name (writer bug — " +
            "should be impossible from this API)"
        else -> "host_event('$name') → undocumented rc $rc"
    }

    /**
     * Boots the runtime: init → register frame callback → mount. The first
     * frame callback fires synchronously INSIDE the mount call (sync mount
     * contract): delivered on .NET's render thread while the calling thread
     * waits in the export (Phase 16.1).
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
     * Activity's views). [retire] the old runtime (drains its lane) BEFORE
     * constructing/starting the replacement: that closes the concurrent-entry
     * hazard, and only that one.
     *
     * FRAMES ARE NOT CONFINED TO HOST CALLS (Phase 16.1). They come from .NET's
     * render thread, during a host call OR later from a continuation: a handler
     * that awaited a host call re-renders when that call completes, with no
     * export in progress. So [retire] does NOT quiesce .NET: it drains only the
     * Kotlin lane and never calls into the runtime, and a late continuation can
     * still deliver a frame to the retired runtime's onFrame (until the
     * replacement's start() re-registers the callback), and afterwards to the
     * replacement's. RetireLateContinuationTest pins that hazard as it stands.
     * [shutdown] IS quiescent: it calls `blazornative_shutdown`, which closes
     * .NET's frame gate and waits out any callback in flight before it
     * returns, so no frame reaches this runtime afterwards
     * (ShutdownQuiescenceTest). Concurrent start() calls (multiple threads) are
     * unguarded — callers must serialize.
     *
     * Returns human-readable status lines for a console pane.
     * Throws [IllegalStateException] on init/registration/mount failure.
     */
    fun start(
        componentName: String = "HelloComponent",
        platformOs: String = "android",
        apiLevel: Int = 0,
        // Phase 10.0 (#121): the shell's real PlatformKind ordinal (BnPlatformKind).
        // Defaults to DEV_HOST — dev/preview/inspector hosts that boot through here
        // report DevHost, and the Android shell passes ANDROID explicitly (MainActivity)
        // so an iOS app can never inherit Android from a shared runtime constant.
        platformKind: Int = BnPlatformKind.DEV_HOST,
        // Phase 11.4 Gate B (#155): the shell's declared BnLogLevel ordinal, read
        // by the runtime BEFORE its first managed line (Exports.cs →
        // BnLog.SetLevelFromOrdinal). Defaults to UNSET, which the runtime resolves
        // to its own quiet Release default (Warn) — so a host that says nothing
        // gets the documented default rather than an accidental verbosity. It is
        // the ONLY input applied early enough to govern init's own failure path,
        // which is why it rides the init struct rather than a setter.
        logLevel: Int = BnLogLevel.UNSET,
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
            platformInfoKind = platformKind // Phase 10.0 (#121): report the shell's real kind
            this.logLevel = logLevel        // Phase 11.4 (#155): offset 28, size still 32
        }
        // #213 item 3: pass our compiled struct size so the runtime size-negotiates.
        // opts.size() is JNA's measured sizeof for THIS shell's struct — a runtime built
        // against a newer header reads only what we sent and defaults the rest, rather
        // than reading past the buffer.
        val init = lib.blazornative_init(opts.size(), opts)
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
            bridgeRegistrar = BridgeRegistrar(
                bridge,
                onBackState = onBackState,
                onBackUnhandled = onBackUnhandled,
                onError = onError,
            ).also { it.register() }
            lines += "[BOOT] shell bridge registered"
        }

        when (val rc = lib.blazornative_mount(componentName.toByteArray(Charsets.UTF_8) + 0)) {
            0 -> lines += "[BOOT] mounted $componentName"
            1 -> throw IllegalStateException("unknown component '$componentName'")
            else -> throw IllegalStateException(
                "mount($componentName) failed with status $rc — detail went to " +
                    "the runtime's stderr; on Android the shell's stderr pump " +
                    "forwards it to logcat (`adb logcat -s BlazorNative/…`)"
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
     * [shutdown]'s job); idempotent. NOT QUIESCENT: a handler continuation
     * that resumes after this returns can still deliver a frame to this
     * runtime's onFrame — see the recreation contract on [start].
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
     * cleared). QUIESCENT (Phase 16.1): `blazornative_shutdown` closes .NET's
     * frame gate, waits for any callback in flight and clears the callback,
     * then joins the render thread, BOUNDED at 5 s: the join times out when a
     * handler never yields. Quiescence holds because of the gate, which stays
     * closed whether or not the join finished, so no frame, not even one from
     * a late continuation, reaches this runtime after it returns
     * (ShutdownQuiescenceTest). Do NOT call this from Activity teardown (onDestroy) —
     * Activity recreation re-runs start() against the same process-global
     * session (see the recreation contract on [start]); shutting down between
     * recreations would kill the session the new Activity expects. Reserved
     * for genuine process-exit paths.
     */
    fun shutdown() {
        retire()
        NativeBindings.INSTANCE.blazornative_shutdown()
    }

    internal companion object {
        /** The JVM name of .NET's render thread while it delivers frames (see the init block). */
        const val RENDER_THREAD_NAME = "BlazorNative-Render"
    }

    /** Caller-allocated NUL-terminated UTF-8 cstring for input pointers. */
    private fun utf8CString(s: String): Memory {
        val bytes = s.toByteArray(Charsets.UTF_8) + 0
        return Memory(bytes.size.toLong()).apply { write(0, bytes, 0, bytes.size) }
    }
}
