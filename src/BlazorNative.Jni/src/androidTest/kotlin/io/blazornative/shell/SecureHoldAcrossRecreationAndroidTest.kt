package io.blazornative.shell

import androidx.lifecycle.Lifecycle
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.HostCallOp
import io.blazornative.jni.SecureStorageStatus
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 18.4 (#601) — a rotation with a secure-storage prompt up, then a finish, on a real
 * MainActivity.
 *
 * On a configuration change androidx keeps the prompt up and answers the OLD bridge's callback,
 * from the new Activity's fragment (18.3 record §1). When that new Activity is then finished, the
 * fragment dies with it and the answer is lost. The prompt belongs to the first Activity's bridge,
 * whose own destroy was a configuration change and denied nothing, so only the second Activity's
 * final destroy can deny it. Without that the auth set's key stays held for the life of the
 * process, and every later request on the key parks forever.
 *
 * This is the real-Activity twin of AndroidHostCallContractTest's
 * `a_final_destroy_denies_a_prompt_orphaned_by_a_configuration_change`: both destroys here come
 * from MainActivity.onDestroy, through a real recreate() and a real finish.
 *
 * DOES NOT COVER: a real system prompt, which biometricGateHook replaces as in
 * AndroidHostCallContractTest, and cancelAuthentication on it. iOS is not affected: it never
 * recreates its root view controller, so it has one bridge, and one hold, per process.
 */
@RunWith(AndroidJUnit4::class)
class SecureHoldAcrossRecreationAndroidTest {

    private data class Completion(val requestId: Long, val status: Int)

    private val completions: MutableList<Completion> = Collections.synchronizedList(mutableListOf())
    private val gates: MutableList<AndroidShellBridge.BiometricGate> = Collections.synchronizedList(mutableListOf())
    private val key = "bn_recreation_hold_test"
    private lateinit var own: AndroidShellBridge

    companion object {
        /** Far above any id .NET issues and apart from AndroidHostCallContractTest's range. */
        private val ids = AtomicLong(2_000_000_000L)
    }

    @Before
    fun setup() {
        AndroidShellBridge.resetHostCallContractForTest()
        AndroidShellBridge.completeHookForTest = { id, status, _, _ -> completions.add(Completion(id, status)) }
        own = AndroidShellBridge(InstrumentationRegistry.getInstrumentation().targetContext) { _, _ -> }
        own.secureDeleteCore(key)
    }

    @After
    fun cleanup() {
        // A failed pin leaves its prompt unanswered and its key held, process-wide since 18.4.
        // Denying every gate the test saw answers each prompt through the product's own path; a
        // gate that already answered is ignored by afterAuth.
        val seen = synchronized(gates) { gates.toList() }
        onMain { for (gate in seen) gate.deny(SecureStorageStatus.AUTH_FAILED) }
        repeat(4) { own.drainForTest() }
        own.secureDeleteCore(key)
        AndroidShellBridge.resetHostCallContractForTest()
    }

    private fun onMain(block: () -> Unit) = InstrumentationRegistry.getInstrumentation().runOnMainSync { block() }

    private fun completionNow(id: Long): Completion? =
        synchronized(completions) { completions.firstOrNull { it.requestId == id } }

    private fun completionFor(id: Long, timeoutMs: Long = 10_000): Completion? {
        val deadline = System.currentTimeMillis() + timeoutMs
        while (System.currentTimeMillis() < deadline) {
            completionNow(id)?.let { return it }
            Thread.sleep(10)
        }
        return null
    }

    private fun bridgeOf(scenario: ActivityScenario<MainActivity>): AndroidShellBridge {
        val bridge = AtomicReference<AndroidShellBridge?>()
        scenario.onActivity { bridge.set(it.shellBridgeForTest) }
        assertNotNull("anchor: the Activity has no shell bridge", bridge.get())
        return bridge.get()!!
    }

    @Test
    fun a_finish_after_a_rotation_denies_the_prompt_the_rotation_orphaned() {
        SecureHarness.setPin()
        try {
            AndroidShellBridge.setAuthAllowsDeviceCredentialForTest = true
            val gateSeen = CountDownLatch(1)
            AndroidShellBridge.biometricGateHook = { g -> gates.add(g); gateSeen.countDown() }
            ActivityScenario.launch(MainActivity::class.java).use { scenario ->
                scenario.awaitBooted()
                val first = bridgeOf(scenario)
                val set = ids.getAndIncrement()
                first.hostCallBegin(set, HostCallOp.SECURE_STORAGE,
                    """{"action":"set","key":"$key","value":"v1","auth":"1"}""")
                assertTrue("anchor: the auth set never reached the prompt", gateSeen.await(10, TimeUnit.SECONDS))

                scenario.recreate()
                scenario.awaitBooted()
                assertTrue("anchor: the recreated Activity kept the first Activity's bridge",
                    bridgeOf(scenario) !== first)
                own.drainForTest()
                assertNull("anchor: the rotation denied the prompt, which androidx keeps up across a configuration change",
                    completionNow(set))

                scenario.moveToState(Lifecycle.State.DESTROYED)
                assertEquals("the orphaned prompt was never denied: its key is held forever",
                    SecureStorageStatus.AUTH_FAILED, completionFor(set)?.status)
                val del = ids.getAndIncrement()
                own.hostCallBegin(del, HostCallOp.SECURE_STORAGE, """{"action":"delete","key":"$key"}""")
                assertEquals("a same-key delete after the finish did not complete Ok: the orphaned prompt's key is still held",
                    SecureStorageStatus.OK, completionFor(del)?.status)
            }
        } finally {
            SecureHarness.clearPin()
        }
    }
}
