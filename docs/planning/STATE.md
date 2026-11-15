# Session State

**Last session:** 2026-11-15
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.11 — audit and close (#568), `[status: active]`. Every plan task is done; PR #713 is ready and waits on the owner.
**Branch:** `feat/18.11-audit`. Base `origin/main` `da6f8c1d`, unchanged. The branch is docs only.
**Next step:** the owner decides item 3's repair, audit §3e; the verdict is FAIL until then. PR #713 still closes #568. `complete-milestone` 18 waits on item 3, with no tag when it comes.

## Current Position

- **18.11 is the audit and close.** Design: `docs/superpowers/specs/2026-11-14-phase-18.11-design.md`. Plan: `docs/superpowers/plans/2026-11-14-phase-18.11-audit.md`. Record: `docs/plans/2026-11-14-phase-18.11-record.md`. Audit: `docs/plans/2026-11-15-milestone-18-audit.md`.
- **Stage 1, fresh reds.** 108 M18 pins with 339 graded sites. 231 stale or unresolvable sites were re-run, each to its own assertion: 115 .NET on both TFMs, 5 JVM through draft PR #714, 80 Android and 31 iOS on scratch refs. An independent sample of 6 reproduced.
- **Register.** 50 M18 cells cite the fresh reds, and #568's three cells are fixed. 39 pre-M18 cells now name their stale reds. Three rows were added for pins that had none.
- **Verdict: FAIL,** 11 MET, 1 MET NARROWLY and 1 NOT MET, after CodeRabbit's review of PR #713.
  - Flakes, item 3, NOT MET: #407's trigger is unproven, so its cause is not found, and #453 leaves #580. The lanes were clean, one sample per lane on `93f886e7`, attempt 1, with no signature hits. The owner decides the repair: find and fix #407's trigger and close #580, or amend the bullet. See the audit §3e.
  - New pins: three register rows were written in 18.11. The owner accepted that on intent on 2026-11-15.
- **Filed:** #716, a stale `[#444]` trace that prints "NO completion" on every passing iOS show since 18.10. #717, the pre-M18 cells that name stale reds and have no fresh ones.
- **Owner decisions, 2026-11-14 and 2026-11-15:**
  - re-run M18 pins only;
  - one PR in two stages;
  - item 13 read on intent;
  - `complete-milestone` as its own follow-up PR.
- **Lesson of the phase:** four "no subject-side red exists" arguments were false: batch A rows 16 and 20, batch B rows 144 and 147, Android rows 191 and 194, and .NET row 323. Each fell to a product-side mutation that a reviewer found. Treat such an argument as a claim that needs an attempt behind every premise.
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

1. The owner merges PR #713. On "Merged ?", check `reviewDecision` and unresolved review threads as well as the checks. Then read state from `origin/main`.
2. Run `complete-phase` for 18.11 and `complete-milestone` for M18 as one small bookkeeping PR. No tag.
3. Then the post-M18 milestone the owner decided on 2026-11-04: 1.0 readiness and a perf baseline.
4. The rules still hold:
   - No Claude signature anywhere, in commits or in the PR body.
   - Ask with AskUserQuestion.
   - Execution is subagent-driven.
   - Never `pkill -f`.
