package io.blazornative.jni

import com.sun.jna.Pointer
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertTrue
import java.nio.file.Files
import java.nio.file.Paths

class ComponentLoadTest {

    @Test
    fun component_loads_from_wasm_bytes() {
        val wasmPath = Paths.get(System.getProperty("wasm.path"))
        assertTrue(Files.exists(wasmPath), "wasm.path system property points at non-existent file: $wasmPath")

        val wasmBytes = Files.readAllBytes(wasmPath)
        assertTrue(wasmBytes.size > 1_000_000, ".wasm seems too small (${wasmBytes.size} bytes)")

        // Lifecycle: engine ← config (component-model on) → store → component
        val config = WasmtimeBindings.INSTANCE.wasm_config_new()
        WasmtimeBindings.INSTANCE.wasmtime_config_wasm_component_model_set(config, 1.toByte())

        val engine = WasmtimeBindings.INSTANCE.wasm_engine_new_with_config(config)
        assertNotNull(engine, "wasm_engine_new_with_config returned null")

        val store = WasmtimeBindings.INSTANCE.wasmtime_store_new(engine!!, Pointer.NULL, Pointer.NULL)
        assertNotNull(store, "wasmtime_store_new returned null")

        // Try to load the component
        val componentPtrRef = arrayOfNulls<Pointer>(1)
        val errPtr = WasmtimeBindings.INSTANCE.wasmtime_component_new(engine, wasmBytes, wasmBytes.size.toLong(), componentPtrRef)
        if (errPtr != null) {
            throw WasmtimeException.fromErrorPointer("wasmtime_component_new", errPtr)
        }
        assertNotNull(componentPtrRef[0], "wasmtime_component_new returned null component despite no error")

        // Cleanup
        WasmtimeBindings.INSTANCE.wasmtime_component_delete(componentPtrRef[0]!!)
        WasmtimeBindings.INSTANCE.wasmtime_store_delete(store!!)
        WasmtimeBindings.INSTANCE.wasm_engine_delete(engine)
    }
}
