package io.blazornative.jni

import com.sun.jna.Pointer
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.nio.file.Path

/**
 * High-level facade: load BlazorNative.WasiHost.wasm as a wasmtime component,
 * configure WASI stdio (capturing stdout to a temp file), instantiate via
 * the linker, invoke wasi:cli/run.run, return captured stdout.
 *
 * Phase 2.1 does NOT register any host functions on the linker — the .wasm
 * imports only standard wasi:* interfaces which wasmtime satisfies natively
 * via wasmtime_component_linker_add_wasip2. See
 * docs/plans/2026-05-26-phase-2.1.0-spike-conclusion.md.
 *
 * Stdout capture: we route the .wasm's stdout to a temp file via
 * wasi_config_set_stdout_file, then read the file back after the run
 * completes. This is more reliable than System.out tee because libwasmtime
 * writes directly to the file descriptor, bypassing the JVM's System.out
 * PrintStream.
 */
object WasiHost {

    fun loadAndRun(wasmPath: Path): String {
        val wasmBytes = Files.readAllBytes(wasmPath)
        val stdoutFile = Files.createTempFile("blazor-native-stdout", ".log")
        val stderrFile = Files.createTempFile("blazor-native-stderr", ".log")
        // The .wasm bundles its assembly via WASM_SINGLE_FILE, but Mono still
        // needs filesystem access for ICU data and assembly auxiliary files.
        // Preopen the AppBundle dir (parent of the .wasm) as ".".
        val appBundleDir = wasmPath.parent.toAbsolutePath()
        try {
            var thrown: Throwable? = null
            try {
                runWasm(wasmBytes, stdoutFile, stderrFile, appBundleDir)
            } catch (t: Throwable) {
                thrown = t
            }
            val stdout = String(Files.readAllBytes(stdoutFile), StandardCharsets.UTF_8)
            val stderr = String(Files.readAllBytes(stderrFile), StandardCharsets.UTF_8)
            if (stdout.isNotEmpty()) {
                System.out.println("[WasiHost] .wasm stdout (${stdout.length} bytes):\n$stdout")
            }
            if (stderr.isNotEmpty()) {
                System.err.println("[WasiHost] .wasm stderr (${stderr.length} bytes):\n$stderr")
            }
            if (thrown != null) throw thrown
            return stdout
        } finally {
            try { Files.deleteIfExists(stdoutFile) } catch (_: Throwable) {}
            try { Files.deleteIfExists(stderrFile) } catch (_: Throwable) {}
        }
    }

