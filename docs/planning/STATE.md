# Session State

**Last session:** 2026-10-02
**Milestone:** 16 — Unblock the Dispatch Lane `[status: active]`
**Phase:** 16.6 — Prove the unreddened pins `[status: complete]`, PR #459 (`a301c9c`). Next is 16.7 — Make the rc contract true (#455) `[status: pending]`.
**Branch:** `chore/16.6-complete-phase` → complete 16.6

## Current Position

- **16.6 is merged.** The record is
  [`docs/plans/2026-10-01-phase-16.6-record.md`](../plans/2026-10-01-phase-16.6-record.md).
  - The population is **38 distinct pins**, from 39 list entries:
    `TheEmittedHostCallOps_MatchTheManifest_InAllThreeLanguages` sat on both of the audit's lists.
  - Every pin has a recorded red on current code. Each mutation ran alone, device mutations ran
    on their own scratch ref, and every device run was checked for `headSha` and attempt 1.
  - Partials are graded "partial, named". A claim that rests on reading the code says so.
  - The register in `docs/pin-standard.md` cites the record, and census row 27a is corrected.
- **About 25 pin defects were found by mutation and fixed in test code,** each shown green before
  the fix and red after. The most important:
  - the caller scan missed a call on its own declaration line;
  - two Kotlin scan roots could silently drop out of the scan;
  - the JVM visibility control depended on overload order;
  - pin 38 had never been run against a production mutation, and its record wrongly credited the
    milestone audit with one. Run B4 now reds it at the harness anchor.
- **Production change:** `BnLog.swift` only, a lock-guarded, nil-checked `emitHookForTest`.
- **Counts on the final tree:**
  - .NET 1295/0;
  - JVM 191/0;
  - iOS 288/0, run 36939708232;
  - Android 233/0, run 36940669280.

  Both lanes ran on `3d0e88c` at attempt 1. Every later commit is docs-only.
- **Filed:** #458, for two pins outside the set with the same bare-message defect, and 8
  `pollUntil` copies to consolidate.
- **Process note:** 16.6 was never set `active` when work began, so `complete-phase` found it
  `pending`. The input was repaired in the complete-phase commit, which says so.

## Open Decisions (owner)

- #406: tier the 9 untiered public types in api-tiers §8.
- The fenced-code-block advice in the global CLAUDE.md is wrong, because a fenced block does not
  protect a line from release-please's parser.

## Blockers

- None. The only wait is the owner's merge of the complete-phase PR.

## Recommended Next Step

1. Merge the complete-phase PR. Read state from `origin/main` afterwards.
2. Run `start-next-phase` for **16.7**. It routes to brainstorming, since there is no spec yet.
   The owner already chose to change the code, not the words: a fault after the first await must
   reach the shell as a FaultNotice even when the host call completed inside `hostCallBegin`.
3. **For 16.8:** the audit's register range `awk 'NR>=396 && NR<=498'` is stale. The register now
   ends at about line 514, so re-measure it.
4. Separately, and in either order: the #454 bounded-wait PR.
