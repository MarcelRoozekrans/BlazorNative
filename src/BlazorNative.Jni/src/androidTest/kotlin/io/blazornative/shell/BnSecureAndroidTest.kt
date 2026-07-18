package io.blazornative.shell

import android.app.KeyguardManager
import android.content.Context
import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.BiometricStatus
import io.blazornative.jni.SecureStorageStatus
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

// ─────────────────────────────────────────────────────────────────────────────
// Phase 9.2 Gate 2 (M9 DoD #4) — biometrics + secure storage on the AVD. The
// on-device third of BnSecureDemoTests.cs (.NET, DevHostBridge drives every status
// headless) and BiometricsSecureStorageTest.kt (JVM, through the dll). THIS proves
// AndroidShellBridge's REAL flow: raw AndroidKeyStore AES-256-GCM, the OS-key-level
// biometric binding, and BiometricPrompt via androidx against MainActivity (now a
// FragmentActivity).
//
// THE PROVEN / UNPROVEN SPLIT (the design's honesty, and biometrics is where the
// emulator is LEAST like reality — adb emu finger touch injects a SYNTHETIC event and
// the keystore is software-backed):
//   • PROVEN here, deterministically:
//       – the real AES-256-GCM store round-trips a NON-auth secret (set→get→delete→NotFound);
//       – the OS-KEY BINDING: a plain get of an AUTH-bound item returns AuthFailed —
//         the keystore's own KeyInfo says the key is user-auth-required and the OS
//         would refuse the decrypt (the "drop setUserAuthenticationRequired" mutation
//         reds this);
//       – biometric `check` returns a STATUS as DATA (never a throw);
//       – the biometric PROMPT path resolves to DATA (Authenticated on a seam-driven
//         success; a denial within a bounded await — NO HANG), and getWithAuth of an
//         absent secret is NotFound.
//   • UNPROVEN until a physical phone (the design's split, named not smuggled): the
//     REAL fingerprint-sensor prompt + the TEE-enforced auth-bound DECRYPT behind it
//     (an AES per-use-auth key's doFinal needs a LIVE OS auth — a synthetic finger
//     cannot unlock the CryptoObject cipher on CI, and the real system sheet is not
//     CI-drivable). CI drives the outcome through [biometricGateHook], the same seam
//     the geolocation/notifications real-dialog bypass uses.
// ─────────────────────────────────────────────────────────────────────────────

/** The keystore half — AndroidShellBridge's secure-storage core driven DIRECTLY
 * (no Activity, no .NET), asserting the real AES-256-GCM crypto and the OS-key
 * binding. A lock-screen PIN is set in @Before so an auth-bound key can be provisioned
 * (an AES user-auth key requires a secure lock screen), and cleared in @After. */
@RunWith(AndroidJUnit4::class)
class SecureStorageAndroidTest {

    private lateinit var bridge: AndroidShellBridge

    private val plainKey = "bn_test_plain"
    private val authKey = "bn_test_auth"

    @Before
    fun setup() {
        SecureHarness.setPin()
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        bridge = AndroidShellBridge(ctx) { _, _ -> }
        bridge.secureDeleteCore(plainKey)
        bridge.secureDeleteCore(authKey)
    }

    @After
    fun cleanup() {
        bridge.secureDeleteCore(plainKey)
        bridge.secureDeleteCore(authKey)
        SecureHarness.clearPin()
    }

    // ── The real AES-256-GCM store round-trips a NON-auth secret ─────────────

    @Test
    fun nonauth_secret_round_trips_through_the_android_keystore() {
        assertEquals(SecureStorageStatus.OK, bridge.secureSetPlainCore(plainKey, "hunter2").status)

        val got = bridge.secureGetCore(plainKey)
        assertEquals(SecureStorageStatus.OK, got.status)
        assertEquals("hunter2", got.value)   // real AES/GCM decrypt round-trip

        assertEquals(SecureStorageStatus.OK, bridge.secureDeleteCore(plainKey).status)
        assertEquals(SecureStorageStatus.NOT_FOUND, bridge.secureGetCore(plainKey).status)
    }

    // ── THE OS-KEY BINDING: a plain get of an auth item AuthFails ────────────

    @Test
    fun a_plain_get_of_an_auth_bound_item_authfails_the_os_key_binding() {
        // Provision an auth-bound key (setUserAuthenticationRequired(true)) + a stored
        // blob. A plain get cannot satisfy the key's fresh-auth requirement, so the OS
        // would refuse the decrypt — the shell reads that from the key's own KeyInfo and
        // returns AuthFailed. MUTATION (drop setUserAuthenticationRequired in
        // provisionKey): the key is no longer auth-bound → the plain get proceeds to
        // decrypt the dummy blob → the GCM tag fails → Error, never AuthFailed → this reds.
        assertTrue("could not provision the auth-bound key (is a secure lock screen set?)",
            bridge.writeAuthBoundSecretForTest(authKey))

        val outcome = bridge.secureGetCore(authKey)
        assertEquals(SecureStorageStatus.AUTH_FAILED, outcome.status)
        assertNull("an AuthFailed get carries no value", outcome.value)
    }