    private fun runWasm(wasmBytes: ByteArray, stdoutFile: Path, stderrFile: Path, appBundleDir: Path) {
        val b = WasmtimeBindings.INSTANCE

        // ── Engine + config (component-model on) ────────────────────────
        val config = b.wasm_config_new()
        b.wasmtime_config_wasm_component_model_set(config, 1.toByte())
        val engine = b.wasm_engine_new_with_config(config)
            ?: error("wasm_engine_new_with_config returned null")

        try {
            // ── Store + context + WASI config ───────────────────────────
            val store = b.wasmtime_store_new(engine, Pointer.NULL, Pointer.NULL)
                ?: error("wasmtime_store_new returned null")

            try {
                val context = b.wasmtime_store_context(store)

                val wasiConfig = b.wasi_config_new()
                // wasi_config_t is consumed by wasmtime_context_set_wasi —
                // do NOT delete it explicitly after that call.
                if (!b.wasi_config_set_stdout_file(wasiConfig, stdoutFile.toAbsolutePath().toString())) {
                    error("wasi_config_set_stdout_file failed for $stdoutFile")
                }
                if (!b.wasi_config_set_stderr_file(wasiConfig, stderrFile.toAbsolutePath().toString())) {
                    error("wasi_config_set_stderr_file failed for $stderrFile")
                }
                // Preopen the AppBundle dir as "." — Mono needs filesystem
                // access for ICU data and assembly auxiliary files. Maps the
                // wasmtime CLI `--dir=.` flag (used by run-wasmtime.sh's
                // implicit CWD-relative behavior, since the parity test runs
                // wasmtime with WorkingDirectory=AppBundleDir).
                // Permission bits: READ=1, WRITE=2 → 3 = READ|WRITE.
                if (!b.wasi_config_preopen_dir(wasiConfig, appBundleDir.toString(), ".", 3L, 3L)) {
                    error("wasi_config_preopen_dir failed for $appBundleDir")
                }
                // Set argv[0] = program name. Mono's WASM_SINGLE_FILE path
                // doesn't strictly need argv[1+] (it uses
                // dotnet_wasi_getentrypointassemblyname()), but argv[0] is
                // conventionally set to the .wasm filename.
                if (!b.wasi_config_set_argv(wasiConfig, 1L, arrayOf("BlazorNative.WasiHost.wasm"))) {
                    error("wasi_config_set_argv failed")
                }
                val setWasiErr = b.wasmtime_context_set_wasi(context, wasiConfig)
                if (setWasiErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_context_set_wasi", setWasiErr)

                // ── Component ───────────────────────────────────────────
                val componentOut = arrayOfNulls<Pointer>(1)
                val compErr = b.wasmtime_component_new(engine, wasmBytes, wasmBytes.size.toLong(), componentOut)
                if (compErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_component_new", compErr)
                val component = componentOut[0] ?: error("wasmtime_component_new returned null component without error")

                try {
                    // ── Linker (add WASIp2 — no custom imports for Phase 2.1) ──
                    val linker = b.wasmtime_component_linker_new(engine)
                        ?: error("wasmtime_component_linker_new returned null")

                    try {
                        val addWasiErr = b.wasmtime_component_linker_add_wasip2(linker)
                        if (addWasiErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_component_linker_add_wasip2", addWasiErr)

                        // ── Instantiate ─────────────────────────────────
                        val instance = ComponentInstance()
                        val instErr = b.wasmtime_component_linker_instantiate(linker, context, component, instance)
                        if (instErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_component_linker_instantiate", instErr)

                        // ── Look up wasi:cli/run.run ────────────────────
                        // Component export index lookup: first the "wasi:cli/run@0.2.0"
                        // INSTANCE export, then the "run" function within it.
                        val runInstanceIdx = findExportIndex(component, null, "wasi:cli/run@0.2.0")
                            ?: findExportIndex(component, null, "wasi:cli/run")
                            ?: error("could not find wasi:cli/run instance export on component")

                        try {
                            val runFuncIdx = findExportIndex(component, runInstanceIdx, "run")
                                ?: error("could not find 'run' function within wasi:cli/run instance")

                            try {
                                val func = ComponentFunc()
                                val found = b.wasmtime_component_instance_get_func(instance, context, runFuncIdx, func)
                                if (!found) error("wasmtime_component_instance_get_func returned false for wasi:cli/run.run")

                                // ── Call run() ──────────────────────────
                                // wasi:cli/run.run signature: () -> result<_,_>
                                // We must provide a 1-element results buffer.
                                // The wasmtime_component_val_t struct is large
                                // (union with up to ~32 bytes payload + kind),
                                // so allocate generously and let wasmtime
                                // write the discriminant + payload.
                                val results = com.sun.jna.Memory(128L)
                                results.clear()
                                val callErr = b.wasmtime_component_func_call(
                                    func, context, null, 0L, results, 1L
                                )
                                if (callErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_component_func_call", callErr)
                            } finally {
                                b.wasmtime_component_export_index_delete(runFuncIdx)
                            }
                        } finally {
                            b.wasmtime_component_export_index_delete(runInstanceIdx)
                        }
                    } finally {
                        b.wasmtime_component_linker_delete(linker)
                    }
                } finally {
                    b.wasmtime_component_delete(component)
                }
            } finally {
                b.wasmtime_store_delete(store)
            }
        } finally {
            b.wasm_engine_delete(engine)
        }
    }

    private fun findExportIndex(component: Pointer, instanceIdx: Pointer?, name: String): Pointer? {
        val nameBytes = name.toByteArray(StandardCharsets.UTF_8)
        return WasmtimeBindings.INSTANCE.wasmtime_component_get_export_index(
            component, instanceIdx, nameBytes, nameBytes.size.toLong()
        )
    }
}
