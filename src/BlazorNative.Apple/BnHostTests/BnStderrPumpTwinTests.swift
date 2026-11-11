// ─────────────────────────────────────────────────────────────────────────────
// BnStderrPumpTwinTests — the iOS half of the #401 twin pin (18.7).
//
// The stderr pump forwards what .NET wrote, at the level .NET wrote it, WITHOUT
// re-applying the shell's level: .NET already applied the managed one. Every case
// in `BnStderrPumpVectors` (generated from src/stderr-pump-vectors.json, which the
// Kotlin suite asserts too) is pushed through the production forward path,
// `BnStderrPump.forward`, with the shell set to the vector's level.
//
// THE LEVEL CONTROL. A pump that forwards everything would also pass if the shell
// level were never applied at all, and the default level is Warn. So each vector
// proves the level took, three ways: the getter reads back the vector's level, a
// gated line AT it reaches the emit hook, and a gated line one level below the
// threshold does not. The last control does not exist at Verbose, the lowest level.
//
// Asserted against `BnLog.emitHookForTest`, which sees every record after all
// gates and before `os_log`, so the test needs no unified-log readback and never
// goes through `sink`.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnStderrPumpTwinTests: XCTestCase {

    override func tearDown() {
        // Both are process-global; a leak would change every later test's gate.
        BnLog.emitHookForTest = nil
        BnLog.setLevelFromOrdinal(BnLog.defaultLevel)
        super.tearDown()
    }

    func testEveryVectorIsForwardedWhateverTheShellLevel() {
        XCTAssertFalse(BnStderrPumpVectors.all.isEmpty, "no vectors ran: the generated table is empty")

        for v in BnStderrPumpVectors.all {
            var captured: [(Int32, String, String)] = []
            BnLog.setLevelFromOrdinal(v.shellLevel)
            BnLog.emitHookForTest = { level, category, message in
                captured.append((level, category, message))
            }

            // Level controls. A pump that forwards everything also passes if the
            // shell level was never applied, so prove the level took, three ways.
            // 1. The setter took: the getter reads back the vector's level.
            XCTAssertEqual(
                BnLog.level, v.shellLevel,
                "shell level \(v.shellLevel) was not applied: the shell's level setter did not take")
            // 2. Positive: a gated line AT the shell level reaches the hook.
            BnLog.write(v.shellLevel, "control", "gated")
            XCTAssertEqual(
                captured.count, 1,
                "shell level \(v.shellLevel) was not applied: a gated line at it did not reach the hook")
            // 3. Negative: a gated line one below the threshold is dropped. There is
            //    nothing below Verbose.
            captured.removeAll()
            if v.shellLevel < BnLogLevel.verbose {
                BnLog.write(v.shellLevel + 1, "control", "gated")
                XCTAssertTrue(
                    captured.isEmpty,
                    "shell level \(v.shellLevel) was not applied: a gated line below it reached the hook")
            }
            // The collector must hold only the forwarded record from here on.
            captured.removeAll()

            BnStderrPump.forward(BnLogFormat.parse(v.line))

            XCTAssertEqual(
                captured.count, 1,
                "vector \(v.name): the pump dropped or re-levelled a line .NET already gated")
            guard let record = captured.first else { continue }
            XCTAssertEqual(record.0, v.level, "vector \(v.name): wrong level")
            XCTAssertEqual(record.1, v.category, "vector \(v.name): wrong category")
            XCTAssertEqual(record.2, v.message, "vector \(v.name): wrong message")
        }
    }
}
