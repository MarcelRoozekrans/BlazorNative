package io.blazornative.shell

import android.content.Intent
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 6.1 Task 2.5 — **THE FRAME TABLE, ON THE DEVICE** (M6 DoD #2).
 *
 * Mounts BnLayoutDemo (the `/layout` page) through the real NativeAOT boot and
 * asserts EVERY frame of the canonical table against the real Views, after the
 * real layout pass. The table is not invented here: it is the one in
 * **`src/BlazorNative.Components/BnLayoutDemo.razor`'s file header**, derived from
 * the .NET patch golden (`BnLayoutDemoTests.cs`).
 *
 * **The iOS XCTest (Gate 3) asserts THE SAME NUMBERS on the simulator.** That
 * pairing — not either test alone — is what makes "lays out identically on both
 * platforms" an asserted result rather than a claim, and it only works because
 * Yoga computes in density-independent units on both (iOS points map 1:1;
 * Android multiplies by `density` at frame-apply time, the one conversion site).
 * So every expectation below is in **dp**, read back as `view.left / density`.
 *
 * **And since the M6 audit (F2), "the same numbers" is an INVARIANT rather than a
 * discipline.** This file writes down no frame number at all: it consumes
 * [bnLayoutDemoFrames] (BnDemoFrameTables.kt), whose iOS twin
 * `BnDemoFrameTables.swift` declares the same table — and `ShellFrameTableDriftTests`,
 * in the REQUIRED lane, demands the two be equal key for key and number for number.
 * Before that, the two shells agreed because someone transcribed carefully. Nothing
 * would have caught one shell's `90` becoming `100` while the other's stayed.
 *
 * ── THE TWO NUMBERS THAT ARE LOAD-BEARING AND EASY TO UNDO ───────────────────
 *
 * 1. **wrap 3 sits at y = 40** — the line-1 cross size. Yoga's `alignContent`
 *    default is `flex-start`, which **DEVIATES from CSS** (`stretch`), and the
 *    demo does not set `alignContent` at all (it is not even on the allow-list),
 *    so NO PATCH would catch a shell that "helpfully" applied the CSS default.
 *    It is asserted here and nowhere else.
 * 2. **the wrap boxes are 90dp, not 100.** Four 100s in a 300 row would sit
 *    exactly ON Yoga's break boundary (`consumed + item > available`; 300 > 300
 *    is false), where half a dp of rounding on either shell flips box 3 to line 2
 *    and the platforms "disagree" for a reason that has nothing to do with the
 *    engine. 3 × 90 = 270 leaves 30dp of slack; the break is a fact.
 *
 * ── THE TWO FRAMES THAT ARE *NOT* NUMBERS ────────────────────────────────────
 *
 * The text leaf and the "← Back" button carry no pinned pixel count: a font's
 * metrics are not a constant anyone gets to invent, and pinning one would be
 * pinning the AVD's font. They sit at the END of the column precisely so a
 * font-dependent height cannot shift anything above them — every frame the parity
 * assertion rests on is fixed.
 *
 * They are asserted **RELATIONALLY** (H > 0; the row's height EQUALS the leaf's
 * measured height; the back row starts where the text row ended) **and by ORACLE**
 * — and the oracle is the one that carries the DoD #3 claim. Every relational
 * assertion here also passes with a FABRICATED measure function: run the 6.0
 * spike's constant 80×20 stub through them and all of them stay green, so the
 * measurement could be entirely invented and this file — the file Gate 3 mirrors —
 * would not notice. [assertOracle] measures the same widget, with the same text and
 * font, under the same spec the measure func hands Yoga, and demands the laid-out
 * frame EQUAL it. Still no font metric written down; no room left to invent one.
 *
 * ── AND ONE FRAME THAT IS A *NEGATIVE* ───────────────────────────────────────
 *
 * The root column **hugs its five sections** rather than filling the host. That is
 * the assertion that catches a stock-`FrameLayout` host root re-laying out every
 * TOP-LEVEL node behind Yoga's back (see [BnYogaFrameLayout]) — a bug invisible to
 * every frame above, because with a single full-width top-level node the framework
 * and Yoga happen to agree on x, y and width. They do not agree on HEIGHT.
 */
@RunWith(AndroidJUnit4::class)
class BnLayoutDemoAndroidTest {

    @Test
    fun layout_demo_matches_the_canonical_frame_table() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnLayoutDemo")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            val demo = pollForDemo(scenario)
            assertTrue("BnLayoutDemo never rendered a laid-out tree within 60s", demo)

