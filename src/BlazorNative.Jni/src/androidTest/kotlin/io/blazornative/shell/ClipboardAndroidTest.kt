package io.blazornative.shell

import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 5.4 Gate 2 (M5 DoD #6) — clipboard + share live on the AVD: the
 * on-device third of ClipboardProbeTests.cs (.NET, in-process) and
 * ClipboardTest.kt (JVM, through the dll). Those proved the wire + the
 * size-negotiated bridge; THIS proves AndroidShellBridge's REAL backends —
 * ClipboardManager and the ACTION_SEND share Intent — through the full dispatch
 * loop on real widgets.
 *
 * Shape (ClipboardProbe.cs's file header): root div → Copy/Paste/Share BnButtons
 * + BnText echo. On-screen (WidgetMapper's NodeType table): widget_root →
 * LinearLayout div → [0] Button "Copy", [1] Button "Paste", [2] Button "Share",
 * [3] TextView (the echo span, text-collapsed).
 *
 * CLIPBOARD ROUND-TRIP: tap Copy → the dispatch loop calls
 * AndroidShellBridge.clipboardWrite → setPrimaryClip on the REAL system
 * ClipboardManager (asserted directly); tap Paste → clipboardRead → getPrimaryClip
 * → the echo TextView shows the payload (the read half, on-device).
 *
 * SHARE (the honest bar): tapping Share builds an ACTION_SEND Intent and would
 * pop the system chooser — the test installs [AndroidShellBridge.shareLaunchHook]
 * to CAPTURE the built Intent and SKIP the launch, then asserts EXTRA_TEXT + type,
 * NOT the system sheet (which is unassertable under instrumentation).
 *
 * Polling: boot deadline 60s, post-event re-render deadline 10s, 250ms cadence;
 * buttons by text, echo by structure (the BnDemoAndroidTest house style). STRICT
 * MODE is guaranteed by BlazorNativeTestRunner.
 */
@RunWith(AndroidJUnit4::class)
class ClipboardAndroidTest {

    private companion object {
        /** Mirror of ClipboardProbe.CopyPayload — the literal Copy writes. */
        const val PAYLOAD = "clip!"
    }

    // ── Copy → Paste round-trip through the REAL ClipboardManager ────────────

    @Test
    fun copy_paste_round_trip_through_real_clipboard() {
        launchProbe().use { scenario ->
            assertNotNull(
                "ClipboardProbe never rendered within 60s — boot/mapper failed",
                pollForProbe(scenario)
            )

            // Android 10+ gates clipboard READS on window focus (an ANR/overlay
            // stealing focus → the read is denied and returns ""). Wait until the
            // activity holds focus before driving Copy/Paste so the reads can land.
            awaitWindowFocus(scenario)

            // Tap Copy → clipboardWrite → the system clipboard holds the payload.
            tapButton(scenario, "Copy")
            assertTrue(
                "Copy never reached the real ClipboardManager within 10s",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    systemClipboardText(act) == PAYLOAD
                }
            )

            // Tap Paste → clipboardRead → the echo TextView shows the payload.
            tapButton(scenario, "Paste")
            assertTrue(
                "echo never showed the pasted clipboard value within 10s",
                pollUntil(scenario, deadlineMs = 10_000) { act ->
                    echoText(act)?.text?.toString() == PAYLOAD
                }
            )
        }
    }

    // ── Share builds an ACTION_SEND Intent (seam assert, not the sheet) ──────

    @Test
    fun share_builds_action_send_intent_with_the_payload() {
        val captured = AtomicReference<Intent?>(null)
        AndroidShellBridge.shareLaunchHook = { intent -> captured.set(intent) }
        try {
            launchProbe().use { scenario ->
                assertNotNull("boot failed", pollForProbe(scenario))

                // Wait for window focus — Paste's clipboard read is focus-gated.
                awaitWindowFocus(scenario)

                // Seed the echo (Share shares the current echo): Copy → Paste.
                tapButton(scenario, "Copy")
                tapButton(scenario, "Paste")
                assertTrue(
                    "echo never seeded within 10s",
                    pollUntil(scenario, deadlineMs = 10_000) { act ->
                        echoText(act)?.text?.toString() == PAYLOAD
                    }
                )

                // Tap Share → the hook captures the built Intent (no chooser).
                tapButton(scenario, "Share")
                assertTrue(
                    "Share never produced an Intent within 10s",
                    pollUntil(scenario, deadlineMs = 10_000) { _ -> captured.get() != null }
                )

                val intent = captured.get()!!
                assertEquals("share must be ACTION_SEND", Intent.ACTION_SEND, intent.action)
                assertEquals("share MIME must be text/plain", "text/plain", intent.type)
                assertEquals(
                    "share EXTRA_TEXT must be the shared echo",
                    PAYLOAD, intent.getStringExtra(Intent.EXTRA_TEXT)
                )
            }
        } finally {
            AndroidShellBridge.shareLaunchHook = null // never leak across tests
        }
    }

    // ── Launch ───────────────────────────────────────────────────────────────

    private fun launchProbe(): ActivityScenario<MainActivity> {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "ClipboardProbe")
        return ActivityScenario.launch(intent)
    }

    // ── Structural pins (the KDoc tree; positions/type, not nodeIds) ─────────

    /** The probe's root div: widget_root's single child, once mounted. */
    private fun probeRoot(act: MainActivity): LinearLayout? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? LinearLayout

    /** The echo TextView: the root's single child that is a TextView but NOT a
     * Button (Button extends TextView — the three buttons must not match). */
    private fun echoText(act: MainActivity): TextView? {
        val root = probeRoot(act) ?: return null
        for (i in 0 until root.childCount) {
            val child = root.getChildAt(i)
            if (child is TextView && child !is Button) return child
        }
        return null
    }

    /** The REAL system clipboard's current primary-clip text (empty when none).
     * Read on the UI thread via the target app's context — the app has focus, so
     * Android 10+ hands back the clip data. */
    private fun systemClipboardText(act: MainActivity): String {
        val cm = act.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        return cm.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.text?.toString() ?: ""
    }

    // ── Helpers (BnDemoAndroidTest house style) ──────────────────────────────

    /** Polls until the probe's full mount shape is on screen (3 buttons + echo). */
    private fun pollForProbe(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long = 60_000,
    ): View? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<View?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = probeRoot(act)
                if (root != null && root.childCount >= 4 && echoText(act) != null) {
                    found.set(root)
                }
            }
            if (found.get() != null) break
            Thread.sleep(250)
        }
        return found.get()
    }

    /** Waits (up to 10s) until the activity holds window focus — the precondition
     * for Android 10+ clipboard READS. Best-effort: a false return still lets the
     * test proceed (and fail loudly on the clipboard assertion) rather than mask a
     * genuine regression as a focus timeout. The CI lane also sets
     * hide_error_dialogs=1 so a boot ANR can't hold focus. */
    private fun awaitWindowFocus(scenario: ActivityScenario<MainActivity>) {
        pollUntil(scenario, deadlineMs = 10_000) { act -> act.hasWindowFocus() }
    }

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
