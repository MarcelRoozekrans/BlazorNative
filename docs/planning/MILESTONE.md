# Milestone 16: Unblock the Dispatch Lane

**Status:** active
**Started:** 2026-09-26

**Design:** [`docs/superpowers/specs/2026-09-26-milestone-16-design.md`](../superpowers/specs/2026-09-26-milestone-16-design.md)
**Predecessor:** Milestone 15 — A Standard for Pins (complete 2026-09-26, verdict **PASS WITH
FINDINGS**, [re-audit](../plans/2026-09-26-milestone-15-reaudit.md)).
**Closes:** [#345][i345], [#346][i346], [#8][i8]. **Re-assesses on measurement:** [#9][i9].

[i8]: https://github.com/MarcelRoozekrans/BlazorNative/issues/8
[i9]: https://github.com/MarcelRoozekrans/BlazorNative/issues/9
[i345]: https://github.com/MarcelRoozekrans/BlazorNative/issues/345
[i346]: https://github.com/MarcelRoozekrans/BlazorNative/issues/346

## Goal

Every export that runs handler code runs it inline, then blocks on `GetAwaiter().GetResult()`. When
a handler awaits the host, that blocking freezes the shell's dispatch lane (#345), and Android
predictive back, which asks .NET for its verdict from the main thread, waits behind it (#346).
**The same blocking is, by accident, the only thing serialising renderer work** — after an awaited
host call the continuation renders on a thread-pool thread while the lane thread waits. The
deadlock and the serialisation are one mechanism today, so "return early" alone would trade a
deadlock for a data race.

M16 separates them: the renderer gets **one .NET-owned thread**; exports post work to it and wait
only for the synchronous part, so the shell lane is freed the moment a handler yields. A fault after
the first await reaches the shell through the existing `hostCallBegin` slot. .NET pushes
`canGoBack` and Android's back callback is toggled to match. **No ABI change.**

The user-visible outcome: **an app cannot freeze because a handler waited for a permission sheet,
and the back gesture always answers.**

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing — .NET, JVM, **and both device lanes dispatched**, with each lane's
      `headSha` compared against the PR head
- [ ] **[#345][i345] is fixed.** `DispatchLaneBlockingTests` is **flipped, not deleted** — the
      export returns while a host call is still open — and the false comment at
      `Exports.cs:504-506` is corrected **as the first commit** of the fix.
- [ ] **[#346][i346] is fixed.** The JVM `..._still_deadlocks_...` test is flipped. **No main-thread
      caller blocks on .NET** — back, `onNewIntent`, and iOS's `navigateDispatcher` included. An
      **iOS XCTest twin** of the lane-blocking pin exists.
- [ ] **One thread owns the renderer, and a pin proves it.** Every frame emission and every
      renderer mutation happens on the render thread; `ReportIfNotTheRenderOwnerThread` fails under
      test instead of only logging.
- [ ] **The sync-mount contract survives.** `MountSyncTests` pins the *behaviour*, not the type
      name `InlineDispatcher`, and 13.2's `Dispose → InvokeAsync` recursion is kept as a named
      regression test.
- [ ] **[#8][i8] is fixed.** A fault after the first await reaches `onError` on both shells, proven
      on the JVM and on XCTest. The rc contract — "rc reports the synchronous part" — is written
      once, and every export's rc table agrees with it.
- [ ] **[#9][i9] is re-assessed on measurement** after the async offload lands: fixed, or
      re-ledgered with a new trigger. Never silently closed.
- [ ] **No ABI change**, verified by diffing — 80-byte bridge struct, 10 exports. The new notice
      ops live in `src/wire-vocabulary.json` and are generated like the others.
- [ ] **Every new pin conforms to [`docs/pin-standard.md`](../pin-standard.md)** on Rules 2–5 and 7,
      with its mutations recorded.

> **No "release tagged in git" criterion.** `docs/planning/CONVENTIONS.md` records **`Milestone
> completion tags a release: no`** — release-please owns the `v<semver>` namespace.

## Phases

1. Phase 16.0 — the render-thread spike [complete] — PROCEED, not a clean GO: 5 of 7, G1 and G6 fail as written with every failure fixable; 11 named 16.1 requirements; spike unmerged at 9df59f0 (#422)
2. Phase 16.1 — the render thread, with async faults folded in from the former 16.2 [complete] — render thread in place of InlineDispatcher; exports wait for the sync part only; late faults delivered as a generated FaultNotice op; shutdown quiesces; #345 and #8 closed; .NET 1201 → 1255, JVM 162 → 170, iOS 271 → 276; #424–#427 filed (#428)
3. Phase 16.2 — back and navigation off the main thread [complete] — BackState/BackUnhandled notices; AndroidX OnBackPressedCallback toggled in the page batch; unhandled back handed to the platform; navigators fire-and-forget; blocking dispatch test-only by caller scan; #346 closed; .NET 1255 → 1278, JVM 170 → 190, Android 228 → 233, iOS 276 → 282 (#431)
4. Phase 16.3 — starvation, measured [active]
5. Phase 16.4 — audit and close [pending]

> **Phases renumbered 2026-09-26, by owner decision.** Async fault delivery was folded into 16.1,
> so no merged state exists in which a fault after the first await is only logged. That window is
> what a separate 16.2 would have opened, and only a manual rule against merging a release PR would
> have guarded it. Old 16.3–16.5 are now 16.2–16.4.

**Ordering rationale.** 16.0 first because it is the only phase that can end the milestone: **a
measured no-go stops M16 and goes to the owner.** 16.1 next. 16.2 depends on 16.1. 16.3 needs
16.1's offload in place, or there is nothing new to measure. 16.4 audits.

## Risk areas

| Risk | Impact | Mitigation |
|---|---|---|
| **13.2's door is shut for a reason not yet found** | M16's core does not work | 16.0 is a spike with an explicit no-go that stops the milestone — not a phase that must succeed |
| **Frames arriving after the export returns break shell assumptions** | Device-only glitches | The render thread is the only frame emitter, so frames stay serial; 16.1 audits Android's `pending` list and the iOS trampoline; both device lanes run every phase |
| **The rc meaning changes quietly** | A third-party shell reads rc 0 as "handler finished" | The contract is written in the C header and `Exports.cs`, and called out in the changelog as a behaviour change |
| **The `canGoBack` stale window** | A back press swallowed at root, or the app finishing one step early | 16.2 writes down which side wins and tests each direction |
| **A threading bug only a device exposes** | Green CI, a hung phone | Both device lanes, `headSha` compared; the iOS twin pin makes iOS measurable |
| **New pins that pass while checking nothing** | M15's lesson repeats inside M16 | The DoD applies the standard; every pin mutation-proven |

## Out of scope

- **1.0 and the remaining iPhone items** — APNs, universal links, thermal.
- **The feature backlog** — #284, #285.
- **M15's carried debt** — #395–#418, except where a 16.x phase edits the same file.
- **#12 and #13** — the hardening ledger's perf items.

## Open questions

- **16.2:** which side wins in the `canGoBack` stale window.
- **16.3:** whether #9 is fixed or re-ledgered — decided on measurement.

## Audit History

| Date | Verdict | Gaps |
|---|---|---|
| — | *(not yet audited)* | — |
