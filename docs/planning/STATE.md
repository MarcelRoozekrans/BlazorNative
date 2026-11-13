# Session State

**Last session:** 2026-11-13
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.8 — the generator's pipe holder (#530), `[status: pending]`. Design approved 2026-11-11 and committed; no plan yet. 18.1 to 18.7 are complete.
**Branch:** `feat/18.8-pipe-holder`, from `main` at `ff39e209`. No PR yet.

## Current Position

- **18.7 is merged:** PR #691, squash `ff39e209`, 2026-11-13. #401 and #396 are closed.
  - Every lane was green on attempt 1 on the final head `45e4f1fa`, the update from `main`.
  - CodeRabbit's two findings were fixed in `c3e5f954`, a guard that both stderr targets are emitted, and `49e1b9d9`, which re-ran R18 to R20 and added R23. Record §3.3.
  - #603 recurred once, on `49e1b9d9`'s attempt 1, and is commented there.
  - The merge used `--admin` for the one-approval review rule only, after its stale CodeRabbit changes-requested review was dismissed with a reason.
- **18.8's design is approved:** `docs/superpowers/specs/2026-11-11-phase-18.8-design.md`. The owner chose a Windows experiment over closing on the evidence or moving the publish output to a file.
  - A scratch-ref `windows-latest` workflow, triggered on push to `scratch/530-**`, loops `ci.yml`'s exact .NET test invocation 20 times with the 17.5 node-reuse fix stripped and 20 times with it in.
  - A watcher names the holder of a stalled pipe with `handle.exe` before the bound fires.
  - The fix follows from what the watcher names. If nothing stalls in 40 iterations, the owner rules, and no PR is opened before that.
- **Measured for 18.8, 2026-11-11:**
  - `ci.yml` ran 333 times since 17.5, and its 130 retrievable failed logs have no bound message.
  - Local Linux runs with the fix stripped: 7 runs, no stall, and no MSBuild worker node ever spawned, so Linux cannot test the suspect.
  - A bash `>(...)` probe leaks fd 63 to a cold `VBCSCompiler`. That is an artifact of the probe; use a plain `|`.
- **Baselines:** .NET 2824 (1412 facts on two TFMs), JVM 210, Android 273, iOS 303.

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
- #444 recurred on PR #634's ios lane, attempt 1, on head `05ba2b86`, a PR with no iOS shell change; the final head was green on attempt 1. The M18 DoD needs attempt 1 green on every lane.
- #444: decouple a show's `Granted` from presentation, because the SpringBoard stall is not ours.

## Blockers

- None.

## Recommended Next Step

1. Run `start-next-phase` on `feat/18.8-pipe-holder`. The phase is `pending` with a design and no plan, and Surface is `Infra`, so it routes straight to `superpowers:writing-plans`.
   - The plan's checks come from `CONVENTIONS.md`.
   - Then `list-phase-assumptions`, then subagent-driven execution.
2. The experiment's workflow and the strip commits live on `scratch/530-*` refs only, are never merged, and are deleted after the record is written. Check before deleting.
3. The rules still hold:
   - No Claude signature anywhere.
   - Implementers tick only their own plan boxes, line by line. A broad sed over-ticked other tasks' boxes in 18.6.
   - Grade pins against `docs/pin-standard.md`, in "Grading a pin: sites and reds". Edit every file before mutating, and give this phase's reds a `18.8-` prefix. Run the final review before the reds, so one fix wave precedes one re-run.
   - Ask questions with AskUserQuestion.
   - Never `pkill -f` a pattern; kill by PID. A pattern can match your own shell.
4. Watch, but do not work, the flakes #598, #602, #603, #604 and #676. The M18 DoD needs attempt 1 green on every lane.
