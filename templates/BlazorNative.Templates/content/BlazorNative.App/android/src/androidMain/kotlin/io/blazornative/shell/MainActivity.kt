package io.blazornative.shell

import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.widget.FrameLayout
import android.widget.TextView
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher
import androidx.fragment.app.FragmentActivity
// LOAD-BEARING, and it is the one import the reference shell does NOT have.
//
// AGP generates `R` into the `namespace` package (build.gradle.kts — YOURS),
// while Kotlin resolves a bare `R` against THIS FILE's package. The reference
// shell gets away with a bare `R` only because its namespace happens to equal
// its source package (io.blazornative.shell). Here they are deliberately
// different: the shell's Kotlin stays io.blazornative.shell so it is
// byte-identical library code, and `namespace` is your app's. So the import is
// what makes R.layout.main / R.id.* below resolve at all. It is rewritten to
// your namespace at generation time.
//
// Delete it and the generated app does not compile: "Unresolved reference 'R'".
import com.example.starterapp.R
import io.blazornative.jni.BlazorNativeRuntime
import io.blazornative.jni.BnPlatformKind
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
 * Phase 9.2 (M9 DoD #4): a FragmentActivity (not a plain Activity) — androidx
 * BiometricPrompt requires a FragmentActivity host to attach its internal
 * fragment, and AndroidShellBridge's op=Biometrics / op=SecureStorage prompt
 * against this activity. FragmentActivity extends ComponentActivity but adds NO
 * back-dispatcher callback of its own here (this activity registers none on the
 * onBackPressedDispatcher), so the manual predictive-back path below is unchanged.
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
class MainActivity : FragmentActivity() {

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
         * The shell's deep-link route -> mount-component map: `blazornative://<route>`
         * opens the page this map names.
         *
         * **A page is declared ONCE**, in your app's `AppPages.All` (AppPages.cs).
         * This map is the one PINNED MIRROR of its ROUTED rows -- it cannot be a
         * derived view of them, because it is consulted at Intent-parse time in
         * onCreate, BEFORE the native library loads. So when you add a ROUTED page,
         * add its pair HERE too, in the same commit. A mirror that drifts fails no
         * compile and no test: the deep link just opens the wrong screen, on Android
         * alone, silently.
         *
         * It starts EMPTY because a one-page app has no non-"/" routes: the "/" row
         * is the one pair this map deliberately does not carry -- it rides the
         * `?: "BnStarterPage"` fallback below. Add pairs as you add routes:
         *
         *     private val DEEP_LINK_COMPONENTS = mapOf(
         *         "/about" to "BnAboutPage",
         *     )
         *
         * The shell resolves the deep-link target component HERE (and still mounts
         * by NAME) rather than leaning on .NET's first-mount startup-route-honor,
         * because that honor only fires on a session's FIRST mount -- which never
         * holds under Activity recreation. Direct resolution is robust; the route
         * slot is still seeded so .NET's CurrentRoute agrees. Unknown route ->
         * the fallback.
         */
        private val DEEP_LINK_COMPONENTS = mapOf<String, String>()

        /** Phase 9.1 — the reserved host-event name for WARM notification
         * tap-through. [onNewIntent] dispatches it with the route as the payload;
         * .NET's DispatchHostEventCore maps it to NavigateToAsync (the "back"
         * precedent — the name→verb mapping lives in .NET so every shell gets
         * identical semantics). Must equal Exports.NavigateEventName. */
        internal const val NAVIGATE_EVENT = "navigate"

        /** Test seam (instrumented BnNotificationsAndroidTest): the rc of the most
         * recent warm-tap "navigate" host_event — 0 = the live session re-routed,
         * 1 = not handled / no session, 2 = faulted. Int.MIN_VALUE before any warm
         * tap. Static because the test cannot reach the private Activity instance. */
        @Volatile @JvmStatic var lastNavigateHostEventRcForTest: Int = Int.MIN_VALUE

        /** Test seam: reset the warm-navigate rc probe between tests. */
        @JvmStatic fun resetNavigateRcForTest() { lastNavigateHostEventRcForTest = Int.MIN_VALUE }
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

    /** Phase 9.0: the shell bridge, held as a field so [onRequestPermissionsResult]
     * can forward the OS permission callback into it. Assigned in onCreate; a
     * recreated Activity builds a FRESH bridge (the app-scoped requestCode→requestId
     * map is static on AndroidShellBridge, so the in-flight request still routes —
     * the recreation-survival design). */
    private var shellBridge: AndroidShellBridge? = null

    /** Phase 6.1: held as a field so [onDestroy] can tear its trees down. The
     * runtime's frame callback captures it and OUTLIVES this Activity (the native
     * session is process-global), so an un-destroyed mapper keeps a dead Activity's
     * whole view hierarchy — and its native Yoga peers — alive across recreation.
     *
     * `internal` rather than private so YogaNodeLifecycleAndroidTest can read its
     * node counts: the subtree-purge regression is only observable in the mapper's
     * BOOKKEEPING (a leaked node still lays out nothing and shows nothing), and the
     * only honest way to see it is against the real renderer's patch stream. */
    internal lateinit var mapper: WidgetMapper

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Phase 6.1: Yoga's JNI core loads through SoLoader, which must be
        // initialised once per process BEFORE the first YogaNode — and every
        // node the WidgetMapper creates below allocates one. Done at shell start
        // (the 6.0 conclusion's Android note; the spike did it from the test).
        // Idempotent, so YogaLayout's own guard can also call it in the
        // Activity-less synthetic-frame tests.
        YogaLayout.ensureSoLoader(this)

        setContentView(R.layout.main)

        val view = findViewById<TextView>(R.id.markers)
        val widgetRoot = findViewById<FrameLayout>(R.id.widget_root)
        // Phase 3.2: UI listeners forward into the dispatch lane. The lambda
        // captures the lateinit `runtime` field (constructed just below) —
        // safe: onUiEvent only fires from listeners that AttachEvent installs,
        // i.e. after runtime.start() has mounted, long after assignment.
        // dispatchEvent is a non-blocking submit — UI-thread safe.
        mapper = WidgetMapper(this, widgetRoot,
            onUiEvent = { h, n, p ->
                runtime.dispatchEvent(h, n, p)
            },
            // Phase 7.2: the scroll wire needs the COMPLETION signal — the
            // conflation submits at most one scroll dispatch per
            // lane-availability, and only this overload can say when the lane
            // freed. Same lane, same FIFO: scroll can never overtake a queued
            // tap (the wire contract's ordering row).
            onScrollEvent = { h, payload, done ->
                runtime.dispatchEvent(h, "scroll", payload, done)
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
        shellBridge = bridge

        val componentName = intent.getStringExtra(EXTRA_COMPONENT)
            ?: deepLinkRoute?.let { DEEP_LINK_COMPONENTS[it] }
            ?: "BnStarterPage"

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
                    platformKind = BnPlatformKind.ANDROID, // Phase 10.0 (#121): report Android, not a shared default
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
        // Phase 6.1: the view tree and the Yoga tree die with the Activity. The
        // mapper is reachable from the runtime's frame callback (process-global
        // session), so without this every recreation would leak a complete view
        // hierarchy plus a complete native Yoga tree. A frame that lands on this
        // mapper after teardown is harmless — it renders into a detached root and
        // the recreated Activity's start() re-registers its own mapper.
        // (isInitialized: a throw in onCreate before the assignment still destroys.)
        if (::mapper.isInitialized) mapper.destroy()
        super.onDestroy()
    }

    // ── Permission results → the shell bridge (Phase 9.0, M9 DoD #1) ──────────

    /**
     * Forwards the system permission-dialog result into the app-scoped
     * [AndroidShellBridge], which looks the in-flight requestId up in its STATIC
     * requestCode→requestId map and completes the .NET call (grant → fetch a fix;
     * deny → a tri-state status). This callback may land on a RECREATED Activity —
     * the map is static precisely so the fresh bridge this recreated Activity built
     * still routes the result to the right in-flight .NET continuation (the phase's
     * named risk). A requestCode the shell never issued is ignored.
     */
    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        shellBridge?.onPermissionResult(requestCode, permissions, grantResults)
    }

    // ── Camera capture result → the shell bridge (Phase 9.3, M9 DoD #5) ────────

    /**
     * Forwards the ACTION_IMAGE_CAPTURE result into the app-scoped [AndroidShellBridge], which
     * looks the in-flight requestId up in its STATIC requestCode→pending-capture map and
     * completes the .NET call (RESULT_OK → downscale + EXIF-normalize + return the file path;
     * RESULT_CANCELED → Cancelled, no path). Like the permission result this may land on a
     * RECREATED Activity (the system camera activity can recreate the caller) — the map is
     * static precisely so the fresh bridge this recreated Activity built still routes the result
     * to the right in-flight .NET continuation. A requestCode the shell never issued is ignored.
     */
    @Deprecated("startActivityForResult/onActivityResult — the shell routes the camera result via the static pending map (recreation-survival, the permission-result twin)")
    @Suppress("DEPRECATION")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        shellBridge?.onCameraResult(requestCode, resultCode)
    }

    // ── Warm notification tap-through → NavigateToAsync (Phase 9.1, M9 DoD #3) ──

    /**
     * A WARM tap on a notification (the app is alive; the activity is singleTop)
     * delivers the deep-link Intent HERE rather than re-running onCreate. 5.1 left
     * this unhandled (launch-time deep links only); 9.1 wires it: parse the same
     * `blazornative://<route>` and ask the LIVE .NET session to re-route via the
     * reserved "navigate" host event (payload = the route), which
     * DispatchHostEventCore maps to NavigateToAsync (the root swap). The COLD case
     * (a killed app) is unchanged — it relaunches through onCreate's deep-link mount.
     *
     * `public` so the instrumented tap-through test can drive it; `setIntent` keeps
     * getIntent() current. Reuses the "back" shape — a brief BLOCKING dispatch for
     * the swap (dispatchHostEventAndWait runs on the dispatch lane; WidgetMapper
     * posts its batch to the main handler, so there is no lane↔main deadlock). The
     * returned rc is DATA the tap-through test asserts (0 = the live session
     * re-routed).
     */
    public override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        val route = parseDeepLinkRoute(intent) ?: return
        if (!booted) return // nothing mounted yet — a (rare) pre-boot tap is dropped
        Log.i(tag, "[deep-link] warm re-route → $route")
        val rc = try {
            runtime.dispatchHostEventAndWait(NAVIGATE_EVENT, route)
        } catch (t: Throwable) {
            Log.e(tag, "navigate dispatch threw", t)
            2
        }
        lastNavigateHostEventRcForTest = rc
        if (rc != 0) Log.w(tag, "[deep-link] warm re-route '$route' → rc $rc")
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
     *
     * ── PHASE 7.4 (design decision 3): BACK CONSULTS THE MODAL STACK FIRST ──
     * The rule, stated once: *on back-invoked, if the mapper has ≥ 1 live
     * overlay, the shell dispatches on the topmost modal's `click` wire — the
     * dismissal REQUEST, the same wire a scrim tap rides — and CONSUMES the
     * back event; navigation-back runs only when no overlay is live.* The
     * dispatch is a request: .NET flips Visible and the re-render removes the
     * overlay — the shell never self-closes (the 7.3 state-owner lesson). A
     * back that races an in-flight dismissal is absorbed by construction
     * (Visible already false, the second VisibleChanged(false) moves nothing;
     * a stale handler is rc-0 at-most-once). The consult sits AFTER the booted
     * guard only for symmetry — no overlay can exist before the mount.
     */
    private fun handleBack(): Boolean {
        if (!booted) return false
        if (mapper.requestTopmostModalDismissal()) return true
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
