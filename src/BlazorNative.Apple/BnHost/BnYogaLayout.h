// ─────────────────────────────────────────────────────────────────────────────
// BnYogaLayout.h — Phase 6.1 Gate 3 (M6 DoD #2/#3): the SHELL-OWNED plain-C node
// API of the iOS layout engine. The iOS twin of Kotlin's `YogaLayout` — same
// style-name table, same value grammar, same node lifecycle — except that on iOS
// the whole thing has to be reachable from Swift, and Yoga is not.
//
// ── THE NON-NEGOTIABLE (six red CI runs in Phase 6.0 bought it) ───────────────
//
// **Yoga's headers must NEVER be visible to Swift.** Xcode's Swift explicit-module
// dependency SCANNER processes the bridging header with its own, path-less header
// search — it honours neither HEADER_SEARCH_PATHS nor `-Xcc -I` via
// OTHER_SWIFT_FLAGS. So any `#include <yoga/Yoga.h>` reachable from the bridging
// header fails the build with "'yoga/Yoga.h' file not found" during *scanning*,
// even though the ordinary Clang compile resolves it fine.
//
// Therefore: ALL Yoga interop lives in BnYogaLayout.mm (Objective-C++, a plain
// Clang compile that HEADER_SEARCH_PATHS *does* reach). It implements only the
// plain C declared below; the bridging header (BlazorNativeRuntimeC.h) includes
// THIS file, and Swift calls it. Exactly the pattern BnYogaProbe.{h,mm} proved in
// 6.0, and the same plain-C discipline the shell already uses to talk to the
// NativeAOT runtime.
//
// This is a SHELL-owned header, deliberately NOT part of the runtime C-ABI mirror
// (that is the top half of BlazorNativeRuntimeC.h, mirroring Exports.cs). Nothing
// here is a runtime export; every symbol is implemented by the shell itself.
//
// ── WHAT LIVES HERE vs. IN SWIFT ─────────────────────────────────────────────
//
//   .mm  — the Yoga tree, the string→setter routing table, the VALUE GRAMMAR
//          parser, the shared YGConfig (pointScaleFactor = 0), node lifetime.
//   Swift — the view tree, the collapse, the measure trampoline (it calls
//          `sizeThatFits`, which only Swift/UIKit can do), frame application.
//
// The style-name table and the value parser live on THIS side of the line on
// purpose: they are the half that must mirror `YogaLayout.kt` name-for-name and
// rule-for-rule, and being plain C they are directly unit-testable from XCTest
// (BnYogaStyleParserTests) without a UIView in sight.
//
// The grammar itself has exactly ONE normative statement —
// docs/plans/2026-07-13-phase-6.1-design.md §"Style value grammar (normative)".
// It is not restated here or in the .mm: three copies of a grammar is how two
// hand-written parsers drift.
// ─────────────────────────────────────────────────────────────────────────────

#ifndef BN_YOGA_LAYOUT_H
#define BN_YOGA_LAYOUT_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/// An opaque `YGNodeRef`. Swift holds these; only the .mm dereferences them.
typedef void* bn_yoga_node;

/// A computed frame, RELATIVE TO ITS PARENT — which is exactly `UIView.frame`'s
/// contract, because a plain `UIView` does not re-place its subviews. Yoga
/// computes in points on iOS (1:1 with the dp Android computes in), and the
/// shared config turns Yoga's own pixel-grid rounding OFF, so these numbers are
/// exact and fractional — the shell must NOT invent a rounding of its own.
typedef struct {
    float x;
    float y;
    float width;
    float height;
} bn_yoga_frame;

/// What a measure function returns (a YGSize by another name).
typedef struct {
    float width;
    float height;
} bn_yoga_size;

/// Yoga's measure modes, re-declared so Swift never needs Yoga's headers. The
/// values ARE `YGMeasureMode`'s and the .mm static_asserts that they still are.
typedef enum {
    bn_yoga_measure_undefined = 0, ///< no constraint at all → `.greatestFiniteMagnitude`
    bn_yoga_measure_exactly = 1,   ///< the size is imposed → return it unchanged
    bn_yoga_measure_at_most = 2,   ///< fit within → clamp the measured size to it
} bn_yoga_measure_mode;

/// The native measurement callback (DoD #3). A C function pointer CANNOT capture,
/// so the node's identity travels in `context` — the same constraint the runtime's
/// frame callback lives under. Swift passes an unretained `UIView` pointer and the
/// trampoline calls `sizeThatFits`.
typedef bn_yoga_size (*bn_yoga_measure_fn)(void* _Nullable context,
                                           float width,
                                           bn_yoga_measure_mode widthMode,
                                           float height,
                                           bn_yoga_measure_mode heightMode);

// ── The node tree ────────────────────────────────────────────────────────────

