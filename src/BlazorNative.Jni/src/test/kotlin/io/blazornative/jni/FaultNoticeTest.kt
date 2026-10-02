package io.blazornative.jni

import com.sun.jna.Memory
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

/**
 * Phase 16.1 (#8) — a fault AFTER a handler's first await reaches [BlazorNativeRuntime]'s
 * onError, through the published NativeAOT dll.
 *
 * Since #345's fix the dispatch export returns once the handler's synchronous part has
 * run, so a later fault can no longer be its rc 2. .NET sends it as a FaultNotice host
 * call, op 5 on the existing hostCallBegin slot, and the shared [BridgeRegistrar]
 * answers it: onError, then an OK completion. This drives it end to end with no test
 * component: BnCameraDemo's Take Photo awaits the camera call, the test answers that
 * call Captured with a MALFORMED payload, and the demo's continuation throws
 * FormatException while parsing it — a genuine late fault in the sample app, raised
 * with the published dll's production error mode.
 *
 * DOES NOT COVER: the iOS arm (BnFaultNoticeTests.swift), or the notice's exact args,
 * which FaultNoticeTests.cs pins on the .NET side. The session is the one
 * process-global .NET session every JVM test shares, so the held call is always
 * answered in a finally.
 */
class FaultNoticeTest {

