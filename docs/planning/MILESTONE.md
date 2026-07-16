# Milestone 7 — Components + Razor

**Status:** in progress — opened 2026-07-15
**Source:** the 2026-07-13 roadmap re-plan (capability before ecosystem) — the second capability
milestone: M6 built the layout engine; M7 builds *the things you build UIs with* on top of it.
**Predecessor:** Milestone 6 — complete 2026-07-15, tagged `v6.0` ([final audit](../plans/2026-07-14-milestone-6-final-audit.md): all 8 DoD PASS)

## Goal

After M6 a developer can lay out a real screen; after M7 they can **author it like a Blazor
developer** (`.razor` files, not hand-written `BuildRenderTree`) and **build the two screens every
real app is made of**: a performant scrolling list and a form — plus the first overlay (modal) and
a component surface deliberately informed by React Native's core set. The proof is the demo app
itself, rebuilt in `.razor`, plus a 500-row virtualized list page asserted on both shells.

**Scope honesty:** this is the fattest milestone yet (a compiler-toolchain risk AND the biggest
component AND a wire-throughput design). The Phase 7.0 spike verdict and the 7.2 wire design are
the two points where scope may consciously shrink; anything cut is ledgered, not dropped.

## The two named risks (spike-first, the 5.0/6.0 discipline)

1. **`.razor` compilation (Phase 7.0).** The Razor SDK's compile targets assume a web host, and
   the generated code must render through `NativeRenderer` under NativeAOT, trim-clean (the 4
   accepted IL2072s must not grow). GREEN unlocks the authoring story; RED re-scopes M7 to
   hand-written components with better helpers, with the fallback documented. **Either verdict
   passes DoD #1 — what it demands is committed evidence.**
2. **`onScroll` — the first 60Hz producer on a wire designed for taps (Phase 7.2).** The
   virtualized list needs scroll position in .NET. The wire design (coalescing/throttling — e.g.
   at most one scroll event per frame commit, shell-side conflation) gets its own design section
   and its own throughput evidence before `BnList` is built on it.

## Definition of Done

1. **Razor-compilation spike verdict committed** — can a `.razor` file compile into a component
   that renders through `NativeRenderer` under NativeAOT, no web dependencies, trim-clean?
   Evidence either way; RED comes with the documented fallback and a re-scope.
2. **The demo app is authored in `.razor`.** The five existing pages (`BnDemo`, `BnSettingsPage`,
   `BnLayoutDemo`, `BnScrollDemo`, `BnImageDemo`) rewritten as `.razor` files with **parity proven
   against the existing goldens and frame tables** (the old `BuildRenderTree` versions are the
   regression baseline until parity, then retire). `@bind-Value` as *syntax*, not just mechanics.
   ✅ **Closed by Phase 7.1** ([conclusion](../plans/2026-07-15-phase-7.1-conclusion.md)): zero
   golden edits, zero shell edits; device lanes re-ran green on the converted app —
   android-instrumented run 29420994993 (111/0), ios run 29420996916 (72/0). Two pre-1.0 breaking
   changes recorded, no shim: `BnThemedPanel` `internal`→`public` (compiler-forced),
   `FontSize`/`Padding` `string?`→`float?` (wire-identical).
3. **A virtualized list (`BnList`).** Windowed rendering over the M6 scroll engine: a 500-row page
   where only ~viewport+overscan rows are live (asserted — a row count, not a feeling), scrolling
   stays interactive, and the frames match on both shells. Includes the **`onScroll` wire design**
   (coalesced; throughput evidence committed) — scroll offset reaches .NET without flooding the
   dispatch lane.
   ✅ **Closed by Phase 7.2** ([conclusion](../plans/2026-07-15-phase-7.2-conclusion.md)):
   shell-side conflation on the existing wire (no ABI change), throughput evidence on both
   platforms — Android burst 100 samples → 2 dispatches (50:1), fling 78 → 35 (2.23:1), final
   offset always delivered; iOS mirrors on the same `/list` numbers. Counted liveness (11/15/11
   of 500 rows) asserted on both shells — Android instrumented **124/0**, iOS XCTest **85/0**
   (run 29435524820); conflation mutation-proven at the mechanism on both (iOS mutations run ON
   CI). The `@key` answer is empirical: a window slide is insert/remove ONLY — the loud
   permutation arm stays, no wire move-concept needed.
