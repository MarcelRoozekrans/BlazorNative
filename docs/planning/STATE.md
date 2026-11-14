# Session State

**Last session:** 2026-11-14
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.10 — iOS show completes when posted (#444), `[status: complete]`, on PR #712 awaiting the owner's merge. 18.1 to 18.9 are complete and merged; 18.9 is `a1bacd6b`.
**Branch:** `feat/18.10-show-posted`, cut from `origin/main` at `a1bacd6b`. PR #712.
**Next step:** the owner merges PR #712. Then cut `feat/18.11-audit` from `origin/main` and run `start-next-phase` for 18.11 (#568).

## Current Position

- **18.9 is merged: PR #710, squash `a1bacd6b`, #564 closed.** The record is [`docs/plans/2026-11-14-phase-18.9-record.md`](../plans/2026-11-14-phase-18.9-record.md).
  - **Cause:** the probe handler of `AShellCallBegunOnAnotherThread_…` blocked a 1 s-bounded dispatch on `Task.Run(...).Wait()`. xUnit 2.9 runs test bodies on the thread pool, so the queued item waited for thread injection. That was found with a pool hold that reproduced the CI message, and with dumps at >1 s spikes. It is not a product bug.
  - **Fix, test code only:**
    - a dedicated begin thread, with a product flowing-scope anchor;
    - the pin `DispatchBoundPoolTests`;
    - `TearDown` and `WaitForNoticeSends` wait without the pool and throw on a timeout, keeping the primary failure;
    - the `RunLateFault` facts run through `OffPool`.
  - **Reds:** R1 to R12 on `7527f158`. Every red taken on `FaultNoticeTests.cs` before 18.9 is stale, and the record's §3 lists them for 18.11.
  - **Filed:** #711, the other ~25 `FaultNoticeTests` facts of #551's shape, by owner decision.
  - **Review fix waves, before the merge:** CodeRabbit found that `TearDown` disposed a session whose overrun dispatch was still running, an unbounded wait. It now abandons that session, pinned by `TearDownOverrunTests`, and the pin joins its teardown thread. The record's §5 has both rounds and their reds.
  - **Flake #602 recurred** on #710's simulator leg at attempt 1, its third sighting on an unrelated PR; it now qualifies for a root-cause fix.
- **Release PR #706 (0.22.1)** carries the 18.8 fix. Merging it is the owner's call.
- **18.10 is complete on PR #712, awaiting the owner's merge.** The record is [`docs/plans/2026-11-14-phase-18.10-record.md`](../plans/2026-11-14-phase-18.10-record.md).
  - **Decision and fix:** the owner decided an iOS `show` completes `Granted` once the request is handed to `UNUserNotificationCenter`, as Android's does after `nm.notify`. `BnNotifications.runAction` now completes a show right after `add`; a late `add` error goes to `onError` or `BnLog`, and `schedule` is unchanged.
  - **Cause:** `add` waited on SpringBoard's `willPresent` delivery, which stalled for 11 s in #444's trace. It was never a product timing bug of ours.
  - **Pins and reds:** four pins in `BnNotificationsTests.swift`, graded by R0 to R9 in the record's §3, all on the reviewed head `69172396` at attempt 1.
  - **Stale reds, for 18.11:** the 18.1 record's #444 diagnostics describe `DaemonProbe`, `pollUntilProbing` and a `handlerAlive` that a show reports; all are gone or changed. The 18.5 and 18.8 records quote the old message "show never completed Granted ...". `BnNotificationsAndroidTest.kt` has no recorded red.
- **18.11** is the audit and close (#568).
- **Findings for 18.11, from #568:**
  - #568's fourth cell was fixed in 18.8.
  - Its first three cells are still open: the 17.2 M5 cell, the 17.7 `!= main` contrast cell and the 17.7 §3 M1 cell. Their line numbers have moved.
  - Many M18-added pins cite reds that later M18 phases made stale, among them 18.3's contract mutations, 18.4's H1 to H5, 18.1's W1 and W5, every pre-18.9 `FaultNoticeTests` red, and now the 18.1 #444 diagnostics. The DoD's new-pin item needs fresh reds.
- **Baselines:** .NET 2828 (1414 facts on two TFMs), JVM 210, Android 273, iOS 307.

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

1. Owner merges PR #712. On "Merged ?" for the standing merge approval, check `reviewDecision` and unresolved review threads as well as the checks. Then read state from `origin/main`.
2. Cut `feat/18.11-audit` from `origin/main` and run `start-next-phase` for **18.11**, the audit and close (#568). It carries the findings above.
3. The MILESTONE DoD flakes box stays open until 18.11 confirms attempt 1 green on the re-audit's own runs.
4. The rules still hold:
   - No Claude signature anywhere, in commits as well as the PR body.
   - Implementers tick only their own plan boxes, line by line.
   - Grade pins against `docs/pin-standard.md`, "Grading a pin: sites and reds". Edit every file before mutating, and run the final review before the reds.
   - Ask questions with AskUserQuestion. Execution is subagent-driven.
   - Never `pkill -f` a pattern; kill by PID.
5. Watch, but do not work, the flakes #598, #602, #603, #604 and #676. The M18 DoD needs attempt 1 green on every lane.
