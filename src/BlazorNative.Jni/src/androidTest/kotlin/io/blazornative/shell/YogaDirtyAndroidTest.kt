package io.blazornative.shell

import android.view.ViewGroup
import android.widget.TextView
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.1 — **DIRTY ON CONTENT CHANGE.** Yoga CACHES a measure function's result
 * and will not re-run it unless the node is marked dirty. So every patch that
 * changes a widget's intrinsic size — `ReplaceText`, `UpdateProp value` /
 * `placeholder`, `SetStyle fontSize` — must call `YogaNode.dirty()`, or the next
 * layout pass silently reuses the size the OLD content measured to.
 *
 * Nothing pinned that. Both measure tests mount FRESH trees, where Yoga's cache is
 * cold either way — so the `markDirty` calls could be deleted and the entire
 * instrumented suite would still pass. That makes the fix one refactor away from a
 * silent regression, and it leaves Gate 3 with no contract to mirror (an `.mm`
 * written from a green Kotlin suite would very plausibly omit `YGNodeMarkDirty`
 * altogether).
 *
 * These tests are TWO-FRAME on the SAME node — which is the only shape that can
 * catch it, because it is the only shape where the cache is warm.
 *
 * **What actually bites here.** `lineCount > 1` does NOT: a `TextView` wraps its
 * text at its own width whatever height it was given, so the stale-framed label
 * still reports multiple lines. Neither does "the row hugs the label": with the
 * dirty call deleted, BOTH are stale and they agree with each other. The assertion
 * that bites is **the label GREW** — the frame changed at all — and [assertOracle],
 * which demands the new frame equal what the widget now measures to.
 */
@RunWith(AndroidJUnit4::class)
class YogaDirtyAndroidTest {

    private val longText =
        "This label is measured natively: it wraps inside 150dp and its measured height drives the row."

    /** Frame 1 renders a short label in a fixed-width row; frame 2 sends
     * `ReplaceText` with a long one on the SAME node. The label must RE-MEASURE
     * (it now wraps) and the row — which declares no height — must re-hug it. */
    @Test fun replace_text_re_measures_the_leaf_and_the_row_re_hugs_it() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "view", null),
            style(1, "flexDirection", "row"), style(1, "width", "150"),
            create(2, "text", 1),
            text(2, "Hi"),
        ))
        val shortHeight = host.read { (host.root.getChildAt(0) as ViewGroup).height }
        assertTrue("frame 1 must have laid the row out at all", shortHeight > 0)

        // FRAME 2 — same node, new text. Yoga's measure cache is WARM now.
        host.render(listOf(text(2, longText)))

        host.read {
            val row = host.root.getChildAt(0) as ViewGroup
            val label = row.getChildAt(0) as TextView

            assertTrue("the long text must WRAP inside the 150dp row (lines=${label.lineCount})",
                label.lineCount > 1)
            assertTrue("THE PIN: the label must have RE-MEASURED. Without markDirty(view) on " +
                "ReplaceText, Yoga serves the CACHED one-line height (${shortHeight}px) and the " +
                "label keeps the frame its OLD text measured to (now ${label.height}px)",
                label.height > shortHeight)
            assertEquals("…and the row, which declares no height, must re-hug the NEW height",
                label.height, row.height)
            assertOracle("the re-texted label", label, availableWidthPx = row.width)
        }
    }

    /** The `SetStyle` twin: a bigger font is a bigger intrinsic size, and the
     * `fontSize` arm dirties the node for exactly that reason. (The same call
     * serves `UpdateProp value`/`placeholder` on an EditText.) */
    @Test fun font_size_change_re_measures_the_leaf_and_the_row_re_hugs_it() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "view", null),
            style(1, "flexDirection", "row"), style(1, "width", "300"),
            create(2, "text", 1),
            text(2, "Sized by its font"),
            style(2, "fontSize", "10"),
        ))
        val smallHeight = host.read { (host.root.getChildAt(0) as ViewGroup).height }
        assertTrue("frame 1 must have laid the row out at all", smallHeight > 0)

        // FRAME 2 — same node, 4× the font. Same warm cache.
        host.render(listOf(style(2, "fontSize", "40")))

        host.read {
            val row = host.root.getChildAt(0) as ViewGroup
            val label = row.getChildAt(0) as TextView

            assertTrue("THE PIN: a 40sp label is TALLER than a 10sp one. Without markDirty(view) " +
                "on the fontSize arm, Yoga serves the CACHED 10sp height (${smallHeight}px) and " +
                "the text is drawn at 40sp inside a 10sp frame (now ${label.height}px)",
                label.height > smallHeight)
            assertEquals("…and the row re-hugs the new measured height",
                label.height, row.height)
            assertOracle("the re-sized label", label, availableWidthPx = row.width)
        }
    }
}
