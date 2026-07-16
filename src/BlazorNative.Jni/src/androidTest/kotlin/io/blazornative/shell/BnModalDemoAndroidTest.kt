package io.blazornative.shell

import android.content.Intent
import android.graphics.Rect
import android.graphics.drawable.ColorDrawable
import android.net.Uri
import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ProgressBar
import android.widget.Switch
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
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
 * Phase 7.4 Gate 2 — **`/modal` ON THE DEVICE** (design §"The proof surface").
 * Mounts `BnModalDemo` through the real NativeAOT boot and asserts the numbers
 * `BnModalDemoTests.cs` pinned as the source of truth, plus the HOST-DERIVED
 * half only a device can assert (the 6.3 oracle discipline): the scrim = the
 * ROOT's own bounds, read at assert time; the content box's DECLARED 280 × 180
 * centered at ((W − 280)/2, (H − 180)/2) against those bounds; the overlay the
 * root's LAST subview; the siblings holding the EXACT frames they hold with
 * the modal closed (the anchor's zero-footprint rule as a frame assertion —
 * the modal sits BETWEEN two declared-size boxes on purpose); the indicator's
 * intrinsic size by ORACLE in BOTH hosting contexts.
 *
 * **EVERY DISMISSAL IS A COUNTED WIRE DISPATCH** ([WidgetMapper.
 * clickDispatchesSent]) — the shell never closes anything itself: a scrim tap
 * dispatches the modal's `click` (the REQUEST), .NET flips Visible, and the
 * remove that follows is .NET's answer. The content-box SWALLOW is likewise a
 * real dispatch that deliberately moves nothing — only the counter can see it
 * (the `changeDispatchesSent` precedent).
 *
 * **THE HIDE FRAME IS FIVE REMOVES, not one** (the Gate 1 finding, recorded in
 * BnModalDemoTests' header): the modal's own diff plus the four nested
 * components' disposal removes, descendants first (the 7.2 removes-first
 * shape). The dismiss test asserts the purge under that composite shape — the
 * node counts return to the closed baseline, the overlay count to 0 — which is
 * exactly what the synthetic one-remove tests (WidgetMapperModalTest) cannot
 * prove.
 *
 * **ANDROID ONLY — BACK ROUTING** (design decision 3): back with the modal
 * open dismiss-REQUESTS (and does not navigate); back with it closed takes the
 * navigation-back path as before — for this history-less launch that is the
 * rc-1 at-root default, observable as `isFinishing`.
 */
@RunWith(AndroidJUnit4::class)
class BnModalDemoAndroidTest {

    private companion object {
        // BnModalDemo.razor's consts (derived there, transcribed here — the
        // BnScrollDemo discipline) + BnModal's pinned default scrim.
        const val CONTENT_W = 280f
        const val CONTENT_H = 180f
        const val SIBLING_W = 220f
        const val SIBLING_H = 48f
        const val BACK_ROW_W = 300f
        const val SCRIM_COLOR = "#80000000"
        const val ECHO_INITIAL = "sw:false"
        const val ECHO_TOGGLED = "sw:true"
    }

    private val instr = InstrumentationRegistry.getInstrumentation()

    // ── Tree access (BnModalDemo.razor's frame table) ─────────────────────────

    private fun widgetRoot(act: MainActivity): FrameLayout? =
        act.findViewById(R.id.widget_root)

    /** The page column: widget_root's FIRST child (the overlay, when live, is
     * the LAST — asserting through index 0 is itself part of the model). */
    private fun page(act: MainActivity): ViewGroup? =
        widgetRoot(act)?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup

    /** The live overlay: widget_root's SECOND (and last) child while the modal
     * is shown; null while it is not. */
    private fun overlay(act: MainActivity): ViewGroup? =
        widgetRoot(act)?.takeIf { it.childCount == 2 }?.getChildAt(1) as? ViewGroup

    /** The content box: the overlay's single child, once its four children landed. */
    private fun contentBox(act: MainActivity): ViewGroup? =
        (overlay(act)?.takeIf { it.childCount == 1 }?.getChildAt(0) as? ViewGroup)
            ?.takeIf { it.childCount == 4 }

    /** The echo: content-box child [0] — BnText's span, text-collapsed. */
    private fun echo(act: MainActivity): TextView? = contentBox(act)?.getChildAt(0) as? TextView

