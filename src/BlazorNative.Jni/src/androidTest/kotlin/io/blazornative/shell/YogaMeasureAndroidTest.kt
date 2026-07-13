package io.blazornative.shell

import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.1 Task 2.2 — NATIVE MEASUREMENT (M6 DoD #3), Android rung.
 *
 * The 6.0 spike proved the measure-callback ROUND TRIP with a stub that returned
 * a constant 80×20. This is the real thing: the measure function calls
 * `view.measure(...)` on the actual widget, so a `TextView` reports the size of
 * its WRAPPED text and that size drives the layout above it.
 *
 * Font metrics are not a constant anyone gets to invent, so the assertions are
 * RELATIONAL — a wrapped label is TALLER than a short one, the row HUGS the
 * label's measured height — rather than a pinned pixel count. That is the same
 * discipline BnLayoutDemo's frame table uses for its two measured leaves, and it
 * is the honest form of the claim: the assertion is that the NATIVE measurement
 * reached Yoga, not that a particular font renders at a particular height.
 *
 * [childless_view_boxes_keep_their_yoga_widths] is the other half, and it is a
 * REGRESSION test for non-negotiable #6: the measure func attaches by NODETYPE,
 * never by "this node has no children". Wire it the wrong way and these three
 * childless `view`s get a measure func, a FrameLayout measures 0×0 through it,
 * and the flexGrow box's width collapses. It fails LOUDLY here rather than
 * quietly in the demo's frame table.
 */
@RunWith(AndroidJUnit4::class)
class YogaMeasureAndroidTest {

    private val longText =
        "This label is measured natively: it wraps inside 150dp and its measured height drives the row."

    /** A long label in a 150dp row cannot fit on one line. Its measure function
     * runs against a real TextView with an AT_MOST(150dp) width spec, so it
     * reports a MULTI-LINE height — and the row, which declares no height of its
     * own, hugs exactly that. */
    @Test fun long_text_wraps_in_a_narrow_row_and_its_height_drives_the_row() {
        val root = render(listOf(
            // A 150dp-wide row with NO height: its height IS the measured content.
            create(1, "view", null),
            style(1, "flexDirection", "row"), style(1, "width", "150"),
            create(2, "text", 1),
            RenderPatch.ReplaceText(nodeId = 2, text = longText),
        ))
        val row = root.getChildAt(0) as ViewGroup
        val label = row.getChildAt(0) as TextView
        val d = density()

        assertTrue("the label must have a real measured height (got ${label.height / d}dp)",
            label.height / d > 0f)
        assertTrue("the label must have WRAPPED — a single line of this text is far wider " +
            "than 150dp, so it must occupy >1 line", label.lineCount > 1)
        // The label is constrained to the row's main axis — but it may end up up
        // to ONE dp wider than it, and that is Yoga, working as designed:
        // YGRoundToPixelGrid CEILS the dimensions of a node that has a MEASURE
        // FUNCTION (its "text rounding" rule) so a fractional measured width can
        // never clip the glyph it was measured from. Measured 150.1dp → frame
        // 151dp. It is the same C++ core with the same default pointScaleFactor
        // on iOS, so it is not a platform divergence — and it is precisely why
        // the demo's two measured leaves are asserted RELATIONALLY rather than
        // as pinned numbers.
        assertTrue("the label must be constrained to the row's main axis, give or take Yoga's " +
            "one-point text-rounding ceil (row=${row.width}px, label=${label.width}px)",
            label.width <= row.width + d)
        assertEquals("the row declares NO height: it must hug the label's MEASURED height " +
            "— that is the whole DoD #3 claim", label.height.toFloat(), row.height.toFloat(), 1f)
    }

    /** The SAME tree with a SHORT text: fewer lines, so a shorter row. Proves the
     * row's height tracks the measurement rather than any fixed default. */
    @Test fun a_short_text_yields_a_shorter_row_than_a_wrapped_one() {
        val shortRoot = render(listOf(
            create(1, "view", null),
            style(1, "flexDirection", "row"), style(1, "width", "150"),
            create(2, "text", 1),
            RenderPatch.ReplaceText(nodeId = 2, text = "Hi"),
        ))
        val longRoot = render(listOf(
            create(1, "view", null),
            style(1, "flexDirection", "row"), style(1, "width", "150"),
            create(2, "text", 1),
            RenderPatch.ReplaceText(nodeId = 2, text = longText),
        ))
        val shortRow = shortRoot.getChildAt(0)
        val longRow = longRoot.getChildAt(0)
        assertTrue(
            "the wrapped label's row (${longRow.height}px) must be TALLER than the one-line " +
                "label's row (${shortRow.height}px) — the height is MEASURED, not defaulted",
            longRow.height > shortRow.height
        )
    }

    /**
     * NON-NEGOTIABLE #6, as a test. The three boxes are childless `view`s — i.e.
     * CONTAINERS that happen to have no children. They must get NO measure
     * function: a FrameLayout's intrinsic size is 0×0, and letting that speak
     * over Yoga would give the flexGrow:1 box a width of 0 and shift box C to 50.
     */
    @Test fun childless_view_boxes_keep_their_yoga_widths() {
        val root = render(listOf(
            create(1, "view", null),
            style(1, "flexDirection", "row"), style(1, "width", "300"), style(1, "height", "100"),
            create(2, "view", 1), style(2, "width", "50"),
            create(3, "view", 1), style(3, "flexGrow", "1"),
            create(4, "view", 1), style(4, "width", "50"),
        ))
        val row = root.getChildAt(0) as ViewGroup
        val d = density()
        assertEquals("a CHILDLESS view is a CONTAINER, not a leaf: no measure func, so Yoga's " +
            "flexGrow keeps the 200dp — a measure func would have collapsed it to 0",
            200f, row.getChildAt(1).width / d, 0.5f)
        assertEquals("box C must still start after the grown box",
            250f, row.getChildAt(2).left / d, 0.5f)
    }

    // ── Helpers (YogaLayoutAndroidTest conventions) ──────────────────────────

    private fun density() =
        InstrumentationRegistry.getInstrumentation().targetContext.resources.displayMetrics.density

    private fun create(nodeId: Int, nodeType: String, parentId: Int?) =
        RenderPatch.CreateNode(nodeId = nodeId, nodeType = nodeType, parentId = parentId)

    private fun style(nodeId: Int, property: String, value: String?) =
        RenderPatch.SetStyle(nodeId = nodeId, property = property, value = value)

    private fun render(patches: List<RenderPatch>): FrameLayout {
        val instr = InstrumentationRegistry.getInstrumentation()
        val ctx = instr.targetContext
        val d = ctx.resources.displayMetrics.density
        lateinit var root: FrameLayout
        instr.runOnMainSync {
            root = FrameLayout(ctx)
            root.layout(0, 0, (400 * d).toInt(), (800 * d).toInt())
            WidgetMapper(ctx, root).apply(RenderFrame(
                frameId = 1, timestampMs = 0L,
                patches = patches + RenderPatch.CommitFrame(1, 0L),
            ))
        }
        instr.waitForIdleSync()
        var child: View? = null
        instr.runOnMainSync { child = root.getChildAt(0) }
        assertTrue("no child created in root after apply", child != null)
        return root
    }
}
