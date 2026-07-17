// ─────────────────────────────────────────────────────────────────────────────
// BnNotificationsTests — Phase 9.1 Gate 3 (M9 DoD #3): local notifications + the
// WARM tap-through on the iOS simulator. The iOS third of BnNotificationsDemoTests.cs
// (.NET, DevHostBridge drives the five statuses headless) + BnNotificationsAndroidTest.kt
// (the AVD NotificationManager flow). The bridge/struct/export pins are UNCHANGED —
// notifications add op=1 + a wire status enum + a handler, and touch the ABI at
// nothing else (BnDriftTests still asserts 80 bytes / offset 72 / 10 exports, UNMOVED).
//
// TWO LAYERS, mirroring the geolocation + Android suites:
//   • UNIT (no NativeAOT boot) — the status MATRIX via completeHookForTest (Granted/
//     Denied/DeniedPermanently as DATA + Error from an unknown action; provisional
//     folds to Granted); the real UNUserNotificationCenter add/remove (a `schedule`
//     posts a PENDING request the test reads back via getPendingNotificationRequests,
//     `cancel` removes it, a `show` builds an immediate request carrying the route);
//     the denial DANCE (notDetermined→deny is a status, never a hang); tap-through
//     driven through the delegate seam (warm → host_event("navigate"), cold → the
//     stashed launch route resolving to a mount component); the delegate RETENTION.
//   • BOOT (real NativeAOT, /notifications mounted) — the round trip through the REAL
//     blazornative_host_call_complete (a Show grant echoes "status:Granted", a denial
//     echoes "status:DeniedPermanently", both within a BOUNDED await, rc 0) and the
//     WARM tap re-routing a LIVE session over the REAL blazornative_host_event (the
//     iOS shell's FIRST call of it) — the page mounts (echo "arrived:/notifications").
//
// SIMULATOR-SCOPED + LABELLED (the M9 iOS deferral): the real permission ALERT + the
// real Lock-Screen/Notification-Center tap UX are owner-device territory. The
// authorization-status OVERRIDE + the prompt SUPPRESS switch + the hand-rolled tap
// fire stand in; the real center's add/remove (schedule/cancel) run UNSUPPRESSED so
// the sim posts real requests the tests read back.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UserNotifications
import UIKit
@testable import BnHost

final class BnNotificationsTests: BnHostTestCase {

    private let capturedLock = NSLock()
    private var captured: [(id: Int64, status: Int32, payload: String?)] = []
    private var runtime: BnRuntime?
    private var root: UIView!

    override func setUp() {
        super.setUp()
        BnNotifications.resetForTest()
        clearCentre()
        captured = []
    }

    override func tearDown() {
        BnNotifications.resetForTest()
        clearCentre()
        UNUserNotificationCenter.current().delegate = nil // never leave a bridge as the global delegate
        super.tearDown()
    }

    private func installCapture() {
        BnNotifications.completeHookForTest = { [weak self] id, status, payload in
            guard let self = self else { return 0 }
            self.capturedLock.lock(); self.captured.append((id, status, payload)); self.capturedLock.unlock()
            return 0
        }
    }

    private func capturedStatuses() -> [Int32] {
        capturedLock.lock(); defer { capturedLock.unlock() }; return captured.map { $0.status }
    }

    // ── Check: each authorization status reported as DATA, never a prompt ─────────

