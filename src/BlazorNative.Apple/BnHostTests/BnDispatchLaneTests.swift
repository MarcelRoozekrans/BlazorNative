// ─────────────────────────────────────────────────────────────────────────────
// BnDispatchLaneTests — Phase 16.1 (#345): the iOS twin of the lane-blocking pin.
//
// The .NET DispatchLaneBlockingTests and the JVM
// HostEventTest.dispatchHostEventAndWait_returns_while_a_handler_holds_a_host_call pin
// that `blazornative_dispatch_event` returns once the handler's synchronous part has
// run, even while the handler is suspended on a host call that has not completed.
// Before 16.1 the export waited for the whole handler, so an unanswered permission
// sheet held the serial dispatch lane, and every later dispatch queued behind it
// (#339's shape). This is the same measurement through the Swift shell: a booted
// NativeAOT session, the real BnRuntime lane, the real camera op.
//
// THE HELD CALL: the camera capture path under the BnCameraTests seam
// (`suppressSystemCameraPresentForTest`) records the in-flight request and never
// completes it until the test fires the delegate. That is this suite's
// "bridge whose camera op records the request and never completes it".
//
// Harness copied from BnCameraTests.swift: `bootCameraDemo`, `probeForm`, `tapButton`,
// `findButton`, `pollUntil`, and `openTheCaptureGatesUnderTheSeam`, unchanged apart
// from names local to this file.
//
// DOES NOT COVER: the frames a continuation delivers after the export returns (the
// JVM ShutdownQuiescenceTest / RetireLateContinuationTest cover the shared .NET side),
// or predictive back, which iOS does not have.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnDispatchLaneTests: BnHostTestCase {

    private var runtime: BnRuntime?
    private var mapper: BnWidgetMapper?
    private var root: UIView!

    override func setUp() {
        super.setUp()
        BnCamera.resetForTest()
    }

    override func tearDown() {
        BnCamera.resetForTest()
        super.tearDown()
    }

    func testADispatchReturnsWhileAHostCallIsOpen() throws {
        openTheCaptureGatesUnderTheSeam()
        let form = try bootCameraDemo()
        let camera = try XCTUnwrap(runtime?.bridge.camera)
        let rt = try XCTUnwrap(runtime)
        let mapper = try XCTUnwrap(self.mapper)

        // Harvest Take Photo's handler id from the mapper's UI-event seam instead of letting
        // the tap dispatch on its own, so the test can make the BLOCKING call itself.
        let productionOnUiEvent = mapper.onUiEvent
        var takePhoto: Int32?
        mapper.onUiEvent = { handlerId, eventName, _ in
            if eventName == "click" { takePhoto = handlerId }
        }
        try tapButton("Take Photo", in: form)
        mapper.onUiEvent = productionOnUiEvent
        let handlerId = try XCTUnwrap(takePhoto, "the Take Photo tap never reached the UI-event seam")

        let returned = DispatchSemaphore(value: 0)
        var rc: Int32 = Int32.min
        defer {
            // Release the held call through the REAL delegate, whose completion crosses the
            // real `blazornative_host_call_complete` (no completeHookForTest is installed),
            // whatever happened above: the call sits on the process-global .NET session every
            // later test shares.
            if pollUntil(deadline: 10, { camera.hasInFlightRequestForTest() }) {
                camera.fireDidCancelForTest()
            }
            _ = returned.wait(timeout: .now() + 10) // bounded: the lane frees once released
        }

        // Dispatch on a background queue through the test seam, which runs on the lane.
        DispatchQueue.global().async {
            rc = rt.dispatchEventBlocking(handlerId: handlerId, eventName: "click")
            returned.signal()
        }

        // Pump main while waiting: the camera op hops to main to record its in-flight request.
        var didReturn = false
        XCTAssertTrue(
            pollUntil(deadline: 2) {
                if returned.wait(timeout: .now()) == .success { didReturn = true }
                return didReturn
            },
            "dispatch_event did NOT return within 2 s while the Take Photo handler held a camera " +
            "host call open. This is #345: the export is still waiting for the whole handler, " +
            "so the serial dispatch lane is held behind an unanswered permission sheet.")
        if didReturn { returned.signal() } // hand the token back for the defer's wait
        XCTAssertEqual(rc, 0, "the dispatch must succeed, not fail fast")

        // Anchor: the call was genuinely open when the export returned, so the measurement
        // was taken against a suspended handler, not one that had already finished.
        XCTAssertTrue(pollUntil(deadline: 10) { camera.hasInFlightRequestForTest() },
                      "the camera op never recorded an in-flight request, so nothing was held open")
    }

    // ── Harness copied from BnCameraTests.swift ──────────────────────────────────────────

    struct BootTimeout: Error {}

    /// Opens both capture gates on the sim (availability + authorization) and skips the
    /// un-presentable picker, so a capture stays in flight until the test fires the delegate.
    private func openTheCaptureGatesUnderTheSeam() {
        BnCamera.sourceTypeAvailableOverrideForTest = { true }
        BnCamera.cameraAuthorizationStatusOverrideForTest = { .authorized }
        BnCamera.suppressSystemCameraPresentForTest = true
    }

    private func bootCameraDemo() throws -> UIView {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let rt = BnRuntime(mapper: mapper)
        rt.onError = { msg, err in NSLog("[BnDispatchLaneTests] \(msg): \(err)") }
        self.runtime = rt
        self.mapper = mapper
        try rt.start(component: "BnCameraDemo", os: "ios")
        guard pollUntil(deadline: 30, { self.probeForm() != nil }), let form = probeForm() else {
            XCTFail("BnCameraDemo never rendered its Take Photo / Check / BnImage / echo tree within 30s")
            throw BootTimeout()
        }
        return form
    }

    /// The demo root div: root's single child with 4 children (2 buttons + BnImage + echo).
    private func probeForm() -> UIView? {
        guard let form = root.subviews.first, form.subviews.count >= 4 else { return nil }
        return form
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
