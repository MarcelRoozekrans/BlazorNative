// ─────────────────────────────────────────────────────────────────────────────
// BnYogaLayout.mm — Phase 6.1 Gate 3 (M6 DoD #2/#3): the iOS layout engine's
// Yoga half. ALL Yoga interop lives HERE, in Objective-C++, and NOT anywhere Swift
// can see it (see BnYogaLayout.h for why — Xcode's Swift module scanner cannot
// resolve <yoga/Yoga.h> from the bridging header, and six red CI runs in Phase 6.0
// bought that lesson).
//
// This file is the twin of
// `src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt`,
// and it is a twin in the load-bearing sense: the same style-name table, the same
// value grammar, the same defaults on reset, the same rounding posture. The two
// shells' frame tables are asserted to be IDENTICAL (M6 DoD #2), so every place
// these two files could disagree is a place the parity assertion goes red for a
// reason that has nothing to do with the engine.
//
// ── THE FOUR THINGS THAT WOULD SILENTLY CORRUPT THE FRAME TABLE ──────────────
//
// 1. **pointScaleFactor = 0.** Yoga defaults it to 1 and then rounds computed
//    frames by TWO different rules — a node with a measure function is CEILED
//    ("text rounding": a fractional measurement must never clip a glyph) while its
//    siblings' offsets are ROUNDED. Android turned Yoga's rounding OFF for that
//    reason; iOS must too, or iOS would quantize measured content to whole points
//    while Android stays exact and the two shells would legitimately disagree on
//    what a label measures to. iOS assigns fractional-point frames straight to
//    `UIView.frame`, and it invents NO rounding of its own. (Android's absolute-edge
//    rounding is Android-specific — it exists only because of the dp→px hop.)
//
// 2. **`margin: auto` is a legal VALUE but is NOT margin's DEFAULT.** Yoga's
//    default is undefined → 0, and `margin: auto` is not inert — it absorbs free
//    space and re-centres the node. So a NULL reset on `margin` goes to
//    `YGUndefined`, never to `YGNodeStyleSetMarginAuto`. (For `width`/`height`/
//    `flexBasis`, `auto` genuinely IS the default.) Android shipped this bug and
//    fixed it; see [bn_parse_length]'s two flags.
//
// 3. **The strict-parse rule.** `strtof(value, NULL)` returns 12.0 for `"12px"`,
//    accepts `"12abc"`, skips leading whitespace, and takes its own extensions
//    (`0x10`, `inf`, `nan`). Kotlin's `toFloatOrNull` accepts a trailing `f`/`d`
//    (`"12f"` → 12.0). Left to their natural implementations the two parsers would
//    HONOUR values the other IGNORES — on the wire's REJECTION path. So both screen
//    against the grammar's number production and demand THE WHOLE STRING BE
//    CONSUMED. See [bn_parse_number].
//
// 4. **Nothing sets `alignContent`.** Yoga's default is `flex-start`, which
//    DEVIATES from CSS's `stretch`, and BnLayoutDemo's wrap row's second line sits
//    at y=40 (the line-1 cross size) BECAUSE of it. `alignContent` is not even on
//    the allow-list, so no patch would catch a shell that "corrected" Yoga here.
//    Do not.
//
// The style GRAMMAR has exactly one normative statement —
// docs/plans/2026-07-13-phase-6.1-design.md §"Style value grammar (normative)" —
// and it is deliberately not restated here: three copies of a grammar is how two
// hand-written parsers drift.
//
// Threading: main-thread only. Every entry point is called from inside
// `BnWidgetMapper.applyBatch` (already hopped to the main queue at CommitFrame) or
// from `viewDidLayoutSubviews`. The measure/context map below is therefore a plain
// unsynchronised container, exactly as Kotlin's maps are.
// ─────────────────────────────────────────────────────────────────────────────

#include "BnYogaLayout.h"

#include <yoga/Yoga.h>

#import <Foundation/Foundation.h>

#include <cmath>
#include <cstdlib>
#include <cstring>
#include <string>
#include <unordered_map>
#include <xlocale.h> // strtof_l — see [bn_parse_number]

