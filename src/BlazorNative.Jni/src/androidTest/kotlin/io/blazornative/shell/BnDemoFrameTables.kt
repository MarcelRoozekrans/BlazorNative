package io.blazornative.shell

/**
 * **THE CANONICAL FRAME TABLES — the Android shell's declaration** (M6 audit, F2).
 *
 * Every frame number the instrumented demo suites assert lives HERE, and nowhere else.
 * `src/BlazorNative.Apple/BnHostTests/BnDemoFrameTables.swift` is the iOS twin, and
 * **`tests/BlazorNative.Renderer.Tests/ShellFrameTableDriftTests.cs` — in the REQUIRED
 * `build-test` lane — parses both files and demands they be equal, table for table, key
 * for key, number for number.**
 *
 * ── WHY THIS FILE EXISTS ─────────────────────────────────────────────────────────
 *
 * "The same frames on both platforms" is not a nice property of M6; it *is* M6. And
 * until the audit these tables were **hand-transcribed literals** scattered through
 * `BnLayoutDemoAndroidTest` / `BnScrollDemoAndroidTest` / `BnImageDemoAndroidTest` and
 * their three Swift twins. They agreed — the auditor checked every number — but they
 * agreed *by careful transcription*, not by an invariant. Nothing in the repo would
 * have noticed one shell's `90` becoming `100` while the other's stayed. It was the
 * last cross-shell contract here that nothing checked, standing next to the style
 * tables, the fixture pixel sizes, the 72-byte bridge struct and the Yoga version pin —
 * all of which ARE pinned, all by a drift test in the required lane, all for exactly
 * this reason.
 *
 * ── THE RULE THIS FILE OBEYS ─────────────────────────────────────────────────────
 *
 * **The parse target IS the assertion.** The device tests do not restate these numbers;
 * they look them up (`assertFrame(bnLayoutDemoFrames, "wrap 3", view)`), and a missing
 * key fails. So the table the .NET drift test compares cannot drift away from the table
 * the AVD asserts — which is the failure mode a regex over the old inline literals would
 * have had, and the reason those literals were not simply parsed where they lay.
 *
 * ── THE GRAMMAR ──────────────────────────────────────────────────────────────────
 *
 * A cell is a sum of terms; a term is a **literal number**, `wi`, `hi`, or [MEASURED].
 * Nothing else — not a named constant, not an expression. A literal is the only thing
 * two languages can be compared on, and any indirection is somewhere a number could
 * hide. (`wi`/`hi` are the intrinsic fixture's natural pixel size, read at run time off
 * the DECODED bytes on both shells — never transcribed, which is what pins "no
 * downsampling". The .NET drift test compares them as SYMBOLS, so `hi + 20` on one shell
 * and `hi + 20` on the other is an equality it can check without knowing the number.
 * [MEASURED] is the same trick for a font metric.)
 *
 * The arithmetic these literals came from is not lost. It stays asserted where it always
 * was: in the .NET goldens (`BnScrollDemoTests.TheContentSizeIsTheContractsArithmetic`
 * — `RowCount × RowHeightDp == 800`, `800 − ViewportHeightDp == 600`), and in the
 * device tests' own derived assertions, which are untouched.
 *
 * Bounded by `// BN-FRAME-TABLE <name>` … `// BN-FRAME-TABLE-END` markers — the parser's
 * anchors. A table without an END marker fails the drift test loudly rather than being
 * silently half-read.
 */

