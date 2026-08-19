# Milestone 13 Design — Consumer Ergonomics

**Date:** 2026-08-19
**Milestone:** 13
**Stage:** milestone (one milestone, multiple phases)

## Goal

M13 makes BlazorNative **pleasant to write apps in**. Today an app author cannot give a
`BnText` a margin, a width, or an `AlignSelf`, and cannot give a `BnButton` anything at all —
the typed layout surface is copy-pasted across **eight** components and **absent from four**.
The lengths those parameters accept are `string?`, so `Width="12px"` compiles, ships, and is
silently logged-and-ignored at runtime by both shells. This milestone declares the item surface
**once**, gives it to every component that can have it, and **types the lengths** so the
malformed cases become compile errors. It also spends the two remaining audit verdicts that do
not need a package built (#22's real deliverables) and retires the stale backlog entries that
keep re-generating the same verdicts.

It is deliberately scheduled **now** because the surface it changes is STABLE-tier and gets
**frozen at 1.0**, and 1.0 blocks on a single administrative item (P3, a real iPhone). This is
very likely the last cheap window to fix a 23-parameter copy-paste and a
`Padding` is `float?` / `Margin` is `string?` inconsistency.

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing (.NET, JVM, and both device lanes dispatched — a green *required* set
      does not mean the advisory lanes ran)
- [ ] **The item surface is declared exactly once.** `BnLayoutItem` holds the 17 item
      parameters; no component re-declares any of them. Pinned by a test that fails if a
      component in the package declares a parameter whose name is already on the base.
- [ ] **Every component derives from `BnLayoutItem`**, except those on an explicit allowlist
      whose entries each carry a written reason. Pinned — without this, component #13 is written
      against `ComponentBase` and the hole silently reopens. (Which components end up on the
      allowlist is decided in 13.0 — see Open Questions; the DoD requires the pin and the
      reasons, not a particular membership.)
- [ ] **Every layout length is typed.** `Width="12px"` is a **compile error**, not a runtime
      log line. `Padding` and `Margin` share one type; the `float?` / `string?` split is gone.
- [ ] **Frame tables are byte-identical** before and after, on **both** shells, across the
      whole sample. This is the refactor's correctness claim and its acceptance test.
- [ ] **The wire is unchanged** — no ABI change, no shell change, no `wire-vocabulary.json`
      change. Verified by diffing the emitted attribute stream, not asserted.
- [ ] **Baselines re-shipped with a written migration note** for consumers on 0.10.0, published
      as part of the release notes.
- [ ] **`CheckAccess()` resolved on measured evidence** — either an honest guard or a
      Debug-only warning, with the measurement recorded (which suites red, and why).
- [ ] **#21 retitled, #22 closed** with the audit as written rationale;
      `BlazorNative.Styling` and `BlazorNative.State` deleted from `BACKLOG.md`; the WASM-era
      APNs / App-Store rows re-worded.
- [ ] **Theme decision recorded** — the design exists and a go/no-go is written down, whichever
      way it goes.

> No "release tagged in git" criterion: `docs/planning/CONVENTIONS.md` records
> **`Milestone completion tags a release: no`** — release-please owns the `v<semver>` namespace
> and Phase 8.6 retired milestone tags. A checkbox nothing will ever tick is a permanent false
> gap.

## Phases

1. **Phase 13.0: Extract the item surface** — `Surface: Refactor`
   - **Goal:** Declare the 17 item parameters once on `BnLayoutItem` (and the 6 container
     parameters on `BnLayoutContainer`), collapse the eight copies, and give the four bare
     components the surface they never had.
2. **Phase 13.1: Type the lengths** — `Surface: Refactor`
   - **Goal:** Introduce `BnLength` / `BnAutoLength` and convert every layout length, making
     malformed values a compile error while leaving the wire grammar untouched.
3. **Phase 13.2: The dispatcher's honest answer** — `Surface: Backend`
   - **Goal:** Measure what breaks when `InlineDispatcher.CheckAccess()` stops returning an
     unconditional `true`, then ship the guard the evidence supports.
4. **Phase 13.3: State docs + backlog retirement** — `Surface: Docs`
   - **Goal:** Write the "State in BlazorNative" page that is #22's real deliverable, and
     retire the stale issues and backlog entries the 2026-08-17 audit already adjudicated.
5. **Phase 13.4: Theme system design** — `Surface: Docs`
   - **Goal:** Produce a written theme design and a go/no-go decision — not an implementation.
6. **Phase 13.5: Audit and close** — `Surface: Docs`
   - **Goal:** Run `audit-milestone` against the DoD on live evidence and close M13.

## Dependencies on Prior Milestones

- **Depends on M11 Phase 11.3 (API stability).** The `PublicAPI` baselines and RS0016/17/37 as
  **errors** are what make this refactor reviewable — the baseline diff *is* the API review.
  Without them the same change would be unauditable.
- **Depends on the M6/M7 frame-parity contract.** `BnYogaLayout.mm` asserting identical frame
  tables on both shells is this milestone's acceptance instrument; the refactor is correct
  exactly when those tables do not move.
- **Depends on #262 (wire vocabulary codegen).** The style names now live once in
  `src/wire-vocabulary.json`, which is why typing the .NET side cannot silently desynchronise
  from the shells.
- **Depends on PR #273** (the M12 roadmap refresh) being merged before `new-milestone` writes
  the M13 block into `ROADMAP.md` — otherwise the milestone numbering has a hole and the two
  edits conflict.

## External Constraints

- **P3 (a real iPhone) may clear at any time** — an external iOS developer is expected. If it
  does, **1.0 becomes reachable**, and criterion **S3** (deliberately out of scope here) becomes
  the remaining gap. M13 must not be structured so that it blocks a 1.0 cut; every phase is
  independently shippable.
- **The API window closes at 1.0.** Every breaking item in this milestone is cheap now and
  costs a major later. That is the whole reason for the ordering.
- **No Apple Developer account** for device verification; simulator-scoped as since M5.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| **Blazor sequence-number collision.** The base emits attributes the derived component also numbers. A collision does not throw — it produces a **wrong diff, silently**. | Severe: corrupted render tree, no diagnostic, the worst failure mode in this codebase. | Reserved bands (item 1–49, container 50–99, component-specific 100+, `ChildContent` 200) plus a test asserting no two attributes in any component share a sequence number, **mutation-proven by deliberately colliding one**. |
| **Frame tables move.** The refactor is supposed to be behaviour-neutral; a reordered or dropped attribute changes layout. | Severe and possibly silent on one platform only. | Frame tables are the acceptance test, run on **both** shells across the whole sample. Any movement fails the phase. |
| **Making `CheckAccess()` honest breaks working code.** Pool threads demonstrably touch this path (#213 item 2 parked an async fault off a pool thread; #9 is dispatch-lane starvation). | Converts currently-working async paths into exceptions. | Phase 13.2 is a **spike first**: change it, run all .NET tests plus the device lanes, and decide on the measurement. Debug-only warning is the documented fallback. |
| **Baseline churn hides a real removal.** ~17 × 8 members move declaring type; a genuine accidental deletion could ride along unnoticed. | An API disappears without anyone deciding to remove it. | Review the baseline diff as an API review in its own right, and pin the member *names* (not declaring types) so a name vanishing reds regardless of where it lives. |
| **Theme design grows into an implementation mid-phase.** It needs a wire event and two shell observers to be real. | Milestone slips on the least-certain item. | 13.4's deliverable is explicitly **a design and a decision**. Building it is a separate go, and "not now" is an acceptable outcome. |
| **Binary break for existing consumers.** Moving members to a base is source-compatible but not binary-compatible. | Anyone compiled against 0.10.0 must recompile. | Accepted deliberately (owner decision, 2026-08-19) because pre-1.0 is the cheap window. Discharged by a written migration note, not by avoidance. |

## Open Questions

- **Does `BnScroll` belong under `BnLayoutContainer` or `BnLayoutItem`?** It has `ChildContent`
  but none of the container flex parameters (`Padding`/`Justify`/`Align`/`Wrap`/`Gap`), so it is
  an item that happens to have children. Current design places it under `BnLayoutItem`; confirm
  during 13.0 against how the shells treat a scroll's child.
- **Should `BnList<TItem>` and `BnModal` gain the item surface too, or stay allowlisted
  exceptions?** Both are structural rather than layout-participating. Decide in 13.0 with the
  allowlist test as the forcing function.
- **Does typing the lengths warrant an analyzer for the negative-legality class** (negatives are
  legal on `margin` and the insets, illegal elsewhere), or does the shell's existing
  log-and-ignore remain the right home? Current design says leave it in the shell; revisit only
  if 13.1 finds it cheap.
