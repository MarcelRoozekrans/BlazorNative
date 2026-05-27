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
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 2.6 UpdateProp handler tests.
 *
 * Covers placeholder (EditText-only) and enabled (universal) — the narrow
 * initial property set per Phase 2.6 design. Unknown property names should
 * log-and-ignore without crashing.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperUpdatePropTest {

    @Test fun placeholder_sets_EditText_hint() {
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "input", parentId = null),
            RenderPatch.UpdateProp(nodeId = 1, name = "placeholder", value = "Enter name")
        ))
        assertEquals("Enter name", (view as EditText).hint.toString())
    }

    @Test fun placeholder_on_non_EditText_logs_and_ignores() {
        // Renders a Button + placeholder; assert the Button was still created
        // and didn't crash. The placeholder ignore is silent (logged but no
        // observable state change).
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "button", parentId = null),
            RenderPatch.UpdateProp(nodeId = 1, name = "placeholder", value = "ignored")
        ))
        assertTrue("expected Button to be created", view is Button)
    }

    @Test fun enabled_false_disables_the_view() {
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "button", parentId = null),
            RenderPatch.UpdateProp(nodeId = 1, name = "enabled", value = "false")
        ))
        assertFalse("view should be disabled when enabled=false", view.isEnabled)
    }

    @Test fun enabled_null_defaults_to_enabled() {
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "button", parentId = null),
            RenderPatch.UpdateProp(nodeId = 1, name = "enabled", value = null)
        ))
        assertTrue("view should default to enabled when value=null", view.isEnabled)
    }

    // ── Helper (duplicated from WidgetMapperNodeTypesTest, ~10 lines) ──

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
