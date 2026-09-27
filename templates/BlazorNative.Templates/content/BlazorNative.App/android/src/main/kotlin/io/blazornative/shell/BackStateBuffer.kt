package io.blazornative.shell

/**
 * Phase 16.2 (#346, spec decision 2) — **the pushed back state, held until the frame that
 * shows its page.**
 *
 * .NET pushes whether it can go back as a BackState notice, and sends it for a navigation
 * BEFORE the frames that show the new page. Applying it on arrival would enable back while
 * the OLD page is still on screen: a press in that window acts on a page the user cannot see,
 * navigating back from a page that has not appeared yet. So the visible screen wins. This
 * class only HOLDS a value; the frame producer takes it when it cuts the next batch, and main
 * applies it inside that batch's runnable, the same looper message that shows the page, so no
 * input event can land between the two.
 *
 * The three calls and their threads, all made by WidgetMapper:
 *  - [offer]: from the notice, on whichever thread .NET sent it from;
 *  - [takeForBatch]: on the frame producer, when a batch is cut at CommitFrame;
 *  - [applyFromBatch] and [canGoBack]: on main, inside that batch's runnable.
 *
 * A value no batch carries stays buffered until the next frame. .NET sends every BackState
 * ahead of frames, with one exception: a swap that throws resends the value its route state
 * still holds, and no frame may follow. If the swap threw before any of its frames, the resend
 * only supersedes the unapplied value, and the applied one is already right. If it threw after
 * a frame had applied the new value, the applied value stays wrong until the next frame. That
 * is a failed navigation on a screen the swap left half-built, and it is not pinned.
 *
 * It lives in `src/main/kotlin`, not `androidMain`, so the JVM suite can test it without a
 * device (BackStateBufferTest), the ImageRequestGuard precedent.
 */
class BackStateBuffer {

    private val lock = Any()

    /** The latest offered value no batch has taken yet, or null. Guarded by [lock]. */
    private var buffered: Boolean? = null

    /** The value the shell acts on. Main thread only. False until a batch applies one, so a
     * new shell starts with back disabled, which is the root's answer. */
    var canGoBack: Boolean = false
        private set

    /** Any thread: .NET's BackState notice. Held, never applied here; a later offer before
     * the next batch supersedes it. */
    fun offer(canGoBack: Boolean) {
        synchronized(lock) { buffered = canGoBack }
    }

    /** The frame producer, as it cuts a batch: the value that batch must apply, or null when
     * none was offered since the last batch. Clears the buffer, so one value rides one batch. */
    fun takeForBatch(): Boolean? = synchronized(lock) {
        val value = buffered
        buffered = null
        value
    }

    /** Main, inside the batch that [takeForBatch] cut: applies [carried]. Returns true when
     * [canGoBack] changed; null or the same value changes nothing. */
    fun applyFromBatch(carried: Boolean?): Boolean {
        if (carried == null || carried == canGoBack) return false
        canGoBack = carried
        return true
    }
}
