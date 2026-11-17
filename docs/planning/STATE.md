# Session State

**Last session:** 2026-11-17
**Milestone:** 19, 1.0 Readiness and Performance, `[status: active]` since 2026-11-17. M18 closed with PASS WITH FINDINGS; its close-out #730 merged as `9e1ee784`.
**Phase:** none started. All eight phases (19.1 to 19.8) are `[status: pending]` with no design or plan yet.
**Next step:**
1. The owner merges the milestone-start PR from `chore/start-m19`.
2. Run `start-next-phase`. It routes to Phase 19.1, re-grade the 1.0 criteria, which starts with a phase-scope brainstorm.

## How M19 was scoped (brainstorm, 2026-11-17)

Design: `docs/superpowers/specs/2026-11-17-milestone-19-design.md`, approved by the owner on 2026-11-17. The owner's answers:

- **Perf goal: record and fix.** Measure, publish, and fix real regressions in M19. Re-judge #12, #13 and #575 on the data. No CI budget gate.
- **Measurement approach A:**
  - cold boot is timed from outside the app (`am start -W`, the XCTest launch metric);
  - dispatch-lane latency and frame-apply time come from a few `Verbose`-gated marks, which must be silent at `Warn` so Q1 stays true;
  - the marks are cherry-picked onto a backport branch cut from the comparison tag.
- **Comparison release: `v0.15.0`.** It runs on .NET 10 and HEAD on .NET 11; the report must separate runtime deltas from framework deltas.
- **All nine carry-overs are in:** #575, #621, #600, #593, #711, #582, #716, #717, #562. #718 stays open as a recurrence watch.
- **P3 is graded in M19.** The owner runs the real-iPhone session and has the Apple Developer account ready. It is scheduled once 19.2's marks land.

My calls, written into the design's "Calls made after the builder check" section, for the owner to correct:

- the measurement runs in a separate `workflow_dispatch` workflow;
- a regression counts only when HEAD's median exceeds the old median by more than the larger IQR, and the real-device run points the same way;
- a cold-boot regression needs a HEAD-on-.NET-10 run before it is blamed on the framework;
- each regression gets one fix round, then the owner's acceptance or a new phase;
- for `v0.15.0`, pinning an older runner or toolchain is fine; if it still won't build, stop and ask the owner;
- #593 fix vs document is decided in 19.5;
- the #717/#562 remainder bar is set in 19.6;
- 19.1 runs first but does not hold back 19.2 to 19.6.

## Open for the owner

- Merge the milestone-start PR.

## Blockers

- None.

## Recommended Next Step

1. On "Merged ?" for the milestone-start PR, check `reviewDecision` and unresolved threads, not only the checks. Read CodeRabbit's review body even on APPROVED.
2. Then `start-next-phase` → Phase 19.1 brainstorm. The criteria file is stale since 2026-07-24. Known since then:
   - #209, #210, #211, #169, #181, #178, #8, #9, #16 and #23 are closed;
   - #17 (P3), #12, #13, #18 and #25 are open;
   - a contributor ran on a real iPhone in September and found #338 and #339.
3. The standing rules still apply:
   - No Claude signature and no closing keywords unless intended.
   - Ask with AskUserQuestion.
   - Execution is subagent-driven.
   - Never `pkill -f`, and never `&`.
   - JVM pins need a draft PR, because of #491.
   - Swift and JVM code cannot compile on Linux, so 19.2's shell marks need a draft PR early.
