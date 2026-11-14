# Session State

**Last session:** 2026-11-14
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.9 — the Windows dispatch bound (#564), `[status: complete]` on the branch. Next is 18.10 (#444), `[status: pending]`, no design yet. 18.1 to 18.9 are complete.
**Branch:** `feat/18.9-dispatch-bound`, PR #710. All lanes were green at attempt 1 on `bc9abb77`. The PR is ready for the owner's merge, and every later commit is docs-only.

## Current Position

- **18.9 is done, in PR #710, not yet merged.** The record is [`docs/plans/2026-11-14-phase-18.9-record.md`](../plans/2026-11-14-phase-18.9-record.md).
  - **Cause:** the probe handler of `AShellCallBegunOnAnotherThread_…` blocked a 1 s-bounded dispatch on `Task.Run(...).Wait()`. xUnit 2.9 runs test bodies on the thread pool, so the queued item waited for thread injection. That was found with a pool hold that reproduced the CI message, and with dumps at >1 s spikes. It is not a product bug.
  - **Fix, test code only:**
    - a dedicated begin thread, with a product flowing-scope anchor;
    - the pin `DispatchBoundPoolTests`;
    - `TearDown` and `WaitForNoticeSends` wait without the pool and throw on a timeout, keeping the primary failure;
    - the `RunLateFault` facts run through `OffPool`.
  - **Reds:** R1 to R12 on `7527f158`. Every red taken on `FaultNoticeTests.cs` before 18.9 is stale, and the record's §3 lists them for 18.11.
  - **Filed:** #711, the other ~25 `FaultNoticeTests` facts of #551's shape, by owner decision.
- **Release PR #706 (0.22.1)** carries the 18.8 fix. Merging it is the owner's call.
- **18.10, #444:** the owner decided that an iOS `show` completes `Granted` once the request is handed to `UNUserNotificationCenter`, as Android's does after `nm.notify` (`AndroidShellBridge.kt:1989`). A later `add` error is logged, not returned. A twin pin holds the parity. Today `BnNotifications.swift:439` waits for `add`'s completion, which waits for SpringBoard's `willPresent` delivery; #444's trace shows SpringBoard stalling for 11 s.
- **18.11** is the audit and close (#568).
- **Findings for 18.11, from #568:**
  - #568's fourth cell was fixed in 18.8.
  - Its first three cells are still open: the 17.2 M5 cell, the 17.7 `!= main` contrast cell and the 17.7 §3 M1 cell. Their line numbers have moved.
  - Many M18-added pins cite reds that later M18 phases made stale, among them 18.3's contract mutations, 18.4's H1 to H5, 18.1's W1 and W5, and now every pre-18.9 `FaultNoticeTests` red. The DoD's new-pin item needs fresh reds.
- **Baselines:** .NET 2826 (1413 facts on two TFMs) once #710 merges, JVM 210, Android 273, iOS 303.

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

1. Owner merges PR #710. Ask "Merged ?" for the standing merge approval. Then read state from `origin/main`.
2. Cut `feat/18.10-show-posted` from `origin/main` and run `start-next-phase` for **18.10** (#444). It has no design, so it routes to brainstorming, with the owner's posted-means-Granted decision above.
3. Then 18.11, the audit (#568).
4. The rules still hold:
   - No Claude signature anywhere, in commits as well as the PR body.
   - Implementers tick only their own plan boxes, line by line.
   - Grade pins against `docs/pin-standard.md`, "Grading a pin: sites and reds". Edit every file before mutating, and run the final review before the reds.
   - Ask questions with AskUserQuestion. Execution is subagent-driven.
   - Never `pkill -f` a pattern; kill by PID.
5. Watch, but do not work, the flakes #598, #602, #603, #604 and #676. The M18 DoD needs attempt 1 green on every lane.
