// ─────────────────────────────────────────────────────────────────────────────
// BnLogTests — Phase 11.4 Gate C (M11 DoD #6, #155): the iOS logging seam's own
// pins.
//
// WHAT THIS CAN AND CANNOT ASSERT, said plainly because logging is the classic
// "green test, unread output" trap:
//
//  · IT CAN assert the LEVEL GATE, the ordinal→threshold map, the line format's
//    round trip, and the stderr pump's line splitting. All four are pure
//    functions over values, and all four are places a silent bug would live.
//  · IT CANNOT assert that a line reached the unified log, or that a Release
//    build is quiet. `os_log` has no readback API; proving quietness means
//    watching Console.app during a simulator run, which design §8.3 classes as
//    inspection-only and this file does not pretend otherwise.
//  · IT DOES NOT install the stderr pump. `dup2` over fd 2 is process-global and
//    irreversible (design R2), and this bundle is HOSTED — it runs inside the
//    BnHost app process that xcodebuild is reading stdio from. Capturing fd 2
//    here would redirect the test runner's own output. So the pump's pure half is
//    driven directly, which is exactly the "assert against the injected sink, not
//    against the platform log" posture design §8.1 pin 5 demands of the Android
//    twin.
//
// The C# twins live in BnLogTests / BnLogFormatDriftTests (tests/BlazorNative.Runtime.Tests);
// the drift pins there READ THIS SHELL'S SOURCE and hold the three copies of the
// line format equal.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnLogTests: XCTestCase {

    override func tearDown() {
        // The threshold is process-global. A test that raised it and did not put it
        // back would leak verbosity into every subsequent test in the bundle — and,
        // worse, would make a level-gate assertion pass for the wrong reason.
        BnLog.setLevelFromOrdinal(BnLogLevel.unset)
        super.tearDown()
    }

    /// THE GATE: a message is emitted at or ABOVE the threshold in severity, i.e.
    /// at a numerically LOWER-OR-EQUAL ordinal. The direction is the thing worth
    /// pinning — inverting it would make Release ship Verbose and Debug builds
    /// silent, and both failures look like "logging is a bit odd" rather than a bug.
    func testTheGateEmitsAtOrAboveTheThreshold() {
        BnLog.setLevelFromOrdinal(BnLogLevel.warn)
        XCTAssertTrue(BnLog.isEnabled(BnLogLevel.error), "errors ship at the Warn default")
        XCTAssertTrue(BnLog.isEnabled(BnLogLevel.warn), "warnings ship at the Warn default")
        XCTAssertFalse(BnLog.isEnabled(BnLogLevel.info), "boot narration must NOT ship in Release")
        XCTAssertFalse(BnLog.isEnabled(BnLogLevel.debug))
        XCTAssertFalse(BnLog.isEnabled(BnLogLevel.verbose))

        BnLog.setLevelFromOrdinal(BnLogLevel.verbose)
        XCTAssertTrue(BnLog.isEnabled(BnLogLevel.verbose), "Verbose ships everything")
        XCTAssertTrue(BnLog.isEnabled(BnLogLevel.error))

        BnLog.setLevelFromOrdinal(BnLogLevel.error)
        XCTAssertTrue(BnLog.isEnabled(BnLogLevel.error))
        XCTAssertFalse(BnLog.isEnabled(BnLogLevel.warn), "Error suppresses even warnings")

        // `unset` is never a MESSAGE level — the gate rejects it outright, so a
        // caller that passed 0 through cannot accidentally emit.
        XCTAssertFalse(BnLog.isEnabled(BnLogLevel.unset))
    }

    /// THE WIRE DECODE: ordinal 0 and anything out of range resolve to the default
    /// (Warn), never to silence and never to Verbose.
    ///
    /// This is what makes the offset-28 field back-compatible: a shell that
    /// predates it leaves the tail padding zero, and zero must mean "apply the
    /// default". The dangerous direction is the quiet one — an out-of-range value
    /// mapping to 0/off would silently disable diagnostics on a device nobody is
    /// watching.
    func testAnUnsetOrOutOfRangeOrdinalResolvesToTheDefault() {
        for ordinal: Int32 in [0, -1, 6, 99, Int32.min, Int32.max] {
            BnLog.setLevelFromOrdinal(ordinal)
            XCTAssertEqual(BnLog.level, BnLog.defaultLevel,
                           "ordinal \(ordinal) must resolve to the default, got \(BnLog.level)")
        }
        XCTAssertEqual(BnLog.defaultLevel, BnLogLevel.warn,
                       "the Release default is Warn — errors and dropped wires ship, narration does not")

        // …and a legal ordinal is honoured verbatim.
        for ordinal in [BnLogLevel.error, BnLogLevel.warn, BnLogLevel.info,
                        BnLogLevel.debug, BnLogLevel.verbose] {
            BnLog.setLevelFromOrdinal(ordinal)
            XCTAssertEqual(BnLog.level, ordinal)
        }
    }

    /// THE NAME KNOB: `BN_LOG_LEVEL` / Info.plist strings map case-insensitively,
    /// and A TYPO RESOLVES TO `unset` — i.e. the default — NOT to silence. A
    /// misspelled Info.plist entry that turned logging off would be discovered in
    /// the field, by a stranger, at the worst moment.
    func testAnUnrecognisedLevelNameResolvesToUnsetAndNotToSilence() {
        XCTAssertEqual(BnLogLevel.fromName("Debug"), BnLogLevel.debug)
        XCTAssertEqual(BnLogLevel.fromName("  vErBoSe  "), BnLogLevel.verbose)
        XCTAssertEqual(BnLogLevel.fromName("warning"), BnLogLevel.warn, "the Android spelling too")
        XCTAssertEqual(BnLogLevel.fromName("trace"), BnLogLevel.verbose)

        XCTAssertEqual(BnLogLevel.fromName("Debgu"), BnLogLevel.unset, "a typo → the default")
        XCTAssertEqual(BnLogLevel.fromName(""), BnLogLevel.unset)
        XCTAssertEqual(BnLogLevel.fromName(nil), BnLogLevel.unset)
        XCTAssertEqual(BnLogLevel.fromName("off"), BnLogLevel.unset,
                       "'off' is NOT a level — it must not silence the shell")
    }

    /// THE LINE FORMAT ROUND-TRIPS, for every level (design R1).
    ///
    /// `BlazorNative.Core.BnLog.FormatLine` writes these lines and this parser
    /// reads them back off fd 2. The cross-language half is pinned from the .NET
    /// suite (BnLogFormatDriftTests reads this file's constants); this is the
    /// SAME-language half, which is what proves the parser is self-consistent
    /// before the drift pin compares it to anything.
    func testEveryLevelRoundTripsThroughTheLineFormat() {
        let category = "BnWidgetMapper"
        let message = "SetStyle fontSize ignored: node 12 is not a UILabel"

        for level in [BnLogLevel.error, BnLogLevel.warn, BnLogLevel.info,
                      BnLogLevel.debug, BnLogLevel.verbose] {
            let line = BnLogFormat.format(level, category, message)
            let parsed = BnLogFormat.parse(line)
            XCTAssertEqual(parsed, BnLogRecord(level: level, category: category, message: message),
                           "round trip failed for ordinal \(level): '\(line)'")
        }

        // An empty message is still ours — FormatLine emits the bracket and one space.
        XCTAssertEqual(BnLogFormat.parse(BnLogFormat.format(BnLogLevel.error, "BnRuntime", "")),
                       BnLogRecord(level: BnLogLevel.error, category: "BnRuntime", message: ""))
    }

    /// THE FALLBACK IS A DECISION, NOT A DEFAULT (design §5.5): a line without the
    /// prefix is NOT DROPPED.
    ///
    /// That output — the BCL's, NativeAOT's `TypeLoadException` detail, a
    /// third-party native library's — is precisely what this transport exists to
    /// rescue and the half no bridge-based option could ever have carried. It
    /// arrives at Warn under `native`, VERBATIM.
    func testAnUnprefixedLineFallsBackToWarnUnderTheNativeCategory() {
        let raw = "Unhandled exception. System.TypeLoadException: Could not load type 'X'"
        XCTAssertEqual(BnLogFormat.parse(raw),
                       BnLogRecord(level: BnLogFormat.fallbackLevel,
                                   category: BnLogFormat.fallbackCategory,
                                   message: raw))
        XCTAssertEqual(BnLogFormat.fallbackLevel, BnLogLevel.warn)
        XCTAssertEqual(BnLogFormat.fallbackCategory, "native")

        // Malformed near-misses fall back too, rather than parsing into nonsense.
        for malformed in ["[BN|", "[BN|E", "[BN|E|", "[BN|Z|cat] hi", "[BN|E|] hi", "[BNE|cat] hi"] {
            XCTAssertEqual(BnLogFormat.parse(malformed).category, BnLogFormat.fallbackCategory,
                           "'\(malformed)' must fall back, not half-parse")
        }
    }

    /// THE PUMP SPLITS ON NEWLINES AND BUFFERS PARTIAL LINES.
    ///
    /// `Console.Error.WriteLine` auto-flushes, so lines arrive whole IN PRACTICE —
    /// but a read boundary can still land mid-line, and a naive read → decode →
    /// split would emit two half-lines with THE SECOND LOSING ITS PREFIX, and
    /// therefore its level. That is a silent severity downgrade on exactly the
    /// lines that matter, so the buffering is pinned rather than trusted.
    func testThePumpSplitsLinesAndBuffersPartialOnes() {
        var seen: [BnLogRecord] = []
        let original = BnStderrPump.sink
        BnStderrPump.sink = { seen.append($0) }
        defer { BnStderrPump.sink = original }

        var pending = Data()
        var overflowed = false

        // One complete line, then a chunk that CUTS THE NEXT ONE mid-prefix.
        BnStderrPump.drain(Data("[BN|E|BnRuntime] boom\n[BN|W|Bn".utf8),
                           pending: &pending, overflowed: &overflowed)
        XCTAssertEqual(seen.count, 1, "only the complete line may be emitted")
        XCTAssertEqual(seen[0], BnLogRecord(level: BnLogLevel.error, category: "BnRuntime", message: "boom"))

        // The rest of the cut line arrives; it must be reassembled BEFORE parsing,
        // or its level and category are lost.
        BnStderrPump.drain(Data("WidgetMapper] dropped\n".utf8),
                           pending: &pending, overflowed: &overflowed)
        XCTAssertEqual(seen.count, 2)
        XCTAssertEqual(seen[1], BnLogRecord(level: BnLogLevel.warn,
                                            category: "BnWidgetMapper", message: "dropped"))

        // CRLF is tolerated, and a bare newline is not a log line.
        BnStderrPump.drain(Data("\n[BN|I|BnRuntime] mounted\r\n".utf8),
                           pending: &pending, overflowed: &overflowed)
        XCTAssertEqual(seen.count, 3, "a bare newline must not become an entry")
        XCTAssertEqual(seen[2].message, "mounted")
    }
}
