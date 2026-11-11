// GENERATED FILE — DO NOT EDIT (18.7).
//
// Source of truth: src/stderr-pump-vectors.json
// Regenerate:      dotnet run --project tools/BlazorNative.WireGen
//
// One stderr line -> forwarded record case, written ONCE in the manifest and
// asserted by the Kotlin JVM suite and the Swift XCTest suite. Editing this
// file by hand puts one pump's expectations out of step with the other's;
// WireVocabularyCodegenTests re-runs the emitter and byte-compares, so the
// edit fails the required build-test lane rather than reaching a device.

package io.blazornative.shell

/** One stderr line and the record the pump must forward for it. Levels are BnLogLevel ordinals. */
data class BnStderrPumpVector(
    val name: String,
    val shellLevel: Int,
    val line: String,
    val level: Int,
    val category: String,
    val message: String,
)

/** The stderr pump cases, generated from the manifest. The DATA only. */
object BnStderrPumpVectors {
    /** Every case. Each expects the line to be forwarded: the managed level is the only gate. */
    val all: List<BnStderrPumpVector> = listOf(
        // a Warn line passes a shell set to Error: the managed level is the only gate
        BnStderrPumpVector("warn-at-error", 1, "[BN|W|boot] slow start", 2, "boot", "slow start"),
        // an unprefixed line is a native fault: forwarded whole at Warn whatever the shell level
        BnStderrPumpVector("unprefixed-at-error", 1, "Unhandled exception. System.TypeLoadException: x", 2, "native", "Unhandled exception. System.TypeLoadException: x"),
        // a Debug line passes a shell set to Warn
        BnStderrPumpVector("debug-at-warn", 2, "[BN|D|dispatch] lane idle", 4, "dispatch", "lane idle"),
        // an Error line is forwarded at Error even on a Verbose shell
        BnStderrPumpVector("error-at-verbose", 5, "[BN|E|render] patch failed", 1, "render", "patch failed"),
    )
}
