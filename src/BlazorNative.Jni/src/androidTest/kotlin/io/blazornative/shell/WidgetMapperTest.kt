package io.blazornative.shell

import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
// IMPORTANT: import path for R matches android.namespace in build.gradle.kts — verify in Step 7.1.
import io.blazornative.shell.R
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 2.5 / 2.8 end-to-end widget-mapper assertion.
 *
 * Launches MainActivity (which boots the NativeAOT runtime on a background
 * thread, posts adapter-decoded patches to the main looper via WidgetMapper),
 * polls the
 * widget_root for the rendered tree, asserts the resulting view tree.
 *
 * As of Phase 2.8 Task 1, Main mounts the HelloComponent. Expected tree:
 *   widget_root: FrameLayout
 *     └── outer LinearLayout (from outer <div>)
 *           ├── inner LinearLayout (from inner <div>)
 *           │     └── TextView ("Hello, BlazorNative! (taps: 0)")   [counter since Phase 3.2]
 *           ├── Button ("Tap")
 *           └── EditText (hint="Type here...")
 *
 * Phase 2.8 Task 3b fixed the text-child-of-TextView collapse in WidgetMapper
 * so that Blazor's `<button>Tap</button>` (a button with a text-node child)
 * renders correctly: the text-node child is absorbed onto Button.setText
 * instead of orphaning as a sibling at widget_root. See
 * WidgetMapperTextChildOnButtonTest for the targeted regression coverage.
 *
 * Polling loop: the 120s deadline is a wasm-era relic (cold wasmtime JIT of
 * the ~14 MB .wasm took 30-75s; the NativeAOT pipeline mounts in ~1-2s since
 * Phase 3.0d). Kept as generous headroom — the poller breaks as soon as
 * widget_root has children (the commit-frame post has fired), so the deadline
 * only bounds fail-detection latency. All assertions then run inside
 * scenario.onActivity { } so they see the latest widget tree on the UI thread.
 *
 * The test method name retains the Phase 2.5 "sentinel_" prefix to avoid churn;
 * the assertions now target HelloComponent's shape.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperTest {

    @Test
    fun sentinel_renders_as_linearlayout_containing_textview() {
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            val deadline = System.currentTimeMillis() + 120_000
            val ready = AtomicReference(false)

            while (System.currentTimeMillis() < deadline) {
                scenario.onActivity { act ->
                    val root = act.findViewById<FrameLayout>(R.id.widget_root)
                    if (root != null && root.childCount > 0) ready.set(true)
                }
                if (ready.get()) break
                Thread.sleep(250)
            }

            assertTrue("widget_root never received children within 60s — mapper did not apply", ready.get())

            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)
                assertEquals("widget_root should have exactly one top-level child (the outer <div> container)",
                    1, root.childCount)

                val outer = root.getChildAt(0)
                assertTrue("top-level child should be a LinearLayout (mapped from outer <div>)",
                    outer is LinearLayout)
                outer as LinearLayout
                assertEquals("outer LinearLayout should be vertical",
                    LinearLayout.VERTICAL, outer.orientation)
                assertEquals("outer LinearLayout should contain 3 children (inner div + button + input)",
                    3, outer.childCount)

                // Child [0]: inner div → LinearLayout containing the Hello TextView
                val innerDiv = outer.getChildAt(0)
                assertTrue("first child should be inner LinearLayout (mapped from inner <div>)",
                    innerDiv is LinearLayout)
                innerDiv as LinearLayout
                assertEquals("inner LinearLayout should contain 1 child (the Hello text)",
                    1, innerDiv.childCount)
                val helloText = innerDiv.getChildAt(0)
                assertTrue("inner child should be a TextView", helloText is TextView)
                helloText as TextView
                // Phase 3.2: HelloComponent is interactive — the counter lives
                // in this text (fresh mount ⇒ taps: 0; the tap round-trip is
                // EventRoundTripAndroidTest's job, not this shape test's).
                assertEquals("Hello, BlazorNative! (taps: 0)", helloText.text.toString())

                // Child [1]: button (with text collapsed via Phase 2.8 Task 3b fix)
                val button = outer.getChildAt(1)
                assertTrue("second child should be a Button (mapped from <button>)",
                    button is Button)
                button as Button
                assertEquals("Tap", button.text.toString())

                // Child [2]: input
                val input = outer.getChildAt(2)
                assertTrue("third child should be an EditText (mapped from <input>)",
                    input is EditText)
                input as EditText
                assertEquals("Type here...", input.hint.toString())
            }
        }
    }
}
