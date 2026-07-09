package io.blazornative.jni

import com.sun.jna.Memory
import com.sun.jna.Pointer

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
     * Boots the runtime: init → register frame callback → mount. The first
     * frame callback fires synchronously INSIDE the mount call (sync mount
     * contract), on the calling thread.
     *
     * ACTIVITY-RECREATION CONTRACT: calling start() a second time in the same
     * process (e.g. from a recreated Activity) is safe TODAY —
     * blazornative_init is idempotent, callback re-registration is last-wins,
     * and re-mounting adds a NEW component instance on the process-global
     * session (old instances are never disposed and accumulate natively).
     * The window where the OLD runtime's callback trampoline is still
     * registered (old Activity destroyed, new start() not yet re-registered)
     * is safe ONLY because frames fire exclusively inside mount() — no mount
     * in flight means no frame can hit the stale trampoline. Phase 3.2's
     * async frames BREAK that invariant: any recreation path must re-register
     * the new callback BEFORE any async frame source starts. Concurrent
     * start() calls (multiple threads) are unguarded — callers must serialize.
     *
     * Returns human-readable status lines for a console pane.
     * Throws [IllegalStateException] on init/registration/mount failure.
     */
    fun start(
        componentName: String = "HelloComponent",
        platformOs: String = "android",
        apiLevel: Int = 0,
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
     * Tears down the process-lifetime native session. Do NOT call this from
     * Activity teardown (onDestroy) — Activity recreation re-runs start()
     * against the same process-global session (see the recreation contract on
     * [start]); shutting down between recreations would kill the session the
     * new Activity expects. Reserved for genuine process-exit paths.
     */
    fun shutdown() = NativeBindings.INSTANCE.blazornative_shutdown()

    /** Caller-allocated NUL-terminated UTF-8 cstring for input pointers. */
    private fun utf8CString(s: String): Memory {
        val bytes = s.toByteArray(Charsets.UTF_8) + 0
        return Memory(bytes.size.toLong()).apply { write(0, bytes, 0, bytes.size) }
    }
}
