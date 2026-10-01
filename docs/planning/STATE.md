# Session State

**Last session:** 2026-10-01
**Milestone:** 16 — Unblock the Dispatch Lane `[status: active]`
**Phase:** 16.5 — Audit and close `[status: complete]`, verdict **FAIL** (#456, `965be6c`). Next is 16.6 — Prove the unreddened pins `[status: pending]`.
**Branch:** `chore/16.5-complete-phase` → complete 16.5 and add gap phases 16.6–16.8

## Current Position

- **The audit is written and reviewed:**
  [`docs/plans/2026-10-01-milestone-16-audit.md`](../plans/2026-10-01-milestone-16-audit.md).
  **Verdict: FAIL.** 7 items are MET, 2 MET NARROWLY, 1 NOT MET.
  - **NOT MET, the pin standard:** 16 new pins have no recorded mutation, meaning they were never
    seen red. Most are device pins, including the iOS twin `BnDispatchLaneTests`, the
    `BnFaultNoticeTests` and two `BackAndroidTest` pins. A further 23 pins were never assessed on
    Rules 2–5. They are carried into the gap plan, **not** counted as failing.
  - **MET NARROWLY, all tests passing:** the local `dotnet test` hung in 2 of 5 runs, in a
    pre-M16 docs fixture (#454).
  - **MET NARROWLY, #8:** the rc contract is false for a host call completed inside
    `hostCallBegin` (#455).
  - #425 and #440 were judged against the items they touch: adjacent, not contradicting.
- **The audit was reviewed three times:** a task review, a scoped re-review, and a final
  whole-branch review followed by one fix wave. Every number has its reproducing command beside it.
- **Counts, measured today on HEAD `0779982`:**
  - .NET 1295/0;
  - JVM 191/0, on a win-x64 DLL built today;
  - Android 233/0, run 36846545820;
  - iOS 288/0, run 36846551321.

  Both lanes match HEAD by `headSha`, on attempt 1. The iOS run logged no cold `LAContext` warning
  and no #444 hit.
- **Filed this session:**
  - **#453**, the S1 margin. The owner confirmed acceptance on 2026-10-01.
  - **#454**, the local test hang. Its cause is unconfirmed; the fixture's unbounded wait is a
    certain defect.
  - **#455**, the rc contract gap, plus an unpinned "verbatim" claim in `threading.md`.
- **Local-environment note:** a win-x64 publish needs the VS Installer directory on PATH, for
  `vswhere`. Without it the publish fails and leaves a stale DLL for Gradle.

## Open Decisions (owner)

- **Decided 2026-10-01:** the gap phases are 16.6 (prove the 16 unreddened pins and assess the 23),
  16.7 (#455, change the **code** so the contract holds), and 16.8 (re-audit and close). #454 is
  fixed in a separate bounded-wait PR outside M16.
- #406: tier the 9 untiered public types in api-tiers §8.
- The fenced-code-block advice in the global CLAUDE.md is wrong, because a fenced block does not
  protect a line from release-please's parser.

## Blockers

- None. The only wait is the owner's merge of the complete-phase and gap-phases PR.

## Recommended Next Step

1. Merge the complete-phase PR. Read state from `origin/main` afterwards.
2. Run `start-next-phase` for **16.6**. It routes to brainstorming, since there is no spec yet.
   Device mutations run one at a time per ref, with `headSha` checked; see the sequential-lane
   memory.
3. Separately, and in either order: the #454 bounded-wait PR.