/// A fresh node on the shell's shared config. Free it with
/// [bn_yoga_node_free_subtree] — these are RAW native allocations and nothing
/// will ever collect them for you.
bn_yoga_node _Nonnull bn_yoga_node_new(void);

/// Inserts [child] under [parent] at [index] — the SAME index the view went to.
/// The two trees mirror each other or every frame after the skew is wrong.
void bn_yoga_node_insert_child(bn_yoga_node _Nonnull parent,
                               bn_yoga_node _Nonnull child,
                               int32_t index);

void bn_yoga_node_remove_child(bn_yoga_node _Nonnull parent, bn_yoga_node _Nonnull child);

/// [node]'s parent, or NULL when it is a root / already detached — the handle
/// `RemoveNode` needs, since the patch carries only the node's own id.
bn_yoga_node _Nullable bn_yoga_node_get_owner(bn_yoga_node _Nonnull node);

int32_t bn_yoga_node_child_count(bn_yoga_node _Nonnull node);

/// FREES [node] AND EVERY DESCENDANT, clearing each one's measure function first.
///
/// The renderer emits **one** `RemoveNodePatch` for a whole subtree (its
/// `PurgeNodeSubtree` is .NET-side bookkeeping), so the host must purge the
/// subtree itself. On iOS this is a REAL leak, not a GC hint: these are raw
/// `YGNodeRef`s owned by the .mm, and every navigation replaces the tree.
/// Clearing the measure funcs is part of the purge on purpose — it breaks the
/// last edge from a native node to a `UIView` that is about to be released.
void bn_yoga_node_free_subtree(bn_yoga_node _Nonnull node);

// ── Styles ───────────────────────────────────────────────────────────────────

/// True when [name] is a LAYOUT style — i.e. it belongs to the Yoga node and to
/// NOTHING else. The mirror of `NativeRenderer.YogaStyleAttributes` (and of
/// Kotlin's `YogaLayout.YOGA_STYLES`); matching is ORDINAL/case-sensitive on
/// every shell. Pinned against .NET's set by `ShellStyleTableDriftTests`.
int32_t bn_yoga_is_layout_style(const char* _Nonnull name);

/// Applies ONE layout style. A **NULL [value] resets** the property to Yoga's
/// default (the wire's "reset" — the null-reset fix's other half); anything the
/// grammar does not accept is logged and ignored, never guessed and never clamped.
///
/// Returns 1 when the style was applied, 0 when it was ignored. (Kotlin's twin
/// returns Unit and logs; the rc is the C-idiomatic form of the same fact, and it
/// is what lets XCTest unit-test the parser's REJECTION path directly.)
int32_t bn_yoga_node_set_style(bn_yoga_node _Nonnull node,
                               const char* _Nonnull name,
                               const char* _Nullable value);

// ── Measurement ──────────────────────────────────────────────────────────────

/// Attaches (or, with a NULL [fn], clears) the measure function. The shell
/// attaches it BY NODETYPE — `text`/`button`/`input`/`image` — and NEVER by "this
/// node has no children": BnLayoutDemo's row is three *childless* `view`s, and a
/// measure func on them would let a UIView's intrinsic size speak over Yoga and
/// destroy the `flexGrow:1` box's computed 200.
void bn_yoga_node_set_measure(bn_yoga_node _Nonnull node,
                              bn_yoga_measure_fn _Nullable fn,
                              void* _Nullable context);

/// Invalidates a measured node's cached size. Yoga CACHES a measure function's
/// result and will not re-run it unless the node is dirty, so every patch that
/// changes a widget's intrinsic size (ReplaceText / value / placeholder /
/// fontSize) must land here — otherwise the next layout serves the size the OLD
/// content measured to. A no-op on a node with no measure function (Yoga's own
/// `YGNodeMarkDirty` would abort).
void bn_yoga_node_mark_dirty(bn_yoga_node _Nonnull node);

// ── The layout pass ──────────────────────────────────────────────────────────

/// ONE layout pass over the whole tree, against the host's bounds.
///
/// A non-positive available dimension means "size to content" (`auto`), not zero:
/// a host with no bounds yet — a detached root, i.e. every synthetic-frame test,
/// and the first commit if it beats the first layout pass — must hug its content
/// rather than collapse. Direction is set to LTR EXPLICITLY (a platform default is
/// exactly the kind of thing the two shells could silently disagree on).
void bn_yoga_calculate(bn_yoga_node _Nonnull root, float availableWidth, float availableHeight);

/// [node]'s computed frame, relative to its parent.
bn_yoga_frame bn_yoga_node_get_frame(bn_yoga_node _Nonnull node);

#ifdef __cplusplus
}
#endif

#endif /* BN_YOGA_LAYOUT_H */
