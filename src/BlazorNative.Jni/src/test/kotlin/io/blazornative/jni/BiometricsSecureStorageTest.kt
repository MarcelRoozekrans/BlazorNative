package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 9.2 Gate 2 — the biometrics + secure-storage ops driven through the published
 * NativeAOT dll (the Kotlin twin of BnSecureDemoTests.cs (.NET, DevHostBridge drives
 * every status + the pairing) and the on-device BnSecureAndroidTest (the AVD)). Three
 * JVM proofs:
 *
 *   (1) WIRE ENUMS — HostCallOp.BIOMETRICS/SECURE_STORAGE and BiometricStatus/
 *       SecureStorageStatus mirror the .NET contract byte-for-byte (ops 2 and 3; the
 *       six + five status values). These integers ARE the ABI — a reorder here
 *       silently corrupts every completion.
 *   (2) op ROUND-TRIPS — mount BnSecureDemo, tap a button; the dll emits
 *       hostCallBegin(op, {"action":…}); a mock host completes with a status (and, for
 *       a get, a {"value":…} payload) and the demo echoes it AS DATA. A denial is a
 *       value, never a hang.
 *   (3) THE {"value":…} PAYLOAD — Unlock (getWithAuth) completes Ok + {"value":"hunter2"}
 *       and the demo echoes "value:hunter2" — the payload channel host_call_complete has
 *       carried since 9.0 (geolocation's fix is the first user; secure get is the second).
 *
 * Node identification is ALWAYS structural / by text (ids are process-global counters) —
 * the NotificationsTest/HostEventTest convention. Safe alongside the other native tests
 * in one JVM process (idempotent init, last-wins registration).
 */
class BiometricsSecureStorageTest {

    /** Inert host whose hostCallBegin completes with a configurable status + optional
     * payload, recording the (op, argsJson) it saw so the round-trip can assert the
     * wire shape. */
    private class SecureHost(private val status: Int, private val payload: Map<String, String>?) :
        ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        val calls = mutableListOf<Pair<Int, String>>()
        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "BiometricsSecureStorageTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
        override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
            calls.add(op to argsJson)
            BridgeHostCallCompleter.complete(requestId, status, payload)
        }
    }

    private class Session(val runtime: BlazorNativeRuntime, val frames: MutableList<RenderFrame>, val host: SecureHost)

    private fun boot(status: Int, payload: Map<String, String>? = null): Session {
        val frames = mutableListOf<RenderFrame>()
        val host = SecureHost(status, payload)
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = "BnSecureDemo", platformOs = "test-host", bridge = host)
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return Session(runtime, frames, host)
    }

    // ── (1) the wire enums mirror .NET byte-for-byte ─────────────────────────

    @Test
    fun wire_enums_mirror_the_dotnet_contract() {
        // op-enum: Geolocation=0 (9.0), Notifications=1 (9.1), Biometrics=2 + SecureStorage=3 (9.2).
        assertEquals(0, HostCallOp.GEOLOCATION)
        assertEquals(1, HostCallOp.NOTIFICATIONS)
        assertEquals(2, HostCallOp.BIOMETRICS)
        assertEquals(3, HostCallOp.SECURE_STORAGE)

        // BiometricStatus — SIX values (a richer terminal set than notifications).
        assertEquals(0, BiometricStatus.AUTHENTICATED)
        assertEquals(1, BiometricStatus.FAILED)
        assertEquals(2, BiometricStatus.CANCELLED)
        assertEquals(3, BiometricStatus.UNAVAILABLE)
        assertEquals(4, BiometricStatus.LOCKED_OUT)
        assertEquals(5, BiometricStatus.ERROR)

        // SecureStorageStatus — FIVE values.
        assertEquals(0, SecureStorageStatus.OK)
        assertEquals(1, SecureStorageStatus.NOT_FOUND)
        assertEquals(2, SecureStorageStatus.AUTH_FAILED)
        assertEquals(3, SecureStorageStatus.UNAVAILABLE)
        assertEquals(4, SecureStorageStatus.ERROR)
    }

    // ── (2) op=Biometrics authenticate round-trips, echoing the status ───────

    @Test
    fun authenticate_round_trips_op_biometrics_and_echoes_authenticated() {
        val s = boot(BiometricStatus.AUTHENTICATED)
        val mount = s.frames.first()
        val echo = echoTextNode(mount)

        val handler = clickHandlerOn(mount, containerOfText(mount, "Authenticate"))
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(handler, "click"))

        assertEquals(1, s.host.calls.size, "Authenticate must make exactly one host call")
        assertEquals(HostCallOp.BIOMETRICS, s.host.calls.single().first)
        assertTrue(
            s.host.calls.single().second.contains("\"action\":\"authenticate\""),
            "the args JSON must carry action:authenticate; got ${s.host.calls.single().second}")

        replaceTextOn(s.frames.subList(before, s.frames.size).toList(), echo, "status:Authenticated")
        s.runtime.retire()
    }

    // ── (3) getWithAuth returns the value in the {"value":…} payload ─────────

    @Test
    fun unlock_round_trips_op_secure_storage_and_echoes_the_value_payload() {
        val s = boot(SecureStorageStatus.OK, payload = mapOf("value" to "hunter2"))
        val mount = s.frames.first()
        val echo = echoTextNode(mount)

        val handler = clickHandlerOn(mount, containerOfText(mount, "Unlock"))
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(handler, "click"))

        assertEquals(HostCallOp.SECURE_STORAGE, s.host.calls.single().first)
        assertTrue(
            s.host.calls.single().second.contains("\"action\":\"getWithAuth\""),
            "the args JSON must carry action:getWithAuth; got ${s.host.calls.single().second}")

        // The Ok status + {"value":"hunter2"} payload round-trips into the echo AS DATA.
        replaceTextOn(s.frames.subList(before, s.frames.size).toList(), echo, "value:hunter2")
        s.runtime.retire()
    }

    // ── denial is DATA, no hang: a getWithAuth AuthFailed shows, never hangs ──

    @Test
    fun unlock_auth_failed_echoes_the_denial_as_data_no_hang() {
        // A non-Ok status is a VALUE the demo shows — never a thrown exception across the
        // boundary, never a dropped completion. If the deny path threw or hung,
        // dispatchEventBlocking would fault (rc 2) or never re-render the echo.
        val s = boot(SecureStorageStatus.AUTH_FAILED)
        val mount = s.frames.first()
        val echo = echoTextNode(mount)

        val handler = clickHandlerOn(mount, containerOfText(mount, "Unlock"))
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(handler, "click"))

        replaceTextOn(s.frames.subList(before, s.frames.size).toList(), echo, "status:AuthFailed")
        s.runtime.retire()
    }

    // ── set carries action + the auth flag on op=SecureStorage ───────────────

    @Test
    fun set_round_trips_op_secure_storage_with_the_auth_flag() {
        val s = boot(SecureStorageStatus.OK)
        val mount = s.frames.first()
        val echo = echoTextNode(mount)

        val handler = clickHandlerOn(mount, containerOfText(mount, "Set"))
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(handler, "click"))

        assertEquals(HostCallOp.SECURE_STORAGE, s.host.calls.single().first)
        val args = s.host.calls.single().second
        assertTrue(args.contains("\"action\":\"set\""), "must carry action:set; got $args")
        assertTrue(args.contains("\"auth\":\"1\""), "the demo sets requireAuth:true → auth:1; got $args")

        replaceTextOn(s.frames.subList(before, s.frames.size).toList(), echo, "status:Ok")
        s.runtime.retire()
    }

    // ── Structural pin helpers (NotificationsTest conventions) ───────────────

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

    /** The demo's echo BnText TEXT node, pinned at mount: root div → the span (the one
     * text-type child of the root; the four BnButtons are button-type) → its single
     * child text node (the NotificationsTest structural walk). */
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
