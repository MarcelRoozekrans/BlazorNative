package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 9.3 Gate 2 — the camera op driven through the published NativeAOT dll (the Kotlin
 * twin of BnCameraDemoTests.cs (.NET, DevHostBridge drives every status headless) and the
 * on-device BnCameraAndroidTest / CameraCaptureAndroidTest (the AVD)). Three JVM proofs:
 *
 *   (1) WIRE ENUMS — HostCallOp.CAMERA (op 4 — the THIRD reuse of the 9.0 generic ABI, the
 *       last M9 capability) and CameraStatus (the five values) mirror the .NET contract
 *       byte-for-byte. These integers ARE the ABI — a reorder here silently corrupts every
 *       completion.
 *   (2) capture ROUND-TRIP — mount BnCameraDemo, tap "Take Photo"; the dll emits
 *       hostCallBegin(op=Camera, {"action":"capture",…}); a mock host completes CAPTURED with
 *       the {"path","width","height","bytes"} payload NAMING a file (the LARGE-artifact-by-
 *       REFERENCE channel — the bytes never cross the wire) and the demo echoes
 *       "captured:WxH:bytes" AS DATA.
 *   (3) DENIAL IS DATA — a Cancelled completion (no path) echoes "status:Cancelled" within a
 *       bounded blocking dispatch; a dropped/thrown completion would hang or fault, never
 *       reach the echo. And `check` rides the SAME op with {"action":"check"}.
 *
 * Node identification is ALWAYS structural / by text (ids are process-global counters) — the
 * BiometricsSecureStorageTest/NotificationsTest convention. Safe alongside the other native
 * tests in one JVM process (idempotent init, last-wins registration).
 */
class CameraTest {

    /** Inert host whose hostCallBegin completes with a configurable status + optional payload,
     * recording the (op, argsJson) it saw so the round-trip can assert the wire shape. */
    private class CameraHost(private val status: Int, private val payload: Map<String, String>?) :
        ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        val calls = mutableListOf<Pair<Int, String>>()
        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "CameraTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
        override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
            calls.add(op to argsJson)
            BridgeHostCallCompleter.complete(requestId, status, payload)
        }
    }

    private class Session(val runtime: BlazorNativeRuntime, val frames: MutableList<RenderFrame>, val host: CameraHost)

    private fun boot(status: Int, payload: Map<String, String>? = null): Session {
        val frames = mutableListOf<RenderFrame>()
        val host = CameraHost(status, payload)
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = "BnCameraDemo", platformOs = "test-host", bridge = host)
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return Session(runtime, frames, host)
    }

    // ── (1) the wire enums mirror .NET byte-for-byte ─────────────────────────

    @Test
    fun wire_enums_mirror_the_dotnet_contract() {
        // op-enum: Geolocation=0 (9.0), Notifications=1 (9.1), Biometrics=2 + SecureStorage=3
        // (9.2), Camera=4 (9.3 — the last M9 capability).
        assertEquals(4, HostCallOp.CAMERA)

        // CameraStatus — FIVE values; the Captured/Cancelled split is the demo's headline.
        assertEquals(0, CameraStatus.CAPTURED)
        assertEquals(1, CameraStatus.CANCELLED)
        assertEquals(2, CameraStatus.DENIED)
        assertEquals(3, CameraStatus.UNAVAILABLE)
        assertEquals(4, CameraStatus.ERROR)
    }

    // ── (2) Take Photo round-trips op=Camera and echoes the {"path",…} payload ──

    @Test
    fun take_photo_round_trips_op_camera_and_echoes_the_captured_payload() {
        // The image crosses as a PATH — the payload NAMES a file, it does not carry the bytes.
        val s = boot(
            CameraStatus.CAPTURED,
            payload = mapOf("path" to "file:///canned.jpg", "width" to "240", "height" to "320", "bytes" to "4096"))
        val mount = s.frames.first()
        val echo = echoTextNode(mount)

        val handler = clickHandlerOn(mount, containerOfText(mount, "Take Photo"))
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(handler, "click"))

        assertEquals(1, s.host.calls.size, "Take Photo must make exactly one host call")
        assertEquals(HostCallOp.CAMERA, s.host.calls.single().first)
        assertTrue(
            s.host.calls.single().second.contains("\"action\":\"capture\""),
            "the args JSON must carry action:capture; got ${s.host.calls.single().second}")

        // The Captured status + the path/dims payload round-trips into the echo AS DATA — the
        // FINAL dims + size, proof the file the path names has real bytes.
        replaceTextOn(s.frames.subList(before, s.frames.size).toList(), echo, "captured:240x320:4096")
        s.runtime.retire()
    }

    // ── (3) a Cancelled capture echoes the denial as DATA, no hang ───────────

    @Test
    fun capture_cancelled_echoes_the_denial_as_data_no_hang() {
        // A non-Captured status is a VALUE the demo shows (no path) — never a thrown exception
        // across the boundary, never a dropped completion. If the cancel path threw or hung,
        // dispatchEventBlocking would fault (rc 2) or never re-render the echo.
        val s = boot(CameraStatus.CANCELLED)
        val mount = s.frames.first()
        val echo = echoTextNode(mount)

        val handler = clickHandlerOn(mount, containerOfText(mount, "Take Photo"))
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(handler, "click"))

        replaceTextOn(s.frames.subList(before, s.frames.size).toList(), echo, "status:Cancelled")
        s.runtime.retire()
    }

    // ── check rides the SAME op with {"action":"check"} ─────────────────────

    @Test
    fun check_round_trips_op_camera_with_action_check() {
        val s = boot(CameraStatus.UNAVAILABLE)
        val mount = s.frames.first()
        val echo = echoTextNode(mount)

        val handler = clickHandlerOn(mount, containerOfText(mount, "Check"))
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(handler, "click"))

        assertEquals(HostCallOp.CAMERA, s.host.calls.single().first)
        assertTrue(
            s.host.calls.single().second.contains("\"action\":\"check\""),
            "the args JSON must carry action:check; got ${s.host.calls.single().second}")

        replaceTextOn(s.frames.subList(before, s.frames.size).toList(), echo, "status:Unavailable")
        s.runtime.retire()
    }

    // ── Structural pin helpers (BiometricsSecureStorageTest conventions) ─────

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

    /** The demo's echo BnText TEXT node, pinned at mount: root div → the span (the one text-type
     * child of the root; the two BnButtons are button-type and the BnImage is image-type) → its
     * single child text node (the BiometricsSecureStorageTest structural walk). */
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
