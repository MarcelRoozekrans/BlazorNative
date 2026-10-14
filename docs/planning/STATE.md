# Session State

**Last session:** 2026-10-14
**Milestone:** 17 — Close the Dispatch Lane's Debt `[status: active]`, started 2026-10-14.
M16 closed the same day, PASS WITH FINDINGS.
**Phase:** 17.1 — Fault attribution (#464, #494) `[status: pending]`, next.
**Branch:** `docs/m17-design` → start M17

## Current Position

- **M16 is closed** on the [16.8 re-audit](../plans/2026-10-14-milestone-16-reaudit.md) of
  `5dde7d3`: 8 items MET, 2 MET NARROWLY.
  - **Item 7** is narrowed by #464, a misattribution that 16.1 introduced. The owner chose to grade
    it rather than block on it.
  - **Item 10** is narrowed by named partials in 16.7's pins, filed as #494. The grade was the
    auditor's call, and the owner accepted it on 2026-10-14.
- Every lane was green on `5dde7d3` at attempt 1: .NET 1316, JVM 192, Android 233, iOS 288.
- **Also shipped today:** `setup.sh`, the Linux and macOS installer, with `setup.ps1`'s drift fixed
  and JDK 25 used everywhere (#490, which closed #404).

## Open Decisions (owner)

- #406: tier the 9 untiered public types in api-tiers §8.
- The fenced-code-block advice in the global CLAUDE.md is wrong, because a fenced block does not
  protect a line from release-please's parser.

## Blockers

- None. The only wait is the owner's merge of the M17 start PR.

## Recommended Next Step

1. Merge the M17 start PR. Read state from `origin/main` afterwards.
2. Run `start-next-phase` for **17.1**. It routes to brainstorming, since there is no phase spec
   yet. The decision is already taken: the flowing scope must match the dispatch's scope at capture
   and at the begun-call mark. A's fault goes to A as a late FaultNotice, and B keeps rc 2. That is
   `fix!`, with a BREAKING note.
3. Housekeeping: decide what happens to `feat/16.7-rc-contract@5847d1d`. The 16.7 record cites it.
