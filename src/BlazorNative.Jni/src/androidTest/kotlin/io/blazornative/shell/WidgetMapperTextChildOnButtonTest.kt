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
 * assert the post-fix behavior without going through the full .wasm boot.
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
