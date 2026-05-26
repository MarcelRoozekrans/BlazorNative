package io.blazornative.jni

import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertNotNull

class LinkerLifecycleTest {

    @Test
    fun component_linker_can_be_created_and_deleted() {
        // Smoke test for the linker lifecycle. Phase 2.1.0 confirmed the .wasm
        // imports no custom mobile_bridge interface (Mono-AOT trimmed the
        // unused [DllImport] declarations), so this test does NOT register
        // any host functions — Phase 2.3 will, once the imports are rooted.
        val config = WasmtimeBindings.INSTANCE.wasm_config_new()
        WasmtimeBindings.INSTANCE.wasmtime_config_wasm_component_model_set(config, 1.toByte())
        val engine = WasmtimeBindings.INSTANCE.wasm_engine_new_with_config(config)!!

        val linker = WasmtimeBindings.INSTANCE.wasmtime_component_linker_new(engine)
        assertNotNull(linker, "wasmtime_component_linker_new returned null")

        WasmtimeBindings.INSTANCE.wasmtime_component_linker_delete(linker!!)
        WasmtimeBindings.INSTANCE.wasm_engine_delete(engine)
    }
}
