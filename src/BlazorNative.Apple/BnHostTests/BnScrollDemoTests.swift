// ─────────────────────────────────────────────────────────────────────────────
// BnScrollDemoTests — Phase 6.2 Gate 3 — **THE SCROLL DEMO, ON THE SIMULATOR**
// (M6 DoD #4).
//
// Mounts `BnScrollDemo` through the real NativeAOT boot and asserts the canonical table
// from **`src/BlazorNative.Components/BnScrollDemo.cs`'s file header** — the same
// discipline, and the same pairing, as `BnLayoutDemoTests`: **`BnScrollDemoAndroidTest`
// asserts THE SAME NUMBERS on the AVD**, line for line, with the same tolerance. That
// PAIRING — not either test alone — is what DoD #4 means by "on both platforms". It
// works because Yoga computes in density-independent units on both: iOS points map 1:1,
// Android multiplies by `density` at frame-apply time.
//
// | node | x | y | w | h |
// |---|---|---|---|---|
// | viewport (`UIScrollView`) | 0 | 0 | 300 | 200 |
// | **content** (SYNTHETIC) | 0 | 0 | 300 | **800** |
// | row *i* | 0 | 80·*i* | 300 | 80 |
// | row 1's flex row | 0 | 0 | 300 | 80 |
// | box A · **box B (`Grow=1`)** · box C | 0 · **50** · 250 | 0 | 50 · **200** · 50 | 80 |
// | back row | 0 | **200** | 300 | *measured* |
//
// ── THE TWO ASSERTIONS THAT ARE THE PHASE ───────────────────────────────────
//
// 1. **`contentSize > viewport`, FROM YOGA.** The content node is 800 tall inside a
//    200-tall viewport, and that 800 is the *computed height of a Yoga node* — not a
//    shell-side union of child frames (non-negotiable #3), and not a number this file
//    transcribed: it is what ten 80-high rows in a `height: auto` column add up to.
// 2. **The rows actually MOVE.** Numbers that add up are not a scroll. So the test
//    drives `contentOffset` to the end of the range and re-reads every row's position
//    *relative to the viewport* — through UIKit's own `convert`, which folds in the
//    scroll view's `bounds.origin`, so it is the framework's answer to "where is this
//    row on the glass" rather than this test's arithmetic about it.
//
// At offset 600 the visible window is content y **600..800**: **row 7 (560..640) is
// PARTIALLY visible — its bottom 40pt only** — and rows 8 and 9 are fully visible. Not
// "rows 7, 8 and 9 fill the viewport"; row 7 does not (the Gate 1 review caught that
// claim in the design before it became an `assertFullyVisible(row7)` in two shells).
//
// ── ONE PLACE THE TWO SHELLS' TESTS DIFFER, AND WHY IT IS NOT A DIVERGENCE ───
//
// Android drives `scrollTo(0, 10_000dp)` and asserts the framework CLAMPS it to exactly
// 600 — `ScrollView` derives its maximum from the content child's laid-out height, so
// the clamp is a second, independent witness to the 800. **`UIScrollView` does not clamp
// a PROGRAMMATIC `contentOffset`** (it accepts arbitrary overscroll, by design), so
// there is no such witness to borrow and this file must not fake one. The same fact is
// asserted directly instead: `contentSize.height − bounds.height == 600`, from the
// contentSize Yoga computed. Same number, same source, one fewer coincidence.
//
// There is no iOS route registry (`HostViewController` hardcodes `BnDemo`), so the demo
// is mounted by its registry NAME — the pattern `BnLayoutDemoTests` uses.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnScrollDemoTests: XCTestCase {

    /// BnScrollDemo's four inputs (BnScrollDemo.cs: RowCount, RowHeightDp,
    /// ViewportWidthDp, ViewportHeightDp) and the two products the contract COMPUTES
    /// from them — ContentHeightDp and ScrollRangeDp. Derived here too, not transcribed:
    /// a changed row height must move both sides at once.
    private let rows = 10
    private let rowH: CGFloat = 80
    private let viewW: CGFloat = 300
    private let viewH: CGFloat = 200
    private var contentH: CGFloat { CGFloat(rows) * rowH }      // 800
    private var scrollRange: CGFloat { contentH - viewH }       // 600
    private let flexRow = 1                                     // the row hosting the nested flex row

    /// Hold the runtime for the test's lifetime so the @convention(c) callback
    /// trampoline is never released mid-render.
    private var runtime: BnRuntime?
    private var host: UIView!

    override func setUpWithError() throws {
        host = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = BnWidgetMapper(root: host)
        let runtime = BnRuntime(mapper: mapper)
        self.runtime = runtime
        runtime.onError = { msg, err in NSLog("[BnScrollDemoTests] \(msg): \(err)") }
        try runtime.start(component: "BnScrollDemo", os: "ios")
    }

    /// Every fixed frame in the table, plus the two nestings (a scroll inside flex; a
    /// flex row inside a scrolled row) — and the content size, which is the phase.
    func testScrollDemoMatchesTheCanonicalFrameTable() throws {
        let root = try pollForDemo()
        XCTAssertEqual(root.subviews.count, 2,
                       "the demo has two sections: the viewport and the back row")

        // ── [0] the VIEWPORT, and the SYNTHETIC content node inside it ───────
        let scroll = try bnScrollView(root.subviews[0])
        assertFrame("the viewport", scroll, 0, 0, viewW, viewH)

        let content = try bnContentView(of: scroll)
        assertFrame("THE CONTENT SIZE: the synthetic content node's Yoga frame. 800 = ten 80-high "
                    + "rows in a height:auto column, computed by Yoga and READ by the shell — never "
                    + "a union of child frames", content, 0, 0, viewW, contentH)
        XCTAssertEqual(scroll.contentSize.width, viewW, accuracy: 0.5,
                       "…and contentSize IS that frame — the number the viewport scrolls over")
        XCTAssertEqual(scroll.contentSize.height, contentH, accuracy: 0.5)

        XCTAssertGreaterThan(scroll.contentSize.height, scroll.bounds.height,
                             "THE PHASE: contentSize (\(scroll.contentSize.height)pt) must EXCEED "
                             + "the viewport (\(scroll.bounds.height)pt) — everything else here is "
                             + "bookkeeping")
        XCTAssertEqual(scroll.contentSize.height - scroll.bounds.height, scrollRange, accuracy: 0.5,
                       "…by exactly the scrollable range, 800 − 200")

        // ── the ten rows, inside the content node ────────────────────────────
        XCTAssertEqual(content.subviews.count, rows, "ten rows, all children of the CONTENT view")
        for i in 0..<rows {
            assertFrame("row \(i) (no Width — stretched to the content node's 300 by Yoga's default "
                        + "alignItems:stretch, which is what proves the content node spans the "
                        + "viewport)",
                        content.subviews[i], 0, rowH * CGFloat(i), viewW, rowH)
        }

        // ── FLEX NESTED INSIDE THE SCROLL (design §Verification #4) ──────────
        // Row 1 on purpose: at offset 0 it is fully visible (y 80..160 of a 200-high
        // viewport), so the nesting proof is in the FIRST screenshot.
        let nested = content.subviews[flexRow].subviews[0]
        assertFrame("the nested flex row (Grow=1 in row 1's definite 80pt column)",
                    nested, 0, 0, viewW, rowH)
        assertFrame("box A (W=50, cross-stretched to the row's 80)",
                    nested.subviews[0], 0, 0, 50, rowH)
        assertFrame("box B (Grow=1 absorbs 300 − 50 − 50) — the SAME 200 BnLayoutDemo's box B "
                    + "computes, now two levels inside a scroll",
                    nested.subviews[1], 50, 0, 200, rowH)
        assertFrame("box C (W=50)", nested.subviews[2], 250, 0, 50, rowH)

        // ── [1] the back row — OUTSIDE the viewport ─────────────────────────
        // A page whose only exit can scroll off the screen is not a page with an exit. It
        // is also the only MEASURED leaf here, so it is asserted relationally and by
        // ORACLE — no font constant is anyone's to invent.
        let backRow = root.subviews[1]
        let back = try XCTUnwrap(backRow.subviews[0] as? UIButton, "the back leaf must be a UIButton")
        XCTAssertEqual(backRow.frame.minY, scroll.frame.maxY, accuracy: 0.5,
                       "the back row starts where the VIEWPORT ends — outside it, not at the bottom "
                       + "of the 800pt of content")
        XCTAssertEqual(backRow.frame.minY, viewH, accuracy: 0.5, "…at the viewport's height (200)")
        XCTAssertEqual(backRow.frame.width, viewW, accuracy: 0.5, "the back row is 300pt wide")
        XCTAssertEqual(back.title(for: .normal), "← Back")
        XCTAssertEqual(backRow.frame.height, back.frame.height, accuracy: 0.5,
                       "the back row declares no height and hugs the button's MEASURED height")
        assertOracle("the measured back button", back, availableWidth: backRow.frame.width)

        XCTAssertEqual(root.frame.height, backRow.frame.maxY, accuracy: 0.5,
                       "the root column HUGS its two sections (viewport + back row) — the pin that "
                       + "catches a host root re-laying out top-level nodes behind Yoga's back")
        XCTAssertNotEqual(root.frame.height, host.bounds.height,
                          "…and that height must not COINCIDE with the host's, or the assertion "
                          + "above proves nothing")
    }

    /// **THE LOAD-BEARING PART: THE ROWS ACTUALLY MOVE.**
    ///
    /// Frames that add up prove arithmetic. This drives `contentOffset` and re-reads every
    /// row's top edge *in the viewport's coordinate space* — the number a user's eye reads
    /// — and asserts the window over the content is the one the contract says.
    func testDrivingTheContentOffsetMovesTheRowsOverTheViewport() throws {
        let root = try pollForDemo()
        let scroll = try bnScrollView(root.subviews[0])
        let content = try bnContentView(of: scroll)

        // ── At offset 0: the window is content y 0..200 ──────────────────────
        XCTAssertEqual(scroll.contentOffset, .zero, "a freshly mounted scroll view sits at offset 0")
        assertVisibleWindow("at offset 0", content: content, scroll: scroll, offset: 0)
        for i in 0..<2 {
            XCTAssertLessThanOrEqual(bottomInViewport(content.subviews[i], scroll), viewH + 0.5,
                                     "row \(i) is fully INSIDE the viewport")
        }
        let row2 = content.subviews[2]
        XCTAssertLessThan(topInViewport(row2, scroll), viewH,
                          "row 2 STRADDLES the bottom edge (content 160..240 against a 200 cut)")
        XCTAssertGreaterThan(bottomInViewport(row2, scroll), viewH, "…on the other side of it")
        for i in 3..<rows {
            XCTAssertGreaterThanOrEqual(topInViewport(content.subviews[i], scroll), viewH - 0.5,
                                        "rows 3-9 are entirely BELOW the viewport (row \(i))")
        }
        let row9Before = topInViewport(content.subviews[9], scroll)

        // ── Drive it to the END OF THE RANGE ─────────────────────────────────
        // The maximum is `contentSize − bounds` = 800 − 200 = 600, and that number came out
        // of YOGA (the content node's computed height). Android gets a second, independent
        // witness for free — `ScrollView` CLAMPS a runaway `scrollTo` to exactly this — but
        // `UIScrollView` accepts arbitrary programmatic overscroll, so there is no clamp to
        // borrow and this file does not pretend otherwise.
        let maxOffset = scroll.contentSize.height - scroll.bounds.height
        XCTAssertEqual(maxOffset, scrollRange, accuracy: 0.5,
                       "the scrollable range is Yoga's content size minus the viewport: 800 − 200")
        scroll.setContentOffset(CGPoint(x: 0, y: maxOffset), animated: false)

        // ── At offset 600: the window is content y 600..800 ──────────────────
        assertVisibleWindow("at offset 600", content: content, scroll: scroll, offset: scrollRange)

        let row9After = topInViewport(content.subviews[9], scroll)
        XCTAssertEqual(row9After - row9Before, -scrollRange, accuracy: 0.5,
                       "THE ROWS MOVED: row 9 travelled exactly the scroll range (600pt) up the "
                       + "viewport — read through UIKit's own coordinate conversion, which folds in "
                       + "the scroll view's bounds.origin. This — not the arithmetic above — is what "
                       + "'it scrolls' means.")

        // Row 7 spans content y 560..640, so at offset 600 only its BOTTOM 40pt is on
        // screen. NOT "rows 7, 8 and 9 fill the viewport" — row 7 does not.
        let row7 = content.subviews[7]
        XCTAssertEqual(topInViewport(row7, scroll), -40, accuracy: 0.5,
                       "row 7's top is 40pt ABOVE the viewport's top edge (560 − 600)")
        XCTAssertEqual(bottomInViewport(row7, scroll), 40, accuracy: 0.5,
                       "…so exactly its bottom 40pt is visible — PARTIALLY, not fully")

        XCTAssertEqual(topInViewport(content.subviews[8], scroll), 40, accuracy: 0.5,
                       "row 8 is FULLY visible, at viewport y 40 (content 640 − 600)")
        XCTAssertEqual(topInViewport(content.subviews[9], scroll), 120, accuracy: 0.5,
                       "row 9 is FULLY visible, at viewport y 120 (content 720 − 600)…")
        XCTAssertEqual(bottomInViewport(content.subviews[9], scroll), viewH, accuracy: 0.5,
                       "…and its bottom edge lands exactly on the viewport's: the content cannot "
                       + "scroll further")

        for i in 0...6 {
            XCTAssertLessThanOrEqual(bottomInViewport(content.subviews[i], scroll), 0.5,
                                     "rows 0-6 are entirely ABOVE the viewport now (row \(i))")
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// Every row's top edge, in the VIEWPORT's coordinate space, is `80i − offset` — the
    /// single sentence both scroll assertions above are made of.
    private func assertVisibleWindow(_ what: String, content: UIView, scroll: UIScrollView,
                                     offset: CGFloat,
                                     file: StaticString = #filePath, line: UInt = #line) {
        for i in 0..<rows {
            XCTAssertEqual(topInViewport(content.subviews[i], scroll),
                           rowH * CGFloat(i) - offset, accuracy: 0.5,
                           "\(what): row \(i) sits at viewport y = 80×\(i) − \(offset)",
                           file: file, line: line)
        }
    }

    /// A view's top edge relative to the viewport's, in points. Goes through UIKit's own
    /// `convert`, which folds in each ancestor's `bounds.origin` — and a `UIScrollView`'s
    /// `contentOffset` **IS** its `bounds.origin`. So this is the framework's answer to
    /// "where is this row on the glass", not ours. (The twin of the Android test's
    /// `getLocationOnScreen`, which subtracts each ancestor's scroll offset for the same
    /// reason.)
    private func topInViewport(_ view: UIView, _ scroll: UIScrollView) -> CGFloat {
        view.convert(view.bounds, to: scroll.superview).minY - scroll.frame.minY
    }

    private func bottomInViewport(_ view: UIView, _ scroll: UIScrollView) -> CGFloat {
        topInViewport(view, scroll) + view.frame.height
    }

    /// Pumps the MAIN runloop (draining the mapper's `DispatchQueue.main.async` batch)
    /// until the mount frame has been applied AND laid out: a viewport with its ten rows
    /// under the synthetic content view, and a root with a computed height.
    private func pollForDemo(deadline seconds: TimeInterval = 30) throws -> UIView {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if let root = host.subviews.first, root.subviews.count == 2, root.frame.height > 0,
               let scroll = root.subviews[0] as? UIScrollView,
               let content = scroll.subviews.compactMap({ $0 as? BnScrollContentView }).first,
               content.subviews.count == rows, content.frame.height > 0 {
                return root
            }
        }
        XCTFail("BnScrollDemo never rendered a laid-out tree within \(Int(seconds))s")
        throw BnRuntimeError.mountFailed(rc: -1, component: "BnScrollDemo")
    }
}
