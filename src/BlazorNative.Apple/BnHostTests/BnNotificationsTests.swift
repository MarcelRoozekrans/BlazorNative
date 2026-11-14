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
    /// The held-presentation pin's delegate, kept so tearDown can release its hold.
    private var holdingDelegate: HoldingPresentationDelegate?

    override func setUp() {
        super.setUp()
        BnNotifications.resetForTest()
        clearCentre()
        captured = []
    }

    override func tearDown() {
        BnNotifications.resetForTest()
        clearCentre()
        // 18.10: a held willPresent never released would stall every later show. The
        // held pin releases it in its own defer too; this covers a test that threw first.
        holdingDelegate?.release()
        holdingDelegate = nil
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
    }

    // ── show: an IMMEDIATE request carrying the route in userInfo (Granted) ───────

    func testShowBuildsAnImmediateRequestCarryingTheRouteInUserInfo() throws {
        installCapture()
        let bridge = AppleShellBridge()
        BnNotifications.authorizationStatusOverrideForTest = { .authorized }
        // #444: an id no earlier test used, so the show never replaces a request the
        // previous test added and setUp is still removing.
        let id = Self.uniqueNotificationId()
        _ = bridge.hostCallBegin(
            10, BnHostCallOp.notifications,
            "{\"action\":\"show\",\"id\":\"\(id)\",\"title\":\"Hello\",\"body\":\"A local notification\",\"route\":\"/notifications\"}")

        let completed = pollUntil { self.capturedStatuses() == [BnNotificationStatus.granted] }
        let diagnosis = diagnose("unit show", observed: "captured statuses \(capturedStatuses())")
        XCTAssertTrue(completed,
                      "show never completed Granted (a hang, or the real center's add never returned). \(diagnosis)")
        let req = try XCTUnwrap(BnNotifications.lastAddedRequestForTest, "show must hand a request to the center")
        XCTAssertEqual(req.identifier, id, "the id is the app-chosen int, stringified")
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

    // ── 18.10 (#444): a show completes Granted once it is posted ─────────────────
    //
    // Owner decision 2026-11-14: a show's Granted means "handed to the center", as
    // Android's does after notify. An immediate request's add completes only after the
    // app has answered willPresent, which SpringBoard once delivered 11.25 s late
    // (run 40642166828), so a show that waits on add waits on SpringBoard. These pins
    // hold that exchange, or replace the add, and require Granted anyway. schedule is
    // unchanged: a scheduled request is not presented, and its add outcome stays data.

    // Android twin: BnNotificationsAndroidTest.show_posts_a_real_notification_and_cancel_removes_it.
    // Shared contract: show completes Granted once posted.
    func testShowCompletesGrantedWhilePresentationIsHeld() {
        installCapture()
        BnNotifications.authorizationStatusOverrideForTest = { .authorized }
        let delegate = HoldingPresentationDelegate()
        holdingDelegate = delegate
        UNUserNotificationCenter.current().delegate = delegate
        defer {
            delegate.release()
            UNUserNotificationCenter.current().delegate = nil
        }
        let bridge = AppleShellBridge()
        let id = Self.uniqueNotificationId()
        _ = bridge.hostCallBegin(
            30, BnHostCallOp.notifications,
            "{\"action\":\"show\",\"id\":\"\(id)\",\"title\":\"Held\",\"body\":\"A held presentation\"}")

        XCTAssertTrue(pollUntil(deadline: 2) { self.capturedStatuses() == [BnNotificationStatus.granted] },
                      "show did not complete Granted within 2 s while its presentation was held: it still "
                        + "waits on add (#444), which waits on willPresent. Captured \(capturedStatuses()); adds ["
                        + BnNotifications.addTracesForTest().map(\.summary).joined(separator: "; ") + "]")

        // The hold anchor. The 30 s bound absorbs a SpringBoard stall before delivery,
        // like #444's 11.25 s; the pin needs delivery to have happened, not to be fast.
        XCTAssertTrue(pollUntil(deadline: 30) { delegate.receivedIdentifier == id },
                      "the test delegate never received willPresent for \(id): the hold did not engage, "
                        + "so this pin proves nothing")
        let traceForId = { BnNotifications.addTracesForTest().last { $0.identifier == id } }
        XCTAssertNotNil(traceForId(), "no add trace for \(id): the show never reached the add")
        XCTAssertNil(traceForId()?.completedAt,
                     "add completed while willPresent was held: the hold is not what add waits on. "
                        + "willPresent at \(delegate.receivedAt.map { BnNotifications.traceClock($0) } ?? "never"); "
                        + (traceForId()?.summary ?? "no trace"))

        // The control: the hold was real, because releasing it lets add complete.
        delegate.release()
        XCTAssertTrue(pollUntil(deadline: 10) { traceForId()?.completedAt != nil },
                      "add never completed after the hold was released. " + (traceForId()?.summary ?? "no trace"))
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.granted], "show completed more than once")
    }

    // The late error has two routes, as AppleShellBridge.deliverFaultNotice's does: the
    // booted runtime's onError, which is the route in a running app, and BnLog.error when
    // no runtime is booted. One pin per route, sharing showThenFailItsAddLate.

    func testALateShowAddErrorIsLoggedNotReturned() {
        let logged = LoggedLines()
        BnLog.emitHookForTest = { level, category, message in logged.append(level, category, message) }
        // The no-runtime route. A runtime an earlier boot test left as BnRuntime.shared
        // would take the error to its onError instead, so the test holds it nil.
        let savedShared = BnRuntime.shared
        BnRuntime.shared = nil
        defer {
            BnLog.emitHookForTest = nil
            BnRuntime.shared = savedShared
            BnNotifications.addForTest = nil
        }
        // Errors pass at every threshold BnLog accepts (error is the most severe level),
        // so the level is read here, not changed.
        XCTAssertTrue(BnLog.isEnabled(BnLogLevel.error), "BnLog's level drops errors, so this pin could not see one")
        guard let id = showThenFailItsAddLate(requestId: 31) else { return }

        XCTAssertTrue(pollUntil(deadline: 2) {
            logged.lines.contains { $0.level == BnLogLevel.error && $0.message.contains(id)
                && $0.message.contains("held for 18.10") }
        }, "a late add error was not logged. Error lines: \(logged.lines.filter { $0.level == BnLogLevel.error })")
        assertTheShowCompletedOnlyGranted()
    }

    func testALateShowAddErrorReachesTheRuntimesOnError() {
        // The running-app route: a never-booted runtime whose onError captures, installed
        // as the one the shell routes to (the BnBackOffMainTests pattern). Nothing here
        // crosses into .NET.
        let errors = CapturedErrors()
        let rt = BnRuntime(mapper: bnMapper(root: UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))))
        rt.onError = { msg, err in errors.append(msg, err) }
        let savedShared = BnRuntime.shared
        BnRuntime.shared = rt
        defer {
            BnRuntime.shared = savedShared
            BnNotifications.addForTest = nil
        }
        guard let id = showThenFailItsAddLate(requestId: 33) else { return }

        XCTAssertTrue(pollUntil(deadline: 2) {
            errors.items.contains { item in
                let described = "\(item.error) \((item.error as NSError).localizedDescription)"
                return item.message.contains(id)
                    && (item.message.contains("held for 18.10") || described.contains("held for 18.10"))
            }
        }, "a late add error did not reach the runtime's onError. onError saw: \(errors.items.map { $0.message })")
        assertTheShowCompletedOnlyGranted()
    }

    func testScheduleStillCompletesWithItsAddOutcome() {
        installCapture()
        BnNotifications.authorizationStatusOverrideForTest = { .authorized }
        let add = StashedAdd()
        BnNotifications.addForTest = { request, completion in add.stash(request, completion) }
        defer { BnNotifications.addForTest = nil }
        let bridge = AppleShellBridge()
        let id = Self.uniqueNotificationId()
        let whenMs = Int64((Date().timeIntervalSince1970 + 60) * 1000)
        _ = bridge.hostCallBegin(
            32, BnHostCallOp.notifications,
            "{\"action\":\"schedule\",\"id\":\"\(id)\",\"title\":\"Later\",\"body\":\"A scheduled add\","
                + "\"when\":\"\(whenMs)\"}")

        guard pollUntil(deadline: 2, { add.ran }) else {
            XCTFail("addForTest never ran: the schedule did not reach the add"); return
        }
        XCTAssertFalse(pollUntil(deadline: 1) { !self.capturedStatuses().isEmpty },
                       "schedule completed before its add did: only show completes on hand-off "
                        + "(owner decision 18.10). Captured \(capturedStatuses())")

        add.complete(NSError(domain: "UNErrorDomain", code: 1,
                             userInfo: [NSLocalizedDescriptionKey: "schedule add failed for 18.10"]))
        XCTAssertTrue(pollUntil(deadline: 2) { self.capturedStatuses() == [BnNotificationStatus.error] },
                      "schedule did not complete Error with its add's error. Captured \(capturedStatuses())")
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
        BnNotifications.navigateHookForTest = { route in fired.append(route) }

        bridge.notifications.fireTapForTest(route: "/notifications")
        // Since 16.2 (#346) the dispatch is fire-and-forget and returns no rc, so the route
        // the hook saw is the whole observation; lastHostEventRcForTest was retired with it.
        XCTAssertEqual(fired, ["/notifications"], "a warm tap fires host_event(navigate, route)")
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
        BnNotifications.navigateHookForTest = { route in fired.append(route) }
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
        let completed = pollUntil { self.echoLabel()?.text == "status:Granted" }
        let diagnosis = diagnose("boot show",
                                 observed: "echo \(echoLabel()?.text ?? "nil"), complete rc \(BnNotifications.lastHostCallCompleteRcForTest)")
        XCTAssertTrue(completed,
                      "Show never round-tripped a Granted status to the echo (a hang or mis-route). \(diagnosis)")
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
        // No rc assertion since 16.2 (#346): the navigator is fire-and-forget and returns
        // none. The mounted page above is the proof that host_event reached .NET and
        // re-routed, which is stronger than the rc 0 it replaced.
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

    // ── #444 diagnostics: a show's add, timed ────────────────────────────────────
    //
    // Since 18.10 a show completes Granted once its request is handed to the center, so
    // nothing here waits on the center's add; the held-presentation pin
    // (testShowCompletesGrantedWhilePresentationIsHeld) is the proof. The add trace
    // stays as history and as a diagnosis aid: it says whether the center ever called
    // back, with what error, and whether the handler was alive to route it. A passing
    // show logs the same line, so the latency is on record.
    //
    // What the add waits on, kept as history: lane run 40624968569 showed that the
    // center completes an IMMEDIATE request's add only after the app has answered
    // `willPresent`, which it delivers on the main thread, and that it waited at least
    // 15 s for an answer a delegate held. Both shows here run with no delegate, because
    // tearDown clears it and the test boot does not install one, so the framework
    // answers `willPresent` itself. The simulator log the lane collects on failure
    // shows that exchange: SpringBoard's "Send willPresentNotification", "delivery
    // succeeded" and "Received response", then the app's "Added notification request".

    private static var notificationIdSeq = 0

    /// A notification id no other test in this process has used: the app-chosen int,
    /// stringified, as the shell expects.
    static func uniqueNotificationId() -> String {
        notificationIdSeq += 1
        return String(444_000 + notificationIdSeq)
    }

    /// The add traces, as one line, also logged so a passing run's latency is on record.
    private func diagnose(_ what: String, observed: String) -> String {
        let traces = BnNotifications.addTracesForTest().map(\.summary)
        let line = "[#444] \(what): \(observed); adds [\(traces.joined(separator: "; "))]"
        NSLog("%@", line)
        return line
    }

    // ── 18.10 helpers: a late add error, a held willPresent, captured sinks ─────

    /// The late-error pins' shared half: a show whose add is stashed by `addForTest`
    /// completes Granted, and only then is the stashed add completed with an error
    /// carrying "held for 18.10". Returns the notification id, or nil after recording
    /// the failure at the caller's line, since an error is not late without a Granted.
    private func showThenFailItsAddLate(requestId: Int64,
                                        file: StaticString = #filePath, line: UInt = #line) -> String? {
        installCapture()
        BnNotifications.authorizationStatusOverrideForTest = { .authorized }
        let add = StashedAdd()
        BnNotifications.addForTest = { request, completion in add.stash(request, completion) }
        let bridge = AppleShellBridge()
        let id = Self.uniqueNotificationId()
        _ = bridge.hostCallBegin(
            requestId, BnHostCallOp.notifications,
            "{\"action\":\"show\",\"id\":\"\(id)\",\"title\":\"Late\",\"body\":\"A late add error\"}")

        guard pollUntil(deadline: 2, { add.ran }) else {
            XCTFail("addForTest never ran: the show did not reach the add", file: file, line: line)
            return nil
        }
        guard pollUntil(deadline: 2, { self.capturedStatuses() == [BnNotificationStatus.granted] }) else {
            XCTFail("show did not complete Granted within 2 s while its add was uncompleted: it still waits on "
                        + "add (#444). Captured \(capturedStatuses())", file: file, line: line)
            return nil
        }
        add.complete(NSError(domain: "UNErrorDomain", code: 1,
                             userInfo: [NSLocalizedDescriptionKey: "held for 18.10"]))
        return id
    }

    /// The show completed once, Granted, with room for a wrong second completion to land.
    private func assertTheShowCompletedOnlyGranted(file: StaticString = #filePath, line: UInt = #line) {
        _ = pollUntil(deadline: 0.5) { false }
        XCTAssertEqual(capturedStatuses(), [BnNotificationStatus.granted],
                       "a late add error was returned as a second completion", file: file, line: line)
    }

    /// What a runtime's onError received, from whatever thread reported it.
    private final class CapturedErrors {
        private let lock = NSLock()
        private var storage: [(message: String, error: Error)] = []

        var items: [(message: String, error: Error)] {
            lock.lock(); defer { lock.unlock() }; return storage
        }

        func append(_ message: String, _ error: Error) {
            lock.lock(); storage.append((message, error)); lock.unlock()
        }
    }

    /// A center delegate that records willPresent and holds its completion handler, so
    /// the presentation exchange an immediate add waits on stays open until `release`.
    /// The technique of lane run 40624968569, which held one for 15 s. willPresent
    /// arrives on the main thread, which `pollUntil` keeps running.
    private final class HoldingPresentationDelegate: NSObject, UNUserNotificationCenterDelegate {
        private let lock = NSLock()
        private var identifier: String?
        private var at: Date?
        private var held: ((UNNotificationPresentationOptions) -> Void)?

        var receivedIdentifier: String? { lock.lock(); defer { lock.unlock() }; return identifier }
        var receivedAt: Date? { lock.lock(); defer { lock.unlock() }; return at }

        func userNotificationCenter(_ center: UNUserNotificationCenter,
                                    willPresent notification: UNNotification,
                                    withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
            lock.lock()
            identifier = notification.request.identifier
            at = Date()
            held = completionHandler
            lock.unlock()
        }

        /// Answers a held willPresent, once. Safe to call again, or with nothing held.
        func release() {
            lock.lock(); let handler = held; held = nil; lock.unlock()
            handler?([.banner])
        }
    }

    /// What `addForTest` was handed: it records that it ran and keeps the completion
    /// uncalled until the test calls `complete`.
    private final class StashedAdd {
        private let lock = NSLock()
        private var completion: ((Error?) -> Void)?
        private var didRun = false

        var ran: Bool { lock.lock(); defer { lock.unlock() }; return didRun }

        func stash(_ request: UNNotificationRequest, _ completion: @escaping (Error?) -> Void) {
            lock.lock(); didRun = true; self.completion = completion; lock.unlock()
        }

        func complete(_ error: Error?) {
            lock.lock(); let c = completion; completion = nil; lock.unlock()
            c?(error)
        }
    }

    /// The lines BnLog let through, from whatever thread logged them.
    private final class LoggedLines {
        private let lock = NSLock()
        private var storage: [(level: Int32, category: String, message: String)] = []

        var lines: [(level: Int32, category: String, message: String)] {
            lock.lock(); defer { lock.unlock() }; return storage
        }

        func append(_ level: Int32, _ category: String, _ message: String) {
            lock.lock(); storage.append((level, category, message)); lock.unlock()
        }
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