// The re-declared measure modes must still BE Yoga's, or the trampoline hands
// Swift the wrong mode and every measured leaf is subtly mis-sized.
static_assert((int)bn_yoga_measure_undefined == (int)YGMeasureModeUndefined, "YGMeasureMode drift");
static_assert((int)bn_yoga_measure_exactly == (int)YGMeasureModeExactly, "YGMeasureMode drift");
static_assert((int)bn_yoga_measure_at_most == (int)YGMeasureModeAtMost, "YGMeasureMode drift");

static const char* const kTag = "[BnYogaLayout]";

static void bn_log_ignore(const char* property, const char* detail) {
    NSLog(@"%s SetStyle %s ignored: %s", kTag, property, detail ? detail : "(null)");
}

// ─────────────────────────────────────────────────────────────────────────────
// The style-name table — THE ROUTING TABLE'S LAYOUT HALF
//
// A hand-written MIRROR of `NativeRenderer.YogaStyleAttributes` (and of Kotlin's
// `YogaLayout.YOGA_STYLES`), and it is PINNED BY A DRIFT TEST:
// `tests/BlazorNative.Renderer.Tests/ShellStyleTableDriftTests.cs` parses this
// literal out of this file and asserts set-equality with .NET's set plus
// disjointness from `VisualStyleAttributes`. Without that pin, a name added on the
// .NET side and missed here does not fail loudly: `BnWidgetMapper`'s router sends
// it to the VISUAL branch, which logs "not yet supported" and SILENTLY DROPS the
// style.
//
// Keep the declaration a plain brace-initialised array of quoted names, declared
// at the start of its line: the drift test parses exactly that shape, and a
// declaration it cannot find fails it LOUDLY (never silently-empty).
//
// In particular `padding`, `margin`, `width` and `height` are LAYOUT — they belong
// to the Yoga node and to NOTHING else. Yoga lays a container's children out
// inside its padding box, so a shell that ALSO insets the view (the old
// `layoutMargins` / `isLayoutMarginsRelativeArrangement` arm) double-applies it.
// ─────────────────────────────────────────────────────────────────────────────
static const char* const kYogaStyles[] = {
    "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap",
    "alignSelf", "flexGrow", "flexShrink", "flexBasis",
    "width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight",
    "padding", "margin",
    "position", "top", "right", "bottom", "left",
};

