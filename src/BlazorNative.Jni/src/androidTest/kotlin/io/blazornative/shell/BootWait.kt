package io.blazornative.shell

import androidx.test.core.app.ActivityScenario
import org.junit.Assert.assertTrue
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Waits until the shell accepts host events: lifecycle, back, a warm deep link, safe-area
 * insets. The first frame is on screen BEFORE that, because the boot thread delivers it from
 * inside `start()`, and `MainActivity` drops a host event that arrives before boot by design:
 * insets are re-sent later only with the device's real values, and a back press goes to the
 * platform, which can finish the Activity. A test that sends one as soon as the page appears
 * therefore flakes; BnSafeAreaAndroidTest timed out this way on PR #513.
 */
internal fun ActivityScenario<MainActivity>.awaitBooted(timeoutMs: Long = 60_000): ActivityScenario<MainActivity> {
    val deadline = System.currentTimeMillis() + timeoutMs
    val booted = AtomicBoolean(false)
    while (System.currentTimeMillis() < deadline) {
        onActivity { booted.set(it.bootedForTest) }
        if (booted.get()) return this
        Thread.sleep(50)
    }
    assertTrue("the shell never finished booting within ${timeoutMs / 1000}s", booted.get())
    return this
}
