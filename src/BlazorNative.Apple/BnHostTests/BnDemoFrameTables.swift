// ─────────────────────────────────────────────────────────────────────────────
// **THE CANONICAL FRAME TABLES — the iOS shell's declaration** (M6 audit, F2).
//
// Every frame number the XCTest demo suites assert lives HERE, and nowhere else.
// `src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/BnDemoFrameTables.kt`
// is the Android twin, and **`tests/BlazorNative.Renderer.Tests/ShellFrameTableDriftTests.cs`
// — in the REQUIRED `build-test` lane — parses both files and demands they be equal, table
// for table, key for key, number for number.**
//
// ── WHY THIS FILE EXISTS ─────────────────────────────────────────────────────
//
// "The same frames on both platforms" is not a nice property of M6; it *is* M6. And until
// the audit these tables were **hand-transcribed literals** scattered through
// `BnLayoutDemoTests` / `BnScrollDemoTests` / `BnImageDemoTests` and their three Kotlin
// twins. They agreed — the auditor checked every number — but they agreed *by careful
// transcription*, not by an invariant. Nothing in the repo would have noticed one shell's
// `90` becoming `100` while the other's stayed. It was the last cross-shell contract here
// that nothing checked, standing next to the style tables, the fixture pixel sizes, the
// 72-byte bridge struct and the Yoga version pin — all of which ARE pinned, all by a drift
// test in the required lane, all for exactly this reason.
//
// ── THE RULE THIS FILE OBEYS ─────────────────────────────────────────────────
//
// **The parse target IS the assertion.** The device tests do not restate these numbers;
// they look them up (`assertFrame(bnLayoutDemoFrames, "wrap 3", view)`), and a missing key
// fails. So the table the .NET drift test compares cannot drift away from the table the
// simulator asserts — which is the failure mode a regex over the old inline literals would
// have had, and the reason those literals were not simply parsed where they lay.
//
// ── THE GRAMMAR ──────────────────────────────────────────────────────────────
//
// A cell is a sum of terms; a term is a **literal number**, `wi`, `hi`, or `MEASURED`.
// Nothing else — not a named constant, not an expression. A literal is the only thing two
// languages can be compared on, and any indirection is somewhere a number could hide.
// (`wi`/`hi` are the intrinsic fixture's natural pixel size, read at run time off the
// DECODED bytes on both shells — never transcribed, which is what pins "no downsampling".
// The .NET drift test compares them as SYMBOLS, so `hi + 20` on one shell and `hi + 20` on
// the other is an equality it can check without knowing the number. `MEASURED` is the same
// trick for a font metric.)
//
// The arithmetic these literals came from is not lost. It stays asserted where it always
// was: in the .NET goldens (`BnScrollDemoTests.TheContentSizeIsTheContractsArithmetic` —
// `RowCount × RowHeightDp == 800`, `800 − ViewportHeightDp == 600`), and in the device
// tests' own derived assertions, which are untouched.
//
// Bounded by `// BN-FRAME-TABLE <name>` … `// BN-FRAME-TABLE-END` markers — the parser's
// anchors. A table without an END marker fails the drift test loudly rather than being
// silently half-read.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit

// ── The vocabulary (the twin of FrameAssertions.kt's) ────────────────────────

/// One row of a canonical frame table: a parent-relative frame, in points.
struct BnRect: Equatable {
    let x: CGFloat
    let y: CGFloat
    let w: CGFloat
    let h: CGFloat
}

/// A table cell whose value is a **measured** quantity — a font's metrics, which are not a
/// constant anyone gets to invent (see `assertOracle`). The dimension is declared present
/// but NOT asserted; the measured leaves are pinned relationally and by oracle instead. It
/// is a token the drift parser reads on BOTH shells, so a dimension that is measured on one
/// platform and pinned on the other is a failure.
///
/// Screaming case, deliberately un-Swifty: it is the same token Kotlin writes, and the two
/// declarations are compared as TEXT by a test that does not know either language.
let MEASURED: CGFloat = .nan

