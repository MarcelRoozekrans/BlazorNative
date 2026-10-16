package io.blazornative.shell

import androidx.lifecycle.Lifecycle
import androidx.test.core.app.ActivityScenario
import java.util.concurrent.atomic.AtomicReference

/** Neutral on purpose: the poll sees only the state, not what finished the activity. A back
 * press that was not handled on the page is one cause; a crash or the system is another. */
internal const val DESTROYED_WHILE_POLLED =
    "the activity was DESTROYED while the test still expected it on screen"

/**
 * Runs [predicate] on the UI thread every [sleepMs] until it is true, and returns false once
 * [deadlineMs] has passed.
 *
 * An activity destroyed under the poll, whatever destroyed it, would otherwise surface as
 * ActivityScenario's bare NullPointerException, which does not name the state. So an NPE out of
 * `onActivity` is caught, and reworded only if the scenario's state is then DESTROYED; an NPE on
 * a live activity rethrows as it was. Any other exception rethrows.
 */
internal fun pollUntil(
    scenario: ActivityScenario<MainActivity>,
    deadlineMs: Long,
    sleepMs: Long = 250,
    predicate: (MainActivity) -> Boolean,
): Boolean {
    val deadline = System.currentTimeMillis() + deadlineMs
    while (System.currentTimeMillis() < deadline) {
        val ok = AtomicReference(false)
        try {
            scenario.onActivity { act -> ok.set(predicate(act)) }
        } catch (e: NullPointerException) {
            if (scenario.state != Lifecycle.State.DESTROYED) throw e
            throw AssertionError(DESTROYED_WHILE_POLLED, e)
        }
        if (ok.get()) return true
        Thread.sleep(sleepMs)
    }
    return false
}
