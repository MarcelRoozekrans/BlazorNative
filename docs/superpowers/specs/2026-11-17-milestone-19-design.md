# Milestone 19 Design — `1.0 Readiness and Performance`

**Date:** 2026-11-17
**Milestone:** `19`
**Stage:** milestone (one milestone, multiple phases)
**Status:** approved 2026-11-17

**Predecessor:** Milestone 18, Acknowledged Bugs. It completed on 2026-11-17 with the verdict
PASS WITH FINDINGS; the close-out merged as `9e1ee784` (#730).
**Scope source:** the owner's decision of 2026-11-04, recorded in STATE.md, plus the owner's
answers in this brainstorm (2026-11-17).
**Carried in from M18:** #575, #621, #600, #593, #711, #582, #716, #717, #562.
**Not carried:** #718 stays open by design as a recurrence watch for #407.

## Goal

Answer one question with evidence: **can BlazorNative be 1.0 today, and if not, exactly what stops
it?** M19 does three things to get there. It measures how fast the framework is on both shells
(cold boot, dispatch-lane latency, frame-apply time) against an older release, and fixes any real
regression the numbers show. It re-grades every row of the 1.0 criteria file against current code,
including P3, which needs a real iPhone session. It also clears the bugs and CI debt carried over
from M18, so the 1.0 decision is not made over known-false promises. The milestone ends in one of
two states: 1.0.0 is published, or the criteria file lists every open blocker with an owner.

## Why now

The criteria file (`docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md`) was last updated on
2026-07-24. Its scoreboard still lists four open blockers, three of which (#209, #210, #211) have
since been closed, along with the DOCUMENTED rows Q4 and Q5 (#169, #181, #178). Seven milestones of
work (M12 to M18) have landed since, so nobody can currently say from the file whether 1.0 is close.
Nothing in the repo has ever measured performance. M16 to M18 reworked the dispatch path (one render
thread, fault classification, thread hops), which is where latency would move if it moved.

## Decisions taken in the brainstorm (owner, 2026-11-17)

1. **Perf goal: record and fix.** Measure, publish the comparison, and fix any real regression it
   shows inside M19. Re-judge the accepted perf debt #12 and #13, and #575, on the data. **No CI
   budget gate**: performance-budget enforcement stays in the Backlog.
2. **Measurement approach A: external cold boot plus a small timing seam.**
   - Cold boot is measured from **outside** the app, so an old release and HEAD are timed by the
     same ruler with no product change: Android `am start -W` (launch to first frame) and the iOS
     launch-to-first-frame metric from XCTest (`XCTApplicationLaunchMetric`) or `simctl` launch
     timing.
   - Dispatch-lane latency and frame-apply time have no external view. They get a few timing marks
     inside the product, emitted only at `Verbose` through the existing log-level gate
     (`BnLog` in .NET, `BnShellLog` / `EXTRA_LOG_LEVEL` on Android, the iOS equivalent). At the
     default `Warn` they print nothing, so criterion Q1 stays true.
   - The same marks are cherry-picked onto a branch cut from the comparison tag, so both sides
     report the same events.
   - Runs happen on the CI device lanes (Android emulator, iOS simulator), many runs per side,
     reported as median plus spread. A real-device pass is added on an Android phone and, during
     the P3 session, on the iPhone.
   - Rejected: all-in-app timers (the in-app clock misses process start and runtime init, exactly
     where a NativeAOT or .NET 11 change shows), and profiler traces only (no named span for
     dispatch or frame-apply, manual, not repeatable).
3. **Comparison release: `v0.15.0`** (2026-09-22). It is just before M16 to M18's dispatch work and
   recent enough that its toolchain should still build. HEAD is on .NET 11 (SDK 11.0.100) and
   v0.15.0 on .NET 10 (SDK 10.0.401), so the comparison also measures the runtime change. The
   report must say so and not attribute a runtime-caused delta to framework code.
4. **All nine carried-over issues are in scope**: #575, #621, #600, #593, #711, #582, #716, #717,
   #562.
5. **P3 is worked inside M19**: a phase where the owner runs P3's seven acceptance items on a real
   iPhone. The iOS real-device perf numbers come from that same session.
6. **The owner runs the iPhone session and has the Apple Developer account ready.** 19.7 delivers a
   signing and staging script plus a step-by-step session checklist. The session is scheduled as
   soon as 19.2's timing marks land. The plan targets "1.0.0 published" as the expected outcome.

## Goals, non-goals, anti-goals

- **Goals:** a reproducible perf comparison on both shells; regressions fixed; the criteria file
  re-graded row by row with fresh evidence; carried-over bugs fixed; a 1.0 cut or a blocker list.
- **Non-goals:** CI performance budgets; new capabilities (FCM, haptics, purchases, background tasks
  stay R4 DOCUMENTED); the consumer test harness #25 (R3 DOCUMENTED); the S3 `internal` move.
- **Anti-goals (a failure even if it technically works):**
  - A 1.0 tag cut while any BLOCKER row is false, or while a row's evidence predates the code it
    judges.
  - A "regression" or "improvement" claimed inside measurement noise.
  - Timing marks that print at the default log level, which would falsify Q1.
  - P3 graded MET from the simulator, or from the September contributor run alone without checking
    it against the seven items.

## Definition of Done

- [ ] All planned phases complete.
- [ ] All tests passing: the required lanes are green on the final head, and the device lanes
      (Android instrumented, iOS simulator) are green at attempt 1 on that head.
- [ ] **Perf report published** under `docs/plans/`, covering cold boot, dispatch-lane latency and
      frame-apply time on both shells, HEAD against `v0.15.0`, from CI lanes with at least 20 runs
      per side per metric, median and spread given. It also carries the real-device numbers from an
      Android phone and the iPhone. The scripts and the backport branch that produced it are
      committed, so the run can be repeated.
- [ ] **Every regression the report calls real is fixed and re-measured**, or the owner accepts it in
      writing on its issue. #12, #13 and #575 each have a data-backed verdict on the issue: fixed, or
      kept as accepted debt with the measured cost.
- [ ] **The timing marks are silent at `Warn`**, proven both ways like Q1: zero marks at the default
      and marks present at `Verbose`.
- [ ] **#621, #600 and #593 are closed**, each fixed with a pin that reds without the fix, or, for
      #593, the drop is documented where consumers read it, if the owner chooses that branch.
- [ ] **#711, #582 and #716 are closed**, and **#717 and #562 are closed or reduced to a written,
      owner-accepted remainder** on the issue.
- [ ] **P3 is graded** against its seven acceptance items from a real-iPhone session, with a record
      under `docs/plans/`. Any finding from the session is fixed or filed and graded against 1.0.
- [ ] **The criteria file is re-graded** on the final head: every row has a state and evidence dated
      in M19, the scoreboard is rewritten, and any new blocker found since July is added with its
      class.
- [ ] **The 1.0 outcome is recorded:** either 1.0.0 is published (a `Release-As: 1.0.0` commit
      merged by the owner, the release PR merged by the owner, and every package `.nupkg` answering
      a HEAD request on nuget.org), or the criteria file and STATE.md list every open blocker with
      its owner, and no tag is cut.

No milestone tag line: `CONVENTIONS.md` says `Milestone completion tags a release: no`, and
release-please owns releases.

## Phases

1. **Phase 19.1: Re-grade the 1.0 criteria** — `Surface: Docs`
   - **Goal:** Re-check every row of the criteria file against HEAD, sweep the issues filed since
     2026-07-24 for new blocker candidates, and publish an interim scoreboard that tells the rest of
     M19 what actually blocks 1.0.
2. **Phase 19.2: Measurement harness** — `Surface: Infra`
   - **Goal:** Add the Verbose-gated timing marks on both shells and in .NET, the external cold-boot
     scripts, the CI jobs that collect N runs, and the `v0.15.0` backport branch that carries the
     same marks; prove the marks silent at `Warn`.
3. **Phase 19.3: Baseline run and report** — `Surface: Docs`
   - **Goal:** Run HEAD and `v0.15.0` on the emulator and simulator lanes and an Android phone,
     publish the report, and name each real regression and the verdict data for #12, #13 and #575.
     iOS verdicts stay provisional until 19.7's real-device pass confirms them, because the
     regression rule needs the real-device direction.
4. **Phase 19.4: Performance fixes** — `Surface: Backend`
   - **Goal:** For each regression 19.3 called real, either fix it and re-measure with the same
     harness, or record the owner's written acceptance on its issue. Record the verdicts on #12,
     #13 and #575. Each one is either fixed, or kept as accepted debt with its
     measured cost written on the issue by the owner. An iOS regression is worked only after 19.7
     confirms it on the iPhone.
5. **Phase 19.5: Android and batch correctness** — `Surface: Backend`
   - **Goal:** Fix #621 (orphaned secure-storage prompt denied by another instance's destroy) and
     #600 (biometrics completion on a stopped Activity, pin the behaviour), each with a pin that
     reds without the fix. Resolve #593 (a faulted batch's remove patches) the way the owner
     chooses in this phase: fixed with such a pin, or the drop documented where consumers read
     it.
6. **Phase 19.6: CI and pin hygiene** — `Surface: Infra`
   - **Goal:** Close #711, #582 and #716, and work #717 and #562 down to closed or an owner-accepted
     remainder.
7. **Phase 19.7: Real-iPhone session** — `Surface: Infra`
   - **Goal:** With a signed device build, run P3's seven acceptance items and the iOS real-device
     perf pass on a real iPhone, record both, and fix or file every finding.
8. **Phase 19.8: The 1.0 decision** — `Surface: Docs`
   - **Goal:** Re-grade the criteria file on the final head, audit M19, and then either cut 1.0.0
     through release-please with the owner, or record the blocker list.

Ordering: 19.1 goes first because it can add blockers that change later phases. 19.2 to 19.4 are a
chain. 19.5 and 19.6 are independent of the perf chain and of each other. 19.7 waits only on the
owner's device availability and on 19.2 (the marks must exist for the perf pass); it can run as soon
as both are ready. iOS regression fixes in 19.4 wait on 19.7's confirmation; Android ones do not.
19.8 goes last.

## Dependencies on Prior Milestones

- M16 to M18's dispatch, fault and thread-hop work is what the perf comparison measures against
  `v0.15.0`.
- M14's `ios-arm64` device-slice build (Phase 14.4) is the starting point for a signed iPhone build.
- M11's criteria file and its Q1 paired-check technique are reused for the "marks are silent"
  criterion.
- The pin standard from M15 and the staleness rules from M17/M18 apply to every new pin.

## External Constraints

- **A real iPhone and an Apple Developer account for signing** (P3, 19.7). The owner has both and runs the session; the
  session is scheduled once 19.2 lands. CI has no attached iPhone.
- **An Android phone** for the real-device perf pass, also the owner's.
- **Device lanes run on PRs against `main` and on `workflow_dispatch`**, one run at a time per ref (`ios.yml` cancels in
  progress). Collecting 20+ runs per side per metric costs lane time; the harness must batch runs
  inside one lane job, not one job per run.
- **Swift and the JVM cannot compile on Linux**, so shell-side marks need a draft PR early to get
  feedback from macOS and Gradle lanes.
- **Cutting 1.0.0** is the owner's act: the `Release-As: 1.0.0` commit and the release-PR merge.
- **Toolchain drift on `v0.15.0`:** the old tag must still build on current runners (macos-latest
  Xcode, Gradle, .NET 10.0.401 SDK).

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| CI emulator and simulator timing is noisy on shared runners | Regressions invented or hidden | At least 20 runs per side in the same lane job, interleaved old/new, median plus spread; call a regression only when the medians differ by more than the spread. The real-device pass corroborates. |
| `v0.15.0` no longer builds on current runners | No comparison at all | 19.2 builds the backport branch first; if it cannot be built, stop and ask the owner for another release rather than silently swapping it. |
| .NET 10 to 11 confounds the comparison | A runtime delta blamed on framework code | The report separates cold boot (runtime-dominated) from dispatch and frame-apply, and names the SDK of each side. Where it matters, one extra run of HEAD's code on .NET 10 is the builder's call. |
| Timing marks change what they measure, or leak at `Warn` | Wrong numbers, or Q1 falsified | Marks are a timestamp plus a log call behind the gate; the paired Warn/Verbose check is a DoD item. |
| The iPhone session slips or finds a blocker | 1.0 cannot be cut in M19 | The owner runs it once 19.2 lands; if it cannot happen or a finding blocks, 19.8 records P3 as the open blocker, which is still a valid M19 outcome. |
| 19.1 finds new blockers | M19 grows | New blockers are added to the criteria file in 19.1; the owner decides whether each is fixed in M19 (a new phase) or left listed. |
| #717 / #562 are large bookkeeping | 19.6 balloons | The DoD allows an owner-accepted remainder written on the issue. |

## Approaches

- **Measurement:** A (external cold boot plus a Verbose-gated seam), picked by the owner. B (all
  in-app) and C (profiler traces only) were rejected for the reasons in Decision 2.
- **Comparison release:** `v0.15.0`, picked by the owner over `v0.5.1` (widest span, highest
  rebuild risk), `v0.20.0` (smallest diff) and two baselines (double the runs).
- **Perf goal:** record and fix, picked by the owner over record-only and record plus CI budgets.

## Left to the builder (my calls, correct them if wrong)

- The exact mark points for "dispatch-lane latency" (proposal: from the shell posting an event to
  the handler starting on the render thread) and "frame-apply time" (proposal: from the shell
  receiving a frame to the last patch applied on the UI thread). 19.2's design fixes them.
- Which sample page is measured: the in-repo `samples/BlazorNative.SampleApp`, the same page on both
  sides.
- The run count above 20 and the exact spread statistic (proposal: IQR).
- How the marks are collected (logcat / unified log scraped by the lane job).

## Calls made after the builder check (mine, correct them if wrong)

- **Where the runs happen.** The measurement runs in a new `workflow_dispatch` workflow that reuses
  the device-lane setup of `ios.yml` and `android-instrumented.yml`. Both already run on PRs
  against `main`, and adding 20+ timed runs to them would slow every PR. The new workflow is
  dispatched on the HEAD ref and on the `v0.15.0` backport branch.
- **What counts as a real regression.** HEAD is worse than `v0.15.0` when HEAD's median exceeds
  the old median by more than the larger of the two IQRs. This must hold on the CI lane and point
  the same way on the real-device pass. Anything smaller is reported as "no measurable change".
- **Runtime-attributable deltas.** A cold-boot regression is not called a framework regression
  until HEAD's code has also been measured on .NET 10. That extra run is mandatory only when a
  cold-boot regression appears. A delta that the .NET 10 run shows is the runtime's own is
  reported, not fixed, and named as such in the report.
- **The fix loop is bounded.** Each regression gets one fix-and-remeasure round in 19.4. A
  regression still present after that round needs the owner's written acceptance on its issue, or
  a new phase the owner adds. It never loops silently.
- **If `v0.15.0` won't build:** pinning an older runner image, Xcode or Gradle for the backport
  branch is acceptable, because that is measurement infrastructure, not product. If it still won't
  build, stop and ask the owner for another release. Never swap the release silently.
  The first task of 19.2 is a build-only check of the backport branch.
- **#593: fix or document.** This is a behaviour choice, so it is made in 19.5's phase brainstorm
  with the owner, on the evidence from that phase.
- **#717 / #562 remainder:** 19.6's design proposes the bar, and the owner writes the acceptance
  on the issue.
- **Phase gating.** 19.1 runs first, but it does not hold back 19.2 to 19.6, which may start
  without waiting. A blocker 19.1 adds becomes a new phase only on the owner's word. 19.8 waits for
  every other phase.

## Open Questions

None for the owner at this point.
