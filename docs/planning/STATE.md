# Session State

**Last session:** 2026-10-31
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.3 — secure-storage per-key ordering (#506) `[status: complete]` on its branch. 18.2 is complete and merged.
**Branch:** `feat/18.3-secure-storage-per-key`, PR #599, ready and awaiting the owner's merge OK.

## Current Position

- **18.3 is complete on `feat/18.3-secure-storage-per-key`.** PR #599 is ready. Its body says "Closes #506." on purpose. After the merge, read state from `origin/main`, not from this branch.
  - Design: `docs/superpowers/specs/2026-10-31-phase-18.3-design.md`.
  - Plan: `docs/superpowers/plans/2026-10-31-phase-18.3-secure-storage-per-key.md`.
  - Record: `docs/plans/2026-10-31-phase-18.3-record.md`.
- **What shipped:** a request that shows a biometric prompt holds its key on both shells, and same-key requests park until it answers. Android denies a pending prompt on a destroy that is not a configuration change, and on a destroyed or stopped Activity. iOS hops the gate's answer onto the work queue.
- **Filed in 18.3:** #600, #601, #602, #603 and #604. #598 is out of scope.
- **18.1 and 18.2 are complete on `main`:** #584 and #596, the latter in release PR #597.
- **Baselines:** .NET 1406, JVM 206, Android 263, iOS 296.
- **The grading rule** for pins lives in `docs/pin-standard.md`, in the section "Grading a pin: sites and reds".
- **Owner rule (2026-10-25):** no Claude signature anywhere, meaning no `Co-Authored-By` trailer and no "Generated with" line. When merging, strip any signature from the squash message. #599's 28 commits were checked with `scripts/commit-parse-check`: all parse, and none carries a signature.

## Owner decisions for M18 (2026-10-24)

| Issue | Decision |
|---|---|
| #500 | Attribute a fault in a re-render or `RunAfterDispatch` action to the dispatch that queued it. Done in 18.2, PR #596. |
| #506 | Per-key secure-storage queue on Android; check iOS too. Done in 18.3, PR #599. |
| #522 | Name the page plus the wrapper position, never a compiler lambda. |
| #543 | Bounded wait plus a Gradle per-test timeout. |

#562, the broad per-statement coverage, is deferred to a later testing milestone.

## Open Decisions (owner)

- #601: a configuration change splits the per-key hold across two bridges. It sits next to #511, so feed it into 18.4's brainstorm.
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

1. **Merge #599** on the owner's OK. It is green and ready. Then read state from `origin/main`.
2. **18.4, #511:** recreation during boot. Brainstorm it first. Take #601 as input, because the per-key hold crosses a recreation there.
3. Lessons from 18.2 and 18.3 still apply:
   - Have every implementer tick its plan checkboxes.
   - Grep for hard-coded test counts early.
   - Expect the staleness rule to force a full re-run of the reds after any late edit to a subject or test file.
4. Watch for a recurrence of #598, the one-off .NET secure-storage timeout on Windows.
