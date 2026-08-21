// ─────────────────────────────────────────────────────────────────────────────
// BnDeepLinkVectorTests — the iOS half of the deep-link parity harness (#278).
//
// Every case comes from `src/deeplink-vectors.json` via `BnDeepLinkVectors.g.swift`.
// The Kotlin (`BnDeepLinkVectorTest`) and .NET (`DeepLinkVectorTests`) suites assert
// the SAME table — that is the whole point. The URL→route parse is hand-written per
// shell (`BnDeepLink.route(from:)` here, `MainActivity.parseDeepLinkRoute` there),
// because you cannot generate a parser from a manifest; what you CAN generate is the
// table it must satisfy. The two parsers disagreed on every multi-segment route for
// months and nothing compared them.
//
// ── WHY THIS IS A SEPARATE FILE FROM BnDeepLinkTests ─────────────────────────
// BnDeepLinkTests keeps the iOS-SPECIFIC pins that have no Android twin and no place
// in a shared table: that the bundle actually declares `CFBundleURLTypes` (without
// which the handler is unreachable dead code), the warm-vs-cold arrival branch, and
// the cold-launch component resolution. Those are not URL→route facts and folding
// them into a generated table would either bloat it with iOS-only rows or, worse,
// tempt someone to delete them. This file holds exactly the shared contract; that
// one holds everything else.
//
// The overlap with BnDeepLinkTests' parsing section is deliberate and not
// redundancy: those assertions name their cases inline and would keep passing if the
// manifest were gutted, so they cannot stand in for the table. This suite fails the
// moment the shared table and this parser disagree, which is the only failure the
// harness exists to produce.
//
// The loop below would pass trivially over an EMPTY table, and that vacuity is
// guarded — but not here. `WireVocabularyCodegenTests` (the .NET build-test lane,
// required on every PR) floors the manifest at seven cases AND byte-compares this
// bundle's `BnDeepLinkVectors.g.swift` against what the emitter produces from it, so
// a gutted or stale Swift table reds there, on a lane that always runs, rather than
// waiting for a macOS runner. A local emptiness check here would be a second, weaker
// copy of a pin that already exists.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnDeepLinkVectorTests: XCTestCase {

    /// The whole harness, in one loop. Deliberately NOT a parameterised set of
    /// individual test methods: a case is added by editing the manifest and running
    /// WireGen, and a shape that needed a hand-written method per row would put the
    /// suite back in the business of listing cases — which is the state the generated
    /// table replaced.
    func testEveryVectorParsesToTheSharedExpectation() {
        for vector in BnDeepLinkVectors.all {
            // NOT `URL(string:)!`. A malformed vector is a bug in the manifest, and a
            // force-unwrap would report it as a crash of the whole test runner rather
            // than as a named failure naming the offending row — and it would take the
            // remaining vectors' results with it.
            guard let url = URL(string: vector.url) else {
                XCTFail("deep-link vector is not a URL: \(vector.url)")
                continue
            }
            XCTAssertEqual(BnDeepLink.route(from: url), vector.route,
                           "deep-link vector: \(vector.url)")
        }
    }
}
