# Session State

**Last session:** 2026-10-14
**Milestone:** 16 — Unblock the Dispatch Lane `[status: active]`
**Phase:** 16.7 — Make the rc contract true (#455) `[status: complete]`, PR #465 (`dc5b737`). Next is 16.8 — Re-audit and close `[status: pending]`.
**Branch:** `chore/16.7-complete-phase` → complete 16.7

## Current Position

- **16.7 is merged** and shipped in **0.18.0**, whose release notes carry the BREAKING note: a
  fault after a handler began a host call or a fetch is a FaultNotice with rc 0. The record is
  [`docs/plans/2026-10-02-phase-16.7-record.md`](../plans/2026-10-02-phase-16.7-record.md).
  - The fault is classified in the dispatch scope; scheduling is unchanged. The yield-based first
    design deadlocked, raced and broke sync mount, and was abandoned on `feat/16.7-rc-contract`.
  - `DispatchUiEventAsync` rethrows the same fault on the direct path.
  - Counts on the final tree `8a3764f`: .NET 1313, JVM 192, iOS 288 (run 37049520311),
    Android 233 (run 37050704542), both at attempt 1.
- **Filed:** #464, the 16.1 window-attribution defect the phase measured but did not pin.
- 0.18.0 and 0.19.0 are both on nuget.org (`.nupkg` HEAD 200).
- The v0.17.0 GitHub release now carries the "Behaviour changes" rc note from `CHANGELOG.md`,
  added 2026-10-14 with the owner's approval.

## Open Decisions (owner)

- #406: tier the 9 untiered public types in api-tiers §8.
- The fenced-code-block advice in the global CLAUDE.md is wrong, because a fenced block does not
  protect a line from release-please's parser.

## Blockers

- None. The only wait is the owner's merge of the complete-phase PR.

## Recommended Next Step

1. Merge the complete-phase PR. Read state from `origin/main` afterwards.
2. Delete the abandoned `feat/16.7-rc-contract` branch and its SDD workspace only after checking
   nothing on it is unmerged that the record still cites; the record points at `5847d1d`.
3. Run `start-next-phase` for **16.8**, a re-run of `audit-milestone` on live evidence. The audit's
   register range `awk 'NR>=396 && NR<=498'` is stale; the register now ends at about line 514,
   so re-measure it.
4. Separately, and in either order: the #454 bounded-wait PR.
