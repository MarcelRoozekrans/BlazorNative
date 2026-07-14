package io.blazornative.shell

import android.view.ViewGroup
import android.widget.TextView
import androidx.test.ext.junit.runners.AndroidJUnit4
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
 * Font metrics are not a constant anyone gets to invent, so nothing here pins a
 * pixel count. The assertions are RELATIONAL — a wrapped label is TALLER than a
 * short one, the row HUGS the label's measured height — **plus one ORACLE**, and
 * the oracle is the load-bearing one: every relational assertion in this file also
 * passes with a measure function that returns a CONSTANT (the 6.0 spike's 80×20
 * stub satisfies all of them), so on its own this file would stay green while the
 * measurement was entirely invented — and Gate 3 mirrors this file. [assertOracle]
 * measures the same text independently, under the same spec the measure func hands
 * Yoga, and demands the laid-out frame equal it: still no font metric written down,
 * but the measurement can no longer be fabricated.
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
            text(2, longText),
        ))
        val row = root.getChildAt(0) as ViewGroup
        val label = row.getChildAt(0) as TextView
        val d = density()

        assertTrue("the label must have a real measured height (got ${label.height / d}dp)",
            label.height / d > 0f)
        assertTrue("the label must have WRAPPED — a single line of this text is far wider " +
            "than 150dp, so it must occupy >1 line", label.lineCount > 1)
        // The label never overflows the row it was measured inside. In PIXELS that
        // is exact — the AT_MOST spec Yoga's measure func hands the TextView is
        // the row's own width converted by the same rounding the frame-apply uses
        // — so it is asserted in px, not dp. (In dp the label can read a hair over
        // 150: 150dp is 393.75px, the spec rounds to 394px, and 394px back is
        // 150.095dp. That is the density, not the engine.)
        assertTrue("the label must not overflow the row it was measured inside " +
            "(row=${row.width}px, label=${label.width}px)",
            label.width <= row.width)
        assertEquals("the row declares NO height: it must hug the label's MEASURED height " +
            "— that is the whole DoD #3 claim", label.height.toFloat(), row.height.toFloat(), 1f)

        // ── THE ORACLE ───────────────────────────────────────────────────────
        // Every assertion above passes with a FABRICATED measure function: feed
        // the 6.0 spike's constant 80×20 stub through them and the label still has
        // a height, the row still hugs it, and lineCount still reports >1 (a
        // TextView wraps its text at its own width regardless of the height it was
        // given). They pin the plumbing, not the MEASUREMENT.
        //
        // So: measure the same text independently, with a throwaway TextView, under
        // the same spec the measure func hands Yoga — AT_MOST(the row) × UNSPECIFIED
        // — and demand the laid-out frame EQUAL it. No font metric is written down
        // anywhere (the oracle asks the same font the same question), so it stays
        // honest on any device; but a constant-size measure func now fails, loudly.
        // Gate 3 mirrors this with a throwaway UILabel + sizeThatFits.
        assertOracle("the wrapped label", label, availableWidthPx = row.width)
    }

    /** The SAME tree with a SHORT text: fewer lines, so a shorter row. Proves the
     * row's height tracks the measurement rather than any fixed default. */
    @Test fun a_short_text_yields_a_shorter_row_than_a_wrapped_one() {
        val shortRoot = render(listOf(
            create(1, "view", null),
            style(1, "flexDirection", "row"), style(1, "width", "150"),
            create(2, "text", 1),
            text(2, "Hi"),
        ))
        val longRoot = render(listOf(
            create(1, "view", null),
            style(1, "flexDirection", "row"), style(1, "width", "150"),
            create(2, "text", 1),
            text(2, longText),
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

    // Helpers (density / create / style / render) live in FrameAssertions.kt.
}
