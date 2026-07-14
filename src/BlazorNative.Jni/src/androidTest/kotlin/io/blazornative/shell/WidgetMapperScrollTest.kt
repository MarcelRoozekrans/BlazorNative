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
        const val CONTENT_H = ROWS * ROW_H          // 800
        const val SCROLL_RANGE = CONTENT_H - VIEW_H // 600
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

    /**
     * A scroll inside a parent with a DEFINITE height, taking its own height **from
     * flex rather than from a declared `height`** — so it sails past the
     * definite-height diagnostic's first condition and is judged only on the second.
     *
     * [growOnly] picks between the two shapes, and **the difference is the whole of
     * `a_Grow_ONLY_scroll_node_does_NOT_get_a_definite_height`**:
     *
     *  - `growOnly = false` — `Grow="1"` **plus `Basis="0"`** (CSS's `flex: 1`). THE
     *    SHAPE THAT WORKS: basis 0 → free space is `parentHeight − 0` (POSITIVE) →
     *    grow gives the viewport exactly the parent's height.
     *  - `growOnly = true` — `Grow="1"` alone, which is what every doc in this phase
     *    used to recommend and **which does not bound the viewport at all** when the
     *    content is taller than the parent (see that test).
     *
     * The scroll's Yoga child is one box of [contentHeight] — enough to scroll over
     * (800) or not enough to fill the viewport (100), which is the difference the
     * diagnostic must NOT confuse for a mistake.
     */
    private fun grownScrollTree(
        parentHeight: Float,
        contentHeight: Float,
        growOnly: Boolean = false,
    ): List<RenderPatch> = buildList {
        add(create(1, "view", null))
        add(style(1, "width", VIEW_W.toInt().toString()))
        add(style(1, "height", parentHeight.toInt().toString()))
        add(create(2, "scroll", 1))
        add(style(2, "flexGrow", "1"))
        if (!growOnly) add(style(2, "flexBasis", "0"))
        add(create(10, "view", 2))
        add(style(10, "height", contentHeight.toInt().toString()))
    }

    private fun scrollUnderWrapper(root: ViewGroup) =
        (root.getChildAt(0) as ViewGroup).getChildAt(0) as ScrollView

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
        val viewport = root.getChildAt(0)

        // The WIDGET CLASS is where "vertical" actually comes from — Android's
        // ScrollView is vertical-ONLY (HorizontalScrollView is a different class, and
        // horizontal scroll is ledgered). Assert it, rather than asserting a child
        // count under a message that talks about the class.
        assertTrue("a scroll node's view is a ScrollView — Android's VERTICAL scroll " +
            "container, which is where 'vertical only' (design decision 2) actually lives " +
            "(got ${viewport::class.simpleName})", viewport is ScrollView)

        val scroll = scrollViewOf(root)
        val content = contentViewOf(root)

        assertEquals("…and it has exactly ONE child, the synthetic content view", 1, scroll.childCount)
        assertTrue("…which is a BnYogaFrameLayout, so the framework does not re-place the rows " +
            "behind Yoga's back (got ${content::class.simpleName})", content is BnYogaFrameLayout)
        assertTrue("the VIEWPORT clips (clipChildren stays at the framework default `true`) — " +
            "unlike every BnYogaFrameLayout, which turns clipping OFF to match iOS's " +
            "UIView.clipsToBounds == NO. A viewport that did not clip would draw all 800dp of " +
            "content over the whole screen; `true` here is what matches UIScrollView.clipsToBounds " +
            "== YES. Gate 3 must NOT mirror 'our containers don't clip' onto the UIScrollView.",
            scroll.clipChildren)

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
     * **NON-NEGOTIABLE #2, THE MID-LIST HALF — THE "SILENT SKEW" THE RULE IS NAMED
     * AFTER.** Front (index 0) and back (append) are the two indices a wrong
     * implementation is most likely to get right by accident: index 0 of the SCROLL
     * node's Yoga children would displace the content node itself (loud), and append
     * to a `ScrollView` throws on the second child (loud). **Index 1 of 3 is the one
     * that fails quietly**, and it is the one a keyed list re-order actually emits.
     *
     * Asserted as FRAMES, because that is the only place a skew is visible: the new
     * 40-high row must land BETWEEN rows 10 and 11, and push the two below it down by
     * exactly its 40.
     */
    @Test fun insertIndex_in_the_MIDDLE_of_a_scroll_nodes_children() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "scroll", null),
            style(1, "width", "300"), style(1, "height", "200"),
            create(10, "view", 1), style(10, "height", "80"),
            create(11, "view", 1), style(11, "height", "80"),
            create(12, "view", 1), style(12, "height", "80"),
        ))
        host.render(listOf(create(13, "view", 1, insertIndex = 1), style(13, "height", "40")))

        host.read {
            val content = contentViewOf(host.root)
            assertEquals("four rows, all under the content view", 4, content.childCount)
            assertFrame("row 10 is untouched at index 0", content.getChildAt(0), 0f, 0f, 300f, 80f)
            assertFrame("THE NEW ROW is the CONTENT node's child at index 1, at y = 80",
                content.getChildAt(1), 0f, 80f, 300f, 40f)
            assertFrame("…and the two below it moved down by exactly its 40dp",
                content.getChildAt(2), 0f, 120f, 300f, 80f)
            assertFrame("…both of them", content.getChildAt(3), 0f, 200f, 300f, 80f)
            assertEquals("the content node computes to 80 + 40 + 80 + 80",
                280f, content.height / density(), 0.5f)
        }
    }

    /**
     * **NON-NEGOTIABLE #2, THE SYMMETRIC HALF.** The rule says the two trees mirror
     * each other *"in BOTH trees, at the same index"* — and removal is the direction
     * nothing asserted. A `RemoveNode` for a scroll node's CHILD must reach the child's
     * Yoga node inside the CONTENT node, not just its view.
     *
     * The frame is what proves it: a Yoga node left behind keeps **reserving its 80dp**,
     * so the surviving row below would stay at y = 160 instead of moving up into the
     * hole. The view tree would look right and the layout would be silently wrong —
     * the 6.1 "ghost node" failure, one level deeper.
     */
    @Test fun removing_a_scroll_nodes_child_removes_it_from_BOTH_trees() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "scroll", null),
            style(1, "width", "300"), style(1, "height", "200"),
            create(10, "view", 1), style(10, "height", "80"),
            create(11, "view", 1), style(11, "height", "80"),
            create(12, "view", 1), style(12, "height", "80"),
        ))
        host.render(listOf(RenderPatch.RemoveNode(nodeId = 11)))   // the MIDDLE one

        host.read {
            val content = contentViewOf(host.root)
            assertEquals("the middle row is gone from the CONTENT view", 2, content.childCount)
            assertFrame("row 10 keeps its place", content.getChildAt(0), 0f, 0f, 300f, 80f)
            assertFrame("row 12 MOVED UP into the hole — which is what proves the removed row " +
                "left the YOGA tree too, not merely the view tree. A ghost node under the content " +
                "node keeps reserving its 80dp and this row stays at y = 160.",
                content.getChildAt(1), 0f, 80f, 300f, 80f)
            assertEquals("…and the content node SHRANK to 160: contentSize follows",
                160f, content.height / density(), 0.5f)
        }
    }

    /**
     * **THE TWO INDEX-MAPPING RULES, MEETING.** 6.1's text collapse says a `text` node
     * whose parent is a text-bearing non-container gets **no view and no Yoga node**;
     * 6.2's rule says a scroll node's wire child at index *i* is the CONTENT node's
     * child at index *i*. Put a `button` (with its collapsed text child) directly
     * inside a `scroll`, followed by a sibling at a KNOWN index, and the two rules have
     * to hold at once.
     *
     * True by construction — the collapse returns before any container is touched — and
     * that is precisely the kind of "true by construction" 6.1 learned to pin: the box
     * at wire index 1 must be the content view's child at index 1, sitting directly
     * under the button's measured height. A collapsed node that took a slot in either
     * tree puts it at index 2 and every frame after it is wrong, silently.
     *
     * It is also **the only MEASURED leaf inside a scroll** anywhere in this suite or
     * the demo — so the measure func is asserted against the [assertOracle], inside a
     * `ScrollView`, where a fabricated constant would otherwise never be caught.
     */
    @Test fun a_collapsed_text_child_inside_a_scroll_does_not_skew_the_content_nodes_indices() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "scroll", null),
            style(1, "width", "300"), style(1, "height", "200"),
            // wire child 0 — a MEASURED leaf. alignSelf:flex-start so its width is its
            // own measured width (Yoga's default alignItems:stretch would stretch it to
            // the content node's 300 and the oracle would have nothing to say).
            create(20, "button", 1), style(20, "alignSelf", "flex-start"),
            create(21, "text", 20),          // COLLAPSED onto the Button: no view, no Yoga node
            text(21, "Scrolled button"),
            // wire child 1 — at an index that only holds if the collapse took no slot.
            create(30, "view", 1, insertIndex = 1), style(30, "height", "50"),
        ))

        host.read {
            val d = density()
            val content = contentViewOf(host.root)
            assertEquals("the collapsed text node gets NO view: the content view's children are " +
                "the button and the box — TWO, not three", 2, content.childCount)

            val button = content.getChildAt(0) as android.widget.Button
            val box = content.getChildAt(1)
            assertEquals("Scrolled button", button.text.toString())

            assertOracle("the button INSIDE the scroll", button, availableWidthPx = content.width)

            assertEquals("THE PIN: the box at wire index 1 is the CONTENT node's child at index " +
                "1, directly under the button's MEASURED height. A collapsed text node that took " +
                "a slot in either tree would put it at index 2, and every frame below it would " +
                "be silently skewed.", button.height, box.top)
            assertFrame("…and it is the 50-high box, stretched to the content node's width",
                box, 0f, button.height / d, VIEW_W, 50f)
            assertEquals("the content node hugs the two of them — a measured leaf's height " +
                "reaches contentSize like any other",
                (button.height + box.height) / d, content.height / d, 0.5f)
        }
    }

    /**
     * **THE 6.1 FALLBACK IS LOAD-BEARING — BUT NOT FOR THE REASON THE FIRST DRAFT OF
     * THIS FILE GAVE.**
     *
     * `ScrollView` measures its single child with an **`UNSPECIFIED` height spec**
     * ("tell me how tall you want to be"), and [BnYogaFrameLayout.onMeasure] answers
     * with **the last size Yoga applied**.
     *
     * This file used to say that answer "is what makes the page scroll at all", and
     * that reverting it makes "the content vanish". **It does not** — the implementer's
     * own mutation run showed the demo still scrolling, and he said so. `applyFrames`
     * walks parent-first and `applyFrame` does a direct `measure(EXACTLY) + layout()`,
     * so **Yoga is the last word on the content's frame either way.** (This is the same
     * shape of error as the `overflow: scroll` claim Gate 1's review corrected; it is
     * corrected here, in the design, and in the shell, before Gate 3 inherits it.)
     *
     * What the fallback actually protects is the **scroll OFFSET** —
     * [a_commit_that_relayouts_the_viewport_does_not_snap_a_scrolled_page_to_the_top]
     * is the test that says so, and it is the one that reddens under the mutation.
     * This test still earns its place: it pins the ANSWER, directly, with the spec
     * `ScrollView` itself uses (`UNSPECIFIED`, with the viewport height as the size
     * hint that a correct implementation IGNORES) — so a wrong number is caught here,
     * one level below where its damage shows up.
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
                // to the viewport. A shell that honoured the SIZE here would report 200
                // and ScrollView would believe the scroll range was zero.
                View.MeasureSpec.makeMeasureSpec((VIEW_H * d).toInt(), View.MeasureSpec.UNSPECIFIED),
            )
            assertEquals("under UNSPECIFIED the content view must report its YOGA height (800dp) " +
                "— that is the number ScrollView turns into a scroll range, and the number it " +
                "re-clamps the user's offset against on every layout.",
                CONTENT_H, content.measuredHeight / d, 0.5f)
        }
    }

    /**
     * **WHAT THE UNSPECIFIED FALLBACK ACTUALLY PROTECTS — AND IT IS ANDROID-SPECIFIC.**
     *
     * `ScrollView.onLayout` ends with `scrollTo(mScrollX, mScrollY)`, which **re-clamps
     * the offset against the content child's just-laid-out height** — on EVERY layout
     * the ScrollView takes part in, i.e. on every commit that dirties the scroll
     * subtree. (Appending a row does: `addView` requestLayouts up through the
     * ScrollView. M7's virtualized list will do it on every frame.)
     *
     * So with a broken `UNSPECIFIED` answer the content is **0-tall at that moment**,
     * `mScrollY` clamps to **0**, and **a re-render while the user is scrolled snaps
     * the page back to the top.** The frames are all still correct afterwards —
     * `applyFrame` re-lays the content to its Yoga height — which is exactly why no
     * frame assertion in this suite can see it, and why it needs a test of its own.
     *
     * **Gate 3 must NOT look for this on iOS.** `UIScrollView` does not re-measure its
     * content view and does not re-clamp `contentOffset` on layout — there is no
     * equivalent of this fallback to mirror. What iOS owes instead is handling a
     * SHRINKING `contentSize` under a live `contentOffset` itself.
     */
    @Test fun a_commit_that_relayouts_the_viewport_does_not_snap_a_scrolled_page_to_the_top() {
        val host = SyntheticHost()
        host.render(scrollTree())
        val d = density()

        host.read { scrollViewOf(host.root).scrollTo(0, (SCROLL_RANGE * d).toInt()) }
        assertEquals("the user scrolled to the end of the range (600 = 800 − 200)",
            SCROLL_RANGE, host.read { scrollViewOf(host.root).scrollY } / d, 0.5f)

        // A commit that touches the scroll's content — one more row appended. Nothing
        // about it concerns the OFFSET; it is an ordinary re-render.
        host.render(listOf(create(20, "view", 1), style(20, "height", ROW_H.toInt().toString())))

        host.read {
            assertEquals("THE PIN: the user's scroll offset SURVIVES the commit. ScrollView's " +
                "onLayout ends with scrollTo(mScrollX, mScrollY), which re-clamps against the " +
                "content child's laid-out height — and that height comes from an UNSPECIFIED " +
                "measure, which BnYogaFrameLayout answers with the last size YOGA applied. " +
                "Break that fallback and the content is 0-tall AT THAT MOMENT, the offset is " +
                "clamped to 0, and the page snaps to the top under the user's finger — silently, " +
                "with every frame still correct.",
                SCROLL_RANGE, scrollViewOf(host.root).scrollY / d, 0.5f)
            assertEquals("…and the appended row grew the content node, so the offset is still " +
                "well inside the (now larger) range — the offset was PRESERVED, not merely " +
                "re-clamped to a coincidentally equal maximum",
                CONTENT_H + ROW_H, contentViewOf(host.root).height / d, 0.5f)
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
     * would still pass the test above.
     *
     * It exits at the FIRST condition (the height is a POINT), so it says nothing about
     * the second — that is what the three tests below are for. */
    @Test fun a_definite_height_scroll_node_warns_about_nothing() {
        val host = SyntheticHost()
        host.render(scrollTree())
        host.read {
            assertTrue("a 300×200 viewport over 800dp of content is the WORKING case — it must " +
                "produce no diagnostic (got: ${host.mapper.scrollDiagnostics})",
                host.mapper.scrollDiagnostics.isEmpty())
        }
    }

    /**
     * **A FLEX-SIZED VIEWPORT THAT SCROLLS MUST NOT BE WARNED ABOUT** — the shape a
     * full-screen scrolling page actually has. `Grow="1" Basis="0"` (CSS's `flex: 1`)
     * in a bounded parent: the scroll node **declares no height at all**, so it sails
     * past the diagnostic's first condition and is saved only by the second — flex DID
     * give it a bounded height (200, from its parent), and 200 ≠ 800.
     */
    @Test fun a_flex_sized_scroll_node_over_taller_content_warns_about_nothing() {
        val host = SyntheticHost()
        host.render(grownScrollTree(parentHeight = VIEW_H, contentHeight = CONTENT_H))

        host.read {
            val scroll = scrollUnderWrapper(host.root)
            val content = scroll.getChildAt(0) as ViewGroup
            assertEquals("the viewport took its 200 from its bounded parent (Grow + Basis=0)",
                VIEW_H, scroll.height / density(), 0.5f)
            assertEquals("…over 800 of content: it SCROLLS", CONTENT_H,
                content.height / density(), 0.5f)
            assertTrue("a flex-sized viewport that scrolls declares no height and is entirely " +
                "correct — it must produce no diagnostic (got: ${host.mapper.scrollDiagnostics})",
                host.mapper.scrollDiagnostics.isEmpty())
        }
    }

    /**
     * **A VIEWPORT TALLER THAN ITS CONTENT MUST NOT BE WARNED ABOUT** — and this is the
     * test that caught the Gate 2 blocker.
     *
     * The same flex-sized viewport, over content SHORTER than itself: a list still
     * loading, a page with one item on it, M7's virtualized list on its first under-full
     * frame. **This is not a mistake.** It is the ordinary case, and it starts scrolling
     * the moment the content grows past the viewport.
     *
     * The shipped condition was `if (scroll.layoutHeight < content.layoutHeight - EPSILON)
     * return` — an "at LEAST as tall as its content" test, where the design and the
     * method's own KDoc both say **exactly**. So this ordinary shape got a warning that
     * *stated a falsehood* ("it computed to 200.0dp, which is exactly its content's
     * height" — it is twice it) and then prescribed a fix the author had already applied.
     * Revert the comparison to `<` and this test — and only this test — goes red.
     */
    @Test fun a_viewport_TALLER_than_its_content_warns_about_nothing() {
        val host = SyntheticHost()
        host.render(grownScrollTree(parentHeight = VIEW_H, contentHeight = 100f))

        host.read {
            val scroll = scrollUnderWrapper(host.root)
            val content = scroll.getChildAt(0) as ViewGroup
            assertEquals("the viewport is 200 tall", VIEW_H, scroll.height / density(), 0.5f)
            assertEquals("…and its content is only 100 — there is nothing to scroll YET",
                100f, content.height / density(), 0.5f)
            assertTrue("A VIEWPORT TALLER THAN ITS CONTENT IS NOT A MISTAKE. It is a viewport " +
                "with nothing to scroll YET — the ordinary case for any list that is still " +
                "loading, and for M7's virtualized list on its first under-full frame. A " +
                "diagnostic that cries wolf on the shape the docs prescribe is worse than no " +
                "diagnostic (got: ${host.mapper.scrollDiagnostics})",
                host.mapper.scrollDiagnostics.isEmpty())
        }
    }

    /**
     * **`Grow="1"` ALONE IS NOT A DEFINITE HEIGHT — AND EVERY DOC IN THIS PHASE SAID IT
     * WAS.** (Found on the AVD while writing the two tests above; the design, `BnScroll`'s
     * XML doc, `BnScrollDemo`'s header and the warning's own message all recommended it.)
     *
     * **It is the phase's own mechanism, one level up, and nobody looked.** A `Grow="1"`
     * scroll node leaves `flexBasis: auto`, so its flex BASIS is its CONTENT's height —
     * 800. Against a 200-high parent the free space is `200 − 800 = −600`: **NEGATIVE**.
     * `flexGrow` only ever distributes POSITIVE free space, so it never gets a say;
     * negative free space goes to the **SHRINK** pass, in proportion to `flexShrink` —
     * **which Yoga defaults to 0.** Nothing shrinks. **The viewport keeps its 800, spills
     * out of its 200-high parent, and viewport == content: there is nothing to scroll.**
     *
     * This is the *exact* sentence the design writes about the CONTENT node
     * (`YogaScrollNodeAndroidTest`, non-negotiable #6) — it is just as true of the
     * VIEWPORT, and the recommendation was written without checking.
     *
     * So the diagnostic is **right** to fire here, and this test pins that it does. The
     * shapes that actually bound a viewport are: an explicit `Height`; or
     * `Grow="1" Basis="0"` (CSS's `flex: 1` — basis 0 makes the free space positive);
     * or `Grow="1" Shrink="1"` (which lets the shrink pass take the −600 back off).
     */
    @Test fun a_Grow_ONLY_scroll_node_does_NOT_get_a_definite_height_and_is_warned_about() {
        val host = SyntheticHost()
        host.render(grownScrollTree(parentHeight = VIEW_H, contentHeight = CONTENT_H, growOnly = true))

        host.read {
            val d = density()
            val scroll = scrollUnderWrapper(host.root)
            val content = scroll.getChildAt(0) as ViewGroup

            assertEquals("Grow=\"1\" with flexBasis:auto takes its BASIS from its content (800), " +
                "and the free space against a 200-high parent is NEGATIVE (−600). flexGrow only " +
                "distributes POSITIVE free space; the negative goes to the SHRINK pass, and " +
                "Yoga's flexShrink default is 0. So NOTHING SHRINKS and the viewport keeps its " +
                "800 — spilling out of its 200-high parent.", CONTENT_H, scroll.height / d, 0.5f)
            assertEquals("…and it is exactly as tall as its content, so THERE IS NOTHING TO " +
                "SCROLL", content.height, scroll.height)

            val warnings = host.mapper.scrollDiagnostics.filter { it.contains("definite height") }
            assertEquals("THE DIAGNOSTIC IS RIGHT TO FIRE HERE, and this is the shape the design, " +
                "BnScroll's XML doc, BnScrollDemo's header and the warning's OWN MESSAGE all " +
                "recommended until the Gate 2 review. `Grow=\"1\"` alone does not bound a " +
                "viewport. Use an explicit Height, or Grow + Basis=\"0\" (CSS's `flex: 1`), or " +
                "Grow + Shrink=\"1\". (got: ${host.mapper.scrollDiagnostics})",
                1, warnings.size)
        }
    }

    /**
     * **AND THE FIRST CONDITION EARNS ITS KEEP TOO.** A scroll node with an EXPLICIT
     * `Height="800"` over exactly 800 of content computes out equal to its content — so
     * it passes the second condition — and it must still say nothing: the author gave it
     * a definite height, which is what the warning would tell them to do. (Its content
     * may well grow past it on the next frame; nothing is wrong here.)
     *
     * Delete the POINT/PERCENT check and this test — and only this test — goes red.
     */
    @Test fun a_definite_height_that_happens_to_equal_its_content_warns_about_nothing() {
        val host = SyntheticHost()
        host.render(scrollTree(viewportHeight = CONTENT_H))   // Height="800" over 800 of content

        host.read {
            val scroll = scrollViewOf(host.root)
            val content = contentViewOf(host.root)
            assertEquals("the viewport and its content are the same height, to the dp",
                content.height, scroll.height)
            assertTrue("…and the author DECLARED that height, which is exactly what the warning " +
                "would have told them to do. Both conditions are needed, and this is the one " +
                "that pins the first (got: ${host.mapper.scrollDiagnostics})",
                host.mapper.scrollDiagnostics.isEmpty())
        }
    }

    /**
     * **THE DIAGNOSTICS BOOKKEEPING DIES WITH ITS NODE.**
     *
     * `removeNode` evicts [YogaLayout.contentNodes] because node ids are **reused** —
     * .NET's ids restart at 1 after a reset, so a retired id is handed straight back
     * out on the next page. The warn-once keys are keyed by the same ids and were NOT
     * evicted: so the diagnostics set grew monotonically across every navigation, and —
     * worse — a genuinely broken scroll node that inherited a retired id **got no
     * warning at all**, silenced by a ghost.
     *
     * Mount a broken (auto-height) scroll node, navigate away, mount another broken one
     * with the same id. It must be told. Twice broken, twice warned.
     */
    @Test fun a_scroll_node_that_REUSES_a_retired_id_gets_its_own_warning() {
        val host = SyntheticHost()
        host.render(scrollTree(viewportHeight = null))
        assertEquals("the first auto-height scroll node is warned about", 1,
            host.read { host.mapper.scrollDiagnostics.size })

        // Navigate away: ONE RemoveNodePatch for the page.
        host.render(listOf(RenderPatch.RemoveNode(nodeId = 1)))
        assertTrue("the diagnostics go with the node they belong to — otherwise the list grows " +
            "by one message per navigation, forever",
            host.read { host.mapper.scrollDiagnostics.isEmpty() })

        // …and the next page's scroll node inherits the retired id 1.
        host.render(scrollTree(viewportHeight = null))

        host.read {
            val warnings = host.mapper.scrollDiagnostics.filter { it.contains("definite height") }
            assertEquals("THE PIN: a scroll node that REUSES a retired id must get its OWN " +
                "warning. Keep the warn-once key past its node's death and this genuinely broken " +
                "node is warned about NOTHING — and the diagnostic is worth most on a " +
                "freshly-written page, which is exactly when a ghost key eats it " +
                "(got: ${host.mapper.scrollDiagnostics})",
                1, warnings.size)
        }
    }
}
