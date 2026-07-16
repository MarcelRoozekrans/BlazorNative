// ─────────────────────────────────────────────────────────────────────────────
// BnModalDemoTests — Phase 7.4 Gate 3: **`/modal` ON THE SIMULATOR** (design
// §"The proof surface"). Mounts `BnModalDemo` through the real NativeAOT boot —
// by its registry NAME, the BnFormDemoTests pattern (iOS mounts by name; the
// route table is .NET's and Android's DEEP_LINK line is Android's) — and
// asserts the numbers `BnModalDemoTests.cs` pinned as the source of truth, plus
// the HOST-DERIVED half only a device can assert (the 6.3 oracle discipline):
// the scrim = the HOST VIEW's own bounds, read at assert time; the content
// box's DECLARED 280 × 180 centered at ((W − 280)/2, (H − 180)/2) against those
// bounds; the overlay the root's LAST subview; the siblings holding the EXACT
// frames they hold with the modal closed (the anchor's zero-footprint rule as a
// frame assertion — the modal sits BETWEEN two declared-size boxes on purpose);
// the indicator's intrinsic size by ORACLE in BOTH hosting contexts.
//
// **EVERY DISMISSAL IS A COUNTED WIRE DISPATCH** ([BnWidgetMapper.
// clickDispatchesSent]) — the shell never closes anything itself: a scrim tap
// dispatches the modal's `click` (the REQUEST), .NET flips Visible, and the
// remove that follows is .NET's answer. The content-box SWALLOW is likewise a
// real dispatch that deliberately moves nothing — only the counter can see it
// (the `changeDispatchesSent` precedent).
//
// **THE HIDE FRAME IS FIVE REMOVES, not one** (the Gate 1 finding, recorded in
// BnModalDemoTests.cs's header): the modal's own diff plus the four nested
// components' disposal removes, descendants first (the 7.2 removes-first
// shape). The dismiss test asserts the purge under that composite shape — the
// node counts return to the closed baseline, the overlay count to 0 — which is
// exactly what the synthetic one-remove tests (BnModalMapperTests) cannot
// prove.
//
// **NO BACK TEST, deliberately** (design decision 3's last line): iOS ships
// nothing there — no hardware back, and sheet/swipe gestures are out of the
// milestone's scope. The Gate 3 row is "same round trips minus back"; nothing
// replaces it.
//
// ── THE TAP PATH — hand-rolled dispatch, and why ─────────────────────────────
// The scrim-tap and content-tap tests fire the recognizer's EFFECT by hand
// (`BnClickTapRecognizer.bnFire()`): a hosted XCTest cannot synthesize a
// `UITouch` (the 7.3 finding, BnFormDemoTests' header — Android drives real
// MotionEvents instead) and a recognizer only transitions state through real
// touch delivery. What the hand-rolled fire bypasses — the touch-view FILTER —
// is pinned separately as a pure decision (BnModalMapperTests' truth table)
// plus the wiring assertion here that the live scrim's recognizer IS its own
// filtering delegate. The dispatch itself then rides the REAL wire: recognizer
// handler → dispatchClick → blazornative_dispatch_event → NativeAOT → the
// @bind pair → the re-render that removes the overlay.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnModalDemoTests: BnHostTestCase {

    // BnModalDemo.razor's consts (derived there, transcribed here — the
    // BnScrollDemo discipline) + BnModal's pinned default scrim.
    private let contentW: CGFloat = 280
    private let contentH: CGFloat = 180
    private let siblingW: CGFloat = 220
    private let siblingH: CGFloat = 48
    private let backRowW: CGFloat = 300
    private let scrimColor = "#80000000"
    private let echoInitial = "sw:false"
    private let echoToggled = "sw:true"

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
        runtime.onError = { msg, err in NSLog("[BnModalDemoTests] \(msg): \(err)") }
        try runtime.start(component: "BnModalDemo", os: "ios")
    }

    // ── Tree access (BnModalDemo.razor's frame table) ─────────────────────────

    /// The page column: the host's FIRST subview (the overlay, when live, is the
    /// LAST — asserting through index 0 is itself part of the model).
    private func page() -> UIView? { host.subviews.first }

    /// The live overlay: the host's SECOND (and last) subview while the modal is
    /// shown; nil while it is not. Found by TYPE (the BnScrollContentView
    /// discipline).
    private func overlay() -> BnModalOverlayView? {
        host.subviews.count == 2 ? host.subviews[1] as? BnModalOverlayView : nil
    }

    /// The content box: the overlay's single child, once its four children landed.
    private func contentBox() -> UIView? {
        guard let over = overlay(), over.subviews.count == 1 else { return nil }
        let box = over.subviews[0]
        return box.subviews.count == 4 ? box : nil
    }

    /// The echo: content-box child [0] — BnText's span, text-collapsed to a UILabel.
    private func echo() -> UILabel? { contentBox()?.subviews.first as? UILabel }

    private func findButton(_ title: String) -> UIButton? {
        func walk(_ view: UIView) -> UIButton? {
            if let button = view as? UIButton, button.title(for: .normal) == title { return button }
            for sub in view.subviews { if let found = walk(sub) { return found } }
            return nil
        }
        return walk(host)
    }

    private func tapButton(_ title: String, file: StaticString = #filePath, line: UInt = #line) {
        guard let button = findButton(title) else {
            XCTFail("Button '\(title)' not found on screen", file: file, line: line)
            return
        }
        button.sendActions(for: .touchUpInside)
    }

    /// The click recognizer on [view] — the scrim's / the content box's wire.
    private func clickRecognizer(on view: UIView) -> BnClickTapRecognizer? {
        view.gestureRecognizers?.compactMap { $0 as? BnClickTapRecognizer }.first
    }

    /// The indicator ORACLE (the 6.3 method; 7.3's lesson — per-platform
    /// intrinsics, never cross-platform pixel claims): a fresh spinner of the
    /// SAME style, asked the SAME question the measure trampoline asks the live
    /// one. The HEIGHT is the assertion; the width is the layout box's business
    /// (asserted on the Yoga frame — the 7.3 UISwitch discipline: whether the
    /// widget accepts an imposed frame is the platform's own answer).
    private func assertIndicatorOracle(_ what: String, _ live: UIView,
                                       file: StaticString = #filePath, line: UInt = #line) {
        guard let spinner = live as? UIActivityIndicatorView else {
            XCTFail("\(what) must be a UIActivityIndicatorView (got \(type(of: live)))",
                    file: file, line: line)
            return
        }
        XCTAssertTrue(spinner.isAnimating, "\(what) must be SPINNING — animating-while-mounted "
                      + "is the contract", file: file, line: line)
        let oracle = UIActivityIndicatorView(style: .medium)
        let fit = oracle.sizeThatFits(
            CGSize(width: live.frame.width, height: .greatestFiniteMagnitude))
        XCTAssertGreaterThan(live.frame.height, 0, "\(what) must have a real height",
                             file: file, line: line)
        XCTAssertEqual(live.frame.height, fit.height, accuracy: 1,
                       "\(what).h must equal what the platform's OWN spinner measures — a "
                       + "fabricated measure func passes every relational assertion and fails "
                       + "this one", file: file, line: line)
    }

    // ── Mount + poll (the BnFormDemoTests runloop-pump shape) ─────────────────

    /// The closed mount, settled: five page-column children, real bounds, no overlay.
    private func pollForPage(deadline seconds: TimeInterval = 60) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
            guard let root = page(), root.subviews.count == 5, root.frame.height > 0,
                  host.subviews.count == 1
            else { continue }
            return true
        }
        return false
    }

    /// The SHOW settled: overlay attached last, content box with its four
    /// children laid out, echo painted.
    private func pollForOpen(deadline seconds: TimeInterval = 10) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
            guard let box = contentBox(), box.frame.height > 0,
                  page()?.subviews.count == 6,
                  (echo()?.text ?? "").isEmpty == false
            else { continue }
            return true
        }
        return false
    }

    /// The HIDE settled: the overlay gone, the page column back to five.
    private func pollForClosed(deadline seconds: TimeInterval = 10) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
            if host.subviews.count == 1 && page()?.subviews.count == 5 { return true }
        }
        return false
    }

    /// Polls the echo only (post-round-trip: the re-render is async off the lane).
    private func pollForEcho(_ expected: String, deadline seconds: TimeInterval = 10) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
            if echo()?.text == expected { return true }
        }
        return echo()?.text == expected
    }

    /// Settles a fixed window (the content-tap test's "a dismissal re-render
    /// would have landed by now" gate).
    private func settle(_ seconds: TimeInterval) {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
        }
    }

    // ── [1] The closed mount (the .NET golden's numbers, on the glass) ────────

    func testMountingByNameMatchesTheClosedGoldensNumbers() throws {
        XCTAssertTrue(pollForPage(), "BnModalDemo never mounted/settled within 60s")
        let root = try XCTUnwrap(page())

        // The golden's child order: trigger, box A, box B, indicator, back row.
        let trigger = try XCTUnwrap(root.subviews[0] as? UIButton)
        XCTAssertEqual(trigger.title(for: .normal), "Show modal")
        // Box A: declared 220 × 48 at x = 0; its y is font-dependent (it sits
        // under the measured trigger) — declared where asserted, measured where
        // not (the 6.3 rule).
        XCTAssertEqual(root.subviews[1].frame.minX, 0, accuracy: 0.5)
        XCTAssertEqual(root.subviews[1].frame.width, siblingW, accuracy: 0.5)
        XCTAssertEqual(root.subviews[1].frame.height, siblingH, accuracy: 0.5)
        XCTAssertEqual(root.subviews[2].frame.minY, root.subviews[1].frame.maxY, accuracy: 0.5,
                       "box B sits DIRECTLY below box A — closed means zero modal wire "
                       + "presence, nothing between the siblings yet")
        XCTAssertEqual(root.subviews[2].frame.width, siblingW, accuracy: 0.5)
        XCTAssertEqual(root.subviews[2].frame.height, siblingH, accuracy: 0.5)

        // The page-level indicator: decision 5's second hosting context.
        assertIndicatorOracle("the page indicator", root.subviews[3])

        // Nav parity: the back row's declared 300.
        let backRow = root.subviews[4]
        XCTAssertEqual(backRow.frame.width, backRowW, accuracy: 0.5)
        let back = try XCTUnwrap(backRow.subviews.first as? UIButton)
        XCTAssertEqual(back.title(for: .normal), "← Back")

        // No overlay, no scrim: the modal has ZERO presence while closed.
        XCTAssertEqual(host.subviews.count, 1)
        XCTAssertEqual(mapper.modalOverlayCount, 0, "no live overlay while closed")
        XCTAssertEqual(mapper.yogaOverlayNodeCount, 0)
    }

    // ── [2] The SHOW frame table (the design's numbers, on the glass) ─────────

    func testShowMatchesTheFrameTableAndTheSiblingsHoldTheirFrames() throws {
        XCTAssertTrue(pollForPage())

        // Capture the siblings' EXACT frames while closed.
        let boxABefore = page()!.subviews[1].frame
        let boxBBefore = page()!.subviews[2].frame

        tapButton("Show modal")
        XCTAssertTrue(pollForOpen(), "the modal never opened within 10s")

        let over = try XCTUnwrap(overlay())

        // THE OVERLAY IS THE ROOT'S LAST SUBVIEW, and the scrim IS the host
        // view's own bounds — read at assert time, never transcribed.
        XCTAssertTrue(host.subviews.last === over, "the overlay is the LAST subview")
        XCTAssertEqual(over.frame, host.bounds,
                       "scrim = the root's own bounds (100%/100% + absolute/0/0)")
        XCTAssertEqual(over.backgroundColor, BnColor.parse(scrimColor),
                       "the scrim paints BnModal's pinned default")

        // The content box: DECLARED 280 × 180 (cross-platform), centered against
        // the root (host-derived): ((W − 280)/2, (H − 180)/2).
        let box = try XCTUnwrap(contentBox())
        let rootW = host.bounds.width
        let rootH = host.bounds.height
        XCTAssertEqual(box.frame.width, contentW, accuracy: 0.5, "box.w — the DECLARED width")
        XCTAssertEqual(box.frame.height, contentH, accuracy: 0.5, "box.h — the DECLARED height")
        XCTAssertEqual(box.frame.minX, (rootW - contentW) / 2, accuracy: 1,
                       "box.x = (W − 280)/2 — the design's arithmetic")
        XCTAssertEqual(box.frame.minY, (rootH - contentH) / 2, accuracy: 1, "box.y = (H − 180)/2")

        // Inside, in page order: echo, switch, indicator (its OVERLAY hosting
        // context — decision 5, proven in both), dismiss.
        XCTAssertEqual((box.subviews[0] as? UILabel)?.text, echoInitial)
        XCTAssertEqual((box.subviews[1] as? UISwitch)?.isOn, false)
        assertIndicatorOracle("the in-modal indicator", box.subviews[2])
        XCTAssertEqual((box.subviews[3] as? UIButton)?.title(for: .normal), "Dismiss")

        // THE ZERO-FOOTPRINT RULE AS A FRAME ASSERTION: the page column now
        // holds SIX children — the anchor took index 2, between the boxes — and
        // the siblings hold the EXACT frames they held closed.
        let root = try XCTUnwrap(page())
        XCTAssertEqual(root.subviews.count, 6)
        let anchor = root.subviews[2]
        XCTAssertTrue(anchor is BnModalAnchorView, "index 2 holds the ANCHOR, found by TYPE")
        XCTAssertEqual(anchor.frame.width, 0, "the anchor is 0 wide")
        XCTAssertEqual(anchor.frame.height, 0, "the anchor is 0 tall")
        XCTAssertEqual(root.subviews[1].frame, boxABefore, "box A did not move")
        XCTAssertEqual(root.subviews[3].frame, boxBBefore,
                       "box B did not move — the anchor contributed NOTHING to the flex flow")

        // The bookkeeping agrees: one live overlay, in both trees.
        XCTAssertEqual(mapper.modalOverlayCount, 1)
        XCTAssertEqual(mapper.yogaOverlayNodeCount, 1)
    }

    // ── [3] The wire INSIDE the overlay (decision 1's whole point) ────────────

    func testSwitchInsideTheModalRoundTripsIntoTheEcho() throws {
        XCTAssertTrue(pollForPage())
        tapButton("Show modal")
        XCTAssertTrue(pollForOpen())

        // Toggle the switch with the user stand-in (sendActions on an ENABLED
        // control is honest — the 7.3 rule): target → dispatch_event → NativeAOT
        // → @bind → re-render → the echo UILabel repaints. Same production
        // ingress as every page — no overlay-special path anywhere.
        let sw = try XCTUnwrap(contentBox()?.subviews[1] as? UISwitch)
        sw.setOn(true, animated: false)
        sw.sendActions(for: .valueChanged)
        XCTAssertTrue(pollForEcho(echoToggled),
                      "the in-modal switch round-trip never re-rendered the echo")

        // …and the re-render REPLACED text in place: the modal is still open,
        // the same overlay, the switch now ON via the value echo.
        XCTAssertEqual(mapper.modalOverlayCount, 1)
        XCTAssertEqual((contentBox()?.subviews[1] as? UISwitch)?.isOn, true)
    }

    // ── [4] Hide is unmount: the composite purge + the overlay-count pin ──────

    func testDismissHidesAndTheOverlayCountReturnsToZeroUnderTheFiveRemoveFrame() throws {
        XCTAssertTrue(pollForPage())
        let nodesClosed = mapper.nodeCount
        let yogaClosed = mapper.yogaNodeCount

        tapButton("Show modal")
        XCTAssertTrue(pollForOpen())
        XCTAssertGreaterThan(mapper.nodeCount, nodesClosed, "the show must have created nodes")
        XCTAssertEqual(mapper.modalOverlayCount, 1)

        // The app's OWN dismiss button — the hide frame this dispatch answers
        // with is the COMPOSITE shape (the Gate 1 finding): FIVE removes, the
        // four nested components' disposal removes ahead of the modal's own
        // (7.2 removes-first). The shell processes them in order; the modal's
        // remove is the one the two-subtree purge hangs off.
        tapButton("Dismiss")
        XCTAssertTrue(pollForClosed(), "the modal never closed within 10s")

        XCTAssertEqual(mapper.nodeCount, nodesClosed,
                       "THE PIN: the node count is back at the closed baseline — under the REAL "
                       + "renderer's five-remove hide frame, not the synthetic one-remove shape")
        XCTAssertEqual(mapper.yogaNodeCount, yogaClosed)
        XCTAssertEqual(mapper.modalOverlayCount, 0,
                       "the overlay count is back to 0 (both subtrees purged)")
        XCTAssertEqual(mapper.yogaOverlayNodeCount, 0)
    }

    // ── [5]/[6] Scrim-tap dismisses; content-tap does not (decision 4) ────────

    func testScrimTapDismissRequestsAndTheBoundPageHides() throws {
        XCTAssertTrue(pollForPage())
        tapButton("Show modal")
        XCTAssertTrue(pollForOpen())

        let over = try XCTUnwrap(overlay())
        let clicksBefore = mapper.clickDispatchesSent

        // The recognizer IS wired for the real touch path (its own filtering
        // delegate, on the scrim itself) — asserted here because the fire below
        // bypasses touch delivery (the file header says why).
        let recognizer = try XCTUnwrap(clickRecognizer(on: over),
                                       "the scrim holds the dismissal-request recognizer")
        XCTAssertTrue(recognizer.delegate === recognizer,
                      "the touch-view filter is in the touch path")

        // The hand-rolled tap: the dismissal REQUEST rides the REAL wire — .NET
        // flips Visible, and the remove that closes the modal is .NET's answer.
        recognizer.bnFire()

        XCTAssertTrue(pollForClosed(), "the scrim tap never hid the modal")
        XCTAssertEqual(mapper.clickDispatchesSent, clicksBefore + 1,
                       "the dismissal was a COUNTED wire dispatch — the shell closed nothing "
                       + "itself; the remove is .NET's answer to the request")
        XCTAssertEqual(mapper.modalOverlayCount, 0)
    }

    func testContentBoxTapDoesNotDismissButItsSwallowDispatchIsReal() throws {
        XCTAssertTrue(pollForPage())
        tapButton("Show modal")
        XCTAssertTrue(pollForOpen())

        let box = try XCTUnwrap(contentBox())
        let clicksBefore = mapper.clickDispatchesSent

        // The content box carries its own click (the SWALLOW — BnModal attaches
        // it so .NET can absorb the tap; on iOS the touch-view filter already
        // keeps the tap off the scrim, and the swallow keeps the WIRE behavior
        // identical across shells). Fire it: a real dispatch, rc 0, that
        // requests nothing.
        let recognizer = try XCTUnwrap(clickRecognizer(on: box),
                                       "the content box holds the swallow recognizer")
        recognizer.bnFire()

        // A dismissal re-render would land within this window.
        settle(1)

        XCTAssertEqual(mapper.modalOverlayCount, 1,
                       "the modal is STILL OPEN — a content tap is not a dismissal")
        XCTAssertEqual(mapper.clickDispatchesSent, clicksBefore + 1,
                       "…but the SWALLOW dispatch was real (rc 0, moves nothing) — only the "
                       + "counter can see it")
        XCTAssertEqual(echo()?.text, echoInitial, "…and the echo never moved")
    }
}
