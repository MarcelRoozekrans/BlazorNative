package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.lifecycle.Lifecycle
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 16.2 (#346) — Android back, OFF the main thread, on the AVD.
 *
 * Until 16.2 MainActivity's back blocked the main thread on .NET for a handled/not-handled rc.
 * Now .NET pushes whether it can go back (a BackState notice), WidgetMapper applies it on main
 * in the same batch as the frame that shows the page, and an AndroidX OnBackPressedCallback is
 * enabled only while back must be intercepted: .NET can go back, or a modal is open. Disabled,
 * back takes the platform default, which finishes an activity launched as these tests launch it.
 * Enabled, the press is handed to .NET fire-and-forget, and a back .NET cannot handle after all
 * comes back as BackUnhandled, which hands the press to that same platform default.
 *
 * Five proofs, all through the real MainActivity:
 *   - back from a page navigates to its parent, and the callback is enabled WITH the page;
 *   - back at the root finishes the activity: the scenario reaches DESTROYED. The first on-device
 *     pin of that path; HostEventAndroidTest used to leave it to the JVM and .NET;
 *   - #346's own scenario: while a SYNCHRONOUS handler holds the render thread (and with it the
 *     dispatch lane) on a camera call, a back press returns at once, and the navigation lands
 *     after the release. The old blocking back took ~3 s here, the release delay;
 *   - navigate, then press back in the very main-thread turn that first shows the new page: the
 *     press navigates back rather than exiting, and is not swallowed;
 *   - with a modal open and nothing to go back to, back dismisses the modal, and the callback
 *     turns off once it closes.
 *
 * HARNESS: the launch/poll/tap helpers and the structural finders are NavigationAndroidTest's and
 * HostEventAndroidTest's (form, settingsTitle, hasEditText, pollUntil, tapButton, firstMatch);
 * the camera hold is BnCameraAndroidTest's seam, AndroidShellBridge.cameraCaptureHook, with the
 * capture held instead of answered, under the sample's BackHoldProbe. "Same runnable" is read
 * through WidgetMapper.inBatchRunnableForTest from inside a wrapped onBackEnabledChanged. Back is driven through `onBackPressedDispatcher.onBackPressed()`
 * on the main thread, the entry API 34's predictive back also feeds, because committing a
 * predictive-back GESTURE under instrumentation is unreliable. The callback's state is read
 * through `hasEnabledCallbacks()`, the dispatcher's own public answer.
 *
 * Every launch uses an explicit Intent with NO action. `ActivityScenario.launch(Class)` builds an
 * ACTION_MAIN + CATEGORY_LAUNCHER intent, and on API 31+ the system's default back moves such a
 * task root to the background rather than finishing it, so DESTROYED would never come.
 *
 * THE SESSION IS PROCESS-GLOBAL. The .NET navigation history survives from test to test, and
 * since 16.2 every mount resends the current back state, so a fresh launch can start with back
 * ENABLED on BnDemo. Each test that needs the root therefore goes forward once and back once
 * first: a forward step from a fresh mount records "/", and a back consumes the only slot.
 *
 * DOES NOT COVER: the platform's predictive-back animation or a real gesture; API levels below
 * 33, where AndroidX dispatches the classic back and only its own contract covers this shell;
 * whether the BackState notice precedes its frame, which BackStateNoticeTests.cs pins on .NET;
 * and the buffer's hold-until-batch rule in isolation, which BackStateBufferTest pins on the JVM.
 * STRICT MODE is guaranteed by BlazorNativeTestRunner.
 */
@RunWith(AndroidJUnit4::class)
class BackAndroidTest {

    @Before
    fun reset() {
        AndroidShellBridge.resetCameraForTest()
    }

    @After
    fun cleanup() {
        AndroidShellBridge.resetCameraForTest()
    }

    // ── 1. back from a page navigates to its parent ──────────────────────────

