// ─────────────────────────────────────────────────────────────────────────────
// BnAppLifecycle — the iOS half of the host lifecycle events, and the mapping
// decision that makes it parity rather than approximation.
//
// Android has dispatched `onResume` / `onPause` / `onDestroy` since Phase 4.x and
// pins them on-device. **iOS dispatched NOTHING** — the export existed, the
// `NativeEvent` channel existed, and no Swift code ever called it for lifecycle.
// An app subscribing to `IMobileBridge.NativeEvents` to save state on
// backgrounding worked on one platform and silently never fired on the other.
//
// ── THE MAPPING, AND WHY THESE PAIRS ─────────────────────────────────────────
// The event NAMES stay Android's, because the name is the wire contract an app
// author subscribes to and two names for one concept would make cross-platform
// code branch for no reason.
//
//   onResume  ← applicationDidBecomeActive
//   onPause   ← applicationWillResignActive
//   onDestroy ← applicationWillTerminate
//
// `willResignActive` — NOT `didEnterBackground` — is the honest twin of Android's
// `onPause`, and the choice is deliberate. Android fires `onPause` whenever the
// Activity stops being in the foreground *including partial obscuring* (a dialog,
// the recents switcher). `willResignActive` is exactly that on iOS: it fires for
// transient interruptions (Control Centre, an incoming call) as well as for
// backgrounding. `didEnterBackground` fires only for the full transition, so an
// app using `onPause` to pause a timer would keep running through an incoming
// call on iOS and not on Android.
//
// ── ONE HONEST ASYMMETRY, WRITTEN DOWN RATHER THAN HIDDEN ────────────────────
// **`applicationWillTerminate` IS NOT GUARANTEED.** iOS routinely kills a
// suspended app without calling it. Android's `onDestroy` is not guaranteed
// either, but it is far more reliable in practice. So `onDestroy` is a
// best-effort signal on BOTH platforms and a *weaker* one on iOS: persistence
// must hang off `onPause`, which fires every time. That sentence is the whole
// reason this comment exists — an app author who learns it from a lost draft
// has learned it the expensive way.
// ─────────────────────────────────────────────────────────────────────────────

import UIKit

enum BnAppLifecycle {

    /// The wire names, shared with Android. Not an enum of iOS concepts: these
    /// are the strings that cross `blazornative_host_event`, and the Kotlin side
    /// spells them exactly this way.
    static let onResume  = "onResume"
    static let onPause   = "onPause"
    static let onDestroy = "onDestroy"

    /// Test seam: receives the event name instead of the runtime, so a hosted
    /// test can assert the mapping without a live native session (the app stays
    /// inert under XCTest, so there is no runtime to dispatch to).
    static var sinkForTest: ((String) -> Void)?

    /// Dispatches one lifecycle event, or does nothing if no session is live.
    ///
    /// THE GUARD IS THE POINT, and it is Android's `booted` flag by another name:
    /// UIKit delivers `didBecomeActive` during launch, BEFORE `HostViewController`
    /// has booted the runtime, and under XCTest it never boots at all. Calling the
    /// ABI then would be a call into a session that does not exist. Android skips
    /// its first `onResume` for the identical reason — the initial mount IS the
    /// first resume.
    static func dispatch(_ name: String) {
        if let sink = sinkForTest { sink(name); return }
        guard let runtime = BnRuntime.current else { return }
        runtime.dispatchHostEvent(name: name, payload: nil)
    }
}
