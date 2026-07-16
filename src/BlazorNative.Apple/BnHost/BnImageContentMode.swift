// ─────────────────────────────────────────────────────────────────────────────
// BnImageContentMode — Phase 7.5 (design decision 3): **THE CONTENT-MODE WIRE
// TABLE, AS A PURE DECISION.** The Swift twin of Kotlin's `ImageContentModeTable.kt`,
// row for row — the table is SHARED, the widget spelling is per-shell
// (`BnWidgetMapper`'s `contentMode` arm maps each row to `UIView.ContentMode`;
// Android's maps the same rows to `ScaleType`).
//
// It is a top-level pure function in its own file for the `bnIsLiveImageRequest`
// reason: the strict four-word parse is unit-testable with no UIKit tree and no
// fixture server — `BnImagePolishMapperTests` pins every row before a single
// frame is read.
//
// **THE STRICT GRAMMAR** (the items-grammar discipline, scaled to a single token):
// exact, case-sensitive membership in the four lowercase wire words.
// `ImageContentMode.ToWireValue` (.NET) is the writer; this is its acceptance set,
// and the two must be EQUAL — a value one shell honours and the other ignores makes
// the two shells' images disagree for a reason that has nothing to do with the
// engine (the `parseWireFloat` lesson).
//
//  - **`nil` (the prop was REMOVED) → [.contain]** — the `Enabled`-null precedent:
//    null on the prop wire means "the author took it away", and what it restores is
//    the DEFAULT, which has been aspect-fit on both shells since 6.3 set it
//    explicitly (the framework defaults disagree; `contain` is the value 6.3 said
//    an M7 `ContentMode` would default to — deliberately NOT RN's `cover`, the
//    recorded decision).
//  - **an unknown word → `nil`: DIAGNOSE, DON'T APPLY** (the modal style-ignore
//    precedent). Reachable by hand-rolled wire only (`ImageContentMode` is a .NET
//    enum — an invalid value is unrepresentable from the component), and the caller
//    keeps the node's CURRENT mode: a guessed fallback would make two shells guess
//    differently.
//
// **MODE IS PAINT-ONLY** — the parity rule, normative: the layout box is Yoga's and
// never changes with mode; the measure func never consults it. That rule is
// enforced at the call site (no `markDirty`, no Yoga write) and asserted as four
// identical frames under four modes on both shells; this file's job is only that
// the four words parse identically.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

/// The four modes, as the wire words name them. The `UIView.ContentMode` spelling
/// of each row lives in the mapper's `contentMode` arm (the Kotlin shape: the
/// table is shared, the widget constant is the shell's).
enum BnImageContentMode {
    case contain
    case cover
    case stretch
    case center
}

/// The strict wire word → mode. `nil` wire value = the prop was removed → the
/// default ([BnImageContentMode.contain]); an unknown word → `nil` =
/// diagnose-don't-apply (the caller keeps the current mode). See the file header
/// for why both rows are what they are.
func bnContentModeFor(_ wire: String?) -> BnImageContentMode? {
    switch wire {
    case nil: return .contain
    case "contain": return .contain
    case "cover": return .cover
    case "stretch": return .stretch
    case "center": return .center
    default: return nil
    }
}
