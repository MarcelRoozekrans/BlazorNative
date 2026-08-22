# Milestone 13: Consumer Ergonomics

**Status:** active
**Started:** 2026-08-19

**Design:** [`docs/superpowers/specs/2026-08-19-milestone-13-design.md`](../superpowers/specs/2026-08-19-milestone-13-design.md)
**Predecessor:** Milestone 12 — Post-M11 Maintenance & Consumer Polish (complete; **retro-fitted,
never audited** — see the note on its ROADMAP block, and do not cite it as audit-backed).
**Source:** owner direction (2026-08-19): continue on the #21/#22/#24/#25 band while P3 waits on
an external iOS developer. Scope set by the **2026-08-17 audit of issues #16–#25**, which found
most of that band obsolete or already delivered and named the one real gap underneath it.

## Goal

M13 makes BlazorNative **pleasant to write apps in**. Today an app author cannot give a `BnText`
a margin, a width, or an `AlignSelf`, and cannot give a `BnButton` anything at all — the typed
layout surface is copy-pasted across **eight** components and **absent from four**. The lengths
those parameters accept are `string?`, so `Width="12px"` compiles, ships, and is silently
logged-and-ignored at runtime by both shells. This milestone declares the item surface **once**,
gives it to every component that can have it, and **types the lengths** so the malformed cases
become compile errors. It also spends the two remaining audit verdicts that do not need a package
built (#22's real deliverables) and retires the stale backlog entries that keep re-generating the
same verdicts.

## Scoping decisions (owner, 2026-08-19)

1. **Break now, while pre-1.0.** The shared-base extraction and the typed lengths land in one
   pass, with the `PublicAPI` baselines rewritten and a written migration note. This surface
   **freezes at 1.0** and 1.0 blocks on a single administrative item, so this is very likely the
   last cheap window.

   ⚠ **CORRECTED 2026-08-20, after measurement.** This decision was originally recorded as
   accepting a **binary break** — *"moving members to a base is source-compatible but
   binary-breaking; anyone compiled against 0.10.0 must recompile."* **That was wrong, and it
   was asserted without being measured.** Phase 13.0 measured it: a probe was compiled against
   the pre-refactor assembly, its member references verified in IL, and the *same binary* run
   against both the old and new assemblies — **both exited 0 with identical, correct values.**
   A `MemberRef` whose parent is a `TypeRef` is resolved by walking the base chain
   (ECMA-335 II.22.25), and moving a member **up** its own hierarchy is explicitly a
   *non-breaking* change in dotnet/runtime's own compatibility rules — moving it **down** is
   the breaking direction. **Callers are unaffected and need no recompile.**

   What genuinely changes is **reflection about declarations**: `DeclaredOnly` queries see the
   members on the base instead of the derived type (measured on `BnView`: 24 → 2). This repo
   observed the same effect internally — its own doc-comment coverage floor moved 196 → 74.

   **The decision itself stands and is stronger for the correction:** the benefit is unchanged
   and the cost was overstated. The one real consumer-facing effect is narrow — an XML
   `<see cref>` or `<inheritdoc cref>` naming the *old* declaring type raises **CS1574, a
   warning**, and only in consumer projects that generate documentation; crefs do not resolve
   inherited members. See [the phase conclusion](../plans/2026-08-19-phase-13.0-conclusion.md)
   for the measurement and its bounds.
2. **No new packages.** Neither `BlazorNative.Styling` (#21) nor `BlazorNative.State` (#22) is
   built — both premises are obsolete per the audit, and both collide with the four-times-recorded
   "no 8th package" decision enforced by `PackagePurityTests`.
3. **The theme system is design-first**, with an explicit go/no-go. Its deliverable is a design
   and a decision; building it is a separate go and "not now" is an acceptable outcome.
4. **#25 → 1.0 criterion S3 is OUT**, by owner choice. Recorded because it is the only item that
   moves the 1.0 scoreboard: if P3 clears while M13 runs, S3 becomes the remaining gap.
5. **Every phase is independently shippable** and **none of M13 blocks a 1.0 cut** — deliberate,
   because the external iOS developer may clear P3 at any time.

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing — .NET, JVM, **and both device lanes dispatched** (a green *required* set
      does not mean the advisory Android/iOS lanes ran; that is how 11.4's pump bug hid)
- [ ] **The item surface is declared exactly once.** `BnLayoutItem` holds the 17 item parameters;
      no component re-declares any of them. Pinned by a test that reds if a component declares a
      parameter name already on the base.
- [ ] **Every component derives from `BnLayoutItem`**, except those on an explicit allowlist whose
      entries each carry a written reason. Pinned — without this, component #13 is written against
      `ComponentBase` and the hole silently reopens. (Membership is decided in 13.0; the DoD
      requires the pin and the reasons, not a particular list.)
- [ ] **Every layout length is typed.** `Width="12px"` is a **compile error**, not a runtime log
      line. `Padding` and `Margin` share one type; the `float?` / `string?` split is gone.
- [ ] **Frame tables are byte-identical** before and after, on **both** shells, across the whole
      sample. This is the refactor's correctness claim and its acceptance test.
- [ ] **The wire is unchanged** — no ABI change, no shell change, no `wire-vocabulary.json`
      change. Verified by diffing the emitted attribute stream, not asserted.
- [ ] **Baselines re-shipped with a written migration note** for consumers on 0.10.0, carried in
      the release notes.
- [ ] **`CheckAccess()` resolved on measured evidence** — an honest guard or a Debug-only warning,
      with the measurement recorded (which suites red, and why).
- [ ] **#21 retitled, #22 closed** with the audit as written rationale; `BlazorNative.Styling` and
      `BlazorNative.State` deleted from `BACKLOG.md`; the WASM-era APNs / App-Store rows re-worded.
- [ ] **Theme decision recorded** — the design exists and a go/no-go is written down, either way.
- [ ] **The twin-divergence class is closed MECHANICALLY, not instance by instance** (added
      2026-08-21 with phase 13.4). #278/#282 (deep-link parsers), #279 (iOS `measuredNodeTypes`
      escaping the #262 codegen) and #280 (the harness bypassing the C-ABI encode) are one defect
      in four places: two copies of one truth, one unpinned. The criterion is that a NEW instance
      of the shape reds — a generated twin, or a pin that compares the two copies — not that these
      four are patched. Six patches leave the seventh free to appear.
- [ ] **The test harness cannot pass what the device rejects** (#280) and does not leak on its
      own `StrictErrors` path (#281). A testing product that passes when the device fails converts
      "untested" into "tested and fine", which is worse than shipping no harness.

> **No "release tagged in git" criterion.** `docs/planning/CONVENTIONS.md` records **`Milestone
> completion tags a release: no`** — release-please owns the `v<semver>` namespace and Phase 8.6
> retired milestone tags. A checkbox nothing will ever tick is a permanent false gap.

## Phases

1. Phase 13.0 — extract the item surface [complete] — closed 2026-08-20 on
   [PR #287](https://github.com/MarcelRoozekrans/BlazorNative/pull/287); both device lanes
   dispatched and green
2. Phase 13.1 — type the lengths [complete] — closed 2026-08-21 on
   [PR #289](https://github.com/MarcelRoozekrans/BlazorNative/pull/289); both device lanes
   dispatched and green; .NET 1032 → 1051
3. Phase 13.2 — the dispatcher's honest answer [complete] — closed 2026-08-21; the premise was
   overturned by measurement (an honest CheckAccess() stack-overflows the process), so the phase
   shipped detection rather than an assertion
4. Phase 13.3 — state docs + backlog retirement (+ #286 Firebase REST docs) [complete] — closed
   2026-08-21; #22 closed with the audit as rationale, #21 retitled, THREE self-contradicting
   BACKLOG sections retired (Styling, State, Navigation), a new Guides category on the site
5. Phase 13.4 — parity and harness fidelity [complete] — closed 2026-08-21 on
   [PR #299](https://github.com/MarcelRoozekrans/BlazorNative/pull/299); nine mutation-proven
   guards, both device lanes green; #280's stated bug proved unreachable and re-scoped;
   .NET 1054 → 1070
6. Phase 13.5 — theme system design [complete] — was 13.4 — verdict SPLIT: go on detection +
   `BnColor`, **no-go before 1.0** on a prescribed colour-role vocabulary, remove `BnTheme`.
   Found that `themeChanged` needs no ABI *or* .NET change, and that host-event names are an
   unpinned three-language vocabulary — a live instance of 13.4's class, now a prerequisite
7. Phase 13.6 — audit and close [pending] — was 13.5

## Risk areas

| Risk | Impact | Mitigation |
|---|---|---|
| **Blazor sequence-number collision** — the base emits attributes the derived component also numbers | Severe: a collision does not throw, it produces a **wrong diff, silently** | Reserved bands (item 1–49, container 50–99, component-specific 100+, `ChildContent` 200) plus a pin **mutation-proven by deliberately colliding one** |
| **Frame tables move** | Severe, and possibly silent on one platform only | Frame tables are the acceptance test, run on **both** shells across the whole sample |
| **Honest `CheckAccess()` breaks working code** — pool threads demonstrably touch this path (#213 item 2, #9) | Converts working async paths into exceptions | 13.2 is a **spike first**: change, measure across all suites, decide. Debug-only warning is the documented fallback |
| **Baseline churn hides a real removal** — ~17 × 8 members change declaring type | An API disappears without anyone deciding to | Read the baseline diff as an API review; pin member **names** so a vanishing name reds regardless of declaring type |
| **Theme design grows into an implementation** | Milestone slips on its least-certain item | **13.5's** (was 13.4's) deliverable is explicitly a design + decision |
| **13.4 is worked as six issues rather than one bug class** (added 2026-08-21) | Six patches land, the seventh instance of the same shape ships later, and the milestone's DoD reads as met | The DoD criterion is a **mechanical** guard — a generated twin or a pin comparing the two copies — not four fixed instances. The precedent is #262, which retired four of five vocabularies and left the fifth (#279) to prove the point |

## Out of scope for this milestone

- **#24 `BlazorNative.Cli`** — the audit's verdict is "keep open, rescope, but it should follow,
  not lead"; two of its four deliverables are blocked on other work.
- **#25 → 1.0 criterion S3** — owner choice (decision 4 above). **The only item that moves the 1.0
  scoreboard**; becomes the remaining gap if P3 clears.
- **`fontWeight` / Inter Bold** — re-filed out of #21 deliberately: synthetic bold changes text
  metrics and would risk the font-parity contract to add a paint property.
- **Responsive breakpoints** — blocked. Nothing surfaces viewport size to .NET; it is an ABI/wire
  item, not a styling one.
- **P3 (real-iPhone verification)** — gated on an Apple Developer account and an external iOS
  developer. Administrative, not technical; `docs/ios-device-verification-handover.md` is the
  handover.

## Audit History

| Date | Verdict | Gaps |
|---|---|---|
| — | not yet audited | — |
