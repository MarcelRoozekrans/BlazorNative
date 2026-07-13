// ─────────────────────────────────────────────────────────────────────────────
// BnYogaProbe — Phase 6.0 Yoga spike (M6 DoD #1, iOS rung): proves Facebook's Yoga
// C++ flexbox engine links into the app alongside the NativeAOT runtime .a and
// that the native MEASURE CALLBACK round-trip works — the load-bearing part of the
// spike (linking is table-stakes; measurement is what makes flexbox usable).
//
// Yoga's C-API (<yoga/Yoga.h>, via the bridging header) is the same C-interop the
// shell uses for the runtime. The @convention(c) measure func can't capture, so it
// signals through a global — the exact runtime-callback pattern (BnRuntime's frame
// trampoline, AppleShellBridge's bridge trampolines). Phase 6.1 replaces the fixed
// stub size with real UILabel/UIImageView measurement and drives the whole view
// tree; this spike only proves the mechanism.
// ─────────────────────────────────────────────────────────────────────────────

import UIKit

/// The measure callback fired (the load-bearing round-trip proof). A global
/// because a `@convention(c)` closure cannot capture context.
private var bnYogaMeasureFired = false

/// Yoga invokes this to measure a leaf whose size is `auto` (no width/height set).
/// The spike returns a FIXED size (80×20); Phase 6.1 measures real native content.
private let bnYogaMeasureFunc: YGMeasureFunc = { _, _, _, _, _ in
    bnYogaMeasureFired = true
    return YGSize(width: 80, height: 20)
}

/// The computed frames of the minimal flex-row proof.
struct BnYogaFlexResult {
    let box1: CGRect
    let box2: CGRect
    let text: CGRect
    let measureFired: Bool
}

enum BnYogaProbe {

    /// Referenced from AppDelegate at launch so the linker keeps Yoga (and this
    /// probe) live in the app binary — proving Yoga is callable in-process
    /// alongside the runtime .a — and as a smoke of the full flex computation.
    static func warmUp() {
        let r = computeMinimalFlexRow()
        NSLog("[BnYogaProbe] Yoga warm-up ok — box2.width=\(r.box2.width) measureFired=\(r.measureFired)")
    }

    /// The minimal flex-row: a `row` container (width 300, height 100) with
    ///   box1  — fixed 50×50
    ///   box2  — flexGrow 1, height 50 (fills the remaining width)
    ///   text  — auto size, a registered measure func → 80×20
    /// Left-to-right, box2 absorbs `300 - 50 - 80 = 170`.
    static func computeMinimalFlexRow() -> BnYogaFlexResult {
        bnYogaMeasureFired = false

        let root = YGNodeNew()
        // bnYogaFlexDirectionRow() / bnYogaDirectionLTR(): stable C accessors for the
        // enum members (Swift prefix-strips them; see the bridging header note).
        YGNodeStyleSetFlexDirection(root, bnYogaFlexDirectionRow())
        YGNodeStyleSetWidth(root, 300)
        YGNodeStyleSetHeight(root, 100)

        let box1 = YGNodeNew()
        YGNodeStyleSetWidth(box1, 50)
        YGNodeStyleSetHeight(box1, 50)
        YGNodeInsertChild(root, box1, 0)

        let box2 = YGNodeNew()
        YGNodeStyleSetFlexGrow(box2, 1)
        YGNodeStyleSetHeight(box2, 50)
        YGNodeInsertChild(root, box2, 1)

        let text = YGNodeNew()
        YGNodeSetMeasureFunc(text, bnYogaMeasureFunc)
        YGNodeInsertChild(root, text, 2)

        // Available size UNDEFINED (NaN) — Yoga uses the root's styled 300×100.
        YGNodeCalculateLayout(root, Float.nan, Float.nan, bnYogaDirectionLTR())

        let result = BnYogaFlexResult(
            box1: frame(box1),
            box2: frame(box2),
            text: frame(text),
            measureFired: bnYogaMeasureFired)

        YGNodeFreeRecursive(root) // frees the whole tree
        return result
    }

    private static func frame(_ node: YGNodeRef?) -> CGRect {
        CGRect(x: CGFloat(YGNodeLayoutGetLeft(node)),
               y: CGFloat(YGNodeLayoutGetTop(node)),
               width: CGFloat(YGNodeLayoutGetWidth(node)),
               height: CGFloat(YGNodeLayoutGetHeight(node)))
    }
}
