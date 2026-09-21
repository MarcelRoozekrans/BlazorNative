// ─────────────────────────────────────────────────────────────────────────────
// BnSafeAreaTests — Phase 14.2 Task 6 (#338) — **`BnSafeArea`, ON THE SIMULATOR,
// THROUGH THE REAL WIRE**.
//
// Mounts `BnSafeAreaDemo` (registry name "BnSafeAreaDemo") through the real
// NativeAOT boot and asserts the canonical table from `bnSafeAreaDemoFrames` —
// the same discipline, and the same cross-shell pairing, as `BnScrollDemoTests`:
// `BnSafeAreaAndroidTest` asserts THE SAME NUMBERS on the AVD, via
// `ShellFrameTableDriftTests` in the required `build-test` lane.
//
// ── WHY THIS CLASS DISPATCHES THE INSETS ITSELF ─────────────────────────────
// An AVD and the iOS simulator do not report the same real insets, so a table
// built from whatever the device happens to say could never agree
// number-for-number across shells. Both device suites instead dispatch
// "safeAreaChanged" with the SAME four fixed values before reading the frame.
//
// ── WHY THIS TEST CALLS `dispatchHostEventAndWait` DIRECTLY, NOT THROUGH
//    `HostViewController` ───────────────────────────────────────────────────
// `HostViewController` is deliberately INERT under XCTest (its own header
// comment: "the test owns the single native session") — every class in this
// suite already bypasses it and drives `BnRuntime` directly, the way this one
// does. `BnRuntime.dispatchHostEventAndWait(_:payload:)` is not a seam added
// for this test: it is the SAME public dispatch primitive
// `HostViewController.reportSafeAreaIfChanged` calls (there, the fire-and-forget
// `dispatchHostEvent` overload; here, the blocking twin so this test does not
// need to poll) — both route through the identical `blazornative_host_event`
// C-ABI export. Calling it directly is this harness's version of what
// `BnSafeAreaAndroidTest` does by feeding a synthetic `WindowInsets` to
// `MainActivity`'s real listener: the furthest either harness can reach into
// production code while still exercising the actual wire path end to end.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnSafeAreaTests: BnHostTestCase {

    // The SAME four numbers bnSafeAreaDemoFrames.kt/.swift declare. Distinct and
    // nonzero on all four edges — see BnDemoFrameTables.swift's header for why.
    private let topDp: CGFloat = 47
    private let rightDp: CGFloat = 21
    private let bottomDp: CGFloat = 34
    private let leftDp: CGFloat = 13

    /// Hold the runtime for the test's lifetime so the @convention(c) callback
    /// trampoline is never released mid-render.
    private var runtime: BnRuntime?
    private var host: UIView!
    private var mapper: BnWidgetMapper!

    override func setUpWithError() throws {
        try super.setUpWithError()
        host = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: host)
        self.mapper = mapper
        let runtime = BnRuntime(mapper: mapper)
        self.runtime = runtime
        runtime.onError = { msg, err in NSLog("[BnSafeAreaTests] \(msg): \(err)") }
        try runtime.start(component: "BnSafeAreaDemo", os: "ios")
    }

    /// Dispatches the fixed insets, waits for the resulting re-render, then asserts
    /// the padded child's frame against the canonical table — proving TopEdge/
    /// RightEdge/BottomEdge/LeftEdge all wired correctly in one frame, since the
    /// child's width depends on left+right and its height on top+bottom (see
    /// BnDemoFrameTables.swift's header for the Grow="1" derivation).
    ///
    /// **Why this polls AFTER `dispatchHostEventAndWait`, not just before it (fix
    /// round 2, CRITICAL 1(a) of the whole-branch review):** the "wait" only covers
    /// .NET's side of the dispatch — `dispatchLane.sync` blocks until
    /// `blazornative_host_event` returns, and that return happens once
    /// `DispatchHostEventCore` has stored the insets and driven `BnSafeArea`'s
    /// `StateHasChanged` synchronously. But the resulting PATCH BATCH still has to
    /// reach UIKit, and `BnWidgetMapper.apply(_:)` hands every batch to
    /// `DispatchQueue.main.async` UNCONDITIONALLY (its own header: "Buffer on the
    /// callback thread; flush atomically on the main queue") — never `.sync`, even
    /// when the caller already IS the main thread, which this test's caller is. So
    /// by the time `dispatchHostEventAndWait` returns, the new frame is merely
    /// QUEUED on the main run loop, not yet applied: asserting immediately reads the
    /// STALE pre-dispatch frame, which is exactly what CI run 35645603753 caught
    /// (measured `(0,0) 300×200` — the pre-dispatch box, not `(13,47) 266×119`).
    /// `BnSafeAreaAndroidTest.kt`'s twin already has this exact step
    /// (`pollForPadding`, dispatching through Android's OWN fire-and-forget path)
    /// for the identical reason; this test just never had it because it was the
    /// first XCTest to call `…AndWait` and then assert synchronously in the same
    /// breath. Spinning the run loop here does not weaken the assertion below in
    /// any way — the expected numbers are unchanged — it only gives the already-
    /// queued main-thread work a chance to run before they are checked.
    func testSafeAreaDemoPadsByTheDispatchedInsetsOnAllFourEdges() throws {
        let safeAreaView = try pollForDemo()
        let content = try XCTUnwrap(safeAreaView.subviews.first,
                                     "the SafeArea's own view must have exactly one child: "
                                     + "the Grow=1 content box")
        let preDispatchFrame = content.frame

        let payload = """
            {"top":"\(topDp)","right":"\(rightDp)","bottom":"\(bottomDp)","left":"\(leftDp)"}
            """
        _ = runtime!.dispatchHostEventAndWait(.safeAreaChanged, payload: payload)

        let landed = pollUntil(deadline: 10) { content.frame != preDispatchFrame }
        XCTAssertTrue(landed, "the safe-area padding never reached the content box within 10s of "
                      + "dispatching safeAreaChanged — the wire (dispatchHostEventAndWait → "
                      + "DispatchHostSafeArea → BnSafeAreaInsets.Current → BnSafeArea → "
                      + "BnWidgetMapper.apply's queued main-thread batch) never landed")

        let f = bnSafeAreaDemoFrames
        assertFrame(f, "content", content,
                    "left=\(leftDp) top=\(topDp) right=\(rightDp) bottom=\(bottomDp), dispatched "
                    + "through the real safeAreaChanged wire; width/height PROVE right/bottom "
                    + "because the child is Grow=1 inside a fixed 300×200 box")
    }

    /// Spins the run loop until `cond` is true or `seconds` elapses — the same
    /// `pollUntil` idiom used throughout this test target (e.g.
    /// `BnModalDemoTests.pollForOpen`), needed here because
    /// `dispatchHostEventAndWait` only guarantees .NET's half of the dispatch is
    /// done; see this method's caller for why.
    @discardableResult
    private func pollUntil(deadline seconds: TimeInterval = 10, _ cond: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            if cond() { return true }
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
        }
        return cond()
    }

    /// Polls until the mount frame has been applied and laid out: `host`'s first
    /// subview (the SafeArea's own view) has its one Grow="1" child, and both
    /// have a computed size.
    private func pollForDemo(deadline seconds: TimeInterval = 30) throws -> UIView {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if let safeAreaView = host.subviews.first, let content = safeAreaView.subviews.first,
               content.frame.height > 0, safeAreaView.frame.height > 0 {
                return safeAreaView
            }
        }
        XCTFail("BnSafeAreaDemo never rendered a laid-out tree within \(Int(seconds))s")
        throw BnRuntimeError.mountFailed(rc: -1, component: "BnSafeAreaDemo")
    }
}
