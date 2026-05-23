# BlazorNative — Codebase Map
*Generated: 2026-05-23*

## Tech stack

| Layer | Tech |
|---|---|
| Runtime target | .NET 9 → `wasi-wasm` via NativeAOT + LLVM (`Microsoft.DotNet.ILCompiler.LLVM`) |
| UI framework | Blazor (server-rendered + WebAssembly interactive components in DevHost) |
| Inner-loop host | ASP.NET Core minimal API (`Microsoft.NET.Sdk.Web`) |
| Cross-language contract | WebAssembly Interface Types (`tools/wit/mobile-bridge.wit`) via `wit-bindgen` |
| WASM runtime (planned) | Wasmtime — embedded in Kotlin (Android) and Swift (iOS) shells |
| Reactive primitives | `System.Reactive` (`Subject<T>`, `IObservable<T>`) |
| Roslyn analyzers | `netstandard2.0` analyzer project (Microsoft.CodeAnalysis.CSharp) |
| Solution-level configs | `Debug`, `Release`, `WASI` |
| Source control | **Not a git repository** — `git tag`/`git commit` steps from project-orchestration skill will not apply until `git init` is run |

## Directory layout

```
BlazorNative/
├── BlazorNative.sln              ← 7 projects, but only 6 exist on disk
├── Makefile                      ← dev/wasi/android/wit-gen/test/clean targets
├── setup.ps1                     ← Windows prerequisite installer
├── README.md                     ← intent, quick start, architecture diagram
├── docs/
│   ├── BACKLOG.md                ← P0–P7 + future roadmap (~530 lines, very detailed)
│   ├── SESSION-HISTORY.md        ← architecture rationale, naming history, WASI gap analysis
│   ├── GITHUB-SETUP.md           ← GitHub setup instructions
│   └── plans/                    ← this file
├── scripts/
│   └── create-github-issues.sh
├── src/
│   ├── BlazorNative.Core/        ← IMobileBridge, WasiBridge, DevHostBridge
│   ├── BlazorNative.Blazor/      ← Pages/Home.razor (almost empty)
│   ├── BlazorNative.Renderer/    ← NativeRenderer, NativeWidgetTree, PatchProtocol, RendererServices
│   ├── BlazorNative.Http/        ← BridgeHttpHandler, HttpServices (DI)
│   ├── BlazorNative.Analyzers/   ← WasiThreadingAnalyzer + WasiBclGapsAnalyzer (src/ and tests/ folders are EMPTY)
│   └── BlazorNative.Host.Android/← BlazorNative.DevHost project (Program.cs ASP.NET app)
└── tools/
    └── wit/mobile-bridge.wit     ← canonical bridge contract (~63 lines)
```

## Project breakdown

| Project | TargetFramework(s) | Role | Status |
|---|---|---|---|
| `BlazorNative.Core` | `net9.0;net9.0-browser;wasi-wasm` | Bridge contract + two implementations (`DevHostBridge`, `WasiBridge`) | **WASI entry point missing** — `OutputType=Exe` is set for `wasi-wasm` but no `Main` / `WasiEntryPoint.cs` exists yet |
| `BlazorNative.Blazor` | (default) | Razor components | Only `Pages/Home.razor` — effectively stub |
| `BlazorNative.Renderer` | (default) | Headless Blazor `Renderer` subclass + JSON patch protocol | **Won't compile** — uses internal `RenderTreeDiff.Edits.Array`, internal `RenderTreeFrame` fields, and outdated `DispatchEventAsync(WebEventData, ...)` signature. P0 blocker. |
| `BlazorNative.Http` | (default) | `HttpMessageHandler` routing through `IMobileBridge.FetchAsync` | Looks complete; needs DI extension verification |
| `BlazorNative.Analyzers` | `netstandard2.0` | Roslyn analyzers BN0001–BN0013 | Two analyzer files at project root (not in `src/` despite the folder existing); `tests/` folder is empty |
| `BlazorNative.DevHost` (in `Host.Android/`) | `net9.0` | ASP.NET minimal-API dev host with `DevHostBridge` and `/dev/*` DevTools endpoints | Works as a normal web app; **does not yet mount `NativeRenderer`** — render-frame output is not wired |
| `BlazorNative.Bridge` | — | Listed in `.sln`, **directory does not exist** on disk | Reserved for `wit-bindgen --language csharp` output; bindings not generated yet |

## Entry points

