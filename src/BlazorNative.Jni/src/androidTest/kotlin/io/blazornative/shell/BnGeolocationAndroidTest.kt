package io.blazornative.shell

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationManager
import android.os.SystemClock
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.rule.GrantPermissionRule
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

// ─────────────────────────────────────────────────────────────────────────────
// Phase 9.0 Gate 2 (M9 DoD #1 + #2) — geolocation + the permission pattern on the
// AVD. The on-device third of BnGeolocationDemoTests.cs (.NET, DevHostBridge drives
// all six statuses headless) and the JVM struct/export drift pins (ShellBridgeTest /
// BootSmokeNativeTest). THIS proves AndroidShellBridge's REAL permission-gated flow:
//   • request → grant → LocationManager fix → host_call_complete → .NET → echo
//     ([BnGeolocationGrantedAndroidTest.locate_… round_trips_a_real_fix]);
//   • DENIAL IS DATA, no hang: a denied Locate resolves the awaiting .NET ValueTask
//     to a status the echo shows — within a bounded await, never blank/forever
//     ([BnGeolocationAndroidTest.locate_denied_…_no_hang]);
//   • RECREATION SURVIVAL (the phase's named Android risk): the OS dialog can recreate
//     the Activity mid-request; the app-scoped requestCode→requestId map survives it, so
//     the recreated Activity's fresh bridge routes the result to the SAME in-flight .NET
//     continuation (process-scoped registry) — proven by host_call_complete returning
//     rc 0 ([BnGeolocationAndroidTest.pending_…_survives_activity_recreation]).
//
// THE REAL SYSTEM DIALOG UX is owner-phone territory (the design's PROVEN/UNPROVEN
// split — a real gesture on the system permission dialog is not reliably drivable
// under instrumentation). CI drives the RESULT via onRequestPermissionsResult and
// bypasses the pop with AndroidShellBridge.permissionRequestHook; the WIRE routing
// (request→result→complete→.NET) is the on-device proof, and the fix round-trip +
// granted-check are the real end-to-end grants.
//
// Shape (BnGeolocationDemo.cs): root div → BnButton "Locate", BnButton "Check",
// BnText echo, BnText accuracy. On-screen: widget_root → ViewGroup div → [0] Button
// "Locate", [1] Button "Check", [2] TextView (the echo span, text-collapsed — the
// FocusProbe/ClipboardProbe echo contract), [3] TextView (the trailing "acc:" line,
// issue #169). The echo is still the FIRST TextView-not-Button, so its selector is
// unchanged; accuracyText picks the SECOND. Polling + node-by-text/structure is the
// ClipboardAndroidTest house style. STRICT MODE via BlazorNativeTestRunner.
// ─────────────────────────────────────────────────────────────────────────────

/** The denied/recreation half — permission NOT held, so Locate takes the prompt
 * path and the [AndroidShellBridge.permissionRequestHook] seam captures it. */
@RunWith(AndroidJUnit4::class)
class BnGeolocationAndroidTest {

    @Before
    fun reset() {
        AndroidShellBridge.resetGeolocationForTest()
        AndroidShellBridge.permissionRequestHook = null
    }

    @After
    fun cleanup() {
        AndroidShellBridge.permissionRequestHook = null
        AndroidShellBridge.resetGeolocationForTest()
    }

    // ── Denial is DATA, within a bounded await — NO HANG (the milestone law) ──

    @Test
    fun locate_denied_shows_a_denial_status_within_a_bounded_await_no_hang() {
        val code = AtomicInteger(-1)
        val perms = AtomicReference<Array<String>?>(null)
        AndroidShellBridge.permissionRequestHook = { requestCode, permissions ->
            perms.set(permissions.copyOf()); code.set(requestCode)
        }

        GeoHarness.launch().use { scenario ->
            assertNotNull("BnGeolocationDemo never rendered within 60s", GeoHarness.pollForProbe(scenario))

            // Tap Locate → not held → the prompt path → the hook captures the request
            // (the real system dialog is bypassed — owner-phone territory).
            GeoHarness.tapButton(scenario, "Locate")
            assertTrue(
                "Locate never reached the permission-request path within 10s",
                GeoHarness.pollTrue(10_000) { code.get() >= 0 }
            )

            // Simulate the user DENYING — routed through the real
            // MainActivity.onRequestPermissionsResult → the bridge → host_call_complete.
            GeoHarness.deliverResult(scenario, code.get(), perms.get()!!, granted = false)

            // The awaiting .NET ValueTask resolves to a denial the echo SHOWS — within a
            // bounded await. A HANG (denial as a thrown/dropped completion) times this out
            // and reddens. (Denied vs DeniedPermanently depends on the OS rationale state,
            // which the seam leaves at never-asked ⇒ DeniedPermanently; both start
            // "status:Denied" and both prove denial-as-data.)
            assertTrue(
                "denial never reached the echo within 10s (a HANG — denial was not data)",
                GeoHarness.pollTrue(10_000) { GeoHarness.echoTextOn(scenario)?.startsWith("status:Denied") == true }
            )
        }
    }

