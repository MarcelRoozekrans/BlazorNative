# Session State

**Last session:** 2026-10-01
**Milestone:** 16 — Unblock the Dispatch Lane `[status: active]`
**Phase:** 16.4 — The lost first tap (#438) `[status: active]`, all tasks done except the post-merge step
**Branch:** `feat/16.4-lost-first-tap` → **PR #449**, open, not merged

## Current Position

- **16.0 to 16.3 are complete on `main`.** This file was two phases stale before this session.
- **16.4 is implemented, verified and in review as PR #449.** Both root causes of #438 are fixed and
  pinned. S2 was a race in the boot-test harness. S1 was a cold `LAContext()` inside
  `hostCallBegin`, which runs on the render thread. Neither is an M16 regression: the pre-M16
  baseline on `8fadff1` failed S1 once in ten runs.
- **This session closed the plan's remaining gaps.**
  - The pre-M16 baseline went into the record.
  - The four mutations, plus R, were run one at a time, and the vacuity contrast was observed in
    three steps. That brings Rule 7 into conformance.
  - The final review found 2 important and 5 minor findings. All were fixed in `106fce0` or are
    tracked.
  - 10 of 10 sequential iOS runs were green on `a65097f`, at 288/0, with `headSha` checked for
    each. android-instrumented was green on the same head.
  - The ROADMAP 16.4 outcome block is written.
- **Counts:** .NET 1295 · JVM 191 · Android 233 · **iOS 288**, up from 282.
- **Cleanup is done.** Every `scratch/16.4-*` branch is deleted, on the remote and locally, and the
  mutation worktree is removed.
- **Filed this session:** #444, a pre-M16 iOS flake in `BnNotificationsTests`' show test.
  **Linked:** #440, Android's broken begin contract, which was filed earlier but never linked from
  the record.

## Open Decisions (owner)

- **PR #449:** review and merge. Nothing was merged overnight.
- **The accepted S1 residual, re-measured.** 2 of the 10 runs logged a cold `LAContext` creation, at
  6453 ms and 25681 ms. The second is 4.3 s inside the boot tests' 30 s wait. The owner accepted
  this risk in Task 3; record section 10 asks for that ruling to be confirmed or revisited with this
  data.
- #406: tier the 9 untiered public types in api-tiers §8.
- The fenced-code-block advice in the global CLAUDE.md is wrong, because a fenced block does not
  protect a line from release-please's parser.

## Blockers

- None. The only wait is the merge of #449.

## Recommended Next Step

1. Check PR #449's checks on its head, comparing `headSha`, and merge it if green. The merge is the
   owner's call.
2. After the merge, run plan Task 4 Step 4. Close #438 with an evidence comment pointing at record
   sections 8 to 10, and confirm the commit appears in the release-please PR.
3. Run `complete-phase` for 16.4, reading `main`'s state after the merge. Then run
   `start-next-phase` for **16.5, the audit**. Expect the DoD "tests" item to depend on #444's
   flake rate as well as #438.
