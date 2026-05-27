package io.blazornative.shell

import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.util.TypedValue
import android.view.View
import android.widget.FrameLayout
import android.widget.LinearLayout
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
 * Phase 2.6 SetStyle handler tests.
 *
 * Covers the three initial style properties — backgroundColor (any View),
 * fontSize (TextView only, sp units), and padding (any View, dp→px, all 4
 * sides equal). Unknown property names should log-and-ignore without
 * crashing.
 *
 * Coercion notes verified by these tests:
 *  - backgroundColor accepts "#RRGGBB" via Color.parseColor.
 *  - fontSize accepts both bare numbers ("24") and sp-suffixed values ("18sp");
 *    measured in pixels post-conversion via TypedValue.applyDimension.
 *  - padding values treated as dp; converted to pixels via
 *    TypedValue.applyDimension(COMPLEX_UNIT_DIP, ...).
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperSetStyleTest {

    @Test fun backgroundColor_sets_view_background() {
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "view", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "backgroundColor", value = "#FF0000")
        ))
        val bg = (view.background as? ColorDrawable)?.color
        assertEquals("backgroundColor should set the view's ColorDrawable to RED", Color.RED, bg)
    }

    @Test fun fontSize_sets_TextView_text_size_in_sp() {
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "text", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "fontSize", value = "24")
        ))
        val tv = view as TextView
        val expectedPx = TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_SP, 24f, tv.context.resources.displayMetrics)
        assertEquals("fontSize=24 should set textSize to 24sp in pixels",
            expectedPx, tv.textSize, 0.5f)
    }

    @Test fun fontSize_strips_sp_suffix() {
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "text", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "fontSize", value = "18sp")
        ))
        val tv = view as TextView
        val expectedPx = TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_SP, 18f, tv.context.resources.displayMetrics)
        assertEquals("fontSize='18sp' should be parsed as 18 sp",
            expectedPx, tv.textSize, 0.5f)
    }

    @Test fun padding_sets_all_four_sides_equal() {
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "view", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "padding", value = "16")
        ))
        val expectedPx = TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP, 16f, view.context.resources.displayMetrics).toInt()
        assertEquals("padding=16 should set paddingTop to 16dp in px", expectedPx, view.paddingTop)
        assertEquals("padding=16 should set paddingLeft", expectedPx, view.paddingLeft)
        assertEquals("padding=16 should set paddingBottom", expectedPx, view.paddingBottom)
        assertEquals("padding=16 should set paddingRight", expectedPx, view.paddingRight)
    }

    @Test fun unknown_property_logs_and_ignores() {
        val view = renderFrame(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "view", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "boxShadow", value = "5px 5px")
        ))
        // No-op; assert the view was still created and didn't crash.
        assertTrue("expected LinearLayout (mapped from 'view') despite unknown style",
            view is LinearLayout)
    }

    // ── Helper (duplicated from WidgetMapperNodeTypesTest, ~18 lines) ──

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
