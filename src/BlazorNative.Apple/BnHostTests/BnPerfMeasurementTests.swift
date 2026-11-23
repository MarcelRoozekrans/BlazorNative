// ─────────────────────────────────────────────────────────────────────────────
// BnPerfMeasurementTests — Phase 19.2 click-to-frame measurement, the twin of the
// Android PerfMeasurementTest. It drives PerfProbe, whose handler is a synchronous
// counter increment, so each click is one dispatch and one frame. The marks land in
// the unified log; scripts/perf/ios-collect.ps1 dumps them for
// tools/BlazorNative.PerfReport.
//
// NOT a PR-lane test. ios.yml's `xcodebuild test` step passes
// `-skip-testing:BnHostTests/BnPerfMeasurementTests`, so the lane's asserted baseline
// (270 on this backport branch) does not count it. The collector runs it with `-only-testing:` and
// `TEST_RUNNER_BN_LOG_LEVEL=Verbose`, because BnRuntime.start resolves the level from
// that environment variable and would otherwise reset it to the default.
//
// Clicks are serial: after each sendActions the test waits until the counter shows the
// new value, because the parser pairs clicks by order. The wire has no test-id prop, so
// nodes are found by text: the button by its label "Click", the counter by the exact
// string "Count: n". Row 0 reads "Row 0 count n" and cannot match.
//
// The click count comes from env PERF_CLICKS (TEST_RUNNER_PERF_CLICKS on the xcodebuild
// command line), default 25.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
import OSLog
@testable import BnHost

final class BnPerfMeasurementTests: BnHostTestCase {

    private static let defaultClicks = 25

    func testSerialClicksOnPerfProbe() throws {
        let raw = ProcessInfo.processInfo.environment["PERF_CLICKS"]
        let clicks = raw.flatMap { Int($0) } ?? Self.defaultClicks
        XCTAssertGreaterThan(clicks, 0, "PERF_CLICKS must be positive, got \(raw ?? "nil")")

        let root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let runtime = BnRuntime(mapper: mapper)
        runtime.onError = { msg, err in NSLog("[BnPerfMeasurementTests] \(msg): \(err)") }
        try runtime.start(component: "PerfProbe", os: "ios")

        // start() has resolved the level from BN_LOG_LEVEL. Below Verbose no mark is written,
        // and the run would produce an empty log, so say why instead of timing out later.
        XCTAssertTrue(BnLog.isEnabled(BnLogLevel.verbose),
                      "BN_LOG_LEVEL must be Verbose, pass TEST_RUNNER_BN_LOG_LEVEL=Verbose to xcodebuild")

        XCTAssertTrue(pollUntil(deadline: 60) { self.findLabel(in: root, exact: "Count: 0") != nil },
                      "the PerfProbe counter 'Count: 0' never appeared within 60s")

        let clicksStarted = Date()

        for n in 1...clicks {
            let button = try XCTUnwrap(findButton(in: root, title: "Click"),
                                       "the 'Click' button was not on the page at click \(n)")
            button.sendActions(for: .touchUpInside)
            XCTAssertTrue(pollUntil(deadline: 10) { self.findLabel(in: root, exact: "Count: \(n)") != nil },
                          "the counter never showed 'Count: \(n)' within 10s of click \(n)")
        }

        // The .NET marks trail the clicks: they pass BnPerfMarks' writer thread and .NET's Console,
        // which on iOS is NSLog, and on CI they reached the unified log up to hundreds of
        // milliseconds after the click's frame. The test host is terminated when this method
        // returns, and a mark still queued then is lost; on CI one run lost 7 of its 10
        // dispatch.enter marks. So wait until this process's own log holds every click's
        // dispatch.handler. The writer is one FIFO queue, so the last handler means every enter
        // before it is out too. A drain that runs out does not fail here: the collector requires
        // each dispatch point exactly once per click and fails the run by name.
        waitForDispatchMarks(clicks, since: clicksStarted)
    }

    private func waitForDispatchMarks(_ clicks: Int, since: Date) {
        let end = Date().addingTimeInterval(30)
        guard #available(iOS 15.0, *) else {
            // No OSLogStore before iOS 15; only the backport's iOS 13 floor compiles this path,
            // and the perf simulator runs a current iOS.
            RunLoop.current.run(until: Date().addingTimeInterval(5))
            return
        }
        while Date() < end {
            if countLogEntries(containing: "BNPERF dispatch.handler ", since: since) >= clicks { return }
            RunLoop.current.run(until: Date().addingTimeInterval(0.25))
        }
    }

    @available(iOS 15.0, *)
    private func countLogEntries(containing text: String, since: Date) -> Int {
        guard let store = try? OSLogStore(scope: .currentProcessIdentifier),
              let entries = try? store.getEntries(at: store.position(date: since)) else { return 0 }
        var n = 0
        for entry in entries where entry.composedMessage.contains(text) { n += 1 }
        return n
    }

    private func findLabel(in view: UIView, exact text: String) -> UILabel? {
        if let l = view as? UILabel, l.text == text { return l }
        for sub in view.subviews {
            if let f = findLabel(in: sub, exact: text) { return f }
        }
        return nil
    }

    private func findButton(in view: UIView, title: String) -> UIButton? {
        if let b = view as? UIButton, b.title(for: .normal) == title { return b }
        for sub in view.subviews {
            if let f = findButton(in: sub, title: title) { return f }
        }
        return nil
    }

    private func pollUntil(deadline seconds: TimeInterval, _ cond: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.01))
            if cond() { return true }
        }
        return cond()
    }
}
