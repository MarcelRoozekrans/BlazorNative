package io.blazornative.shell

import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.util.TypedValue
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
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
 * SetStyle handler tests — **Phase 6.1: the handler is now a ROUTER.**
 *
 * The SetStyle allow-list is PARTITIONED (design §"The allow-list is a routing
 * table"), and each style name has exactly one destination:
 *
 *  - **VISUAL** (`backgroundColor`, `fontSize`, …) → the **View**. Paint. These
 *    are the Phase 2.6 handlers, and they are unchanged — including their
 *    tolerant `dp`/`sp`/`px` suffix strip, which survives for the legacy visual
 *    props only (the layout grammar has no unit suffixes).
 *  - **LAYOUT** (`padding`, `margin`, `width`, `height`, every flex property) →
 *    the **Yoga node**, and NOWHERE ELSE.
 *
 * TWO Phase-2.6 assertions were INVERTED by this phase, and their inversion is
 * the point:
 *
 * 1. `padding` used to call `view.setPadding(...)`. It must not any more: Yoga
 *    lays a container's children out INSIDE its padding box, so a surviving
 *    setPadding applies the inset a SECOND time and every child in that
 *    container is off by it. [padding_insets_the_children_not_the_view] pins
 *    both halves — the children move, the View's own padding stays 0.
 * 2. "unknown property → still a LinearLayout" was a type pin, and the type is
 *    gone. [unknown_property_is_logged_and_ignored] pins what actually matters:
 *    an unknown name changes NO FRAME. (It is logged; logcat is not an assertion
 *    surface, so the pin is the absence of an effect.)
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperSetStyleTest {

    @Test fun backgroundColor_sets_view_background() {
        val view = renderSingle(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "view", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "backgroundColor", value = "#FF0000")
        ))
        val bg = (view.background as? ColorDrawable)?.color
        assertEquals("backgroundColor should set the view's ColorDrawable to RED", Color.RED, bg)
    }

    @Test fun fontSize_sets_TextView_text_size_in_sp() {
        val view = renderSingle(listOf(
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
        val view = renderSingle(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "text", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "fontSize", value = "18sp")
        ))
        val tv = view as TextView
        val expectedPx = TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_SP, 18f, tv.context.resources.displayMetrics)
        assertEquals("fontSize='18sp' should be parsed as 18 sp (the LEGACY visual props keep " +
            "their tolerant suffix strip; the layout grammar has no suffixes)",
            expectedPx, tv.textSize, 0.5f)
    }

    /** THE INVERSION (non-negotiable #8). `padding` is LAYOUT: it belongs to the
     * Yoga node, which insets the container's CHILDREN. The View's own padding
     * must stay 0 — a surviving `view.setPadding(...)` would double-apply it. */
    @Test fun padding_insets_the_children_not_the_view() {
        val root = render(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "view", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "width", value = "300"),
            RenderPatch.SetStyle(nodeId = 1, property = "height", value = "100"),
            RenderPatch.SetStyle(nodeId = 1, property = "padding", value = "16"),
            RenderPatch.CreateNode(nodeId = 2, nodeType = "view", parentId = 1),
            RenderPatch.SetStyle(nodeId = 2, property = "width", value = "50"),
            RenderPatch.SetStyle(nodeId = 2, property = "height", value = "50"),
        ))
        val box = root.getChildAt(0) as ViewGroup
        val d = box.context.resources.displayMetrics.density
        val child = box.getChildAt(0)
        assertEquals("padding=16 must inset the CHILD by 16dp on the main axis",
            16f, child.left / d, 0.5f)
        assertEquals("…and on the cross axis", 16f, child.top / d, 0.5f)
        assertEquals("the View's own padding must stay ZERO — Yoga already applied the inset, " +
            "and view.setPadding would apply it a second time", 0, box.paddingLeft)
        assertEquals(0, box.paddingTop)
    }

    /** THE OTHER INVERSION. The old premise was "unknown property → still a
     * LinearLayout"; the type is gone, so the assertion is now the honest one:
     * an unknown name is logged and ignored, and NO FRAME MOVES. */
    @Test fun unknown_property_is_logged_and_ignored() {
        val root = render(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "view", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "width", value = "120"),
            RenderPatch.SetStyle(nodeId = 1, property = "height", value = "60"),
            RenderPatch.SetStyle(nodeId = 1, property = "boxShadow", value = "5px 5px"),
        ))
        val view = root.getChildAt(0)
        val d = view.context.resources.displayMetrics.density
        assertEquals("an unknown style must not disturb the computed width", 120f, view.width / d, 0.5f)
        assertEquals("…nor the computed height", 60f, view.height / d, 0.5f)
        assertTrue("…nor take the view down with it", view is ViewGroup)
    }

    /**
     * An UNPARSEABLE value for a KNOWN layout property is the same contract:
     * logged and ignored, never guessed and never clamped. `12px` is not in the
     * grammar — there are NO unit suffixes (px is not dp, sp is font-scaled, and
     * neither exists on iOS) — so `width` must never be set at all.
     *
     * The node therefore keeps Yoga's default `width: auto`, which for a
     * top-level child of the host root means it STRETCHES to the host's width
     * (Yoga's default `alignItems: stretch`) — 400dp, the width [render] gives
     * the host. "Still stretched" is the observable proof the setter never ran;
     * a shell that guessed `12` would show 12.
     */
    @Test fun unparseable_layout_value_is_logged_and_ignored() {
        val root = render(listOf(
            RenderPatch.CreateNode(nodeId = 1, nodeType = "view", parentId = null),
            RenderPatch.SetStyle(nodeId = 1, property = "height", value = "60"),
            RenderPatch.SetStyle(nodeId = 1, property = "width", value = "12px"),
        ))
        val view = root.getChildAt(0)
        val d = view.context.resources.displayMetrics.density
        assertEquals("'12px' must be IGNORED, not read as 12 — the node must keep Yoga's " +
            "default width:auto and stay stretched to the 400dp host",
            400f, view.width / d, 0.5f)
        assertEquals("the rest of the node is untouched", 60f, view.height / d, 0.5f)
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private fun renderSingle(patches: List<RenderPatch>): View = render(patches).getChildAt(0)

    private fun render(patches: List<RenderPatch>): FrameLayout {
        val instr = InstrumentationRegistry.getInstrumentation()
        val ctx = instr.targetContext
        val d = ctx.resources.displayMetrics.density
        lateinit var root: FrameLayout
        instr.runOnMainSync {
            root = FrameLayout(ctx)
            root.layout(0, 0, (400 * d).toInt(), (800 * d).toInt())
            WidgetMapper(ctx, root).apply(RenderFrame(
                frameId = 1, timestampMs = 0L,
                patches = patches + RenderPatch.CommitFrame(1, 0L)
            ))
        }
        instr.waitForIdleSync()
        var ok = false
        instr.runOnMainSync { ok = root.childCount > 0 }
        assertTrue("no child created in root after apply", ok)
        return root
    }
}