    @Test
    fun back_from_a_page_navigates_to_its_parent() {
        launch().use { scenario ->
            assertTrue("BnDemo never rendered within 60s", pollUntil(scenario, 60_000) { form(it) != null })
            // Start from the root, so the tap below is what turns back ON.
            goForwardAndBackToTheRoot(scenario)
            val toggles = recordEnableToggles(scenario)

            tapButton(scenario, "Settings →")
            assertTrue("the swap to settings never completed within 10s",
                pollUntil(scenario, 10_000) { act -> settingsTitle(act) != null && !hasEditText(act) })
            // THE PIN (spec decision 2), DETERMINISTIC: back turned on exactly once, INSIDE the
            // batch runnable, with the settings page already applied. Too early (on arrival, or
            // with the swap's removal batch) sees no settings page; too late (a runnable posted
            // after the batch) sees the flag down.
            assertEnabledOnceWithThePage(toggles)

            scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }

            assertTrue("BnDemo never returned within 10s of back",
                pollUntil(scenario, 10_000) { act ->
                    form(act) != null && hasEditText(act) && settingsTitle(act) == null
                })
            scenario.onActivity { act ->
                assertFalse("a handled back must not finish the activity", act.isFinishing)
            }
            // Back consumed the only history slot, so the root's answer arrives: disabled.
            assertTrue("back stayed enabled at the root after the slot was consumed",
                pollUntil(scenario, 10_000) { !it.onBackPressedDispatcher.hasEnabledCallbacks() })
        }
    }

    // ── 2. back at the root finishes the activity ────────────────────────────

    @Test
    fun back_at_root_finishes_the_activity() {
        val scenario = launch()
        try {
            assertTrue("BnDemo never rendered within 60s", pollUntil(scenario, 60_000) { form(it) != null })
            goForwardAndBackToTheRoot(scenario)

            scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }

            assertTrue("back at the root did not finish the activity within 10s: the scenario is " +
                "${scenario.state}, not DESTROYED",
                pollState(scenario, Lifecycle.State.DESTROYED, 10_000))
        } finally {
            scenario.close()
        }
    }

    // ── 3. #346: a held RENDER THREAD does not hold the back press ───────────

    @Test
    fun back_while_the_render_thread_is_held_returns_at_once_and_navigates_after_release() {
        val held = AtomicReference<AndroidShellBridge.CameraCapture?>(null)
        val cameraCall = CountDownLatch(1)
        // BnCameraAndroidTest's seam, HELD rather than answered.
        AndroidShellBridge.cameraCaptureHook = { capture -> held.set(capture); cameraCall.countDown() }

        // History first: a forward step from BnDemo, so the probe mounted next can go back.
        launch().use { scenario ->
            assertTrue("BnDemo never rendered within 60s", pollUntil(scenario, 60_000) { form(it) != null })
            tapButton(scenario, "Settings →")
            assertTrue("the swap to settings never completed within 10s",
                pollUntil(scenario, 10_000) { settingsTitle(it) != null && !hasEditText(it) })
        }

        val released = AtomicBoolean(false)
        var releaser: Thread? = null
        launch(component = "BackHoldProbe").use { scenario ->
            try {
                assertTrue("BackHoldProbe never rendered within 60s",
                    pollUntil(scenario, 60_000) { buttonLabelled(it, "Hold") != null })
                assertTrue("back must be enabled on the probe, or the press never reaches .NET",
                    pollUntil(scenario, 10_000) { it.onBackPressedDispatcher.hasEnabledCallbacks() })

                // Hold's SYNCHRONOUS handler blocks the render thread on the camera call, so
                // the dispatch lane is held too (BackNoticeTest's positive control proves it).
                tapButton(scenario, "Hold")
                assertTrue("Hold never began the camera call within 10s",
                    cameraCall.await(10, TimeUnit.SECONDS))

                // Release in ~3 s from a thread of its own, so the OLD back, which waited on
                // the lane from the main thread, finishes in ~3 s rather than hanging forever.
                releaser = Thread({
                    Thread.sleep(3_000)
                    released.set(true)
                    held.getAndSet(null)?.cancel()
                }, "back-hold-releaser").apply { isDaemon = true; start() }

                // THE PIN: the press returns at once. The pre-16.2 back took ~3 s here.
                val elapsedMs = AtomicLong(-1)
                scenario.onActivity { act ->
                    val t0 = System.nanoTime()
                    act.onBackPressedDispatcher.onBackPressed()
                    elapsedMs.set(TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - t0))
                }
                assertTrue("the back press held the main thread for ${elapsedMs.get()} ms while the " +
                    "render thread was held. That is #346: back waits on .NET.",
                    elapsedMs.get() in 0 until 1_000)

                // Nothing can have moved yet: the render thread is still held.
                scenario.onActivity { act ->
                    assertFalse("the release fired before this check; the window is too short to " +
                        "observe the held state", released.get())
                    assertNotNull("the page changed while the render thread was held",
                        buttonLabelled(act, "Hold"))
                    assertFalse("the press finished the activity", act.isFinishing)
                }

                // After the release the queued back lands: BnDemo, the history's page.
                assertTrue("the back never navigated to BnDemo within 15s of the press",
                    pollUntil(scenario, 15_000) { act -> form(act) != null && buttonLabelled(act, "Hold") == null })
                assertTrue("the navigation cannot precede the release", released.get())
            } finally {
                releaser?.join(10_000)
                held.getAndSet(null)?.cancel() // never leave the shared session held
            }
        }
    }

    // ── 4. navigate, then back at once ───────────────────────────────────────

    @Test
    fun navigate_then_back_at_once_neither_exits_nor_swallows() {
        launch().use { scenario ->
            assertTrue("BnDemo never rendered within 60s", pollUntil(scenario, 60_000) { form(it) != null })
            goForwardAndBackToTheRoot(scenario)
            val toggles = recordEnableToggles(scenario)

            tapButton(scenario, "Settings →")
            // Press back in the SAME main-thread turn that first sees the settings page.
            val pressed = AtomicBoolean(false)
            assertTrue("the swap to settings never completed within 10s",
                pollUntil(scenario, 10_000, sleepMs = 5) { act ->
                    val shown = settingsTitle(act) != null && !hasEditText(act)
                    if (shown) {
                        act.onBackPressedDispatcher.onBackPressed()
                        pressed.set(true)
                    }
                    shown
                })
            assertTrue(pressed.get())
            // Deterministic half: back was already ON when the page appeared, because it
            // turned on inside the very runnable that applied the page.
            assertEnabledOnceWithThePage(toggles)

            // Not swallowed: the press navigated back. Not an exit: the activity lives.
            assertTrue("the back pressed as the settings page appeared never navigated back " +
                "within 10s — the press was swallowed or the app exited",
                pollUntil(scenario, 10_000) { act ->
                    form(act) != null && hasEditText(act) && settingsTitle(act) == null
                })
            scenario.onActivity { act ->
                assertFalse("the back pressed as the page appeared finished the activity", act.isFinishing)
            }
        }
    }

    // ── 5. a modal open at the root: back dismisses it ───────────────────────

    @Test
    fun back_with_a_modal_open_dismisses_the_modal() {
        // First bring the session to its root and close that activity, so the modal demo
        // below mounts with NOTHING to go back to. Then only the open modal can enable back,
        // which is the term of isEnabled this test is about.
        launch().use { scenario ->
            assertTrue("BnDemo never rendered within 60s", pollUntil(scenario, 60_000) { form(it) != null })
            goForwardAndBackToTheRoot(scenario)
        }

        launch(component = "BnModalDemo").use { scenario ->
            assertTrue("BnModalDemo never rendered within 60s",
                pollUntil(scenario, 60_000) { buttonLabelled(it, "Show modal") != null })
            assertTrue("back must be DISABLED on a root page with no modal: the anchor that makes " +
                "the modal the only reason it turns on",
                pollUntil(scenario, 10_000) { !it.onBackPressedDispatcher.hasEnabledCallbacks() })

            tapButton(scenario, "Show modal")
            assertTrue("the modal never opened within 10s",
                pollUntil(scenario, 10_000) { it.mapper.modalOverlayCount == 1 })
            scenario.onActivity { act ->
                assertTrue("an open modal must enable back, or back finishes the app from under it",
                    act.onBackPressedDispatcher.hasEnabledCallbacks())
            }

            scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }

            assertTrue("back never dismissed the open modal within 10s",
                pollUntil(scenario, 10_000) { it.mapper.modalOverlayCount == 0 })
            scenario.onActivity { act ->
                assertFalse("back with a modal open must not finish the activity", act.isFinishing)
                assertNotNull("the page did not navigate: BnModalDemo is still mounted",
                    buttonLabelled(act, "Show modal"))
            }
            // The modal closed and there is nothing to go back to: back turns off again.
            assertTrue("back stayed enabled after the modal closed at the root",
                pollUntil(scenario, 10_000) { !it.onBackPressedDispatcher.hasEnabledCallbacks() })
        }
    }

    // ── Harness (NavigationAndroidTest / HostEventAndroidTest conventions) ───

    /** One `true` publish of the back state: whether it ran inside a batch runnable, and
     * whether the settings page was already applied when it did. */
    private data class Toggle(val inBatchRunnable: Boolean, val settingsShown: Boolean)

    /** Wraps MainActivity's `onBackEnabledChanged` to record every toggle to `true`, then
     * forwards it, so the production callback still runs. */
    private fun recordEnableToggles(scenario: ActivityScenario<MainActivity>): MutableList<Toggle> {
        val toggles = CopyOnWriteArrayList<Toggle>()
        scenario.onActivity { act ->
            val production = act.mapper.onBackEnabledChanged
            assertNotNull("MainActivity no longer wires onBackEnabledChanged", production)
            act.mapper.onBackEnabledChanged = { enabled ->
                if (enabled) toggles.add(Toggle(
                    inBatchRunnable = act.mapper.inBatchRunnableForTest,
                    settingsShown = settingsTitle(act) != null && !hasEditText(act),
                ))
                production?.invoke(enabled)
            }
        }
        return toggles
    }

    private fun assertEnabledOnceWithThePage(toggles: List<Toggle>) {
        assertEquals("back must turn ON exactly once for the forward step; toggles: $toggles",
            1, toggles.size)
        assertTrue("back turned on OUTSIDE the batch runnable (${toggles[0]}): the back state was " +
            "applied in a runnable of its own, before or after the page", toggles[0].inBatchRunnable)
        assertTrue("back turned on before the settings page was applied (${toggles[0]}): it rode " +
            "an earlier batch, such as the swap's removal", toggles[0].settingsShown)
    }

    /** An explicit Intent with no action; see the class KDoc for why not launch(Class). */
    private fun launch(component: String? = null): ActivityScenario<MainActivity> {
        val intent = Intent(ApplicationProvider.getApplicationContext(), MainActivity::class.java)
        if (component != null) intent.putExtra(MainActivity.EXTRA_COMPONENT, component)
        return ActivityScenario.launch(intent)
    }

    /** From BnDemo: forward to settings, then back. A forward step from a fresh mount records
     * "/", so the back returns to BnDemo and consumes the only history slot: the session is at
     * its root, and back is disabled, whatever earlier tests left. */
    private fun goForwardAndBackToTheRoot(scenario: ActivityScenario<MainActivity>) {
        tapButton(scenario, "Settings →")
        assertTrue("the swap to settings never completed within 10s",
            pollUntil(scenario, 10_000) { settingsTitle(it) != null && !hasEditText(it) })
        assertTrue("back is not enabled on settings, so the drain cannot run",
            pollUntil(scenario, 10_000) { it.onBackPressedDispatcher.hasEnabledCallbacks() })
        scenario.onActivity { it.onBackPressedDispatcher.onBackPressed() }
        assertTrue("BnDemo never returned within 10s of the draining back",
            pollUntil(scenario, 10_000) { form(it) != null && hasEditText(it) && settingsTitle(it) == null })
        assertTrue("back stayed enabled at the root after the draining back",
            pollUntil(scenario, 10_000) { !it.onBackPressedDispatcher.hasEnabledCallbacks() })
    }

    private fun widgetRoot(act: MainActivity): FrameLayout? = act.findViewById(R.id.widget_root)

    /** BnDemo's form div: widget_root → ScrollView → content → the form (HostEventAndroidTest). */
    private fun form(act: MainActivity): ViewGroup? {
        val scroll = widgetRoot(act)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? ScrollView ?: return null
        val content = scroll.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup ?: return null
        return (content.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup)
            ?.takeIf { it.childCount >= 6 }
    }

    /** The settings title: a non-Button TextView reading exactly "Settings". */
    private fun settingsTitle(act: MainActivity): TextView? =
        widgetRoot(act)?.let { root ->
            firstMatch(root) { v -> v is TextView && v !is Button && v.text.toString() == "Settings" }
        } as? TextView

    /** True while any EditText is on screen: BnDemo's input. */
    private fun hasEditText(act: MainActivity): Boolean =
        widgetRoot(act)?.let { firstMatch(it) { v -> v is EditText } } != null

    private fun buttonLabelled(act: MainActivity, label: String): Button? =
        widgetRoot(act)?.let { root ->
            firstMatch(root) { v -> v is Button && v.text.toString() == label }
        } as? Button

    private fun pollUntil(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long,
        sleepMs: Long = 250,
        predicate: (MainActivity) -> Boolean,
    ): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            val ok = AtomicReference(false)
            scenario.onActivity { act -> ok.set(predicate(act)) }
            if (ok.get()) return true
            Thread.sleep(sleepMs)
        }
        return false
    }

    private fun pollState(
        scenario: ActivityScenario<MainActivity>,
        state: Lifecycle.State,
        deadlineMs: Long,
    ): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            if (scenario.state == state) return true
            Thread.sleep(100)
        }
        return scenario.state == state
    }

    private fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            val button = buttonLabelled(act, label)
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
