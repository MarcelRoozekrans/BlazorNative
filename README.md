# BlazorNative

> .NET → WASM → Native mobile. Blazor apps compiled to WASI and run on Android/iOS via a thin native shell.

BlazorNative is a proof-of-concept framework for running .NET Blazor applications as native mobile apps — without React Native, Flutter, or MAUI. The approach:

1. Your Blazor UI and business logic compiles to a `.wasm` binary via Native AOT + WASI
2. A thin native shell (Kotlin on Android, Swift on iOS) embeds Wasmtime and loads the binary
3. A typed WIT contract (`mobile-bridge.wit`) defines the boundary between .NET and native code

## Quick start

```powershell
# Windows — installs all prerequisites automatically
powershell -ExecutionPolicy Bypass -File setup.ps1

# Then start the dev host (hot reload, no WASM compile)
dotnet watch run --project src\BlazorNative.Host.Android\BlazorNative.DevHost.csproj
```

Or with `make`:

```bash
make setup   # install workloads
make dev     # hot reload dev host → https://localhost:5273
make wasi    # compile to wasi-wasm
make wasi-run# run via wasmtime
```

## Architecture

```
[Blazor Components]   ← your UI, shared across all targets
        ↓
[BlazorNative.Core]      ← IMobileBridge contract + AOT-compatible impl
        ↓  compiles to .wasm via Native AOT
[mobile-bridge.wit]   ← canonical WIT interface (generates C#/Kotlin/Swift)
        ↓  WASI P/Invoke
[Native Shell]        ← thin Kotlin (Android) or Swift (iOS) host
        ↓
[Wasmtime embedded]   ← runtime inside the app package
```

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
| .NET 9 SDK | Everything | ✅ |
| wasi-experimental workload | WASM compile | ✅ |
| Wasmtime CLI | Run .wasm locally | ✅ |
| maui-android workload | Android build | When needed |
| Android SDK | Android build | When needed |
| Java 17+ | Android toolchain | When needed |
| Rust + wit-bindgen | Regenerate WIT bindings | Optional |

Run `setup.ps1` on Windows to install everything automatically.

## Project structure

```
BlazorNative/
├── setup.ps1                        ← prerequisite installer (Windows)
├── Makefile                         ← dev/wasi/android/wit-gen targets
├── src/
│   ├── BlazorNative.Core/              ← IMobileBridge, WasiBridge, DevHostBridge
│   ├── BlazorNative.Blazor/            ← Razor components
│   └── BlazorNative.Host.Android/      ← DevHost (ASP.NET) + DevTools API
└── tools/
    └── wit/mobile-bridge.wit        ← canonical WIT contract
```

## Roadmap

- [x] Core scaffold + WIT contract
- [x] DevHostBridge (hot reload dev experience)
- [x] WasiBridge (WASI P/Invoke skeleton)
- [ ] wasi-wasm compile passing
- [ ] Wasmtime round-trip call (Android JNI)
- [ ] Blazor canvas render bridge
- [ ] Native widget renderer (VDOM patch protocol)
- [ ] iOS Swift shell

## Compatibility

Designed to be compatible with [ZeroAlloc-Net](https://github.com/ZeroAlloc-Net) libraries — all core types are AOT-safe, zero-allocation friendly, and use `readonly record struct` throughout.

## License

MIT
