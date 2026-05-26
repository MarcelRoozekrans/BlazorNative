package io.blazornative.jni

/**
 * Phase 2.3 mobile_bridge host handler infrastructure (revised C-1 path).
 *
 * The original Phase 2.3 design called for WIT-typed imports satisfied via
 * `wasmtime_component_linker_define_func`. Task 2 (commit 3aa83c9) found
 * three wasi-experimental SDK gaps that block that path today. The revised
 * design (`docs/plans/2026-05-26-phase-2.3-design-revision.md`) uses the
 * standard `wasi:cli/environment` interface instead — host passes JSON via
 * an environment variable, .NET reads it via Environment.GetEnvironmentVariable.
 *
 * MobileBridgeHandlers holds per-call host implementations. Each WasiHost
 * caller (JVM tests, Android MainActivity) provides their own handlers —
 * Defaults.handlers is the JVM-stub fallback for tests that don't need real data.
 *
 * Phase 2.3 has just `platformInfo`; future imports add fields per
 * docs/BACKLOG.md's deferred list (Phase 2.5 navigate/storage, M4+ fetch).
 * Dynamic bridges (runtime event callbacks) wait for the export-based pattern
 * — env vars are initialization-time only.
 */
data class MobileBridgeHandlers(
    val platformInfo: () -> String
)

/**
 * Stub defaults — used when no MobileBridgeHandlers is explicitly passed
 * to WasiHost.loadAndRun. Returns a fixed JVM-stub JSON so BootSmokeTest
 * (JVM unit test) can assert on a known marker shape without needing to
 * pass a real implementation.
 */
object Defaults {
    val handlers = MobileBridgeHandlers(
        platformInfo = { """{"os":"jvm-default","note":"stub-host"}""" }
    )
}
