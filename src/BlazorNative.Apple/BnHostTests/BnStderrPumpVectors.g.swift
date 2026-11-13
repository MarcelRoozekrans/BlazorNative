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

/// One stderr line and the record the pump must forward for it. Levels are BnLogLevel ordinals.
struct BnStderrPumpVector {
    let name: String
    let shellLevel: Int32
    let line: String
    let level: Int32
    let category: String
    let message: String
}

/// The stderr pump cases, generated from the manifest. The DATA only.
enum BnStderrPumpVectors {
    /// Every case. Each expects the line to be forwarded: the managed level is the only gate.
    static let all: [BnStderrPumpVector] = [
        // a Warn line passes a shell set to Error: the managed level is the only gate
        BnStderrPumpVector(name: "warn-at-error", shellLevel: 1, line: "[BN|W|boot] slow start", level: 2, category: "boot", message: "slow start"),
        // an unprefixed line is a native fault: forwarded whole at Warn whatever the shell level
        BnStderrPumpVector(name: "unprefixed-at-error", shellLevel: 1, line: "Unhandled exception. System.TypeLoadException: x", level: 2, category: "native", message: "Unhandled exception. System.TypeLoadException: x"),
        // a Debug line passes a shell set to Warn
        BnStderrPumpVector(name: "debug-at-warn", shellLevel: 2, line: "[BN|D|dispatch] lane idle", level: 4, category: "dispatch", message: "lane idle"),
        // an Error line is forwarded at Error even on a Verbose shell
        BnStderrPumpVector(name: "error-at-verbose", shellLevel: 5, line: "[BN|E|render] patch failed", level: 1, category: "render", message: "patch failed"),
    ]
}
