# BlazorNative — Roadmap

*Source of truth for milestone and phase state. Updated by project-orchestration sub-skills.*

## Milestones

### ✅ Milestone 1 — P0: Runtime Boots End-to-End  *(complete 2026-05-24, tagged `v1.0`)*

Toolchain produces a `.wasm` that loads under wasmtime with correct exports and a working cooperative scheduler. Renderer internal-API strategy decided. All 8 DoD criteria met — see [final audit](../plans/2026-05-24-milestone-1-final-audit.md).

Definition of done: see [MILESTONE.md](MILESTONE.md).

Phases:
- ✅ **Phase 1.1** — Renderer internal-API spike — *complete (2026-05-23)*
   - Verdict: `BlazorInterop.cs` isolation layer with `Bn*` ref-struct wrappers. Against Blazor 10, most render-tree members turned out to be public — `[UnsafeAccessor]` is only needed for `Renderer.DispatchEventAsync` (uses internal `EventFieldInfo`). See [design](../plans/2026-05-23-renderer-internal-api-design.md) + [implementation plan](../plans/2026-05-23-phase-1.1-implementation-plan.md).
   - Side effects: full retarget to .NET 10; `System.Reactive` → `ZeroAlloc.AsyncEvents`; `List<RenderPatch>` → `ZeroAlloc.Collections.PooledList`; smoke test (`FirstFrame_HasExpectedPatches`) passes; allocation budget test deferred to Milestone 4.
   - Discovered: .NET 10's `wasi-experimental` workload provides **Mono-AOT**, not NativeAOT, for `wasi-wasm`. Design's "Native AOT + WASI" framing was wrong; updated.
- ✅ **Phase 1.2** — WASI entry point + DI bootstrap — *complete (2026-05-24)*
   - Real `Main` in new `BlazorNative.WasiHost` project (architectural deviation from plan — Core stays a library; WasiHost is the executable composition root, breaks the would-be circular Core↔Renderer/Http dep). Builds DI graph via ZeroAlloc.Inject MS DI Extension mode, resolves IMobileBridge + NativeRenderer, exits 0. End-to-end verified by `tests/BlazorNative.Wasi.Tests/BootSmoke` which publishes for `wasi-wasm` + invokes the AOT'd `.wasm` under `wasmtime`.
   - Discoveries:
     - .NET 10 Mono-WASI **does not support async `Main`** — `Task.InternalWaitCore` throws `PlatformNotSupportedException`. The `await Task.Delay(1)` round-trip from MILESTONE DoD #5 was reframed; sync `Main` is the supported shape (matches `dotnet new wasiconsole` template).
     - wasi-experimental workload (manifest 10.0.108) pins to `wasi-sdk-25` specifically. Newer wasi-sdk versions are rejected.
     - `dotnet publish --output X` copies IL .dlls but NOT the AOT'd app `.wasm` — that lands at `bin/Release/.../wasi-wasm/AppBundle/<App>.wasm` always.
     - wasmtime needs `-Shttp` to enable wasi:http (Mono imports it via System.Net.Http transitively); `--dir=.` (not absolute) for ICU lookup.
   - See [design](../plans/2026-05-23-phase-1.2-design.md) + [implementation plan](../plans/2026-05-23-phase-1.2-implementation-plan.md).
- ✅ **Phase 1.3** — `[UnmanagedCallersOnly]` export verification — *complete (2026-05-24)*
   - Verified `blazornative_dispatch_event` appears in the AOT'd `.wasm` via direct byte-scan in `tests/BlazorNative.Wasi.Tests/ExportSmoke.cs`. No external tool required at test time.
   - Iteration findings: (a) `wasmtime --invoke` rejects core-module exports through the component-model layer wasi-experimental emits; (b) `wasm-tools print` works but produces ~10 MB output, piping deadlocks the test; (c) direct in-process byte scan settled cleanly.
   - **Critical fix uncovered:** `[UnmanagedCallersOnly]` alone wasn't enough of a trim root on Mono-AOT — `WasiBridge.DispatchEvent` got stripped completely (the string was absent from the 13 MB .wasm). Added `[DynamicDependency(All, typeof(WasiBridge))]` on `Program.Main` in `src/BlazorNative.WasiHost/WasiEntryPoint.cs`.
   - Also extracted `WasmtimeRunner` from `BootSmoke` for shared subprocess-invocation helpers.
   - See [design](../plans/2026-05-24-phase-1.3-design.md) + [implementation plan](../plans/2026-05-24-phase-1.3-implementation-plan.md).
