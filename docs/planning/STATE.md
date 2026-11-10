# Session State

**Last session:** 2026-11-10
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.5 — slow-handler naming for compiler lambdas (#522), `[status: active]`. The design was approved on 2026-11-04 and the plan on 2026-11-10. No task has started yet. 18.1 to 18.4 are complete and merged.
**Branch:** `feat/18.5-bind-handler-naming`.

## Current Position

- **Plan approved 2026-11-10:** `docs/superpowers/plans/2026-11-10-phase-18.5-bind-handler-naming.md`, six tasks. Spec: `docs/superpowers/specs/2026-11-04-phase-18.5-design.md`.
- **Branch** `feat/18.5-bind-handler-naming`, rebased onto `main` at 0.21.2 (`43f11713`). No PR yet; Task 1 Step 6 opens the draft PR, which closes #522.
- **Plan-time decisions by the owner on 2026-11-10:**
  - The JVM probe gains a capturing lambda on a `BnCheckbox`, which covers value-type `EventCallback<T>` reflection under NativeAOT.
  - An IL2075 suppression is allowed only if the trim analyzer fires, paired with the `[DynamicDependency]`, in the style of `BlazorInterop.cs:87-95`. Record it in record §5; the final review checks it.
  - CONVENTIONS.md now records Build and Test: `dotnet build` / `dotnet test` on `BlazorNative.sln -c Release`.
- **Worktree:** the work happens in `.claude/worktrees/bridge-cse_01RwoARgGNqhyJEGJMs6wt4L`, which holds this branch. The main checkout is on `feat/16.7-classify-fault`.
- **Baselines:** .NET 1406, JVM 208, Android 271, iOS 296. The plan moves .NET to 1410 and JVM to 209.

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

1. Execute the 18.5 plan subagent-driven, from Task 1 Step 1. The plan's `list-phase-assumptions` gate is passed: the owner said "Lets go" on 2026-11-10.
2. The rules still hold:
   - No Claude signature anywhere.
   - Implementers tick their plan boxes.
   - Grade pins against `docs/pin-standard.md`, in "Grading a pin: sites and reds".
   - Ask questions with AskUserQuestion.
3. Watch, but do not work, the flakes #598, #602, #603 and #604. The M18 DoD needs attempt 1 green on every lane.
