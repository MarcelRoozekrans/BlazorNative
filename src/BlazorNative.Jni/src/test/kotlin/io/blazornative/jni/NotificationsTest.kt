package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 9.1 Gate 2 — the notifications op + the "navigate" tap-through wire driven
 * through the published NativeAOT dll (the Kotlin twin of BnNotificationsDemoTests.cs
 * (.NET, DevHostBridge drives the five statuses) and the on-device
 * BnNotificationsAndroidTest (the AVD)). Three JVM proofs:
 *
 *   (1) WIRE ENUMS — HostCallOp.NOTIFICATIONS and NotificationStatus mirror the
 *       .NET contract byte-for-byte (op == 1; the five status values 0..4). These
 *       integers ARE the ABI — a reorder here silently corrupts every completion.
 *   (2) op=Notifications ROUND-TRIP — mount BnNotificationsDemo, tap "Show"; the dll
 *       emits hostCallBegin(op:Notifications, {"action":"show",…}); a mock host
 *       completes with a NotificationStatus and the demo echoes it AS DATA. Both a
 *       Granted and a Denied status round-trip — denial is a value, never a hang.
 *   (3) the "navigate" HOST-EVENT round-trip — dispatchHostEvent("navigate","/settings")
 *       re-routes the LIVE session to BnSettingsPage (rc 0). This is the warm
 *       tap-through wire the Android shell's onNewIntent drives; the mapping lives in
 *       .NET (Exports.DispatchHostEventCore), so this JVM path and the AVD share it.
 *
 * Node identification is ALWAYS structural / by text (ids are process-global
 * counters) — the HostEventTest/NavigationTest convention. Safe alongside the other
 * native tests in one JVM process (idempotent init, last-wins registration).
 */
class NotificationsTest {

