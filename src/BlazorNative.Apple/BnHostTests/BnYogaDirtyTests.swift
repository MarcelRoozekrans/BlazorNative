// ─────────────────────────────────────────────────────────────────────────────
// BnYogaDirtyTests — Phase 6.1 Gate 3: **DIRTY ON CONTENT CHANGE.** The iOS twin of
// `YogaDirtyAndroidTest`, and it exists for the same reason.
//
// Yoga CACHES a measure function's result and will not re-run it unless the node is
// marked dirty. So every patch that changes a widget's intrinsic size — `ReplaceText`,
// `UpdateProp value`/`placeholder`, `SetStyle fontSize` — must call
// `bn_yoga_node_mark_dirty`, or the next layout pass silently reuses the size the OLD
// content measured to.
//
// Nothing else pins that. The frame-table test and the measure oracle both mount FRESH
// trees, where Yoga's cache is cold either way — so the `markDirty` calls could be
// deleted and the whole XCTest suite would still pass. These tests are TWO-FRAME on the
// SAME node, which is the only shape that can catch it, because it is the only shape
// where the cache is warm.
//
// **What actually bites here.** Not "the row hugs the label": with the dirty call
// deleted, BOTH the label and the row are stale and they agree with each other. The
// assertion that bites is **the label GREW** — the frame changed at all — and
// [assertOracle], which demands the new frame equal what the widget NOW measures to.
// (Mutation-verified: deleting `markDirty` from `handleReplaceText` / the `fontSize`
// arm turns exactly these two tests red and nothing else.)
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnYogaDirtyTests: XCTestCase {

    private let longText =
        "This label is measured natively: it wraps inside 150dp and its measured height drives the row."

    /// Frame 1 renders a short label in a fixed-width row; frame 2 sends `ReplaceText`
    /// with a long one on the SAME node. The label must RE-MEASURE (it now wraps) and
    /// the row — which declares no height — must re-hug it.
    func testReplaceTextReMeasuresTheLeafAndTheRowReHugsIt() throws {
        let host = bnRender([
            bnCreate(1, "view", nil),
            bnStyle(1, "flexDirection", "row"),
            bnStyle(1, "width", "150"),
            bnCreate(2, "text", 1),
            bnText(2, "Hi"),
        ])
        let row = host.root.subviews[0]
        let label = try XCTUnwrap(row.subviews[0] as? UILabel)
        let shortHeight = row.frame.height
        XCTAssertGreaterThan(shortHeight, 0, "frame 1 must have laid the row out at all")

        // FRAME 2 — same node, new text. Yoga's measure cache is WARM now.
        host.render([bnText(2, longText)])

        XCTAssertGreaterThan(
            label.frame.height, shortHeight,
            "THE PIN: the label must have RE-MEASURED. Without bn_yoga_node_mark_dirty on "
            + "ReplaceText, Yoga serves the CACHED one-line height (\(shortHeight)) and the label "
            + "keeps the frame its OLD text measured to (now \(label.frame.height))")
        XCTAssertEqual(row.frame.height, label.frame.height, accuracy: 0.5,
                       "…and the row, which declares no height, must re-hug the NEW height")
        assertOracle("the re-texted label", label, availableWidth: row.frame.width)
    }

    /// The `SetStyle` twin: a bigger font is a bigger intrinsic size, and the
    /// `fontSize` arm dirties the node for exactly that reason. (The same call serves
    /// `UpdateProp value`/`placeholder` on a UITextField.)
    func testFontSizeChangeReMeasuresTheLeafAndTheRowReHugsIt() throws {
        let host = bnRender([
            bnCreate(1, "view", nil),
            bnStyle(1, "flexDirection", "row"),
            bnStyle(1, "width", "300"),
            bnCreate(2, "text", 1),
            bnText(2, "Sized by its font"),
            bnStyle(2, "fontSize", "10"),
        ])
        let row = host.root.subviews[0]
        let label = try XCTUnwrap(row.subviews[0] as? UILabel)
        let smallHeight = row.frame.height
        XCTAssertGreaterThan(smallHeight, 0, "frame 1 must have laid the row out at all")

        // FRAME 2 — same node, 4× the font. Same warm cache.
        host.render([bnStyle(2, "fontSize", "40")])

        XCTAssertGreaterThan(
            label.frame.height, smallHeight,
            "THE PIN: a 40pt label is TALLER than a 10pt one. Without bn_yoga_node_mark_dirty on "
            + "the fontSize arm, Yoga serves the CACHED 10pt height (\(smallHeight)) and the text "
            + "is drawn at 40pt inside a 10pt frame (now \(label.frame.height))")
        XCTAssertEqual(row.frame.height, label.frame.height, accuracy: 0.5,
                       "…and the row re-hugs the new measured height")
        assertOracle("the re-sized label", label, availableWidth: row.frame.width)
    }
}
