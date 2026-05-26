package io.blazornative.jni

import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertTrue
import java.nio.file.Paths

/**
 * Phase 2.3 sentinel verification.
 *
 * BootSmokeTest asserts the marker prefix only ("[BOOT] bridge-ok platform-info=")
 * because Defaults.handlers returns a static stub JSON. This test goes further:
 * it passes a CUSTOM MobileBridgeHandlers with a per-run sentinel value, then
 * asserts that exact sentinel appears in the captured stdout.
 *
 * If the sentinel appears, the host's platformInfo() lambda was actually called
 * and its result actually flowed into the .wasm via the BLAZOR_PLATFORM_INFO
 * env var (read by .NET's Environment.GetEnvironmentVariable through
 * wasi:cli/environment). A constant baked into the .wasm couldn't produce a
 * fresh sentinel per run — this proves the host round-trip works.
 */
class BridgePlatformInfoTest {

    @Test
    fun env_var_bridge_routes_custom_handler_output_to_dotnet() {
        // Sentinel value the handler returns. nanoTime() makes it fresh per run
        // so a stale .wasm with a constant baked in could not pass this test.
        val sentinel = """{"phase":"2.3","sentinel":"BRIDGE-INVOKED-AT-${System.nanoTime()}"}"""

        val customHandlers = MobileBridgeHandlers(
            platformInfo = { sentinel }
        )

        val wasmPath = Paths.get(System.getProperty("wasm.path"))
        val stdout = WasiHost.loadAndRun(wasmPath, customHandlers)

        assertTrue(
            stdout.contains(sentinel),
            "Expected sentinel '$sentinel' in captured stdout (proves host handler fired). Captured stdout:\n$stdout"
        )
    }
}
