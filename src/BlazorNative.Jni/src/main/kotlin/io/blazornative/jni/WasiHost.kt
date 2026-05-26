package io.blazornative.jni

import com.sun.jna.Pointer
import java.io.File
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.nio.file.Path
import java.util.UUID

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
 * Stdout capture: we route the .wasm's stdout to a file via
 * wasi_config_set_stdout_file, then read the file back after the run
 * completes. This is more reliable than System.out tee because libwasmtime
 * writes directly to the file descriptor, bypassing the JVM's System.out
 * PrintStream.
 *
 * Phase 2.2 split: the primary signature `(ByteArray, File)` accepts raw
 * .wasm bytes + a writable scratch directory. Android calls this with
 * `assets.open(...).readBytes()` + `context.cacheDir`. The legacy
 * `(Path)` overload delegates to the primary by reading the file +
 * using `Files.createTempDirectory()` for scratch — keeps Phase 2.1's
 * JVM BootSmokeTest working unchanged.
 */
object WasiHost {

    /**
     * JVM-only convenience: reads .wasm bytes from disk, uses an ephemeral
     * temp dir for stdout capture + Mono's filesystem preopen.
     */
    fun loadAndRun(wasmPath: Path, handlers: MobileBridgeHandlers = Defaults.handlers): String {
        val wasmBytes = Files.readAllBytes(wasmPath)
        val scratchDir = Files.createTempDirectory("blazor-native-jni").toFile()
        try {
            return loadAndRun(wasmBytes, scratchDir, handlers)
        } finally {
            scratchDir.deleteRecursively()
        }
    }

    /**
     * Primary: caller provides .wasm bytes + a writable scratch directory.
     * Android passes `context.cacheDir`; JVM convenience overload creates
     * an ephemeral one. The scratch dir is preopened to the .wasm as "."
     * (Mono needs writable space for ICU data + assembly auxiliary files)
     * AND houses the stdout + stderr capture files.
     *
     * Phase 2.3 env-var bridge: handlers.platformInfo() is called once
     * during this setup and passed to the .wasm as the BLAZOR_PLATFORM_INFO
     * environment variable, which .NET reads via
     * Environment.GetEnvironmentVariable for the [BOOT] bridge-ok self-test.
     */
    fun loadAndRun(wasmBytes: ByteArray, scratchDir: File, handlers: MobileBridgeHandlers = Defaults.handlers): String {
        if (!scratchDir.exists()) scratchDir.mkdirs()
        val runId = UUID.randomUUID().toString().substring(0, 8)
        val stdoutFile = File(scratchDir, "wasm-stdout-$runId.txt")
        val stderrFile = File(scratchDir, "wasm-stderr-$runId.txt")
        try {
            var thrown: Throwable? = null
            try {
                runWasm(wasmBytes, stdoutFile.toPath(), stderrFile.toPath(), scratchDir.toPath(), handlers)
            } catch (t: Throwable) {
                thrown = t
            }
            val stdout = if (stdoutFile.exists()) String(Files.readAllBytes(stdoutFile.toPath()), StandardCharsets.UTF_8) else ""
            val stderr = if (stderrFile.exists()) String(Files.readAllBytes(stderrFile.toPath()), StandardCharsets.UTF_8) else ""
            if (stdout.isNotEmpty()) {
                System.out.println("[WasiHost] .wasm stdout (${stdout.length} bytes):\n$stdout")
            }
            if (stderr.isNotEmpty()) {
                System.err.println("[WasiHost] .wasm stderr (${stderr.length} bytes):\n$stderr")
            }
            if (thrown != null) throw thrown
            return stdout
        } finally {
            try { stdoutFile.delete() } catch (_: Throwable) {}
            try { stderrFile.delete() } catch (_: Throwable) {}
        }
    }

