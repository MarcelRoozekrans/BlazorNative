# Session State

**Last session:** 2026-11-22
**Milestone:** 19, 1.0 Readiness and Performance, `[status: active]` since 2026-11-17 (#732, merged as `51c837bd`).
**Phase:** 19.1 (re-grade the 1.0 criteria) is complete and merged: PR #733, squash `9aa49d85` on `main`. Phases 19.2 to 19.9 are `[status: pending]`, with no design or plan yet.
**Branch:** `docs/19.2-measurement-harness`, cut from `main` at `9aa49d85`. It holds only this handoff.
**Next step:** run `start-next-phase`. It routes to Phase 19.2 (measurement harness, Surface: Infra), which starts with a phase-scope brainstorm.

## What 19.1 produced

- **The evidence log** is `docs/plans/2026-11-17-phase-19.1-regrade.md`, anchored at `51c837bd`. It has one entry per criteria row and a sweep of 101 issues. It ends with the owner's rulings.
- **The ledger** is `docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md`, re-graded. Every row carries an M19 note.

The interim scoreboard:

| | Count |
|---|---:|
| Blockers | 17: 14 MET, 2 OPEN, 1 partial |
| Non-blocking | 13: 8 met, 4 still false but with a published caveat (#402, #495, D3, R4), 1 OPEN (R2, the owner's 1.0 cut) |

The two OPEN blockers are P3 (the real iPhone) and #600. The partial blocker is A5. Q3 counts as MET, but provisionally: the #12/#13 verdicts are pending in 19.3/19.4.

## Owner rulings, 2026-11-17

- **A5, the Unshipped→Shipped drain.** It is a 1.0 requirement. The new **Phase 19.8, API baseline drain (A5)** owns it. The 1.0 decision moved from 19.8 to **19.9**.
- **Q2.** It means surfacing, so it is MET. The rc clause is narrowed to the parameter-binding fault.
- **#210.** Narrowing the docs counts, so it is MET.
- **#402 and #495.** Both are DOCUMENTED, and their caveats are published. The #402 caveat is the corrected `BlazorNativeApp.cs` xmldoc. The #495 caveat is on quick-start, the Android shell page and the template README.
- **#600.** It is a BLOCKER. The existing **Phase 19.5** owns it, because its goal already includes the #600 fix with a pin. No phase was inserted for it, and no caveat was published.
- **Q5.** The `IMobileBridge.cs` xmldoc now says a zeroed `MaxDimension` keeps full resolution, so Q5 is MET.
- **D3 and R4.** Their caveats are published: the api-stability page, and a README Limitations bullet.
- **README status paragraph.** Count-free; it points at the 1.0 checklist.
- **R3.** Reworded to name `BlazorNative.Testing`.

## Owner ruling, 2026-11-22

- **P3's scope.** APNs and universal links are dropped from P3's acceptance items, because neither feature is built and both are published 1.0 limitations. CodeRabbit raised it on #733. P3 now has **five** items, and stays OPEN and a BLOCKER until the 19.7 session. Recorded in the ledger (P3 row and §6), the evidence log (ruling 12), the handover, ROADMAP 19.7, MILESTONE's DoD, the M19 design and `website/docs/api-stability.md`.

## Other fixes made in 19.1

These sentences were found false and corrected:

- the S3 xmldoc in `NativeRenderer.cs`, which called the move non-breaking;
- the PR baseline of the release-validation lane in `.github/workflows/release.yml`, which credited `main`'s release commit to #733 and red its nuget.org preflight; it now diffs against the merge ref's first parent;
- the stale package counts and a false "404 today" claim in `scripts/release-preflight.ps1`, which was a prose-only edit that kept the BOM;
- the NOT-API count in the tiers doc, which is now 29;
- two "last blocker" claims in `docs/ios-device-verification-handover.md`.

## Open for the owner

- Nothing on 19.1. The owner runs the iPhone session in 19.7.

## Blockers

- None.

## Recommended Next Step

1. Run `start-next-phase` on `docs/19.2-measurement-harness`. It routes to the Phase 19.2 brainstorm. Use the M19 design's "Calls made after the builder check" section:
   - a `workflow_dispatch` measurement workflow;
   - marks gated at `Verbose` and silent at `Warn`, proven both ways;
   - a `v0.15.0` backport branch, with a build-only check of it as the first task;
   - the dispatch-lane and frame-apply mark points the design proposes.
2. Swift and JVM code cannot compile on Linux, so open a draft PR early for 19.2's shell marks.
3. The standing rules still apply:
   - no Claude signature, and no closing keywords unless they are intended;
   - ask with AskUserQuestion;
   - execution is subagent-driven;
   - never `pkill -f`, and never `&`.
4. CodeRabbit re-reviews every push and can turn a green PR to CHANGES_REQUESTED; #733 took two rounds. Check `reviewDecision`, unresolved threads and its review body before saying a PR is ready.