- ✅ **Phase 1.4** — `DispatchEventAsync` signature fix — *complete (2026-05-24, work landed in Phase 1.1)*
   - `BlazorInterop.RefAccessors.DispatchEventAsync` (`src/BlazorNative.Renderer/BlazorInterop.cs:216-222`) declares `[UnsafeAccessor(Method)]` with `[UnsafeAccessorType("Microsoft.AspNetCore.Components.RenderTree.EventFieldInfo, Microsoft.AspNetCore.Components")]` for the internal `EventFieldInfo` parameter. Public helper `BlazorInterop.DispatchEventViaAccessor(renderer, handlerId, args)` passes `null` for `fieldInfo` (matches what we need for M1).
   - `NativeRenderer.DispatchUiEventAsync` (`src/BlazorNative.Renderer/NativeRenderer.cs:281-291`) calls the helper instead of the now-removed `WebEventData` shim.
   - `BlazorInterop.VerifyAccessors` (`BlazorInterop.cs:63-82`) probes the Blazor side at type-load — if `Renderer.DispatchEventAsync(ulong, EventFieldInfo?, EventArgs)` is renamed or its signature drifts, `BlazorVersionMismatchException` fires with the offending member named.
   - Formally closed out during Phase 1.4; no new code or test required.
- ✅ **Phase 1.5** — Analyzer scoping + DoD wording cleanup — *complete (2026-05-24)*
   - `BlazorNative.Analyzers` wired as `<ProjectReference OutputItemType="Analyzer">` from `BlazorNative.WasiHost` only — Core / Renderer / Http / DevHost / tests don't reference it, so analyzer-graph scoping satisfies DoD #7 without per-folder `.editorconfig` suppression. Verified: 0 BN0001-BN0013 warnings on full-solution build.
   - DoD wording housekeeping flagged by the 2026-05-24 mid-milestone audit: #1c (`BlazorNative.Core.wasm` → `BlazorNative.WasiHost.wasm` + drop pre-AOT launcher-script references), #3 (`src/BlazorNative.Core/WasiEntryPoint.cs` + ".NET 9 cooperative scheduler" → `src/BlazorNative.WasiHost/WasiEntryPoint.cs` + ".NET 10 Mono-WASI sync `Main`"), #4 (`wasm-tools dump | grep` → in-process byte-scan + note the `[DynamicDependency]` trim-root requirement), #7 (refined to project-graph scoping).
   - Suppressed RS2008 on the Analyzers csproj (release-tracking files are P3 work alongside the analyzer test harness).

---

### 🔄 Milestone 2 — P1: First End-to-End Demo on Android  *(active, started 2026-05-24)*

Render a Blazor component as native Android widgets via a Kotlin shell that embeds Wasmtime and loads our WASI module. Full goal + 8-point DoD: [MILESTONE.md](MILESTONE.md).

Maps to BACKLOG.md "P1 — First end-to-end demo", plus the Mono-WASI async-trap remediation carried over from M1.

