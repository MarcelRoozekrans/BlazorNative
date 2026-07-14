# BlazorNative — Session History

> # 🛑 HISTORICAL — SUPERSEDED. Do not build against this document.
>
> **This is a dated session log from May 2026, not reference material.** The
> architecture it describes **no longer exists in this repository**.
>
> Everything below is written around a design that has since been **deleted**:
>
> - a `.wasm` binary produced by NativeAOT + WASI, loaded by an **embedded
>   Wasmtime** runtime inside each native shell;
> - **`tools/wit/mobile-bridge.wit`** as "the canonical source of truth" for the
>   bridge, with WASI P/Invoke on either side of it;
> - a **JSON patch protocol** on the frame path;
> - analyzer rules written for that world.
>
> **What actually happened.** The WASI/Wasmtime approach was abandoned in the
> **3.0e architecture collapse (2026-07-09)**. Wasmtime and the `.wasm` were
> deleted outright. What shipped instead is a **direct NativeAOT C-ABI**: the
> app compiles to a real native library per platform (`.so` on Android, a static
> `.a` on iOS), the shell links or `dlopen`s it and calls **nine exported
> C functions**, and frames cross as **typed structs** — no interpreter, no WIT,
> no JSON on the frame path. The demo's cold boot went from ~36 s to ~1.6 s.
> `mobile-bridge.wit` is retired, and the analyzer rules that policed the old
> model were retired in Phase 4.1.
>
> **Where the live documentation is:**
> [`README.md`](../README.md) (current architecture) ·
> [`planning/ROADMAP.md`](planning/ROADMAP.md) (milestone + phase state) ·
> [`plans/`](plans/) (the per-phase decision log, including
> [the 3.0e conclusion](plans/2026-07-09-phase-3.0e-conclusion.md) that closed
> this design out).
>
> This file is kept only as a record of **how the project reasoned its way in**
> — the landscape survey and the false starts have value precisely because they
> were wrong. Read it as history. Nothing below is a description of the code.

*Conversation log with design decisions, rationale, and context*
*Written: May 2026 — architecture superseded 2026-07-09 (Phase 3.0e)*

---

## How this project started

The conversation began with a simple question: **can a Blazor WASM app be compiled for Android and iPhone?**

The short answer was no — not directly. Blazor WASM targets `browser-wasm`, not a mobile runtime. From there we explored the landscape:

- **MAUI Blazor Hybrid** — closest existing option, embeds Blazor in a `BlazorWebView` inside a MAUI native app. Reuses Razor components but still needs MAUI packaging per platform. iOS uses Mono, not full NativeAOT.
- **React Native / Flutter** — dominant cross-platform native approaches. React Native renders actual native widgets from JS/TS. Flutter uses its own Skia/Impeller rendering engine from Dart.
- **PWA** — installable on Android reasonably well, iOS is limited due to Safari/WebKit restrictions.

The key insight: **WASM runtimes exist on both platforms but aren't first-class**. Both Android and iOS support WASM inside a WebView, but that's just a browser in an app. The interesting direction is embedding a WASM runtime (Wasmtime) directly in a native shell app and loading a .NET compiled `.wasm` binary.

---

## Architecture decisions

### The core idea
```
[Blazor Components]  ← your UI, unchanged
        ↓
[BlazorNative.Core]  ← IMobileBridge contract
        ↓  compiles to .wasm via Native AOT + WASI
[mobile-bridge.wit]  ← canonical WIT interface
        ↓  WASI P/Invoke
[Native Shell]       ← thin Kotlin (Android) / Swift (iOS)
        ↓
[Wasmtime embedded]  ← runtime inside the app package
```

The native shell is intentionally **paper thin** — just enough to boot the WASM runtime and wire up platform APIs. All business logic and UI logic lives in .NET.

### Why not MAUI?
MAUI was considered but rejected as the primary target for this POC because:
- iOS still uses Mono, not NativeAOT — defeats the zero-alloc goals
- MAUI is a full framework dependency, not a thin shell
- The WASI approach is more portable and future-proof (WASI Component Model)
- Building the thin shell ourselves is the research contribution

### Why WIT?
`mobile-bridge.wit` is the **canonical source of truth** for the bridge contract. C#, Kotlin, and Swift bindings are generated from it via `wit-bindgen`. This means:
- Both sides of the bridge are always in sync
- Adding a new platform (iOS) means implementing the same WIT contract
- The contract is language-agnostic and could support other runtimes in future

### Dev experience philosophy
The inner loop must be **fast**. The key insight: during development, you don't need WASM at all. The `DevHostBridge` mock implements the full `IMobileBridge` contract in-process, letting you run with hot reload as a normal ASP.NET app. WASM compilation only happens when you explicitly run `make wasi`.

This gives three modes:
1. `make dev` — normal .NET hot reload, no WASM, fastest possible loop
2. `make wasi` — validate WASM compilation
3. `make wasi-run` — run compiled module via wasmtime CLI

---

## Project structure rationale

