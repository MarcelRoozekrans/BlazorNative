package io.blazornative.jni

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 2.2 GREEN CHECKPOINT.
 *
 * Same 4 [BOOT] marker assertions as:
 *   - tests/BlazorNative.Wasi.Tests/BootSmoke.cs (.NET-side, wasmtime CLI subprocess)
 *   - src/BlazorNative.Jni/src/test/.../BootSmokeTest.kt (JVM in-process JNA)
 *
 * Three-way cross-validation: if all three pass, the .wasm boots identically
 * in subprocess wasmtime CLI, JVM in-process JNA, AND Android in-process JNA
 * via per-ABI cross-compiled libwasmtime.so.
 *
 * Runs via ./gradlew connectedAndroidTest against a running emulator/device.
 * Failure messages include the full captured stdout — the diagnostic IS the
 * stdout.
 */
@RunWith(AndroidJUnit4::class)
class BootSmokeAndroidTest {

    @Test
    fun boots_and_emits_markers_on_android() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val wasmBytes = context.assets.open("BlazorNative.WasiHost.wasm").use { it.readBytes() }
        assertTrue(".wasm seems too small (${wasmBytes.size} bytes)", wasmBytes.size > 1_000_000)

        val stdout = WasiHost.loadAndRun(wasmBytes, context.cacheDir)

        assertTrue("missing [BOOT] runtime-start. stdout:\n$stdout",
            stdout.contains("[BOOT] runtime-start"))
        assertTrue("missing [BOOT] di-ok. stdout:\n$stdout",
            stdout.contains("[BOOT] di-ok bridge=WasiBridge renderer=NativeRenderer"))
        assertTrue("missing [BOOT] event-ok. stdout:\n$stdout",
            stdout.contains("[BOOT] event-ok fired=True name=self-test payload=phase-2.0"))
        assertTrue("missing [BOOT] done. stdout:\n$stdout",
            stdout.contains("[BOOT] done"))
    }
}
