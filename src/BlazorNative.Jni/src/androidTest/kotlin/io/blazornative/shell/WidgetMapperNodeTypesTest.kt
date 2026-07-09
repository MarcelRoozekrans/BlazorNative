package io.blazornative.shell

import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.ScrollView
import android.widget.Spinner
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 2.6 NodeType coverage tests.
 *
 * Asserts that each of the 5 unexercised NodeTypes from DoD #6 actually
 * instantiate the correct Android widget class when their CreateNodePatch
 * arrives. View (mapped to LinearLayout) and text (mapped to TextView) are
 * covered by Phase 2.5's WidgetMapperTest end-to-end run.
 *
 * Test approach: synthetic frame fixtures via in-process WidgetMapper.
 * No runtime boot — each test is ~1s vs. WidgetMapperTest's full
 * end-to-end mount. Tests run on the AVD (instrumented) because Android
 * View construction requires a real Context + Looper.
 *
 * Threading: mapper.apply(frame) posts the batch to mainHandler.post.
 * runOnMainSync puts us on the main thread, but the inner Handler.post
 * still queues for the next loop iteration — so waitForIdleSync() drains
 * it before we read the resulting view tree.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperNodeTypesTest {

    @Test fun creates_Button_from_button_nodetype() {
        val view = renderSingleNode("button")
        assertTrue("expected Button, got ${view::class.simpleName}", view is Button)
    }

    @Test fun creates_EditText_from_input_nodetype() {
        val view = renderSingleNode("input")
        assertTrue("expected EditText, got ${view::class.simpleName}", view is EditText)
    }

    @Test fun creates_ImageView_from_image_nodetype() {
        val view = renderSingleNode("image")
        assertTrue("expected ImageView, got ${view::class.simpleName}", view is ImageView)
    }

    @Test fun creates_ScrollView_from_scroll_nodetype() {
        val view = renderSingleNode("scroll")
        assertTrue("expected ScrollView, got ${view::class.simpleName}", view is ScrollView)
    }

    @Test fun creates_Spinner_from_picker_nodetype() {
        val view = renderSingleNode("picker")
        assertTrue("expected Spinner, got ${view::class.simpleName}", view is Spinner)
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private fun renderSingleNode(nodeType: String): View =
        renderFrame(listOf(RenderPatch.CreateNode(nodeId = 1, nodeType = nodeType, parentId = null)))

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
