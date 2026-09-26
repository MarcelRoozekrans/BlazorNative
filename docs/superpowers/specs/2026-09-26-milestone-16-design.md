# Milestone 16 Design — `Unblock the Dispatch Lane`

**Date:** 2026-09-26
**Milestone:** `16`
**Stage:** milestone (one milestone, multiple phases)

**Closes:** [#345], [#346], [#8]. **Re-assesses on measurement:** [#9].
**Predecessor:** Milestone 15, A Standard for Pins. It completed on 2026-09-26 with the verdict
PASS WITH FINDINGS. M16 applies that milestone's pin standard to its own new pins.

[#8]: https://github.com/MarcelRoozekrans/BlazorNative/issues/8
[#9]: https://github.com/MarcelRoozekrans/BlazorNative/issues/9
[#345]: https://github.com/MarcelRoozekrans/BlazorNative/issues/345
[#346]: https://github.com/MarcelRoozekrans/BlazorNative/issues/346

## Goal

Every export that runs handler code does so inline, then blocks on `GetAwaiter().GetResult()`. When
a handler awaits the host, that blocking freezes the shell's dispatch lane: #345 on .NET, and #346
through Android predictive back. It is also, by accident, **the only thing keeping renderer work
serial**. After an awaited host call, the handler's continuation resumes on a thread-pool thread and
renders there while the lane thread waits. Today the deadlock and the serialisation are the same
mechanism, so "just return early" would trade a deadlock for a data race.

M16 separates them.
- **One owner thread.** The renderer gets one .NET-owned thread. Exports post work to it and wait
  only for the synchronous part, so the shell lane is freed the moment a handler yields.
- **Faults.** A fault after the first await reaches the shell through the existing `hostCallBegin`
  slot.
- **Back.** Android back stops asking .NET a blocking question. .NET pushes `canGoBack`, and the
  shell enables or disables its callback to match.

For users, the outcome is that **an app cannot freeze because a handler waited for a permission
sheet, and the back gesture always answers.** There is **no ABI change**.

## Measured starting point

Every fact below was measured on `main` at `8e6a6ad`.

- **Exports block today.** `Exports.DispatchEventCore` (`Exports.cs:507-508`) and
  `DispatchHostEventCore`'s `back` and `navigate` arms (`:773`, `:804`) block on
  `GetAwaiter().GetResult()`. The comment at `:504-506` claims the opposite, and it is false for
  async handlers.
- **Continuations run on the pool, and nothing serialises them.**
  - `NativeShellBridge.InvokeHostCallAsync` parks a `RunContinuationsAsynchronously` TCS and awaits
    it with `ConfigureAwait(false)` (`NativeShellBridge.cs:466-491`). The continuation therefore
    resumes on the thread pool.
  - `InlineDispatcher` installs no synchronization context (`NativeRenderer.cs:135-162`).
  - `CompleteHostCall` touches only a `ConcurrentDictionary` and `TrySetResult`. Nothing serialises it
    against `DispatchEventCore`.
  - `ReportIfNotTheRenderOwnerThread` (`NativeRenderer.cs:348-370`) only logs.
- **The shells assume frames arrive one at a time.** Android's `WidgetMapper.apply` appends to an
  unsynchronised `pending` list from whichever thread calls it (`WidgetMapper.kt:741-750`).
- **Android back blocks the main thread.** `handleBack` calls
  `dispatchHostEventAndWait(BnHostEvent.Back)`, which ends in an untimed `future.get()`, on main
  (`MainActivity.kt:632-641`). The callback is registered once in `onCreate` and never toggled
  (`:585-591`). `onNewIntent`'s warm navigate blocks the same way (`:567`).
- **iOS has no system back.** Its `navigateDispatcher`s call `dispatchLane.sync` from main
  (`BnRuntime.swift:346-355`).
- **Both deadlocks are pinned, but only on two of three platforms.** The pins assert that the bug
  still exists:
  - `DispatchLaneBlockingTests.AnAsyncHandlerAwaitingAnOpenHostCall_STILL_BlocksTheDispatchLane` for
    .NET;
  - `HostEventTest.dispatchHostEventAndWait_still_deadlocks_behind_a_held_dispatch_lane` for the
    JVM.

  **There is no iOS twin.**
- **Phase 13.2's closed door.**
  - An honest `CheckAccess()` on the **inline** dispatcher overflowed the stack in a
    `Dispose → InvokeAsync` loop, 3 runs out of 3.
  - Its conclusion was that "an honest answer needs a real queue, which breaks the sync-mount
    contract" (`2026-08-21-phase-13.2-conclusion.md:13-41`).
  - `MountSyncTests:81-90` pins the dispatcher's *type name* as `InlineDispatcher`.
- **The ABI surface available to M16.** The ABI is 10 exports and an 80-byte bridge struct. The only
  .NET-to-shell channels are the frame callback and `hostCallBegin`. No rc means "pending"
  (`Exports.cs:388-392`).

## Owner decisions (brainstorm, 2026-09-26)

1. **Theme:** unblock the dispatch lane, chosen over M15's carried debt, streaming transport and the
   1.0 device items.
2. **Serialiser:** a .NET-owned render thread. It was chosen over a renderer lock with the inline
   dispatcher kept, and over bounded waits in the shells, which would be a workaround.
3. **Faults after the first await** travel over `hostCallBegin` as a reserved op. There is **no ABI
   change**. rc keeps meaning "the synchronous part ran".
4. **Android back:** .NET pushes `canGoBack`, and the shell toggles its `OnBackInvokedCallback` to
   match. Back and deep-link navigation become fire-and-forget. This was chosen over a fast
   synchronous verdict.

## Definition of Done

- [ ] All planned phases complete
- [ ] All tests passing — .NET, JVM, **and both device lanes dispatched**, with each lane's
      `headSha` compared against the PR head
- [ ] **#345 is fixed.**
  - `DispatchLaneBlockingTests` is **flipped, not deleted**: the export returns while a host call is
    still open.
  - The false comment at `Exports.cs:504-506` is corrected **as the first commit** of the fix.
- [ ] **#346 is fixed.**
  - The JVM `..._still_deadlocks_...` test is flipped.
  - **No main-thread caller blocks on .NET.** This covers back, `onNewIntent`, and iOS's
    `navigateDispatcher`.
  - An **iOS XCTest twin** of the lane-blocking pin exists.
- [ ] **One thread owns the renderer, and a pin proves it.** Every frame emission and every
      renderer mutation happens on the render thread. `ReportIfNotTheRenderOwnerThread` fails under
      test instead of only logging.
- [ ] **The sync-mount contract survives.**
  - `MountSyncTests` pins the *behaviour*, not the type name `InlineDispatcher`.
  - Phase 13.2's `Dispose → InvokeAsync` recursion is kept as a named regression test.
- [ ] **#8 is fixed.**
  - A fault after the first await reaches `onError` on both shells, proven on the JVM and on XCTest.
  - The rc contract, "rc reports the synchronous part", is written once, and the per-export rc
    tables agree with it.
- [ ] **#9 is re-assessed on measurement** after the async offload lands. It is either fixed, or
      re-ledgered with a new trigger. It is never silently closed.
- [ ] **No ABI change**, verified by diff: 80 bytes and 10 exports. The new notice ops live in
      `src/wire-vocabulary.json` and are generated like the other names.
- [ ] **Every new pin conforms to [`docs/pin-standard.md`](../../pin-standard.md)** on Rules 2–5
      and 7, with its mutations recorded.

## Phases

1. **Phase 16.0: The render-thread spike** — `Surface: Backend`
   - **Goal:** Measure whether a .NET-owned single-thread dispatcher can replace `InlineDispatcher`.
     Exports post their work and wait for the synchronous part, and continuations are marshalled
     back to the owner thread.
   - **Checks:** the spike runs:
     - all 1201 tests;
     - `MountSyncTests`;
     - the navigation dispatch-window tests;
     - 13.2's recursion.
   - **Output:**
     - a measured go or no-go;
     - the list of tests whose *assertions*, not their behaviour, depend on `InlineDispatcher`.

     **A no-go stops the milestone and goes to the owner.**
2. **Phase 16.1: The render thread** — `Surface: Backend`
   - **Goal:** Replace the inline dispatcher with the render thread, so the lane is freed on yield
     and #345 closes.
   - **Scope:**
     - the comment correction first;
     - the `DispatchLaneBlockingTests` flip;
     - the owner-thread pin;
     - `MountSyncTests` pinned on behaviour;
     - the rc contract written once;
     - an audit of the shells' frame paths.
3. **Phase 16.2: Async faults** — `Surface: Backend`
   - **Goal:** Make #8's capture window continuous on the render thread, and deliver faults after
     the first await to both shells' `onError`.
   - **Mechanism:** a reserved notice op, generated from `wire-vocabulary.json` and sent over
     `hostCallBegin`.
4. **Phase 16.3: Back and navigation off the main thread** — `Surface: Mixed`
   - **Goal:** Push `canGoBack` from .NET and toggle Android's back callback to match. Make back and
     deep-link navigation fire-and-forget on both shells, so #346 closes.
   - **Scope:**
     - a written rule and a test for each direction of the stale-state window;
     - an update to `dispatch-surface.json`;
     - the iOS twin pin;
     - the template mirrors.
5. **Phase 16.4: Starvation, measured** — `Surface: Mixed`
   - **Goal:** With async offload in place, measure what a slow *synchronous* handler still costs.
     Then fix #9 or re-ledger it with a new trigger.
   - **Also:** publish an app-author page on threading: what runs where, and what rc and async
     faults mean.
6. **Phase 16.5: Audit and close** — `Surface: Docs`
   - **Goal:** Audit against the DoD on live evidence and close the milestone. **No tag**, per
     `CONVENTIONS.md`.

**Ordering.** 16.0 comes before everything else, because it is the only phase that can end the
milestone. 16.1 comes next. 16.2 and 16.3 are independent of each other; each depends on 16.1.
16.4 needs 16.1's offload in place, or there is nothing new to measure.

## Dependencies on Prior Milestones

- Depends on:
  - **M14** for `src/dispatch-surface.json` and `DispatchSurfaceDriftTests`, the differential pin
    over the lane surface;
  - **M14** for the host-event vocabulary generation (`BnHostEvent`);
  - **M14.1** for `DispatchLaneBlockingTests` and the JVM deadlock pin.
- Depends on: **M15** for the pin standard that every new pin must meet.
- Depends on: **M13.2** for the finding this milestone re-examines. 16.0 is the measured answer to
  its closed door.
- External: none required. The device lanes run in CI. No phase needs a physical device, an Apple
  account or an external contributor.

## External Constraints

- **The ABI is frozen at 80 bytes and 10 exports, and it stays frozen.** Every new .NET-to-shell
  signal rides `hostCallBegin`.
- **Android API levels.** `OnBackInvokedCallback` exists only on API 33 and above; below 33, the
  deprecated `onBackPressed()` path is taken. The build is `minSdk` 24 and `targetSdk` 34, so both
  paths need a verdict that does not block.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| **13.2's door is shut for a reason not yet found** | The core of M16 does not work | 16.0 is a spike with an explicit no-go that stops the milestone. It is not a phase that must succeed |
| **Frames arriving after the export returns break shell assumptions** | Visual glitches that show only on a device | The render thread is the only frame emitter, so frames stay serial. Android's `pending` list and the iOS trampoline are audited in 16.1. Both device lanes run every phase |
| **The rc meaning changes quietly** | A third-party shell reads rc 0 as "the handler finished" | The contract is written in the C header and `Exports.cs`, and the changelog calls it out as a behaviour change |
| **The stale window in `canGoBack`** | A back press is swallowed at root, or the app finishes one step early | 16.3 must write a rule for which side wins, and test each direction |
| **A threading bug that only a device exposes** | CI passes, and a phone hangs | Both device lanes run with `headSha` compared. The iOS twin pin makes iOS measurable |
| **New pins that pass while checking nothing** | M15's lesson repeats inside M16 | DoD: the standard applies, and every pin is proven by mutation |

## Out of Scope

- **1.0 and the remaining iPhone items:** APNs, universal links and thermal behaviour.
- **The feature backlog:** #284 (push notifications) and #285 (streaming transport).
- **M15's carried debt,** #395–#418, except where a 16.x phase edits the same file.
- **#12 and #13,** the perf items in the hardening ledger.

## Open Questions

None blocking. Two questions are decided inside their phases:
- 16.3: which side wins in the `canGoBack` stale window.
- 16.4: whether #9 is fixed or re-ledgered, on measurement.
