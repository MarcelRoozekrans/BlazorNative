# Session State

**Last session:** 2026-10-15
**Milestone:** 17 — Close the Dispatch Lane's Debt `[status: active]`, started 2026-10-14.
M16 closed the same day, PASS WITH FINDINGS.
**Phase:** 17.2 — Android host-call contract `[status: complete]`, PR #507. Next is 17.3 — Activity recreation (#425) `[status: pending]`.
**Branch:** `chore/17.2-complete-phase` → complete 17.2

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

1. Merge the complete-phase PR.
2. Run `start-next-phase` for **17.3**, #425. Its design must first prove a carrier for the session
   generation that fits the frozen ABI; the fallback is `retire()` closing .NET's gate. Recreation
   is wired into the shell and the template, and navigation history resets.
3. **Owner decisions waiting:**
   - #506, the order of an auth-bound set's after-auth work;
   - #500, per-render fault attribution.
4. **#454 has a lead:** it reproduced on Linux on 2026-10-15. The stalled child is
   `generate-reference.ps1`'s `dotnet publish`, and the dumps are in `~/bn-454-dumps`. That is
   for 17.5.
