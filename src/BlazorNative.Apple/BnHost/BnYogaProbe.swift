// ─────────────────────────────────────────────────────────────────────────────
// BnYogaProbe — Phase 6.0 Yoga spike (M6 DoD #1, iOS rung): the Swift face of the
// flexbox probe.
//
// This file contains NO Yoga interop. All of it lives in BnYogaProbe.mm
// (Objective-C++), which exposes a plain-C surface (bn_yoga_result /
// bn_yoga_compute_flex_row / bn_yoga_warm_up) that the bridging header declares.
//
// Why: Xcode's Swift explicit-module dependency SCANNER processes the bridging
// header with a path-less search that honours neither HEADER_SEARCH_PATHS nor
// `-Xcc -I`, so a `#include <yoga/Yoga.h>` there fails the build ("'yoga/Yoga.h'
// file not found") even though the ordinary Clang compile resolves it fine.
// Keeping Yoga's headers out of Swift's sight — reaching them only from the .mm,
// which IS a plain Clang compile — is the spike's iOS-rung fix.
//
// This mirrors how the shell already talks to the NativeAOT runtime: plain C
// across the boundary, no foreign headers in the bridging header. See
// BnYogaProbe.mm for the Yoga tree and the full story.
// ─────────────────────────────────────────────────────────────────────────────

import UIKit

/// The computed frames of the minimal flex-row proof, plus whether the native
/// measure callback fired (the load-bearing round-trip).
struct BnYogaFlexResult {
    let box1: CGRect
    let box2: CGRect
    let text: CGRect
    let measureFired: Bool
}

enum BnYogaProbe {

    /// Referenced from AppDelegate at launch so the linker keeps Yoga (and this
    /// probe) live in the app binary — proving Yoga is callable in-process
    /// alongside the runtime's static .a — and as a smoke of the full computation.
    static func warmUp() {
        bn_yoga_warm_up()
        let r = computeMinimalFlexRow()
        NSLog("[BnYogaProbe] Yoga warm-up ok — box2.width=\(r.box2.width) measureFired=\(r.measureFired)")
    }

    /// The minimal flex-row (built in BnYogaProbe.mm): a `row` container
    /// (300 × 100) with
    ///   box1  — fixed 50 × 50
    ///   box2  — flexGrow 1, height 50 (fills the remaining width)
    ///   text  — auto size, a registered measure func → 80 × 20
    /// Left-to-right, box2 absorbs `300 - 50 - 80 = 170`.
    static func computeMinimalFlexRow() -> BnYogaFlexResult {
        let r = bn_yoga_compute_flex_row()
        return BnYogaFlexResult(
            box1: CGRect(x: CGFloat(r.box1X), y: CGFloat(r.box1Y),
                         width: CGFloat(r.box1W), height: CGFloat(r.box1H)),
            box2: CGRect(x: CGFloat(r.box2X), y: CGFloat(r.box2Y),
                         width: CGFloat(r.box2W), height: CGFloat(r.box2H)),
            text: CGRect(x: CGFloat(r.textX), y: CGFloat(r.textY),
                         width: CGFloat(r.textW), height: CGFloat(r.textH)),
            measureFired: r.measureFired != 0
        )
    }
}
