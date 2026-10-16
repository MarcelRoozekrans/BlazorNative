package io.blazornative.shell

import android.content.Intent
import androidx.lifecycle.Lifecycle
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.fail
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 17.5 (#458) — the shared [pollUntil] names a destroyed activity.
 *
 * Ten test classes each had a private copy, and nine surfaced ActivityScenario's bare
 * NullPointerException when the activity was gone. The anchor is the scenario's own state:
 * the test asserts DESTROYED before it polls, so a poll that never met a destroyed activity
 * cannot pass for the right reason.
 *
 * DOES NOT COVER: why an activity was destroyed in some other test. The helper reports the state,
 * not the cause.
 */
@RunWith(AndroidJUnit4::class)
class PollAndroidTest {

    @Test
    fun a_poll_on_a_destroyed_activity_fails_naming_it() {
        val intent = Intent(ApplicationProvider.getApplicationContext(), MainActivity::class.java)
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            scenario.awaitBooted()
            scenario.moveToState(Lifecycle.State.DESTROYED)
            assertEquals("anchor: the scenario must be DESTROYED before the poll",
                Lifecycle.State.DESTROYED, scenario.state)

            try {
                pollUntil(scenario, 2_000) { true }
            } catch (e: AssertionError) {
                assertEquals("the activity was DESTROYED while the test still expected it on screen", e.message)
                return
            }
            fail("pollUntil returned on a destroyed activity instead of failing naming it")
        }
    }
}