| Mode | Entry | Command |
|---|---|---|
| DevHost (inner loop) | `src/BlazorNative.Host.Android/Program.cs` | `dotnet watch run --project src\BlazorNative.Host.Android\BlazorNative.DevHost.csproj` or `make dev` → https://localhost:5273 |
| WASI compile | `src/BlazorNative.Core/BlazorNative.Core.csproj` (Exe, needs `Main`) | `make wasi` |
| WASI run | Wasmtime CLI on output `.wasm` | `make wasi-run` |
| Android shell | **Not implemented** | (planned `src/BlazorNative.Shell.Android/`) |
| iOS shell | **Not implemented** | (planned) |

## Key patterns and conventions

1. **Bridge contract is the architectural seam.** `IMobileBridge` is implemented twice — `DevHostBridge` (in-process mock) and `WasiBridge` (P/Invoke to WASM imports). Components depend only on the interface.
2. **ZeroAlloc-Net compatibility.** `readonly record struct` for value types, `ValueTask` returns on hot paths, `JsonSerializerContext` for AOT-safe serialization, `InvariantGlobalization=true`.
3. **WIT is canonical.** `tools/wit/mobile-bridge.wit` is the source of truth; C#/Kotlin/Swift bindings are meant to be generated, not hand-written.
4. **Reactive event flow.** `IObservable<NativeEvent>` for native→WASM events; `[UnmanagedCallersOnly(EntryPoint="blazornative_dispatch_event")]` is the WASM export the host calls.
5. **Patch protocol is JSON polymorphic.** `RenderPatch` uses `[JsonPolymorphic]` with `"op"` discriminator; one frame = list of patches + a `CommitFramePatch` boundary.
6. **Element → NodeType mapping** lives in `NativeRenderer.MapElementToNodeType` — currently HTML-element-driven, planned `Bn*` component library would emit native types directly.
7. **Analyzers as a safety net.** WASI-hostile APIs (threads, sockets, file I/O, processes) are caught at compile time rather than runtime.

## Test framework

- `make test` runs `dotnet test`, but **no test projects exist yet**. `BlazorNative.Analyzers/tests/` is an empty folder.

## Known P0 blockers (from `docs/BACKLOG.md`)

1. **No WASI `Program.cs` entry point** — Core is set as `Exe` for `wasi-wasm` but has no `Main`.
2. **Cooperative async scheduler bootstrap missing** — `WasiEventLoop.Run()` (or .NET 9 equivalent) must be the outermost call.
3. **`[UnmanagedCallersOnly]` export not verified** in compiled `.wasm` module's export table.
4. **Renderer internal-API strategy undecided** — `RenderTreeDiff` / `RenderTreeFrame` are `internal` in `Microsoft.AspNetCore.Components`; current `NativeRenderer.cs` references them directly and won't compile. BACKLOG suggests `UnsafeAccessor` (option D) as preferred path. **Spike required before any renderer work continues.**
5. **`DispatchEventAsync` signature drift** — current call site uses `WebEventData` wrapper that doesn't match Blazor's internal signature.

## Recommended starting points for new work

- **First spike:** `UnsafeAccessor` strategy for accessing internal Blazor render-tree types (P0 #4). Everything else in P0 depends on this verdict.
- **Second:** Write `src/BlazorNative.Core/WasiEntryPoint.cs` with correct async bootstrap (P0 #1 + #2).
- **Third:** Verify export table with `wasm-tools dump` after a clean `make wasi` (P0 #3).
- **Fourth:** Fix `RendererServices.cs` event dispatch signature once internal-API verdict is in (P0 #5).
- **Parallel track:** Run `make wit-gen` to populate `src/BlazorNative.Bridge/Generated/` so the `.sln`-referenced project actually exists on disk (P3 task).
- **Parallel track:** Initialise this as a git repo (`git init`) so the project-orchestration skill's commit/tag flow works.

## Inputs to the next step (brainstorming / new-milestone)

The BACKLOG already encodes a strong phase plan (P0 → P7). A natural fit for project-orchestration is:

- **Milestone 1** = "P0 — Runtime boots end-to-end" (WASI entry point, scheduler, export verification, renderer API verdict, signature fix).
- Subsequent milestones map 1:1 to BACKLOG phases P1…P7.

Suggested next sub-skill: **`new-milestone`** with Milestone 1 defined as P0 — DoD: `dotnet build -c WASI` succeeds, `make wasi` emits a `.wasm` with `blazornative_dispatch_event` in the export table, `wasmtime` runs the module to a clean exit, and `await Task.Delay(1)` round-trip works.
