package io.blazornative.jni

import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assertions.assertFalse
import java.nio.file.Paths

/**
 * Phase 2.4 end-to-end transport assertion. Mounts the sentinel component in
 * the .wasm, captures stdout, parses [FRAME] lines, asserts onFrame fires
 * with the expected shape (one view CreateNode + one CommitFrame).
 *
 * Proves the full path: NativeRenderer.UpdateDisplayAsync -> DispatchFrame ->
 * Console.WriteLine -> wasi:cli/stdout -> wasi_config_set_stdout_file ->
 * captured string -> FrameStreamParser.parse -> handlers.onFrame.
 */
class FrameDispatchTest {

    @Test
    fun sentinel_frame_round_trips_to_onFrame_callback() {
        val captured = mutableListOf<RenderFrame>()
        val handlers = MobileBridgeHandlers(
            platformInfo = { """{"os":"jvm-test","note":"frame-dispatch"}""" },
            onFrame = { captured.add(it) }
        )

        val wasmPath = Paths.get(System.getProperty("wasm.path"))
        WasiHost.loadAndRun(wasmPath, handlers)

        assertFalse(captured.isEmpty(),
            "onFrame never fired - no [FRAME] lines in captured stdout")

        val frame = captured.first()
        assertTrue(frame.patches.any { it is RenderPatch.CommitFrame },
            "expected at least one CommitFrame patch in $frame")
        assertTrue(
            frame.patches.any { it is RenderPatch.CreateNode && it.nodeType == "view" },
            "expected at least one CreateNode(view) patch in $frame"
        )
    }
}
