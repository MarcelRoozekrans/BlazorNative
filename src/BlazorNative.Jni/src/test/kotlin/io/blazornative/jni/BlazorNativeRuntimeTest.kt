package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 3.0d Gate 3 — desktop-JVM proof of the BlazorNativeRuntime lifecycle
 * wrapper against the win-x64 NativeAOT dll: start() boots init → register →
 * mount and the HelloComponent frame arrives through the struct path.
 *
 * Safe to run alongside the other native tests in one JVM process:
 * blazornative_init is idempotent and callback re-registration is last-wins,
 * so each test's runtime instance simply takes over the callback slot.
 */
class BlazorNativeRuntimeTest {

    @Test
    fun start_boots_and_delivers_a_frame() {
        val captured = AtomicReference<RenderFrame>()
        val runtime = BlazorNativeRuntime(onFrame = { captured.set(it) })

        // The first frame callback fires synchronously INSIDE mount, so no
        // latch/wait is needed — by the time start() returns the frame is set.
        val lines = runtime.start(platformOs = "test-host", apiLevel = 0)

        assertEquals(3, lines.size, "expected exactly 3 [BOOT] lines; got $lines")
        assertTrue(lines.all { it.startsWith("[BOOT]") }, "non-[BOOT] line in $lines")
        assertTrue(lines[0].contains("native init ok"), "unexpected first line: ${lines[0]}")
        assertTrue(lines[1].contains("frame callback registered"), "unexpected second line: ${lines[1]}")
        assertTrue(lines[2].contains("mounted HelloComponent"), "unexpected third line: ${lines[2]}")

        val frame = captured.get()
        assertNotNull(frame, "no frame arrived via onFrame during mount")
        assertTrue(frame.patches.isNotEmpty(), "frame arrived but carried no patches")
        assertTrue(
            frame.patches.any { it is RenderPatch.CommitFrame },
            "frame should end in a CommitFrame patch; got ${frame.patches.map { it::class.simpleName }}"
        )
    }

    @Test
    fun callback_wraps_consumer_throw_and_routes_to_onError() {
        val error = AtomicReference<Pair<String, Throwable>>()
        val runtime = BlazorNativeRuntime(
            onFrame = { throw IllegalStateException("consumer boom") },
            onError = { msg, t -> error.set(msg to t) },
        )

        // Must NOT throw: the callback body catches the consumer throw before
        // JNA's swallow-to-stderr handler would see it, and mount completes.
        val lines = runtime.start(platformOs = "test-host", apiLevel = 0)
        assertEquals(3, lines.size, "boot should complete despite consumer throw; got $lines")

        val captured = error.get()
        assertNotNull(captured, "onError was not invoked for the throwing consumer")
        assertTrue(
            captured.first.contains("frame dropped"),
            "unexpected onError message: ${captured.first}"
        )
        assertEquals("consumer boom", captured.second.message)
    }
}
