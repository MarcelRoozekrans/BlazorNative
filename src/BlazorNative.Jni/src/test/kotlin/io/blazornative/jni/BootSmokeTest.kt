package io.blazornative.jni

import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertTrue
import java.nio.file.Paths

class BootSmokeTest {

    @Test
    fun boots_and_emits_markers() {
        val wasmPath = Paths.get(System.getProperty("wasm.path"))
        val stdout = WasiHost.loadAndRun(wasmPath)

        // The 4 [BOOT] markers from Phase 2.0's WasiEntryPoint.Main.
        // Direct parity with tests/BlazorNative.Wasi.Tests/BootSmoke.cs.
        assertTrue(
            stdout.contains("[BOOT] runtime-start"),
            "missing [BOOT] runtime-start in stdout. Captured stdout:\n$stdout"
        )
        assertTrue(
            stdout.contains("[BOOT] di-ok bridge=WasiBridge renderer=NativeRenderer"),
            "missing [BOOT] di-ok in stdout. Captured stdout:\n$stdout"
        )
        assertTrue(
            stdout.contains("[BOOT] event-ok fired=True name=self-test payload=phase-2.0"),
            "missing [BOOT] event-ok in stdout. Captured stdout:\n$stdout"
        )
        // Phase 2.3: env-var bridge marker. The default JVM handlers return
        // the stub-host JSON; assert just on the prefix since the payload
        // varies per host (Defaults.handlers vs AndroidPlatformInfo).
        assertTrue(
            stdout.contains("[BOOT] bridge-ok platform-info="),
            "missing [BOOT] bridge-ok in stdout. Captured stdout:\n$stdout"
        )
        assertTrue(
            stdout.contains("[BOOT] done"),
            "missing [BOOT] done in stdout. Captured stdout:\n$stdout"
        )
    }
}