### BlazorNative.Core
The heart of the project. Contains:
- `IMobileBridge` — the typed C# contract (mirrors the WIT file)
- `WasiBridge` — real WASM P/Invoke implementation using `[UnmanagedCallersOnly]` for the native→WASM callback
- `DevHostBridge` — full in-process mock with logging, real HTTP passthrough, injectable events, storage snapshot, route history

Targets: `net9.0`, `net9.0-browser`, `wasi-wasm`

### BlazorNative.Blazor
Razor components that inject `IMobileBridge`. Zero platform coupling — works identically in DevHost and compiled WASM.

### BlazorNative.Renderer
The hardest and most innovative part. A headless Blazor `Renderer` subclass that:
- Intercepts component render tree diffs via `UpdateDisplayAsync`
- Walks the `RenderBatch` to produce `RenderPatch` commands
- Dispatches a `RenderFrame` (JSON patch set) to the native shell via `IMobileBridge`
- Ingests `NativeUiEvent` objects back from the native shell and dispatches them to Blazor event handlers

The `PatchProtocol` uses a polymorphic JSON discriminator (`"op"` field) and a fully AOT-safe `JsonSerializerContext`. Commands: `create`, `prop`, `append`, `remove`, `text`, `style`, `event`, `detach`, `commit`.

The `NativeWidgetTree` maintains the mapping between Blazor's internal `(componentId, siblingIndex)` tuples and our stable native `nodeId` integers.

### BlazorNative.Http
Fixes WASI's "no sockets" limitation. `BridgeHttpHandler` is an `HttpMessageHandler` that routes all `HttpClient` traffic through `IMobileBridge.FetchAsync`. The native shell performs the actual HTTP request and returns the response. Zero changes needed in application code — just register via DI.

### BlazorNative.Analyzers
Roslyn analyzers that catch WASI-incompatible code at **compile time**:

| Code | Diagnostic | Severity |
|---|---|---|
| `new Thread(...)` | BN0001 | Error |
| `Task.Run(...)` | BN0002 | Error |
| `Parallel.For/ForEach` | BN0003 | Error |
| `Thread.Sleep(...)` | BN0004 | Error |
| `Monitor`/`Mutex` | BN0005 | Warning |
| `[ThreadStatic]` | BN0006 | Warning |
| `System.Net.Sockets.*` | BN0010 | Error |
| `new HttpClient()` direct | BN0011 | Warning |
| `File.*`/`Directory.*` | BN0012 | Warning |
| `Process.*` | BN0013 | Error |

### Host.Android (DevHost)
ASP.NET minimal API that runs the Blazor app with `DevHostBridge`. Includes DevTools REST endpoints:
- `POST /dev/event` — inject a native event (simulate push notification, back button, etc.)
- `GET /dev/storage` — inspect current key/value storage state
- `GET /dev/routes` — navigation history
- `DELETE /dev/storage/{key}` — clear a storage entry

---

## WASI experimental — known gaps and mitigations

This was a dedicated conversation topic. The gaps and their status:

| Gap | Status | Mitigation |
|---|---|---|
| No threads | Fundamental WASI Preview 1 limit | Cooperative async only. Enforced by BN0001-BN0004 analyzers. |
| No sockets | Fundamental WASI Preview 1 limit | `BridgeHttpHandler` routes all HTTP via native shell |
| Blazor ≠ WASI natively | Core problem to solve | `NativeRenderer` headless renderer + patch protocol |
| BCL gaps | Many APIs throw `PlatformNotSupportedException` | BN0010-BN0013 analyzers catch at compile time |
| Reflection/AOT | AOT trims aggressively | Source generators pattern (ZeroAlloc-Net convention). `JsonSerializerContext` for all serialization. |
| No debugger on WASM | Tooling limitation | DevHost is so complete you rarely need WASM debugger |
| API instability | `wasi-experimental` is experimental | .NET 10 is stabilising it. Pin workload version in CI. |

---

## Naming history

The project was initially scaffolded as **WasmShell**, then rebranded to **BlazorNative** at the end of the session. All namespaces, project names, file names, and references were updated. The name BlazorNative was chosen because:
- More descriptive of the actual goal (Blazor → native mobile)
- Better discoverability on GitHub/NuGet
- Fits naturally alongside ZeroAlloc-Net ecosystem naming

---

## ZeroAlloc-Net compatibility notes

This project is designed to be compatible with the ZeroAlloc-Net ecosystem:
- All value types use `readonly record struct` where possible
- No reflection at runtime — all registration is compile-time
- `JsonSerializerContext` used throughout (AOT-safe serialization)
- `IMobileBridge` returns `ValueTask` not `Task` on hot paths
- `NativeWidgetTree` is single-threaded by design (WASI cooperative scheduler)
- Analyzer project follows the same `netstandard2.0` target as other Roslyn tooling in the ecosystem

---

## Session date
May 2026 — initial scaffold session.
Next session will tackle P0 items (see BACKLOG.md).
