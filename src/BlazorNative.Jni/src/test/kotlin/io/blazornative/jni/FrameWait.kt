package io.blazornative.jni

/**
 * Since 16.7 an await on a host call or a fetch always yields, even when the shell answers the
 * call inside hostCallBegin, so the continuation's frame arrives AFTER dispatchEventBlocking
 * returns, from the render thread. A test that reads the frames right after the dispatch races
 * that frame. This waits for it, bounded.
 *
 * [before] is the frame count taken after mount and must be at least 1, so the window never
 * contains the mount frame. The predicate is the same one the assertion applies: a single
 * ReplaceText of [text] on [nodeId]. On timeout the window so far is returned, so the caller's
 * own assertion fails with its usual message.
 *
 * [frames] must be a synchronized list, because the render thread appends to it.
 */
internal fun awaitReplaceText(
    frames: MutableList<RenderFrame>,
    before: Int,
    nodeId: Int,
    text: String,
    timeoutMs: Long = 10_000,
): List<RenderFrame> {
    require(before >= 1) { "before must be taken after the mount frame arrived" }
    fun window(): List<RenderFrame> = synchronized(frames) { frames.subList(before, frames.size).toList() }
    val deadline = System.nanoTime() + timeoutMs * 1_000_000
    while (true) {
        val w = window()
        val hit = w.flatMap { it.patches.filterIsInstance<RenderPatch.ReplaceText>() }
            .singleOrNull { it.nodeId == nodeId && it.text == text }
        if (hit != null || System.nanoTime() >= deadline) return w
        Thread.sleep(10)
    }
}
