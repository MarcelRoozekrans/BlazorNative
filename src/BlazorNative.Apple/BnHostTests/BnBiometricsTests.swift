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
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
        // Available → Authenticated ("present + enrolled + ready", the Android
        // canAuthenticateStatus SUCCESS→AUTHENTICATED twin).
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.authenticated])
    }

    func testCheckReportsUnavailableWhenNotEnrolled() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.canEvaluatePolicyOverrideForTest = { (false, LAError(.biometryNotEnrolled)) }
        _ = bridge.hostCallBegin(2, BnHostCallOp.biometrics, "{\"action\":\"check\"}")
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.unavailable])
    }

    // ── authenticate MATRIX: each LAError outcome as a status VALUE (no hang) ──────

    func testAuthenticateSuccessIsAuthenticated() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(true, nil) }
        _ = bridge.hostCallBegin(10, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"Prove it's you\"}")
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.authenticated])
    }

    func testAuthenticateRejectedIsFailed() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.authenticationFailed)) }
        _ = bridge.hostCallBegin(11, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
        // A biometric was presented and rejected — Failed (retry allowed), never a hang.
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.failed])
    }

    func testAuthenticateUserCancelIsCancelled() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.userCancel)) }
        _ = bridge.hostCallBegin(12, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.cancelled])
    }

    func testAuthenticateSystemCancelIsCancelled() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.systemCancel)) }
        _ = bridge.hostCallBegin(13, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.cancelled])
    }

    func testAuthenticateLockoutIsLockedOut() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.biometryLockout)) }
        _ = bridge.hostCallBegin(14, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.lockedOut])
    }

    func testAuthenticateNotAvailableIsUnavailable() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.biometryNotAvailable)) }
        _ = bridge.hostCallBegin(15, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.unavailable])
    }

    func testAuthenticateUnknownErrorIsError() {
        installCapture()
        let bridge = AppleShellBridge()
        // An out-of-set LAError code (and, by the same funnel, a nil/foreign error) maps
        // to Error — still DATA, never a throw (the .NET ToBiometricStatus twin).
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.invalidContext)) }
        _ = bridge.hostCallBegin(16, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
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
        bridge.biometrics.drainForTest() // the LocalAuthentication work runs on the handler's queue (16.4)
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

    func testTheLAContextIsRetainedDuringEvaluationThenReleased() throws {
        installCapture()
        let bridge = AppleShellBridge()
        // Capture the reply WITHOUT calling it — the evaluation is now "in flight".
        let box = ArmedReply()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in box.arm(reply) }
        _ = bridge.hostCallBegin(30, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        bridge.biometrics.drainForTest() // the evaluation is armed on the handler's queue (16.4)

        let reply = try XCTUnwrap(box.armed, "the drained queue must have armed the evaluation")
        XCTAssertTrue(bridge.biometrics.hasInFlightRequestForTest(), "the request awaits the evaluation")
        XCTAssertTrue(bridge.biometrics.contextIsRetainedForTest(),
                      "the LAContext must be held for the call's duration (a deallocated context cancels the eval)")
        XCTAssertTrue(capturedStatuses().isEmpty, "no completion before the reply")

        reply(true, nil) // the OS (here the seam) resolves the evaluation
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.authenticated])
        XCTAssertFalse(bridge.biometrics.contextIsRetainedForTest(), "the context is released on completion")
        XCTAssertFalse(bridge.biometrics.hasInFlightRequestForTest())
    }

    // ── 16.4 (#438) PIN 1: the boot harness waits for the ARMED reply ───────────────
    //
    // WHAT IT PINS. `awaitArmedReply`, the synchronisation both boot tests use, must not
    // return until the seam holds the reply, even when it is called INSIDE the window
    // where the in-flight flag is already true and no reply is armed yet. That window is
    // real and required (see `BnBiometrics.inFlightRequestId`). Before 16.4 it lasted a
    // few instructions, which is why #438's S2 was rare. Since 16.4 it spans the
    // context's creation on the handler's queue, so the old harness would lose it often,
    // not rarely. Here `beforeEvaluationArmedHookForTest` holds it open, so the
    // interleaving is forced, not waited for.
    //
    // HOW IT STAYS DETERMINISTIC. The release is queued on MAIN, and main runs queued
    // blocks only when the harness yields to the run loop. The harness checks before it
    // yields, so its first check always sees the held window. The old shape, "the flag
    // is true, so reply now", returns there with no reply, and this reds.
    //
    // WHAT IT DOES NOT COVER. It pins the harness, not LocalAuthentication, and not the
    // time a cold `LAContext()` takes to reach the window: the boot tests' 30 s still
    // includes that, #438's S1. The begin-contract pin below covers the calling thread.

    func testTheBootHarnessWaitsForTheArmedReplyNotTheInFlightFlag() throws {
        installCapture()
        let bridge = AppleShellBridge()
        let box = ArmedReply()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in box.arm(reply) }
        let inWindow = DispatchSemaphore(value: 0)
        let release = DispatchSemaphore(value: 0)
        BnBiometrics.beforeEvaluationArmedHookForTest = { inWindow.signal(); release.wait() }
        // Whatever the outcome, let the queue finish arming before tearDown resets the seams.
        defer { release.signal(); bridge.biometrics.drainForTest() }

        _ = bridge.hostCallBegin(31, BnHostCallOp.biometrics, "{\"action\":\"authenticate\",\"reason\":\"x\"}")
        XCTAssertEqual(inWindow.wait(timeout: .now() + 10), .success,
                       "the biometrics queue never reached the window between the record and the arming")

        // The window, held open: the old signal already says "go", and there is nothing to call.
        XCTAssertTrue(bridge.biometrics.hasInFlightRequestForTest(), "the request is recorded before the arming")
        XCTAssertNil(box.armed, "no reply is armed inside the window")

        DispatchQueue.main.async { release.signal() }
        let reply = try XCTUnwrap(awaitArmedReply(box, on: bridge.biometrics, deadline: 10),
                                  "the boot harness returned without an armed reply: a reply sent now would be a silent no-op, #438's S2")
        reply(false, LAError(.userCancel))
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.cancelled], "the reply the harness returned completes the call")
    }

    // ── 16.4 (#438) PIN 2: hostCallBegin returns while LAContext creation is slow ─────
    //
    // WHAT IT PINS. The begin contract: `hostCallBegin` returns at once, and the outcome
    // arrives later through the completion. `contextFactoryForTest` blocks on a
    // semaphore, standing in for a cold `LAContext()` of seconds, and `hostCallBegin`
    // must return while it is still blocked. It is called off main, as the .NET render
    // thread calls it in production. If the context were created on the calling thread
    // again, `hostCallBegin` could not return until the release, and this reds on the
    // 5 s wait instead of hanging.
    //
    // WHAT IT DOES NOT COVER. It proves the CALLING thread is not held. It does not make
    // a cold `LAContext()` faster: the handler's own queue still waits for it, and so
    // does a boot test waiting for the armed reply.

    func testHostCallBeginReturnsWhileContextCreationIsBlocked() {
        installCapture()
        let bridge = AppleShellBridge()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in reply(false, LAError(.userCancel)) }
        let entered = DispatchSemaphore(value: 0)
        let release = DispatchSemaphore(value: 0)
        BnBiometrics.contextFactoryForTest = { entered.signal(); release.wait(); return LAContext() }
        defer { release.signal(); bridge.biometrics.drainForTest() }

        assertBeginReturnsWhileTheFactoryIsBlocked(bridge, requestId: 32,
                                                   "{\"action\":\"authenticate\",\"reason\":\"x\"}",
                                                   entered: entered)
        release.signal()
        bridge.biometrics.drainForTest()
        XCTAssertEqual(capturedStatuses(), [BnBiometricStatus.cancelled], "the flow completes once the context exists")
    }

    func testHostCallBeginReturnsWhileContextCreationIsBlockedForCheck() {
        installCapture()
        let bridge = AppleShellBridge()
        // No canEvaluatePolicy override: `check` must build a context, through the factory.
        let entered = DispatchSemaphore(value: 0)
        let release = DispatchSemaphore(value: 0)
        BnBiometrics.contextFactoryForTest = { entered.signal(); release.wait(); return LAContext() }
        defer { release.signal(); bridge.biometrics.drainForTest() }

        assertBeginReturnsWhileTheFactoryIsBlocked(bridge, requestId: 33, "{\"action\":\"check\"}", entered: entered)
        release.signal()
        bridge.biometrics.drainForTest()
        // The simulator's real answer, whatever it is, arrives exactly once as a status.
        XCTAssertEqual(capturedStatuses().count, 1, "check completes once the context exists")
    }

    private func assertBeginReturnsWhileTheFactoryIsBlocked(_ bridge: AppleShellBridge, requestId: Int64, _ args: String,
                                                            entered: DispatchSemaphore,
                                                            file: StaticString = #filePath, line: UInt = #line) {
        let returned = DispatchSemaphore(value: 0)
        DispatchQueue.global(qos: .userInitiated).async {
            _ = bridge.hostCallBegin(requestId, BnHostCallOp.biometrics, args)
            returned.signal()
        }
        XCTAssertEqual(entered.wait(timeout: .now() + 10), .success,
                       "the LAContext factory was never called", file: file, line: line)
        XCTAssertEqual(returned.wait(timeout: .now() + 5), .success,
                       "hostCallBegin did not return while LAContext creation was blocked: the begin contract is broken",
                       file: file, line: line)
        XCTAssertTrue(capturedStatuses().isEmpty, "no outcome before the context exists", file: file, line: line)
    }

    // ── BOOT: the round trip through the REAL host_call_complete (the /secure demo) ─

    // The evaluatePolicy reply is DEFERRED — captured, then fired AFTER the evaluation is
    // confirmed ARMED (the notifications-boot pattern). This is the realistic shape
    // (the OS Face ID sheet resolves LATER, off the hostCallBegin call — a synchronous
    // reply during hostCallBegin is a path production never takes) and it absorbs the
    // first-cold-boot event-dispatch latency (this is the first NativeAOT boot in the
    // process) BEFORE the bounded echo poll begins. See `awaitArmedReply` for why the
    // wait is for the armed reply and not the in-flight flag (16.4, #438).

    func testAuthenticateBootRoundTripsAuthenticatedThroughTheRealHostCallComplete() throws {
        let box = ArmedReply()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in box.arm(reply) }
        let form = try bootSecureDemo()
        let bio = try XCTUnwrap(runtime?.bridge.biometrics)

        try tapButton("Authenticate", in: form)
        let reply = try XCTUnwrap(awaitArmedReply(box, on: bio, deadline: 30),
                                  "Authenticate never reached the evaluation (the tap did not round-trip to the biometrics op)")
        reply(true, nil) // the OS (here the seam) confirms the identity

        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:Authenticated" },
                      "Authenticate never round-tripped Authenticated to the echo (a hang or mis-route)")
        XCTAssertEqual(BnBiometrics.lastHostCallCompleteRcForTest, 0,
                       "host_call_complete did not route to the in-flight .NET requestId")
    }

    func testAuthenticateBootDeniedIsDataWithinABoundedAwaitNoHang() throws {
        let box = ArmedReply()
        BnBiometrics.evaluatePolicyReplyOverrideForTest = { _, reply in box.arm(reply) }
        let form = try bootSecureDemo()
        let bio = try XCTUnwrap(runtime?.bridge.biometrics)

        try tapButton("Authenticate", in: form)
        let reply = try XCTUnwrap(awaitArmedReply(box, on: bio, deadline: 30),
                                  "Authenticate never reached the evaluation")
        reply(false, LAError(.userCancel)) // the user cancels (the sheet is owner-device territory)

        // The awaiting .NET ValueTask resolves to a CANCELLED the echo shows — bounded
        // await. A HANG (denial thrown/dropped) times this poll out and reddens.
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:Cancelled" },
                      "a cancelled auth never reached the echo within the bounded await (a HANG — denial was not data)")
        XCTAssertEqual(BnBiometrics.lastHostCallCompleteRcForTest, 0)
    }

    // ── The seam's reply, and the wait for it (16.4, #438) ─────────────────────────

    /// The test's hold on the seam's reply. The seam runs on the biometrics queue and the
    /// test reads on main, so it is lock-guarded. Until 16.4 it was a bare captured
    /// `var`, written on one thread and read on another with no synchronisation.
    final class ArmedReply {
        private let lock = NSLock()
        private var reply: ((Bool, Error?) -> Void)?
        func arm(_ r: @escaping (Bool, Error?) -> Void) { lock.lock(); reply = r; lock.unlock() }
        var armed: ((Bool, Error?) -> Void)? { lock.lock(); defer { lock.unlock() }; return reply }
    }

    /// THE BOOT TESTS' SYNCHRONISATION. They reply through the seam, so what they depend
    /// on is the reply being ARMED, and this waits for exactly that event. Until 16.4 they
    /// waited for `hasInFlightRequestForTest()`, which `BnBiometrics` publishes BEFORE it
    /// arms the evaluation, and must (see `inFlightRequestId`). A read inside that window
    /// saw `true` with no reply stored yet, the test's `pendingReply?(…)` silently did
    /// nothing, and the host call hung: #438's S2, logged in diag run 36382752535 as
    /// `inFlight=true replyCaptured=false`.
    ///
    /// This is NOT a loosened assertion. The deadline is unchanged, and the wait is now on
    /// the event the test actually depends on instead of an earlier one. The in-flight
    /// assertion is kept where it says something true: once the reply is armed and not
    /// yet sent, the request is in flight by construction.
    ///
    /// It checks BEFORE it yields to the run loop; the reply-arming pin relies on that.
    ///
    /// A TIMEOUT DIAGNOSES ITSELF (16.4). Since the context moved off the render thread,
    /// 16.3's slow-handler line no longer sees a cold `LAContext()`, so the failure says
    /// which side of the record it stopped on. Recorded but not armed means the queue
    /// was still creating the context: a cold LocalAuthentication start, #438's S1,
    /// accepted as real first-prompt latency. Not recorded means the tap never reached
    /// the op.
    private func awaitArmedReply(_ box: ArmedReply, on bio: BnBiometrics, deadline seconds: TimeInterval,
                                 file: StaticString = #filePath, line: UInt = #line) -> ((Bool, Error?) -> Void)? {
        let end = Date().addingTimeInterval(seconds)
        while true {
            if let reply = box.armed {
                XCTAssertTrue(bio.hasInFlightRequestForTest(), "a reply is armed but no request is in flight",
                              file: file, line: line)
                return reply
            }
            if Date() >= end {
                let message: String
                if bio.hasInFlightRequestForTest() {
                    message = "no reply armed within \(seconds) s, but the request IS recorded "
                        + "(hasInFlightRequestForTest() == true): stuck between the record and the arming, "
                        + "which is LAContext creation on the biometrics queue, a cold LocalAuthentication "
                        + "start, #438 S1. Look for the 'slow LAContext creation' warning."
                } else {
                    message = "no reply armed within \(seconds) s and no request recorded "
                        + "(hasInFlightRequestForTest() == false): the tap never reached the biometrics op."
                }
                XCTFail(message, file: file, line: line)
                return nil
            }
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
        }
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
