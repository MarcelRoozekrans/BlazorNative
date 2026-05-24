# BlazorNative

> .NET → WASM → Native mobile. Blazor apps compiled to WASI and run on Android/iOS via a thin native shell.

BlazorNative is a proof-of-concept framework for running .NET Blazor applications as native mobile apps — without React Native, Flutter, or MAUI. The approach:

1. Your Blazor UI and business logic run on the **Mono runtime compiled to WebAssembly** (via the `wasi-experimental` workload), with the application IL **AOT-compiled into the same WASM module** using `wasi-sdk`
2. A thin native shell (Kotlin on Android, Swift on iOS) embeds Wasmtime and loads the binary
3. A typed WIT contract (`mobile-bridge.wit`) defines the boundary between .NET and native code

## Quick start

```powershell
# Windows — installs all prerequisites automatically (.NET 10, wasi-sdk-25, wasmtime v45, ...)
powershell -ExecutionPolicy Bypass -File setup.ps1

# Then start the dev host (hot reload, no WASM compile)
dotnet watch run --project src\BlazorNative.Host.Android\BlazorNative.DevHost.csproj
```

Or with `make`:

```bash
make setup     # install workloads (Windows: prefer setup.ps1 — installs wasi-sdk + wasmtime too)
make dev       # hot reload dev host → https://localhost:5273
make wasi      # publish BlazorNative.WasiHost → AOT'd .wasm in bin/Release/.../AppBundle/
make wasi-run  # publish + execute via wasmtime
make wasi-test # publish + boot smoke test
```

## Architecture

```
[Blazor Components]      ← your UI, shared across all targets
        ↓
[BlazorNative.Core]      ← IMobileBridge contract, WasiBridge, DevHostBridge (library)
[BlazorNative.Renderer]  ← NativeRenderer + RenderFrame patch protocol (library)
[BlazorNative.Http]      ← BridgeHttpHandler (library)
        ↓
[BlazorNative.WasiHost]  ← executable composition root — Mono-AOT'd to .wasm via wasi-sdk
        ↓
[mobile-bridge.wit]      ← canonical WIT interface (generates C#/Kotlin/Swift)
        ↓  WASI P/Invoke
[Native Shell]           ← thin Kotlin (Android) or Swift (iOS) host
        ↓
[Wasmtime embedded]      ← runtime inside the app package
```

`BlazorNative.Core` / `.Renderer` / `.Http` are pure libraries — they do not produce executables and do not own `Main`. `BlazorNative.WasiHost` is the executable composition root that wires DI, owns the WASI entry point, and is the project that gets published with `-r wasi-wasm` to produce the AOT'd `.wasm` module.

## Dev experience

The inner loop runs as a **normal ASP.NET app** — no WASM compilation, full hot reload, and a DevTools REST API for simulating native events:

```bash
# Inject a native event during development
curl -X POST https://localhost:5273/dev/event \
  -H 'Content-Type: application/json' \
  -d '{ "name": "push", "payload": "hello" }'

# Inspect storage state
curl https://localhost:5273/dev/storage
```

## Prerequisites

| Tool | Purpose | Required |
|---|---|---|
| .NET 10 SDK | Everything | ✅ |
| `wasi-experimental` workload | Mono runtime + WASM build targets | ✅ |
| `wasi-sdk` 25.0 | C toolchain for Mono-AOT'ing app IL into the .wasm | ✅ Required for `make wasi` / `dotnet publish -r wasi-wasm` |
| `wasmtime` CLI v45 | Run the produced `.wasm` locally (and used by `make wasi-test`) | ✅ |
| `maui-android` workload | Android build | When needed |
| Android SDK | Android build | When needed |
| Java 17+ | Android toolchain | When needed |
| Rust + wit-bindgen | Regenerate WIT bindings | Optional |

Run `setup.ps1` on Windows to install everything automatically — it pins `wasi-sdk-25` (the workload rejects newer versions) and `wasmtime v45`, both extracted to `C:\Tools\`, with `WASI_SDK_PATH` and `PATH` updated at user scope.

## Project structure

```
BlazorNative/
├── setup.ps1                              ← prerequisite installer (Windows)
├── Makefile                               ← dev/wasi/android/wit-gen targets
├── src/
│   ├── BlazorNative.Core/                 ← IMobileBridge, WasiBridge, DevHostBridge (library)
│   ├── BlazorNative.Renderer/             ← NativeRenderer + RenderFrame patch protocol (library)
│   ├── BlazorNative.Http/                 ← BridgeHttpHandler + DI (library)
│   ├── BlazorNative.Analyzers/            ← Roslyn analyzers (BN0001–BN0013)
│   ├── BlazorNative.Blazor/               ← Razor components
│   ├── BlazorNative.WasiHost/             ← executable composition root, published to wasi-wasm
│   └── BlazorNative.Host.Android/         ← DevHost (ASP.NET) + DevTools API
├── tests/
│   └── BlazorNative.Wasi.Tests/           ← WasiPublishFixture + BootSmoke (wasmtime integration)
└── tools/
    └── wit/mobile-bridge.wit              ← canonical WIT contract
```

## Roadmap

- [x] Core scaffold + WIT contract
- [x] DevHostBridge (hot reload dev experience)
- [x] WasiBridge (WASI P/Invoke skeleton)
- [x] wasi-wasm publish passing (Mono-AOT via wasi-sdk-25)
- [x] WasiHost boots under wasmtime (BootSmoke green)
- [ ] Wasmtime round-trip call (Android JNI)
- [ ] Blazor canvas render bridge
- [ ] Native widget renderer (VDOM patch protocol)
- [ ] iOS Swift shell

## Compatibility

Designed to be compatible with [ZeroAlloc-Net](https://github.com/ZeroAlloc-Net) libraries — all core types are AOT-safe, zero-allocation friendly, and use `readonly record struct` throughout.

## License

MIT
