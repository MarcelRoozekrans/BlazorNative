// ─────────────────────────────────────────────────────────────────────────────
// AppDelegate — Phase 5.2 (M5 DoD #2): the minimal iOS host that exists so the
// Swift shell links the NativeAOT static archive and so the hosted XCTest bundle
// has a TEST_HOST to run inside. A classic (non-scene) UIWindow app to keep the
// project shape minimal — no Info.plist scene manifest needed.
//
// Under XCTest the app stays INERT (it does not boot the runtime): the test owns
// the single native session (init is idempotent, register/mount are last-wins, so
// two booters would race the callback routing). For a real launch the
// HostViewController boots BnDemo — the seed of the Phase 5.3 demo app.
// ─────────────────────────────────────────────────────────────────────────────

import UIKit

@main
final class AppDelegate: UIResponder, UIApplicationDelegate {

    var window: UIWindow?

    func application(_ application: UIApplication,
                     didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        // Phase 6.0 Yoga spike: reference the Yoga probe at launch so the linker
        // keeps Yoga live in the app binary (proving it coexists with the runtime
        // .a) + a launch smoke that Yoga is callable in-process.
        BnYogaProbe.warmUp()

        // A COLD deep link: the app was LAUNCHED by a blazornative:// URL. There is
        // no session yet, so this only stashes the route — HostViewController reads
        // it a moment later to mount the right page directly, instead of showing the
        // default one for a frame and navigating away from it.
        if let url = launchOptions?[.url] as? URL {
            BnDeepLink.shared.handle(url: url)
        }

        let window = UIWindow(frame: UIScreen.main.bounds)
        window.rootViewController = HostViewController()
        window.makeKeyAndVisible()
        self.window = window
        return true
    }

    // ── Deep links (warm) ────────────────────────────────────────────────────

    /// A blazornative:// URL opened while the app is alive — the twin of Android's
    /// `onNewIntent` re-route. Returning the handled flag honestly matters: iOS
    /// hands this callback URLs the app never registered for, and claiming those
    /// would be claiming to have navigated somewhere.
    func application(_ app: UIApplication,
                     open url: URL,
                     options: [UIApplication.OpenURLOptionsKey: Any] = [:]) -> Bool {
        BnDeepLink.shared.handle(url: url)
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────
    //
    // The Android twins, name for name — see BnAppLifecycle for why these
    // particular UIKit callbacks are the honest pairs, and for the one asymmetry
    // (`willTerminate` is not guaranteed, so persistence belongs on `onPause`).
    // Each is a no-op until a session exists, which is Android's `booted` guard:
    // UIKit delivers `didBecomeActive` during launch, before the runtime boots.

    func applicationDidBecomeActive(_ application: UIApplication) {
        BnAppLifecycle.dispatch(BnAppLifecycle.onResume)
    }

    func applicationWillResignActive(_ application: UIApplication) {
        BnAppLifecycle.dispatch(BnAppLifecycle.onPause)
    }

    func applicationWillTerminate(_ application: UIApplication) {
        BnAppLifecycle.dispatch(BnAppLifecycle.onDestroy)
    }
}
