// ─────────────────────────────────────────────────────────────────────────────
// BnNotifications — Phase 9.1 Gate 3 (M9 DoD #3): the iOS half of local
// notifications + the WARM tap-through, the mirror of AndroidShellBridge's
// NotificationManager/AlarmManager flow. The FIRST reuse of the 9.0 generic ABI on
// iOS: op=Notifications rides the SAME `AppleShellBridge.hostCallBegin` slot
// (offset 72) geolocation uses, so the bridge stays 80 bytes / 10 exports — no
// struct grow, no new export. Notifications add an op-enum value + a wire-mirrored
// status enum + host handlers, and touch the ABI at NOTHING else.
//
// DENIAL IS DATA (the milestone law, restated for UNUserNotificationCenter): the
// terminal outcome ALWAYS returns as a wire-mirrored `BnNotificationStatus` via
// `blazornative_host_call_complete` — never a Swift error thrown across the C
// boundary, never a dropped completion (a hang). schedule/show/cancel carry NO
// payload (a status is the whole answer). Every branch — check, grant, denial,
// error — calls `complete(...)` exactly once, so the awaiting .NET ValueTask always
// resolves within a bounded await.
//
// DELEGATE RETENTION (the CLLocationManager lesson from 9.0 Gate 3, restated for
// UNUserNotificationCenter): `UNUserNotificationCenter.current().delegate` is a WEAK
// reference. So the bridge holds THIS handler strongly (AppleShellBridge.notifications,
// app-lifetime), and this handler IS the center's delegate — neither can be
// deallocated while a tap is being delivered. A locally-scoped delegate would be gone
// before `didReceive` fires.
//
// TAP-THROUGH, ZERO ABI (the design's real work on iOS): a tapped notification carries
// its route in `userInfo["route"]`. The delegate's `didReceive` reads it and, over a
// LIVE session, the iOS shell starts CALLING the EXISTING `blazornative_host_event`
// export with the reserved name "navigate" + the route (the 9.0 "first off-lane call"
// pattern — iOS started calling the pre-existing host_call_complete in 9.0; it starts
// calling the pre-existing host_event here). The name→verb mapping lives in .NET
// (DispatchHostEventCore → NavigateToAsync), so every shell gets identical semantics.
// COLD launch (no live session yet): the route is stashed as `pendingLaunchRoute`,
// which HostViewController resolves (deepLinkComponents — the iOS twin of Android's
// DEEP_LINK_COMPONENTS pinned mirror) to seed the initial mount BY NAME, the way the
// sim boot tests mount a routed component.
//
// SIMULATOR-SCOPED + LABELLED (the M9 iOS deferral): the real notification-permission
// system ALERT is owner-device territory (an Apple Developer account is the trigger,
// exactly as the location dialog was in 9.0), and the real Lock-Screen / Notification-
// Center tap UX is device territory. So the shell exposes seams — an authorization-
// status OVERRIDE, a switch that SUPPRESSES the real requestAuthorization prompt, a
// completion HOOK, and a hand-rolled tap fire — and CI asserts granted-post +
// denied-no-hang + tap→navigate deterministically. The real center's add/remove
// (schedule/cancel) run UNSUPPRESSED, so the sim posts real requests the tests read
// back via getPendingNotificationRequests.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation
import UserNotifications

/// The wire-mirrored notification status (mirror of .NET NotificationStatus / Kotlin
/// NotificationStatus, byte-identical): geolocation's shape MINUS LocationUnavailable
/// (a notification has no such analogue), so Error is 4, not 5. Denial (1/2/3) and
/// error (4) are VALUES the awaiting .NET ValueTask resolves to — never exceptions,
/// never hangs.
///
/// PLATFORM NOTE (a doc-vs-reality finding, recorded here): `UNAuthorizationStatus`
/// has NO `.restricted` case (that state belongs to CLLocationManager, which is where
/// the design's status table copied the row from). So `restricted` (3) is defined for
/// wire parity but is UNREACHABLE from a real UNUserNotificationCenter authorization
/// read on iOS — the reachable iOS statuses are Granted / Denied / DeniedPermanently /
/// Error. The constant stays so the three-way mirror is byte-identical.
enum BnNotificationStatus {
    static let granted: Int32 = 0            // permission held; the op ran (posted / scheduled / cancelled)
    static let denied: Int32 = 1             // denied THIS time; a later request MAY prompt again
    static let deniedPermanently: Int32 = 2  // iOS .denied — only Settings changes it
    static let restricted: Int32 = 3         // policy/MDM — NO UNUserNotificationCenter analogue (wire parity only)
    static let error: Int32 = 4              // unexpected host error (a caught throw / unknown action)
}

