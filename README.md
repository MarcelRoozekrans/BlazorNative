# BlazorNative

> .NET → NativeAOT → native mobile widgets. Blazor components rendered as real Android views, no WebView, no JavaScript, no wasm.

BlazorNative is a proof-of-concept framework for running .NET Blazor applications as native mobile apps — without React Native, Flutter, or MAUI. The approach:

1. Your Blazor UI and business logic are compiled **ahead-of-time into a platform-native shared library** (`BlazorNative.Runtime`) — a .NET NativeAOT binary, one per platform/ABI
2. A headless `NativeRenderer` drives the Blazor render tree and emits **typed struct patches** (create node, set style, replace text, …) through a C-ABI frame callback
3. A thin native shell (Kotlin on Android) loads the library via JNA, reads the patch structs, and maps them to **real platform widgets** (`LinearLayout`, `TextView`, `Button`, `EditText`, …)

## Quick start

```powershell
# Windows — installs/verifies all prerequisites (.NET 10 SDK, JDK 21, Android SDK + NDK 26.3)
powershell -ExecutionPolicy Bypass -File setup.ps1

# Publish the runtime (setup.ps1 §5 one-liners):
dotnet publish src\BlazorNative.Runtime -c Release -r win-x64            # JVM dev loop (.dll)
dotnet publish src\BlazorNative.Runtime -c Release -r linux-bionic-x64   # Android emulator (.so)
dotnet publish src\BlazorNative.Runtime -c Release -r linux-bionic-arm64 # Android device (.so)

# JVM-side tests (load the win-x64 .dll via JNA, no emulator needed)
cd src\BlazorNative.Jni; .\gradlew testDebugUnitTest

# Instrumented tests on an Android emulator/device
.\gradlew connectedAndroidTest
```

For the .NET inner loop there is also a hot-reload dev host (no AOT publish involved):

```powershell
dotnet watch run --project src\BlazorNative.Host.Android\BlazorNative.DevHost.csproj
```

## Architecture

```
[Blazor Components]           ← your UI, plain Razor/C#
        ↓
[BlazorNative.Renderer]       ← headless NativeRenderer + RenderPatch model
        ↓
[BlazorNative.Runtime]        ← NativeAOT composition root + C-ABI exports
   PatchProtocolNative /         (blazornative_init / mount / register_frame_callback / …)
   FrameEncoder                  typed-struct frames, 48 B patches / 24 B frame header
        ↓  one native library per platform
   BlazorNative.Runtime.dll      win-x64 — JVM dev loop
   libBlazorNative.Runtime.so    linux-bionic-x64 / arm64 — Android, cross-compiled
                                 from Windows via the runtime-pack bypass
        ↓  JNA (cdecl callback)
[BlazorNative.Jni]            ← Kotlin shell: NativeBindings → NativeFrameAdapter
        ↓                        (offset reads) → WidgetMapper
[Android widgets]             ← LinearLayout / TextView / Button / EditText …
```

One runtime, one transport: the same NativeAOT library and typed-struct protocol run everywhere — the JVM desktop loop is the fast feedback surface, the Android `.so` is the product. There is no interpreter and no serialization on the frame path.

`BlazorNative.Core` / `.Renderer` / `.Http` are pure libraries. `BlazorNative.Runtime` is the publishable composition root that wires DI and owns the `[UnmanagedCallersOnly]` export surface.

## Dev experience

The inner loop runs as a **normal ASP.NET app** — full hot reload plus a DevTools REST API for simulating native events:

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
| .NET 10 SDK (10.0.3xx, see `global.json`) | Build + NativeAOT publish | ✅ |
| Temurin JDK 21 | Gradle / Kotlin shell | ✅ |
| Android SDK + NDK 26.3.11579264 | bionic cross-compile + emulator | ✅ for Android |
| AVD (x86_64, API 34) | `connectedAndroidTest` | ✅ for instrumented tests |

Run `setup.ps1` on Windows to install and pin everything automatically. The Android `.so`s are produced **directly on Windows**: .NET 10 ships no `linux-bionic-*` ILCompiler packages, so the vendored `build/BionicNativeAot.targets` uses the runtime-pack bypass (`PublishAotUsingRuntimePack=true`, runtime packs 10.0.9) and links against the NDK.

## Project structure

```
BlazorNative/
├── setup.ps1                              ← prerequisite installer/verifier (Windows)
├── Makefile                               ← dev/test/android/publish targets
├── build/BionicNativeAot.targets          ← runtime-pack bypass + NDK linker hookup
├── src/
│   ├── BlazorNative.Core/                 ← IMobileBridge contract, bridge impls (library)
│   ├── BlazorNative.Renderer/             ← headless NativeRenderer + RenderPatch model (library)
│   ├── BlazorNative.Http/                 ← BridgeHttpHandler + DI (library)
│   ├── BlazorNative.Analyzers/            ← Roslyn analyzers (wasm-era rules, rescope pending)
│   ├── BlazorNative.Blazor/               ← Razor components
│   ├── BlazorNative.Runtime/              ← NativeAOT composition root + C-ABI exports + FrameEncoder
│   ├── BlazorNative.Jni/                  ← Kotlin shell: JNA bindings, frame adapter,
│   │                                         WidgetMapper, MainActivity (Android + JVM tests)
│   └── BlazorNative.Host.Android/         ← DevHost (ASP.NET) + DevTools API
├── tests/
│   ├── BlazorNative.Renderer.Tests/       ← renderer, bridge, trim-safety, frame-sink tests
│   ├── BlazorNative.Runtime.Tests/        ← encoder/arena/protocol + typed Hello golden test
│   └── BlazorNative.Analyzers.Tests/      ← analyzer harness
└── tools/wit/mobile-bridge.wit            ← historical bridge contract (3.1 redesigns as C-ABI)
```

## Test surface

| Surface | Command | Count |
|---|---|---|
| .NET | `dotnet test` | 49 passed / 2 skipped |
| JVM (JNA + win-x64 .dll) | `gradlew testDebugUnitTest` | 10 |
| Android (instrumented, AVD) | `gradlew connectedAndroidTest` | 21 |

## Status

- [x] Headless Blazor renderer with typed patch protocol
- [x] NativeAOT runtime for win-x64 + linux-bionic-x64/arm64 (Android, built on Windows)
- [x] HelloComponent renders as native Android widgets (~1.6 s cold boot)
- [ ] Bidirectional events (`@onclick` → native tap → .NET handler) — Phase 3.2
- [ ] Six `shell_*` bridge exports as C-ABI (navigate/storage/fetch) — Phase 3.1
- [ ] `Bn*` component library, `@bind`, navigation — Milestone 3
- [ ] iOS Swift shell — Milestone 4

## Compatibility

Designed to be compatible with [ZeroAlloc-Net](https://github.com/ZeroAlloc-Net) libraries — all core types are AOT-safe, zero-allocation friendly, and use `readonly record struct` throughout.

## License

MIT