func bnRect(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> BnRect {
    BnRect(x: x, y: y, w: w, h: h)
}

/// Builds a canonical frame table. Duplicate keys FAIL: two rows named the same thing means
/// one of them is silently unasserted, and the drift test would be comparing a table that is
/// not the one the simulator asserts.
///
/// `KeyValuePairs` rather than a `[String: BnRect]` literal, precisely so the duplicates are
/// still visible to be rejected — a dictionary literal would have already dropped one.
func bnFrameTable(_ entries: KeyValuePairs<String, BnRect>,
                  file: StaticString = #filePath, line: UInt = #line) -> [String: BnRect] {
    var table: [String: BnRect] = [:]
    for (key, rect) in entries {
        if table[key] != nil {
            XCTFail("duplicate frame-table key \"\(key)\" — one of the two rows is dead, and the "
                    + "drift test would be comparing a table the simulator does not assert",
                    file: file, line: line)
        }
        table[key] = rect
    }
    return table
}

/// Asserts a UIView's frame against its row of a canonical frame table.
///
/// A missing key FAILS: this is the only lookup path, so a typo must not silently assert
/// nothing. `MEASURED` dimensions are skipped by design (a font metric is not a number this
/// repo pins) — and they are skipped IDENTICALLY on Android, because the drift test compares
/// the `MEASURED` token itself.
func assertFrame(_ table: [String: BnRect], _ key: String, _ view: UIView, _ why: String = "",
                 file: StaticString = #filePath, line: UInt = #line) {
    guard let r = table[key] else {
        XCTFail("no frame named \"\(key)\" in the canonical table. The table "
                + "(BnDemoFrameTables.swift) is the ONE place a frame number is written down, and "
                + "its Android twin must declare the same key — add it to both, in the same commit",
                file: file, line: line)
        return
    }
    let what = why.isEmpty ? key : "\(key) — \(why)"
    if !r.x.isNaN {
        XCTAssertEqual(view.frame.minX, r.x, accuracy: 0.5, "\(what).x", file: file, line: line)
    }
    if !r.y.isNaN {
        XCTAssertEqual(view.frame.minY, r.y, accuracy: 0.5, "\(what).y", file: file, line: line)
    }
    if !r.w.isNaN {
        XCTAssertEqual(view.frame.width, r.w, accuracy: 0.5, "\(what).w", file: file, line: line)
    }
    if !r.h.isNaN {
        XCTAssertEqual(view.frame.height, r.h, accuracy: 0.5, "\(what).h", file: file, line: line)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BnLayoutDemo — `/layout` (M6 DoD #2). The table in BnLayoutDemo.cs's file header.
//
// The two numbers that are load-bearing and easy to undo:
//   · **wrap 3 sits at y = 40** — the line-1 cross size. Yoga's `alignContent` default is
//     `flex-start`, which DEVIATES from CSS (`stretch`), and the demo never sets it, so no
//     patch would catch a shell that "helpfully" applied the CSS default.
//   · **the wrap boxes are 90, not 100.** Four 100s in a 300 row sit exactly ON Yoga's break
//     boundary (300 > 300 is false), where half a unit of drift on either shell flips box 3
//     to line 2 and the platforms "disagree" about nothing. 3 × 90 = 270.
//
// The text row and the back row hold a MEASURED height: a font's metrics are not a constant
// anyone gets to invent, and pinning one would be pinning the simulator's font. They are LAST
// in the column precisely so a font-dependent height cannot shift anything the parity table
// rests on — and they are asserted by ORACLE instead (assertOracle).
// ─────────────────────────────────────────────────────────────────────────────
// BN-FRAME-TABLE BnLayoutDemo
let bnLayoutDemoFrames: [String: BnRect] = bnFrameTable([
    "row section": bnRect(0, 0, 300, 100),
    "row box A": bnRect(0, 0, 50, 100),
    "row box B": bnRect(50, 0, 200, 100),
    "row box C": bnRect(250, 0, 50, 100),
    "column section": bnRect(0, 100, 300, 200),
    "column item 0": bnRect(0, 0, 100, 40),
    "column item 1": bnRect(100, 80, 100, 40),
    "column item 2": bnRect(0, 160, 100, 40),
    "wrap section": bnRect(0, 300, 300, 100),
    "wrap 0": bnRect(0, 0, 90, 40),
    "wrap 1": bnRect(90, 0, 90, 40),
    "wrap 2": bnRect(180, 0, 90, 40),
    "wrap 3": bnRect(0, 40, 90, 40),
    "text row": bnRect(0, 400, 150, MEASURED),
    "back row": bnRect(0, MEASURED, 300, MEASURED),
])
// BN-FRAME-TABLE-END

// ─────────────────────────────────────────────────────────────────────────────
// BnScrollDemo — `/scroll` (M6 DoD #4). The table in BnScrollDemo.cs's file header.
//
// `content` is the SYNTHETIC content node — never on the wire — and its 800 is the COMPUTED
// HEIGHT OF A YOGA NODE (ten 80-high rows in a height:auto column), not a shell-side union of
// child frames. The back row sits at y = 200, OUTSIDE the viewport: a page whose only exit can
// scroll off the screen is not a page with an exit.
//
// BnScrollDemoImageTests asserts THIS SAME TABLE with the row image LOADED, and
// BnScrollDemoTests asserts it with the row image FAILED. Both consume this declaration, so
// the two halves of that proof cannot silently diverge either.
// ─────────────────────────────────────────────────────────────────────────────
// BN-FRAME-TABLE BnScrollDemo
let bnScrollDemoFrames: [String: BnRect] = bnFrameTable([
    "viewport": bnRect(0, 0, 300, 200),
    "content": bnRect(0, 0, 300, 800),
    "row 0": bnRect(0, 0, 300, 80),
    "row 1": bnRect(0, 80, 300, 80),
    "row 2": bnRect(0, 160, 300, 80),
    "row 3": bnRect(0, 240, 300, 80),
    "row 4": bnRect(0, 320, 300, 80),
    "row 5": bnRect(0, 400, 300, 80),
    "row 6": bnRect(0, 480, 300, 80),
    "row 7": bnRect(0, 560, 300, 80),
    "row 8": bnRect(0, 640, 300, 80),
    "row 9": bnRect(0, 720, 300, 80),
    "nested flex row": bnRect(0, 0, 300, 80),
    "nested box A": bnRect(0, 0, 50, 80),
    "nested box B": bnRect(50, 0, 200, 80),
    "nested box C": bnRect(250, 0, 50, 80),
    "back row": bnRect(0, 200, 300, MEASURED),
])
// BN-FRAME-TABLE-END

// ─────────────────────────────────────────────────────────────────────────────
// BnScrollDemo, the image inside it (6.3 non-negotiable #2).
//
// Both axes are DECLARED, so Yoga never calls the image's measure func — the bytes cannot
// move a frame even in principle, and the fixture's own natural size is nowhere in this
// answer. Everything ELSE this page asserts is `bnScrollDemoFrames`, unchanged.
// ─────────────────────────────────────────────────────────────────────────────
// BN-FRAME-TABLE BnScrollDemo/Image
let bnScrollDemoImageFrames: [String: BnRect] = bnFrameTable([
    "row image": bnRect(0, 0, 40, 40),
])
// BN-FRAME-TABLE-END

// ─────────────────────────────────────────────────────────────────────────────
// BnImageDemo — `/image` (M6 DoD #5), BEFORE the bytes land.
//
// Observable at all only because the fixture server HOLDS every response until the test has
// read the table. The load-bearing row is `[1] band I` at **y = 0**: the reflow has not
// happened. (The band is a 20pt view with both axes explicit, so it cannot move for its own
// reasons — which is what makes it an honest witness.)
// ─────────────────────────────────────────────────────────────────────────────
// BN-FRAME-TABLE BnImageDemo/Before
let bnImageDemoBeforeFrames: [String: BnRect] = bnFrameTable([
    "[0] fixed section": bnRect(0, 0, 300, 140),
    "[0] fixed image": bnRect(0, 0, 200, 120),
    "[0] band F": bnRect(0, 120, 300, 20),
    "[1] intrinsic section": bnRect(0, 140, 300, 20),
    "[1] intrinsic image": bnRect(0, 0, 0, 0),
    "[1] band I": bnRect(0, 0, 300, 20),
    "[2] failing section": bnRect(0, 160, 300, 20),
    "[2] failing image": bnRect(0, 0, 0, 0),
    "[2] band X": bnRect(0, 0, 300, 20),
    "[3] back section": bnRect(0, 180, 300, MEASURED),
])
// BN-FRAME-TABLE-END

/// BnImageDemo — `/image` (M6 DoD #5), **AFTER** the bytes land. The difference between this
/// table and `bnImageDemoBeforeFrames` *is* Phase 6.3.
///
/// `wi × hi` is the intrinsic fixture's natural PIXEL size, read at run time off the DECODED
/// bytes — never a constant either shell invents. **One file pixel is one dp/pt** is the only
/// reading under which iOS and Android compute the same frame, and it is what the
/// fixture-server drift pins in `BnImageDemoTests.cs` enforce.
///
/// The three rows that carry the phase:
///   · `[1] intrinsic image` → the natural size (0 × 0 before);
///   · `[1] band I` → **y: 0 → hi**. THE REFLOW PROOF. The image's own frame could be faked by
///     a shell that painted and never re-solved; the band's y could not.
///   · `[2] failing section` → slid down by hi, while `[2] band X` INSIDE it stayed at y = 0:
///     the failure reserved NOTHING.
/// `[0]` is unchanged, row for row: both its axes are definite, so Yoga never called its
/// measure func at all.
// BN-FRAME-TABLE BnImageDemo/After
func bnImageDemoAfterFrames(wi: CGFloat, hi: CGFloat) -> [String: BnRect] {
    bnFrameTable([
        "[0] fixed section": bnRect(0, 0, 300, 140),
        "[0] fixed image": bnRect(0, 0, 200, 120),
        "[0] band F": bnRect(0, 120, 300, 20),
        "[1] intrinsic section": bnRect(0, 140, 300, hi + 20),
        "[1] intrinsic image": bnRect(0, 0, wi, hi),
        "[1] band I": bnRect(0, hi, 300, 20),
        "[2] failing section": bnRect(0, 160 + hi, 300, 20),
        "[2] failing image": bnRect(0, 0, 0, 0),
        "[2] band X": bnRect(0, 0, 300, 20),
        "[3] back section": bnRect(0, 180 + hi, 300, MEASURED),
    ])
}
// BN-FRAME-TABLE-END
