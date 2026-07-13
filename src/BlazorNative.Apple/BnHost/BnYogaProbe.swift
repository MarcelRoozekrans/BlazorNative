// ─────────────────────────────────────────────────────────────────────────────
// BnYogaProbe — Phase 6.0 Yoga spike (M6 DoD #1, iOS rung): the Swift face of the
// flexbox probe.
//
// This file contains NO Yoga interop. All of it lives in BnYogaProbe.mm
// (Objective-C++), which implements the plain-C surface declared in BnYogaProbe.h
// (bn_yoga_result / bn_yoga_compute_flex_row / bn_yoga_warm_up) — a SHELL-owned
// header, deliberately not part of the runtime C-ABI mirror; the bridging header
// merely includes it so Swift sees the plain C.
//
// Why: Xcode's Swift explicit-module dependency SCANNER processes the bridging
// header with a path-less search that honours neither HEADER_SEARCH_PATHS nor
// `-Xcc -I`, so a `#include <yoga/Yoga.h>` reachable from it fails the build
// ("'yoga/Yoga.h' file not found") even though the ordinary Clang compile resolves
// it fine. Keeping Yoga's headers out of Swift's sight — reaching them only from
// the .mm, which IS a plain Clang compile — is the spike's iOS-rung fix.
//
// This mirrors how the shell already talks to the NativeAOT runtime: plain C
// across the boundary, no foreign headers in Swift's sight. See BnYogaProbe.mm for
// the canonical tree and the full story.
// ─────────────────────────────────────────────────────────────────────────────

import UIKit

/// The computed frames of the canonical flex-row proof, plus whether the native
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
    /// ONE layout pass: `bn_yoga_warm_up()` returns the frames it computed (it used
    /// to compute, then discard, then have Swift compute the very same tree again).
    static func warmUp() {
        let r = toResult(bn_yoga_warm_up())
        NSLog("[BnYogaProbe] Yoga warm-up ok — box2.width=\(r.box2.width) text.height=\(r.text.height) measureFired=\(r.measureFired)")
    }

    /// The CANONICAL tree (built in BnYogaProbe.mm; byte-identical to the one the
    /// Android rung builds) — a `row` container, 300 × 100, direction LTR:
    ///   box1  — width 50, height 50                       → x=0,   y=0, w=50,  h=50
    ///   box2  — flexGrow 1, height 50                     → x=50,  y=0, w=170, h=50
    ///   text  — no width/height, measure func → 80 × 20,
    ///           alignSelf flex-start                      → x=220, y=0, w=80,  h=20
    /// Left-to-right; box2 absorbs `300 - 50 - 80 = 170`; the text leaf's frame
    /// height is the MEASURED 20 (flex-start, not stretched), which is what proves
    /// the height channel of the measure round-trip.
    static func computeMinimalFlexRow() -> BnYogaFlexResult {
        toResult(bn_yoga_compute_flex_row())
    }

    private static func toResult(_ r: bn_yoga_result) -> BnYogaFlexResult {
        BnYogaFlexResult(
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
