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
 * batch that SHOWS A PAGE, and main applies it inside that batch's runnable, the same looper
 * message that puts the page on screen, so no input event can land between the two.
 *
 * WHY "SHOWS A PAGE" AND NOT "THE NEXT BATCH". A swap is TWO frames, each with its own
 * CommitFrame: the old root's RemoveNodes, then the new page's creates (measured in fix round
 * 1 of Task 3). Riding the next batch would apply the value with the removal, on a blank
 * screen, one runnable before the page. A batch shows a page when it creates a PARENTLESS node:
 * the root of a mounted component, which is what every mount and every swap's second frame
 * begin with. [showsAPage] is that test.
 *
 * The three calls and their threads, all made by WidgetMapper:
 *  - [offer]: from the notice, on whichever thread .NET sent it from;
 *  - [takeForBatch]: on the frame producer, when a batch is cut at CommitFrame;
 *  - [applyFromBatch] and [canGoBack]: on main, inside that batch's runnable.
 *
 * A value no page carries stays buffered. .NET sends every BackState ahead of a mount or a
 * swap, with one exception: a swap that throws resends the value its route state still holds.
 * That resend supersedes the unapplied value, and because the failed swap mounted no page, the
 * applied value was never changed and is already right. The stale buffer is harmless: it holds
 * the value already applied, and the next navigation's notice supersedes it.
 *
 * It lives in `src/main/kotlin`, not `androidMain`, so the JVM suite can test it without a
 * device (BackStateBufferTest, and BackNoticeTest against the dll's real frames), the
 * ImageRequestGuard precedent.
 */
class BackStateBuffer {

    private val lock = Any()

    /** The latest offered value no page has carried yet, or null. Guarded by [lock]. */
    private var buffered: Boolean? = null

    /** The value the shell acts on. Main thread only. False until a batch applies one, so a
     * new shell starts with back disabled, which is the root's answer. */
    var canGoBack: Boolean = false
        private set

    /** Any thread: .NET's BackState notice. Held, never applied here; a later offer before
     * the next page supersedes it. */
    fun offer(canGoBack: Boolean) {
        synchronized(lock) { buffered = canGoBack }
    }

    /** The frame producer, as it cuts [batch]: the value that batch must apply, or null. Only
     * a batch that [showsAPage] carries the value, and taking it clears the buffer, so one
     * value rides one page. Any other batch carries nothing and leaves the buffer alone. */
    fun takeForBatch(batch: List<RenderPatch>): Boolean? {
        if (!showsAPage(batch)) return null
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
        /** True when [batch] mounts a page: it creates a parentless node, the root of a mounted
         * component. A swap's removal frame, a re-render and a modal's show create none. */
        fun showsAPage(batch: List<RenderPatch>): Boolean =
            batch.any { it is RenderPatch.CreateNode && it.parentId == null }
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
