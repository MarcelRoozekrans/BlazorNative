# BlazorNative — Feature Backlog
*Last updated: May 2026*

> ## ⚠️ This file is a stale wishlist, not a plan
>
> It was written **before the 3.0e architecture collapse** (which retired the
> WASI/Wasmtime design for a direct NativeAOT C-ABI) and **before the M6
> re-plan** (which renumbered every milestone after M5). It has not been kept in
> step with either.
>
> **Items here may already have shipped, or may have been deliberately retired.**
> Unchecked boxes below are *not* evidence that something is undone, and the
> "P5 / P6 / P7" headings do **not** correspond to today's milestone numbers.
>
> **The live planning documents are [`planning/ROADMAP.md`](planning/ROADMAP.md)
> and [`planning/MILESTONE.md`](planning/MILESTONE.md).** Check there first. The
> per-phase decision log lives in [`plans/`](plans/).
>
> The file is kept because it still holds useful long-tail ideas that nothing
> else records. It is being corrected opportunistically, not rewritten.

---

## P0 — Blocks everything
*Nothing works end-to-end without these.*

- [x] **WASI `Program.cs` entry point** — **resolved 2026-05-24 (Phase 1.2)**
  Implemented in `src/BlazorNative.WasiHost/WasiEntryPoint.cs` (new project — Core stays a library; WasiHost is the executable composition root). Builds DI graph via ZeroAlloc.Inject's generated `AddBlazorNativeCoreServices()` / `AddBlazorNativeRendererServices()` / `AddBlazorNativeHttpServices()` methods, resolves `IMobileBridge` (→ WasiBridge) + `NativeRenderer`, exits 0. Root component mount deferred to M2 (would require the native shell to provide `mobile_bridge` extern imports).

- [x] **Cooperative async scheduler bootstrap** — **resolved 2026-05-24 (sort of — see below)**
  Original framing was .NET 9-era — assumed `WasiEventLoop.Run()`-style explicit bootstrap. **.NET 10 reality:** Mono-WASI runtime handles WASI bootstrap transparently. **But:** async `Main` is NOT supported — `Task.InternalWaitCore` throws `PlatformNotSupportedException` because the single-threaded WASI scheduler can't actually wait. The `dotnet new wasiconsole` template uses sync `Main` for exactly this reason. Our `WasiEntryPoint.cs` uses sync `Main` too. Cooperative async (`await Task.Delay`, etc.) on Mono-WASI is a deferred concern — either (a) .NET 11 may add support, or (b) we use it inside the Android shell where the real cooperative scheduler runs. Tracked as the new BACKLOG bullet below.

- [ ] **Cooperative async on Mono-WASI** *(deferred from P0)*
  When .NET 11 ships Mono-WASI changes that allow async `Main`, or when we're inside the Android/iOS shell, replace the sync `Main` body with the original design: DI compose → `await Task.Delay(1)` round-trip → ready signal. For now, sync `Main` is the only viable shape. **2026-05-25 update:** Phase 2.0 closed the immediate trap risk via the sync-bridge contract; this bullet is now only about the optional async-Main upgrade, no longer M2-blocking.

- [x] **Mono-WASI async trap will fire on first real bridge event** — **resolved 2026-05-25 (Phase 2.0)**
  Closed structurally — `IMobileBridge.NativeEvents` changed to `event Action<NativeEvent>`, `WasiBridge.DispatchEvent` split into native-export + managed-core paths, BN0014 analyzer enforces sync handlers at compile time. End-to-end verified by `[BOOT] event-ok fired=True name=self-test` marker. Picked option (b) from the original three (sync bridge contract, no awaits in the unmanaged-callback chain). See `docs/plans/2026-05-25-phase-2.0-design.md` + `docs/plans/2026-05-25-phase-2.0-implementation-plan.md`.

- [x] **`[UnmanagedCallersOnly]` export wiring** — **resolved 2026-05-24 (Phase 1.3)**
  Verified `blazornative_dispatch_event` is in the AOT'd `.wasm` export table via direct byte-scan in `tests/BlazorNative.Wasi.Tests/ExportSmoke.cs`. **Required the predicted explicit export hint:** `[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasiBridge))]` on `Program.Main`. Without it, Mono-AOT trimmed `WasiBridge.DispatchEvent` completely (the string was absent from the 13 MB .wasm) — `[UnmanagedCallersOnly]` alone wasn't enough of a trim root. Also discovered `wasmtime --invoke` doesn't reach core-module exports through the component-model layer; `wasm-tools print` works but its 10 MB output deadlocks subprocess pipes. In-process byte-scan was the right verification.

- [x] **Renderer internal API access strategy** — **resolved 2026-05-23**
  Original framing: `RenderTreeDiff`, `RenderTreeFrame`, `RenderBatch` are `internal` in `Microsoft.AspNetCore.Components`. Four options evaluated (A: InternalsVisibleTo fork; B: NonPublic reflection; C: public surface only; D: UnsafeAccessor).
  - **Verdict:** Option D + `[UnsafeAccessorType]` behind a single isolation file (`src/BlazorNative.Renderer/BlazorInterop.cs`). Design rationale: [docs/plans/2026-05-23-renderer-internal-api-design.md](plans/2026-05-23-renderer-internal-api-design.md). Implementation plan: [docs/plans/2026-05-23-phase-1.1-implementation-plan.md](plans/2026-05-23-phase-1.1-implementation-plan.md).
  - **Unexpected finding:** Against Microsoft.AspNetCore.Components **10.0.x**, almost every render-tree member is already accessible as a public field/property. `[UnsafeAccessor]` was genuinely required only for the protected `Renderer.DispatchEventAsync` (which takes the internal `EventFieldInfo`). The `Bn*` ref-struct wrappers in `BlazorInterop.cs` remain as the naming-isolation seam, but they mostly read from the public surface. Simpler and more future-proof than the original design anticipated.

