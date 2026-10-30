# Session State

**Last session:** 2026-10-30
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.1 — CI flakes `[status: complete]`, PR #584. Next is **18.2 — fault attribution for queued work (#500)** `[status: pending]`.
**Branch:** `chore/18.1-complete-phase` → complete 18.1 and hand off.

## Current Position

- **18.1 is merged** (#584, `5a7ab54a`):
  - #564 is fixed: `BnLog` now uses a non-blocking sink.
  - #551 is fixed: process waits stay off the test thread pool.
  - #453 is fixed: the iOS shell warms `LAContext` at boot.
  - #407 is fixed: the camera test names a recreation, and an emulator settle script runs first.
- **#444 stays open** by owner decision. On 2026-10-28 the instrumentation caught a hit: the simulator's SpringBoard delayed `willPresent` delivery by 11 s, and the app answered within 50 ms. The details are on the issue.
- **Baselines:** .NET 1387, JVM 206, Android 249, iOS 289.
- **The grading rule** for pins lives in `docs/pin-standard.md`, in the section "Grading a pin: sites and reds". A red is stale once its files change after the red's own commit.
- **Owner rule (2026-10-25):** no Claude signature anywhere, meaning no `Co-Authored-By` trailer and no "Generated with" line. When merging, strip any signature from the squash message.

## Owner decisions for M18 (2026-10-24)

| Issue | Decision |
|---|---|
| #500 | Attribute a fault in a re-render or `RunAfterDispatch` action to the dispatch that queued it, using the flowing scope #464 used. |
| #506 | Per-key secure-storage queue on Android; check iOS too. |
| #522 | Name the page plus the wrapper position, never a compiler lambda. |
| #543 | Bounded wait plus a Gradle per-test timeout. |

#562, the broad per-statement coverage, is deferred to a later testing milestone.

## Open Decisions (owner)

- #575: the frame path's two thread-pool hops.
- #580: iOS cold `LAContext` over 40 s.
- #582: the allowlist for a second-Activity logcat check.
- #444: decouple a show's `Granted` from presentation, because the SpringBoard stall is not ours.
- #401 and #396: the canonical twin behaviour. 18.7's design proposes one.

## Blockers

- None.

## Recommended Next Step

1. Merge the 18.1 complete-phase PR.
2. Run `start-next-phase` for **18.2, #500**. Brainstorm the design: tag each queued re-render and each `RunAfterDispatch` action with the queuing dispatch's flowing scope, so a fault becomes A's late FaultNotice and B keeps its rc. Pin both outcomes with mutation proof.
3. Execute subagent-driven. That is the owner's standing preference.