    // ── Recreation survival: the app-scoped map + the process-scoped .NET registry ──

    @Test
    fun pending_permission_request_survives_activity_recreation() {
        val code = AtomicInteger(-1)
        val perms = AtomicReference<Array<String>?>(null)
        AndroidShellBridge.permissionRequestHook = { requestCode, permissions ->
            perms.set(permissions.copyOf()); code.set(requestCode)
        }

        GeoHarness.launch().use { scenario ->
            assertNotNull("BnGeolocationDemo never rendered within 60s", GeoHarness.pollForProbe(scenario))

            GeoHarness.tapButton(scenario, "Locate")
            assertTrue(
                "Locate never reached the permission-request path within 10s",
                GeoHarness.pollTrue(10_000) { code.get() >= 0 }
            )
            val requestCode = code.get()

            // The app-scoped requestCode→requestId map holds the in-flight request.
            assertTrue(
                "the request was not registered in the app-scoped map",
                AndroidShellBridge.hasPendingPermissionRequestForTest(requestCode)
            )

            // Recreate the Activity WHILE the request is airborne — exactly what the OS
            // permission dialog can do. A fresh MainActivity + a fresh AndroidShellBridge
            // are built; the map is STATIC so the entry survives.
            scenario.recreate()
            assertNotNull("re-mount after recreation never rendered", GeoHarness.pollForProbe(scenario))
            assertTrue(
                "the pending request was LOST across Activity recreation (map not app-scoped)",
                AndroidShellBridge.hasPendingPermissionRequestForTest(requestCode)
            )

            // The recreated Activity's fresh bridge routes the OS result to the surviving
            // requestId; the .NET registry (process-scoped) never noticed the recreation,
            // so host_call_complete finds the in-flight continuation → rc 0 (delivered).
            GeoHarness.deliverResult(scenario, requestCode, perms.get()!!, granted = false)
            assertTrue(
                "the completion did not reach a live .NET continuation after recreation " +
                    "(the process-scoped registry lost the id, or the map mis-routed)",
                GeoHarness.pollTrue(10_000) { AndroidShellBridge.lastHostCallCompleteRcForTest == 0 }
            )
            // …and the surviving entry was consumed by the routed result.
            assertFalse(
                "the pending entry was not consumed by the recreated Activity's result",
                AndroidShellBridge.hasPendingPermissionRequestForTest(requestCode)
            )
        }
    }
}

/** The granted half — permission held (GrantPermissionRule), so Locate fetches a
 * real fix directly and Check reports Granted, both without a dialog. */
@RunWith(AndroidJUnit4::class)
class BnGeolocationGrantedAndroidTest {

    @get:Rule
    val grant: GrantPermissionRule = GrantPermissionRule.grant(
        android.Manifest.permission.ACCESS_FINE_LOCATION,
        android.Manifest.permission.ACCESS_COARSE_LOCATION,
    )

    @Before
    fun reset() {
        AndroidShellBridge.resetGeolocationForTest()
        AndroidShellBridge.permissionRequestHook = null
    }

    @After
    fun cleanup() {
        AndroidShellBridge.resetGeolocationForTest()
        removeMockProvider()
    }

    // ── A real position round-trips: request → grant → fix → .NET → echo ──────

