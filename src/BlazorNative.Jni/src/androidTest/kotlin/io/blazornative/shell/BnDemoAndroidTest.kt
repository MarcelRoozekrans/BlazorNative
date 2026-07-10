package io.blazornative.shell

import android.graphics.drawable.ColorDrawable
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 3.4 Gate 4 — the Bn* library live on the AVD: the on-device third of
 * BnDemoTest (JVM, through the dll) and BnDemoTests.cs (.NET, in-process).
 * Launches MainActivity with NO Intent extra — BnDemo IS the default since
 * this gate — and proves M3 DoD #5 (two-way bound input + live echo) and
 * DoD #6 (cascading theme toggle) on real widgets.
 *
 * Shape: see src/BlazorNative.Components/BnDemo.cs's file header — the
 * canonical pinned tree lives there. On-screen (WidgetMapper's NodeType
 * table + the Phase 2.8 text collapse):
 *   widget_root: FrameLayout
 *     └── form LinearLayout (#FFEEAA), 6 children IN THIS ORDER:
 *           [0] TextView "BnDemo"          (title span, text-collapsed)
 *           [1] EditText                    (the bound input; hint "Type here...")
 *           [2] LinearLayout echo panel (#FFEEAA)
 *                 └── TextView              (the live echo, "" on mount)
 *           [3] Button "Clear"
 *           [4] Button "Theme"
 *           [5] Button "Settings →"        (Phase 3.5 — navigates; NavigationAndroidTest)
 *
 * THE ANDROID-ONLY INVERSION: the JVM twin asserts the bound value's
 * write-back UpdateProp ARRIVES; here the assertion INVERTS — after typing,
 * the EditText must NOT be clobbered by the echo loop. The write-back
 * "value" UpdateProp lands inside applyBatch where (a) the inequality check
 * skips the redundant setText (the echo merely confirms what was typed) and
 * (b) the applyingBatch guard would swallow the watcher dispatch even if it
 * didn't — so typing produces exactly one change dispatch and the input
 * keeps the user's text. Typing "héllo→世界" exercises the IME/UTF-8 leg
 * (Kotlin String → JNA → UTF-8 C ABI → .NET → back) the JVM twin's
 * test-host payload can't distinguish from plain ASCII on a real device.
 *
 * STRICT MODE (DoD #9): strict is guaranteed by BlazorNativeTestRunner —
 * the runner sets BLAZORNATIVE_STRICT=1 before any test class loads
 * (Phase 3.5 Gate 0; the per-class setenv pattern is gone).
 *
 * Polling: boot deadline 60s, post-event re-render deadline 10s (the
 * EventRoundTripAndroidTest precedent — dispatch is async from the UI
 * thread). Views are found structurally (child positions pinned above) and
 * buttons by text, never by nodeId (process-global counters, JVM-twin
 * convention).
 */
@RunWith(AndroidJUnit4::class)
class BnDemoAndroidTest {

    companion object {
        const val DEFAULT_BACKGROUND = 0xFFFFEEAA.toInt() // #FFEEAA
        const val ALT_BACKGROUND = 0xFF334455.toInt()     // #334455
    }

    /** Launches MainActivity with NO extra: BnDemo as the DEFAULT is itself
     * under test (the Gate 4 flip — the launcher demo is the Bn* form). */
    private fun launchDefault(): ActivityScenario<MainActivity> =
        ActivityScenario.launch(MainActivity::class.java)

    // ── DoD #5 on-screen: type → echo updates, input NOT clobbered ──────────

    @Test
    fun bind_loop_types_and_echo_updates() {
        launchDefault().use { scenario ->
            assertNotNull("BnDemo never rendered within 60s — boot/mapper failed",
                pollForForm(scenario))

            // "Type": setText on the UI thread fires the change TextWatcher
            // exactly like IME input (outside any batch → it dispatches).
            val typed = "héllo→世界"
            scenario.onActivity { act -> editText(act)!!.setText(typed) }

            // The bind loop closes: dispatch → ValueChanged → re-render →
            // echo ReplaceText — the echo TextView must show the typed text.
            assertTrue(
                "echo never showed '$typed' within 10s of typing",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    echoText(act)?.text?.toString() == typed
                }
            )

            // THE INVERTED WRITE-BACK ASSERTION (see class KDoc): the same
            // frame carried the input's "value" UpdateProp echoing the typed
            // text back — the applyingBatch guard + inequality check must
            // leave the EditText untouched, not clobbered/looped/emptied.
            scenario.onActivity { act ->
                assertEquals(
                    "the echo frame's value write-back must NOT clobber the EditText",
                    typed, editText(act)!!.text.toString()
                )
            }
        }
    }

    // ── Clear: both halves reset ─────────────────────────────────────────────

    @Test
    fun clear_resets_both() {
        launchDefault().use { scenario ->
            assertNotNull("boot failed", pollForForm(scenario))

            // Seed state first — Clear on a pristine mount is a no-op diff.
            scenario.onActivity { act -> editText(act)!!.setText("hello") }
            assertTrue(
                "echo never showed the seed text within 10s",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    echoText(act)?.text?.toString() == "hello"
                }
            )

            tapButton(scenario, "Clear")

            // Clear sets _text = "" → the re-render pushes BOTH the input's
            // value prop (a REAL programmatic overwrite this time — the
            // inequality check passes, setText("") runs under the guard, no
            // change dispatch) and the echo's ReplaceText.
            assertTrue(
                "input + echo never both emptied within 10s of Clear",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    editText(act)?.text?.toString() == "" &&
                        echoText(act)?.text?.toString() == ""
                }
            )
        }
    }

    // ── DoD #6 on-screen: cascading theme flips ≥2 backgrounds, both ways ───

    @Test
    fun theme_toggle_flips_backgrounds_both_ways() {
        launchDefault().use { scenario ->
            assertNotNull("boot failed", pollForForm(scenario))

            // Mount: both themed containers carry the default background.
            scenario.onActivity { act ->
                assertEquals("form div must mount with the default background",
                    DEFAULT_BACKGROUND, backgroundColorOf(form(act)!!))
                assertEquals("echo panel must mount with the default background",
                    DEFAULT_BACKGROUND, backgroundColorOf(echoPanel(act)!!))
            }

            // Theme tap → NEW BnTheme record cascades → BOTH BnThemedPanels
            // re-render → SetStyle backgroundColor lands on ≥2 pinned views.
            tapButton(scenario, "Theme")
            assertTrue(
                "both themed backgrounds never flipped to alt within 10s",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    backgroundColorOf(form(act)!!) == ALT_BACKGROUND &&
                        backgroundColorOf(echoPanel(act)!!) == ALT_BACKGROUND
                }
            )

            // Second tap → back to the default on both (the toggle swaps the
            // record's fields, so the flip is symmetric).
            tapButton(scenario, "Theme")
            assertTrue(
                "both themed backgrounds never flipped back within 10s",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    backgroundColorOf(form(act)!!) == DEFAULT_BACKGROUND &&
                        backgroundColorOf(echoPanel(act)!!) == DEFAULT_BACKGROUND
                }
            )
        }
    }

    // ── Structural pins (the KDoc tree; positions, not nodeIds) ─────────────

    /** The BnDemo form div: widget_root's single child, once fully mounted. */
    private fun form(act: MainActivity): LinearLayout? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? LinearLayout

    /** Form child [1]: the bound EditText. */
    private fun editText(act: MainActivity): EditText? =
        form(act)?.takeIf { it.childCount >= 6 }?.getChildAt(1) as? EditText

    /** Form child [2]: the echo panel div (themed container #2). */
    private fun echoPanel(act: MainActivity): LinearLayout? =
        form(act)?.takeIf { it.childCount >= 6 }?.getChildAt(2) as? LinearLayout

    /** The echo TextView: the echo panel's single (text-collapsed) child. */
    private fun echoText(act: MainActivity): TextView? =
        echoPanel(act)?.takeIf { it.childCount >= 1 }?.getChildAt(0) as? TextView

    /** SetStyle backgroundColor lands as a ColorDrawable; -1 if absent. */
    private fun backgroundColorOf(view: View): Int =
        (view.background as? ColorDrawable)?.color ?: -1

    // ── Helpers (CompositionAndroidTest conventions) ─────────────────────────

    /** Polls until the mount frame is fully applied (form + all 6 children —
     * batches are atomic per frame, but the chained echo panel's create rides
     * the same mount frame; poll to the complete shape regardless). */
    private fun pollForForm(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long = 60_000,
    ): LinearLayout? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<LinearLayout?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                found.set(form(act)?.takeIf { it.childCount >= 6 })
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

    /** Finds the Button whose text equals [label] and performClicks it on the
     * UI thread. Fails the test if the button is not on screen. */
    private fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            val root = act.findViewById<FrameLayout>(R.id.widget_root)
            val button = root?.let {
                firstMatch(it) { v -> v is Button && v.text.toString() == label }
            } as? Button
            if (button != null) {
                button.performClick()
                clicked.set(true)
            }
        }
        assertTrue("Button '$label' not found on screen", clicked.get())
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
