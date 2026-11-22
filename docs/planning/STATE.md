# Session State

**Last session:** 2026-11-22
**Milestone:** 19, 1.0 Readiness and Performance, `[status: active]` since 2026-11-17.
**Phase:** 19.2, Measurement harness, `[status: active]`. The design and the plan are approved
(owner, 2026-11-22). No plan task has started.
**Branch:** `docs/19.2-measurement-harness`. It holds the design, the plan and the start-phase
commit. Another worktree holds that branch name, so a worktree may need to check it out under a local
alias and push with `git push origin HEAD:docs/19.2-measurement-harness`.
**Next step:** execute the plan subagent-driven, starting at Task 1, the build-only check of the
`v0.15.0` backport branch.

## Current Position

- **Design:** `docs/superpowers/specs/2026-11-22-phase-19.2-design.md`, approved 2026-11-22.
- **Plan:** `docs/superpowers/plans/2026-11-22-phase-19.2-measurement-harness.md`, with 11 tasks,
  `## Checks`, and Tasks 9 and 10 marked `**Model:** opus`.
- **Owner rulings, 2026-11-22, in the 19.2 brainstorm:**
  1. Cold boot runs from the kernel's process start to the first applied batch with a `CommitFrame`,
     not to the OS first frame. Both shells boot .NET off the main thread, so `am start -W` and
     `XCTApplicationLaunchMetric` would only see an empty layout. Android also calls
     `reportFullyDrawn()` at that point.
  2. The clicks come from measurement test classes (androidTest and BnHostTests) that the PR lanes
     exclude. No test hook goes into the shipped shell.
  3. HEAD and `v0.15.0` are interleaved in one emulator job and one simulator job, from one dispatch.
     This resolves the M19 design's contradiction between "dispatch per ref" and "interleave".
- **Calls made in the design and plan.** Each is listed in the design's "Calls made" section; the
  owner approved both documents with them:
  - pairing by order in a serial test, because the ABI is frozen;
  - `PerfProbe` boots in every run;
  - a temporary branch-limited `push` trigger on `perf.yml`, removed in Task 11;
  - a `.v015` app-id suffix on the backport branch;
  - the parser is a C# tool, `tools/BlazorNative.PerfReport`, tested in
    `BlazorNative.Runtime.Tests` rather than written in `pwsh`;
  - the marks and `reportFullyDrawn` stay in the product.

## Open Decisions

- None. Task 1 stops and asks the owner only if `v0.15.0` will not build, even with toolchain pins.

## Blockers

- None.

## Recommended Next Step

1. Run `resume-work`, then `start-next-phase`. 19.2 is `active` with a plan, so it routes to
   `list-phase-assumptions` and then `subagent-driven-development`. The owner already approved the
   plan, so the assumptions gate is a confirmation.
2. **Task 1:**
   - cut `perf/v0.15.0-marks` from `v0.15.0`;
   - dispatch its own `android-instrumented.yml` and `ios.yml`;
   - record the run ids in `docs/plans/2026-11-22-phase-19.2-record.md`;
   - confirm the launch paths at the tag: Android `EXTRA_COMPONENT`, and the iOS deep-link map.
3. Open the draft PR at Task 5, the first shell marks. Swift and JVM cannot compile on Linux.
4. Pinned counts at the start of 19.2: .NET 2828, JVM 210, instrumented 273, XCTest 307.
5. Standing rules:
   - no Claude signature or `Co-Authored-By` trailer, and no closing keywords;
   - ask with AskUserQuestion;
   - execution is subagent-driven;
   - never `pkill -f`, and never `&`;
   - CodeRabbit re-reviews every push, so check `reviewDecision`, unresolved threads and its review
     body before calling the PR ready.
