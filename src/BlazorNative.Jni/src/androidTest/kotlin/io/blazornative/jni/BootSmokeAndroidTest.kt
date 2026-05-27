package io.blazornative.jni

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.MobileBridgeHandlers
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch
import io.blazornative.shell.AndroidPlatformInfo
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

        val captured = mutableListOf<RenderFrame>()
        val androidHandlers = AndroidPlatformInfo.handlers
        val handlers = MobileBridgeHandlers(
            platformInfo = androidHandlers.platformInfo,
            onFrame = { frame ->
                androidHandlers.onFrame(frame)  // preserve logcat side-effect
                captured.add(frame)
            }
        )
        val stdout = WasiHost.loadAndRun(wasmBytes, context.cacheDir, handlers)

        assertTrue("missing [BOOT] runtime-start. stdout:\n$stdout",
            stdout.contains("[BOOT] runtime-start"))
        assertTrue("missing [BOOT] di-ok. stdout:\n$stdout",
            stdout.contains("[BOOT] di-ok bridge=WasiBridge renderer=NativeRenderer"))
        assertTrue("missing [BOOT] event-ok. stdout:\n$stdout",
            stdout.contains("[BOOT] event-ok fired=True name=self-test payload=phase-2.0"))
        // Phase 2.3 env-var bridge: assert the marker is present AND contains the
        // literal '"os":"Android"' substring proving AndroidPlatformInfo.handlers
        // ran (not the JVM Defaults stub).
        assertTrue("missing [BOOT] bridge-ok. stdout:\n$stdout",
            stdout.contains("[BOOT] bridge-ok platform-info="))
        assertTrue("expected '\"os\":\"Android\"' in bridge-ok payload. stdout:\n$stdout",
            stdout.contains("\"os\":\"Android\""))
        // Phase 2.4: sentinel + frame round-trip on Android.
        assertTrue("missing [BOOT] mounting sentinel. stdout:\n$stdout",
            stdout.contains("[BOOT] mounting sentinel"))
        assertTrue("missing [BOOT] frame-emitted. stdout:\n$stdout",
            stdout.contains("[BOOT] frame-emitted"))
        assertTrue("onFrame never fired. stdout:\n$stdout",
            captured.isNotEmpty())
        assertTrue("expected CommitFrame patch in first captured frame. captured: $captured",
            captured.first().patches.any { it is RenderPatch.CommitFrame })
        assertTrue("expected CreateNode(view) patch in first captured frame. captured: $captured",
            captured.first().patches.any { it is RenderPatch.CreateNode && it.nodeType == "view" })
        assertTrue("missing [BOOT] done. stdout:\n$stdout",
            stdout.contains("[BOOT] done"))
    }
}