    @Test
    fun locate_with_permission_granted_round_trips_a_real_fix() {
        // Seed a fix into the platform LocationManager the bridge reads (the mock test
        // provider is the CI-deterministic equivalent of `adb emu geo fix <lon> <lat>`,
        // which feeds the same getLastKnownLocation(GPS) the bridge queries).
        injectMockFix(LAT, LNG)

        GeoHarness.launch().use { scenario ->
            assertNotNull("BnGeolocationDemo never rendered within 60s", GeoHarness.pollForProbe(scenario))

            // Permission is HELD → Locate fetches the fix directly (no dialog) and the
            // demo echoes "fix:{lat},{lng}" — a real Granted position, end to end.
            GeoHarness.tapButton(scenario, "Locate")
            assertTrue(
                "a Granted fix never round-tripped to the echo within 15s",
                GeoHarness.pollTrue(15_000) { GeoHarness.echoTextOn(scenario)?.startsWith("fix:") == true }
            )

            val echo = GeoHarness.echoTextOn(scenario)!!
            val coords = echo.removePrefix("fix:").split(",")
            assertEquals("echo must carry lat,lng: '$echo'", 2, coords.size)
            assertEquals("latitude round-trip", LAT, coords[0].toDouble(), 1e-4)
            assertEquals("longitude round-trip", LNG, coords[1].toDouble(), 1e-4)

            // Issue #169: accuracy surfaces on its OWN trailing line ("acc:<metres>") — the
            // echo shape above is UNCHANGED (still exactly lat,lng). This proves the
            // round-tripped Accuracy is observable on-device (M11 DoD #2 records the app's
            // own value, not just `dumpsys location`). The mock fix seeds accuracy = 5m.
            val accuracy = GeoHarness.accuracyTextOn(scenario)!!
            assertTrue("accuracy line must carry 'acc:<metres>': '$accuracy'", accuracy.startsWith("acc:"))
            assertEquals("accuracy round-trip (mock fix is 5m)", 5.0, accuracy.removePrefix("acc:").toDouble(), 0.5)
        }
    }

    // ── The read-only Check reports Granted without prompting ────────────────

    @Test
    fun check_permission_reports_granted_without_prompting() {
        GeoHarness.launch().use { scenario ->
            assertNotNull("BnGeolocationDemo never rendered within 60s", GeoHarness.pollForProbe(scenario))

            GeoHarness.tapButton(scenario, "Check")
            assertTrue(
                "Check never echoed the held permission within 10s",
                GeoHarness.pollTrue(10_000) { GeoHarness.echoTextOn(scenario) == "status:Granted" }
            )
            // No permission request was ever raised (Check never prompts).
            assertEquals(0, AndroidShellBridge.pendingPermissionRequestCountForTest())
        }
    }

    // ── Mock-fix injection (the self-contained CI equivalent of adb emu geo fix) ──

    private fun injectMockFix(lat: Double, lng: Double) {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        // The app must be the mock-location app to feed a test provider — granted via
        // appops from the instrumentation's shell identity (same UID as the app process).
        runShell("appops set ${ctx.packageName} android:mock_location allow")
        val lm = ctx.getSystemService(Context.LOCATION_SERVICE) as LocationManager
        runCatching { lm.removeTestProvider(LocationManager.GPS_PROVIDER) }
        @Suppress("DEPRECATION")
        lm.addTestProvider(
            LocationManager.GPS_PROVIDER,
            /* requiresNetwork = */ false, /* requiresSatellite = */ false, /* requiresCell = */ false,
            /* hasMonetaryCost = */ false, /* supportsAltitude = */ true,
            /* supportsSpeed = */ true, /* supportsBearing = */ true,
            android.location.Criteria.POWER_LOW, android.location.Criteria.ACCURACY_FINE,
        )
        lm.setTestProviderEnabled(LocationManager.GPS_PROVIDER, true)
        val loc = Location(LocationManager.GPS_PROVIDER).apply {
            latitude = lat; longitude = lng; accuracy = 5f; altitude = 3.0
            time = System.currentTimeMillis()
            elapsedRealtimeNanos = SystemClock.elapsedRealtimeNanos()
        }
        lm.setTestProviderLocation(LocationManager.GPS_PROVIDER, loc)
    }

