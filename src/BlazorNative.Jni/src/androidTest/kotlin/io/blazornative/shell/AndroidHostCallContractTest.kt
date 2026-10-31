package io.blazornative.shell

import androidx.lifecycle.Lifecycle
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.BiometricStatus
import io.blazornative.jni.HostCallOp
import io.blazornative.jni.SecureStorageStatus
import org.junit.After
import android.util.Log
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

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
 *     with getWithAuth, which pin 5 drives, so one hop is pinned for both. Provisioning the set's
 *     key needs an enrolled biometric, which the CI emulator lacks; only pin 6 reaches the set's
 *     prompt, through setAuthAllowsDeviceCredentialForTest, and it drives only a denial;
 *   - nothing of #506 any more: since 18.3 a request that shows a prompt holds its key, and the
 *     18.3 section below pins that a same-key request waits for the prompt's answer, while
 *     another key does not;
 *   - 18.3 Task 3: MainActivity.onDestroy's call to onHostActivityDestroyed, and
 *     cancelAuthentication on a real prompt. The destroy pins call the entry point directly with
 *     a hook-held gate, because a real prompt needs a real Activity and the hook replaces it;
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

    /** Begins [args], denies its prompt from the main thread, as the real prompt answers, and
     * returns the denial's completion. */
    private fun denyOnMain(what: String, args: String): Completion {
        val gateSeen = CountDownLatch(1)
        var gate: AndroidShellBridge.BiometricGate? = null
        AndroidShellBridge.biometricGateHook = { g -> gate = g; gateSeen.countDown() }
        val id = ids.getAndIncrement()
        bridge.hostCallBegin(id, HostCallOp.SECURE_STORAGE, args)
        assertTrue("anchor: the $what never reached the prompt", gateSeen.await(10, TimeUnit.SECONDS))
        var denier = ""
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            denier = Thread.currentThread().name
            gate!!.deny(SecureStorageStatus.AUTH_FAILED)
        }
        assertEquals("anchor: the $what's denial was not driven from the main thread", "main", denier)
        val done = completionFor(id)
        assertTrue("no completion after the $what's prompt was denied: a hang", done != null)
        assertEquals("the $what's denial must complete AuthFailed", SecureStorageStatus.AUTH_FAILED, done!!.status)
        return done
    }

    private fun assertOnTheExecutor(what: String, done: Completion) {
        assertTrue("#537: the denied $what completed on ${done.thread}, the main thread: an auth-bound storage request " +
            "must complete on the secure-storage executor whether its prompt is granted or denied",
            done.thread != "main")
        assertEquals("#537: the denied $what completed on ${done.thread}: it must complete on the " +
            "secure-storage executor", "BlazorNative-SecureStorage", done.thread)
    }

    @Test
    fun a_denied_storage_prompt_completes_on_the_secure_storage_executor() {
        SecureHarness.setPin()
        try {
            AndroidShellBridge.setAuthAllowsDeviceCredentialForTest = true
            val set = denyOnMain("auth-bound set",
                """{"action":"set","key":"$key","value":"v","auth":"1"}""")
            assertOnTheExecutor("auth-bound set", set)

            assertTrue("could not provision the auth-bound key (is a secure lock screen set?)",
                bridge.writeAuthBoundSecretForTest(key))
            val get = denyOnMain("getWithAuth",
                """{"action":"getWithAuth","key":"$key","reason":"test"}""")
            assertOnTheExecutor("getWithAuth", get)
        } finally {
            SecureHarness.clearPin()
        }
    }

    // ── 18.3 (#506): a request that shows a prompt holds its key ─────────────────────────────
    //
    // The gate hook keeps every gate it is handed, in order, and answers none, so a prompt stays
    // up until the test answers it on main, as the real prompt answers. "Completes before" is
    // the index in [completions], which the completion hook appends to in completion order.
    //
    // The seam's succeed() passes no AuthenticationResult, so an auth set's encrypt runs on the
    // un-unlocked cipher of a per-use auth-bound key and is expected to complete AuthFailed
    // through afterAuth. That still runs the release, which is the subject. The assertions below
    // hold whichever status the after-auth work yields, and each test logs it under BN-18.3.

    private val gates = LinkedBlockingQueue<AndroidShellBridge.BiometricGate>()
    private val otherKey = key + "_other"

    private fun holdEveryGate() {
        AndroidShellBridge.biometricGateHook = { g -> gates.add(g) }
    }

    private fun awaitGate(what: String): AndroidShellBridge.BiometricGate {
        val gate = gates.poll(10, TimeUnit.SECONDS)
        assertNotNull("anchor: the $what never reached the prompt", gate)
        return gate!!
    }

    private fun onMain(block: () -> Unit) = InstrumentationRegistry.getInstrumentation().runOnMainSync { block() }

    private fun begin(args: String): Long {
        val id = ids.getAndIncrement()
        bridge.hostCallBegin(id, HostCallOp.SECURE_STORAGE, args)
        return id
    }

    /** The completion for [id] as it stands now, without waiting. */
    private fun completionNow(id: Long): Completion? =
        synchronized(completions) { completions.firstOrNull { it.requestId == id } }

    /** The position of [id]'s first completion in completion order, or -1. */
    private fun indexOf(id: Long): Int =
        synchronized(completions) { completions.indexOfFirst { it.requestId == id } }

    private fun mustComplete(what: String, id: Long): Completion {
        val done = completionFor(id)
        assertNotNull("no completion for the $what within 10 s: a hang", done)
        return done!!
    }

    private fun authSet(value: String) = """{"action":"set","key":"$key","value":"$value","auth":"1"}"""
    private val deleteArgs = """{"action":"delete","key":"$key"}"""

    private fun logStatus(test: String, what: String, done: Completion) =
        Log.i("BN-18.3", "$test: $what completed status=${done.status} on ${done.thread}")

    /** Runs [body] with a lock-screen PIN and the set's device-credential fallback, as pin 6 does,
     * so an auth-bound set can provision its key and reach its prompt on the CI emulator. */
    private fun withPin(body: () -> Unit) {
        SecureHarness.setPin()
        try {
            AndroidShellBridge.setAuthAllowsDeviceCredentialForTest = true
            holdEveryGate()
            body()
        } finally {
            bridge.secureDeleteCore(otherKey)
            SecureHarness.clearPin()
        }
    }

    @Test
    fun a_same_key_delete_waits_behind_a_pending_auth_set() = withPin {
        val set = begin(authSet("v1"))
        val gate = awaitGate("auth set")
        val del = begin(deleteArgs)
        bridge.drainForTest()
        assertNull("anchor: the delete completed while the auth set's prompt was up: #506", completionNow(del))

        onMain { gate.succeed() }
        val setDone = mustComplete("auth set", set)
        mustComplete("delete", del)
        logStatus("delete_waits", "auth set", setDone)
        assertTrue("the delete overtook the auth set's after-auth work", indexOf(set) < indexOf(del))
        assertFalse("an orphan blob survived the delete", bridge.hasBlobForTest(key))
    }

    @Test
    fun a_same_key_plain_set_waits_behind_a_pending_auth_set() = withPin {
        val set = begin(authSet("v1"))
        val gate = awaitGate("auth set")
        val plain = begin("""{"action":"set","key":"$key","value":"v2","auth":"0"}""")
        bridge.drainForTest()
        assertNull("anchor: the plain set completed while the auth set's prompt was up: #506", completionNow(plain))

        onMain { gate.succeed() }
        val setDone = mustComplete("auth set", set)
        assertEquals("the plain set must complete Ok", SecureStorageStatus.OK, mustComplete("plain set", plain).status)
        logStatus("plain_set_waits", "auth set", setDone)
        assertTrue("the plain set overtook the auth set's after-auth work", indexOf(set) < indexOf(plain))
        val got = mustComplete("get", begin("""{"action":"get","key":"$key"}"""))
        assertEquals("the late auth work overwrote the plain set", SecureStorageStatus.OK, got.status)
        assertEquals("the late auth work overwrote the plain set", "v2", got.payload?.get("value"))
    }

    @Test
    fun a_second_prompt_request_takes_the_hold_when_it_drains() = withPin {
        val a = begin(authSet("a"))
        val gateA = awaitGate("first auth set")
        val b = begin(authSet("b"))
        val c = begin(deleteArgs)
        bridge.drainForTest()

        onMain { gateA.succeed() }
        val gateB = awaitGate("second auth set, drained when the first answered,")
        bridge.drainForTest()
        assertNull("anchor: the delete ran while the second auth set's prompt was up", completionNow(c))

        onMain { gateB.succeed() }
        logStatus("second_prompt", "first auth set", mustComplete("first auth set", a))
        logStatus("second_prompt", "second auth set", mustComplete("second auth set", b))
        mustComplete("delete", c)
        assertTrue("the delete did not complete last: first=${indexOf(a)} second=${indexOf(b)} delete=${indexOf(c)}",
            indexOf(a) < indexOf(c) && indexOf(b) < indexOf(c))
    }

    @Test
    fun another_key_is_not_blocked_by_a_pending_auth_set() = withPin {
        val set = begin(authSet("v1"))
        val gate = awaitGate("auth set")
        val otherSet = begin("""{"action":"set","key":"$otherKey","value":"o1","auth":"0"}""")
        val otherGet = begin("""{"action":"get","key":"$otherKey"}""")
        assertEquals("a plain set on another key waited behind key A's prompt",
            SecureStorageStatus.OK, completionFor(otherSet)?.status)
        val got = completionFor(otherGet)
        assertEquals("a get on another key waited behind key A's prompt", SecureStorageStatus.OK, got?.status)
        assertEquals("the get on another key returned the wrong value", "o1", got?.payload?.get("value"))
        assertNull("anchor: key A's prompt is still up", completionNow(set))
        assertTrue("anchor: key A's prompt is still up", bridge.isKeyHeldForTest(key))

        onMain { gate.deny(SecureStorageStatus.AUTH_FAILED) }
        mustComplete("auth set", set)
    }

    @Test
    fun a_same_key_delete_waits_behind_a_pending_getWithAuth() {
        SecureHarness.setPin()
        try {
            assertTrue("could not provision the auth-bound key (is a secure lock screen set?)",
                bridge.writeAuthBoundSecretForTest(key))
            holdEveryGate()
            val read = begin("""{"action":"getWithAuth","key":"$key","reason":"test"}""")
            val gate = awaitGate("getWithAuth")
            val del = begin(deleteArgs)
            bridge.drainForTest()
            assertNull("anchor: the delete completed while the getWithAuth's prompt was up: #506", completionNow(del))

            onMain { gate.succeed() }
            logStatus("getWithAuth_delete", "getWithAuth", mustComplete("getWithAuth", read))
            assertEquals("the delete must complete Ok", SecureStorageStatus.OK, mustComplete("delete", del).status)
            assertTrue("the delete overtook the getWithAuth's after-auth read", indexOf(read) < indexOf(del))
        } finally {
            SecureHarness.clearPin()
        }
    }

    @Test
    fun a_denied_prompt_releases_its_key() = withPin {
        val set = begin(authSet("v1"))
        val gate = awaitGate("auth set")
        val del = begin(deleteArgs)
        bridge.drainForTest()
        assertNull("anchor: the delete completed while the auth set's prompt was up: #506", completionNow(del))

        onMain { gate.deny(SecureStorageStatus.AUTH_FAILED) }
        assertEquals("the denied auth set must complete AuthFailed",
            SecureStorageStatus.AUTH_FAILED, mustComplete("auth set", set).status)
        val delDone = mustComplete("delete parked behind the denied prompt", del)
        assertEquals("the parked delete must complete Ok", SecureStorageStatus.OK, delDone.status)
        assertEquals("the parked delete must run on the secure-storage executor",
            "BlazorNative-SecureStorage", delDone.thread)
    }

    @Test
    fun a_late_answer_does_not_release_another_requests_hold() = withPin {
        val a = begin(authSet("a"))
        val gateA = awaitGate("first auth set")
        val b = begin(authSet("b"))
        val c = begin(deleteArgs)
        bridge.drainForTest()

        onMain { gateA.deny(SecureStorageStatus.AUTH_FAILED) }
        mustComplete("first auth set", a)
        val gateB = awaitGate("second auth set, drained when the first was denied,")
        onMain { gateA.deny(SecureStorageStatus.AUTH_FAILED) }
        bridge.drainForTest()
        assertEquals("A's late answer completed it a second time", 1,
            synchronized(completions) { completions.count { it.requestId == a } })
        assertNull("anchor: A's second answer released B's hold", completionNow(c))

        onMain { gateB.deny(SecureStorageStatus.AUTH_FAILED) }
        mustComplete("second auth set", b)
        mustComplete("delete", c)
    }

    @Test
    fun a_throw_before_the_prompt_releases_its_key() = withPin {
        // A holds the key; B and the delete park behind it. When A answers, B is drained, takes
        // the hold, and throws before its gate is armed: the second gate call throws.
        val gateCalls = AtomicInteger(0)
        AndroidShellBridge.gateThrowForTest = {
            if (gateCalls.incrementAndGet() == 2) throw IllegalStateException("gate-throw")
        }
        val a = begin(authSet("a"))
        val gateA = awaitGate("first auth set")
        val b = begin(authSet("b"))
        val del = begin(deleteArgs)
        bridge.drainForTest()
        assertNull("anchor: the delete completed while the first auth set's prompt was up: #506", completionNow(del))

        onMain { gateA.deny(SecureStorageStatus.AUTH_FAILED) }
        mustComplete("first auth set", a)
        assertEquals("the auth set that threw before its prompt must complete Error",
            SecureStorageStatus.ERROR, mustComplete("auth set that threw", b).status)
        assertEquals("the delete parked behind the throw must complete Ok",
            SecureStorageStatus.OK, mustComplete("delete parked behind the throw", del).status)
        assertTrue("the delete overtook the auth set that threw", indexOf(b) < indexOf(del))
    }

    // ── 18.3 (#506) Task 3: a destroyed Activity denies its pending prompts ──────────────────
    //
    // Record §1: when the Activity that hosts a prompt is destroyed by finish() or after going to
    // the background, androidx can drop the prompt's answer, which would hold its key forever.
    // On a configuration change androidx keeps the prompt up and later answers the ORIGINAL
    // callback, so that destroy must deny nothing.
    //
    // The gate hook replaces the real prompt, so no Activity hosts it here. These pins drive the
    // destroy through AndroidShellBridge.onHostActivityDestroyed, the entry point MainActivity's
    // onDestroy calls, with a hook-held gate registered as pending. NOT COVERED: the
    // MainActivity.onDestroy call site itself, and cancelAuthentication on a real prompt.

    @Test
    fun a_destroyed_activity_releases_the_held_key() = withPin {
        val set = begin(authSet("v1"))
        awaitGate("auth set")
        val del = begin(deleteArgs)
        bridge.drainForTest()
        assertNull("anchor: the delete completed while the auth set's prompt was up: #506", completionNow(del))

        onMain { bridge.onHostActivityDestroyed(isChangingConfigurations = false) }
        assertEquals("anchor: the auth set whose Activity was destroyed must complete AuthFailed",
            SecureStorageStatus.AUTH_FAILED, mustComplete("auth set whose Activity was destroyed", set).status)
        assertEquals("anchor: the delete parked behind the lost prompt must complete Ok",
            SecureStorageStatus.OK, mustComplete("delete parked behind the lost prompt", del).status)
        assertTrue("the delete overtook the denied auth set", indexOf(set) < indexOf(del))
        assertFalse("the key is still held after its Activity was destroyed", bridge.isKeyHeldForTest(key))
    }

    @Test
    fun a_prompt_request_drained_after_the_destroy_is_denied_at_once() = withPin {
        // A holds the key; auth set B and the delete park. The destroy denies A, and B is drained
        // with no Activity left to prompt on: it must be denied at once, not wait for a prompt
        // that can never show.
        val a = begin(authSet("a"))
        awaitGate("first auth set")
        val b = begin(authSet("b"))
        val del = begin(deleteArgs)
        bridge.drainForTest()
        assertNull("anchor: the delete completed while the first auth set's prompt was up: #506", completionNow(del))

        onMain { bridge.onHostActivityDestroyed(isChangingConfigurations = false) }
        assertEquals("the first auth set must complete AuthFailed",
            SecureStorageStatus.AUTH_FAILED, mustComplete("first auth set", a).status)
        assertEquals("anchor: the auth set drained after the destroy must complete AuthFailed",
            SecureStorageStatus.AUTH_FAILED, mustComplete("auth set drained after the destroy", b).status)
        assertNull("the auth set drained after the destroy reached a prompt", gates.poll())
        assertEquals("the delete parked behind both must complete Ok",
            SecureStorageStatus.OK, mustComplete("delete parked behind both", del).status)
        assertTrue("the delete did not complete last: first=${indexOf(a)} second=${indexOf(b)} delete=${indexOf(del)}",
            indexOf(a) < indexOf(del) && indexOf(b) < indexOf(del))
    }

    @Test
    fun a_configuration_change_keeps_the_prompt_and_its_hold() = withPin {
        // Record §1 (a): androidx keeps the prompt up across a recreation and answers the original
        // callback, so the destroy of a configuration change denies nothing.
        val set = begin(authSet("v1"))
        val gate = awaitGate("auth set")
        val del = begin(deleteArgs)
        onMain { bridge.onHostActivityDestroyed(isChangingConfigurations = true) }
        bridge.drainForTest()
        assertNull("anchor: a configuration change denied the pending prompt", completionNow(set))
        assertNull("anchor: a configuration change released the held key", completionNow(del))
        assertTrue("anchor: a configuration change released the held key", bridge.isKeyHeldForTest(key))

        onMain { gate.deny(SecureStorageStatus.AUTH_FAILED) }
        mustComplete("auth set answered after the recreation", set)
        assertEquals("the parked delete must complete Ok once the prompt answers",
            SecureStorageStatus.OK, mustComplete("delete", del).status)
    }

    @Test
    fun a_prompt_on_an_activity_destroyed_by_a_configuration_change_is_denied_at_once() {
        // The REAL gate path, no hook: this bridge is built on a real MainActivity, which is then
        // recreated. A configuration change denies nothing (the pin above), so this bridge's own
        // destroy flag stays false, and only the destroyed Activity itself can tell the gate that
        // no prompt will ever show. androidx would drop a prompt on it silently, so without a
        // denial the auth set would hold its key forever and the delete behind it would hang.
        SecureHarness.setPin()
        try {
            AndroidShellBridge.setAuthAllowsDeviceCredentialForTest = true
            ActivityScenario.launch(MainActivity::class.java).use { scenario ->
                scenario.awaitBooted()
                // Held strongly, so the bridge's WeakReference still yields the destroyed
                // Activity and the gate does not take the no-Activity path instead.
                val old = AtomicReference<MainActivity>()
                val onOld = AtomicReference<AndroidShellBridge>()
                scenario.onActivity { a ->
                    old.set(a)
                    onOld.set(AndroidShellBridge(a) { _, _ -> })
                }
                scenario.recreate()
                assertTrue("anchor: the old Activity was not destroyed by the recreation", old.get().isDestroyed)

                val dead = onOld.get()
                try {
                    val set = ids.getAndIncrement()
                    dead.hostCallBegin(set, HostCallOp.SECURE_STORAGE, authSet("v1"))
                    val del = ids.getAndIncrement()
                    dead.hostCallBegin(del, HostCallOp.SECURE_STORAGE, deleteArgs)
                    assertEquals("anchor: the auth set gated on a destroyed Activity must complete AuthFailed",
                        SecureStorageStatus.AUTH_FAILED, mustComplete("auth set gated on a destroyed Activity", set).status)
                    assertEquals("anchor: the delete parked behind it must complete Ok",
                        SecureStorageStatus.OK, mustComplete("delete parked behind the destroyed Activity's gate", del).status)
                    assertTrue("the delete overtook the auth set", indexOf(set) < indexOf(del))
                } finally {
                    dead.drainForTest()
                }
            }
        } finally {
            SecureHarness.clearPin()
        }
    }

    @Test
    fun a_prompt_on_a_stopped_activity_is_denied_at_once() {
        // The REAL gate path, no hook. Once the Activity is stopped its FragmentManager has saved
        // its state, and androidx's authenticate then drops the prompt silently ("Called after
        // onSaveInstanceState"). No destroy need ever follow: the user may come back. Without a
        // denial the auth set would hold its key forever and the delete behind it would hang.
        SecureHarness.setPin()
        try {
            AndroidShellBridge.setAuthAllowsDeviceCredentialForTest = true
            ActivityScenario.launch(MainActivity::class.java).use { scenario ->
                scenario.awaitBooted()
                // Held strongly, so the bridge's WeakReference still yields the Activity.
                val activity = AtomicReference<MainActivity>()
                val onIt = AtomicReference<AndroidShellBridge>()
                scenario.onActivity { a ->
                    activity.set(a)
                    onIt.set(AndroidShellBridge(a) { _, _ -> })
                }
                scenario.moveToState(Lifecycle.State.CREATED)
                val saved = AtomicReference<Boolean>()
                onMain { saved.set(activity.get().supportFragmentManager.isStateSaved) }
                assertTrue("anchor: the stopped Activity's FragmentManager has not saved its state", saved.get())
                assertFalse("anchor: the stopped Activity is destroyed or finishing, which is another pin's case",
                    activity.get().isDestroyed || activity.get().isFinishing)

                val stopped = onIt.get()
                try {
                    val set = ids.getAndIncrement()
                    stopped.hostCallBegin(set, HostCallOp.SECURE_STORAGE, authSet("v1"))
                    val del = ids.getAndIncrement()
                    stopped.hostCallBegin(del, HostCallOp.SECURE_STORAGE, deleteArgs)
                    assertEquals("anchor: the auth set gated on a stopped Activity must complete AuthFailed",
                        SecureStorageStatus.AUTH_FAILED, mustComplete("auth set gated on a stopped Activity", set).status)
                    assertEquals("anchor: the delete parked behind it must complete Ok",
                        SecureStorageStatus.OK, mustComplete("delete parked behind the stopped Activity's gate", del).status)
                    assertTrue("the delete overtook the auth set", indexOf(set) < indexOf(del))
                } finally {
                    stopped.drainForTest()
                }
            }
        } finally {
            SecureHarness.clearPin()
        }
    }

    @Test
    fun a_prompt_that_cannot_be_shown_is_denied() {
        // The REAL gate path, no hook, on a RESUMED Activity: the gate passes its Activity-state
        // check, and building or showing the prompt throws on main. That runs after the caller
        // returned, so only the main-thread catch can answer: a prompt that cannot be shown is a
        // denial. Without it the auth set would never complete, its key would stay held, and the
        // delete behind it would hang. promptThrowForTest throws where the real prompt is built.
        SecureHarness.setPin()
        try {
            AndroidShellBridge.setAuthAllowsDeviceCredentialForTest = true
            AndroidShellBridge.promptThrowForTest = { throw IllegalStateException("prompt-throw") }
            ActivityScenario.launch(MainActivity::class.java).use { scenario ->
                scenario.awaitBooted()
                // Held strongly, so the bridge's WeakReference still yields the Activity.
                val activity = AtomicReference<MainActivity>()
                val onIt = AtomicReference<AndroidShellBridge>()
                scenario.onActivity { a ->
                    activity.set(a)
                    onIt.set(AndroidShellBridge(a) { _, _ -> })
                }
                assertEquals("anchor: the Activity is not resumed, which is another pin's case",
                    Lifecycle.State.RESUMED, scenario.state)

                val resumed = onIt.get()
                try {
                    val set = ids.getAndIncrement()
                    resumed.hostCallBegin(set, HostCallOp.SECURE_STORAGE, authSet("v1"))
                    val del = ids.getAndIncrement()
                    resumed.hostCallBegin(del, HostCallOp.SECURE_STORAGE, deleteArgs)
                    assertEquals("the auth set whose prompt could not be shown must complete AuthFailed",
                        SecureStorageStatus.AUTH_FAILED, mustComplete("auth set whose prompt could not be shown", set).status)
                    assertEquals("the delete parked behind it must complete Ok",
                        SecureStorageStatus.OK, mustComplete("delete parked behind the prompt that could not be shown", del).status)
                    assertTrue("the delete overtook the auth set", indexOf(set) < indexOf(del))
                } finally {
                    resumed.drainForTest()
                }
            }
        } finally {
            SecureHarness.clearPin()
        }
    }
}
