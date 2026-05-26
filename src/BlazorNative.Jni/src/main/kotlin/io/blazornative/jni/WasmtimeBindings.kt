package io.blazornative.jni

import com.sun.jna.Library
import com.sun.jna.Native
import com.sun.jna.Pointer

/**
 * JNA bindings for the wasmtime C API (libwasmtime / wasmtime.dll).
 *
 * Loaded lazily on first INSTANCE access; JNA searches the path declared in
 * the `jna.library.path` system property (set by build.gradle.kts to
 * vendor/wasmtime/). If wasmtime.dll is not found, JNA throws
 * UnsatisfiedLinkError with the search path in the message.
 *
 * Phase 2.1: minimum surface for boot smoke test — engine, store, component,
 * linker, instance, func + WASI stdio. Phase 2.3+ extends as bridge calls
 * require richer marshaling.
 *
 * See docs/plans/2026-05-26-phase-2.1-design.md for the full C-API surface plan.
 */
interface WasmtimeBindings : Library {

    // ─── Engine ──────────────────────────────────────────────────────────
    fun wasm_engine_new(): Pointer?
    fun wasm_engine_delete(engine: Pointer)

    // ─── Config (component-model toggle) ─────────────────────────────────
    // Note: config_new comes from the upstream wasm-c-api (wasm_* prefix);
    // component_model_set is a wasmtime extension (wasmtime_* prefix).
    fun wasm_config_new(): Pointer
    fun wasmtime_config_wasm_component_model_set(config: Pointer, enable: Byte)
    fun wasm_engine_new_with_config(config: Pointer): Pointer?

    // ─── Store ────────────────────────────────────────────────────────────
    fun wasmtime_store_new(engine: Pointer, data: Pointer?, finalizer: Pointer?): Pointer?
    fun wasmtime_store_delete(store: Pointer)

    // ─── Component ───────────────────────────────────────────────────────
    // Returns wasmtime_error_t* on failure (null = success). out: wasmtime_component_t**.
    fun wasmtime_component_new(engine: Pointer, wasmBytes: ByteArray, wasmLen: Long, componentOut: Array<Pointer?>): Pointer?
    fun wasmtime_component_delete(component: Pointer)

    // ─── Component Linker ────────────────────────────────────────────────
    fun wasmtime_component_linker_new(engine: Pointer): Pointer?
    fun wasmtime_component_linker_delete(linker: Pointer)

    // ─── Error ───────────────────────────────────────────────────────────
    fun wasmtime_error_message(errPtr: Pointer, nameOut: WasmName)
    fun wasmtime_error_delete(errPtr: Pointer)

    // ─── wasm_name_t helpers ─────────────────────────────────────────────
    // wasm_name_t is a typedef alias of wasm_byte_vec_t; only the byte_vec
    // deleter exists as a C symbol.
    fun wasm_byte_vec_delete(name: WasmName)

    // Stubs for the rest — Phase 2.1's later tasks fill these in.
    // Declaring the interface progressively (one task at a time) keeps each
    // commit reviewable.

    companion object {
        // JNA library name "wasmtime" → looks for wasmtime.dll (Windows),
        // libwasmtime.so (Linux), libwasmtime.dylib (macOS).
        val INSTANCE: WasmtimeBindings = Native.load("wasmtime", WasmtimeBindings::class.java)
    }
}
