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

/** Neutral like [DESTROYED_WHILE_POLLED]: the poll sees a new instance, not what replaced the old
 * one. A configuration change, the system's own relaunch, or a test's `recreate()` all look alike. */
internal const val RECREATED_WHILE_POLLED =
    "the activity was recreated while the test still expected the one it started on"

/** The MainActivity the scenario holds right now. Pass it to [pollUntilOnSameActivity]. */
internal fun ActivityScenario<MainActivity>.currentActivity(): MainActivity {
    val out = AtomicReference<MainActivity>()
    onActivity { act -> out.set(act) }
    return out.get()
}

/**
 * [pollUntil], for a test whose state lives on one activity's page: it fails with
 * [RECREATED_WHILE_POLLED] as soon as the scenario holds an activity other than [activity].
 *
 * A recreated MainActivity mounts its page fresh, so page state the test built, such as a photo's
 * `src`, is gone, and the predicate can only time out. #407 was that timeout, reported as the
 * thing the test waited for. The instance is checked before [predicate] runs, so a predicate the
 * new page happens to satisfy still fails.
 *
 * A poll that lands between the old activity's destruction and the new one's creation fails with
 * [pollUntil]'s [DESTROYED_WHILE_POLLED] instead. Either message names what happened.
 */
internal fun pollUntilOnSameActivity(
    scenario: ActivityScenario<MainActivity>,
    activity: MainActivity,
    deadlineMs: Long,
    sleepMs: Long = 250,
    predicate: (MainActivity) -> Boolean,
): Boolean = pollUntil(scenario, deadlineMs, sleepMs) { act ->
    if (act !== activity) throw AssertionError(RECREATED_WHILE_POLLED)
    predicate(act)
}
