package io.blazornative.shell

import android.view.ViewGroup
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.1 — **THE NULL-RESET WIRE, ARM BY ARM.**
 *
 * `SetStyle(name, null)` means "reset this property to Yoga's default" (design
 * §"Style value grammar", the `null` row). That path is not hypothetical: Gate 1
 * exists largely to *create* it — a removed style attribute used to leave on the
 * PROP wire (`UpdateProp(name, null)`), which no shell resets, so a `BnView.Margin`
 * going conditionally null would have kept its old value forever. Gate 1 rerouted
 * it onto `SetStyle(name, null)`; **this file is the other half of that fix**, and
 * before it, nothing on any shell exercised a single reset arm except `flexGrow`.
 *
 * Every test is TWO FRAMES: frame 1 sets the property, frame 2 sends null, and the
 * assertion is the frame after the reset — Yoga's default, which is **Yoga's, not
 * CSS's** (`flexShrink` → 0, not 1; `alignItems` → stretch; `flexDirection` →
 * column).
 *
 * ── THE ARM THAT WAS ACTUALLY BROKEN ─────────────────────────────────────────
 * [margin_null_resets_to_zero_not_auto]. `auto` was doing double duty in the
 * parser: "an accepted VALUE" and "the DEFAULT". Those coincide for
 * `width`/`height`/`flexBasis` and they DO NOT for `margin`, whose default is
 * undefined → 0 while `margin: auto` absorbs the free space and RE-CENTRES the
 * node. So a removed margin used to MOVE the node instead of resetting it — on the
 * exact wire path Gate 1 had just built. Gate 3's `.mm` must split the same flag.
 *
 * (`flexGrow`'s reset is pinned in YogaLayoutAndroidTest, where it was born.)
 */
@RunWith(AndroidJUnit4::class)
class YogaStyleResetAndroidTest {

    /**
     * **THE C2 REGRESSION.** `margin: auto` is a legal VALUE but it is NOT margin's
     * DEFAULT — and it is anything but inert: on all four edges it centres the node
     * in its parent. A null reset must return the node to margin 0, i.e. to the
     * container's origin. Resetting it to `auto` puts this 50×50 box at (125, 25).
     */
    @Test fun margin_null_resets_to_zero_not_auto() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "flexDirection", "row"), style(1, "width", "300"), style(1, "height", "100"),
                create(2, "view", 1),
                style(2, "width", "50"), style(2, "height", "50"), style(2, "margin", "10"),
            ),
            listOf(style(2, "margin", null)),
        )
        val row = root.getChildAt(0) as ViewGroup
        assertFrame(
            "margin=null must reset to Yoga's default (undefined → 0), NOT to `auto` — " +
                "`auto` absorbs the free space and would centre this box at (125, 25)",
            row.getChildAt(0), 0f, 0f, 50f, 50f,
        )
    }

    /** flexDirection's Yoga default is `column` (the reason an un-styled tree still
     * stacks). A reset must put the row back into a column. */
    @Test fun flexDirection_null_resets_to_column() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "flexDirection", "row"), style(1, "width", "300"), style(1, "height", "200"),
                create(2, "view", 1), style(2, "width", "50"), style(2, "height", "50"),
                create(3, "view", 1), style(3, "width", "50"), style(3, "height", "50"),
            ),
            listOf(style(1, "flexDirection", null)),
        )
        val box = root.getChildAt(0) as ViewGroup
        assertFrame("child 0", box.getChildAt(0), 0f, 0f, 50f, 50f)
        assertFrame("child 1 must stack BELOW child 0 again — column is Yoga's default",
            box.getChildAt(1), 0f, 50f, 50f, 50f)
    }

    /** justifyContent's default is `flex-start`: the children go back to the top. */
    @Test fun justifyContent_null_resets_to_flex_start() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "width", "100"), style(1, "height", "300"),
                style(1, "justifyContent", "center"),
                create(2, "view", 1), style(2, "width", "50"), style(2, "height", "50"),
                create(3, "view", 1), style(3, "width", "50"), style(3, "height", "50"),
            ),
            listOf(style(1, "justifyContent", null)),
        )
        val col = root.getChildAt(0) as ViewGroup
        assertFrame("child 0 — centred at y=100 before the reset", col.getChildAt(0), 0f, 0f, 50f, 50f)
        assertFrame("child 1", col.getChildAt(1), 0f, 50f, 50f, 50f)
    }

    /** alignItems' default is `stretch`: the child with no cross size fills again. */
    @Test fun alignItems_null_resets_to_stretch() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "width", "200"), style(1, "height", "100"),
                style(1, "alignItems", "center"),
                create(2, "view", 1), style(2, "height", "40"), // no width: the cross size
            ),
            listOf(style(1, "alignItems", null)),
        )
        val col = root.getChildAt(0) as ViewGroup
        assertFrame("the child must STRETCH across the cross axis again (centred, it had the " +
            "content width — 0 — and sat at x=100)", col.getChildAt(0), 0f, 0f, 200f, 40f)
    }

    /** alignSelf's default is `auto` — i.e. "inherit the parent's alignItems". */
    @Test fun alignSelf_null_resets_to_auto() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "width", "200"), style(1, "height", "100"),
                style(1, "alignItems", "flex-start"),
                create(2, "view", 1), style(2, "width", "50"), style(2, "height", "40"),
                style(2, "alignSelf", "center"),
            ),
            listOf(style(2, "alignSelf", null)),
        )
        val col = root.getChildAt(0) as ViewGroup
        assertFrame("alignSelf=null is AUTO: the child follows the parent's alignItems " +
            "(flex-start → x=0) instead of staying centred at x=75",
            col.getChildAt(0), 0f, 0f, 50f, 40f)
    }

    /** flexWrap's default is `nowrap`: the overflowing child comes back onto line 1. */
    @Test fun flexWrap_null_resets_to_nowrap() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "flexDirection", "row"), style(1, "width", "100"), style(1, "height", "100"),
                style(1, "flexWrap", "wrap"),
                create(2, "view", 1), style(2, "width", "60"), style(2, "height", "20"),
                create(3, "view", 1), style(3, "width", "60"), style(3, "height", "20"),
            ),
            listOf(style(1, "flexWrap", null)),
        )
        val row = root.getChildAt(0) as ViewGroup
        assertFrame("child 1 must be back on LINE 1 (it wrapped to y=40 before the reset) — " +
            "nowrap lets it overflow, because flexShrink's Yoga default is 0",
            row.getChildAt(1), 60f, 0f, 60f, 20f)
    }

    /** position's default is `relative`: the node rejoins the flow. */
    @Test fun position_null_resets_to_relative() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "width", "300"), style(1, "height", "300"),
                create(2, "view", 1), style(2, "width", "50"), style(2, "height", "50"),
                create(3, "view", 1), style(3, "width", "50"), style(3, "height", "50"),
                style(3, "position", "absolute"),
            ),
            listOf(style(3, "position", null)),
        )
        val col = root.getChildAt(0) as ViewGroup
        assertFrame("the absolute child took no part in the flow and sat at (0,0), on top of " +
            "its sibling; reset to `relative` it must FLOW below it",
            col.getChildAt(1), 0f, 50f, 50f, 50f)
    }

    /** The position offsets reset to UNDEFINED — not to 0-as-a-value, but to
     * "unset", which for an absolute node means its parent's content origin. */
    @Test fun position_offsets_null_reset_to_undefined() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "width", "300"), style(1, "height", "300"),
                create(2, "view", 1), style(2, "width", "50"), style(2, "height", "50"),
                style(2, "position", "absolute"), style(2, "top", "100"), style(2, "left", "80"),
            ),
            listOf(style(2, "top", null), style(2, "left", null)),
        )
        val box = root.getChildAt(0) as ViewGroup
        assertFrame("top/left=null must reset the offsets to undefined — the node returns to " +
            "its parent's origin from (80, 100)", box.getChildAt(0), 0f, 0f, 50f, 50f)
    }

    /** flexShrink's Yoga default is **0** — it DEVIATES from CSS's 1. Resetting to
     * 1 here would silently make every node in the tree shrinkable. */
    @Test fun flexShrink_null_resets_to_zero_not_one() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "flexDirection", "row"), style(1, "width", "100"), style(1, "height", "50"),
                create(2, "view", 1), style(2, "width", "80"), style(2, "height", "50"),
                style(2, "flexShrink", "1"),
                create(3, "view", 1), style(3, "width", "80"), style(3, "height", "50"),
                style(3, "flexShrink", "1"),
            ),
            listOf(style(2, "flexShrink", null), style(3, "flexShrink", null)),
        )
        val row = root.getChildAt(0) as ViewGroup
        assertFrame("with flexShrink back at Yoga's default (0) the box keeps its full 80 and " +
            "OVERFLOWS the 100dp row — shrunk, it was 50",
            row.getChildAt(0), 0f, 0f, 80f, 50f)
        assertEquals("…and its sibling starts after it, not at 50",
            80f, row.getChildAt(1).left / density(), 0.5f)
    }

    /** width/height's default IS `auto` — the one family where `auto` is both a
     * legal value and the default (see [YogaLayout]'s `autoIsDefault`). Auto width
     * on a top-level node means "stretch to the host" (alignItems: stretch); auto
     * height means "hug the content". */
    @Test fun width_and_height_null_reset_to_auto() {
        val root = render(
            listOf(
                create(1, "view", null), style(1, "width", "200"), style(1, "height", "100"),
                create(2, "view", 1), style(2, "width", "50"), style(2, "height", "30"),
            ),
            listOf(style(1, "width", null), style(1, "height", null)),
        )
        assertFrame("width=null → auto → stretched to the 400dp host; height=null → auto → " +
            "hugging the 30dp child (it was 200×100)",
            root.getChildAt(0), 0f, 0f, 400f, 30f)
    }

    /** flexBasis' default is `auto`: the main size falls back to `width`. */
    @Test fun flexBasis_null_resets_to_auto() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "flexDirection", "row"), style(1, "width", "300"), style(1, "height", "50"),
                create(2, "view", 1), style(2, "width", "50"), style(2, "height", "50"),
                style(2, "flexBasis", "200"),
            ),
            listOf(style(2, "flexBasis", null)),
        )
        val row = root.getChildAt(0) as ViewGroup
        assertFrame("flexBasis=null → auto → the main size comes from `width` (50) again, " +
            "not from the 200 basis", row.getChildAt(0), 0f, 0f, 50f, 50f)
    }

    /** The min/max constraints reset to UNDEFINED — the clamp lifts entirely. */
    @Test fun min_and_max_constraints_null_reset_to_undefined() {
        val root = render(
            listOf(
                create(1, "view", null), style(1, "width", "100"), style(1, "height", "50"),
                style(1, "minWidth", "200"),
                create(2, "view", null), style(2, "width", "300"), style(2, "height", "50"),
                style(2, "maxWidth", "100"),
                create(3, "view", null), style(3, "width", "50"), style(3, "height", "20"),
                style(3, "minHeight", "80"),
                create(4, "view", null), style(4, "width", "50"), style(4, "height", "200"),
                style(4, "maxHeight", "60"),
            ),
            listOf(
                style(1, "minWidth", null), style(2, "maxWidth", null),
                style(3, "minHeight", null), style(4, "maxHeight", null),
            ),
        )
        assertEquals("minWidth=null lifts the clamp: back to the declared 100 (it was 200)",
            100f, root.getChildAt(0).width / density(), 0.5f)
        assertEquals("maxWidth=null: back to the declared 300 (it was 100)",
            300f, root.getChildAt(1).width / density(), 0.5f)
        assertEquals("minHeight=null: back to the declared 20 (it was 80)",
            20f, root.getChildAt(2).height / density(), 0.5f)
        assertEquals("maxHeight=null: back to the declared 200 (it was 60)",
            200f, root.getChildAt(3).height / density(), 0.5f)
    }

    /** padding resets to UNDEFINED → 0: the children un-inset. */
    @Test fun padding_null_resets_to_zero() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "width", "300"), style(1, "height", "100"), style(1, "padding", "20"),
                create(2, "view", 1), style(2, "width", "50"), style(2, "height", "50"),
            ),
            listOf(style(1, "padding", null)),
        )
        val box = root.getChildAt(0) as ViewGroup
        assertFrame("padding=null must un-inset the child from (20, 20) to the origin",
            box.getChildAt(0), 0f, 0f, 50f, 50f)
    }

    /** gap resets to UNDEFINED → 0: the gutter closes. */
    @Test fun gap_null_resets_to_zero() {
        val root = render(
            listOf(
                create(1, "view", null),
                style(1, "flexDirection", "row"), style(1, "width", "300"), style(1, "height", "100"),
                style(1, "gap", "20"),
                create(2, "view", 1), style(2, "width", "50"), style(2, "height", "50"),
                create(3, "view", 1), style(3, "width", "50"), style(3, "height", "50"),
            ),
            listOf(style(1, "gap", null)),
        )
        val row = root.getChildAt(0) as ViewGroup
        assertFrame("gap=null closes the 20dp gutter: the second box butts up against the first " +
            "(it started at 70)", row.getChildAt(1), 50f, 0f, 50f, 50f)
    }
}