final class BnNotifications: NSObject, UNUserNotificationCenterDelegate {

    /// The reserved host-event name the WARM tap-through fires — the EXACT literal
    /// .NET's DispatchHostEventCore intercepts (Exports.NavigateEventName) and Kotlin's
    /// MainActivity.NAVIGATE_EVENT uses. A drift here reds the warm tap-through.
    static let navigateEventName = "navigate"

    /// The iOS route→mount-component mirror — the Swift twin of Android's
    /// MainActivity.DEEP_LINK_COMPONENTS (a hand-written PINNED mirror, resolved before
    /// the `.so` loads / independent of the .NET route table). iOS mounts by NAME, so a
    /// COLD-launch tap route resolves to the component the sim boot mounts. Sample-only
    /// (no iOS template tree — the recorded gap, not a regression).
    static let deepLinkComponents: [String: String] = [
        "/notifications": "BnNotificationsDemo",
        "/geolocation": "BnGeolocationDemo",
    ]

    private let lock = NSLock()

    /// The single in-flight requestId for a permission-gated op (show/schedule/request).
    /// `complete(...)` consumes it (one-shot), so a late/duplicate callback no-ops.
    private var inFlightRequestId: Int64?

    /// The suspended request context for a notDetermined→prompt path (the args + action
    /// to continue with on the authorization result). Cleared by `complete`.
    private var pendingPermissionContext: (requestId: Int64, args: [String: String], action: String)?

    /// A COLD-launch tap route stashed when no live session exists yet (the delegate
    /// fired before boot). HostViewController reads it to seed the initial mount.
    private var pendingLaunchRoute: String?

    /// Wired by BnRuntime (at boot) to dispatch the WARM "navigate" host event on the
    /// serial dispatch lane — the ABI must NOT be called from the delegate's arbitrary
    /// (main) thread directly. Its being non-nil is also the "a live session exists"
    /// signal `handleTap` uses to choose warm re-route over cold stash.
    var navigateDispatcher: ((String) -> Int32)?

    // ── Test seams (static, reset in teardown — the BnGeolocation companion twins) ──

    /// Overrides the read of the current UNAuthorizationStatus so a hosted test drives a
    /// branch deterministically (the sim's real notification-authorization state is not
    /// drivable, and getNotificationSettings is async). Null in production.
    static var authorizationStatusOverrideForTest: (() -> UNAuthorizationStatus)?

    /// When true, the notDetermined path SKIPS the real `requestAuthorization` prompt
    /// (owner-device territory) — the test fires the result via
    /// `fireAuthorizationResultForTest`. Does NOT suppress the real center's add/remove
    /// (schedule/cancel post real requests the sim tests read back). Null/false in
    /// production; reset in teardown so it never leaks.
    static var suppressAuthorizationPromptForTest = false

    /// Intercepts the completion so a PURE unit test (no NativeAOT boot) observes the
    /// routed (requestId, status, payload) without a live .NET continuation.
    static var completeHookForTest: ((Int64, Int32, String?) -> Int32)?

    /// Intercepts the WARM navigate dispatch so a pure unit test observes the (route)
    /// the tap fires without a live .NET continuation. Null → the wired dispatcher (or
    /// the direct export fallback) is called.
    static var navigateHookForTest: ((String) -> Int32)?

    /// The rc of the most recent `blazornative_host_call_complete` — 0 = delivered to a
    /// live .NET continuation, 1 = unknown/already-completed id (benign). Int32.min
    /// before any completion. The BnGeolocation.lastHostCallCompleteRcForTest twin.
    static var lastHostCallCompleteRcForTest: Int32 = Int32.min

