# Milestone 6 — Real-UI Foundation: Layout + Scroll + Image

**Status:** in progress — opened 2026-07-13
**Source:** the 2026-07-13 roadmap re-plan (capability before ecosystem) — the layout
engine the original P0–P7 list never had; the #1 gap between "proven POC" and "usable
framework" per the React-Native comparison.
**Predecessor:** Milestone 5 — complete 2026-07-13, tagged `v5.0` ([final audit](../plans/2026-07-13-milestone-5-final-audit.md): PASS, all 8 DoD criteria)

## Goal

M5 proved the architecture on two platforms. M6 makes it *build real screens*: a real
flexbox **layout engine** (there is only vertical stacking today), plus the two stubbed
leaf types — **scrolling** and **URL images** — filled in. After M6 a developer can lay
out a genuine multi-element screen (rows, columns, grow/shrink, wrap, alignment) that
scrolls and shows remote images, rendering identically on Android and iOS from one
runtime.

## The architecture decision (2026-07-13 brainstorm)

**Yoga (C++, Facebook's flexbox engine) linked into both shells** — the React Native
model. Android via JNI/JNA, iOS via Yoga's C-API (the same interop the shells already
use for the NativeAOT runtime). Rationale: flexbox must **measure leaf content** (text,
images) to lay out, and measurement is inherently platform-specific (font metrics), so
layout cannot live purely in the .NET core — Yoga's native measure callback is the
designed solution, and one C++ library serves both platforms (measurement "just works").

**Key consequence — no C-ABI change.** Flex properties ride the *existing* `SetStyle`
wire as new style keys; the layout computation is entirely shell-local (the shell builds
a Yoga node tree mirroring the view tree, applies flex props, measures leaves natively,
computes, and places views at computed frames). The renderer stays thin. The ABI stays
at **9 exports + the 72-byte 9-callback bridge** — this is a shell + component-params
change, not an ABI evolution. The shell's placement model changes from
"stack in a `LinearLayout`/`UIStackView`" to "Yoga computes frames, apply to a plain
container."

## Definition of Done

Initial M6 contract drafted at milestone-open. Subject to refinement during the
Phase 6.0 brainstorm — and explicitly subject to the Phase 6.0 Yoga-integration spike
verdict.

1. ✅ **Yoga-integration spike verdict committed.** — *closed by Phase 6.0 (2026-07-13,
   PR #54).* **GREEN on both rungs:** Yoga 3.2.1 links alongside the NativeAOT runtime on
   both shells, the native measure-callback round-trip works in both channels (measured
   width AND height reach the frame), and both shells compute *identical frames from an
   identical tree* (twelve numbers asserted per rung). The architecture holds — Yoga in the
   shells, no C-ABI change — and the fallback ladder (managed flexbox / native-layout
   mapping) is closed. Verdict + the pinned per-platform integration recipes:
   [spike conclusion](../plans/2026-07-13-phase-6.0-spike-conclusion.md).
2. ✅ **Flexbox layout on both platforms.** — *closed by Phase 6.1 (2026-07-13).* Flex props
   ride `SetStyle` (no ABI change); both shells run Yoga and place every child at a computed
   frame. `BnLayoutDemo` (row + column + grow + wrap + alignment) lays out **identically** on
   the AVD and the iOS simulator — the same frame table asserted number-for-number on both
   (`BnLayoutDemoAndroidTest` / `BnLayoutDemoTests`), derived from the .NET patch golden.
3. ✅ **Native measurement.** — *closed by Phase 6.1.* Text/button/input leaves are measured
   through Yoga's measure callback using real platform metrics (a long label wraps and its
   measured height drives its row). Attached **by NodeType**, never by childlessness. Pinned
   by an independent oracle on both platforms — a constant-size measure func passes every
   relational assertion and fails the oracle.
4. **Real scrolling.** The `scroll` NodeType (stubbed today) → `ScrollView` /
   `UIScrollView`; content taller than the viewport scrolls on both platforms, with Yoga
   laying out the scroll content.
5. **URL images.** The `image` NodeType (stubbed today) → async URL load into
   `ImageView` / `UIImageView` on both platforms, measured by Yoga (intrinsic/explicit
   size).
6. ✅ **Flex container components.** — *closed by Phase 6.1.* `BnView` gains the typed flex
   parameter surface (enums/numerics that stringify onto the wire); `BnRow`/`BnColumn` ship as
   thin presets; `BnLayoutDemo` at `/layout` is the cross-platform proof surface.
   **Deviation, consciously taken:** **no `BnStack`** — it would be a synonym for `BnColumn`,
   and two names for one thing is a library smell on day one.
7. **Every new surface is CI-asserted.** Test counts recorded/asserted at each phase
   close (the M4-onward discipline); the layout/scroll/image demos asserted on all
   surfaces (.NET frames, JVM, Android instrumented, iOS XCTest).
8. **Decision log committed.** Design + plan + conclusion per phase, plus the M6 final
   audit at close → tag `v6.0`.

## Out of scope for this milestone

- `.razor` compilation + the broader component library (virtualized list, modal, form
  controls) — Milestone 7
- nuget.org publication, CLI, docs site — Milestone 8
- Camera/geolocation/biometrics/notifications, real-device iOS — Milestone 9
- Accessibility, i18n, perf/security hardening — Milestone 10
- CSS/stylesheet parsing, animations, gestures beyond tap/change/focus — later (M7/M10)
- Grid layout (`display:grid`) — flexbox only this milestone

## Inherited from prior milestones

- **The `image`/`scroll` NodeTypes** — wired in the mapper switch since Phase 2.5 but
  unexercised (survey-noted); M6 makes them real (DoD #4/#5).
- **Stringly `FontSize`/`Padding`** (M4 ledger) — flex props join them as style keys;
  the typed-props cleanup stays M7 (component library) unless cheap here.
- **Open hardening issues #8/#9/#12/#13** — unchanged; the TranslateToViewIndex/
  RemoveComponent perf items (#12/#13) may be *touched* by the placement-model change
  (Yoga owns placement now) — reassess at the relevant phase, re-ledger if not closed.

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open:

- ✅ **Phase 6.0** — Yoga-integration spike (DoD #1) — *the named risk, verified first (both shells)* — **GREEN, complete 2026-07-13**
- **Phase 6.1** — Flexbox layout core: flex props + the shell Yoga pass + the flex demo (DoD #2, #3, #6) — *next*
- **Phase 6.2** — Real scrolling on both platforms (DoD #4)
- **Phase 6.3** — URL images on both platforms (DoD #5)
- **Phase 6.4** — M6 final audit + close (DoD #7, #8) → `v6.0`

Sequencing: the spike gates the whole approach; 6.1 is the engine (the big phase); 6.2/6.3
fill the two leaf types on top of the working engine; 6.4 audits and tags.

## Why this milestone exists

The architecture is proven, but you cannot build a real UI with vertical stacking and
three style props. A flexbox layout engine is the foundation every component and every
real screen sits on — it is the single thing most separating BlazorNative from a usable
framework. Doing it now, before the ecosystem/packaging work (M8), means what eventually
ships is a framework you can actually build with, not a toy that packages cleanly.
