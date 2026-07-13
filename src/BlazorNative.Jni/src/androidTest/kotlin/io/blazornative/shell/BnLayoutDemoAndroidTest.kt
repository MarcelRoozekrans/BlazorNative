package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
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
 * **`src/BlazorNative.Components/BnLayoutDemo.cs`'s file header**, derived from
 * the .NET patch golden (`BnLayoutDemoTests.cs`).
 *
 * **The iOS XCTest (Gate 3) asserts THE SAME NUMBERS on the simulator.** That
 * pairing — not either test alone — is what makes "lays out identically on both
 * platforms" an asserted result rather than a claim, and it only works because
 * Yoga computes in density-independent units on both (iOS points map 1:1;
 * Android multiplies by `density` at frame-apply time, the one conversion site).
 * So every expectation below is in **dp**, read back as `view.left / density`.
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
 * The text leaf and the "← Back" button are asserted **RELATIONALLY** (H > 0; the
 * row's height EQUALS the leaf's measured height; the back row starts where the
 * text row ended). Deliberately: a font's metrics are not a constant anyone gets
 * to invent, and pinning a number there would be pinning the AVD's font. They sit
 * at the END of the column precisely so a font-dependent height cannot shift
 * anything above them — every frame the parity assertion rests on is fixed.
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
                val d = act.resources.displayMetrics.density
                val root = host.getChildAt(0) as ViewGroup

                // The root BnColumn fills the host's width (Yoga's default
                // alignItems: stretch) and stacks its five sections.
                assertEquals("the root column starts at the host's origin", 0, root.left)
                assertEquals(0, root.top)
                assertEquals("the root column must fill the host's width", host.width, root.width)
                assertEquals("the demo has five sections", 5, root.childCount)

                // ── [0] the row: fixed 50 · flexGrow:1 · fixed 50 ────────────
                val row = root.getChildAt(0) as ViewGroup
                assertFrame(d, "row section", row, 0f, 0f, 300f, 100f)
                assertFrame(d, "box A (W=50, cross-stretched)", row.getChildAt(0), 0f, 0f, 50f, 100f)
                assertFrame(d, "box B (Grow=1 absorbs 300-50-50)", row.getChildAt(1), 50f, 0f, 200f, 100f)
                assertFrame(d, "box C (W=50)", row.getChildAt(2), 250f, 0f, 50f, 100f)

                // ── [1] the column: space-between + one alignSelf:center ─────
                // free = 200 − 3×40 = 80, split into two 40dp gaps → y 0/80/160.
                val col = root.getChildAt(1) as ViewGroup
                assertFrame(d, "column section", col, 0f, 100f, 300f, 200f)
                assertFrame(d, "item 0", col.getChildAt(0), 0f, 0f, 100f, 40f)
                assertFrame(d, "item 1 (AlignSelf=Center → x = (300-100)/2)",
                    col.getChildAt(1), 100f, 80f, 100f, 40f)
                assertFrame(d, "item 2", col.getChildAt(2), 0f, 160f, 100f, 40f)

                // ── [2] the wrap row: 3 × 90 on line 1, the 4th onto line 2 ───
                val wrap = root.getChildAt(2) as ViewGroup
                assertFrame(d, "wrap section", wrap, 0f, 300f, 300f, 100f)
                assertFrame(d, "wrap 0", wrap.getChildAt(0), 0f, 0f, 90f, 40f)
                assertFrame(d, "wrap 1", wrap.getChildAt(1), 90f, 0f, 90f, 40f)
                assertFrame(d, "wrap 2 (270 of 300 consumed — it still fits)",
                    wrap.getChildAt(2), 180f, 0f, 90f, 40f)
                assertFrame(d, "wrap 3 — line 2, at y = 40 BECAUSE Yoga's alignContent " +
                    "defaults to flex-start (CSS says stretch; do not 'correct' it)",
                    wrap.getChildAt(3), 0f, 40f, 90f, 40f)

                // ── [3] the text row: the DoD #3 proof, asserted RELATIONALLY ─
                val textRow = root.getChildAt(3) as ViewGroup
                val label = textRow.getChildAt(0) as TextView
                assertEquals("the text row starts where the wrap row ended", wrap.bottom, textRow.top)
                assertEquals("the text row is 150dp wide", 150f, textRow.width / d, 0.5f)
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

                // ── [4] the back row: a second MEASURED leaf ─────────────────
                val backRow = root.getChildAt(4) as ViewGroup
                val back = backRow.getChildAt(0) as Button
                assertEquals("the back row starts where the text row ended (y = 400 + H)",
                    textRow.bottom, backRow.top)
                assertEquals("the back row is 300dp wide", 300f, backRow.width / d, 0.5f)
                assertEquals("← Back", back.text.toString())
                assertTrue("the button must have a MEASURED height", back.height > 0)
                assertTrue("the button must have a MEASURED width", back.width > 0)
                assertEquals("the back row declares no height and hugs the button's measured height",
                    back.height, backRow.height)
                assertEquals("the button starts at the row's origin", 0, back.left)
                assertEquals(0, back.top)
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /** Every frame in dp, with the 0.5dp tolerance a whole-pixel frame-apply
     * needs (the AVD is density 2.625 — 300dp is 787.5px). */
    private fun assertFrame(d: Float, what: String, v: View, x: Float, y: Float, w: Float, h: Float) {
        assertEquals("$what.x", x, v.left / d, 0.5f)
        assertEquals("$what.y", y, v.top / d, 0.5f)
        assertEquals("$what.w", w, v.width / d, 0.5f)
        assertEquals("$what.h", h, v.height / d, 0.5f)
    }

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
