# Session State

**Last session:** 2026-11-03
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.4 — recreation during boot (#511), with #601 folded in. Complete, pending merge of PR #605. 18.1, 18.2 and 18.3 are complete and merged.
**Branch:** `feat/18.4-recreation-during-boot`.

## Current Position

- **18.4 is complete pending the merge of draft PR #605** on `feat/18.4-recreation-during-boot`. All six plan tasks are done, the whole-branch review and its fix wave are in, and the phase is marked complete in ROADMAP and MILESTONE.
  - Spec: `docs/superpowers/specs/2026-10-31-phase-18.4-design.md`. Plan: `docs/superpowers/plans/2026-10-31-phase-18.4-recreation-during-boot.md`. Record: `docs/plans/2026-10-31-phase-18.4-record.md`.
  - Final lanes on code head `4b9c986c`, all attempt 1 with `headSha` compared: ci 41629060648, android-instrumented 41629060661, ios 41629060729, commitlint 41629060659.
  - Commit parse check: 35 commits and the squash message all parse. No signature, session link or closing keyword.
  - **Baselines:** .NET 1406, JVM 208, Android 271, iOS 296.
  - **Squash merge:** an earlier branch commit body says "closed #506". It is harmless because #506 is already closed, but strip it from the squash message.
  - Filed from the review: #621, see Open Decisions.
  - The controller marks the PR ready and merges; the plan's Task 6 Step 6 is ticked for the complete-phase gate only.

## Owner decisions for M18 (2026-10-24)

| Issue | Decision |
|---|---|
| #500 | Attribute a fault in a re-render or `RunAfterDispatch` action to the dispatch that queued it. Done in 18.2, PR #596. |
| #506 | Per-key secure-storage queue on Android; check iOS too. Done in 18.3, PR #599. |
| #522 | Name the page plus the wrapper position, never a compiler lambda. |
| #543 | Bounded wait plus a Gradle per-test timeout. |

#562, the broad per-statement coverage, is deferred to a later testing milestone.

## Open Decisions (owner)

- #601 was decided on 2026-10-31: a process-wide hold, in 18.4. Done.
- #621: a second instance's final destroy denies another instance's orphaned secure-storage prompt. The fix needs per-Activity prompt ownership, a design decision.
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

1. Merge PR #605 once the controller marks it ready.
2. Then start 18.5 (#522): name the page plus the wrapper position, never a compiler lambda. Surface Backend, no design spec yet, so brainstorm first.
3. The rules still hold:
   - No Claude signature anywhere.
   - Implementers tick their plan boxes.
   - Grade pins against `docs/pin-standard.md`, in "Grading a pin: sites and reds".
   - Ask questions with AskUserQuestion.
4. Watch, but do not work, the flakes #598, #602, #603 and #604. The M18 DoD needs attempt 1 green on every lane.
