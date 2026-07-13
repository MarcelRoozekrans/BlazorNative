package io.blazornative.shell

import android.view.ViewGroup
import android.widget.FrameLayout
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.1 Task 2.3 — RELAYOUT ON HOST RESIZE.
 *
 * Yoga solved the tree against the host's bounds; when those bounds change
 * (rotation, split-screen, any resize) the solution is stale. **No patch is
 * involved** — .NET does not know the host got wider, and nothing in the render
 * tree changed — so the shell must re-run calculate+apply off the host's own
 * layout signal. [YogaLayout] listens on the host root's layout and recomputes
 * when (and only when) its bounds actually changed.
 *
 * The test drives the host root's [android.view.View.layout] directly — that is
 * the exact call the framework makes on a real resize, and it is deterministic,
 * where rotating the AVD under instrumentation is not.
 *
 * The tree is written in PERCENTS on purpose: a percent width is the only kind
 * whose computed frame is a FUNCTION of the host's size, so a stale layout is
 * visible as a wrong number rather than as nothing at all.
 */
@RunWith(AndroidJUnit4::class)
class YogaResizeAndroidTest {

    @Test fun frames_recompute_when_the_host_root_is_resized() {
        val instr = InstrumentationRegistry.getInstrumentation()
        val ctx = instr.targetContext
        val d = ctx.resources.displayMetrics.density
        lateinit var root: FrameLayout

        instr.runOnMainSync {
            root = FrameLayout(ctx)
            root.layout(0, 0, (200 * d).toInt(), (400 * d).toInt())
            WidgetMapper(ctx, root).apply(RenderFrame(
                frameId = 1, timestampMs = 0L,
                patches = listOf(
                    // A full-width row; its child takes half of it.
                    RenderPatch.CreateNode(1, "view", null),
                    RenderPatch.SetStyle(1, "width", "100%"),
                    RenderPatch.SetStyle(1, "height", "50"),
                    RenderPatch.CreateNode(2, "view", 1),
                    RenderPatch.SetStyle(2, "width", "50%"),
                    RenderPatch.SetStyle(2, "height", "50"),
                    RenderPatch.CommitFrame(1, 0L),
                ),
            ))
        }
        instr.waitForIdleSync()

        instr.runOnMainSync {
            val row = root.getChildAt(0) as ViewGroup
            assertEquals("at a 200dp host: the 100%-wide row is 200dp",
                200f, row.width / d, 0.5f)
            assertEquals("at a 200dp host: its 50% child is 100dp",
                100f, row.getChildAt(0).width / d, 0.5f)
        }

        // THE RESIZE. No patch, no re-render — just new host bounds, exactly as
        // the framework delivers them after a rotation.
        instr.runOnMainSync { root.layout(0, 0, (400 * d).toInt(), (300 * d).toInt()) }
        instr.waitForIdleSync()

        instr.runOnMainSync {
            val row = root.getChildAt(0) as ViewGroup
            assertEquals("after the host doubled in width, the 100% row must have RE-SOLVED to 400dp " +
                "— a stale layout would still read 200", 400f, row.width / d, 0.5f)
            assertEquals("and its 50% child to 200dp",
                200f, row.getChildAt(0).width / d, 0.5f)
        }
    }
}
