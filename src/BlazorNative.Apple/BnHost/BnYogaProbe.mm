// ─────────────────────────────────────────────────────────────────────────────
// BnYogaProbe.mm — Phase 6.0 Yoga spike (M6 DoD #1, iOS rung).
//
// ALL Yoga interop lives HERE, in Objective-C++, and NOT in the Swift-visible
// bridging header. That placement is the load-bearing fix of this spike's iOS rung:
//
//   Xcode's Swift **explicit-module dependency scanner** processes the bridging
//   header (BlazorNativeRuntimeC.h) with its own, path-less search — it honours
//   neither HEADER_SEARCH_PATHS nor `-Xcc -I` via OTHER_SWIFT_FLAGS. So any
//   `#include <yoga/Yoga.h>` in the bridging header fails the build with
//   "'yoga/Yoga.h' file not found" during scanning, even when the ordinary Clang
//   compile resolves it fine. (Six CI runs proved this the hard way.)
//
//   The fix: keep Yoga's headers out of Swift's sight entirely. This .mm is a
//   plain Clang compile (HEADER_SEARCH_PATHS *does* reach it), so it can include
//   <yoga/Yoga.h> freely; it exposes only a plain-C result struct + two C
//   functions, which the bridging header declares. Swift calls those. This mirrors
//   how the shell already talks to the NativeAOT runtime: hand-declared plain C,
//   no foreign headers in the bridging header.
//
// The spike proves two things the Phase 6.1 layout engine depends on:
//   1. libyoga.a links and is callable in-process ALONGSIDE the runtime's static
//      .a (coexistence — a green hosted XCTest IS that proof: both native archives
//      in one binary).
//   2. The native MEASURE CALLBACK round-trip works — Yoga calls back out to
//      measure an auto-sized leaf and *uses* the returned size. Linking is
//      table-stakes; measurement is what makes flexbox usable, because a text
//      leaf's intrinsic size can only come from native font metrics.
//
// Phase 6.1 replaces the fixed stub size below with real UILabel/UIImageView
// measurement and drives the whole view tree; this spike proves only the mechanism.
// ─────────────────────────────────────────────────────────────────────────────

#include "BlazorNativeRuntimeC.h"

#include <yoga/Yoga.h>

// Whether the measure callback fired — the load-bearing round-trip proof. A file
// static because a C function pointer cannot capture context (the same constraint
// the runtime's frame callback and the bridge trampolines live under).
static int32_t bn_yoga_measure_fired = 0;

// Yoga invokes this to measure a leaf whose size is `auto` (no width/height set).
// The spike returns a FIXED 80x20; Phase 6.1 measures real native content.
static YGSize bn_yoga_measure(YGNodeConstRef node,
                              float width,
                              YGMeasureMode widthMode,
                              float height,
                              YGMeasureMode heightMode) {
    (void)node; (void)width; (void)widthMode; (void)height; (void)heightMode;
    bn_yoga_measure_fired = 1;
    YGSize size;
    size.width = 80.0f;
    size.height = 20.0f;
    return size;
}

// Builds the minimal flex-row proof and returns the computed frames:
//
//   root: flexDirection row, 300 x 100
//     ├─ box1: width 50            (fixed)          -> x=0,   w=50
//     ├─ box2: flexGrow 1          (fills the rest) -> x=50,  w=170
//     └─ text: auto + measure func (measured 80x20) -> x=220, w=80
//
// 50 + 170 + 80 = 300. The text leaf's MAIN-axis width (80) comes from the measure
// callback; its CROSS-axis height stretches to the row's 100 (default alignItems:
// stretch), so the measured 20 is the intrinsic size, overridden cross-axis.
bn_yoga_result bn_yoga_compute_flex_row(void) {
    bn_yoga_measure_fired = 0;

    YGNodeRef root = YGNodeNew();
    YGNodeStyleSetFlexDirection(root, YGFlexDirectionRow);
    YGNodeStyleSetWidth(root, 300.0f);
    YGNodeStyleSetHeight(root, 100.0f);

    YGNodeRef box1 = YGNodeNew();
    YGNodeStyleSetWidth(box1, 50.0f);
    YGNodeInsertChild(root, box1, 0);

    YGNodeRef box2 = YGNodeNew();
    YGNodeStyleSetFlexGrow(box2, 1.0f);
    YGNodeInsertChild(root, box2, 1);

    YGNodeRef text = YGNodeNew();
    YGNodeSetMeasureFunc(text, bn_yoga_measure);
    YGNodeInsertChild(root, text, 2);

    YGNodeCalculateLayout(root, YGUndefined, YGUndefined, YGDirectionLTR);

    bn_yoga_result r;
    r.box1X = YGNodeLayoutGetLeft(box1);
    r.box1Y = YGNodeLayoutGetTop(box1);
    r.box1W = YGNodeLayoutGetWidth(box1);
    r.box1H = YGNodeLayoutGetHeight(box1);

    r.box2X = YGNodeLayoutGetLeft(box2);
    r.box2Y = YGNodeLayoutGetTop(box2);
    r.box2W = YGNodeLayoutGetWidth(box2);
    r.box2H = YGNodeLayoutGetHeight(box2);

    r.textX = YGNodeLayoutGetLeft(text);
    r.textY = YGNodeLayoutGetTop(text);
    r.textW = YGNodeLayoutGetWidth(text);
    r.textH = YGNodeLayoutGetHeight(text);

    r.measureFired = bn_yoga_measure_fired;

    YGNodeFreeRecursive(root);
    return r;
}

// Referenced from AppDelegate at launch so the linker keeps Yoga (and this probe)
// live in the app binary — the in-process coexistence smoke.
void bn_yoga_warm_up(void) {
    (void)bn_yoga_compute_flex_row();
}
