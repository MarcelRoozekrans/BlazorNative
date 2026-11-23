# Session State

**Last session:** 2026-11-23
**Milestone:** 19, 1.0 Readiness and Performance, `[status: active]` since 2026-11-17.
**Phase:** 19.2, Measurement harness, `[status: complete]` on this branch, 2026-11-23. All 11 plan
tasks are done and reviewed. The final whole-branch review's fix wave and its residuals are fixed.
**Branch:** `docs/19.2-measurement-harness`, PR #740, head at the commit that adds this file. Another
worktree holds that branch name, so check it out under a local alias and push with
`git push origin HEAD:docs/19.2-measurement-harness`.
**Backport branch:** `perf/v0.15.0-marks` at fe4a8bf3. It is never merged and has no PR.
**Next step:** get PR #740 through CodeRabbit and merged, then start 19.3.

## Current Position

- **PR #740:** marked ready on 2026-11-23, after the final fix wave. It was `BEHIND` main then.
  CodeRabbit had not reviewed it yet, because it skips drafts.
- **Lanes at 43a3442c:** ci green with .NET 2966, android-instrumented green with 273, and ios green
  with 315. The JVM count is 216.
- **Backport lanes at fe4a8bf3:** android-instrumented green with 226, and ios green with 270. The
  .NET count is 1115, run locally only, because ci.yml at the tag has no `workflow_dispatch`.
- **perf.yml:**
  - It runs on `workflow_dispatch` only; the temporary push trigger is removed.
  - Last full run: 45211320588 at 03379cff, green on all 9 jobs, with 25 click samples per side from
    5 processes and none dropped.
  - Earlier full runs, now void because the workload changed: 45155603687, 45171629275, 45181266672
    and 45190626287.
- **Record:** `docs/plans/2026-11-22-phase-19.2-record.md`. Its "For 19.3" section holds the facts
  19.3 must explain.

## Owner decisions in 19.2, all 2026-11-23

1. iOS cold boot launches PerfProbe through a new shipped env seam, `BN_LAUNCH_COMPONENT`, because
   `simctl openurl` raises the "Open in" sheet on iOS 26. The seam is ported to the backport.
2. The backport installs a background-queued `BnLog.Sink` shaped like HEAD's `BnLogQueue`. Without it,
   v0.15.0 writes its Verbose lines synchronously on the measured thread.
3. Every PerfProbe row tracks the counter, so one click is 51 ReplaceText patches.
4. `BnPerfMarks` is `internal` on HEAD, so it adds nothing to the 1.0 API. The backport copy differs
   only by its access lines.

## Facts for 19.3, from the record

- iOS HEAD dispatch was slow in all 5 click processes in run 45211320588, and v015 was slow in none.
  The macOS runner's load average was 45-318 during that run.
- Android shows no such split.
- Both shells are Debug builds, and .NET is Release NativeAOT.
- The first click of every process is a cold outlier, and it is included in the samples.

## Open Decisions

- None.

## Blockers

- None.

## Filed during 19.2

- #743: iOS `BnImageDemoTests.testCleartextLoopbackIsPermittedByATS` failed once with errno 32 from the
  fixture server. It is unrelated to 19.2.

## Recommended Next Step

1. Wait for CodeRabbit on PR #740. Read `reviewDecision`, the unresolved threads and its review body.
   Fix genuine findings on the branch.
2. Update the branch if it is still behind main, and keep every lane green. Device lanes must pass at
   attempt 1 on the final head.
3. The owner merges, or says "Merged ?", which is a standing approval. After the merge, read the
   milestone state from `origin/main`.
4. Start 19.3, the baseline run and report: run `resume-work`, then `start-next-phase`.
5. Standing rules:
   - no Claude signature or `Co-Authored-By` trailer, and no closing keywords;
   - ask with AskUserQuestion;
   - execution is subagent-driven;
   - never `pkill -f`, and never `&`.
