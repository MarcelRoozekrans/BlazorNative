# Font parity — conclusion (#126 closed)

Standalone feature, post-M10. Bundled **Inter** (OFL) on both shells and forced it on every
text leaf, then **tightened the parity contract to cover text** — a measured height that used
to be skipped is now an asserted number equal across shells. `feat:` → the rolling release PR
walks a minor (0.1.x → 0.2.0). Native control chrome stays native (a deliberate boundary).

## Gate A — bundle + register Inter
`Inter-Regular.ttf` (rsms/inter v4.1, OFL, 411640 bytes) committed once per shell + the
template mirror (byte-identical). iOS: app resource + `UIAppFonts`; Android: `res/font/
inter_regular.ttf`. Font names verified from the asset: PostScript **`Inter-Regular`** (iOS
`UIFont(name:)`), family **`Inter`** (Android). Load guards: `BnFontTests` (iOS) +
`BnFontAndroidTest` (Android). **CI-proven** — both resolve, not the system fallback.

## Gate B — force Inter on every text leaf + the measure path
iOS: a single `interFont(ofSize:)` helper (`.systemFont` fallback logged) at `UILabel`
creation + the `fontSize` arm; the Yoga measure trampoline reads `label.font`, so measurement
uses Inter. Android: cached `interTypeface` set on `TextView` at creation; the Yoga measure
callback measures the real `TextView`, so layout measures in Inter. **Resolved-family tests
green on both lanes** (iOS 236, Android 210 — reconciled from 235/209 for the load-test
methods). Chrome untouched.

## Gate C — the parity contract tightened (the payoff)
The problem: even with the same TTF, Android `TextView` (`includeFontPadding` ON) measured
text taller than iOS `UILabel`. Fixed with **`includeFontPadding = false`** on Android text
leaves (the oracle mirrors it). CI-verified this normalization breaks no existing assertion.

But the existing demo had **no cell that could prove the payoff** — its one non-button text
cell is multi-line at each shell's *different* default size (family unified, not size), the
rest are button chrome. So we **added one**: a single-line `<BnText FontSize="20">` "parity
row" appended to `/layout` (non-disruptive — nothing above it moves). Its height cell:
- **first run** written as `MEASURED` + a targeted height log → CI measured **iOS 24.333pt**;
- **then** swapped to the **shared literal `24.333`** in *both* frame tables.

The proof, on the last run: `ShellFrameTableDriftTests` asserts the two tables carry the same
literal (symbol equality), and **each shell's on-device oracle confirms it renders that row
within the 0.5dp frame tolerance** — iOS and Android *both green*. So the height that was
skipped *because* the fonts differed is now an asserted number *because they don't*. That is
the contract genuinely covering text.

**Honest boundary (kept `MEASURED`, with rationale in both `FrameAssertions`):** the older
multi-line `text row` (different default sizes → different wrap/line-count) and every `back
row` cell (native `Button`/`UIButton` chrome) stay skipped — genuinely platform-variant, the
DoD #2 sanctioned fallback, not a forced green.

## Gate D — doc + close
`website/docs/architecture/parity.md`'s "Not pixel-identical rendering" note updated:
**typography is now identical (one bundled font, text geometry asserted); control chrome
remains native — a deliberate, separate boundary.** #126 closed.

## Proof surface at close
- `.NET` **780 / 0** (`ShellFrameTableDriftTests` green with the literal); Docusaurus builds.
- iOS **236 / 0** + Android **210 / 0** on the dispatch lanes — the font resolves as Inter and
  the parity-row height asserts equal across shells.
- Frozen bridge untouched; no ABI change. Counts reconciled (iOS 236, Android 210) in
  `ios.yml` / `android-instrumented.yml` / README.

## Ledger
Future (not this feature): a `fontWeight` prop (none exists today — Inter Regular only);
pixel-identical control chrome (custom-drawn controls — a separate opt-in); more Inter weights
when a weight prop lands.
