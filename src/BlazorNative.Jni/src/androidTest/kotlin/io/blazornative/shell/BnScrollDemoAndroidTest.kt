package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.ScrollView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
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
 *
 * ── AND SINCE 6.3: THE OTHER HALF OF THE IMAGE PROOF (Gate 2 review, I5) ────
 *
 * Row 0 now holds an image. This class **stands up no fixture server**, so that image
 * **FAILS to load** — connection refused, immediately, offline — and the table above is
 * asserted UNCHANGED anyway. That is not an accident to be tidied away: it is the other
 * half of [BnScrollDemoImageAndroidTest]'s proof. Between the two classes, the 6.2 table
 * is pinned against an image that **loaded** and against one that **did not**.
 *
 * **The claim used to be false, and order-dependent.** This class asserted nothing about
 * the image's outcome and did not clear Coil's caches — so run after
 * [BnScrollDemoImageAndroidTest], `fixed.png` was a MEMORY-CACHE HIT and the image
 * **succeeded**. The class was quietly proving the same thing as its sibling, and the
 * header said otherwise. Now: the caches are cleared in `@Before`, the failure is
 * **awaited and asserted** (`ERROR`, on the URL the wire carried), and nothing was
 * painted. The claim is real, and it is true in any test order.
 */
@RunWith(AndroidJUnit4::class)
class BnScrollDemoAndroidTest {

    /**
     * **NO FIXTURE SERVER — and the caches are CLEARED so that means something.** Coil's
     * cache is process-wide and outlives an Activity, so without this the row image would
     * be served from memory whenever an image test ran first, and "a FAILED load moves
     * nothing" would silently become "a CACHED load moves nothing" — the sibling class's
     * claim, made twice, with this one's header lying about it.
     */
    @Before fun clearTheImageCaches() {
        ImageFixtureServer.clearCoilCaches()
    }

