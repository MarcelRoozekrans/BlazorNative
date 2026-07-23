// ─────────────────────────────────────────────────────────────────────────────
// BnGeolocationTests — Phase 9.0 Gate 3 (M9 DoD #1 + #2): geolocation + the
// permission pattern on the iOS simulator. The iOS third of BnGeolocationDemoTests.cs
// (.NET, DevHostBridge drives all six statuses headless) + BnGeolocationAndroidTest.kt
// (the AVD LocationManager flow) + the struct/export drift pins (BnDriftTests moved
// 72→80; ios.yml's nm gate asserts the 10th export blazornative_host_call_complete).
//
// TWO LAYERS, mirroring the Android suite:
//   • UNIT (no NativeAOT boot) — the tri-state MATRIX via BnGeolocation.completeHookForTest:
//     all six status values 0..5 (Granted/Denied/DeniedPermanently/Restricted/
//     LocationUnavailable/Error) reached as DATA, the fix's flat-JSON KEY NAMES pinned
//     against the .NET/Android contract (lat/lng/accuracy/altitude/timestamp), the
//     completion ROUTED to the right requestId, and the CLLocationManager+delegate
//     RETENTION pinned as a property.
//   • BOOT (real NativeAOT, /geolocation mounted) — the round trip through the REAL
//     blazornative_host_call_complete into a LIVE .NET continuation: a grant echoes the
//     fix and a denial echoes a status, both within a bounded await (rc 0 proves the
//     deferred completion routed to the process-scoped id — the iOS analogue of
//     Android's recreation-survival concern), NO HANG.
//
// SIMULATOR-SCOPED + LABELLED (the design's iOS honesty split): the real system
// authorization ALERT is owner-device territory (an Apple Developer account is the
// trigger, exactly as Android's real dialog is owner-phone territory), and a simulator
// "location" is a set coordinate, not a GPS fix. So the delegate is driven directly (the
// hand-rolled-fire pattern of 7.3/7.4 — synthesize the trigger, run the REAL delegate
// code); an authorization-status OVERRIDE + a SUPPRESS switch stand in for the un-drivable
// TCC state and system UI. The production paths are unsuppressed and untouched.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import CoreLocation
import UIKit
@testable import BnHost

final class BnGeolocationTests: BnHostTestCase {

    private static let requestArgs = "{\"mode\":\"request\"}"
    private static let checkArgs = "{\"mode\":\"check\"}"

    /// Amsterdam — distinctive coordinates that round-trip exactly through the flat-JSON
    /// wire (Swift Double description ↔ .NET InvariantCulture parse), matching the AVD test.
    private static let amsterdam = CLLocation(
        coordinate: CLLocationCoordinate2D(latitude: 52.3702, longitude: 4.8952),
        altitude: 3.0, horizontalAccuracy: 5.0, verticalAccuracy: 2.0,
        timestamp: Date(timeIntervalSince1970: 1_700_000_000))

    /// The completions the hook captured this test (id, status, payload), in order.
    private var captured: [(id: Int64, status: Int32, payload: String?)] = []
    private var runtime: BnRuntime?
    private var root: UIView!

    override func setUp() {
        super.setUp()
        BnGeolocation.resetForTest()
        captured = []
    }

    override func tearDown() {
        BnGeolocation.resetForTest()
        super.tearDown()
    }

    /// Capture completions instead of crossing into .NET (the unit lane).
    private func installCapture() {
        BnGeolocation.completeHookForTest = { [weak self] id, status, payload in
            self?.captured.append((id, status, payload))
            return 0
        }
    }

    // ── Check: each authorization status reported as DATA, never a prompt ─────────

