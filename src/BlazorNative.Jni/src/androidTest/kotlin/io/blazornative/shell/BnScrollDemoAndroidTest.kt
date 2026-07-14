package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ScrollView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 6.2 Gate 2 Task 2.3 — **THE SCROLL DEMO, ON THE DEVICE** (M6 DoD #4).
 *
 * Mounts `BnScrollDemo` (the `/scroll` page) through the real NativeAOT boot and
 * asserts the canonical table from **`src/BlazorNative.Components/BnScrollDemo.cs`'s
 * file header** — the same discipline, and the same pairing, as
 * [BnLayoutDemoAndroidTest]: **the iOS XCTest (Gate 3) asserts THE SAME NUMBERS on
 * the simulator.** Yoga computes in density-independent units on both platforms, so
 * every expectation here is in **dp**, read back as `view.left / density`.
 *
 * | node | x | y | w | h |
 * |---|---|---|---|---|
 * | viewport (`ScrollView`) | 0 | 0 | 300 | 200 |
 * | **content** (SYNTHETIC) | 0 | 0 | 300 | **800** |
 * | row *i* | 0 | 80·*i* | 300 | 80 |
 * | row 1's flex row | 0 | 0 | 300 | 80 |
 * | box A · **box B (`Grow=1`)** · box C | 0 · **50** · 250 | 0 | 50 · **200** · 50 | 80 |
 * | back row | 0 | **200** | 300 | *measured* |
 *
 * ── THE TWO ASSERTIONS THAT ARE THE PHASE ───────────────────────────────────
 *
 * 1. **`contentSize > viewport`, FROM YOGA.** The content node is 800 tall inside a
 *    200-tall viewport, and that 800 is the *computed height of a Yoga node* — not a
 *    shell-side union of child frames (non-negotiable #3), and not a number this file
 *    transcribed: it is what ten 80-high rows in a `height: auto` column add up to.
 * 2. **The rows actually MOVE.** Numbers that add up are not a scroll. So the test
 *    drives the scroll position to its maximum and re-reads every row's position
 *    *relative to the viewport* — and the maximum itself is an assertion, because
 *    `ScrollView` derives it from the content view's laid-out height: it clamps to
 *    **600**, which is `800 − 200`, which is Yoga's content size minus the viewport,
 *    arrived at by a completely independent path.
 *
 * At offset 600 the visible window is content y **600..800**: **row 7 (560..640) is
 * PARTIALLY visible — its bottom 40dp only** — and rows 8 and 9 are fully visible.
 * Not "rows 7, 8 and 9 fill the viewport"; row 7 does not (the Gate 1 review caught
 * that claim in the design before it became an `assertFullyVisible(row7)` in two
 * shells).
 *
 * The back button is the page's only measured leaf and sits OUTSIDE the viewport, so
 * it is asserted RELATIONALLY (it starts where the viewport ends) and by ORACLE — no
 * invented font constant, same as [BnLayoutDemoAndroidTest].
 */
@RunWith(AndroidJUnit4::class)
class BnScrollDemoAndroidTest {

    private companion object {
        /** BnScrollDemo's four inputs (BnScrollDemo.cs: RowCount, RowHeightDp,
         * ViewportWidthDp, ViewportHeightDp) and the two products the contract
         * COMPUTES from them — ContentHeightDp and ScrollRangeDp. Derived here too,
         * not transcribed: a changed row height must move both sides at once. */
        const val ROWS = 10
        const val ROW_H = 80f
        const val VIEW_W = 300f
        const val VIEW_H = 200f
        const val CONTENT_H = ROWS * ROW_H          // 800
        const val SCROLL_RANGE = CONTENT_H - VIEW_H // 600
        const val FLEX_ROW = 1                      // the row hosting the nested flex row
    }

    /**
     * Every fixed frame in the table, plus the two nestings (a scroll inside flex; a
     * flex row inside a scrolled row) — and the content size, which is the phase.
     */
    @Test fun scroll_demo_matches_the_canonical_frame_table() = withDemo { act ->
        val d = act.resources.displayMetrics.density
        val host = act.findViewById<FrameLayout>(R.id.widget_root)
        val root = host.getChildAt(0) as ViewGroup

        assertEquals("the demo has two sections: the viewport and the back row", 2, root.childCount)

        // ── [0] the VIEWPORT, and the SYNTHETIC content node inside it ───────
        val scroll = root.getChildAt(0) as ScrollView
        assertFrame("the viewport", scroll, 0f, 0f, VIEW_W, VIEW_H)

        assertEquals("the ScrollView's ONLY child is the synthetic content view — the ten rows " +
            "are NOT its view children", 1, scroll.childCount)
        val content = scroll.getChildAt(0) as ViewGroup
        assertFrame("THE CONTENT SIZE: the synthetic content node's Yoga frame. 800 = ten 80-high " +
            "rows in a height:auto column, computed by Yoga and READ by the shell — never a union " +
            "of child frames", content, 0f, 0f, VIEW_W, CONTENT_H)

        assertTrue("THE PHASE: contentSize (${content.height / d}dp) must EXCEED the viewport " +
            "(${scroll.height / d}dp) — everything else here is bookkeeping",
            content.height > scroll.height)
        assertEquals("…by exactly the scrollable range, 800 − 200",
            SCROLL_RANGE, (content.height - scroll.height) / d, 0.5f)

        // ── the ten rows, inside the content node ────────────────────────────
        assertEquals("ten rows, all children of the CONTENT view", ROWS, content.childCount)
        for (i in 0 until ROWS) {
            assertFrame("row $i (no Width — stretched to the content node's 300 by Yoga's " +
                "default alignItems:stretch, which is what proves the content node spans the " +
                "viewport)", content.getChildAt(i), 0f, ROW_H * i, VIEW_W, ROW_H)
        }

        // ── FLEX NESTED INSIDE THE SCROLL (design §Verification #4) ──────────
        // Row 1 on purpose: at offset 0 it is fully visible (y 80..160 of a
        // 200-high viewport), so the nesting proof is in the FIRST screenshot.
        val flexRow = (content.getChildAt(FLEX_ROW) as ViewGroup).getChildAt(0) as ViewGroup
        assertFrame("the nested flex row (Grow=1 in row 1's definite 80dp column)",
            flexRow, 0f, 0f, VIEW_W, ROW_H)
        assertFrame("box A (W=50, cross-stretched to the row's 80)",
            flexRow.getChildAt(0), 0f, 0f, 50f, ROW_H)
        assertFrame("box B (Grow=1 absorbs 300 − 50 − 50) — the SAME 200 BnLayoutDemo's box B " +
            "computes, now two levels inside a scroll",
            flexRow.getChildAt(1), 50f, 0f, 200f, ROW_H)
        assertFrame("box C (W=50)", flexRow.getChildAt(2), 250f, 0f, 50f, ROW_H)

        // ── [1] the back row — OUTSIDE the viewport ─────────────────────────
        // A page whose only exit can scroll off the screen is not a page with an
        // exit. It is also the only MEASURED leaf here, so it is asserted
        // relationally and by oracle — no font constant is anyone's to invent.
        val backRow = root.getChildAt(1) as ViewGroup
        val back = backRow.getChildAt(0) as Button
        assertEquals("the back row starts where the VIEWPORT ends (y = 200) — outside it, not " +
            "at the bottom of the 800dp of content", scroll.bottom, backRow.top)
        assertEquals("…at the viewport's height, in dp", VIEW_H, backRow.top / d, 0.5f)
        assertEquals("the back row is 300dp wide", VIEW_W, backRow.width / d, 0.5f)
        assertEquals("← Back", back.text.toString())
        assertEquals("the back row declares no height and hugs the button's MEASURED height",
            back.height, backRow.height)
        assertOracle("the measured back button", back, availableWidthPx = backRow.width)

        assertEquals("the root column HUGS its two sections (viewport + back row) — the pin that " +
            "catches a host root re-laying out top-level nodes behind Yoga's back",
            backRow.bottom, root.height)
    }

    /**
     * **THE LOAD-BEARING PART: THE ROWS ACTUALLY MOVE.**
     *
     * Frames that add up prove arithmetic. This drives the scroll position and re-reads
     * every row's top edge *in the viewport's coordinate space* — the number a user's
     * eye reads — and asserts the window over the content is the one the contract says.
     *
     * The maximum offset is itself an assertion of the content size, arrived at
     * independently: `ScrollView.scrollTo` clamps to `contentHeight − viewportHeight`
     * using the content view's LAID-OUT height, so a request for 10 000dp coming back
     * as exactly **600** is the ScrollView agreeing with Yoga about the 800.
     */
    @Test fun driving_the_scroll_position_moves_the_rows_over_the_viewport() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnScrollDemo")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnScrollDemo never rendered a laid-out tree within 60s", pollForDemo(scenario))
            val row9Before = AtomicReference(0)

            // ── At offset 0: the window is content y 0..200 ──────────────────
            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                val scroll = scrollOf(act)
                val content = scroll.getChildAt(0) as ViewGroup

                assertEquals("a freshly mounted scroll view sits at offset 0", 0, scroll.scrollY)
                assertVisibleWindow("at offset 0", content, scroll, d, offsetDp = 0f)
                assertTrue("rows 0 and 1 are fully INSIDE the viewport",
                    (0..1).all { bottomInViewport(content.getChildAt(it), scroll) / d <= VIEW_H })
                assertTrue("row 2 STRADDLES the bottom edge (content 160..240 against a 200 cut)",
                    topInViewport(content.getChildAt(2), scroll) / d < VIEW_H &&
                        bottomInViewport(content.getChildAt(2), scroll) / d > VIEW_H)
                assertTrue("rows 3-9 are entirely BELOW the viewport",
                    (3 until ROWS).all { topInViewport(content.getChildAt(it), scroll) / d >= VIEW_H })

                row9Before.set(screenY(content.getChildAt(9)))
            }

            // ── Drive it PAST the end; the ScrollView clamps to the real range ─
            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                scrollOf(act).scrollTo(0, (10_000 * d).toInt())
            }

            // ── At offset 600: the window is content y 600..800 ───────────────
            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                val scroll = scrollOf(act)
                val content = scroll.getChildAt(0) as ViewGroup

                assertEquals("THE MAXIMUM IS THE CONTENT SIZE, ARRIVED AT INDEPENDENTLY: " +
                    "ScrollView clamps a runaway offset to (content − viewport) using the content " +
                    "view's LAID-OUT height — so 600 here is the ScrollView agreeing with Yoga " +
                    "about the 800", SCROLL_RANGE, scroll.scrollY / d, 0.5f)

                assertEquals("THE ROWS MOVED: row 9 travelled exactly the scroll range up the " +
                    "screen. This — not the arithmetic above — is what 'it scrolls' means.",
                    -SCROLL_RANGE, (screenY(content.getChildAt(9)) - row9Before.get()) / d, 0.5f)

                assertVisibleWindow("at offset 600", content, scroll, d, offsetDp = SCROLL_RANGE)

                // Row 7 spans content y 560..640, so at offset 600 only its BOTTOM
                // 40dp is on screen. NOT "rows 7, 8 and 9 fill the viewport" — row 7
                // does not, and the Gate 1 review caught that claim in the design
                // before it became an assertFullyVisible(row7) in two shells.
                val row7 = content.getChildAt(7)
                assertEquals("row 7's top is 40dp ABOVE the viewport's top edge (560 − 600)",
                    -40f, topInViewport(row7, scroll) / d, 0.5f)
                assertEquals("…so exactly its bottom 40dp is visible — PARTIALLY, not fully",
                    40f, bottomInViewport(row7, scroll) / d, 0.5f)

                assertEquals("row 8 is FULLY visible, at viewport y 40 (content 640 − 600)",
                    40f, topInViewport(content.getChildAt(8), scroll) / d, 0.5f)
                assertEquals("row 9 is FULLY visible, at viewport y 120 (content 720 − 600)…",
                    120f, topInViewport(content.getChildAt(9), scroll) / d, 0.5f)
                assertEquals("…and its bottom edge lands exactly on the viewport's: the content " +
                    "cannot scroll further",
                    VIEW_H, bottomInViewport(content.getChildAt(9), scroll) / d, 0.5f)

                assertTrue("rows 0-6 are entirely ABOVE the viewport now",
                    (0..6).all { bottomInViewport(content.getChildAt(it), scroll) <= 0 })
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /** Every row's top edge, in the VIEWPORT's coordinate space, is `80i − offset` —
     * the single sentence both scroll assertions above are made of. Read through
     * [screenY], so it goes through the framework's own scroll accounting rather than
     * this test's arithmetic about it. */
    private fun assertVisibleWindow(
        what: String,
        content: ViewGroup,
        scroll: ScrollView,
        d: Float,
        offsetDp: Float,
    ) {
        for (i in 0 until ROWS) {
            assertEquals("$what: row $i sits at viewport y = 80×$i − $offsetDp",
                ROW_H * i - offsetDp, topInViewport(content.getChildAt(i), scroll) / d, 0.5f)
        }
    }

    private fun screenY(v: View): Int = IntArray(2).also { v.getLocationOnScreen(it) }[1]

    /** A view's top edge relative to the viewport's, in px. Goes through
     * `getLocationOnScreen`, which subtracts each ancestor's scroll offset — so this
     * is the framework's answer to "where is this row on the glass", not ours. */
    private fun topInViewport(v: View, scroll: ScrollView): Int = screenY(v) - screenY(scroll)

    private fun bottomInViewport(v: View, scroll: ScrollView): Int =
        topInViewport(v, scroll) + v.height

    private fun scrollOf(act: MainActivity): ScrollView =
        ((act.findViewById<FrameLayout>(R.id.widget_root).getChildAt(0) as ViewGroup)
            .getChildAt(0)) as ScrollView

    /** Mounts BnScrollDemo, waits for a laid-out tree, and runs [block] on the main
     * thread. Both tests want exactly this. */
    private fun withDemo(block: (MainActivity) -> Unit) {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnScrollDemo")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnScrollDemo never rendered a laid-out tree within 60s", pollForDemo(scenario))
            scenario.onActivity(block)
        }
    }

    /** Polls until the mount frame has been applied AND laid out: a viewport with its
     * ten rows under the synthetic content view, and a root with a computed height. */
    private fun pollForDemo(scenario: ActivityScenario<MainActivity>): Boolean {
        val deadline = System.currentTimeMillis() + 60_000
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)
                    ?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup
                val scroll = root?.takeIf { it.childCount == 2 }?.getChildAt(0) as? ScrollView
                val content = scroll?.takeIf { it.childCount == 1 }?.getChildAt(0) as? ViewGroup
                ready.set(content != null && content.childCount == ROWS && content.height > 0 &&
                    root.height > 0)
            }
            if (ready.get()) break
            Thread.sleep(250)
        }
        return ready.get()
    }
}
