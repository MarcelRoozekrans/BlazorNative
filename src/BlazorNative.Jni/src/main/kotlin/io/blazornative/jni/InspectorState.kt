package io.blazornative.jni

import java.util.concurrent.CopyOnWriteArrayList

/**
 * Phase 4.4 Gate 1 — the InspectorHost's shared state: the 4.3 [TreeSnapshot]
 * (tree of record) + a bounded patch ring buffer (seq, frame timestamp, patch
 * summary — last [patchCapacity]) + a bounded event log (frame deliveries,
 * dispatches with rc, onError faults — last [eventCapacity]). Both buffers
 * drop the OLDEST entry at capacity while the shared seq counter keeps
 * increasing across drops, so a reader can always tell how much it missed.
 *
 * SEQ CONTRACT: ONE global monotonic counter spans both buffers — every ring
 * entry and every log entry consumes the next seq, globally ordering patches
 * against log events. [onFrame] consumes one seq per patch plus one for its
 * own "frame applied" log entry.
 *
 * THREADING CONTRACT (the design's risky part, as built):
 *  - Writers: [onFrame]/[onError] arrive on whatever thread is inside the
 *    dll (the mount caller during boot; the HTTP thread serialized by
 *    InspectorServer's dispatchGate during POST /api/dispatch — see the
 *    server's KDoc). [logDispatch] is called by the POST handler AFTER the
 *    blocking dispatch returned.
 *  - Readers: [treeJson]/[patchesJson]/[eventsJson] run on HTTP server
 *    threads; they build their JSON strings entirely UNDER the lock (renders
 *    are quick string building — the design's stated trade).
 *  - ONE coarse [lock] guards snapshot + ring + log + seq. No code path
 *    acquires any other lock while holding it, and listener notification
 *    happens strictly AFTER the lock is released — no nested locking, no
 *    lock-order cycle with InspectorServer's dispatchGate (which is only
 *    ever taken FIRST, never from within state code).
 *  - Listener callbacks may therefore interleave out of seq order under
 *    concurrent writers; harmless — the SSE pull model just triggers
 *    re-fetches, and the fetched JSON is always lock-consistent.
 */
