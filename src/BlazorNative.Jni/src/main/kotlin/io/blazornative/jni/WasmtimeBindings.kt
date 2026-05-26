package io.blazornative.jni

import com.sun.jna.Library
import com.sun.jna.Native
import com.sun.jna.Pointer
import com.sun.jna.Structure

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
    fun wasmtime_store_context(store: Pointer): Pointer

    // ─── Component ───────────────────────────────────────────────────────
    // Returns wasmtime_error_t* on failure (null = success). out: wasmtime_component_t**.
    fun wasmtime_component_new(engine: Pointer, wasmBytes: ByteArray, wasmLen: Long, componentOut: Array<Pointer?>): Pointer?
    fun wasmtime_component_delete(component: Pointer)
    fun wasmtime_component_get_export_index(
        component: Pointer,
        instanceExportIndex: Pointer?,
        name: ByteArray,
        nameLen: Long
    ): Pointer?
    fun wasmtime_component_export_index_delete(exportIndex: Pointer)

    // ─── Component Linker ────────────────────────────────────────────────
    fun wasmtime_component_linker_new(engine: Pointer): Pointer?
    fun wasmtime_component_linker_delete(linker: Pointer)
    // Add all standard WASI preview2 interfaces to the linker. This is the
    // simplest way to satisfy the .wasm's wasi:* imports — wasmtime handles
    // the actual implementation.
    fun wasmtime_component_linker_add_wasip2(linker: Pointer): Pointer?
    fun wasmtime_component_linker_instantiate(
        linker: Pointer,
        context: Pointer,
        component: Pointer,
        instanceOut: ComponentInstance
    ): Pointer?

    // ─── Component Instance ──────────────────────────────────────────────
    // Returns bool: true if found, writes funcOut. Takes an export_index (NOT
    // a name string!); use wasmtime_component_get_export_index to obtain one.
    fun wasmtime_component_instance_get_func(
        instance: ComponentInstance,
        context: Pointer,
        exportIndex: Pointer,
        funcOut: ComponentFunc
    ): Boolean

    // ─── Component Function ──────────────────────────────────────────────
    // Note: NO trap out-param. Errors and traps both surface via the returned
    // wasmtime_error_t* per Phase 2.1's wasmtime C API.
    fun wasmtime_component_func_call(
        func: ComponentFunc,
        context: Pointer,
        args: Pointer?,
        argsSize: Long,
        results: Pointer?,
        resultsSize: Long
    ): Pointer?

    // ─── WASI config ─────────────────────────────────────────────────────
    fun wasi_config_new(): Pointer
    fun wasi_config_inherit_stdout(config: Pointer)
    fun wasi_config_inherit_stderr(config: Pointer)
    fun wasi_config_inherit_stdin(config: Pointer)
    // Route .wasm stdout to a host file path (UTF-8). Returns true on success.
    // We use a file rather than System.out tee because libwasmtime writes
    // directly to fd1, bypassing the JVM's System.out PrintStream.
    fun wasi_config_set_stdout_file(config: Pointer, path: String): Boolean
    fun wasi_config_set_stderr_file(config: Pointer, path: String): Boolean
    // Grant the .wasm filesystem access. `dir_perms` and `file_perms` are
    // size_t bitmasks (1 = READ, 2 = WRITE). Use 3 (READ|WRITE).
    fun wasi_config_preopen_dir(
        config: Pointer,
        hostPath: String,
        guestPath: String,
        dirPerms: Long,
        filePerms: Long
    ): Boolean
    // Set argv (UTF-8 strings). argv[0] is conventionally the program name.
    fun wasi_config_set_argv(config: Pointer, argc: Long, argv: Array<String>): Boolean
    // Set environment variables — parallel arrays of names + values.
    // Phase 2.3 env-var bridge (revised design): host passes BLAZOR_PLATFORM_INFO
    // here; .NET reads it via Environment.GetEnvironmentVariable.
    fun wasi_config_set_env(config: Pointer, envc: Long, names: Array<String>, values: Array<String>): Boolean
    fun wasmtime_context_set_wasi(context: Pointer, config: Pointer): Pointer?

    // ─── Error ───────────────────────────────────────────────────────────
    fun wasmtime_error_message(errPtr: Pointer, nameOut: WasmName)
    fun wasmtime_error_delete(errPtr: Pointer)

    // ─── wasm_name_t helpers ─────────────────────────────────────────────
    // wasm_name_t is a typedef alias of wasm_byte_vec_t; only the byte_vec
    // deleter exists as a C symbol.
    fun wasm_byte_vec_delete(name: WasmName)

    companion object {
        // JNA library name "wasmtime" → looks for wasmtime.dll (Windows),
        // libwasmtime.so (Linux), libwasmtime.dylib (macOS).
        val INSTANCE: WasmtimeBindings = Native.load("wasmtime", WasmtimeBindings::class.java)
    }
}

/**
 * Mirror of `wasmtime_component_instance_t` from wasmtime/component/instance.h:
 *     typedef struct { uint64_t store_id; uint32_t __private; }
 *         wasmtime_component_instance_t;
 *
 * Passed by value in C. JNA marshals @ByValue Structures as the bytes
 * themselves rather than a pointer, which matches the C ABI.
 */
@Structure.FieldOrder("storeId", "private1")
open class ComponentInstance : Structure() {
    @JvmField var storeId: Long = 0
    @JvmField var private1: Int = 0
}

/**
 * Mirror of `wasmtime_component_func_t` from wasmtime/component/func.h:
 *     typedef struct {
 *         struct { uint64_t store_id; uint32_t __private1; };
 *         uint32_t __private2;
 *     } wasmtime_component_func_t;
 */
@Structure.FieldOrder("storeId", "private1", "private2")
open class ComponentFunc : Structure() {
    @JvmField var storeId: Long = 0
    @JvmField var private1: Int = 0
    @JvmField var private2: Int = 0
}
