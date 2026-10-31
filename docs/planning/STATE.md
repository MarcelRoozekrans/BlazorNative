# Session State

**Last session:** 2026-10-31
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.4 — recreation during boot (#511), with #601 folded in. In execution: Tasks 1–3 of 6 implemented, paused before the Task 3 review. 18.1, 18.2 and 18.3 are complete and merged.
**Branch:** `feat/18.4-recreation-during-boot`.

## Current Position

- **18.4 is in execution: Tasks 1–3 of 6 are implemented, and Tasks 1 and 2 are reviewed.** Draft PR #605 is on `feat/18.4-recreation-during-boot`, head `25779361`. Its body closes #511 and #601 on purpose.
  - Spec: `docs/superpowers/specs/2026-10-31-phase-18.4-design.md`. Plan: `docs/superpowers/plans/2026-10-31-phase-18.4-recreation-during-boot.md`, Tasks 1–3 ticked. Record: `docs/plans/2026-10-31-phase-18.4-record.md`, §2 and §6 filled for Tasks 1–3.
  - **Task 1, the start lock:** `ade44034`. Reviewed after one fix round. JVM 207 in CI.
  - **Task 2, the boot record:** `d0da5301`. Reviewed after one fix round. Pin 1b was renamed `a_relaunch_after_a_failed_first_boot_still_retires_it`.
  - **Task 3, the process-wide hold:** `69d157aa..25779361`. **Implemented, NOT yet task-reviewed.** Green run 41243759052: 271 Android tests, 0 failures. 4c, the real-Activity variant, works.
  - Every lane fails only at its count step, as expected: JVM 207 vs 206, Android 271 vs 263. Task 4 fixes the counts.
- **Paused on 2026-10-31** because the owner's weekly token budget was at 90%. The owner chose to let Task 3 finish and to pause before its review.

## Carried into Tasks 3–6 (controller rulings)

- **Task 3 review comes first.** The implementer named five concerns:
  - The reset's "secure storage: a test left a held key" check has no red of its own.
  - The 4x drain in `AndroidHostCallContractTest`'s `@After` is a heuristic.
  - A parked job's throw is reported through the releasing bridge's `onError`.
  - 3a's "no orphan blob" check is weak, as in 18.3: the hook's `succeed()` never writes a blob.
  - Its `@After` now destroys every bridge it built. `RecreationAndroidTest` was not edited.
- **Task 5 must also red, or argue per RS1 part 3:**
  - `StartLockTest`'s `anchor: A is inside start()`. J1 and J2 leave it passing; try moving the probe increment after the hook.
  - 1b's premise anchor, which only a fixture red reaches.
  - The D2 10 s warn and the ignored `retire()` result, which no pin covers.
  - 1a's positive assertions, "page is dead" and `foreignPatchesForTest == 0`. Once the anchor catches a missing join, a single subject mutation may not reach them.
- **B3 is argued in writing, not run.** The plan's mutation B3 puts the join and the retire inside the `try`, and it cannot red anything. The older boot throws on another thread, so the `try` only guards this boot's own `start()`. Record §6 says so.
- **Stale reds:**
  - The earlier `StartLockTest` red (run 41231265863) and the 1b red on `045f3274` predate later test edits. Task 5's J1/J2 and B-runs re-establish them.
  - The `RecreationAndroidTest.kt` and `RecreationTests.cs` header lines saying "#511 not covered" are out of date. Task 5 names them in their register cells; their files stay unedited.
- **The squash merge:** an earlier branch commit body contains "closed #506". #506 is already closed, so this is harmless, but strip it from the squash message.
- **This session's worktree:** the local `feat/18.4…` name is checked out in another locked worktree, so this one pushed with `git push origin HEAD:feat/18.4-recreation-during-boot`. The SDD ledger under `.superpowers/sdd/` is git-ignored and local to that worktree; this list carries what it held.

## Owner decisions for M18 (2026-10-24)

| Issue | Decision |
|---|---|
| #500 | Attribute a fault in a re-render or `RunAfterDispatch` action to the dispatch that queued it. Done in 18.2, PR #596. |
| #506 | Per-key secure-storage queue on Android; check iOS too. Done in 18.3, PR #599. |
| #522 | Name the page plus the wrapper position, never a compiler lambda. |
| #543 | Bounded wait plus a Gradle per-test timeout. |

#562, the broad per-statement coverage, is deferred to a later testing milestone.

## Open Decisions (owner)

- #601 was decided on 2026-10-31: a process-wide hold, in 18.4.
- Biometrics `authenticate` now completes ERROR on a stopped or destroyed Activity instead of hanging. That behaviour change is unpinned; see #600.
- #593: deliver a faulted batch's remove patches, or document that its diffs are dropped.
- #575: the frame path's two thread-pool hops.
- #580: iOS cold `LAContext` over 40 s.
- #582: the allowlist for a second-Activity logcat check.
- #444: decouple a show's `Granted` from presentation, because the SpringBoard stall is not ours.
- #401 and #396: the canonical twin behaviour. 18.7's design proposes one.

## Blockers

- None.

## Recommended Next Step

1. Resume 18.4 subagent-driven on `feat/18.4-recreation-during-boot`, head `25779361`.
2. **First, the Task 3 task review** of `d0da5301..25779361`, against the plan's Task 3, D4, D5 pins 3 and 4, and Review Focus 3 and 4. Then its fix loop.
3. Then Task 4 (counts: JVM 207, Android 271 if Task 3's review adds none), Task 5 (mutations, plus the carried list above), and Task 6 (whole-branch review before the close-out).
4. The rules still hold:
   - No Claude signature anywhere.
   - Implementers tick their plan boxes.
   - Grade pins against `docs/pin-standard.md`, in "Grading a pin: sites and reds".
   - Ask questions with AskUserQuestion.
5. Watch, but do not work, the flakes #598, #602, #603 and #604. The M18 DoD needs attempt 1 green on every lane.
