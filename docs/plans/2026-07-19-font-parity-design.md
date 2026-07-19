# Font parity — bundle one font across both shells (#126)

**Standalone feature** (not a milestone — owner's framing), post-M10. Closes review finding
**#126**. Bundle **Inter** (OFL, owner-chosen) and force it on every text leaf so text metrics
match across shells → the `[MEASURED]` skip in the frame-table drift tests becomes assertable,
*tightening* the parity contract. **Native control chrome stays native** (buttons/switches/etc.
— explicit boundary, unchanged).

## What the current code actually is (scoping correction)
- **No `fontWeight` prop exists.** Text styling today is `fontSize`-only: iOS
  `BnWidgetMapper.swift:3452` `label.font = UIFont.systemFont(ofSize: size)`; Android
  `WidgetMapper.kt` `setTextSize` only. So there are **no weights to map** — the issue's
  "matching weights" is moot for now. Bundle a single Inter weight (Regular); if the variable
  font renders identically we can use it, but static **Inter-Regular** is the simplest asset
  that can't drift on a weight axis. (A future `fontWeight` prop would extend both mappers +
  bundle more weights — out of scope here.)
- Text leaves are `UILabel` / `TextView` (the collapse targets). Buttons are chrome — leave
  them native.

## Gate A — bundle + register Inter on both shells
- **Asset:** obtain `Inter-Regular.ttf` (OFL) from the rsms/inter release (or Google Fonts) —
  fetch the binary (`curl -L`), commit it with the OFL license text. One canonical location
  per shell + the template mirror.
- **iOS:** add the ttf to the app bundle via `project.yml` (BnHost resources); register with
  `UIAppFonts` in `Info.plist` (or `CTFontManagerRegisterFontsForURL` at boot). Prove it loads:
  a test that `UIFont(name: "Inter-Regular", size:)` (exact PostScript name — verify with
  `fc-scan`/`CTFont`) is non-nil.
- **Android:** ship the ttf in `res/font/` (or assets); resolve a `Typeface`. Prove it loads:
  the typeface is non-null/non-default.
- No behavior change yet — just the asset present + registerable on both. Template mirror synced.

## Gate B — force Inter on every text leaf + the measure callbacks
- **iOS:** at `UILabel` creation set `label.font = interFont(defaultSize)`; in the `fontSize`
  arm use `UIFont(name:"Inter-Regular", size:) ?? .systemFont(ofSize:)` (fallback logged). Apply
  the SAME font in the text-measure path (the intrinsic-size / measure callback) so measurement
  uses Inter, not the system font.
- **Android:** at `TextView` creation `setTypeface(inter)`; ensure the Yoga measure callback
  measures with the Inter typeface (the measure `TextView`/`Paint` must carry it too). Consider
  `includeFontPadding` (see Gate C).
- **Tests (DoD #1):** assert the resolved family on both — iOS mirrors the existing
  `BnRenderTests.swift:96` `pointSize` assertion (add family == Inter); Android an instrumented
  assertion the `TextView.typeface` resolves to the bundled Inter. Both are dispatch/instrumented
  lanes — the owner bridges the CI wait.

## Gate C — tighten the parity contract (the payoff)
- **The hard part, stated honestly:** the same ttf does NOT guarantee equal *measured heights* —
  Android `TextView` adds `includeFontPadding` (top/bottom padding from font metrics) and iOS
  `UILabel` line height differs. Normalize: Android `includeFontPadding=false` (and/or an explicit
  line-height), match iOS's line box. Measure a text row on both at the same `fontSize` and
  confirm equal heights (within the frame table's existing tolerance).
- Remove the `[MEASURED]` skip in `FrameAssertions.kt:64,92` for cells that now hold; assert the
  text cells in `BnDemoFrameTables.kt:42`. **If any cell genuinely can't reach parity** (a metric
  Android/iOS compute differently even normalized), narrow the skip to exactly those cells **with
  a written rationale** (DoD #2's sanctioned fallback) — do not force a false green.
- `ShellFrameTableDriftTests` passes with the newly-asserted text cells (both instrumented +
  iOS lanes). Reconcile any count moves across ci.yml + README (all four rows).

## Gate D — doc + close
- Update `website/docs/architecture/parity.md`'s "Not pixel-identical rendering" note: **typography
  is now identical (one bundled font); control chrome remains native — a deliberate boundary.**
- Conclusion doc; close #126; PR to main. `feat:` (new bundled-font capability) → the rolling
  release PR walks a minor (0.1.0 → 0.2.0) — the owner's call when to publish.

## Risks / honest unknowns
- **Measured-height parity (Gate C)** is the make-or-break; it may need real line-metric
  normalization and a few cells may stay `[MEASURED]` with rationale. That's success, not failure
  — the contract still tightens for every cell that matches.
- **Font PostScript name** must be verified (the `name:` iOS resolves by, and the Android family)
  — a wrong name silently falls back to the system font and re-breaks parity; the resolved-family
  test (Gate B) is the guard.
- **Binary asset in git** — one ~300KB ttf per shell + template mirror; keep it a single canonical
  copy where possible.
