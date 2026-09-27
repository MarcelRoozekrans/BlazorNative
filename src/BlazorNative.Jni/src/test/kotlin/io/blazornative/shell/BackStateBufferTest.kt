package io.blazornative.shell

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 16.2 (#346, spec decision 2) — the pushed back state is HELD until a batch carries it.
 *
 * .NET sends BackState for a navigation BEFORE the frames that show the new page. If the shell
 * applied it on arrival, back would be enabled while the OLD page is still on screen, and a
 * press in that window would act on a page the user cannot see. [BackStateBuffer] therefore only
 * holds a value; the frame producer takes it when it cuts the next batch, and main applies it
 * inside that batch's runnable, the same looper message that shows the page. WidgetMapper is the
 * only production caller: offer from the notice, take at CommitFrame, apply at the end of
 * applyBatch.
 *
 * DOES NOT COVER: WidgetMapper's wiring of the three calls, or MainActivity's callback, which
 * need Android and are pinned by the instrumented BackAndroidTest,
 * navigate_then_back_at_once_neither_exits_nor_swallows most directly; nor the .NET half of the
 * order, that the notice precedes the frame, which BackStateNoticeTests.cs pins.
 */
class BackStateBufferTest {

    @Test
    fun an_offered_value_is_not_applied_until_a_batch_carries_it() {
        val buffer = BackStateBuffer()
        assertFalse(buffer.canGoBack, "a new shell starts with back disabled")

        buffer.offer(true)
        // THE PIN: holding, not applying. A buffer that applied on arrival reds here.
        assertFalse(buffer.canGoBack,
            "the offered value was applied before any batch carried it: back would be enabled " +
                "while the previous page is still on screen")

        val carried = buffer.takeForBatch()
        assertEquals(true, carried, "the next batch must carry the offered value")
        assertFalse(buffer.canGoBack, "taking the value for a batch must not apply it either")

        assertTrue(buffer.applyFromBatch(carried), "applying a new value reports a change")
        assertTrue(buffer.canGoBack, "the batch applied the value")
    }

    @Test
    fun a_value_is_carried_by_exactly_one_batch() {
        val buffer = BackStateBuffer()
        buffer.offer(true)
        assertEquals(true, buffer.takeForBatch())
        // Positive control for the null below: the buffer DID hold a value a moment ago.
        assertNull(buffer.takeForBatch(), "a value must be taken once; the next batch carries nothing")
    }

    @Test
    fun a_batch_that_carries_nothing_leaves_the_applied_value_alone() {
        val buffer = BackStateBuffer()
        buffer.offer(true)
        buffer.applyFromBatch(buffer.takeForBatch())
        assertTrue(buffer.canGoBack)

        assertFalse(buffer.applyFromBatch(buffer.takeForBatch()), "an empty batch changed nothing")
        assertTrue(buffer.canGoBack, "a batch with no BackState must not reset the applied value")
    }

    @Test
    fun the_latest_offer_before_a_batch_wins() {
        val buffer = BackStateBuffer()
        // A navigation, then a failed swap's resend of the old value, before any frame.
        buffer.offer(true)
        buffer.offer(false)
        assertEquals(false, buffer.takeForBatch(), "the later notice supersedes the earlier one")
    }

    @Test
    fun applying_the_value_already_applied_reports_no_change() {
        val buffer = BackStateBuffer()
        buffer.offer(false)
        assertFalse(buffer.applyFromBatch(buffer.takeForBatch()),
            "false over a new shell's false is not a change, so the callback is not touched")
        assertFalse(buffer.canGoBack)
    }
}
