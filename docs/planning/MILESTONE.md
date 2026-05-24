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
   - `dotnet build BlazorNative.sln -c Debug` succeeds with 0 warnings, 0 errors.
   - `dotnet build BlazorNative.sln -c Release` succeeds with 0 warnings, 0 errors.
   - `dotnet publish src/BlazorNative.WasiHost/BlazorNative.WasiHost.csproj -r wasi-wasm -c Release` (with `wasi-sdk-25` installed and `WASI_SDK_PATH` set) produces an AOT-compiled `BlazorNative.WasiHost.wasm` (~13 MB) at `bin/Release/net10.0/wasi-wasm/AppBundle/`. Wording fix 2026-05-24: the original criterion mentioned `BlazorNative.Core.wasm` + `run-wasmtime.sh` launcher scripts; that was the pre-AOT Mono-interpreter shape. After the Phase 1.2 architectural pivot (Core stays a library; WasiHost is the executable), and the Phase 1.3 trim-hint fix that lets the AOT-compiled .wasm actually export `blazornative_dispatch_event`, a single bundled `.wasm` is the correct artifact.

2. **Renderer internal-API verdict captured.** A written decision (in `docs/plans/`) recording the chosen approach (`UnsafeAccessor` preferred per BACKLOG, or alternative if spike disproves it) with a working proof-of-concept compiling against `Microsoft.AspNetCore.Components`'s public-only surface.

3. **WASI entry point exists.** `src/BlazorNative.WasiHost/WasiEntryPoint.cs` defines a synchronous `Main` that builds the DI graph (via ZeroAlloc.Inject's source-generated extension methods), resolves `IMobileBridge` + `NativeRenderer`, and exits cleanly with exit code 0. Wording fix 2026-05-24: the original criterion said `src/BlazorNative.Core/WasiEntryPoint.cs` + ".NET 9 cooperative scheduler". Reality after Phase 1.2: the entry point lives in `BlazorNative.WasiHost` (new executable project — Core stays a library), and `.NET 10 Mono-WASI handles WASI bootstrap transparently` — no explicit scheduler initialization needed, and async `Main` is unsupported (sync `int Main()` is the only viable shape until .NET 11 ships Mono-WASI cooperative-async support).

4. **`[UnmanagedCallersOnly]` export visible.** The export `blazornative_dispatch_event` is present in the AOT'd `.wasm`'s export section. Verified by `tests/BlazorNative.Wasi.Tests/ExportSmoke.Export_BlazorNativeDispatchEvent_IsPresent` via direct byte-scan of the .wasm. Wording fix 2026-05-24: the original criterion prescribed `wasm-tools dump | grep`. In practice (a) `wasm-tools dump`'s 10 MB output deadlocks subprocess pipes; (b) `wasmtime --invoke` can't reach core-module exports through the component-model layer wasi-experimental emits; (c) an in-process byte scan needs no external tool. Critical implementation detail discovered during Phase 1.3: `[UnmanagedCallersOnly]` alone is NOT a sufficient trim root for Mono-AOT — without `[DynamicDependency(All, typeof(WasiBridge))]` on `Program.Main`, the trimmer removes `WasiBridge.DispatchEvent` entirely.

5. **WASI runtime + DI graph compose under wasmtime.** Running the AOT'd `.wasm` via `wasmtime -Shttp --dir=. BlazorNative.WasiHost.wasm` emits `[BOOT] runtime-start` + `[BOOT] di-ok` + `[BOOT] done` to stdout and exits with code 0. ~~Originally asked for `await Task.Delay(1)` round-trip — dropped during Phase 1.2 because .NET 10 Mono-WASI doesn't support async Main (Task.Wait traps with PlatformNotSupportedException on single-threaded WASI).~~ Cooperative async on Mono-WASI is deferred to a later phase (.NET 11 candidate) or to the Android shell context (where threads exist).

6. **`DispatchEventAsync` signature compiles.** `BlazorNative.Renderer` builds without errors against the chosen internal-API strategy. `NativeRenderer.DispatchUiEventAsync` calls the renderer base correctly.

7. **Analyzers do not fire on DevHost or test projects.** `BlazorNative.Analyzers` (containing `WasiThreadingAnalyzer` BN0001-BN0006 + `WasiBclGapsAnalyzer` BN0010-BN0013) is wired as a `ProjectReference` with `OutputItemType="Analyzer"` from `BlazorNative.WasiHost` only. Core / Renderer / Http / Blazor / DevHost / tests do not reference it. Verified post-wire: `dotnet build BlazorNative.sln` produces 0 warnings, including no BN0001-BN0013 output on any project. Future wasi-wasm-targeting projects (M2+) should add the same `ProjectReference` shape. Wording refinement 2026-05-24: the original `.editorconfig` mechanism would have been one valid approach; the project-graph approach we picked is cleaner and doesn't require per-folder suppression files.

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
