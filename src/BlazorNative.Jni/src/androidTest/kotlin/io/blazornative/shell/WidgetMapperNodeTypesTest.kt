package io.blazornative.shell

import android.view.View
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Spinner
import android.widget.Switch
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

    // ── Phase 7.3: the three new NodeTypes (wire ids 8/9/10) ───────────────
    // FRAMEWORK widgets, deliberately — this shell has no appcompat/Material
    // dependency (WidgetMapper.handleCreate records the decision).

    @Test fun creates_CheckBox_from_checkbox_nodetype() {
        val view = renderSingleNode("checkbox")
        assertTrue("expected CheckBox, got ${view::class.simpleName}", view is CheckBox)
    }

    @Test fun creates_Switch_from_switch_nodetype() {
        val view = renderSingleNode("switch")
        assertTrue("expected Switch, got ${view::class.simpleName}", view is Switch)
    }

    @Test fun creates_SeekBar_from_slider_nodetype() {
        val view = renderSingleNode("slider")
        assertTrue("expected SeekBar, got ${view::class.simpleName}", view is SeekBar)
    }

    // ── Phase 7.4: the two new NodeTypes (wire ids 11/12) ──────────────────

    @Test fun creates_indeterminate_ProgressBar_from_activityindicator_nodetype() {
        val view = renderSingleNode("activityindicator")
        assertTrue("expected ProgressBar, got ${view::class.simpleName}", view is ProgressBar)
        assertTrue("the indicator must be INDETERMINATE — animating while mounted IS the " +
            "contract (no start/stop prop exists for two shells to keep equal)",
            (view as ProgressBar).isIndeterminate)
    }

    @Test fun creates_anchor_plus_overlay_from_modal_nodetype() {
        // A `modal` is TWO shell-side pieces (design decision 1), so the
        // single-node helper is not enough: the wire slot holds the ANCHOR (a
        // plain View — it can host nothing; children redirect to the overlay)
        // and the host root's LAST child is the OVERLAY container.
        val host = SyntheticHost()
        host.render(listOf(create(1, "modal", null)))
        host.read {
            assertTrue("expected anchor + overlay under the host root",
                host.root.childCount == 2)
            val anchor = host.root.getChildAt(0)
            val overlay = host.root.getChildAt(1)
            assertTrue("the wire slot holds a plain-View ANCHOR, got " +
                "${anchor::class.simpleName}", anchor !is android.view.ViewGroup)
            assertTrue("the last child is the OVERLAY container, got " +
                "${overlay::class.simpleName}", overlay is BnYogaFrameLayout)
        }
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
