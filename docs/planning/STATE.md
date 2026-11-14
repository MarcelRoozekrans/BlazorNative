# Session State

**Last session:** 2026-11-14
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.8 — the generator's pipe holder (#530), `[status: complete]` on this branch. 18.1 to 18.8 are complete; 18.9 (#568) is next.
**Branch:** `feat/18.8-pipe-holder`, PR #707 against `main`. Draft until its lanes are green on the complete-phase head, then ready. Not merged.

## Current Position

- **#530's holder is identified:** on Windows CI it is a reused MSBuild worker node, `dotnet … MSBuild.dll /nodemode:1 /nodeReuse:true`, started by the generator's `dotnet publish`. It kept the publish's stdout pipe after the publish exited. Record: `docs/plans/2026-11-13-phase-18.8-record.md`.
  - Stripped arm, node reuse on: 20 of 20 iterations stalled, 59 watcher firings, 98 holder rows, all the node. Run 43477943304.
  - Fixed arm, 17.5's `-nodeReuse:false` plus `MSBUILDDISABLENODEREUSE=1`: 20 of 20 clean. Run 43477938291.
  - So 17.5's change is the source fix on Windows. The 2026-10-15 Linux holder was not measured and is not claimed.
- **D2 was amended twice.** The owner ruled on 2026-11-13 to match by kernel pipe identity, because the redirect pipe is anonymous on `windows-latest`. A controller ruling then added the write-end match with ServerPid equal to the reader, and an anchor-created rule. The PR body asks the owner to confirm it; 96 of 98 rows meet the stricter rule alone.
- **Pins:** 18.8-M3 and M3b re-red the node-reuse pin; 25 stale reds of pin-standard rows 613 to 615 were re-run as `18.8-*`, and the rows are remapped. 18.8-E1 is the behavioural evidence, and nothing in CI re-runs it.
- **The harness is gone from the remote:** the `scratch/530-*` refs were deleted after the record was written. Four local `scratch/530-*` branches remain in the main checkout; they are safe to delete.
- **Lanes on the Task 5 head `06c87c6d`:** ci, android-instrumented and ios all green on attempt 1. iOS on `6c17145f` red once with the #444 signature, recorded in record section 1.
- **Commits:** the branch history was rewritten with a message-only change to strip two Co-Authored-By trailers, then force-pushed before the PR existed.
- **Baselines unchanged:** .NET 2824 (1412 facts on two TFMs), JVM 210, Android 273, iOS 303.

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

- **Confirm D2's extended match rule**, asked in PR #707's body.
- **After M18 (decided 2026-11-04):** a short milestone, 1.0 readiness plus performance. Add a perf baseline: cold boot, dispatch-lane latency and frame-apply time on both shells, compared against an older release. Re-check every 1.0 criterion in `docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md` against current code. Then cut 1.0 or list what blocks it. The repo has no benchmark today.
- #621: a second instance's final destroy denies another instance's orphaned secure-storage prompt. The fix needs per-Activity prompt ownership, a design decision.
- Biometrics `authenticate` now completes ERROR on a stopped or destroyed Activity instead of hanging. That behaviour change is unpinned; see #600.
- #593: deliver a faulted batch's remove patches, or document that its diffs are dropped.
- #575: the frame path's two thread-pool hops.
- #580: iOS cold `LAContext` over 40 s.
- #582: the allowlist for a second-Activity logcat check.
- #444: decouple a show's `Granted` from presentation, because the SpringBoard stall is not ours. It recurred on PR #707's ios lane on `6c17145f`.

## Blockers

- None. PR #707 waits on its lanes for the complete-phase head, then the owner's merge.

## Recommended Next Step

1. Merge PR #707 when it is green and ready. Read state from `origin/main` afterwards; the repo squash-merges.
2. Run `start-next-phase` for **18.9, Audit and close (#568)**, on a new branch from `main`. It routes to brainstorming, since it has no design yet.
   - #568's list now includes the 18.8 comment on it, with the stale reds of rows 613 to 615 resolved.
3. The rules still hold:
   - No Claude signature anywhere, in commits as well as the PR body.
   - Implementers tick only their own plan boxes, line by line.
   - Grade pins against `docs/pin-standard.md`, "Grading a pin: sites and reds". Edit every file before mutating, and run the final review before the reds.
   - Ask questions with AskUserQuestion.
   - Never `pkill -f` a pattern; kill by PID.
4. Watch, but do not work, the flakes #598, #602, #603, #604, #676 and #444. The M18 DoD needs attempt 1 green on every lane.
