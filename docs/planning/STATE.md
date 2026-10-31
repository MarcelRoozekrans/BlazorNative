# Session State

**Last session:** 2026-10-31
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.3 — secure-storage per-key ordering (#506) `[status: pending]`, in design. Its design is drafted and awaits owner approval. 18.2 is complete and merged.
**Branch:** `feat/18.3-secure-storage-per-key`, from `main` at `d246778f`.

## Current Position

- **18.1 is complete on `main`:** #584 shipped it, and #591 recorded it.
- **18.2 is complete and merged.** PR #596 was squash-merged as `d246778f` and closed #500. It is in release PR #597.
  - Design: `docs/superpowers/specs/2026-10-30-phase-18.2-design.md`, approved 2026-10-30 and amended 2026-10-31.
  - Plan: `docs/superpowers/plans/2026-10-30-phase-18.2-queued-fault-attribution.md`.
  - Record: `docs/plans/2026-10-30-phase-18.2-record.md`. Its §6 lists the deviations from the design.
- **The owner confirmed R10 on 2026-10-31.** A is held from its inline segment's entry until B's window closes, even when it dirtied nothing. The design's `AwaitWholeHandler` flush could not work, because that continuation runs on the thread pool.
- **Filed in 18.2:** #593, a faulted batch never delivers its remove patches or its disposed handler ids. Its options need an owner decision. Also a comment on #568: the 17.1 cell lines name their commits now, and the 17.1 reds may be stale after the 18.1 and 18.2 harness changes. That is 18.9's audit question.
- **Baselines:** .NET 1406, JVM 206, Android 249, iOS 289.
- **The grading rule** for pins lives in `docs/pin-standard.md`, in the section "Grading a pin: sites and reds".
- **Owner rule (2026-10-25):** no Claude signature anywhere, meaning no `Co-Authored-By` trailer and no "Generated with" line. When merging, strip any signature from the squash message. #596's commits were checked with `scripts/commit-parse-check`: all 35 parse, and none carries a signature.

## Owner decisions for M18 (2026-10-24)

| Issue | Decision |
|---|---|
| #500 | Attribute a fault in a re-render or `RunAfterDispatch` action to the dispatch that queued it. Done in 18.2, PR #596. |
| #506 | Per-key secure-storage queue on Android; check iOS too. |
| #522 | Name the page plus the wrapper position, never a compiler lambda. |
| #543 | Bounded wait plus a Gradle per-test timeout. |

#562, the broad per-statement coverage, is deferred to a later testing milestone.

## Open Decisions (owner)

- #593: deliver a faulted batch's remove patches, or document that its diffs are dropped.
- #575: the frame path's two thread-pool hops.
- #580: iOS cold `LAContext` over 40 s.
- #582: the allowlist for a second-Activity logcat check.
- #444: decouple a show's `Granted` from presentation, because the SpringBoard stall is not ours.
- #401 and #396: the canonical twin behaviour. 18.7's design proposes one.

## Blockers

- None.

## Recommended Next Step

1. ~~Merge #596.~~ **Done:** squash-merged as `d246778f`, now in release PR #597.
2. **18.3, #506:** get the owner's approval of `docs/superpowers/specs/2026-10-31-phase-18.3-design.md`. Then write the plan and execute it subagent-driven. The plan's first task measures whether an Android `BiometricPrompt` answers when its Activity is destroyed or recreated.
3. Lessons from 18.2 still apply:
   - Have every implementer tick its plan checkboxes.
   - Grep for hard-coded test counts early.
   - Expect the staleness rule to force a full re-run of the reds after any late edit to a subject or test file.
4. Watch for a recurrence of #598, the one-off .NET secure-storage timeout on Windows. 18.3 does not touch .NET.
