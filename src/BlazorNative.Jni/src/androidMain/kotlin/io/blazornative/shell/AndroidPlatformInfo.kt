package io.blazornative.shell

import android.os.Build
import android.util.Log
import io.blazornative.jni.MobileBridgeHandlers
import io.blazornative.jni.RenderFrame

/**
 * Phase 2.3 Android-side handlers for the env-var bridge.
 *
 * Provides real Build.VERSION_CODES + ABI + model data so the captured
 * [BOOT] bridge-ok marker shows runtime-accurate device info. The marker
 * payload varying between hosts (this Android impl vs JVM Defaults.handlers)
 * is the proof that the host's lambda actually fired — a constant baked
 * into the .wasm would be identical across all hosts.
 *
 * Bridge path: WasiHost.loadAndRun calls handlers.platformInfo() during
 * setup, passes the result via wasi_config_set_env(BLAZOR_PLATFORM_INFO=...),
 * .NET reads via Environment.GetEnvironmentVariable.
 *
 * Phase 2.4: extends with onFrame — logs each parsed RenderFrame to logcat
 * for diagnostic visibility. Phase 2.5 swaps this for the real widget mapper.
 */
object AndroidPlatformInfo {
    private const val TAG = "BlazorNative"

    val handlers = MobileBridgeHandlers(
        platformInfo = {
            // Quote-escape Build.MODEL because OEM builds may contain quotes.
            val abi = Build.SUPPORTED_ABIS.firstOrNull() ?: "unknown"
            val model = Build.MODEL.replace("\"", "\\\"")
            """{"os":"Android","sdk":${Build.VERSION.SDK_INT},"abi":"$abi","model":"$model"}"""
        },
        onFrame = { frame ->
            Log.i(TAG, "Frame ${frame.frameId}: ${frame.patches.size} patches")
        }
    )
}
