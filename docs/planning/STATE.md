# Session State

**Last session:** 2026-11-10
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.6 — test-suite honesty, part two (#543, #544), `[status: complete]`, PR #659 awaiting merge. 18.1 to 18.6 are complete. Next: 18.7, twin divergences (#401, #396), no design yet.
**Branch:** `feat/18.6-test-suite-honesty`, PR #659, against `main`.

## Current Position

- **18.6 is done and PR #659 is ready for review.** All six plan tasks are complete, the whole-branch review's fix wave is in, and every lane is green on attempt 1 on the head. The phase record is `docs/plans/2026-11-10-phase-18.6-record.md`.
  - **#543:** the hold-probe test bounds its blocking back with a 10 s join. The JVM test task has a 120 s per-test timeout in `SEPARATE_THREAD` mode and a 25 min backstop, measured against a 2 s slowest test and a ~90 s step. `ci.yml` prints the five slowest JVM tests.
  - **#544:** `pressHandledBack` in `Poll.kt` checks `isFinishing` in the press's own turn, and every handled-back press after boot uses it. One press inside a poll predicate stays inline with the same check.
  - **Reds:** 18.6-R1 to R7, R2b, S4 to S7 and D10r, each on attempt 1 except where the record says otherwise. Stale reds are named in the register and on #568.
- **A dependency break was fixed on main on the way, owner decision 2026-11-10.** The floating `10.0.1*` on Components picked up a partly published 10.0.15 and split the family (NU1605). PR #660, `dbca4940`, pins the whole dotnet 10 family exactly at 10.0.14, so Renovate's dotnet monorepo group moves it together.
- **Release PR #658 (0.21.3)** still awaits the owner.
- **Worktree:** `.claude/worktrees/bridge-cse_01RwoARgGNqhyJEGJMs6wt4L` holds the 18.6 branch.
- **Baselines:** .NET 1412, JVM 209, Android 271, iOS 296. Unchanged by 18.6.

## Owner decisions for M18 (2026-10-24)

| Issue | Decision |
|---|---|
| #500 | Attribute a fault in a re-render or `RunAfterDispatch` action to the dispatch that queued it. Done in 18.2, PR #596. |
| #506 | Per-key secure-storage queue on Android; check iOS too. Done in 18.3, PR #599. |
| #522 | Name the page plus the wrapper position, never a compiler lambda. Done in 18.5, PR #634. |
| #543 | Bounded wait plus a Gradle per-test timeout. Done in 18.6, PR #659. |
| #544 | A shared press helper with an in-turn `isFinishing` check, decided 2026-11-10. Done in 18.6, PR #659. |

#562, the broad per-statement coverage, is deferred to a later testing milestone.

## Open Decisions (owner)

- **After M18 (decided 2026-11-04):** a short milestone, 1.0 readiness plus performance. Add a perf baseline: cold boot, dispatch-lane latency and frame-apply time on both shells, compared against an older release. Re-check every 1.0 criterion in `docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md` against current code. Then cut 1.0 or list what blocks it. The repo has no benchmark today.
- #621: a second instance's final destroy denies another instance's orphaned secure-storage prompt. The fix needs per-Activity prompt ownership, a design decision.
- Biometrics `authenticate` now completes ERROR on a stopped or destroyed Activity instead of hanging. That behaviour change is unpinned; see #600.
- #593: deliver a faulted batch's remove patches, or document that its diffs are dropped.
- #575: the frame path's two thread-pool hops.
- #580: iOS cold `LAContext` over 40 s.
- #582: the allowlist for a second-Activity logcat check.
- #444 recurred on PR #634's ios lane, attempt 1, on head `05ba2b86`, a PR with no iOS shell change; the final head was green on attempt 1. The M18 DoD needs attempt 1 green on every lane.
- #444: decouple a show's `Granted` from presentation, because the SpringBoard stall is not ours.
- #401 and #396: the canonical twin behaviour. 18.7's design proposes one.

## Blockers

- None.

## Recommended Next Step

1. The owner merges PR #659. Before merging, check that its body still closes #543 and #544 and that the lanes are green on the head.
2. Then brainstorm 18.7 (#401, #396) via `start-next-phase`: there is no design yet. Read both issues first. The 18.7 design proposes one canonical twin behaviour.
3. The rules still hold:
   - No Claude signature anywhere.
   - Implementers tick only their own plan boxes, line by line. A broad sed over-ticked other tasks' boxes in 18.6.
   - Grade pins against `docs/pin-standard.md`, in "Grading a pin: sites and reds". Edit every file before mutating, and give this phase's reds a `18.x-` prefix.
   - Ask questions with AskUserQuestion.
4. Watch, but do not work, the flakes #598, #602, #603, #604 and the new #676 (`NotificationBridgeTests.Completion_KeyedByRequestId_ResolvesTheRightCall`). The M18 DoD needs attempt 1 green on every lane.