    func testCheckReportsGrantedWhenAuthorized() {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .authorized }
        _ = bridge.hostCallBegin(1, BnHostCallOp.notifications, "{\"action\":\"check\"}")
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.granted])
    }

    func testCheckReportsGrantedWhenProvisional() {
        installCapture()
        let bridge = AppleShellBridge()
        // The design decision: .provisional (quiet notifications) POST — so Granted.
        BnNotifications.authorizationStatusOverrideForTest = { .provisional }
        _ = bridge.hostCallBegin(2, BnHostCallOp.notifications, "{\"action\":\"check\"}")
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.granted])
    }

    func testCheckReportsDeniedWhenNotDetermined() {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .notDetermined }
        _ = bridge.hostCallBegin(3, BnHostCallOp.notifications, "{\"action\":\"check\"}")
        // Not held → Denied (a later request MAY prompt) — never a hang, never a prompt.
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.denied])
    }

    func testCheckReportsDeniedPermanentlyWhenDenied() {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .denied }
        _ = bridge.hostCallBegin(4, BnHostCallOp.notifications, "{\"action\":\"check\"}")
        // iOS .denied is one-shot — re-request will not prompt; only Settings changes it.
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.deniedPermanently])
    }

    // ── Error (4) is DATA: an unknown action never crashes ───────────────────────

    func testUnknownActionCompletesErrorAsData() {
        installCapture()
        let bridge = AppleShellBridge()
        let rc = bridge.hostCallBegin(5, BnHostCallOp.notifications, "{\"action\":\"frobnicate\"}")
        XCTAssertEqual(rc, 0, "begin returns synchronously even for an unknown action")
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.error])
    }

    // ── The op routes to the notifications handler (op=1, not geolocation) ───────

    func testHostCallBeginRoutesTheNotificationsOpToTheHandler() {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .authorized }
        // op=1 must reach BnNotifications (a Granted check), NOT geolocation (op 0).
        _ = bridge.hostCallBegin(6, BnHostCallOp.notifications, "{\"action\":\"check\"}")
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.granted])
    }

    // ── The wire status enum — the three-way mirror pinned (byte-identical) ──────

    func testTheWireStatusConstantsMatchTheThreeWayContract() {
        // The EXACT integers .NET NotificationStatus / Kotlin NotificationStatus carry
        // (geolocation's shape MINUS LocationUnavailable → Error is 4). restricted (3)
        // is defined for parity though UNAuthorizationStatus has no analogue (see
        // BnNotifications' PLATFORM NOTE).
        XCTAssertEqual(BnNotificationStatus.granted, 0)
        XCTAssertEqual(BnNotificationStatus.denied, 1)
        XCTAssertEqual(BnNotificationStatus.deniedPermanently, 2)
        XCTAssertEqual(BnNotificationStatus.restricted, 3)
        XCTAssertEqual(BnNotificationStatus.error, 4)
        // The reserved warm-tap event name must be the exact literal .NET intercepts.
        XCTAssertEqual(BnNotifications.navigateEventName, "navigate")
    }

    // ── show: an IMMEDIATE request carrying the route in userInfo (Granted) ───────

    func testShowBuildsAnImmediateRequestCarryingTheRouteInUserInfo() throws {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .authorized }
        _ = bridge.hostCallBegin(
            10, BnHostCallOp.notifications,
            "{\"action\":\"show\",\"id\":\"7\",\"title\":\"Hello\",\"body\":\"A local notification\",\"route\":\"/notifications\"}")

        XCTAssertTrue(pollUntil { self.capturedStatuses() == [BnNotificationStatus.granted] },
                      "show never completed Granted (a hang, or the real center's add never returned)")
        let req = try XCTUnwrap(BnNotifications.lastAddedRequestForTest, "show must hand a request to the center")
        XCTAssertEqual(req.identifier, "7", "the id is the app-chosen int, stringified")
        XCTAssertEqual(req.content.title, "Hello")
        XCTAssertEqual(req.content.body, "A local notification")
        XCTAssertNil(req.trigger, "an immediate show carries a NIL trigger")
        XCTAssertEqual(req.content.userInfo["route"] as? String, "/notifications",
                       "the tap-through route rides userInfo[route]")
    }

    // ── schedule: a time-interval request built from `when`, handed to the centre ─
    //
    // Asserted on the construction seam, NOT getPendingNotificationRequests: the CI
    // simulator is UNAUTHORIZED (the permission alert is owner-device territory), and an
    // unauthorized app's `add` does not register a pending request — getPending is empty
    // on CI regardless. The production `add` still runs; the seam is the honest proof.

    func testScheduleBuildsATimeIntervalRequestFromWhen() throws {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .authorized }
        let whenMs = Int64((Date().timeIntervalSince1970 + 3600) * 1000) // an hour out
        _ = bridge.hostCallBegin(
            11, BnHostCallOp.notifications,
            "{\"action\":\"schedule\",\"id\":\"7\",\"title\":\"Hello (soon)\",\"body\":\"A scheduled notification\","
                + "\"when\":\"\(whenMs)\",\"route\":\"/notifications\"}")

        XCTAssertTrue(pollUntil { self.capturedStatuses() == [BnNotificationStatus.granted] },
                      "schedule never completed Granted")
        let req = try XCTUnwrap(BnNotifications.lastAddedRequestForTest)
        XCTAssertEqual(req.identifier, "7")
        XCTAssertEqual(req.content.userInfo["route"] as? String, "/notifications")
        let trigger = try XCTUnwrap(req.trigger as? UNTimeIntervalNotificationTrigger,
                                    "schedule carries a time-interval trigger built from `when`")
        XCTAssertFalse(trigger.repeats, "a scheduled notification fires ONCE")
        XCTAssertGreaterThan(trigger.timeInterval, 3000, "the interval is ~when-minus-now, not an immediate 0.1")
    }

    // ── cancel: targets the id at the REAL centre + completes Granted (idempotent) ─

    func testCancelTargetsTheIdAtTheCentreAndCompletesGranted() {
        installCapture()
        let bridge = AppleShellBridge()
        // cancel is permission-free — no authorization override needed.
        _ = bridge.hostCallBegin(13, BnHostCallOp.notifications, "{\"action\":\"cancel\",\"id\":\"7\"}")
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.granted], "cancel is idempotent → Granted")
        // The production removePending/removeDelivered ran against the real centre keyed by
        // the id (the seam — an unauthorized sim never had a pending request to observe gone).
        XCTAssertEqual(BnNotifications.lastCancelledIdForTest, "7", "cancel targeted the app-chosen id")
    }

    // ── Denial dance: notDetermined then the user's outcome as DATA (never a hang) ─

    func testRequestNotDeterminedThenUserDenialIsDataNotAHang() {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .notDetermined }
        BnNotifications.suppressAuthorizationPromptForTest = true

        _ = bridge.hostCallBegin(20, BnHostCallOp.notifications, "{\"action\":\"request\"}")
        XCTAssertTrue(bridge.notifications.hasInFlightRequestForTest(), "the request awaits the authorization outcome")
        bridge.notifications.fireAuthorizationResultForTest(granted: false) // the user says no

        // Denial is a VALUE (2), delivered — not a Swift error across the boundary, not a hang.
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.deniedPermanently])
        XCTAssertFalse(bridge.notifications.hasInFlightRequestForTest(), "the slot is consumed by the outcome")
    }

    func testRequestNotDeterminedThenUserGrantIsGranted() {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .notDetermined }
        BnNotifications.suppressAuthorizationPromptForTest = true

        _ = bridge.hostCallBegin(21, BnHostCallOp.notifications, "{\"action\":\"request\"}")
        bridge.notifications.fireAuthorizationResultForTest(granted: true)
        // request is permission-only → a grant posts nothing and confirms Granted.
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.granted])
    }

    func testALateDuplicateAuthorizationResultNoOps() {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .notDetermined }
        BnNotifications.suppressAuthorizationPromptForTest = true

        _ = bridge.hostCallBegin(22, BnHostCallOp.notifications, "{\"action\":\"request\"}")
        bridge.notifications.fireAuthorizationResultForTest(granted: false) // completes id 22
        bridge.notifications.fireAuthorizationResultForTest(granted: true)  // late duplicate — context cleared
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.deniedPermanently],
                       "exactly ONE completion per request; the suspended context is one-shot")
    }

    // ── Tap-through: the delegate seam → warm host_event / cold launch-route stash ─

    func testWarmTapFiresTheNavigateHostEventWithTheRoute() {
        let bridge = AppleShellBridge()
        var fired: [String] = []
        BnNotifications.navigateHookForTest = { route in fired.append(route); return 0 }

        bridge.notifications.fireTapForTest(route: "/notifications")
        XCTAssertEqual(fired, ["/notifications"], "a warm tap fires host_event(navigate, route)")
        XCTAssertEqual(BnNotifications.lastHostEventRcForTest, 0, "the warm re-route reported rc 0")
    }

    func testColdTapStashesTheLaunchRouteAndResolvesTheMountComponent() {
        let bridge = AppleShellBridge()
        // No dispatcher wired (no live session) and no hook → COLD: the route is stashed
        // and resolves (deepLinkComponents) to the component the sim boot mounts BY NAME.
        bridge.notifications.fireTapForTest(route: "/notifications")
        XCTAssertEqual(bridge.notifications.pendingLaunchRouteForTest(), "/notifications")
        XCTAssertEqual(bridge.notifications.resolvedLaunchComponent(), "BnNotificationsDemo",
                       "the launch route seeds the initial mount by name (the iOS deep-link mirror)")
    }

    func testATapWithNoRouteFiresNothingAndStashesNothing() {
        let bridge = AppleShellBridge()
        var fired: [String] = []
        BnNotifications.navigateHookForTest = { route in fired.append(route); return 0 }
        bridge.notifications.fireTapForTest(route: nil)
        bridge.notifications.fireTapForTest(route: "")
        XCTAssertTrue(fired.isEmpty, "a routeless tap acts on nothing")
        XCTAssertNil(bridge.notifications.pendingLaunchRouteForTest())
    }

    // ── Delegate retention pinned as a property (the CLLocationManager lesson) ────

    func testTheDelegateIsRetainedByTheBridge() {
        let bridge = AppleShellBridge()
        bridge.notifications.installDelegate()
        // UNUserNotificationCenter holds its delegate WEAKLY; the bridge holds the handler
        // and the handler IS the delegate — no observable sees it, so it is a property pin.
        XCTAssertTrue(bridge.notifications.delegateIsRetainedForTest(),
                      "the UNUserNotificationCenter delegate must be the bridge's retained handler")
    }

    // ── BOOT: the round trip through the REAL host_call_complete + host_event ─────

    func testShowGrantRoundTripsThroughTheRealHostCallComplete() throws {
        BnNotifications.authorizationStatusOverrideForTest = { .authorized }
        let form = try bootNotificationsDemo()

        XCTAssertEqual(echoLabel()?.text, "arrived:/notifications", "the demo mounts on its own route")
        try tapButton("Show", in: form)
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:Granted" },
                      "Show never round-tripped a Granted status to the echo (a hang or mis-route)")
        XCTAssertEqual(BnNotifications.lastHostCallCompleteRcForTest, 0,
                       "host_call_complete did not route to the in-flight .NET requestId")
    }

    func testShowDeniedIsDataWithinABoundedAwaitNoHang() throws {
        BnNotifications.authorizationStatusOverrideForTest = { .notDetermined }
        BnNotifications.suppressAuthorizationPromptForTest = true
        let form = try bootNotificationsDemo()
        let notif = try XCTUnwrap(runtime?.bridge.notifications)

        try tapButton("Show", in: form)
        XCTAssertTrue(pollUntil { notif.hasInFlightRequestForTest() }, "Show never reached the prompt path")
        notif.fireAuthorizationResultForTest(granted: false) // the user says no (the alert is owner-device territory)

        // The awaiting .NET ValueTask resolves to a DENIAL the echo shows — bounded await.
        // A HANG (denial thrown/dropped) times this poll out and reddens (the milestone law).
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:DeniedPermanently" },
                      "denial never reached the echo within the bounded await (a HANG — denial was not data)")
        XCTAssertEqual(BnNotifications.lastHostCallCompleteRcForTest, 0)
    }

    func testWarmTapReRoutesTheLiveSessionToTheNotificationsPage() throws {
        // Boot the DEFAULT demo so the warm re-route to /notifications is an observable
        // CHANGE, not a no-op (the Android EXTRA_COMPONENT="BnDemo" precedent).
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let rt = BnRuntime(mapper: mapper)
        rt.onError = { msg, err in NSLog("[BnNotificationsTests] \(msg): \(err)") }
        self.runtime = rt
        try rt.start(component: "BnDemo", os: "ios")
        XCTAssertTrue(pollUntil(deadline: 30) { self.root.subviews.first != nil }, "BnDemo never booted")
        XCTAssertNil(findLabel(in: root, text: "arrived:/notifications"),
                     "must start on BnDemo, not already on the /notifications page")

        // A warm tap (a live session exists → navigateDispatcher is wired) re-routes over
        // the REAL blazornative_host_event — the iOS shell's FIRST call of that export.
        let notif = try XCTUnwrap(runtime?.bridge.notifications)
        notif.fireTapForTest(route: "/notifications")

        XCTAssertTrue(pollUntil { self.findLabel(in: self.root, text: "arrived:/notifications") != nil },
                      "the warm re-route never mounted the /notifications page (host_event did not re-route)")
        XCTAssertEqual(BnNotifications.lastHostEventRcForTest, 0,
                       "host_event(navigate) did not reach a live .NET continuation (rc 0)")
    }

    // ── Boot + tree accessors (the BnGeolocationTests house style) ───────────────

    struct BootTimeout: Error {}

    private func bootNotificationsDemo() throws -> UIView {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let rt = BnRuntime(mapper: mapper)
        rt.onError = { msg, err in NSLog("[BnNotificationsTests] \(msg): \(err)") }
        self.runtime = rt
        try rt.start(component: "BnNotificationsDemo", os: "ios")
        guard pollUntil(deadline: 30, { self.probeForm() != nil }), let form = probeForm() else {
            XCTFail("BnNotificationsDemo never rendered its Show/Schedule/Cancel/echo tree within 30s")
            throw BootTimeout()
        }
        return form
    }

    /// The demo root div: root's single child with the 4 children (3 buttons + echo).
    private func probeForm() -> UIView? {
        guard let form = root.subviews.first, form.subviews.count >= 4 else { return nil }
        return form
    }

    /// The echo: the only UILabel that is a DIRECT child of the div.
    private func echoLabel() -> UILabel? {
        probeForm()?.subviews.first { $0 is UILabel } as? UILabel
    }

    /// A UILabel with the given text, anywhere in the tree (used across a re-route where
    /// the whole subtree is replaced).
    private func findLabel(in view: UIView, text: String) -> UILabel? {
        if let label = view as? UILabel, label.text == text { return label }
        for sub in view.subviews {
            if let f = findLabel(in: sub, text: text) { return f }
        }
        return nil
    }

    private func tapButton(_ title: String, in view: UIView,
                           file: StaticString = #filePath, line: UInt = #line) throws {
        let button = try XCTUnwrap(findButton(in: view, title: title),
                                   "button '\(title)' not on screen", file: file, line: line)
        button.sendActions(for: .touchUpInside)
    }

    private func findButton(in view: UIView, title: String) -> UIButton? {
        if let b = view as? UIButton, b.title(for: .normal) == title { return b }
        for sub in view.subviews {
            if let f = findButton(in: sub, title: title) { return f }
        }
        return nil
    }

    /// Purge the real centre so no cross-test pending/delivered request leaks.
    private func clearCentre() {
        let center = UNUserNotificationCenter.current()
        center.removeAllPendingNotificationRequests()
        center.removeAllDeliveredNotifications()
    }

    private func pollUntil(deadline seconds: TimeInterval = 10, _ cond: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if cond() { return true }
        }
        return cond()
    }
}
