# BlazorNative

[![ci](https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml/badge.svg)](https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml)

**[Documentation](https://marcelroozekrans.github.io/BlazorNative/)** — getting started, the architecture story, the component reference (generated from the components' own doc comments), the parity contract, and both shell setup guides.

> **Status: pre-release proof of concept.** Milestones 1–9 are complete, and the seven packages are **published on nuget.org** (v0.1.0 — a stable release, no `--prerelease` needed). Milestone 8 (Developer Ecosystem) shipped publish-ready packages, `dotnet new blazornative`, and the public docs site; Milestone 9 (Platform Breadth) added the host-capability bridge pattern — geolocation, notifications, biometrics + secure storage, and camera — on top of Milestone 7's `.razor` authoring and component library and Milestone 6's Yoga engine, which owns all placement on both shells and asserts identical frame tables on the Android emulator and the iOS simulator. **Milestone 10 (Consolidation & Hardening) is in progress.** Not production-ready — the API surface is unstable and changes without notice; iOS is simulator-only (real-device iOS is deferred).

> .NET → NativeAOT → native mobile widgets. Blazor components rendered as real Android and iOS views, no WebView, no JavaScript, no wasm.

BlazorNative is a proof-of-concept framework for running .NET Blazor applications as native mobile apps — without React Native, Flutter, or MAUI. The approach:

1. Your Blazor UI and business logic are compiled **ahead-of-time into a platform-native shared library** (`BlazorNative.Runtime`) — a .NET NativeAOT binary, one per platform/ABI
2. A headless `NativeRenderer` drives the Blazor render tree and emits **typed struct patches** (create node, set style, replace text, …) through a C-ABI frame callback
3. A thin native shell (Kotlin on Android, Swift/UIKit on iOS) loads the library, reads the patch structs, and builds **two mirrored trees**: a tree of real platform widgets (`TextView`/`UILabel`, `Button`, `EditText`/`UITextField`, …) and a **Yoga node tree** beside it. Style names are partitioned by an allow-list — layout names (`flexDirection`, `justifyContent`, `width`, `margin`, …) go to the Yoga node, visual names (`backgroundColor`, `color`, `fontSize`, …) to the view. Text and other leaves are **measured natively** through Yoga's measure callback, Yoga computes, and every child is placed at its **computed frame**. Containers are plain layout-suppressed frame containers that never re-place a child themselves.

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
[Blazor Components]           ← your UI, plain Razor/C# (BnView / BnRow / BnColumn / BnScroll …)
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
   BlazorNative.Runtime.a        iossimulator-arm64 — static archive, linked into the app
        ↓                             ↓
   JNA (cdecl callback)          direct static link (C-ABI, no JNA)
        ↓                             ↓
[BlazorNative.Jni]            [BlazorNative.Apple]
   Kotlin shell:                 Swift/UIKit shell:
   NativeBindings →              BnBridge → BnFrameAdapter (offset reads)
   NativeFrameAdapter →          → BnWidgetMapper
   WidgetMapper
        ↓                             ↓
        └──────── each shell builds TWO mirrored trees ────────┘
                              ↓
        [view tree]                        [Yoga node tree]
   real platform widgets              Yoga 3.2.1 (Facebook's C++ flexbox engine)
   TextView / UILabel                   Android: com.facebook.yoga:yoga (Maven JNI)
   Button, EditText / UITextField        iOS: source-built libyoga.a, reached through
   ScrollView / UIScrollView                  Objective-C++ behind a plain-C surface
   containers = layout-suppressed             (Yoga's C++ headers can never be
   frame containers                            visible to Swift)
   (BnYogaFrameLayout / plain UIView)
                              ↓
   style names are partitioned by an allow-list:
      layout names  → the Yoga node   (flexDirection, justifyContent, width, margin, …)
      visual names  → the View        (backgroundColor, color, fontSize, …)
   leaves are measured natively via Yoga's measure callback (a long label wraps,
   and its measured height drives its row)
                              ↓
   Yoga computes → every child is placed at its COMPUTED FRAME
```

One runtime, one transport, one layout engine: the same NativeAOT library, typed-struct protocol and Yoga tree run everywhere — the JVM desktop loop is the fast feedback surface, the Android `.so` and the iOS `.a` are the product. There is no interpreter and no serialization on the frame path.

The style routing table is hand-written in three places (`NativeRenderer.cs`, `YogaLayout.kt`, `BnYogaLayout.mm`); a drift test in the required CI lane parses all three and asserts set-equality, because a name present in one and missing from another is silently dropped rather than failing loudly.

`BlazorNative.Core` / `.Renderer` / `.Http` are pure libraries. `BlazorNative.Runtime` is the publishable composition root that wires DI and owns the `[UnmanagedCallersOnly]` export surface.

## Layout and styling

**Yoga owns all placement.** You write typed flex parameters in C#; they ride the existing `SetStyle` wire (no ABI change), and both shells compute the same frames from them. The proof is a test result, not a claim: `BnLayoutDemo` and `BnScrollDemo` assert the *same numbers* on an Android emulator and an iOS simulator, frame for frame.

`BnView` carries the flex surface:

| | Parameters |
|---|---|
| **Container** | `Direction` · `Justify` · `Align` · `Wrap` · `Gap` · `Padding` |
| **Item** | `AlignSelf` · `Grow` · `Shrink` · `Basis` · `Margin` |
| **Size** | `Width` · `Height` · `MinWidth` · `MaxWidth` · `MinHeight` · `MaxHeight` |
| **Position** | `Position` · `Top` · `Right` · `Bottom` · `Left` |
| **Visual** | `BackgroundColor` |

`BnRow` and `BnColumn` are thin presets over it — they forward every parameter *except* `Direction`, because a `BnRow` **is** a row. Reach for `BnView` when the direction is dynamic. **There is deliberately no `BnStack`**: it would be a synonym for `BnColumn`, and two names for one thing is a library smell on day one.

```razor
<BnColumn Gap="16" Padding="16">

  @* Grow absorbs the free space: box B computes to exactly 200 on both platforms *@
  <BnRow Width="300" Height="100">
    <BnView Width="50" BackgroundColor="#E57373" />
    <BnView Grow="1"   BackgroundColor="#64B5F6" />
    <BnView Width="50" BackgroundColor="#81C784" />
  </BnRow>

  <BnRow Justify="FlexJustify.SpaceBetween" Align="FlexAlign.Center">
    <BnText Text="Left" />
    <BnText Text="Right" />
  </BnRow>

  @* A long label wraps, and its NATIVELY MEASURED height drives its row *@
  <BnRow Width="150">
    <BnText Text="A label long enough to wrap onto several lines." />
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

**`BnScroll` is a flex *item*, not a flex *container*.** It has no `Direction`/`Justify`/`Align`/`Wrap`/`Gap`/`Padding` by construction — those would style the *viewport*, whose only child is a shell-synthesised content node, and `Justify="Center"` over 800dp of content in a 200dp viewport would offset it to y = −300 and make the top of the page permanently unreachable. To shape the content, compose a `BnColumn Gap="8"` **inside** the scroll (React Native's `contentContainerStyle`, without a second style surface). The shells enforce the same rule at the wire, so the raw-element hatch is closed by the same sentence.

Give a `BnScroll` a **definite height** (`Height`, or `Grow="1" Basis="0"` in a bounded parent). `Grow="1"` alone leaves `flexBasis: auto` — the basis becomes the *content's* height, the free space is negative, and `flexGrow` distributes only positive free space, so the viewport hugs its content and never scrolls. That is exactly why CSS's `flex: 1` shorthand sets basis to `0`; the shells emit a diagnostic when a viewport is indefinite.

Three of the demo app's pages exist to prove this section: **`/layout`** (`BnLayoutDemo` — row/column/wrap/`Grow`/`AlignSelf` with a natively measured text leaf), **`/scroll`** (`BnScrollDemo` — a 300×200 viewport over 800dp of content that actually scrolls) and **`/image`** (`BnImageDemo` — a fixed-size image that never reflows, an intrinsic-size image whose loaded bytes reflow the sibling below it, and a failing URL that reserves nothing).

### Not yet

Honest boundaries, all ledgered:

- **No density-aware image sources.** The unit rule is one file pixel = one dp/pt, so a `@2x` asset renders at 2× its intended physical size on both platforms. (`BnImage`'s surface itself is no longer minimal — `PlaceholderColor`, `OnError` and `ContentMode`'s four modes shipped in M7, each with its own design, because each one had to answer to *measurement*.) The demo app has no fixture server of its own, so `/image` shows three failed loads outside the test targets — by design.
- **`picker` does not flex its children** — `Spinner`/`UIPickerView` are framework containers that run their own layout inside themselves. The picker node itself is placed correctly by its parent.
- **No horizontal scroll.** Android's `ScrollView` is vertical-only; horizontal is a different widget class that would have to be chosen at `CreateNode` from a `flexDirection` that arrives in a *later* `SetStyle` patch.
- **No `scrollTo`**, and no scroll-offset restore across navigation. (`onScroll` itself shipped in M7 — it got the design its 60 Hz demanded, and arrives conflated rather than queued, so you cannot count ticks with it.)
- **`alignContent`, `rowGap`, `columnGap`, `display`, `flex`** are accepted by nothing — no typed parameter, no producer. Every accepted name is a name three parsers must implement.
- **iOS is simulator-only.** Real-device iOS (code signing, provisioning, App Store validation) needs an Apple Developer account and is **deferred** — that account is the trigger. (Milestone 9 delivered the rest of platform breadth; real-device iOS was the one item it carried forward.)

## Dev experience

Three lanes, honestly labeled:

| Lane | Command | Feedback model | What it exercises |
|---|---|---|---|
| Native fast lane | `make devloop` | **Fast-restart**, ~10–11 s/save | The real thing: NativeAOT publish → JNA load → C-ABI patch decode → console tree dump (`PreviewHost`) |
| Device lane | `make devloop-android` | **Fast-restart**, ~14 s/save | The full APK: bionic publish → `installDebug` → launch → logcat boot marker on an emulator/device |
| Inspector | `make inspect` | **Fast-restart** (long-lived session) | A localhost DevTools page over a **live native session** (`InspectorHost`): collapsible widget tree, live patch stream, event log, and dispatch-from-the-page (fire clicks, send change payloads) — all against the published NativeAOT dll |

**Fast-restart, not hot-reload — by design, not omission.** JNA's `Native.load` is process-lifetime: there is no unload API, and Windows locks the loaded dll, so a warm JVM can never pick up a rebuilt native library — and NativeAOT binaries cannot hot-patch. The native loop therefore restarts a tiny host process per cycle (`PreviewHost`: boot → mount → dump tree → exit ~0.3 s), and the loop script makes that restart automatic: save a `.cs` file, get a fresh widget tree.

Measured on the dev machine (warm, `devloop.ps1 -Once`, one `BlazorNative.Components` file touched):

| Stage | Time |
|---|---|
| Incremental win-x64 NativeAOT publish | ~8–9 s |
| PreviewHost boot-to-tree (dll load + init + mount) | ~0.3 s |
| Full JVM-lane cycle (publish → tree) | ~10–11 s |
| Full ADB-lane cycle (publish → install → launch → mounted) | ~14 s |

The NativeAOT publish dominates both lanes; everything downstream of it is seconds or less.

**The inspector rides the native session.** `make inspect` serves http://localhost:5199 over the same NativeAOT dll, C-ABI frames, and dispatch lane the Android app rides. It is fast-restart like everything native: restart the host (rerun `make inspect`) to pick up a rebuilt dll; `PORT=n` / `COMPONENT=Name` override the defaults. The page is one self-contained inline HTML file (no CDN, no build step): widget tree (`<details>`-collapsible, auto-refreshing over SSE), patch tail, event log, and per-node dispatch buttons that call `POST /api/dispatch` on the live session.

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
│   ├── BlazorNative.Analyzers/            ← Roslyn analyzers
│   ├── BlazorNative.Components/           ← Bn* component library: BnView (flex surface),
│   │                                         BnRow/BnColumn (presets), BnScroll (viewport),
│   │                                         BnText/BnButton/BnInput/BnImage, BnList
│   │                                         (virtualized), BnModal, the form controls
│   │                                         (BnCheckbox/BnPicker/BnSlider/BnSwitch),
│   │                                         BnActivityIndicator, BnTheme
│   ├── BlazorNative.Runtime/              ← NativeAOT composition root + C-ABI exports + FrameEncoder
│   ├── BlazorNative.Jni/                  ← Kotlin shell: JNA bindings, frame adapter, WidgetMapper,
│   │                                         YogaLayout, MainActivity (Android + JVM tests)
│   └── BlazorNative.Apple/                ← Swift/UIKit shell (iOS simulator): BnBridge, frame
│                                             adapter, BnWidgetMapper, BnYogaLayout.mm (Obj-C++
│                                             over libyoga.a), XCTest suite + vendored Yoga
├── samples/
│   ├── BlazorNative.SampleApp/            ← the demo app: the NativeAOT publish head, and the
│   │                                         library's first real CONSUMER — its pages live
│   │                                         here rather than inside Components (Phase 8.0)
│   └── ConsumerSmoke/                     ← the blank consumer the smoke mounts against the
│                                             packed nupkgs
├── templates/BlazorNative.Templates/      ← `dotnet new blazornative`: the .NET app + a runnable
│                                             Android shell
├── website/                               ← the Docusaurus docs site (docs.yml deploys it; the
│                                             component reference is GENERATED at build time and
│                                             never committed)
└── tests/
    ├── BlazorNative.Renderer.Tests/       ← renderer, bridge, trim-safety, frame-sink tests
    ├── BlazorNative.Runtime.Tests/        ← encoder/arena/protocol + typed Hello golden test
    └── BlazorNative.Analyzers.Tests/      ← analyzer harness
```

## Test surface

Each count is asserted by a workflow — but **not all four gate a pull request,
and the honest split matters.** Only the `build-test` lane is a required check, so a
drift in the **.NET (780)** or the **JVM `testDebugUnitTest` (120)** count **fails the
PR build** — both are load-bearing, and the JVM guard is not a formality: it caught a
real break in Phase 10.1. The **Android (209)** and **iOS (235)** counts are asserted in
the `android-instrumented.yml` (nightly + manual dispatch) and `ios.yml` (on merge to
`main` + manual dispatch) lanes, which are **advisory, not required** — a drift there reds
that lane, not your PR. The `Asserted by` column below names which is which.

| Surface | Command | Count | Asserted by |
|---|---|---|---|
| .NET | `dotnet test` | 780 passed / 0 skipped | `ci.yml` → `build-test` — **required, gates the PR** |
| JVM (JNA + win-x64 .dll) | `gradlew testDebugUnitTest` | 120 | `ci.yml` → `build-test` — **required, gates the PR** |
| Android (instrumented, AVD) | `gradlew connectedAndroidTest` | 210 | `android-instrumented.yml` — advisory (nightly/dispatch) |
| iOS (XCTest, simulator) | `xcodebuild test` | 236 | `ios.yml` — advisory (on-merge/dispatch) |

**The gate is the truth; this table is a copy of it.** When the two disagree, the workflow is
right — and they have disagreed before: for four milestones this table read 333 / 83 / 111 / 72
while the gates asserted otherwise, and not one of the four was within 50% of reality. Nothing
re-runs a number on a page.

**So a gate re-runs these.** Since Phase 8.5, `ReadmeDriftTests` (in `build-test`, the required
lane) parses each number above and compares it to the `if` condition that actually decides the
corresponding gate — the code, not the step's name, which is prose that has drifted too. The
same test holds the Yoga version literal in the architecture diagram above against the version
`build.gradle.kts` pins — the fifth home of a number `ci.yml`'s parity step already holds in
four. The numbers still live on this page; they are simply no longer kept true by someone
remembering.

*(This paragraph originally named that version — a third copy of the literal, written into the
sentence explaining that the literal has one home. The pin above caught it: with the diagram's
copy deleted as a test, the suite stayed green, because the prose copy was still satisfying it.
Phase 8.4's Gate 3 author did the same thing while removing a different copy. The pull toward a
fresh copy is not theoretical.)*

## Status

- [x] Headless Blazor renderer with typed patch protocol (composition-grade: nested components, keyed lists, real disposal)
- [x] NativeAOT runtime for win-x64 + linux-bionic-x64/arm64 (Android) + iossimulator-arm64 — nine-export C-ABI
- [x] Bidirectional events (`@onclick` → native tap → .NET handler → re-render) — Phase 3.2
- [x] Shell bridge as host-registered C-ABI callbacks (navigate/storage/fetch, plain `HttpClient` works on Android) — Phase 3.1
- [x] `Bn*` component library, `@bind` mechanics, cascading values, navigation — a demo app on the AVD (~1.6 s cold boot) — Milestone 3
- [x] Public repo + CI, analyzer rescope, hardening triage, dev inner loop, NuGet packages — Milestone 4
- [x] Full platform coverage — the **iOS Swift/UIKit shell** (simulator, on CI macOS runners) + host-initiated events (lifecycle, predictive back, deep links) on Android — Milestone 5
- [x] **Real-UI foundation — Milestone 6**
  - [x] Yoga linked into both shells — Phase 6.0
  - [x] Yoga owns all placement; native text measurement; `BnView`'s flex surface + `BnRow`/`BnColumn` — Phase 6.1
  - [x] Real scrolling — `BnScroll` as a viewport over a synthesised content node — Phase 6.2
  - [x] URL images — `BnImage` via Coil/Kingfisher behind one parity contract — Phase 6.3
  - [x] Milestone audit (all 8 DoD PASS) + a required compile gate per shell (`build-test`/`android-build`/`ios-build`) — Phase 6.4
- [x] **Components + Razor — Milestone 7**
  - [x] `.razor` authoring under NativeAOT — the demo pages rewritten as the parity proof, patch stream byte-identical to their hand-written twins — Phases 7.0/7.1
  - [x] The `onScroll` wire design + `BnList`, the virtualized list that forced it — Phase 7.2
  - [x] Form controls + a real `picker` — Phase 7.3
  - [x] `BnModal`, the first overlay surface + the RN parity survey's cheap wins — Phase 7.4
  - [x] `BnImage` polish — `PlaceholderColor` / `OnError` / `ContentMode`, each its own *measurement* design — Phase 7.5
  - [x] Route-registry unification + milestone audit (all 8 DoD PASS) — Phase 7.6
- [x] **Developer Ecosystem — Milestone 8**: publish-ready packages, `dotnet new blazornative`, and a public docs site — **published to nuget.org (v0.1.0)** with release automation (release-please auto-publish on merge)
- [x] **Platform Breadth — Milestone 9**: the host-capability bridge pattern — geolocation, notifications, biometrics + secure storage, camera — each a permission-gated async host call with **zero further ABI change** (real-device iOS deferred)
- [ ] **Consolidation & Hardening — Milestone 10** (in progress): low-severity hardening, docs/README accuracy, and precision fixes

The demo app's pages are declared once, in `samples/BlazorNative.SampleApp/SampleAppPages.cs` — that array is the roster, and the runtime's mount registry and route table are derived views of it.

## Compatibility

Designed to be compatible with [ZeroAlloc-Net](https://github.com/ZeroAlloc-Net) libraries — all core types are AOT-safe, zero-allocation friendly, and use `readonly record struct` throughout.

## License

MIT
