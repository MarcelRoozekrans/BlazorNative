package io.blazornative.shell

import android.content.Intent
import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.CheckBox
import android.widget.FrameLayout
import android.widget.SeekBar
import android.widget.Spinner
import android.widget.Switch
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 7.3 Gate 2 Task 2.3 — **`/form` ON THE DEVICE** (M7 DoD #4's Android
 * half). Mounts `BnFormDemo` through the real NativeAOT boot and asserts the
 * numbers **`BnFormDemoTests.cs` pinned as the source of truth** — derived
 * there, transcribed here, asserted by Gate 3 on the iOS simulator as THE SAME
 * NUMBERS (for the LAYOUT — declared 240/300 widths; the intrinsic sizes are
 * each platform's OWN, asserted per-platform by ORACLE, the 6.3 method):
 *
 *     10 root children: 2 CheckBox, 2 Switch, 2 SeekBar, 2 Spinner,
 *     the echo TextView, the back row. Echo literal "cb:false sw:true sl:25 pk:0".
 *     Bound slider stepped (max 20, progress 5 = value 25); disabled slider
 *     continuous (max 1000, progress 500 = value 50). Both pickers hold the
 *     ["Alpha","Bravo","Charlie"] literal; selections 0 (bound) / 1 (disabled).
 *
 * The round-trips ride **the real wire**: a drive on the native widget → the
 * change listener → `blazornative_dispatch_event` → NativeAOT → the `@bind-`
 * pair → a re-render → the echo TextView repaints. Every echo assertion is
 * therefore an end-to-end assertion of Gate 1's .NET half AND this gate's
 * listeners at once.
 *
 * **"DISABLED CONTROLS DISPATCH NOTHING" IS A COUNTER ASSERTION**
 * ([WidgetMapper.changeDispatchesSent]), not an echo one — deliberately: the
 * demo's disabled handlers are UNBOUND .NET-side (the recorded attach
 * decision), so a dispatch that leaked from a disabled widget would move no
 * echo and no frame; only the counter can see it. The taps are REAL touch
 * streams (dispatchTouchEvent), not performClick — `View.performClick()`
 * bypasses the enabled check by design, so it would "prove" a false failure;
 * the touch path (`onTouchEvent` consumes-but-ignores on a disabled view) is
 * what a finger actually exercises. The same gesture is proven live on the
 * BOUND checkbox first, so the disabled quartet's silence is a fact about
 * `Enabled`, not about the gesture helper.
 */
@RunWith(AndroidJUnit4::class)
class BnFormDemoAndroidTest {

    private companion object {
        // BnFormDemo.razor's consts (derived there, transcribed here — the
        // BnScrollDemo discipline).
        const val CONTROL_W = 240f
        const val BACK_ROW_W = 300f
        const val INITIAL_ECHO = "cb:false sw:true sl:25 pk:0"
        val ITEMS = listOf("Alpha", "Bravo", "Charlie")
        // The slider geometry under the precision contract (WidgetMapper.SliderState):
        // bound 25/0..100 step 5 → max 20, progress 5; disabled 50/0..100
        // continuous → max 1000, progress 500.
        const val BOUND_SLIDER_MAX = 20
        const val BOUND_SLIDER_PROGRESS = 5
        const val DISABLED_SLIDER_MAX = 1000
        const val DISABLED_SLIDER_PROGRESS = 500
    }

    private val instr = InstrumentationRegistry.getInstrumentation()

    // ── Tree access ───────────────────────────────────────────────────────────

    private fun rootOf(act: MainActivity): ViewGroup? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup

    private fun echoOf(root: ViewGroup): TextView = root.getChildAt(8) as TextView

    private fun launch(): ActivityScenario<MainActivity> {
        val ctx = instr.targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnFormDemo")
        return ActivityScenario.launch(intent)
    }

    private fun withForm(block: (ActivityScenario<MainActivity>) -> Unit) {
        launch().use(block)
    }

