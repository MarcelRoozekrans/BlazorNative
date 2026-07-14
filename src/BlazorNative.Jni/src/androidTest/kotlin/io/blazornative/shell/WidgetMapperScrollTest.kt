package io.blazornative.shell

import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.view.View
import android.view.ViewGroup
import android.widget.ScrollView
import androidx.test.ext.junit.runners.AndroidJUnit4
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.2 Gate 2 Task 2.2 — **THE SYNTHETIC CONTENT NODE, IN THE SHELL.**
 *
 * `YogaScrollNodeAndroidTest` settled the numbers in Yoga alone. This file is the
 * shell's half: a `scroll` node becomes a vertical `ScrollView` whose single child
 * is a [BnYogaFrameLayout] **content view**, and the scroll node's **wire children
 * parent into that content view** — in the view tree AND the Yoga tree.
 *
 * ```
 *   WIRE                    VIEW / YOGA
 *   scroll                  ScrollView            ← the VIEWPORT (definite height)
 *    ├─ row 0                └─ content view      ← SYNTHETIC. Never on the wire.
 *    ├─ row 1                     ├─ row 0
 *    └─ …                         ├─ row 1
 *                                 └─ …
 * ```
 *
 * That is the **second index-mapping rule** in this shell, after 6.1's text-collapse
 * invariant, and it fails the same way: silently, as a skew. `insertIndex == -1`
 * means "append to the CONTENT node's children" — never to the scroll node's, whose
 * only child *is* the content node ([insertIndex_targets_the_content_views_children]
 * is the pin).
 *
 * The content node gets `height: auto`, `width: 100%`, `flexDirection: column`, and
 * **never `flexShrink`** — Yoga's default 0 is the entire mechanism by which it keeps
 * its 800 against a 200-high viewport (non-negotiable #6; `YogaScrollNodeAndroidTest.
 * flexShrink_1_on_the_content_node_collapses_it_to_the_viewport` is what that
 * sentence costs if it is ignored).
 *
 * And the two scroll-node **diagnostics**, both warn-once, both asserted here rather
 * than left to logcat: container styles are ignored-and-logged, and an auto-height
 * scroll node — which takes its height FROM its content and therefore cannot scroll —
 * gets one warning, because "the page just doesn't move" is otherwise baffling.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperScrollTest {

    private companion object {
        const val ROWS = 10
        const val ROW_H = 80f
        const val VIEW_W = 300f
        const val VIEW_H = 200f
        const val CONTENT_H = ROWS * ROW_H // 800
    }

    /** The demo's shape: a 300×200 viewport over ten 80-high rows. [scrollStyles]
     * are extra SetStyle patches on the SCROLL node (the container-style test's
     * hatch); a null [viewportHeight] leaves the scroll node auto-height. */
    private fun scrollTree(
        viewportHeight: Float? = VIEW_H,
        scrollStyles: List<RenderPatch> = emptyList(),
    ): List<RenderPatch> = buildList {
        add(create(1, "scroll", null))
        add(style(1, "width", VIEW_W.toInt().toString()))
        if (viewportHeight != null) add(style(1, "height", viewportHeight.toInt().toString()))
        addAll(scrollStyles)
        for (i in 0 until ROWS) {
            add(create(10 + i, "view", 1))
            add(style(10 + i, "height", ROW_H.toInt().toString()))
        }
    }

    private fun scrollViewOf(root: ViewGroup) = root.getChildAt(0) as ScrollView
    private fun contentViewOf(root: ViewGroup) = scrollViewOf(root).getChildAt(0) as ViewGroup

    // ── The model ────────────────────────────────────────────────────────────

    /**
     * **THE PHASE, IN ONE ASSERTION.** The viewport is 300×200; the content view it
     * wraps computes to 300×**800** — from Yoga, as the content node's frame, not
     * from any shell-side union of child frames (non-negotiable #3). Ten rows, each
     * 80 tall, at y = 80i, including the seven that sit entirely below the viewport's
     * bottom edge. `contentSize > viewport` is the whole phase.
     */
    @Test fun a_scroll_node_is_a_ScrollView_over_a_synthetic_content_view() {
        val root = render(scrollTree())
        val scroll = scrollViewOf(root)
        val content = contentViewOf(root)

        assertEquals("a scroll node's view is a vertical ScrollView", 1, scroll.childCount)
        assertTrue("its ONE child is the synthetic content view — a BnYogaFrameLayout, so " +
            "the framework does not re-place the rows behind Yoga's back " +
            "(got ${content::class.simpleName})", content is BnYogaFrameLayout)

        assertFrame("the viewport", scroll, 0f, 0f, VIEW_W, VIEW_H)
        assertFrame("THE CONTENT SIZE — the synthetic content node's Yoga frame, 800 tall " +
            "inside a 200-high viewport", content, 0f, 0f, VIEW_W, CONTENT_H)

        assertEquals("all ten rows are children of the CONTENT view", ROWS, content.childCount)
        for (i in 0 until ROWS) {
            assertFrame("row $i", content.getChildAt(i), 0f, ROW_H * i, VIEW_W, ROW_H)
        }
    }

    /**
     * **NON-NEGOTIABLE #2, THE APPEND HALF.** `insertIndex == -1` means append to the
     * **content** node's children. Append them to the ScrollView instead and the very
     * first row would displace the content view — a `ScrollView` holds exactly one
     * child and throws on a second — so the failure is loud here; on iOS the same
     * mistake is a silent skew. Asserted from both sides: the ScrollView has ONE
     * child, and the rows are all under the content view.
     */
    @Test fun wire_children_parent_into_the_content_view_never_into_the_ScrollView() {
        val root = render(scrollTree())
        val scroll = scrollViewOf(root)
        val content = contentViewOf(root)

        assertEquals("the ScrollView's ONLY child is the content view — a scroll node's wire " +
            "children are NOT its view children", 1, scroll.childCount)
        assertEquals(ROWS, content.childCount)
        for (i in 0 until ROWS) {
            assertEquals("row $i's parent must be the CONTENT view", content, content.getChildAt(i).parent)
        }
    }

    /**
     * **NON-NEGOTIABLE #2, THE INDEXED HALF.** A scroll node's wire child at index *i*
     * is the CONTENT node's child at index *i*. A shell that applied `insertIndex` to
     * the ScrollView's children instead would be indexing a list whose only member is
     * the content view — and index 1 is out of range, so it would throw (Android) or
     * clamp (iOS). Neither is a skew you would see in a frame table, which is why this
     * asserts the FRAME: the late row must land FIRST, at y = 0, and push the two
     * originals down.
     */
    @Test fun insertIndex_targets_the_content_views_children() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "scroll", null),
            style(1, "width", "300"), style(1, "height", "200"),
            create(10, "view", 1), style(10, "height", "80"),        // append
            create(11, "view", 1), style(11, "height", "80"),        // append
        ))
        // …and now a row at index 0 of the CONTENT node's children.
        host.render(listOf(create(12, "view", 1, insertIndex = 0), style(12, "height", "40")))

        host.read {
            val content = contentViewOf(host.root)
            assertEquals("three rows, all under the content view", 3, content.childCount)
            assertFrame("the row inserted at index 0 must be the CONTENT node's FIRST child",
                content.getChildAt(0), 0f, 0f, 300f, 40f)
            assertFrame("…and the two originals must have moved down by its 40dp",
                content.getChildAt(1), 0f, 40f, 300f, 80f)
            assertFrame("…both of them", content.getChildAt(2), 0f, 120f, 300f, 80f)
            assertEquals("the content node still computes to the sum: 40 + 80 + 80",
                200f, content.height / density(), 0.5f)
        }
    }

    /**
     * **THE 6.1 FALLBACK IS LOAD-BEARING NOW — DO NOT REGRESS IT.**
     *
     * `ScrollView` measures its single child with an **`UNSPECIFIED` height spec**
     * ("tell me how tall you want to be"), and [BnYogaFrameLayout.onMeasure] answers
     * with **the last size Yoga applied**. The 6.1 review put that fallback in for a
     * boundary we were then only *documenting*; it is what makes the ScrollView see
     * 800dp of content and therefore what makes the page scroll at all. Restore the
     * old `getDefaultSize(0, …)` behaviour and this test reports 0 — and the content
     * vanishes.
     *
     * Asked directly, with the spec ScrollView itself uses (`UNSPECIFIED`, with the
     * viewport height as the size hint that a correct implementation IGNORES).
     */
    @Test fun the_content_view_reports_its_yoga_height_under_an_UNSPECIFIED_spec() {
        val host = SyntheticHost()
        host.render(scrollTree())

        host.read {
            val d = density()
            val content = contentViewOf(host.root)
            content.measure(
                View.MeasureSpec.makeMeasureSpec((VIEW_W * d).toInt(), View.MeasureSpec.EXACTLY),
                // Exactly what ScrollView.measureChild hands it: UNSPECIFIED, sized
                // to the viewport. A shell that honoured the SIZE here would clamp
                // the content to 200 and the page would never scroll.
                View.MeasureSpec.makeMeasureSpec((VIEW_H * d).toInt(), View.MeasureSpec.UNSPECIFIED),
            )
            assertEquals("under UNSPECIFIED the content view must report its YOGA height (800dp) " +
                "— that is the number ScrollView turns into a scroll range. Zero here (the " +
                "pre-6.1-review getDefaultSize behaviour) makes the content vanish.",
                CONTENT_H, content.measuredHeight / d, 0.5f)
        }
    }

    // ── The two diagnostics ──────────────────────────────────────────────────

    /**
     * **CONTAINER STYLES ON A SCROLL NODE ARE IGNORED AND LOGGED** (non-negotiable #5
     * / design decision 6). `BnScroll`'s surface cannot produce them, but the raw
     * element can — `OpenElement("scroll") + AddAttribute("gap", …)` reaches the wire,
     * and a .NET test pins that it does, precisely so this rule is known to be live
     * code. Each of the six would style the *scroll* node, whose only Yoga child is
     * the content node, and each fails silently and bafflingly: `flexDirection: row`
     * stretches the content to the viewport height and the page stops scrolling;
     * `justifyContent: center` offsets it to y = −300 and the top of the content
     * becomes permanently unreachable.
     *
     * So: the frames must be **exactly** the un-styled ones, and each drop must be
     * named in a diagnostic.
     */
    @Test fun container_styles_on_a_scroll_node_are_ignored_and_logged() {
        val ignored = listOf(
            "flexDirection" to "row",
            "justifyContent" to "center",
            "alignItems" to "center",
            "flexWrap" to "wrap",
            "gap" to "8",
            "padding" to "16",
        )
        val host = SyntheticHost()
        host.render(scrollTree(scrollStyles = ignored.map { (p, v) -> style(1, p, v) }))

        host.read {
            val content = contentViewOf(host.root)
            assertFrame("the viewport is untouched", scrollViewOf(host.root), 0f, 0f, VIEW_W, VIEW_H)
            assertFrame("the content node is EXACTLY where it would be with no styles at all — " +
                "flexDirection:row would have stretched it to 200 and killed scrolling; " +
                "padding:16 would have moved every row",
                content, 0f, 0f, VIEW_W, CONTENT_H)
            for (i in 0 until ROWS) {
                assertFrame("row $i", content.getChildAt(i), 0f, ROW_H * i, VIEW_W, ROW_H)
            }

            val diags = host.mapper.scrollDiagnostics
            for ((property, _) in ignored) {
                assertTrue("'$property' on a scroll node must be DROPPED WITH A WARNING naming " +
                    "the node and the style (got: $diags)",
                    diags.any { it.contains("node 1") && it.contains(property) })
            }
        }
    }

    /**
     * The other half of the same rule: **item styles and `backgroundColor` apply
     * NORMALLY.** A `BnScroll` *is* a flex item — how the viewport is placed in its
     * parent is entirely the author's business; it is only the scroll node's
     * CONTAINER layout that belongs to the shell. Over-broad filtering here would be
     * as wrong as no filtering.
     */
    @Test fun item_styles_and_backgroundColor_apply_normally_to_a_scroll_node() {
        val host = SyntheticHost()
        host.render(scrollTree(scrollStyles = listOf(
            style(1, "margin", "10"),
            style(1, "backgroundColor", "#FF0000"),
        )))

        host.read {
            val scroll = scrollViewOf(host.root)
            assertFrame("margin is an ITEM style: it places the VIEWPORT in its parent and must " +
                "still be honoured", scroll, 10f, 10f, VIEW_W, VIEW_H)
            assertEquals("…and so is backgroundColor (it paints the viewport)",
                Color.RED, (scroll.background as? ColorDrawable)?.color)
            assertFrame("…while the content node is unmoved by either — margin insets the " +
                "viewport, not the content", contentViewOf(host.root), 0f, 0f, VIEW_W, CONTENT_H)
            assertTrue("no diagnostic: an item style on a scroll node is not a mistake " +
                "(got: ${host.mapper.scrollDiagnostics})", host.mapper.scrollDiagnostics.isEmpty())
        }
    }

    /**
     * **THE DEFINITE-HEIGHT WARNING** (non-negotiable #6 of the plan / design
     * §"The constraint this introduces").
     *
     * An `auto`-height scroll node takes its height **from** its content, so the
     * viewport IS the content and there is nothing to scroll — asserted in Yoga alone
     * by `YogaScrollNodeAndroidTest.an_auto_height_scroll_node_hugs_its_content`, and
     * this is the shell warning written against that answer. The symptom is a page
     * that simply does not move: no exception, no dropped patch, no wrong frame. So
     * the shell says so, ONCE (a layout pass runs per committed frame; a warning per
     * frame would be a log flood, and a flood is a thing people mute).
     */
    @Test fun an_auto_height_scroll_node_warns_once() {
        val host = SyntheticHost()
        host.render(scrollTree(viewportHeight = null))
        // A second frame: the layout pass runs again, and the warning must NOT.
        host.render(listOf(create(99, "view", null), style(99, "height", "10")))

        host.read {
            val scroll = scrollViewOf(host.root)
            val content = contentViewOf(host.root)
            assertEquals("the auto-height viewport HUGS its content — 800 over 800, scroll " +
                "range zero. Nothing errors; the page just never moves.",
                content.height, scroll.height)

            val warnings = host.mapper.scrollDiagnostics.filter { it.contains("definite height") }
            assertEquals("exactly ONE warning, across TWO layout passes (got: " +
                "${host.mapper.scrollDiagnostics})", 1, warnings.size)
            assertTrue("…and it must name the node (got: ${warnings.first()})",
                warnings.first().contains("node 1"))
        }
    }

    /** …and the negative: a scroll node with a definite height is the normal case and
     * must say nothing at all. Without this, a warning that fired on EVERY scroll node
     * would still pass the test above. */
    @Test fun a_definite_height_scroll_node_warns_about_nothing() {
        val host = SyntheticHost()
        host.render(scrollTree())
        host.read {
            assertTrue("a 300×200 viewport over 800dp of content is the WORKING case — it must " +
                "produce no diagnostic (got: ${host.mapper.scrollDiagnostics})",
                host.mapper.scrollDiagnostics.isEmpty())
        }
    }
}
