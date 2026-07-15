// ─────────────────────────────────────────────────────────────────────────────
// BnLayoutDemoTests — Phase 6.1 Task 3.4 — **THE FRAME TABLE, ON THE SIMULATOR**
// (M6 DoD #2).
//
// Mounts BnLayoutDemo (the `/layout` page) through the real NativeAOT boot and
// asserts EVERY frame of the canonical table against the real UIViews, after the real
// layout pass. The table is not invented here: it is the one in
// **`src/BlazorNative.Components/BnLayoutDemo.razor`'s file header**, derived from the
// .NET patch golden (`BnLayoutDemoTests.cs`).
//
// **`BnLayoutDemoAndroidTest` asserts THE SAME NUMBERS on the AVD** — line for line,
// in the same order, with the same tolerance. That pairing — not either test alone —
// is what makes "lays out identically on both platforms" an asserted result rather
// than a claim, and it only works because Yoga computes in density-independent units
// on both: iOS points map 1:1, Android multiplies by `density` at frame-apply time
// (the one conversion site). So every expectation below is in **points = dp**.
//
// **And since the M6 audit (F2), "the same numbers" is an INVARIANT rather than a
// discipline.** This file writes down no frame number at all: it consumes
// `bnLayoutDemoFrames` (BnDemoFrameTables.swift), whose Android twin
// `BnDemoFrameTables.kt` declares the same table — and `ShellFrameTableDriftTests`, in
// the REQUIRED lane, demands the two be equal key for key and number for number. Before
// that, the two shells agreed because someone transcribed carefully. Nothing would have
// caught one shell's `90` becoming `100` while the other's stayed.
//
// There is no iOS route registry (`HostViewController` hardcodes `BnDemo`), so the
// test starts the runtime with "BnLayoutDemo" by NAME — the mount registry key.
//
// ── THE TWO NUMBERS THAT ARE LOAD-BEARING AND EASY TO UNDO ───────────────────
//
//  1. **wrap 3 sits at y = 40** — the line-1 cross size. Yoga's `alignContent`
//     default is `flex-start`, which **DEVIATES from CSS** (`stretch`), and the demo
//     does not set `alignContent` at all (it is not even on the allow-list), so NO
//     PATCH would catch a shell that "helpfully" applied the CSS default. It is
//     asserted here and nowhere else.
//  2. **the wrap boxes are 90dp, not 100.** Four 100s in a 300 row would sit exactly
//     ON Yoga's break boundary (`consumed + item > available`; 300 > 300 is false),
//     where half a point of drift on either shell flips box 3 to line 2 and the
//     platforms "disagree" for a reason that has nothing to do with the engine.
//     3 × 90 = 270 leaves 30 of slack; the break is a fact.
//
// ── THE TWO FRAMES THAT ARE *NOT* NUMBERS ────────────────────────────────────
//
// The text leaf and the "← Back" button carry no pinned point count: a font's metrics
// are not a constant anyone gets to invent, and pinning one would be pinning the
// simulator's font. They sit at the END of the column precisely so a font-dependent
// height cannot shift anything above them — every frame the parity assertion rests on
// is fixed.
//
// They are asserted **RELATIONALLY** (H > 0; the row's height EQUALS the leaf's
// measured height; the back row starts where the text row ended) **and by ORACLE** —
// and the oracle is the one that carries the DoD #3 claim. Every relational assertion
// here also passes with a FABRICATED measure function: feed them the 6.0 spike's
// constant 80×20 stub and all of them stay green, so the measurement could be entirely
// invented and this file would not notice. [assertOracle] measures the same widget,
// with the same text and font, under the same constraint the measure func hands Yoga,
// and demands the laid-out frame EQUAL it. Still no font metric written down; no room
// left to invent one.
//
// ── AND ONE FRAME THAT IS A *NEGATIVE* ───────────────────────────────────────
//
// The root column **hugs its five sections** rather than filling the host. On Android
// that assertion catches a stock `FrameLayout` host root re-laying out every TOP-LEVEL
// node behind Yoga's back. A plain `UIView` does not re-place its subviews, so iOS
// ought to get it for free — but "ought to" is not an assertion, and the old shell DID
// pin the top-level node with three NSLayoutConstraints (which would fight a frame
// assignment). Those are gone; this asserts they stayed gone.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnLayoutDemoTests: BnHostTestCase {

    /// Hold the runtime for the test's lifetime so the @convention(c) callback
    /// trampoline is never released mid-render.
    private var runtime: BnRuntime?

    func testLayoutDemoMatchesTheCanonicalFrameTable() throws {
        let host = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: host)
        let runtime = BnRuntime(mapper: mapper)
        self.runtime = runtime
        runtime.onError = { msg, err in NSLog("[BnLayoutDemoTests] \(msg): \(err)") }

        // HostViewController hardcodes "BnDemo" and there is no iOS route registry —
        // mount the layout page by its registry NAME.
        try runtime.start(component: "BnLayoutDemo", os: "ios")

        let root = try pollForDemo(in: host)
        XCTAssertEqual(root.subviews.count, 5, "the demo has five sections")

        // THE CANONICAL TABLE — declared in BnDemoFrameTables.swift, and pinned against the
        // Android shell's own declaration by ShellFrameTableDriftTests in the REQUIRED lane.
        // No frame number is written down in this file any more: that is the F2 fix. A number
        // that appears here and not there (or moves on one shell and not the other) now
        // reddens a required check.
        let f = bnLayoutDemoFrames

        // The root BnColumn fills the host's width (Yoga's default alignItems:
        // stretch) and stacks its five sections.
        XCTAssertEqual(root.frame.minX, 0, accuracy: 0.5, "the root column starts at the host's origin")
        XCTAssertEqual(root.frame.minY, 0, accuracy: 0.5)
        XCTAssertEqual(root.frame.width, host.bounds.width, accuracy: 0.5,
                       "the root column must fill the host's width")

        // ── [0] the row: fixed 50 · flexGrow:1 · fixed 50 ────────────────────
        let row = root.subviews[0]
        assertFrame(f, "row section", row)
        assertFrame(f, "row box A", row.subviews[0], "W=50, cross-stretched")
        assertFrame(f, "row box B", row.subviews[1], "Grow=1 absorbs 300-50-50")
        assertFrame(f, "row box C", row.subviews[2], "W=50")

        // ── [1] the column: space-between + one alignSelf:center ─────────────
        // free = 200 − 3×40 = 80, split into two 40 gaps → y 0/80/160.
        let col = root.subviews[1]
        assertFrame(f, "column section", col)
        assertFrame(f, "column item 0", col.subviews[0])
        assertFrame(f, "column item 1", col.subviews[1], "AlignSelf=Center → x = (300-100)/2")
        assertFrame(f, "column item 2", col.subviews[2])

        // ── [2] the wrap row: 3 × 90 on line 1, the 4th onto line 2 ──────────
        let wrap = root.subviews[2]
        assertFrame(f, "wrap section", wrap)
        assertFrame(f, "wrap 0", wrap.subviews[0])
        assertFrame(f, "wrap 1", wrap.subviews[1])
        assertFrame(f, "wrap 2", wrap.subviews[2], "270 of 300 consumed — it still fits")
        assertFrame(f, "wrap 3", wrap.subviews[3],
                    "line 2, at y = 40 BECAUSE Yoga's alignContent defaults to flex-start "
                    + "(CSS says stretch; do not 'correct' it)")

        // ── [3] the text row: the DoD #3 proof ───────────────────────────────
        let textRow = root.subviews[3]
        let label = try XCTUnwrap(textRow.subviews[0] as? UILabel, "the text leaf must be a UILabel")
        // x = 0, y = 400 (the wrap row ended there), w = 150 — from the table. Its HEIGHT is
        // MEASURED and therefore absent from the table: a font's metrics are not a constant
        // anyone gets to invent. It is pinned below, relationally and by oracle.
        assertFrame(f, "text row", textRow)
        XCTAssertEqual(textRow.frame.minY, wrap.frame.maxY, accuracy: 0.5,
                       "the text row starts where the wrap row ended (y = 400)")
        XCTAssertGreaterThan(label.frame.height, 0, "the label must have a MEASURED height (> 0)")
        XCTAssertGreaterThan(
            label.frame.height, label.font.lineHeight * 1.5,
            "the label must have WRAPPED inside 150pt — that is the measurement reaching Yoga "
            + "(one line would be \(label.font.lineHeight)pt, the frame is \(label.frame.height)pt)")
        XCTAssertEqual(textRow.frame.height, label.frame.height, accuracy: 0.5,
                       "THE DoD #3 CLAIM: the row declares no height and hugs the label's "
                       + "MEASURED height")
        XCTAssertEqual(label.frame.minX, 0, accuracy: 0.5, "the label starts at the row's origin")
        XCTAssertEqual(label.frame.minY, 0, accuracy: 0.5)
        XCTAssertLessThanOrEqual(label.frame.width, textRow.frame.width + 0.5,
                                 "the label must not overflow the 150pt row it was measured inside")
        // …AND THE ORACLE. Every assertion above passes with a FABRICATED measure
        // function (feed them the 6.0 spike's constant 80×20 stub and all of them stay
        // green). See assertOracle: same widget, same text, same constraint the measure
        // func hands Yoga, no font metric written down anywhere.
        assertOracle("the measured label", label, availableWidth: textRow.frame.width)

        // ── [4] the back row: a second MEASURED leaf ─────────────────────────
        let backRow = root.subviews[4]
        let back = try XCTUnwrap(backRow.subviews[0] as? UIButton, "the back leaf must be a UIButton")
        // x = 0, w = 300 from the table; its y AND height are MEASURED (both depend on the
        // text row's font-dependent height above it), so both are declared MEASURED in the
        // table and pinned relationally + by oracle here.
        assertFrame(f, "back row", backRow)
        XCTAssertEqual(backRow.frame.minY, textRow.frame.maxY, accuracy: 0.5,
                       "the back row starts where the text row ended (y = 400 + H)")
        XCTAssertEqual(back.title(for: .normal), "← Back")
        XCTAssertGreaterThan(back.frame.height, 0, "the button must have a MEASURED height")
        XCTAssertGreaterThan(back.frame.width, 0, "the button must have a MEASURED width")
        XCTAssertEqual(backRow.frame.height, back.frame.height, accuracy: 0.5,
                       "the back row declares no height and hugs the button's measured height")
        XCTAssertEqual(back.frame.minX, 0, accuracy: 0.5, "the button starts at the row's origin")
        XCTAssertEqual(back.frame.minY, 0, accuracy: 0.5)
        assertOracle("the measured button", back, availableWidth: backRow.frame.width)

        // ── THE ROOT COLUMN HUGS ITS CONTENT ─────────────────────────────────
        // The root BnColumn declares no height, so Yoga sizes it to the sum of its five
        // sections — and NOTHING ELSE may size it. On Android this catches a stock
        // FrameLayout host root re-placing top-level children; on iOS it catches a
        // surviving NSLayoutConstraint (the pre-6.1 shell pinned the top-level node's
        // top/leading/trailing edges, and a live constraint fighting a frame assignment
        // is a classic UIKit bug).
        XCTAssertEqual(root.frame.height, backRow.frame.maxY, accuracy: 0.5,
                       "the root column must HUG its five sections, not fill the host")
        XCTAssertNotEqual(root.frame.height, host.bounds.height,
                          "…and that height must not COINCIDE with the host's, or the assertion "
                          + "above proves nothing (host=\(host.bounds.height), content=\(root.frame.height))")
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // assertFrame / assertOracle live in BnFrameAssertions.swift (shared with the
    // synthetic-frame tests — the point contract and its 0.5pt tolerance are stated
    // once, and they are the SAME sentences the Android suite's FrameAssertions.kt
    // states).

    /// Pumps the MAIN runloop (draining the mapper's DispatchQueue.main.async batch)
    /// until the mount frame has been applied AND laid out: five sections under the
    /// root column, and the root actually has a computed height.
    private func pollForDemo(in host: UIView, deadline seconds: TimeInterval = 30) throws -> UIView {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if let root = host.subviews.first, root.subviews.count == 5, root.frame.height > 0 {
                return root
            }
        }
        XCTFail("BnLayoutDemo never rendered a laid-out five-section tree within \(Int(seconds))s")
        throw BnRuntimeError.mountFailed(rc: -1, component: "BnLayoutDemo")
    }
}
