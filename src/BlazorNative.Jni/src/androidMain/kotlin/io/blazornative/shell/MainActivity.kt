package io.blazornative.shell

import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.widget.FrameLayout
import android.widget.TextView
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher
import androidx.fragment.app.FragmentActivity
import io.blazornative.jni.BlazorNativeRuntime
import io.blazornative.jni.BnLogLevel
import io.blazornative.jni.BnPlatformKind
import kotlin.concurrent.thread

/**
 * Phase 3.0d Android shell entry point — boots the NativeAOT pipeline.
 *
 * On launch: spawns a background thread that runs [BlazorNativeRuntime.start]
 * (init → register frame callback → register shell bridge → mount
 * the app's default component, or the [EXTRA_COMPONENT] Intent-extra
 * override; 4 [BOOT] lines since Phase 3.1) against the
 * NativeAOT libBlazorNative.Runtime.so from the APK's jniLibs. Frames
 * arrive through the C-ABI struct path (NativeFrameAdapter) and render via
 * [WidgetMapper] into widget_root; [BOOT] status lines go to the green-on-black
 * console TextView ALWAYS, and to logcat only at [BnLogLevel.INFO] or higher
 * (#200 — they are narration, and the default threshold is Warn). The two are
 * deliberately different surfaces: the panel is app UI, the logcat line is a log.
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
         * (a mount-registry name, HostSession.cs). Absent → the app's default
         * launcher component (the last-resort mount name in [onCreate]'s chain,
         * which the deep-link route table's "/" row overrides); tests that pin
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

        /**
         * Phase 11.4 Gate B (#155): Intent-extra override for the framework log
         * level — a BnLogLevel NAME ("Error"/"Warn"/"Info"/"Debug"/"Verbose"),
         * case-insensitive. Highest precedence, mirroring [EXTRA_COMPONENT]'s
         * test-override role: an instrumented test or `adb shell am start -e … Debug`
         * can raise verbosity for ONE launch without touching the app.
         */
        const val EXTRA_LOG_LEVEL = "io.blazornative.shell.EXTRA_LOG_LEVEL"

        /**
         * Phase 11.4 Gate B (#155): THE APP AUTHOR'S KNOB — a manifest
         * `<meta-data android:name="io.blazornative.logLevel" android:value="Debug"/>`
         * on the `<application>` (or on this `<activity>`).
         *
         * WHY MANIFEST META-DATA RATHER THAN THE MSBUILD PROPERTY §3.3(3) SKETCHES.
         * The design's third input is "an MSBuild property flowing into the shell's
         * own default". On Android there is no such flow: the shell is a GRADLE
         * project built from COPIED SOURCE, and the .NET build only ever produces the
         * `.so` it links — no MSBuild property reaches this file. The honest Android
         * equivalents were a gradle `buildConfigField` (which needs
         * `buildFeatures { buildConfig = true }` in both this build.gradle.kts and
         * the template's mirror, to configure something a manifest attribute already
         * expresses) or this. Manifest meta-data wins: it is per-APK, per-flavour,
         * needs no build-system change in either copy, and an app author editing
         * their own AndroidManifest is the platform-idiomatic way to set an app-wide
         * default. The MSBuild-property variant is therefore SCOPED OUT for Android,
         * deliberately; iOS's Info.plist twin is Gate C's to decide.
         *
         * An absent or unparseable value resolves to UNSET → the runtime's quiet
         * Release default (Warn), never to silence.
         */
        const val LOG_LEVEL_META_DATA = "io.blazornative.logLevel"
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

        // ── Phase 11.4 Gate B (#155/#164): THE STDERR → LOGCAT PUMP ──────────
        //
        // FIRST STATEMENT IN onCreate, AND THE ORDER IS THE POINT — not tidiness.
        // Everything the .NET runtime, the BCL and NativeAOT itself diagnose goes
        // to process fd 2, which Android sends to /dev/null. BnStderrLogcatPump
        // dup2()s a pipe over fd 2 and forwards each line to android.util.Log.
        //
        // It MUST be installed before `blazornative_init` runs — i.e. before the
        // dlopen that NativeBindings.INSTANCE triggers inside runtime.start() on
        // the boot thread below. init's failure path is the one place the
        // framework deliberately emits a full ex.ToString(), because for the real
        // NativeAOT trim failures (TypeLoadException, MissingMethodException) the
        // Message alone hides the offending type. Catching THAT line is one of the
        // two reasons the design chose this transport over routing logs through
        // the bridge; install it late and the highest-value diagnostic the
        // framework ever writes is still lost.
        //
        // Ahead of SoLoader too, so a native-loader failure is visible as well.
        // Idempotent: Activity recreation re-enters onCreate and the second call
        // is a no-op — fd 2 is already ours and one reader thread is enough.
        BnStderrLogcatPump.install()

        // ── #200: THE SHELL'S OWN NARRATION JOINS THE SAME GATE ──────────────
        //
        // ONE resolution, TWO uses, from ONE local — and that is the whole
        // guarantee that this is not a second level concept. `resolveLogLevel()`
        // reads the documented knobs (EXTRA_LOG_LEVEL / the manifest meta-data)
        // exactly once; the ordinal is installed in BnShellLog here and handed to
        // the runtime at BlazorNativeInitOptions.logLevel (offset 28) on the boot
        // thread below. Raising the level raises both, always, because they are
        // the same number.
        //
        // RESOLVED HERE, NOT ON THE BOOT THREAD (where it used to be read): the
        // FIRST gated line in this Activity is the deep-link startup route, and
        // that is emitted further down onCreate, long before the boot thread
        // reaches init. A threshold installed later would let exactly the lines
        // #200 observed through at Warn.
        val logLevel = resolveLogLevel()
        BnShellLog.setLevelFromOrdinal(logLevel)

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
        // resolves the mount component by name (the generated route map) — the
        // mount is still by NAME, per the design, but resolved shell-side so it
        // is robust to Activity recreation + the shared instrumented session
        // (see loadDeepLinkRoutes). PRECEDENCE: an explicit EXTRA_COMPONENT
        // (test override) wins the mount over a deep link; the route seed still
        // applies.
        val deepLinkRoute = parseDeepLinkRoute(intent)
        // #200: Info — narration, suppressed at the Release default (Warn) and
        // back at `Info` or higher. NOT deleted: the Phase 11.2 device runbook
        // names this exact line in its deep-link PASS criteria.
        if (deepLinkRoute != null) BnShellLog.info(tag, "[deep-link] startup route → $deepLinkRoute")

        // Phase 3.1: the shell half of IMobileBridge. Passing the Activity is
        // safe — AndroidShellBridge captures applicationContext ONLY (the
        // process-lifetime retention contract on ShellBridgeHandlers).
        val bridge = AndroidShellBridge(this, initialRoute = deepLinkRoute ?: "/", onError = onError)
        shellBridge = bridge

        val routes = loadDeepLinkRoutes()
        val componentName = intent.getStringExtra(EXTRA_COMPONENT)
            ?: deepLinkRoute?.let { routes[it] }
            ?: routes["/"]
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
                    platformKind = BnPlatformKind.ANDROID, // Phase 10.0 (#121): report Android, not a shared default
                    logLevel = logLevel,                   // Phase 11.4 (#155): offset 28, resolved in onCreate (#200) — the SAME ordinal BnShellLog holds
                    bridge = bridge,
                )
                booted = true // host→.NET entry (lifecycle/back) is safe only now
                // Emit each line as one gated Info call so logcat shows them as
                // atomic lines (filter via `adb logcat -s BlazorNative`).
                //
                // #200: these four are the lines observed leaking at the default
                // Warn on a released device build. They are NARRATION — Info — so
                // they are silent in Release and reappear at Info or higher.
                // ⚠ THE ON-SCREEN PANEL BELOW IS UNCHANGED AND MUST STAY SO: it
                // is app UI, not logging, and no threshold governs it.
                lines.forEach { BnShellLog.info(tag, it) }
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
        // #200: narration (Info). The rc != 0 line below stays a bare Log.w —
        // a re-route the live session refused is a bent contract, and warnings
        // ship in Release.
        BnShellLog.info(tag, "[deep-link] warm re-route → $route")
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

    /**
     * Phase 11.0 (M11 DoD #1): the deep-link route → mount-component map, read
     * from the build-time-GENERATED Android resource
     * res/raw/blazornative_routes.json.
     *
     * A page is declared ONCE, in the app's manifest (AppPages.All →
     * BlazorNativeApp.RegisterPages); BlazorNative.RouteGen reads that registry at
     * build time and emits this resource, so the map is never hand-maintained and
     * cannot drift from the pages — the pre-11.0 footgun (add a routed page, forget
     * the hand-written map, and the deep link silently opens the wrong screen). The
     * shell still resolves the target component HERE, at Intent-parse time BEFORE
     * the native library loads: the resource is readable without .NET.
     *
     * Resolved by NAME (getIdentifier), not the compile-time
     * R.raw.blazornative_routes: this file is a byte-identical template mirror and a
     * generated app's R lives in a different package, so a compile-time R reference
     * here would fail `dotnet new` template compilation (the WidgetMapper font
     * precedent). A missing or malformed resource → an empty map, LOGGED, and the
     * caller falls back to the default component — a deep link resolves to the
     * default rather than crashing Intent parse.
     */
    private fun loadDeepLinkRoutes(): Map<String, String> {
        return try {
            val id = resources.getIdentifier("blazornative_routes", "raw", packageName)
            if (id == 0) {
                Log.w(tag, "deep-link routes (res/raw/blazornative_routes.json) did not " +
                    "resolve — deep links fall back to the default component")
                return emptyMap()
            }
            val json = resources.openRawResource(id).bufferedReader().use { it.readText() }
            val obj = org.json.JSONObject(json)
            buildMap {
                for (key in obj.keys()) put(key, obj.getString(key))
            }
        } catch (t: Throwable) {
            Log.e(tag, "failed to read res/raw/blazornative_routes.json — deep links fall " +
                "back to the default component", t)
            emptyMap()
        }
    }

    /**
     * Phase 11.4 Gate B (#155): resolves the BnLogLevel ordinal this boot passes in
     * `BlazorNativeInitOptions.logLevel` (offset 28) — and, since #200, the ordinal
     * [BnShellLog] gates the shell's OWN narration with. Called EXACTLY ONCE, at the
     * top of [onCreate], and the single result feeds both: that is what makes the
     * shell and the runtime incapable of disagreeing about the configured level.
     *
     * PRECEDENCE, highest first — the same shape [EXTRA_COMPONENT] establishes for
     * the mount name:
     *   1. the [EXTRA_LOG_LEVEL] Intent extra — a per-launch override for a test or
     *      an `adb shell am start -e …` session;
     *   2. the [LOG_LEVEL_META_DATA] manifest entry — the APP AUTHOR'S declaration,
     *      read from this Activity's `<meta-data>` first and then the
     *      `<application>`'s, so an app can set one app-wide default and still
     *      override it for a debug Activity;
     *   3. nothing — ordinal 0 (UNSET), which the runtime resolves to its own quiet
     *      Release default (Warn).
     *
     * NEVER THROWS AND NEVER SILENCES. A typo'd name, a missing package entry, or a
     * SecurityException reading meta-data all fall through to UNSET, because the
     * failure mode of "the log config was wrong" must not be "there are no logs".
     */
    private fun resolveLogLevel(): Int {
        intent?.getStringExtra(EXTRA_LOG_LEVEL)?.let { name ->
            val fromExtra = BnLogLevel.fromName(name)
            if (fromExtra != BnLogLevel.UNSET) return fromExtra
            Log.w(tag, "$EXTRA_LOG_LEVEL='$name' is not a BnLogLevel name " +
                "(Error/Warn/Info/Debug/Verbose) — falling back to the manifest/default")
        }

        return try {
            val pm = packageManager
            val activityMeta = pm.getActivityInfo(componentName, PackageManager.GET_META_DATA)
                .metaData?.getString(LOG_LEVEL_META_DATA)
            val appMeta = pm.getApplicationInfo(packageName, PackageManager.GET_META_DATA)
                .metaData?.getString(LOG_LEVEL_META_DATA)

            val name = activityMeta ?: appMeta ?: return BnLogLevel.UNSET
            val level = BnLogLevel.fromName(name)
            if (level == BnLogLevel.UNSET) {
                Log.w(tag, "<meta-data android:name=\"$LOG_LEVEL_META_DATA\" " +
                    "android:value=\"$name\"/> is not a BnLogLevel name " +
                    "(Error/Warn/Info/Debug/Verbose) — using the runtime default")
            }
            level
        } catch (t: Throwable) {
            Log.w(tag, "could not read $LOG_LEVEL_META_DATA — using the runtime default", t)
            BnLogLevel.UNSET
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
