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
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch
import io.blazornative.shell.R
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 3.2 Gate 3 — the tap round-trip, live on the device.
 *
 * [tap_increments_counter_on_screen] is M3 DoD #2's on-screen proof: launch
 * MainActivity (NativeAOT boot), performClick the rendered Button →
 * WidgetMapper's click listener → BlazorNativeRuntime.dispatchEvent →
 * BlazorNative-Dispatch lane → blazornative_dispatch_event → @onclick handler
 * → re-render → frame callback → WidgetMapper batch → the counter TextView
 * updates. Tapping TWICE proves the listener survives the re-render
 * (re-attach lands on the same view; see the Phase 3.2 design risk table).
 *
 * [programmatic_setText_during_batch_does_not_dispatch] pins the TextWatcher
 * re-entrancy guard from both directions with the Phase 2.6 synthetic-frame
 * fixture pattern (no runtime boot): a ReplaceText applied DURING a batch must
 * not dispatch a change event (guard closes the setText → change → re-render
 * → setText loop), while a text change OUTSIDE a batch must dispatch.
 *
 * [change_reattach_dispatches_exactly_once_with_live_handler] pins the Phase
 * 4.2 stale-watcher fix with the same synthetic-frame pattern (a REAL
 * re-attach needs a re-render that swaps handler identity, which Blazor's
 * stable delegate targets never do for the Bn* components — so the frames
 * are driven directly): a last-wins change re-attach (same node, new
 * handlerId, NO detach) must REPLACE the watcher, not stack a second one —
 * typing dispatches exactly once, with the live handlerId (pre-fix: once per
 * stacked watcher, the first with the dead id).
 *
 * Polling: boot deadline 60s (NativeAOT mounts in ~1-2s; generous headroom,
 * WidgetMapperTest precedent); post-tap re-render deadline 10s — the dispatch
 * lane is async from the UI thread, so the updated text is polled, not read
 * synchronously after performClick.
 */
@RunWith(AndroidJUnit4::class)
class EventRoundTripAndroidTest {

    @Test
    fun tap_increments_counter_on_screen() {
        // Phase 3.4 Gate 4: MainActivity's no-extra default flipped to
        // "BnDemo", but this round-trip pins HELLO's tap counter — so it must
        // now request HelloComponent explicitly instead of riding the default
        // (per the 3.4 design risk table).
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "HelloComponent")
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            // 1. Boot: poll for the rendered Button (the mount's first frame).
            val button = pollForView<Button>(scenario, deadlineMs = 60_000) { it is Button }
            assertNotNull("Button never appeared in widget_root within 60s — boot/mapper failed", button)

            // First frame carries the initial counter text (batch is atomic).
            val initial = findCounterText(scenario)
            assertTrue(
                "expected initial counter text to contain 'taps: 0', got '$initial'",
                initial != null && initial.contains("taps: 0")
            )

            // 2. Tap → dispatch lane → .NET @onclick → re-render → "taps: 1".
            scenario.onActivity { button!!.performClick() }
            assertTrue(
                "counter never showed 'taps: 1' within 10s of the first tap",
                pollForCounter(scenario, "taps: 1", deadlineMs = 10_000)
            )

