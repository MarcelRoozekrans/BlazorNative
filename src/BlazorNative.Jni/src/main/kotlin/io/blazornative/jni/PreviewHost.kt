package io.blazornative.jni

import com.sun.jna.Memory
import kotlin.system.exitProcess

/**
 * Phase 4.3 Gate 1 — the repo's first JVM `main()`: the devloop fast-lane
 * surface. Boots the NativeAOT dll via [BlazorNativeRuntime], mounts one
 * component (args[0], default "BnDemo"), drains the mount frames into a
 * [TreeSnapshot], prints the indented tree + per-stage timings, and exits —
 * run it via `gradlew runPreviewHost [-Pcomponent=Name]`.
 *
 * FAST-RESTART, NOT HOT-RELOAD (the design's survey-proven constraint): JNA's
 * Native.load is process-lifetime — there is no unload API and Windows locks
 * the dll — so a warm process can never pick up a rebuilt dll. This host
 * therefore does one dump and EXITS (exit 0 on success, 1 + stderr on
 * failure); the devloop script restarts it per cycle, and nothing lingers to
 * hold the dll against the next publish.
 *
 * PLACEMENT (documented per the design's judgment call): lives in main/kotlin
 * next to the runtime classes it drives. That source set is shared with the
 * Android app, so the APK carries this file as ~1 KB of dead code — accepted:
 * a JVM-only source set inside an AGP application module would buy those KBs
 * back at the cost of a second compilation arrangement for one file.
 *
 * STAGE TIMINGS: `init` is measured by an explicit blazornative_init probe
 * BEFORE the canonical [BlazorNativeRuntime.start] boot — init is idempotent
 * (documented on start()'s recreation contract), so start()'s own init call
 * is a cheap no-op and the `mount` stage (start()'s wall time) is dominated
 * by register + mount + first-frame delivery.
 */
fun main(args: Array<String>) {
    val component = args.firstOrNull() ?: "BnDemo"
    val tStart = System.nanoTime()
    try {
        // Stage 1 — dll load: first INSTANCE touch runs Native.load against
        // jna.library.path (the win-x64 publish directory).
        val lib = NativeBindings.INSTANCE
        val tLoaded = System.nanoTime()

        // Stage 2 — init probe (idempotent; see KDoc). Keep the Memory locals
        // alive across the call (init copies, but the buffers must survive it).
        val osBytes = "preview-host".toByteArray(Charsets.UTF_8) + 0
        val osMem = Memory(osBytes.size.toLong()).apply { write(0, osBytes, 0, osBytes.size) }
        val noteBytes = "phase-4.3-previewhost".toByteArray(Charsets.UTF_8) + 0
        val noteMem = Memory(noteBytes.size.toLong()).apply { write(0, noteBytes, 0, noteBytes.size) }
        val opts = BlazorNativeInitOptions.ByReference().apply {
            platformInfoOs = osMem
            platformInfoApiLevel = 0
            platformInfoNote = noteMem
        }
        val init = lib.blazornative_init(opts)
        if (init.status != 0) {
            val err = init.errorMessage?.getString(0, "UTF-8") ?: "<no detail>"
            throw IllegalStateException("blazornative_init failed (status=${init.status}): $err")
        }
        val tInit = System.nanoTime()

        // Stage 3 — the canonical boot: register callback + mount; the mount
        // frame(s) arrive synchronously inside start() (sync-mount contract).
        val snapshot = TreeSnapshot()
        val runtime = BlazorNativeRuntime(onFrame = snapshot::apply)
        val bootLines = runtime.start(componentName = component, platformOs = "preview-host")
        val tMounted = System.nanoTime()

        bootLines.forEach(::println)
        println()
        println("[TREE] $component (${snapshot.framesApplied} frame(s))")
        println(snapshot.render())
        println()
        fun ms(from: Long, to: Long) = (to - from) / 1_000_000
        println("[TIME] dll-load ${ms(tStart, tLoaded)} ms")
        println("[TIME] init     ${ms(tLoaded, tInit)} ms")
        println("[TIME] mount    ${ms(tInit, tMounted)} ms")
        println("[TIME] total    ${ms(tStart, System.nanoTime())} ms")
        exitProcess(0)
    } catch (t: Throwable) {
        System.err.println("[PreviewHost] FAILED to mount '$component': $t")
        exitProcess(1)
    }
}
