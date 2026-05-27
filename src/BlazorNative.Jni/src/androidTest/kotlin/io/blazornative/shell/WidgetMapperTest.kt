package io.blazornative.shell

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
 * Phase 2.5 end-to-end widget-mapper assertion.
 *
 * Launches MainActivity (which boots the .wasm on a background thread, posts
 * parsed [FRAME] patches to the main looper via WidgetMapper), polls the
 * widget_root for the rendered sentinel, asserts the resulting view tree:
 *   widget_root: FrameLayout
 *     └── LinearLayout (from <div> → "view" → vertical LinearLayout)
 *           └── TextView (from "frame-self-test" text node)
 *
 * Polling loop: cold wasmtime JIT + Mono AOT init of the ~14 MB .wasm on the
 * AVD x86_64 emulator can take 30-50s; 60s deadline gives headroom. The
 * synchronous JVM BootSmokeAndroidTest exhibits the same latency.
 * The poller breaks as soon as widget_root has children (the commit-frame post
 * has fired). All assertions then run inside scenario.onActivity { } so they
 * see the latest widget tree on the UI thread.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperTest {

    @Test
    fun sentinel_renders_as_linearlayout_containing_textview() {
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            val deadline = System.currentTimeMillis() + 60_000
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
                assertEquals("widget_root should have exactly one top-level child (the <div> container)",
                    1, root.childCount)
                val container = root.getChildAt(0)
                assertTrue("top-level child should be a LinearLayout (mapped from <div>)",
                    container is LinearLayout)
                container as LinearLayout
                assertEquals("LinearLayout should be vertical orientation", LinearLayout.VERTICAL, container.orientation)
                assertEquals("LinearLayout should contain exactly one child (the text node)",
                    1, container.childCount)
                val text = container.getChildAt(0)
                assertTrue("inner child should be a TextView", text is TextView)
                text as TextView
                assertEquals("TextView should show the sentinel's text content",
                    "frame-self-test", text.text.toString())
            }
        }
    }
}
