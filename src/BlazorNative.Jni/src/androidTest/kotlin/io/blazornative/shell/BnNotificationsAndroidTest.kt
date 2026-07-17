package io.blazornative.shell

import android.app.Notification
import android.app.NotificationManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
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
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

// ─────────────────────────────────────────────────────────────────────────────
// Phase 9.1 Gate 2 (M9 DoD #3) — local notifications + permission + tap-through on
// the AVD. The on-device third of BnNotificationsDemoTests.cs (.NET, DevHostBridge
// drives all five statuses headless) and NotificationsTest.kt (JVM, through the dll).
// THIS proves AndroidShellBridge's REAL flow:
//   • show → real NotificationManager.notify on the "blazornative_default" channel,
//     asserted via NotificationManager.activeNotifications (title + body), then cancel
//     removes it ([BnNotificationsGrantedAndroidTest]);
//   • DENIAL IS DATA, no hang: a denied Show resolves the awaiting .NET ValueTask to a
//     status the echo shows — within a bounded await, never blank/forever;
//   • TAP-THROUGH, BOTH halves:
//       – COLD (app killed): the notification's content PendingIntent is the 5.1 launch
//         deep link; launching that exact Intent relaunches into onCreate → the
//         /notifications page mounts (its "arrived:/notifications" marker);
//       – WARM (app alive, singleTop): onNewIntent → host_event("navigate","/notifications")
//         → .NET NavigateToAsync re-routes the LIVE session (rc 0) and the page mounts.
//
// THE REAL SYSTEM PERMISSION-DIALOG UX + the real shade tap are owner-phone territory
// (the design's PROVEN/UNPROVEN split). CI drives the RESULT through
// onRequestPermissionsResult and bypasses the pop with permissionRequestHook; a real
// notification POSTS and a real deep-link Intent ROUTES are the on-device proofs.
//
// Shape (BnNotificationsDemo.cs): root div → BnButton "Show"/"Schedule"/"Cancel" +
// BnText echo. On-screen: widget_root → ViewGroup div → three Buttons + one TextView
// (the echo, non-Button). The mount echo is "arrived:/notifications" (the tap-through
// landing marker); each op echoes "status:<NotificationStatus>". STRICT MODE via
// BlazorNativeTestRunner; polling + node-by-text is the ClipboardAndroidTest house style.
// ─────────────────────────────────────────────────────────────────────────────

/** The denial + tap-through half — POST_NOTIFICATIONS NOT held (revoked in @Before),
 * so Show takes the prompt path and the permissionRequestHook seam captures it; the
 * tap-through tests post nothing and need no permission. */
@RunWith(AndroidJUnit4::class)
class BnNotificationsAndroidTest {

    @Before
    fun reset() {
        AndroidShellBridge.resetGeolocationForTest()
        AndroidShellBridge.permissionRequestHook = null
        MainActivity.resetNavigateRcForTest()
        // Guarantee the denied prompt path even if a prior granted test in this run
        // left POST_NOTIFICATIONS granted (grants are app-wide, not per-test).
        NotifHarness.revokePostNotifications()
    }

    @After
    fun cleanup() {
        AndroidShellBridge.permissionRequestHook = null
        AndroidShellBridge.resetGeolocationForTest()
        MainActivity.resetNavigateRcForTest()
        NotifHarness.cancelAll()
    }

    // ── Denial is DATA, within a bounded await — NO HANG (the milestone law) ──

    @Test
    fun show_denied_shows_a_denial_status_within_a_bounded_await_no_hang() {
        val code = AtomicInteger(-1)
        val perms = AtomicReference<Array<String>?>(null)
        AndroidShellBridge.permissionRequestHook = { requestCode, permissions ->
            perms.set(permissions.copyOf()); code.set(requestCode)
        }

        NotifHarness.launchDemo().use { scenario ->
            assertNotNull("BnNotificationsDemo never rendered within 60s", NotifHarness.pollForProbe(scenario))

            // Tap Show → not held → the prompt path → the hook captures the request
            // (the real system dialog is bypassed — owner-phone territory).
            NotifHarness.tapButton(scenario, "Show")
            assertTrue(
                "Show never reached the permission-request path within 10s",
                NotifHarness.pollTrue(10_000) { code.get() >= 0 }
            )

            // Simulate the user DENYING — routed through the real
            // MainActivity.onRequestPermissionsResult → the bridge → host_call_complete.
            NotifHarness.deliverResult(scenario, code.get(), perms.get()!!, granted = false)

            // The awaiting .NET ValueTask resolves to a denial the echo SHOWS, within a
            // bounded await. A HANG (denial as a thrown/dropped completion) times out.
            assertTrue(
                "denial never reached the echo within 10s (a HANG — denial was not data)",
                NotifHarness.pollTrue(10_000) {
                    NotifHarness.echoTextOn(scenario)?.startsWith("status:Denied") == true
                }
            )
        }
    }

