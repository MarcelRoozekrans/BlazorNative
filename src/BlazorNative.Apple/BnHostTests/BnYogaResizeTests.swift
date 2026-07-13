// ─────────────────────────────────────────────────────────────────────────────
// BnYogaResizeTests — Phase 6.1 Gate 3 (Task 3.2's relayout half). The iOS twin of
// `YogaResizeAndroidTest`.
//
// Yoga solved the tree against the HOST's bounds, so a rotation / split-screen / any
// bounds change must re-solve. **No patch is involved** — .NET never learns the host
// got wider, and nothing in the render tree changed; this is a pure host event. In
// production the hook is `HostViewController.viewDidLayoutSubviews`, which calls the
// same `calculateAndApply` CommitFrame does; here the synthetic host calls it
// directly, because HostViewController stays inert under XCTest (the test bundle owns
// the native session).
//
// The pin is a PERCENTAGE width: a fixed one would look identical whether the tree
// re-solved or not, so it would assert nothing.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnYogaResizeTests: XCTestCase {

    func testAHostResizeReSolvesTheTree() {
        let host = BnSyntheticHost(width: 400, height: 800)
        host.render([
            bnCreate(1, "view", nil),
            bnStyle(1, "width", "50%"),
            bnStyle(1, "height", "40"),
        ])
        let box = host.root.subviews[0]
        XCTAssertEqual(box.frame.width, 200, accuracy: 0.5, "50% of the 400pt host")

        // The host gets narrower — the twin of a rotation.
        host.resize(width: 300, height: 800)

        XCTAssertEqual(box.frame.width, 150, accuracy: 0.5,
                       "THE PIN: the tree must RE-SOLVE against the new bounds — 50% of 300. "
                       + "A stale 200 means viewDidLayoutSubviews' calculateAndApply never ran "
                       + "(or ran against the old bounds), and rotation would leave the layout "
                       + "solved for a screen that no longer exists")
        XCTAssertEqual(box.frame.height, 40, accuracy: 0.5, "the fixed height is unchanged")
    }
}
