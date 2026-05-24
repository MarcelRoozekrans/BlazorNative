# Milestone 1 — Final Audit (Pre-Completion)

*Run: 2026-05-24*
*Triggered by: user request after Phase 1.5 close-out — last gate before `complete-milestone`*

## Verdict

**PASS — all 8 DoD criteria met. Ready for `complete-milestone` + tag `v1.0`.**

The 2026-05-23 mid-milestone audit reported 6/8 (gaps: #4 export verification = Phase 1.3; #7 analyzer scoping = Phase 1.5). Both have been resolved. Additionally, the four DoD-wording staleness flags from that audit (#1c, #3, #4, #7) were all updated as part of Phase 1.5's housekeeping commit.

## Per-criterion verification (2026-05-24, after Phase 1.5)

| # | DoD | Status | Evidence (this run) |
|---|---|---|---|
| 1a | `dotnet build BlazorNative.sln -c Debug` | ✅ | 0 errors, 0 warnings |
| 1b | `dotnet build BlazorNative.sln -c Release` | ✅ | 0 errors, 0 warnings |
| 1c | WASI publish produces AOT'd .wasm | ✅ | `src/BlazorNative.WasiHost/bin/Release/net10.0/wasi-wasm/AppBundle/BlazorNative.WasiHost.wasm` exists, 13,773,961 bytes (last modified 2026-05-24 16:36) |
| 2 | Renderer internal-API verdict captured | ✅ | [docs/plans/2026-05-23-renderer-internal-api-design.md](2026-05-23-renderer-internal-api-design.md) — Option D (`UnsafeAccessor` + `UnsafeAccessorType`) behind `BlazorInterop.cs` |
| 3 | WASI entry point exists | ✅ | `src/BlazorNative.WasiHost/WasiEntryPoint.cs` — sync `int Main()` builds DI graph + exits clean |
| 4 | `[UnmanagedCallersOnly]` export visible | ✅ | `blazornative_dispatch_event` present in the .wasm export section (2 byte-occurrences — once as export name, once as metadata). Verified by `tests/BlazorNative.Wasi.Tests/ExportSmoke.Export_BlazorNativeDispatchEvent_IsPresent`. Required `[DynamicDependency(All, typeof(WasiBridge))]` on `Program.Main` — `[UnmanagedCallersOnly]` alone wasn't enough of a trim root. |
| 5 | WASI runtime + DI graph compose under wasmtime | ✅ | `BlazorNative.Wasi.Tests.BootSmoke` → 2 passed in 7s end-to-end (publish→wasmtime→exit). Stdout shows `[BOOT] runtime-start` + `[BOOT] di-ok bridge=WasiBridge renderer=NativeRenderer` + `[BOOT] done` + exit 0. |
| 6 | `DispatchEventAsync` signature compiles | ✅ | `BlazorInterop.RefAccessors.DispatchEventAsync` declares `(ulong, EventFieldInfo?, EventArgs)` via `[UnsafeAccessor(Method)]` + `[UnsafeAccessorType]`. `NativeRenderer.DispatchUiEventAsync` calls the wrapper. Build green. |
| 7 | Analyzers silent on non-WASI projects | ✅ | `BlazorNative.Analyzers` wired via `<ProjectReference OutputItemType="Analyzer">` from `BlazorNative.WasiHost` only. `dotnet build BlazorNative.sln` produces 0 BN0001-BN0013 warnings. Core / Renderer / Http / Blazor / DevHost / tests do not reference the analyzer. |
| 8 | Decision log committed | ✅ | 8 design/plan docs in `docs/plans/`: codebase map, renderer-internal-api-design, phase-1.1-impl-plan, phase-1.2-design, phase-1.2-impl-plan, milestone-1-audit (mid), phase-1.3-design, phase-1.3-impl-plan, and this final audit. |

## Final test summary

```
dotnet build BlazorNative.sln -c Debug   → 0 errors, 0 warnings
dotnet build BlazorNative.sln -c Release → 0 errors, 0 warnings
dotnet test  BlazorNative.sln -c Debug   → 4 passed, 2 skipped, 0 failed

  BlazorNative.Renderer.Tests:  1 passed,  2 skipped   (831 ms)
  BlazorNative.Wasi.Tests:      3 passed,  0 skipped   (1 s)
```

The 2 skipped renderer tests are deliberate deferrals captured in earlier phases:
- `RenderWalk_IsAllocationFree_OnSteadyState` — needs `StateHasChanged`-on-mounted-root (M4 / BACKLOG P3)
- `NestedElements_EmitCreateNodeForEachLevel` — nested-element sibling-key handling (M2 / BACKLOG P1)

## Phase ledger

| Phase | Status | Verdict link |
|---|---|---|
| 1.1 — Renderer internal-API spike | ✅ complete | Surprise: most Blazor 10 render-tree members are public; `[UnsafeAccessor]` only needed for `Renderer.DispatchEventAsync`. |
| 1.2 — WASI entry point + DI bootstrap | ✅ complete | Two findings: (a) Mono-AOT not NativeAOT for `wasi-wasm`; (b) async `Main` traps on Mono-WASI — sync `Main` is the supported shape. Spawned `BlazorNative.WasiHost` as a new executable project (Core stays a library; breaks the would-be circular dep). |
| 1.3 — `[UnmanagedCallersOnly]` export verification | ✅ complete | Three findings: (a) `wasmtime --invoke` can't reach core-module exports through the component-model layer; (b) `wasm-tools` subprocess pipes deadlock on multi-MB output; (c) in-process byte-scan was the right verification. Critical fix: `[DynamicDependency]` to defeat Mono-AOT trimming of `[UnmanagedCallersOnly]` methods. |
| 1.4 — `DispatchEventAsync` signature fix | ✅ complete (formal close-out; work landed in 1.1) | No new code. |
| 1.5 — Analyzer scoping + DoD wording cleanup | ✅ complete | Wired `BlazorNative.Analyzers` into `WasiHost` only (project-graph scoping, not `.editorconfig`). Updated MILESTONE.md DoD wording for #1c / #3 / #4 / #7 to reflect post-pivot reality. Added `WasiTestCollection` to share `WasiPublishFixture` between `BootSmoke` + `ExportSmoke` (fixed an xUnit parallel-fixture race surfaced during audit re-run). |

## Risks carried into M2

| # | Risk | Owner | Where tracked |
|---|---|---|---|
| 1 | **Mono-WASI async trap will fire on first real bridge event.** `WasiBridge.DispatchEvent`'s `.AsTask().GetAwaiter().GetResult()` and `NativeRenderer.DispatchFrameAsync`'s `await _bridge.WriteStorageAsync(...)` both trip the same `Task.InternalWaitCore PlatformNotSupportedException` we hit with async `Main`. **First failure point: M2 when the Android shell pushes a real `blazornative_dispatch_event`.** | M2 brainstorm — must be addressed before M2 native-shell integration begins | BACKLOG P0 "Mono-WASI async trap will fire on first real bridge event" — three remediation options enumerated |
| 2 | `wasi-experimental` workload pins wasi-sdk to version 25.0. When the workload bumps, `setup.ps1` and `WASI_SDK_PATH` need to follow. | Calendar reminder; no tracking bullet | (none) |
| 3 | `[DynamicDependency(All, typeof(WasiBridge))]` is load-bearing for M2 native-shell symbol lookups. If a future refactor removes/renames `WasiBridge`, the trim hint must follow. | M2 — covered by Phase 1.3 ExportSmoke test catching the regression at WASI publish time | `tests/BlazorNative.Wasi.Tests/ExportSmoke.cs` |

## Recommendation

Invoke `complete-milestone` — update ROADMAP.md M1 status to `complete`, MILESTONE.md status to `complete`, tag `v1.0`. Then `new-milestone` for M2 (P1 — First end-to-end demo on Android).

The remediation for risk #1 (Mono-WASI async trap) should be the **first item** in M2's brainstorm — picking which of the three BACKLOG-documented options to pursue, before any native-shell scaffolding work begins. Otherwise M2 will hit it at integration time and require unscheduled refactoring of the bridge layer.
