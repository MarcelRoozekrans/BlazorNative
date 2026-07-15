package io.blazornative.shell

import android.content.Intent
import android.util.Log
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ScrollView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotSame
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.ceil
import kotlin.math.floor

/**
 * Phase 7.2 Gate 2 Task 2.2 — **THE VIRTUALIZED LIST, ON THE DEVICE** (M7 DoD #3).
 *
 * Mounts `BnListDemo` (the `/list` page) through the real NativeAOT boot and
 * asserts the canonical numbers from **`src/BlazorNative.Components/
 * BnListDemo.razor`'s file header** — derived there, not invented here, and
 * asserted by Gate 3 on the iOS simulator as THE SAME NUMBERS:
 *
 *     content height   500 × 64 = 32,000 dp      scroll range  31,600 dp
 *     window @ 0       [0, 11)   → 13 children   spacers  0     | 31,296
 *     window @ 640     [6, 21)   → 17 children   spacers  384   | 30,656
 *     window @ 31,600  [489,500) → 13 children   spacers  31,296| 0
 *
 * **LIVENESS IS A COUNTED ASSERTION**: the synthetic content view's children
 * are ALWAYS 2 spacers + the window rows. 500 rows exist; if more than ~17
 * native EditTexts ever exist at once, virtualization is not virtualizing —
 * and no frame table would notice (every frame stays correct with 500 live
 * rows; only the CHILD COUNT sees it).
 *
 * Unlike every previous demo page, the scroll events here ride **the real
 * wire**: `scrollTo`/`fling` on the device → `setOnScrollChangeListener` →
 * the conflation slot → `blazornative_dispatch_event("scroll", offset-in-dp)`
 * → NativeAOT → `BnListWindow.Compute` → a keyed re-render → patches back to
 * this shell. Every window-slide assertion below is therefore an end-to-end
 * assertion of Gate 1's .NET half AND Gate 2's conflation at once.
 *
 * The THROUGHPUT tests are the contract's evidence row: samples-seen vs
 * events-dispatched, printed as the conflation ratio (grep logcat for
 * `[7.2-throughput]`), plus the never-queue bound — a burst of 100
 * same-message samples may submit at most a handful of dispatches (the first,
 * then one per lane-availability) while the FINAL offset always arrives.
 */
@RunWith(AndroidJUnit4::class)
class BnListDemoAndroidTest {

    private companion object {
        const val TAG = "BlazorNative.Test"

        // BnListDemo.razor's consts (the header's "derived, not invented"
        // discipline): the four inputs and the products the tests assert.
        const val ROWS = 500
        const val ROW_H = 64f
        const val VIEW_W = 300f
        const val VIEW_H = 400f
        const val OVERSCAN = 4
        const val CONTENT_H = ROWS * ROW_H            // 32_000
        const val SCROLL_RANGE = CONTENT_H - VIEW_H   // 31_600
        const val MID_OFFSET = 640f                   // the golden's mid-scroll offset
    }

    // ── The numbers, derived (BnListWindow.Compute's arithmetic, mirrored) ────

    /** The half-open live window at [offsetDp] — the same clamp/floor/ceil
     * `BnListWindow.Compute` performs, so the fling test can derive the
     * expected window from wherever the fling actually settled. */
    private fun window(offsetDp: Float): Pair<Int, Int> {
        val clamped = offsetDp.coerceIn(0f, maxOf(0f, CONTENT_H - VIEW_H))
        val firstVisible = floor(clamped / ROW_H).toInt()
        val lastVisibleEx = ceil((clamped + VIEW_H) / ROW_H).toInt()
        return maxOf(firstVisible - OVERSCAN, 0) to minOf(lastVisibleEx + OVERSCAN, ROWS)
    }

    // ── Tree access ───────────────────────────────────────────────────────────

    private fun rootOf(act: MainActivity): ViewGroup? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup

    private fun scrollOf(act: MainActivity): ScrollView =
        rootOf(act)!!.getChildAt(0) as ScrollView

    private fun contentOf(act: MainActivity): ViewGroup =
        scrollOf(act).getChildAt(0) as ViewGroup

