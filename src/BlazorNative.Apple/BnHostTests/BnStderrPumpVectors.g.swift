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
    static let all: [BnStderrPumpVector] = []
}
