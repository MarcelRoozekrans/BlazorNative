# Milestone 18: Acknowledged Bugs

**Status:** active
**Started:** 2026-10-24

**Design:** [`docs/superpowers/specs/2026-10-24-milestone-18-design.md`](../superpowers/specs/2026-10-24-milestone-18-design.md)
**Predecessor:** Milestone 17 — Close the Dispatch Lane's Debt (complete 2026-10-24, verdict **PASS WITH
FINDINGS**, [final re-audit](../plans/2026-10-22-milestone-17-reaudit-3.md)).
**Closes:** #551, #564, #453, #444, #407, #500, #506, #511, #522, #543, #544, #401, #396, #530, #568.

## Goal

Fix every bug M17 left acknowledged but unfixed, so the dispatch lane, both shells and the test suites behave as their contracts and docs say. This milestone exists because the owner decided on 2026-10-21 that "we need to finish them all".

- **CI comes first.** The Windows CI flakes cost a diagnosis and a re-run on three of M17's last PRs, so the suites get honest before anything else.
- **Then the user-facing bugs.** Fault attribution, secure-storage ordering, a blank recreated Activity, and the slow-handler warning's naming.
- **Then the test-suite hangs and the twin divergences.**
- **Last, the generator's pipe holder.** It is the one investigation whose cause is still unknown.

Every new pin is graded against the written rule in `docs/pin-standard.md`, in the section "Grading a pin: sites and reds". M17 ended with that rule in place.

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing, .NET, JVM **and both device lanes**, with each lane's `headSha` compared against the PR head
- [ ] **The CI flakes are fixed at their cause:** #551, #564, #453, #444 and #407.
  - Each flake's cause is found and fixed. Each fix is pinned where a pin can hold it.
  - No fix raises a bound to hide a slow path.
  - The re-audit's own CI runs hit none of these signatures. Attempt 1 is green on every lane.
- [x] **#500 is fixed as decided.** The owner decided on 2026-10-24 to attribute faults to the dispatch that queued the work.
  - A re-render queued by dispatch A's continuation is tagged with A's scope, as is an action A queues through `RunAfterDispatch`.
  - A fault in that work is A's: a late FaultNotice under A's handler id. The window it runs in keeps its own rc.
  - Both outcomes are pinned and mutation-proven.
- [ ] **#506 is fixed as decided.** The owner decided on 2026-10-24 on a per-key queue.
  - On Android, a secure-storage request on a key waits behind a pending auth-bound request on the same key. Requests on other keys keep flowing.
  - A pin shows a delete or a plain set after an auth set cannot run first. A second pin shows another key is not blocked.
  - iOS is checked for the same hazard, and fixed or pinned to match.
- [ ] **#511 is fixed.** An Activity recreated during boot shows its page, not a blank screen. A device pin proves it.
- [ ] **#522 is fixed as decided.** The owner decided on 2026-10-24 on page plus wrapper position.
  - For a bound input, the slow-handler warning names the page type and the wrapper's position, never a `<…>b__` compiler lambda.
  - The warning still names the app's method wherever one exists.
- [ ] **#543 and #544 are fixed.**
  - **#543**, decided by the owner on 2026-10-24: bounded wait plus suite timeout. A JVM test that waits on a host call joins it with a bound and fails by name. The Gradle test task has a per-test timeout, so no single test can hold the step for 45 minutes.
  - **#544:** the back tests check in their own turn that a back press does not finish the Activity, and S4, finishing on back, reds all five tests.
- [ ] **#401 and #396 are fixed.** Each twin divergence is resolved to one documented behaviour on both shells and pinned by a twin pin.
  - #401 is the stderr pump's level gate.
  - #396 is secure storage on an unenrolled device.
- [ ] **#530 is fixed.** The process that outlives `dotnet publish` and holds the reference generator's output pipe is identified and fixed at its source. The bound from 17.5 still names it if it ever returns.
- [ ] **#568 is fixed.** No register cell cites a stale red without naming it stale.
- [ ] **No ABI change.** The 80-byte bridge struct and the 10 exports are unchanged. Any wire addition goes through `src/wire-vocabulary.json`.
- [ ] **Every new pin conforms to the grading rule.**
  - Its sites, meaning the labelled anchors and name claims, have fresh reds that quote their messages.
  - Its register cell is written in the phase that adds it.
  - Staleness is checked against each red's own commit.

> **No "release tagged in git" criterion.** `CONVENTIONS.md` records `Milestone completion tags a release: no`, and release-please owns releases.

## Phases

1. Phase 18.1 — CI flakes at their cause (#551, #564, #453, #444, #407) [complete] — #564, #551, #453 and #407 fixed at cause; #444 open with instrumentation, SpringBoard stall captured (#584)
2. Phase 18.2 — fault attribution for queued work (#500) [complete]
3. Phase 18.3 — secure-storage per-key ordering (#506) [active]
4. Phase 18.4 — recreation during boot (#511) [pending]
5. Phase 18.5 — slow-handler naming for bound inputs (#522) [pending]
6. Phase 18.6 — test-suite honesty, part two (#543, #544) [pending]
7. Phase 18.7 — twin divergences (#401, #396) [pending]
8. Phase 18.8 — the generator's pipe holder (#530) [pending]
9. Phase 18.9 — audit and close (#568) [pending]

> Flakes go first, by owner decision of 2026-10-24, so every later M18 PR gets clean CI.

## Audit History

| Date | Verdict | Gaps |
|---|---|---|