    /** Polls until the page is mounted AND settled: 10 children, the echo at
     * [echo], and both pickers' ASYNC selection positioning delivered. */
    private fun pollForForm(
        scenario: ActivityScenario<MainActivity>,
        echo: String = INITIAL_ECHO,
        timeoutMs: Long = 60_000,
    ): Boolean {
        val deadline = System.currentTimeMillis() + timeoutMs
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = rootOf(act)?.takeIf { it.childCount == 10 }
                ready.set(root != null &&
                    root.height > 0 &&
                    (root.getChildAt(8) as? TextView)?.text?.toString() == echo &&
                    (root.getChildAt(6) as? Spinner)?.selectedItemPosition == 0 &&
                    (root.getChildAt(7) as? Spinner)?.selectedItemPosition == 1)
            }
            if (ready.get()) {
                instr.waitForIdleSync()
                return true
            }
            Thread.sleep(150)
        }
        return false
    }

    /** Polls the echo only (post-round-trip: the re-render is async). */
    private fun pollForEcho(
        scenario: ActivityScenario<MainActivity>,
        echo: String,
        timeoutMs: Long = 10_000,
    ): Boolean {
        val deadline = System.currentTimeMillis() + timeoutMs
        val seen = AtomicReference("")
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                seen.set(rootOf(act)?.let { echoOf(it).text.toString() } ?: "")
            }
            if (seen.get() == echo) return true
            Thread.sleep(100)
        }
        return false
    }

    /** A REAL tap: DOWN+UP through dispatchTouchEvent, view-local center.
     * NOT performClick — see the class KDoc (performClick bypasses `enabled`). */
    private fun tap(view: View) {
        val now = SystemClock.uptimeMillis()
        val down = MotionEvent.obtain(now, now, MotionEvent.ACTION_DOWN,
            view.width / 2f, view.height / 2f, 0)
        val up = MotionEvent.obtain(now, now + 50, MotionEvent.ACTION_UP,
            view.width / 2f, view.height / 2f, 0)
        view.dispatchTouchEvent(down)
        view.dispatchTouchEvent(up)
        down.recycle()
        up.recycle()
    }

    // ── [1] The mount golden, on the glass ────────────────────────────────────

    @Test fun mounting_form_matches_the_dotnet_goldens_numbers() = withForm { scenario ->
        assertTrue("BnFormDemo never mounted/settled within 60s", pollForForm(scenario))
        scenario.onActivity { act ->
            val d = act.resources.displayMetrics.density
            val root = rootOf(act)!!

            // The widget classes, in the golden's child order — TWO of each new
            // NodeType actually decoded and instantiated (a missed nodeTypes
            // entry would have made a "?" → TextView fallback here).
            val cb0 = root.getChildAt(0) as CheckBox
            val cb1 = root.getChildAt(1) as CheckBox
            val sw2 = root.getChildAt(2) as Switch
            val sw3 = root.getChildAt(3) as Switch
            val sb4 = root.getChildAt(4) as SeekBar
            val sb5 = root.getChildAt(5) as SeekBar
            val sp6 = root.getChildAt(6) as Spinner
            val sp7 = root.getChildAt(7) as Spinner

            // Initial state = the goldens' prop tables.
            assertFalse("bound checkbox starts unchecked", cb0.isChecked)
            assertTrue("disabled checkbox is fixed CHECKED", cb1.isChecked)
            assertTrue("bound switch starts ON", sw2.isChecked)
            assertTrue("disabled switch is fixed ON", sw3.isChecked)
            assertEquals("bound slider is STEPPED: 0..100 step 5 → 20 units",
                BOUND_SLIDER_MAX, sb4.max)
            assertEquals("…at value 25 → progress 5", BOUND_SLIDER_PROGRESS, sb4.progress)
            assertEquals("disabled slider is CONTINUOUS: 1000 units",
                DISABLED_SLIDER_MAX, sb5.max)
            assertEquals("…at value 50 → progress 500", DISABLED_SLIDER_PROGRESS, sb5.progress)
            assertEquals("the items literal, through the strict parser",
                ITEMS, (0 until sp6.adapter.count).map { sp6.adapter.getItem(it) })
            assertEquals(ITEMS, (0 until sp7.adapter.count).map { sp7.adapter.getItem(it) })
            assertEquals(0, sp6.selectedItemPosition)
            assertEquals(1, sp7.selectedItemPosition)

            // The disabled quartet renders DISABLED (its dispatch silence is [4]).
            assertFalse(cb1.isEnabled); assertFalse(sw3.isEnabled)
            assertFalse(sb5.isEnabled); assertFalse(sp7.isEnabled)
            assertTrue(cb0.isEnabled); assertTrue(sw2.isEnabled)
            assertTrue(sb4.isEnabled); assertTrue(sp6.isEnabled)

            // The DECLARED widths — the numbers asserted CROSS-platform (the
            // measurement rule: declared where asserted on both shells).
            assertEquals("bound slider width is the declared 240", CONTROL_W, sb4.width / d, 0.5f)
            assertEquals(CONTROL_W, sb5.width / d, 0.5f)
            assertEquals(CONTROL_W, sp6.width / d, 0.5f)
            assertEquals(CONTROL_W, sp7.width / d, 0.5f)

            // The echo literal (Gate 1 pinned it for exactly this line).
            assertEquals(INITIAL_ECHO, echoOf(root).text.toString())

            // Nav parity: the back row (declared 300) and its measured button.
            val backRow = root.getChildAt(9) as ViewGroup
            assertEquals(BACK_ROW_W, backRow.width / d, 0.5f)
            val back = backRow.getChildAt(0) as Button
            assertEquals("← Back", back.text.toString())
        }
    }

    // ── [2] The intrinsic-size ORACLE (checkbox/switch — the 6.3 method) ─────

    /**
     * The checkbox/switch quartet declares NO styles (the golden pins zero), so
     * their sizes are the PLATFORM's own — asserted against the platform's own
     * measurement, never a transcribed constant: a fresh widget of the same
     * class, measured with the same specs Yoga's measure func hands the live
     * one (EXACTLY the stretched width — a column's default alignItems is
     * stretch, so the cross-axis WIDTH is layout, not intrinsic — and
     * UNSPECIFIED height). Gate 3 mirrors this with a fresh UISwitch and
     * `sizeThatFits` — DIFFERENT numbers, same method: frame parity applies to
     * layout, never to intrinsic control sizes.
     */
    @Test fun checkbox_and_switch_take_the_platforms_own_intrinsic_height() =
        withForm { scenario ->
            assertTrue(pollForForm(scenario))
            scenario.onActivity { act ->
                val root = rootOf(act)!!
                fun assertOracleHeight(what: String, live: View, oracle: View) {
                    oracle.measure(
                        View.MeasureSpec.makeMeasureSpec(live.width, View.MeasureSpec.EXACTLY),
                        View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED))
                    assertEquals("$what.h must equal what the platform's OWN widget measures " +
                        "— a fabricated measure func passes every relational assertion and " +
                        "fails this one", oracle.measuredHeight.toFloat(), live.height.toFloat(), 1f)
                    assertTrue("$what must have a real height", live.height > 0)
                }
                assertOracleHeight("bound checkbox", root.getChildAt(0), CheckBox(act))
                assertOracleHeight("disabled checkbox", root.getChildAt(1), CheckBox(act))
                assertOracleHeight("bound switch", root.getChildAt(2), Switch(act))
                assertOracleHeight("disabled switch", root.getChildAt(3), Switch(act))

                // The cross-axis width is the STRETCH (Yoga's default alignItems),
                // not an intrinsic: all four fill the column. Pinned so nobody
                // "fixes" a full-width checkbox into a declared width silently.
                val colW = root.width
                for (i in 0..3) {
                    assertEquals("child $i stretches to the column width (alignItems: stretch)",
                        colW, root.getChildAt(i).width)
                }
            }
        }

    // ── [3] The four round-trips, over the REAL wire, into the echo ──────────

    @Test fun driving_each_bound_control_round_trips_into_the_echo() = withForm { scenario ->
        assertTrue(pollForForm(scenario))
        val dispatchesBefore = AtomicInteger(0)
        scenario.onActivity { act -> dispatchesBefore.set(act.mapper.changeDispatchesSent) }

        // Checkbox: a REAL tap (the touch path a finger takes).
        scenario.onActivity { act -> tap(rootOf(act)!!.getChildAt(0)) }
        assertTrue("the checkbox round-trip never re-rendered the echo",
            pollForEcho(scenario, "cb:true sw:true sl:25 pk:0"))
        scenario.onActivity { act ->
            assertTrue("…and the value prop wrote back into the widget",
                (rootOf(act)!!.getChildAt(0) as CheckBox).isChecked)
        }

        // Switch: tap it OFF.
        scenario.onActivity { act -> tap(rootOf(act)!!.getChildAt(2)) }
        assertTrue("the switch round-trip never re-rendered the echo",
            pollForEcho(scenario, "cb:true sw:false sl:25 pk:0"))

        // Slider: progress 12 → value 60 (the precision contract) — programmatic
        // setProgress IS the user-input stand-in (the listener fires for it, the
        // verified finding; a real drag reduces to the same listener).
        scenario.onActivity { act ->
            (rootOf(act)!!.getChildAt(4) as SeekBar).progress = 12
        }
        assertTrue("the slider round-trip never re-rendered the echo — the payload is " +
            "the invariant float 60.0 and .NET echoes it as sl:60",
            pollForEcho(scenario, "cb:true sw:false sl:60 pk:0"))

        // Picker: setSelection IS the dropdown's own item-click path.
        scenario.onActivity { act ->
            (rootOf(act)!!.getChildAt(6) as Spinner).setSelection(2)
        }
        assertTrue("the picker round-trip never re-rendered the echo",
            pollForEcho(scenario, "cb:true sw:false sl:60 pk:2"))

        // Four drives → at least four change dispatches — and the value echoes
        // the runtime wrote back re-fired NONE of them (or this count runs away
        // and the echo above never settles: the loop guards, end to end).
        scenario.onActivity { act ->
            val sent = act.mapper.changeDispatchesSent - dispatchesBefore.get()
            assertTrue("expected exactly the four drives' dispatches (got $sent)", sent == 4)
        }
    }

    // ── [4] Disabled controls dispatch NOTHING (the device half) ─────────────

    @Test fun disabled_controls_dispatch_nothing_under_real_touch() = withForm { scenario ->
        assertTrue(pollForForm(scenario))

        // Sensitivity first: the SAME gesture on the BOUND checkbox moves the
        // echo — so the silence below is a fact about Enabled, not the helper.
        scenario.onActivity { act -> tap(rootOf(act)!!.getChildAt(0)) }
        assertTrue("the tap gesture itself must work (bound checkbox toggles)",
            pollForEcho(scenario, "cb:true sw:true sl:25 pk:0"))

        val before = AtomicInteger(0)
        scenario.onActivity { act -> before.set(act.mapper.changeDispatchesSent) }

        // Tap all four DISABLED controls; try to drag the disabled slider too.
        scenario.onActivity { act ->
            val root = rootOf(act)!!
            tap(root.getChildAt(1)) // disabled checkbox
            tap(root.getChildAt(3)) // disabled switch
            tap(root.getChildAt(5)) // disabled slider (a tap on the track seeks)
            tap(root.getChildAt(7)) // disabled picker
        }
        instr.waitForIdleSync()
        Thread.sleep(500) // an async dispatch would land within this window
        instr.waitForIdleSync()

        scenario.onActivity { act ->
            val root = rootOf(act)!!
            assertEquals("DISABLED CONTROLS DISPATCH NOTHING — the counter is the only " +
                "honest witness (the demo's disabled handlers are unbound .NET-side, so " +
                "a leaked dispatch would move no echo)",
                before.get(), act.mapper.changeDispatchesSent)
            // …and the native state did not move either.
            assertTrue((root.getChildAt(1) as CheckBox).isChecked)
            assertTrue((root.getChildAt(3) as Switch).isChecked)
            assertEquals(DISABLED_SLIDER_PROGRESS, (root.getChildAt(5) as SeekBar).progress)
            assertEquals(1, (root.getChildAt(7) as Spinner).selectedItemPosition)
            assertEquals("…and the echo never moved", "cb:true sw:true sl:25 pk:0",
                echoOf(root).text.toString())
        }
    }
}