    private fun runWasm(wasmBytes: ByteArray, stdoutFile: Path, stderrFile: Path, preopenDir: Path, handlers: MobileBridgeHandlers) {
        val b = WasmtimeBindings.INSTANCE

        // ── Engine + config (component-model on) ────────────────────────
        val config = b.wasm_config_new()
        b.wasmtime_config_wasm_component_model_set(config, 1.toByte())
        val engine = b.wasm_engine_new_with_config(config)
            ?: error("wasm_engine_new_with_config returned null")

        // ── Store + context + WASI config ───────────────────────────
        val store = b.wasmtime_store_new(engine, Pointer.NULL, Pointer.NULL)
            ?: error("wasmtime_store_new returned null")

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
                // Preopen the scratch dir as "." — Mono needs writable filesystem
                // access for ICU data and assembly auxiliary files. On JVM,
                // this is an ephemeral temp dir (same dir as the stdout/stderr
                // files). On Android, this is context.cacheDir.
                // Permission bits: READ=1, WRITE=2 → 3 = READ|WRITE.
                if (!b.wasi_config_preopen_dir(wasiConfig, preopenDir.toAbsolutePath().toString(), ".", 3L, 3L)) {
                    error("wasi_config_preopen_dir failed for $preopenDir")
                }
                // Set argv[0] = program name. Mono's WASM_SINGLE_FILE path
                // doesn't strictly need argv[1+] (it uses
                // dotnet_wasi_getentrypointassemblyname()), but argv[0] is
                // conventionally set to the .wasm filename.
                if (!b.wasi_config_set_argv(wasiConfig, 1L, arrayOf("BlazorNative.WasiHost.wasm"))) {
                    error("wasi_config_set_argv failed")
                }
                // Phase 2.3 env-var bridge: pass the host's platform-info JSON
                // through BLAZOR_PLATFORM_INFO; .NET reads it via
                // Environment.GetEnvironmentVariable for the [BOOT] bridge-ok
                // self-test (see WasiEntryPoint.cs Main).
                val platformInfo = handlers.platformInfo()
                if (!b.wasi_config_set_env(
                        wasiConfig, 1L,
                        arrayOf("BLAZOR_PLATFORM_INFO"),
                        arrayOf(platformInfo))) {
                    error("wasi_config_set_env failed")
                }
                val setWasiErr = b.wasmtime_context_set_wasi(context, wasiConfig)
                if (setWasiErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_context_set_wasi", setWasiErr)

                // ── Component ───────────────────────────────────────────
                val componentOut = arrayOfNulls<Pointer>(1)
                val compErr = b.wasmtime_component_new(engine, wasmBytes, wasmBytes.size.toLong(), componentOut)
                if (compErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_component_new", compErr)
                val component = componentOut[0] ?: error("wasmtime_component_new returned null component without error")

                // ── Linker (add WASIp2 — no custom imports for Phase 2.1) ──
                val linker = b.wasmtime_component_linker_new(engine)
                    ?: error("wasmtime_component_linker_new returned null")

                val addWasiErr = b.wasmtime_component_linker_add_wasip2(linker)
                if (addWasiErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_component_linker_add_wasip2", addWasiErr)

                // ── Instantiate ─────────────────────────────────
                val instance = ComponentInstance()
                val instErr = b.wasmtime_component_linker_instantiate(linker, context, component, instance)
                if (instErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_component_linker_instantiate", instErr)

                // ── Look up wasi:cli/run.run ────────────────────
                val runInstanceIdx = findExportIndex(component, null, "wasi:cli/run@0.2.0")
                    ?: findExportIndex(component, null, "wasi:cli/run")
                    ?: error("could not find wasi:cli/run instance export on component")
                val runFuncIdx = findExportIndex(component, runInstanceIdx, "run")
                    ?: error("could not find 'run' function within wasi:cli/run instance")

                val func = ComponentFunc()
                val found = b.wasmtime_component_instance_get_func(instance, context, runFuncIdx, func)
                if (!found) error("wasmtime_component_instance_get_func returned false for wasi:cli/run.run")

                // ── Call run() ──────────────────────────
                // wasi:cli/run.run signature: () -> result<_,_>
                // 1-element results buffer; wasmtime_component_val_t is a tagged
                // union, 128 bytes is conservative.
                val results = com.sun.jna.Memory(128L)
                results.clear()
                val callErr = b.wasmtime_component_func_call(
                    func, context, null, 0L, results, 1L
                )
                if (callErr != null) throw WasmtimeException.fromErrorPointer("wasmtime_component_func_call", callErr)

                // ── Cleanup intentionally skipped — see BACKLOG ──────────
                // wasmtime_component_linker_delete + cascading deletes
                // crash with SIGABRT (Scudo "corrupted chunk header") on
                // Android's hardened allocator. Same call sequence works
                // fine on Windows. The wasm execution completes BEFORE the
                // crash (verified: all 4 [BOOT] markers captured to
                // stdoutFile). For Phase 2.2 GREEN CHECKPOINT we accept the
                // process-scoped leak — the test process exits immediately
                // after; the MainActivity user can restart the app.
                //
                // Phase 2.2b backlog item: investigate why libwasmtime v45's
                // Linker::drop fails Scudo validation on Android x86_64.
                // Possible angles: Mono-AOT'd .wasm holds Arcs that wasmtime
                // double-decrements; wasmtime's TLS cleanup ordering differs
                // on Bionic vs glibc; cargo-ndk build flags missing some
                // Android-specific allocator wiring.
                //
        // Skipping in deliberate creation-reverse order: would have
        // been linker → component → store → engine. All leaked.
    }

    private fun findExportIndex(component: Pointer, instanceIdx: Pointer?, name: String): Pointer? {
        val nameBytes = name.toByteArray(StandardCharsets.UTF_8)
        return WasmtimeBindings.INSTANCE.wasmtime_component_get_export_index(
            component, instanceIdx, nameBytes, nameBytes.size.toLong()
        )
    }
}