    /** Content child [i] as a ROW (a BnView wrapper whose only child is the
     * row's BnInput). Index 0 and childCount−1 are the SPACERS, not rows. */
    private fun rowInput(content: ViewGroup, i: Int): EditText =
        (content.getChildAt(i) as ViewGroup).getChildAt(0) as EditText

    /** The rows' identities, in order — "Row 6".."Row 20" is the window. */
    private fun hints(content: ViewGroup): List<String> =
        (1 until content.childCount - 1).map { rowInput(content, it).hint.toString() }

    private fun inputByHint(content: ViewGroup, hint: String): EditText? =
        (1 until content.childCount - 1).map { rowInput(content, it) }
            .firstOrNull { it.hint?.toString() == hint }

    // ── Launch + poll ─────────────────────────────────────────────────────────

    private fun launch(): ActivityScenario<MainActivity> {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnListDemo")
        return ActivityScenario.launch(intent)
    }

    /** Launch + use, returning Unit — JUnit test methods must be void, and
     * `ActivityScenario.onActivity` returns the scenario (fluent), so a bare
     * `launch().use { … }` expression body silently is not. */
    private fun withList(block: (ActivityScenario<MainActivity>) -> Unit) {
        launch().use(block)
    }

    /**
     * Polls until the CONTENT view shows the window whose first row is
     * [firstRow] with [childCount] children (2 spacers + rows), laid out, AND
     * the scroll wire is QUIESCENT (nothing in flight, nothing pending) — the
     * settle gate: a window assertion mid-conflation is a race, not a pin.
     */
    private fun pollForWindow(
        scenario: ActivityScenario<MainActivity>,
        firstRow: Int,
        childCount: Int,
        timeoutMs: Long = 60_000,
    ): Boolean {
        val deadline = System.currentTimeMillis() + timeoutMs
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = rootOf(act)?.takeIf { it.childCount == 2 }
                val scroll = root?.getChildAt(0) as? ScrollView
                val content = scroll?.takeIf { it.childCount == 1 }?.getChildAt(0) as? ViewGroup
                ready.set(content != null &&
                    content.childCount == childCount &&
                    content.height > 0 &&
                    (content.getChildAt(1) as? ViewGroup)?.childCount == 1 &&
                    (rowInput(content, 1).hint?.toString() == "Row $firstRow") &&
                    act.mapper.scrollBusyWireCount == 0)
            }
            if (ready.get()) {
                InstrumentationRegistry.getInstrumentation().waitForIdleSync()
                return true
            }
            Thread.sleep(150)
        }
        return false
    }

    private fun assertWindow(
        what: String,
        act: MainActivity,
        start: Int,
        end: Int,
    ) {
        val d = act.resources.displayMetrics.density
        val content = contentOf(act)
        val live = end - start

        assertEquals("$what: LIVENESS — content children are ALWAYS 2 spacers + the window " +
            "([$start,$end) = $live rows). Any other count means virtualization is not " +
            "virtualizing", live + 2, content.childCount)

        // The spacers: index 0 and last, childless, exactly the arithmetic.
        val lead = content.getChildAt(0) as ViewGroup
        val trail = content.getChildAt(content.childCount - 1) as ViewGroup
        assertEquals("$what: the LEAD spacer is childless (a pure height reservation)",
            0, lead.childCount)
        assertEquals("$what: lead spacer = start × 64", start * ROW_H, lead.height / d, 0.5f)
        assertEquals("$what: trail spacer = (500 − end) × 64",
            (ROWS - end) * ROW_H, trail.height / d, 0.5f)
        assertEquals("$what: the trail spacer starts where row ${end - 1} ends",
            end * ROW_H, trail.top / d, 0.5f)

        // The rows: identity ("Row i" — the demo's invariant placeholder),
        // 64dp pitch at ABSOLUTE content y = i × 64, full width.
        assertEquals("$what: the window's rows, in order",
            (start until end).map { "Row $it" }, hints(content))
        for ((j, i) in (start until end).withIndex()) {
            val row = content.getChildAt(1 + j)
            assertEquals("$what: row $i sits at content y = 64 × $i — the spacer arithmetic " +
                "keeps every row at ITS OWN absolute position while the window slides",
                i * ROW_H, row.top / d, 0.5f)
            assertEquals("$what: row $i is exactly one ItemHeight tall", ROW_H, row.height / d, 0.5f)
            assertEquals("$what: row $i stretches to the content width", VIEW_W, row.width / d, 0.5f)
        }

        // And the content node's height is 32,000 BY CONSTRUCTION — spacers +
        // window rows tile it at every offset, virtualized or not.
        assertEquals("$what: content height = 500 × 64 = 32,000dp, Yoga-computed, at EVERY " +
            "window position", CONTENT_H, content.height / d, 0.5f)
    }

    // ── [1] Mount: the header's numbers, at offset 0 ─────────────────────────

    @Test fun mounting_the_list_matches_the_headers_numbers() = withList { scenario ->
        assertTrue("BnListDemo never rendered its mount window within 60s",
            pollForWindow(scenario, firstRow = 0, childCount = 13))
        scenario.onActivity { act ->
            val d = act.resources.displayMetrics.density
            val root = rootOf(act)!!
            val scroll = scrollOf(act)

            assertEquals("the page has two sections: the viewport and the back row",
                2, root.childCount)
            assertFrame("viewport", scroll, 0f, 0f, VIEW_W, VIEW_H)
            assertEquals("a fresh mount sits at offset 0", 0, scroll.scrollY)

            // Window @ 0 = [0, 11): 7 visible + 4 trailing overscan, leading
            // overscan clamped at the list start → 13 children incl. spacers.
            assertWindow("at offset 0", act, start = 0, end = 11)

            // 500 rows EXIST; 11 of them are live. The other 489 are one
            // trailing spacer — that difference is the phase.
            assertEquals("…of which the lead spacer is ZERO-height (nothing above row 0)",
                0, contentOf(act).getChildAt(0).height)

            // Nav parity: the exit is OUTSIDE the viewport, so no amount of
            // scrolling 32,000dp of content can take it off the glass.
            val backRow = root.getChildAt(1) as ViewGroup
            val back = backRow.getChildAt(0) as Button
            assertEquals("← Back", back.text.toString())
            assertEquals("the back row starts where the VIEWPORT ends — outside it, not at " +
                "the bottom of the 32,000dp of content", scroll.bottom, backRow.top)
            assertEquals("…which is 400dp", VIEW_H, backRow.top / d, 0.5f)
        }
    }

    // ── [2] The slide: 0 → 640 over the REAL wire ────────────────────────────

    @Test fun sliding_to_640_via_the_real_wire_moves_the_window_and_the_spacers() =
        withList { scenario ->
            assertTrue(pollForWindow(scenario, firstRow = 0, childCount = 13))
            val dispatchesBefore = AtomicReference(0)
            scenario.onActivity { act ->
                dispatchesBefore.set(act.mapper.scrollDispatchesSent)
                val d = act.resources.displayMetrics.density
                scrollOf(act).scrollTo(0, (MID_OFFSET * d).toInt())
            }

            assertTrue("the window never slid to [6,21) — the scroll event did not arrive, " +
                "or .NET's window math disagrees with the header",
                pollForWindow(scenario, firstRow = 6, childCount = 17, timeoutMs = 30_000))

            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density

                // The wire: the conflated event FIRED, and the freshest offset
                // it carried is the one we drove — in dp.
                assertTrue("at least one conflated scroll dispatch went out",
                    act.mapper.scrollDispatchesSent > dispatchesBefore.get())
                assertEquals("…and the LAST dispatch carried 640dp — px ÷ density at the " +
                    "source (the 6.1 unit rule): the same number iOS will send as pt",
                    MID_OFFSET, act.mapper.lastScrollDispatchDp!!, 0.01f)

                // Window @ 640 = [6, 21): 15 live rows (ceil(400/64) + 2×4) →
                // 17 children; spacers 384 | 30,656 — the header's numbers.
                assertWindow("at offset 640", act, start = 6, end = 21)

                // And on the GLASS: the first VISIBLE row is row 10 (640/64),
                // exactly at the viewport's top edge — rows 6..9 are overscan
                // above it. The user's eye and the window math agree.
                val content = contentOf(act)
                val row10 = content.getChildAt(1 + (10 - 6))
                assertEquals("row 10's content y (640) minus the offset (640) is ZERO: it sits " +
                    "exactly at the viewport's top edge",
                    0f, (screenY(row10) - screenY(scrollOf(act))) / d, 0.5f)
            }
        }

    // ── [3] The bottom window, at max offset ─────────────────────────────────

    @Test fun the_bottom_window_at_max_offset_clamps_to_31600_and_shows_the_last_11_rows() =
        withList { scenario ->
            assertTrue(pollForWindow(scenario, firstRow = 0, childCount = 13))
            scenario.onActivity { act ->
                // Drive it PAST the end: the clamp is itself an assertion.
                scrollOf(act).scrollTo(0, Int.MAX_VALUE / 2)
            }

            assertTrue("the window never slid to the bottom [489,500)",
                pollForWindow(scenario, firstRow = 489, childCount = 13, timeoutMs = 30_000))

            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                val scroll = scrollOf(act)

                assertEquals("THE MAXIMUM IS THE CONTENT SIZE, ARRIVED AT INDEPENDENTLY: " +
                    "ScrollView clamps a runaway offset to content − viewport using the " +
                    "content view's LAID-OUT height — 31,600 here is the framework agreeing " +
                    "with Yoga about the 32,000", SCROLL_RANGE, scroll.scrollY / d, 0.5f)
                assertEquals("…and that clamped offset is what the wire carried",
                    SCROLL_RANGE, act.mapper.lastScrollDispatchDp!!, 0.5f)

                // Window @ 31,600 = [489, 500): trailing overscan clamped at the
                // list end → 13 children; spacers 31,296 | 0.
                assertWindow("at offset 31,600", act, start = 489, end = 500)

                // The last row's bottom edge IS the content's bottom edge IS the
                // viewport's bottom edge: nothing below, nowhere further to go.
                val content = contentOf(act)
                val lastRow = content.getChildAt(content.childCount - 2)
                assertEquals("row 499 ends exactly at content y 32,000",
                    CONTENT_H, lastRow.bottom / d, 0.5f)
            }
        }

    // ── [4] Row state TRAVELS with the @key — and eviction really evicts ─────

    /**
     * The header's proof, verbatim: the row inputs are deliberately UNBOUND, so
     * text typed into one lives ONLY in the native EditText. A row that SURVIVES
     * a window slide (row 6 is in [0,11) AND [6,21)) must keep its node → its
     * view → its text; a row that LEAVES (row 0 is not in [6,21)) is destroyed
     * with its text, and comes back FRESH — losing the text is what proves the
     * eviction was real. @key = the item is why the survivor survives.
     */
    @Test fun row_state_travels_with_the_key_and_eviction_really_evicts() =
        withList { scenario ->
            assertTrue(pollForWindow(scenario, firstRow = 0, childCount = 13))
            val row6View = AtomicReference<EditText>()
            val row0View = AtomicReference<EditText>()

            scenario.onActivity { act ->
                val content = contentOf(act)
                val row6 = inputByHint(content, "Row 6")!!
                val row0 = inputByHint(content, "Row 0")!!
                row6.setText("i travel with my key")
                row0.setText("eviction loses me")
                row6View.set(row6)
                row0View.set(row0)
            }

            // Slide the window to [6, 21): rows 0–5 leave, row 6 survives.
            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                scrollOf(act).scrollTo(0, (MID_OFFSET * d).toInt())
            }
            assertTrue(pollForWindow(scenario, firstRow = 6, childCount = 17, timeoutMs = 30_000))
            scenario.onActivity { act ->
                val content = contentOf(act)
                val row6 = inputByHint(content, "Row 6")!!
                assertSame("ROW 6 SURVIVED THE SLIDE AS THE SAME NATIVE VIEW — its node ids " +
                    "appeared in no create/remove, so the EditText instance is IDENTICAL " +
                    "(@key = the item)", row6View.get(), row6)
                assertEquals("…and its uncommitted native text rode along",
                    "i travel with my key", row6.text.toString())
                assertEquals("row 0 LEFT the window — it is gone, not hidden",
                    null, inputByHint(content, "Row 0"))
            }

            // …and back to [0, 11): row 6 survives AGAIN; row 0 re-enters FRESH.
            scenario.onActivity { act -> scrollOf(act).scrollTo(0, 0) }
            assertTrue(pollForWindow(scenario, firstRow = 0, childCount = 13, timeoutMs = 30_000))
            scenario.onActivity { act ->
                val content = contentOf(act)
                val row6 = inputByHint(content, "Row 6")!!
                val row0 = inputByHint(content, "Row 0")!!
                assertSame("row 6 was in BOTH windows — same view across the round trip",
                    row6View.get(), row6)
                assertEquals("…text intact", "i travel with my key", row6.text.toString())
                assertNotSame("row 0 was DESTROYED and re-created: a DIFFERENT EditText — " +
                    "this is what proves virtualization actually virtualizes",
                    row0View.get(), row0)
                assertEquals("…and unbound native state did NOT survive eviction (BnList's " +
                    "re-entry rule: bind it if you want it back)", "", row0.text.toString())
            }
        }

    // ── [5] Throughput: the never-queue proof, deterministically ─────────────

    /**
     * **THE CONTRACT'S EVIDENCE ROW.** 100 scroll samples delivered in ONE
     * main-thread message (so no completion can interleave — deterministic):
     * the first sample finds the lane free and dispatches; samples 2..100
     * conflate, each REPLACING the slot; the completion then flushes exactly
     * the freshest. The wire carries a HANDFUL of dispatches (≤ 4 allows a
     * re-clamp echo; the deterministic count is 2) — never 100, and never a
     * queue — while the FINAL offset always arrives, proven both by the
     * counter and by .NET's window landing at [2,17).
     *
     * The required mutations redden HERE (and in the synthetic twin): dispatch
     * per-sample → 100 dispatches trip the ≤ 4 bound; queue-instead-of-replace
     * → the flush drains a backlog → same trip; drop-the-freshest → the window
     * never reaches [2,17) and lastScrollDispatchDp ≠ 400.
     */
    @Test fun a_burst_of_100_samples_conflates_to_a_handful_of_dispatches_and_the_final_offset_arrives() =
        withList { scenario ->
            assertTrue(pollForWindow(scenario, firstRow = 0, childCount = 13))
            val samples0 = AtomicReference(0)
            val dispatches0 = AtomicReference(0)
            scenario.onActivity { act ->
                samples0.set(act.mapper.scrollSamplesSeen)
                dispatches0.set(act.mapper.scrollDispatchesSent)
                val d = act.resources.displayMetrics.density
                val scroll = scrollOf(act)
                // 100 samples, one message: 4dp steps to 400dp — every scrollTo
                // lands on a distinct px offset, so every one fires the listener.
                for (i in 1..100) scroll.scrollTo(0, (i * 4 * d).toInt())
            }

            // 400dp → window [2,17): floor(400/64)=6 −4 → 2; ceil(800/64)=13 +4 → 17.
            assertTrue("the final offset's window [2,17) never arrived",
                pollForWindow(scenario, firstRow = 2, childCount = 17, timeoutMs = 30_000))

            scenario.onActivity { act ->
                val samples = act.mapper.scrollSamplesSeen - samples0.get()
                val dispatches = act.mapper.scrollDispatchesSent - dispatches0.get()
                val ratio = samples.toFloat() / dispatches

                // The conclusion-feed number — grep logcat for [7.2-throughput].
                Log.i(TAG, "[7.2-throughput] burst: samples=$samples dispatches=$dispatches " +
                    "conflation-ratio=${"%.1f".format(ratio)}:1")

                assertEquals("all 100 samples reached the conflation slot", 100, samples)
                assertTrue("THE NEVER-QUEUE BOUND: 100 samples may cost at most a handful of " +
                    "dispatches (1 immediate + 1 per lane-availability; got $dispatches). " +
                    "100 here means the wire dispatches PER SAMPLE; >4 means samples QUEUED " +
                    "instead of replacing", dispatches in 1..4)
                assertTrue("the ratio IS the proof the wire held: $samples samples → " +
                    "$dispatches dispatches (${ratio}:1, demanded ≥ 25:1)", ratio >= 25f)
                assertEquals("…and the FINAL offset always arrives: the last dispatch carried " +
                    "exactly 400dp — conflation drops stale values, never the freshest",
                    400f, act.mapper.lastScrollDispatchDp!!, 0.01f)
            }
        }

    // ── [6] A scripted fling: the 60Hz producer, measured ────────────────────

    /**
     * The design's named risk, exercised for real: a fling produces a scroll
     * sample per animation frame for a second-plus. The wire must (a) never
     * carry more dispatches than samples, (b) settle with the FINAL offset
     * delivered — .NET's window must match wherever the framework's fling
     * physics actually stopped (derived via [window], not transcribed), and
     * (c) report the measured conflation ratio for the conclusion-feed.
     * (The ratio varies with .NET's dispatch latency — a fast lane conflates
     * little, a busy one more; the BURST test above owns the hard bound.)
     */
    @Test fun a_scripted_fling_settles_with_the_final_offset_delivered() =
        withList { scenario ->
            assertTrue(pollForWindow(scenario, firstRow = 0, childCount = 13))
            val samples0 = AtomicReference(0)
            val dispatches0 = AtomicReference(0)
            scenario.onActivity { act ->
                samples0.set(act.mapper.scrollSamplesSeen)
                dispatches0.set(act.mapper.scrollDispatchesSent)
                val d = act.resources.displayMetrics.density
                scrollOf(act).fling((6000 * d).toInt()) // px/s, downward
            }

            // Settle: the offset stable across two reads 400ms apart, the wire
            // QUIESCENT, and the last dispatch already carrying that offset —
            // all three, or the assertions below race the fling's tail.
            data class WireState(val scrollY: Int, val busy: Int, val lastDp: Float?)
            fun read(): WireState {
                val out = AtomicReference<WireState>()
                scenario.onActivity { act ->
                    out.set(WireState(scrollOf(act).scrollY,
                        act.mapper.scrollBusyWireCount, act.mapper.lastScrollDispatchDp))
                }
                return out.get()
            }
            val deadline = System.currentTimeMillis() + 30_000
            var settledPx = -1
            val dHint = AtomicReference(0f)
            scenario.onActivity { act -> dHint.set(act.resources.displayMetrics.density) }
            while (System.currentTimeMillis() < deadline) {
                val a = read()
                Thread.sleep(400)
                val b = read()
                if (a.scrollY > 0 && a.scrollY == b.scrollY &&
                    a.busy == 0 && b.busy == 0 &&
                    b.lastDp == b.scrollY / dHint.get()) {
                    settledPx = b.scrollY; break
                }
            }
            assertTrue("the fling never moved and settled within 30s", settledPx > 0)

            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                val settledDp = settledPx / d
                val (start, end) = window(settledDp)
                val samples = act.mapper.scrollSamplesSeen - samples0.get()
                val dispatches = act.mapper.scrollDispatchesSent - dispatches0.get()

                Log.i(TAG, "[7.2-throughput] fling: settled=${settledDp}dp samples=$samples " +
                    "dispatches=$dispatches " +
                    "conflation-ratio=${"%.2f".format(samples.toFloat() / dispatches)}:1")

                assertTrue("a fling produces a stream of samples (got $samples)", samples > 1)
                assertTrue("the wire NEVER carries more dispatches than samples " +
                    "($dispatches vs $samples) — a dispatch without a sample would be an " +
                    "invented offset", dispatches in 1..samples)
                assertEquals("THE FINAL OFFSET ALWAYS ARRIVES: the last dispatch is the " +
                    "settled offset — conflation may drop any sample except the freshest",
                    settledDp, act.mapper.lastScrollDispatchDp!!, 0.01f)
                assertWindow("where the fling settled ($settledDp dp)", act, start, end)
            }
        }

    private fun screenY(v: android.view.View): Int =
        IntArray(2).also { v.getLocationOnScreen(it) }[1]
}
