# Session State

**Last session:** 2026-11-10
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.5 — slow-handler naming for compiler lambdas (#522), `[status: complete]`, 2026-11-10. 18.1 to 18.5 are complete. Next is 18.6, `[status: pending]`.
**Branch:** `feat/18.5-bind-handler-naming`.

## Current Position

- **18.5 is complete.** PR #634 is ready for review and awaits the owner's merge; it closes #522. Plan: `docs/superpowers/plans/2026-11-10-phase-18.5-bind-handler-naming.md`. Record: `docs/plans/2026-11-10-phase-18.5-record.md`, with the lanes in §1a.
- **Lanes on the final head `05ba2b86`:** ci and android-instrumented green on attempt 1. ios failed on attempt 1 with one test, `BnNotificationsTests.testShowBuildsAnImmediateRequestCarryingTheRouteInUserInfo`: the #444 signature. The PR touches no iOS shell code. Commented on #444.
- **Next phase:** 18.6, #543 and #544 (test-suite honesty, part two). Pending, no design yet.
- **Worktree:** `.claude/worktrees/bridge-cse_01RwoARgGNqhyJEGJMs6wt4L` holds the 18.5 branch. The main checkout is on `feat/16.7-classify-fault`.
- **Baselines:** .NET 1412, JVM 209, Android 271, iOS 296.

## Owner decisions for M18 (2026-10-24)

| Issue | Decision |
|---|---|
| #500 | Attribute a fault in a re-render or `RunAfterDispatch` action to the dispatch that queued it. Done in 18.2, PR #596. |
| #506 | Per-key secure-storage queue on Android; check iOS too. Done in 18.3, PR #599. |
| #522 | Name the page plus the wrapper position, never a compiler lambda. Done in 18.5, PR #634. |
| #543 | Bounded wait plus a Gradle per-test timeout. |

#562, the broad per-statement coverage, is deferred to a later testing milestone.

## Open Decisions (owner)

- **After M18 (decided 2026-11-04):** a short milestone, 1.0 readiness plus performance. Add a perf baseline: cold boot, dispatch-lane latency and frame-apply time on both shells, compared against an older release. Re-check every 1.0 criterion in `docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md` against current code. Then cut 1.0 or list what blocks it. The repo has no benchmark today.
- #621: a second instance's final destroy denies another instance's orphaned secure-storage prompt. The fix needs per-Activity prompt ownership, a design decision.
- Biometrics `authenticate` now completes ERROR on a stopped or destroyed Activity instead of hanging. That behaviour change is unpinned; see #600.
- #593: deliver a faulted batch's remove patches, or document that its diffs are dropped.
- #575: the frame path's two thread-pool hops.
- #580: iOS cold `LAContext` over 40 s.
- #582: the allowlist for a second-Activity logcat check.
- #444 recurred on PR #634's ios lane, attempt 1, on a PR with no iOS shell change. The M18 DoD needs attempt 1 green on every lane.
- #444: decouple a show's `Granted` from presentation, because the SpringBoard stall is not ours.
- #401 and #396: the canonical twin behaviour. 18.7's design proposes one.

## Blockers

- None.

## Recommended Next Step

1. Wait for the owner to merge PR #634, then brainstorm 18.6 (#543 and #544): there is no design yet.
2. The rules still hold:
   - No Claude signature anywhere.
   - Implementers tick their plan boxes.
   - Grade pins against `docs/pin-standard.md`, in "Grading a pin: sites and reds".
   - Ask questions with AskUserQuestion.
3. Watch, but do not work, the flakes #598, #602, #603 and #604. The M18 DoD needs attempt 1 green on every lane.
