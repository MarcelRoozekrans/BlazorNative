package io.blazornative.shell

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.BiometricStatus
import io.blazornative.jni.HostCallOp
import io.blazornative.jni.SecureStorageStatus
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

/**
 * Phase 17.2 (#440) — the host-call contract on Android: `hostCallBegin` records the request
 * and RETURNS AT ONCE; the outcome arrives later through `host_call_complete`. Biometrics and
 * secure storage used to do their BiometricManager and AndroidKeyStore work inside the begin,
 * on the caller's thread, which is .NET's render thread. Since 17.2 each handler has its own
 * serial executor, BlazorNative-Biometrics and BlazorNative-SecureStorage, as iOS has since
 * 16.4.
 *
 * The bridge here is the test's own, and its request ids start at 1_000_000_000, far above
 * any .NET issues. AndroidShellBridge.completeHookForTest takes every completion with its
 * thread name, instead of sending it to .NET.
 *
 * DOES NOT COVER:
 *   - a real fingerprint or a real system prompt: the prompt runs through biometricGateHook,
 *     as BnSecureAndroidTest does. Real hardware is owner-device territory;
 *   - that an after-auth decrypt SUCCEEDS: the hook's succeed() passes no AuthenticationResult,
 *     and the emulator's key is per-use auth-bound, so the decrypt throws and completes
 *     AuthFailed. Pin 5 proves only WHERE the after-auth work runs;
 *   - the auth-bound SET's after-auth path on its own: it shares AndroidShellBridge.afterAuth
 *     with getWithAuth, which pin 5 drives, so one hop is pinned for both. The set itself cannot
 *     reach its prompt on the CI emulator, since provisioning its key needs an enrolled biometric;
 *   - the order of an auth-bound request's after-auth work against later requests, #506;
 *   - geolocation and notifications, whose begins #440 does not name;
 *   - the iOS twin: BnBiometricsTests and BnSecureStorageTests pin it since 16.4.
 */
@RunWith(AndroidJUnit4::class)
class AndroidHostCallContractTest {

    private data class Completion(val requestId: Long, val status: Int, val payload: Map<String, String>?, val thread: String)

    private lateinit var bridge: AndroidShellBridge
    private val completions: MutableList<Completion> = Collections.synchronizedList(mutableListOf())
    private val callers: MutableList<Thread> = Collections.synchronizedList(mutableListOf())

    companion object {
        /** Process-wide, so no two tests ever share a request id: a gated job one test leaves
         * behind cannot complete into the next test's hook under the same id. */
        private val ids = java.util.concurrent.atomic.AtomicLong(1_000_000_000L)
    }
    private val key = "bn_contract_test"

    @Before
    fun setup() {
        AndroidShellBridge.resetHostCallContractForTest()
        AndroidShellBridge.completeHookForTest = { id, status, payload, thread ->
            completions.add(Completion(id, status, payload, thread))
        }
        bridge = AndroidShellBridge(InstrumentationRegistry.getInstrumentation().targetContext) { _, _ -> }
        bridge.secureDeleteCore(key)
    }

    @After
    fun cleanup() {
        AndroidShellBridge.workGateForTest?.countDown()
        synchronized(callers) { callers.toList() }.forEach { it.join(10_000) }
        bridge.drainForTest()
        bridge.secureDeleteCore(key)
        AndroidShellBridge.resetHostCallContractForTest()
    }

    private fun completionFor(id: Long, timeoutMs: Long = 10_000): Completion? {
        val deadline = System.currentTimeMillis() + timeoutMs
        while (System.currentTimeMillis() < deadline) {
            synchronized(completions) { completions.firstOrNull { it.requestId == id } }?.let { return it }
            Thread.sleep(10)
        }
        return null
    }

    /** Begins [op] on a worker thread and returns whether hostCallBegin returned within [ms]. */
    private fun beginReturnsWithin(ms: Long, id: Long, op: Int, args: String): Boolean {
        val returned = CountDownLatch(1)
        val caller = Thread({ bridge.hostCallBegin(id, op, args); returned.countDown() }, "contract-test-caller")
        callers.add(caller)
        caller.start()
        return returned.await(ms, TimeUnit.MILLISECONDS)
    }