    // ── biometric `check` returns a STATUS as DATA (never a throw) ───────────

    @Test
    fun biometric_check_reports_a_status_as_data() {
        val status = bridge.canAuthenticateStatus()
        // A valid BiometricStatus VALUE — Authenticated when a biometric is enrolled
        // (owner phone), Unavailable on the bare emulator (none enrolled). Either way it
        // is DATA in range, never a thrown exception.
        assertTrue("check must return a BiometricStatus value (0..5), got $status",
            status in BiometricStatus.AUTHENTICATED..BiometricStatus.ERROR)
    }
}

/** The prompt half — biometrics + secure storage driven through BnSecureDemo (the real
 * .NET round-trip), with the real BiometricPrompt sheet bypassed via
 * [AndroidShellBridge.biometricGateHook] (the geolocation/notifications split). */
@RunWith(AndroidJUnit4::class)
class BnSecureAndroidTest {

    @Before
    fun reset() {
        AndroidShellBridge.biometricGateHook = null
    }

    @After
    fun cleanup() {
        AndroidShellBridge.biometricGateHook = null
    }

    // ── The biometric prompt resolves to Authenticated (seam-driven success) ──

    @Test
    fun authenticate_via_the_prompt_seam_echoes_authenticated() {
        // The seam captures the pending auth INSTEAD of the real system sheet, then
        // succeeds — the shell completes Authenticated and the demo echoes it AS DATA.
        AndroidShellBridge.biometricGateHook = { gate -> gate.succeed() }

        SecureHarness.launchDemo().use { scenario ->
            assertNotNull("BnSecureDemo never rendered within 60s", SecureHarness.pollForProbe(scenario))

            SecureHarness.tapButton(scenario, "Authenticate")
            assertTrue(
                "Authenticate never echoed Authenticated within 10s",
                SecureHarness.pollTrue(10_000) {
                    SecureHarness.echoTextOn(scenario) == "status:Authenticated"
                })
        }
    }

    // ── Denial is DATA, within a bounded await — NO HANG (the milestone law) ──

    @Test
    fun authenticate_denied_is_data_within_a_bounded_await_no_hang() {
        // The seam drives a Cancelled denial. If the deny path threw or dropped the
        // completion, the echo would stay blank forever — a HANG. It resolves to DATA.
        AndroidShellBridge.biometricGateHook = { gate -> gate.deny(BiometricStatus.CANCELLED) }

        SecureHarness.launchDemo().use { scenario ->
            assertNotNull("BnSecureDemo never rendered within 60s", SecureHarness.pollForProbe(scenario))

            SecureHarness.tapButton(scenario, "Authenticate")
            assertTrue(
                "a cancelled auth never reached the echo within 10s (a HANG — denial was not data)",
                SecureHarness.pollTrue(10_000) {
                    SecureHarness.echoTextOn(scenario) == "status:Cancelled"
                })
        }
    }

    // ── getWithAuth of an absent secret is NotFound (no hang, no prompt) ──────

    @Test
    fun unlock_of_an_absent_secret_is_not_found_no_hang() {
        // Ensure nothing is stored under the demo's key, then Unlock (getWithAuth) → the
        // shell finds no blob and completes NotFound BEFORE any prompt — DATA, no hang.
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        AndroidShellBridge(ctx) { _, _ -> }.secureDeleteCore(BnSecureKeys.DEMO_KEY)

        SecureHarness.launchDemo().use { scenario ->
            assertNotNull("BnSecureDemo never rendered within 60s", SecureHarness.pollForProbe(scenario))

            SecureHarness.tapButton(scenario, "Unlock")
            assertTrue(
                "Unlock of an absent secret never echoed NotFound within 10s",
                SecureHarness.pollTrue(10_000) {
                    SecureHarness.echoTextOn(scenario) == "status:NotFound"
                })
        }
    }

    // ── getWithAuth of an auth item passes a CryptoObject; denial is AuthFailed ──

