package io.blazornative.shell

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.facebook.soloader.SoLoader
import com.facebook.yoga.YogaConstants
import com.facebook.yoga.YogaDirection
import com.facebook.yoga.YogaFlexDirection
import com.facebook.yoga.YogaNode
import com.facebook.yoga.YogaNodeFactory
import com.facebook.yoga.YogaOverflow
import org.junit.Assert.assertEquals
import org.junit.BeforeClass
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.2 Gate 2 Task 2.1 — **THE MECHANISM TEST. IT RUNS BEFORE ANY WIDGET
 * EXISTS.**
 *
 * The whole phase rests on one number: a 200-high viewport whose synthetic content
 * node carries ten 80-high rows computes that content node to **800**, not to 200.
 * If it came out 200 there would be nothing to scroll and no error anywhere — the
 * page would simply sit still. So the number is settled here, in Yoga alone, with
 * no `ScrollView`, no `WidgetMapper` and no density in the picture to blame.
 *
 * ── WHAT PRODUCES THE 800 (the design's ORIGINAL claim was WRONG) ────────────
 *
 * The design said `overflow: scroll` is what stops Yoga clamping the content to
 * the viewport. It is not. **Yoga's `flexShrink` default is `0`** — CSS's is `1`,
 * and this is one of the handful of places Yoga deliberately diverges. The content
 * node is the scroll node's only child and carries 800 of children, so the free
 * space is `200 − 800 = −600`; negative free space is distributed by the **shrink**
 * pass, in proportion to `flexShrink`, which is **0**. Nothing shrinks. The content
 * node keeps its 800.
 *
 * These four tests are what turn that from a corrected sentence into a fact:
 *
 * 1. [the_content_node_computes_to_the_full_content_height] — the premise. 800.
 * 2. [the_800_survives_overflow_visible] — the same 800 with the overflow flag
 *    OFF. The mechanism is not the flag. (We keep setting `overflow: scroll` on
 *    the viewport because it is semantically right and it is what React Native
 *    does — but the contract must not *claim* it makes the 800, or a shell written
 *    against that claim will "fix" the wrong thing when a frame comes out wrong.)
 * 3. [flexShrink_1_on_the_content_node_collapses_it_to_the_viewport] — the
 *    CSS-instinct mutation, made visible ONCE, in a test, instead of on a device.
 *    This is the trap non-negotiable #6 exists for: **never set `flexShrink` on
 *    the content node.**
 * 4. [an_auto_height_scroll_node_hugs_its_content] — what an `auto`-height scroll
 *    node actually computes to. The definite-height runtime warning is written
 *    against THIS answer, not against a guess.
 *
 * Instrumented, not JVM: Yoga's bindings are JNI over a `.so` and need an Android
 * runtime (the 6.0 conclusion's Android note — there is no JVM-unit rung for Yoga).
 *
 * Gate 3 mirrors these four numbers on the simulator.
 */
@RunWith(AndroidJUnit4::class)
class YogaScrollNodeAndroidTest {

    companion object {
        /** The demo's arithmetic, and the shells': ten 80-high rows in a 200-high
         * viewport. Mirrors BnScrollDemo's `RowCount` / `RowHeightDp` /
         * `ViewportHeightDp` — kept as literals here on purpose: this file is the
         * one place the numbers are *derived* rather than transcribed. */
        private const val ROW_COUNT = 10
        private const val ROW_HEIGHT = 80f
        private const val VIEWPORT_WIDTH = 300f
        private const val VIEWPORT_HEIGHT = 200f
        private const val CONTENT_HEIGHT = ROW_COUNT * ROW_HEIGHT   // 800
        private const val SCROLL_RANGE = CONTENT_HEIGHT - VIEWPORT_HEIGHT // 600

        @JvmStatic
        @BeforeClass
        fun initSoLoader() {
            SoLoader.init(InstrumentationRegistry.getInstrumentation().targetContext, false)
        }
    }

    /** The tree every test here builds — the shells' tree, minus the widgets:
     *
     * ```
     * scroll  (300 × [viewportHeight], overflow = [overflow])   ← the VIEWPORT
     *  └─ content  (width 100%, height AUTO, flexDirection COLUMN)
     *       ├─ row 0 .. row 9   (height 80 each)
     * ```
     *
     * [contentFlexShrink] is `null` in production — **the content node must NEVER
     * set it** (test 3 sets it to 1 to prove why). */
    private class Tree(
        viewportHeight: Float?,
        overflow: YogaOverflow,
        contentFlexShrink: Float? = null,
    ) {
        val scroll: YogaNode = YogaNodeFactory.create().apply {
            setDirection(YogaDirection.LTR)
            setWidth(VIEWPORT_WIDTH)
            if (viewportHeight != null) setHeight(viewportHeight) else setHeightAuto()
            setOverflow(overflow)
        }

        val content: YogaNode = YogaNodeFactory.create().apply {
            setWidthPercent(100f)
            setHeightAuto()
            setFlexDirection(YogaFlexDirection.COLUMN)
            // The ONE line the shells must not write. Yoga's default is 0.
            if (contentFlexShrink != null) setFlexShrink(contentFlexShrink)
        }

        val rows: List<YogaNode> = (0 until ROW_COUNT).map {
            YogaNodeFactory.create().apply { setHeight(ROW_HEIGHT) }
        }

        init {
            scroll.addChildAt(content, 0)
            rows.forEachIndexed { i, row -> content.addChildAt(row, i) }
            scroll.calculateLayout(YogaConstants.UNDEFINED, YogaConstants.UNDEFINED)
        }
    }

    /** **THE PHASE'S PREMISE.** A definite 200-high viewport; ten 80-high rows; the
     * content node computes to 800 and every row sits at its own `80 × i`, including
     * the seven that are entirely outside the viewport. `contentSize` is that 800,
     * read straight out of Yoga — never a shell-side union of child frames. */
    @Test fun the_content_node_computes_to_the_full_content_height() {
        val t = Tree(VIEWPORT_HEIGHT, YogaOverflow.SCROLL)

        assertEquals("the viewport keeps its definite height", VIEWPORT_HEIGHT, t.scroll.layoutHeight, 0.5f)
        assertEquals("THE CONTENT SIZE: the content node computes to the sum of its rows — " +
            "800, not clamped to the 200-high viewport. That number IS contentSize.",
            CONTENT_HEIGHT, t.content.layoutHeight, 0.5f)
        assertEquals("the content node spans the viewport's width (width: 100%)",
            VIEWPORT_WIDTH, t.content.layoutWidth, 0.5f)
        assertEquals("…so the scrollable range is 800 − 200",
            SCROLL_RANGE, t.content.layoutHeight - t.scroll.layoutHeight, 0.5f)

        t.rows.forEachIndexed { i, row ->
            assertEquals("row $i sits at y = 80 × $i INSIDE the content node — the rows below " +
                "the viewport are laid out too; that is what there is to scroll TO",
                ROW_HEIGHT * i, row.layoutY, 0.5f)
            assertEquals("row $i is stretched to the content node's width", VIEWPORT_WIDTH, row.layoutWidth, 0.5f)
            assertEquals("row $i is 80 tall", ROW_HEIGHT, row.layoutHeight, 0.5f)
        }
    }

    /** **THE MECHANISM, ISOLATED.** Same tree, `overflow: visible` — the flag the
     * design originally credited with the 800 is OFF, and the 800 is unchanged.
     * What actually holds it is Yoga's `flexShrink` default of **0** (test 3).
     * `overflow: scroll` stays in the shells because it is what a scrolling
     * viewport MEANS (and what RN sets), not because it computes anything here. */
    @Test fun the_800_survives_overflow_visible() {
        val visible = Tree(VIEWPORT_HEIGHT, YogaOverflow.VISIBLE)
        val scroll = Tree(VIEWPORT_HEIGHT, YogaOverflow.SCROLL)

        assertEquals("the content node still computes to 800 with overflow: VISIBLE — so the " +
            "overflow flag is NOT what produces it",
            CONTENT_HEIGHT, visible.content.layoutHeight, 0.5f)
        assertEquals("…and it is the SAME number overflow: SCROLL produces, to the dp",
            scroll.content.layoutHeight, visible.content.layoutHeight, 0.5f)
        assertEquals("the viewport is unaffected either way", VIEWPORT_HEIGHT, visible.scroll.layoutHeight, 0.5f)
    }

    /** **THE TRAP, SPRUNG ON PURPOSE** (non-negotiable #6). `flexShrink: 1` is CSS's
     * default and React Native style sheets are full of it. On the content node it
     * hands the shrink pass a −600 free space to distribute, the content collapses
     * from 800 to the viewport's 200, and the page stops scrolling **with no error
     * anywhere**. This test is the reason that failure mode is a documented fact
     * rather than a device-day.
     *
     * **And note WHERE the damage is not.** The rows keep their 80 (they carry
     * Yoga's `flexShrink: 0` themselves) and simply OVERFLOW the collapsed content
     * node — so the last 600dp of them still exist, still at the right y, and the
     * only thing that changed is the one number the shells read as `contentSize`.
     * A shell would then hand its `ScrollView` a 200-high content over a 200-high
     * viewport: **scroll range zero, rows silently clipped, no frame out of place
     * and nothing to see in a screenshot of the first 200dp.** That is the whole
     * reason this mutation is pinned in a test instead of being left to a device. */
    @Test fun flexShrink_1_on_the_content_node_collapses_it_to_the_viewport() {
        val t = Tree(VIEWPORT_HEIGHT, YogaOverflow.SCROLL, contentFlexShrink = 1f)

        assertEquals("flexShrink: 1 collapses the content node onto the viewport — 800 becomes " +
            "200 and there is nothing left to scroll. NEVER set flexShrink on the content node; " +
            "Yoga's default 0 is the whole mechanism.",
            VIEWPORT_HEIGHT, t.content.layoutHeight, 0.5f)
        assertEquals("…while the ROWS are untouched — they keep their 80 and overflow the " +
            "collapsed content node. Nothing looks broken; only contentSize is wrong.",
            ROW_HEIGHT, t.rows[0].layoutHeight, 0.5f)
        assertEquals("…the last row still sits at y = 720, 520dp outside a content node that " +
            "now claims to be 200 tall — which is exactly how a shell ends up clipping 600dp " +
            "of content with no error anywhere",
            ROW_HEIGHT * (ROW_COUNT - 1), t.rows[ROW_COUNT - 1].layoutY, 0.5f)
    }

    /** **WHAT THE DEFINITE-HEIGHT WARNING IS WRITTEN AGAINST.** An `auto`-height
     * scroll node takes its height FROM its content: viewport == content == 800, the
     * scrollable range is zero, and the page silently never scrolls. The overflow
     * flag does not change that (it governs how a node sizes itself under
     * fit-content, and `scroll` still hugs) — asserted both ways here, because the
     * shell's warning fires on exactly this shape. */
    @Test fun an_auto_height_scroll_node_hugs_its_content() {
        val t = Tree(viewportHeight = null, overflow = YogaOverflow.SCROLL)

        assertEquals("an auto-height scroll node computes to its CONTENT's height",
            CONTENT_HEIGHT, t.scroll.layoutHeight, 0.5f)
        assertEquals("…which is the content node's height", t.content.layoutHeight, t.scroll.layoutHeight, 0.5f)
        assertEquals("…so the scrollable range is ZERO: nothing scrolls, and nothing errors. " +
            "THAT is why the shells warn once when a scroll node ends up auto-height.",
            0f, t.content.layoutHeight - t.scroll.layoutHeight, 0.5f)

        val visible = Tree(viewportHeight = null, overflow = YogaOverflow.VISIBLE)
        assertEquals("the overflow flag does not rescue an auto-height viewport either",
            t.scroll.layoutHeight, visible.scroll.layoutHeight, 0.5f)
    }
}
