// ─────────────────────────────────────────────────────────────────────────────
// BnYogaTests — Phase 6.0 Yoga spike (M6 DoD #1, iOS rung): the hosted-XCTest
// proof that Yoga computes correct flexbox frames on the simulator AND that the
// native measure callback fired. Runs inside the app process (BUNDLE_LOADER =
// BnHost), so the app's linked libyoga.a coexists with the runtime .a — a green
// run IS the coexistence proof (both static native libs in one binary + loaded).
//
// Calls BnYogaProbe (@testable) rather than Yoga directly: the Yoga C-API is
// exposed to the APP via the bridging header, and BnYogaProbe is the app-side
// Swift wrapper the linker keeps live (AppDelegate.warmUp references it).
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnYogaTests: XCTestCase {

    func testYogaMinimalFlexRowLayoutAndMeasureCallback() {
        let r = BnYogaProbe.computeMinimalFlexRow()

        // box1: fixed, at the left edge.
        XCTAssertEqual(r.box1.minX, 0, accuracy: 0.5, "box1 sits at the left")
        XCTAssertEqual(r.box1.width, 50, accuracy: 0.5, "box1 keeps its fixed width")

        // box2: laid out after box1, flexGrow fills the remaining width
        // (300 - box1(50) - text(80) = 170).
        XCTAssertEqual(r.box2.minX, 50, accuracy: 0.5, "box2 follows box1 (left-to-right)")
        XCTAssertEqual(r.box2.width, 170, accuracy: 0.5, "flexGrow:1 fills the remaining width")

        // text: after box2 (left 220); its MAIN-axis (width) is the measured 80 —
        // the load-bearing proof the measure func's returned size drives layout.
        XCTAssertEqual(r.text.minX, 220, accuracy: 0.5, "the measured leaf follows box2")
        XCTAssertEqual(r.text.width, 80, accuracy: 0.5, "the leaf width comes from the measure func")
        // The CROSS-axis (height) stretches to the row's 100 (default alignItems:
        // stretch); the measured 20 is the intrinsic size, overridden cross-axis.
        XCTAssertEqual(r.text.height, 100, accuracy: 0.5, "cross-axis stretches to the row height")

        // Left-to-right placement (row direction).
        XCTAssertLessThan(r.box1.minX, r.box2.minX)
        XCTAssertLessThan(r.box2.minX, r.text.minX)

        // THE load-bearing round-trip: Yoga → shell measure func → Yoga used the size.
        XCTAssertTrue(r.measureFired, "the YGNodeSetMeasureFunc callback must have fired")
    }
}