// ─────────────────────────────────────────────────────────────────────────────
// BnLayoutDemo — `/layout` (M6 DoD #2). The table in BnLayoutDemo.razor's file header.
//
// The two numbers that are load-bearing and easy to undo:
//   · **wrap 3 sits at y = 40** — the line-1 cross size. Yoga's `alignContent` default
//     is `flex-start`, which DEVIATES from CSS (`stretch`), and the demo never sets it,
//     so no patch would catch a shell that "helpfully" applied the CSS default.
//   · **the wrap boxes are 90, not 100.** Four 100s in a 300 row sit exactly ON Yoga's
//     break boundary (300 > 300 is false), where half a unit of rounding on either shell
//     flips box 3 to line 2 and the platforms "disagree" about nothing. 3 × 90 = 270.
//
// The text row and the back row hold a MEASURED height: a font's metrics are not a
// constant anyone gets to invent, and pinning one would be pinning the AVD's font. They
// are LAST in the column precisely so a font-dependent height cannot shift anything the
// parity table rests on — and they are asserted by ORACLE instead (assertOracle).
//
// The `parity row` (font parity Gate C, #126) is the exception that is NOT a bare
// MEASURED forever: it is a SINGLE-LINE text leaf at an EXPLICIT shared fontSize (20),
// so — with the bundled Inter and Android's normalized line box (includeFontPadding =
// false) — its per-line height is the SAME integer on both shells. Its height is written
// MEASURED here only until CI reports it; the controller then replaces that 4th arg with
// the shared literal H (un-skipping the cell). It sits LAST for the same reason: a
// measured height must shift nothing above it.
// ─────────────────────────────────────────────────────────────────────────────
// BN-FRAME-TABLE BnLayoutDemo
internal val bnLayoutDemoFrames: Map<String, BnRect> = bnFrameTable(
    "row section" to bnRect(0f, 0f, 300f, 100f),
    "row box A" to bnRect(0f, 0f, 50f, 100f),
    "row box B" to bnRect(50f, 0f, 200f, 100f),
    "row box C" to bnRect(250f, 0f, 50f, 100f),
    "column section" to bnRect(0f, 100f, 300f, 200f),
    "column item 0" to bnRect(0f, 0f, 100f, 40f),
    "column item 1" to bnRect(100f, 80f, 100f, 40f),
    "column item 2" to bnRect(0f, 160f, 100f, 40f),
    "wrap section" to bnRect(0f, 300f, 300f, 100f),
    "wrap 0" to bnRect(0f, 0f, 90f, 40f),
    "wrap 1" to bnRect(90f, 0f, 90f, 40f),
    "wrap 2" to bnRect(180f, 0f, 90f, 40f),
    "wrap 3" to bnRect(0f, 40f, 90f, 40f),
    "text row" to bnRect(0f, 400f, 150f, MEASURED),
    "back row" to bnRect(0f, MEASURED, 300f, MEASURED),
    // Font parity Gate C (#126): single-line Inter leaf at fontSize 20. Height is now a
    // SHARED LITERAL (24.333dp) — the payoff: both shells render one bundled font at one
    // explicit size, so the height that used to be MEASURED (SF Pro vs Roboto differed) is
    // an asserted number equal across shells (iOS measured 24.333pt; Android within the
    // 0.5dp frame tolerance). y stays MEASURED — the row sits below the native ← Back
    // button, whose chrome height is platform-variant. Keep identical to the Swift twin.
    "parity row" to bnRect(0f, MEASURED, 300f, 24.333f),
)
// BN-FRAME-TABLE-END

// ─────────────────────────────────────────────────────────────────────────────
// BnScrollDemo — `/scroll` (M6 DoD #4). The table in BnScrollDemo.razor's file header.
//
// `content` is the SYNTHETIC content node — never on the wire — and its 800 is the
// COMPUTED HEIGHT OF A YOGA NODE (ten 80-high rows in a height:auto column), not a
// shell-side union of child frames. The back row sits at y = 200, OUTSIDE the viewport:
// a page whose only exit can scroll off the screen is not a page with an exit.
//
// BnScrollDemoImage asserts THIS SAME TABLE with the row image LOADED, and
// BnScrollDemoAndroidTest asserts it with the row image FAILED. Both consume this
// declaration, so the two halves of that proof cannot silently diverge either.
// ─────────────────────────────────────────────────────────────────────────────
// BN-FRAME-TABLE BnScrollDemo
internal val bnScrollDemoFrames: Map<String, BnRect> = bnFrameTable(
    "viewport" to bnRect(0f, 0f, 300f, 200f),
    "content" to bnRect(0f, 0f, 300f, 800f),
    "row 0" to bnRect(0f, 0f, 300f, 80f),
    "row 1" to bnRect(0f, 80f, 300f, 80f),
    "row 2" to bnRect(0f, 160f, 300f, 80f),
    "row 3" to bnRect(0f, 240f, 300f, 80f),
    "row 4" to bnRect(0f, 320f, 300f, 80f),
    "row 5" to bnRect(0f, 400f, 300f, 80f),
    "row 6" to bnRect(0f, 480f, 300f, 80f),
    "row 7" to bnRect(0f, 560f, 300f, 80f),
    "row 8" to bnRect(0f, 640f, 300f, 80f),
    "row 9" to bnRect(0f, 720f, 300f, 80f),
    "nested flex row" to bnRect(0f, 0f, 300f, 80f),
    "nested box A" to bnRect(0f, 0f, 50f, 80f),
    "nested box B" to bnRect(50f, 0f, 200f, 80f),
    "nested box C" to bnRect(250f, 0f, 50f, 80f),
    "back row" to bnRect(0f, 200f, 300f, MEASURED),
)
// BN-FRAME-TABLE-END

