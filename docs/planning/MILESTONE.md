# Milestone 15: A Standard for Pins

**Status:** active
**Started:** 2026-09-22

**Design:** [`docs/superpowers/specs/2026-09-22-milestone-15-design.md`](../superpowers/specs/2026-09-22-milestone-15-design.md)
**Predecessor:** Milestone 14 — Twin Divergence, Closed Mechanically (complete 2026-09-22, verdict
**PASS WITH FINDINGS**, [audit](../plans/2026-09-22-milestone-14-audit.md)).
**Source:** M14's own audit. Its central criterion — *a NEW divergence reds* — was met **narrowly**,
and the four reproduced holes filed as [#364][i364] carried an argument larger than themselves:
the thing to reason about is a pin's **coverage**, not its assertions.

[i296]: https://github.com/MarcelRoozekrans/BlazorNative/issues/296
[i297]: https://github.com/MarcelRoozekrans/BlazorNative/issues/297
[i298]: https://github.com/MarcelRoozekrans/BlazorNative/issues/298
[i302]: https://github.com/MarcelRoozekrans/BlazorNative/issues/302
[i356]: https://github.com/MarcelRoozekrans/BlazorNative/issues/356
[i357]: https://github.com/MarcelRoozekrans/BlazorNative/issues/357
[i364]: https://github.com/MarcelRoozekrans/BlazorNative/issues/364
[i365]: https://github.com/MarcelRoozekrans/BlazorNative/issues/365
[i291]: https://github.com/MarcelRoozekrans/BlazorNative/issues/291

## Goal

This repo defends its invariants with **drift pins** — tests that read source or config and assert
that two copies of one truth agree. There are **at least nineteen** of them and **four** manifests,
accumulated across many milestones, each written to catch the bug in front of it. They have no
shared standard, and it shows: M14's newest pin was defeated by **five successive reviews**, each
with a shape the previous round had not tried, and every fix was local to the instance.

M15 establishes what a pin must do to be trusted, applies that standard to every existing pin, and
closes the backlog of missing and broken ones as **consequences rather than as nine separate
errands**. The user-visible outcome is narrow but real: **a green CI run means more afterwards than
it does today.**

## The thesis, stated precisely

**A pin that can pass while checking nothing is not a pin.** Today we cannot say which of ours can.

Vacuity is more general than "the scan stopped matching". Any assertion of the form *for every X,
assert Y* passes trivially when there are no X — whether X came from a regex over source, a
directory walk, or a collection comparison. `RouteMenuDriftTests` performs **no file scanning at
all**, and `EveryRoutedPage_ExceptTheTwoExemptions_HasAMenuRow` still passes over an empty page
list.

A first heuristic measurement suggested **6 of the 16 in `Runtime.Tests`** carry no anti-vacuity
assertion. Some may not need one. **Phase 15.0 replaces that guess with a measured per-pin
verdict** — the number is not to be trusted until it is.

> **Correction, found in 15.0's first minutes and kept here as evidence rather than tidied away.**
> That heuristic counted **one test project**. The real population is **19 `*DriftTests` across
> three projects**, plus at least five more pins that do not carry that suffix at all —
> `LayoutSurfacePinTests`, `PackageVersionPinTests`, `ReleaseWorkflowPinTests`,
> `DefaultStructTrapSweepTests`, `AnalyzerDiagnosticRosterTests`.
>
> **You cannot enumerate this population by name.** `Drift`, `Pin`, `Sweep` and `Roster` are all in
> use. That is not cosmetic: it directly threatens the DoD's enforcement criterion, because a
> convention test reflecting over `*DriftTests` would **silently miss a quarter of the pins** —
> this milestone's own bug class, reproduced inside the mechanism meant to close it.
>
> **So 15.0 has a prior question: *what makes a test a pin?*** The naming is evidence that the
> answer is not currently obvious, and enforcement cannot key on a suffix a quarter of the
> population does not use.

> **[#357][i357] is the thesis in miniature.** 14.1's completeness pin lacks an assertion that
> 14.3's structurally identical twin has. **The guards built to catch twin divergence have drifted
> from each other.**

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing — .NET, JVM, **and both device lanes dispatched**, with each lane's
      `headSha` compared against the PR head rather than its conclusion read alone
- [ ] **A written pin standard exists**, in the repo rather than in a milestone doc, stating what
      every drift pin must do — at minimum: it must fail when it scans nothing, it must fail when
      its subject moves, and it must state what it does **not** cover.
- [ ] **Every one of the existing pins is assessed against that standard**, recorded per
      pin as *conforms*, *fixed*, or *exempt with a written reason*. An unassessed pin is a gap;
      "exempt" is an acceptable outcome, **silence is not**.
- [ ] **The standard is enforced mechanically, not by review.** A new pin that can pass while
      checking nothing must red. If no mechanical form exists, that is a finding to record
      explicitly with its evidence — never a line to quietly drop.
- [ ] **[#364][i364] is answered, not merely fixed.** The auth-semantics pin's *coverage* is
      written down — which trees, which spellings, which file kinds — and its four known holes
      close as consequences of that definition rather than as four patches.
- [ ] **[#296][i296] is closed by the standard rather than around it.** It is the first live
      divergence to meet the new mechanism, and **it must red before it is fixed**.
- [ ] **[#357][i357], [#297][i297] and [#302][i302] are closed** — an asymmetry between twins, a
      missing pin, and a missing guard.
- [ ] **[#291][i291] is answered for prose.** M14 found **three** unpinned documentation
      transcription pairs. Either they are pinned, or the milestone records that prose pinning was
      attempted and judged not worth its cost — **with the reasoning, not the conclusion alone**.
- [ ] **The small corrections land:** [#298][i298], [#356][i356], [#365][i365].
- [ ] **No new public API, wire or ABI change** — verified by diffing, not asserted.

> **No "release tagged in git" criterion.** `docs/planning/CONVENTIONS.md` records **`Milestone
> completion tags a release: no`** — release-please owns the `v<semver>` namespace. A checkbox
> nothing will ever tick is a permanent false gap.

## Phases

1. Phase 15.0 — the pin standard [complete] — closed 2026-09-22 on
   [PR #372](https://github.com/MarcelRoozekrans/BlazorNative/pull/372) +
   [PR #373](https://github.com/MarcelRoozekrans/BlazorNative/pull/373); .NET 1111 → **1112**;
   no production source change. **The population is enumerable by BEHAVIOUR** — 24 copy-pasted
   `RepoRoot()` walks became one, and **callers of the shared helper are the pin population,
   exactly**. The count had been wrong **four** times, every time by counting a name. It found
   **six unhardened comment strippers**, one of them letting a bare undeclared `NSLog` hide
   behind an ordinary URL and leaving a **PII guard green** over a tree that contained it — now
   one stripper, eight callers. Census judges **per fact**: 118 facts, 102 pins,
   **93 conforms / 9 gap / 16 exempt**. **The enforcement verdict is NEGATIVE** and is the
   phase's most useful output: a presence-style convention test scores **0 of 4** against the
   known defects, because all four already execute a count assertion on the **wrong set** — so
   it would hand out a green under a name claiming coverage. 15.1 re-scoped accordingly
2. Phase 15.1 — enforce the standard [complete] — all nine gap facts closed, plus the live style-table defect and the two non-pin detectors; suite 1112 → 1132 (#377)
3. Phase 15.2 — define the auth pin's coverage [active]
4. Phase 15.3 — the first live test: deep-link scheme [pending]
5. Phase 15.4 — the missing guards [pending]
6. Phase 15.5 — prose, and the small corrections [pending]
7. Phase 15.6 — audit and close [pending]

**Ordering rationale.** 15.0 comes first because the standard is what everything else applies;
writing it afterwards would make it a description of whatever we happened to do. **15.2 and 15.3
are adjacent deliberately** — 15.2 defines coverage for the pin that has been defeated five times,
and 15.3 is the first live divergence to meet it, which is the cheapest honest test of whether the
definition was any good. 15.4 and 15.5 are independent of each other and of 15.3.

## Risk areas

| Risk | Impact | Mitigation |
|---|---|---|
| **The standard describes what we already do**, so every pin "conforms" and nothing changes | The milestone documents the status quo, and the sixth review finds a seventh shape | 15.0's census is **measured per pin, not asserted**, and must replace the heuristic 6-of-16-in-one-project with a real number. If it finds every pin conforming, that is a finding to challenge — the evidence says otherwise |
| **No mechanical enforcement exists** for "a pin must not pass while checking nothing" | The standard degrades into a review checklist, which is what M14 already had | Record it explicitly with evidence rather than dropping it. A partial mechanism — enforcing only the anti-vacuity half — is an acceptable honest outcome |
| **15.2 defines coverage too narrowly** and 15.3 exposes it immediately | Rework, and the definition loses credibility | That is the *point* of the adjacency. An early failure there is cheap and informative; finding it in 15.6's audit is not |
| **Prose pinning proves not worth its cost** | #291 goes unanswered again, having been deferred once already | "Attempted and judged not worth it, with reasoning" is an acceptable pass. **Silence is not** |
| **The pin population is a lot of surface for one milestone** | 15.1 balloons and crowds out 15.2-15.5 | 15.0's census sizes it before 15.1 commits. If the non-conforming set is large, split 15.1 by pin family and say which were deferred |
| **The milestone polishes pins while the truths they guard rot** | A perfectly standardised population guarding stale facts | 15.3 and 15.4 are **real bugs**, not pin work. They are here deliberately so the milestone ships behaviour, not only mechanism |

## Out of scope

- **1.0** — carried forward from M14's scoping decision 1. Whether the pin standard is a 1.0
  requirement is a separate owner call.
- **#17's Apple-account cluster and real-device execution** — externally dependent, untouched here.
  **No phase in this milestone needs a device, an Apple account, or an external contributor**, and
  that is deliberate.
- **The feature backlog** — #18, #21, #24, #284, #285, #286. Each is its own milestone.
- **The accepted hardening debt** — #8, #9, #12, #13, owner-accepted as Q3.

## Open questions

- **Does a mechanical form exist for "this pin cannot pass while checking nothing"?** A Roslyn
  analyzer over test methods, a convention test reflecting over pin classes, or something cheaper.
  **15.0 must answer this**, because the DoD's enforcement criterion depends on it — and a negative
  answer reshapes 15.1 rather than failing it.
- **Is set-comparison in scope for the standard?** `RouteMenuDriftTests` scans nothing and can
  still go vacuous. The design says yes — vacuity is about the *assertion shape*, not the input —
  but it widens the census. **Confirm in 15.0.**

## Audit History

| Date | Verdict | Gaps |
|---|---|---|
| — | *(not yet audited)* | — |