    private fun removeMockProvider() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val lm = ctx.getSystemService(Context.LOCATION_SERVICE) as LocationManager
        runCatching { lm.removeTestProvider(LocationManager.GPS_PROVIDER) }
    }

    private fun runShell(cmd: String) {
        val pfd = InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand(cmd)
        // Drain + close so the command completes and the fd does not leak.
        java.io.FileInputStream(pfd.fileDescriptor).use { it.readBytes() }
        pfd.close()
    }

    private companion object {
        /** Distinctive coordinates (Amsterdam) that round-trip exactly through the
         * flat-JSON wire (invariant Double.toString ↔ .NET InvariantCulture parse). */
        const val LAT = 52.3702
        const val LNG = 4.8952
    }
}

/** Shared launch/poll/tap harness (the ClipboardAndroidTest house style), factored
 * out so both geolocation test classes share one set of structural pins. */
private object GeoHarness {

    fun launch(): ActivityScenario<MainActivity> {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnGeolocationDemo")
        return ActivityScenario.launch(intent)
    }

    /** The demo's root div: widget_root's single child once mounted. */
    private fun probeRoot(act: MainActivity): ViewGroup? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? ViewGroup

    /** The echo TextView: the root's single child that is a TextView but NOT a Button
     * (Button extends TextView — the two buttons must not match). */
    private fun echoText(act: MainActivity): TextView? {
        val root = probeRoot(act) ?: return null
        for (i in 0 until root.childCount) {
            val child = root.getChildAt(i)
            if (child is TextView && child !is Button) return child
        }
        return null
    }

    /** The current echo string, read on the UI thread (null until mounted). */
    fun echoTextOn(scenario: ActivityScenario<MainActivity>): String? {
        val out = AtomicReference<String?>(null)
        scenario.onActivity { act -> out.set(echoText(act)?.text?.toString()) }
        return out.get()
    }

    /** The accuracy TextView (issue #169): the SECOND root child that is a TextView but NOT
     * a Button — the trailing "acc:" line (the echo is the first such child). */
    private fun accuracyText(act: MainActivity): TextView? {
        val root = probeRoot(act) ?: return null
        var seen = 0
        for (i in 0 until root.childCount) {
            val child = root.getChildAt(i)
            if (child is TextView && child !is Button && ++seen == 2) return child
        }
        return null
    }

    /** The current accuracy-line string, read on the UI thread (null until mounted). */
    fun accuracyTextOn(scenario: ActivityScenario<MainActivity>): String? {
        val out = AtomicReference<String?>(null)
        scenario.onActivity { act -> out.set(accuracyText(act)?.text?.toString()) }
        return out.get()
    }

    /** Polls until the demo's mount shape is on screen (2 buttons + echo). */
    fun pollForProbe(scenario: ActivityScenario<MainActivity>, deadlineMs: Long = 60_000): View? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<View?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = probeRoot(act)
                if (root != null && root.childCount >= 3 && echoText(act) != null) found.set(root)
            }
            if (found.get() != null) break
            Thread.sleep(250)
        }
        return found.get()
    }

    /** Finds the Button whose text equals [label] and performClicks it on the UI thread. */
    fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            val root = act.findViewById<FrameLayout>(R.id.widget_root)
            val button = root?.let { firstMatch(it) { v -> v is Button && v.text.toString() == label } } as? Button
            if (button != null) { button.performClick(); clicked.set(true) }
        }
        assertTrue("Button '$label' not found on screen", clicked.get())
    }

    /** Delivers a permission result through the REAL MainActivity.onRequestPermissionsResult
     * (the recreated Activity's, when recreated) so the whole shell wiring is exercised. */
    fun deliverResult(
        scenario: ActivityScenario<MainActivity>,
        requestCode: Int,
        permissions: Array<String>,
        granted: Boolean,
    ) {
        val result = if (granted) PackageManager.PERMISSION_GRANTED else PackageManager.PERMISSION_DENIED
        val grantResults = IntArray(permissions.size) { result }
        scenario.onActivity { act -> act.onRequestPermissionsResult(requestCode, permissions, grantResults) }
    }

    /** Polls a device-independent predicate (static seam reads) to a bounded deadline. */
    fun pollTrue(deadlineMs: Long, predicate: () -> Boolean): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            if (predicate()) return true
            Thread.sleep(200)
        }
        return predicate()
    }

    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) {
            for (i in 0 until view.childCount) firstMatch(view.getChildAt(i), predicate)?.let { return it }
        }
        return null
    }
}