    /// The rc of the most recent `blazornative_host_event("navigate", …)` — 0 = the live
    /// session re-routed, 1 = not handled (no session / unknown route). Int32.min before
    /// any warm tap. Proves the iOS shell CALLED the pre-existing host_event export.
    static var lastHostEventRcForTest: Int32 = Int32.min

    /// The UNNotificationRequest most recently handed to the center (captured BEFORE the
    /// add, so a show/schedule's construction — id, content, userInfo route, trigger — is
    /// deterministically assertable regardless of the sim's real delivery/authorization).
    static var lastAddedRequestForTest: UNNotificationRequest?

    static func resetForTest() {
        authorizationStatusOverrideForTest = nil
        suppressAuthorizationPromptForTest = false
        completeHookForTest = nil
        navigateHookForTest = nil
        lastHostCallCompleteRcForTest = Int32.min
        lastHostEventRcForTest = Int32.min
        lastAddedRequestForTest = nil
    }

    func clearInFlightForTest() { lock.lock(); inFlightRequestId = nil; pendingPermissionContext = nil; lock.unlock() }
    func hasInFlightRequestForTest() -> Bool { lock.lock(); defer { lock.unlock() }; return inFlightRequestId != nil }
    func pendingLaunchRouteForTest() -> String? { lock.lock(); defer { lock.unlock() }; return pendingLaunchRoute }
    /// The COLD-launch mount name the stashed route resolves to (the deepLinkComponents
    /// mirror) — how the launch route seeds the initial mount BY NAME. nil when no tap
    /// launch route was stashed (the default-mount case). Read at boot by HostViewController.
    func resolvedLaunchComponent() -> String? {
        guard let route = pendingLaunchRouteForTest() else { return nil }
        return BnNotifications.deepLinkComponents[route]
    }
    /// The retention pin as a PROPERTY (no observable otherwise sees the weak-delegate
    /// hold — the CLLocationManager/6.2 contentInsetAdjustment precedent).
    func delegateIsRetainedForTest() -> Bool {
        UNUserNotificationCenter.current().delegate === self
    }

    // ── The op entry (AppleShellBridge.hostCallBegin forwards here for op=Notifications) ──

    /// Parses the flat-JSON `action` and dispatches. Returns FAST (the begin contract);
    /// the terminal status is a deferred `complete(...)`. cancel/check are permission-
    /// free and synchronous; show/schedule/request are permission-gated (may prompt).
    func begin(requestId: Int64, argsJson: String) {
        let args = BnFlatJson.parseObject(argsJson) ?? [:]
        let action = args["action"] ?? "show"
        switch action {
        case "cancel":
            cancel(requestId: requestId, idString: args["id"])
        case "check":
            currentAuthorizationStatus { [weak self] status in
                guard let self = self else { return }
                self.complete(requestId, self.checkStatus(status), nil)
            }
        case "show", "schedule", "request":
            beginPermissionGated(requestId: requestId, args: args, action: action)
        default:
            // An unknown action is DATA (Error 4), never a crash (the Kotlin posture).
            complete(requestId, BnNotificationStatus.error, nil)
        }
    }

    /// An unknown host-call op: DATA (Error), not a crash — the awaiting .NET ValueTask
    /// resolves rather than leaking a pending entry (the geolocation unknown-op posture,
    /// mapped onto the notification status enum). Not routed through the in-flight slot.
    func completeUnknownOp(requestId: Int64) {
        complete(requestId, BnNotificationStatus.error, nil)
    }

    // ── UNUserNotificationCenterDelegate (the REAL delegate code) ─────────────────

    /// A tapped notification (foreground/background/cold) is delivered here. The route
    /// rides `userInfo["route"]`; a live session re-routes over host_event, a cold launch
    /// stashes it for the mount seed. ALWAYS calls the completion handler (UIKit contract).
    func userNotificationCenter(_ center: UNUserNotificationCenter,
                                didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        handleTap(route: response.notification.request.content.userInfo["route"] as? String)
        completionHandler()
    }