    // ── COLD tap-through: the notification's launch Intent mounts /notifications ──

    @Test
    fun cold_tap_intent_routes_to_the_notifications_page() {
        // The EXACT Intent the shown notification's content PendingIntent wraps — the
        // 5.1 launch deep link. Launching it is a cold tap: onCreate parses the route
        // and mounts BnNotificationsDemo by name. Drop the data (the mutation) and the
        // tap lands on the default page instead — this reds.
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val tap = AndroidShellBridge.buildTapIntent(ctx, "/notifications")
        ActivityScenario.launch<MainActivity>(tap).use { scenario ->
            assertTrue(
                "the cold tap Intent never landed on the /notifications page within 60s",
                NotifHarness.pollTrue(60_000) {
                    NotifHarness.echoTextOn(scenario) == "arrived:/notifications"
                }
            )
        }
    }

    // ── WARM tap-through: onNewIntent → host_event("navigate") → live re-route ──

    @Test
    fun warm_tap_reroutes_the_live_session_via_navigate_host_event() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        // Launch with the deep-link VIEW Intent so ActivityScenario's activity-identity
        // match (getIntent().filterEquals(launchIntent), which IGNORES extras + flags)
        // SURVIVES onNewIntent's production setIntent — otherwise the scenario stops
        // tracking lifecycle and close() cannot see DESTROYED. EXTRA_COMPONENT forces
        // the INITIAL mount to BnDemo (it wins onCreate's precedence) so the warm
        // re-route to /notifications is an observable CHANGE, not a no-op.
        val launch = AndroidShellBridge.buildTapIntent(ctx, "/notifications")
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnDemo")
        ActivityScenario.launch<MainActivity>(launch).use { scenario ->
            assertTrue(
                "BnDemo never booted within 60s",
                NotifHarness.pollTrue(60_000) { NotifHarness.widgetRootChild(scenario) != null }
            )
            // Not on the /notifications page yet (started on BnDemo).
            assertTrue(
                "must start on BnDemo, not already on the /notifications page",
                NotifHarness.echoTextOn(scenario) != "arrived:/notifications"
            )

            // A warm tap delivers the deep-link Intent to onNewIntent (singleTop).
            val tap = AndroidShellBridge.buildTapIntent(ctx, "/notifications")
            scenario.onActivity { act -> act.onNewIntent(tap) }

            // The reserved "navigate" host event re-routes the LIVE session: rc 0 =
            // the .NET continuation navigated (proves warm tap-through over host_event).
            assertTrue(
                "the warm 'navigate' host event never returned rc 0 within 10s",
                NotifHarness.pollTrue(10_000) { MainActivity.lastNavigateHostEventRcForTest == 0 }
            )
            // …and the live session actually landed on the /notifications page.
            assertTrue(
                "the warm re-route never mounted the /notifications page within 10s",
                NotifHarness.pollTrue(10_000) {
                    NotifHarness.echoTextOn(scenario) == "arrived:/notifications"
                }
            )
        }
    }
}

/** The granted half — POST_NOTIFICATIONS held, so Show posts a real notification with
 * no dialog and Cancel removes it. */
@RunWith(AndroidJUnit4::class)
class BnNotificationsGrantedAndroidTest {

    @get:Rule
    val grant: GrantPermissionRule =
        GrantPermissionRule.grant("android.permission.POST_NOTIFICATIONS")

    @Before
    fun reset() {
        AndroidShellBridge.resetGeolocationForTest()
        AndroidShellBridge.permissionRequestHook = null
        NotifHarness.cancelAll()
    }

