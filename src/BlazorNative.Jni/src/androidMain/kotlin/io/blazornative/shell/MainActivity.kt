package io.blazornative.shell

import android.app.Activity
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.widget.FrameLayout
import android.widget.TextView
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher
import io.blazornative.jni.BlazorNativeRuntime
import kotlin.concurrent.thread

/**
 * Phase 3.0d Android shell entry point — boots the NativeAOT pipeline.
 *
 * On launch: spawns a background thread that runs [BlazorNativeRuntime.start]
 * (init → register frame callback → register shell bridge → mount
 * BnDemo, or the [EXTRA_COMPONENT] Intent-extra override; 4 [BOOT]
 * lines since Phase 3.1) against the
 * NativeAOT libBlazorNative.Runtime.so from the APK's jniLibs. Frames
 * arrive through the C-ABI struct path (NativeFrameAdapter) and render via
 * [WidgetMapper] into widget_root; [BOOT] status lines go to logcat and the
 * green-on-black console TextView.
 *
 * The wasmtime/.wasm boot path was retired from this Activity in Phase 3.0d,
 * and Phase 3.0e deleted the WASM era from the tree entirely — the NativeAOT
 * runtime is the only boot path.
 *
 * Threading/lifetime notes:
 *  - [runtime] is an Activity FIELD deliberately: it strongly holds the JNA
 *    frame callback; if it were a local, GC could collect the callback's
 *    trampoline while native code still points at it.
 *  - All throwables are caught → Log.e + "FAIL: ..." in the TextView so a
 *    boot crash is visible without attaching to logcat. Frame-level errors
 *    (adapter/consumer throws inside the JNA callback) route through
 *    onError → Log.e — JNA would otherwise swallow them to stderr.
 */
class MainActivity : Activity() {

    companion object {
        /**
         * Phase 3.3 Task 9: Intent-extra override for the mounted component
         * (a mount-registry name, HostSession.cs). Absent → "BnDemo" since
         * Phase 3.4 Gate 4 — the Bn* library demo (bound input + live echo +
         * cascading theme toggle) IS the launcher experience; tests that pin
         * another component's shape pass it explicitly ("HelloComponent",
         * "CompositionProbe") via ActivityScenario.launch(Intent). An unknown
         * name fails the boot loudly (mount rc 1 → FAIL in the console pane),
         * same as any other boot error.
         */
        const val EXTRA_COMPONENT = "io.blazornative.shell.EXTRA_COMPONENT"

        /** Phase 5.1 deep-link custom scheme (design §4): blazornative://<route>.
         * A custom scheme (no domain verification) is the simplest honest proof
         * of the launch-time deep-link mechanism; https App Links are later work. */
        const val DEEP_LINK_SCHEME = "blazornative"

        /**
         * The shell's deep-link route → mount-component map (Phase 5.1). Mirrors
         * the tiny .NET NativeNavigationManager route table. The shell resolves
         * the deep-link target component HERE (and still mounts by NAME) rather
         * than leaning on .NET's first-mount startup-route-honor, because that
         * honor only fires on a session's FIRST mount — which never holds under
         * Activity recreation OR the shared-process instrumented session. Direct
         * resolution is robust in all three; the route slot is still seeded so
         * .NET's CurrentRoute agrees. Unknown route → default (BnDemo). A shared
         * route registry is M6 work (nav lifts into a package).
         */
        private val DEEP_LINK_COMPONENTS = mapOf("/settings" to "BnSettingsPage")
    }

    private val tag = "BlazorNative"

    /** Strong ref for the .so's lifetime — see class KDoc. */
    private lateinit var runtime: BlazorNativeRuntime

    /**
     * Phase 5.1 boot guard: true only after [BlazorNativeRuntime.start] has
     * completed on the boot thread. Lifecycle callbacks (onResume/onPause/
     * onDestroy) and predictive-back are host→.NET entry — they MUST NOT fire
     * before the mount, both because there is nothing to notify and (the real
     * hazard) because entering the dispatch lane while the boot thread is still
     * inside start() would put two threads in the .NET session at once (the
     * concurrent-entry hazard the runtime KDoc warns about). @Volatile: written
     * on the boot thread, read on the main thread.
     */
    @Volatile private var booted = false

