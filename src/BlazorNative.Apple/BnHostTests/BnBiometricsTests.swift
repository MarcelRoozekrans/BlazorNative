// ─────────────────────────────────────────────────────────────────────────────
// BnBiometricsTests — Phase 9.2 Gate 3 (M9 DoD #4): biometric authentication on the
// iOS simulator. The iOS third of BnSecureDemoTests.cs (.NET, DevHostBridge drives
// every status headless) + the AVD BnSecureAndroidTest.kt (BiometricPrompt). The
// bridge/struct/export pins are UNCHANGED — biometrics add op=2 + a wire status enum +
// a handler, and touch the ABI at nothing else (BnDriftTests still asserts 80 bytes /
// offset 72 / 10 exports, UNMOVED).
//
// TWO LAYERS (the geolocation/notifications house style):
//   • UNIT (no NativeAOT boot) — the authenticate MATRIX via the evaluatePolicy REPLY
//     seam (Authenticated / Failed / Cancelled / Unavailable / LockedOut / Error, all
//     DATA — never a Swift error across the C boundary, never a hang); the read-only
//     `check` via the canEvaluatePolicy override; the op routing; the wire enum pins;
//     the LAContext RETENTION during a suspended evaluation.
//   • BOOT (real NativeAOT, /secure mounted) — the round trip through the REAL
//     blazornative_host_call_complete: an Authenticate tap echoes "status:Authenticated"
//     on a seam-driven success, and a denial echoes "status:Cancelled" within a BOUNDED
//     await (no hang), both rc 0.
//
// SIMULATOR-SCOPED + LABELLED (the M9 iOS deferral): evaluatePolicy's Face/Touch ID
// system sheet is owner-device territory (iOS real-device is DEFERRED). The
// evaluatePolicy REPLY override + the canEvaluatePolicy override stand in; the real
// LAContext calls are untouched.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import LocalAuthentication
import UIKit
@testable import BnHost

final class BnBiometricsTests: BnHostTestCase {

    private let capturedLock = NSLock()
    private var captured: [(id: Int64, status: Int32)] = []
    private var runtime: BnRuntime?
    private var root: UIView!

    override func setUp() {
        super.setUp()
        BnBiometrics.resetForTest()
        captured = []
    }

    override func tearDown() {
        BnBiometrics.resetForTest()
        super.tearDown()
    }

    private func installCapture() {
        BnBiometrics.completeHookForTest = { [weak self] id, status, _ in
            guard let self = self else { return 0 }
            self.capturedLock.lock(); self.captured.append((id, status)); self.capturedLock.unlock()
            return 0
        }
    }

    private func capturedStatuses() -> [Int32] {
        capturedLock.lock(); defer { capturedLock.unlock() }; return captured.map { $0.status }
    }

    // ── check: canEvaluatePolicy reported as DATA, never a prompt ─────────────────

