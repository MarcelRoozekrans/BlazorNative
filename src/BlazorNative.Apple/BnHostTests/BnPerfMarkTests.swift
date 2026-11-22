// ─────────────────────────────────────────────────────────────────────────────
// BnPerfMarkTests — 19.2 Task 6. The Verbose gate, the line format and the once-flag of
// BnPerfMark, the iOS twin of BnPerfMarkTest.kt.
//
// Every test uses the point "unit.test" or its own once-flag, so a mark another test's
// runtime writes on a lane thread cannot reach these assertions: the sink is filtered to
// that point. DOES NOT COVER: the sysctl process-start read, which only runs on a device or
// simulator and is not asserted here beyond not crashing; the placement of the marks in
// BnRuntime and BnWidgetMapper, which the perf run measures.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnPerfMarkTests: XCTestCase {

    private let point = "unit.test"
    private var lines: [String] = []
    private let linesLock = NSLock()
    private var clockReads = 0
    private var testThread: Thread!

    override func setUp() {
        super.setUp()
        lines = []
        clockReads = 0
        // The clock counts reads made on this thread only: a runtime thread left over from an
        // earlier test could mark at Verbose and bump a process-wide counter.
        testThread = Thread.current
        BnPerfMark.sinkForTest = { [unowned self] line in
            guard line.hasPrefix("BNPERF \(self.point) ") else { return }
            self.linesLock.lock(); self.lines.append(line); self.linesLock.unlock()
        }
        BnPerfMark.clockForTest = { [unowned self] in
            if Thread.current === self.testThread {
                self.linesLock.lock(); self.clockReads += 1; self.linesLock.unlock()
            }
            return 4242
        }
    }

    override func tearDown() {
        BnPerfMark.sinkForTest = nil
        BnPerfMark.clockForTest = nil
        BnLog.setLevelFromOrdinal(BnLogLevel.unset)
        super.tearDown()
    }

    func testMarkAtWarnReadsNoClockAndWritesNothing() {
        BnLog.setLevelFromOrdinal(BnLogLevel.warn)
        BnPerfMark.mark(point)
        BnPerfMark.mark(point, id: 7)
        BnPerfMark.bootStart()
        BnPerfMark.bootContentOnce(once: BnOnceFlag())
        XCTAssertEqual(clockReads, 0, "below Verbose a mark must not read the clock")
        XCTAssertTrue(lines.isEmpty, "below Verbose a mark must write nothing, got \(lines)")
    }

    func testMarkAtVerboseWritesOneLine() {
        BnLog.setLevelFromOrdinal(BnLogLevel.verbose)
        BnPerfMark.mark(point)
        BnPerfMark.mark(point, id: 9)
        XCTAssertEqual(clockReads, 2)
        XCTAssertEqual(lines.count, 2)
        let first = lines[0].split(separator: " ").map(String.init)
        XCTAssertEqual(first.count, 4, "no id field when id is 0: \(lines[0])")
        XCTAssertEqual(first[0], "BNPERF")
        XCTAssertEqual(first[1], point)
        XCTAssertTrue(first[2].hasPrefix("seq="))
        XCTAssertEqual(first[3], "t=4242")
        let seq1 = Int(first[2].dropFirst(4))
        XCTAssertNotNil(seq1)
        let second = lines[1].split(separator: " ").map(String.init)
        XCTAssertEqual(second.count, 5, "an id field when id is not 0: \(lines[1])")
        XCTAssertEqual(second[4], "id=9")
        XCTAssertEqual(Int(second[2].dropFirst(4)), seq1.map { $0 + 1 }, "seq counts per point")
    }

    func testBootContentFiresOnce() {
        BnLog.setLevelFromOrdinal(BnLogLevel.verbose)
        var contentLines: [String] = []
        BnPerfMark.sinkForTest = { line in
            if line.hasPrefix("BNPERF boot.content ") { contentLines.append(line) }
        }
        let once = BnOnceFlag()
        XCTAssertTrue(BnPerfMark.bootContentPending(once: once))
        BnPerfMark.bootContentOnce(once: once)
        BnPerfMark.bootContentOnce(once: once)
        XCTAssertFalse(BnPerfMark.bootContentPending(once: once))
        XCTAssertEqual(contentLines.count, 1, "boot.content is once per flag, got \(contentLines)")
        XCTAssertTrue(contentLines[0].hasSuffix(" t=4242"))
    }

    func testBootContentConsumesTheFlagBelowVerbose() {
        BnLog.setLevelFromOrdinal(BnLogLevel.warn)
        let once = BnOnceFlag()
        BnPerfMark.bootContentOnce(once: once)
        XCTAssertFalse(BnPerfMark.bootContentPending(once: once),
                       "the flag is consumed at Warn, so the first-content scan stops after boot")
        XCTAssertEqual(clockReads, 0)
    }

    func testBootStartWritesOneLineAtVerbose() {
        BnLog.setLevelFromOrdinal(BnLogLevel.verbose)
        var startLines: [String] = []
        BnPerfMark.sinkForTest = { line in
            if line.hasPrefix("BNPERF boot.start ") { startLines.append(line) }
        }
        BnPerfMark.clockForTest = nil
        BnPerfMark.bootStart()
        XCTAssertEqual(startLines.count, 1)
        let t = startLines.first.flatMap { $0.split(separator: " ").last }.flatMap { UInt64($0.dropFirst(2)) }
        XCTAssertNotNil(t)
        XCTAssertGreaterThan(t ?? 0, 1_500_000_000 * 1_000_000_000, "the process start is a wall clock in ns")
        XCTAssertLessThanOrEqual(t ?? .max, clock_gettime_nsec_np(CLOCK_REALTIME))
    }
}
