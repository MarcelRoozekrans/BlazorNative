package io.blazornative.shell

import android.content.Intent
import android.net.Uri
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.TextView
import androidx.lifecycle.Lifecycle
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 5.1 Gate 3 — host-INITIATED events live on the AVD (M5 DoD #5), the
 * on-device third of HostEventProbeTests.cs / NavigationTests.cs (.NET) and
 * HostEventTest.kt (JVM). Three proofs, all through the real MainActivity
 * wiring (lifecycle overrides, predictive-back OnBackInvokedCallback, deep-link
 * parse):
 *
 *   (1) LIFECYCLE — EXTRA_COMPONENT=HostEventProbe, moveToState(STARTED) from
 *       RESUMED fires onPause → dispatchHostEvent("onPause") → the probe's echo
 *       TextView shows "onPause" on screen (the NEW lifecycle instrumented
 *       pattern);
 *   (2) DEEP LINK — an ACTION_VIEW intent (blazornative://settings) seeds the
 *       startup route before boot → the 3.5 startup-honor mounts BnSettingsPage
 *       (title + Back, no input);
 *   (3) PREDICTIVE BACK — BnDemo → tap "Settings →" → system back (API 34's
 *       OnBackInvokedCallback → dispatchHostEventAndWait("back") → NavigateBack)
 *       → BnDemo returns.
 *
 * The back-at-root → finish() path (rc 1) is NOT asserted here (finish() is
 * awkward to observe cleanly through ActivityScenario after a back); it is
 * covered by the JVM back-at-root rc-1 pin (HostEventTest) and the .NET
 * HostBackEvent_AtRoot_Returns1 test — the SAME .NET routing all three drive.
 *
 * Views are found structurally / by text (nodeIds are process-global counters).
 * STRICT MODE guaranteed by BlazorNativeTestRunner. Polling deadlines mirror
 * NavigationAndroidTest (60s boot, 10s re-render — dispatch is async).
 */
@RunWith(AndroidJUnit4::class)
class HostEventAndroidTest {

    // ── (1) lifecycle onPause reaches the mounted probe on screen ────────────

    @Test
    fun lifecycle_onPause_reaches_probe_on_screen() {
        val intent = Intent(ApplicationProvider.getApplicationContext(), MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "HostEventProbe")
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            // The probe mounted once its echo TextView is on screen (empty at
            // mount) — which also means start() returned and the boot guard is
            // open, so the lifecycle event that follows will actually dispatch.
            assertTrue(
                "HostEventProbe never mounted within 60s",
                pollUntil(scenario, 60_000) { echoTextView(it) != null }
            )

            // RESUMED → STARTED fires onPause → dispatchHostEvent("onPause") →
            // the probe re-renders its echo to "onPause (1)".
            scenario.moveToState(Lifecycle.State.STARTED)

            assertTrue(
                "the probe never showed 'onPause' on screen within 10s of onPause",
                pollUntil(scenario, 10_000) {
                    echoTextView(it)?.text?.toString()?.contains("onPause") == true
                }
            )
        }
    }

    // ── (2) deep link starts the app on BnSettingsPage ───────────────────────

    @Test
    fun deep_link_starts_on_settings_page() {
        // An explicit ACTION_VIEW intent carrying the deep-link data — the
        // manifest's blazornative:// filter is what routes a REAL browser link;
        // ActivityScenario needs the explicit class, but MainActivity.onCreate
        // parses intent.data identically, so this exercises the same seam.
        val intent = Intent(Intent.ACTION_VIEW, Uri.parse("blazornative://settings"))
            .setClass(ApplicationProvider.getApplicationContext(), MainActivity::class.java)
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            // The seed → QueryStartupRoute("/settings") → BnSettingsPage mounts:
            // title "Settings" + "← Back", and NO input (the shape pin).
            assertTrue(
                "BnSettingsPage never mounted from the deep link within 60s",
                pollUntil(scenario, 60_000) { settingsTitle(it) != null && !hasEditText(it) }
            )
            scenario.onActivity { act ->
                assertNotNull(
                    "the settings page must show the '← Back' button",
                    firstMatch(widgetRoot(act)) { v -> v is Button && v.text.toString() == "← Back" }
                )
            }
        }
    }

    // ── (3) predictive back navigates back to BnDemo ─────────────────────────

    @Test
    fun predictive_back_returns_to_bndemo() {
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            assertTrue(
                "BnDemo never rendered within 60s — boot/mapper failed",
                pollUntil(scenario, 60_000) { form(it) != null }
            )
            // Forward to settings (this records the previous-route slot = "/").
            tapButton(scenario, "Settings →")
            assertTrue(
                "the swap to settings never completed within 10s",
                pollUntil(scenario, 10_000) { settingsTitle(it) != null && !hasEditText(it) }
            )

            // Drive the single back entry point directly. On API 34 the
            // registered OnBackInvokedCallback delegates to onBackPressed()
            // (verified: the callback registration appears in logcat), so this
            // exercises the IDENTICAL production back logic — handleBack() →
            // dispatchHostEventAndWait("back") → NavigateBack (rc 0, consumed).
            // Committing a system predictive-back GESTURE under instrumentation
            // is unreliable (it starts then cancels), so the entry point is
            // driven directly rather than through an injected gesture/key.
            scenario.onActivity { it.onBackPressed() }

            // BnDemo returns: its full form is back with the input on screen and
            // the settings title gone (mutually-exclusive trees).
            assertTrue(
                "BnDemo never returned after predictive back within 10s",
                pollUntil(scenario, 10_000) {
                    form(it) != null && hasEditText(it) && settingsTitle(it) == null
                }
            )
        }
    }

    // ── Structural pins (NavigationAndroidTest conventions) ──────────────────

    private fun widgetRoot(act: MainActivity): FrameLayout =
        act.findViewById(R.id.widget_root)

    /** The HostEventProbe echo TextView: the single TextView descendant of
     * widget_root (root div → span → TextView; the console markers TextView is
     * OUTSIDE widget_root). */
    private fun echoTextView(act: MainActivity): TextView? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.let { firstMatch(it) { v -> v is TextView && v !is Button } } as? TextView

    /** The BnDemo form div: widget_root's child once BnDemo is mounted. */
    private fun form(act: MainActivity): ViewGroup? =
        (act.findViewById<FrameLayout>(R.id.widget_root)?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? ViewGroup)?.takeIf { it.childCount >= 6 }

    /** The settings title: a non-Button TextView reading exactly "Settings". */
    private fun settingsTitle(act: MainActivity): TextView? =
        act.findViewById<FrameLayout>(R.id.widget_root)?.let { root ->
            firstMatch(root) { v -> v is TextView && v !is Button && v.text.toString() == "Settings" }
        } as? TextView

    /** True while any EditText is in widget_root (BnDemo's input; BnSettingsPage
     * and HostEventProbe create none). */
    private fun hasEditText(act: MainActivity): Boolean =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.let { firstMatch(it) { v -> v is EditText } } != null

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

    private fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            val button = firstMatch(widgetRoot(act)) { v ->
                v is Button && v.text.toString() == label
            } as? Button
            if (button != null) { button.performClick(); clicked.set(true) }
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
