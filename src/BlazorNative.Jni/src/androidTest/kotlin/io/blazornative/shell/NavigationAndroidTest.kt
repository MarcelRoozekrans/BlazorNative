package io.blazornative.shell

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
 * Phase 3.5 Gate 3 — two-page navigation live on the AVD: the on-device
 * third of NavigationTests.cs (.NET, in-process) and NavigationTest.kt
 * (JVM, through the dll), closing M3 DoD #7 on real widgets. Launches
 * MainActivity with NO Intent extra (BnDemo IS the default) and drives the
 * whole loop through the screen: tap "Settings →" → the ENTIRE BnDemo tree
 * leaves widget_root (the 3.3 disposal machinery's RemoveNodes, applied by
 * WidgetMapper) and BnSettingsPage's views replace it; tap "← Back" →
 * BnDemo remounts FRESH (empty input + empty echo — state does not survive
 * the swap; BnDemo has no taps counter, this is the twins' honest
 * freshness pin).
 *
 * Shapes: BnDemo per src/BlazorNative.Components/BnDemo.cs's canonical
 * header (form LinearLayout, 6 children — see BnDemoAndroidTest's tree).
 * BnSettingsPage per BnSettingsPage.cs's, on-screen:
 *   widget_root: FrameLayout
 *     └── settings LinearLayout (#FFEEAA), 2 children IN THIS ORDER:
 *           [0] TextView "Settings"        (title span, text-collapsed)
 *           [1] Button "← Back"
 *   — and NO EditText anywhere: the shape pin BnSettingsPage lacks by
 *   design, which is how "the BnDemo input left the screen" is asserted.
 *
 * The JVM twin pins the dispatch-window ORDER (removes before creates,
 * Navigate callback observed synchronously); on-device the dispatch is
 * async from the UI thread, so the pins here are the END STATES the swap
 * must reach — mutually exclusive trees (settings visible ⇒ input gone;
 * demo visible ⇒ settings title gone), the twins' contract on real views.
 *
 * STRICT MODE (DoD #9): strict is guaranteed by BlazorNativeTestRunner —
 * the runner sets BLAZORNATIVE_STRICT=1 before any test class loads
 * (Phase 3.5 Gate 0; the per-class setenv pattern is gone).
 *
 * Polling: boot deadline 60s, post-tap re-render deadline 10s (the
 * EventRoundTripAndroidTest precedent — dispatch is async from the UI
 * thread). Views are found structurally and buttons by text, never by
 * nodeId (process-global counters, JVM-twin convention).
 */
@RunWith(AndroidJUnit4::class)
class NavigationAndroidTest {

    /** Launches MainActivity with NO extra: the routed default ("/" →
     * BnDemo) is itself part of what this class proves. */
    private fun launchDefault(): ActivityScenario<MainActivity> =
        ActivityScenario.launch(MainActivity::class.java)

    // ── Settings → : the whole screen swaps ─────────────────────────────────

    @Test
    fun settings_tap_swaps_whole_screen() {
        launchDefault().use { scenario ->
            assertNotNull("BnDemo never rendered within 60s — boot/mapper failed",
                pollForForm(scenario))

            tapButton(scenario, "Settings →")

            // The swap's end state: the settings title is on screen AND the
            // BnDemo input is GONE from the view tree — the old root's
            // RemoveNodes landed, not just a new page painted on top.
            assertTrue(
                "settings title never appeared with the BnDemo input gone within 10s",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    settingsTitle(act) != null && !hasEditText(act)
                }
            )

            // Structural pins on the settled settings tree (the JVM twin's
            // settings-mount pins, on real views): a single container under
            // widget_root holding exactly title + back button.
            scenario.onActivity { act ->
                val container = rootChild(act)
                assertTrue("settings root must be a LinearLayout, got " +
                    "${container?.let { it::class.simpleName }}", container is LinearLayout)
                container as LinearLayout
                assertEquals("settings page must hold exactly title + back button",
                    2, container.childCount)
                assertEquals("child 0 must be the title span",
                    "Settings", (container.getChildAt(0) as? TextView)?.text?.toString())
                assertEquals("child 1 must be the back button",
                    "← Back", (container.getChildAt(1) as? Button)?.text?.toString())
            }
        }
    }

    // ── ← Back: BnDemo remounts FRESH (state does not survive the swap) ─────

    @Test
    fun back_tap_remounts_bndemo_fresh() {
        launchDefault().use { scenario ->
            assertNotNull("boot failed", pollForForm(scenario))

            // Seed state through the bind loop: type, wait for the echo —
            // the state that must NOT survive the round trip.
            scenario.onActivity { act -> editText(act)!!.setText("hello") }
            assertTrue(
                "echo never showed the seed text within 10s",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    echoText(act)?.text?.toString() == "hello"
                }
            )

            tapButton(scenario, "Settings →")
            assertTrue(
                "the swap to settings never completed within 10s",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    settingsTitle(act) != null && !hasEditText(act)
                }
            )

            tapButton(scenario, "← Back")

            // BnDemo returns FRESH: the full 6-child form is back with an
            // EMPTY input and an EMPTY echo (the .NET twin's
            // NavigateBack_RemountsFresh / the JVM twin's fresh-remount
            // pins — BnDemo has no taps counter; emptiness IS freshness).
            assertTrue(
                "BnDemo never remounted fresh (empty input + empty echo) within 10s of Back",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    form(act) != null &&
                        editText(act)?.text?.toString() == "" &&
                        echoText(act)?.text?.toString() == ""
                }
            )

            // And the seeded text is nowhere in the new tree — stale state
            // (or a stale settings title) did not survive the remount.
            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)!!
                assertTrue("stale 'hello' survived the remount",
                    firstMatch(root) { v -> v is TextView && v.text.toString() == "hello" } == null)
                assertTrue("the settings title is still on screen after Back",
                    settingsTitle(act) == null)
            }
        }
    }

    // ── Structural pins (BnDemoAndroidTest's tree; positions, not nodeIds) ──

    /** widget_root's single child — whichever page currently owns the screen. */
    private fun rootChild(act: MainActivity): View? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0)

    /** The BnDemo form div: widget_root's child once BnDemo is mounted. */
    private fun form(act: MainActivity): LinearLayout? =
        (rootChild(act) as? LinearLayout)?.takeIf { it.childCount >= 6 }

    /** Form child [1]: the bound EditText. */
    private fun editText(act: MainActivity): EditText? =
        form(act)?.getChildAt(1) as? EditText

    /** The echo TextView: form child [2]'s single (text-collapsed) child. */
    private fun echoText(act: MainActivity): TextView? =
        (form(act)?.getChildAt(2) as? LinearLayout)
            ?.takeIf { it.childCount >= 1 }
            ?.getChildAt(0) as? TextView

    /** The settings title: a non-Button TextView reading exactly "Settings"
     * (Button extends TextView — BnDemo's "Settings →" never matches). */
    private fun settingsTitle(act: MainActivity): TextView? =
        act.findViewById<FrameLayout>(R.id.widget_root)?.let { root ->
            firstMatch(root) { v ->
                v is TextView && v !is Button && v.text.toString() == "Settings"
            }
        } as? TextView

    /** True while any EditText is in widget_root's tree (BnDemo's input —
     * BnSettingsPage creates none by design). */
    private fun hasEditText(act: MainActivity): Boolean =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.let { firstMatch(it) { v -> v is EditText } } != null

    // ── Helpers (BnDemoAndroidTest / CompositionAndroidTest conventions) ────

    /** Polls until BnDemo's mount frame is fully applied (form + all 6
     * children — batches are atomic per frame, but the chained echo panel's
     * create rides the same mount frame; poll to the complete shape). */
    private fun pollForForm(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long = 60_000,
    ): LinearLayout? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<LinearLayout?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act -> found.set(form(act)) }
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
