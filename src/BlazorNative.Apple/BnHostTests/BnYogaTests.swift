// ─────────────────────────────────────────────────────────────────────────────
// BnYogaTests — Phase 6.0 Yoga spike (M6 DoD #1, iOS rung): the hosted-XCTest
// proof that Yoga computes the CANONICAL flexbox frames on the simulator AND that
// the native measure callback fired in both channels (width AND height). Runs
// inside the app process (BUNDLE_LOADER = BnHost), so the app's linked libyoga.a
// coexists with the runtime .a — a green run IS the coexistence proof (both static
// native libs in one binary + loaded).
//
// The twelve numbers asserted below are the SAME twelve YogaSpikeAndroidTest
// asserts against the SAME tree — that pairing is what makes "identical frames from
// one engine on two platforms" an asserted result, not a claim.
//
// Calls BnYogaProbe (@testable) rather than Yoga directly: Yoga's headers are
// visible ONLY to BnYogaProbe.mm (Objective-C++) — never to Swift, whose module
// scanner cannot resolve them — so the app exposes the probe as plain C and
// BnYogaProbe is the Swift wrapper the linker keeps live (AppDelegate.warmUp).
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnYogaTests: XCTestCase {

    func testYogaMinimalFlexRowLayoutAndMeasureCallback() {
        let r = BnYogaProbe.computeMinimalFlexRow()

        // box1 — fixed 50 × 50, at the left edge of the row.
        XCTAssertEqual(r.box1.minX, 0, accuracy: 0.5, "box1.x")
        XCTAssertEqual(r.box1.minY, 0, accuracy: 0.5, "box1.y")
        XCTAssertEqual(r.box1.width, 50, accuracy: 0.5, "box1.w")
        XCTAssertEqual(r.box1.height, 50, accuracy: 0.5, "box1.h")

        // box2 — flexGrow:1, height 50; follows box1 and absorbs the remaining
        // width (300 - box1(50) - text(80) = 170).
        XCTAssertEqual(r.box2.minX, 50, accuracy: 0.5, "box2.x")
        XCTAssertEqual(r.box2.minY, 0, accuracy: 0.5, "box2.y")
        XCTAssertEqual(r.box2.width, 170, accuracy: 0.5, "box2.w — flexGrow fills the rest")
        XCTAssertEqual(r.box2.height, 50, accuracy: 0.5, "box2.h")

        // text — auto-sized via the measure func, alignSelf:flex-start. BOTH channels
        // of the round-trip land in the frame: the main-axis width IS the measured 80
        // AND the cross-axis height IS the measured 20 (not stretched to the row's 100).
        XCTAssertEqual(r.text.minX, 220, accuracy: 0.5, "text.x — follows box2")
        XCTAssertEqual(r.text.minY, 0, accuracy: 0.5, "text.y")
        XCTAssertEqual(r.text.width, 80, accuracy: 0.5, "text.w — the MEASURED width reaches the frame")
        XCTAssertEqual(r.text.height, 20, accuracy: 0.5, "text.h — the MEASURED height reaches the frame")

        // Left-to-right placement (row direction, LTR).
        XCTAssertLessThan(r.box1.minX, r.box2.minX)
        XCTAssertLessThan(r.box2.minX, r.text.minX)

        // THE load-bearing round-trip: Yoga → shell measure func → Yoga used the size.
        XCTAssertTrue(r.measureFired, "the YGNodeSetMeasureFunc callback must have fired")
    }
}
