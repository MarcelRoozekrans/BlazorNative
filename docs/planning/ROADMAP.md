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

### ✅ Milestone 2 — P1: First End-to-End Demo on Android  *(complete 2026-05-28, tagged `v2.0`)*

8 phases (2.0 — 2.7) + Phase 2.8 close-out shipped. Same `.wasm` boots
identically in three runtimes (wasmtime CLI, JVM JNA, Android JNA);
HelloComponent renders as native Android widgets via the Kotlin shell.
Audit verdict: PASS WITH PIVOTS (7/8 DoD PASS, #4 PIVOTED per Phase 2.3).
See [final audit](../plans/2026-05-27-milestone-2-final-audit.md).

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
- ✅ **Phase 2.3** — `mobile_bridge` revival via env-var bridge (`shell-platform-info`) — *complete (2026-05-26)*
   - **GREEN across all three runtimes:** the same `.wasm` emits `[BOOT] bridge-ok platform-info=<json>` with a host-supplied payload via wasmtime CLI subprocess (.NET-side `BootSmoke`), JVM in-process JNA (`BootSmokeTest`), AND Android in-process JNA (`BootSmokeAndroidTest`). Marker payload differs per host (Defaults stub vs Android `Build.*` vs CLI literal), proving real round-trip vs constant baked into .wasm. Sentinel-based `BridgePlatformInfoTest` further verifies the host's lambda actually fires.
   - **Design pivoted mid-execution** (`docs/plans/2026-05-26-phase-2.3-design-revision.md`, commit `e2975b7`). Original design called for WIT-typed imports via `wasmtime_component_linker_define_func`; Task 2's spike (commit `3aa83c9`) found **three wasi-experimental SDK gaps** that block custom WIT imports from materializing in the .wasm (custom `_WasmPInvokeModules`, `allow-undefined` linker arg, missing `wit-component` MSBuild bridge). Pivoted to env-var bridge over standard `wasi:cli/environment` — zero SDK gaps, ships today.
   - **Architecture:** host calls `MobileBridgeHandlers.platformInfo()` once during `WasiHost.loadAndRun` setup, passes the result via `wasi_config_set_env(wasiConfig, "BLAZOR_PLATFORM_INFO", json)` before `wasmtime_context_set_wasi`. `.NET` reads via `Environment.GetEnvironmentVariable("BLAZOR_PLATFORM_INFO")` which Mono-WASI implements through the standard `wasi:cli/environment.get-environment` surface that wasi-experimental's component-adapter pre-includes.
   - **Files touched:** `mobile_bridge.wit` (documentation-only); `IMobileBridge.cs` (+ sync `PlatformInfo` getter); `WasiBridge.cs` (delete `Native` class — 7 dead [DllImport]s gone; `PlatformInfo` reads env var); `DevHostBridge.cs` (+ matching `PlatformInfo`); `WasiEntryPoint.cs` (+ bridge-ok marker emission); `MobileBridgeHost.kt` (new; data class + Defaults); `WasmtimeBindings.kt` (+ `wasi_config_set_env` binding, 1 line); `WasiHost.kt` (accepts `MobileBridgeHandlers`); `AndroidPlatformInfo.kt` (new; real `Build.*` JSON); `MainActivity.kt` (passes AndroidPlatformInfo); test files (5 new/modified); `WasmtimeRunner.cs` (refactor to `ArgumentList` for JSON-containing args).
   - **Trade-offs accepted:** one-way (host → .NET), init-time only — correct semantics for platform-info, insufficient for dynamic runtime event callbacks. Phase 2.5+ revisits with export-based pattern when UI events need it.
   - **Test count after Phase 2.3:** `.NET` suite **12 passed**, 2 skipped, 0 failed (no regression; the bridge-ok marker is now part of `BootSmoke.cs`); JVM `./gradlew testDebugUnitTest` → **5 passed** (4 existing + `BridgePlatformInfoTest`); Android instrumented → **1 passed** (`BootSmokeAndroidTest` with new bridge-ok assertion).
   - 9 commits: `25efdb2`, `3aa83c9`, `e2975b7`, `aada6bf`, `83774e9`, `c18641e`, `98b3134`, ... See [design](../plans/2026-05-26-phase-2.3-design.md) + [revision](../plans/2026-05-26-phase-2.3-design-revision.md) + [implementation plan](../plans/2026-05-26-phase-2.3-implementation-plan.md).
- ✅ **Phase 2.4** — Render-frame consumer (WASM-side dispatch + Android-side parse) — *complete (2026-05-27)*
   - **Three-way GREEN with one observed flake (5/6 successive Android runs pass).** Same `.wasm` mounts the `_BridgeFrameSelfTest` sentinel in `Main`, emits one `[FRAME] {json}` line via sync `Console.WriteLine`; host parses by line-prefix, deserializes via `kotlinx.serialization` sealed `RenderPatch` hierarchy keyed on `op` discriminator; `handlers.onFrame` fires with the parsed frame. **Wasmtime CLI subprocess (.NET-side `BootSmoke` + `FrameSelfTestParsesAsRenderFrame`): PASS.** **JVM in-process JNA (`BootSmokeTest` + `FrameDispatchTest`): PASS.** **Android in-process JNA (`BootSmokeAndroidTest`): PASS (1/6 historical flake).** Task 14's initial Android run hit a non-deterministic `panic_bounds_check` in `wasmtime::Func::call → post_return_impl → export_lifted_function`; subsequent 5 runs (including dedicated Phase 2.4b reproducibility investigation) all PASS. Same family as Phase 2.2's intermittent Scudo-allocator issues — Android-x86_64-emulator-specific, low rate, root cause untraced (no actionable reproduction). Documented as Phase 2.4b watch item; not blocking.
   - **Sync mount API:** new `NativeRenderer.Mount<T>` calls `InstantiateComponent + AssignRootComponentId + RenderRootComponentAsync` directly (bypasses `MountAsync`'s async wrapper), inspects the inner Task's `IsCompletedSuccessfully`. No `.GetAwaiter().GetResult()` anywhere. **Three cascading Mono-WASI defects discovered in Task 4 investigation:** (a) `Dispatcher.CreateDefault()` is not inline-only on Mono-WASI even when work completes synchronously — replaced with a private `InlineDispatcher` that runs work directly on the calling thread; (b) `async`/`await` inside `MountAsync`'s lambda re-queues continuations even with an inline dispatcher — simplified to `() => AddComponentAsync(...)`; (c) `default(ParameterView)` throws NRE in `ComponentState.SupplyCombinedParameters` on Mono-WASI AOT (silently swallowed by `HandleException`, masquerading as "mount succeeded but nothing rendered") — `Mount<T>()` parameterless overload explicitly passes `ParameterView.Empty`. Plus AOT-trimming requirement: `[DynamicDependency(All, typeof(_BridgeFrameSelfTest))]` on Main keeps the component's ctor alive. All four findings documented in code comments.
   - **`DispatchFrame` is sync** — replaces the dead `DispatchFrameAsync` which awaited `_bridge.WriteStorageAsync` + `_bridge.FetchAsync` (both `NotImplementedException` stubs). Three lines, no bridge surface used. The now-unused `IMobileBridge` ctor dependency was dropped from `NativeRenderer` in a follow-up cleanup commit.
   - **Streaming spike: PASS at Rung 1** (tee'd stdout via `wasi_config_set_stdout_file`). Spike validated that the wasm's `Console.WriteLine` flushes line-by-line, observable from a 10ms background poller, with ≤15ms host-visible latency. Phase 2.5+ can build streaming on this primitive without inventing new transport. Full investigation in [`docs/plans/2026-05-27-phase-2.4-streaming-paths.md`](../plans/2026-05-27-phase-2.4-streaming-paths.md).
   - **Test counts:** `.NET` 12 + MountSyncTests (2) + Task-4-followup MountSyncTests (2) + FrameSelfTestParsesAsRenderFrame (1) = **17 passed / 2 skipped / 0 failed**; JVM 5 + RenderFrameSerializationTest (1) + FrameStreamParserTest (3) + FrameDispatchTest (1) + StreamingSpike_Rung1Test (1) = **11 passed**; Android instrumented 1 FAIL (the wasmtime regression above — to be unblocked via Phase 2.4b).
   - **Carryover items for future phases:**
     - **Phase 2.4b — Android wasmtime regression investigation.** Wasmtime v45 `post_return_impl` bounds-check on Android x86_64 after Phase 2.4's larger reachable IL surface. Try wasmtime v46+ upgrade, cargo cross-build flag review, or open upstream issue.
     - **Task 2 follow-up — `Mount<T>` faulted-task masking.** If `MountAsync` faults synchronously (constructor throws, etc.), `IsCompletedSuccessfully==false` → current diagnostic masks the real exception. Cheap fix: `if (task.IsFaulted) task.GetAwaiter().GetResult(); // rethrow original` before the generic check.
     - **Task 7 follow-ups.** `NativeUiEvent` is defined in `.NET` `PatchProtocol.cs` (lines 109-113) but not yet mirrored on the Kotlin side — needed when Phase 2.5+ wires native gesture events back to `.NET`. Also: `RenderFrame` serialization is currently read-only on the Kotlin side; future Phase 3+ symmetric round-trip + golden fixtures could lock the contract from both ends.
     - **Task 15 finding — BN0004 analyzer.** The `WasiThreadingAnalyzer` (BN0004) flags `Thread.Sleep` as forbidden, but the streaming-spike code path requires it (Mono-WASI's `Task.Delay` throws PNSE). Suppressed via pragma with explanatory comment in `WasiEntryPoint.cs`; consider adding a `// WASI: blocking-sleep-acceptable` opt-in annotation pattern instead.
   - **19 commits.** Task 1: `6f7500f`. Task 2: `b9d7dd0`. Task 3 + follow-ups: `ee514d6`, `9a43460`, `d238ee1`. Task 4 + fix + follow-ups: `3d8db4e`, `f58b16f`, `26d3274`. Task 5: `70bd5e5`. Task 6: `dc36715`. Task 7: `708f58d`. Task 8: `172241b`. Task 9: `9ae7c6f`. Task 10: `e94043b`. Task 11: `eb6bc6e`. Task 12: `561ca41`. Task 13: `ac9d3e1`. Task 14 marker: `11f352f`. Task 15: `2f0e497`. See [design](../plans/2026-05-26-phase-2.4-design.md) + [implementation plan](../plans/2026-05-26-phase-2.4-implementation-plan.md) + [streaming-paths](../plans/2026-05-27-phase-2.4-streaming-paths.md).
- 👁 **Phase 2.4b** — Android wasmtime flake watch — *low-priority, no scheduled work* *(2026-05-27)*
   - Original framing as "Phase 2.4 regression that needs fixing" was wrong — the failure was a 1/6 non-deterministic flake, not a deterministic regression. Phase 2.4b investigation re-ran `connectedAndroidTest` 5 times after the initial Task 14 failure; all 5 passed cleanly. No actionable reproduction → no root cause to fix → no fix to ship. **Action:** keep watching for recurrence in CI / future runs. Same family as Phase 2.2's intermittent Scudo-allocator issues. If recurrence rate rises (e.g., >10% on CI), revisit with: (a) wasmtime v46+ upgrade + re-cross-compile via `cargo ndk`; (b) JNA Structure clear/init audit on the call path; (c) minimal-repro for upstream wasmtime issue.
- ✅ **Phase 2.5** — Native widget mapper (`NodeType` → Android widgets) — *complete (2026-05-27)*
   - **Three-way GREEN with widgets on screen.** Sentinel `_BridgeFrameSelfTest` mounted in `Main` now renders as a real Android `TextView("frame-self-test")` inside a vertical `LinearLayout`, inside the `widget_root` FrameLayout in MainActivity's split-screen layout. New `WidgetMapperTest` instrumented test launches MainActivity via `ActivityScenario`, polls the widget tree, and asserts the structure (1 LinearLayout containing 1 TextView with the expected text). `BootSmokeAndroidTest` continues to assert the [FRAME] marker round-trip via captured stdout. Full pipeline validated end-to-end: .NET `DispatchFrame → [FRAME] stdout → FrameStreamParser → handlers.onFrame → WidgetMapper.apply → mainHandler.post → View mutations`.
   - **Architecture:** `WidgetMapper` — single class with `when (patch)` switch over 4 active patch types (CreateNode, ReplaceText, RemoveNode, CommitFrame). 5 deferred types (UpdateProp/SetStyle/AttachEvent/DetachEvent/AppendChild) stubbed with `Log.w` "TODO Phase 3+". Caller-thread-agnostic; collects patches per frame and atomically posts the batch to the main looper on CommitFrame. All 7 NodeTypes from DoD #6 wired in the create switch (view/text/button/input/image/scroll/picker) plus an `else → TextView` fallback for unknown types; only view+text exercised by the sentinel today.
   - **.NET-side fix (Task 1):** `NativeRenderer.ProcessFrame` now populates `CreateNodePatch.ParentId` for child elements/text — without it, the mapper would have attached every node to the widget root (TextView as sibling of LinearLayout instead of inside it). 5-line change; backward-compatible (existing renderer tests pass unchanged since `ParentId` already defaulted to `null`). **Known follow-up (Task 1 review finding):** the `PrependFrame` arm in `ProcessRenderTreeDiff` currently passes `parentNodeId: null`, which is correct for root-component PrependFrames but wrong for nested-component re-renders (each child component's diff arrives as a separate `BnRenderTreeDiff` and the root of *that* subtree should attach to the parent component's view, not to the host root). Track for Phase 3+/M3 when multi-component support lands.
   - **MainActivity layout:** split-screen 60/40. Top `FrameLayout(@+id/widget_root)` (white background) hosts the rendered widgets. Bottom keeps the green-on-black console `ScrollView+TextView(@+id/markers)` so BOOT/FRAME markers remain visible for diagnostics. Phase 2.7's audit can decide whether to drop the console for the polished demo.
   - **Task 7 deviation from plan:** `WidgetMapperTest` polling deadline extended from spec'd 10s to **60s** (with 250ms sleep interval). Justified: cold wasmtime JIT + Mono AOT init of the 14 MB .wasm on the AVD x86_64 measured ~36s in logcat. The poller exits as soon as `widget_root.childCount > 0`, so the extended deadline only affects fail-detection latency, not the happy path. Documented in the test's KDoc.
   - **Test counts:** `.NET` unchanged at **17 passed / 2 skipped** (the new assertion extends `FrameSelfTestParsesAsRenderFrame`, no new test file). JVM unchanged at **11 passed** (mapper is `androidMain`-only). Android +1 instrumented test: **2 passed** total (BootSmokeAndroidTest + WidgetMapperTest). One Phase 2.4b flake-pattern wasmtime crash recurred during Task 7 execution; passed cleanly on retry. The 1-in-N flake remains a known Android-x86_64 wasmtime characteristic; not Phase 2.5-specific.
   - **Carryover items:**
     - 5 deferred patch types (UpdateProp, SetStyle, AttachEvent, DetachEvent, AppendChild) — Phase 3+ when the M3 component library needs them.
     - 5 unexercised NodeTypes (button, input, image, scroll, picker) — wired in the switch, tests land as real components use them.
     - Event flow (host→.NET button click) — Phase 2.6+/M3 separately.
     - Nested-component `PrependFrame` parenting (Task 1 review finding) — Phase 3+.
   - **9 commits.** Task 1: `9e805bb`. Task 2: `d3c0bc3`. Task 3: `e1b0eef`. Task 4: `fbbae93`. Task 5: `ffd236d`. Task 6: `9e801a1`. Task 7: `e56eb6c`. Task 8 GREEN marker: `65e200a`. Task 9: this update. See [design](../plans/2026-05-27-phase-2.5-design.md) + [implementation plan](../plans/2026-05-27-phase-2.5-implementation-plan.md).
- ✅ **Phase 2.6** — Widget mapper completeness — *complete (2026-05-27)*
   - **Three-way GREEN.** 14 new instrumented tests pass on the AVD: 5 NodeType coverage tests (button, input, image, scroll, picker), 4 UpdateProp tests (placeholder happy/wrong-widget + enabled true/null-defaults-true), 5 SetStyle tests (backgroundColor + fontSize numeric + fontSize sp-suffix + padding all-4-sides + unknown-property no-op). `.NET` unchanged at 17/2; JVM unchanged at 11 (mapper is androidMain); Android **16 passed** (2 existing + 14 new).
   - **`UpdateProp` handler:** `placeholder` (EditText.hint, log-ignore on wrong widget), `enabled` (View.isEnabled, `null` value defaults to `true` matching HTML's absence-means-enabled semantics). Unknown property names + unknown nodeIds log-and-ignore without crashing.
   - **`SetStyle` handler:** `backgroundColor` (Color.parseColor accepts `#RGB` / `#RRGGBB` / `#AARRGGBB` / named colors), `fontSize` (TextView only, sp units, suffix-strips `sp`/`dp`/`px`), `padding` (any View, dp→px via `TypedValue.applyDimension`, all 4 sides equal). Helper functions `parseColorOrNull` / `parseFloatOrNull` for safe coercion.
   - **Test infrastructure:** synthetic frame fixtures via `InstrumentationRegistry.runOnMainSync` + `waitForIdleSync` for ~1s tests (vs. Phase 2.5 WidgetMapperTest's ~36s cold-boot pattern). The mapper's `Handler(Looper.getMainLooper()).post` batch dispatch still happens, but is drained by `waitForIdleSync` before assertions read the view tree. Helper duplicated across 3 test files (~18 lines each); hoist to a shared `WidgetMapperTestHelpers.kt` only when a 4th file lands.
   - **Out of scope** (genuine M3 work, see M3 inheritance section below): AttachEvent/DetachEvent (bidirectional event flow), AppendChild (not emitted), nested-component PrependFrame parenting, per-side padding, ImageView `src` URI handling, Spinner/EditText `value` semantics, color names beyond `Color.parseColor` builtins, style-shorthand parsing.
   - **Operational notes from execution:** one Phase 2.4b wasmtime flake recurred during the Android sweep (`BootSmokeAndroidTest` "Process crashed"); passed cleanly on first re-run. One MSBuild ILLink task-host hang on the Wasi project; resolved by `dotnet build-server shutdown` + republish (same pattern as Phase 2.4 Task 6 / Phase 2.5 Task 8 — known flake of this dev environment).
   - **5 commits.** Task 1: `4c5da6e`. Task 2: `28aae97`. Task 3: `390087c`. Task 4 GREEN marker: `5ab2285`. Task 5: this update. See [design](../plans/2026-05-27-phase-2.6-design.md) + [implementation plan](../plans/2026-05-27-phase-2.6-implementation-plan.md).
- ✅ **Phase 2.7** — Renderer hardening (Bugs A + B fix, spike graduation) — *complete (2026-05-27)*
   - **Three-way GREEN.** `.NET` 23 passed / 2 skipped (Renderer 16/2 = 10 original + 5 spike probes + 1 BugA regression); JVM 11 (unchanged); Android 16 (unchanged).
   - **Original "BlazorNativeHostElement stub" framing rejected.** Phase 2.7 was speculatively scoped before Phase 2.4-2.6 proved that `AssignRootComponentId`-based mounting is sufficient. The Phase 2.7 host-element investigation spike (doc: [`docs/plans/2026-05-27-phase-2.7-host-element-spike.md`](../plans/2026-05-27-phase-2.7-host-element-spike.md)) ran 5 probes covering parameters, events, nested components, `[Inject]` DI, and `CascadingValue` — all PASS with the current renderer. **No host element abstraction needed; no AngleSharp dependency added.** The spike surfaced two real renderer bugs instead, which became Phase 2.7's actual scope.
   - **Bug A fix** (`NativeRenderer.MountAsync<T>` no-args overload threads `ParameterView.Empty` explicitly): Phase 2.4 Task 4's sync `Mount<T>` fix was incomplete — the async variant still defaulted to `default(ParameterView)` which silently NREs in Blazor's `ParameterView.GetEnumerator` (swallowed by `HandleException`, masquerading as "mount succeeded but no frame fired"). Also fires on the .NET host CLR — recontextualizes the Phase 2.4 finding as a Blazor framework API misuse, not a Mono-WASI-specific defect.
   - **Bug B fix** (`NativeRenderer.ProcessFrame` handles `Component` frames): the subtree iteration now explicitly skips `Component` frames' `ComponentSubtreeLength`, preventing the component's child `Attribute` frames (carrying parameter values like `Label="A"`) from being mis-attributed to the parent `Element` as `UpdatePropPatch`es on the wrong nodeId. Visible output was already correct (children render via their own `ComponentRenderTreeDiff`); patch stream is now clean too. Required exposing `RenderTreeFrame.ComponentSubtreeLength` on the `BnRenderTreeFrame` wrapper.
   - **Spike graduation:** `Phase27HostElementSpike.cs` renamed to `RendererBlazorAPICoverage.cs` (90% similarity rename preserves history) as permanent regression coverage. Strengthened soft assertions to hard ones for the cascading-value probe.
   - **Operational note:** Android sweep hit the Phase 2.4b 1/N flake on first attempt (`WidgetMapperTest` timed out after 60s). Passed cleanly on first re-run. **Third recurrence of the documented Phase 2.4b pattern** since it was first observed (Phase 2.4 Task 14, Phase 2.6 Task 4, Phase 2.7 Task 4). All three re-runs PASS. Still 0% reproduction rate on re-attempt — the watch item stays low-priority.
   - **Carryover for Phase 2.8 audit:** (a) the renderer's `HandleException` silently swallowing exceptions to `Console.Error` is a debugging hazard — both Bug A and Bug B's silent symptoms came from this. Consider a strict-mode opt-in or public `LastException` property for M3. (b) Pre-existing doc-comment defect on sync `Mount<T>` in `NativeRenderer.cs` (duplicate `<summary>` opening tag inside the doc block) — surfaced during Task 1 spec review but out of scope for that fix. Cosmetic; cleanup in Phase 2.8 or M3.
   - **5 commits.** Spike: `5dcf3f9`. Bug A: `d13f25b`. Bug B: `9039d1d`. Spike graduation: `f82d734`. GREEN marker: `14cf70b`. Task 5: this update. See [spike findings](../plans/2026-05-27-phase-2.7-host-element-spike.md) + [implementation plan](../plans/2026-05-27-phase-2.7-implementation-plan.md).
- ✅ **Phase 2.8** — Hello demo + M2 final audit + runtime arch eval — *complete (2026-05-28)*
   - **Three-way GREEN.** `.NET` 23/2 (unchanged — assertions re-targeted from sentinel to Hello); JVM 11 (unchanged; one regression caught and fixed in `3007c69` — Task 1's `[DynamicDependency]` swap dropped `_BridgeFrameSelfTest`, which the dormant `BLAZOR_STREAMING_SPIKE=1` path still mounts → `CtorNotLocated` at runtime; re-added second trim-root annotation); Android 19 (16 baseline + 3 `WidgetMapperTextChildOnButtonTest` added in Task 3b; WidgetMapperTest's structural assertion now matches Hello's outer LinearLayout + 3 children shape).
   - **HelloComponent** renders on the AVD: outer LinearLayout (backgroundColor=#FFEEAA, padding=16dp) containing inner LinearLayout (fontSize=24sp "Hello, BlazorNative!") + Button ("Tap") + EditText (placeholder="Type here..."). Exercises Phase 2.5 mapper + Phase 2.6 SetStyle (backgroundColor/fontSize/padding) + Phase 2.6 UpdateProp (placeholder) in one screenshot. Evidence: `docs/plans/2026-05-27-phase-2.8-hello-screenshot.png` + `docs/plans/2026-05-27-phase-2.8-hello-logcat.txt`.
   - **Task 3b finding — text-child-on-TextView collapse.** Button/EditText extend TextView, not ViewGroup. Naive mapping would orphan child text nodes to `widget_root`. New `WidgetMapper` special-case: when a `CreateNode { nodeType=text, parentId=<TextView-derived> }` arrives, map the text node's id to its parent (no separate view); subsequent `ReplaceText` then calls `parent.setText(...)`. Matches the React Native idiom. 3 new regression tests in `WidgetMapperTextChildOnButtonTest`.
   - **M2 final audit:** `docs/plans/2026-05-27-milestone-2-final-audit.md` walks all 8 DoD criteria. **Verdict: PASS WITH PIVOTS — 7 of 8 PASS, DoD #4 PIVOTED (1 of 7 mobile_bridge exports shipped; 6 deferred to M3 per Phase 2.3 design revision).**
   - **Runtime architecture evaluation:** `docs/plans/2026-05-27-phase-2.8-runtime-architecture-eval.md` — research-subagent output with WebSearch/WebFetch evidence on all three options (wasi-experimental / componentize-dotnet / NativeAOT-per-ABI). Recommendation: start M3 on wasi-experimental (status quo); time-box 1-week componentize-dotnet spike at Phase 3.0; defer NativeAOT-per-ABI to M4/M5.
   - **Housekeeping:** sync `Mount<T>` doc-comment duplicate-`<summary>` defect fixed (Phase 2.7 carryover).
   - **M2 closed:** tagged `v2.0`. M3 ("P2 — Real Apps Can Be Built") opens with the runtime-architecture decision as its first concern.
   - See [design](../plans/2026-05-27-phase-2.8-design.md) + [implementation plan](../plans/2026-05-27-phase-2.8-implementation-plan.md).

---

### ✅ Milestone 3 — P2: Real Apps Can Be Built  *(complete 2026-07-10, tagged `v3.0`)*

10 phases (3.0a–3.0e runtime re-platform, 3.1–3.5 application layer) shipped.
A real two-page app runs on the AVD through a NativeAOT `.so` with a typed
eight-export C-ABI: `Bn*` components, `@bind` mechanics, cascading values,
bidirectional events, the six shell-bridge operations, multi-component
composition, strict mode, and `INavigationManager` root-component navigation.
wasmtime + `.wasm` deleted entirely (3.0e). Audit verdict: **PASS — all 11 DoD
criteria PASS** (wording-level honesty notes on #3/#5/#6/#7 recorded per
criterion). See [final audit](../plans/2026-07-10-milestone-3-final-audit.md).

`@bind` two-way binding, `Bn*` component library, cascading values, end-to-end DI, navigation service, `BlazorNativeComponentBase` ergonomics.

**Runtime architecture decision committed 2026-05-28: NativeAOT-per-ABI** — drops wasmtime + .wasm; see [Phase 3.0 design](../plans/2026-05-28-phase-3.0-design.md). MILESTONE.md DoD #1 closed. Reaffirmed 2026-07-09: runtime-pack bypass makes NativeAOT viable for Android on .NET 10; the 2026-06-01 dual-runtime pivot is superseded.

Phases:
- ✅ **Phase 3.0a** — Renderer trim safety — *complete (2026-05-31)*
   - Annotation pass on `BlazorNative.Renderer` + `BlazorNative.WasiHost`. `<TrimmerSingleWarn>false</TrimmerSingleWarn>` surfaced 6 individual warnings (vs M2's single IL2104 aggregate). Fixes: `[DynamicallyAccessedMembers(All)]` on all 4 `Mount<T>` / `MountAsync<T>` overloads + `AddComponentAsync(Type)`; `BlazorInterop.VerifyAccessors` refactored to `Type.GetType` via const-hoisted name with scoped `[UnconditionalSuppressMessage("IL2057")]`. Eliminates IL2087 (Mount) + IL2026 (VerifyAccessors). 4 library-deep IL2072s **accepted** (on `Microsoft.AspNetCore.Components` parameter-binding hot path — root-descriptor is wrong tool for flow-annotation gaps; these are explicit Phase 3.0b runtime smoke-test targets). Regression test `TrimSafetyTests` mounts a `HelloComponent`-faithful `TrimSafetyProbe` via `NativeRenderer.Frames` event channel, strongly-typed assertion catches the componentize-spike null-ElementName bug shape. Test count: **.NET 24/2** (was 23/2), JVM 11, Android 19. 3 gates, 7 commits, ~2h wall-clock. See [conclusion doc](../plans/2026-05-28-phase-3.0a-spike-conclusion.md). **Phase 3.0b unblocked.**
- ⚠ **Phase 3.0b** — NativeAOT runtime works — *partial: Gates 1–3 GREEN, Gate 4 RED (2026-06-01)*
   - **Gates 1+2 GREEN — NativeAOT desktop (win-x64) foundation shipped.** `BlazorNative.NativeHost.csproj` with `PublishAot=true` produces a 2.02 MB .dll on win-x64 with 0 trim warnings (Phase 3.0a annotations carry over cleanly). Three boot exports (`blazornative_init/shutdown/version`) verified via dumpbin. JNA bindings (`NativeBindings.kt`) + `BootSmokeNativeTest` pass on JVM desktop; struct size sanity (24 bytes) verified. Three trim-validation probes (`ParameterProbe`, `CascadingProbe`, `InjectProbe`) pass on host CLR. `BlazorInterop.EnsureInitialized()` runs clean inside the NativeAOT .dll — R1 + R5 settled.
   - **Gate 3 GREEN — WSL `blazornative-ubuntu` distro installed** with .NET 10.0.300 SDK ready. setup.ps1 Section 3 rewritten. (Practical value now reduced under the dual-runtime pivot — but the distro doesn't hurt anything, and Phase 3.0c may still find uses for it.)
   - **Gate 4 RED — `linux-bionic-*` NativeAOT cross-compile not supported in .NET 10.** `Microsoft.DotNet.ILCompiler` 10.0.8's `runtime.json` lists 13 supported RIDs, none `linux-bionic-*`. `runtime.linux-bionic-x64.Microsoft.DotNet.ILCompiler` has exactly one nuget.org version (`8.0.0-preview.6.23329.7`, June 2023, abandoned). Failure is intrinsic to .NET 10 NativeAOT, reproduced in a 3-line minimal scaffold. R2 from the design materialized in its hardest form — not "limited experience" but "experience doesn't exist." Microsoft reserves `linux-bionic-*` for the Mono-AOT + `dotnet/android` workload path. See [Phase 3.0b conclusion doc](../plans/2026-05-31-phase-3.0b-spike-conclusion.md).
   - **Pivot decision committed 2026-06-01: dual runtime (option C).** Phase 3.0c will produce Android .so via Mono-AOT (`dotnet/android` workload / `net10.0-android` TFM) while keeping the NativeAOT desktop foundation from Gates 1+2 for the JVM dev loop. Both runtimes share `Exports.cs` + the JNA `NativeBindings.kt` surface.
   - **Test counts at merge:** .NET 28/2, JVM 13, Android 19. Phase 3.0b's Gate 5 (Android instrumented) deferred to 3.0c's Mono-AOT path.
- ✅ **Phase 3.0c** — Android NativeAOT via runtime-pack bypass — *complete (2026-07-09)*
   - **All 4 gates GREEN, Windows-direct — WSL never needed.** The runtime-pack bypass (`PublishAotUsingRuntimePack=true` + vendored `build/BionicNativeAot.targets` + NDK linker hookup) produces working Android NativeAOT `.so`s from a Windows host on .NET 10, resolving Phase 3.0b's Gate 4 RED.
   - **Pinned combo, enforced in-repo:** SDK 10.0.301 via `global.json` (10.0.3xx band), ILCompiler + runtime packs 10.0.9 via `RuntimeFrameworkVersion`, NDK 26.3.11579264 revision-checked by setup.ps1 §8d. One LOCAL DEVIATION in the vendored targets (`PrependShimDirToNodePath` inline task, solves `NoDefaultCurrentDirectoryInExePath=1` durably).
   - **Gate 2:** NativeHost publishes `linux-bionic-x64` (2,306,456 B) + `linux-bionic-arm64` (2,351,256 B) with 3 boot exports; win-x64 regression clean.
   - **Gate 3:** pure-JNA boot smoke on the AVD via **unchanged** `NativeBindings.kt` — C-ABI parity proven; dotted lib filename (`libBlazorNative.NativeHost.so`) loads fine.
   - **Gate 4:** `blazornative_run_trim_probes` runs the 3 trim probes (parameter binding, cascading values, `[Inject]` DI — covering the 4 Phase-3.0a-accepted IL2072 paths) **inside the trimmed binary**: status 0 on desktop win-x64 AND on the AVD. The 4 IL2072s reappeared under ILC once the probes rooted renderer code (Gate 2's zero was an artifact of nothing being rooted) — same 4 shapes as 3.0a, zero new, zero of-our-code, proven benign at runtime.
   - **Final test counts:** .NET 28 passed / 2 skipped; JVM `testDebugUnitTest` 14; Android `connectedAndroidTest` 22/22. Phase 2.4b wasmtime flake recurred twice during Gate 4 sweeps (pre-existing carrier tests only, never the NativeAOT tests), clean on re-run.
   - **Consequence: the 2026-06-01 dual-runtime pivot is SUPERSEDED** — single NativeAOT runtime on every platform; Mono never enters the codebase. 3.0d unblocked; WSL distro retirement is a 3.0e decision.
   - See [redesign doc](../plans/2026-07-08-phase-3.0c-redesign.md) + [implementation plan](../plans/2026-07-08-phase-3.0c-implementation-plan.md) + [spike conclusion](../plans/2026-07-08-phase-3.0c-spike-conclusion.md) (carryover list for 3.0d lives in the conclusion doc's "Carryover into Phase 3.0d" section).
- ✅ **Phase 3.0d** — Native wire protocol + renderer cutover — *complete (2026-07-09)*
   - **All 4 gates GREEN.** `HelloComponent` renders on the AVD through the NativeAOT `.so` via typed structs end-to-end: init → register frame callback → mount → `FrameEncoder` → cdecl callback → Kotlin offset-read adapter → unchanged `WidgetMapper`. The `[FRAME]` JSON-over-stdout transport survives only as the WasiHost fallback until 3.0e.
   - **Gate 1 — protocol types + `FrameArena` + `FrameEncoder` (.NET, TDD):** `PatchProtocolNative.cs` typed 9-kind ABI (`BlazorNativePatch` 48 B / `BlazorNativeFrame` 24 B, layout drift-tested on both sides), thread-cached NativeMemory `FrameArena`, `FrameEncoder` with 9-kind round-trip — 16 TDD tests. 6 kinds wired live; AttachEvent/DetachEvent/AppendChild are ABI-reserved for 3.2/M3 (dormant).
   - **Gate 2 — desktop end-to-end + golden fixture:** `NativeRenderer.FrameSink` pluggable transport (NativeHost installs the struct marshaller; WasiHost leaves it null → stdout fallback); `blazornative_register_frame_callback` + `blazornative_mount` + dormant `blazornative_dispatch_event` exports; Kotlin `NativeFrameAdapter` offset reads. Hello golden fixture proves **struct == JSON: 13 patches, zero diff across both transports** on the JVM (win-x64 .dll).
   - **Gate 3 — Android cutover:** `MainActivity` boots via new `BlazorNativeRuntime` lifecycle wrapper (wasmtime path stays for the pre-existing tests until 3.0e). `WidgetMapperTest` sees widgets in **~1.6 s vs ~36 s historical wasm cold-boot (≈22× faster demo boot)**; full instrumented suite 22/22; **zero cutover iterations, zero wasmtime flakes** during the Gate 3 runs.
   - **Gate 4 — full regression sweep:** .NET **55 passed / 2 skipped**, JVM `testDebugUnitTest` **21**, Android `connectedAndroidTest` **22/22** (zero flake re-runs), wasmtime CLI coexistence explicitly re-verified (`BlazorNative.Wasi.Tests` 4/4 — stdout fallback intact).
   - **Review-loop highlights:** Gate 1 — offset-pinning + FrameArena growth-path coverage added; Gate 2 — 8 follow-ups incl. shutdown callback-clear + JNA callback exception posture; Gate 3 — Activity-recreation contract documented in `BlazorNativeRuntime`, mount-error message honesty (Android discards native stderr), unknown-component failure-mapping test.
   - **Carryover** (3.0e deletion-list additions + 3.2 items incl. the async-frames trampoline re-register hazard): see the [conclusion doc](../plans/2026-07-09-phase-3.0d-conclusion.md).
- ✅ **Phase 3.0e** — WASM-era collapse — *complete (2026-07-09)*
   - **Three staged commit-groups, all gates GREEN.** Stage 1 delete (`17db745`…`28e71d4` + follow-ups `e572ab7`): WasiHost, Wasi.Tests, spikes, sln `WASI|*` platform surgery, `[FRAME]` stdout fallback + .NET JSON wire layer (`RendererJsonContext` + all serialization attributes), the 7-file wasmtime Kotlin layer + 10 wasmtime-era tests + kotlinx-serialization, analyzer detach from the runtime project, setup.ps1 collapsed to sequential §1–7 (old §8d = new §5), Makefile `wasi*` targets. Stage 2 rename (`4a251be`, `36d7f44`, gate `e9de4bc`): `BlazorNative.NativeHost` → **`BlazorNative.Runtime`** end-to-end — folder/csproj/sln/namespaces, JNA `Native.load("BlazorNative.Runtime")` (`BlazorNative.Runtime.dll` / `libBlazorNative.Runtime.so`), version string `BlazorNative.Runtime 0.5.0-phase-3.0e`, Gradle `copyRuntimeSo` + paths, fresh triple publish re-verified on JVM + AVD. Stage 3 docs: README rewritten to the NativeAOT architecture, conclusion doc, survivor sweep.
   - **Deletion scale:** 71 files, −2,823/+265 lines (through Stage 2), plus off-repo mass: `vendor/` deleted from disk (~GBs of cargo-built wasmtime) and `wsl --unregister blazornative-ubuntu`.
   - **Golden tests went typed on both sides** (the same 13 Hello patches as in-code expected lists; fixtures + byte-identity test deleted) — this is what made full kotlinx-serialization removal legal. Transcribe-before-delete verified in review.
   - **Final counts:** .NET **49 passed / 2 skipped**, JVM **10**, Android **21/21**.
   - **Survivor policy:** `grep -i "wasm|wasi|wasmtime|[FRAME]"` over src/tests/build files returns only deliberate survivors — Core bridge types (3.1 owns their fate), analyzer rule names/messages (rescope = P3/M4), `BridgeEventTests`/`WasiBridgeTestCollection`, and provenance comments that reference the era as retired. Verbatim list + rename record + carryovers (3.1 bridge fate + six exports; 3.2 NativeUiEvent serialization + dispatch_event wiring + async-frames hazard; M3-close trim-probes deletion; P3/M4 analyzer rescope): see the [conclusion doc](../plans/2026-07-09-phase-3.0e-conclusion.md).
- ✅ **Phase 3.1** — shell bridge C-ABI (six `shell_*` operations) — *complete (2026-07-09)*
   - **All 4 gates GREEN — M3 DoD #3 CLOSED.** The six operations (navigate, current-route, storage read/write/delete, fetch) ship as **host-registered C-ABI callbacks**, not .NET exports: the host registers a 6-pointer struct once at boot via `blazornative_register_bridge`; `NativeShellBridge : IMobileBridge` (Runtime) invokes them — sync ops via caller-allocated 4 KB buffers with the `-needed` one-retry protocol, fetch via the **async completion pattern** (`FetchBegin` returns immediately; the host answers later through the `blazornative_fetch_complete` export into a TCS table). That completion machinery is the template for 3.2's event completion.
   - **Milestone moment: plain injected `HttpClient` works on Android** for the first time — `BridgeHttpHandler` → `NativeShellBridge` → `AndroidShellBridge` (`SharedPreferences` storage, single daemon-executor `HttpURLConnection` fetch). Version string `BlazorNative.Runtime 0.6.0-phase-3.1`; new exports: `register_bridge` + `fetch_complete` + temporary `run_bridge_probes` (deletes at M3 close with the trim probes).
   - **Two Android findings:** (1) `INTERNET` permission is required for ANY socket, even a loopback fetch in an instrumented test; (2) the design doc's "localhost is cleartext-exempt" claim was WRONG — targetSdk >= 28 blocks cleartext with no localhost exemption; fixed via a debug res overlay for `network_security_config.xml` (`<debug-overrides>` can't express `cleartextTrafficPermitted`), release keeps the secure default.
   - **Final counts:** .NET **98 passed / 2 skipped**; JVM `testDebugUnitTest` **18**; Android `connectedAndroidTest` **23/23**. Zero contract mismatches in the Kotlin-mirror adversarial review.
   - Carryovers (3.2: WasiBridge deletion + `BridgeEventTests` retarget, `NativeEvents` redesign, dispatch_event wiring, main-thread/`commit()` StrictMode risk; M3 close: delete both probe exports; pre-production: cleartext-overlay scope, text-only bodies, header collapse): see the [conclusion doc](../plans/2026-07-09-phase-3.1-conclusion.md).
- ✅ **Phase 3.2** — bidirectional events — *complete (2026-07-09)*
   - **All 4 gates GREEN — M3 DoD #2 CLOSED.** The tap round-trip is live on the AVD: tap → `WidgetMapper` click listener → single Kotlin dispatch lane (`BlazorNative-Dispatch`) → `blazornative_dispatch_event` → `@onclick` handler in the trimmed `.so` → synchronous re-render → widget text updates (`taps: 1`, tap again → `taps: 2` — listener re-attach after re-render proven). `HelloComponent` is interactive; change events (EditText `TextWatcher` → `ChangeEventArgs`, `applyingBatch` re-entrancy guard) are plumbed end-to-end as `@bind` groundwork; focus/blur stay unwired. Export contract 0/1/2/3 (0 includes stale-handler at-most-once); WasiBridge deleted — `NativeShellBridge` is the sole runtime `IMobileBridge`. Version `BlazorNative.Runtime 0.7.0-phase-3.2`.
   - **Discovery 1: Blazor 10 swallows handler throws** — `Renderer.DispatchEventAsync` routes handler exceptions to `HandleException` and completes its task successfully. rc 2 is honored via a depth-counted exception-capture window in `NativeRenderer` around each dispatch (handler, re-render, and frame delivery all land in it) — the partial DoD #9: handler exceptions are now visible; full renderer strict mode stays open.
   - **Discovery 2: the device exposed a latent re-render addressing bug** — diff edits were resolved by relative `SiblingIndex` ignoring Blazor's StepIn/StepOut cursor; Hello's counter `ReplaceText` hit the outer div and silently no-opped on screen. Survived Gates 1–2 because every prior re-render assertion was text-only. Fixed with a proper diff cursor over ordered-child bookkeeping (`NativeWidgetTree._childOrderMap`) + poison-sentinel guard; nodeId now pinned at three layers (both goldens + the on-device test) plus a dedicated `StepIn(1)` `DiffCursorTests`.
   - **Final counts:** .NET **108 passed / 2 skipped**; JVM `testDebugUnitTest` **24**; Android `connectedAndroidTest` **25/25**.
   - Carryovers (3.3+: five in-code diff-cursor limits at `_childOrderMap` incl. no `DetachEventPatch` emission, async-handler window revisit, dispatch-lane starvation watch, focus/blur, WidgetMapper `AppendChild`; M3 remaining DoD #4–#10; M3 close: both probe exports delete): see the [conclusion doc](../plans/2026-07-09-phase-3.2-conclusion.md).
- ✅ **Phase 3.3** — composition-grade renderer — *complete (2026-07-10)*
   - **All 4 gates GREEN — M3 DoD #8/#9/#10 CLOSED.** The composite is live on the AVD: `CompositionProbe` (root div → header → interleaved `ItemComponent` badge → label → keyed `@foreach` list of `ItemComponent`s → Add/Insert/Remove buttons) renders with the badge **visibly at child index 1**, insert-at-front lands the new item **first** in the list container (`[item-3, item-1, item-2]`), remove-first promotes item-2, and the badge's own `@onclick` increments its own counter. Gate 3 shipped with zero iterations; zero contract mismatches across both Kotlin-mirror gates. Version `BlazorNative.Runtime 0.8.0-phase-3.3`.
   - **Slot lists** replace the append-only `_childOrderMap` (all five 3.2 carryovers (a)–(e) closed): `Slot` = node xor component marker, buckets keyed `(componentId, parentNodeId)`, every diff edit resolves through the cursor, the batch-relative sibling map deleted. **Nested parenting** (DoD #8): component frames occupy slots; the component-parent map roots a child component's diff at the parent's view, not the host root; `DisposedComponentIDs` really tears down — the old disposal loop read a never-populated map and had always silently no-oped. **`CreateNode.InsertIndex`** (DoD #10): `AppendChildPatch` deleted, wire kind 2 reserved-dormant; `TranslateToHostInsertIndex` adds the component-slot-chain base offset within shared host containers (the phase's one design deviation). **Detach**: `on*` RemoveAttribute now emits `DetachEventPatch` (+`EventName` on the ABI's free Text field) from a `(nodeId, eventName) → handlerId` registry. **Strict mode** (DoD #9): `NativeRenderer.StrictErrors` rethrows out-of-window `HandleException` routes (production default false — documented POC posture); all test harnesses run strict incl. the instrumented suite via `BLAZORNATIVE_STRICT=1`; **zero latent errors flushed** from the .NET surface.
   - **Final counts:** .NET **150 passed / 2 skipped**; JVM `testDebugUnitTest` **29**; Android `connectedAndroidTest` **29/29** (strict session).
   - Carryovers (3.4 MUST-FIX: **Region frames are not walked** — `RenderFragment`/`CascadingValue` ChildContent subtrees silently drop; plus strict-ordering one-shot hardening, bucket-scan/translation perf ledger, stale-watcher caveat; M3 close: probe-scaffolding deletion ledger): see the [conclusion doc](../plans/2026-07-10-phase-3.3-conclusion.md).
- ✅ **Phase 3.4** — `Bn*` component library + `@bind` + cascading values — *complete (2026-07-10)*
   - **All 5 gates GREEN — M3 DoD #4/#5/#6 CLOSED.** The `Bn*` library (new public `BlazorNative.Components`: sealed `BnView`/`BnText`/`BnButton`/`BnInput`, hand-written BuildRenderTree) is live on the AVD as the launcher default: `BnDemo`'s bound `BnInput` echoes `héllo→世界` live into the cascaded-theme panel **without clobbering the input** (the complementary write-back proof: JVM pins the value UpdateProp *arrives*, Android pins the EditText is *not* overwritten), Clear resets both halves, and the Theme button flips the `CascadingValue<BnTheme>` background on both themed views — both ways. `@bind` ships as **mechanics, not syntax**: the `Value`/`ValueChanged` pair `@bind-Value` compiles to (Razor syntax = M6). Version `BlazorNative.Runtime 0.9.0-phase-3.4`. **Zero iterations and zero contract mismatches across Gates 3–4 — no ABI change at all this phase.**
   - **Corrected diagnosis (honest reversal):** 3.3's "Region subtrees silently drop" claim was **empirically wrong** — Blazor 10 decomposes region inserts per-child and the old no-default fall-through was accidentally transparent; the plan's RegionWalkTests were GREEN pre-implementation, and sabotage testing (skip-without-descend → 4/6 RED) proved they bite. Gate 1's explicit Region arm converts the accident into a pinned contract.
   - **Two renderer finds:** (1) the corrected Region diagnosis above; (2) component-root chaining (`BnThemedPanel` → `BnView`) recorded the component-parent entry against the emit parent instead of the slot container — wrong-bucket lookups sent the chained inner's view to the quiet append fallback, **caught by 3.3's `ContractWarning` working as designed** (the strict infrastructure's first real bug); fixed in `e51d5de`, `ComponentChainTests` pins the mid-list `InsertIndex 1`.
   - **Final counts:** .NET **170 passed / 2 skipped**; JVM `testDebugUnitTest` **33**; Android `connectedAndroidTest` **32/32** (strict session).
   - Carryovers (PRE-3.5 MUST: custom `AndroidJUnitRunner` for strict mode — the `Os.setenv` pattern is at its ceiling incl. a filtered-run gap; M6 ledger: stringly FontSize/Padding, stale-echo sequence-stamping, paired pin-harness extraction; M3 close: probe-exports deletion + `CompositionProbe` fate, `BnDemo` stays): see the [conclusion doc](../plans/2026-07-10-phase-3.4-conclusion.md).
- ✅ **Phase 3.5** — navigation service (DoD #7) + M3 close — *complete (2026-07-10, the LAST M3 phase)*
   - **All 6 gates GREEN — M3 DoD #7 CLOSED ON-DEVICE; milestone complete.** The shipped demo is a two-page app: tap "Settings →" on the AVD → the whole screen swaps to `BnSettingsPage` (BnDemo's input GONE from the tree); "← Back" → `BnDemo` remounts **fresh**. `INavigationManager` (Core) + `NativeNavigationManager` (Runtime; route table `/` → BnDemo, `/settings` → BnSettingsPage): host-notify via the 3.1 `Navigate` callback → `Unmount` (`RemoveRootComponent`, verified FIRST — the 3.3 disposal machinery clears the screen) → fresh `TryMount` → afterSwap (`CurrentRoute`/`RouteChanged` track the screen, not the intent). Version `BlazorNative.Runtime 1.0.0-phase-3.5`.
   - **Gate 0 (the PRE-3.5 MUST):** `BlazorNativeTestRunner` sets `BLAZORNATIVE_STRICT=1` before any test class loads — both `@BeforeClass` setenv copies + ordering KDocs deleted; the filtered-run strict gap closed by construction.
   - **Mid-dispatch finding:** Blazor 10 holds the event batch open across handlers, so in-handler swaps defer via `NativeRenderer.RunAfterDispatch` and drain at the outermost dispatch unwind — still before `blazornative_dispatch_event` returns (dispatch-window pin holds; drain faults → rc 2, every fault stderr-logged).
   - **M3 close code (Gate 4):** both probe exports retired (`TrimProbes.cs`/`BridgeProbes.cs` + bindings + their tests, per-test judgment) — the final **eight-export C-ABI** verified via dumpbin + `llvm-readelf` on all three RIDs; guarded-catch → −1 → `HostError` re-pinned on both wire legs after review caught a false "covered elsewhere" claim. `TrimValidationProbes` + `CompositionProbe` stay (scaffolding ledger in the audit).
   - **Final counts:** .NET **177 passed / 2 skipped**; JVM **32**; Android **32/32** (runner-strict).
   - Honest boundaries (non-strict drain faults log-only; failed-swap blank screen with stale `CurrentRoute`; host-route-updates-first divergence; throwing `RouteChanged` subscriber) + carryovers (M5 host-initiated nav; M6 ledger incl. the `BlazorNative.Navigation` package lift; M4+ `RouteChanged` subscriber isolation): see the [conclusion doc](../plans/2026-07-10-phase-3.5-conclusion.md) + [final audit](../plans/2026-07-10-milestone-3-final-audit.md).

**M2 architectural carryover (resolved by Phase 3.0 decision):**
- **Bidirectional event flow.** No long-running-Main concern — the NativeAOT library is always loaded. ✅ shipped in Phase 3.2 (2026-07-09): `blazornative_dispatch_event` live, tap round-trip on the AVD, M3 DoD #2 closed.
- **6 deferred mobile_bridge exports** — ✅ shipped in Phase 3.1 (2026-07-09) as host-registered C-ABI callbacks + async fetch completion.
- **Multi-component support / `AppendChild` patch emission.** Pure renderer work, runtime-neutral. ✅ shipped in Phase 3.3 (2026-07-10): slot-list model + nested-component parenting close DoD #8; AppendChild deleted in favor of `CreateNode.InsertIndex` (DoD #10).

Maps to BACKLOG.md "P2 — Real apps can be built".

---

### ✅ Milestone 4 — P3: Production-Shippable  *(complete 2026-07-12, tagged `v4.0`)*

6 phases (4.0–4.5) shipped. The repo is public with branch protection and an
assert-don't-observe CI pipeline on every PR; the BN analyzer suite is rescoped
for NativeAOT, tested, and rides every src build under a zero-warning bar; the
runtime-hardening ledger is triaged (4 fixed with tests, 5 re-ledgered with
rationale + revisit triggers); the measured fast-restart dev loop and the
live-native-session DevTools inspector exist; and the five NuGet packages are
proven by a blank-consumer smoke that runs on every PR. **Windows + Android only —
the iOS Swift shell deferred to M5** (milestone-open decision). Audit verdict:
**PASS — all 8 DoD criteria PASS** (honesty notes on #1/#2/#4/#5/#6/#7 recorded
per criterion). See [final audit](../plans/2026-07-12-milestone-4-final-audit.md).
Full 8-point DoD: [MILESTONE.md](MILESTONE.md).

Maps to BACKLOG.md "P3 — Production readiness". Triage input: the [M3 final audit](../plans/2026-07-10-milestone-3-final-audit.md) carryover table (host-initiated nav is M5; the packaging ledger is M6; the diagnostics/host-error surface + still-open runtime items land in Phase 4.2's triage).

Phases (approved at milestone-open 2026-07-11):
- ✅ **Phase 4.0** — GitHub publish + CI pipeline (DoD #1, #2) — *complete (2026-07-11)*
   - Repo public at **github.com/MarcelRoozekrans/BlazorNative** (revised from the initial ZeroAlloc-Net choice at brainstorm — personal POC home, transfer is cheap later). PR CI (`ci`/`build-test`, windows-latest): build + analyzers, `dotnet test` **asserting 177/2/0**, three NativeAOT publishes asserting 4 IL2072s + eight-export set-equality, JVM `testDebugUnitTest` asserting 32/0 — green first attempt, 13m cold / 6m warm. Nightly + manual `android-instrumented`: windows `.so` artifact → ubuntu KVM emulator (API 34 x86_64 pixel_6) → **32/32 first attempt**; informational until a green-nightly baseline. Gradle `ciSoDir` property (x86_64-only CI staging; local path behavior-identical). Issue structure live: 37 labels, 7 milestones (M1–M3 closed w/ audit links), 25 open-work issues. Branch protection on `main` (require PR + `build-test`, admins included) — applied post-flip (private repos need Pro; documented deviation). **PR-merge workflow from 4.1 on.** Three-entry diagnosis ledger (workflow-dispatch registration quirk; git-bash MSYS path mangling on `gh api` — caught by Gate 0's failure surfacing exactly as designed; protection-needs-Pro). See [design](../plans/2026-07-11-phase-4.0-design.md) + [plan](../plans/2026-07-11-phase-4.0-implementation-plan.md) + [conclusion](../plans/2026-07-11-phase-4.0-conclusion.md).
- ✅ **Phase 4.1** — Analyzer rescope + unit tests (DoD #3) — *complete (2026-07-11)*
   - The BN rule set tells the truth about the NativeAOT runtime. **6 WASI-era rules retired** (BN0001/0002/0003/0005/0006/0012 — BN0006 was actively false: `FrameArena` uses `[ThreadStatic]` deliberately; IDs never reused), **5 survivors rescoped** (BN0004/0010 Error→Warning; BN0011 narrowed to the parameterless `HttpClient` ctor; BN0013 stays Error; BN0014 reworded off Mono-WASI → category `BlazorNative.Interop`), **2 new interop rules** for the `[UnmanagedCallersOnly]` C-ABI boundary: **BN0020** (no exception may escape — top-level try/catch-all shape, no throw in any catch) + **BN0021** (explicit `EntryPoint` + `CallConvCdecl`), both Error. Consolidation: `MobilePolicyAnalyzer` + `BridgeAsyncHandlerAnalyzer` + new `InteropBoundaryAnalyzer`; per-rule docs at `docs/analyzers.md`. TDD-first throughout: **23 analyzer tests** (18 + 2 review-driven false-negative fixes: target-typed `new()` evasion, BN0020 specific-catch rethrow), the 3 Phase-2.0 BN0014 markup tests green unchanged. Release tracking real (`AnalyzerReleases.Shipped.md` releases `2.0.0` backfill + `4.1.0` — RS2007 rejects prerelease suffixes, documented deviation; RS2008 NoWarn deleted; `-warnaserror` clean). Analyzer attached to **all six src projects**; `Exports.cs` conforms to its own rules (CallConvs cdecl ×8, Shutdown/Version wraps — behavior-neutral, dumpbin 8/8 exports unchanged, 4 IL2072s, JVM 32/0); zero-warning bar met with **one justified pragma** (BN0011, `DevHostBridge` — it IS the bridge). Solution **197/2/0** asserted in ci.yml. Commits `ff47531`, `fc3ad34`, `f211269`. See [design](../plans/2026-07-11-phase-4.1-design.md) + [plan](../plans/2026-07-11-phase-4.1-implementation-plan.md) + [conclusion](../plans/2026-07-11-phase-4.1-conclusion.md).
- ✅ **Phase 4.2** — Runtime hardening: ledger triage + fixes (DoD #4) — *complete (2026-07-11)*
   - The runtime-hardening ledger is deliberate: **4 fixed with tests, 1 stale skip cleaned up, 5 re-ledgered with written rationale + revisit triggers** in the [triage doc](../plans/2026-07-11-phase-4.2-hardening-triage.md) — the ledger of record, pointed at by in-code breadcrumbs at all five sites. **Fixed:** focus/blur wired end-to-end (BnInput optional `OnFocus`/`OnBlur` attach-only-when-set — BnDemo's 4-attach golden unchanged; `FocusProbe` scaffolding; WidgetMapper per-view `FocusEntry` pair behind Android's single focus-listener slot; proven on real EditText focus transitions on the AVD); RouteChanged subscriber isolation (`GetInvocationList` + per-subscriber try/catch, **contained under strict mode too** — strict surfaces renderer contract violations, not app-listener bugs); the M1 allocation-budget test enabled via the test-only `TriggerRootRenderForTests` seam — **measured 295,200 B / 900 steady-state re-renders = 328 B/frame deterministic, bound 600 KB (~2x)**; stale-watcher leak fixed (`watchers` re-keyed handlerId → nodeId, re-attach removes the prior TextWatcher — exactly-one-dispatch pinned on-device). **Re-ledgered:** async-handler capture window + dispatch-lane starvation (one design — first real async `@onclick` consumer owns both), RemoveComponent bucket scan + TranslateToViewIndex memoization (await a keyed Bn* list benchmark), NativeEvents redesign (→ M5 host-initiated lifecycle ingress; deleting would orphan BN0014 + the DevHost path). Version `1.1.0-phase-4.2`. Counts (all asserted in CI): .NET **203/0** (was 197/2), JVM **34** (was 32), Android **35** (was 32); 4 IL2072s ×3 RIDs unchanged. TDD throughout; gates `1d0158f` (.NET) / `02939bd` (JVM) / `3b00f56` (Android) + docs close-out. See [design](../plans/2026-07-11-phase-4.2-design.md) + [plan](../plans/2026-07-11-phase-4.2-implementation-plan.md) + [triage doc](../plans/2026-07-11-phase-4.2-hardening-triage.md) + [conclusion](../plans/2026-07-11-phase-4.2-conclusion.md).
- ✅ **Phase 4.3** — Dev inner loop / fast-restart (DoD #5) — *complete (2026-07-11)*
   - The one-command native dev loop, **measured**: `make devloop` watches `src/BlazorNative.{Core,Renderer,Http,Components,Runtime}/**/*.cs` (500 ms debounce, obj/bin excluded) and turns every save into win-x64 publish → **`PreviewHost`** — the repo's first interactive JVM surface: a `main()` that boots the dll via `BlazorNativeRuntime`, mounts a component (`-Pcomponent=`, default BnDemo), and prints the widget tree via **`TreeSnapshot`** (pure-JVM WidgetMapper-parity node model, 13 TDD tests: bucket-local InsertIndex, text-child collapse, last-wins re-attach, subtree remove, out-of-range-throws pin) + per-stage timings, exit-code honest (a dropped frame → PARTIAL + exit 1). `make devloop-android`: bionic-x64 publish → `installDebug` → `am start` with `EXTRA_COMPONENT` → logcat `[BOOT] mounted` marker, round-trip stamped. **Warm numbers (each reproduced ≥2×):** incremental publish 8.2–9.4 s · boot-to-tree ~0.3 s · JVM cycle 9.9–11.2 s · ADB cycle 14.1–14.5 s — the NativeAOT publish dominates; the gradle-overhead contingency was not needed (warm exec leg ~2 s, daemon kept — `JavaExec` forks, the daemon never loads the dll). **Fast-restart, not hot-reload**, honestly documented in the README's three-lane Dev experience rewrite (NativeAOT can't hot-patch; Windows locks the loaded dll; the design doc's "no unload API" wording corrected in the conclusion — `NativeLibrary.dispose()` exists but the other two legs make restart correct regardless). No .NET changes; version stays `1.1.0-phase-4.2`; JVM **47** (was 34), .NET 203/0 + Android 35 untouched. Gates `8ac3fb8` (PreviewHost TDD) / `9966b4f` (devloop + measurements + README) + docs close-out. See [design](../plans/2026-07-11-phase-4.3-design.md) + [plan](../plans/2026-07-11-phase-4.3-implementation-plan.md) + [conclusion](../plans/2026-07-11-phase-4.3-conclusion.md).
- ✅ **Phase 4.4** — DevTools render-tree inspector (DoD #6) — *complete (2026-07-11)*
   - `make inspect` serves a localhost DevTools page over a **live native session**: **`InspectorHost`** (PreviewHost's long-lived sibling) boots the real NativeAOT dll and serves the live patch stream (bounded ring, 500), the collapsible `<details>` widget tree, the event log (bounded, 200 — frame deliveries, dispatches with rc, onError faults), and **dispatch-from-the-page** (interactive-lite: "fire click" buttons, payload input + "send change") — same dll, C-ABI frames, and dispatch lane the APK rides. Zero new dependencies: JDK `com.sun.net.httpserver` + hand-rolled JSON (the FlatJson escaping contract, shared not copied); the page is ONE self-contained inline HTML+CSS+JS string (7.2 KB, `textContent`-only DOM — XSS-clean, no external requests, asserted in the e2e). **Forced discovery:** `com.sun.net.httpserver` is absent from `android.jar` and every AGP compilation targets it, so the server/host live in a `src/jvmHost/kotlin` source dir compiled by a dedicated plain-JVM KotlinCompile (KGP's sanctioned `KotlinBaseApiPlugin` factory + one reflective convention-set whose failure mode on a KGP bump is loud-by-design). New public `BlazorNativeRuntime.dispatchEventAndWait` (lane-marshalled blocking dispatch, self-deadlock carve-out pinned); ONE coarse state lock, SSE pull model with slow-client drop; in-memory `InspectorHostBridge` (fetch = honest transport failure). E2e over a real session rides the 3.5 navigation (harvested handlerId → dispatch rc 0 → settings tree → SSE `tree-changed`); Gate 2 smoke drove the page's own buttons in headless Chrome via CDP. No .NET changes; version stays `1.1.0-phase-4.2`; JVM **73** (was 47), .NET 203/0 + Android 35 untouched. Gates `0c49cc2` (host + API TDD) / `db3a726` (page + docs) + docs close-out. See [design](../plans/2026-07-11-phase-4.4-design.md) + [plan](../plans/2026-07-11-phase-4.4-implementation-plan.md) + [conclusion](../plans/2026-07-11-phase-4.4-conclusion.md).
- ✅ **Phase 4.5** — NuGet packaging + consumer smoke + M4 final audit → `v4.0` (DoD #7, #8) — *complete (2026-07-12, the LAST M4 phase)*
   - **All 3 gates GREEN — M4 DoD #7 CLOSED; #8 closed by the final audit; milestone complete.** The five packages (`BlazorNative.Core`/`.Renderer`/`.Http`/`.Components`/`.Analyzers`) pack **clean** (zero warnings, NU5xxx included) at **`1.2.0-phase-4.5`** with per-csproj metadata (MIT, repo URL, descriptions; documented call — no `Directory.Build.props` exists and a scoped one would leak onto the non-packed Runtime/Blazor projects). Analyzers package verified by unzip inspection: dll in `analyzers/dotnet/cs`, **no `lib/`**, `DevelopmentDependency=true`, Roslyn refs pinned (`4.1*`→4.14.0, `3.1*`→3.11.0), empty dependency group suppressed (the NU5128 metadata fix); **props/targets verified NOT needed** (plain DiagnosticAnalyzers; NuGet auto-loads the folder — proven live in the consumer). Library nuspecs carry correct deps (ZeroAlloc + AspNetCore concretes; no PrivateAssets leaks). **MIT LICENSE added — the repo had been public without one since 4.0** (Gate 1 step-0 finding).
   - **The DoD #7 proof (Gate 2):** `samples/ConsumerSmoke` — a blank console project **outside the solution**, restoring the five packages from the local `artifacts/packages` feed only (own `nuget.config` with `<clear/>`; provenance **asserted** from NuGet's `.nupkg.metadata`: BlazorNative.* ← local feed, transitives ← nuget.org) — mounts BnView/BnText/BnButton via the renderer harness shape and asserts the patch set (1 frame, 10 patches, exactly one click AttachEvent). Analyzers-live pin both directions: a `#if ANALYZER_TRIP` parameterless `HttpClient` fires **BN0011** under `-p:AnalyzerTrip=true`, zero BN diagnostics clean. `scripts/consumer-smoke.ps1` green locally and as a **ci.yml step on every PR** (PR #46 green first attempt, 6m59s; smoke step ~21 s).
   - **Honesty notes:** `BlazorNative.Runtime` deliberately NOT packaged (the NativeAOT composition root is an app-shape concern — M6 template/story); **nuget.org deferred** (local/CI feed only; re-decide at M6 — `PackageReadmeFile` is the recorded prerequisite). Version churn exercised the full contract: `Exports.cs` + both Kotlin assertions → `1.2.0-phase-4.5`, forcing both bionic republishes + the instrumented rerun (now five version-bearing surfaces incl. the smoke script/csproj — recorded carryover).
   - **Final counts (all CI-asserted):** .NET **203/0** · JVM **73/0** · Android **35/35** · 4 IL2072s ×3 RIDs unchanged. Commits `d54c761` (Gate 1) / `c4e4ac1` (Gate 2) + docs close-out. See [design](../plans/2026-07-12-phase-4.5-design.md) + [plan](../plans/2026-07-12-phase-4.5-implementation-plan.md) + [conclusion](../plans/2026-07-12-phase-4.5-conclusion.md) + [M4 final audit](../plans/2026-07-12-milestone-4-final-audit.md).

---

### ✅ Milestone 5 — P4: Full Platform Coverage  *(complete 2026-07-13, tagged `v5.0`)*

6 phases (5.0–5.5) shipped. A **second native platform in fact**: the same `BnDemo`
two-page app runs interactively on the **iOS simulator** in CI (a Swift/UIKit shell
over a NativeAOT static `.a`) and on the **Android AVD** (the Kotlin/JNA shell over a
bionic `.so`), from one runtime and one nine-export C-ABI. The Android shell gained
real host-initiated lifecycle/back/deep-link ingress (closing the `NativeEvents` fork
from the [4.2 triage](../plans/2026-07-11-phase-4.2-hardening-triage.md)); clipboard +
share landed on both platforms through a documented, size-negotiated bridge-extension
pattern. **iOS simulator-only** — device/signing/App Store deferred (no Apple
Developer account; milestone-open decision). Audit verdict: **PASS — all 8 DoD
criteria PASS** (honesty notes on #1/#2/#3/#4/#5/#6/#7 recorded per criterion,
incl. the DoD #4 two-job→single-job wording reconcile + the iOS-XCTest 12→13 count
reconcile). See [final audit](../plans/2026-07-13-milestone-5-final-audit.md).
Full 8-point DoD: [MILESTONE.md](MILESTONE.md).

**Locked at milestone-open (2026-07-12):** iOS via **free public-repo GitHub macOS
runners** (no Mac hardware — user decision), **simulator-only** (no signing/Apple
Developer account; device + App Store validation deferred), **spike-first** (a
feasibility RED reshapes the milestone early); Android scope = the host-initiated
events cluster only (FCM + secure storage stay BACKLOG); APIs = clipboard + share
only (the pattern is the deliverable).

Maps to BACKLOG.md "P4 — Full platform coverage" (scoped subset; iOS device
specifics — APNs, Keychain, universal links, App Store validation — deferred there).
Issues: #16 (Android, narrowed), #17 (iOS), #18 (APIs, narrowed), #19 (host-initiated).

Phases (approved at milestone-open):
- ✅ **Phase 5.0** — iOS feasibility spike (DoD #1) — *complete (2026-07-12, PR #47 `16a637a`)*
   - **GREEN — .NET 10 NativeAOT works on iOS.** The runtime-pack bypass (3.0c's linux-bionic trick) ports to iOS via `PublishAotUsingRuntimePack=true` + `DisableUnsupportedError=true`, RID-gated in `BlazorNative.Runtime.csproj` (behavior-neutral for win-x64/bionic — verified). Rungs 1/2 (plain-RID publish, iOS workload) RED on the SDK's `NETSDK1203` AOT-RID gate; rung 3 (bypass) GREEN. Produces a linkable `.dylib` for **`iossimulator-arm64` AND `ios-arm64`** with all 8 `blazornative_*` exports; the link probe builds a simulator executable; **bonus: the runtime BOOTS on the simulator via the C-ABI** (`simctl spawn` printed the version). Pinned: SDK 10.0.301, ILC+packs 10.0.9, `runtime.iossimulator-arm64.microsoft.dotnet.ilcompiler` 10.0.9 (the RID ILC exists on nuget.org, unlike bionic), Xcode 26.5 on `macos-latest`. Fallback ladder NOT triggered. `.github/workflows/ios-spike.yml` (dispatch-only), `scripts/ios-spike-verify.sh`, throwaway `spikes/ios-aot-probe/`. **DoD #1 closed.** See [design](../plans/2026-07-12-phase-5.0-design.md) + [spike conclusion](../plans/2026-07-12-phase-5.0-spike-conclusion.md).
- ✅ **Phase 5.1** — Host-initiated events: lifecycle + predictive back + deep links (DoD #5) — *complete (2026-07-12)*
   - **GREEN — host-originated events reach .NET and land on the screen, all three surfaces.** A **9th C-ABI export** `blazornative_host_event(name, payload)` fires the *real* `NativeShellBridge.NativeEvents` multicast (the 3.2 no-op is gone; per-subscriber isolation; rc 0/2/3). Android lifecycle (`onPause`/`onResume`/`onDestroy`) marshals into it (onDestroy fires but never `shutdown`s — the recreation trap); predictive back (`OnBackInvokedCallback` → `dispatchHostEventAndWait("back")`) routes through the *reserved "back" name, mapped in .NET* (intercepted before the multicast; rc 1 = not-handled → shell finishes) to a new `NavigateBackAsync` (single previous-route slot, cleared after a back — no ping-pong); a launch-time deep link (`blazornative://<route>`, custom scheme) starts the app on the linked page (shell-side component resolution — the .NET first-mount honor can't cover recreation/shared-session). `HostEventProbe` is the on-device consumer (scaffolding). The ABI grew **eight→nine** in one gate (5 assertion sites + `NativeBindings.kt` + README/GITHUB-SETUP prose); win-x64 + both bionic RIDs verified at **9 exports / 4 IL2072**. Counts: **.NET 220, JVM 78, Android 38**; version `1.3.0-phase-5.1`. `BN0014` now guards a real `NativeEvents`. **DoD #5 closed on the AVD** (the 4.2 `NativeEvents` fork closed). See [design](../plans/2026-07-12-phase-5.1-design.md) + [conclusion](../plans/2026-07-12-phase-5.1-conclusion.md).
- ✅ **Phase 5.2** — Swift shell foundation: boot + tree render on the simulator (DoD #2, #4) — *complete (2026-07-12)*
   - **GREEN — the first non-Kotlin shell: BnDemo renders on the CI iOS simulator through a Swift/UIKit shell over the NativeAOT static `.a`.** The shell is the imperative twin of the Android `WidgetMapper` (`UIStackView`/`UILabel`/`UIButton`/`UITextField` ↔ `LinearLayout`/`TextView`/`Button`/`EditText`), reading read-only rt→shell frames at the exact 48/24-byte offsets (`BnFrameAdapter`, `loadUnaligned`, copy-strings-in-callback), buffering to `DispatchQueue.main` on `CommitFrame` — the `mainHandler.post` twin. `NativeLib=Static` (iOS-only RID group; behavior-neutral — win-x64 stays `.dll`, bionic `.so`) emits `libBlazorNative.Runtime.a`. **The load-bearing discovery: linking a NativeAOT *static* archive into an Xcode app** — `bootstrapperdll.o` as a DIRECT object (its `__attribute__((constructor))` inits the runtime; as an archive member it is never pulled → SIGSEGV in `blazornative_init`) + `-force_load` the app `.a` + a merged on-demand `libBnRuntimeSupport.a` (the runtime pack's `native/*.a` minus the non-default GC/eventpipe variants) + the 5.0 spike frameworks (Foundation/Security/CoreFoundation, `c++`/`z`/`icucore`/`objc`). A hosted **XCTest** boots→mounts→asserts the real `UIView` tree (6 arranged subviews in order, mid-list echo panel at index 2, `#FFEEAA`, button titles, title fontSize 24) + a **Swift wire-drift guard** (the third alongside Kotlin/.NET). New **`.github/workflows/ios.yml`** (`macos-latest`, single job, **informational** like the emulator lane): publish → `nm -gU` 9 exports → XcodeGen + `xcodebuild test` on a runner-selected sim → assert **2 passed / 0 failed**. Retired the 5.0 `ios-spike.yml` + `scripts/ios-spike-verify.sh` + `spikes/ios-aot-probe/` (superseded). Version unchanged **`1.3.0-phase-5.1`** (no Runtime source changed — only the RID-gated csproj + new `src/BlazorNative.Apple/`). No-regression: **.NET 220 / JVM 78 / Android 38** untouched. Xcode 26.5, SDK 10.0.301, ILC+packs 10.0.9. **DoD #2 + #4 closed.** See [design](../plans/2026-07-12-phase-5.2-design.md) + [conclusion](../plans/2026-07-12-phase-5.2-conclusion.md). Interactivity/bridge/nav = 5.3; device/signing = later.
- ✅ **Phase 5.3** — Swift shell interactivity: events + bridge + navigation parity (DoD #3) — *complete (2026-07-12)*
   - **GREEN — the interactive two-page BnDemo runs on the iOS simulator: iOS reaches the Android v3.0 bar.** Additive to the 5.2 render shell, **Swift + `ios.yml` only — zero shared-code change** (`dispatch_event`/`register_bridge` already exist and are used by Android). A serial `DispatchQueue("BlazorNative-Dispatch")` crosses taps to `blazornative_dispatch_event` (rc 0/1/2/3 → error sink); `BnWidgetMapper` wires `AttachEvent` to UIControl targets (`UIButton .touchUpInside` / `UITextField .editingChanged`, last-wins, identity cleanup) via a retained `BnControlTarget` seam; `BnFlatJson` writes the args (`{"name":"click"}` / `{"name":"change","payload":"<raw>"}`) byte-exact to the Kotlin `FlatJson`. `AppleShellBridge` supplies all 6 `@convention(c)` callbacks (singleton-routed, no-throw by construction): `navigate`+`currentRoute` real (route slot `NSLock`-guarded, seeded `/`, the -needed buffer protocol = `writeUtf8` twin), storage ×3 in-memory, fetch fails synchronously. The **`@bind` iOS simplification**: value write-back needs NO re-entrancy guard — UIKit doesn't fire `.editingChanged` on a programmatic set, so the loop can't form. Nav + theme ride 5.2's render path (SetStyle backgroundColor + RemoveNode/CreateNode). Interactive hosted XCTests **2 → 9** (BnInteractionTests ×5: bind/echo with the `héllo→世界` UTF-8 leg + input-not-clobbered, Clear, Theme flip both ways, Settings→ no-textfield, ←Back fresh remount; BnBridgeTests ×2: the -needed protocol + FlatJson), asserted in `ios.yml`. **Green on the first CI attempt** (3m20s) — correct-first on the survey's byte-exact contracts + the proven 5.2 static-`.a` foundation. Version unchanged `1.3.0-phase-5.1`; **.NET 220 / JVM 78 / Android 38** untouched (nothing shared changed). **DoD #3 closed.** See [design](../plans/2026-07-12-phase-5.3-design.md) + [conclusion](../plans/2026-07-12-phase-5.3-conclusion.md). Clipboard/share = 5.4; real-device/signing = later.
- ✅ **Phase 5.4** — Clipboard + share + the bridge-extension pattern (DoD #6) — *complete (2026-07-12)*
   - **GREEN — clipboard + share on BOTH platforms, delivered through a documented, size-negotiated bridge-extension pattern (DoD #6's real deliverable).** The shell bridge grew 6→9 callbacks (`ClipboardRead@48`, `ClipboardWrite@56`, `Share@64`; struct 48→72) in lockstep across .NET + JVM + the Swift header. The growth is **versioned, not a raw lockstep edit**: `blazornative_register_bridge` gained a leading `structSize`, and the runtime **min-copies + zero-fills** (`Math.Clamp(structSize,0,72)` + `Buffer.MemoryCopy` + `RequireSlot`), so an unsupplied slot surfaces as `NotSupportedException` — the bridge is now forward- AND backward-compatible (old 48-byte shell ⇄ new 72-byte runtime), pinned by the 48-byte old-shell + negative-structSize tests. `IMobileBridge` gained `ClipboardReadAsync` (buffer protocol) / `ClipboardWriteAsync` / `ShareAsync` (one-string); `DevHostBridge` mocks them; the **`ClipboardProbe`** scaffolding (`[Inject] IMobileBridge`, Copy/Paste/Share `BnButton`s + echo `BnText`) is what all three shells mount. **Android** (`AndroidShellBridge`): real `ClipboardManager` (appContext) + an `ACTION_SEND` share Intent (`FLAG_ACTIVITY_NEW_TASK` from appContext — the no-Activity-retained rule) captured via a `shareLaunchHook` seam. **iOS** (`AppleShellBridge`): real `UIPasteboard.general` + a `UIActivityViewController` (main-thread hop, key-window root VC) captured via a `shareHook` seam. **Share is asserted at the callback-content bar** (the system sheet is unassertable). The pattern is documented in **[docs/bridge-extension.md](../bridge-extension.md)** — the recipe camera/geo/etc. follow in M6+. Counts **.NET 220→230 / JVM 78→79 / Android 38→40 / iOS XCTest 9→13** (the 13th = the Swift `bn_bridge_callbacks` struct-drift pin, PR #51 `c9ac4f2`); version **`1.4.0-phase-5.4`**; **exports unchanged at 9** (a struct grow + one-symbol signature change, not an export grow), 4 IL2072 per publish. See [design](../plans/2026-07-12-phase-5.4-design.md) + [conclusion](../plans/2026-07-12-phase-5.4-conclusion.md) + [pattern](../bridge-extension.md). M5 final audit + close = 5.5.
- ✅ **Phase 5.5** — M5 final audit + close (DoD #7, #8) → `v5.0` — *complete (2026-07-13, the LAST M5 phase)*
   - **All gates GREEN — M5 DoD #7 CLOSED (four CI-asserted count gates) + #8 CLOSED (decision log + this audit); milestone complete.** The [M5 final audit](../plans/2026-07-13-milestone-5-final-audit.md) walked all 8 DoD criteria against evidence (each phase conclusion + the CI count gates + the on-sim/on-AVD proofs) and returned **PASS on all eight** — two native platforms in fact. Docs-only gate; no code/test/csproj/workflow changed. **Three doc-vs-CI reconciles recorded** (all doc lags behind a correct shipped gate): DoD #4 "two-job" → **single job** (`ios.yml` publishes AND tests on one macOS runner); iOS XCTest count "12" → **13** (the shipped `ios.yml` baseline — the `bn_bridge_callbacks` struct-drift pin `c9ac4f2` landed after the 5.4 conclusion was written); .NET "2 skipped" → **0 skipped** (zero skips since M4). Counts at close (CI-asserted): **.NET 230 / JVM 79 / Android 40 / iOS XCTest 13**; version `1.4.0-phase-5.4`; ABI 9 exports + the 72-byte 9-callback bridge. Scaffolding ledger (HostEventProbe/ClipboardProbe/FocusProbe/CompositionProbe/TrimValidationProbes + the share/dispatch test seams — all STAY, none on the shipped ABI) + carryover table → M6+ in the audit. Tag command recorded for the controller (post-merge on main): `git tag -a v5.0 -m "Milestone 5: P4 — Full Platform Coverage complete"`. See [design](../plans/2026-07-13-phase-5.5-design.md) + [conclusion](../plans/2026-07-13-phase-5.5-conclusion.md) + [M5 final audit](../plans/2026-07-13-milestone-5-final-audit.md).

---

> **Roadmap re-planned 2026-07-13** (after the M5 close + a React-Native capability
> comparison). The original P0–P7 list had no milestone for a real **layout engine** —
> the single biggest gap between "proven POC" and "usable framework" — and it sequenced
> *ecosystem/packaging* (old P5) before the *capability* that makes packaging worthwhile.
> The re-plan applies **capability before ecosystem**: build what makes real apps
> possible (layout → components → platform breadth), then package/publish, then harden.
> M1–M5 (shipped, v1.0–v5.0) are unaffected; a new capability milestone (M6) is inserted,
> `.razor` compilation is promoted to its own milestone concern (M7), and the old
> Ecosystem/Hardening/Enterprise milestones shift down.

### ✅ Milestone 6 — Real-UI Foundation: Layout + Scroll + Image  *(complete 2026-07-15, tagged `v6.0`)*

The capability that unblocks real screens. **Yoga (C++, Facebook's flexbox engine) linked
into both shells** — Android via JNI/JNA, iOS via its C-API (the same interop the shells
already use for the runtime); the renderer stays thin (flex props —
`flexDirection`/`justifyContent`/`alignItems`/`flexGrow`/`flexWrap`/absolute — ride the
existing `SetStyle` wire), the shell builds a Yoga node tree, **measures text/images
natively** (Yoga's measure callback — the reason layout can't live purely in .NET), computes,
and places. Plus **real scrolling** (the `scroll` NodeType is stubbed today → `ScrollView`/
`UIScrollView`) and **URL images** (the `image` NodeType stub → async load into
`ImageView`/`UIImageView`). Flex containers (`BnRow`/`BnColumn`/`BnStack`) come as thin
`BnView` wrappers. This closes the #1 RN-parity gap (there is only vertical stacking today).

Maps to BACKLOG "P4/P5 UI/styling" (re-scoped). Full 8-point DoD: [MILESTONE.md](MILESTONE.md).

Phases (approved at milestone-open 2026-07-13):
- ✅ **Phase 6.0** — Yoga-integration spike, both shells (DoD #1) — *complete (2026-07-13)*
   - **Verdict: GREEN on both rungs.** Yoga **3.2.1** links alongside the NativeAOT runtime
     artifact on both shells (Android `libyoga.so` from the `com.facebook.yoga:yoga` Maven
     JNI bindings — no NDK build needed; iOS `libyoga.a` built from source for the simulator,
     C++20, no duplicate symbols against the runtime archives), and the **native
     measure-callback round-trip works in BOTH channels** — the measured width *and* height
     reach the frame. So the architecture holds: **Yoga in the shells, no C-ABI change**
     (flex props ride the existing `SetStyle` wire); the fallback ladder (managed flexbox in
     .NET / native-layout mapping) is closed.
   - **Frame parity is asserted, not assumed:** both rungs build one canonical tree (row
     300×100 · box1 50×50 · box2 `flexGrow:1` · text auto-sized by a measure func with
     `alignSelf: flex-start`) and each asserts **all twelve numbers** (x/y/w/h × 3 frames) —
     the same twelve. A review caught the two rungs originally building *different* trees and
     both staying green only because neither asserted the heights that differed.
   - **The load-bearing iOS lesson (six red CI runs):** Xcode's Swift explicit-module
     dependency scanner reads the bridging header with a path-less header search — it honours
     neither `HEADER_SEARCH_PATHS` nor `-Xcc -I`. **Yoga's headers must never be visible to
     Swift.** All Yoga interop lives in Objective-C++ (`BnYogaProbe.mm`) behind a plain-C
     surface — the same discipline the shell already uses for the runtime. **Phase 6.1's iOS
     Yoga layer is therefore Objective-C++, budgeted, not re-litigated.**
   - **Final counts (all CI-asserted):** .NET **230/0** · JVM **79/0** · Android instrumented
     **41/41** · iOS XCTest **14/14**; the Yoga version pin (3.2.1 in both shells) is now
     enforced by the required lane. Merged in `#54`. See
     [design](../plans/2026-07-13-phase-6.0-design.md) +
     [spike conclusion](../plans/2026-07-13-phase-6.0-spike-conclusion.md).
- ✅ **Phase 6.1** — Flexbox layout core: flex props + the shell Yoga pass + the flex demo (DoD #2, #3, #6) — *complete (2026-07-13)*
   - **Yoga owns all placement on both shells.** `view` containers became plain frame containers
     (`BnYogaFrameLayout` / `UIView`); the vertical `LinearLayout`, the `UIStackView` and the three
     `NSLayoutConstraint`s that pinned the top-level form are gone from the render path. Typed C#
     flex params (`BnRow`/`BnColumn`/`BnView`) ride the **existing** `SetStyle` wire — **no ABI
     change** (still 9 exports + the 72-byte bridge).
   - **DoD #2 is a test result, not a claim:** `BnLayoutDemo` (`/layout`) asserts the **same frame
     table number-for-number on the AVD and the iOS simulator** — a `Grow=1` box computing exactly
     200 on both, a wrap row breaking at the same child on both. Measured leaves are pinned by an
     independent oracle (a constant-size measure func passes every relational assertion and fails
     the oracle).
   - **What the devices taught us:** a stock `FrameLayout` is not inert (it re-places children by
     gravity behind Yoga's back — and only in a real Activity); Yoga rounds with *two* rules, so
     its own rounding is off on both shells and the one conversion site owns all snapping; one
     `RemoveNodePatch` means a whole **subtree**, and on iOS a missed descendant is a **dangling
     `YGNodeRef`**, not merely a leak; `margin: auto` is not `margin`'s default; `strtof("12px")`
     returns 12.0, so both parsers need a strict whole-string rule (and the C locale).
   - The style routing table is hand-written in three places (.NET, Kotlin, Objective-C++) and is
     pinned by a **drift test across all three mirrors** — including Kotlin against the `.mm`
     directly, which is the sentence DoD #2 rests on.
   - **Final counts (all CI-asserted):** .NET **294/0** · JVM **79/0** · Android instrumented
     **71/71** · iOS XCTest **29/29**. See [design](../plans/2026-07-13-phase-6.1-design.md) +
     [plan](../plans/2026-07-13-phase-6.1-implementation-plan.md) +
     [conclusion](../plans/2026-07-13-phase-6.1-conclusion.md).
- ✅ **Phase 6.2** — Real scrolling on both platforms (DoD #4) — *complete (2026-07-14)*
   - The `scroll` NodeType, stubbed since Phase 2.5, is real. A `scroll` node is a **viewport**;
     the shell synthesises a **content node** (`height: auto`, *not on the wire*) whose
     Yoga-computed height **is** the content size — read straight out of Yoga, never derived by
     each shell. `BnScrollDemo` (`/scroll`) computes the same 800dp of content over a 200dp
     viewport, and the same row frames, on the AVD **and** the iOS simulator — and actually
     scrolls on both. **No ABI change.**
   - **`BnScroll` is a flex ITEM, not a flex CONTAINER** — the container family is absent by
     construction (`Justify="Center"` over 800 of content in a 200 viewport would offset it to
     y = −300 and make the top of the page permanently unreachable). Compose a `BnColumn` inside.
     The shells enforce the same rule at the wire, pinned by a Kotlin ≡ Objective-C++ drift test.
   - **The phase's real lesson: four plausible causal stories were written into this design, and
     every one was wrong.** `overflow: scroll` does not produce the 800 (**Yoga's `flexShrink`
     default of 0** does); the `onMeasure` fallback does not make the page scroll (it stops it
     snapping to the top on re-render); `Grow="1"` does not bound a viewport (it needs
     `Basis="0"` — which is why CSS's `flex: 1` sets basis to 0); and the iOS `contentOffset` clamp
     did not fire "on shrink" but on *every commit*, killing a rubber-band under the user's finger.
     Each was caught only by writing the test — none survived one.
   - **Final counts (all CI-asserted):** .NET **304/0** · JVM **79/0** · Android instrumented
     **96/96** · iOS XCTest **50/50**. See
     [design](../plans/2026-07-14-phase-6.2-design.md) +
     [plan](../plans/2026-07-14-phase-6.2-implementation-plan.md) +
     [conclusion](../plans/2026-07-14-phase-6.2-conclusion.md).
- ✅ **Phase 6.3** — URL images on both platforms (DoD #5) — *complete (2026-07-14, the last capability criterion)*
   - `<BnImage Src="…" />` fetches, decodes, measures and lays out — **Coil** on Android, **Kingfisher**
     on iOS, with **identical computed frames**. The `image` NodeType, stubbed since Phase 2.5, is
     real. **No ABI change** (`Src` rides the existing `UpdateProp` wire; the shell fetches the bytes).
   - **The sibling is the witness, not the image.** An intrinsic image measures 0×0 until its bytes
     land, then marks the Yoga node dirty and re-solves — and the proof is that *the band below it
     moves*. A shell could paint the bytes and never re-solve, and the image would still look right;
     deleting `markDirty` produces exactly that, and only the band says otherwise.
   - **The unit rule — one file pixel is one dp/pt — is the divergence no frame table could have
     caught.** An `ImageView`'s intrinsic size is in *pixels*, so the generic measure path reports
     `px/density` (**61dp** at the AVD's 2.625) where iOS reports **160** (`UIImage(data:).scale == 1`).
     Both shells now read the decoded pixel buffer directly, and Kingfisher's documented
     `.scaleFactor(UIScreen.main.scale)` idiom is **forbidden** (it would give 53.3pt vs 160dp).
   - **Two of three demo cases assert that *nothing moved*** — so without awaiting each loader's
     terminal callback, a suite could be **fully green on a device that never loaded a byte** (a
     blocked cleartext fetch is indistinguishable from a 404). Defended by awaiting all three
     outcomes by name, asserting the fixture's decoded size before any frame, and a loopback fixture
     server so CI never touches the internet.
   - **Frames are necessary but not sufficient**: making the re-solve re-enter mid-batch reddens
     *only* a layout-pass counter — every frame stays correct. Mechanism-level pins (layout-pass
     count, in-flight count, a pure guard with a unit test) come from reasoning, not from more frame
     tests.
   - **Final counts (all CI-asserted):** .NET **319/0** · JVM **83/0** · Android instrumented
     **111/111** · iOS XCTest **72/72**. See [design](../plans/2026-07-14-phase-6.3-design.md) +
     [plan](../plans/2026-07-14-phase-6.3-implementation-plan.md) +
     [conclusion](../plans/2026-07-14-phase-6.3-conclusion.md).
- ✅ **Phase 6.4** — M6 final audit + close (DoD #7, #8) → `v6.0` — *complete (2026-07-15)*
   - **All eight DoD criteria PASS** — [final audit](../plans/2026-07-14-milestone-6-final-audit.md),
     evidence-based per the house rule (no criterion accepted because a conclusion doc says so;
     guards mutation-tested). DoD #7 was PARTIAL at first audit and was **closed rather than
     papered over**: the AGP 9 incident (a Renovate toolchain bump silently un-compiled the whole
     Android shell, PR #81) proved a green assertion can sit on a source set nobody builds — so
     the close added a **required compile gate per shell** (`android-build` #84/#87, `ios-build`
     #83; branch protection now requires all three checks) and a **frame-table drift test**
     (Kotlin ≡ Swift, symbolic for measured sizes), retiring the last cross-shell contract held
     by transcription.
   - The audit also caught a required-check design trap live: a `needs`-gated job is invisible on
     the PR check list and wedges "Expected — waiting" if its dependency fails — `android-build`
     was rebuilt self-contained (its own bionic publish, no `needs`), proven by all three compile
     jobs starting in parallel on PR #87.
   - **Final counts (all CI-asserted, all behind required or named lanes):** .NET **324/0** ·
     JVM **83/0** · Android instrumented **111/111** · iOS XCTest **72/72**.

---

### ✅ Milestone 7 — Components + Razor  *(complete 2026-07-16 — `v7.0` tag pending merge)*

The things you build UIs *with*: **`.razor` authoring** (the standing M3-era ledger item —
author components in Razor syntax instead of hand-written `BuildRenderTree`, with the five demo
pages rewritten as the parity proof) and the `Bn*` components a real app opens with, on top of
M6's engine: a **virtualized list** (`BnList` — which forces the `onScroll` wire design M6
deferred to exactly this customer), form controls + a real `picker`, `BnModal` (the first
overlay surface), `BnImage` polish (Placeholder/OnError/ContentMode — each a *measurement*
design), and a **React Native core-component parity survey** (have / ships-in-M7 / ledgered)
whose cheap wins ship. Hygiene: typed `FontSize`/`Padding`, the route-registry unification.
**Two named risks, spike-first:** Razor compilation under NativeAOT (7.0) and the 60Hz
`onScroll` producer on a wire designed for taps (7.2). Full 8-point DoD: [MILESTONE.md](MILESTONE.md).

Phases (approved at milestone-open 2026-07-15; subject to the 7.0 verdict):
- ✅ **Phase 7.0** — the Razor-compilation spike (DoD #1) — *complete (2026-07-15), verdict **GREEN***
   - A `.razor` file compiled by the Razor source generator (`Microsoft.NET.Sdk.Razor` on
     `BlazorNative.Components`, `StaticWebAssetsEnabled=false` the ONE switch) renders through
     `NativeRenderer` with a patch stream **byte-identical** to its hand-written twin across
     mount + `@bind` change + `@onclick` dispatches (`SpikeRazorTests.GoldenVsTwin`,
     mutation-verified: a `[Parameter]` removal and a one-digit style drift both redden by name).
   - **The `@bind` answer** (design risk #1): own `[BindElement]` attributes are **impossible**
     without Components.Web — the compiler's bind provider requires its
     `BindInputElementAttribute` type before reading ANY bind metadata (verified empirically and
     in the dotnet/roslyn source) — and Components.Web (already our pinned reference since 3.4)
     declares exactly our wire, so we declare nothing. Footgun recorded: an out-of-scope `@bind`
     compiles **silently** to literal markup — 7.1's `_Imports.razor` makes it structural.
   - **Renderer finding, fixed red-first** (`MarkupFrameTests`, 3/5 red before the fix): the
     compiler preserves inter-element whitespace as Markup frames, which the walk dropped
     WITHOUT a sibling slot while Blazor's diff counts them — every edit after a markup sibling
     resolved one slot short. Whitespace markup now takes a slot (`SlotKind.Markup`), emits no
     patch, contributes zero host views; non-whitespace markup (no native innerHTML) is a
     strict-mode contract violation. No ABI change, no shell change.
   - **Fallback ladder closed** (own generator / stay hand-written — not taken). Counts:
     .NET **324 → 333/0** (+5 renderer, +4 spike; ci.yml provenance) · JVM **83/0** · publish
     **4 IL2072 + 9 exports on all three RIDs**, zero web-asset traces in the publish log.
     See [design](../plans/2026-07-15-phase-7.0-design.md) +
     [conclusion](../plans/2026-07-15-phase-7.0-spike-conclusion.md) (the pinned recipe).
- ✅ **Phase 7.1** — `.razor` authoring end-to-end: the five pages + parity + typed-props cleanup (DoD #2, part #8) — *complete (2026-07-15)*
   - **The app is authored in `.razor`** (DoD #2 closed): all five pages + `BnThemedPanel`
     converted via **twin → swap → retire** (per page: a transitional `.razor` twin proven
     patch-stream **record-identical** across every interaction, mutation-verified, then the
     `.cs` deleted and the twin retired). **Zero golden edits, zero shell edits** — and the
     device lanes re-ran green on the converted app: **android-instrumented run 29420994993
     (111/0)** · **ios run 29420996916 (XCTest 72/0)**.
   - **Two pre-1.0 breaking changes, recorded, no shim:** `BnThemedPanel` `internal` → `public`
     (forced — Razor component discovery is public-only; an internal component in markup silently
     parses as a plain HTML element) and `FontSize`/`Padding` `string?` → `float?` (the M4-ledger
     stragglers — wire-identical via the 6.1 invariant-culture lift; the goldens were the tripwire
     and none moved). The typed-props half of DoD #8 is closed.
   - The **supported `.razor` subset** is normative and ledgered for a future analyzer (no
     `@page`/`.razor.css`/raw HTML/JS interop; `@key` insert/remove only — reorders stay a loud
     violation with 7.2's `BnList` as the named customer; `_Imports.razor` load-bearing;
     `[Inject] public` not `@inject`; public-only component discovery).
   - Counts: .NET **339/0** (the pre-conversion baseline — conversions are wire-neutral by
     construction) · JVM **83/0** · publish **4 IL2072 + 9 exports** · consumer smoke **PASS**.
     See [conclusion](../plans/2026-07-15-phase-7.1-conclusion.md) (subset + recipe + ledger).
- ✅ **Phase 7.2** — the `onScroll` wire design + `BnList` (DoD #3) — *complete (2026-07-15)*
   - **The first 60Hz producer runs on the existing event wire** (DoD #3 closed): shell-side
     conflation — ONE pending offset per scroll node, replace-not-queue, dispatched on the three
     lane-availability edges, ordering free via the single FIFO lane, dp at the source. **No ABI
     change** (9 exports, all three RIDs). Throughput evidence, both platforms: **Android burst
     100 samples → 2 dispatches (50:1), fling 78 → 35 (2.23:1), final offset always delivered;
     iOS mirrors on the same `/list` numbers.** Conflation mutation-proven at the mechanism on
     BOTH shells — iOS mutations run ON CI (dispatch-per-sample → run 29436079300, 81/4 red;
     queue-not-replace → run 29436083505, 83/2 red — the same failure shapes as Android).
   - **`BnList<TItem>`** (generic, `.razor` via `@typeparam`) over `BnListWindow.Compute` (pure,
     35 cases): keyed spacers + keyed window rows; `/list` = 500 × 64dp with **counted liveness**
     (11/15/11 rows, asserted .NET-side and on both shells' child counts). Row state travels by
     view IDENTITY (`EditText`/`UITextField` text + focus survive the slide); eviction destroys.
   - **The empirical `@key` answer:** a keyed window slide diffs as **insert/remove ONLY** —
     permutations fire only when surviving keys CROSS. Detector ran strict with the 7.0 `default:`
     arm live (`KeyedWindowSlideTests`); the loud arm stays, **no wire move-concept needed**. The
     true-move ABI conversation is deferred with its trigger (first component whose UX crosses
     surviving keys).
   - **Two 3.3-era renderer bugs, red-first:** disposal removes now PRECEDE the batch's creates
     (InsertIndex was translated against trimmed state); the same-batch re-render + disposal
     zombie — review-called "narrow future case", then **constructed through a public path**
     (child handler `StateHasChanged()` + parent remove callback) — fixed with a pass-2 delta
     emission, mutation-proven (`SameBatchRerenderDisposalTests`).
   - Hardening #9 re-examined with real numbers (the milestone's promise): scroll cannot flood
     the lane by construction; the slow-HANDLER half of #9 stays open for M10.
   - **Final counts (all CI-asserted):** .NET **388/0** · JVM **83/0** · Android instrumented
     **124/0** · iOS XCTest **85/0** (run 29435524820). See
     [design](../plans/2026-07-15-phase-7.2-design.md) +
     [conclusion](../plans/2026-07-15-phase-7.2-conclusion.md).
- ✅ **Phase 7.3** — form controls + a real `picker` (DoD #4) — *complete (2026-07-15)*
   - **The form is real on both shells** (DoD #4 closed): `BnCheckbox`/`BnSwitch`/`BnSlider`
     (three new NodeTypes 8/9/10 — a wire-VOCABULARY extension on the existing int32 field, no
     ABI change, pinned on all three mirrors incl. the Kotlin content pin Gate 1 found missing)
     + **`picker` made real** (`Spinner`/`UIPickerView`, ending the last 2.5-era stub). All four
     `.razor`, `@bind-` pairs on the existing change wire; `/form` is the seventh page.
   - **The state-owner precedent:** the picker owns items + selection natively — `items` as
     strict flat-JSON on `UpdateProp` (one acceptance set, three parsers, 15 normative malformed
     categories + the casing-discriminating rejection vector), selection back on the change
     wire, the normative clamp rule (`items`-before-`selectedIndex`, every items application
     re-clamps, the **CLAMPED index is dispatched** — asserted on both device lanes).
   - **The guard asymmetry, verified per control:** Android `applyingBatch` for the synchronous
     three + the Spinner's expected-selection compare (its notifier is POSTED) + the `BnSpinner`
     self-layout fix; iOS **no batch guard anywhere** — instead the slider step-dedup and the
     picker same-row compare, with record-before-apply recognized (Gate 3 review) as structurally
     the same expected-selection guard, made falsifiable by a raw-`selectRow` test.
   - **Two iOS 26 platform findings, fixed on CI:** `UISlider` stores value as a Float32 track
     fraction (one-ulp read-back → the exact-value shim); `UISwitch` refuses imposed frames —
     the first widget in either shell to do so (stretch law asserted on the Yoga box + oracle).
   - **Final counts (all CI-asserted):** .NET **492/0** · JVM **90/0** · Android instrumented
     **147/0** · iOS XCTest **111/0** (run 29451417339 on `e9a7376`). iOS mutations run ON CI:
     29449282815 (108/1), 29449284412 (108/1), 29449281293 (104/5). See
     [design](../plans/2026-07-15-phase-7.3-design.md) +
     [conclusion](../plans/2026-07-15-phase-7.3-conclusion.md).
- ✅ **Phase 7.4** — `BnModal` + the RN survey's cheap wins (DoD #5, #7) — *complete (2026-07-16)*
   - **The first overlay surface is real on both shells** (DoD #5 closed): `BnModal` as an
     **overlay in the existing root** — NodeType `modal = 11`, a wire-VOCABULARY extension, no
     ABI change; the native-dialog option rejected and recorded (a second Yoga root + a second
     frame-application surface + a window that OWNS its dismissal — the state-owner violation).
     The **anchor+overlay model**: a 0-sized absolute anchor at the modal's wire slot (the
     THIRD index-mapping rule — sibling insert indices never skew), a full-root overlay
     attached LAST (stacking = creation order; re-show = re-create = on top), children
     redirected, ONE resolved index feeding both trees; the two-subtree `RemoveNode` purge as
     a **fixpoint**, exactly-once by ownership-disjointness (nested-modal explicit test).
   - **Dismissal is a REQUEST** — scrim tap / dismiss button / Android back all ride the wire
     as `click`; the shell never self-closes; `@if (Visible)` unmount is the only close path.
     Android back consults the modal stack BEFORE navigation-back (consume-on-presence); iOS
     ships nothing there — the second "same wire, platform-appropriate trigger" divergence.
   - **The survey's cheap win** (DoD #7 closed): `BnActivityIndicator` (NodeType 12, measured
     leaf, `ProgressBar`/`UIActivityIndicatorView`, intrinsics by oracle) + the committed
     19-row RN parity table (11 have-it; Image polish → 7.5; SafeAreaView ledgered with the
     named edge-to-edge problem; the rest ledgered with reasons).
   - **Empirical findings:** a modal hide frame is FIVE removes (the 7.2 disposal shape
     composing — pinned at both altitudes); a zero-attribute `.razor` element collapses to
     `AddMarkupContent` (renderer rejects by contract → the indicator is hand-written C#); the
     design's literal tap-filter mutation expectation was unsatisfiable (no hosted XCTest can
     synthesize a `UITouch`) — restated as truth-table + wiring + live-recognizer pins, the
     untested seam shrunk to the `touch.view` property read.
   - **Final counts:** .NET **518/0** · JVM **92/0** · Android instrumented **166/0** (local
     AVD, asserted in android-instrumented.yml) · iOS XCTest **132/0** (run 29487073302).
     iOS mutations ON CI: 29485603676 (118/13), 29485649558 (125/6), 29485693301 (129/2);
     4 Android mutations run locally, redlines in the commit bodies. Reviews: Gate 1 PASS
     (findings applied in b4b8b02), Gate 2 PASS clean, Gate 3 PASS (M1 applied in 2e24d7d).
     See [design](../plans/2026-07-16-phase-7.4-design.md) +
     [conclusion](../plans/2026-07-16-phase-7.4-conclusion.md).
- ✅ **Phase 7.5** — `BnImage` polish: Placeholder/OnError/ContentMode (DoD #6) — *complete (2026-07-16)*
   - **The 6.3 image ledger resolved with ZERO new measurement states** (DoD #6 closed): two
     props on the existing prop wire (`placeholderColor`, `contentMode` — seq 25/26, no
     renumbering) + one event name (`error`) on the existing dispatch wire; no new NodeTypes,
     exports stay 9, no ABI change. The 6.3 measurement contract survives verbatim, re-proven
     on the ninth page (`/imagepolish` — `/image`'s parity goldens stayed byte-identical).
   - **The three features as proven:** placeholder = paint that never measures (4-row state
     table normative on both shells; kept on ERROR, cleared on SUCCESS/null; iOS's
     bounds-tracking subview a recorded improvement over the design's Kingfisher suggestion);
     `OnError` = the wire src verbatim, at-most-once per (src, generation) behind the shared
     liveness guard, attach-iff-HasDelegate, CANCELLED never dispatches, failure never
     changes measurement in either direction; `ContentMode` = the strict four-word table,
     paint-only (four identical frames under four modes), default Contain diverging from
     RN's cover with the recorded 6.3 reason, `clipsToBounds` always on iOS (pinned).
   - **The headline — the defer mechanism twice corrected by review:** Gate 2 I-1 (the DEFER
     arm fired a decision-time capture — latent on Android, proven LIVE on iOS via the
     nil-URL sync failure; fixed by fire-time re-decision on both shells, both adversarial
     same-batch orderings pinned end-to-end) and Gate 3 I-1 (the dispatch table's ROW ORDER
     was normative and wrong — handlerAttached before applyingBatch dropped the mount-time
     sync failure on iOS while Android dispatched, a divergence a test comment had recorded
     as parity; row swap in BOTH tables). Plus the .NET equivalent mutant (the HasDelegate
     drop is unobservable — RenderTreeBuilder omits delegate-less callbacks; honestly
     recorded, behavior pinned by the zero-wire-presence golden).
   - **Final counts:** .NET **537/0** · JVM **106/0** · Android instrumented **182/0** (local
     AVD, asserted in android-instrumented.yml) · iOS XCTest **153/0** (run 29510920883 — a
     re-run after a `BnClipboardTests` environmental flake, 5.4-era, zero image-adjacent
     diff; prior green 29501402766 at 152/0). iOS mutations ON CI: 29502212508 (149/3),
     29502258241 (149/3), 29502299416 (149/3), 29502355939 (149/3); seven Android mutations
     run locally, redlines in the commit bodies. Reviews: Gate 1 PASS (miscount fixed
     1dee252), Gate 2 PASS (I-1 fixed 8ef64be), Gate 3 PASS (I-1 fixed 20afbcb).
     See [design](../plans/2026-07-16-phase-7.5-design.md) +
     [conclusion](../plans/2026-07-16-phase-7.5-conclusion.md).
- ✅ **Phase 7.6** — route-registry unification + M7 final audit + close (rest of #8) — *complete (2026-07-16)*
   - **The route-registry unification** (DoD #8's last open half, promised since 5.1):
     `PageManifest.cs` declares every page ONCE (14 rows: 9 routed + 5 probes, the
     `Mount<T>` lambdas verbatim — the trim-law shape moved, not changed: publish stays
     **4 IL2072 + 9 exports**, verified live). `HostSession.s_components` and
     `NativeNavigationManager.s_routes` are **derived views** (one array, a fan-out, no
     static-init cycle); Android's `DEEP_LINK_COMPONENTS` is the one surviving **pinned
     mirror** (consulted at Intent-parse time, before the `.so` loads — transmit is
     structurally impossible, generate rejected as build machinery for an 8-row map),
     held by `RouteTableDriftTests` in the **required lane**: the PAIR-FOR-PAIR pin
     (a route on the wrong page is drift set-equality cannot see), the `?: "BnDemo"`
     fallback pin, the nine-page baseline retargeted to the manifest. Five mutations run
     red-first (redlines in `90ac62c`); the pin landed green against the untouched Kotlin
     map; the `EveryRoute_Resolves…` tautology retired, not kept. iOS: zero files —
     no route surface exists; the drift-test header names what a future deep-link story owes.
   - **The hygiene ledger paid** (7.4/7.5 deferrals): H1 stale lifecycle headers repointed
     (both shells), H2 `scrollDiagnostics` → `diagnostics` (both shells), H3
     `BnClipboardTests` bounded re-tap (3 attempts, real `UIPasteboard`, retry history in
     the failure), H4 the indexed-insert-at-root synthetic (+1 instrumented), H5 the
     matched placeholder recolor/null-clear pair (+1 instrumented, +1 XCTest). H6/H7
     deferred by name into the M8 ledger.
   - **ios.yml drops `pull_request`** (owner cost request, `eee4551`): the advisory lane's
     PR coverage moved to per-gate dispatch + push-main; both M6-audit F1 fixes stand
     (push-main kept, no paths filter); restore condition recorded in the workflow header.
   - **The M7 final audit** ([2026-07-16-milestone-7-final-audit.md](../plans/2026-07-16-milestone-7-final-audit.md)):
     **PASS on all eight DoD**, every locally runnable row re-run live at the tip
     (.NET 539/0 · JVM 106/0 · publish 4 IL2072 + 9 exports via dumpbin), every cited
     device run verified green via the GitHub API, branch protection read back == exactly
     the three required contexts, the ABI re-proven independently (72-byte bridge × 3
     mirrors, thirteen NodeTypes × 3 mirrors, Yoga 3.2.1 × 3 files). Four findings
     recorded, none blocking.
   - **Combined review PASS** — I-1 (MainActivity KDoc names its pin) + I-2 (GITHUB-SETUP
     vs. ios.yml contradiction) applied in `7aaa424`; M-1 (drift-parser comment-line
     limitation — the house pattern's shared limitation) and M-2 (retry visibility is
     log-only) recorded.
   - **Final counts:** .NET **539/0** · JVM **106/0** · Android instrumented **184/0**
     (local AVD, asserted in android-instrumented.yml) · iOS XCTest **154/0**
     (run 29515968994). See [design](../plans/2026-07-16-phase-7.6-design.md) +
     [conclusion](../plans/2026-07-16-phase-7.6-conclusion.md). **`v7.0` is tagged after
     the close PR merges, on the owner's go.**

Maps to BACKLOG "P5 — components".

---

### 🔄 Milestone 8 — Developer Ecosystem  *(active — opened 2026-07-16; the old P5, repositioned after capability)*

Now there's a real layout engine + component library worth shipping. Scoped at
milestone-open (owner decisions recorded in [MILESTONE.md](MILESTONE.md)): publish-READY
packages with the real push on a GitHub Release (manual go); the `dotnet new` template =
.NET app + Android shell; the docs site = **Docusaurus** (the owner's AdoNet.Async
pattern); the `BlazorNative.Cli` **ledgered**, not built. Maps to BACKLOG.md "P5 —
Developer experience and ecosystem".

- ✅ **Phase 8.0** — the samples/library separation + registration inversion (DoD #1) —
  *complete (2026-07-16)*
   - **The inversion as proven:** `BlazorNativeApp.RegisterPages` +
     `BlazorNativePage.Routed<T>/Named<T>` (the DAM(All) factories are the ONLY
     constructors of the mount thunk, the lambda verbatim the 7.6 row shape); the app
     registers via `[ModuleInitializer]` (eager inside `blazornative_init` under
     NativeAOT, idempotent `EnsureRegistered()` for CoreCLR test hosts); `PageManifest`
     = the internal store (loud validation, register-once, never-after-freeze), the
     derived views lazy-after-freeze; Runtime's `ProjectReference` to Components
     **deleted** — the library dependency graph no longer knows the app. The publish
     head = `samples/BlazorNative.SampleApp` with `UnmanagedEntryPointsAssembly`
     keeping the 9 exports in Runtime and an `AfterTargets=Publish` target freezing
     the artifact names — every consumer re-pointed a DIRECTORY; **zero shell-code
     lines** (the filtered Kotlin/Swift/ObjC++/project.yml diff is empty;
     `build.gradle.kts` moved 4 path strings only).
   - **THE HEADLINE — the trim finding (the milestone's named risk, materialized):**
     the design's premise "module initializers are unconditional ILC roots" was FALSE
     in nativelib mode — ILC roots only the export assembly's entry points and trimmed
     the ENTIRE SampleApp (0 IL2072, 400KB smaller, pages absent, silent until rc-1 at
     first mount). Caught by the stop-and-analyze rule; fixed minimally with
     `TrimmerRootAssembly Include="BlazorNative.SampleApp"` (the app roots ITSELF; the
     shipped libraries stay fully trimmable — the restored 4-IL2072 shape is the
     proof). Now pinned three ways: the IL2072 `-ne 4` gate, the JVM lane's real mount
     of the published binary, the page-name presence probe in the export step. Recorded
     as a design-doc correction + two NAMED 8.3-template inputs (the csproj line; the
     M-1 `EnsureRegistered` guard order) in MILESTONE.md DoD #4.
   - **Purity + file fates:** 16 types moved (11 `.razor` at 100% rename similarity;
     SpikeRazor relocated — the codegen canary lives; BnThemedPanel out on the
     demo-only verdict); `PackagePurityTests` pins the roster BOTH directions + the
     pattern net + the shipped set at exactly six assemblies. 11 golden test files
     changed by exactly one `using` line each; `RouteTableDriftTests` retargeted one
     expression, green against the UNTOUCHED Kotlin map.
   - **Final counts:** .NET **553/0** (539 → 553: +10 RegistrationTests, +4
     PackagePurityTests; the ci.yml guard literal fixed in `b96a927` after review C-1
     caught it at 539) · JVM **106/0** (canonicalized win-x64 dll, new directory) ·
     Android instrumented **184/0** (local AVD; bionic republish 4 IL2072) · iOS XCTest
     **154/0** (run 29527121729 — iossimulator publish 4 IL2072 + all 9 symbols).
     Mutations run on both gates (purity ×2, dup-route, Kotlin wrong-page, deleted
     row — redlines quoted in `028d47d`). Review: Gate 1 PASS (C-1 + I-1/I-2 applied,
     M-1..M-3 carried). See [design](../plans/2026-07-16-phase-8.0-design.md) +
     [conclusion](../plans/2026-07-16-phase-8.0-conclusion.md).
- **Phase 8.1** — publish-ready packages + consumer smoke on the real set (DoD #2) — ⏳
- **Phase 8.2** — the release pipeline, manual go (DoD #3) — ⏳
- **Phase 8.3** — the `dotnet new` template: app + Android shell (DoD #4) — ⏳
- **Phase 8.4** — the docs site: Docusaurus + GitHub Pages (DoD #5) — ⏳
- **Phase 8.5** — hygiene + M8 final audit + close (DoD #6) → `v8.0` — ⏳

---

### ⏳ Milestone 9 — Platform Breadth + Real Device  *(pending)*

More host APIs via the [bridge-extension pattern](../bridge-extension.md) (camera,
geolocation, biometrics, notifications), **real-device iOS** (code signing, provisioning,
App Store validation — **requires an Apple Developer account**, the M5 simulator-only
deferral), and the Android completeness deferred from M5 (FCM push, secure storage). The
on-device inspector channel (4.4 carryover) lands here (the route→component registry
unification, once slated here, was closed by Phase 7.6). Maps to BACKLOG.md "P4 — full
platform coverage" (remainder).

---

### ⏳ Milestone 10 — Framework Hardening  *(pending — old P6)*

Accessibility, i18n (with the `InvariantGlobalization` workaround), performance & memory
budgets (the M1-deferred allocation-budget work continues), a security model (URL
allowlist, secure buffers, crash isolation), error handling & crash recovery, and the
**open hardening ledger** (issues #8/#9/#12/#13 — async-handler window, dispatch-lane
starvation, RemoveComponent bucket scan, TranslateToViewIndex memoization). Maps to
BACKLOG.md "P6 — Framework hardening".

---

### 🔮 Backlog / Future *(uncommitted — promote to a dated milestone when they approach)*

**Enterprise readiness** (old P7): OTA updates with delta + rollback, MD3 / iOS HIG
compliance defaults, legal compliance (SBOM, license audit, GDPR, export control, FIPS),
observability/analytics, performance-budget enforcement. **Also:** multi-window support,
Windows/macOS shells, WASM Component Model migration, BlazorNative Studio, the ZeroAlloc
deep integrations, a reference app. See BACKLOG.md "P7 — Enterprise readiness" + "Future /
exploratory".

---

## Notes

- **Capability before ecosystem** (2026-07-13 re-plan): M6 (layout) → M7 (components+razor)
  → M8 (ecosystem/publish) → M9 (platform breadth + real device) → M10 (hardening).
- M9's real-device iOS is gated on an **Apple Developer account** (user dependency).
- Each milestone closes with `audit-milestone` → `complete-milestone` → tag `vN.0`
  (M6 → `v6.0`, and so on).