    func testCheckReportsAuthenticatedWhenAvailable() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.canEvaluatePolicyOverrideForTest = { (true, nil) }
        _ = bridge.hostCallBegin(1, BnHostCallOp.biometrics, "{\"action\":\"check\"}")
        // Available → Authenticated ("present + enrolled + ready", the Android
        // canAuthenticateStatus SUCCESS→AUTHENTICATED twin).
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.authenticated])
    }

    func testCheckReportsUnavailableWhenNotEnrolled() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.canEvaluatePolicyOverrideForTest = { (false, LAError(.biometryNotEnrolled)) }
        _ = bridge.hostCallBegin(2, BnHostCallOp.biometrics, "{\"action\":\"check\"}")
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.unavailable])
    }

    // ── authenticate MATRIX: each LAError outcome as a status VALUE (no hang) ──────

    func testAuthenticateSuccessIsAuthenticated() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(true, nil) }
        _ = bridge.hostCallBegin(10, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"Prove it's you\"}")
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.authenticated])
    }

    func testAuthenticateRejectedIsFailed() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.authenticationFailed)) }
        _ = bridge.hostCallBegin(11, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        // A biometric was presented and rejected — Failed (retry allowed), never a hang.
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.failed])
    }

    func testAuthenticateUserCancelIsCancelled() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.userCancel)) }
        _ = bridge.hostCallBegin(12, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.cancelled])
    }

    func testAuthenticateSystemCancelIsCancelled() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.systemCancel)) }
        _ = bridge.hostCallBegin(13, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.cancelled])
    }

    func testAuthenticateLockoutIsLockedOut() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.biometryLockout)) }
        _ = bridge.hostCallBegin(14, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.lockedOut])
    }

    func testAuthenticateNotAvailableIsUnavailable() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.biometryNotAvailable)) }
        _ = bridge.hostCallBegin(15, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.unavailable])
    }

    func testAuthenticateUnknownErrorIsError() {
        installCapture()
        let bridge = AppleShellBridge()
        // An out-of-set LAError code (and, by the same funnel, a nil/foreign error) maps
        // to Error — still DATA, never a throw (the .NET ToBiometricStatus twin).
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.invalidContext)) }
        _ = bridge.hostCallBegin(16, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.error])
    }

    // ── Error (5) is DATA: an unknown action never crashes ────────────────────────

    func testUnknownActionCompletesErrorAsData() {
        installCapture()
        let bridge = AppleShellBridge()
        let rc = bridge.hostCallBegin(20, BnHostCallOp.biometrics, "{\"action\":\"frobnicate\"}")
        XCTAssertEqual(rc, 0, "begin returns synchronously even for an unknown action")
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.error])
    }

    // ── The op routes to the biometrics handler (op=2, not geolocation/notifications) ─

    func testHostCallBeginRoutesTheBiometricsOpToTheHandler() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.canEvaluatePolicyOverrideForTest = { (true, nil) }
        // op=2 must reach BnBiometrics (a Check → Authenticated), NOT geolocation(0)/notifications(1).
        _ = bridge.hostCallBegin(21, BnHostCallOp.biometrics, "{\"action\":\"check\"}")
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.authenticated])
    }

    // ── The wire status enum + op value — the three-way mirror pinned ─────────────

    func testTheWireStatusConstantsMatchTheThreeWayContract() {
        // The EXACT integers .NET BiometricStatus / Kotlin BiometricStatus carry (SIX).
        XCTAssertEqual(BnBiometricStatus.authenticated, 0)
        XCTAssertEqual(BnBiometricStatus.failed, 1)
        XCTAssertEqual(BnBiometricStatus.cancelled, 2)
        XCTAssertEqual(BnBiometricStatus.unavailable, 3)
        XCTAssertEqual(BnBiometricStatus.lockedOut, 4)
        XCTAssertEqual(BnBiometricStatus.error, 5)
        // The op value — wire vocabulary on the existing int op field (no struct grow).
        XCTAssertEqual(BnHostCallOp.biometrics, 2)
    }

    // ── LAContext RETENTION across a suspended evaluation (the CLLocationManager lesson) ─

    func testTheLAContextIsRetainedDuringEvaluationThenReleased() {
        installCapture()
        let bridge = AppleShellBridge()
        // Capture the reply WITHOUT calling it — the evaluation is now "in flight".
        var pendingReply: ((Bool, Error?) -> Void)?
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in pendingReply = reply }
        _ = bridge.hostCallBegin(30, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")

        XCTAssertTrue(bridge.biometrics.hasInFlightRequestForTest(), "the request awaits the evaluation")
        XCTAssertTrue(bridge.biometrics.contextIsRetainedForTest(),
                      "the LAContext must be held for the call's duration (a deallocated context cancels the eval)")
        XCTAssertTrue(capturedStatuses().isEmpty, "no completion before the reply")

        pendingReply?(true, nil) // the OS (here the seam) resolves the evaluation
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.authenticated])
        XCTAssertFalse(bridge.biometrics.contextIsRetainedForTest(), "the context is released on completion")
        XCTAssertFalse(bridge.biometrics.hasInFlightRequestForTest())
    }

    // ── BOOT: the round trip through the REAL host_call_complete (the /secure demo) ─

    func testAuthenticateBootRoundTripsAuthenticatedThroughTheRealHostCallComplete() throws {
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(true, nil) }
        let form = try bootSecureDemo()

        try tapButton("Authenticate", in: form)
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:Authenticated" },
                      "Authenticate never round-tripped Authenticated to the echo (a hang or mis-route)")
        XCTAssertEqual(BnBiometrics.lastHostCallCompleteRcForTest, 0,
                       "host_call_complete did not route to the in-flight .NET requestId")
    }

    func testAuthenticateBootDeniedIsDataWithinABoundedAwaitNoHang() throws {
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.userCancel)) }
        let form = try bootSecureDemo()

        try tapButton("Authenticate", in: form)
        // The awaiting .NET ValueTask resolves to a CANCELLED the echo shows — bounded
        // await. A HANG (denial thrown/dropped) times this poll out and reddens.
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:Cancelled" },
                      "a cancelled auth never reached the echo within the bounded await (a HANG — denial was not data)")
        XCTAssertEqual(BnBiometrics.lastHostCallCompleteRcForTest, 0)
    }

    // ── Boot + tree accessors (the BnNotificationsTests house style) ──────────────

    struct BootTimeout: Error {}

    private func bootSecureDemo() throws -> UIView {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let rt = BnRuntime(mapper: mapper)
        rt.onError = { msg, err in NSLog("[BnBiometricsTests] \(msg): \(err)") }
        self.runtime = rt
        try rt.start(component: "BnSecureDemo", os: "ios")
        guard pollUntil(deadline: 30, { self.probeForm() != nil }), let form = probeForm() else {
            XCTFail("BnSecureDemo never rendered its Authenticate/Set/Unlock/Delete/echo tree within 30s")
            throw BootTimeout()
        }
        return form
    }

    /// The demo root div: root's single child with 5 children (4 buttons + echo).
    private func probeForm() -> UIView? {
        guard let form = root.subviews.first, form.subviews.count >= 5 else { return nil }
        return form
    }

    private func echoLabel() -> UILabel? {
        probeForm()?.subviews.first { $0 is UILabel } as? UILabel
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
