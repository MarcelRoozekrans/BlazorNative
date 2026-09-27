// ─────────────────────────────────────────────────────────────────────────────
// BnBackOffMainTests — Phase 16.2 (#346): the iOS half of "no main thread waits on .NET".
//
// iOS has no system back, so its share of 16.2 is two things:
//
//   1. NAVIGATION IS FIRE-AND-FORGET. Both navigators BnRuntime wires at boot, the deep
//      link's and the notification tap's, used to call `dispatchHostEventAndWait`, a
//      `dispatchLane.sync` from UIKit's main thread. While a handler held the lane, a
//      link opened from Safari froze the app until the handler let go. They now call
//      `dispatchHostEvent`, a `dispatchLane.async`. BnBackOffMainNavigateTests pins that
//      against a lane that is REALLY held, and proves the hold with a control.
//
//   2. THE BACK OPS HAVE AN ARM. .NET sends BackState (op 6) and BackUnhandled (op 7) to
//      every shell. Without an arm iOS took the unknown-op branch, completed each with
//      Error and logged a warning on every mount. BnBackOpArmTests pins that both now
//      complete OK with no payload, return 0 and do nothing else.
//
// THE HELD LANE: the sample's BackHoldProbe page, whose "Hold" handler is SYNCHRONOUS and
// blocks the render thread on a camera capture. An async handler would not do: since 16.1
// the dispatch export returns at a handler's first await, so nothing would stay held. The
// capture runs under the BnCameraTests seam (`suppressSystemCameraPresentForTest`), which
// records the in-flight request and never completes it until the test fires the delegate.
// The Android twin is BackAndroidTest's #346 pin against the same page.
//
// Harness copied from BnDispatchLaneTests.swift: `openTheCaptureGatesUnderTheSeam`, the
// boot, `tapButton`, `findButton` and `pollUntil`, unchanged apart from names local to this
// file and the probe, which finds BackHoldProbe's "Hold" button instead of the camera
// demo's form. The op-arm unit layer copies BnFaultNoticeTests.swift: completions captured
// through BnGeolocation.completeHookForTest, and a capturing never-booted runtime.
//
// DOES NOT COVER: the notification navigator under a held lane, which is wired by the same
// two lines of BnRuntime.start and shares `dispatchHostEvent` with the deep link, so it is
// pinned only by reading; that .NET SENDS ops 6 and 7, which BackStateNoticeTests.cs pins;
// or the Android arms, which live in the shared Kotlin ShellBridge and BackNoticeTest.kt.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnBackOffMainNavigateTests: BnHostTestCase {

    private var runtime: BnRuntime?
    private var root: UIView!

    override func setUp() {
        super.setUp()
        BnCamera.resetForTest()
    }

    override func tearDown() {
        BnCamera.resetForTest()
        BnDeepLink.shared.clearForTest()
        BnDeepLink.shared.navigateDispatcher = nil // see BnDeepLinkTests.setUp — one hosted process
        super.tearDown()
    }

    func testADeepLinkNavigateReturnsWhileTheLaneIsHeld() throws {
        openTheCaptureGatesUnderTheSeam()
        let page = try bootBackHoldProbe()
        let camera = try XCTUnwrap(runtime?.bridge.camera)
        XCTAssertNotNil(BnDeepLink.shared.navigateDispatcher,
                        "a booted runtime must wire the deep-link navigator, or handle(url:) takes the cold path")

        // Hold's SYNCHRONOUS handler blocks the render thread, and with it the dispatch lane.
        try tapButton("Hold", in: page)
        // Pump main until the camera op has recorded its request: it hops to main to do so,
        // and a blocking navigate would stop main before it could.
        XCTAssertTrue(pollUntil(deadline: 10) { camera.hasInFlightRequestForTest() },
                      "Hold never began the camera call, so nothing holds the lane")

        // Release in ~3 s from a thread of its own, so the OLD navigate, which waited on the
        // lane from main, returns in ~3 s and reds below instead of hanging the suite.
        var released = false
        let releaseLock = NSLock()
        let releaser = DispatchSemaphore(value: 0)
        DispatchQueue.global().async {
            Thread.sleep(forTimeInterval: 3)
            releaseLock.lock(); released = true; releaseLock.unlock()
            camera.fireDidCancelForTest()
            releaser.signal()
        }
        defer {
            _ = releaser.wait(timeout: .now() + 10)
            // Never leave the process-global .NET session held for a later test.
            if camera.hasInFlightRequestForTest() { camera.fireDidCancelForTest() }
        }

        // THE PIN: the warm deep link returns at once. The pre-16.2 navigator took ~3 s here.
        let t0 = Date()
        XCTAssertTrue(BnDeepLink.shared.handle(url: URL(string: "blazornative://settings")!),
                      "the link is ours; handle(url:) must report it handled")
        let elapsed = Date().timeIntervalSince(t0)
        XCTAssertLessThan(elapsed, 1.0,
                          "BnDeepLink.handle(url:) held the main thread for \(elapsed) s while the " +
                          "lane was held. That is #346's iOS shape: the navigator waits on .NET.")

        // Anchor: the measurement was taken against a held lane. Nothing can have moved yet.
        releaseLock.lock(); let releasedEarly = released; releaseLock.unlock()
        XCTAssertFalse(releasedEarly, "the release fired before this check; the window is too short " +
                       "to observe the held state")
        XCTAssertNotNil(findButton(in: root, title: "Hold"), "the page changed while the lane was held")

        // The navigate was ISSUED, not dropped: after the release it lands on /settings.
        XCTAssertTrue(pollUntil(deadline: 15) { self.findButton(in: self.root, title: "Hold") == nil },
                      "the fire-and-forget navigate never replaced BackHoldProbe within 15 s of the " +
                      "release, so it was dropped rather than queued")
        releaseLock.lock(); let releasedAtTheEnd = released; releaseLock.unlock()
        XCTAssertTrue(releasedAtTheEnd, "the navigation cannot precede the release")
    }

    /// Rule 3's control, the twin of BackNoticeTest's positive control on the JVM: the probe
    /// REALLY holds the lane on iOS. The blocking `dispatchHostEventAndWait`, the navigator
    /// the pin above replaced, called from a background thread, does not return until the
    /// held camera call is released. If the probe ever stopped holding, the old navigator
    /// would pass the pin too, and the pin could no longer tell the two apart.
    func testTheHoldProbeHoldsTheLane_Control() throws {
        openTheCaptureGatesUnderTheSeam()
        let page = try bootBackHoldProbe()
        let camera = try XCTUnwrap(runtime?.bridge.camera)
        let rt = try XCTUnwrap(runtime)

        try tapButton("Hold", in: page)
        XCTAssertTrue(pollUntil(deadline: 10) { camera.hasInFlightRequestForTest() },
                      "Hold never began the camera call, so nothing holds the lane")

        let lock = NSLock()
        var releasedAt: Date?
        var returnedAt: Date?
        let returned = DispatchSemaphore(value: 0)
        DispatchQueue.global().async {
            Thread.sleep(forTimeInterval: 1.5)
            lock.lock(); releasedAt = Date(); lock.unlock()
            camera.fireDidCancelForTest()
        }
        DispatchQueue.global().async {
            _ = rt.dispatchHostEventAndWait(.navigate, payload: "/settings")
            lock.lock(); returnedAt = Date(); lock.unlock()
            returned.signal()
        }

        // Pump main while waiting: the held handler's frames apply on main after the release.
        var didReturn = false
        XCTAssertTrue(pollUntil(deadline: 15) {
            if returned.wait(timeout: .now()) == .success { didReturn = true }
            return didReturn
        }, "the blocking navigate never returned within 15 s, even after the release")
        if !didReturn, camera.hasInFlightRequestForTest() { camera.fireDidCancelForTest() }

        lock.lock(); let released = releasedAt; let back = returnedAt; lock.unlock()
        let r = try XCTUnwrap(released, "the release never fired")
        let b = try XCTUnwrap(back, "the blocking navigate never returned")
        XCTAssertGreaterThanOrEqual(b, r,
                                    "the blocking navigate returned BEFORE the held camera call was " +
                                    "released, so BackHoldProbe does not hold the lane and the pin " +
                                    "above cannot tell the old navigator from the new")
    }

    // ── Harness copied from BnDispatchLaneTests.swift ────────────────────────────────────

    struct BootTimeout: Error {}

    /// Opens both capture gates on the sim (availability + authorization) and skips the
    /// un-presentable picker, so a capture stays in flight until the test fires the delegate.
    private func openTheCaptureGatesUnderTheSeam() {
        BnCamera.sourceTypeAvailableOverrideForTest = { true }
        BnCamera.cameraAuthorizationStatusOverrideForTest = { .authorized }
        BnCamera.suppressSystemCameraPresentForTest = true
    }

    private func bootBackHoldProbe() throws -> UIView {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let rt = BnRuntime(mapper: mapper)
        rt.onError = { msg, err in NSLog("[BnBackOffMainNavigateTests] \(msg): \(err)") }
        self.runtime = rt
        try rt.start(component: "BackHoldProbe", os: "ios")
        guard pollUntil(deadline: 30, { self.findButton(in: self.root, title: "Hold") != nil }) else {
            XCTFail("BackHoldProbe never rendered its Hold button within 30s")
            throw BootTimeout()
        }
        return root
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

final class BnBackOpArmTests: BnHostTestCase {

    /// The completions the hook captured this test (id, status, payload), in order.
    private var captured: [(id: Int64, status: Int32, payload: String?)] = []
    /// What reached the runtime's onError this test, in order.
    private var errors: [(message: String, error: Error)] = []
    private var runtime: BnRuntime?
    private var savedShared: BnRuntime?

    override func setUp() {
        super.setUp()
        BnGeolocation.resetForTest()
        captured = []
        errors = []
        savedShared = BnRuntime.shared
        BnGeolocation.completeHookForTest = { [weak self] id, status, payload in
            self?.captured.append((id, status, payload))
            return 0
        }
        installCapturingRuntime()
    }

    override func tearDown() {
        BnRuntime.shared = savedShared
        runtime = nil
        BnGeolocation.resetForTest()
        super.tearDown()
    }

    /// A never-booted runtime whose onError captures, installed as the one the bridge
    /// routes to, so "does nothing" is observable. Nothing here crosses into .NET.
    private func installCapturingRuntime() {
        let root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let rt = BnRuntime(mapper: bnMapper(root: root))
        rt.onError = { [weak self] msg, err in self?.errors.append((msg, err)) }
        runtime = rt
        BnRuntime.shared = rt
    }

    func testTheBackOpsAreSixAndSeven() {
        // The wire values, frozen: .NET's HostCallOp and Kotlin's BACK_STATE / BACK_UNHANDLED.
        XCTAssertEqual(BnHostCallOp.backState, 6)
        XCTAssertEqual(BnHostCallOp.backUnhandled, 7)
    }

    func testABackStateNoticeCompletesOkAndDoesNothingElse() {
        let bridge = AppleShellBridge()

        let rcTrue = bridge.hostCallBegin(60, BnHostCallOp.backState, "{\"canGoBack\":\"true\"}")
        let rcFalse = bridge.hostCallBegin(61, BnHostCallOp.backState, "{\"canGoBack\":\"false\"}")

        XCTAssertEqual(rcTrue, 0, "hostCallBegin must return 0 for a BackState")
        XCTAssertEqual(rcFalse, 0, "hostCallBegin must return 0 for a BackState")
        // Completed OK (0) with no payload, each for ITS request, so .NET drops its entry.
        XCTAssertEqual(captured.map({ $0.id }), [60, 61])
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.granted, BnHostCallStatus.granted])
        XCTAssertTrue(captured.allSatisfy({ $0.payload == nil }), "a BackState completes with no payload")
        XCTAssertTrue(errors.isEmpty, "iOS has no system back: a BackState must not reach onError")
    }

    func testABackUnhandledNoticeCompletesOkAndDoesNothingElse() {
        let bridge = AppleShellBridge()

        let rc = bridge.hostCallBegin(62, BnHostCallOp.backUnhandled, "{}")

        XCTAssertEqual(rc, 0, "hostCallBegin must return 0 for a BackUnhandled")
        XCTAssertEqual(captured.count, 1)
        XCTAssertEqual(captured.first?.id, 62)
        XCTAssertEqual(captured.first?.status, BnHostCallStatus.granted)
        XCTAssertNil(captured.first?.payload)
        XCTAssertTrue(errors.isEmpty, "iOS has no system back: a BackUnhandled must not reach onError")
    }

    func testTheOpAfterBackUnhandledStillTakesTheErrorBranch_Control() {
        // Rule 3: the capture can tell an arm from the unknown-op branch. Before this phase
        // ops 6 and 7 took that branch and completed with Error; the next op still does.
        let bridge = AppleShellBridge()

        let rc = bridge.hostCallBegin(63, BnHostCallOp.backUnhandled + 1, "{}")

        XCTAssertEqual(rc, 0)
        XCTAssertEqual(captured.map({ $0.status }), [BnHostCallStatus.error])
    }
}
