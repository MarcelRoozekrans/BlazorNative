package io.blazornative.jni

import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertNotNull

class EngineLifecycleTest {

    @Test
    fun engine_can_be_created_and_deleted() {
        // Smoke test that JNA loads wasmtime.dll and the engine lifecycle works.
        // If wasmtime.dll is missing from vendor/wasmtime/, this fails with
        // UnsatisfiedLinkError — diagnostic IS the error message.
        val engine = WasmtimeBindings.INSTANCE.wasm_engine_new()
        assertNotNull(engine, "wasm_engine_new returned null")
        WasmtimeBindings.INSTANCE.wasm_engine_delete(engine!!)
    }
}