    private companion object {
        /** BnScrollDemo's four inputs (BnScrollDemo.cs: RowCount, RowHeightDp,
         * ViewportWidthDp, ViewportHeightDp) and the two products the contract
         * COMPUTES from them — ContentHeightDp and ScrollRangeDp. Derived here too,
         * not transcribed: a changed row height must move both sides at once. */
        const val ROWS = 10
        const val ROW_H = 80f
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

        // ── THE OTHER HALF OF THE IMAGE PROOF: THE ROW IMAGE *FAILED* ────────
        // No fixture server is running (this class starts none), and Coil's caches were
        // cleared — so the fetch is a REAL, immediate connection refusal, offline. The
        // whole table below is then asserted UNCHANGED, which is the claim: a FAILED load
        // moves nothing, for two independent reasons (the row's height is definite, and
        // the image's size is definite — Yoga never calls its measure func at all).
        //
        // Asserted, not assumed. Without this the class proved nothing about the image and
        // was ORDER-DEPENDENT: run after BnScrollDemoImageAndroidTest, fixed.png was a
        // memory-cache HIT and the image quietly SUCCEEDED.
        assertEquals("THE ROW IMAGE FAILED — Coil's own terminal callback says so. This class " +
            "stands up NO fixture server and clears the caches, so the fetch is a real, " +
            "immediate connection refusal. Everything below is asserted about a page whose " +
            "image DID NOT LOAD, which is the half BnScrollDemoImageAndroidTest cannot prove.",
            listOf(ImageFixtureServer.FIXED_URL to WidgetMapper.ImageOutcome.ERROR),
            act.mapper.imageResults.map { it.url to it.outcome })

        assertEquals("the demo has two sections: the viewport and the back row", 2, root.childCount)

        // THE CANONICAL TABLE — declared in BnDemoFrameTables.kt and pinned against the iOS
        // shell's own declaration by ShellFrameTableDriftTests in the REQUIRED lane (M6 audit,
        // F2). BnScrollDemoImageAndroidTest consumes the SAME table with the row image LOADED;
        // this class asserts it with the image FAILED. Two halves of one proof, one table.
        val f = bnScrollDemoFrames

        // ── [0] the VIEWPORT, and the SYNTHETIC content node inside it ───────
        val scroll = root.getChildAt(0) as ScrollView
        assertFrame(f, "viewport", scroll)

        assertEquals("the ScrollView's ONLY child is the synthetic content view — the ten rows " +
            "are NOT its view children", 1, scroll.childCount)
        val content = scroll.getChildAt(0) as ViewGroup
        assertFrame(f, "content", content,
            "THE CONTENT SIZE: the synthetic content node's Yoga frame. 800 = ten 80-high rows " +
                "in a height:auto column, computed by Yoga and READ by the shell — never a " +
                "union of child frames")

        assertTrue("THE PHASE: contentSize (${content.height / d}dp) must EXCEED the viewport " +
            "(${scroll.height / d}dp) — everything else here is bookkeeping",
            content.height > scroll.height)
        // …and the range is still DERIVED from the contract's arithmetic, not read off the
        // table: ROWS × ROW_H − VIEW_H. The table's literals are what the two SHELLS must
        // agree on; this is what the CONTRACT must agree with.
        assertEquals("…by exactly the scrollable range, 800 − 200",
            SCROLL_RANGE, (content.height - scroll.height) / d, 0.5f)

        assertNull("…and NOTHING WAS PAINTED into the row image: a failure reserves nothing " +
            "and paints nothing",
            ((content.getChildAt(0) as ViewGroup).getChildAt(0) as ImageView).drawable)

        // ── the ten rows, inside the content node ────────────────────────────
        assertEquals("ten rows, all children of the CONTENT view", ROWS, content.childCount)
        for (i in 0 until ROWS) {
            assertFrame(f, "row $i", content.getChildAt(i),
                "no Width — stretched to the content node's 300 by Yoga's default " +
                    "alignItems:stretch, which is what proves the content node spans the viewport")
        }

        // ── FLEX NESTED INSIDE THE SCROLL (design §Verification #4) ──────────
        // Row 1 on purpose: at offset 0 it is fully visible (y 80..160 of a
        // 200-high viewport), so the nesting proof is in the FIRST screenshot.
        val flexRow = (content.getChildAt(FLEX_ROW) as ViewGroup).getChildAt(0) as ViewGroup
        assertFrame(f, "nested flex row", flexRow, "Grow=1 in row 1's definite 80dp column")
        assertFrame(f, "nested box A", flexRow.getChildAt(0), "W=50, cross-stretched to the row's 80")
        assertFrame(f, "nested box B", flexRow.getChildAt(1),
            "Grow=1 absorbs 300 − 50 − 50 — the SAME 200 BnLayoutDemo's box B computes, now two " +
                "levels inside a scroll")
        assertFrame(f, "nested box C", flexRow.getChildAt(2), "W=50")

        // ── [1] the back row — OUTSIDE the viewport ─────────────────────────
        // A page whose only exit can scroll off the screen is not a page with an
        // exit. It is also the only MEASURED leaf here, so its HEIGHT is MEASURED in
        // the table and pinned relationally and by oracle — no font constant is
        // anyone's to invent.
        val backRow = root.getChildAt(1) as ViewGroup
        val back = backRow.getChildAt(0) as Button
        assertFrame(f, "back row", backRow,
            "it starts where the VIEWPORT ends (y = 200) — outside it, not at the bottom of the " +
                "800dp of content")
        assertEquals("…and that y IS the viewport's bottom edge, in the framework's own pixels",
            scroll.bottom, backRow.top)
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

    /** Mounts BnScrollDemo, waits for a laid-out tree **and for the row image's request to
     * TERMINATE**, then runs [block] on the main thread.
     *
     * The image wait is the synchronization gate in its one-request form, and it is needed
     * for the same reason it is needed on `/image`: "the failed load moved nothing" is a
     * statement about a request that FINISHED. Read the table while the fetch is still in
     * flight and the assertion is vacuous — the frames would not have moved YET. (Here the
     * fetch is an immediate connection refusal, so the wait is milliseconds; that is not a
     * licence to skip it.) */
    private fun withDemo(block: (MainActivity) -> Unit) {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnScrollDemo")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnScrollDemo never rendered a laid-out tree within 60s", pollForDemo(scenario))
            awaitTheRowImage(scenario)
            scenario.onActivity(block)
        }
    }

    /** Waits for the row image's request to reach a TERMINAL state — here, an ERROR (no
     * fixture server is running). A timeout FAILS: the frame table below is only worth
     * asserting about a request that ended. */
    private fun awaitTheRowImage(scenario: ActivityScenario<MainActivity>) {
        val deadline = System.currentTimeMillis() + 30_000
        val seen = AtomicReference(0)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act -> seen.set(act.mapper.imageResults.size) }
            if (seen.get() >= 1) {
                InstrumentationRegistry.getInstrumentation().waitForIdleSync()
                return
            }
            Thread.sleep(100)
        }
        throw AssertionError("the row image's request never terminated within 30s. It is expected " +
            "to FAIL (this class stands up no fixture server) — but it must fail, not hang: " +
            "'a failed load moves nothing' is a statement about a request that ENDED.")
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
