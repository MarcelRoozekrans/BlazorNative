// ─────────────────────────────────────────────────────────────────────────────
// BnYogaProbe.mm — Phase 6.0 Yoga spike (M6 DoD #1, iOS rung).
//
// ALL Yoga interop lives HERE, in Objective-C++, and NOT anywhere Swift can see it.
// That placement is the load-bearing fix of this spike's iOS rung:
//
//   Xcode's Swift **explicit-module dependency scanner** processes the bridging
//   header (BlazorNativeRuntimeC.h) with its own, path-less search — it honours
//   neither HEADER_SEARCH_PATHS nor `-Xcc -I` via OTHER_SWIFT_FLAGS. So any
//   `#include <yoga/Yoga.h>` reachable from the bridging header fails the build
//   with "'yoga/Yoga.h' file not found" during scanning, even when the ordinary
//   Clang compile resolves it fine. (Six CI runs proved this the hard way.)
//
//   The fix: keep Yoga's headers out of Swift's sight entirely. This .mm is a
//   plain Clang compile (HEADER_SEARCH_PATHS *does* reach it), so it can include
//   <yoga/Yoga.h> freely; it exposes only a plain-C result struct + two C
//   functions, which the bridging header declares. Swift calls those. This mirrors
//   how the shell already talks to the NativeAOT runtime: hand-declared plain C,
//   no foreign headers in Swift's sight.
//
// The spike proves two things the Phase 6.1 layout engine depends on:
//   1. libyoga.a links and is callable in-process ALONGSIDE the runtime's static
//      .a (coexistence — a green hosted XCTest IS that proof: both native archives
//      in one binary).
//   2. The native MEASURE CALLBACK round-trip works in BOTH channels — Yoga calls
//      back out to measure an auto-sized leaf and *uses* the returned width AND
//      height. Linking is table-stakes; measurement is what makes flexbox usable,
//      because a text leaf's intrinsic size can only come from native font metrics.
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

// Builds THE CANONICAL TREE — byte-identical to the one YogaSpikeAndroidTest builds
// on the Android rung — and returns the computed frames:
//
//   root: flexDirection row, width 300, height 100, direction LTR
//     ├─ box1: width 50, height 50                  -> x=0,   y=0, w=50,  h=50
//     ├─ box2: flexGrow 1, height 50                -> x=50,  y=0, w=170, h=50
//     └─ text: no width/height, measure func 80x20,
//              alignSelf flex-start                 -> x=220, y=0, w=80,  h=20
//
// 50 + 170 + 80 = 300. Both rungs assert all four numbers of all three frames — the
// SAME twelve numbers — which is what makes "identical frames from one engine on two
// platforms" an asserted result rather than a claim. That parity is the entire
// architectural reason for choosing Yoga over two native layout systems.
//
// `alignSelf: flex-start` on the text leaf is deliberate: under Yoga's default
// `alignItems: stretch` the leaf's CROSS axis would take the row's 100 and the
// measured height (20) would be DISCARDED — only the width channel of the measure
// round-trip would be proven. flex-start makes the frame height the measured 20, so
// `text.h == 20` proves the returned HEIGHT reaches the frame too. For 6.1 that is
// the channel that matters most (text wrapping / multi-line labels in a column are
// exactly where measured height drives the frame); re-proving Yoga's own stretch
// default is worth less than proving our round-trip.
bn_yoga_result bn_yoga_compute_flex_row(void) {
    bn_yoga_measure_fired = 0;

    YGNodeRef root = YGNodeNew();
    YGNodeStyleSetFlexDirection(root, YGFlexDirectionRow);
    YGNodeStyleSetWidth(root, 300.0f);
    YGNodeStyleSetHeight(root, 100.0f);

    YGNodeRef box1 = YGNodeNew();
    YGNodeStyleSetWidth(box1, 50.0f);
    YGNodeStyleSetHeight(box1, 50.0f);
    YGNodeInsertChild(root, box1, 0);

    YGNodeRef box2 = YGNodeNew();
    YGNodeStyleSetFlexGrow(box2, 1.0f);
    YGNodeStyleSetHeight(box2, 50.0f);
    YGNodeInsertChild(root, box2, 1);

    YGNodeRef text = YGNodeNew();
    YGNodeStyleSetAlignSelf(text, YGAlignFlexStart);
    YGNodeSetMeasureFunc(text, bn_yoga_measure);
    YGNodeInsertChild(root, text, 2);

    // Direction is passed EXPLICITLY (the Android rung sets it explicitly too) so
    // the two trees cannot diverge on a platform default.
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
