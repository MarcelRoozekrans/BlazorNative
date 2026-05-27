package io.blazornative.jni

import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assertions.assertEquals
import java.io.File
import java.nio.charset.StandardCharsets
import java.nio.file.Files
import java.nio.file.Paths
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import kotlin.concurrent.thread

/**
 * Phase 2.4 streaming spike — Rung 1: tee'd stdout.
 *
 * Hypothesis: wasi_config_set_stdout_file flushes line-by-line; a background
 * thread polling the file every 10ms can observe [FRAME] lines before
 * wasmtime_component_func_call returns.
 *
 * Mechanism: drive the BLAZOR_STREAMING_SPIKE=1 code path in Main so a second
 * [FRAME] line emits 200ms after the first. Background poller records the
 * timestamp at which each new line first appears; main thread records the
 * wall time when the wasmtime process exits. Pass = at least one [FRAME] line
 * observed before run-completion timestamp.
 *
 * This test uses the wasmtime CLI subprocess (file-redirected stdout) — the
 * cheapest possible variant of the in-process JNA flow used by WasiHost. If
 * stdout streams here, it'll stream when WasiHost uses the same file-backed
 * stdout config; if it doesn't, the in-process path won't either.
 */
class StreamingSpike_Rung1Test {

    @Test
    fun teed_stdout_observes_frame_lines_before_func_call_returns() {
        val wasmPath = Paths.get(System.getProperty("wasm.path"))
        val stdoutFile = File.createTempFile("spike-rung1-", ".txt")
        try {
            val wasmtimeExe = resolveWasmtimeExe()
            val cmd = listOf(
                wasmtimeExe.absolutePath,
                "--dir=.",
                "--env", "BLAZOR_PLATFORM_INFO={\"os\":\"spike\"}",
                "--env", "BLAZOR_STREAMING_SPIKE=1",
                wasmPath.toString()
            )
            val pb = ProcessBuilder(cmd)
                .redirectOutput(stdoutFile)
                .redirectErrorStream(true)

            val firstFrameTimestamp = AtomicLong(0L)
            val secondFrameTimestamp = AtomicLong(0L)
            val stop = AtomicBoolean(false)
            val poller = thread(name = "rung1-poller") {
                while (!stop.get()) {
                    if (stdoutFile.exists() && stdoutFile.length() > 0) {
                        val text = String(Files.readAllBytes(stdoutFile.toPath()), StandardCharsets.UTF_8)
                        val frameCount = text.split("[FRAME]").size - 1
                        if (frameCount >= 1 && firstFrameTimestamp.get() == 0L) {
                            firstFrameTimestamp.set(System.nanoTime())
                        }
                        if (frameCount >= 2 && secondFrameTimestamp.get() == 0L) {
                            secondFrameTimestamp.set(System.nanoTime())
                        }
                    }
                    Thread.sleep(10)
                }
            }

            val process = pb.start()
            val exit = process.waitFor()
            val runCompletionTimestamp = System.nanoTime()
            stop.set(true)
            poller.join(1000)

            val capturedStdout = String(Files.readAllBytes(stdoutFile.toPath()), StandardCharsets.UTF_8)
            println("[Rung 1] exit=$exit captured stdout length=${capturedStdout.length}")
            println("[Rung 1] firstFrame=${firstFrameTimestamp.get()} secondFrame=${secondFrameTimestamp.get()} runComplete=$runCompletionTimestamp")
            println("[Rung 1] stdout follows:\n$capturedStdout")

            assertEquals(0, exit, "wasmtime exited non-zero: $exit")
            assertTrue(capturedStdout.contains("[FRAME]"), "no [FRAME] in captured stdout — fixture broken")
            assertTrue(capturedStdout.contains("[BOOT] spike-second-frame-emitted"),
                "spike second mount didn't fire — env var not propagating")

            assertTrue(firstFrameTimestamp.get() > 0L,
                "Rung 1 FAIL: poller never observed [FRAME] line — stdoutFile may be buffer-flushed only on exit")

            // The KEY assertion: did we see a frame BEFORE the process finished?
            val firstFrameRelToRunComplete = firstFrameTimestamp.get() - runCompletionTimestamp
            assertTrue(firstFrameTimestamp.get() < runCompletionTimestamp,
                "Rung 1 FAIL: poller observed [FRAME] AT or AFTER run completion " +
                "(delta=${firstFrameRelToRunComplete / 1_000_000}ms) — stdoutFile buffers, " +
                "tee'd file approach does NOT support streaming")
        } finally {
            stdoutFile.delete()
        }
    }

    private fun resolveWasmtimeExe(): File {
        // setup.ps1 puts wasmtime.exe in C:\Tools; CI / other dev machines may
        // have it elsewhere — check a small set of known locations.
        val pathCandidates = sequenceOf(
            File("C:\\Tools\\wasmtime-v45.0.0-x86_64-windows\\wasmtime.exe"),
            File(System.getProperty("jna.library.path"), "wasmtime.exe"),
            File("C:\\Users\\MarcelRoozekrans\\.cargo\\bin\\wasmtime.exe")
        )
        return pathCandidates.firstOrNull { it.exists() }
            ?: error("wasmtime.exe not found in known locations")
    }
}
