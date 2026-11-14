# Session State

**Last session:** 2026-11-14
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.9 — the Windows dispatch bound (#564), `[status: pending]`, no design yet. 18.1 to 18.8 are complete.
**Branch:** `feat/18.9-dispatch-bound`, cut from `origin/main` `d9bf0711`. It holds one commit, the phase insertion. No PR yet.

## Current Position

- **PR #707 (18.8) is merged** as `d9bf0711` on 2026-11-14, after CodeRabbit approved head `a42d1290` with no findings and every lane was green. #530 closed. The branch is deleted. The owner confirmed D2's extended match rule as written.
- **Release PR #706 (0.22.1)** carries the 18.8 fix. Merging it is the owner's call.
- **Two phases were inserted before the audit**, by owner decision on 2026-11-14: fix the open DoD flakes at their cause first.
  - **18.9, #564:** a `FaultNoticeTests` dispatch misses its 1 s bound on the Windows runner. 18.1 closed it, and it reopened on 2026-11-05: run 41910675549, `AShellCallBegunOnAnotherThread_DoesNotMarkTheDispatch_SoAFaultAfterItIsRc2`, `DispatchBounded` at `FaultNoticeTests.cs:212`. No bound may be raised.
  - **18.10, #444:** the owner decided that an iOS `show` completes `Granted` once the request is handed to `UNUserNotificationCenter`, as Android's does after `nm.notify` (`AndroidShellBridge.kt:1989`). A later `add` error is logged, not returned. A twin pin holds the parity. Today `BnNotifications.swift:439` waits for `add`'s completion, which waits for SpringBoard's `willPresent` delivery; #444's trace shows SpringBoard stalling for 11 s.
  - **18.11** is the old 18.9, audit and close (#568).
- **Findings for 18.11, from this session's look at #568:**
  - #568's fourth cell was fixed in 18.8.
  - Its first three cells are still open: the 17.2 M5 cell, the 17.7 `!= main` contrast cell and the 17.7 §3 M1 cell, at old lines `:552`, `:553` and `:556`, which have moved since. So are the `FaultNoticeTests` line numbers that 18.2 reported.
  - Many M18-added pins now cite reds that later M18 phases made stale: 18.3's contract mutations, 18.4's H1 to H5, 18.1's W1 and W5. The DoD's new-pin item needs fresh reds, so they must be re-run.
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

- **After M18 (decided 2026-11-04):** a short milestone, 1.0 readiness plus performance. Add a perf baseline: cold boot, dispatch-lane latency and frame-apply time on both shells, compared against an older release. Re-check every 1.0 criterion in `docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md` against current code. Then cut 1.0 or list what blocks it. The repo has no benchmark today.
- #621: a second instance's final destroy denies another instance's orphaned secure-storage prompt. The fix needs per-Activity prompt ownership, a design decision.
- Biometrics `authenticate` now completes ERROR on a stopped or destroyed Activity instead of hanging. That behaviour change is unpinned; see #600.
- #593: deliver a faulted batch's remove patches, or document that its diffs are dropped.
- #575: the frame path's two thread-pool hops.
- #580: iOS cold `LAContext` over 40 s.
- #582: the allowlist for a second-Activity logcat check.

## Blockers

- None.

## Recommended Next Step

1. Run `start-next-phase` on `feat/18.9-dispatch-bound`. Phase 18.9 (#564) has no design, so it routes to brainstorming.
   - Find the cause first, with systematic debugging. Read #564 and its comments, the 18.1 record's #564 section, and `FaultNoticeTests.DispatchBounded`.
   - The rule is no guessed fix and no raised bound.
2. Then 18.10 (#444), with the posted-means-Granted decision above. Then 18.11, the audit.
3. The rules still hold:
   - No Claude signature anywhere, in commits as well as the PR body.
   - Implementers tick only their own plan boxes, line by line.
   - Grade pins against `docs/pin-standard.md`, "Grading a pin: sites and reds". Edit every file before mutating, and run the final review before the reds.
   - Ask questions with AskUserQuestion. Execution is subagent-driven.
   - Never `pkill -f` a pattern; kill by PID.
4. Watch, but do not work, the flakes #598, #602, #603, #604 and #676. The M18 DoD needs attempt 1 green on every lane.
