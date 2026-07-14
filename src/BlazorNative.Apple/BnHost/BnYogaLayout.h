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

// ── `scroll`: the SYNTHETIC CONTENT NODE (Phase 6.2) ─────────────────────────
//
// A `scroll` node is a VIEWPORT, and its wire children live one level deeper than
// the wire says: under a SYNTHETIC content node the shell creates and the renderer
// never hears about. That node's COMPUTED HEIGHT *is* the content size — Yoga's
// number, read straight out (`bn_yoga_node_get_frame`), never a shell-side union of
// child frames. **A scroll node's wire child at index *i* is the CONTENT node's child
// at index *i***, in this tree AND in `BnWidgetMapper`'s view tree: the second
// index-mapping rule after 6.1's text collapse, and it fails the same way — silently,
// as a skew. (Worse here than on Android: Android THROWS on an out-of-range insert
// index, iOS CLAMPS — the recorded 6.1 decision — so a skew that fails loudly there
// is silent here.)

/// Turns [scroll] into a VIEWPORT and creates its synthetic CONTENT node, returning
/// it. The two facts are one call because they are one decision, and splitting them
/// is how a scroll node ends up with an overflow flag and no content node (or the
/// reverse) in one of the two shells. The twin of `YogaLayout.attachContentNode`.
///
/// The content node's styles are the whole of it, and **the one it must NOT have is
/// the load-bearing one**:
///
///   - `height: auto`      — its computed height IS the content size.
///   - `width: 100%`       — it spans the viewport, so a row with no width stretches.
///   - `flexDirection: column` — Yoga's default, set anyway: this node's layout is the
///     SHELL's, and an explicit column is one less thing for two shells to disagree on.
///   - **`flexShrink`: NEVER SET.** Yoga's default is **0** where CSS's is 1, and that
///     default is the ENTIRE mechanism by which the content node keeps its 800 against
///     a 200-high viewport: free space is `200 − 800 = −600`, negative free space is
///     distributed by the SHRINK pass in proportion to `flexShrink`, and 0 means "none
///     of it". It is **not** `overflow: scroll` that does this (Gate 2's Yoga-only test
///     computes the same 800 with `overflow: visible`, and computes 200 the moment
///     `flexShrink: 1` is set). Write that one line and the page stops scrolling with
///     no error anywhere — and, as Android proved, every ROW frame stays correct; the
///     only corrupted number is the one the shell reads as `contentSize`.
///
/// `overflow: scroll` is set on the SCROLL node because that is what a scrolling
/// viewport MEANS and what React Native sets — not because it computes anything. The
/// contract does not claim it does.
///
/// The content node is a Yoga CHILD of [scroll], so [bn_yoga_node_free_subtree] frees
/// it for nothing — which is exactly why the purge must not be short-circuited: on iOS
/// a descendant the shell keeps a handle to after the free is a **dangling
/// `YGNodeRef`**, not a leak.
bn_yoga_node _Nonnull bn_yoga_node_attach_scroll_content(bn_yoga_node _Nonnull scroll);

/// True when [node]'s **DECLARED** height is a POINT or a PERCENT — i.e. the author
/// gave the node a definite height of its own. The first half of the definite-height
/// diagnostic (`BnWidgetMapper.warnIfIndefiniteHeight`); the second half is a
/// comparison of COMPUTED heights, which the shell reads off
/// [bn_yoga_node_get_frame]. Yoga's declared style is not otherwise reachable from
/// Swift, and it must not be tracked shell-side in parallel — a second copy of "what
/// did the author declare?" is a second thing to keep in step.
///
/// **Known false negative, ledgered:** `height: 100%` against a parent with no
/// definite height resolves to `auto` in Yoga — the node hugs its content and dies
/// exactly as silently — but its DECLARED unit is `PERCENT`, so this answers 1 and the
/// diagnostic exits at its first condition. Closing it means asking Yoga whether the
/// percent actually RESOLVED, which it does not expose. Same false negative on Android,
/// deliberately: one rule, two shells.
int32_t bn_yoga_node_has_declared_height(bn_yoga_node _Nonnull node);

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

/// True when [name] is one of the six CONTAINER-layout styles that are **IGNORED AND
/// LOGGED on a `scroll` node** (design decision 6 — NORMATIVE for both shells;
/// `kScrollIgnoredContainerStyles` in the .mm is the list, and Kotlin's
/// `SCROLL_IGNORED_CONTAINER_STYLES` is its twin).
///
/// Every one of them styles the *scroll* node, whose only Yoga child is the synthetic
/// content node — so every one of them fails silently and bafflingly:
/// `flexDirection: row` lays the content node out across the cross axis and stretches
/// it to the viewport height (**the page just stops scrolling**); `justifyContent` /
/// `alignItems` distribute free space that on a scrolling viewport is **NEGATIVE**
/// (200 − 800 = −600), so `center` offsets the content to y = −300 and the top of the
/// page becomes **permanently unreachable**; `gap` spaces the scroll node's ONE child
/// against nothing; `padding` insets the content node and moves every frame in the
/// parity table.
///
/// ITEM styles (`flexGrow`, `flexShrink`, `flexBasis`, `alignSelf`, the box, `margin`,
/// `position`, the offsets) and `backgroundColor` apply NORMALLY: a `BnScroll` *is* a
/// flex item, and how the viewport is placed in its parent is the author's business.
/// Over-broad filtering here would be as wrong as none.
///
/// The two shells' lists are pinned EQUAL by `ShellStyleTableDriftTests
/// .TheTwoShellsScrollIgnoreLists_AreIdenticalToEachOther`, which parses the `.mm`'s
/// declaration with the same source-format-agnostic parser it uses for `kYogaStyles`.
int32_t bn_yoga_is_scroll_ignored_container_style(const char* _Nonnull name);

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
