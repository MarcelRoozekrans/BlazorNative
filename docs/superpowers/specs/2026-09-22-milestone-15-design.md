# Milestone 15 Design — `A Standard for Pins`

**Date:** 2026-09-22
**Milestone:** `15`
**Stage:** milestone (one milestone, multiple phases)

**Predecessor:** [Milestone 14 — Twin Divergence, Closed Mechanically](../../planning/ROADMAP.md)
(complete 2026-09-22, verdict **PASS WITH FINDINGS**,
[audit](../../plans/2026-09-22-milestone-14-audit.md)).

## Goal

This repo defends its invariants with **drift pins** — tests that read source or config and assert
two copies of one truth agree. There are **sixteen** of them and **four** manifests, accumulated
across many milestones, each written to catch the bug in front of it. They have no shared standard,
and it shows: M14's newest pin was defeated by **five successive reviews**, each with a shape the
previous round had not tried, and every fix was local to the instance. M15 establishes what a pin
must do to be trusted, applies that standard to every existing pin, and closes the backlog of
missing and broken ones as consequences rather than as nine separate errands.

The user-visible outcome is narrow but real: **a green CI run means more afterwards than it does
today.**

## The thesis, stated precisely

**A pin that can pass while checking nothing is not a pin.** Today we cannot say which of ours can.

Vacuity is more general than "the scan stopped matching". Any assertion of the form *for every X,
assert Y* passes trivially when there are no X — whether X came from a regex over source, a
directory walk, or a collection comparison. `RouteMenuDriftTests` performs **no file scanning at
all**, and `EveryRoutedPage_ExceptTheTwoExemptions_HasAMenuRow` still passes over an empty page
list.

A first measurement — heuristic, and Phase 15.0's job to make exact — suggests **6 of 16 pins carry
no anti-vacuity assertion**. Some may not need one; that has to be established per pin rather than
assumed either way.

**#357 is the thesis in miniature, and it is embarrassing in a useful way:** 14.1's completeness
pin lacks an assertion that 14.3's structurally identical twin has. **The guards built to catch
twin divergence have drifted from each other.**

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing — .NET, JVM, **and both device lanes dispatched**, with each lane's
      `headSha` compared against the PR head rather than its conclusion read alone
- [ ] **A written pin standard exists**, in the repo rather than in a milestone doc, stating what
      every drift pin must do — at minimum: it must fail when it scans nothing, it must fail when
      its subject moves, and it must state what it does **not** cover.
- [ ] **Every one of the sixteen existing pins is assessed against that standard**, and the
      assessment is recorded per pin — *conforms*, *fixed*, or *exempt with a written reason*.
      An unassessed pin is a gap; "exempt" is an acceptable outcome, silence is not.
