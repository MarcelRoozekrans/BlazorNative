package io.blazornative.shell

import android.widget.ScrollView
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 7.2 Gate 2 Task 2.1 — **THE CONFLATION, AT THE MECHANISM** (the wire
 * contract, docs/plans/2026-07-15-phase-7.2-design.md §"The wire contract" —
 * NORMATIVE; iOS mirrors these exact behaviours in Gate 3, over
 * `UIScrollView`'s delegate instead of `setOnScrollChangeListener`).
 *
 * The contract's rows, each a test:
 *
 *  - **Event**: a scroll node with the `scroll` event attached dispatches its
 *    content offset — in **dp** (px ÷ density at the source, the 6.1 rule), as
 *    the invariant float payload `NativeRenderer.ParseScrollOffset` parses.
 *  - **Conflation / Backpressure**: ONE pending offset per node; a new sample
 *    REPLACES it; at most one dispatch in flight per node — a busy lane means
 *    FEWER, FRESHER events, **never a queue**. The middle value of a burst is
 *    NEVER dispatched; the freshest always is. (This is the test the required
 *    mutations must redden: queue-instead-of-replace, dispatch-per-sample.)
 *  - **Batch guard**: a sample arriving DURING a patch batch (Android's
 *    ScrollView re-clamps the offset from its own onLayout inside the batch's
 *    layout pass — the 6.2 mechanism, ANDROID-SPECIFIC) conflates and is
 *    flushed ONCE at the batch end.
 *  - **Detach / purge**: the 6.3 stale-callback discipline — a detached or
 *    removed node's pending offset is DROPPED, never dispatched, and a late
 *    completion is a no-op.
 *
 * The dispatcher here is a [RecordingLane]: it records every dispatch and can
 * WITHHOLD the completion signal, which is how a test makes the lane "busy"
 * deterministically — the same seam `MainActivity` wires to
 * `BlazorNativeRuntime.dispatchEvent(h, "scroll", payload, onComplete)`.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperOnScrollTest {

    private companion object {
        const val SCROLL_ID = 1
        const val HANDLER = 77
        const val ROWS = 10
        const val ROW_H = 80
        const val VIEW_W = 300
        const val VIEW_H = 200
    }

    /**
     * The scroll lane, scripted: records (handlerId, payload) per dispatch and —
     * when [withhold] — parks the completion for the test to release, which is a
     * deterministic "the lane is busy" (a real lane is busy for exactly the
     * duration between submit and the completion callback).
     */
    private class RecordingLane(var withhold: Boolean = false) {
        val dispatches = mutableListOf<Pair<Int, String>>()
        val parked = ArrayDeque<() -> Unit>()
        val dispatcher: (Int, String, () -> Unit) -> Unit = { handlerId, payload, done ->
            dispatches += handlerId to payload
            if (withhold) parked.addLast(done) else done()
        }
    }

    private val instr = InstrumentationRegistry.getInstrumentation()

    /** BnScrollDemo's shape (300×200 over ten 80-high rows, range 600) with the
     * `scroll` event ATTACHED — the one thing 6.2's trees never carried. */
    private fun wiredScrollTree(
        handlerId: Int = HANDLER,
        wrapperId: Int? = null,
    ): List<RenderPatch> = buildList {
        var parent: Int? = null
        if (wrapperId != null) {
            add(create(wrapperId, "view", null))
            parent = wrapperId
        }
        add(create(SCROLL_ID, "scroll", parent))
        add(style(SCROLL_ID, "width", VIEW_W.toString()))
        add(style(SCROLL_ID, "height", VIEW_H.toString()))
        for (i in 0 until ROWS) {
            add(create(10 + i, "view", SCROLL_ID))
            add(style(10 + i, "height", ROW_H.toString()))
        }
        add(RenderPatch.AttachEvent(SCROLL_ID, "scroll", handlerId))
    }

    private fun scrollOf(host: SyntheticHost): ScrollView = host.read {
        val first = host.root.getChildAt(0)
        if (first is ScrollView) first
        else (first as android.view.ViewGroup).getChildAt(0) as ScrollView
    }

    /** Drives the REAL listener: a main-thread scrollTo, exactly what a drag,
     * a fling tick and a programmatic re-clamp all reduce to. */
    private fun sample(host: SyntheticHost, scroll: ScrollView, px: Int) {
        instr.runOnMainSync { scroll.scrollTo(0, px) }
        instr.waitForIdleSync() // lets a posted completion→flush chain settle
    }

    private fun releaseOneCompletion(lane: RecordingLane) {
        instr.runOnMainSync { lane.parked.removeFirst().invoke() }
        instr.waitForIdleSync()
    }

    // ── Event: the offset crosses in dp, on the attached handler ─────────────

    @Test fun a_scroll_sample_dispatches_the_offset_in_dp_on_the_attached_handler() {
        val lane = RecordingLane()
        val host = SyntheticHost(onScrollEvent = lane.dispatcher)
        host.render(wiredScrollTree())
        val scroll = scrollOf(host)

        val d = density()
        val px = (150 * d).toInt()
        sample(host, scroll, px)

        assertEquals("ONE sample, ONE dispatch — the lane was free", 1, lane.dispatches.size)
        val (handlerId, payload) = lane.dispatches.single()
        assertEquals("…on the handlerId the AttachEvent carried", HANDLER, handlerId)
        assertEquals("THE UNIT RULE (6.1, one conversion site): the payload is px ÷ density " +
            "— dp, the number Yoga speaks and iOS asserts as pt. An un-divided px payload " +
            "would scroll the .NET window 2.625× too far on this device",
            px / d, payload.toFloat(), 0f)
        assertEquals("…and it is the exact invariant string ParseScrollOffset parses " +
            "(Float.toString never localizes — a \"1,5\" would be a loud rc-2 fault)",
            (px / d).toString(), payload)
        host.read {
            assertEquals("counters: 1 seen", 1, host.mapper.scrollSamplesSeen)
            assertEquals("counters: 1 sent", 1, host.mapper.scrollDispatchesSent)
        }
    }

    // ── Conflation: THE RULE — replace, never queue; freshest wins ────────────

    /**
     * **THE PHASE'S ROW, AT THE MECHANISM** — and the test the required
     * mutations must redden:
     *
     *  - *queue instead of replace* (a list in the slot) → the middle offset
     *    gets dispatched → the `[first, freshest]` assertion fails;
     *  - *dispatch per sample* (ignore `inFlight`) → 3 dispatches for 3
     *    samples while the lane is busy → the "exactly 1 while busy" and the
     *    "2 total" assertions fail.
     *
     * A busy lane sees FEWER, FRESHER events: 3 samples → 2 dispatches, and
     * the value that was superseded while the lane was busy is NEVER on the
     * wire. That non-event is the whole difference between idempotent state
     * and an event log.
     */
    @Test fun a_busy_lane_conflates_LATEST_WINS_the_superseded_offset_is_never_dispatched() {
        val lane = RecordingLane(withhold = true)
        val host = SyntheticHost(onScrollEvent = lane.dispatcher)
        host.render(wiredScrollTree())
        val scroll = scrollOf(host)
        val d = density()

        val pxA = (100 * d).toInt()
        val pxB = (200 * d).toInt()
        val pxC = (300 * d).toInt()

        sample(host, scroll, pxA) // lane free → dispatches, completion PARKED
        sample(host, scroll, pxB) // lane busy → conflates
        sample(host, scroll, pxC) // lane busy → REPLACES B in the slot

        assertEquals("while the dispatch is in flight, NOTHING else is submitted — " +
            "at most one per node on the lane, ever (the never-queue proof)",
            1, lane.dispatches.size)
        host.read {
            assertEquals("…the slot holds ONE offset, the freshest: C replaced B",
                pxC / d, host.mapper.scrollPendingOffsetDp(SCROLL_ID)!!, 0f)
            assertEquals("…and all three samples were SEEN — they conflated, they were not lost " +
                "at the listener", 3, host.mapper.scrollSamplesSeen)
        }

        releaseOneCompletion(lane) // the lane frees → the freshest value goes out

        assertEquals("the completion flushed exactly the freshest value: 3 samples, 2 dispatches",
            listOf(HANDLER to (pxA / d).toString(), HANDLER to (pxC / d).toString()),
            lane.dispatches)
        assertTrue("offset B (${pxB / d}dp) is NEVER on the wire — it was idempotent state " +
            "that a fresher sample superseded, not an event to be queued",
            lane.dispatches.none { it.second == (pxB / d).toString() })

        releaseOneCompletion(lane) // C's completion: the slot is empty
        assertEquals("an empty slot dispatches nothing", 2, lane.dispatches.size)
        host.read { assertEquals("quiescent", 0, host.mapper.scrollBusyWireCount) }
    }

    // ── Batch guard: a mid-batch sample conflates and flushes ONCE at batch end ──

    /**
     * The sample here is REAL and Android-specific: growing the viewport
     * (SetStyle height 200 → 700) makes the current offset (600) exceed the new
     * range (800 − 700 = 100), and `ScrollView.onLayout` ends with a `scrollTo`
     * that re-clamps — INSIDE `calculateAndApply`, inside the batch (the 6.2
     * conclusion's mechanism #2; iOS has no such re-clamp and must NOT go
     * looking for one — its shrink clamp is explicit shell code). The wire
     * contract's answer: the sample CONFLATES (a batch is a busy lane) and the
     * batch end is a lane-availability — ONE dispatch, the clamped offset,
     * after the commit. Deleting the flush leaves the re-clamped offset
     * stranded in the slot forever and .NET's window desynchronized from the
     * glass — that is the mutation this test reddens on.
     */
    @Test fun a_mid_batch_reclamp_sample_conflates_and_flushes_once_at_batch_end() {
        val lane = RecordingLane()
        val host = SyntheticHost(onScrollEvent = lane.dispatcher)
        host.render(wiredScrollTree())
        val scroll = scrollOf(host)
        val d = density()

        sample(host, scroll, (600 * d).toInt()) // to the bottom (range 600)
        assertEquals(1, lane.dispatches.size)

        // The batch: the viewport grows past what the offset allows. The
        // re-clamp fires the scroll listener DURING applyBatch's layout pass.
        host.render(listOf(style(SCROLL_ID, "height", "700")))

        assertEquals("the mid-batch sample conflated and the batch end flushed it: " +
            "exactly ONE more dispatch (at most one per committed frame)",
            2, lane.dispatches.size)
        val clampedPx = host.read { scroll.scrollY }
        assertEquals("…carrying the offset the framework re-clamped to — the content (800) " +
            "minus the new viewport (700), in the framework's own pixels, ÷ density",
            (clampedPx / d).toString(), lane.dispatches.last().second)
        assertTrue("sanity: the re-clamp really happened (600dp is no longer reachable)",
            clampedPx < (600 * d).toInt())
        host.read { assertEquals("…and nothing is left stranded in the slot",
            0, host.mapper.scrollBusyWireCount) }
    }

    // ── Detach / purge: the 6.3 stale-callback discipline ────────────────────

    @Test fun detach_drops_the_pending_offset_it_is_NEVER_dispatched() {
        val lane = RecordingLane(withhold = true)
        val host = SyntheticHost(onScrollEvent = lane.dispatcher)
        host.render(wiredScrollTree())
        val scroll = scrollOf(host)
        val d = density()

        sample(host, scroll, (100 * d).toInt()) // in flight, completion parked
        sample(host, scroll, (250 * d).toInt()) // conflated into the slot

        host.render(listOf(RenderPatch.DetachEvent(SCROLL_ID, HANDLER, "scroll")))
        host.read {
            assertEquals("the wire died with the detach", 0, host.mapper.scrollWireCount)
            assertNull("…taking its pending offset with it",
                host.mapper.scrollPendingOffsetDp(SCROLL_ID))
        }

        releaseOneCompletion(lane) // the in-flight dispatch's completion lands LATE

        assertEquals("the detached node's pending offset was DROPPED, never dispatched — " +
            "a completion into a dead wire is a no-op by construction (the 6.3 discipline)",
            1, lane.dispatches.size)

        // And a sample AFTER the detach reaches nothing: the listener slot was
        // cleared, the wire is gone — no dispatch, no crash, not even a count.
        val seenBefore = host.read { host.mapper.scrollSamplesSeen }
        sample(host, scroll, (400 * d).toInt())
        assertEquals("a post-detach scroll is the node's own business — nothing dispatched",
            1, lane.dispatches.size)
        host.read { assertEquals("…and no sample counted against a dead wire",
            seenBefore, host.mapper.scrollSamplesSeen) }
    }

    @Test fun removing_the_scroll_nodes_SUBTREE_purges_the_wire_pending_and_all() {
        val lane = RecordingLane(withhold = true)
        val host = SyntheticHost(onScrollEvent = lane.dispatcher)
        // The scroll under a WRAPPER — the shape navigation actually emits (the
        // 6.2 lesson: the RemoveNode names the page's column, never the scroll).
        host.render(wiredScrollTree(wrapperId = 100))
        val scroll = scrollOf(host)
        val d = density()

        sample(host, scroll, (100 * d).toInt()) // in flight
        sample(host, scroll, (250 * d).toInt()) // pending

        host.render(listOf(RenderPatch.RemoveNode(100)))
        host.read {
            assertEquals("ONE RemoveNodePatch stands for the subtree — the wire is part of it",
                0, host.mapper.scrollWireCount)
        }

        releaseOneCompletion(lane)
        assertEquals("the purged node's pending offset died unsent — a dispatch after the " +
            "purge would enter a handler whose node no longer exists",
            1, lane.dispatches.size)
        host.read { assertEquals(0, host.mapper.scrollBusyWireCount) }
    }

    // ── Re-attach: last-wins on the LIVE wire, the 4.2 watcher discipline ─────

    @Test fun a_reattach_swaps_the_handler_on_the_live_wire_last_wins_no_stacking() {
        val lane = RecordingLane()
        val host = SyntheticHost(onScrollEvent = lane.dispatcher)
        host.render(wiredScrollTree(handlerId = HANDLER))
        val scroll = scrollOf(host)
        val d = density()

        sample(host, scroll, (100 * d).toInt())
        assertEquals(HANDLER, lane.dispatches.last().first)

        // Same node, NEW handlerId, NO preceding detach — Blazor's last-wins
        // re-attach (a re-rendered OnScroll delegate).
        host.render(listOf(RenderPatch.AttachEvent(SCROLL_ID, "scroll", 99)))
        sample(host, scroll, (200 * d).toInt())

        assertEquals("the new sample dispatches on the NEW handler", 99, lane.dispatches.last().first)
        assertEquals("…and each sample dispatched exactly ONCE — the wire was REUSED, not " +
            "stacked (the 4.2 stale-watcher lesson, applied to scroll)",
            2, lane.dispatches.size)
        host.read { assertEquals("one node, one wire", 1, host.mapper.scrollWireCount) }
    }

    // ── And the widget guard: scroll on a non-viewport is ignored, loudly ─────

    @Test fun attaching_scroll_to_a_non_scroll_node_is_ignored_and_dispatches_nothing() {
        val lane = RecordingLane()
        val host = SyntheticHost(onScrollEvent = lane.dispatcher)
        host.render(listOf(
            create(1, "view", null),
            style(1, "height", "200"),
            RenderPatch.AttachEvent(1, "scroll", HANDLER),
        ))
        host.read {
            assertEquals("no wire for a node that cannot scroll", 0, host.mapper.scrollWireCount)
        }
        // …and scrolling the (non-Scroll) view programmatically reaches nothing.
        instr.runOnMainSync { host.root.getChildAt(0).scrollTo(0, 50) }
        instr.waitForIdleSync()
        assertEquals(0, lane.dispatches.size)
        host.read {
            assertSame("sanity: the node exists and simply is not a viewport",
                BnYogaFrameLayout::class.java, host.root.getChildAt(0)::class.java)
        }
    }
}