    /** The predictive-back callback (API 33+); null on lower APIs (they use the
     * deprecated [onBackPressed] fallback). Held so it could be unregistered. */
    private var backCallback: OnBackInvokedCallback? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.main)

        val view = findViewById<TextView>(R.id.markers)
        val widgetRoot = findViewById<FrameLayout>(R.id.widget_root)
        // Phase 3.2: UI listeners forward into the dispatch lane. The lambda
        // captures the lateinit `runtime` field (constructed just below) —
        // safe: onUiEvent only fires from listeners that AttachEvent installs,
        // i.e. after runtime.start() has mounted, long after assignment.
        // dispatchEvent is a non-blocking submit — UI-thread safe.
        val mapper = WidgetMapper(this, widgetRoot, onUiEvent = { h, n, p ->
            runtime.dispatchEvent(h, n, p)
        })

        val onError: (String, Throwable) -> Unit = { msg, t -> Log.e(tag, msg, t) }
        runtime = BlazorNativeRuntime(
            onFrame = { frame -> mapper.apply(frame) },
            onError = onError,
        )

        // Phase 5.1 (M5 DoD #5): a VIEW-intent deep link (blazornative://<route>)
        // both seeds the startup route (so .NET's CurrentRoute agrees) AND
        // resolves the mount component by name (DEEP_LINK_COMPONENTS) — the
        // mount is still by NAME, per the design, but resolved shell-side so it
        // is robust to Activity recreation + the shared instrumented session
        // (see DEEP_LINK_COMPONENTS). PRECEDENCE: an explicit EXTRA_COMPONENT
        // (test override) wins the mount over a deep link; the route seed still
        // applies.
        val deepLinkRoute = parseDeepLinkRoute(intent)
        if (deepLinkRoute != null) Log.i(tag, "[deep-link] startup route → $deepLinkRoute")

        // Phase 3.1: the shell half of IMobileBridge. Passing the Activity is
        // safe — AndroidShellBridge captures applicationContext ONLY (the
        // process-lifetime retention contract on ShellBridgeHandlers).
        val bridge = AndroidShellBridge(this, initialRoute = deepLinkRoute ?: "/", onError = onError)

        val componentName = intent.getStringExtra(EXTRA_COMPONENT)
            ?: deepLinkRoute?.let { DEEP_LINK_COMPONENTS[it] }
            ?: "BnDemo"

        // Phase 5.1: register predictive back on API 33+ (the AVD is API 34, so
        // this IS the tested path). Lower APIs fall back to onBackPressed().
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerPredictiveBack()
        }

        thread(name = "BlazorNative-Runtime-Boot") {
            try {
                val lines = runtime.start(
                    componentName = componentName,
                    platformOs = "android",
                    apiLevel = Build.VERSION.SDK_INT,
                    bridge = bridge,
                )
                booted = true // host→.NET entry (lifecycle/back) is safe only now
                // Emit each line as one Log.i call so logcat shows them as
                // atomic lines (filter via `adb logcat -s BlazorNative`).
                lines.forEach { Log.i(tag, it) }
                runOnUiThread { view.text = lines.joinToString("\n") }
            } catch (t: Throwable) {
                Log.e(tag, "Boot failed", t)
                runOnUiThread { view.text = "FAIL: ${t.javaClass.simpleName}: ${t.message}" }
            }
        }
    }

    // ── Lifecycle → host events (Phase 5.1, M5 DoD #5) ───────────────────────
    //
    // Each override marshals a host event onto the dispatch lane
    // (fire-and-forget), guarded by [booted] so nothing enters .NET before the
    // mount. The first onResume runs before boot completes (guard skips it —
    // the initial mount IS the first resume); later resume/pause pairs fire.

    override fun onResume() {
        super.onResume()
        if (booted) runtime.dispatchHostEvent("onResume")
    }

    override fun onPause() {
        super.onPause()
        if (booted) runtime.dispatchHostEvent("onPause")
    }

    override fun onDestroy() {
        // THE onDestroy TRAP (BlazorNativeRuntime.start KDoc): fire the lifecycle
        // event but DO NOT call runtime.shutdown(). Activity recreation re-runs
        // start() against the PROCESS-GLOBAL native session; shutting it down
        // here would clear the frame callback + tear down the session the
        // recreated Activity expects. shutdown() is reserved for genuine
        // process-exit paths, which Android does not give an Activity.
        if (booted) runtime.dispatchHostEvent("onDestroy")
        super.onDestroy()
    }

    // ── Predictive / system back → NavigateBack (Phase 5.1) ──────────────────

    /** Registers the API 33+ predictive-back callback (PRIORITY_DEFAULT on the
     * activity's onBackInvokedDispatcher). It DELEGATES to [onBackPressed] so
     * both the 33+ dispatcher path and the lower-API fallback share ONE back
     * entry point — the instrumented test drives that single entry ([onBackPressed])
     * directly, which is reliable, whereas committing a system predictive-back
     * GESTURE under instrumentation is not. Split into its own method so the
     * OnBackInvokedCallback class is only loaded under the version guard. */
    private fun registerPredictiveBack() {
        @Suppress("DEPRECATION")
        val cb = OnBackInvokedCallback { onBackPressed() }
        backCallback = cb
        onBackInvokedDispatcher.registerOnBackInvokedCallback(
            OnBackInvokedDispatcher.PRIORITY_DEFAULT, cb)
    }

    /**
     * THE single back entry point (Phase 5.1). On API 33+ the registered
     * [OnBackInvokedCallback] routes here; on lower APIs the framework calls
     * this deprecated override directly (plain Activity has no
     * onBackPressedDispatcher — that is ComponentActivity — so the deprecated
     * onBackPressed IS the honest fallback). Handled (rc 0) → consume; not
     * handled (rc 1 at-root / no session, rc 2 fault) → default back = finish.
     */
    @Suppress("DEPRECATION", "OVERRIDE_DEPRECATION")
    override fun onBackPressed() {
        if (!handleBack()) {
            @Suppress("DEPRECATION")
            super.onBackPressed() // default = finish the root activity
        }
    }

    /**
     * Routes a system-back to .NET NavigateBack via the reserved "back" host
     * event, BLOCKING for the handled/not-handled decision
     * ([BlazorNativeRuntime.dispatchHostEventAndWait]). Returns true when .NET
     * navigated back (rc 0 — consume the gesture), false when it did not (rc 1
     * at-root / no session, or rc 2 fault — the caller lets default back
     * proceed). Guarded by [booted]: a back before boot is "not handled" so the
     * activity finishes normally. The main-thread block is brief (the swap is
     * synchronous on the lane); WidgetMapper posts its batch to the main handler
     * (non-blocking), so there is no lane↔main deadlock.
     */
    private fun handleBack(): Boolean {
        if (!booted) return false
        return try {
            runtime.dispatchHostEventAndWait("back") == 0
        } catch (t: Throwable) {
            Log.e(tag, "back dispatch threw", t)
            false
        }
    }

    /** Parses a VIEW-intent deep link `blazornative://<route>` into a nav route
     * ("blazornative://settings" → "/settings"). Null for a normal (LAUNCHER)
     * launch or a foreign scheme. onNewIntent (already-running) is out of scope
     * — launch-time only (design §4). */
    private fun parseDeepLinkRoute(intent: Intent): String? {
        if (intent.action != Intent.ACTION_VIEW) return null
        val data = intent.data ?: return null
        if (data.scheme != DEEP_LINK_SCHEME) return null
        val host = data.host ?: return null // blazornative://settings → "settings"
        return "/$host"                      // → "/settings"
    }
}
