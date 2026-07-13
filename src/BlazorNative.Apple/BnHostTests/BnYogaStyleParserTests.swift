// ─────────────────────────────────────────────────────────────────────────────
// BnYogaStyleParserTests — Phase 6.1 Gate 3 Task 3.1: the VALUE GRAMMAR, unit-tested.
//
// `BnYogaLayout`'s string→setter table and value parser are plain C precisely so
// they can be tested from here without a UIView in sight — and they must be, because
// they are a HAND-WRITTEN MIRROR of Kotlin's `YogaLayout`, and the two would drift on
// the wire's REJECTION path if left to their natural implementations:
//
//   - C's `strtof(value, NULL)` returns 12.0 for "12px", accepts "12abc", skips
//     leading whitespace, and takes its own extensions ("0x10", "inf", "nan").
//   - Java's float grammar (what Kotlin's `toFloatOrNull` screens with) accepts a
//     trailing `f`/`d` — "12f" parses as 12.0.
//
// So each shell HONOURS values the other IGNORES, the two frame tables disagree, and
// the engine gets blamed. **The TEST is the contract, not the implementation**:
// `WidgetMapperSetStyleTest`'s `unparseable_layout_value_is_logged_and_ignored`
// ("12px") and `a_trailing_float_suffix_is_rejected_like_any_other_garbage` ("12f")
// are the pair every shell must pass — and this file is the iOS half of that pair,
// with the rest of the grammar's accept/reject surface around it.
//
// The grammar has exactly ONE normative statement —
// docs/plans/2026-07-13-phase-6.1-design.md §"Style value grammar (normative)".
//
// `bn_yoga_node_set_style` returns 1 when it applied the style and 0 when it logged
// and ignored it: the rc IS the parser's verdict, so a rejection is asserted here
// directly rather than inferred from a frame. The two cases the design NAMES are
// asserted BOTH ways — rc and frame — because "the setter never ran" is only really
// proven by the node keeping Yoga's default.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnYogaStyleParserTests: XCTestCase {

    /// A bare Yoga node, freed at the end of the test. (These are RAW native
    /// allocations — nothing collects them.)
    private func withNode(_ body: (UnsafeMutableRawPointer) -> Void) {
        let node = bn_yoga_node_new()
        body(node)
        bn_yoga_node_free_subtree(node)
    }

    /// The rc of one SetStyle. `nil` value = the wire's reset.
    private func set(_ node: UnsafeMutableRawPointer, _ name: String, _ value: String?) -> Int32 {
        name.withCString { n in
            guard let value = value else { return bn_yoga_node_set_style(node, n, nil) }
            return value.withCString { bn_yoga_node_set_style(node, n, $0) }
        }
    }

    private func assertAccepted(_ node: UnsafeMutableRawPointer, _ name: String, _ value: String?,
                                file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertEqual(set(node, name, value), 1,
                       "\(name)=\(value.map { "'\($0)'" } ?? "null") must be ACCEPTED",
                       file: file, line: line)
    }

    private func assertRejected(_ node: UnsafeMutableRawPointer, _ name: String, _ value: String?,
                                file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertEqual(set(node, name, value), 0,
                       "\(name)=\(value.map { "'\($0)'" } ?? "null") must be REJECTED (logged and ignored)",
                       file: file, line: line)
    }

    // ── The number production ────────────────────────────────────────────────

    /// `[+-]? ( digit+ ('.' digit*)? | '.' digit+ ) ( [eE] [+-]? digit+ )?` — plus
    /// the two other length forms, `N%` and `auto`. Nothing else is a length.
    func testTheNumberProductionAndTheOtherTwoLengthFormsAreAccepted() {
        withNode { node in
            for value in ["12", "0", "12.5", "12.", ".5", "+3", "1e2", "1E2", "1.5e-2", "0.0"] {
                assertAccepted(node, "width", value)
            }
            for value in ["50%", "0%", "12.5%"] {
                assertAccepted(node, "width", value)
            }
            assertAccepted(node, "width", "auto")
            // Unitless floats take the same production — minus % and auto (below).
            assertAccepted(node, "flexGrow", "1")
            assertAccepted(node, "flexShrink", "0.5")
        }
    }

    /// **THE WHOLE STRING MUST BE CONSUMED.** Trailing garbage is a rejection, never
    /// a prefix parse — and the grammar excludes hex/inf/nan and leading whitespace,
    /// all of which C's `strtof` would happily take.
    func testTrailingGarbageAndStrtofsOwnExtensionsAreRejected() {
        withNode { node in
            for value in [
                "12px", "12dp", "12sp",   // NO unit suffixes exist in this grammar
                "12f", "12d",             // Java's float grammar — Kotlin screens these out too
                "12abc", "12 ", " 12", "1 2",
                "0x10", "inf", "-inf", "nan", "NaN", // strtof's own extensions
                "", ".", "+", "-", "e5", "1e", "1e+", "1.2.3", "%", "12%%", "abc",
                "1e40",                   // overflows to +inf — rejected, exactly as Kotlin does
            ] {
                assertRejected(node, "width", value)
            }
        }
    }

    /// The two cases the design NAMES, asserted the way Android asserts them: the
    /// observable proof that the setter never ran is that the node keeps Yoga's
    /// default `width: auto` and stays STRETCHED to the 400pt host (Yoga's default
    /// `alignItems: stretch`). A shell that guessed `12` would show 12.
    func testTheTwoNamedRejectionsLeaveTheNodeAtYogasDefault() {
        for garbage in ["12px", "12f"] {
            let host = bnRender([
                bnCreate(1, "view", nil),
                bnStyle(1, "height", "60"),
                bnStyle(1, "width", garbage),
            ])
            let view = host.root.subviews[0]
            XCTAssertEqual(view.frame.width, 400, accuracy: 0.5,
                           "'\(garbage)' must be IGNORED, not read as 12 — the node must keep Yoga's "
                           + "default width:auto and stay stretched to the 400pt host")
            XCTAssertEqual(view.frame.height, 60, accuracy: 0.5, "the rest of the node is untouched")
        }
    }

    // ── Negatives, `auto`, and the enum words ────────────────────────────────

    /// Negatives are accepted ONLY for `margin` and the position offsets — Yoga
    /// defines both (a negative margin pulls a node toward its sibling; a negative
    /// offset shifts it against the inset direction). Everywhere else they are
    /// REJECTED AND LOGGED, never clamped: a silently-clamped 0 is a frame the two
    /// platforms may not agree on.
    func testNegativesAreAcceptedOnlyForMarginAndTheOffsets() {
        withNode { node in
            assertAccepted(node, "margin", "-8")
            assertAccepted(node, "top", "-8")
            assertAccepted(node, "right", "-8")
            assertAccepted(node, "bottom", "-8")
            assertAccepted(node, "left", "-8")

            for name in ["width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight",
                         "flexBasis", "padding", "gap", "flexGrow", "flexShrink"] {
                assertRejected(node, name, "-1")
            }
        }
    }

    /// `auto` is a legal value only where YOGA has a setter for it.
    func testAutoIsOnlyLegalWhereYogaHasIt() {
        withNode { node in
            assertAccepted(node, "width", "auto")
            assertAccepted(node, "height", "auto")
            assertAccepted(node, "flexBasis", "auto")
            assertAccepted(node, "margin", "auto")

            for name in ["padding", "gap", "minWidth", "maxHeight", "top", "left",
                         "flexGrow", "flexShrink"] {
                assertRejected(node, name, "auto")
            }
        }
    }

    /// The enum words are EXACTLY the strings `FlexStyleValues.ToStyleValue()` emits.
    /// No aliases ("horizontal"), no case folding ("ROW"), no cross-enum borrowing
    /// (`space-between` is a `justifyContent` word, not an `alignItems` one).
    func testEnumWordsAreExactlyTheStringsDotNetEmits() {
        withNode { node in
            for value in ["row", "column", "row-reverse", "column-reverse"] {
                assertAccepted(node, "flexDirection", value)
            }
            for value in ["flex-start", "center", "flex-end", "space-between", "space-around", "space-evenly"] {
                assertAccepted(node, "justifyContent", value)
            }
            for value in ["auto", "flex-start", "center", "flex-end", "stretch", "baseline"] {
                assertAccepted(node, "alignItems", value)
                assertAccepted(node, "alignSelf", value)
            }
            for value in ["nowrap", "wrap", "wrap-reverse"] {
                assertAccepted(node, "flexWrap", value)
            }
            for value in ["relative", "absolute"] {
                assertAccepted(node, "position", value)
            }

            assertRejected(node, "flexDirection", "ROW")
            assertRejected(node, "flexDirection", "horizontal")
            assertRejected(node, "justifyContent", "start")
            assertRejected(node, "alignItems", "space-between") // a justify word, not an align one
            assertRejected(node, "flexWrap", "no-wrap")
            assertRejected(node, "position", "static")
            assertRejected(node, "width", "row") // an enum word is not a length
        }
    }

    // ── The null reset ───────────────────────────────────────────────────────

    /// **THE MARGIN/AUTO RULE** — the bug Android shipped and fixed, and the one an
    /// `.mm` written from a green Kotlin suite would re-ship.
    ///
    /// `auto` is a legal VALUE for `margin` but it is NOT its DEFAULT (Yoga's default
    /// is undefined → 0), and `margin: auto` is NOT inert: it absorbs the free space
    /// and re-centres the node. So a null reset must go to `YGUndefined`, never to
    /// `YGNodeStyleSetMarginAuto` — otherwise a REMOVED margin MOVES the node instead
    /// of putting it back, on the exact wire path Gate 1's null-reset fix exists to
    /// serve.
    ///
    /// Frame 1 centres a 100-wide box in a 300-wide row with `margin: auto` (x = 100).
    /// Frame 2 removes the margin → `SetStyle(margin, null)` → the box must go back to
    /// x = 0. A reset-to-auto leaves it at 100.
    func testNullResetsMarginToZeroNotToAuto() {
        let host = bnRender(
            [
                bnCreate(1, "view", nil),
                bnStyle(1, "flexDirection", "row"),
                bnStyle(1, "width", "300"),
                bnStyle(1, "height", "50"),
                bnCreate(2, "view", 1),
                bnStyle(2, "width", "100"),
                bnStyle(2, "height", "50"),
                bnStyle(2, "margin", "auto"),
            ])
        let row = host.root.subviews[0]
        let box = row.subviews[0]
        XCTAssertEqual(box.frame.minX, 100, accuracy: 0.5,
                       "frame 1: `margin: auto` must ABSORB the free space and centre the box "
                       + "— if it did not, the reset below would prove nothing")

        host.render([bnStyle(2, "margin", nil)])

        XCTAssertEqual(box.frame.minX, 0, accuracy: 0.5,
                       "THE PIN: a null reset on `margin` goes to YGUndefined (0), NOT to "
                       + "setMarginAuto. `auto` is a legal VALUE for margin but is not its DEFAULT "
                       + "— resetting to it would MOVE the node instead of putting it back")
    }

    /// The OTHER half of the same rule: for `width`/`height`/`flexBasis`, `auto`
    /// genuinely IS the Yoga default, so a null reset must land on the auto setter —
    /// and a top-level node (a child of the host root, which is a Yoga column with
    /// `alignItems: stretch`) then goes back to filling the 400pt host.
    func testNullResetsWidthToAutoWhichIsItsDefault() {
        let host = bnRender([
            bnCreate(1, "view", nil),
            bnStyle(1, "width", "100"),
            bnStyle(1, "height", "50"),
        ])
        let box = host.root.subviews[0]
        XCTAssertEqual(box.frame.width, 100, accuracy: 0.5, "frame 1: the explicit width")

        host.render([bnStyle(1, "width", nil)])

        XCTAssertEqual(box.frame.width, 400, accuracy: 0.5,
                       "a null reset on `width` restores Yoga's default (auto) — and a top-level "
                       + "node then stretches to the host again. Keeping 100 would mean the reset "
                       + "never reached the node; going to 0 would mean it reset to the WRONG default")
        XCTAssertEqual(box.frame.height, 50, accuracy: 0.5, "the rest of the node is untouched")
    }

    // ── The routing table ────────────────────────────────────────────────────

    /// `bn_yoga_is_layout_style` is the .mm's half of the partition — the routing
    /// table `BnWidgetMapper.handleSetStyle` dispatches on. (Its CONTENT is pinned
    /// against `NativeRenderer.YogaStyleAttributes` by `ShellStyleTableDriftTests` in
    /// the .NET suite, the one lane where all three mirrors are checkout-visible.)
    func testTheRoutingTableSendsLayoutToYogaAndPaintToTheView() {
        for name in ["flexDirection", "justifyContent", "alignItems", "flexWrap", "gap",
                     "alignSelf", "flexGrow", "flexShrink", "flexBasis",
                     "width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight",
                     "padding", "margin", "position", "top", "right", "bottom", "left"] {
            XCTAssertEqual(bn_yoga_is_layout_style(name), 1, "'\(name)' is LAYOUT — it belongs to Yoga")
        }
        for name in ["backgroundColor", "color", "fontSize", "fontWeight", "background", "style"] {
            XCTAssertEqual(bn_yoga_is_layout_style(name), 0,
                           "'\(name)' is VISUAL — routing it to Yoga would double-apply or drop it")
        }
        // Matching is ORDINAL/case-sensitive on every shell, and an unknown name is
        // neither: it falls to the visual branch, which logs "not yet supported".
        XCTAssertEqual(bn_yoga_is_layout_style("FlexGrow"), 0, "matching is case-SENSITIVE")
        XCTAssertEqual(bn_yoga_is_layout_style("boxShadow"), 0)
        XCTAssertEqual(bn_yoga_is_layout_style("alignContent"), 0,
                       "alignContent is deliberately NOT on the allow-list — the wrap demo RELIES "
                       + "on Yoga's flex-start default, and nothing types it")
    }
}