            scenario.onActivity { act ->
                val host = act.findViewById<FrameLayout>(R.id.widget_root)
                val root = host.getChildAt(0) as ViewGroup

                // THE CANONICAL TABLE — declared in BnDemoFrameTables.kt, and pinned
                // against the iOS shell's own declaration by ShellFrameTableDriftTests in
                // the REQUIRED lane. No frame number is written down in this file any more:
                // that is the F2 fix. A number that appears here and not there (or moves on
                // one shell and not the other) now reddens a required check.
                val f = bnLayoutDemoFrames

                // The root BnColumn fills the host's width (Yoga's default
                // alignItems: stretch) and stacks its five sections.
                assertEquals("the root column starts at the host's origin", 0, root.left)
                assertEquals(0, root.top)
                assertEquals("the root column must fill the host's width", host.width, root.width)
                assertEquals("the demo has five sections", 5, root.childCount)

                // ── [0] the row: fixed 50 · flexGrow:1 · fixed 50 ────────────
                val row = root.getChildAt(0) as ViewGroup
                assertFrame(f, "row section", row)
                assertFrame(f, "row box A", row.getChildAt(0), "W=50, cross-stretched")
                assertFrame(f, "row box B", row.getChildAt(1), "Grow=1 absorbs 300-50-50")
                assertFrame(f, "row box C", row.getChildAt(2), "W=50")

                // ── [1] the column: space-between + one alignSelf:center ─────
                // free = 200 − 3×40 = 80, split into two 40dp gaps → y 0/80/160.
                val col = root.getChildAt(1) as ViewGroup
                assertFrame(f, "column section", col)
                assertFrame(f, "column item 0", col.getChildAt(0))
                assertFrame(f, "column item 1", col.getChildAt(1),
                    "AlignSelf=Center → x = (300-100)/2")
                assertFrame(f, "column item 2", col.getChildAt(2))

                // ── [2] the wrap row: 3 × 90 on line 1, the 4th onto line 2 ───
                val wrap = root.getChildAt(2) as ViewGroup
                assertFrame(f, "wrap section", wrap)
                assertFrame(f, "wrap 0", wrap.getChildAt(0))
                assertFrame(f, "wrap 1", wrap.getChildAt(1))
                assertFrame(f, "wrap 2", wrap.getChildAt(2), "270 of 300 consumed — it still fits")
                assertFrame(f, "wrap 3", wrap.getChildAt(3),
                    "line 2, at y = 40 BECAUSE Yoga's alignContent defaults to flex-start " +
                        "(CSS says stretch; do not 'correct' it)")

                // ── [3] the text row: the DoD #3 proof ───────────────────────
                val textRow = root.getChildAt(3) as ViewGroup
                val label = textRow.getChildAt(0) as TextView
                // x = 0, y = 400 (the wrap row ended there), w = 150 — from the table. Its
                // HEIGHT is MEASURED and therefore absent from the table: a font's metrics
                // are not a constant anyone gets to invent. It is pinned below, relationally
                // and by oracle.
                assertFrame(f, "text row", textRow)
                assertEquals("the text row starts where the wrap row ended", wrap.bottom, textRow.top)
                assertTrue("the label must have a MEASURED height (> 0)", label.height > 0)
                assertTrue("the label must have WRAPPED inside 150dp — that is the measurement " +
                    "reaching Yoga (lines=${label.lineCount})", label.lineCount > 1)
                assertEquals("THE DoD #3 CLAIM: the row declares no height and hugs the label's " +
                    "MEASURED height", label.height, textRow.height)
                assertEquals("the label starts at the row's origin", 0, label.left)
                assertEquals(0, label.top)
                assertTrue("the label must not overflow the 150dp row it was measured inside " +
                    "(label=${label.width}px, row=${textRow.width}px)",
                    label.width <= textRow.width)
                // …AND THE ORACLE. Every assertion above passes with a FABRICATED
                // measure function (feed them the 6.0 spike's constant 80×20 stub and
                // all of them stay green), which would leave the DoD #3 half of this
                // table — and the file Gate 3 mirrors — asserting nothing about the
                // measurement. See assertOracle: same widget, same text, same spec the
                // measure func hands Yoga, no font metric written down anywhere.
                assertOracle("the measured label", label, availableWidthPx = textRow.width)

                // ── [4] the back row: a second MEASURED leaf ─────────────────
                val backRow = root.getChildAt(4) as ViewGroup
                val back = backRow.getChildAt(0) as Button
                // x = 0, w = 300 from the table; its y AND height are MEASURED (both depend
                // on the text row's font-dependent height above it), so both are declared
                // MEASURED in the table and pinned relationally + by oracle here.
                assertFrame(f, "back row", backRow)
                assertEquals("the back row starts where the text row ended (y = 400 + H)",
                    textRow.bottom, backRow.top)
                assertEquals("← Back", back.text.toString())
                assertTrue("the button must have a MEASURED height", back.height > 0)
                assertTrue("the button must have a MEASURED width", back.width > 0)
                assertEquals("the back row declares no height and hugs the button's measured height",
                    back.height, backRow.height)
                assertEquals("the button starts at the row's origin", 0, back.left)
                assertEquals(0, back.top)
                assertOracle("the measured button", back, availableWidthPx = backRow.width)

                // ── THE ROOT COLUMN HUGS ITS CONTENT ─────────────────────────
                // The root BnColumn declares no height, so Yoga sizes it to the sum
                // of its five sections — and NOTHING ELSE may size it. This is the
                // assertion that catches a stock-FrameLayout host root: `addView`/
                // `setText` inside a batch call requestLayout(), and the framework
                // traversal that follows would re-measure the host and re-place every
                // TOP-LEVEL child at (0, 0, hostW, hostH) behind Yoga's back — which
                // the resize listener's bounds guard cannot see and nothing repairs.
                // The host is a BnYogaFrameLayout (main.xml) precisely so it cannot.
                assertEquals("the root column must HUG its five sections, not fill the host",
                    backRow.bottom, root.height)
                assertNotEquals("…and that height must not COINCIDE with the host's, or the " +
                    "assertion above proves nothing on this device (host=${host.height}px, " +
                    "content=${root.height}px)",
                    host.height, root.height)
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // assertFrame / assertOracle live in FrameAssertions.kt (shared with the
    // synthetic-frame tests — the dp contract and its 0.5dp whole-pixel tolerance
    // are stated once).

    /** Polls until the mount frame has been applied AND laid out: five sections
     * under the root column, and the root actually has a computed height. */
    private fun pollForDemo(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long = 60_000,
    ): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)
                    ?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup
                ready.set(root != null && root.childCount == 5 && root.height > 0)
            }
            if (ready.get()) break
            Thread.sleep(250)
        }
        return ready.get()
    }
}
