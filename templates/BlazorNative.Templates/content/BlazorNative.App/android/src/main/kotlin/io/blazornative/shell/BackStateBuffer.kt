package io.blazornative.shell

import io.blazornative.jni.RenderPatch

/**
 * Phase 16.2 (#346, spec decision 2) — **the pushed back state, held until the batch that
 * shows its page.**
 *
 * .NET pushes whether it can go back as a BackState notice, and sends it for a navigation
 * BEFORE the frames that show the new page. Applying it on arrival would enable back while
 * the OLD page is still on screen: a press in that window acts on a page the user cannot see.
 * So the visible screen wins. This class only HOLDS a value; the frame producer hands it to the
 * first batch after it that is NOT REMOVAL-ONLY, and main applies it inside that batch's
 * runnable, the same looper message that puts the new page on screen, so no input event can
 * land between the two.
 *
 * WHY SKIP A REMOVAL-ONLY BATCH. A swap is TWO frames, each with its own CommitFrame: the old
 * root's RemoveNodes, then the new page's mount (measured in fix round 1 of Task 3). Riding the
 * removal would apply the value on a blank screen, one runnable before the page.
 *
 * WHY NOT WAIT FOR A CREATED NODE. Fix round 1 did: it waited for a batch that created a
 * parentless node. A page whose FIRST render creates nothing, an `@if (_loaded)` page, mounts
 * with a batch that is a lone CommitFrame (measured through the dll in fix round 2), so that
 * rule left the value unapplied, and back disabled on a sub-page, until content appeared, if it
 * ever did. The mount's batch always exists, and it is the first one after the removal that is
 * not removal-only, so that is the one that carries the value. [isRemovalOnly] is the test.
 *
 * The three calls and their threads, all made by WidgetMapper:
 *  - [offer]: from the notice, on whichever thread .NET sent it from;
 *  - [takeForBatch]: on the frame producer, when a batch is cut at CommitFrame;
 *  - [applyFromBatch] and [canGoBack]: on main, inside that batch's runnable.
 *
 * .NET sends every BackState ahead of a mount or a swap, with one exception: a swap that throws
 * resends the value its route state still holds. If the swap threw before any frame, the
 * resend supersedes the unapplied value and the next batch carries the value already applied,
 * which changes nothing. If it threw after its removal frame, that frame carried nothing, and
 * the same holds. The case that does apply a value the resend then contradicts is MEASURED
 * (Task 3 re-review): when the swap target's mount fails after rendering, as an async-init page
 * does, the failed mount first emits its first render, with content, and then throws. That frame
 * is not removal-only, so it carries the new value, and the resend waits for the page's next
 * re-render. Harmless: a press in that window reaches .NET, which answers BackUnhandled, and the
 * press goes to the platform default. Not pinned.
 *
 * It lives in `src/main/kotlin`, not `androidMain`, so the JVM suite can test it without a
 * device (BackStateBufferTest, and BackNoticeTest against the dll's real frames), the
 * ImageRequestGuard precedent.
 */
class BackStateBuffer {

    private val lock = Any()

    /** The latest offered value no batch has carried yet, or null. Guarded by [lock]. */
    private var buffered: Boolean? = null

    /** The value the shell acts on. Main thread only. False until a batch applies one, so a
     * new shell starts with back disabled, which is the root's answer. */
    var canGoBack: Boolean = false
        private set

    /** Any thread: .NET's BackState notice. Held, never applied here; a later offer before
     * the next carrying batch supersedes it. */
    fun offer(canGoBack: Boolean) {
        synchronized(lock) { buffered = canGoBack }
    }

    /** The frame producer, as it cuts [batch]: the value that batch must apply, or null. A
     * batch that [isRemovalOnly] carries nothing and leaves the buffer alone; any other batch
     * takes the value, clearing the buffer, so one value rides one batch. */
    fun takeForBatch(batch: List<RenderPatch>): Boolean? {
        if (isRemovalOnly(batch)) return null
        return synchronized(lock) {
            val value = buffered
            buffered = null
            value
        }
    }

    /** Main, inside the batch that [takeForBatch] cut: applies [carried]. Returns true when
     * [canGoBack] changed; null or the same value changes nothing. */
    fun applyFromBatch(carried: Boolean?): Boolean {
        if (carried == null || carried == canGoBack) return false
        canGoBack = carried
        return true
    }

    companion object {
        /** True when [batch] only takes nodes away: at least one RemoveNode, and nothing but
         * RemoveNode, DetachEvent and CommitFrame. A swap's first frame, the old root's
         * removal, is this shape; a mount, even an empty one that is a lone CommitFrame, is
         * not. CAVEAT, measured: a swap FROM a page whose current render is empty disposes it
         * with a lone-CommitFrame frame (`BackState false | CommitFrame only | BnDemo`). That
         * frame is not removal-only, so it carries the value one runnable early. Harmless: the
         * screen is blank both before and after it. */
        fun isRemovalOnly(batch: List<RenderPatch>): Boolean =
            batch.any { it is RenderPatch.RemoveNode } &&
                batch.all { it is RenderPatch.RemoveNode || it is RenderPatch.DetachEvent || it is RenderPatch.CommitFrame }
    }
}

/**
 * Phase 16.2 (#346) — hands one back press to the platform's default, then puts the back
 * callback back to the state the shell computes.
 *
 * Handing a press to the default means disabling the AndroidX callback and re-dispatching, so
 * the dispatcher's fallback runs: on API 31+ a launcher task root moves to the background, and
 * otherwise the activity finishes. Leaving the callback disabled afterwards would DRIFT from
 * WidgetMapper, which only publishes on a change: its next batch computing `true` would publish
 * nothing, and back would then exit the app from a sub-page. So this always restores
 * [setEnabled] to [enabledNow], read after the dispatch.
 *
 * Pure, with no Android types, so BackStateBufferTest pins the restore on the JVM. MainActivity
 * uses it for a press before boot and for .NET's BackUnhandled notice.
 */
fun handBackToPlatform(setEnabled: (Boolean) -> Unit, enabledNow: () -> Boolean, dispatch: () -> Unit) {
    setEnabled(false)
    try {
        dispatch()
    } finally {
        setEnabled(enabledNow())
    }
}