// ─────────────────────────────────────────────────────────────────────────────
// BnScrollDemo, the image inside it (6.3 non-negotiable #2).
//
// Both axes are DECLARED, so Yoga never calls the image's measure func — the bytes
// cannot move a frame even in principle, and the fixture's own natural size is nowhere
// in this answer. Everything ELSE this page asserts is `bnScrollDemoFrames`, unchanged.
// ─────────────────────────────────────────────────────────────────────────────
// BN-FRAME-TABLE BnScrollDemo/Image
internal val bnScrollDemoImageFrames: Map<String, BnRect> = bnFrameTable(
    "row image" to bnRect(0f, 0f, 40f, 40f),
)
// BN-FRAME-TABLE-END

// ─────────────────────────────────────────────────────────────────────────────
// BnImageDemo — `/image` (M6 DoD #5), BEFORE the bytes land.
//
// Observable at all only because the fixture server HOLDS every response until the test
// has read the table. The load-bearing row is `[1] band I` at **y = 0**: the reflow has
// not happened. (The band is a 20dp view with both axes explicit, so it cannot move for
// its own reasons — which is what makes it an honest witness.)
// ─────────────────────────────────────────────────────────────────────────────
// BN-FRAME-TABLE BnImageDemo/Before
internal val bnImageDemoBeforeFrames: Map<String, BnRect> = bnFrameTable(
    "[0] fixed section" to bnRect(0f, 0f, 300f, 140f),
    "[0] fixed image" to bnRect(0f, 0f, 200f, 120f),
    "[0] band F" to bnRect(0f, 120f, 300f, 20f),
    "[1] intrinsic section" to bnRect(0f, 140f, 300f, 20f),
    "[1] intrinsic image" to bnRect(0f, 0f, 0f, 0f),
    "[1] band I" to bnRect(0f, 0f, 300f, 20f),
    "[2] failing section" to bnRect(0f, 160f, 300f, 20f),
    "[2] failing image" to bnRect(0f, 0f, 0f, 0f),
    "[2] band X" to bnRect(0f, 0f, 300f, 20f),
    "[3] back section" to bnRect(0f, 180f, 300f, MEASURED),
)
// BN-FRAME-TABLE-END

/**
 * BnImageDemo — `/image` (M6 DoD #5), **AFTER** the bytes land. The difference between
 * this table and [bnImageDemoBeforeFrames] *is* Phase 6.3.
 *
 * `wi × hi` is the intrinsic fixture's natural PIXEL size, read at run time off the
 * DECODED bytes — never a constant either shell invents. **One file pixel is one dp/pt**
 * is the only reading under which Android and iOS compute the same frame, and it is what
 * the fixture-server drift pins in `BnImageDemoTests.cs` enforce.
 *
 * The three rows that carry the phase:
 *   · `[1] intrinsic image` → the natural size (0 × 0 before);
 *   · `[1] band I` → **y: 0 → hi**. THE REFLOW PROOF. The image's own frame could be
 *     faked by a shell that painted and never re-solved; the band's y could not.
 *   · `[2] failing section` → slid down by hi, while `[2] band X` INSIDE it stayed at
 *     y = 0: the failure reserved NOTHING.
 * `[0]` is unchanged, row for row: both its axes are definite, so Yoga never called its
 * measure func at all.
 */
// BN-FRAME-TABLE BnImageDemo/After
internal fun bnImageDemoAfterFrames(wi: Float, hi: Float): Map<String, BnRect> = bnFrameTable(
    "[0] fixed section" to bnRect(0f, 0f, 300f, 140f),
    "[0] fixed image" to bnRect(0f, 0f, 200f, 120f),
    "[0] band F" to bnRect(0f, 120f, 300f, 20f),
    "[1] intrinsic section" to bnRect(0f, 140f, 300f, hi + 20f),
    "[1] intrinsic image" to bnRect(0f, 0f, wi, hi),
    "[1] band I" to bnRect(0f, hi, 300f, 20f),
    "[2] failing section" to bnRect(0f, 160f + hi, 300f, 20f),
    "[2] failing image" to bnRect(0f, 0f, 0f, 0f),
    "[2] band X" to bnRect(0f, 0f, 300f, 20f),
    "[3] back section" to bnRect(0f, 180f + hi, 300f, MEASURED),
)
// BN-FRAME-TABLE-END
