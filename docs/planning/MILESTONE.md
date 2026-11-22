# Milestone 19: 1.0 Readiness and Performance

**Status:** active
**Started:** 2026-11-17

**Design:** [`docs/superpowers/specs/2026-11-17-milestone-19-design.md`](../superpowers/specs/2026-11-17-milestone-19-design.md)
**Predecessor:** Milestone 18 — Acknowledged Bugs (complete 2026-11-17, verdict **PASS WITH FINDINGS**,
[re-audit](../plans/2026-11-16-milestone-18-reaudit.md), close-out #730).
**Carried in from M18:** #575, #621, #600, #593, #711, #582, #716, #717, #562.
**Not carried:** #718 stays open by design as a recurrence watch for #407.

## Goal

Answer one question with evidence: **can BlazorNative be 1.0 today, and if not, exactly what stops it?**

- **Measure.** Cold boot, dispatch-lane latency and frame-apply time on both shells, HEAD against `v0.15.0`, and fix any real regression the numbers show.
- **Re-grade.** Every row of `docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md` against current code, including P3 from a real-iPhone session the owner runs.
- **Clear the carry-over.** The bugs and CI debt left by M18, so the 1.0 decision is not made over known-false promises.

The milestone ends in one of two states: 1.0.0 is published, or the criteria file lists every open blocker with an owner.

Scope comes from the owner's decision of 2026-11-04 and the brainstorm of 2026-11-17. The design records the decisions, the measurement approach (external cold boot plus a `Verbose`-gated timing seam), the regression rule and the other calls.

## Definition of Done

- [ ] All planned phases complete.
- [ ] All tests passing: the required lanes are green on the final head, and the device lanes (Android instrumented, iOS simulator) are green at attempt 1 on that head.
- [ ] **Perf report published** under `docs/plans/`, covering cold boot, dispatch-lane latency and frame-apply time on both shells, HEAD against `v0.15.0`, from CI lanes with at least 20 runs per side per metric, median and spread given, plus real-device numbers from an Android phone and the iPhone. The scripts and the backport branch that produced it are committed, so the run can be repeated.
- [ ] **Every regression the report calls real is fixed and re-measured**, or the owner accepts it in writing on its issue. #12, #13 and #575 each have a data-backed verdict on the issue: fixed, or kept as accepted debt with the measured cost.
- [ ] **The timing marks are silent at `Warn`**, proven both ways like Q1: zero marks at the default and marks present at `Verbose`.
- [ ] **#621, #600 and #593 are closed**, each fixed with a pin that reds without the fix, or, for #593, the drop is documented where consumers read it, if the owner chooses that branch.
- [ ] **#711, #582 and #716 are closed**, and **#717 and #562 are closed or reduced to a written, owner-accepted remainder** on the issue.
- [ ] **P3 is graded** against its five acceptance items from a real-iPhone session, with a record under `docs/plans/`. Any finding from the session is fixed or filed and graded against 1.0.
- [ ] **The criteria file is re-graded** on the final head: every row has a state and evidence dated in M19, the scoreboard is rewritten, and any new blocker found since July is added with its class.
- [ ] **The 1.0 outcome is recorded:** either 1.0.0 is published (a `Release-As: 1.0.0` commit merged by the owner, the release PR merged by the owner, and every package `.nupkg` answering a HEAD request on nuget.org), or the criteria file and STATE.md list every open blocker with its owner, and no tag is cut.

No milestone tag, per `CONVENTIONS.md`: release-please owns releases.

## Phases

1. Phase 19.1 — re-grade the 1.0 criteria [complete]
2. Phase 19.2 — measurement harness [pending]
3. Phase 19.3 — baseline run and report [pending]
4. Phase 19.4 — performance fixes (#575, regressions, #12/#13 verdicts) [pending]
5. Phase 19.5 — Android and batch correctness (#621, #600, #593) [pending]
6. Phase 19.6 — CI and pin hygiene (#711, #582, #716, #717, #562) [pending]
7. Phase 19.7 — real-iPhone session (P3 and iOS real-device perf) [pending]
8. Phase 19.8 — API baseline drain (A5) [pending]
9. Phase 19.9 — the 1.0 decision [pending]

> 19.1 runs first but does not hold back 19.2 to 19.6. 19.2 → 19.3 → 19.4 is a chain. 19.7 waits on 19.2's marks and the owner's session; iOS regression fixes in 19.4 wait on 19.7's confirmation. 19.8 (the A5 drain) runs once 19.2–19.6 have landed their API changes, early enough that a release cycle passes before 19.9; 19.9 goes last.