Phases:
- ✅ **Phase 2.0** — Mono-WASI async-trap remediation — *complete (2026-05-25)*
   - `IMobileBridge.NativeEvents` changed from `event AsyncEvent<NativeEvent>` to `event Action<NativeEvent>`. `WasiBridge.DispatchEvent` split into `[UnmanagedCallersOnly] DispatchEventNative` (the export) + `internal static DispatchEventCore` (managed-callable, used by both the export and `Main`'s self-test). Multicast via `GetInvocationList()` + per-handler try/catch so one subscriber's exception doesn't strand siblings.
   - New analyzer rule `BN0014` (error severity) flags async lambdas / async-method registrations against `IMobileBridge.NativeEvents` at compile time — closes the `async void` footgun that `Action<T>` alone permits.
   - End-to-end verified by new `[BOOT] event-ok fired=True name=self-test` marker emitted from `Main`'s self-test, asserted by `BootSmoke`. The trap is structurally absent from the call chain — no `Task` / `ValueTask` / `Wait` from `DispatchEventNative` → `DispatchEventCore` → subscriber.
   - New `tests/BlazorNative.Analyzers.Tests/` project establishes the analyzer-test infrastructure (BACKLOG P3 follow-up can drop in BN0001-BN0013 tests alongside).
   - 11 commits + atomic Tasks 1-3 bundle. See [design](../plans/2026-05-25-phase-2.0-design.md) + [implementation plan](../plans/2026-05-25-phase-2.0-implementation-plan.md).
   - **2026-05-25 follow-ups landed:** xUnit Collection for WasiBridge.Current singleton safety; DevHostBridge multicast-symmetry test; BN0014 async-method-reference test; payload in [BOOT] event-ok marker; trim-root doc comment on DispatchEventCore; stale DispatchEvent → DispatchEventNative doc renames. Final test count: 12 passed, 2 skipped.
- ✅ **Phase 2.1** — JVM desktop hosts `.wasm` via libwasmtime + JNA — *complete (2026-05-26)*
   - **GREEN CHECKPOINT met:** JNA loads `vendor/wasmtime/wasmtime.dll` (cargo-built from `bytecodealliance/wasmtime` v45.0.0 source, ~31 MB) into the JVM in-process, hosts `BlazorNative.WasiHost.wasm` as a wasmtime component, invokes the `wasi:cli/run` export, captures stdout via `wasi_config_set_stdout_file`, and `BootSmokeTest` asserts all 4 `[BOOT]` markers (parity with `.NET`-side `BootSmoke.cs`). Cross-validates the .wasm boots identically in subprocess `wasmtime` CLI and in-process JNA-bound `libwasmtime`. Strategy G (RN-Hermes pattern) validated on desktop JVM. Phase 2.2 ports to Android by cross-compiling `libwasmtime.so` for android-arm64 + adding the Android Gradle plugin; the Kotlin/JNA layer ships unchanged.
   - **Phase 2.1.0 format-pivot spike was a NO-OP** (committed `16a8e5f`). The original brainstorm premise that we needed to strip `wasi:http/types` was based on Phase 1.3 observations conflating wasmtime CLI's `-Shttp` flag with actual component imports. Baseline WIT capture (`docs/plans/2026-05-26-phase-2.1.0-baseline-wit.txt`) showed `wasi:http` was already absent. Additionally, Mono-AOT's trimmer stripped the unused `[DllImport("mobile_bridge")]` declarations in `WasiBridge.Native` (nothing in the reachable graph from `Main` calls any `shell_*` extern). The .wasm's actual import surface is just standard `wasi:*` interfaces, which wasmtime satisfies natively via `wasmtime_component_linker_add_wasip2` + `wasmtime_context_set_wasi`. **Carryover question for Phase 2.3:** when reviving `mobile_bridge` imports, choose between (a) `[DynamicDependency(All, typeof(Native))]` to root the externs, or (b) migrate to `wit-bindgen`-generated typed bindings. See `docs/plans/2026-05-26-phase-2.1.0-spike-conclusion.md`.
   - **Toolchain hardening landed mid-phase:** `setup.ps1` extended with sections for wasm-tools (cargo install), CMake (winget — wasmtime-c-api build.rs prereq), Temurin JDK 21 (winget — Gradle 8.x daemon needs JDK 8-23, JDK 25 on box was too new), and libwasmtime cargo build (clone + cargo build wasmtime-c-api + copy to `vendor/wasmtime/`). `vendor/`, `.gradle/`, `build/` added to `.gitignore`.
   - **New `src/BlazorNative.Jni/` Kotlin/Gradle module:**
     - `build.gradle.kts` — Kotlin 2.0.21 + JNA 5.14 + JUnit 5.11.3 + `jvmToolchain(21)`. `systemProperty` wiring for `wasm.path` and `jna.library.path`.
     - `WasmtimeBindings.kt` — JNA `Library` interface with the C-API surface used by Phase 2.1: engine / config / store / context / component / linker / instance / func / WASI config / error / trap / wasm_byte_vec helpers. JNA `Structure` wrappers for `ComponentInstance` and `ComponentFunc` (16-byte value types, NOT opaque pointers — important finding, the plan's spec was wrong).
     - `WasiHost.kt` — high-level façade: `loadAndRun(wasmPath: Path): String`. Engine + config(component-model=on) + store + WASI config(preopen_dir=AppBundleDir, argv=["BlazorNative.WasiHost.wasm"], stdout=tempfile) + component_new + linker_new + linker_add_wasip2 + linker.instantiate(store, component) + get_export_index for `wasi:cli/run@0.2.0`/`run` + func_call. Cleanup at the end.
     - `WasmtimeException.kt` + `WasmName.kt` — error marshaling helpers.
     - `BootSmokeTest.kt` — single JUnit 5 test asserting the 4 `[BOOT]` markers.
   - **C-API symbol-name corrections vs plan spec** (discovered via dumpbin + reading `vendor/wasmtime-src/crates/c-api/include/wasmtime/component/*.h`):
     - `wasmtime_config_new` is actually `wasm_config_new` (upstream wasm-c-api prefix).
     - `wasm_name_delete` doesn't exist; `wasm_byte_vec_delete` is the right call.
     - `wasmtime_component_instantiate` is actually `wasmtime_component_linker_instantiate` (signature is `(linker, context, component, instance_out) → error*`).
     - `wasmtime_component_instance_get_func` takes an `export_index` (opaque), not a name string — two-step lookup via `wasmtime_component_get_export_index`.
     - `wasmtime_component_func_call` has no trap out-param; traps come back as `wasmtime_error_t*`.
     - `wasmtime_component_linker_add_wasip2(linker)` is the canonical way to satisfy all standard `wasi:*` imports.
   - **Test count after Phase 2.1:** `.NET` test suite unchanged (12 passed, 2 skipped). Kotlin/JNA side adds 4 new tests (`EngineLifecycleTest`, `ComponentLoadTest`, `LinkerLifecycleTest`, `BootSmokeTest` — all green).
   - Commits: `252ee02`, `3ccc927`, `16a8e5f`, `685632b`, `3d0246b`, `172fba8`, `991c2ac`, `3651543`, `de77f23`, `ff4d7c7`, `0b4ca93`. See [design](../plans/2026-05-26-phase-2.1-design.md) + [implementation plan](../plans/2026-05-26-phase-2.1-implementation-plan.md) + [spike conclusion](../plans/2026-05-26-phase-2.1.0-spike-conclusion.md).
- ✅ **Phase 2.2** — Android port: cross-compile libwasmtime + Android Gradle scaffold — *complete (2026-05-26)*
   - **GREEN CHECKPOINT met:** `./gradlew connectedAndroidTest` → `BootSmokeAndroidTest > boots_and_emits_markers_on_android PASSED` on `blazornative-pixel6-x86_64` AVD. The same `.wasm` boots identically in three runtimes — wasmtime CLI subprocess (.NET-side `BootSmoke`), JVM in-process JNA (Phase 2.1's `BootSmokeTest`), AND Android in-process JNA. Three-way cross-validation complete.
   - **Toolchain:** `setup.ps1` sections 7 + 8c extended. NDK 26.3.11579264 + `system-images;android-34;google_apis;x86_64` + emulator + AVD `blazornative-pixel6-x86_64` installed via sdkmanager + avdmanager. `cargo-ndk` wrapper + Rust targets `aarch64-linux-android` + `x86_64-linux-android` added. `libwasmtime` cross-compiled for both ABIs into `vendor/wasmtime/jniLibs/` (arm64-v8a: 36 MB, x86_64: 39 MB). Three fixes landed during execution: JAVA_HOME=JDK21 before sdkmanager invocations (Java 8 was first on PATH → UnsupportedClassVersionError); idempotent 7a cleanup of stale `cmdline-tools/latest` (partial-install recovery); both prereq winget installs (CMake, JDK 21) for cargo + Gradle.
   - **Module restructure:** existing `src/BlazorNative.Jni/` extended with Android Gradle Plugin 8.7.3 + `kotlin("android") 2.0.21`. Shared Kotlin sources under `src/main/kotlin/` literal-reused across JVM tests and Android shell. New `androidMain/` source set (MainActivity, manifest, layout, assets, jniLibs). New `androidTest/` source set (BootSmokeAndroidTest). JNA scoped per-classpath: `:aar` on `implementation` (APK runtime, bundles `libjnidispatch.so` for both ABIs), `:jar` on `testImplementation` (JVM unit tests, bundles desktop dispatch). Packaging block excludes duplicate META-INF license files; `gradle.properties` enables `android.useAndroidX=true`.
   - **WasiHost.kt refactored:** primary signature now `(ByteArray, File)` (Android uses `assets + cacheDir`); legacy `(Path)` overload preserved as a thin delegate (JVM `BootSmokeTest` unchanged).
   - **Gradle automation:** `copyWasm` + `copyJniLibs` tasks wired to `preBuild` — APK always contains latest `.wasm` + `libwasmtime.so`. Both tasks fail-fast with actionable messages if source files missing. `.gitignore` extended to exclude the derived destinations.
   - **MainActivity:** green-on-black console-style ScrollView + TextView. Loads .wasm from assets on background thread, calls `WasiHost.loadAndRun(bytes, cacheDir)`, emits captured stdout to logcat (`Log.i("BlazorNative", ...)` per line) AND displays in the TextView. Catches throwables → `FAIL: ...` for diagnosis.
   - **One known issue documented as Phase 2.2b carryover:** `wasmtime_component_linker_delete` triggers SIGABRT (Scudo "corrupted chunk header") on Android's hardened allocator. The wasm execution completes BEFORE the crash — all 4 `[BOOT]` markers ARE captured to the stdout file. Workaround: skip the cleanup deletes (process-scoped leak, bounded to one test/app launch — acceptable for POC GREEN CHECKPOINT). Root-cause investigation deferred to BACKLOG.
   - **Test count after Phase 2.2:** `.NET` suite still 12 passed / 2 skipped (no change). JVM-side Kotlin tests still 4 passed (Phase 2.1's unchanged). Android-side: 1 new instrumented test (`BootSmokeAndroidTest`).
   - Commits: `3a2c807`, `1ef3e67`, `f15c48a`, `77173c3`, `3ccb4b1`, `7cbb4bc`, `58eb30f`, `8a24939`, `df5d0ac`, `c04d689`. Strategy G (RN-Hermes pattern) validated end-to-end: portable .wasm artifact + per-platform native libwasmtime + shared JNA bindings layer + thin per-platform Activity shell. See [design](../plans/2026-05-26-phase-2.2-design.md) + [implementation plan](../plans/2026-05-26-phase-2.2-implementation-plan.md).
- ⏳ **Phase 2.3** — `mobile_bridge` symbol implementations (Android side) — *pending*
- ⏳ **Phase 2.4** — Render-frame consumer (WASM-side dispatch + Android-side parse) — *pending*
- ⏳ **Phase 2.5** — Native widget mapper (`NodeType` → Android widgets) — *pending*
- ⏳ **Phase 2.6** — `BlazorNativeHostElement` stub (renderer-side host element descriptor) — *pending*
- ⏳ **Phase 2.7** — End-to-end demo + final audit — *pending*

---

### ⏳ Milestone 3 — P2: Real Apps Can Be Built  *(pending)*

`@bind` two-way binding, `Bn*` component library, cascading values, end-to-end DI, navigation service, `BlazorNativeComponentBase` ergonomics.

Maps to BACKLOG.md "P2 — Real apps can be built".

---

### ⏳ Milestone 4 — P3: Production-Shippable  *(pending)*

Analyzer unit tests, `.editorconfig` analyzer scoping (full), GitHub Actions CI, iOS Swift shell, DevTools render-tree inspector, `wit-bindgen` C# bindings committed, initial NuGet packages, WASI hot-reload protocol.

Maps to BACKLOG.md "P3 — Production readiness".

---

### ⏳ Milestone 5 — P4: Full Platform Coverage  *(pending, parallel with M6/M7)*

Android shell complete (lifecycle, permissions, FCM, secure storage, deep links, predictive back). iOS shell complete (APNs, Keychain, universal links, App Store validation). Cross-platform APIs: geolocation, camera, clipboard, share, haptics, biometrics, purchases, background tasks.

Maps to BACKLOG.md "P4 — Full platform coverage".

---

### ⏳ Milestone 6 — P5: Developer Ecosystem  *(pending, parallel with M5/M7)*

`BlazorNative.Components`, `BlazorNative.Styling`, `BlazorNative.State`, `BlazorNative.Navigation`, `BlazorNative.Cli` global tool, full test infrastructure, CI/CD release pipeline, documentation site, NuGet packaging.

Maps to BACKLOG.md "P5 — Developer experience and ecosystem".

---

### ⏳ Milestone 7 — P6: Framework Hardening  *(pending, parallel with M5/M6)*

Security model (signed WASM, URL allowlist, secure buffers, crash isolation), error handling and crash recovery, accessibility, i18n (with `InvariantGlobalization` workaround), performance monitoring, memory management, WIT contract hardening.

Maps to BACKLOG.md "P6 — Framework hardening".

---

### ⏳ Milestone 8 — P7: Enterprise Readiness  *(pending)*

OTA updates with delta + rollback, multi-window support, Material Design 3 / iOS HIG compliance, platform gesture recognition, keyboard avoidance, safe-area handling, legal compliance (SBOM, license audit, GDPR, export control, FIPS), observability and analytics, performance budget enforcement.

Maps to BACKLOG.md "P7 — Enterprise readiness".

---

### 🔮 Future / Exploratory  *(no milestone assigned)*

WASM Component Model migration, Windows/macOS shells, BlazorNative Studio, `RAG.net` integration, `ZeroAlloc.EventSourcing` deep integration, ZeroFlux/StaticFlux as default state, `BlazorNative.Templates`, bol.com reference app.

See BACKLOG.md "Future / exploratory" — these promote into milestones when ecosystem maturity and demand justify them.

---

## Notes

- Milestones M5, M6, M7 are explicitly **parallel** per BACKLOG's phase summary — they may be worked concurrently after M4 is complete.
- M8 requires M6 + M7.
- Each milestone closes with `audit-milestone` → `complete-milestone` → tag `vN.0`.
