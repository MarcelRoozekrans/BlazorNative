# Session State

**Last session:** 2026-10-30
**Milestone:** 18 — Acknowledged Bugs `[status: active]`, started 2026-10-24.
**Phase:** 18.2 — Fault attribution for queued work (#500) `[status: pending]`. The design is approved; the plan is not written yet.
**Branch:** `feat/18.2-queued-fault-attribution`, from `main` at `97217e63`.

## Current Position

- **18.1 is complete on `main`.** #584 shipped it, and #591, merged 2026-10-30 as `97217e63`, recorded it. #591's one red was the iOS simulator lane: `xcodebuild` could not find the booted simulator, so no tests ran. That error appears once in the last 30 ios.yml runs, and the job passed on rerun.
- **The 18.2 design is approved:** `docs/superpowers/specs/2026-10-30-phase-18.2-design.md`. The owner approved it on 2026-10-30, including its five "Calls made in this design".
- **Baselines:** .NET 1387, JVM 206, Android 249, iOS 289.
- **The grading rule** for pins lives in `docs/pin-standard.md`, in the section "Grading a pin: sites and reds".
- **Owner rule (2026-10-25):** no Claude signature anywhere, meaning no `Co-Authored-By` trailer and no "Generated with" line. When merging, strip any signature from the squash message.

## Owner decisions for 18.2 (2026-10-30)

- **Re-render tagging: flags plus the render log.**
  - An `AsyncLocal` change callback on `s_flowingScope` marks when A's flow runs inside B's window.
  - A diff of `ComponentBase._hasPendingQueuedRender` across that interval tags the components A dirtied.
  - The renderer's own logger catches Blazor's Debug `RenderingComponent` call, so it knows which component is rendering.
  - Reading Blazor's render queue through `UnsafeAccessorType` was rejected.
- **Drift guard: throw at load.** `_hasPendingQueuedRender` joins `BlazorInterop.VerifyAccessors`, so a mismatch throws `BlazorVersionMismatchException`. It does not degrade.
- **The key finding the plan must keep: the Done trap.** In the inline case, A's handler finishes before B's batch renders A's re-render. So `AwaitWholeHandler` must flush the diff explicitly, then await, never block on, A's outstanding tagged work. The settle is a `TaskCompletionSource` with `RunContinuationsAsynchronously`, released in B's `DispatchSyncPart` `finally`.

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

1. On `feat/18.2-queued-fault-attribution`, run `start-next-phase`. 18.2 is pending, its spec exists and it has no plan, so the next step is `superpowers:writing-plans` from the approved spec. The plan sets the phase `active` and adds its `**Plan:**` line in ROADMAP.md.
2. In the plan, mark the `NativeRenderer` tasks, the callback, settle and attribution, `**Model:** opus`, because they involve tricky concurrency. Include a mutation-record task under "Grading a pin: sites and reds", the register cell in `docs/pin-standard.md`, and the rewrite of `FaultNoticeTests`' 17.1 DOES NOT COVER block.
3. Execute subagent-driven, the owner's standing preference, then `complete-phase` and a PR. Use no closing keywords unless the closure is intended; #500 is meant to close with this phase.
