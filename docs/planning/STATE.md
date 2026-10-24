# Session State

**Last session:** 2026-10-24
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
M17 closed the same day, PASS WITH FINDINGS (#569, #572).
**Phase:** 18.1 — CI flakes at their cause `[status: pending]`.
**Branch:** `chore/m18-start` → start M18

## Current Position

- **M17 is closed** on its [final re-audit](../plans/2026-10-22-milestone-17-reaudit-3.md): 6 items MET, 5 MET NARROWLY (4, 6 #511, 8 #522, 9 #530, 11 #568).
- **The grading rule** for pins now lives in `docs/pin-standard.md`, "Grading a pin: sites and reds". Every later milestone's pins are graded against it.
- **The owner decided on 2026-10-24:**
  - #500: attribute faults to the dispatch that queued the work.
  - #506: a per-key queue.
  - #522: page plus wrapper position.
  - #543: a bounded wait plus a suite timeout.
  - The flakes phase comes first.
  - #562 is deferred to a later testing milestone.

## Open Decisions (owner)

- #401 and #396: each twin divergence's canonical behaviour. 18.7's design proposes one per issue.

## Blockers

- None.

## Recommended Next Step

1. Merge the M18 start PR.
2. Run `start-next-phase` for **18.1**, the CI flakes. Its design finds each flake's cause before any fix.