    private fun launchByName(): ActivityScenario<MainActivity> {
        val intent = Intent(instr.targetContext, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnModalDemo")
        return ActivityScenario.launch(intent)
    }

    /** Launch-by-name + scoped use, returning Unit (a JUnit @Test must be void
     * — the BnFormDemo `withForm` shape). */
    private fun withModalDemo(block: (ActivityScenario<MainActivity>) -> Unit) {
        launchByName().use(block)
    }

    /** The closed mount, settled: five root-column children, real bounds. */
    private fun pollForPage(scenario: ActivityScenario<MainActivity>, timeoutMs: Long = 60_000): Boolean =
        pollUntil(scenario, timeoutMs) { act ->
            val p = page(act)
            p != null && p.childCount == 5 && p.height > 0 &&
                widgetRoot(act)?.childCount == 1
        }

    /** The SHOW settled: overlay attached last, content box with its four
     * children, echo painted. */
    private fun pollForOpen(scenario: ActivityScenario<MainActivity>, timeoutMs: Long = 10_000): Boolean =
        pollUntil(scenario, timeoutMs) { act ->
            val box = contentBox(act)
            box != null && box.height > 0 && page(act)?.childCount == 6 &&
                (echo(act)?.text?.toString() ?: "").isNotEmpty()
        }

    /** The HIDE settled: the overlay gone, the page column back to five. */
    private fun pollForClosed(scenario: ActivityScenario<MainActivity>, timeoutMs: Long = 10_000): Boolean =
        pollUntil(scenario, timeoutMs) { act ->
            widgetRoot(act)?.childCount == 1 && page(act)?.childCount == 5
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
            if (ok.get()) {
                instr.waitForIdleSync()
                return true
            }
            Thread.sleep(150)
        }
        return false
    }

