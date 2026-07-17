package io.blazornative.shell

/**
 * Phase 7.5 (design decision 3) — **THE CONTENT-MODE WIRE TABLE, AS A PURE DECISION.**
 *
 * **It lives in `src/main/kotlin`, NOT `src/androidMain`, and that is load-bearing** — the
 * [isLiveImageRequest] precedent, verbatim: the JVM suite (`src/test/kotlin`) compiles against
 * the shared source set, so the strict four-word parse is unit-testable **before any emulator
 * boots**. The table below is the whole of the decision; `WidgetMapper`'s `contentMode` arm is
 * only the lookup plus the per-platform `ScaleType` spelling of each row (and iOS Gate 3 owes
 * the same table with `UIView.ContentMode` spellings — mechanism per shell, table shared).
 *
 * **THE STRICT GRAMMAR** (the items-grammar discipline, scaled to a single token): exact,
 * case-sensitive membership in the four lowercase wire words. `BnImage.cs`'s
 * `ImageContentMode.ToWireValue` is the writer; this is its acceptance set, and the two must
 * be EQUAL — a value one shell honours and the other ignores makes the two shells' images
 * disagree for a reason that has nothing to do with the engine (the `parseWireFloat` lesson).
 *
 *  - **`null` (the prop was REMOVED) → [ImageContentMode.CONTAIN]** — the `Enabled`-null
 *    precedent: null on the prop wire means "the author took it away", and what it restores
 *    is the DEFAULT, which has been aspect-fit on both shells since 6.3 set it explicitly
 *    (the framework defaults disagree; `contain` is the value 6.3 said an M7 `ContentMode`
 *    would default to — deliberately NOT RN's `cover`, the recorded decision).
 *  - **an unknown word → `null`: DIAGNOSE, DON'T APPLY** (the modal style-ignore precedent).
 *    Reachable by hand-rolled wire only (`ImageContentMode` is an enum — an invalid value is
 *    unrepresentable from the component), and the caller keeps the node's CURRENT mode: a
 *    guessed fallback would make two shells guess differently.
 *
 * **MODE IS PAINT-ONLY** — the parity rule, normative: the layout box is Yoga's and never
 * changes with mode; the measure func never consults it. That rule is enforced at the call
 * site (no `markDirty`, no Yoga write) and asserted as four identical frames under four
 * modes on both shells; this file's job is only that the four words parse identically.
 */
internal enum class ImageContentMode { CONTAIN, COVER, STRETCH, CENTER }

/**
 * The strict wire word → mode. `null` wire value = the prop was removed → the default
 * ([ImageContentMode.CONTAIN]); an unknown word → `null` = diagnose-don't-apply (the caller
 * keeps the current mode). See the table KDoc above for why both rows are what they are.
 */
internal fun contentModeFor(wire: String?): ImageContentMode? = when (wire) {
    null -> ImageContentMode.CONTAIN
    "contain" -> ImageContentMode.CONTAIN
    "cover" -> ImageContentMode.COVER
    "stretch" -> ImageContentMode.STRETCH
    "center" -> ImageContentMode.CENTER
    else -> null
}
