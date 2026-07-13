package io.blazornative.shell

import android.view.ViewGroup
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
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

    // Helpers (assertFrame / create / style / render — the 400×800dp synthetic
    // host, rooted in the PRODUCTION BnYogaFrameLayout) live in FrameAssertions.kt.
}
