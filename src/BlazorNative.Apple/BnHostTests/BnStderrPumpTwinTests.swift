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
        resetCaptured()
        BnLog.setLevelFromOrdinal(BnLog.defaultLevel)
        super.tearDown()
    }

    /// The hook runs on whichever thread logs (BnLog.swift documents this), and the
    /// test is hosted in the app process, where earlier tests leave background
    /// queues. So the buffer is locked and only this test's categories are kept: a
    /// stray line from another category must not turn the count assertions red.
    private let capturedLock = NSLock()
    private var capturedStorage: [(Int32, String, String)] = []

    private func resetCaptured() {
        capturedLock.lock(); defer { capturedLock.unlock() }
        capturedStorage.removeAll()
    }

    private func snapshotCaptured() -> [(Int32, String, String)] {
        capturedLock.lock(); defer { capturedLock.unlock() }
        return capturedStorage
    }

    func testEveryVectorIsForwardedWhateverTheShellLevel() {
        XCTAssertFalse(BnStderrPumpVectors.all.isEmpty, "no vectors ran: the generated table is empty")

        for v in BnStderrPumpVectors.all {
            resetCaptured()
            BnLog.setLevelFromOrdinal(v.shellLevel)
            BnLog.emitHookForTest = { [weak self] level, category, message in
                guard let self = self, category == "control" || category == v.category else { return }
                self.capturedLock.lock()
                self.capturedStorage.append((level, category, message))
                self.capturedLock.unlock()
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
                snapshotCaptured().count, 1,
                "shell level \(v.shellLevel) was not applied: a gated line at it did not reach the hook")
            // 3. Negative: a gated line one below the threshold is dropped. There is
            //    nothing below Verbose.
            resetCaptured()
            if v.shellLevel < BnLogLevel.verbose {
                BnLog.write(v.shellLevel + 1, "control", "gated")
                XCTAssertTrue(
                    snapshotCaptured().isEmpty,
                    "shell level \(v.shellLevel) was not applied: a gated line below it reached the hook")
            }
            // The collector must hold only the forwarded record from here on.
            resetCaptured()

            BnStderrPump.forward(BnLogFormat.parse(v.line))

            let forwarded = snapshotCaptured()
            XCTAssertEqual(
                forwarded.count, 1,
                "vector \(v.name): the pump dropped or re-levelled a line .NET already gated")
            guard let record = forwarded.first else { continue }
            XCTAssertEqual(record.0, v.level, "vector \(v.name): wrong level")
            XCTAssertEqual(record.1, v.category, "vector \(v.name): wrong category")
            XCTAssertEqual(record.2, v.message, "vector \(v.name): wrong message")
        }
    }
}
