package io.blazornative.shell

import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.1 Task 2.1 — Yoga owns placement (Android).
 *
 * The engine test: a synthetic patch stream goes through [WidgetMapper], and the
 * assertion is the resulting FRAME of each real View — not its class, not its
 * LayoutParams. Yoga computes in density-independent units on BOTH platforms
 * (design §"The layout model"/Units), so every expectation below is stated in dp
 * and read back as `view.left / density`; the ONE conversion site is the
 * mapper's frame-apply.
 *
 * The row here is the 6.0 spike's canonical shape lifted onto the real wire:
 * 300×100 row of `50 · flexGrow:1 · 50`. It is also the FIRST section of
 * BnLayoutDemo — so a break here fails the cheap synthetic test long before the
 * slow full-mount one (BnLayoutDemoAndroidTest).
 *
 * NON-NEGOTIABLE #3 (measure func by NODETYPE) is what makes the grow box work:
 * the three boxes are childless `view`s. If the mapper attached a measure
 * function to them because "they have no children", the FrameLayout's intrinsic
 * size would speak over Yoga and box B's 200 would collapse. The grow assertion
 * IS that regression test.
 *
 * NON-NEGOTIABLE #8 (the partition): `padding` is LAYOUT — it goes to the Yoga
 * node, which lays a container's children out INSIDE the padding box. The old
 * `view.setPadding(...)` call is gone; [padding_is_a_yoga_property_not_a_view_one]
 * pins both halves (children inset; the View's own padding untouched), because a
 * surviving setPadding would double-apply and every child would be off by it.
 */
@RunWith(AndroidJUnit4::class)
class YogaLayoutAndroidTest {

    /** Yoga's row: box A fixed 50, box B flexGrow 1, box C fixed 50, in a 300×100
     * row. B absorbs exactly 300 − 50 − 50 = 200; none of them declares a height,
     * so the row's default cross-axis STRETCH gives all three its full 100. */
    @Test fun row_places_fixed_grow_fixed_at_computed_frames() {
        val root = render(listOf(
            create(1, "view", null),
            style(1, "flexDirection", "row"),
            style(1, "width", "300"),
            style(1, "height", "100"),
            create(2, "view", 1), style(2, "width", "50"),
            create(3, "view", 1), style(3, "flexGrow", "1"),
            create(4, "view", 1), style(4, "width", "50"),
        ))
        val row = root.getChildAt(0) as ViewGroup
        assertFrame("row", row, 0f, 0f, 300f, 100f)
        assertFrame("box A (fixed 50, cross-stretched to 100)", row.getChildAt(0), 0f, 0f, 50f, 100f)
        assertFrame("box B (flexGrow:1 absorbs 300-50-50)", row.getChildAt(1), 50f, 0f, 200f, 100f)
        assertFrame("box C (fixed 50)", row.getChildAt(2), 250f, 0f, 50f, 100f)
    }

    /** An UN-STYLED tree stacks vertically: Yoga's default flexDirection is
     * `column`, which is the whole reason the demo pages keep looking the same
     * while the engine underneath is replaced (design decision 1). */
    @Test fun unstyled_tree_still_stacks_vertically() {
        val root = render(listOf(
            create(1, "view", null), style(1, "width", "200"),
            create(2, "view", 1), style(2, "width", "100"), style(2, "height", "40"),
            create(3, "view", 1), style(3, "width", "100"), style(3, "height", "40"),
            create(4, "view", 1), style(4, "width", "100"), style(4, "height", "40"),
        ))
        val col = root.getChildAt(0) as ViewGroup
        assertFrame("child 0", col.getChildAt(0), 0f, 0f, 100f, 40f)
        assertFrame("child 1 — stacked BELOW child 0, not beside it", col.getChildAt(1), 0f, 40f, 100f, 40f)
        assertFrame("child 2", col.getChildAt(2), 0f, 80f, 100f, 40f)
    }

    /** A null SetStyle value RESETS the property to Yoga's default (design
     * §"Style value grammar", the `null` row — and the other half of the 1.2
     * null-reset fix). flexGrow's Yoga default is 0, so box B stops growing and
     * falls back to its content width (0) — 300 stays with the row. */
    @Test fun null_value_resets_the_property_to_the_yoga_default() {
        val root = render(listOf(
            create(1, "view", null),
            style(1, "flexDirection", "row"), style(1, "width", "300"), style(1, "height", "100"),
            create(2, "view", 1), style(2, "width", "50"),
            create(3, "view", 1), style(3, "flexGrow", "1"),
        ), listOf(
            // Frame 2: the app re-renders with Grow=null → SetStyle(flexGrow, null).
            style(3, "flexGrow", null),
        ))
        val row = root.getChildAt(0) as ViewGroup
        assertFrame("box A is unmoved", row.getChildAt(0), 0f, 0f, 50f, 100f)
        assertEquals(
            "flexGrow=null must reset to Yoga's default (0) — the box stops absorbing the slack",
            0f, row.getChildAt(1).width / density(), 0.5f
        )
    }

    /** `padding` is LAYOUT (non-negotiable #8): the Yoga node owns it and lays
     * the children out inside the padding box. The View's own padding must stay
     * ZERO — a surviving `view.setPadding(...)` would apply the inset a SECOND
     * time and every child would be off by it. */
    @Test fun padding_is_a_yoga_property_not_a_view_one() {
        val root = render(listOf(
            create(1, "view", null),
            style(1, "width", "300"), style(1, "height", "100"), style(1, "padding", "20"),
            create(2, "view", 1), style(2, "width", "50"), style(2, "height", "50"),
        ))
        val box = root.getChildAt(0) as ViewGroup
        assertFrame("the child sits INSIDE the padding box (20, 20)", box.getChildAt(0), 20f, 20f, 50f, 50f)
        assertEquals("view.setPadding must NOT also fire — that would double-apply the inset",
            0, box.paddingLeft)
        assertEquals(0, box.paddingTop)
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private fun density() =
        InstrumentationRegistry.getInstrumentation().targetContext.resources.displayMetrics.density

    private fun create(nodeId: Int, nodeType: String, parentId: Int?) =
        RenderPatch.CreateNode(nodeId = nodeId, nodeType = nodeType, parentId = parentId)

    private fun style(nodeId: Int, property: String, value: String?) =
        RenderPatch.SetStyle(nodeId = nodeId, property = property, value = value)

    /** Asserts a View's frame in DP (design: Yoga computes in dp; the mapper's
     * frame-apply is the one place density enters). 0.5dp tolerance — the frame
     * is rounded to whole pixels on apply. */
    private fun assertFrame(what: String, v: View, x: Float, y: Float, w: Float, h: Float) {
        val d = density()
        assertEquals("$what.x", x, v.left / d, 0.5f)
        assertEquals("$what.y", y, v.top / d, 0.5f)
        assertEquals("$what.w", w, v.width / d, 0.5f)
        assertEquals("$what.h", h, v.height / d, 0.5f)
    }

    /**
     * Drives [WidgetMapper] with one frame per patch list and returns the host
     * root. The root is given a real 400×800dp bound (via [View.layout]) BEFORE
     * the patches arrive: a detached ViewGroup has no size, and Yoga's available
     * space is the host's — the same bound the Activity's widget_root supplies in
     * production.
     */
    private fun render(vararg frames: List<RenderPatch>): FrameLayout {
        val instr = InstrumentationRegistry.getInstrumentation()
        val ctx = instr.targetContext
        val d = ctx.resources.displayMetrics.density
        lateinit var root: FrameLayout
        instr.runOnMainSync {
            root = FrameLayout(ctx)
            root.layout(0, 0, (400 * d).toInt(), (800 * d).toInt())
            val mapper = WidgetMapper(ctx, root)
            frames.forEachIndexed { i, patches ->
                mapper.apply(RenderFrame(
                    frameId = i + 1, timestampMs = 0L,
                    patches = patches + RenderPatch.CommitFrame(i + 1, 0L),
                ))
            }
        }
        instr.waitForIdleSync()
        var ok = false
        instr.runOnMainSync { ok = root.childCount > 0 }
        assertTrue("no child created in root after apply", ok)
        return root
    }
}
