package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 4.4 Gate 1 — InspectorState's bounded buffers, driven by synthetic
 * frames (no JNA, no dll): the patch ring buffer (cap ~500 in production,
 * tiny here) and the event log (cap ~200) both drop the OLDEST entry at
 * capacity while the shared seq counter keeps increasing across drops — a
 * reader can always tell "how much did I miss" from the gap.
 *
 * Seq contract (pinned here): ONE global monotonic counter spans both
 * buffers — every ring entry and every event-log entry consumes the next
 * seq, so patch records and log records are globally ordered. onFrame
 * consumes one seq per patch (ring entries) plus one for its own
 * "frame applied" event-log entry.
 */
class InspectorStateTest {

    private var nextFrameId = 0

    private fun frame(vararg patches: RenderPatch): RenderFrame {
        val id = ++nextFrameId
        return RenderFrame(frameId = id, timestampMs = id * 100L, patches = patches.toList())
    }

    private fun create(id: Int, type: String, parent: Int? = null) =
        RenderPatch.CreateNode(nodeId = id, nodeType = type, parentId = parent)

    private fun seqsIn(json: String): List<Long> =
        Regex("\"seq\":(\\d+)").findAll(json).map { it.groupValues[1].toLong() }.toList()

    // ── Patch ring buffer ────────────────────────────────────────────────────

    @Test
    fun patch_ring_keeps_all_entries_below_capacity() {
        val state = InspectorState(patchCapacity = 10, eventCapacity = 10)
        state.onFrame(frame(create(1, "view"), create(2, "text", parent = 1)))
        assertEquals(listOf(1L, 2L), seqsIn(state.patchesJson()))
    }

    @Test
    fun patch_ring_drops_oldest_at_capacity_and_seq_stays_monotonic() {
        val state = InspectorState(patchCapacity = 3, eventCapacity = 10)
        // 5 patches → seqs 1..5; ring cap 3 keeps 3,4,5.
        state.onFrame(
            frame(
                create(1, "view"),
                create(2, "text", parent = 1),
                create(3, "text", parent = 1),
                create(4, "text", parent = 1),
                create(5, "text", parent = 1),
            )
        )
        assertEquals(listOf(3L, 4L, 5L), seqsIn(state.patchesJson()))

        // A later frame keeps counting where the last left off (6 was the
        // frame-applied log entry): drops never reset or reuse seqs.
        state.onFrame(frame(RenderPatch.ReplaceText(2, "x")))
        assertEquals(listOf(4L, 5L, 7L), seqsIn(state.patchesJson()))
    }

    @Test
    fun patches_since_filters_strictly_greater() {
        val state = InspectorState(patchCapacity = 10, eventCapacity = 10)
        state.onFrame(frame(create(1, "view"), create(2, "text", parent = 1), create(3, "text", parent = 1)))
        assertEquals(listOf(3L), seqsIn(state.patchesJson(since = 2)))
        assertEquals(emptyList<Long>(), seqsIn(state.patchesJson(since = 99)))
    }

    @Test
    fun patch_records_carry_frame_id_timestamp_and_summary() {
        val state = InspectorState(patchCapacity = 10, eventCapacity = 10)
        state.onFrame(frame(create(7, "button", parent = 1)))
        val json = state.patchesJson()
        assertTrue(json.contains("\"frameId\":1"), "ring entry must carry the frame id; got $json")
        assertTrue(json.contains("\"timestampMs\":100"), "ring entry must carry the frame timestamp; got $json")
        assertTrue(json.contains("CreateNode #7"), "ring entry must carry a patch summary; got $json")
    }

    // ── Event log ────────────────────────────────────────────────────────────

    @Test
    fun event_log_records_frame_deliveries_dispatches_and_errors() {
        val state = InspectorState(patchCapacity = 10, eventCapacity = 10)
        state.onFrame(frame(create(1, "view")))
        state.logDispatch(handlerId = 12, eventName = "click", payload = null, rc = 0)
        state.logDispatch(handlerId = 11, eventName = "change", payload = "typed", rc = 0)
        state.logError("frame dropped (adapter/consumer threw)", IllegalStateException("boom"))
        val json = state.eventsJson()
        assertTrue(json.contains("\"kind\":\"frame\""), "frame delivery must be logged; got $json")
        assertTrue(json.contains("\"kind\":\"dispatch\""), "dispatch must be logged; got $json")
        assertTrue(json.contains("handlerId=12 event='click' rc=0"), "dispatch detail missing; got $json")
        assertTrue(json.contains("payload='typed'"), "change payload missing; got $json")
        assertTrue(json.contains("\"kind\":\"error\""), "onError fault must be logged; got $json")
        assertTrue(json.contains("boom"), "error detail missing; got $json")
    }

    @Test
    fun event_log_drops_oldest_at_capacity_and_seq_stays_monotonic() {
        val state = InspectorState(patchCapacity = 10, eventCapacity = 2)
        state.logDispatch(1, "click", null, 0)  // seq 1
        state.logDispatch(2, "click", null, 0)  // seq 2
        state.logDispatch(3, "click", null, 0)  // seq 3 → drops seq 1
        assertEquals(listOf(2L, 3L), seqsIn(state.eventsJson()))
        assertFalse(state.eventsJson().contains("handlerId=1 "), "oldest event must be dropped")
    }

    // ── Tree render + listeners ──────────────────────────────────────────────

    @Test
    fun tree_json_wraps_roots_with_seq_and_frames_applied() {
        val state = InspectorState(patchCapacity = 10, eventCapacity = 10)
        state.onFrame(frame(create(1, "view")))
        // 1 patch (seq 1) + the frame-applied log entry (seq 2).
        assertEquals(
            "{\"seq\":2,\"framesApplied\":1,\"roots\":[{\"id\":1,\"type\":\"view\"}]}",
            state.treeJson()
        )
    }

    @Test
    fun listeners_hear_tree_changed_on_frames_and_event_logged_on_log_writes() {
        val state = InspectorState(patchCapacity = 10, eventCapacity = 10)
        val heard = mutableListOf<Pair<String, Long>>()
        state.addListener { kind, seq -> heard.add(kind to seq) }

        state.onFrame(frame(create(1, "view")))               // seqs 1 (patch) + 2 (log)
        state.logDispatch(5, "click", null, 0)                // seq 3
        state.logError("fault", IllegalStateException("x"))   // seq 4

        assertEquals(
            listOf("tree-changed" to 2L, "event-logged" to 3L, "event-logged" to 4L),
            heard
        )
    }
}