    /** Holds the camera call open and records every op it is handed. */
    private class HoldingCameraHost : ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        val ops: MutableList<Int> = Collections.synchronizedList(mutableListOf())
        val cameraCall = CountDownLatch(1)
        @Volatile var heldRequestId: Long = -1
        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "FaultNoticeTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
        override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
            ops.add(op)
            if (op == HostCallOp.CAMERA) {
                heldRequestId = requestId
                cameraCall.countDown() // NOT completed: the test answers it
            } else {
                BridgeHostCallCompleter.complete(requestId, HostCallStatus.ERROR, null)
            }
        }
    }

    /** Answers the camera call INSIDE hostCallBegin, as Android's inline arms answer theirs,
     *  with a payload that is not flat JSON, so BnCameraDemo's continuation throws. The
     *  completer takes a map, so the malformed text goes through NativeBindings. */
    private class InlineCameraHost : ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        @Volatile var answeredInsideBegin = 0
        @Volatile var completeRc = -1
        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "FaultNoticeTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
        override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
            if (op == HostCallOp.CAMERA) {
                val bad = Memory(6).apply { setString(0, "{bad", "UTF-8") }
                completeRc = NativeBindings.INSTANCE.blazornative_host_call_complete(requestId, CameraStatus.CAPTURED, bad)
                java.lang.ref.Reference.reachabilityFence(bad)
                answeredInsideBegin++
            } else {
                BridgeHostCallCompleter.complete(requestId, HostCallStatus.ERROR, null)
            }
        }
    }

    @Test
    fun a_fault_after_an_inline_answered_host_call_is_a_fault_notice_not_rc_2() {
        val host = InlineCameraHost()
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val errors = Collections.synchronizedList(mutableListOf<Pair<String, Throwable>>())
        val faultSeen = CountDownLatch(1)
        val runtime = BlazorNativeRuntime(
            onFrame = { frames.add(it) },
            onError = { msg, t ->
                errors.add(msg to t)
                if (msg.startsWith("handler fault after await:")) faultSeen.countDown()
            },
        )
        runtime.start(componentName = "BnCameraDemo", platformOs = "test-host", bridge = host)
        try {
            val mount = frames.first()
            val takePhoto = clickHandlerOn(mount, containerOfText(mount, "Take Photo"))
            val rc = runtime.dispatchEventBlocking(takePhoto, "click")
            // Anchor: the call really was answered inside begin.
            assertEquals(1, host.answeredInsideBegin, "the camera call was not answered inside hostCallBegin")
            assertEquals(0, host.completeRc, "the camera call answered inside hostCallBegin was not pending")
            assertEquals(0, rc,
                "a fault after an await on a call answered inside hostCallBegin came back as rc $rc. " +
                    "The await did not yield, which is #455, through the NativeAOT dll.")
            assertTrue(faultSeen.await(10, TimeUnit.SECONDS),
                "no FaultNotice reached onError for the fault after the inline-answered call. onError saw: $errors")
            val (msg, _) = errors.first { it.first.startsWith("handler fault after await:") }
            assertTrue(msg.contains("System.FormatException:"), "message was: $msg")
            assertTrue(msg.contains("event 'click'"), "message was: $msg")
        } finally {
            runtime.retire()
        }
    }

    @Test
    fun fault_notice_is_op_five() {
        assertEquals(5, HostCallOp.FAULT_NOTICE)
    }

    @Test
    fun a_fault_after_the_first_await_reaches_onError_and_the_notice_is_completed() {
        val host = HoldingCameraHost()
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val errors = Collections.synchronizedList(mutableListOf<Pair<String, Throwable>>())
        val faultSeen = CountDownLatch(1)
        val runtime = BlazorNativeRuntime(
            onFrame = { frames.add(it) },
            onError = { msg, t ->
                errors.add(msg to t)
                if (msg.startsWith("handler fault after await:")) faultSeen.countDown()
            },
        )
        runtime.start(componentName = "BnCameraDemo", platformOs = "test-host", bridge = host)
        try {
            val mount = frames.first()
            val takePhoto = clickHandlerOn(mount, containerOfText(mount, "Take Photo"))
            BridgeRegistrar.lastFaultNoticeCompleteRcForTest = -1

            // The synchronous part runs and the export returns rc 0 while the call is open.
            assertEquals(0, runtime.dispatchEventBlocking(takePhoto, "click"))
            assertTrue(host.cameraCall.await(10, TimeUnit.SECONDS), "Take Photo never began the camera call")
            // Anchor: nothing has faulted yet, so nothing may have been reported.
            assertFalse(errors.any { it.first.startsWith("handler fault after await:") },
                "a fault was reported before the held call was answered: $errors")

            // Answer Captured with a payload that is not flat JSON: the demo's continuation
            // throws while parsing it, AFTER the export has long since returned.
            val bad = Memory(6).apply { setString(0, "{bad", "UTF-8") }
            val rc = NativeBindings.INSTANCE.blazornative_host_call_complete(host.heldRequestId, CameraStatus.CAPTURED, bad)
            java.lang.ref.Reference.reachabilityFence(bad)
            assertEquals(0, rc, "the held camera call was not pending any more")
            host.heldRequestId = -1

            assertTrue(faultSeen.await(10, TimeUnit.SECONDS),
                "the handler faulted after its first await and nothing reached onError within 10s. " +
                    "The fault was only logged, which is #8. onError saw: $errors")
            val (msg, t) = errors.first { it.first.startsWith("handler fault after await:") }
            assertTrue(msg.startsWith("handler fault after await: System.FormatException:"), "message was: $msg")
            assertTrue(msg.contains("event 'click'"), "message was: $msg")
            assertTrue(t is RuntimeException, "throwable was $t")

            // The shell completed the notice, and .NET found its pending entry: rc 0.
            val deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(10)
            while (BridgeRegistrar.lastFaultNoticeCompleteRcForTest != 0 && System.nanoTime() < deadline) Thread.sleep(10)
            assertEquals(0, BridgeRegistrar.lastFaultNoticeCompleteRcForTest,
                "the FaultNotice was not completed, or .NET no longer held its pending entry")

            // The notice never reached the host's own handlers: BridgeRegistrar answers it.
            assertFalse(host.ops.contains(HostCallOp.FAULT_NOTICE), "handlers saw ops ${host.ops}")
        } finally {
            if (host.heldRequestId >= 0) {
                BridgeHostCallCompleter.complete(host.heldRequestId, CameraStatus.CANCELLED, null)
            }
            runtime.retire()
        }
    }

    // ── Structural helpers (the CameraTest conventions) ─────────────────────

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
}
