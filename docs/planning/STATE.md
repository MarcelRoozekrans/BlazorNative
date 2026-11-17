# Session State

**Last session:** 2026-11-17
**Milestone:** 19, 1.0 Readiness and Performance, `[status: active]` since 2026-11-17 (#732, merged as `51c837bd`).
**Phase:** 19.1 (re-grade the 1.0 criteria) is complete on branch `docs/19.1-criteria-regrade`, PR #733. Phases 19.2 to 19.9 are `[status: pending]`, with no design or plan yet.
**Next step:**
1. Get PR #733 green. Mark it ready when CI passes, then the owner merges it.
2. Run `start-next-phase`. It routes to Phase 19.2 (measurement harness, Surface: Infra), which starts with a phase-scope brainstorm.

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

## Other fixes made in 19.1

These sentences were found false and corrected:

- the S3 xmldoc in `NativeRenderer.cs`, which called the move non-breaking;
- the stale package counts and a false "404 today" claim in `scripts/release-preflight.ps1`, which was a prose-only edit that kept the BOM;
- the NOT-API count in the tiers doc, which is now 29;
- two "last blocker" claims in `docs/ios-device-verification-handover.md`.

## Open for the owner

- Merge PR #733 once it is green.

## Blockers

- None.

## Recommended Next Step

1. On "Merged ?" for #733, follow the merge rule. Check `reviewDecision` and the unresolved threads as well as the checks, and read CodeRabbit's review body even when it says APPROVED.
2. Then run `start-next-phase`, which routes to the Phase 19.2 brainstorm. Use the M19 design's "Calls made after the builder check" section for 19.2:
   - a `workflow_dispatch` measurement workflow;
   - marks gated at `Verbose` and silent at `Warn`, proven both ways;
   - a `v0.15.0` backport branch, with a build-only check of it as the first task;
   - the dispatch-lane and frame-apply mark points the design proposes.
3. Swift and JVM code cannot compile on Linux, so 19.2's shell marks need a draft PR early.
4. The standing rules still apply:
   - no Claude signature, and no closing keywords unless they are intended;
   - ask with AskUserQuestion;
   - execution is subagent-driven;
   - never `pkill -f`, and never `&`.