    /// Present a notification even while the app is FOREGROUND (the sim posts while the
    /// test host is frontmost, so without this a `show` would be silently withheld).
    func userNotificationCenter(_ center: UNUserNotificationCenter,
                                willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        if #available(iOS 14.0, *) {
            completionHandler([.banner, .sound])
        } else {
            completionHandler([.alert, .sound])
        }
    }

    // ── Delegate installation + the tap fire (hand-rolled — run the REAL handler) ──

    /// Sets THIS handler as the center's (weakly-held) delegate. Called by BnRuntime at
    /// boot; the bridge's strong hold on this handler keeps the delegate alive.
    func installDelegate() {
        UNUserNotificationCenter.current().delegate = self
    }

    /// Test-only: synthesize a tap carrying `route` and run the REAL `handleTap` (a
    /// UNNotificationResponse has no public initialiser, so the fire drives the seam the
    /// delegate funnels into — the 7.3/9.0 hand-rolled-fire discipline).
    func fireTapForTest(route: String?) { handleTap(route: route) }

    /// Test-only: run the REAL authorization-result continuation for a suppressed prompt.
    func fireAuthorizationResultForTest(granted: Bool) {
        lock.lock(); let ctx = pendingPermissionContext; lock.unlock()
        guard let ctx = ctx else { return }
        handleAuthorizationResult(granted: granted, error: nil,
                                  requestId: ctx.requestId, args: ctx.args, action: ctx.action)
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private func handleTap(route: String?) {
        guard let route = route, !route.isEmpty else { return } // a routeless tap acts on nothing
        if navigateHookForTestIsSet() || navigateDispatcher != nil {
            fireNavigate(route) // WARM — a live session (or a test hook) re-routes
        } else {
            lock.lock(); pendingLaunchRoute = route; lock.unlock() // COLD — seed the mount
        }
    }

    private func navigateHookForTestIsSet() -> Bool { BnNotifications.navigateHookForTest != nil }

    /// Fires the reserved "navigate" host event (route as the bare payload). In
    /// production the wired dispatcher hops to the serial lane before calling the ABI;
    /// the test hook / direct fallback call inline.
    private func fireNavigate(_ route: String) {
        let rc: Int32
        if let hook = BnNotifications.navigateHookForTest {
            rc = hook(route)
        } else if let dispatcher = navigateDispatcher {
            rc = dispatcher(route)
        } else {
            rc = BnNotifications.navigateEventName.withCString { n in
                route.withCString { p in blazornative_host_event(n, p) }
            }
        }
        BnNotifications.lastHostEventRcForTest = rc
    }

    /// Reads the current authorization status (override in test; the async
    /// getNotificationSettings in production). The completion may run on an arbitrary
    /// UNUserNotificationCenter thread — the callers only call `complete`, which is safe.
    private func currentAuthorizationStatus(_ completion: @escaping (UNAuthorizationStatus) -> Void) {
        if let override = BnNotifications.authorizationStatusOverrideForTest {
            completion(override()); return
        }
        UNUserNotificationCenter.current().getNotificationSettings { completion($0.authorizationStatus) }
    }

    /// GRANTED/DENIED/… for the read-only Check (never prompts). `.provisional` folds to
    /// Granted (quiet notifications post — what the caller asked; the design decision).
    private func checkStatus(_ status: UNAuthorizationStatus) -> Int32 {
        switch status {
        case .authorized, .provisional: return BnNotificationStatus.granted
        case .notDetermined: return BnNotificationStatus.denied // not held; a request MAY prompt
        case .denied: return BnNotificationStatus.deniedPermanently // one-shot; only Settings changes it
        @unknown default: return BnNotificationStatus.error // .ephemeral (App Clip) / any future case
        }
    }

    /// show/schedule/request: gate on the current authorization; if held → run the op; if
    /// notDetermined → prompt (or the suppressed test path); a denial is DATA.
    private func beginPermissionGated(requestId: Int64, args: [String: String], action: String) {
        lock.lock(); inFlightRequestId = requestId; lock.unlock()
        currentAuthorizationStatus { [weak self] status in
            guard let self = self else { return }
            switch status {
            case .authorized, .provisional:
                self.runAction(requestId: requestId, args: args, action: action)
            case .notDetermined:
                self.requestAuthorization(requestId: requestId, args: args, action: action)
            case .denied:
                self.complete(requestId, BnNotificationStatus.deniedPermanently, nil)
            @unknown default:
                self.complete(requestId, BnNotificationStatus.error, nil)
            }
        }
    }

    /// Raises the notification-permission prompt (suppressed in test). Stashes the
    /// request context so the result continuation can post on grant / deny as data.
    private func requestAuthorization(requestId: Int64, args: [String: String], action: String) {
        lock.lock(); pendingPermissionContext = (requestId, args, action); lock.unlock()
        if BnNotifications.suppressAuthorizationPromptForTest { return } // the test fires the result
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) { [weak self] granted, error in
            self?.handleAuthorizationResult(granted: granted, error: error,
                                            requestId: requestId, args: args, action: action)
        }
    }

    private func handleAuthorizationResult(granted: Bool, error: Error?,
                                           requestId: Int64, args: [String: String], action: String) {
        if error != nil { complete(requestId, BnNotificationStatus.error, nil); return }
        if granted {
            runAction(requestId: requestId, args: args, action: action)
        } else {
            complete(requestId, BnNotificationStatus.deniedPermanently, nil) // iOS one-shot
        }
    }

    /// Runs the actual op (permission held / just granted). `request` posts nothing (it
    /// only confirms the grant); show/schedule build a UNNotificationRequest and hand it
    /// to the REAL center. A caught throw is DATA (Error), never a hang.
    private func runAction(requestId: Int64, args: [String: String], action: String) {
        if action == "request" {
            complete(requestId, BnNotificationStatus.granted, nil)
            return
        }
        let content = UNMutableNotificationContent()
        content.title = args["title"] ?? ""
        content.body = args["body"] ?? ""
        if let route = args["route"] { content.userInfo = ["route": route] } // the tap-through carrier
        let identifier = args["id"] ?? "0"
        let request = UNNotificationRequest(
            identifier: identifier, content: content, trigger: triggerFor(action: action, when: args["when"]))
        BnNotifications.lastAddedRequestForTest = request
        UNUserNotificationCenter.current().add(request) { [weak self] error in
            self?.complete(requestId, error == nil ? BnNotificationStatus.granted : BnNotificationStatus.error, nil)
        }
    }

    /// The trigger: `show` is immediate (nil trigger → delivered at once); `schedule`
    /// fires from `when` (Unix ms) as a one-shot time-interval trigger. A missing/invalid
    /// `when` on schedule falls back to a near-immediate trigger (UNTimeInterval must be
    /// > 0), mirroring Android's `whenMs ?: now`.
    private func triggerFor(action: String, when: String?) -> UNNotificationTrigger? {
        guard action == "schedule" else { return nil }
        let nowMs = Date().timeIntervalSince1970 * 1000
        let whenMs = when.flatMap { Double($0) } ?? nowMs
        let seconds = max((whenMs - nowMs) / 1000.0, 1.0)
        return UNTimeIntervalNotificationTrigger(timeInterval: seconds, repeats: false)
    }

    /// cancel → drop a shown notification AND a pending scheduled one by id (idempotent,
    /// permission-free — nothing to deny, so GRANTED). The real center's remove* run
    /// unsuppressed so the sim tests read the removal back via getPending.
    private func cancel(requestId: Int64, idString: String?) {
        let identifier = idString ?? "0"
        let center = UNUserNotificationCenter.current()
        center.removePendingNotificationRequests(withIdentifiers: [identifier])
        center.removeDeliveredNotifications(withIdentifiers: [identifier])
        complete(requestId, BnNotificationStatus.granted, nil)
    }

    /// The single completion funnel. Consumes the in-flight slot iff it still matches
    /// (one-shot), clears any suspended prompt context, then delivers the status to .NET
    /// (payload is always nil for notifications — a status is the whole answer).
    private func complete(_ requestId: Int64, _ status: Int32, _ payload: String?) {
        lock.lock()
        if inFlightRequestId == requestId { inFlightRequestId = nil }
        if pendingPermissionContext?.requestId == requestId { pendingPermissionContext = nil }
        lock.unlock()

        let rc: Int32
        if let hook = BnNotifications.completeHookForTest {
            rc = hook(requestId, status, payload)
        } else if let payload = payload {
            rc = payload.withCString { blazornative_host_call_complete(requestId, status, $0) }
        } else {
            rc = blazornative_host_call_complete(requestId, status, nil)
        }
        BnNotifications.lastHostCallCompleteRcForTest = rc
    }
}
