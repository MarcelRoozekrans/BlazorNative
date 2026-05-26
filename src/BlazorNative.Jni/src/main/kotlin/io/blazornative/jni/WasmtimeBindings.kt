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

    // Stubs for the rest — Phase 2.1's later tasks fill these in.
    // Declaring the interface progressively (one task at a time) keeps each
    // commit reviewable.

    companion object {
        // JNA library name "wasmtime" → looks for wasmtime.dll (Windows),
        // libwasmtime.so (Linux), libwasmtime.dylib (macOS).
        val INSTANCE: WasmtimeBindings = Native.load("wasmtime", WasmtimeBindings::class.java)
    }
}
