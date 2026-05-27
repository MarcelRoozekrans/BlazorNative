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
- ⏳ **Phase 2.8** — End-to-end Hello demo + final audit — *pending (was 2.7 before 2026-05-27 restructure)*

---

### ⏳ Milestone 3 — P2: Real Apps Can Be Built  *(pending)*

`@bind` two-way binding, `Bn*` component library, cascading values, end-to-end DI, navigation service, `BlazorNativeComponentBase` ergonomics.

**Architectural items inherited from M2 (added 2026-05-27 during M2 phase restructure):**
- **Bidirectional event flow.** AttachEvent/DetachEvent patches need host→.NET event dispatch. Requires keeping the `.wasm` alive past `Main` return (currently it exits after the sentinel mounts). Substantial runtime-loop change. The event-ingress mechanism (`WasiBridge.DispatchEventCore` via `[UnmanagedCallersOnly]` export) already exists from Phase 2.0 — what's missing is the long-running-Main shape that lets the host invoke it post-boot.
- **Multi-component support.** `NativeRenderer.ProcessRenderTreeDiff`'s `PrependFrame` arm currently passes `parentNodeId: null` for all subtrees. This is correct for root-component PrependFrames but wrong for nested-component re-renders (each child component's diff arrives as a separate `BnRenderTreeDiff` whose root should attach to the parent component's view, not to the host root). Track and fix when component composition lands.
- **`AppendChild` patch emission.** Currently defined in `PatchProtocol.cs` but never emitted. Whether component composition needs it (vs. re-emitting CreateNode + parent linkage) is an M3 design question.

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
