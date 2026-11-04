# Session State

**Last session:** 2026-11-04
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.5 — slow-handler naming for compiler lambdas (#522), design approved by the owner on 2026-11-04; plan next. 18.1 to 18.4 are complete and merged; #605 merged as `0b430432` on 2026-11-03 and closed #511 and #601.
**Branch:** `feat/18.5-bind-handler-naming`.

## Current Position

- **18.5 design drafted:** `docs/superpowers/specs/2026-11-04-phase-18.5-design.md`, builder check folded in, approved by the owner on 2026-11-04.
- Owner decisions of 2026-11-04: the position is the control type plus its Razor frame sequence (not an ordinal), and the rule covers every compiler lambda, explicit ones included, not only bound inputs.
- **Baselines:** .NET 1406, JVM 208, Android 271, iOS 296.

## Owner decisions for M18 (2026-10-24)

| Issue | Decision |
|---|---|
| #500 | Attribute a fault in a re-render or `RunAfterDispatch` action to the dispatch that queued it. Done in 18.2, PR #596. |
| #506 | Per-key secure-storage queue on Android; check iOS too. Done in 18.3, PR #599. |
| #522 | Name the page plus the wrapper position, never a compiler lambda. |
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
- #444: decouple a show's `Granted` from presentation, because the SpringBoard stall is not ours.
- #401 and #396: the canonical twin behaviour. 18.7's design proposes one.

## Blockers

- None.

## Recommended Next Step

1. Write the 18.5 plan with writing-plans and execute it subagent-driven.
2. The rules still hold:
   - No Claude signature anywhere.
   - Implementers tick their plan boxes.
   - Grade pins against `docs/pin-standard.md`, in "Grading a pin: sites and reds".
   - Ask questions with AskUserQuestion.
3. Watch, but do not work, the flakes #598, #602, #603 and #604. The M18 DoD needs attempt 1 green on every lane.
