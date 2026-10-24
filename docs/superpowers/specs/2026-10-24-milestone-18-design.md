# Milestone 18 Design — `Acknowledged Bugs`

**Date:** 2026-10-24
**Milestone:** `18`
**Stage:** milestone (one milestone, multiple phases)

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
- [ ] **#500 is fixed as decided.** The owner decided on 2026-10-24 to attribute faults to the dispatch that queued the work.
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

1. **Phase 18.1: CI flakes at their cause** — `Surface: Mixed`
   - **Goal:** find and fix the causes of #551 and #564, the Windows .NET flakes, and of #453, #444 and #407, the device flakes, so every later M18 PR gets clean CI.
2. **Phase 18.2: Fault attribution for queued work** — `Surface: Backend`
   - **Goal:** charge a fault in a re-render or `RunAfterDispatch` action to the dispatch that queued it (#500).
3. **Phase 18.3: Secure-storage per-key ordering** — `Surface: Mixed`
   - **Goal:** queue same-key secure-storage requests behind a pending auth on Android, and match on iOS (#506).
4. **Phase 18.4: Recreation during boot** — `Surface: Mixed`
   - **Goal:** an Activity recreated before boot completes shows its page (#511).
5. **Phase 18.5: Slow-handler naming for bound inputs** — `Surface: Backend`
   - **Goal:** name the page and wrapper position instead of a compiler lambda (#522).
6. **Phase 18.6: Test-suite honesty, part two** — `Surface: Infra`
   - **Goal:** add a bounded host-call wait and a suite timeout for the JVM tests (#543), and in-turn `isFinishing` checks for the back tests (#544).
7. **Phase 18.7: Twin divergences** — `Surface: Mixed`
   - **Goal:** give the stderr level gate (#401) and unenrolled-device secure storage (#396) one behaviour on both shells, each with a twin pin.
8. **Phase 18.8: The generator's pipe holder** — `Surface: Infra`
   - **Goal:** identify and fix what holds the reference generator's output pipe after `dotnet publish` (#530).
9. **Phase 18.9: Audit and close** — `Surface: Docs`
   - **Goal:** fix #568's four cells, then audit M18 against this DoD and close it, with no tag.

## Dependencies on Prior Milestones

- **M17 is complete.** It delivered the written grading rule in `docs/pin-standard.md`, the flowing-scope fault attribution that #500 builds on, the per-handler executors that #506 builds on, and the recreation wiring that #511 builds on.
- **Device lanes:** both lanes run on PRs, so each phase's PR proves its own device work.

## External Constraints

- **#530 needs a Windows host to reproduce,** or the Windows CI runner. The local host is Linux.
- **#396 may need a real unenrolled iPhone.** The simulator might not reproduce it. 18.7's design measures this first.
- **#453 and #444 are iOS flakes.** Proving a fix needs repeated runs, so the lane cost is high.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| A flake fix only raises a bound | The flake comes back, or a real hang hides | DoD bars it; each phase design finds the cause first |
| #500's tracking slows the render path | A regression on hot paths | Measure before and after; reuse the flowing scope instead of adding a new one |
| The #506 per-key queue reorders cross-key work | An ordering regression | Pin cross-key independence, and keep #440's begin contract |
| #530's cause is not found | 18.8 stalls | Time-box the investigation; if the cause stays unknown, the owner rules on it |
| Audit loops, as in M17 | Extra rounds | Grade against the written rule only; anything broader goes in #562's scope |

## Open Questions

None blocking. The owner made all four design decisions, #500, #506, #522 and #543, on 2026-10-24, and set the phase order with flakes first. The two twin divergences, #401 and #396, need their canonical behaviour chosen. 18.7's design measures each shell's documented contract and proposes one, for the owner to confirm. #562, the broad per-statement coverage, is deferred to a later testing milestone by owner decision.