4. **Form controls:** `BnCheckbox`, `BnSwitch`, `BnSlider`, and **`picker` made real** (the last
   stubbed widget — native `Spinner`/`UIPickerView` with items + selection round-trip on the
   existing change wire). Two-way bind on all four.
   ✅ **Closed by Phase 7.3** ([conclusion](../plans/2026-07-15-phase-7.3-conclusion.md)):
   three new NodeTypes (8/9/10 — vocabulary, not ABI; pinned on all three mirrors), the picker
   as the **state-owner precedent** (strict flat-JSON items, normative clamp rule, the CLAMPED
   index dispatched — asserted on both device lanes), per-control loop guards verified and
   **asymmetric by platform**, two iOS 26 platform findings fixed on CI. Counts .NET **492/0** ·
   JVM **90/0** · Android instrumented **147/0** · iOS XCTest **111/0** (run 29451417339).
5. **`BnModal`** — the first overlay surface (show/hide + `ChildContent`; native dialog primitives
   or a second Yoga root — the design decides and records why). No animation system.
   ✅ **Closed by Phase 7.4** ([conclusion](../plans/2026-07-16-phase-7.4-conclusion.md)):
   an **overlay in the existing root** (NodeType `modal = 11` — vocabulary, not ABI; native
   dialog rejected: a second Yoga root/frame surface + a self-dismissing window violating the
   state-owner law). The anchor+overlay model (the **third index-mapping rule**: 0-sized
   absolute anchor at the wire slot; full-root overlay attached LAST; one resolved index feeds
   both trees), dismissal as a **REQUEST** on the `click` wire (scrim tap / dismiss button /
   Android back — the shell never self-closes; Android back consults the modal stack before
   navigation-back), the two-subtree fixpoint purge exactly-once by ownership-disjointness.
   Counts .NET **518/0** · JVM **92/0** · Android instrumented **166/0** · iOS XCTest **132/0**
   (run 29487073302).