    /** Inert host whose hostCallBegin completes op=Notifications with a configurable
     * status (synchronously — legal per the ShellBridgeHandlers contract), recording
     * the (op, argsJson) it saw so the round-trip can assert the wire shape. */
    private class NotifHost(private val status: Int) : ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        val calls = mutableListOf<Pair<Int, String>>()
        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "NotificationsTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
        override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
            calls.add(op to argsJson)
            BridgeHostCallCompleter.complete(requestId, status, null)
        }
    }

    private class Session(val runtime: BlazorNativeRuntime, val frames: MutableList<RenderFrame>, val host: NotifHost)

    private fun boot(componentName: String, status: Int): Session {
        val frames = mutableListOf<RenderFrame>()
        val host = NotifHost(status)
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = componentName, platformOs = "test-host", bridge = host)
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return Session(runtime, frames, host)
    }

    // ── (1) the wire enums mirror .NET byte-for-byte ─────────────────────────

    @Test
    fun wire_enums_mirror_the_dotnet_contract() {
        // op-enum: Geolocation = 0 (9.0), Notifications = 1 (9.1's one added value).
        assertEquals(0, HostCallOp.GEOLOCATION)
        assertEquals(1, HostCallOp.NOTIFICATIONS)
        // NotificationStatus: geolocation's shape minus LocationUnavailable, so
        // Error is 4 here (not HostCallStatus's 5).
        assertEquals(0, NotificationStatus.GRANTED)
        assertEquals(1, NotificationStatus.DENIED)
        assertEquals(2, NotificationStatus.DENIED_PERMANENTLY)
        assertEquals(3, NotificationStatus.RESTRICTED)
        assertEquals(4, NotificationStatus.ERROR)
    }

    // ── (2) op=Notifications round-trips, and denial is DATA ─────────────────

    @Test
    fun show_round_trips_op_notifications_and_echoes_granted() {
        val s = boot("BnNotificationsDemo", NotificationStatus.GRANTED)
        val mount = s.frames.first()
        val echo = echoTextNode(mount)
        // The demo mounts showing its arrival marker (the tap-through landing proof).
        replaceTextOn(listOf(mount), echo, "arrived:/notifications")

        val showHandler = clickHandlerOn(mount, containerOfText(mount, "Show"))
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(showHandler, "click"))

        // The dll emitted exactly one host call: op=Notifications, action=show.
        assertEquals(1, s.host.calls.size, "Show must make exactly one host call")
        assertEquals(HostCallOp.NOTIFICATIONS, s.host.calls.single().first)
        assertTrue(
            s.host.calls.single().second.contains("\"action\":\"show\""),
            "the args JSON must carry action:show; got ${s.host.calls.single().second}")

        // The returned status re-renders on the echo node AS DATA.
        replaceTextOn(s.frames.subList(before, s.frames.size).toList(), echo, "status:Granted")
        s.runtime.retire()
    }

    @Test
    fun denied_show_echoes_the_denial_as_data_no_hang() {
        // A non-Granted status is a VALUE the demo shows — never a thrown exception
        // across the boundary, never a dropped completion. If the deny path threw or
        // hung, dispatchEventBlocking would fault (rc 2) or never re-render the echo.
        val s = boot("BnNotificationsDemo", NotificationStatus.DENIED)
        val mount = s.frames.first()
        val echo = echoTextNode(mount)

        val showHandler = clickHandlerOn(mount, containerOfText(mount, "Show"))
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(showHandler, "click"))

        replaceTextOn(s.frames.subList(before, s.frames.size).toList(), echo, "status:Denied")
        s.runtime.retire()
    }

    // ── (3) the "navigate" host event re-routes the live session ─────────────

    @Test
    fun navigate_host_event_reroutes_the_live_session() {
        val s = boot("BnDemo", NotificationStatus.GRANTED)
        val before = s.frames.size

        // The warm tap-through wire: host_event("navigate", route) → NavigateToAsync.
        // rc 0 = the live session re-routed; the settings page mounts inside the swap.
        assertEquals(0, s.runtime.dispatchHostEventBlocking("navigate", "/settings"))
        val window = s.frames.subList(before, s.frames.size).toList()
        assertTrue(
            window.any { hasText(it, "Settings") },
            "the navigate host event must mount the settings page")
        s.runtime.retire()
    }

    // ── Structural pin helpers (HostEventTest conventions) ───────────────────

    private fun root(mount: RenderFrame): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.parentId == null }
        ) { "expected exactly one parentless create (the root); got ${mount.patches}" }

    private fun createOf(frame: RenderFrame, nodeId: Int): RenderPatch.CreateNode =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.nodeId == nodeId }
        ) { "expected exactly one CreateNode for node $nodeId; got ${frame.patches}" }

    private fun containerOfText(frame: RenderFrame, text: String): Int {
        val t = checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.ReplaceText>().singleOrNull { it.text == text }
        ) { "expected exactly one ReplaceText '$text'; got ${frame.patches}" }
        return checkNotNull(createOf(frame, t.nodeId).parentId) { "text '$text' node must have a parent" }
    }

    private fun clickHandlerOn(frame: RenderFrame, nodeId: Int): Int =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .singleOrNull { it.nodeId == nodeId && it.eventName == "click" }
        ) { "expected exactly one click AttachEvent on node $nodeId; got ${frame.patches}" }.handlerId

    private fun hasText(frame: RenderFrame, text: String): Boolean =
        frame.patches.filterIsInstance<RenderPatch.ReplaceText>().any { it.text == text }

    /** The demo's echo BnText TEXT node, pinned at mount: root div → the span
     * (the one text-type child of the root; the three BnButtons are button-type) →
     * its single child text node (the HostEventTest structural walk). */
    private fun echoTextNode(mount: RenderFrame): Int {
        val r = root(mount)
        val span = checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == r.nodeId && it.nodeType == "text" }
        ) { "expected exactly one text-type child of the root; got ${mount.patches}" }
        return checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.parentId == span.nodeId }
        ) { "expected exactly one child of the echo span; got ${mount.patches}" }.nodeId
    }

    private fun replaceTextOn(frames: List<RenderFrame>, nodeId: Int, text: String) =
        checkNotNull(
            frames.flatMap { it.patches.filterIsInstance<RenderPatch.ReplaceText>() }
                .singleOrNull { it.nodeId == nodeId && it.text == text }
        ) { "expected a ReplaceText '$text' on node $nodeId; got ${frames.map { it.patches }}" }
}