    @Test
    fun unlock_of_an_auth_bound_secret_passes_a_cryptoobject_to_the_prompt_and_denial_is_authfailed() {
        // Provision the demo's key as AUTH-BOUND (the keystore + secure prefs are
        // app-global, so the demo's OWN bridge finds it), then Unlock (getWithAuth) → the
        // shell inits the decrypt Cipher and hands the prompt a CryptoObject wrapping it.
        // The seam asserts the CryptoObject IS present — THE OS-KEY BINDING on the read
        // side (MUTATION: pass crypto=null to the getWithAuth prompt → hasCryptoObject
        // false → this reds) — and drives a denial → AuthFailed, DATA, no hang. (The REAL
        // OS-unlocked decrypt behind a real fingerprint is owner-phone territory.)
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        SecureHarness.setPin()
        val seeder = AndroidShellBridge(ctx) { _, _ -> }
        try {
            assertTrue("could not provision the demo's auth-bound key",
                seeder.writeAuthBoundSecretForTest(BnSecureKeys.DEMO_KEY))

            val gateHadCrypto = AtomicReference<Boolean?>(null)
            AndroidShellBridge.biometricGateHook = { gate ->
                gateHadCrypto.set(gate.hasCryptoObject)
                gate.deny(SecureStorageStatus.AUTH_FAILED)
            }

            SecureHarness.launchDemo().use { scenario ->
                assertNotNull("BnSecureDemo never rendered within 60s", SecureHarness.pollForProbe(scenario))

                SecureHarness.tapButton(scenario, "Unlock")
                assertTrue(
                    "getWithAuth of an auth item never echoed AuthFailed within 10s (a HANG?)",
                    SecureHarness.pollTrue(10_000) {
                        SecureHarness.echoTextOn(scenario) == "status:AuthFailed"
                    })
                assertEquals(
                    "getWithAuth of an auth-bound item MUST pass a CryptoObject to the prompt",
                    true, gateHadCrypto.get())
            }
        } finally {
            seeder.secureDeleteCore(BnSecureKeys.DEMO_KEY)
            SecureHarness.clearPin()
        }
    }

    // ── delete echoes Ok (idempotent, permission-free) ───────────────────────

    @Test
    fun delete_echoes_ok() {
        SecureHarness.launchDemo().use { scenario ->
            assertNotNull("BnSecureDemo never rendered within 60s", SecureHarness.pollForProbe(scenario))

            SecureHarness.tapButton(scenario, "Delete")
            assertTrue(
                "Delete never echoed Ok within 10s",
                SecureHarness.pollTrue(10_000) { SecureHarness.echoTextOn(scenario) == "status:Ok" })
        }
    }
}

/** The demo's key, mirrored from BnSecureDemo.Key ("demo-secret"). */
private object BnSecureKeys {
    const val DEMO_KEY = "demo-secret"
}

/** Shared launch/poll/tap harness (the NotifHarness house style) + the lock-screen PIN
 * helpers the keystore tests need. */
private object SecureHarness {

    fun launchDemo(): ActivityScenario<MainActivity> {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnSecureDemo")
        return ActivityScenario.launch(intent)
    }

    /** The demo's root div: widget_root's single child once mounted. */
    private fun probeRoot(act: MainActivity): ViewGroup? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? ViewGroup

    /** The echo TextView: the root's child that is a TextView but NOT a Button. */
    private fun echoText(act: MainActivity): TextView? {
        val root = probeRoot(act) ?: return null
        for (i in 0 until root.childCount) {
            val child = root.getChildAt(i)
            if (child is TextView && child !is Button) return child
        }
        return null
    }

    fun echoTextOn(scenario: ActivityScenario<MainActivity>): String? {
        val out = AtomicReference<String?>(null)
        scenario.onActivity { act -> out.set(echoText(act)?.text?.toString()) }
        return out.get()
    }

    /** Polls until the demo's mount shape is on screen (4 buttons + echo). */
    fun pollForProbe(scenario: ActivityScenario<MainActivity>, deadlineMs: Long = 60_000): View? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<View?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = probeRoot(act)
                if (root != null && root.childCount >= 5 && echoText(act) != null) found.set(root)
            }
            if (found.get() != null) break
            Thread.sleep(250)
        }
        return found.get()
    }

    fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            val root = act.findViewById<FrameLayout>(R.id.widget_root)
            val button = root?.let { firstMatch(it) { v -> v is Button && v.text.toString() == label } } as? Button
            if (button != null) { button.performClick(); clicked.set(true) }
        }
        assertTrue("Button '$label' not found on screen", clicked.get())
    }

    fun pollTrue(deadlineMs: Long, predicate: () -> Boolean): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            if (predicate()) return true
            Thread.sleep(200)
        }
        return predicate()
    }

    /** Sets a lock-screen PIN so an auth-bound keystore key can be provisioned (an AES
     * user-auth key requires a secure lock screen). Best-effort + idempotent: a PIN may
     * already be set (local AVD); we only need isDeviceSecure true afterwards. */
    fun setPin() {
        runShell("locksettings set-pin 0000")
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val km = ctx.getSystemService(Context.KEYGUARD_SERVICE) as KeyguardManager
        assertTrue(
            "no secure lock screen — an auth-bound keystore key cannot be provisioned",
            km.isDeviceSecure)
    }

    fun clearPin() = runShell("locksettings clear --old 0000")

    private fun runShell(cmd: String) {
        val pfd = InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand(cmd)
        java.io.FileInputStream(pfd.fileDescriptor).use { it.readBytes() }
        pfd.close()
    }

    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) {
            for (i in 0 until view.childCount) firstMatch(view.getChildAt(i), predicate)?.let { return it }
        }
        return null
    }
}