    private fun assertBeginReturnsBeforeTheWorkRuns(op: Int, args: String, workThread: String) {
        val gate = CountDownLatch(1)
        val reached = CountDownLatch(1)
        AndroidShellBridge.workGateForTest = gate
        AndroidShellBridge.gateReachedForTest = reached
        val id = ids.getAndIncrement()

        val returned = beginReturnsWithin(1_000, id, op, args)
        assertTrue("anchor: the work never reached its gate, so this proves nothing",
            reached.await(10, TimeUnit.SECONDS))
        assertTrue("hostCallBegin did not return while the work was gated: the work runs inside the begin, " +
            "on the caller's thread. That is #440.", returned)
        assertEquals("a completion arrived while the work was still gated", null,
            synchronized(completions) { completions.firstOrNull { it.requestId == id } })

        gate.countDown()
        val done = completionFor(id)
        assertTrue("no completion within 10 s after the gate opened", done != null)
        assertEquals("the work must complete on its handler's executor", workThread, done!!.thread)
    }

    @Test
    fun biometrics_begin_returns_before_the_check_runs() =
        assertBeginReturnsBeforeTheWorkRuns(HostCallOp.BIOMETRICS, """{"action":"check"}""", "BlazorNative-Biometrics")

    @Test
    fun secure_storage_begin_returns_before_the_keystore_work_runs() =
        assertBeginReturnsBeforeTheWorkRuns(HostCallOp.SECURE_STORAGE,
            """{"action":"set","key":"$key","value":"v0","auth":"0"}""", "BlazorNative-SecureStorage")

    @Test
    fun secure_storage_keeps_request_order() {
        val set = ids.getAndIncrement()
        val get = ids.getAndIncrement()
        bridge.hostCallBegin(set, HostCallOp.SECURE_STORAGE, """{"action":"set","key":"$key","value":"v1","auth":"0"}""")
        bridge.hostCallBegin(get, HostCallOp.SECURE_STORAGE, """{"action":"get","key":"$key"}""")
        bridge.drainForTest()
        assertEquals("the set must complete Ok", SecureStorageStatus.OK, completionFor(set)?.status)
        val got = completionFor(get)
        assertEquals("the get, begun after the set, must see its value: requests keep their order",
            SecureStorageStatus.OK, got?.status)
        assertEquals("v1", got?.payload?.get("value"))
    }

    @Test
    fun a_throw_on_the_executor_completes_error_never_a_hang() {
        AndroidShellBridge.jobFaultForTest = { throw IllegalStateException("job-fault") }
        val id = ids.getAndIncrement()
        bridge.hostCallBegin(id, HostCallOp.SECURE_STORAGE, """{"action":"get","key":"$key"}""")
        val done = completionFor(id)
        assertTrue("a job that threw left the request pending: a hang", done != null)
        assertEquals(SecureStorageStatus.ERROR, done!!.status)
    }

    @Test
    fun the_after_auth_work_runs_on_the_secure_storage_executor() {
        SecureHarness.setPin()
        try {
            assertTrue("could not provision the auth-bound key (is a secure lock screen set?)",
                bridge.writeAuthBoundSecretForTest(key))
            val gateSeen = CountDownLatch(1)
            var gate: AndroidShellBridge.BiometricGate? = null
            AndroidShellBridge.biometricGateHook = { g -> gate = g; gateSeen.countDown() }
            val id = ids.getAndIncrement()
            bridge.hostCallBegin(id, HostCallOp.SECURE_STORAGE, """{"action":"getWithAuth","key":"$key","reason":"test"}""")
            assertTrue("anchor: the getWithAuth never reached the prompt", gateSeen.await(10, TimeUnit.SECONDS))
            // The real prompt answers on the main thread; so does this.
            InstrumentationRegistry.getInstrumentation().runOnMainSync { gate!!.succeed() }
            val done = completionFor(id)
            assertTrue("no completion after the prompt succeeded", done != null)
            assertEquals("the after-auth KeyStore work ran on ${done!!.thread}: it must run on the secure-storage " +
                "executor, never on main", "BlazorNative-SecureStorage", done.thread)
        } finally {
            SecureHarness.clearPin()
        }
    }
}
