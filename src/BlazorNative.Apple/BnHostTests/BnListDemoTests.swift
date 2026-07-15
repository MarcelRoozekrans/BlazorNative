// ─────────────────────────────────────────────────────────────────────────────
// BnListDemoTests — Phase 7.2 Gate 3 Task 3.2 — **THE VIRTUALIZED LIST, ON THE
// SIMULATOR** (M7 DoD #3).
//
// Mounts `BnListDemo` (the `/list` page) through the real NativeAOT boot — by its
// registry NAME, the `BnScrollDemoTests` pattern (there is no iOS route registry)
// — and asserts the canonical numbers from **`src/BlazorNative.Components/
// BnListDemo.razor`'s file header** — derived there, not invented here, and
// asserted by Gate 2 on the AVD as THE SAME NUMBERS (`BnListDemoAndroidTest`,
// line for line — that PAIRING is what "on both platforms" means):
//
//     content height   500 × 64 = 32,000 pt      scroll range  31,600 pt
//     window @ 0       [0, 11)   → 13 children   spacers  0     | 31,296
//     window @ 640     [6, 21)   → 17 children   spacers  384   | 30,656
//     window @ 31,600  [489,500) → 13 children   spacers  31,296| 0
//
// **LIVENESS IS A COUNTED ASSERTION**: the synthetic content view's children are
// ALWAYS 2 spacers + the window rows. 500 rows exist; if more than ~17 native
// UITextFields ever exist at once, virtualization is not virtualizing — and no
// frame table would notice (every frame stays correct with 500 live rows; only
// the CHILD COUNT sees it).
//
// Unlike every previous demo page, the scroll events here ride **the real
// wire**: `setContentOffset` on the simulator → `scrollViewDidScroll` → the
// conflation slot → `blazornative_dispatch_event("scroll", offset-in-pt)` →
// NativeAOT → `BnListWindow.Compute` → a keyed re-render → patches back to this
// shell. Every window-slide assertion below is therefore an end-to-end assertion
// of Gate 1's .NET half AND Gate 3's conflation at once.
//
// ── ONE PLACE THE TWO SHELLS' TESTS DIFFER, AND WHY IT IS NOT A DIVERGENCE ───
// Android's bottom-window test drives `scrollTo(Int.MAX/2)` and the FRAMEWORK
// clamps to 31,600 (`ScrollView` derives its maximum from the content child's
// laid-out height). **`UIScrollView` does not clamp a programmatic
// `contentOffset`** — what iOS has instead is the SHELL's own 6.2 clamp
// (`applyScrollFrames`), which runs at the commit the runaway dispatch triggers
// and takes the offset back to contentSize − viewport. So the same drive-past-
// the-end move lands on the same 31,600 — through the shell's clamp, whose write
// is itself a delegate sample: the clamp echo conflates mid-batch, flushes at
// batch end, and the wire's LAST dispatch carries exactly 31,600. Same number,
// same source (Yoga's 32,000), one clamp per platform, each pinned on its own
// suite.
//
// The THROUGHPUT test is the contract's evidence row: samples-seen vs
// events-dispatched, printed as the conflation ratio (grep the xcodebuild log
// for `[7.2-throughput]`), plus the never-queue bound — a burst of 100
// same-message samples may submit at most a handful of dispatches (the first,
// then one per lane-availability) while the FINAL offset always arrives.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnListDemoTests: BnHostTestCase {

    // BnListDemo.razor's consts (the header's "derived, not invented"
    // discipline): the four inputs and the products the tests assert.
    private let rows = 500
    private let rowH: CGFloat = 64
    private let viewW: CGFloat = 300
    private let viewH: CGFloat = 400
    private let overscan = 4
    private var contentH: CGFloat { CGFloat(rows) * rowH }      // 32,000
    private var scrollRange: CGFloat { contentH - viewH }       // 31,600
    private let midOffset: CGFloat = 640                        // the golden's mid-scroll offset

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
        runtime.onError = { msg, err in NSLog("[BnListDemoTests] \(msg): \(err)") }
        try runtime.start(component: "BnListDemo", os: "ios")
    }

    // ── The numbers, derived (BnListWindow.Compute's arithmetic, mirrored) ────

    /// The half-open live window at [offsetPt] — the same clamp/floor/ceil
    /// `BnListWindow.Compute` performs, so an assertion can derive the expected
    /// window instead of transcribing it.
    private func window(_ offsetPt: CGFloat) -> (start: Int, end: Int) {
        let clamped = min(max(0, offsetPt), max(0, contentH - viewH))
        let firstVisible = Int(floor(clamped / rowH))
        let lastVisibleEx = Int(ceil((clamped + viewH) / rowH))
        return (max(firstVisible - overscan, 0), min(lastVisibleEx + overscan, rows))
    }

    // ── Tree access ───────────────────────────────────────────────────────────

    private func rootView() throws -> UIView {
        try XCTUnwrap(host.subviews.first, "BnListDemo has no root view")
    }

    private func scrollView() throws -> UIScrollView {
        try bnScrollView(try rootView().subviews[0])
    }

    private func contentView() throws -> UIView {
        try bnContentView(of: try scrollView())
    }

    /// Content child [i] as a ROW (a BnView wrapper whose only child is the
    /// row's BnInput). Index 0 and count−1 are the SPACERS, not rows.
    private func rowInput(_ content: UIView, _ i: Int) throws -> UITextField {
        try XCTUnwrap(content.subviews[i].subviews.first as? UITextField,
                      "content child \(i) must be a row wrapping a UITextField")
    }

    /// The rows' identities, in order — "Row 6".."Row 20" is the window.
    private func placeholders(_ content: UIView) -> [String] {
        (1..<(content.subviews.count - 1)).compactMap {
            (content.subviews[$0].subviews.first as? UITextField)?.placeholder
        }
    }

    private func inputByPlaceholder(_ content: UIView, _ placeholder: String) -> UITextField? {
        (1..<(content.subviews.count - 1))
            .compactMap { content.subviews[$0].subviews.first as? UITextField }
            .first { $0.placeholder == placeholder }
    }

    /// A view's top edge relative to the viewport's, in points — through UIKit's
    /// own `convert`, which folds in the scroll view's `bounds.origin` (its
    /// contentOffset). The framework's answer to "where is this row on the
    /// glass", not this test's arithmetic about it (BnScrollDemoTests' helper).
    private func topInViewport(_ view: UIView, _ scroll: UIScrollView) -> CGFloat {
        view.convert(view.bounds, to: scroll.superview).minY - scroll.frame.minY
    }

    // ── Mount + poll ──────────────────────────────────────────────────────────

    /// Pumps the MAIN runloop until the CONTENT view shows the window whose
    /// first row is [firstRow] with [childCount] children (2 spacers + rows),
    /// laid out, AND the scroll wire is QUIESCENT (nothing in flight, nothing
    /// pending) — the settle gate: a window assertion mid-conflation is a race,
    /// not a pin.
    private func pollForWindow(firstRow: Int, childCount: Int,
                               deadline seconds: TimeInterval = 60) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
            guard let root = host.subviews.first, root.subviews.count == 2,
                  let scroll = root.subviews.first as? UIScrollView,
                  let content = scroll.subviews.compactMap({ $0 as? BnScrollContentView }).first,
                  content.subviews.count == childCount,
                  content.frame.height > 0,
                  content.subviews.count > 2,
                  content.subviews[1].subviews.count == 1,
                  (content.subviews[1].subviews.first as? UITextField)?.placeholder == "Row \(firstRow)",
                  mapper.scrollBusyWireCount == 0
            else { continue }
            // One more turn for any batch a completion may itself have posted.
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
            return true
        }
        return false
    }

    private func assertWindow(_ what: String, start: Int, end: Int,
                              file: StaticString = #filePath, line: UInt = #line) throws {
        let content = try contentView()
        let live = end - start

        XCTAssertEqual(content.subviews.count, live + 2,
                       "\(what): LIVENESS — content children are ALWAYS 2 spacers + the window "
                       + "([\(start),\(end)) = \(live) rows). Any other count means "
                       + "virtualization is not virtualizing", file: file, line: line)

        // The spacers: index 0 and last, childless, exactly the arithmetic.
        let lead = content.subviews[0]
        let trail = content.subviews[content.subviews.count - 1]
        XCTAssertEqual(lead.subviews.count, 0,
                       "\(what): the LEAD spacer is childless (a pure height reservation)",
                       file: file, line: line)
        XCTAssertEqual(lead.frame.height, CGFloat(start) * rowH, accuracy: 0.5,
                       "\(what): lead spacer = start × 64", file: file, line: line)
        XCTAssertEqual(trail.frame.height, CGFloat(rows - end) * rowH, accuracy: 0.5,
                       "\(what): trail spacer = (500 − end) × 64", file: file, line: line)
        XCTAssertEqual(trail.frame.minY, CGFloat(end) * rowH, accuracy: 0.5,
                       "\(what): the trail spacer starts where row \(end - 1) ends",
                       file: file, line: line)

        // The rows: identity ("Row i" — the demo's invariant placeholder),
        // 64pt pitch at ABSOLUTE content y = i × 64, full width.
        XCTAssertEqual(placeholders(content), (start..<end).map { "Row \($0)" },
                       "\(what): the window's rows, in order", file: file, line: line)
        for (j, i) in (start..<end).enumerated() {
            let row = content.subviews[1 + j]
            XCTAssertEqual(row.frame.minY, CGFloat(i) * rowH, accuracy: 0.5,
                           "\(what): row \(i) sits at content y = 64 × \(i) — the spacer "
                           + "arithmetic keeps every row at ITS OWN absolute position while the "
                           + "window slides", file: file, line: line)
            XCTAssertEqual(row.frame.height, rowH, accuracy: 0.5,
                           "\(what): row \(i) is exactly one ItemHeight tall", file: file, line: line)
            XCTAssertEqual(row.frame.width, viewW, accuracy: 0.5,
                           "\(what): row \(i) stretches to the content width", file: file, line: line)
        }

        // And the content node's height is 32,000 BY CONSTRUCTION — spacers +
        // window rows tile it at every offset, virtualized or not.
        XCTAssertEqual(content.frame.height, contentH, accuracy: 0.5,
                       "\(what): content height = 500 × 64 = 32,000pt, Yoga-computed, at EVERY "
                       + "window position", file: file, line: line)
    }

    // ── [1] Mount: the header's numbers, at offset 0 ─────────────────────────

    func testMountingTheListMatchesTheHeadersNumbers() throws {
        XCTAssertTrue(pollForWindow(firstRow: 0, childCount: 13),
                      "BnListDemo never rendered its mount window within 60s")
        let root = try rootView()
        let scroll = try scrollView()

        XCTAssertEqual(root.subviews.count, 2,
                       "the page has two sections: the viewport and the back row")
        assertFrame("viewport", scroll, 0, 0, viewW, viewH)
        XCTAssertEqual(scroll.contentOffset, .zero, "a fresh mount sits at offset 0")
        XCTAssertEqual(scroll.contentSize.height, contentH, accuracy: 0.5,
                       "contentSize IS the content node's Yoga frame: 500 × 64 = 32,000 — read "
                       + "straight out, never a shell-side union of child frames")

        // Window @ 0 = [0, 11): 7 visible + 4 trailing overscan, leading
        // overscan clamped at the list start → 13 children incl. spacers.
        try assertWindow("at offset 0", start: 0, end: 11)

        // 500 rows EXIST; 11 of them are live. The other 489 are one trailing
        // spacer — that difference is the phase.
        XCTAssertEqual(try contentView().subviews[0].frame.height, 0, accuracy: 0.5,
                       "…of which the lead spacer is ZERO-height (nothing above row 0)")

        // Nav parity: the exit is OUTSIDE the viewport, so no amount of
        // scrolling 32,000pt of content can take it off the glass.
        let backRow = root.subviews[1]
        let back = try XCTUnwrap(backRow.subviews.first as? UIButton,
                                 "the back leaf must be a UIButton")
        XCTAssertEqual(back.title(for: .normal), "← Back")
        XCTAssertEqual(backRow.frame.minY, scroll.frame.maxY, accuracy: 0.5,
                       "the back row starts where the VIEWPORT ends — outside it, not at the "
                       + "bottom of the 32,000pt of content")
        XCTAssertEqual(backRow.frame.minY, viewH, accuracy: 0.5, "…which is 400pt")
    }

    // ── [2] The slide: 0 → 640 over the REAL wire ────────────────────────────

    func testSlidingTo640ViaTheRealWireMovesTheWindowAndTheSpacers() throws {
        XCTAssertTrue(pollForWindow(firstRow: 0, childCount: 13))
        let dispatchesBefore = mapper.scrollDispatchesSent
        try scrollView().setContentOffset(CGPoint(x: 0, y: midOffset), animated: false)

        XCTAssertTrue(pollForWindow(firstRow: 6, childCount: 17, deadline: 30),
                      "the window never slid to [6,21) — the scroll event did not arrive, or "
                      + ".NET's window math disagrees with the header")

        // The wire: the conflated event FIRED, and the freshest offset it
        // carried is the one we drove — in points, undivided.
        XCTAssertGreaterThan(mapper.scrollDispatchesSent, dispatchesBefore,
                             "at least one conflated scroll dispatch went out")
        XCTAssertEqual(mapper.lastScrollDispatchPt ?? -1, Float(midOffset), accuracy: 0.01,
                       "…and the LAST dispatch carried 640pt — points ARE the density-"
                       + "independent unit (no conversion site): the same number Android sends "
                       + "as dp")

        // Window @ 640 = [6, 21): 15 live rows (ceil(400/64) + 2×4) → 17
        // children; spacers 384 | 30,656 — the header's numbers.
        try assertWindow("at offset 640", start: 6, end: 21)

        // And on the GLASS: the first VISIBLE row is row 10 (640/64), exactly
        // at the viewport's top edge — rows 6..9 are overscan above it. The
        // user's eye and the window math agree.
        let content = try contentView()
        let scroll = try scrollView()
        let row10 = content.subviews[1 + (10 - 6)]
        XCTAssertEqual(topInViewport(row10, scroll), 0, accuracy: 0.5,
                       "row 10's content y (640) minus the offset (640) is ZERO: it sits exactly "
                       + "at the viewport's top edge")
    }

    // ── [3] The bottom window, at max offset ─────────────────────────────────

    func testTheBottomWindowAtMaxOffsetClampsTo31600AndShowsTheLast11Rows() throws {
        XCTAssertTrue(pollForWindow(firstRow: 0, childCount: 13))
        // Drive it PAST the end: the clamp is itself an assertion — see the file
        // header for whose clamp it is on this platform.
        try scrollView().setContentOffset(CGPoint(x: 0, y: 1_000_000), animated: false)

        XCTAssertTrue(pollForWindow(firstRow: 489, childCount: 13, deadline: 30),
                      "the window never slid to the bottom [489,500)")

        let scroll = try scrollView()
        XCTAssertEqual(scroll.contentOffset.y, scrollRange, accuracy: 0.5,
                       "THE MAXIMUM IS THE CONTENT SIZE, ARRIVED AT INDEPENDENTLY: UIScrollView "
                       + "does not clamp a programmatic overscroll, so this 31,600 is the "
                       + "SHELL's own 6.2 clamp (applyScrollFrames) taking the runaway offset "
                       + "back to contentSize − viewport — the shell agreeing with Yoga about "
                       + "the 32,000")
        XCTAssertEqual(mapper.lastScrollDispatchPt ?? -1, Float(scrollRange), accuracy: 0.5,
                       "…and that clamped offset is what the wire carried LAST: the clamp's own "
                       + "contentOffset write is a delegate sample — it conflated mid-batch and "
                       + "the batch end flushed it (the echo path, live on the device)")

        // Window @ 31,600 = [489, 500): trailing overscan clamped at the list
        // end → 13 children; spacers 31,296 | 0.
        try assertWindow("at offset 31,600", start: 489, end: 500)

        // The last row's bottom edge IS the content's bottom edge IS the
        // viewport's bottom edge: nothing below, nowhere further to go.
        let content = try contentView()
        let lastRow = content.subviews[content.subviews.count - 2]
        XCTAssertEqual(lastRow.frame.maxY, contentH, accuracy: 0.5,
                       "row 499 ends exactly at content y 32,000")
    }

    // ── [4] Row state TRAVELS with the @key — and eviction really evicts ─────

    /// The header's proof, verbatim: the row inputs are deliberately UNBOUND, so
    /// text set on one lives ONLY in the native UITextField. A row that SURVIVES
    /// a window slide (row 6 is in [0,11) AND [6,21)) must keep its node → its
    /// view → its text; a row that LEAVES (row 0 is not in [6,21)) is destroyed
    /// with its text, and comes back FRESH — losing the text is what proves the
    /// eviction was real. @key = the item is why the survivor survives.
    func testRowStateTravelsWithTheKeyAndEvictionReallyEvicts() throws {
        XCTAssertTrue(pollForWindow(firstRow: 0, childCount: 13))
        var content = try contentView()
        let row6 = try XCTUnwrap(inputByPlaceholder(content, "Row 6"))
        let row0 = try XCTUnwrap(inputByPlaceholder(content, "Row 0"))
        row6.text = "i travel with my key"
        row0.text = "eviction loses me"

        // Slide the window to [6, 21): rows 0–5 leave, row 6 survives.
        try scrollView().setContentOffset(CGPoint(x: 0, y: midOffset), animated: false)
        XCTAssertTrue(pollForWindow(firstRow: 6, childCount: 17, deadline: 30))
        content = try contentView()
        let row6Mid = try XCTUnwrap(inputByPlaceholder(content, "Row 6"))
        XCTAssertTrue(row6Mid === row6,
                      "ROW 6 SURVIVED THE SLIDE AS THE SAME NATIVE VIEW — its node ids appeared "
                      + "in no create/remove, so the UITextField instance is IDENTICAL "
                      + "(@key = the item)")
        XCTAssertEqual(row6Mid.text, "i travel with my key",
                       "…and its uncommitted native text rode along")
        XCTAssertNil(inputByPlaceholder(content, "Row 0"),
                     "row 0 LEFT the window — it is gone, not hidden")

        // …and back to [0, 11): row 6 survives AGAIN; row 0 re-enters FRESH.
        try scrollView().setContentOffset(.zero, animated: false)
        XCTAssertTrue(pollForWindow(firstRow: 0, childCount: 13, deadline: 30))
        content = try contentView()
        let row6Back = try XCTUnwrap(inputByPlaceholder(content, "Row 6"))
        let row0Back = try XCTUnwrap(inputByPlaceholder(content, "Row 0"))
        XCTAssertTrue(row6Back === row6,
                      "row 6 was in BOTH windows — same view across the round trip")
        XCTAssertEqual(row6Back.text, "i travel with my key", "…text intact")
        XCTAssertFalse(row0Back === row0,
                       "row 0 was DESTROYED and re-created: a DIFFERENT UITextField — this is "
                       + "what proves virtualization actually virtualizes")
        XCTAssertEqual(row0Back.text ?? "", "",
                       "…and unbound native state did NOT survive eviction (BnList's re-entry "
                       + "rule: bind it if you want it back)")
    }

    // ── [5] Throughput: the never-queue proof, deterministically ─────────────

    /// **THE CONTRACT'S EVIDENCE ROW.** 100 scroll samples delivered in ONE
    /// main-thread pass (so no completion can interleave — the lane's completion
    /// is a `DispatchQueue.main.async` hop that cannot run while this thread is
    /// still in the loop — deterministic): the first sample finds the lane free
    /// and dispatches; samples 2..100 conflate, each REPLACING the slot; the
    /// completion then flushes exactly the freshest. The wire carries a HANDFUL
    /// of dispatches (≤ 4 allows a clamp echo; the deterministic count is 2) —
    /// never 100, and never a queue — while the FINAL offset always arrives,
    /// proven both by the counter and by .NET's window landing at [2,17).
    ///
    /// The required mutations redden HERE (and in the mechanism twin): dispatch
    /// per-sample → 100 dispatches trip the ≤ 4 bound; queue-instead-of-replace
    /// → the flush drains a backlog → same trip; drop-the-freshest → the window
    /// never reaches [2,17) and lastScrollDispatchPt ≠ 400.
    func testABurstOf100SamplesConflatesToAHandfulOfDispatchesAndTheFinalOffsetArrives() throws {
        XCTAssertTrue(pollForWindow(firstRow: 0, childCount: 13))
        let samples0 = mapper.scrollSamplesSeen
        let dispatches0 = mapper.scrollDispatchesSent
        let scroll = try scrollView()
        // 100 samples, one pass: 4pt steps to 400pt — every write lands on a
        // distinct offset, so every one fires the delegate.
        for i in 1...100 {
            scroll.contentOffset = CGPoint(x: 0, y: CGFloat(i) * 4)
        }

        // 400pt → window [2,17): floor(400/64)=6 −4 → 2; ceil(800/64)=13 +4 → 17.
        XCTAssertTrue(pollForWindow(firstRow: 2, childCount: 17, deadline: 30),
                      "the final offset's window [2,17) never arrived")

        let samples = mapper.scrollSamplesSeen - samples0
        let dispatches = mapper.scrollDispatchesSent - dispatches0
        let ratio = Float(samples) / Float(max(dispatches, 1))

        // The conclusion-feed number — grep the xcodebuild log for [7.2-throughput].
        NSLog("[7.2-throughput] burst: samples=\(samples) dispatches=\(dispatches) "
              + "conflation-ratio=\(String(format: "%.1f", ratio)):1")

        XCTAssertEqual(samples, 100, "all 100 samples reached the conflation slot")
        XCTAssertTrue((1...4).contains(dispatches),
                      "THE NEVER-QUEUE BOUND: 100 samples may cost at most a handful of "
                      + "dispatches (1 immediate + 1 per lane-availability; got \(dispatches)). "
                      + "100 here means the wire dispatches PER SAMPLE; >4 means samples QUEUED "
                      + "instead of replacing")
        XCTAssertGreaterThanOrEqual(ratio, 25,
                                    "the ratio IS the proof the wire held: \(samples) samples → "
                                    + "\(dispatches) dispatches (\(ratio):1, demanded ≥ 25:1)")
        XCTAssertEqual(mapper.lastScrollDispatchPt ?? -1, 400, accuracy: 0.01,
                       "…and the FINAL offset always arrives: the last dispatch carried exactly "
                       + "400pt — conflation drops stale values, never the freshest")

        // The derivation, kept honest: the asserted window IS the pure function's
        // answer for the driven offset (the same clamp/floor/ceil .NET ran).
        let derived = window(400)
        XCTAssertEqual(derived.start, 2)
        XCTAssertEqual(derived.end, 17)
        try assertWindow("at offset 400", start: derived.start, end: derived.end)
    }
}
