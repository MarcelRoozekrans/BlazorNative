# BlazorNative

[![ci](https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml/badge.svg)](https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml)

> **Status: pre-release proof of concept.** Milestones 1–3 are complete (tagged `v1.0`–`v3.0`); Milestone 4 (production-shippable) is in progress. Not production-ready — the API surface is unstable and changes without notice.

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

For the native inner loop, `make devloop` watches the .NET source and re-runs publish → preview on every save (see [Dev experience](#dev-experience)):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\devloop.ps1          # JVM fast lane
powershell -ExecutionPolicy Bypass -File scripts\devloop.ps1 -Android # device lane
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

Three lanes, honestly labeled:

| Lane | Command | Feedback model | What it exercises |
|---|---|---|---|
| Browser DevHost | `make dev` | **True hot-reload** (plain ASP.NET, no AOT) | Component/business logic against a **mock** bridge — fast, but it tests the wrong transport for native work |
| Native fast lane | `make devloop` | **Fast-restart**, ~10–11 s/save | The real thing: NativeAOT publish → JNA load → C-ABI patch decode → console tree dump (`PreviewHost`) |
| Device lane | `make devloop-android` | **Fast-restart**, ~14 s/save | The full APK: bionic publish → `installDebug` → launch → logcat boot marker on an emulator/device |

**Fast-restart, not hot-reload — by design, not omission.** JNA's `Native.load` is process-lifetime: there is no unload API, and Windows locks the loaded dll, so a warm JVM can never pick up a rebuilt native library — and NativeAOT binaries cannot hot-patch. The native loop therefore restarts a tiny host process per cycle (`PreviewHost`: boot → mount → dump tree → exit ~0.3 s), and the loop script makes that restart automatic: save a `.cs` file, get a fresh widget tree.

Measured on the dev machine (warm, `devloop.ps1 -Once`, one `BlazorNative.Components` file touched):

| Stage | Time |
|---|---|
| Incremental win-x64 NativeAOT publish | ~8–9 s |
| PreviewHost boot-to-tree (dll load + init + mount) | ~0.3 s |
| Full JVM-lane cycle (publish → tree) | ~10–11 s |
| Full ADB-lane cycle (publish → install → launch → mounted) | ~14 s |

The NativeAOT publish dominates both lanes; everything downstream of it is seconds or less.

The browser-side DevHost additionally exposes a DevTools REST API for simulating native events:

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
│   ├── BlazorNative.Analyzers/            ← Roslyn analyzers (legacy pre-NativeAOT rules; rescope + tests = Phase 4.1)
│   ├── BlazorNative.Blazor/               ← Razor components
│   ├── BlazorNative.Components/           ← Bn* component library (BnView/BnText/BnButton/BnInput)
│   ├── BlazorNative.Runtime/              ← NativeAOT composition root + C-ABI exports + FrameEncoder
│   ├── BlazorNative.Jni/                  ← Kotlin shell: JNA bindings, frame adapter,
│   │                                         WidgetMapper, MainActivity (Android + JVM tests)
│   └── BlazorNative.Host.Android/         ← DevHost (ASP.NET) + DevTools API
├── tests/
│   ├── BlazorNative.Renderer.Tests/       ← renderer, bridge, trim-safety, frame-sink tests
│   ├── BlazorNative.Runtime.Tests/        ← encoder/arena/protocol + typed Hello golden test
│   └── BlazorNative.Analyzers.Tests/      ← analyzer harness
└── tools/wit/mobile-bridge.wit            ← historical bridge contract (retired — Phase 3.1 shipped the C-ABI bridge)
```

## Test surface

| Surface | Command | Count |
|---|---|---|
| .NET | `dotnet test` | 177 passed / 2 skipped |
| JVM (JNA + win-x64 .dll) | `gradlew testDebugUnitTest` | 32 |
| Android (instrumented, AVD) | `gradlew connectedAndroidTest` | 32 |

## Status

- [x] Headless Blazor renderer with typed patch protocol (composition-grade: nested components, keyed lists, real disposal)
- [x] NativeAOT runtime for win-x64 + linux-bionic-x64/arm64 (Android, built on Windows) — eight-export C-ABI
- [x] Bidirectional events (`@onclick` → native tap → .NET handler → re-render) — Phase 3.2
- [x] Shell bridge as host-registered C-ABI callbacks (navigate/storage/fetch, plain `HttpClient` works on Android) — Phase 3.1
- [x] `Bn*` component library, `@bind` mechanics, cascading values, navigation — a two-page demo app on the AVD (~1.6 s cold boot) — Milestone 3
- [ ] Public repo + CI, analyzer rescope, hardening triage, dev inner loop, NuGet packages — Milestone 4 (in progress)
- [ ] iOS Swift shell — Milestone 5

## Compatibility

Designed to be compatible with [ZeroAlloc-Net](https://github.com/ZeroAlloc-Net) libraries — all core types are AOT-safe, zero-allocation friendly, and use `readonly record struct` throughout.

## License

MIT