class InspectorState(
    private val patchCapacity: Int = 500,
    private val eventCapacity: Int = 200,
) {
    private class PatchRecord(val seq: Long, val frameId: Int, val timestampMs: Long, val summary: String)
    private class EventRecord(val seq: Long, val timestampMs: Long, val kind: String, val message: String)

    private val lock = Any()
    private val snapshot = TreeSnapshot()
    private val patchRing = ArrayDeque<PatchRecord>()
    private val eventLog = ArrayDeque<EventRecord>()
    private var seq = 0L

    /** SSE fan-out subscribers: (kind = "tree-changed" | "event-logged", seq).
     * COW list — registration is rare, iteration is per-write. Callbacks run
     * OUTSIDE the state lock and must not block (InspectorServer's fan-out is
     * a non-blocking queue offer). */
    private val listeners = CopyOnWriteArrayList<(kind: String, seq: Long) -> Unit>()

    fun addListener(listener: (kind: String, seq: Long) -> Unit) {
        listeners.add(listener)
    }

    // ── Writers ──────────────────────────────────────────────────────────────

    /** Frame callback sink (wire as [BlazorNativeRuntime]'s onFrame): applies
     * the frame to the tree of record, rings every patch, logs the delivery.
     * Notifies "tree-changed" once per frame (not per patch — the pull model
     * re-fetches everything anyway). */
    fun onFrame(frame: RenderFrame) {
        val lastSeq = synchronized(lock) {
            snapshot.apply(frame)
            for (patch in frame.patches) {
                patchRing.addLast(PatchRecord(++seq, frame.frameId, frame.timestampMs, summarize(patch)))
                if (patchRing.size > patchCapacity) patchRing.removeFirst()
            }
            pushEvent("frame", "frame ${frame.frameId} applied (${frame.patches.size} patches)")
            seq
        }
        notifyListeners("tree-changed", lastSeq)
    }

    /** Dispatch outcome sink — the POST handler calls this AFTER the blocking
     * dispatch returned (never while dispatching; see the server's KDoc). */
    fun logDispatch(handlerId: Int, eventName: String, payload: String?, rc: Int) {
        val detail = buildString {
            append("dispatch handlerId=").append(handlerId)
            append(" event='").append(eventName).append('\'')
            if (payload != null) append(" payload='").append(payload).append('\'')
            append(" rc=").append(rc)
        }
        val s = synchronized(lock) {
            pushEvent("dispatch", detail)
            seq
        }
        notifyListeners("event-logged", s)
    }

    /** onError fault sink (wire as [BlazorNativeRuntime]'s onError). */
    fun logError(message: String, t: Throwable) {
        val s = synchronized(lock) {
            pushEvent("error", "$message: $t")
            seq
        }
        notifyListeners("event-logged", s)
    }

    /** Must be called under [lock]. */
    private fun pushEvent(kind: String, message: String) {
        eventLog.addLast(EventRecord(++seq, System.currentTimeMillis(), kind, message))
        if (eventLog.size > eventCapacity) eventLog.removeFirst()
    }

    private fun notifyListeners(kind: String, seq: Long) {
        for (listener in listeners) listener(kind, seq)
    }

    // ── Readers (JSON renders — built under the lock) ────────────────────────

    /** `{"seq":…,"framesApplied":…,"roots":[…]}` — GET /api/tree. */
    fun treeJson(): String = synchronized(lock) {
        "{\"seq\":$seq,\"framesApplied\":${snapshot.framesApplied},\"roots\":${snapshot.renderJson()}}"
    }

    /** `{"patches":[{"seq":…,"frameId":…,"timestampMs":…,"summary":…}]}` —
     * GET /api/patches?since=; entries with seq STRICTLY greater. */
    fun patchesJson(since: Long = 0L): String = synchronized(lock) {
        val sb = StringBuilder(256)
        sb.append("{\"patches\":[")
        var first = true
        for (r in patchRing) {
            if (r.seq <= since) continue
            if (!first) sb.append(',')
            first = false
            sb.append("{\"seq\":").append(r.seq)
                .append(",\"frameId\":").append(r.frameId)
                .append(",\"timestampMs\":").append(r.timestampMs)
                .append(",\"summary\":")
            InspectorJson.appendString(sb, r.summary)
            sb.append('}')
        }
        sb.append("]}")
        sb.toString()
    }

    /** `{"events":[{"seq":…,"timestampMs":…,"kind":…,"message":…}]}` —
     * GET /api/events. */
    fun eventsJson(): String = synchronized(lock) {
        val sb = StringBuilder(256)
        sb.append("{\"events\":[")
        var first = true
        for (e in eventLog) {
            if (!first) sb.append(',')
            first = false
            sb.append("{\"seq\":").append(e.seq)
                .append(",\"timestampMs\":").append(e.timestampMs)
                .append(",\"kind\":")
            InspectorJson.appendString(sb, e.kind)
            sb.append(",\"message\":")
            InspectorJson.appendString(sb, e.message)
            sb.append('}')
        }
        sb.append("]}")
        sb.toString()
    }

    /** One compact human-readable line per patch — the ring's summary field. */
    private fun summarize(p: RenderPatch): String = when (p) {
        is RenderPatch.CreateNode ->
            "CreateNode #${p.nodeId} type=${p.nodeType}" +
                (p.parentId?.let { " parent=$it" } ?: "") + " at=${p.insertIndex}"
        is RenderPatch.ReplaceText -> "ReplaceText #${p.nodeId} \"${p.text}\""
        is RenderPatch.RemoveNode -> "RemoveNode #${p.nodeId}"
        is RenderPatch.UpdateProp -> "UpdateProp #${p.nodeId} ${p.name}=${p.value}"
        is RenderPatch.SetStyle -> "SetStyle #${p.nodeId} ${p.property}=${p.value}"
        is RenderPatch.AttachEvent -> "AttachEvent #${p.nodeId} ${p.eventName}=#${p.handlerId}"
        is RenderPatch.DetachEvent -> "DetachEvent #${p.nodeId} ${p.eventName} (was #${p.handlerId})"
        is RenderPatch.CommitFrame -> "CommitFrame frame=${p.frameId}"
    }
}