            // 3. Tap again → "taps: 2" — proves the listener survived the
            //    re-render (re-attach on the same view, possibly new handlerId).
            scenario.onActivity { button!!.performClick() }
            assertTrue(
                "counter never showed 'taps: 2' within 10s of the second tap",
                pollForCounter(scenario, "taps: 2", deadlineMs = 10_000)
            )
        }
    }

    @Test
    fun programmatic_setText_during_batch_does_not_dispatch() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val ctx = instrumentation.targetContext
        val changeCount = AtomicInteger(0)
        lateinit var root: FrameLayout
        lateinit var mapper: WidgetMapper

        // Frame 1: create an input and attach a change watcher to it.
        instrumentation.runOnMainSync {
            root = FrameLayout(ctx)
            mapper = WidgetMapper(ctx, root, onUiEvent = { _, eventName, _ ->
                if (eventName == "change") changeCount.incrementAndGet()
            })
            mapper.apply(RenderFrame(
                frameId = 1, timestampMs = 0L,
                patches = listOf(
                    RenderPatch.CreateNode(nodeId = 1, nodeType = "input", parentId = null),
                    RenderPatch.AttachEvent(nodeId = 1, eventName = "change", handlerId = 7),
                    RenderPatch.CommitFrame(1, 0L),
                )
            ))
        }
        instrumentation.waitForIdleSync()

        var editText: EditText? = null
        instrumentation.runOnMainSync { editText = root.getChildAt(0) as? EditText }
        assertNotNull("frame 1 did not create an EditText", editText)

        // Frame 2: programmatic text change DURING batch application — the
        // watcher fires (setText is synchronous) but the applyingBatch guard
        // must swallow the dispatch.
        instrumentation.runOnMainSync {
            mapper.apply(RenderFrame(
                frameId = 2, timestampMs = 0L,
                patches = listOf(
                    RenderPatch.ReplaceText(nodeId = 1, text = "from-batch"),
                    RenderPatch.CommitFrame(2, 0L),
                )
            ))
        }
        instrumentation.waitForIdleSync()
        instrumentation.runOnMainSync {
            assertEquals("batch-applied ReplaceText reached the EditText",
                "from-batch", editText!!.text.toString())
        }
        assertEquals(
            "programmatic setText during batch application must NOT dispatch a change event",
            0, changeCount.get()
        )

        // Outside a batch (user-typing equivalent): the watcher MUST dispatch.
        instrumentation.runOnMainSync { editText!!.setText("typed") }
        assertEquals(
            "text change outside a batch must dispatch exactly one change event",
            1, changeCount.get()
        )
    }

    @Test
    fun change_reattach_dispatches_exactly_once_with_live_handler() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val ctx = instrumentation.targetContext
        val dispatched = mutableListOf<Int>() // handlerIds; main-thread only
        lateinit var root: FrameLayout
        lateinit var mapper: WidgetMapper

        // Frame 1: input + change attach h1. Frame 2: last-wins re-attach h2
        // on the SAME node — no DetachEvent in between (the renderer's
        // re-attach contract, RenderFrame.kt's mapping notes).
        instrumentation.runOnMainSync {
            root = FrameLayout(ctx)
            mapper = WidgetMapper(ctx, root, onUiEvent = { handlerId, eventName, _ ->
                if (eventName == "change") dispatched.add(handlerId)
            })
            mapper.apply(RenderFrame(
                frameId = 1, timestampMs = 0L,
                patches = listOf(
                    RenderPatch.CreateNode(nodeId = 1, nodeType = "input", parentId = null),
                    RenderPatch.AttachEvent(nodeId = 1, eventName = "change", handlerId = 1),
                    RenderPatch.CommitFrame(1, 0L),
                )
            ))
            mapper.apply(RenderFrame(
                frameId = 2, timestampMs = 0L,
                patches = listOf(
                    RenderPatch.AttachEvent(nodeId = 1, eventName = "change", handlerId = 2),
                    RenderPatch.CommitFrame(2, 0L),
                )
            ))
        }
        instrumentation.waitForIdleSync()

        // Type: EXACTLY ONE dispatch, carrying the LIVE handlerId (h2). The
        // pre-fix behavior was two dispatches — h1's stacked stale watcher
        // fired first with the dead id.
        instrumentation.runOnMainSync {
            (root.getChildAt(0) as EditText).setText("typed")
            assertEquals(
                "a change re-attach must REPLACE the watcher: exactly one dispatch, the live handlerId",
                listOf(2), dispatched
            )
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /** Polls widget_root's subtree until [predicate] matches a view. */
    private inline fun <reified T : View> pollForView(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long,
        crossinline predicate: (View) -> Boolean,
    ): T? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<T?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)
                if (root != null) found.set(firstMatch(root) { predicate(it) } as? T)
            }
            if (found.get() != null) break
            Thread.sleep(250)
        }
        return found.get()
    }

    /** Current text of the counter TextView (contains "taps:"), or null. */
    private fun findCounterText(scenario: ActivityScenario<MainActivity>): String? {
        val text = AtomicReference<String?>(null)
        scenario.onActivity { act ->
            val root = act.findViewById<FrameLayout>(R.id.widget_root)
            val tv = root?.let {
                firstMatch(it) { v -> v is TextView && v !is Button && v.text.contains("taps:") }
            } as? TextView
            text.set(tv?.text?.toString())
        }
        return text.get()
    }

    /** Polls until the counter TextView's text contains [expected]. */
    private fun pollForCounter(
        scenario: ActivityScenario<MainActivity>,
        expected: String,
        deadlineMs: Long,
    ): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            if (findCounterText(scenario)?.contains(expected) == true) return true
            Thread.sleep(250)
        }
        return false
    }

    /** Depth-first search of a view subtree (includes [view] itself). */
    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) {
            for (i in 0 until view.childCount) {
                firstMatch(view.getChildAt(i), predicate)?.let { return it }
            }
        }
        return null
    }
}