- [x] **`DispatchEventAsync` signature fix** — **resolved 2026-05-24 (Phase 1.4 close-out; work landed in Phase 1.1)**
  The broken `WebEventData` shim was deleted during Phase 1.1's renderer rewrite. `BlazorInterop.RefAccessors.DispatchEventAsync` declares the matching `(ulong, EventFieldInfo?, EventArgs)` signature via `[UnsafeAccessor(Method)]` + `[UnsafeAccessorType]` for the internal `EventFieldInfo` parameter. `NativeRenderer.DispatchUiEventAsync` calls the new helper. The startup version probe (`BlazorInterop.VerifyAccessors`) catches Blazor-side signature drift at type-load time.

---

## P1 — First end-to-end demo
*Required to show Blazor UI rendering on a real Android device.*

- [ ] **Android Kotlin shell — project scaffold**
  Create `src/BlazorNative.Shell.Android/` as an Android Studio project (or dotnet-android project). Minimal `MainActivity.kt` that loads the `.wasm` binary from assets.

- [ ] **wasmtime-java JNI integration**
  Embed `wasmtime-java` via Gradle. Load the compiled `BlazorNative.Core.wasm` module. Wire up the `mobile_bridge` WASM import symbols to Kotlin implementations.
  - Dependency: `dev.wasmtime:wasmtime-java:latest`
  - Reference: https://github.com/bytecodealliance/wasmtime-java

- [ ] **`mobile_bridge` symbol exports (Android)**
  Implement each symbol the WASM module imports:
  - `shell_navigate(routePtr, routeLen)`
  - `shell_current_route(buf, bufLen) → int`
  - `shell_storage_read(keyPtr, keyLen, valBuf, valBufLen) → int`
  - `shell_storage_write(keyPtr, keyLen, valPtr, valLen)`
  - `shell_storage_delete(keyPtr, keyLen)`
  - `shell_fetch(reqPtr, reqLen, resBuf, resBufLen) → int`
  - `shell_platform_info(buf, bufLen) → int`

- [ ] **Render frame consumer (Android)**
  After WASM writes a `RenderFrame` JSON to the bridge, the Android shell needs to:
  1. Receive the frame (via storage write hook or a dedicated export call)
  2. Parse the `RenderPatch[]` list
  3. Apply patches to its native widget tree