    @After
    fun cleanup() {
        AndroidShellBridge.resetGeolocationForTest()
        NotifHarness.cancelAll()
    }

    // ── A real notification POSTS on the channel, then CANCEL removes it ──────

    @Test
    fun show_posts_a_real_notification_and_cancel_removes_it() {
        NotifHarness.launchDemo().use { scenario ->
            assertNotNull("BnNotificationsDemo never rendered within 60s", NotifHarness.pollForProbe(scenario))

            // Permission HELD → Show posts directly (no dialog) and echoes Granted.
            NotifHarness.tapButton(scenario, "Show")
            assertTrue(
                "Show never echoed Granted within 10s",
                NotifHarness.pollTrue(10_000) { NotifHarness.echoTextOn(scenario) == "status:Granted" }
            )

            // The REAL notification is in the system now — id 7, title "Hello", body
            // "A local notification" (BnNotificationsDemo.ShowAsync), asserted via
            // NotificationManager.activeNotifications.
            assertTrue(
                "the notification never appeared in activeNotifications within 10s",
                NotifHarness.pollTrue(10_000) {
                    NotifHarness.activePost(7)?.let { sbn ->
                        val extras = sbn.notification.extras
                        extras.getString(Notification.EXTRA_TITLE) == "Hello" &&
                            extras.getString(Notification.EXTRA_TEXT) == "A local notification"
                    } == true
                }
            )

            // Cancel removes it — the same id, gone from the system.
            NotifHarness.tapButton(scenario, "Cancel")
            assertTrue(
                "Cancel never echoed Granted within 10s",
                NotifHarness.pollTrue(10_000) { NotifHarness.echoTextOn(scenario) == "status:Granted" }
            )
            assertTrue(
                "the notification was still posted 10s after Cancel",
                NotifHarness.pollTrue(10_000) { NotifHarness.activePost(7) == null }
            )
        }
    }

    // ── The already-granted fast path: no prompt is ever raised ──────────────

    @Test
    fun show_with_permission_held_never_prompts() {
        NotifHarness.launchDemo().use { scenario ->
            assertNotNull("BnNotificationsDemo never rendered within 60s", NotifHarness.pollForProbe(scenario))

            NotifHarness.tapButton(scenario, "Show")
            assertTrue(
                "Show never echoed Granted within 10s",
                NotifHarness.pollTrue(10_000) { NotifHarness.echoTextOn(scenario) == "status:Granted" }
            )
            // The fast path: permission was held, so no permission request was raised.
            assertEquals(0, AndroidShellBridge.pendingPermissionRequestCountForTest())
        }
    }
}

/** Shared launch/poll/tap harness (the GeoHarness house style). */
private object NotifHarness {

    fun launchDemo(): ActivityScenario<MainActivity> {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnNotificationsDemo")
        return ActivityScenario.launch(intent)
    }

    /** The demo's root div: widget_root's single child once mounted. */
    private fun probeRoot(act: MainActivity): ViewGroup? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? ViewGroup

    fun widgetRootChild(scenario: ActivityScenario<MainActivity>): View? {
        val out = AtomicReference<View?>(null)
        scenario.onActivity { act -> out.set(probeRoot(act)) }
        return out.get()
    }

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

    /** Polls until the demo's mount shape is on screen (3 buttons + echo). */
    fun pollForProbe(scenario: ActivityScenario<MainActivity>, deadlineMs: Long = 60_000): View? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<View?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = probeRoot(act)
                if (root != null && root.childCount >= 4 && echoText(act) != null) found.set(root)
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

    /** The posted notification with [id], or null (read on the app's own manager —
     * instrumentation runs in-process, so activeNotifications sees the shell's posts). */
    fun activePost(id: Int): android.service.notification.StatusBarNotification? {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val nm = ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        return nm.activeNotifications.firstOrNull { it.id == id }
    }

    fun cancelAll() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        (ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).cancelAll()
    }

    fun revokePostNotifications() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        runShell("pm revoke ${ctx.packageName} android.permission.POST_NOTIFICATIONS")
    }

    fun pollTrue(deadlineMs: Long, predicate: () -> Boolean): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            if (predicate()) return true
            Thread.sleep(200)
        }
        return predicate()
    }

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
