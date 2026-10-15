package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.After
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 17.3 (#425) — Activity recreation on a device.
 *
 * A recreated Activity starts a new runtime, which mounts a root on the one process-global .NET
 * session. Since 17.3 that mount REPLACES the old root: a late continuation of the old page
 * re-renders nothing, the recreated shell stays on the page the user was on with its back stack,
 * and the old Activity's runtime is retired in onDestroy, so a frame that still reaches it is
 * dropped. RecreationTests pins the .NET half; RetireLateContinuationTest the retired runtime.
 *
 * DOES NOT COVER: a recreation during boot, while the old boot thread may still be inside start();
 * and the iOS shell, which mounts once per app and has no Activity recreation.
 */
@RunWith(AndroidJUnit4::class)
class RecreationAndroidTest {

    @Before
    fun reset() {
        AndroidShellBridge.resetCameraForTest()
        AndroidShellBridge.resetGeolocationForTest()
    }

    @After
    fun cleanup() {
        AndroidShellBridge.resetCameraForTest()
        AndroidShellBridge.resetGeolocationForTest()
    }

    @Test
    fun a_recreated_activity_never_shows_the_old_pages_frame() {
        // The camera demo's Take Photo awaits the capture; its continuation writes the echo
        // "status:Cancelled". Hold the capture, recreate, then cancel it: the continuation that
        // runs belongs to the OLD page, which the recreated Activity's mount has disposed.
        val held = AtomicReference<AndroidShellBridge.CameraCapture?>(null)
        AndroidShellBridge.cameraCaptureHook = { capture -> held.set(capture) }

        launch("BnCameraDemo").use { scenario ->
            assertTrue("BnCameraDemo never rendered within 60s", pollUntil(scenario, 60_000) { echoText(it) != null })
            tapButton(scenario, "Take Photo")
            assertTrue("anchor: the capture was never handed to the hook", pollTrue(10_000) { held.get() != null })

            scenario.recreate()
            assertTrue("the recreated BnCameraDemo never rendered within 60s",
                pollUntil(scenario, 60_000) { echoText(it) != null })

            held.get()!!.cancel()
            // Anchor: the cancel reached a LIVE .NET continuation, the old page's, so it ran.
            assertTrue("anchor: the cancel never reached the old page's continuation (rc " +
                "${AndroidShellBridge.lastHostCallCompleteRcForTest})",
                pollTrue(10_000) { AndroidShellBridge.lastHostCallCompleteRcForTest == 0 })
            Thread.sleep(2_000) // a late frame, if any, lands well within this
            val shown = AtomicReference<String?>(null)
            scenario.onActivity { shown.set(echoText(it)?.text?.toString()) }
            assertFalse("the recreated Activity shows '${shown.get()}': the OLD page's late continuation " +
                "rendered into the new Activity. That is #425.", shown.get() == "status:Cancelled")
        }
    }

    @Test
    fun a_recreated_activity_stays_on_its_page_and_back_goes_to_the_parent() {
        launch().use { scenario ->
            assertTrue("BnDemo never rendered within 60s", pollUntil(scenario, 60_000) { hasEditText(it) })
            tapButton(scenario, "Settings →")
            assertTrue("anchor: the swap to settings never completed within 10s",
                pollUntil(scenario, 10_000) { settingsTitle(it) != null && !hasEditText(it) })

            scenario.recreate()
            assertTrue("the recreated Activity did not stay on /settings: it showed the home page, so " +
                "rotation lost the user's place",
                pollUntil(scenario, 60_000) { settingsTitle(it) != null && !hasEditText(it) })
            assertTrue("the recreated Activity lost its back stack: back is not enabled on /settings",
                pollUntil(scenario, 10_000) { it.onBackPressedDispatcher.hasEnabledCallbacks() })

            scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
            assertTrue("back from /settings after recreation did not return to the home page within 10s",
                pollUntil(scenario, 10_000) { hasEditText(it) && settingsTitle(it) == null })
            scenario.onActivity { act -> assertFalse("a handled back must not finish the activity", act.isFinishing) }
        }
    }

    @Test
    fun a_recreated_deep_linked_activity_stays_on_the_page_it_navigated_to() {
        // A recreated Activity keeps its launch Intent, deep link included. The route it saved
        // must win over that deep link, or rotating after navigating away resets it to the
        // linked page and drops its back stack.
        val ctx = ApplicationProvider.getApplicationContext<android.content.Context>()
        ActivityScenario.launch<MainActivity>(AndroidShellBridge.buildTapIntent(ctx, "/")).use { scenario ->
            assertTrue("the deep-linked BnDemo never rendered within 60s", pollUntil(scenario, 60_000) { hasEditText(it) })
            tapButton(scenario, "Settings →")
            assertTrue("anchor: the swap to settings never completed within 10s",
                pollUntil(scenario, 10_000) { settingsTitle(it) != null && !hasEditText(it) })

            scenario.recreate()
            assertTrue("the recreated deep-linked Activity went back to its linked page: its launch " +
                "Intent's deep link outranked the route it saved",
                pollUntil(scenario, 60_000) { settingsTitle(it) != null && !hasEditText(it) })
            assertTrue("the recreated deep-linked Activity lost its back stack",
                pollUntil(scenario, 10_000) { it.onBackPressedDispatcher.hasEnabledCallbacks() })
        }
    }

    private fun launch(component: String? = null): ActivityScenario<MainActivity> {
        val intent = Intent(ApplicationProvider.getApplicationContext(), MainActivity::class.java)
        if (component != null) intent.putExtra(MainActivity.EXTRA_COMPONENT, component)
        return ActivityScenario.launch(intent)
    }

    private fun widgetRoot(act: MainActivity): FrameLayout? = act.findViewById(R.id.widget_root)

    private fun echoText(act: MainActivity): TextView? {
        val root = widgetRoot(act)?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup ?: return null
        for (i in 0 until root.childCount) {
            val child = root.getChildAt(i)
            if (child is TextView && child !is Button) return child
        }
        return null
    }

    private fun settingsTitle(act: MainActivity): TextView? =
        widgetRoot(act)?.let { root -> firstMatch(root) { v -> v is TextView && v !is Button && v.text.toString() == "Settings" } } as? TextView

    private fun hasEditText(act: MainActivity): Boolean = widgetRoot(act)?.let { firstMatch(it) { v -> v is EditText } } != null

    private fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            (widgetRoot(act)?.let { root -> firstMatch(root) { v -> v is Button && v.text.toString() == label } } as? Button)
                ?.let { it.performClick(); clicked.set(true) }
        }
        assertTrue("Button '$label' not found on screen", clicked.get())
    }

    private fun pollUntil(scenario: ActivityScenario<MainActivity>, deadlineMs: Long, predicate: (MainActivity) -> Boolean): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            val ok = AtomicReference(false)
            scenario.onActivity { act -> ok.set(predicate(act)) }
            if (ok.get()) return true
            Thread.sleep(250)
        }
        return false
    }

    private fun pollTrue(deadlineMs: Long, predicate: () -> Boolean): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            if (predicate()) return true
            Thread.sleep(100)
        }
        return predicate()
    }

    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) for (i in 0 until view.childCount) firstMatch(view.getChildAt(i), predicate)?.let { return it }
        return null
    }
}
