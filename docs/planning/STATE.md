# Session State

**Last session:** 2026-11-15
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.12, #407's relaunch trigger at its cause (#718) plus the #580 decision, `[status: active]`. The design is approved and the plan is approved; execution has not started.
**Branch:** `feat/18.12-relaunch-trigger`, head `e5a3116e` plus the start-phase commit. No PR yet. Another worktree holds the branch, so push with `git push origin HEAD:feat/18.12-relaunch-trigger`.
**Next step:** execute the plan, subagent-driven, from Task 1.

## 18.12 handoff

- **Design:** `docs/superpowers/specs/2026-11-15-phase-18.12-design.md`, approved 2026-11-15.
- **Plan:** `docs/superpowers/plans/2026-11-15-phase-18.12-relaunch-trigger.md`. It has 8 tasks; Tasks 1 and 7 are marked `**Model:** opus`, and Task 3 is the coordinator's own.
- **Owner decisions, 2026-11-15:**
  - **"Found" needs both a forced and a natural reproduction.** A trace event must force the relaunch: PermissionController's uid flag update, the SdkSandbox reconcile, GMS `PACKAGE_CHANGED`, or a fresh install's delayed processing fired at install time. A natural recurrence in the 10-run loop also counts. Mechanism probes (overlay re-evaluation, `install -r --dont-kill`) are supporting evidence only.
  - **If nothing forces it,** stop at Task 3 and ask the owner before writing any fix.
  - **The fix is a wait in the test runner,** in `BlazorNativeTestRunner.onStart` through a new `PostInstallSettle`. It waits only on signals the found trigger moves. If the probes isolate a single broadcaster, the settle script also stops that source.
  - **#580: investigate the cause first.** Up to 10 fresh-boot iOS runs with LocalAuthentication and coreauthd logs. Fix it at the cause if one is found; otherwise record the evidence and close #580 as accepted.
- **The calls I made:**
  - 10 runs for the natural loop and 10 for #580;
  - the trigger proof is a red/green pair of scratch lane runs, fired by an `adb` watcher, while the merged pins cover the wait logic;
  - the Android job's 45-minute timeout is the time budget.

## Current Position

- **The 18.11 artefacts:**
  - Design: `docs/superpowers/specs/2026-11-14-phase-18.11-design.md`.
  - Plan: `docs/superpowers/plans/2026-11-14-phase-18.11-audit.md`.
  - Record: `docs/plans/2026-11-14-phase-18.11-record.md`.
  - Audit: `docs/plans/2026-11-15-milestone-18-audit.md`.
- **Stage 1, fresh reds.** There are 108 M18 pins with 339 graded sites. 231 of the sites were stale or unresolvable, and each was re-run to its own assertion:
  - 115 .NET, on both TFMs;
  - 5 JVM, through draft PR #714;
  - 80 Android and 31 iOS, on scratch refs.

  An independent sample of 6 reproduced.
- **Register.** 50 M18 cells cite fresh reds, and #568's four cells are traced. 39 pre-M18 cells now name their stale reds. Three rows were added for pins that had none.
- **Verdict: FAIL.** 11 items are MET, 1 MET NARROWLY and 1 NOT MET.
  - **Item 3 is NOT MET.** #407's trigger was never found; 18.1 only made the failure name itself. #453 also leaves #580 open. CodeRabbit raised the contradiction on PR #713.
  - **Item 13 is MET NARROWLY.** Three register rows were written in 18.11, not in the phase that added them. The owner accepted this on intent on 2026-11-15.
- **The owner's choice, 2026-11-15:** a gap phase, then a re-audit.
  - **18.12** finds and fixes #407's relaunch trigger, filed as #718, and decides #580. The audit's best lead: the relaunch coincided with the install's own delayed package broadcasts, which the settle step does not cover, because it runs before the install.
  - **18.13** re-audits, as 17.12 did, and runs `complete-milestone` if the result is a pass.
- **Filed:**
  - #716: a stale `[#444]` trace that prints "NO completion" on every passing iOS show.
  - #717: pre-M18 cells that name stale reds and have no fresh ones.
  - #718: #407's trigger.
- **Owner decisions:**
  - re-run M18 pins only;
  - one PR in two stages;
  - read item 13 on intent;
  - `complete-milestone` gets its own PR;
  - item 3 is repaired at its cause, not amended.
- **Lesson of the phase:** four arguments that "no subject-side red exists" were false: rows 16 and 20, 144 and 147, 191 and 194, and 323. In each case a reviewer found a product-side mutation. Treat such an argument as a claim, and put an attempt behind every premise.
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

1. Execute the **18.12** plan, subagent-driven, from Task 1. The questions below are now answered in the design:
   - **#407's trigger.** In #407's one occurrence, `MainActivity` relaunched about 2 s after the APK install, with no "Config changes" line. That was within about 0.1 s of the install's own delayed package broadcasts: a PermissionController uid update, SdkSandboxManager reconciling the app and its test package, and GMS `PACKAGE_CHANGED`. `scripts/android-emulator-settle.sh` runs before Gradle installs the APKs, so it does not cover that window. Reproduce the relaunch first, then fix the wait condition. Do not raise a bound. The 18.1 record §5 has the full trace.
   - **#580.** On iOS, a cold `LAContext` start that takes over 40 s still fails the boot tests' named warm-up step. #453's fix left this open. Decide it inside 18.12, as the owner chose on 2026-11-15: fix it at its cause, or record why not.
2. Then **18.13** re-audits M18 against its DoD, as 17.12 did after 17.11. Use the 18.11 audit (`docs/plans/2026-11-15-milestone-18-audit.md`) as the model. If it passes, `complete-milestone` follows as its own small PR, with no tag.
3. The rules still hold:
   - No Claude signature anywhere, in commits or in the PR body.
   - Ask with AskUserQuestion, with one option marked recommended.
   - Execution is subagent-driven.
   - Write PR bodies to the template from the start.
   - Never `pkill -f`.
   - Swift cannot compile on this Linux host.
   - JVM pins need a draft PR, because JNA cannot load the win-x64 dll (#491).
   - On "Merged ?", check `reviewDecision` and unresolved threads, not only the checks.
   - Brief implementers: a "no subject-side red exists" claim needs at least two subject-side attempts behind it. Four such claims were false in 18.11.