- [ ] **Native widget mapper (Android)**
  Map `NodeType` strings to Android widget classes:
  | NodeType | Android widget |
  |---|---|
  | `view` | `BnYogaFrameLayout` — a **layout-suppressed** frame container *(shipped 6.1; NOT a `LinearLayout` — Yoga computes every child's frame, nothing is appended in order)* |
  | `text` | `TextView` |
  | `button` | `Button` |
  | `input` | `EditText` |
  | `image` | `ImageView` *(created, but no source-loading path — Phase 6.3)* |
  | `scroll` | `ScrollView` *(**shipped 6.2** — a viewport over a shell-synthesised content node; vertical only)* |
  | `picker` | `Spinner` *(does not flex its children — it runs its own layout)* |

- [ ] **`BlazorNativeHostElement` stub**
  `Renderer.AddRootComponent` needs a host element descriptor. Create a minimal stub that satisfies Blazor's requirements without a real DOM.

---

## P2 — Real apps can be built
*Required before any non-trivial Blazor app works correctly.*

- [ ] **Phase 2.3b — wasi-experimental WIT-import unblocker** *(deferred from Phase 2.3)*
  Phase 2.3 found three wasi-experimental SDK 10.0.8 gaps that block first-class WIT-typed imports from materializing in the .wasm:
    1. `_WasmPInvokeModules` auto-population covers only statically-linked libs; user modules need explicit MSBuild opt-in.
    2. `$(WasmAllowUndefinedSymbols)` property is documented but never consumed by the SDK; wasm-ld fails with "undefined symbol".
    3. `wasm-component-ld --component-type` expects a binary blob (from `wit-component`), not raw .wit text — and no SDK helper assembles that blob.
  Phase 2.3 sidestepped via env-var bridge over `wasi:cli/environment`. Closing the gaps is a 1-3 hour spike that would revive the wit-bindgen path for dynamic bridges. The `mobile_bridge.wit` files (commit `25efdb2`) capture the long-term direction; revisit when (i) wasi-experimental ships first-class WIT integration, OR (ii) Phase 2.5+ event callbacks need a typed dynamic bridge that env-vars can't provide.

- [ ] **Phase 2.5+ — export-based dynamic bridge for runtime event callbacks** *(deferred from Phase 2.3 redesign)*
  Env-var bridge (Phase 2.3) is one-way (host → .NET) and initialization-time only. When user-event callbacks (button tap → .NET event handler) need runtime-resident bidirectional communication, design + ship the `[UnmanagedCallersOnly]` export pattern: host calls into the .wasm via exported `blazornative_*` functions; .wasm responds via exported `blazornative_response_*` patterns. Proven mechanism (Phase 1.3's `blazornative_dispatch_event` works this way). Phase 2.5+ brainstorm when there's a real UI event to bridge.

- [ ] **Phase 2.3 deferred mobile_bridge imports** *(landing in respective phases)*
  Each lands additively in the phase that needs it:
  - `shell-navigate` + `shell-current-route` → Phase 2.5 (widget mapper + navigation)
  - `shell-storage-read/write/delete` → Phase 2.5+ (state persistence — likely env-var-shaped for init + file-based via wasi:filesystem preopen for runtime, OR the export-based dynamic pattern from Phase 2.5+)
  - `shell-fetch` → M4 (P3 production-readiness) or earlier if a demo needs it (response data path needs the dynamic-bridge pattern)

- [ ] **Phase 2.2b — wasmtime Linker::drop SIGABRT on Android Scudo allocator** *(deferred from Phase 2.2)*
  `wasmtime_component_linker_delete` crashes with `Scudo ERROR: corrupted chunk header` on Android x86_64 emulator (API 34). The wasm execution completes successfully before the crash — `[BOOT]` markers ARE captured. Workaround in place: skip all cleanup deletes in `WasiHost.runWasm` (process-scoped leak). Backtrace shows `wasmtime_component_linker_t::drop → NameMap<Atom, Definition>::drop → Arc<Definition>::drop_slow → scudo::deallocate → corrupted header`. Investigation angles:
    (a) Mono-AOT'd .wasm might hold Arc<wasmtime::Engine> refs that wasmtime double-decrements during cleanup
    (b) wasmtime's TLS cleanup ordering differs on Bionic vs glibc
    (c) cargo-ndk build flags might be missing Android-specific allocator wiring (e.g. `-C link-arg=-Wl,-z,noexecstack`)
    (d) libwasmtime v45 may have an Android-specific bug; check wasmtime upstream issue tracker; consider bumping to v46+ when available
  Same call sequence works fine on Windows x86_64 (JVM tests pass). Phase 2.2b should ALSO add a per-test Android instrumented test for each lifecycle pair (engine alone, store alone, etc.) to bisect which delete is the actual problem — the current "skip everything" workaround leaks the whole graph; finding the minimal failing delete narrows the fix.

- [ ] **Android x86_64 emulator HAXM/Hyper-V documentation** *(deferred from Phase 2.2)*
  Phase 2.2 assumed Marcel's dev box had HAXM or Hyper-V acceleration for the x86_64 emulator. Documented working on Marcel's box. On boxes without it, emulator boot is too slow / fails. Document the fallback (arm64 emulator on Windows-on-ARM, or `-accel off` slow mode) in setup.ps1 with a probe + warning.

- [ ] **APK size reduction** *(deferred from Phase 2.2 — P3)*
  Phase 2.2 ships ~81 MB APK (libwasmtime per ABI is ~36-39 MB each, plus .wasm ~13 MB). R8 / ProGuard / wasmtime feature trimming can reduce. M4 hardening work.

- [ ] **Universal vs ABI-split APK** *(deferred from Phase 2.2 — P3)*
  Currently building a single universal APK with both ABIs. Play Store best practice is per-ABI split APKs (or App Bundle). M4 work.

- [ ] **Linux/macOS `setup.ps1` parity for libwasmtime cargo build** *(deferred from Phase 2.1)*
  Phase 2.1's `setup.ps1` section 8b is Windows-only. Equivalent shell scripts (`setup.sh`) for Linux + macOS need to clone wasmtime, cargo-build wasmtime-c-api, and place the resulting `.so` / `.dylib` at `vendor/wasmtime/`. Same logic; different binary names and PATH conventions. Required before contributors on Linux/macOS can run `./gradlew test` from `src/BlazorNative.Jni/`.

- [ ] **Phase 2.3 — `mobile_bridge` import revival strategy** *(deferred from Phase 2.1)*
  Phase 2.1.0 spike found that Mono-AOT trimmed the `[DllImport("mobile_bridge")]` declarations because nothing reachable from `Main` calls them. When Phase 2.3 needs real `mobile_bridge` implementations from the Android side, choose between:
  (a) `[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Native))]` on Main to force-root all 7 externs as a trim root — quick fix, ~30 min, mirrors the Phase 1.3 trim-root pattern.
  (b) Migrate to `wit-bindgen`-generated typed bindings — author `src/BlazorNative.WasiHost/wit/mobile_bridge.wit`, generate .NET-side partial methods, replace `[DllImport]` declarations with WIT-typed externs. Bigger refactor; aligns with the design's "WIT-typed imports = JSI-equivalent" future direction.
  Phase 2.3 brainstorm picks.

- [ ] **WIT-typed records for `shell-fetch` and `shell-storage-write`** *(deferred from Phase 2.1)*
  Phase 2.1 Q5 chose JSON-string payloads for the structured `mobile_bridge` imports because wasmtime C-API issues #11437 (Resources) and #11617 (Byte arrays) were open and we didn't want to bet the phase on full WIT-record marshaling. When those issues close upstream, refactor `mobile_bridge.wit`'s `shell-fetch` from `func(request-json: string) -> string` to `func(req: fetch-request) -> fetch-response` with typed records. Same shape upgrade for `shell-storage-write`. Non-breaking when shipped alongside compatible JNA marshaling.

- [ ] **`@bind` two-way binding**
  Input value changes from native (`EditText.afterTextChanged`) need to flow back into Blazor component state via `DispatchEventAsync`. Requires the `NativeUiEvent` → Blazor event pipeline to be complete.

- [ ] **`BnView` / `BnText` / `BnButton` component library**
  Currently developers write HTML elements (`<div>`, `<button>`) which get mapped through the HTML→native translation. Better DX: first-class Blazor components that emit the right `NodeType` directly.
  ```razor
  <BnView Style="flex-direction: column">
      <BnText FontSize="24">Hello from BlazorNative</BnText>
      <BnButton OnClick="HandleClick">Tap me</BnButton>
  </BnView>
  ```
  - Files to create: `src/BlazorNative.Components/`

- [ ] **Cascading values support**
  Wire up `CascadingValue<T>` in the headless renderer so theme/auth context flows down the component tree correctly.

- [ ] **DI fully wired end-to-end**
  - `Program.cs` (DevHost) uses `AddBlazorNativeHttp()` and `AddBlazorNativeRenderer()`
  - `NativeRenderer` is mounted and running in DevHost
  - Render frames appear in `/dev/renderframe` endpoint

- [ ] **Navigation service**
  `IMobileBridge.NavigateAsync` is the primitive but apps need a higher-level `INativeNavigator` service with typed routes, back stack management, and transition hints.

- [ ] **`@inject IMobileBridge` ergonomics**
  Add a `BlazorNativeComponentBase` base class that pre-injects the bridge and exposes helpers like `Navigate()`, `ReadStorage()`, `FetchJson<T>()`.

---

## P3 — Production readiness

- [ ] **`AddBlazorNativeRenderer()` / `AddBlazorNativeHttp()` will be called from DevHost in P2**
  Phase 1.2 made these thin re-exports of the ZA.Inject-generated `AddBlazorNativeRendererServices` / `AddBlazorNativeHttpServices`. The DevHost's `Program.cs` doesn't call them yet (it's still a pure Razor app). When P2's "DI fully wired end-to-end" task lands ("Render frames appear in `/dev/renderframe` endpoint"), DevHost will need to call both methods after `var devBridge = new DevHostBridge();`. Until then the re-exports are intentionally dead code — keep them so the call site in P2 doesn't need to know about the underlying generator naming.

- [ ] **Analyzer unit tests**
  `src/BlazorNative.Analyzers/tests/` is empty. Add `Microsoft.CodeAnalysis.Testing` based tests for every diagnostic (BN0001–BN0013). Each test should verify: fires on bad code, silent on correct code, fix suggestion (where applicable).

- [x] **`.editorconfig` analyzer scoping** — **resolved 2026-05-24 (Phase 1.5; via project-graph instead of `.editorconfig`)**
  Chosen mechanism: `BlazorNative.Analyzers` is wired as `<ProjectReference OutputItemType="Analyzer">` from `BlazorNative.WasiHost` only. Non-wasi-wasm projects (DevHost, tests, etc.) don't reference the analyzer, so it can't fire on them — cleaner than per-folder `.editorconfig` suppression. Verified: `dotnet build BlazorNative.sln` produces 0 BN0001-BN0013 warnings. Future wasi-wasm-targeting projects should add the same `ProjectReference` shape.

- [ ] **GitHub Actions CI pipeline**
  `.github/workflows/ci.yml`:
  - Build all projects
  - Run analyzers
  - Run tests
  - Build `wasi-wasm` target and validate with `wasm-tools validate`
  - Artifact: upload `.wasm` binary

- [ ] **iOS Swift shell**
  Same as Android shell but Swift + WasmKit or wasmtime-swift. Same WIT contract, same `.wasm` binary. Requires Mac + Xcode build agent in CI.

- [ ] **DevTools render tree inspector**
  Add `GET /dev/rendertree` endpoint that returns the current widget tree state as JSON. Add a simple browser UI at `/dev` that shows:
  - Live patch stream (SSE)
  - Current widget tree (collapsible tree view)
  - Event log
  - Storage state

- [ ] **wit-bindgen C# output**
  Run `make wit-gen` and commit the generated C# bindings to `src/BlazorNative.Bridge/Generated/`. Currently the folder is referenced in the solution but the files don't exist.

- [ ] **NuGet packaging**
  Package `BlazorNative.Core`, `BlazorNative.Renderer`, `BlazorNative.Http`, `BlazorNative.Analyzers`, and `BlazorNative.Components` as NuGet packages. Analyzers package requires special `.props`/`.targets` inclusion.

- [ ] **Hot reload protocol for WASI**
  WASM binaries can't hot-reload. Implement a file-watcher in DevHost that detects source changes, triggers a background `dotnet build -r wasi-wasm`, and sends a `reload` native event to any connected Android shells running in dev mode.

---

## P4 — Full platform coverage
*Required to be competitive with React Native / Flutter.*

### Android shell (complete)
- [ ] **Activity lifecycle hooks**
  Wire Android `onPause`, `onResume`, `onDestroy`, `onBackPressed` into native events dispatched into WASM. Allows Blazor components to subscribe to app lifecycle via `IMobileBridge.NativeEvents`.

- [ ] **Asset pipeline**
  `.wasm` binary must be bundled into the APK automatically at build time. Add a Gradle task that copies the compiled `BlazorNative.Core.wasm` from the .NET build output into `app/src/main/assets/` before packaging.

- [ ] **Android permissions API**
  Extend `IMobileBridge` with `RequestPermissionAsync(string permission) → bool` and implement on Android via `ActivityCompat.requestPermissions`. Required for camera, location, notifications.

- [ ] **Android push notifications (FCM)**
  `FirebaseMessagingService` integration. Incoming FCM message → `NativeEvent("push", payload)` into WASM. Registration token surfaced via `IMobileBridge`.

- [ ] **Android secure storage**
  Implement `ISecureStorage` (separate from the plain key/value bridge storage) backed by Android Keystore. For tokens, credentials, sensitive data.

- [ ] **Android deep links / Intent handling**
  Intercept incoming Android Intents (URLs, share targets) and route them as `NativeEvent("deeplink", url)` into WASM so Blazor navigation can respond.

- [ ] **Android back gesture / button**
  Predictive back gesture (Android 13+) and hardware back button → `NativeEvent("back")`. Allow Blazor navigation stack to handle or defer.

### iOS shell (complete)
- [ ] **iOS shell — project scaffold**
  Xcode project with Swift Package Manager. `AppDelegate.swift` and `SceneDelegate.swift` stubs. Load `.wasm` from app bundle via WasmKit or wasmtime-swift.

- [ ] **`mobile_bridge` symbol exports (iOS)**
  Same WIT contract as Android, implemented in Swift. All 7 import symbols wired to UIKit/Foundation equivalents.

- [ ] **Render frame consumer (iOS)**
  Same patch protocol as Android. UIKit widget mapper:
  | NodeType | UIKit class |
  |---|---|
  | `view` | `UIView` |
  | `text` | `UILabel` |
  | `button` | `UIButton` |
  | `input` | `UITextField` |
  | `image` | `UIImageView` *(created, but no source-loading path — Phase 6.3)* |
  | `scroll` | `UIScrollView` *(**shipped 6.2** — a viewport over a shell-synthesised content node; vertical only)* |
  | `picker` | `UIPickerView` *(does not flex its children)* |

- [ ] **iOS push notifications (APNs)**
  `UNUserNotificationCenter` integration. APNs token surfaced via bridge. Incoming notification → `NativeEvent("push", payload)`.

- [ ] **iOS secure storage**
  Keychain integration behind `ISecureStorage`. Replaces plain bridge storage for sensitive data.

- [ ] **iOS deep links / Universal Links**
  `application(_:continue:restorationHandler:)` → `NativeEvent("deeplink", url)`.

- [ ] **iOS App Store compliance**
  Apple forbids JIT compilation. Validate that the `.wasm` binary compiled via .NET NativeAOT does not trigger App Store rejection. May require `com.apple.security.cs.allow-jit` entitlement review. Document findings.

### Additional platform APIs
- [ ] **Geolocation** (`CLLocationManager` / `FusedLocationProviderClient`) → `IBridgeGeolocation`
- [ ] **Camera** (`AVCaptureSession` / `CameraX`) → `IBridgeCamera` (photo + QR scan)
- [ ] **Clipboard** → `IBridgeClipboard`
- [ ] **Share sheet** (native OS share dialog) → `IBridgeShare`
- [ ] **Haptics** (`UIImpactFeedbackGenerator` / `Vibrator`) → `IBridgeHaptics`
- [ ] **Biometrics** (`LAContext` / `BiometricPrompt`) → `IBridgeBiometrics`
- [ ] **In-app purchases** (StoreKit / Google Play Billing) → `IBridgePurchasing`
- [ ] **Background tasks** (`BGTaskScheduler` / `WorkManager`) → `IBridgeBackgroundWork`

Each platform API follows the same pattern:
1. Add to `mobile-bridge.wit`
2. C# interface in `BlazorNative.Core`
3. `DevHostBridge` mock implementation
4. Android Kotlin implementation
5. iOS Swift implementation

---

## P5 — Developer experience and ecosystem
*Required to attract external contributors and users.*

### Component library (`BlazorNative.Components`)
- [ ] **Project scaffold** — `src/BlazorNative.Components/BlazorNative.Components.csproj`
- [x] **Layout components** — **`BnView` and `BnScroll` shipped (M6).** `BnView` carries the
  full typed flex surface (`Direction`/`Justify`/`Align`/`Grow`/`Wrap`/`Gap`/sizing/…), with
  `BnRow` and `BnColumn` as thin presets over it; `BnScroll` is a real scrolling viewport (6.2).
  **`BnStack` is a deliberate non-goal** — it would be a synonym for `BnColumn`, and two names
  for one thing is a library smell on day one; `BnRow`/`BnColumn` say which axis they mean.
  Still open: `BnSafeArea`, `BnGrid`.
- [ ] **Typography** — `BnText`, `BnHeading`, `BnLabel`, `BnLink`
- [ ] **Input components** — `BnButton`, `BnInput`, `BnTextArea`, `BnCheckbox`, `BnSwitch`, `BnSlider`, `BnPicker`, `BnDatePicker`
- [ ] **Media** — `BnImage`, `BnIcon` (SF Symbols / Material Icons mapping)
- [ ] **Feedback** — `BnActivityIndicator`, `BnProgressBar`, `BnToast`, `BnAlert`
- [ ] **Navigation components** — `BnTabBar`, `BnNavigationBar`, `BnDrawer`, `BnModal`, `BnBottomSheet`
- [ ] **List components** — `BnList<TItem>`, `BnVirtualList<TItem>` (windowed rendering for large datasets)
- [ ] **`BlazorNativeComponentBase`** — base class with pre-injected bridge, `Navigate()`, `ReadStorage()`, `FetchJson<T>()` helpers

### Styling system (`BlazorNative.Styling`)
- [ ] **Typed `NativeStyle` record** — replace string-based style properties with a strongly-typed, AOT-safe style object
- [ ] **`StyleSheet` — define-once, reference-by-name** — React Native StyleSheet pattern
- [ ] **Theme system** — `IBlazorNativeTheme`, `ThemeProvider`, dark/light mode detection via `NativeEvent("themeChanged")`
- [ ] **Platform style overrides** — `[AndroidStyle(backgroundColor: "#fff")]` / `[iOSStyle(...)]` attributes on components
- [ ] **Responsive layout** — screen size breakpoints (`Compact`, `Regular`, `Large`) mapped to device classes

### State management (`BlazorNative.State`)
- [ ] **`BlazorNativeStore<TState>`** — minimal built-in Redux-like store for small apps. AOT-compatible, source-generated reducers.
- [ ] **ZeroFlux/StaticFlux integration** — wire up the existing AOT-compatible Flux library (already explored) as the recommended state solution for larger apps
- [ ] **`ZeroAlloc.EventSourcing` integration** — event-sourced state that survives app backgrounding via bridge storage

### Navigation system (`BlazorNative.Navigation`)
- [ ] **`INativeNavigator`** — typed route service
- [ ] **Route definitions** — `[Route("/product/{id}")]` attribute on page components, source-generated route table
- [ ] **Back stack** — `GoBackAsync()`, `GoToRootAsync()`, `CanGoBack` property
- [ ] **Transition hints** — `NavigationTransition.Slide`, `.Fade`, `.Modal`, `.None`
- [ ] **Deep link → route mapping** — incoming `NativeEvent("deeplink")` parsed and resolved to typed route
- [ ] **Tab bar navigation** — `BnTabBar` with declarative tab definitions and nested navigation stacks

### CLI tool (`BlazorNative.Cli`)
- [ ] **`dotnet tool install -g BlazorNative.Cli`** — published as a .NET global tool
- [ ] **`blazornative new <AppName>`** — scaffold new project from template
- [ ] **`blazornative run --platform devhost`** — start DevHost with hot reload
- [ ] **`blazornative run --platform android`** — build WASM + package APK + deploy to emulator/device
- [ ] **`blazornative run --platform ios`** — build WASM + package IPA + deploy to simulator/device (Mac only)
- [ ] **`blazornative build wasi`** — compile to WASM and validate
- [ ] **`blazornative inspect`** — open DevTools browser UI
- [ ] **`blazornative wit-gen`** — regenerate bridge bindings from `.wit` file
- [ ] **`blazornative add platform android`** — add Android shell to existing project

### Testing infrastructure
- [ ] **`BlazorNative.Analyzers.Tests`** — `Microsoft.CodeAnalysis.Testing` unit tests for all BN0001–BN0013 diagnostics
- [ ] **`BlazorNative.Renderer.Tests`** — mount a component, assert `RenderPatch[]` output matches expected. No native shell needed.
  - Currently includes one [Fact(Skip)] test `RenderWalk_IsAllocationFree_OnSteadyState` (`tests/BlazorNative.Renderer.Tests/RendererSpike.cs`). When this milestone enables StateHasChanged-on-mounted-root, un-skip and set a realistic ~512 B/iteration budget.
- [ ] **`BlazorNative.Integration.Tests`** — run compiled `.wasm` module via wasmtime, assert bridge call round-trips
- [ ] **`BlazorNative.Components.Tests`** — bunit-style tests for component library
- [ ] **`.editorconfig` analyzer scoping** — suppress WASI analyzers in DevHost and test projects

### CI/CD pipeline
- [ ] **GitHub Actions — `ci.yml`** — build, analyze, test, WASM compile + validate on every PR
- [ ] **GitHub Actions — `release.yml`** — NuGet publish on tag, GitHub Release with CHANGELOG
- [ ] **Android emulator in CI** — run integration tests against Android emulator (GitHub Actions macOS runner)
- [ ] **iOS simulator in CI** — macOS runner, Xcode, iOS simulator integration tests

### Documentation site
- [ ] **Getting started guide** — scaffold → run → first component on device in under 15 minutes
- [ ] **Architecture deep-dive** — WASM, WASI, WIT, patch protocol, cooperative scheduler explained
- [ ] **Component reference** — every `Bn*` component with props, examples, platform notes
- [ ] **Platform API reference** — every `IBridge*` interface documented
- [ ] **WIT contract reference** — `mobile-bridge.wit` annotated
- [ ] **Migration guide** — from MAUI Blazor Hybrid to BlazorNative
- [ ] **WASI compatibility guide** — what works, what doesn't, how analyzers help
- [ ] **Troubleshooting** — common AOT trim issues, WASM compile errors, bridge wiring mistakes

### NuGet packaging
- [ ] `BlazorNative.Core` — bridge contract + DevHostBridge
- [ ] `BlazorNative.Renderer` — headless renderer + patch protocol
- [ ] `BlazorNative.Http` — BridgeHttpHandler + DI extensions
- [ ] `BlazorNative.Analyzers` — Roslyn analyzers (special `.props`/`.targets` packaging)
- [ ] `BlazorNative.Components` — component library
- [ ] `BlazorNative.Styling` — styling system
- [ ] `BlazorNative.State` — state management
- [ ] `BlazorNative.Navigation` — navigation system

---

## P6 — Framework hardening
*Required before any production app ships. Parallel with P5.*

### Security model
- [ ] **WASM binary signature verification**
  Native shell validates a SHA-256 + Ed25519 signature on the `.wasm` binary before loading. Prevents tampering with the bundled module. Signing happens as part of the release CI pipeline.

- [ ] **Bridge URL allowlist**
  `shell_fetch` enforces a configurable allowlist of permitted hosts defined at app build time. A WASM component cannot call arbitrary URLs — the native shell rejects disallowed hosts before making any network call. Configured via `blazornative.json`.

- [ ] **Secure buffer zeroing**
  Bridge buffers that cross the WASM boundary containing sensitive data (auth tokens, passwords, biometric results) are explicitly zeroed after use. Add `SecureBridgeBuffer` helper that wraps `byte[]` with `IDisposable` zeroing.

- [ ] **WASM crash isolation**
  If the WASM module throws an unhandled exception or traps, the native shell catches it, logs the crash, and restarts the module without killing the host app. Max restart attempts configurable. Native shell shows a fallback UI during restart.

- [ ] **DevTools endpoint authentication**
  `POST /dev/event` and other DevTools endpoints are only active in `Debug` builds. In `Release` builds the entire `/dev` route group is removed. In `Debug`, endpoints require a local-only binding (`localhost` only, not `0.0.0.0`).

- [ ] **Bridge call sandboxing**
  Define which bridge calls are available per-permission level. A component that hasn't been granted camera permission cannot call `shell_camera_*` — the native shell rejects the call before it reaches the platform API.

### Error handling and crash recovery
- [ ] **Unhandled exception handler in WASM**
  Register a global `AppDomain.CurrentDomain.UnhandledException` handler inside the WASM module. Serialise the exception (type, message, stack trace) and dispatch it to the native shell via a dedicated `shell_report_crash` export before the module terminates.

- [ ] **Native crash reporter integration**
  Native shell receives crash reports from WASM and forwards them to the platform crash reporter. Android: Firebase Crashlytics. iOS: same. DevHost: writes to `logs/crashes/`. Abstract behind `ICrashReporter` bridge interface.

- [ ] **Partial render failure handling**
  Native shell validates each incoming `RenderFrame` before applying. If a patch frame is malformed (invalid JSON, unknown `op`, negative `nodeId`), the shell discards the frame, logs the error, and requests a full re-render from WASM via `NativeEvent("requestFullRender")`.

- [ ] **Bridge call timeout**
  All bridge calls (`shell_fetch`, `shell_storage_*`, platform API calls) have a configurable timeout. If the native shell doesn't respond within the timeout, the WASM side receives a defined error code rather than blocking the cooperative scheduler indefinitely.

- [ ] **Large payload handling**
  Replace the hardcoded 64KB fetch response buffer with a growable protocol. For responses larger than the initial buffer, the native shell chunks the response and the WASM side reassembles. Alternatively: shared memory ring buffer via WASM `memory.grow`.

- [ ] **Bridge buffer pooling**
  Replace per-call `byte[]` allocations in `WasiBridge` with an `ArrayPool<byte>` backed pool. Significant GC pressure reduction on the hot path (render frames, frequent fetch calls).

### Accessibility
- [ ] **Screen reader bridge interface**
  Extend `mobile-bridge.wit` with accessibility annotations: `set_accessibility_label`, `set_accessibility_hint`, `set_accessibility_role`. Android: maps to `contentDescription` + `ViewCompat.setAccessibilityDelegate`. iOS: maps to `accessibilityLabel` + `accessibilityTraits`.

- [ ] **`BnAccessibility` component attributes**
  Add `Label`, `Hint`, `Role`, `IsHidden` props to all `Bn*` components that flow through to the native accessibility tree automatically.

- [ ] **Focus management**
  `INativeNavigator` emits focus events on navigation. New screen's first focusable element receives focus automatically for screen reader users. `BnFocusScope` component controls focus order.

- [ ] **Semantic roles in patch protocol**
  Add `role` field to `CreateNodePatch` — `"button"`, `"heading"`, `"image"`, `"list"`, `"listitem"`, `"alert"`. Native shell maps roles to platform accessibility APIs.

- [ ] **Dynamic text size support**
  Native shell detects system font scale change → `NativeEvent("fontScaleChanged", scale)`. `BnText` components respond by re-rendering with scaled `FontSize`. Layout system reflows.

- [ ] **High contrast mode**
  Native shell detects high contrast / increased contrast setting → `NativeEvent("contrastChanged", "high"|"normal")`. Theme system responds with high-contrast colour overrides.

### Internationalisation (i18n)
- [ ] **Locale detection**
  Native shell reads device locale on startup → passed to WASM via `PlatformInfo.Locale` (extend the struct). Available to components via `IBlazorNativeCulture` service.

- [ ] **`InvariantGlobalization` workaround**
  We set `InvariantGlobalization=true` for WASI which breaks locale-sensitive formatting. Implement a `BridgeGlobalization` service that delegates number/date/currency formatting to the native shell via bridge calls, which uses the platform's native formatters.

- [ ] **RTL layout support**
  Extend `PlatformInfo` with `IsRtl` flag. `BnView` flips layout direction automatically. Patch protocol adds `layoutDirection: "ltr"|"rtl"` to `CreateNodePatch`. Native shell applies `layoutDirection` to Android `ViewCompat` / iOS `semanticContentAttribute`.

- [ ] **String localisation pipeline**
  Define a `.resx`-compatible localisation workflow that survives AOT trimming. Source generator emits a strongly-typed `Strings` class at build time. No reflection-based `ResourceManager` at runtime.

- [ ] **Bidirectional text**
  Ensure `BnText` and `BnInput` correctly handle mixed LTR/RTL text (Arabic numerals in English text, etc.). Pass `bidiOverride` hint through patch protocol to native text widgets.

### Performance monitoring (`BlazorNative.Diagnostics`)
- [ ] **Frame timing instrumentation**
  Measure and record: time from `UpdateDisplayAsync` call → `RenderFrame` JSON serialised → dispatched to native → `CommitFramePatch` acknowledged. Surface via `IFrameMetrics` service injected into components.

- [ ] **Bridge call profiler**
  Each bridge call records duration, call type, payload size. Aggregated stats available via `GET /dev/metrics` in DevHost and via `IBlazorNativeDiagnostics` in production (opt-in).

- [ ] **Slow frame detection**
  Frames taking >16ms (60Hz) or >8ms (120Hz) are flagged. In DevHost, slow frames appear in the DevTools UI highlighted in amber/red. In production, slow frames are included in crash/analytics reports.

- [ ] **WASM memory tracking**
  Expose current WASM linear memory usage, GC heap size, and `memory.grow` call count via `IBlazorNativeDiagnostics`. DevHost DevTools shows a live memory graph.

- [ ] **`BlazorNative.Diagnostics` NuGet package**
  All diagnostics behind a separate opt-in package. Zero overhead when not referenced. Tree-shaken by AOT when not used.

### Memory management
- [ ] **Initial WASM memory configuration**
  Make initial WASM linear memory size configurable in `blazornative.json` (default: 16MB, min: 4MB, max: 256MB). Document memory sizing guidelines for different app complexity levels.

- [ ] **Memory growth strategy**
  Configure `memory.grow` behaviour — aggressive (grow by 2x) vs conservative (grow by 1 page = 64KB). Aggressive reduces `memory.grow` call frequency; conservative reduces peak memory on constrained devices.

- [ ] **OOM handling**
  Native shell catches WASM `OOM` trap. Attempts module restart with larger initial memory. If restart fails, shows user-facing error and reports crash.

### WIT contract hardening
- [ ] **WIT versioning strategy**
  Add `@since(version = "0.1.0")` annotations to all WIT interfaces. Define a compatibility policy: minor versions are additive only, major versions require explicit migration. Native shell and WASM module negotiate version on startup.

- [ ] **Typed error returns in WIT**
  Replace negative integer return codes with proper WIT `result<T, error-kind>` types. `error-kind` enum: `timeout`, `not-found`, `permission-denied`, `network-error`, `serialization-error`.

- [ ] **Streaming interface for large payloads**
  Add `fetch-stream` to WIT for streaming HTTP responses. Native shell writes chunks via repeated calls to a WASM-exported `receive-chunk` function. Eliminates large buffer requirement.

---

## P7 — Enterprise readiness
*Required for adoption by teams building serious commercial apps.*

### Over-the-air (OTA) updates
- [ ] **OTA update protocol design**
  Define the update flow: app checks a configurable endpoint for a new `.wasm` version → downloads + verifies signature → stores alongside current version → swaps on next cold start. Native shell manages version storage.

- [ ] **Delta updates**
  Only ship changed WASM sections rather than the full binary. Investigate `bsdiff`/`zstd` delta compression between `.wasm` versions. Can reduce update payload by 60-80% for minor releases.

- [ ] **Version negotiation**
  On WASM module load, native shell and module exchange version handshake. If WIT contract version is incompatible, native shell refuses to load and falls back to previous version automatically.

- [ ] **Rollback mechanism**
  Native shell keeps the last two `.wasm` versions. If the new version crashes on launch (detected within first 30 seconds), automatically rolls back to the previous version and reports the failure.

- [ ] **OTA update UI primitives**
  `NativeEvent("updateAvailable", version)` and `NativeEvent("updateDownloaded", version)` allow Blazor components to show in-app update prompts. `INativeNavigator` exposes `InstallUpdateAndRestartAsync()`.

### Multi-window support
- [ ] **Multi-instance WASM**
  Design and validate running two WASM module instances simultaneously (iPad split-screen, Android multi-window). Each instance has its own linear memory heap. Bridge storage is shared — define concurrency semantics (last-write-wins vs optimistic locking).

- [ ] **Window lifecycle events**
  `NativeEvent("windowResized", json)` with new dimensions. `NativeEvent("windowFocused")` / `NativeEvent("windowBlurred")`. Components can adapt layout to window size changes.

- [ ] **iPad multitasking**
  Safe area insets update when split-screen changes. `BnSafeArea` responds to `NativeEvent("safeAreaChanged")`. Stage Manager (iPadOS 16+) support.

### Platform design guidelines compliance
- [ ] **Material Design 3 defaults (Android)**
  `Bn*` components use MD3 defaults on Android: correct elevation, ripple effects, typography scale, colour roles. Configurable via `MaterialTheme` in `BnThemeProvider`.

- [ ] **Human Interface Guidelines defaults (iOS)**
  `Bn*` components use iOS HIG defaults: correct corner radii, SF Symbols, Dynamic Type, list separators, navigation bar behaviour.

- [ ] **Platform gesture recognition**
  iOS swipe-to-go-back (interactive pop gesture) wired to `INativeNavigator` back stack. Android predictive back gesture (Android 14+) with preview animation. Both dispatch `NativeEvent("gestureBack")` before committing.

- [ ] **Keyboard avoidance**
  When software keyboard appears, `BnScroll` containing focused `BnInput` scrolls the input above the keyboard automatically. Android: `WindowInsetsCompat`. iOS: `UIResponder` keyboard notifications. No Blazor code required.

- [ ] **Edge-to-edge / safe areas**
  Android 15 enforces edge-to-edge. `BnSafeArea` wraps content with correct insets from `WindowInsetsCompat` (Android) / `safeAreaInsets` (iOS). Status bar and navigation bar colours configurable via bridge.

- [ ] **Android 15 predictive back**
  Full implementation of Android 15 predictive back animation — native shell provides the back preview surface, BlazorNative navigation stack provides the destination snapshot.

### Legal and compliance
- [ ] **SBOM generation**
  GitHub Actions release pipeline generates a CycloneDX SBOM listing all dependencies (wasmtime, .NET runtime, NuGet packages). Published alongside each release as `sbom.json`.

- [ ] **Open source license audit**
  Audit all dependencies for license compatibility: wasmtime (Apache 2.0), .NET (MIT), Blazor (MIT), wasmtime-java (Apache 2.0), WasmKit (Apache 2.0). Document in `LICENSES.md`. Flag any copyleft dependencies.

- [ ] **GDPR compliance documentation**
  Document what data the bridge stores, for how long, and how to clear it. `IMobileBridge.DeleteStorageAsync` is the user data deletion primitive — document its scope. Publish a data retention policy template for app developers.

- [ ] **Export control review**
  WASM + .NET crypto libraries may be subject to export control regulations. Legal review required before publishing to NuGet and distributing in App Store / Google Play in restricted regions.

- [ ] **FIPS 140-2 considerations**
  Document whether bridge TLS (handled by native shell) can be configured to use FIPS-validated crypto providers. Android: `BouncyCastle` FIPS. iOS: platform SecureTransport is already FIPS-validated.

### Observability and analytics
- [ ] **Structured logging pipeline**
  `IBlazorNativeLogger` service available in WASM. Log entries serialised and dispatched to native shell via `shell_log(level, message, structuredData)`. Native shell routes to platform logging (Logcat / OSLog) and optionally to a remote sink.

- [ ] **Analytics bridge interface**
  `IBridgeAnalytics` with `TrackEvent(name, properties)` and `TrackScreen(name)`. DevHostBridge logs to console. Android: Firebase Analytics. iOS: same. Developers call analytics from Blazor components, platform implementation is swappable.

- [ ] **Performance budget enforcement**
  CI pipeline fails if WASM binary exceeds a configurable size budget (default: 10MB compressed). Frame timing regressions (P95 > 16ms in integration tests) fail the build.

---

## Future / exploratory

*No phase assigned — depends on ecosystem maturity and community interest.*

- [ ] **WASM Component Model migration** — when .NET targets WASI Preview 2 + Component Model, replace manual P/Invoke bridge with generated component glue. Eliminates `WasiBridge.cs` entirely.
- [ ] **Windows shell** — thin Win32/WinUI3 shell. Same `.wasm` binary, four platforms from one codebase.
- [ ] **macOS shell** — Swift/AppKit. Reuse iOS shell with minimal changes.
- [ ] **BlazorNative Studio** — VS Code extension: live render tree inspector, native event injector, WASI compile status indicator, BN diagnostic quick-fixes.
- [ ] **`RAG.net` integration** — run local embeddings and retrieval entirely client-side in WASM. No server needed for AI features in mobile apps.
- [ ] **`ZeroAlloc.EventSourcing` deep integration** — event-sourced component state that survives app backgrounding via secure bridge storage.
- [ ] **ZeroFlux/StaticFlux as default state solution** — promote the AOT-compatible Flux library (already designed) as the canonical state management recommendation.
- [ ] **Hot reload protocol for WASI** — file-watcher in DevHost triggers background `dotnet build -r wasi-wasm`, sends `reload` native event to connected Android/iOS shells in dev mode.
- [ ] **BlazorNative.Templates** — `dotnet new` template pack: blank app, tabbed app, master-detail app, e-commerce starter.
- [ ] **bol.com marketplace app** — reference application built entirely on BlazorNative, demonstrating real-world usage of the framework across all platforms.

---

## Phase summary

| Phase | Theme | Prerequisite |
|---|---|---|
| P0 | Runtime boots | Nothing |
| P1 | First pixel on Android | P0 |
| P2 | Real apps possible | P1 |
| P3 | Shippable on both platforms | P2 |
| P4 | Full platform coverage | P3 |
| P5 | Developer ecosystem | P3 (parallel with P4) |
| P6 | Framework hardening | P3 (parallel with P4/P5) |
| P7 | Enterprise readiness | P5 + P6 |
| Future | Long-term vision | P7 |

---

## Next session starting point

**Resume with P0:**

> "Continue BlazorNative — tackle P0: WASI entry point and renderer internal API strategy"

Specific tasks in order:
1. Spike `UnsafeAccessor` approach for `RenderTreeDiff` access — verdict before anything else
2. Write `WasiEntryPoint.cs` with correct async bootstrap
3. Verify `[UnmanagedCallersOnly]` export appears in `.wasm` with `wasm-tools dump`
4. Fix `DispatchEventAsync` signature based on internal API verdict

All context is in `docs/SESSION-HISTORY.md`.
