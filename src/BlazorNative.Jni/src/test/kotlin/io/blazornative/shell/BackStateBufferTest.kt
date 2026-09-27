package io.blazornative.shell

import io.blazornative.jni.RenderPatch
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 16.2 (#346, spec decision 2) — the pushed back state is HELD until the batch that
 * shows a page carries it; and a press handed to the platform puts the back callback back.
 *
 * .NET sends BackState for a navigation BEFORE the frames that show the new page, and a swap is
 * two frames: the old root's removal, then the new page. If the shell applied the value on
 * arrival, or with the removal batch, back would change while the old page, or a blank screen,
 * is what the user sees. [BackStateBuffer] therefore only holds a value, and only a batch that
 * creates a parentless node, a page's root, carries it. WidgetMapper is the only production
 * caller: offer from the notice, take at CommitFrame, apply at the end of applyBatch.
 *
 * DOES NOT COVER: WidgetMapper's wiring of the three calls or that the value is applied in the
 * SAME main-thread runnable as the page, which need Android and are pinned by the instrumented
 * BackAndroidTest through `inBatchRunnableForTest`; the real dll's frame shapes, which
 * BackNoticeTest feeds through this buffer; nor the .NET half of the order, that the notice
 * precedes the frame, which BackStateNoticeTests.cs pins.
 */
class BackStateBufferTest {

    private val page = listOf(
        RenderPatch.CreateNode(nodeId = 1, nodeType = "div", parentId = null),
        RenderPatch.CreateNode(nodeId = 2, nodeType = "text", parentId = 1),
        RenderPatch.CommitFrame(frameId = 1, timestampMs = 0),
    )
    private val removal = listOf(RenderPatch.RemoveNode(7), RenderPatch.CommitFrame(frameId = 1, timestampMs = 0))
    private val rerender = listOf(
        RenderPatch.CreateNode(nodeId = 3, nodeType = "text", parentId = 1),
        RenderPatch.ReplaceText(3, "x"),
        RenderPatch.CommitFrame(frameId = 1, timestampMs = 0),
    )

    @Test
    fun an_offered_value_is_not_applied_until_a_page_carries_it() {
        val buffer = BackStateBuffer()
        assertFalse(buffer.canGoBack, "a new shell starts with back disabled")

        buffer.offer(true)
        // THE PIN: holding, not applying. A buffer that applied on arrival reds here.
        assertFalse(buffer.canGoBack,
            "the offered value was applied before any batch carried it: back would be enabled " +
                "while the previous page is still on screen")

        val carried = buffer.takeForBatch(page)
        assertEquals(true, carried, "the batch that shows the page must carry the offered value")
        assertFalse(buffer.canGoBack, "taking the value for a batch must not apply it either")

        assertTrue(buffer.applyFromBatch(carried), "applying a new value reports a change")
        assertTrue(buffer.canGoBack, "the batch applied the value")
    }

    @Test
    fun a_swaps_removal_batch_does_not_carry_the_value_and_its_page_does() {
        val buffer = BackStateBuffer()
        buffer.offer(true)
        assertNull(buffer.takeForBatch(removal),
            "the removal batch carried the value: back would change on a blank screen, one " +
                "runnable before the page it describes")
        assertNull(buffer.takeForBatch(rerender), "a re-render that mounts no page carried the value")
        // Positive control for the nulls above: the value was still held for the page.
        assertEquals(true, buffer.takeForBatch(page), "the page's batch must still carry it")
    }

    @Test
    fun a_value_is_carried_by_exactly_one_page() {
        val buffer = BackStateBuffer()
        buffer.offer(true)
        assertEquals(true, buffer.takeForBatch(page))
        assertNull(buffer.takeForBatch(page), "a value must be taken once; the next page carries nothing")
    }

    @Test
    fun a_batch_that_carries_nothing_leaves_the_applied_value_alone() {
        val buffer = BackStateBuffer()
        buffer.offer(true)
        buffer.applyFromBatch(buffer.takeForBatch(page))
        assertTrue(buffer.canGoBack)

        assertFalse(buffer.applyFromBatch(buffer.takeForBatch(page)), "an empty batch changed nothing")
        assertTrue(buffer.canGoBack, "a batch with no BackState must not reset the applied value")
    }

    @Test
    fun the_latest_offer_before_a_page_wins() {
        val buffer = BackStateBuffer()
        // A navigation, then a failed swap's resend of the old value, before any page.
        buffer.offer(true)
        buffer.offer(false)
        assertEquals(false, buffer.takeForBatch(page), "the later notice supersedes the earlier one")
    }

    @Test
    fun applying_the_value_already_applied_reports_no_change() {
        val buffer = BackStateBuffer()
        buffer.offer(false)
        assertFalse(buffer.applyFromBatch(buffer.takeForBatch(page)),
            "false over a new shell's false is not a change, so the callback is not touched")
        assertFalse(buffer.canGoBack)
    }

    // ── handBackToPlatform: the callback never drifts from the computed state ──

    @Test
    fun a_press_handed_to_the_platform_runs_disabled_and_the_callback_is_restored() {
        val states = mutableListOf<Boolean>()
        var enabledDuringDispatch: Boolean? = null
        // The mapper computes `true` afterwards: a page with history is on screen.
        handBackToPlatform(
            setEnabled = { states.add(it) },
            enabledNow = { true },
            dispatch = { enabledDuringDispatch = states.lastOrNull() },
        )
        assertEquals(false, enabledDuringDispatch,
            "the callback must be disabled while the press is re-dispatched, or it re-enters itself")
        // THE PIN: restored to the computed value. Left disabled, the mapper, which publishes
        // only on a change, would never re-enable it, and back would exit from a sub-page.
        assertEquals(listOf(false, true), states, "the callback was not restored to the computed state")
    }

    @Test
    fun the_callback_is_restored_even_when_the_dispatch_throws() {
        val states = mutableListOf<Boolean>()
        assertThrows(IllegalStateException::class.java) {
            handBackToPlatform(
                setEnabled = { states.add(it) },
                enabledNow = { true },
                dispatch = { throw IllegalStateException("dispatch failed") },
            )
        }
        assertEquals(listOf(false, true), states)
    }

    @Test
    fun the_restore_reads_the_state_after_the_dispatch() {
        // The negative: when the computed state is false, the callback stays off.
        var computed = true
        val states = mutableListOf<Boolean>()
        handBackToPlatform(
            setEnabled = { states.add(it) },
            enabledNow = { computed },
            dispatch = { computed = false },
        )
        assertEquals(listOf(false, false), states, "the restore must read the state AFTER the dispatch")
    }
}
