package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import java.util.concurrent.atomic.AtomicReference
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 19.2 click-to-frame measurement. It drives PerfProbe, whose handler is a synchronous
 * counter increment, so each click is one dispatch and one frame. The marks land in logcat; the
 * collector (scripts/perf/android-collect.ps1) dumps them for tools/BlazorNative.PerfReport.
 *
 * Clicks are serial: after each performClick the test waits until the counter shows the new
 * value, because the parser pairs clicks by order. The wire has no test-id prop, so nodes are
 * found by text: the button by its label "Click", the counter by the exact string "Count: n".
 * Row 0 reads "Row 0 count n" and cannot match.
 *
 * Excluded from the PR lane by [PerfMeasurement]. Run it with `-PperfOnly`, or through the
 * collector's click kind. The click count comes from the instrumentation argument perfClicks,
 * default 25. The argument perfLevel is Verbose by default; `default` launches with no level extra,
 * which is how the collector's q1 shows that a click at the default level writes no BNPERF line.
 *
 * BACKPORT: main's class, adapted to what the release this branch is cut from has. It has no
 * shared BootWait/Poll helpers and no `bootedForTest`, so the poll is a private copy of main's
 * and `awaitBooted()` is not called. The wait for "Count: 0" is the gate instead: the click
 * listener exists only once the mount's frame is applied, and a click is a UI event, not one of
 * the host events that `booted` guards.
 */
@PerfMeasurement
@RunWith(AndroidJUnit4::class)
class PerfMeasurementTest {

    @Test
    fun serial_clicks_on_perf_probe() {
        val args = InstrumentationRegistry.getArguments()
        val clicks = args.getString("perfClicks")?.toIntOrNull() ?: DEFAULT_CLICKS
        assertTrue("perfClicks must be positive, got $clicks", clicks > 0)

        val level = args.getString("perfLevel") ?: "Verbose"
        assertTrue("perfLevel must be Verbose or default, got $level", level == "Verbose" || level == "default")

        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "PerfProbe")
        if (level == "Verbose") intent.putExtra(MainActivity.EXTRA_LOG_LEVEL, "Verbose")
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue(
                "the PerfProbe counter 'Count: 0' never appeared within 60s",
                pollUntil(scenario, 60_000, sleepMs = 50) { findText(it, "Count: 0") != null },
            )
            for (n in 1..clicks) {
                val button = AtomicReference<View?>(null)
                scenario.onActivity { act ->
                    val b = findButton(act, "Click")
                    button.set(b)
                    b?.performClick()
                }
                assertNotNull("the 'Click' button was not on the page at click $n", button.get())
                assertTrue(
                    "the counter never showed 'Count: $n' within 10s of click $n",
                    pollUntil(scenario, 10_000, sleepMs = 10) { findText(it, "Count: $n") != null },
                )
            }
        }
    }

    /** Main's Poll.kt pollUntil, without the destroyed-activity rewording. */
    private fun pollUntil(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long,
        sleepMs: Long,
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

    private fun root(act: MainActivity): FrameLayout? = act.findViewById(R.id.widget_root)

    private fun findText(act: MainActivity, exact: String): TextView? =
        root(act)?.let { r ->
            firstMatch(r) { it is TextView && it !is Button && it.text.toString() == exact }
        } as? TextView

    private fun findButton(act: MainActivity, label: String): Button? =
        root(act)?.let { r ->
            firstMatch(r) { it is Button && it.text.toString() == label }
        } as? Button

    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) {
            for (i in 0 until view.childCount) {
                firstMatch(view.getChildAt(i), predicate)?.let { return it }
            }
        }
        return null
    }

    private companion object {
        const val DEFAULT_CLICKS = 25
    }
}