6. **`BnImage` polish:** `Placeholder`, `OnError`, `ContentMode` — each is a **measurement**
   design (what does a placeholder measure as? does a failure keep reserved space?), specified in
   the parity contract and asserted on both shells like everything else.
   ✅ **Closed by Phase 7.5** ([conclusion](../plans/2026-07-16-phase-7.5-conclusion.md)):
   **zero new measurement states** — the 6.3 contract survives verbatim, re-proven with the
   features present on the ninth page (`/imagepolish`). `PlaceholderColor` (paint-only, the
   4-row state table normative on both shells; iOS's bounds-tracking subview a recorded
   improvement over the design's Kingfisher suggestion), `OnError` (`error` on the existing
   wire, payload = the src string verbatim, at-most-once per (src, generation),
   attach-iff-HasDelegate; failure never changes measurement), `ContentMode`
   (contain/cover/stretch/center, paint-only — four identical frames under four modes;
   default Contain diverging from RN's cover, recorded; `clipsToBounds` pinned on iOS). No
   ABI change, no new NodeTypes. The headline finding: the defer mechanism's two
   review-caught construction errors (the decision-time DEFER capture, latent on Android and
   live on iOS; the dispatch table's wrong row order dropping the mount-time sync failure) —
   both fixed on both shells, adversarial orderings pinned. Counts .NET **537/0** · JVM
   **106/0** · Android instrumented **182/0** · iOS XCTest **153/0** (run 29510920883).
7. **The React Native parity survey.** RN's core-component set mapped to BlazorNative:
   *have it / ships in M7 / ledgered with a reason*. Cheap high-value wins identified by the
   survey (candidates: `ActivityIndicator`, `SafeAreaView`) ship; heavy ones
   (`RefreshControl`, `KeyboardAvoidingView`, `StatusBar`, gestures) are ledgered explicitly.
   ✅ **Closed by Phase 7.4** ([conclusion](../plans/2026-07-16-phase-7.4-conclusion.md)):
   the survey table committed in the [design](../plans/2026-07-16-phase-7.4-design.md) —
   11/19 have-it; `BnModal` + `BnActivityIndicator` (NodeType 12, measured leaf,
   `ProgressBar`/`UIActivityIndicatorView`, intrinsics by oracle) shipped this phase; Image
   polish → 7.5; `SafeAreaView` ledgered with the named edge-to-edge problem (M9-adjacent);
   SectionList / Pressable-state / StatusBar / RefreshControl / KeyboardAvoidingView / Alert
   ledgered with reasons, not dropped.
8. **Hygiene + close:** typed `FontSize`/`Padding` (the last stringly M4-ledger stragglers —
   ✅ closed by Phase 7.1: `string?`→`float?`, wire-identical, goldens untouched); the
   **route-registry unification** (routes duplicated in .NET + Kotlin since 5.1 — one source,
   drift-tested like the style tables); every new surface CI-asserted (counts + the three required
   compile gates); decision log per phase + final audit → tag **`v7.0`**.

## Out of scope for this milestone

- nuget.org publication, CLI, docs site — **Milestone 8**
- Camera/geolocation/biometrics/notifications, real-device iOS — **Milestone 9**
- Accessibility, i18n, perf/security hardening — **Milestone 10**
- Animations, gesture system beyond tap/change/focus/scroll, navigation transitions — M8+
- CSS/stylesheet parsing; `display: grid` — still out
- Density-aware image assets (`@2x`/`srcset`) — ledgered from M6, revisit with `ContentMode`

## Inherited from prior milestones (the ledger M7 consumes)

- **From 6.2/6.3:** `onScroll`/`scrollTo` + offset restore (DoD #3's wire design);
  `Placeholder`/`OnError`/`ContentMode` (DoD #6); `picker` un-flexed and stubbed (DoD #4);
  horizontal scroll (revisit with `BnList` — a `Horizontal` list variant may force it, else ledger).
- **From 5.1:** the route→component table duplicated in .NET + Kotlin (DoD #8).
- **From M4:** stringly `FontSize`/`Padding` (DoD #8); open hardening issues #8/#9/#12/#13 —
  the dispatch-lane items (#9) get **re-examined by the onScroll design**, which is the first
  real load on that lane.
- **CI posture:** the three required compile gates + advisory device lanes stay as-is; the iOS
  lanes' ~3min duplicated publish is a ledgered annoyance, not M7 work.

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open; **subject to the 7.0 verdict**:

- **Phase 7.0** — the Razor-compilation spike (DoD #1) — *the named risk, verified first*
- **Phase 7.1** — `.razor` authoring end-to-end: the five pages rewritten + parity + typed-props
  cleanup riding along (DoD #2, part of #8)
- **Phase 7.2** — the `onScroll` wire design + `BnList` (DoD #3)
- **Phase 7.3** — form controls + a real `picker` (DoD #4)
- **Phase 7.4** — `BnModal` + the RN survey's cheap wins (DoD #5, #7)
- **Phase 7.5** — `BnImage` polish: Placeholder/OnError/ContentMode (DoD #6)
- **Phase 7.6** — route-registry unification + M7 final audit + close (rest of #8) → `v7.0`

Sequencing: the spike gates the authoring story; 7.1 converts the app (everything after is
authored in `.razor` from day one); 7.2 is the big engine phase; 7.3–7.5 are component phases on
a working list; 7.6 audits and tags. **If 7.0 goes RED, 7.1 becomes the fallback-authoring phase
and the rest of the plan stands.**

## Why this milestone exists

M6 proved the same real screen lays out identically on both platforms — but authoring it still
means hand-writing `BuildRenderTree`, and the library still lacks the two screens every real app
opens with: a fast list and a form. M7 closes the authoring gap (the standing M3-era ledger item)
and builds the components that make the M6 engine worth having — before M8 packages any of it.