    private fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            val button = widgetRoot(act)?.let {
                firstMatch(it) { v -> v is Button && v.text.toString() == label }
            }
            if (button != null) {
                button.performClick()
                clicked.set(true)
            }
        }
        assertTrue("Button '$label' not found on screen", clicked.get())
    }

    /** A REAL tap at view-local ([x], [y]): DOWN+UP through dispatchTouchEvent —
     * the touch path a finger takes, so child consumption is exercised (the
     * BnFormDemo helper, with an explicit point: the scrim tap must land on the
     * scrim itself, outside the centered content box). */
    private fun tapAt(view: View, x: Float, y: Float) {
        val now = SystemClock.uptimeMillis()
        val down = MotionEvent.obtain(now, now, MotionEvent.ACTION_DOWN, x, y, 0)
        val up = MotionEvent.obtain(now, now + 50, MotionEvent.ACTION_UP, x, y, 0)
        view.dispatchTouchEvent(down)
        view.dispatchTouchEvent(up)
        down.recycle()
        up.recycle()
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

    private fun frameOf(v: View) = Rect(v.left, v.top, v.right, v.bottom)

    /** The indicator ORACLE (the 6.3 method, the BnFormDemo checkbox shape): a
     * fresh ProgressBar measured with the SAME specs Yoga's measure func hands
     * the live one — EXACTLY the stretched width, UNSPECIFIED height. */
    private fun assertIndicatorOracle(what: String, live: View) {
        assertTrue("$what must be a ProgressBar", live is ProgressBar)
        val oracle = ProgressBar(live.context)
        oracle.measure(
            View.MeasureSpec.makeMeasureSpec(live.width, View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED))
        assertTrue("$what must have a real height", live.height > 0)
        assertEquals("$what.h must equal what the platform's OWN spinner measures — " +
            "a fabricated measure func passes every relational assertion and fails this",
            oracle.measuredHeight.toFloat(), live.height.toFloat(), 1f)
    }

    // ── [1] The closed mount, via the ROUTE (the DEEP_LINK_COMPONENTS row) ────

    @Test fun mounting_by_deep_link_matches_the_closed_goldens_numbers() {
        val intent = Intent(Intent.ACTION_VIEW, Uri.parse("blazornative://modal"))
            .setClass(ApplicationProvider.getApplicationContext(), MainActivity::class.java)
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnModalDemo never mounted from the deep link within 60s — the " +
                "DEEP_LINK_COMPONENTS '/modal' row is what this launch asserts",
                pollForPage(scenario))
            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                val root = page(act)!!

                // The golden's child order: trigger, box A, box B, indicator, back row.
                val trigger = root.getChildAt(0) as Button
                assertEquals("Show modal", trigger.text.toString())
                // Box A: declared 220 × 48 at x = 0; its y is font-dependent (it
                // sits under the measured trigger) — declared where asserted,
                // measured where not (the 6.3 rule).
                assertEquals(0, root.getChildAt(1).left)
                assertEquals(SIBLING_W, root.getChildAt(1).width / d, 0.5f)
                assertEquals(SIBLING_H, root.getChildAt(1).height / d, 0.5f)
                assertEquals("box B sits DIRECTLY below box A — closed means zero modal " +
                    "wire presence, nothing between the siblings yet",
                    root.getChildAt(1).bottom, root.getChildAt(2).top)
                assertEquals(SIBLING_W, root.getChildAt(2).width / d, 0.5f)
                assertEquals(SIBLING_H, root.getChildAt(2).height / d, 0.5f)

                // The page-level indicator: decision 5's second hosting context.
                assertIndicatorOracle("the page indicator", root.getChildAt(3))

                // Nav parity: the back row's declared 300.
                val backRow = root.getChildAt(4) as ViewGroup
                assertEquals(BACK_ROW_W, backRow.width / d, 0.5f)
                assertEquals("← Back", (backRow.getChildAt(0) as Button).text.toString())

                // No overlay, no scrim: the modal has ZERO presence while closed.
                assertEquals(1, widgetRoot(act)!!.childCount)
                assertEquals("no live overlay while closed", 0, act.mapper.modalOverlayCount)
            }
        }
    }

    // ── [2] The SHOW frame table (the design's numbers, on the glass) ─────────

    @Test fun show_matches_the_frame_table_and_the_siblings_hold_their_frames() = withModalDemo { scenario ->
        assertTrue(pollForPage(scenario))

        // Capture the siblings' EXACT pixel frames while closed.
        val boxABefore = AtomicReference<Rect>()
        val boxBBefore = AtomicReference<Rect>()
        scenario.onActivity { act ->
            boxABefore.set(frameOf(page(act)!!.getChildAt(1)))
            boxBBefore.set(frameOf(page(act)!!.getChildAt(2)))
        }

        tapButton(scenario, "Show modal")
        assertTrue("the modal never opened within 10s", pollForOpen(scenario))

        scenario.onActivity { act ->
            val d = act.resources.displayMetrics.density
            val host = widgetRoot(act)!!
            val over = overlay(act)!!

            // THE OVERLAY IS THE ROOT'S LAST SUBVIEW, and the scrim IS the
            // root's own bounds — read at assert time, never transcribed.
            assertTrue("the overlay is the LAST subview", host.getChildAt(host.childCount - 1) === over)
            assertEquals("scrim.w = root.w", host.width, over.width)
            assertEquals("scrim.h = root.h", host.height, over.height)
            assertEquals(0, over.left); assertEquals(0, over.top)
            assertEquals("the scrim paints BnModal's pinned default",
                android.graphics.Color.parseColor(SCRIM_COLOR),
                (over.background as ColorDrawable).color)

            // The content box: DECLARED 280 × 180 (cross-platform), centered
            // against the root (host-derived): ((W − 280)/2, (H − 180)/2) in dp.
            val box = contentBox(act)!!
            val rootW = host.width / d
            val rootH = host.height / d
            assertEquals("box.w — the DECLARED width", CONTENT_W, box.width / d, 0.5f)
            assertEquals("box.h — the DECLARED height", CONTENT_H, box.height / d, 0.5f)
            assertEquals("box.x = (W − 280)/2 — the design's arithmetic",
                (rootW - CONTENT_W) / 2, box.left / d, 1f)
            assertEquals("box.y = (H − 180)/2", (rootH - CONTENT_H) / 2, box.top / d, 1f)

            // Inside, in page order: echo, switch, indicator (its OVERLAY
            // hosting context — decision 5, proven in both), dismiss.
            assertEquals(ECHO_INITIAL, (box.getChildAt(0) as TextView).text.toString())
            assertFalse((box.getChildAt(1) as Switch).isChecked)
            assertIndicatorOracle("the in-modal indicator", box.getChildAt(2))
            assertEquals("Dismiss", (box.getChildAt(3) as Button).text.toString())

            // THE ZERO-FOOTPRINT RULE AS A FRAME ASSERTION: the page column now
            // holds SIX children — the anchor took index 2, between the boxes —
            // and the siblings hold the EXACT pixel frames they held closed.
            val root = page(act)!!
            assertEquals(6, root.childCount)
            val anchor = root.getChildAt(2)
            assertEquals("the anchor is 0 wide", 0, anchor.width)
            assertEquals("the anchor is 0 tall", 0, anchor.height)
            assertEquals("box A did not move", boxABefore.get(), frameOf(root.getChildAt(1)))
            assertEquals("box B did not move — the anchor contributed NOTHING to the " +
                "flex flow", boxBBefore.get(), frameOf(root.getChildAt(3)))

            // The bookkeeping agrees: one live overlay, in both trees.
            assertEquals(1, act.mapper.modalOverlayCount)
            assertEquals(1, act.mapper.yogaOverlayNodeCount)
        }
    }

    // ── [3] The wire INSIDE the overlay (decision 1's whole point) ────────────

    @Test fun switch_inside_the_modal_round_trips_into_the_echo() = withModalDemo { scenario ->
        assertTrue(pollForPage(scenario))
        tapButton(scenario, "Show modal")
        assertTrue(pollForOpen(scenario))

        // Toggle the switch with a REAL tap: listener → dispatch_event →
        // NativeAOT → @bind → re-render → the echo TextView repaints. Same
        // production ingress as every page — no overlay-special path anywhere.
        scenario.onActivity { act ->
            val sw = contentBox(act)!!.getChildAt(1)
            tapAt(sw, sw.width / 2f, sw.height / 2f)
        }
        assertTrue("the in-modal switch round-trip never re-rendered the echo",
            pollUntil(scenario, 10_000) { act -> echo(act)?.text?.toString() == ECHO_TOGGLED })

        // …and the re-render REPLACED text in place: the modal is still open,
        // the same overlay, the switch now visually ON via the value echo.
        scenario.onActivity { act ->
            assertEquals(1, act.mapper.modalOverlayCount)
            assertTrue((contentBox(act)!!.getChildAt(1) as Switch).isChecked)
        }
    }

    // ── [4] Hide is unmount: the composite purge + the overlay-count pin ──────

    @Test fun dismiss_hides_and_the_overlay_count_returns_to_0_under_the_FIVE_remove_frame() =
        withModalDemo { scenario ->
            assertTrue(pollForPage(scenario))

            val nodesClosed = AtomicInteger(); val yogaClosed = AtomicInteger()
            scenario.onActivity { act ->
                nodesClosed.set(act.mapper.nodeCount)
                yogaClosed.set(act.mapper.yogaNodeCount)
            }

            tapButton(scenario, "Show modal")
            assertTrue(pollForOpen(scenario))
            scenario.onActivity { act ->
                assertTrue("the show must have created nodes", act.mapper.nodeCount > nodesClosed.get())
                assertEquals(1, act.mapper.modalOverlayCount)
            }

            // The app's OWN dismiss button — the hide frame this dispatch answers
            // with is the COMPOSITE shape (the Gate 1 finding): FIVE removes, the
            // four nested components' disposal removes ahead of the modal's own
            // (7.2 removes-first). The shell processes them in order; the modal's
            // remove is the one the two-subtree purge hangs off.
            tapButton(scenario, "Dismiss")
            assertTrue("the modal never closed within 10s", pollForClosed(scenario))

            scenario.onActivity { act ->
                assertEquals("THE PIN: the node count is back at the closed baseline — " +
                    "under the REAL renderer's five-remove hide frame, not the synthetic " +
                    "one-remove shape", nodesClosed.get(), act.mapper.nodeCount)
                assertEquals(yogaClosed.get(), act.mapper.yogaNodeCount)
                assertEquals("the overlay count is back to 0 (both subtrees purged)",
                    0, act.mapper.modalOverlayCount)
                assertEquals(0, act.mapper.yogaOverlayNodeCount)
            }
        }

    // ── [5]/[6] Scrim-tap dismisses; content-tap does not (decision 4) ────────

    @Test fun scrim_tap_dismiss_requests_and_the_bound_page_hides() = withModalDemo { scenario ->
        assertTrue(pollForPage(scenario))
        tapButton(scenario, "Show modal")
        assertTrue(pollForOpen(scenario))

        val clicksBefore = AtomicInteger()
        scenario.onActivity { act -> clicksBefore.set(act.mapper.clickDispatchesSent) }

        // A REAL touch on the scrim itself — the top-left corner, well outside
        // the centered content box (decision 4's rule: a tap dismiss-requests
        // ONLY when it lands on the scrim view itself).
        scenario.onActivity { act -> tapAt(overlay(act)!!, 10f, 10f) }

        assertTrue("the scrim tap never hid the modal", pollForClosed(scenario))
        scenario.onActivity { act ->
            assertEquals("the dismissal was a COUNTED wire dispatch — the shell closed " +
                "nothing itself; the remove is .NET's answer to the request",
                clicksBefore.get() + 1, act.mapper.clickDispatchesSent)
            assertEquals(0, act.mapper.modalOverlayCount)
        }
    }

    @Test fun content_box_tap_does_NOT_dismiss_but_its_swallow_dispatch_is_real() =
        withModalDemo { scenario ->
            assertTrue(pollForPage(scenario))
            tapButton(scenario, "Show modal")
            assertTrue(pollForOpen(scenario))

            val clicksBefore = AtomicInteger()
            scenario.onActivity { act -> clicksBefore.set(act.mapper.clickDispatchesSent) }

            // A REAL touch on the content box's own padding area (inside the box,
            // outside its children): the clickable box CONSUMES it before it can
            // fall through to the scrim — the Android half of decision 4's rule.
            scenario.onActivity { act ->
                val box = contentBox(act)!!
                tapAt(box, box.width / 2f, 6f)
            }
            instr.waitForIdleSync()
            Thread.sleep(500) // a dismissal re-render would land within this window
            instr.waitForIdleSync()

            scenario.onActivity { act ->
                assertEquals("the modal is STILL OPEN — a content tap is not a dismissal",
                    1, act.mapper.modalOverlayCount)
                assertEquals("…but the SWALLOW dispatch was real (rc 0, moves nothing) — " +
                    "only the counter can see it", clicksBefore.get() + 1,
                    act.mapper.clickDispatchesSent)
                assertEquals("…and the echo never moved", ECHO_INITIAL,
                    echo(act)?.text?.toString())
            }
        }

    // ── [7] Back: dismiss-request first, navigation-back only when closed ─────

    @Test fun back_with_the_modal_open_dismiss_requests_with_it_closed_it_navigates() =
        withModalDemo { scenario ->
            assertTrue(pollForPage(scenario))
            tapButton(scenario, "Show modal")
            assertTrue(pollForOpen(scenario))

            val clicksBefore = AtomicInteger()
            scenario.onActivity { act -> clicksBefore.set(act.mapper.clickDispatchesSent) }

            // Back #1 — the modal is open: the shell consults the modal stack
            // BEFORE navigation-back (decision 3), dispatch-requests dismissal on
            // the modal's click wire, and CONSUMES the event: no navigation, no
            // finish — and the hide that follows is .NET's, not the shell's.
            scenario.onActivity { act -> act.onBackPressed() }
            assertTrue("back never dismissed the open modal", pollForClosed(scenario))
            scenario.onActivity { act ->
                assertFalse("back with a live overlay must NOT finish the activity",
                    act.isFinishing)
                assertEquals("the dismissal rode the modal's click wire, counted",
                    clicksBefore.get() + 1, act.mapper.clickDispatchesSent)
                assertEquals("the page did NOT navigate: BnModalDemo is still mounted",
                    "Show modal", (page(act)!!.getChildAt(0) as Button).text.toString())
            }

            // Back #2 — no modal is open: the consult declines and the
            // NAVIGATION-BACK path runs AS BEFORE (the 5.1 behavior every page
            // has had). The nav stack is PROCESS-GLOBAL across the shared
            // instrumented session, so the rc depends on what ran before this
            // test: with history .NET navigates back (rc 0 — BnDemo swaps in),
            // at root it declines (rc 1 — default back finishes the activity).
            // EITHER proves the point: the back left BnModalDemo, through the
            // pre-existing path, not through a modal consult. (The mutation
            // that drops the consult reddens back #1 above while this half —
            // and HostEventAndroidTest.predictive_back — stays green: the
            // design's own mutation expectation.)
            scenario.onActivity { act -> act.onBackPressed() }
            assertTrue("back with NO live overlay must take the navigation-back path " +
                "(BnDemo returns, or the at-root default finishes)",
                pollUntilBackNavigatedAway(scenario))
        }

    /** Back #2's settle: the activity finished (rc 1 at-root default) OR the
     * screen swapped off BnModalDemo (rc 0 — .NET navigated back). */
    private fun pollUntilBackNavigatedAway(scenario: ActivityScenario<MainActivity>): Boolean {
        val deadline = System.currentTimeMillis() + 10_000
        while (System.currentTimeMillis() < deadline) {
            val ok = AtomicReference(false)
            try {
                scenario.onActivity { act ->
                    ok.set(act.isFinishing ||
                        firstMatch(widgetRoot(act) ?: return@onActivity) { v ->
                            v is Button && v.text.toString() == "Show modal"
                        } == null)
                }
            } catch (_: RuntimeException) {
                return true // destroyed mid-poll: the finish landed
            }
            if (ok.get()) return true
            Thread.sleep(150)
        }
        return false
    }
}
