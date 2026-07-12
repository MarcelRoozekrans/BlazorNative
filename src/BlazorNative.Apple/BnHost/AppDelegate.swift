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
        let window = UIWindow(frame: UIScreen.main.bounds)
        window.rootViewController = HostViewController()
        window.makeKeyAndVisible()
        self.window = window
        return true
    }
}
