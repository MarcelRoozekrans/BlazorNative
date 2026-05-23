# Milestone 1 — P0: Runtime Boots End-to-End

**Status:** active
**Started:** 2026-05-23
**Source:** maps to BACKLOG.md "P0 — Blocks everything"

## Goal

A `BlazorNative.Core` WASM module that boots cleanly under `wasmtime`, has the correct `[UnmanagedCallersOnly]` exports visible in its export table, can round-trip a cooperative `await Task.Delay(1)`, and has a chosen, working strategy for accessing Blazor's internal render-tree types.

After this milestone the project compiles in all configurations (Debug, Release, WASI) and runs both the inner-loop DevHost and the WASI binary without errors. No native shell or actual native rendering yet — that is Milestone 2.

**Update (Phase 1.1, 2026-05-23):** .NET 10's `wasi-experimental` workload provides **Mono-AOT** for `wasi-wasm`, not NativeAOT. The original design's "Native AOT + WASI" framing was wrong; the actual model is a Mono runtime compiled to WASM (`dotnet.wasm`, ~12 MB) that loads a managed `BlazorNative.Core.dll` at runtime. Different bundling shape, same external interface (one `dotnet.wasm` + side-by-side `.dll` files in an AppBundle). Phase 1.2 needs `wasi-sdk` installed for the IL→WASM AOT step that produces the app-specific `BlazorNative.Core.wasm`.

## Definition of Done

1. **Build green in all configurations.**
   - `dotnet build BlazorNative.sln -c Debug` succeeds.
   - `dotnet build BlazorNative.sln -c Release` succeeds.
   - `dotnet build src/BlazorNative.Core/BlazorNative.Core.csproj -r wasi-wasm -c Release` succeeds and produces an AppBundle containing `dotnet.wasm` (Mono runtime), the launcher scripts (`run-wasmtime.sh`, `run-node.sh`), and the managed `BlazorNative.Core.dll`. The app-specific `BlazorNative.Core.wasm` is produced only on `dotnet publish` with `wasi-sdk` installed — Phase 1.2 work.

2. **Renderer internal-API verdict captured.** A written decision (in `docs/plans/`) recording the chosen approach (`UnsafeAccessor` preferred per BACKLOG, or alternative if spike disproves it) with a working proof-of-concept compiling against `Microsoft.AspNetCore.Components`'s public-only surface.

3. **WASI entry point exists.** `src/BlazorNative.Core/WasiEntryPoint.cs` (or equivalent) defines `Main`, bootstraps the .NET 9 cooperative scheduler, and exits cleanly.

4. **`[UnmanagedCallersOnly]` export visible.** `wasm-tools dump artifacts/wasi/BlazorNative.Core.wasm | grep blazornative_dispatch_event` returns a match in the export section.

5. **Cooperative async round-trip works.** Running the produced `.wasm` via `wasmtime artifacts/wasi/BlazorNative.Core.wasm` performs at least one `await Task.Delay(1)` and exits with code 0 (proves the scheduler is wired correctly).

6. **`DispatchEventAsync` signature compiles.** `BlazorNative.Renderer` builds without errors against the chosen internal-API strategy. `NativeRenderer.DispatchUiEventAsync` calls the renderer base correctly.

7. **Analyzers do not fire on DevHost or test projects.** `.editorconfig` (or equivalent suppression) scoped so `WasiThreadingAnalyzer` / `WasiBclGapsAnalyzer` are silent in non-`wasi-wasm` targets.

8. **Decision log committed.** Each phase produces a short plan doc in `docs/plans/` capturing what was tried, what worked, and any follow-up for later milestones.

## Out of scope for this milestone

- Android Kotlin shell (Milestone 2)
- Native widget rendering (Milestone 2)
- Component library / `Bn*` components (Milestone 3)
- iOS shell (Milestone 4)
- NuGet packaging, CI pipeline, OTA updates (later milestones)

## Phases

Tracked in `ROADMAP.md`. Initial plan (subject to refinement via `add-phase`/`insert-phase`):

- Phase 1.1 — Renderer internal-API spike (`UnsafeAccessor` verdict)
- Phase 1.2 — WASI entry point + cooperative scheduler bootstrap
- Phase 1.3 — `[UnmanagedCallersOnly]` export verification
- Phase 1.4 — `DispatchEventAsync` signature fix against chosen strategy
- Phase 1.5 — Analyzer scoping (`.editorconfig` / project-level suppressions)

## Why this milestone exists

Nothing further can be demonstrated — not a render frame, not an Android shell, not a single bridge round-trip — until the .NET → WASM toolchain produces a binary that loads, exports the right symbols, and runs the cooperative scheduler correctly. Every later phase in BACKLOG.md assumes these foundations hold. Failing to nail them here means rework downstream.