    func testCheckReportsGrantedWhenAuthorized() {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .authorizedWhenInUse }
        _ = bridge.hostCallBegin(1, BnHostCallOp.geolocation, Self.checkArgs)
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.granted])
        XCTAssertNil(captured.first?.payload, "Check carries no fix payload")
    }

    func testCheckReportsDeniedWhenNotDetermined() {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .notDetermined }
        _ = bridge.hostCallBegin(2, BnHostCallOp.geolocation, Self.checkArgs)
        // Not held → Denied (a later request MAY prompt) — never a hang, never a prompt.
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.denied])
    }

    func testCheckReportsDeniedPermanentlyWhenDenied() {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .denied }
        _ = bridge.hostCallBegin(3, BnHostCallOp.geolocation, Self.checkArgs)
        // iOS .denied is one-shot — re-request will not prompt; only Settings changes it.
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.deniedPermanently])
    }

    func testCheckReportsRestrictedWhenRestricted() {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .restricted }
        _ = bridge.hostCallBegin(4, BnHostCallOp.geolocation, Self.checkArgs)
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.restricted])
    }

    // ── Request: a held fix → Granted with the EXACT contract key names ──────────

    func testRequestWhenHeldFetchesAFixAsGrantedWithTheContractKeys() throws {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .authorizedWhenInUse }
        BnGeolocation.suppressSystemLocationCallsForTest = true

        _ = bridge.hostCallBegin(10, BnHostCallOp.geolocation, Self.requestArgs)
        XCTAssertTrue(bridge.geolocation.hasInFlightRequestForTest(), "request 10 must be in flight before the fix")
        bridge.geolocation.fireLocationFixForTest(Self.amsterdam) // the hand-rolled delegate fix

        XCTAssertEqual(captured.count, 1)
        XCTAssertEqual(captured[0].id, 10, "the completion must route to the requesting id")
        XCTAssertEqual(captured[0].status, BnHostCallStatus.granted)

        let payload = try XCTUnwrap(captured[0].payload, "a Granted fix carries the flat-JSON payload")
        let fix = try XCTUnwrap(BnFlatJson.parseObject(payload), "the fix must be readable flat JSON")
        // THE CONTRACT: the exact key set NativeShellBridge.ParseGeolocationResult reads and
        // AndroidShellBridge.fixPayload writes — lat/lng/accuracy/altitude/timestamp.
        XCTAssertEqual(Set(fix.keys), ["lat", "lng", "accuracy", "altitude", "timestamp"],
                       "the fix key names must match the .NET/Android wire contract EXACTLY (lng, not lon)")
        XCTAssertEqual(Double(fix["lat"]!)!, 52.3702, accuracy: 1e-6)
        XCTAssertEqual(Double(fix["lng"]!)!, 4.8952, accuracy: 1e-6)
        XCTAssertEqual(Double(fix["accuracy"]!)!, 5.0, accuracy: 1e-6)
        XCTAssertEqual(Double(fix["altitude"]!)!, 3.0, accuracy: 1e-6)
        XCTAssertEqual(Int64(fix["timestamp"]!)!, 1_700_000_000_000, "timestamp is Unix ms")
    }

    // ── Request: notDetermined → the user's outcome arrives as DATA (never a throw) ──

    func testRequestNotDeterminedThenUserDenialIsDataNotAThrow() {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .notDetermined }
        BnGeolocation.suppressSystemLocationCallsForTest = true

        _ = bridge.hostCallBegin(20, BnHostCallOp.geolocation, Self.requestArgs)
        XCTAssertTrue(bridge.geolocation.hasInFlightRequestForTest(), "the request awaits the authorization outcome")
        bridge.geolocation.fireAuthorizationChangeForTest(.denied) // the user says no

        // Denial is a VALUE (2), delivered — not a Swift error across the boundary, not a hang.
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.deniedPermanently])
        XCTAssertEqual(captured.first?.id, 20)
        XCTAssertNil(captured.first?.payload)
        XCTAssertFalse(bridge.geolocation.hasInFlightRequestForTest(), "the slot is consumed by the outcome")
    }

    func testRequestNotDeterminedThenRestrictedIsData() {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .notDetermined }
        BnGeolocation.suppressSystemLocationCallsForTest = true

        _ = bridge.hostCallBegin(21, BnHostCallOp.geolocation, Self.requestArgs)
        bridge.geolocation.fireAuthorizationChangeForTest(.restricted)
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.restricted])
    }

    func testRequestNotDeterminedGrantThenFixRoundTripsToTheSameId() throws {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .notDetermined }
        BnGeolocation.suppressSystemLocationCallsForTest = true

        _ = bridge.hostCallBegin(22, BnHostCallOp.geolocation, Self.requestArgs)
        // The user GRANTS → the delegate resumes → the fix is fetched (suppressed here) →
        // the test fires it. The whole prompt→grant→fix flow keeps the SAME in-flight id.
        bridge.geolocation.fireAuthorizationChangeForTest(.authorizedWhenInUse)
        XCTAssertTrue(captured.isEmpty, "a grant is not itself a completion — the fix is")
        bridge.geolocation.fireLocationFixForTest(Self.amsterdam)

        XCTAssertEqual(captured.count, 1)
        XCTAssertEqual(captured[0].id, 22, "the fix must complete the SAME id the prompt was raised for")
        XCTAssertEqual(captured[0].status, BnHostCallStatus.granted)
    }

    // ── Request: a fix FAILURE maps to a status by CLError code ──────────────────

    func testFixFailureLocationUnknownMapsToLocationUnavailable() {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .authorizedWhenInUse }
        BnGeolocation.suppressSystemLocationCallsForTest = true

        _ = bridge.hostCallBegin(30, BnHostCallOp.geolocation, Self.requestArgs)
        bridge.geolocation.fireLocationFailureForTest(
            NSError(domain: kCLErrorDomain, code: CLError.Code.locationUnknown.rawValue))
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.locationUnavailable])
        XCTAssertNil(captured.first?.payload)
    }

    func testFixFailureDeniedMapsToDeniedPermanently() {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .authorizedWhenInUse }
        BnGeolocation.suppressSystemLocationCallsForTest = true

        _ = bridge.hostCallBegin(31, BnHostCallOp.geolocation, Self.requestArgs)
        bridge.geolocation.fireLocationFailureForTest(
            NSError(domain: kCLErrorDomain, code: CLError.Code.denied.rawValue))
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.deniedPermanently])
    }

    // ── Unknown op: DATA (Error), never a crash — the generic dispatch's safety net ──

    func testUnknownOpCompletesErrorNeverCrashes() {
        installCapture()
        let bridge = AppleShellBridge()
        let rc = bridge.hostCallBegin(40, 99 /* not a wired op */, "{}")
        XCTAssertEqual(rc, 0, "begin returns synchronously even for an unknown op")
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.error])
    }

    // ── Delegate retention pinned as a property (the 5.3/7.3 lesson, for CoreLocation) ──

    func testTheManagerAndDelegateAreRetainedForTheRequestDuration() {
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .authorizedWhenInUse }
        BnGeolocation.suppressSystemLocationCallsForTest = true

        _ = bridge.hostCallBegin(50, BnHostCallOp.geolocation, Self.requestArgs)
        // The bridge holds the handler; the handler holds the CLLocationManager whose
        // delegate is the handler (weak on the manager's side). No observable can see the
        // retention, so it is pinned as a property (the 6.2 contentInsetAdjustment precedent).
        XCTAssertTrue(bridge.geolocation.managerDelegateIsRetainedForTest(),
                      "the CLLocationManager must exist with this handler as its delegate")
    }

    // ── Routing / double-completion guard: a late duplicate callback no-ops ──────

    func testALateDuplicateCallbackAfterCompletionNoOps() {
        installCapture()
        let bridge = AppleShellBridge()
        BnGeolocation.authorizationStatusOverrideForTest = { .authorizedWhenInUse }
        BnGeolocation.suppressSystemLocationCallsForTest = true

        _ = bridge.hostCallBegin(60, BnHostCallOp.geolocation, Self.requestArgs)
        bridge.geolocation.fireLocationFixForTest(Self.amsterdam)   // completes id 60
        bridge.geolocation.fireLocationFixForTest(Self.amsterdam)   // late duplicate — slot cleared
        bridge.geolocation.fireLocationFailureForTest(
            NSError(domain: kCLErrorDomain, code: CLError.Code.locationUnknown.rawValue))

        XCTAssertEqual(captured.count, 1, "exactly ONE completion per request; the slot is one-shot")
        XCTAssertEqual(captured[0].id, 60)
    }

    // ── BOOT: the round trip through the REAL blazornative_host_call_complete ─────

    func testLocateWithGrantRoundTripsAFixThroughTheRealHostCallComplete() throws {
        BnGeolocation.authorizationStatusOverrideForTest = { .authorizedWhenInUse }
        BnGeolocation.suppressSystemLocationCallsForTest = true
        let form = try bootGeolocationDemo()
        let geo = try XCTUnwrap(runtime?.bridge.geolocation)

        try tapButton("Locate", in: form)
        XCTAssertTrue(pollUntil { geo.hasInFlightRequestForTest() },
                      "Locate never reached the host-call begin (no in-flight request)")
        geo.fireLocationFixForTest(Self.amsterdam) // the hand-rolled delegate fix

        XCTAssertTrue(pollUntil { self.echoLabel()?.text?.hasPrefix("fix:") == true },
                      "a Granted fix never round-tripped to the echo (a hang or a mis-routed completion)")
        let echo = try XCTUnwrap(echoLabel()?.text)
        let coords = echo.replacingOccurrences(of: "fix:", with: "").split(separator: ",")
        XCTAssertEqual(coords.count, 2, "echo must carry lat,lng: '\(echo)'")
        XCTAssertEqual(Double(String(coords[0]))!, 52.3702, accuracy: 1e-3)
        XCTAssertEqual(Double(String(coords[1]))!, 4.8952, accuracy: 1e-3)

        // Issue #169: accuracy surfaces on its OWN trailing UILabel ("acc:<metres>") — the
        // echo shape above is UNCHANGED (still exactly lat,lng). This proves the
        // round-tripped Accuracy is observable on the device (M11 DoD #2 reads the app's own
        // value). The Amsterdam fixture's horizontalAccuracy is 5.0.
        let accuracyText = try XCTUnwrap(accuracyLabel()?.text)
        XCTAssertTrue(accuracyText.hasPrefix("acc:"), "accuracy line must carry 'acc:<metres>': '\(accuracyText)'")
        XCTAssertEqual(Double(accuracyText.replacingOccurrences(of: "acc:", with: ""))!, 5.0, accuracy: 1e-3)
        // The completion reached a LIVE .NET continuation (process-scoped registry) → rc 0.
        XCTAssertEqual(BnGeolocation.lastHostCallCompleteRcForTest, 0,
                       "host_call_complete did not route to the in-flight .NET requestId")
    }

    func testLocateDeniedIsDataWithinABoundedAwaitNoHang() throws {
        BnGeolocation.authorizationStatusOverrideForTest = { .notDetermined }
        BnGeolocation.suppressSystemLocationCallsForTest = true
        let form = try bootGeolocationDemo()
        let geo = try XCTUnwrap(runtime?.bridge.geolocation)

        try tapButton("Locate", in: form)
        XCTAssertTrue(pollUntil { geo.hasInFlightRequestForTest() },
                      "Locate never reached the prompt path")
        geo.fireAuthorizationChangeForTest(.denied) // the user says no (the real alert is owner-device territory)

        // The awaiting .NET ValueTask resolves to a DENIAL the echo shows — within a bounded
        // await. A HANG (denial thrown/dropped) times this poll out and reddens (the milestone law).
        XCTAssertTrue(pollUntil { self.echoLabel()?.text?.hasPrefix("status:DeniedPermanently") == true },
                      "denial never reached the echo within the bounded await (a HANG — denial was not data)")
        XCTAssertEqual(BnGeolocation.lastHostCallCompleteRcForTest, 0,
                       "the denial completion did not route to the in-flight .NET requestId")
    }

    // ── Boot + tree accessors (the BnClipboardTests house style) ─────────────────

    struct BootTimeout: Error {}

    private func bootGeolocationDemo() throws -> UIView {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let rt = BnRuntime(mapper: mapper)
        rt.onError = { msg, err in NSLog("[BnGeolocationTests] \(msg): \(err)") }
        self.runtime = rt
        try rt.start(component: "BnGeolocationDemo", os: "ios")
        guard pollUntil(deadline: 30, { self.probeForm() != nil }), let form = probeForm() else {
            XCTFail("BnGeolocationDemo never rendered its Locate/Check/echo tree within 30s")
            throw BootTimeout()
        }
        return form
    }

    /// The demo root div: root's single child (a plain UIView) with the 4 children
    /// (Locate/Check buttons + echo label + trailing accuracy label, issue #169).
    private func probeForm() -> UIView? {
        guard let form = root.subviews.first, form.subviews.count >= 3 else { return nil }
        return form
    }

    /// The echo: the FIRST UILabel that is a DIRECT child of the div (a UIButton's internal
    /// titleLabel is a subview of the BUTTON, never of the div). Issue #169 added a trailing
    /// accuracy UILabel, so this is now "first" rather than "only" — the echo is unchanged.
    private func echoLabel() -> UILabel? {
        probeForm()?.subviews.first { $0 is UILabel } as? UILabel
    }

    /// The accuracy line (issue #169): the SECOND direct-child UILabel of the div — the
    /// trailing "acc:<metres>" node placed after the echo.
    private func accuracyLabel() -> UILabel? {
        probeForm()?.subviews.compactMap { $0 as? UILabel }.dropFirst().first
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

    private func pollUntil(deadline seconds: TimeInterval = 10, _ cond: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if cond() { return true }
        }
        return cond()
    }
}
