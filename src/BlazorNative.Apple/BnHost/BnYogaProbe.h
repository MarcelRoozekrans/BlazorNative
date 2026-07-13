// ─────────────────────────────────────────────────────────────────────────────
// BnYogaProbe.h — Phase 6.0 Yoga spike (M6 DoD #1, iOS rung): the SHELL-OWNED
// plain-C surface of the flexbox probe.
//
// This header is deliberately SEPARATE from BlazorNativeRuntimeC.h. That header is
// the mirror of src/BlazorNative.Runtime/Exports.cs — the runtime's C-ABI (9
// exports + the 72-byte bridge struct), one of three pinned mirrors. The bn_yoga_*
// symbols below are NOT runtime exports: they are implemented by the shell itself
// (BnYogaProbe.mm), so they live here. Phase 6.1's full bn_yoga_* node-tree API
// grows in THIS file, never in the runtime mirror.
//
// The bridging header #includes this one, so Swift still sees the plain-C surface.
// That indirection is fine — only YOGA'S OWN headers must stay invisible to Swift:
//
//   Xcode's Swift **explicit-module dependency scanner** processes the bridging
//   header with its own, path-less header search — it honours neither
//   HEADER_SEARCH_PATHS nor `-Xcc -I` via OTHER_SWIFT_FLAGS. So any
//   `#include <yoga/Yoga.h>` reachable from the bridging header fails the build
//   with "'yoga/Yoga.h' file not found" during scanning, even though the ordinary
//   Clang compile resolves it fine. (Six red CI runs proved this the hard way.)
//
// The rule, therefore: ALL Yoga interop lives in BnYogaProbe.mm (Objective-C++, a
// plain Clang compile that HEADER_SEARCH_PATHS *does* reach). It exposes only the
// plain C declared below. This mirrors how the shell already talks to the NativeAOT
// runtime: hand-declared plain C, no foreign headers in Swift's sight.
// ─────────────────────────────────────────────────────────────────────────────

#ifndef BN_YOGA_PROBE_H
#define BN_YOGA_PROBE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// The computed frames of the canonical flex-row proof (x/y/w/h per node) + whether
// the measure callback fired. The Android rung (YogaSpikeAndroidTest) builds the
// SAME tree and asserts the SAME twelve numbers — cross-platform frame parity is
// the whole architectural reason for choosing Yoga.
typedef struct {
    float box1X, box1Y, box1W, box1H;
    float box2X, box2Y, box2W, box2H;
    float textX, textY, textW, textH;
    int32_t measureFired;
} bn_yoga_result;

// Builds the canonical tree (see BnYogaProbe.mm for the full shape + the expected
// frames), computes the layout, and returns the frames by value.
bn_yoga_result bn_yoga_compute_flex_row(void);

// A launch-time smoke that keeps Yoga linked + callable in-process (AppDelegate).
// Returns the same frames, so the caller need not recompute for logging.
bn_yoga_result bn_yoga_warm_up(void);

#ifdef __cplusplus
}
#endif

#endif /* BN_YOGA_PROBE_H */