- [ ] **The standard is enforced mechanically, not by review.** A new pin that can pass while
      checking nothing must red. If no mechanical form exists, that is a finding to record
      explicitly with its evidence — never a line to quietly drop. *(Wording deliberately inherited
      from M14's central criterion, which this milestone exists because of.)*
- [ ] **#364 is answered, not merely fixed.** The auth-semantics pin's *coverage* is written down —
      which trees, which spellings, which file kinds — and the four known holes are closed as
      consequences of that definition rather than as four patches.
- [ ] **#296 is closed**, and it is closed by the standard rather than around it: it is the first
      live divergence to meet the new mechanism, and it must red before it is fixed.
- [ ] **#357, #297 and #302 are closed** — respectively an asymmetry between twins, a missing pin,
      and a missing guard.
- [ ] **#291 is answered for prose.** M14 found **three** unpinned documentation transcription
      pairs. Either they are pinned, or the milestone records explicitly that prose pinning was
      attempted and found not worth its cost — with the reasoning, not the conclusion alone.
- [ ] **The small corrections land:** #298, #356, #365.
- [ ] **No new public API, wire or ABI change** — verified by diffing, not asserted.

## Phases

1. **Phase 15.0: The pin standard** — `Surface: Docs`
   - **Goal:** Write down what a pin must do to be trusted, and take an exact census of the sixteen
     existing pins against it — replacing this design's heuristic 6-of-16 with a measured per-pin
     verdict.

2. **Phase 15.1: Enforce the standard** — `Surface: Backend`
   - **Goal:** Make the standard mechanical — a new pin that can pass while checking nothing reds —
     and bring every non-conforming pin up to it, including **#357**'s asymmetry.

3. **Phase 15.2: Define the auth pin's coverage** — `Surface: Backend`
   - **Goal:** Answer **#364** by stating what the auth-semantics scan must cover, then closing its
     four reproduced holes as consequences of that statement.

4. **Phase 15.3: The first live test — deep-link scheme** — `Surface: Backend`
   - **Goal:** Close **#296** using the mechanism rather than around it: establish which behaviour
     is correct, align the outlier, and pin it so the third copy in `AndroidManifest.xml` and its
     template mirror cannot drift either.

5. **Phase 15.4: The missing guards** — `Surface: Mixed`
   - **Goal:** Close **#297** and **#302** — a pin that does not exist, and a release-notes guard
     that does not exist — each written to the 15.0 standard.

6. **Phase 15.5: Prose, and the small corrections** — `Surface: Docs`
   - **Goal:** Answer **#291** for the three transcription pairs, and land **#298**, **#356**,
     **#365**.

7. **Phase 15.6: Audit and close** — `Surface: Docs`
   - **Goal:** Run `audit-milestone` against the DoD on live evidence and close M15.

**Ordering rationale.** 15.0 before everything, because the standard is what the rest applies —
writing it after fixing pins would make it a description of what we happened to do. 15.2 and 15.3
are deliberately adjacent: 15.2 defines coverage for the pin that has been defeated five times, and
15.3 is the **first live divergence to meet it**, which is the cheapest honest test of whether the
definition was any good. 15.4 and 15.5 are independent of each other and of 15.3.

## Dependencies on Prior Milestones

- **Depends on M14** for the mechanism this milestone standardises: `src/auth-semantics.json` +
  `AuthSemanticsDriftTests`, `src/dispatch-surface.json` + `DispatchSurfaceDriftTests`, and the
  wire-vocabulary codegen from #262 and 14.0.
- **Depends on M14's audit** for the problem statement: #364's argument that the thing to reason
  about is coverage rather than assertions is this milestone's premise.
- **External: none.** Unlike M14, no phase here needs a device, an Apple account, or an external
  contributor. That is deliberate — M14's only externally-blocked item was its device lane, and
  this milestone was scoped to avoid inheriting that dependency.

## External Constraints

- **None that gate phases.** #17's Apple-account cluster and real-device execution remain open but
  are outside this milestone entirely.
- **1.0 is not this milestone's DoD**, carried forward from M14's scoping decision 1. Whether the
  pin standard is a 1.0 requirement is a separate owner call.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| **The standard is written to describe what we already do**, so nothing changes and every pin "conforms" | The milestone becomes documentation of the status quo, and the sixth review finds a seventh shape | 15.0's census is **measured per pin, not asserted**, and must replace this design's heuristic number with a real one. If the census finds every pin already conforms, that is a finding worth challenging — the evidence says otherwise |
| **No mechanical enforcement exists** for "a pin must not be able to pass while checking nothing" | The standard degrades into a review checklist, which is what M14 already had | The DoD says to record that explicitly with evidence rather than drop it. A partial mechanism — e.g. enforcing only the anti-vacuity half — is an acceptable honest outcome |
| **Coverage is defined too narrowly in 15.2**, and 15.3 exposes it immediately | Rework, and the definition loses credibility | That is the *point* of the 15.2 → 15.3 adjacency. An early failure there is cheap and informative; discovering it in 15.6's audit is not |
| **Prose pinning proves not worth its cost** | #291 goes unanswered again, having already been deferred once | The DoD accepts "attempted and judged not worth it" **with reasoning** as a pass. What it does not accept is silence |
| **Sixteen pins is a lot of surface for one milestone** | 15.1 balloons and crowds out 15.2-15.5 | The census in 15.0 sizes it before 15.1 commits. If the non-conforming set is large, split 15.1 by pin family and say which were deferred |
| **The milestone fixes pins while the things they guard rot** | A perfectly standardised pin population guarding stale truths | 15.3 and 15.4 are real bugs, not pin work. They are in the milestone deliberately so it ships behaviour, not only mechanism |

## Open Questions

- **Does a mechanical form exist for "this pin cannot pass while checking nothing"?** A Roslyn
  analyzer over test methods, a convention test that reflects over pin classes, or something
  cheaper. **15.0 must answer this**, because the DoD's enforcement criterion depends on it, and a
  negative answer changes 15.1's shape rather than failing it.
- **Is `RouteMenuDriftTests`-style set comparison in scope for the standard?** It scans nothing and
  can still go vacuous. This design says yes — vacuity is about the *assertion shape*, not the
  input — but it widens the census. Confirm in 15.0.
