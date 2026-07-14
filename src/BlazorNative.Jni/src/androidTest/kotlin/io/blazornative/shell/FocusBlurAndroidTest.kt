package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 4.2 Gate 3 (M4 DoD #4) — focus/blur live on the AVD: the on-device
 * third of FocusProbeTests.cs (.NET, in-process) and FocusBlurTest.kt (JVM,
 * through the dll). Those two proved the wire; THIS proves WidgetMapper's
 * setOnFocusChangeListener arm — real Android focus transitions on a real
 * EditText drive the echo TextView through the full dispatch loop.
 *
 * Shape (FocusProbe.cs's file header): root div → BnInput (focus + blur +
 * the always-on change attach) + BnText echo ("" → "focused"/"blurred").
 * On-screen: widget_root → ViewGroup div → [0] EditText, [1] TextView
 * (the echo span, text-collapsed).
 *
 * FOCUS PARKING (the judgment call): blur only fires when focus actually
 * LEAVES the EditText, and clearFocus() on the last focusable view can
 * bounce focus straight back — so the test parks focus on the console
 * markers TextView (main.xml's boot pane, made focusable on the UI thread)
 * instead. Parking FIRST also neutralizes the initial-focus ambiguity: in
 * non-touch mode the first layout may hand the EditText focus at boot, so
 * the test never asserts the pre-park echo, only the two transitions it
 * drives itself.
 *
 * [focus_detach_sides_and_listener_removal] pins the SINGLE-LISTENER
 * semantics at the WidgetMapper level with the synthetic-frame fixture
 * pattern (no runtime boot — FocusProbe never detaches its handlers, so the
 * detach arm is unreachable from the probe): detaching one side leaves the
 * other dispatching; detaching both removes the listener from the view.
 *
 * Polling: boot deadline 60s, post-event re-render deadline 10s, 250ms
 * cadence; UI actions via scenario.onActivity (the BnDemoAndroidTest house
 * style). STRICT MODE is guaranteed by BlazorNativeTestRunner.
 */
@RunWith(AndroidJUnit4::class)
class FocusBlurAndroidTest {

    // ── The real thing: focus/blur transitions on the AVD ───────────────────

    @Test
    fun focus_blur_round_trip_on_screen() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "FocusProbe")
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            // 1. Boot: poll for the probe's full shape (EditText + echo).
            assertNotNull(
                "FocusProbe never rendered within 60s — boot/mapper failed",
                pollForProbe(scenario)
            )

            // 2. Park focus on the console pane (see class KDoc) so the
            //    EditText is deterministically UNfocused before the test
            //    drives its own transitions.
            scenario.onActivity { act -> parkFocus(act) }
            assertTrue(
                "EditText never released focus after parking",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    editText(act)?.isFocused == false
                }
            )

            // 3. requestFocus → OnFocusChangeListener(hasFocus=true) →
            //    dispatch lane → OnFocus handler → re-render → echo "focused".
            scenario.onActivity { act -> editText(act)!!.requestFocus() }
            assertTrue(
                "echo never showed 'focused' within 10s of requestFocus",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    echoText(act)?.text?.toString() == "focused"
                }
            )

            // 4. Park focus elsewhere → hasFocus=false → echo "blurred".
            scenario.onActivity { act -> parkFocus(act) }
            assertTrue(
                "echo never showed 'blurred' within 10s of losing focus",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    echoText(act)?.text?.toString() == "blurred"
                }
            )
        }
    }

    // ── Detach sides + listener removal (synthetic frames, mapper-level) ────

    @Test
    fun focus_detach_sides_and_listener_removal() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val ctx = instrumentation.targetContext
        val dispatched = mutableListOf<Pair<Int, String>>() // main-thread only
        lateinit var root: FrameLayout
        lateinit var mapper: WidgetMapper

        // Frame 1: two inputs (the second is the focus-parking target inside
        // the detached hierarchy) + focus(h1)/blur(h2) attached to input 1.
        instrumentation.runOnMainSync {
            root = FrameLayout(ctx)
            mapper = WidgetMapper(ctx, root, onUiEvent = { handlerId, eventName, _ ->
                dispatched.add(handlerId to eventName)
            })
            mapper.apply(RenderFrame(
                frameId = 1, timestampMs = 0L,
                patches = listOf(
                    RenderPatch.CreateNode(nodeId = 1, nodeType = "input", parentId = null),
                    RenderPatch.CreateNode(nodeId = 2, nodeType = "input", parentId = null),
                    RenderPatch.AttachEvent(nodeId = 1, eventName = "focus", handlerId = 1),
                    RenderPatch.AttachEvent(nodeId = 1, eventName = "blur", handlerId = 2),
                    RenderPatch.CommitFrame(1, 0L),
                )
            ))
        }
        instrumentation.waitForIdleSync()

        var input1: EditText? = null
        var input2: EditText? = null
        instrumentation.runOnMainSync {
            input1 = root.getChildAt(0) as? EditText
            input2 = root.getChildAt(1) as? EditText
        }
        assertNotNull("frame 1 did not create input 1", input1)
        assertNotNull("frame 1 did not create input 2", input2)

        // Both sides attached: gain dispatches (h1, focus); loss (h2, blur).
        instrumentation.runOnMainSync {
            input1!!.requestFocus()
            input2!!.requestFocus()
        }
        instrumentation.runOnMainSync {
            assertEquals(
                "both sides attached: exactly focus then blur",
                listOf(1 to "focus", 2 to "blur"), dispatched
            )
            dispatched.clear()
        }

        // Frame 2: detach the BLUR side only — focus keeps dispatching,
        // losing focus dispatches NOTHING (the pair's blur slot is empty).
        instrumentation.runOnMainSync {
            mapper.apply(RenderFrame(
                frameId = 2, timestampMs = 0L,
                patches = listOf(
                    RenderPatch.DetachEvent(nodeId = 1, handlerId = 2, eventName = "blur"),
                    RenderPatch.CommitFrame(2, 0L),
                )
            ))
        }
        instrumentation.waitForIdleSync()
        instrumentation.runOnMainSync {
            input1!!.requestFocus()
            input2!!.requestFocus()
            assertEquals(
                "blur detached: only the focus side may dispatch",
                listOf(1 to "focus"), dispatched
            )
            dispatched.clear()
            assertNotNull(
                "one side still attached: the single listener must survive",
                input1!!.onFocusChangeListener
            )
        }

        // Frame 3: detach the FOCUS side too — both gone, listener removed.
        instrumentation.runOnMainSync {
            mapper.apply(RenderFrame(
                frameId = 3, timestampMs = 0L,
                patches = listOf(
                    RenderPatch.DetachEvent(nodeId = 1, handlerId = 1, eventName = "focus"),
                    RenderPatch.CommitFrame(3, 0L),
                )
            ))
        }
        instrumentation.waitForIdleSync()
        instrumentation.runOnMainSync {
            assertNull(
                "both sides detached: the focus listener must be removed",
                input1!!.onFocusChangeListener
            )
            input1!!.requestFocus()
            input2!!.requestFocus()
            assertEquals(
                "both sides detached: no dispatches",
                emptyList<Pair<Int, String>>(), dispatched
            )
        }
    }

    // ── Structural pins (FocusProbe's tree; positions, not nodeIds) ─────────

    /** The probe's root div: widget_root's single child, once mounted. */
    private fun probeRoot(act: MainActivity): ViewGroup? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? ViewGroup

    /** Root child [0]: the BnInput's EditText. */
    private fun editText(act: MainActivity): EditText? =
        probeRoot(act)?.takeIf { it.childCount >= 2 }?.getChildAt(0) as? EditText

    /** Root child [1]: the echo TextView (BnText span, text-collapsed). */
    private fun echoText(act: MainActivity): TextView? =
        probeRoot(act)?.takeIf { it.childCount >= 2 }?.getChildAt(1) as? TextView

    /** Steals focus onto the console markers TextView (see class KDoc). */
    private fun parkFocus(act: MainActivity) {
        val console = act.findViewById<TextView>(R.id.markers)
        console.isFocusableInTouchMode = true
        console.isFocusable = true
        console.requestFocus()
    }

    // ── Polling helpers (BnDemoAndroidTest house style) ──────────────────────

    /** Polls until the probe's full mount shape is on screen. */
    private fun pollForProbe(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long = 60_000,
    ): View? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<View?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                if (editText(act) != null && echoText(act) != null) {
                    found.set(probeRoot(act))
                }
            }
            if (found.get() != null) break
            Thread.sleep(250)
        }
        return found.get()
    }

    /** Polls [predicate] on the UI thread until true or [deadlineMs]. */
    private fun pollUntil(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long,
        predicate: (MainActivity) -> Boolean,
    ): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            val ok = AtomicReference(false)
            scenario.onActivity { act -> ok.set(predicate(act)) }
            if (ok.get()) return true
            Thread.sleep(250)
        }
        return false
    }
}
