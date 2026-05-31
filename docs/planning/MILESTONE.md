# Milestone 3 — P2: Real Apps Can Be Built

**Status:** active
**Started:** 2026-05-28
**Source:** maps to BACKLOG.md "P2 — Real apps can be built"
**Predecessor:** Milestone 2 — complete 2026-05-28, tagged `v2.0` ([final audit](../plans/2026-05-27-milestone-2-final-audit.md))

## Goal

M2 proved the architecture works end-to-end for a static Hello render. M3 makes that architecture *useful* — real components, real interactivity, real platform-API access. After M3, a developer can plausibly build a small real app on BlazorNative: a multi-screen view hierarchy with bound form inputs, button taps that mutate state, navigation between routes, and access to host storage / fetch / current-route from .NET.

M3 also resolves the runtime-architecture question carried over from M2: pick (or confirm) the long-term build target — Mono-AOT wasi-experimental (status quo), componentize-dotnet (typed WIT, same runtime), or NativeAOT-per-ABI (drops .wasm entirely). Phase 3.0 owns this decision before any larger component-library investment.

## Definition of Done

The criteria below are the initial M3 contract drafted at milestone-open. Subject to refinement during the Phase 3.0 brainstorm.

1. **Runtime architecture decision committed.** ✅ **Decided 2026-05-28: NativeAOT-per-ABI** — see [Phase 3.0 design](../plans/2026-05-28-phase-3.0-design.md). Drops wasmtime + .wasm entirely; targets `win-x64` (JVM dev loop) + `linux-bionic-arm64`/`linux-bionic-x64` (Android). Sequenced as three sub-phases: **3.0a** renderer trim safety (on wasi-experimental, prerequisite), **3.0b** NativeAOT runtime works (JVM-desktop first, then Android, no renderer), **3.0c** native wire protocol + renderer + WASM-era collapse. Rationale: wasi-experimental upstream stalled + Phase 2.3's SDK gaps unfixed; componentize-dotnet 1-day spike on 2026-05-28 RED'd at Gate 2 (renderer `Bn*` wrapper trim-fragility under NativeAOT-LLVM — `RenderTreeFrame.ElementName` returned null at runtime). NativeAOT-per-ABI eliminates the WASI ABI threading ceiling, removes the wasmtime layer, and enables typed C-ABI for DoD #2 (bidirectional events) + DoD #3 (six deferred mobile_bridge exports). Phase 2.8's eval doc ([runtime-architecture-eval.md](../plans/2026-05-27-phase-2.8-runtime-architecture-eval.md)) is now historical context.

2. **Bidirectional event flow.** `<button @onclick>` round-trips end-to-end: tap on Android widget → host invokes .NET event-dispatch C-ABI export → the Blazor component's handler fires → re-render emits a frame → widget tree updates. **Long-running-Main concern retires automatically** under the Phase 3.0 NativeAOT decision — the runtime library is always loaded, event dispatch is just another `[UnmanagedCallersOnly]` call. Implementation lands in Phase 3.2.

3. **6 deferred `mobile_bridge` exports implemented** (Phase 2.3 carryover):
   - `shell_navigate(route)`
   - `shell_current_route() → string`
   - `shell_storage_read(key) → string`
   - `shell_storage_write(key, value)`
   - `shell_storage_delete(key)`
   - `shell_fetch(request) → response`

   **Transport mechanism settled by Phase 3.0 decision:** direct `[UnmanagedCallersOnly]` C-ABI exports + JNA callbacks, no env-var shoehorn, no WIT-typed-import toolchain. Implementation lands in Phase 3.1.

4. **`Bn*` component library** — typed wrappers around the raw `NodeType`s from M2: `BnView`, `BnText`, `BnButton`, `BnInput`, plus parameters that flow through to widget properties (`BackgroundColor`, `FontSize`, `Padding`, `Placeholder`, `Enabled`, `OnClick`).

5. **`@bind` two-way binding** works for at least one form input — EditText `value` ↔ component state. Triggers re-render on change.

6. **Cascading values** propagate from a root-mounted parent component to nested child components, and a change in the parent triggers child re-render.

7. **Navigation service** (`BlazorNative.Navigation`) — at minimum, `INavigationManager.NavigateTo(route)` triggers a root-component swap. Wired through the `shell_navigate` / `shell_current_route` exports from DoD #3.

8. **Multi-component composition.** Components compose other components; nested-component `PrependFrame` parenting works correctly (Phase 2.5 Task 1 review finding fixed — `ProcessRenderTreeDiff`'s `PrependFrame` arm uses the parent component's view, not the host root).

9. **`HandleException` strict-mode opt-in.** Phase 2.7 carryover. Renderer exceptions are surfaced rather than silently swallowed to `Console.Error`. Both Phase 2.4 (Bug A) and Phase 2.7 (Bug B) lost a day each to silent-swallow debugging; M3 prevents recurrence.

10. **`AppendChild` patch emission decision.** Currently defined in `PatchProtocol.cs` but never emitted. M3 either makes it load-bearing for composition or removes the dead patch type.

11. **Decision log committed.** Same pattern as M1/M2: design + plan doc per phase, plus an M3 final-audit doc at close.

## Out of scope for this milestone

- iOS Swift shell — Milestone 4 / BACKLOG P3
- Production hardening (security, accessibility, i18n, OTA updates) — Milestones 6/7
- NuGet packaging, CI pipeline, DevTools render-tree inspector — Milestone 4 / BACKLOG P3
- Multi-window support, MD3/HIG defaults — Milestone 8 / BACKLOG P7
- Allocation-budget tests (deferred from M1) — Milestone 4
- Predictive back, lifecycle, FCM, secure storage, deep links — Milestone 5

## Inherited from M2

These items were identified during M2 phases but are M3 work by scope:

- **Bidirectional event flow + long-running `Main`** — covered by DoD #2 above.
- **Nested-component `PrependFrame` parenting** — covered by DoD #8 above.
- **`AppendChild` patch emission resolution** — covered by DoD #10 above.
- **`HandleException` debugging hazard** — covered by DoD #9 above.
- **6 deferred `mobile_bridge` exports** — covered by DoD #3 above. (Phase 2.3 pivot shipped only `shell-platform-info`; the other 6 are intentional M3 work per the revised design.)
- **`Mount<T>` faulted-task masking** — Phase 2.4 Task 2 carryover. If `MountAsync` faults synchronously, the current diagnostic masks the real exception. Cheap fix as part of DoD #9's strict-mode work.
- **`NativeUiEvent` Kotlin-side mirror** — Phase 2.4 Task 7 carryover. Needed for DoD #2's event-flow wiring.
- **Shared `WidgetMapperTestHelpers.kt`** — Phase 2.6 cleanup. Hoist when 4th test file lands.
- **Android wasmtime 1/N flake (Phase 2.4b watch)** — keep watching; revisit with wasmtime v46+ upgrade if recurrence rate rises.

## Initial phase plan

Tracked in `ROADMAP.md`. Subject to refinement via `add-phase` / `insert-phase`. The first phase (3.0) covers the runtime-architecture decision; subsequent phases TBD via brainstorming.

## Why this milestone exists

M1 proved the toolchain. M2 proved the architecture. M3 makes the architecture *usable*. Skipping M3's component library + event flow means later milestones (P3 production hardening, P4 platform coverage, P5 ecosystem) inherit untested ergonomics and an unproven interactivity contract. The bidirectional event flow in particular changes the runtime shape (`Main` no longer exits) — that's a fundamental enough shift that delaying it past M3 would force every downstream milestone to be retrofitted.
