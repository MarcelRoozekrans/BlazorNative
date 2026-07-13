package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
// IMPORTANT: import path for R matches android.namespace in build.gradle.kts — verify in Step 7.1.
import io.blazornative.shell.R
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 2.5 / 2.8 end-to-end widget-mapper assertion — **rewritten to FRAMES in
 * Phase 6.1**.
 *
 * Launches MainActivity (which boots the NativeAOT runtime on a background
 * thread, posts adapter-decoded patches to the main looper via WidgetMapper),
 * polls widget_root for the rendered tree, asserts the result.
 *
 * Mounts HelloComponent via an explicit [MainActivity.EXTRA_COMPONENT] extra
 * (the no-extra default is "BnDemo" since Phase 3.4 Gate 4). Expected tree:
 *   widget_root: FrameLayout
 *     └── outer container (from outer <div>)
 *           ├── inner container (from inner <div>)
 *           │     └── TextView ("Hello, BlazorNative! (taps: 0)")   [counter since Phase 3.2]
 *           ├── Button ("Tap")
 *           └── EditText (hint="Type here...")
 *
 * ── WHAT PHASE 6.1 CHANGED, AND WHY THIS TEST GOT STRONGER ───────────────────
 * It used to pin `outer is LinearLayout` and `orientation == VERTICAL` — i.e. it
 * asserted the MECHANISM of the vertical stack. Yoga is the mechanism now, and
 * the containers are plain FrameLayouts whose children are absolutely placed, so
 * those pins are gone. What replaced them is the thing they were a proxy for:
 * **the children actually stack.** Each child starts at x = 0 and at exactly the
 * y its predecessor ended — asserted on the real, computed frames.
 *
 * That is the stronger assertion, and it is the load-bearing regression signal of
 * the whole phase: HelloComponent is an UN-STYLED tree, and an un-styled tree must
 * lay out exactly as it did before the engine was swapped. It does, because Yoga's
 * default flexDirection is `column`.
 *
 * Phase 2.8 Task 3b's text-child-of-TextView collapse still holds (the Button's
 * text child is absorbed onto Button.setText rather than orphaning as a sibling)
 * — see WidgetMapperTextChildOnButtonTest, and note the collapse is now also what
 * keeps the Yoga tree's child indices aligned with the view tree's.
 *
 * Polling loop: the 120s deadline is a wasm-era relic; the poller breaks as soon
 * as widget_root has children, so it only bounds fail-detection latency.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperTest {

    @Test
    fun hello_renders_as_a_vertical_stack_of_computed_frames() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "HelloComponent")
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            val deadline = System.currentTimeMillis() + 120_000
            val ready = AtomicReference(false)

            while (System.currentTimeMillis() < deadline) {
                scenario.onActivity { act ->
                    val root = act.findViewById<FrameLayout>(R.id.widget_root)
                    // Wait for the frames too, not just the views: the mount batch
                    // creates the views and lays them out in the same applyBatch,
                    // but a zero-height child would mean we read mid-flight.
                    val outer = root?.takeIf { it.childCount > 0 }?.getChildAt(0)
                    if (outer != null && outer.height > 0) ready.set(true)
                }
                if (ready.get()) break
                Thread.sleep(250)
            }

            assertTrue("widget_root never received a laid-out tree — mapper did not apply", ready.get())

            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)
                assertEquals("widget_root should have exactly one top-level child (the outer <div>)",
                    1, root.childCount)

                val outer = root.getChildAt(0)
                assertTrue("the outer <div> must be a container", outer is ViewGroup)
                outer as ViewGroup
                assertEquals("outer container should hold 3 children (inner div + button + input)",
                    3, outer.childCount)

                // THE VERTICAL-STACK PIN, as frames. An un-styled tree is a Yoga
                // column: children at x = 0, each starting exactly where the last
                // one ended, none of them empty.
                assertStacksVertically(outer)

                // Child [0]: inner div → container holding the Hello TextView.
                val innerDiv = outer.getChildAt(0)
                assertTrue("first child should be the inner container", innerDiv is ViewGroup)
                innerDiv as ViewGroup
                assertEquals("inner container should hold 1 child (the Hello text)",
                    1, innerDiv.childCount)
                val helloText = innerDiv.getChildAt(0)
                assertTrue("inner child should be a TextView", helloText is TextView)
                helloText as TextView
                // Phase 3.2: HelloComponent is interactive — the counter lives in
                // this text (fresh mount ⇒ taps: 0).
                assertEquals("Hello, BlazorNative! (taps: 0)", helloText.text.toString())
                // DoD #3, in passing: the label was MEASURED — the inner container
                // has no size of its own and hugs whatever the TextView reported.
                assertTrue("the Hello label must have a measured height", helloText.height > 0)
                assertEquals("the inner container must hug its measured label",
                    helloText.height, innerDiv.height)

                // Child [1]: button (with text collapsed via the Phase 2.8 fix).
                val button = outer.getChildAt(1)
                assertTrue("second child should be a Button", button is Button)
                assertEquals("Tap", (button as Button).text.toString())

                // Child [2]: input.
                val input = outer.getChildAt(2)
                assertTrue("third child should be an EditText", input is EditText)
                assertEquals("Type here...", (input as EditText).hint.toString())
            }
        }
    }

    /**
     * The frame form of "this container is a vertical stack": every child shares
     * the container's CONTENT-BOX left edge, every child is non-empty, and every
     * child is butted up against the previous one's bottom edge.
     *
     * The content-box edge is read from child [0] rather than pinned to 0,
     * because a container with `padding` insets its children — and after Phase
     * 6.1 that inset is Yoga's (the Yoga node lays children out inside the
     * padding box) rather than a `view.setPadding`, so `container.paddingLeft`
     * is 0 and the inset lives in the children's frames. HelloComponent's outer
     * div is padded; the pin is that the children agree on the edge and tile,
     * which is the whole claim.
     */
    private fun assertStacksVertically(container: ViewGroup) {
        val contentLeft = container.getChildAt(0).left
        var expectedTop = container.getChildAt(0).top
        for (i in 0 until container.childCount) {
            val child: View = container.getChildAt(i)
            assertEquals("child $i must share the container's content-box left edge",
                contentLeft, child.left)
            assertTrue("child $i must have a real height (got ${child.height}px)", child.height > 0)
            assertEquals("child $i must start exactly where child ${i - 1} ended " +
                "— an un-styled tree is a Yoga COLUMN", expectedTop, child.top)
            expectedTop = child.bottom
        }
    }
}
