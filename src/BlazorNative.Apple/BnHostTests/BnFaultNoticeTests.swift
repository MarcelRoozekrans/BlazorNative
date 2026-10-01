// ─────────────────────────────────────────────────────────────────────────────
// BnFaultNoticeTests — Phase 16.1 (#8): the iOS arm of the FaultNotice host-call op.
//
// Since #345's fix a dispatch export returns once the handler's synchronous part has
// run, so a fault AFTER the handler's first await can no longer be its rc 2. .NET
// sends it instead as a FaultNotice: op 5 on the existing hostCallBegin slot, flat-JSON
// args {handlerId, event, type, message}. This suite pins that AppleShellBridge routes
// it to the live runtime's onError and completes it OK with no payload, returning 0.
//
// UNIT LAYER ONLY, the BnGeolocationTests house style: completions are captured
// through BnGeolocation.completeHookForTest instead of crossing into .NET, and the
// onError sink is a capturing BnRuntime installed as BnRuntime.shared for the test.
//
// DOES NOT COVER: that .NET SENDS the notice, which is FaultNoticeTests.cs; the round
// trip through a booted NativeAOT session, which FaultNoticeTest.kt covers on the JVM
// against the same .NET code; or the Android arm, which lives in the shared Kotlin
// BridgeRegistrar.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnFaultNoticeTests: BnHostTestCase {

    private static let noticeArgs =
        "{\"handlerId\":\"7\",\"event\":\"click\",\"type\":\"System.InvalidOperationException\",\"message\":\"late\"}"

    /// The completions the hook captured this test (id, status, payload), in order.
    private var captured: [(id: Int64, status: Int32, payload: String?)] = []
    /// What reached the runtime's onError this test, in order.
    private var errors: [(message: String, error: Error)] = []
    /// Every line BnLog let through this test, in order.
    private var logged: [(level: Int32, category: String, message: String)] = []
    private var runtime: BnRuntime?
    private var savedShared: BnRuntime?

    override func setUp() {
        super.setUp()
        BnGeolocation.resetForTest()
        captured = []
        errors = []
        logged = []
        BnLog.emitHookForTest = { [weak self] level, category, message in
            // Only the bridge's own main-thread lines: the hook is process-wide, and an
            // unsynchronized append from another class's off-main log would be a data race.
            guard category == "AppleShellBridge" else { return }
            self?.logged.append((level, category, message))
        }
        savedShared = BnRuntime.shared
        BnGeolocation.completeHookForTest = { [weak self] id, status, payload in
            self?.captured.append((id, status, payload))
            return 0
        }
    }

    override func tearDown() {
        BnRuntime.shared = savedShared
        runtime = nil
        BnGeolocation.resetForTest()
        BnLog.emitHookForTest = nil
        super.tearDown()
    }

    /// A never-booted runtime whose onError captures, installed as the one the bridge
    /// routes to. Nothing here crosses into .NET.
    private func installCapturingRuntime() {
        let root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let rt = BnRuntime(mapper: bnMapper(root: root))
        rt.onError = { [weak self] msg, err in self?.errors.append((msg, err)) }
        runtime = rt
        BnRuntime.shared = rt
    }

    func testFaultNoticeIsOpFive() {
        // The wire value, frozen: .NET's HostCallOp.FaultNotice and Kotlin's FAULT_NOTICE.
        XCTAssertEqual(BnHostCallOp.faultNotice, 5)
    }

    func testAFaultNoticeRoutesToOnErrorAndCompletesOk() {
        installCapturingRuntime()
        let bridge = AppleShellBridge()

        let rc = bridge.hostCallBegin(40, BnHostCallOp.faultNotice, Self.noticeArgs)

        XCTAssertEqual(rc, 0, "hostCallBegin must return 0 for a FaultNotice")
        XCTAssertEqual(errors.count, 1, "the notice must reach onError exactly once")
        XCTAssertTrue(errors.first?.message.hasPrefix(
            "handler fault after await: System.InvalidOperationException: late") == true,
            "onError message was: \(errors.first?.message ?? "<none>")")
        let fault = errors.first?.error as? BnFaultNotice
        XCTAssertEqual(fault?.handlerId, "7")
        XCTAssertEqual(fault?.eventName, "click")
        XCTAssertEqual(fault?.type, "System.InvalidOperationException")
        XCTAssertEqual(fault?.message, "late")

        // Completed OK (0) with no payload, for THIS request, so .NET drops its entry.
        XCTAssertEqual(captured.count, 1)
        XCTAssertEqual(captured.first?.id, 40)
        XCTAssertEqual(captured.first?.status, BnHostCallStatus.granted,
            "a FaultNotice must complete OK, status 0, not Error, status 5: the arm must call " +
            "completeNotice, not completeUnknownOp")
        XCTAssertNil(captured.first?.payload)
        XCTAssertTrue(logged.filter({ $0.category == "AppleShellBridge" }).isEmpty,
            "a routed notice must not also be logged by the bridge")
    }

    func testAnUnknownOpStillTakesTheErrorBranch_Control() {
        // Rule 3: the capture and the onError sink can tell the two branches apart. An op
        // no shell knows completes with Error and never reaches onError on iOS.
        installCapturingRuntime()
        let bridge = AppleShellBridge()

        let rc = bridge.hostCallBegin(41, 99, "{}")

        XCTAssertEqual(rc, 0, "hostCallBegin must return 0 for an unknown op too: the op is data, completed Error, not a refused call")
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.error],
            "an unknown op must complete Error, status 5, not OK, status 0: the default arm must " +
            "call completeUnknownOp, or a notice arm and the unknown-op branch read the same")
        XCTAssertTrue(errors.isEmpty, "an unknown op must not be reported as a handler fault")
    }

    func testAFaultNoticeWithNoRuntimeIsStillCompletedOk() {
        // Before boot, or under a test bundle that owns no session: logged through BnLog,
        // and still answered, so .NET never waits on it.
        BnRuntime.shared = nil
        let bridge = AppleShellBridge()

        let rc = bridge.hostCallBegin(42, BnHostCallOp.faultNotice, Self.noticeArgs)

        XCTAssertEqual(rc, 0, "hostCallBegin must return 0 for a FaultNotice with no runtime")
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.granted],
            "a FaultNotice with no runtime must still complete OK, status 0, not Error, status 5: " +
            "the arm must call completeNotice, not completeUnknownOp")

        // Defect 3 (16.6): the header's "logged through BnLog" is now asserted, not argued.
        let lines = logged.filter { $0.category == "AppleShellBridge" && $0.level == BnLogLevel.error }
        XCTAssertEqual(lines.count, 1, "the no-runtime FaultNotice must be logged exactly once")
        XCTAssertEqual(lines.first?.message,
            "handler fault after await: System.InvalidOperationException: late (handler 7, event 'click')")
    }
}