int32_t bn_yoga_is_layout_style(const char* name) {
    if (name == NULL) return 0;
    const size_t count = sizeof(kYogaStyles) / sizeof(kYogaStyles[0]);
    for (size_t i = 0; i < count; i++) {
        if (strcmp(name, kYogaStyles[i]) == 0) return 1; // ORDINAL / case-sensitive
    }
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// The shared config + the measure registry
// ─────────────────────────────────────────────────────────────────────────────

/// Every node in every tree shares one config, and its whole job is
/// `pointScaleFactor = 0` — see the file header, reason #1.
///
/// A function-local static initialised by a lambda, not the `if (ptr == NULL)`
/// idiom: this file is C++, so the static's initialisation is guaranteed to run
/// exactly once even if two threads reach it together (the "magic static" of
/// [stmt.dcl]). The check-then-assign form is not — it would race two YGConfigNew
/// calls and leak one. The rest of this file is main-thread-only by contract, but a
/// thread-safety story that costs a lambda is not worth having a caveat about.
static YGConfigRef bn_yoga_config(void) {
    static YGConfigRef config = [] {
        YGConfigRef c = YGConfigNew();
        YGConfigSetPointScaleFactor(c, 0.0f);
        return c;
    }();
    return config;
}

namespace {
struct BnMeasureEntry {
    bn_yoga_measure_fn fn;
    void* context;
};
} // namespace

/// node → (Swift trampoline, context). Also IS the "has a measure function" set:
/// `YGNodeMarkDirty` ABORTS on a node without one, so [bn_yoga_node_mark_dirty]
/// checks membership here first. Kept on this side rather than using Yoga's own
/// context slot so the .mm owns the whole lifetime story in one place — the purge
/// in [bn_yoga_node_free_subtree] is the only thing that may drop an entry.
static std::unordered_map<YGNodeRef, BnMeasureEntry>& bn_measure_registry(void) {
    static std::unordered_map<YGNodeRef, BnMeasureEntry> registry;
    return registry;
}

/// Yoga's measure callback → the Swift trampoline. A C function pointer cannot
/// capture, so the node's identity travels through the registry (and, from there,
/// through the `void* context` Swift handed us — an unretained UIView).
static YGSize bn_yoga_measure_trampoline(YGNodeConstRef node,
                                         float width,
                                         YGMeasureMode widthMode,
                                         float height,
                                         YGMeasureMode heightMode) {
    YGSize out = {0.0f, 0.0f};
    auto& registry = bn_measure_registry();
    auto it = registry.find(const_cast<YGNodeRef>(node));
    if (it == registry.end() || it->second.fn == NULL) return out;
    const bn_yoga_size size = it->second.fn(it->second.context,
                                            width,
                                            (bn_yoga_measure_mode)widthMode,
                                            height,
                                            (bn_yoga_measure_mode)heightMode);
    out.width = size.width;
    out.height = size.height;
    return out;
}

// ─────────────────────────────────────────────────────────────────────────────
// The node tree
// ─────────────────────────────────────────────────────────────────────────────

bn_yoga_node bn_yoga_node_new(void) {
    return (bn_yoga_node)YGNodeNewWithConfig(bn_yoga_config());
}

void bn_yoga_node_insert_child(bn_yoga_node parent, bn_yoga_node child, int32_t index) {
    YGNodeRef p = (YGNodeRef)parent;
    const int32_t count = (int32_t)YGNodeGetChildCount(p);
    // −1 = append; anything else is the exact index the VIEW went to.
    //
    // **An out-of-range insertIndex is a RENDERER BUG, and the two shells answer it
    // differently ON PURPOSE — ONE recorded decision, not two parsers disagreeing.**
    // See the Gate 4 ledger (docs/plans/2026-07-13-phase-6.1-implementation-plan.md
    // §"Recorded decisions carried in from the Gate 3 review", entry 2), which is the
    // single statement; `YogaLayout.createNode`'s comment points at the same entry.
    // Here: CLAMPED and logged, because `BnWidgetMapper.handleCreate` clamps the VIEW
    // insert identically (`insertIndex <= subviews.count`, else append) — so the two
    // trees cannot skew against each other, which is the only thing the mirroring
    // exists to prevent — and because trapping inside a render callback on iOS aborts
    // the app with no diagnostic at all. Android throws, where a JNI throw surfaces as
    // a stack trace naming the renderer.
    int32_t at = (index < 0 || index > count) ? count : index;
    if (index > count) {
        NSLog(@"%s insert index %d out of range (childCount=%d) — appending", kTag, index, count);
    }
    YGNodeInsertChild(p, (YGNodeRef)child, (size_t)at);
}

void bn_yoga_node_remove_child(bn_yoga_node parent, bn_yoga_node child) {
    YGNodeRemoveChild((YGNodeRef)parent, (YGNodeRef)child);
}

bn_yoga_node bn_yoga_node_get_owner(bn_yoga_node node) {
    return (bn_yoga_node)YGNodeGetOwner((YGNodeRef)node);
}

/// Clears one node's measure function (breaking the last edge to its UIView) and
/// drops its registry entry.
static void bn_yoga_purge(YGNodeRef node) {
    auto& registry = bn_measure_registry();
    if (registry.erase(node) > 0) {
        YGNodeSetMeasureFunc(node, NULL);
    }
}

static void bn_yoga_purge_subtree(YGNodeRef node) {
    const size_t count = YGNodeGetChildCount(node);
    for (size_t i = 0; i < count; i++) {
        bn_yoga_purge_subtree(YGNodeGetChild(node, i));
    }
    bn_yoga_purge(node);
}

void bn_yoga_node_free_subtree(bn_yoga_node node) {
    YGNodeRef n = (YGNodeRef)node;
    bn_yoga_purge_subtree(n);
    YGNodeFreeRecursive(n); // raw native memory — nothing else will ever free it
}

// ─────────────────────────────────────────────────────────────────────────────
// The value grammar
// ─────────────────────────────────────────────────────────────────────────────

/// THE number production, screened BEFORE parsing so that **the whole string must
/// be consumed** — trailing garbage is a rejection, never a prefix parse:
///
///     [+-]? ( digit+ ( '.' digit* )? | '.' digit+ ) ( [eE] [+-]? digit+ )?
///
/// Screening first is what keeps `strtof` on the rails: unscreened it would accept
/// `"12px"` (prefix parse), `" 12"` (leading whitespace), `"0x10"`, `"inf"` and
/// `"nan"` — all of which Kotlin's `toFloatOrNull` rejects. See the file header,
/// reason #3.
static int bn_parse_number(const char* s, float* out) {
    if (s == NULL || *s == '\0') return 0;

    const char* p = s;
    if (*p == '+' || *p == '-') p++;

    int intDigits = 0;
    while (*p >= '0' && *p <= '9') { p++; intDigits++; }

    if (*p == '.') {
        p++;
        int fracDigits = 0;
        while (*p >= '0' && *p <= '9') { p++; fracDigits++; }
        if (intDigits == 0 && fracDigits == 0) return 0; // "." / "+." / "-."
    } else if (intDigits == 0) {
        return 0; // no digits at all — "abc", "", "%", "e5", "inf", "nan", "0x10"
    }

    if (*p == 'e' || *p == 'E') {
        p++;
        if (*p == '+' || *p == '-') p++;
        int expDigits = 0;
        while (*p >= '0' && *p <= '9') { p++; expDigits++; }
        if (expDigits == 0) return 0; // "1e", "1e+"
    }

    if (*p != '\0') return 0; // THE WHOLE STRING MUST BE CONSUMED — "12px", "12f"

    // The production is guaranteed now, so strtof cannot wander off into hex /
    // inf / nan; the endptr check is belt-and-braces, and the finiteness check
    // rejects an overflowing literal ("1e40") exactly as Kotlin's does.
    //
    // **strtof_l with the C locale — a CROSS-SHELL PARITY GUARD, not a micro-
    // optimisation.** Plain `strtof` honours `LC_NUMERIC`: under a comma-decimal
    // locale it stops at the `.` of "12.5", the endptr check then REJECTS the whole
    // value — while Kotlin's `Float.parseFloat` is locale-INDEPENDENT and accepts it.
    // That is exactly the "one shell HONOURS what the other IGNORES" divergence the
    // strict-parse rule exists to prevent (file header, reason #3), reintroduced
    // through the one call the grammar screen was meant to make safe. Apple defaults
    // to the C locale, so it is latent — but it is one `setlocale` in one linked
    // dependency away, and the symptom (every fractional frame wrong, iOS only) is
    // maximally confusing. A NULL locale_t IS the C locale on Darwin.
    char* end = NULL;
    const float value = strtof_l(s, &end, NULL);
    if (end != s + strlen(s)) return 0;
    if (std::isnan(value) || std::isinf(value)) return 0;
    *out = value;
    return 1;
}

namespace {
enum BnLengthKind {
    kBnLengthRejected = 0,
    kBnLengthPoints,
    kBnLengthPercent,
    kBnLengthAuto,
};
struct BnLength {
    BnLengthKind kind;
    float value;
};
} // namespace

/// Parses a layout length: a bare number (points), `N%`, or `auto` — nothing else.
/// There are NO unit suffixes in this grammar (`px` is not dp, `sp` is font-scaled,
/// and neither exists on iOS).
///
/// A NULL value means **the property's Yoga default**, and the two flags are what
/// make that a per-property fact rather than a guess:
///
///   - [autoAllowed]   — `auto` is an accepted VALUE (Yoga has a setter for it).
///   - [autoIsDefault] — `auto` is also the property's DEFAULT.
///
/// They coincide for `width`/`height`/`flexBasis` and they DO NOT for `margin`,
/// whose default is undefined → 0 while `auto` means "absorb the free space". One
/// flag doing both jobs is how a null reset silently re-centres a node (file
/// header, reason #2). Everything else resets to `YGUndefined` points — Yoga's
/// "unset".
///
/// Negatives are accepted ONLY where Yoga defines them (`margin` and the position
/// offsets); everywhere else they are REJECTED AND LOGGED, never clamped — a
/// silently-clamped 0 is a frame the two platforms may not agree on.
static BnLength bn_parse_length(const char* property,
                                const char* value,
                                int autoAllowed,
                                int autoIsDefault,
                                int negativeAllowed) {
    BnLength out = {kBnLengthRejected, 0.0f};

    if (value == NULL) {
        if (autoIsDefault) { out.kind = kBnLengthAuto; return out; }
        out.kind = kBnLengthPoints;
        out.value = YGUndefined;
        return out;
    }

    if (strcmp(value, "auto") == 0) {
        if (autoAllowed) { out.kind = kBnLengthAuto; return out; }
        bn_log_ignore(property, "'auto' is not a legal value for this property");
        return out;
    }

    const size_t len = strlen(value);
    const int percent = (len > 0 && value[len - 1] == '%');

    // A std::string, not a fixed `char scratch[64]`: with a buffer, a percentage
    // literal LONGER than the buffer is rejected on iOS for a reason that has
    // nothing to do with the grammar — and Kotlin's `value.dropLast(1)` has no such
    // limit, so the two shells would disagree on the wire's REJECTION path. Nothing
    // .NET emits comes close to 64 chars, so this is unreachable in practice; the
    // grammar is nonetheless meant to be comparable on BOTH paths, and the file is
    // C++, so the asymmetry costs one line to delete rather than one line to excuse.
    const std::string number = percent ? std::string(value, len - 1) : std::string(value);

    float parsed = 0.0f;
    if (!bn_parse_number(number.c_str(), &parsed)) {
        bn_log_ignore(property, value); // "not a number, a percentage or 'auto'"
        return out;
    }
    if (parsed < 0.0f && !negativeAllowed) {
        NSLog(@"%s SetStyle %s ignored: negative values are not accepted (got '%s')",
              kTag, property, value);
        return out;
    }

    out.kind = percent ? kBnLengthPercent : kBnLengthPoints;
    out.value = parsed;
    return out;
}

/// A unitless float (`flexGrow`/`flexShrink`) — NOT a length: no `%`, no `auto`,
/// no units, no negatives. NULL → [defaultValue] (Yoga's).
static int bn_parse_unitless(const char* property, const char* value, float defaultValue, float* out) {
    if (value == NULL) { *out = defaultValue; return 1; }
    float parsed = 0.0f;
    if (!bn_parse_number(value, &parsed)) {
        bn_log_ignore(property, value); // "not a unitless number"
        return 0;
    }
    if (parsed < 0.0f) {
        NSLog(@"%s SetStyle %s ignored: negative values are not accepted (got '%s')",
              kTag, property, value);
        return 0;
    }
    *out = parsed;
    return 1;
}

// Yoga's setters come in three shapes; these adapters let one dispatcher serve
// every length property (the edge/gutter ones need the edge bound in).
typedef void (*BnPointsSetter)(YGNodeRef, float);
typedef void (*BnPercentSetter)(YGNodeRef, float);
typedef void (*BnAutoSetter)(YGNodeRef);

static int bn_apply_length(YGNodeRef node,
                           BnLength length,
                           BnPointsSetter points,
                           BnPercentSetter percent,
                           BnAutoSetter autoSetter) {
    switch (length.kind) {
        case kBnLengthPoints:  points(node, length.value); return 1;
        case kBnLengthPercent: percent(node, length.value); return 1;
        case kBnLengthAuto:
            if (autoSetter != NULL) { autoSetter(node); return 1; }
            return 0; // unreachable: bn_parse_length only yields Auto when allowed
        case kBnLengthRejected:
        default:
            return 0;
    }
}

static void bn_set_padding(YGNodeRef n, float v) { YGNodeStyleSetPadding(n, YGEdgeAll, v); }
static void bn_set_padding_percent(YGNodeRef n, float v) { YGNodeStyleSetPaddingPercent(n, YGEdgeAll, v); }
static void bn_set_gap(YGNodeRef n, float v) { YGNodeStyleSetGap(n, YGGutterAll, v); }
static void bn_set_gap_percent(YGNodeRef n, float v) { YGNodeStyleSetGapPercent(n, YGGutterAll, v); }
static void bn_set_margin(YGNodeRef n, float v) { YGNodeStyleSetMargin(n, YGEdgeAll, v); }
static void bn_set_margin_percent(YGNodeRef n, float v) { YGNodeStyleSetMarginPercent(n, YGEdgeAll, v); }
static void bn_set_margin_auto(YGNodeRef n) { YGNodeStyleSetMarginAuto(n, YGEdgeAll); }

/// The enum words are EXACTLY the strings `FlexStyleValues.ToStyleValue()` emits —
/// no aliases, no case folding. An unknown word is logged and ignored.
static int bn_parse_align(const char* value, YGAlign* out) {
    if (value == NULL) return 0;
    if (strcmp(value, "auto") == 0)       { *out = YGAlignAuto;      return 1; }
    if (strcmp(value, "flex-start") == 0) { *out = YGAlignFlexStart; return 1; }
    if (strcmp(value, "center") == 0)     { *out = YGAlignCenter;    return 1; }
    if (strcmp(value, "flex-end") == 0)   { *out = YGAlignFlexEnd;   return 1; }
    if (strcmp(value, "stretch") == 0)    { *out = YGAlignStretch;   return 1; }
    if (strcmp(value, "baseline") == 0)   { *out = YGAlignBaseline;  return 1; }
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// SetStyle → Yoga
//
// The reset defaults below are YOGA's, not CSS's, and they say so: `flexShrink`
// back to **0** (CSS says 1), `alignItems` to `stretch`, `flexDirection` to
// `column`, `justifyContent` to `flex-start`.
// ─────────────────────────────────────────────────────────────────────────────

int32_t bn_yoga_node_set_style(bn_yoga_node handle, const char* name, const char* value) {
    YGNodeRef node = (YGNodeRef)handle;
    if (node == NULL || name == NULL) return 0;

    // ── Enum words ──
    if (strcmp(name, "flexDirection") == 0) {
        YGFlexDirection d;
        if (value == NULL)                            d = YGFlexDirectionColumn;
        else if (strcmp(value, "row") == 0)            d = YGFlexDirectionRow;
        else if (strcmp(value, "column") == 0)         d = YGFlexDirectionColumn;
        else if (strcmp(value, "row-reverse") == 0)    d = YGFlexDirectionRowReverse;
        else if (strcmp(value, "column-reverse") == 0) d = YGFlexDirectionColumnReverse;
        else { bn_log_ignore(name, value); return 0; }
        YGNodeStyleSetFlexDirection(node, d);
        return 1;
    }
    if (strcmp(name, "justifyContent") == 0) {
        YGJustify j;
        if (value == NULL)                           j = YGJustifyFlexStart;
        else if (strcmp(value, "flex-start") == 0)    j = YGJustifyFlexStart;
        else if (strcmp(value, "center") == 0)        j = YGJustifyCenter;
        else if (strcmp(value, "flex-end") == 0)      j = YGJustifyFlexEnd;
        else if (strcmp(value, "space-between") == 0) j = YGJustifySpaceBetween;
        else if (strcmp(value, "space-around") == 0)  j = YGJustifySpaceAround;
        else if (strcmp(value, "space-evenly") == 0)  j = YGJustifySpaceEvenly;
        else { bn_log_ignore(name, value); return 0; }
        YGNodeStyleSetJustifyContent(node, j);
        return 1;
    }
    if (strcmp(name, "alignItems") == 0) {
        YGAlign a = YGAlignStretch; // Yoga's default
        if (value != NULL && !bn_parse_align(value, &a)) { bn_log_ignore(name, value); return 0; }
        YGNodeStyleSetAlignItems(node, a);
        return 1;
    }
    if (strcmp(name, "alignSelf") == 0) {
        YGAlign a = YGAlignAuto; // Yoga's default
        if (value != NULL && !bn_parse_align(value, &a)) { bn_log_ignore(name, value); return 0; }
        YGNodeStyleSetAlignSelf(node, a);
        return 1;
    }
    if (strcmp(name, "flexWrap") == 0) {
        YGWrap w;
        if (value == NULL || strcmp(value, "nowrap") == 0) w = YGWrapNoWrap;
        else if (strcmp(value, "wrap") == 0)               w = YGWrapWrap;
        else if (strcmp(value, "wrap-reverse") == 0)       w = YGWrapWrapReverse;
        else { bn_log_ignore(name, value); return 0; }
        YGNodeStyleSetFlexWrap(node, w);
        return 1;
    }
    if (strcmp(name, "position") == 0) {
        YGPositionType p;
        if (value == NULL || strcmp(value, "relative") == 0) p = YGPositionTypeRelative;
        else if (strcmp(value, "absolute") == 0)             p = YGPositionTypeAbsolute;
        else { bn_log_ignore(name, value); return 0; }
        YGNodeStyleSetPositionType(node, p);
        return 1;
    }

    // ── Unitless floats (NOT lengths: no %, no auto) ──
    if (strcmp(name, "flexGrow") == 0) {
        float v = 0.0f;
        if (!bn_parse_unitless(name, value, 0.0f, &v)) return 0;
        YGNodeStyleSetFlexGrow(node, v);
        return 1;
    }
    if (strcmp(name, "flexShrink") == 0) {
        // Yoga's flexShrink default is 0 — it DEVIATES from CSS's 1. Resetting to
        // 1 here would silently make every node shrinkable.
        float v = 0.0f;
        if (!bn_parse_unitless(name, value, 0.0f, &v)) return 0;
        YGNodeStyleSetFlexShrink(node, v);
        return 1;
    }

    // ── Layout lengths ──
    // width/height/flexBasis: `auto` is BOTH a legal value AND the Yoga default,
    // so a null reset lands on the auto setter.
    if (strcmp(name, "width") == 0) {
        return bn_apply_length(node, bn_parse_length(name, value, 1, 1, 0),
                               YGNodeStyleSetWidth, YGNodeStyleSetWidthPercent, YGNodeStyleSetWidthAuto);
    }
    if (strcmp(name, "height") == 0) {
        return bn_apply_length(node, bn_parse_length(name, value, 1, 1, 0),
                               YGNodeStyleSetHeight, YGNodeStyleSetHeightPercent, YGNodeStyleSetHeightAuto);
    }
    if (strcmp(name, "flexBasis") == 0) {
        return bn_apply_length(node, bn_parse_length(name, value, 1, 1, 0),
                               YGNodeStyleSetFlexBasis, YGNodeStyleSetFlexBasisPercent, YGNodeStyleSetFlexBasisAuto);
    }
    if (strcmp(name, "minWidth") == 0) {
        return bn_apply_length(node, bn_parse_length(name, value, 0, 0, 0),
                               YGNodeStyleSetMinWidth, YGNodeStyleSetMinWidthPercent, NULL);
    }
    if (strcmp(name, "maxWidth") == 0) {
        return bn_apply_length(node, bn_parse_length(name, value, 0, 0, 0),
                               YGNodeStyleSetMaxWidth, YGNodeStyleSetMaxWidthPercent, NULL);
    }
    if (strcmp(name, "minHeight") == 0) {
        return bn_apply_length(node, bn_parse_length(name, value, 0, 0, 0),
                               YGNodeStyleSetMinHeight, YGNodeStyleSetMinHeightPercent, NULL);
    }
    if (strcmp(name, "maxHeight") == 0) {
        return bn_apply_length(node, bn_parse_length(name, value, 0, 0, 0),
                               YGNodeStyleSetMaxHeight, YGNodeStyleSetMaxHeightPercent, NULL);
    }
    if (strcmp(name, "padding") == 0) {
        return bn_apply_length(node, bn_parse_length(name, value, 0, 0, 0),
                               bn_set_padding, bn_set_padding_percent, NULL);
    }
    if (strcmp(name, "gap") == 0) {
        return bn_apply_length(node, bn_parse_length(name, value, 0, 0, 0),
                               bn_set_gap, bn_set_gap_percent, NULL);
    }
    if (strcmp(name, "margin") == 0) {
        // autoAllowed = 1, autoIsDefault = 0 — and the second half is the whole
        // point (file header, reason #2).
        return bn_apply_length(node, bn_parse_length(name, value, 1, 0, 1),
                               bn_set_margin, bn_set_margin_percent, bn_set_margin_auto);
    }
    if (strcmp(name, "top") == 0 || strcmp(name, "right") == 0 ||
        strcmp(name, "bottom") == 0 || strcmp(name, "left") == 0) {
        YGEdge edge = YGEdgeLeft;
        if (strcmp(name, "top") == 0) edge = YGEdgeTop;
        else if (strcmp(name, "right") == 0) edge = YGEdgeRight;
        else if (strcmp(name, "bottom") == 0) edge = YGEdgeBottom;
        // margin and the position offsets are the ONLY places a negative is
        // meaningful — Yoga defines both.
        const BnLength length = bn_parse_length(name, value, 0, 0, 1);
        switch (length.kind) {
            case kBnLengthPoints:  YGNodeStyleSetPosition(node, edge, length.value); return 1;
            case kBnLengthPercent: YGNodeStyleSetPositionPercent(node, edge, length.value); return 1;
            default: return 0;
        }
    }

    bn_log_ignore(name, "not a Yoga style (routing bug — bn_yoga_is_layout_style said it was)");
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// Measurement
// ─────────────────────────────────────────────────────────────────────────────

void bn_yoga_node_set_measure(bn_yoga_node handle, bn_yoga_measure_fn fn, void* context) {
    YGNodeRef node = (YGNodeRef)handle;
    if (fn == NULL) {
        bn_yoga_purge(node);
        return;
    }
    BnMeasureEntry entry;
    entry.fn = fn;
    entry.context = context;
    bn_measure_registry()[node] = entry;
    YGNodeSetMeasureFunc(node, bn_yoga_measure_trampoline);
}

void bn_yoga_node_mark_dirty(bn_yoga_node handle) {
    YGNodeRef node = (YGNodeRef)handle;
    auto& registry = bn_measure_registry();
    if (registry.find(node) == registry.end()) return; // YGNodeMarkDirty would abort
    if (YGNodeIsDirty(node)) return;
    YGNodeMarkDirty(node);
}

// ─────────────────────────────────────────────────────────────────────────────
// The layout pass
// ─────────────────────────────────────────────────────────────────────────────

void bn_yoga_calculate(bn_yoga_node handle, float availableWidth, float availableHeight) {
    YGNodeRef root = (YGNodeRef)handle;

    if (availableWidth > 0.0f) YGNodeStyleSetWidth(root, availableWidth);
    else YGNodeStyleSetWidthAuto(root);

    if (availableHeight > 0.0f) YGNodeStyleSetHeight(root, availableHeight);
    else YGNodeStyleSetHeightAuto(root);

    YGNodeStyleSetDirection(root, YGDirectionLTR);
    YGNodeCalculateLayout(root, YGUndefined, YGUndefined, YGDirectionLTR);
}

bn_yoga_frame bn_yoga_node_get_frame(bn_yoga_node handle) {
    YGNodeRef node = (YGNodeRef)handle;
    bn_yoga_frame frame;
    frame.x = YGNodeLayoutGetLeft(node);
    frame.y = YGNodeLayoutGetTop(node);
    frame.width = YGNodeLayoutGetWidth(node);
    frame.height = YGNodeLayoutGetHeight(node);
    return frame;
}
