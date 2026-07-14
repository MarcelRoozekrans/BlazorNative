package io.blazornative.shell

import android.view.View
import android.widget.Button
import android.widget.EditText
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
 * Phase 2.8 Task 3b regression test.
 *
 * The renderer emits Blazor-style child text frames for content like
 *   <button>Tap</button>
 * as a CreateNode(text) + ReplaceText pair whose parentId points at the
 * button. Android's Button (and EditText, plain TextView, etc.) is NOT a
 * ViewGroup — it's a TextView subclass. Pre-fix, the mapper would orphan
 * the child text node to widget_root because `as? ViewGroup` returned null
 * for the parent. Post-fix, the mapper detects "text child of TextView-
 * but-not-ViewGroup" and collapses the text into the parent's setText,
 * matching the React Native text-content pattern.
 *
 * These tests synthesize the renderer's patch stream directly so we can
 * assert the post-fix behavior without going through the full runtime boot.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperTextChildOnButtonTest {

    @Test fun text_child_of_button_collapses_into_button_setText() {
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "button", parentId = null),
            RenderPatch.CreateNode(nodeId = 2, nodeType = "text",   parentId = 1),
            RenderPatch.ReplaceText(nodeId = 2, text = "Tap")
        ))
        assertTrue("expected single child to be Button, got ${view::class.simpleName}",
            view is Button)
        assertEquals("Tap", (view as Button).text.toString())
    }

    @Test fun text_child_of_edittext_collapses_into_edittext_setText() {
        // EditText also extends TextView (not ViewGroup) — same code path.
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "input", parentId = null),
            RenderPatch.CreateNode(nodeId = 2, nodeType = "text",  parentId = 1),
            RenderPatch.ReplaceText(nodeId = 2, text = "default value")
        ))
        assertTrue("expected single child to be EditText, got ${view::class.simpleName}",
            view is EditText)
        assertEquals("default value", (view as EditText).text.toString())
    }

    @Test fun root_childCount_stays_1_when_button_has_text_child() {
        // The widget_root should have exactly ONE child (the Button) — the
        // text child should NOT be orphaned as a sibling at root level.
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        lateinit var root: FrameLayout
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            root = FrameLayout(ctx)
            val mapper = WidgetMapper(ctx, root)
            mapper.apply(RenderFrame(
                frameId = 1, timestampMs = 0L,
                patches = listOf(
                    RenderPatch.CreateNode(nodeId = 1, nodeType = "button", parentId = null),
                    RenderPatch.CreateNode(nodeId = 2, nodeType = "text",   parentId = 1),
                    RenderPatch.ReplaceText(nodeId = 2, text = "Tap"),
                    RenderPatch.CommitFrame(1, 0L)
                )
            ))
        }
        InstrumentationRegistry.getInstrumentation().waitForIdleSync()
        var actualChildCount = -1
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            actualChildCount = root.childCount
        }
        assertEquals("widget_root should have exactly 1 child (Button) — text child must NOT orphan",
            1, actualChildCount)
    }

    /**
     * **THE ALIAS, ON THE REMOVE PATH (Phase 6.1).** A collapsed text node's map
     * entry points at its PARENT's view — the Button — which it does not own. So a
     * `RemoveNode` for that text id must drop the map entry and NOTHING else.
     *
     * Untracked, it removes the wrong things in both trees: `removeView(nodes[textId])`
     * detaches the **Button** (pre-6.1 behaviour, already wrong), while
     * `yoga.removeNode(textId)` correctly no-ops — so Yoga keeps laying out and
     * reserving space for a widget that is no longer in the view hierarchy, and
     * every sibling after it is offset by a GHOST. Reachable whenever a button's
     * text child is conditionally removed.
     */
    @Test fun removing_a_collapsed_text_node_leaves_its_parent_button_on_screen() {
        val root = render(
            listOf(
                create(1, "view", null), style(1, "width", "300"),
                create(2, "button", 1),
                create(3, "text", 2),          // COLLAPSED onto the Button — no view, no Yoga node
                text(3, "Tap"),
                create(4, "view", 1), style(4, "width", "50"), style(4, "height", "50"),
            ),
            // Frame 2: the button's text child is conditionally removed.
            listOf(RenderPatch.RemoveNode(nodeId = 3)),
        )
        val col = root.getChildAt(0) as android.view.ViewGroup

        assertEquals("removing the COLLAPSED text child must NOT detach its parent Button — " +
            "nodes[textId] IS the Button, and removeView() on it takes the whole widget off screen",
            2, col.childCount)
        val button = col.getChildAt(0)
        assertTrue("child 0 must still be the Button", button is Button)
        assertEquals("Tap", (button as Button).text.toString())
        assertTrue("the Button must still have a measured frame", button.height > 0)
        assertEquals("the sibling must still start exactly where the Button ends — with the " +
            "Button detached but its Yoga node alive, this space would be reserved for a GHOST",
            button.bottom, col.getChildAt(1).top)
    }

    // ── Helper (duplicated from sibling test files for now; share later) ──

    private fun renderFrame(patches: List<RenderPatch>): View {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        lateinit var root: FrameLayout
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            root = FrameLayout(ctx)
            val mapper = WidgetMapper(ctx, root)
            mapper.apply(RenderFrame(
                frameId = 1, timestampMs = 0L,
                patches = patches + RenderPatch.CommitFrame(1, 0L)
            ))
        }
        InstrumentationRegistry.getInstrumentation().waitForIdleSync()
        var result: View? = null
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            result = root.getChildAt(0)
        }
        return requireNotNull(result) { "no child created in root after apply" }
    }
}
