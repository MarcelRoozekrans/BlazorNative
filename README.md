# BlazorNative

[![ci](https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml/badge.svg)](https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/BlazorNative.Runtime.svg)](https://www.nuget.org/packages?q=BlazorNative)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**Write your mobile app in Blazor. Ship real native widgets — no WebView, no JavaScript, no WebAssembly.**

BlazorNative compiles your Blazor components ahead-of-time with **NativeAOT** and renders
them as **actual Android and iOS views** (`TextView`/`UILabel`, `Button`, `EditText`/
`UITextField`, `ScrollView`…). No React Native, no Flutter, no MAUI — just .NET, a typed
C-ABI, and Facebook's Yoga flexbox engine doing the layout on both platforms.

📖 **[Documentation](https://marcelroozekrans.github.io/BlazorNative/)** · 📦 **[NuGet packages](https://www.nuget.org/packages?q=BlazorNative)**

---

## How it works

1. Your Blazor UI and logic compile **ahead-of-time into a native shared library** (one per platform/ABI).
2. A headless `NativeRenderer` drives the Blazor render tree and emits **typed struct patches** (create node, set style, replace text…) across a **10-export C-ABI** — no interpreter, no JSON on the frame path.
3. A thin native shell (Kotlin on Android, Swift/UIKit on iOS) reads the patches and builds **two mirrored trees**: real platform widgets, and a **Yoga node tree** beside them. Layout style names go to Yoga, visual names to the view; leaves are measured natively; Yoga computes and every child is placed at its computed frame.

One runtime, one transport, one layout engine — the same NativeAOT library, typed-struct
protocol, and Yoga tree run everywhere.

## Status

> **Pre-1.0, published, and hardened.** Milestones 1–11 are complete and the packages are
> **on nuget.org as stable releases** (no `--prerelease` needed). The runtime is a frozen
> 80-byte / 10-export C-ABI; Android is **device-proven**, diagnostic logging is level-gated
> and quiet by default, and render faults surface rather than crash.

**1.0 is blocked on exactly one thing: a real iPhone.** iOS today is **simulator-only** —
real-device iOS (code signing, provisioning, App Store validation, Secure Enclave, APNs)
needs an Apple Developer account and is deferred. Everything else the 1.0 criteria ask for
is met. See [API stability](https://marcelroozekrans.github.io/BlazorNative/docs/api-stability)
for the tier table, the compatibility statement, and the full 1.0 checklist.

**Honest caveats** (deliberate, documented):

- The **public API is marked but not frozen** — a per-package `PublicAPI.Shipped.txt`
  baseline gates every change, but this is still `0.x` and a minor version may break the
  surface on purpose. The *stable core* (the `Bn*` components + parameters, the `[Inject]`
  device façades, the capability result types, `BlazorNativeApp`) is what freezes at 1.0.
- **Render faults log-and-continue** (`rc 0`) — crashing a running app over one bad click
  handler would be worse — except a parameter-binding fault, which aborts the mount with
  `rc 2` instead of reporting success over a half-rendered screen. There is no *programmatic*
  error channel beyond those return codes yet.

## Quick start

```powershell
# Windows — installs/verifies prerequisites (.NET 10 SDK, JDK 21, Android SDK + NDK)
powershell -ExecutionPolicy Bypass -File setup.ps1

# Publish the runtime for the target you want
dotnet publish src\BlazorNative.Runtime -c Release -r win-x64            # JVM dev loop (.dll)
dotnet publish src\BlazorNative.Runtime -c Release -r linux-bionic-arm64 # Android device (.so)

# Fast inner loop: watches .NET source, re-publishes → previews the widget tree on each save
powershell -ExecutionPolicy Bypass -File scripts\devloop.ps1             # JVM fast lane (~10 s/save)
powershell -ExecutionPolicy Bypass -File scripts\devloop.ps1 -Android    # device lane (~14 s/save)
```

Scaffold a new app with the template:

```sh
dotnet new install BlazorNative.Templates
dotnet new blazornative -n MyApp    # the .NET app + a runnable Android shell
```

Full setup, the architecture story, the generated component reference, and both shell guides
live on the **[documentation site](https://marcelroozekrans.github.io/BlazorNative/)**.

## What it looks like

You write plain Razor. Yoga computes the same frames on both platforms — proven by tests that
assert the *same numbers* on an Android emulator and an iOS simulator, frame for frame.

```razor
<BnColumn Gap="16" Padding="16">

  @* Grow absorbs the free space: the middle box computes to exactly 200 on both platforms *@
  <BnRow Width="300" Height="100">
    <BnView Width="50" BackgroundColor="#E57373" />
    <BnView Grow="1"   BackgroundColor="#64B5F6" />
    <BnView Width="50" BackgroundColor="#81C784" />
  </BnRow>

  <BnRow Justify="FlexJustify.SpaceBetween" Align="FlexAlign.Center">
    <BnText Text="Left" />
    <BnText Text="Right" />
  </BnRow>

  @* BnScroll is a VIEWPORT: give it a definite height, compose the content inside *@
  <BnScroll Height="200">
    <BnColumn Gap="8">
      @foreach (var row in Rows)
      {
        <BnRow Height="80"><BnText Text="@row" /></BnRow>
      }
    </BnColumn>
  </BnScroll>

</BnColumn>
```

## Features

- **Component library** — `BnView` (the flex surface), `BnRow`/`BnColumn` presets, `BnScroll`
  (a real scrolling viewport), `BnText`/`BnButton`/`BnInput`/`BnImage`, `BnList` (virtualized),
  `BnModal`, the form controls (`BnCheckbox`/`BnPicker`/`BnSlider`/`BnSwitch`),
  `BnActivityIndicator`, and `BnTheme`.
- **`.razor` authoring under NativeAOT** — `@bind`, cascading values, keyed lists, real
  disposal, event dispatch (`@onclick` → native tap → .NET handler → re-render).
- **Yoga owns all placement** — you write typed flex parameters in C#; both shells compute
  identical frames. Native text measurement means a long label wraps and its measured height
  drives its row.
- **Programmatic scrolling** — `ScrollToAsync` / `ScrollToEndAsync` / `AutoScrollToEnd` on
  `BnScroll`, for append-driven views (log tails, chat). The command rides the frame that
  changed the content, so the shells scroll to the *new* end, not the old one.
- **Host capabilities via one bridge pattern** — geolocation, notifications, biometrics,
  secure storage, and camera, each an `[Inject]`-able façade over a permission-gated async
  host call. Plain `HttpClient` works too (routed through the shell).
- **A real dev loop** — a native fast lane, an Android device lane, and a localhost inspector
  (a DevTools page over a live native session: widget tree, patch stream, dispatch-from-page).

## Limitations (honest boundaries)

- **iOS is simulator-only** — real-device iOS is deferred pending an Apple Developer account.
- **No universal links on iOS** — the `blazornative://` custom scheme works on both platforms, but iOS universal links (and Android https App Links) need a domain and per-platform verification files, so they are deferred with real-device iOS.
- **`onDestroy` is best-effort, and weaker on iOS** — iOS routinely kills a suspended app without calling `applicationWillTerminate`. Persist on `onPause`, which fires every time on both platforms.
- **No density-aware image sources** — one file pixel = one dp/pt, so a `@2x` asset renders at 2× size.
- **No horizontal scroll, and no scroll-offset restore** across navigation — `BnScroll` *does* have programmatic scrolling (`ScrollToAsync`, `ScrollToEndAsync`, `AutoScrollToEnd`), but a route change rebuilds the page tree and nothing remembers where you were.
- **HTTP responses are fully buffered, as UTF-8 text** — the bridge delivers a body once, complete; SSE / chunked / long-poll degrade to polling, and binary bodies are unsupported.
- **`picker` does not flex its children** — it runs its own internal layout (the node itself is placed correctly).
- **`alignContent`, `rowGap`, `columnGap`, `display`, `flex`** are not implemented — every accepted style name is a name three parsers must implement.

The [documentation site](https://marcelroozekrans.github.io/BlazorNative/) tracks these as a ledger.

## Architecture

```
[Blazor components]   your UI — plain Razor/C# (BnView / BnRow / BnColumn / BnScroll …)
        │
[BlazorNative.Renderer]   headless NativeRenderer + typed RenderPatch model
        │
[BlazorNative.Runtime]    NativeAOT composition root + 10-export C-ABI (init / mount /
        │                 register_frame_callback / dispatch_event / register_bridge / …)
        │  one native library per platform:
        │    BlazorNative.Runtime.dll   win-x64            — JVM dev loop
        │    libBlazorNative.Runtime.so linux-bionic-*     — Android (cross-compiled on Windows)
        │    BlazorNative.Runtime.a     iossimulator-arm64 — static archive, linked into the app
        │
        ├───────────── JNA (cdecl) ─────────────┬────────── direct static link ──────────┐
        │                                        │                                         │
[BlazorNative.Jni]  Kotlin shell          [BlazorNative.Apple]  Swift/UIKit shell
        │                                        │
        └──────── each shell builds TWO mirrored trees ────────┘
                    │                             │
             [view tree]                   [Yoga node tree]
        real platform widgets         Yoga 3.2.1 (Facebook's C++ flexbox)
        (TextView/UILabel, Button,      Android: com.facebook.yoga (Maven JNI)
         EditText/UITextField, …)       iOS: source-built libyoga.a via Obj-C++
                                        │
        style names partition by allow-list: layout → Yoga node, visual → the view.
        Leaves measured natively → Yoga computes → every child placed at its COMPUTED FRAME.
```

`BlazorNative.Core` / `.Renderer` / `.Http` are pure libraries; `BlazorNative.Runtime` is the
publishable composition root that owns the `[UnmanagedCallersOnly]` exports. The style-routing
table is hand-written in three places (`NativeRenderer.cs`, `YogaLayout.kt`, `BnYogaLayout.mm`)
and a required-lane drift test asserts all three agree — a name in one and missing from another
is dropped silently, not loudly. The deeper story (the wire protocol, the layout contract, the
dev loop, logging) is on the [documentation site](https://marcelroozekrans.github.io/BlazorNative/).

## Test surface

Each count is asserted by a workflow, but **only `build-test` gates a pull request** — a drift
in the **.NET** or **JVM** count fails the PR build; the **Android** and **iOS** counts are
asserted in advisory lanes (a drift there reds that lane, not your PR). A required-lane test
re-parses this table against the gates, so these numbers can't quietly go stale.

| Surface | Command | Count | Asserted by |
|---|---|---|---|
| .NET | `dotnet test` | 993 passed / 0 skipped | `ci.yml` → `build-test` — **required, gates the PR** |
| JVM (JNA + win-x64 .dll) | `gradlew testDebugUnitTest` | 161 | `ci.yml` → `build-test` — **required, gates the PR** |
| Android (instrumented, AVD) | `gradlew connectedAndroidTest` | 224 | `android-instrumented.yml` — advisory (nightly/dispatch) |
| iOS (XCTest, simulator) | `xcodebuild test` | 269 | `ios.yml` — advisory (on-merge/dispatch) |

## Prerequisites

| Tool | Purpose | Required |
|---|---|---|
| .NET 10 SDK (see `global.json`) | build + NativeAOT publish | ✅ |
| Temurin JDK 21 | Gradle / Kotlin shell | ✅ |
| Android SDK + NDK 26.3 | bionic cross-compile + emulator | for Android |
| Xcode (macOS) | iOS-simulator shell + XCTest | for iOS |

`setup.ps1` installs and pins everything on Windows. The Android `.so`s are produced **directly
on Windows** — .NET 10 ships no `linux-bionic-*` ILCompiler, so `build/BionicNativeAot.targets`
uses the runtime-pack bypass and links against the NDK.

## Project structure

```
src/
  BlazorNative.Core/        IMobileBridge contract + bridge impls   (library)
  BlazorNative.Renderer/    headless NativeRenderer + RenderPatch    (library)
  BlazorNative.Http/        BridgeHttpHandler + DI                   (library)
  BlazorNative.Components/   the Bn* component library
  BlazorNative.Analyzers/   Roslyn analyzers
  BlazorNative.Runtime/     NativeAOT composition root + C-ABI exports (published head)
  BlazorNative.Jni/         Kotlin shell (Android + JVM tests)
  BlazorNative.Apple/       Swift/UIKit shell (iOS simulator)
samples/                    the demo app + a blank consumer smoke
templates/                  dotnet new blazornative
website/                    the Docusaurus docs site (reference generated at build time)
tests/                      renderer / runtime / analyzer suites
```

## Compatibility

Designed to be compatible with [ZeroAlloc-Net](https://github.com/ZeroAlloc-Net) libraries —
core types are AOT-safe, zero-allocation friendly, and use `readonly record struct` throughout.

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for setup, the build/test
gate, and the commit/release conventions. A [Contributor License Agreement](CLA.md) must be
signed before a first contribution can be merged; it keeps the framework core MIT for consumers
while preserving the maintainer's dual-licensing options.

## License

MIT — see [LICENSE](LICENSE). Contributions are made under the [CLA](CLA.md).
