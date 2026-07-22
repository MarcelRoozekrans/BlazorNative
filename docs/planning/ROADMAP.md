# BlazorNative — Roadmap

*Source of truth for milestone and phase state. Updated by project-orchestration sub-skills.*

## Milestones

> ### ⚠ The milestone tags `v1.0`–`v7.0` are RETIRED — and this is the place that says why
>
> **If `git checkout v6.0` fails for you, this note is the answer.**
>
> Every milestone below records the tag that closed it — *"tagged `v6.0`"* — and **each of
> those statements was true when it was written.** **Phase 8.6 (2026-07-17) retired the
> milestone-tag namespace**: `v<semver>` now belongs to release-please, which cuts
> `v0.1.0`-shaped tags for **package releases**, and **no `vN.0` will ever be cut
> again.** `v8.0` was **cancelled, not deferred** — M8's DoD #6 named it, M8 is complete
> without it, and the [M8 audit addendum](../plans/2026-07-17-milestone-8-audit-addendum.md)
> is where that is said out loud.
>
> **STATE, as of 2026-07-17 — stated precisely because the rest of this note is about
> their absence: ALL SEVEN TAGS (`v1.0`…`v7.0`) ARE GONE.** They were deleted on
> **2026-07-17** on the owner's go — `git push origin --delete v1.0 … v7.0`, plus the
> locals — as the **last step** of Phase 8.6's close. `git ls-remote --tags origin` returns
> **nothing**, and this repo now has **no tags at all** until release-please cuts the first
> `v0.1.0`. **That is why `git checkout v6.0` fails**, and it is the whole reason
> this note exists.
>
> **Two readers will now disagree about whether `v6.0` exists, and both are right:** a
> **fresh clone** does not have them; an **existing clone or fork keeps all seven forever**,
> because `git fetch --prune` does **not** delete tags without `--prune-tags`. So the
> failure this note answers is one that only *some* readers can see, which is exactly the
> kind that needs a written answer.
>
> **The chapter record is this file and the milestone audits — it always was.** The phase
> and audit documents in `docs/plans/` say *"tagged `vN.0`"* throughout and **are not
> rewritten**: they are **dated records**, and a record edited to agree with today is not a
> record. The tags were how the chapters were marked; the writing is what the chapters were.

> ### ⚠ The version is `0.x` — the `1.0.0-preview.N` scheme is RETIRED, and this note governs every mention of it below
>
> **Phase 8.7 (2026-07-17) moved the version from `1.0.0-preview.N` to pre-1.0 semver.**
> Entries below say *"Versioning verdict: `1.0.0-preview.1`"* and *"at `1.0.0-preview.1`,
> `fix:`/`feat:`/`feat!:` all produce `1.0.0-preview.2`"*. **Every one of those was true when
> it was written**, and — exactly as with the tags above — **they are not retrofitted.** This
> note is the answer instead.
>
> **What is true now**, measured against release-please 17.10.3's own strategy rather than
> reasoned: the first release is **`0.1.0`**, and from there `fix:` → `0.1.1`, `feat:` →
> `0.2.0`, `feat!:` → `0.2.0`. **The commit type moves the version again** — 8.6's *"the
> version is a counter"* was a property of the preview suffix, and the suffix is gone.
>
> **Nothing had ever been published when this changed** (0 releases, 0 release-triggered runs
> — verified at the time), which is the only reason it was free to do. **It is permanent at
> the first publish.** `docs/GITHUB-SETUP.md` carries the ladder and the graduation section.

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

### ✅ Milestone 7 — Components + Razor  *(complete 2026-07-16, tagged `v7.0`)*

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

### ✅ Milestone 8 — Developer Ecosystem  *(complete — opened 2026-07-16, closed 2026-07-17; **no `v8.0` tag — cancelled by Phase 8.6**, which retired the milestone-tag namespace: M8 is complete on its [final audit](../plans/2026-07-17-milestone-8-final-audit.md), see the [addendum](../plans/2026-07-17-milestone-8-audit-addendum.md); the old P5, repositioned after capability)*

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
- ✅ **Phase 8.1** — publish-ready packages + consumer smoke on the real set (DoD #2) —
  *complete (2026-07-16)*
   - **ZERO library code, the wire not at all, the publish head zero-diff — the packages
     became the truth.** The 4.5-era packaging (five packages at a phase-stamped version,
     metadata copied per-csproj, a smoke predating the registration API) becomes the M8
     shape: **six** packages (Core, Renderer, **Http**, Components, **Runtime**,
     Analyzers) sharing ONE metadata home (`src/Directory.Build.props`) and ONE version
     literal, each with a README + license + symbols + SourceLink, packed
     deterministically and proven every PR.
   - **Two set corrections, both findings:** DoD #2's five-name list was **shorthand
     drift** — Http has shipped through the smoke since 4.5 and the purity pin says six
     (MILESTONE.md corrected); and the 4.5 "per-csproj by design" rule was obsolete **by
     construction** — the purity pin proves src/ holds exactly the six, so a src-scoped
     props rides precisely the packed set. Runtime is the new package, packable only
     because 8.0 evicted `PublishAot`. **The M4 `PackageReadmeFile` prerequisite is
     CLOSED** (six READMEs; the Http one written against `AndroidShellBridge.kt` reality).
   - **Versioning verdict:** `1.0.0-preview.1`, hand-bumped, reset off the never-public
     `1.2.0-phase-4.5` (no observer of the downgrade); the churn surface collapses 7 → 1.
     GitVersion/release-please **rejected for now** — height-based version-per-merge
     contradicts DoD #3's "nothing public until the owner's Release", the payoff starts
     once 8.2's workflow exists (a named 8.2 input), conventional-commit inference
     mis-bumps phase-structured history, and `tag-prefix: v` collides with the `v4.0`–
     `v7.0` milestone tags.
     > *(**All four reasons are now answered, and release-please is IN** — Phase 8.6,
     > decision 1. Reason 1 (auto-publish contradicts DoD #3) → **`draft: true`**: a draft
     > Release fires no workflow, so the owner's click is still the go. Reason 2 (the payoff
     > starts once 8.2's workflow exists) → **it exists.** Reason 3 (mis-bumps
     > phase-structured history) → **`bootstrap-sha`** draws a line under all 580
     > pre-8.6 commits, plus the new commit contract. Reason 4 (`tag-prefix: v` collides
     > with the milestone tags) → **the milestone-tag namespace is retired.** **GitVersion
     > stays out** for reason 1, undisturbed. This is an append, not a retraction: **the
     > rejection was right on 2026-07-16** and it named, in advance, what would change it.)*
   - **THE HEADLINE — the review demonstrated a vacuous pass:** the nupkg type scanner
     passed GREEN while **blind** ("0 types clean" ×6, exit 0), violating the house rule
     this repo states in its own code — *a pin that cannot see its subject must never pass
     vacuously* — the same failure mode an in-code comment records EYE catching once
     before. Fixed with a two-arm positive control (non-empty + a real sentinel type per
     package), proven **non-redundant** by re-running the historical `return ,$names` bug:
     count=1 sails past the count arm; only the sentinel catches it.
   - **Mutation 4 corrected:** the design's SampleApp vehicle is structurally impossible
     (MSB4006 circular, quoted — a genuine finding), but the assertion is **provable** —
     Gate 1's Newtonsoft substitute had exercised the wrong branch (the allowlist, not the
     no-app-dependency tooth), now proven with an equivalent non-cyclic `BlazorNative.*`
     stub. Recorded as proven-with-an-equivalent-vehicle, NOT unprovable-by-construction.
   - **The four-copies finding (I-2):** only 1 of 4 shipped-set copies was pinned, and the
     roster's docstring claimed a pin that did not exist (false *and* unfalsifiable). The
     roster is **deleted** (the version pin enumerates src/ itself); the two irremovable
     copies (the .ps1, the out-of-solution ConsumerSmoke csproj) are pinned against the
     same enumeration. A fake seventh package now reds **five** pins instead of one
     greenable literal.
   - **Final counts:** .NET **557/0** (553 → 555 → 557; 23 + 132 + 402) · JVM **106/0**
     (untouched) · publish gates **unmoved** (win-x64: 4 IL2072 + 9 exports + the page
     probe; both bionic RIDs 4) · smoke green end to end (6 nupkg + 5 snupkg at
     `1.0.0-preview.1`, zero pack warnings, SourceLink `repository@commit` stamped,
     provenance ×6, BN0011 trip, ConsumerSmoke PASS — **the 8.0 API's first out-of-repo
     consumer**, RegisterPages laws proven from the packages alone). Device lanes
     untouched: **184**/**154** stand on 8.0's provenance. Review: Gate 1 PASS — 3 Important
     + 4 Minor (I-1/I-2/I-3 + M-1 fixed, M-4 accepted as-placed, the review's own M-2/M-3
     ledgered as accepted limitations; separately, 8.0's M-2/M-3 riders both closed here). See
     [design](../plans/2026-07-16-phase-8.1-design.md) +
     [conclusion](../plans/2026-07-16-phase-8.1-conclusion.md).
- ✅ **Phase 8.2** — the release pipeline, manual go (DoD #3) — *complete (2026-07-16)*
   - **One door, and every check in front of it.** `release.yml` — `validate` (**no key,
     ever**) → artifact → `push` (`needs: validate`, `if: event_name == 'release'`, the
     **sole** `NUGET_API_KEY` reference, **no checkout at all**, so *"it pushes only
     validated bytes"* is **structural**, not promised). The split exists for one fact:
     **the six-package push is not atomic, cannot be made atomic, and has no undo** —
     nuget.org has no hard delete, only unlist. Every **decision** lives in
     `scripts/release-preflight.ps1` (classifier + tag↔props assertion + the nuget.org
     preflight + an 8-row `-SelfTest` that runs on every PR), because YAML firing once
     per Release is the least testable code in the repo. **Zero library code, zero shell,
     zero wire.**
   - **THE HEADLINE — the `v8.0` hazard: the phase that builds the pipeline creates its
     own worst input.** DoD #6 tags `v8.0` to close M8, and the `release` event carries
     **no tag filter** — so an AdoNet.Async-shaped `${TAG#v}` would have turned that
     milestone announcement into **six packages pushed at version "8.0", permanently**.
     **Double-guarded and independently verified:** the classifier reads the tag's
     **shape** (milestone → announce + **skip**, exit 0 — reddening a legitimate
     announcement would train the owner to ignore reds on release runs), the assertion
     compares its **content** to the props; **the hazard survives removal of either**
     (milestone regex neutered → `v8.0` falls through to unrecognized → **RED**). The
     review built the adversarial set (`v8.0`, `v8.0.1`, `pkg/8.0`, `pkg/8.0.0`,
     `pkg/…+build`, `refs/tags/…`, whitespace, `pkg/`) — **no path from a milestone
     Release to a push**.
   - **`dotnet nuget push` has NO `--dry-run`** (verified against the live CLI) — so the
     DoD's "dry-run lane" became a **nuget.org-state preflight**: the only thing a real
     push can reject that no local step can know. Its own **vacuity trap** — all six ids
     404 today, so *every* current answer to "is it free?" is "absent" — is closed by a
     **two-arm positive control** (`newtonsoft.json` @ `13.0.3` = published, @
     `0.0.0-does-not-exist` = free), itself mutation-proven: already-published → RED;
     typo'd endpoint → RED *"HTTP 400 … not evidence the id is free"*; and **the offline
     arm proved itself for real** when the sandbox's DNS failed mid-gate.
   - **release-please: OUT — the re-evaluation reversed 8.1's REASON, not its verdict.**
     Not "too early": its payoff mechanism (**merge release PR → cut Release**) is
     verbatim what DoD #3 forbids, and its unit is the **commit** while this repo's is
     the **phase**. Reversal conditions named (component scopes **and** release cadence
     **and** draft-mode confirmed). **No CHANGELOG** — the Release body *is* the
     changelog; trigger = the **second** public release.
   - **The honesty deliverable — the PROVEN/UNPROVEN table (U1–U8), carried verbatim into
     the conclusion.** The real push cannot be tested (no key, nothing public, by
     decision), so the phase ships the arrows rather than a claim: **eight fail safe,
     SEVEN fail LOUD, and U8 (verdict propagation) is the quiet one** — an empty verdict
     **skips** `push` rather than redding it, **so the owner's first-Release check is
     "did `push` RUN?", not "was there a red?"**. **P1 is PROVEN** — run **29540566554**
     (`release / validate`, `pull_request`) **green**: the workflow runs on a real
     runner, **the paths filter SELECTS** (the one thing actionlint provably cannot
     check — a typo'd path passes green and silently narrows the lane to nothing), the
     self-test runs on GitHub's runner, and the preflight reaches nuget.org from GitHub's
     network. **The self-proving lane proved itself on its own PR.**
   - **Review: Gate 1 PASS — 3 Important, all applied.** **I-1** the P1 **ordering flaw**
     (a Gate 1 bar behind a Gate 2 deliverable — *the gates were wrong, not the
     mechanism*; fixed by opening the PR early) + the "no actionlint, no faith" wording
     that **disparaged the very evidence Gate 1 leaned on**; **I-2** U7/U8 missing from
     the table — the cross-job artifact hand-off and `needs.<job>.outputs` propagation,
     **both NOVEL to this repo**; **I-3** the hand-off's **precedent DID NOT EXIST**
     (`ci.yml` has **zero `needs:` edges**; the real precedent is `publish-so → emulator`
     in the advisory **nightly** lane). **The reviewer judged the honesty split directly
     and found it INCOMPLETE, NOT INFLATED — omissions of unproven arrows, never
     overclaims of proven ones.** The **false-belief sweep**: *"build-test uploads .so
     artifacts the shell lanes consume"* was never true — found and corrected in **four**
     places (root cause: `download-artifact` cannot read another **workflow's** run;
     **nothing consumes build-test's uploads** — they are diagnostics).
   - **Final counts:** .NET **559/0** (557 → 559; 23 + 132 + 404) · JVM **106/0**
     (untouched) · publish head **byte-identical** (no `src/`/`samples/` change, so the
     gates **could not have moved** — the review confirmed leaving them unrun is sound,
     not a shortcut) · smoke green with the new **pairing** tooth (**the counts stayed
     6/5 GREEN while Core shipped symbol-less** — pairing, not counts) · actionlint 1.7.7
     clean on all four workflows, five mutants caught (incl. `releases:`-for-`release:` =
     U1's exact shape). Device lanes untouched: **184**/**154** on prior provenance.
     **Nothing published; no tag created; no secret added.** See
     [design](../plans/2026-07-16-phase-8.2-design.md) +
     [conclusion](../plans/2026-07-16-phase-8.2-conclusion.md).
- ✅ **Phase 8.3** — the `dotnet new` template: app + Android shell (DoD #4) — *complete
  (2026-07-17)*
   - **`dotnet new blazornative`** — `templates/BlazorNative.Templates`, a real template pack
     on its **own feed** (`artifacts/templates`), **outside the shipped six** and outside the
     release path. `src/` membership is load-bearing: `PackagePurityTests` proves `src/` holds
     exactly the six, and **that proof is what licenses 8.1's props home** — a seventh csproj
     there un-licenses it, and the six's pins are **assembly-shaped** (type purity off the PE,
     a sentinel type per package, snupkg pairing) which a **content-only pack satisfies none
     of**. Joining would mean a special case in **five pinned places**, and **a special case is
     a hole in a pin**. The template's own feed is why the smoke's `6 nupkg + 5 snupkg` stays
     an *exact* count that means something.
   - **THE HEADLINE — two defects, both invisible to every pin, both found by DOING THE REAL
     THING.** **(1) The generated Android app did not compile.** `MainActivity` used a bare
     `R.layout.main` with no `import <namespace>.R`. AGP generates `R` into the `namespace`;
     Kotlin resolves a bare `R` against the **FILE's** package. **The repo's two match by
     COINCIDENCE** (`namespace = "io.blazornative.shell"` happens to equal its source package);
     **the template's differ BY DESIGN** — and that divergence **IS the byte-identity trick**
     that lets the shell's Kotlin be compared file-for-file while `namespace` is the user's app
     id. **So the trick had a hole exactly where the design didn't look.** **All nine .NET pins
     were green; only `assembleDebug` saw it** — precisely why the design chose a **real
     compile** over `gradlew tasks`. **DoD #4's own sentence ("runnable end-to-end") was unmet
     until this landed.** *Byte-identity to a reference is not correctness when the reference's
     correctness depends on a property the copy deliberately changes.* Fixed with an import
     rewritten by `finalApplicationId`'s **existing** `replaces` (**no new symbol**), chosen
     over changing the namespace **because that moves the trap into the user's code**.
     **(2) `.gitignore` was swallowing `templates/.../build/BionicNativeAot.targets`** — every
     generated app would have shipped with **no NDK shim → no bionic publish → no APK** — and
     **a byte-identity pin cannot see a file git never committed.** Found by construction; the
     second trap got right too (`template.json`'s `exclude` narrows to `**/build/**/*.jar`, not
     `**/build/**`, which would have re-swallowed it at *generation* time).
   - **THE CENTRAL PROOF — the trim line, proven on GENERATED output.** A text pin proves *the
     line is in the file*, not that the app survives ILC — and **8.0's own line was found
     missing by a PUBLISH, not by a READ**. So `scripts/template-smoke.ps1` **packs → installs
     from the NUPKG** (the real shipping path) **→ generates → restores** (temp cache,
     provenance ×3) **→ publishes → `assembleDebug`**. Green: **`IL2072: 4` · `Exports (9)` ·
     `probe 'BnStarterPage': present (3838 KB)` · `TemplateSmokeApp-debug.apk (15590 KB)`**.
     **The delete-the-line mutation reds arm 1 AND arm 3** (`IL2072: 0`, `probe: ABSENT`, 3592
     KB) — **and arm 2 STAYS GREEN**, because the 9 exports ride out of the *referenced*
     Runtime assembly and survive a whole-module trim of the app. **Arms 1–2 are facts about
     the LIBRARIES; only the probe is a fact about the USER'S CODE — that is why arm 3
     exists.** The step evaluates **all three arms before failing**: the trio is a *signature*,
     and a fail-fast at arm 1 could never have quoted arm 3.
   - **The decisions as proven:** the version is a **pinned MIRROR, not a wildcard** — the CI
     lane restores from a local feed holding **one** version, so `1.0.0-preview.*` always
     resolves and **the drift is invisible on CI**: a **vacuous pass**, 8.1's headline, **not
     twice**. **`$(AssemblyName)`** in the trim root (MSBuild does not validate a root name;
     verified on hostile names → `"Identity": "Weird.Name.With.Dots"`). The guard order flipped
     in **both** copies, behaviour-identical **verified at the mechanism**. **The FOURTH Yoga
     pin** (android=ios=ci=template=**3.2.1**) — without it a generated app lays out
     differently from **both** shells, silently. The lane lives **inside `build-test`** — **no
     fourth required check**, ~1.8 min local / ~3–5 min CI against a 45-min timeout, and
     **zero extra publishes** (`build-test` already publishes both bionic RIDs, so
     `-PappPubRoot` gets a real APK for free).
   - **Review: 1 Critical + 1 Important + 3 Minor — all fixed.** **C-1** is the headline.
     **I-1 — the shell pin was a ROSTER and did not know how many files it SHOULD pin**, proven
     live with **three mutations ALL GREEN** (roster 15→1; a new `src/` shell file the template
     never gets; **7 unrostered template files deleted** — `gradlew`, the wrapper JAR,
     `AndroidManifest.xml`, `BnStarterPage.razor`… **a `dotnet new` app missing its wrapper JAR
     is a tree whose FIRST command fails, and it would have shipped**) — violating **the file's
     OWN cited rule** ("never a roster"). **The fix pass then found the reviewer's prescribed
     fix insufficient for the reviewer's own class** (a manifest pin closes only the *deletion*
     mutation) and closed it **from three sides**: a **32-name content manifest** enumerated
     from disk and **matching the pack's own glob** (so **it IS the nupkg's inventory**), the
     shell set **DERIVED from the tree** (**15 → 19** — revealing `gradlew`/`gradlew.bat`/the
     wrapper JAR/`gradle-wrapper.properties` were byte-identical copies **nothing compared at
     all**), and the **repo-side** pin. **And the root cause the review missed: the design's
     split table filed the gradle wrapper as TEMPLATE-OWNED — that wrong row is WHY the roster
     never listed those four files. A wrong row in a table became a hole in a pin: the
     anti-roster argument, restated.** **M-1** the guard-order pin was **comment-blind** (the
     bug present + a comment naming the call → **PASSED**: *the pin was finding its own
     documentation and calling it code*); **M-2** a floor of 5 under a real count of 6 → a
     **named set** (*a count names a number and a set names the file*); **M-3** the prose
     staleness is **TWO** spots (`MainActivity.kt:33` and **`:58`, which contradicts the code
     134 lines below it**) — ledgered with the principle: **excisions we already need are free;
     new excisions are new holes**.
   - **The iOS doc** (`docs/ios-shell-setup.md`) — **its honesty is that it REFUSES to
     transcribe.** `project.yml` links against `$(SRCROOT)/vendor/…` and names no publish
     directory and no app anywhere; **the only thing that populates it today is CI** (~90 lines
     of bash in two workflow steps). A transcription would be a **fourth copy** that rots the
     day `ci.yml` moves. It points at what **`ios-build` compiles every PR** — a **required**
     check, so **the referenced material is kept true BY A GATE, not by prose discipline**.
     Three claims verified with real line numbers (`HostViewController.swift:60` hardcodes
     `"BnDemo"`, twin at `BnRuntime.swift:184`; the ATS exemption at `Info.plist:44-48` exists
     only for the fixture server) **and one correction: the design cited `AppleShellBridge.swift:102`
     — that is the doc comment; the code is `fetchBegin` at `:106`, `return -1` at `:108`.**
   - **Final counts:** .NET **570/0** (23 + 415 + 132; **559 → 568 → 570** with provenance) ·
     JVM **106/0** untouched · publish gates **re-quoted clean** after the head moved
     (`SampleAppPages.cs`): **4 IL2072, 9 exports, pages present, DLL 4217 KB — no
     re-baselining** · consumer smoke still exactly **6 nupkg + 5 snupkg** · template smoke
     **PASS** · actionlint clean · zero-warning bar holds. Device lanes untouched:
     **184**/**154** on prior provenance. **DoD #4 closes with its arrows NAMED** — **U1
     CLOSED** (`dotnet new search blazornative` → *"No templates found"*), **U3 mooted** by the
     generated-output probe; **U2** (the CI APK carries the sample's `.so`) and **U4** (no lane
     executes the iOS procedure) stand; **U6 stands and matters — proven on the CI runner, not
     on a stranger's laptop.** **Nothing published; no tag created.** See
     [design](../plans/2026-07-17-phase-8.3-design.md) +
     [conclusion](../plans/2026-07-17-phase-8.3-conclusion.md).
- ✅ **Phase 8.4** — the docs site: Docusaurus + GitHub Pages (DoD #5) — *complete (2026-07-17)*
   - **The site exists** — `website/`, Docusaurus **3.10.2**, scaffolded from the owner's **live**
     AdoNet.Async mirror: **41 pages**, `onBrokenLinks: 'throw'` **and `onBrokenAnchors: 'throw'`**,
     no versioning, no blog, no i18n, **no search** (assessed, not waved: Algolia needs a live URL
     and an application, so it **cannot be applied for until U1 closes**; ledgered with a trigger —
     >30 pages, or the owner asks). **`docs.yml` builds on PRs and deploys on main, and is
     ADVISORY** — the three required gates keep their names and contexts; a fourth required check is
     a branch-protection change DoD #5 does not ask for.
   - **THE HEADLINE — a phase about stale copies that KEPT CATCHING ITSELF.** **(1) The generator
     lied during design.** xmldoc2md against `bin/` → **`Generation: 10 succeeded, 0 failed` — exit
     0, green — and ZERO components.** `Microsoft.AspNetCore.Components.dll` is not beside the
     assembly → `ComponentBase` will not resolve → **every derived type drops SILENTLY**. Against a
     **publish** output: **26**. ***10 vs 26 is invisible to anything that does not count*** — the
     fourth arrival of 8.1's `return ,$names`, 8.1 I-3's blind scanner and 8.3 I-1's roster, by a
     new door. **It is not a risk that was mitigated; it is a defect that already happened, on this
     machine, during design — found by counting.** **(2) Pin 2 read as ON and was OFF, TWICE:**
     `src/Directory.Build.props` imports **before** the project body, so
     `{BnEnforceDocCoverage:true, NoWarn:…;CS1591}` — **true and suppressed anyway** (fixed with
     `src/Directory.Build.targets`); then **STILL not a pin**, because `ci.yml` builds without
     `-warnaserror` and nothing set `TreatWarningsAsErrors` — CS1591 would have been **8 warnings in
     a lane that exits 0**. ***A pin's costume on a pin's absence.*** **(3) Pin 2's guarantee covered
     exactly HALF the parameter surface** (review S1-1): **the Razor generator emits `#pragma warning
     disable 1591` at line 3 of every `*_razor.g.cs`**, and `@code`-block `[Parameter]`s live inside
     it → **CS1591 is structurally blind**. Deleting `BnSlider.Value`'s summary → **0 errors, 4/4
     pins green, and the property `@bind-Value` targets GONE from the shipped XML**. Closed by
     `EveryParameter_CarriesADocComment`. **(4) `onBrokenAnchors` defaults to `'warn'`** — anchors are
     **half** the internal-link surface, and **the reference is GENERATED from `<see cref>`→anchor
     links**, so the site's largest anchor body is machine-written from doc comments nobody
     proofreads; **Gate 2 fixed five at the source cref and nothing kept them fixed**. Now `'throw'`
     (Docusaurus's own default carries `// TODO Docusaurus v4: change to throw` — **v4's behaviour
     adopted early rather than inherited late**).
   - **THE COUNT THAT WAS TRUE OF SOMETHING ELSE — the phase's emblem.** **"21" was TRUE** as *21
     documented types* (27 − 6 headless razor components) **before Gate 2's editorial pass**. It was
     then **transplanted into three files as "21 components"**, where **it was never true of
     anything**. Measured: **15 concrete `ComponentBase` types, 26 public types, 196 `[Parameter]`
     properties** (`ci.yml`'s own recorded Pin 1 mutation says *"MISSING 15 OF 15 COMPONENTS"*, which
     is how we know). **The original measurement did not rot — it was COPIED OUT OF ITS MEANING**,
     the exact failure mode the design is built around, landing on the design. **And "192" was wrong
     too:** it came from `grep '[Parameter]'`, which **cannot see `[Parameter, EditorRequired]`**
     (BnList declares **four**; the review read BnList as **2**, the assembly says **6**). **The fix
     pass's pin DERIVED 196 on its first run, because it reflects instead of grepping** — and **the
     review had reproduced the design's number and reconciled to it, inheriting the same blind
     pattern**. The blind half is **exactly 50% (98/98)**, not the 51% computed. *A number is not
     measured until the thing that reads it can see every form it takes.*
   - **THE DocFX ANSWER, as the owner asked for it.** **DocFX genuinely wins the free API
     reference** — native .NET, ingests assemblies + XML docs, no glue, auto cross-linking; **for a
     repo whose reference surface IS .NET types that is the most relevant feature either tool has,
     and Docusaurus lacks it at any price.** **Docusaurus wins that the owner already runs it** (one
     toolchain, one CI shape, one thing to upgrade) **and a docs site is 80% prose**. **What
     MEASURING changed, and why it is not close:** the XML docs were **already COMPLETE** (8 gaps,
     all `BuildRenderTree`) — **so nothing was traded away; 8.1's premise that "doc coverage is
     8.4's editorial work" was WRONG. Coverage was done; VOICE was the job.** And their content was
     **repo-voiced**: `BnTheme` cited *"Phase 3.4 design §4"*; `BnView` said *"since Phase 3.4 Gate
     1"*, *"the BnDemo goldens stay byte-identical"* and *"Razor awaits .razor compilation (M6)"* —
     **which was not merely dated but FALSE**: `<BnInput @bind-Value="_text" />` is live in
     **`BnDemo.razor:51`**, so four component pages would have told strangers the framework cannot do
     what the sample app does on line 51. **DocFX would have published that phase history exactly as
     faithfully** — a generator is a printing press. **Both tools needed the same editorial pass on
     the same XML. DocFX saves the PLUMBING, not the WORK** — and against a toolchain the owner
     already knows, **plumbing is the cheaper thing to give up.**
   - **THE ONE-HOME RULE AS SHIPPED — GENERATED / LINKED / OWNED.** ***"Kept in sync is not a
     home."*** **The site NEVER includes — it POINTS.** Docusaurus *can* read outside its tree
     (`docs.path`), so INCLUDE was available and **rejected**: it makes the site's build depend on the
     shape of contributor docs and produces a second *rendering* of a doc written to be read in the
     repo — **it buys nothing a link does not**. **8.3's iOS-doc precedent, generalized: *prose a gate
     does not keep true should POINT, not COPY*.** **The component reference is GENERATED**
     (`scripts/generate-reference.ps1` → xmldoc2md against a **publish** output → **27 MDX** into a
     `.gitignore`d dir) **and never committed** — the `///` next to the code stays the one home; **if
     the copy need not exist, the pin for it need not exist**. `docs/plans/` links **as a class**; the
     parity page **states the claim and links** 6.3 §parity + `ShellFrameTableDriftTests` rather than
     reproducing the 10-row table. **Reviewer-audited: the site states NO number a gate owns** — no
     counts, no versions, **no Yoga literal**. *A number on a page nobody re-runs is a number that is
     already wrong.*
   - **GATE 1's BEST CATCH — a permanent 404 INSIDE a package.** The design named **ONE** inbound link
     to the moved docs; **there were EIGHT**. The other seven are **`helpLinkUri` values in the
     analyzer sources** — *the string a consumer's IDE opens from a BN squiggle*. **Deleting
     `docs/analyzers.md` per the fates table would have shipped a permanent 404 to every user of every
     BN rule, at the moment they are already confused.** Re-pointed — and now **PINNED**
     (`AnalyzerHelpLinkDriftTests`, review S2-2). They were correct; **nothing kept them correct**
     (`grep -rln helpLinkUri tests/` returned **nothing**). **They ship INSIDE a nupkg and are
     absolute external URLs: the ONE link class a consumer's IDE resolves that no site build can ever
     see** — `onBrokenLinks`/`onBrokenAnchors` check the links the *site contains*, not the ones
     pointed *at* it from a released package, and **a wrong one is a permanent 404 for everyone who
     installed that version; redeploying the site cannot fix it**. **Both sides derived** (subjects
     reflected from `SupportedDiagnostics`; anchors parsed from the page **with code fences skipped**
     — it opens with `#pragma warning disable BN0004` inside one, which a naive `/^#/` parser mints an
     anchor for; the base composed from the site's own `url` + `baseUrl` + `routeBasePath` + the file's
     name, so **not one segment is transcribed**). **Its `baseUrl` mutation reds all seven at once — U2
     from the package side.**
   - **The design-corrections** (the review judged **15 of 16** right — the corrections are the
     record): no `prism-razor.js` (it is **`prism-cshtml.js`**; razor is an alias); **Gate 1 cannot own
     the sidebar** (the autogenerated category needs Gate 2's generated dir); **`generate-reference.ps1`
     has exactly TWO callers** — the site's `prebuild` and Pin 1 — ***"had the lane and the pin run
     different pipelines, the pin would be green while the lane went blind"*** (**the review called this
     the strongest architectural call in the phase**); the script **asserts NOTHING about
     completeness**, so Pin 1's mutation proves **Pin 1**, not a guard; **GitHub Actions has no YAML
     anchors** (the two filters are duplicated by construction, kept identical); **`format: 'detect'`**
     (MDX rejects the generator's `<br>`); **the paths filter must be wider than `website/**`**
     (+`Directory.Build.props`/`.targets` — ***"they decide whether the XML exists at all"***);
     **Pin 3 scans the PUBLIC SURFACE, not the file** (the shipped XML also documents internal types —
     `BnItemsJson`, `BnListWindow.Compute`, `BnPicker.Clamp` — that the generator drops; **a whole-file
     regex would red on maintainer docs doing their job and *teach the next author to delete
     engineering truth to go green***); **Pin 3's own mutation CORRECTED the pin** (`/\bgolden\b/`
     **cannot match "goldens"**, the plural the repo writes — 3 of 4 fired; the boundary is gone).
     **THE 16th: the design asked for an IMPOSSIBLE mutation** (*"rename a component ⇒ Pin 1 red both
     ways"*) — **Pin 1 derives BOTH sides**, so a rename moves the type and its expectation together:
     **green, correctly, and by design**, exactly as its own doc comment claims. Satisfying it would
     need **the hand-maintained roster 8.3's I-1 forbids**. ***STRUCK, with the reason. The mutation was
     mis-specified, not the pin.***
   - **Review: PASS with 2 Important + 8 lesser — ALL applied, none deferred. And THREE PRESCRIPTIONS
     THE FIX PASS REFUSED, CORRECTLY** (worth recording: **the reviewer is strong and was still wrong
     three times**): **reusing `PublicSurfaceDocs()` for S1-1 would have reddened 124
     correctly-documented properties** — it returns `member.Value`, and **an inheritdoc-only member's
     Value is the EMPTY STRING**, indistinguishable from undocumented (**124 of the 196 are documented
     that way**, overwhelmingly BnView's flex vocabulary re-exposed — demanding a hand-written summary
     each would demand **the 124 copies this repo exists to refuse**); **`navbar.logo.to` is invalid** —
     **the build refuses it** (*"navbar.logo.to is not allowed"*): `to` is for navbar **items**, the
     logo schema takes `href` only, so **the diagnosis was right and `href: '/'` is the remedy, and the
     build is the pin that said so**; and **the "192" reconciliation**. **S2-3** also found **two more
     baseUrl copies inside the file the one-home rule is about** — the navbar logo's hardcoded
     `href: '/BlazorNative/'` **WORKED, and only by luck** (`addBaseUrl` skips prefixing when the url
     already `startsWith(baseUrl)`): **move the site and that guard stops matching** and the logo
     becomes `/Foo/BlazorNative/` and 404s. **U2's blast radius, from inside the config.**
   - **THE SEVENTH PACKAGE** (Gate 3's find — **no ledger had noticed**): 8.1 declared
     `src/Directory.Build.props` **the ONE metadata home**; **8.3 then created
     `templates/BlazorNative.Templates` OUTSIDE `src/` DELIBERATELY** — a seventh csproj inside
     **un-licenses** the props (**the shipped-set pin is what makes that home legal**) — so it carries
     its own `PackageProjectUrl` **by necessity**, and it was **still pointing at the repo while the six
     moved to the site**. ***Shipping 6-of-7 is not a rule, it is a coincidence waiting to be noticed by
     a user.*** Split the same way Gate 3 split the six: **`RepositoryUrl` = where the source is;
     `PackageProjectUrl` = where the docs are** — **verified in the PACKED nuspec rather than the
     csproj, since the pack is what a user gets** (a props edit that never reached a package would have
     read as done). **Also Gate 3's:** the footer label **`Source: <repo>`** re-pointed at a docs site
     would read ***"Source: a site that is not the source"*** — **a fact made wrong by the act of fixing
     it**, this phase's own failure class arriving through the door marked "mechanical". Now **`Docs:`**;
     nothing is lost — **nuget.org still renders Source from the nuspec's `<repository>`**, which the
     smoke already interrogates as `repository@commit`.
   - **FOUND ONLY BECAUSE SOMETHING FINALLY LOOKED:** the social-card SVG's tagline **overflowed its own
     1200px viewBox**, clipping the final "s" from "views" — SVG text does not wrap, and **nothing had
     ever rendered the file**, so **nothing had ever shown it**. And **`og:image` as an SVG renders NO
     preview on any social platform** (X, Facebook, LinkedIn, Slack — **all require a raster**): a link
     posted as a bare URL would have unfurled *nothing*, **with the tag present and the file resolving
     200**. **Decision 6 had already learned this for NuGet ("JPEG/PNG only, SVG unsupported") and it
     did not transfer.** Both fixed; **the card is a PNG**, the SVG stays as the vector source.
   - **The Gate 3 sweep — and the design was the one that was wrong.** The four counts
     (333/83/111/72 → **577/106/184/154**) were taken **from their own gates, not from the design**,
     which cited `ci.yml:1016` and `ci.yml:1273`; **the gates are at `:1076` and `:1333`**. ***Two of
     four line numbers rotted within a day of being written, in the document arguing that copies
     rot.*** The table now cites **WORKFLOW → JOB** — stable and greppable — and **never a line
     number**, **and it says it is a copy**. `GITHUB-SETUP.md`'s Pages section **refused to transcribe
     the paths filter**: the draft was **WRONG BEFORE IT WAS SAVED** (five entries against the seven
     `docs.yml` carries) — *a doc misquoting the filter it exists to explain, in the section about
     stale copies* — so it explains **why** the filter is wide and **sends the reader to `docs.yml`**.
     **Falsified by M7, found by reading the CODE rather than the ROADMAP:** *"Placeholder/OnError/
     ContentMode — each gets its own design in M7"* (**all three SHIPPED in 7.5**) and *"no onScroll…
     it needs its own design"* (**7.2**); two stale demo-page rosters **DELETED, not re-typed** — their
     home is `SampleAppPages.cs`, and *re-typing nine names that would be ten next phase is the defect,
     not the fix*.
   - **Final counts:** .NET **577/0** (Renderer **132** + Analyzers **25** + Runtime **420**;
     **570 → 574 → 577** with provenance; re-run at close: 25 / 132 / 420, 0 failed / 0 skipped) ·
     JVM **106/0** untouched · **device lanes untouched** (**184**/**154** on prior provenance — **no
     wire/ABI/golden/shell change**) · **publish gates unmoved STRUCTURALLY, argued not re-quoted**
     (`Directory.Build.props`/`.targets` exist **ONLY under `src/`**; the publish head is `samples/`,
     so **MSBuild's upward search finds nothing** — the XML docs, the CS1591 flip and the icon metadata
     never reach it; 4 IL2072 / 9 exports stand) · **smoke PASS: 6 nupkg + 5 snupkg**, zero pack
     warnings, nuspec + inventory ✓ ×6, **`icon.png` at the package ROOT**, and **it printed the
     packaged link live** (`warning BN0011: … /docs/analyzers#bn0011`) · **fresh-clone `npm run
     build`** (generated dir + `.docusaurus` + `build` deleted): `Generation: 26 succeeded, 0 failed` →
     **27 files** → **[SUCCESS], 41 pages, 0 warnings, 0 broken links, 0 broken anchors**, exactly
     **one** baseUrl-aware `og:image` · **actionlint 1.7.7 clean, all workflows** · **the icon: pack IS
     the pin, verified by RUNNING it** — **`error NU5019: File not found`, exit 1**, ***not NU5046 as
     three places claimed***: the `<None>` carries **no `Exists()` guard**, so the **item-level**
     failure fires before pack reaches the check NU5046 names (the load-bearing claim — *the pack is
     the pin* — is **VERIFIED TRUE**; only the code was wrong).
   - **DoD #5 closes with its arrows NAMED (U1–U7).** **U1/U2 stand and matter: NOTHING RENDERS until
     the owner clicks Settings → Pages → Source: GitHub Actions** (`has_pages: false` today), **and U2
     is THE QUIET ARROW — a wrong `baseUrl` builds GREEN, deploys GREEN, reports success, and serves a
     STYLELESS site; no local build and no workflow can see it. The owner's first check is a LOOK, not
     a red.** Every re-pointed link (**7 `helpLinkUri` + the twelve README links + the template's**)
     **404s until that click — bounded** (nothing publishes without the owner's Release; the interval is
     one setting long) **and strictly better than the old targets, which 404 FOREVER**; **the root
     README's Documentation link is the ONE exposure that argument does not cover** — the repo is
     public — **named rather than smuggled**. **U3 stands: Pin 3 catches phase history; it cannot catch
     dull, wrong-level or unhelpful — taste is unpinnable**, and **the review found it live**
     (`BnImage.cs:291`'s maintainer parenthetical shipping to a public page, carrying no banned word;
     cut). **U7** (the card's unfurl) is **decision 6's lesson arriving a second time**. **Nothing
     published; no tag created; no secret added — this phase publishes a website and nothing else, and
     does not publish that until one setting is clicked.** See
     [design](../plans/2026-07-17-phase-8.4-design.md) +
     [conclusion](../plans/2026-07-17-phase-8.4-conclusion.md).
- ✅ **Phase 8.5** — hygiene + M8 final audit + close (DoD #6) — *complete (2026-07-17)* — *(this
  line planned `→ v8.0`; Phase 8.6 cancelled the tag — see the retirement note at the top of this
  file and the [M8 audit addendum](../plans/2026-07-17-milestone-8-audit-addendum.md))*
   - **ALL SIX DoD PASS — [final audit](../plans/2026-07-17-milestone-8-final-audit.md)**, every row
     **re-verified LIVE at the tip, not cited** (the M7 audit's method, verbatim): .NET **580/0**
     (132 + 25 + 423) · JVM **106/0** across 19 suites via `--rerun-tasks` (**27 executed — a cached
     green is not a re-run**) · publish gate **4 IL2072 + 9 exports off dumpbin + the page probe** on
     a CLEAN ILC pass (DLL **4217 KB**, unmoved) · smoke **6 nupkg + 5 snupkg** at the one version
     literal, `repository@commit` at the tip · reference **`Generation: 26 succeeded`**, components
     present · `release-preflight -SelfTest` **8/8, exit 0** · template's **generated output** clean
     + **APK 15590 KB** (run 29554045138) · **Yoga 3.2.1 × FOUR pins** printed equal by the required
     lane · branch protection == exactly the three contexts · **iOS 154/0 AT `6afd8af`** (run
     **29554540199**) — main's exact tip.
   - **THE ABI DID NOT MOVE AND NEITHER DID THE SHELLS' SOURCE.** `git diff v7.0..HEAD` over both
     shell trees touches **zero** `.kt`/`.swift`/`.h`/`.mm` — only `project.yml` (**comment-only**)
     and `build.gradle.kts` (8.0's publish-head retarget). **The device baselines therefore stand on
     8.0's provenance, not v7.0's** — which is why 8.0 re-ran both. **M8 added no wire vocabulary at
     all**; 9 exports / 72-byte bridge / thirteen NodeTypes verified on all three mirrors.
   - **THE HONESTY ROWS.** **DoD #2 said five packages; SIX ship** (Http — shorthand drift, corrected
     8.1), **plus a SEVENTH** pack 8.1's one-metadata-home rule **structurally cannot reach** — the
     rule's true scope is **one home for the six; the seventh agrees by pin-less discipline**.
     **DoD #3 says "dry-run"; `dotnet nuget push` HAS no `--dry-run`** — a state preflight shipped
     instead: *the DoD's word is wrong; the thing is better than the word*. **DoD #5's "~20
     components" counts nothing** — re-measured: **15 / 26 / 196**, and **the blind grep reproduces
     at exactly 192**.
   - **THE HYGIENE LEDGER, ITEM BY ITEM.** **DONE** — the README's four counts + the Yoga literal are
     **gate-held** (`ReadmeDriftTests`, +3, required lane; both sides derived **from the `if`
     CONDITION, never the step's `name:` prose — this file's own header read "92 JVM" for four
     milestones**; the roster knows its size, 8.3's I-1; five mutations run and quoted). ⚠ **The
     literal-deleted mutation went GREEN on its first run: the paragraph explaining that the number
     has ONE home had named the number.** A third copy minted inside the sentence about not copying —
     **8.4's Gate 3 author did the identical thing.** **DEFERRED** — the KDoc sweep + map extraction
     (**one item**; the clean fix retires 8.3's brittle excision and costs a Kotlin change + a **184
     device re-run**; **trigger: before the first Release that publishes the template pack** — U5
     bounds it, the pack is not on nuget.org); `BionicNativeAot.targets` → Runtime's `build/` (costs
     the smoke's inventory tooth). **ACCEPTED, PREMISE FALSE** — 8.4's *"~15-min local Runtime lane"*
     measures **4 s cold**; the 15 min was the solution BUILD. ***A cost never measured, carried into
     a ledger, about to justify weakening a pin — this milestone's own failure mode arriving in a
     document about it.***
   - **U1 OBSERVED, NOT PREDICTED:** `/pages` → **404**; `Deploy Documentation` at main's tip is
     **`build: success` / `deploy: failure`** with the exact legible error 8.4 promised. **Nothing
     renders until the owner clicks Settings → Pages → Source: GitHub Actions**, and **U2 is the
     quiet arrow — the first check is a LOOK, not a red.**
   - **`v8.0` PUBLISHES NOTHING** — re-run live, the classifier reads it as a milestone tag and
     **announces + skips, exit 0**; a `pkg/<semver>` Release is the only publishing shape. **Nothing
     published; no tag created; no secret added.** See
     [final audit](../plans/2026-07-17-milestone-8-final-audit.md).
     > *(Superseded on the same day, and left standing as the record of what 8.5 measured:
     > **Phase 8.6 inverted this arm — `v8.0` is now RED, not announce-and-skip**, and the
     > `pkg/<semver>` namespace it names as "the only publishing shape" was itself retired in
     > favour of `v<semver>`. The self-test that was **8/8 with `v8.0 → SKIP`** is now **9/9
     > with `v8.0 → RED`**. **8.5's measurement was correct against the machine it measured**;
     > 8.6 changed the machine. See the
     > [M8 audit addendum](../plans/2026-07-17-milestone-8-audit-addendum.md).)*
- ✅ **Phase 8.6** — the release automation — *complete (2026-07-17, **after** M8's close; closes no
  DoD — it succeeds the audit rather than re-opening it)*
   - **release-please authors the version; the seven milestone tags are DELETED.** The
     **manifest** (`.release-please-manifest.json`) is the only file into which a version is ever
     *decided*; `src/Directory.Build.props`' `<Version>` is its **first mirror** and stays the
     build's only truth. **One author, six mirrors, seven literals, four files** — 8.1's "ONE
     literal" was always "one literal in `src/`". `release-please.yml` has **one job and it opens a
     PR**: no publish job, no `needs:`, and it never mentions the key (**the reference's own
     release-please.yml pushes six packages on merge with no click — copying it destroys DoD #3's
     law in one paste**; `ReleaseWorkflowPinTests`, written in 8.2 for another reason, already reds
     on it).
   - **THE HEADLINE — 8.2 wrote this phase's test in advance, and the owner waived it.** 8.2
     rejected release-please and named **three** conditions that would reverse it, *all three must
     hold*: (1) commit convention → component scopes — **holds**; (2) releases frequent enough that
     hand-writing the body is a chore — ❌ **does NOT hold, measured: `gh api …/releases` → 0.
     Nobody has hand-written one, ever**; (3) draft mode preserves the manual go — **holds, proven
     here**. **The owner waives #2. That is a VALUATION, not a discovery** — 8.2 named this exact
     config (`draft: true`) and judged its value insufficient against a measurement still true
     today. *A reversal that pretends to be a discovery is flattery.*
   - **What release-please actually buys — NOT the version.** Traced through `prerelease.ts` **and
     executed**: at `1.0.0-preview.1`, `fix:`/`feat:`/`feat!:` **all** produce `1.0.0-preview.2`.
     `isPreMajor = major < 1`, so both of the reference's `*-pre-major` flags are **dead no-ops** at
     major=1 — **and at the reference's own 1.3.3**, so mirroring it faithfully imports two no-ops
     (omitted: *config that cannot fire is a comment pretending to be a decision*). **The version is
     a COUNTER; the commits compute the CHANGELOG.** ⚠ **The graduation trap:** `Release-As: 1.0.0`
     needs a **config change too**, or **all three** commit types silently re-enter preview
     (`1.0.1-preview`/`1.1.0-preview`/`2.0.0-preview` — the design named only `fix:`; Gate 1
     corrected it by executing). **Nothing warns; every pin stays green while the version goes
     backwards.** Sited at `GITHUB-SETUP.md`'s graduation section, **where the person typing
     `Release-As` is standing**.
   - **P10 PROVEN — the phase's one belief became an observation.** Draft created (`draft:true`,
     `published_at:null`) → `release.yml` runs **byte-identical** before/after; **runs ever
     triggered by `release`: 0**. **Bonus: `--cleanup-tag` → "Reference does not exist" — a draft
     does not even CREATE the tag**, so it cannot fire a tag-push trigger either. ⚠ **And Gate 2 did
     this with the key LIVE** (`NUGET_API_KEY` set 06:27:40Z, mid-phase): **the "no key ⇒ no
     publish" backstop was gone, so it PROVED the guards instead of leaning on the key's absence** —
     the probe used a deliberately non-publishing shape whose failure mode is **the classifier doing
     its job** (`class: unrecognized`, exit 1, *"Nothing was published."*).
   - **The dry-run caught a crash Gate 1 would have shipped.** A **bare path** in `extra-files` lets
     the **FILE EXTENSION** pick the updater — `.json` → `CompositeUpdater(GenericJson, Generic)` →
     strict `JSON.parse` → **threw at line 48 col 42 of `template.json`, exactly where the
     annotation comment starts.** ***release-please would have crashed on EVERY run — no release PR,
     ever.*** The other three files reached the right updater **by accident of their extensions**.
     **Decision 3's "one updater kind everywhere" was true for three by luck and false for the
     fourth**; the object form (`{"type":"generic","path":…}`) makes it true **by construction**,
     applied at all four (*depending on a third-party tool's ignorance is not a design*). Pinned by
     a test carrying **no copy of release-please's dispatch ladder** — *a third party's table
     drifts*.
   - ⚠ **THE LINE WRAP DECLARED A BREAKING CHANGE.** The dry-run's changelog emitted
     `### ⚠ BREAKING CHANGES / * release: footers.` — **`004179f`'s body, hard-wrapped at 80 chars,
     pushed the token to column 0 in front of the word "footers."** **Decision 5's premise is HALF
     true: subjects are discarded under squash; BODIES ARE NOT** — they concatenate (the setting is
     `COMMIT_MESSAGES`) and release-please parses them. A **release-as** token at column 0 in the
     **last paragraph** → `Version.parse` → ***that becomes the released version***. So *"never a
     wrong version"* is **exact about the subject, not the body**.
   - **The footer check** (`scripts/footer-check.ps1`): **a PR's commit BODIES may declare a release
     footer only if the PR's TITLE declares the same thing** — it asserts **subject/body AGREEMENT,
     not intent**. **No label**, and the reason is the failure itself: ***a label is a human
     attesting to their own accident, and `004179f`'s author would have applied one without
     hesitating.*** ⚠ **"Column 0" is measurably the WRONG boundary** — indentation, tabs, `* `
     bullets, **lowercase** and **a missing colon** all still produce a note; **the naive check
     passes four real hazards** (the brief and the design both said column-0; Gate 3 measured
     against the real parser and corrected it). **It catches `004179f` on the real commit** (*"body
     line 28 … exit=1"*), **reds exactly one of the branch's 12 commits**, and **Gate 2's commit —
     which discusses these tokens at length — passes**. ***Then it caught its own author***: Gate
     3's final commit tripped it, **in the commit whose body claimed its bodies were clean**; fixed
     as the RED prescribes (re-wrap; never touch the title). **Self-test 14/14.**
   - **The classifier: SKIP → RED.** 8.2's SKIP rested on **one** premise — *a milestone Release is
     legitimate* — **and this phase deletes that premise.** 8 rows → **9** (1 PUBLISH, **8 RED, 0
     SKIP**); `skip` leaves the vocabulary, so `release.yml`'s `verdict != 'skip'` guard is **deleted
     rather than rewritten**. **M3′ proves the guard-count is real, not arithmetic: with SemVer
     validation neutered AND the legacy arm deleted, `v8.0` is STILL RED** — guard 3 holding alone.
     **`v8.0`'s defence went from 2 guards to 4.** *Converting arm 1 from SKIP to RED does not remove
     a guard; it makes the first one louder.*
   - **The tags are gone, and the machine did not notice.** `git push origin --delete v1.0 … v7.0` +
     the locals, on the owner's **fresh go** at the moment it would happen. **Both tag lists are now
     empty**; `git checkout v6.0` fails, and **the standing note at the top of this file is the named
     place that explains it**. ***Not one line of the classifier changed*** — it reds on a tag's
     **shape**, never on a tag list, which is the retirement-vs-emptying claim proving itself.
   - **The sweep's honesty.** The design's `33` was wrong twice **and its pattern was wrong in BOTH
     directions**: `v[1-7]\.0` counts `v1.0.0-preview.2` — ***the one shape that publishes*** — as a
     milestone tag; the obvious fix **under-counts** `git diff v7.0..HEAD`, a real reference. Precise:
     **35 → 29**. ⚠ **"Re-grep to zero" is the WRONG BAR** — unreachable and self-contradictory
     (**decision 7 MANDATES a note spelling `v1.0`–`v7.0` in the doc its Gate row forbids it**; zero
     would delete the note whose job is explaining why `git checkout v6.0` fails). **The bar worked
     to: ZERO EXISTENCE CLAIMS.** History keeps *"tagged `v6.0`"* **governed by the note** rather than
     rewritten — `docs/plans`' rule applied to a live doc. ⚠ **And the addendum's own deletion
     checklist was WRONG BY ONE**: it dropped `release-preflight.ps1:537` — **the site Gate 3 called
     "the sharpest"** — whose text carried a live existence claim **inside the self-test table that is
     the classifier's proof**. ***A list of what to change is a copy, and it rots the same way***;
     caught only because the close re-grepped instead of reading the roster. **`docs/plans`: zero
     pre-existing files touched across the whole branch.**
   - **The rider (8.3's defect, not 8.6's).** The template pin enumerated **disk** files, not
     **git-tracked** ones — and its message said *"add it to the manifest"*, ***so a developer
     following it would have added Gradle caches to the manifest***. **Worse, the pack shipped
     them**: **45 entries → 8 `.gradle` files rode**; `dotnet new` handed a stranger lock files from
     the packer's laptop. **After: 37 entries, 32 under `content/` = exactly tracked, zero junk** —
     **with the caches still on disk: the pin was fixed, not the tree**. The Exclude is **surgical**
     (`NoDefaultExcludes` respected — the wrapper still ships; `BionicNativeAot.targets` still ships)
     and **the pin now PARSES the Exclude, so pack and pin cannot disagree**.
   - **The review trail — PASS with 2 Important, both applied.** **S1-1:** the pin whose stated job
     is auditing the machine ***would never run on the machine's PR*** — GitHub creates
     approval-required runs for `GITHUB_TOKEN`-opened PRs, and **three shipped comments said the
     opposite**. The owner chose **keep `GITHUB_TOKEN` + name the approve-click** over a PAT (a second
     repo-scoped secret that expires and rotates, **against the one-secret law**); the click is itself
     a human gate. **New `U8`** records the shape U1 missed — ***the PR appears and its checks never
     start*** — which **fails by STALLING, so it is safe**. **S1-2:** the classifier's own failure
     message **instructed the ritual it REDs** (*"bump the props… tag `pkg/…`"*) — **both halves
     wrong**; the **duplicated** console/summary prose is **why it went stale twice**, now one
     `$remedy`.
   - **Evidence:** .NET **582/0** (25 + 132 + 425) · JVM **106** · device lanes **untouched**
     (184/154) · publish gates **unmoved** · `release-preflight -SelfTest` **9/9** · `footer-check
     -SelfTest` **14/14** · template-smoke **PASS** (31 files, 4 IL2072, 9 exports, APK 15590 KB) ·
     `actionlint` clean on **7** workflows · required contexts read back exactly
     `["build-test","ios-build","android-build"]` (**commitlint's two are OWED — sequenced AFTER this
     merge: the file must be on main first or it wedges every PR, the 7.x lesson**) · **0 releases, 0
     release-triggered runs — with the key LIVE**. See
     [design](../plans/2026-07-17-phase-8.6-design.md) +
     [conclusion](../plans/2026-07-17-phase-8.6-conclusion.md) +
     [M8 audit addendum](../plans/2026-07-17-milestone-8-audit-addendum.md).

---

### ✅ Milestone 9 — Host APIs (Platform Breadth)  *(complete 2026-07-18)*

The bridge grows four real capabilities via the
[bridge-extension pattern](../bridge-extension.md): **geolocation, local notifications,
biometrics + secure storage (the M5 deferral), and camera photo capture** — each on both
shells, each with the permission story the bridge had never carried (clipboard, the 5.4
growth, needed none: that's the milestone's named risk, proven first on geolocation in
9.0 — where the ABI grew a second time, generically, so the remaining phases add an op
constant and no more).
Scoped at milestone-open (owner decisions in [MILESTONE.md](MILESTONE.md)):
**real-device iOS is DEFERRED** — no Apple Developer account for now; with no local Mac
the honest path is CI-signed IPA → TestFlight, and that trigger is named. **FCM push
stays ledgered** (needs a Firebase project). The **owner's physical Android phone** is
the honesty check for what emulators only simulate (camera, biometrics); CI stays on the
emulator lanes. The inspector channel is ledgered a third time. Maps to BACKLOG.md
"P4 — full platform coverage" (remainder).

- ✅ **Phase 9.0** — the permission pattern + geolocation (DoD #1, #2) — *complete (2026-07-17)*
   - **THE HEADLINE — the ABI grew for the first time since Phase 3.1, deliberately and
     argued.** An honest async-permission completion could ride **no** existing export:
     `fetch_complete` is **fetch-typed**; `host_event` is **contractually synchronous** (it
     drives `StateHasChanged` inline, a permission result arrives on a background thread after
     an arbitrary suspension); polling re-invents a push the bridge already has. So the bridge
     grew **72 → 80 bytes** (+1 `HostCallBegin` slot at **offset 72**, the `FetchBegin` twin —
     op-enum + flat-JSON args) and **9 → 10 exports** (+`blazornative_host_call_complete`, the
     `fetch_complete` twin — `requestId` + `int status` + optional flat-JSON payload; contract
     **0 delivered / 1 unknown-benign / 2 failure**, never throws across the ABI). **Shaped
     GENERICALLY** so 9.1/9.2/9.3 add an op constant + a host handler with **ZERO** further
     struct/export/gate/drift change — *pay once, reuse thrice*, and the Gate 1 review verified
     it holds (no geolocation specifics in the struct or export; even request/check `mode`
     rides the JSON). **Every hard-coded-9 site moved in lockstep** — `ci.yml` ×4 RID arrays,
     `ios.yml`, `android-instrumented.yml`, template-smoke, `BootSmokeNativeTest` (9 → 10), the
     three-mirror drift pin (72 → 80 on .NET/Kotlin/Swift). A 72-byte shell against the 80-byte
     runtime fails **LOUDLY** for the new capability only (`NotSupportedException` via the
     `RequireSlot` guard), never a silent misread — tested.
   - **The permission model, proven (DoD #1).** **Denial is DATA — a status integer, never an
     exception, never a hang** (the milestone's law, tested on both shells within a bounded
     await). The status is a **wire-mirrored 6-value enum** (`Granted` / `Denied` /
     `DeniedPermanently` / `Restricted` / `LocationUnavailable` / `Error`, 0..5) across
     .NET/Kotlin/Swift; a non-`Granted` status carries a **null payload and never parses**. The
     pending-call registry (`s_pendingHostCalls`) is the `s_pendingFetches` twin: process-scoped,
     `Interlocked` id, `RunContinuationsAsynchronously`, CT → `TrySetCanceled`, unknown-id benign.
     **The named risk — surviving the OS suspending the app — is PROVEN:** Android recreates the
     Activity mid-prompt (a **static** `requestCode → requestId` map survives + the process-scoped
     .NET registry) and the result routes to the **same** in-flight continuation (rc 0); iOS's
     async `CLLocationManager` authorization routes by `requestId` the same way (the delegate is a
     **weak** ref — the 5.3/7.3 retention lesson restated for CoreLocation, so the shell holds the
     handler for the app lifetime). `docs/bridge-extension.md` grew **section (f)** as the pattern
     9.1/9.2/9.3 copy.
   - **Geolocation (DoD #2).** `IGeolocation` in the new **7th package `BlazorNative.Device`** over
     `IMobileBridge.GetCurrentPositionAsync` in **Core** (so `DevHostBridge` mocks the tri-state
     headless); both shells (Android `LocationManager` + `requestPermissions`; iOS
     `CLLocationManager` when-in-use, `NSLocationWhenInUseUsageDescription`); `/geolocation` demo
     (`BnGeolocationDemo`, the 10th routed page). Fix key set pinned exactly — `lat / lng /
     accuracy / altitude / timestamp` (`lng`, not `lon`).
   - **A milestone-first:** the **first off-lane async TCS completion the iOS shell has ever done**
     (fetch's stub fails synchronously; `host_event` is undeclared on iOS). The grown ABI exercised
     a path the shell didn't previously have — a ThreadPool continuation renders, the frame hops to
     main via the existing off-lane `CommitFrame`. It works (the two BOOT tests).
   - **Counts (all CI-asserted):** .NET **616/0** (132 + 25 + 459; **583 → 616**, +33) · JVM
     **106/0** (the moved 80-byte struct pin + `BridgeHostCallCompleter`; `BootSmokeNativeTest`
     9 → 10) · Android instrumented **188/0** (**184 → 188**, +4 on the local AVD: real fix
     round-trip + denial-no-hang + `scenario.recreate()` survival) · iOS XCTest **169/0** (**154 →
     169**, +15, run 29597931873; archive gate `all 10 blazornative_* present`) · publish gate
     **4 IL2072 + 10 exports** (dumpbin) + `BnGeolocationDemo` page-probe · consumer smoke **7
     nupkg + 6 snupkg**, Device pure. **Gate 1 review PASS** (4 Minors, folded). Mutations: Gate 1
     (deny-throws, mis-key, tri-state, export-9-stale), Gate 2 (struct-72, deny-throws, map-mis-key
     — all on the AVD), Gate 3 (three `ios.yml` runs — struct72 `29598528447`, deny-hang
     `29598558150`, tristate `29598581044`, each RED on its named assertion).
   - **PROVEN on CI** = the emulator mocked fix + mocked denial + recreation, the simulator
     mocked-`CLLocationManager` fix + denial. **UNPROVEN until the owner's physical Android phone**
     = the real GPS fix, the real system permission-dialog UX, `DeniedPermanently` across real
     restarts (both shells bypass the real OS dialog gesture in CI — not drivable in
     instrumentation/hosted XCTest; the design named this split). iOS real-device stays deferred
     (Apple Developer account trigger). See
     [design](../plans/2026-07-17-phase-9.0-design.md) +
     [conclusion](../plans/2026-07-17-phase-9.0-conclusion.md).
- ✅ **Phase 9.1** — local notifications + tap-through (DoD #3) — *complete (2026-07-17)*
   - **THE HEADLINE — the 9.0 ABI bet paid off on its FIRST draw: local notifications cost the ABI
     NOTHING.** 9.0 grew the bridge once, generically, arguing 9.1/9.2/9.3 would reuse it for free.
     They do: the three ops (`schedule` / `show` / `cancel`) + permission `request` / `check` all
     ride the **existing** `HostCallBegin` slot (offset 72) and `host_call_complete` export, the
     action carried in the flat JSON (geolocation's `mode` precedent). **Bridge stayed 80 bytes,
     exports stayed 10, no drift-pin moved, no gate arithmetic** — proven on all three RIDs
     (win-x64/bionic `dumpbin`+`readelf` 10; iOS archive `nm` the SAME ten; the 80-byte struct
     pins unmoved). Notifications added only **WIRE VOCABULARY**: one op value
     (`HostCallOp.Notifications = 1`), the `NotificationStatus` enum, the reserved host-event name
     `"navigate"`. The reuse is **falsifiable, not asserted**: `NotificationsAbiUnchangedTests`
     pins 80 bytes / offset 72 / op == 1, and assert-81-bytes reds it (`Actual: 80`) while the iOS
     struct-grow mutant **failed to COMPILE** (`missing argument 'mutSlot11'`) — the type system
     forbids a silent grow. *Pay once, reuse thrice, by construction.*
   - **Tap-through — an inbound event, solved without a new mechanism** (the design's hardest
     question). A tapped notification has **no in-flight .NET call to complete**, so it correctly
     does NOT use `host_call_complete`. Both cases, each on pre-existing machinery: **cold** (app
     killed) reuses the 5.1 launch deep-link `blazornative://<route>` → mount-by-name verbatim;
     **warm** (app alive) wires `onNewIntent` (Android, `singleTop`) / `didReceive` (iOS) — both
     unhandled since 5.1 — to CALL the pre-existing `blazornative_host_event` export with
     `"navigate"` → `NavigateToAsync` re-routes the live session (rc 0). **The iOS shell CALLS
     `host_event` for the FIRST TIME here** (the 9.0 `host_call_complete` precedent). The `"back"`
     shape from 5.1, extended to a route — no ABI, no new export.
   - **The three ops + permission.** `schedule` (inexact `AlarmManager` / `UNTimeInterval`) / `show`
     (`NotificationManager.notify` on the `blazornative_default` channel, Android 8+ /
     `UNMutableNotificationContent`) / `cancel`; `POST_NOTIFICATIONS` with the implicit-grant fast
     path below API 33; iOS `requestAuthorization` folding `.provisional` → Granted. **Denial is
     DATA on both shells** (a status, no throw, no hang, bounded — the 9.0 law, mutation-proven).
     `INotifications` in the existing **7th package** `BlazorNative.Device` (no 8th); `/notifications`
     demo (`BnNotificationsDemo`, the 11th routed page, sample-only).
   - **Two doc-vs-reality findings (Gate 3, honesty rows):** (1) an unauthorized simulator (all CI
     can offer) doesn't register a `getPendingNotificationRequests` entry, so schedule/cancel assert
     on a construction seam — the posted-in-the-shade proof is physical-device UNPROVEN; (2)
     `UNAuthorizationStatus` has **no `.restricted` case** — `restricted = 3` is a wire-parity
     constant only, iOS's reachable statuses are 0/1/2/4 (the design's table copied the row from
     `CLLocationManager`). **The 9.0 process fix, honored:** Gate 2 changed Android shell files with
     byte-identical template mirrors, synced the mirror AND ran the .NET drift tests (18/18) BEFORE
     pushing — the drift did NOT surprise at the merge.
   - **Counts (all CI-asserted):** .NET **655/0** (498 + 132 + 25; **616 → 655**, +39) · JVM
     **110/0** (**106 → 110**, +4 — `NotificationsTest.kt`; `BootSmokeNativeTest` still 10 exports)
     · Android instrumented **193/0** (**188 → 193**, +5 on the AVD: a real notification posts +
     cancels, both tap-through halves, denial-no-hang) · iOS XCTest **189/0** (**169 → 189**, +20,
     run 29612118131; archive `nm` gate the SAME ten symbols) · publish gate **4 IL2072 + 10
     exports** (UNCHANGED — the reuse proof) + `BnNotificationsDemo` page-probe. Mutations: Gate 1
     (deny-throws, navigate-missing, action-misparse, reuse-proof 81-bytes), Gate 2 (navigate-typo,
     route-dropped, deny-throws — on the AVD), Gate 3 (four `ios.yml` runs — navigate-typo
     `29612912372`, deny-drops `29612918098`, action-misparse `29612923977`, struct-grow
     `29612929731` — the COMPILE-FAIL).
   - **PROVEN on CI** = the AVD real post + cancel + both tap-through paths + denial-no-hang; the
     simulator schedule/cancel/denial + tap-through. **UNPROVEN until the owner's physical Android
     phone** = the real notification in the shade, a real tap from a genuinely killed process, the
     real `POST_NOTIFICATIONS` dialog gesture, `DeniedPermanently` across restarts. iOS
     simulator-scoped + labeled (Apple Developer account trigger). See
     [design](../plans/2026-07-17-phase-9.1-design.md) +
     [conclusion](../plans/2026-07-17-phase-9.1-conclusion.md).
- ✅ **Phase 9.2** — biometrics + secure storage (DoD #4) — *complete (2026-07-18)*
   - **THE HEADLINE — the 9.0 ABI bet paid a THIRD time: biometrics + secure storage cost the ABI
     NOTHING.** Two ops (`Biometrics = 2`, `SecureStorage = 3`) ride the **existing** `HostCallBegin`
     slot (offset 72) and `host_call_complete` export; the secure `get`'s value rides the OPTIONAL
     payload the completion channel has carried since 9.0 (geolocation's fix was the first user —
     this is the SECOND, proving the channel generic, not geolocation-shaped). **Bridge stayed 80
     bytes / 10 exports, no drift-pin moved, no gate arithmetic** — proven on all three RIDs
     (win-x64/bionic `dumpbin`+`readelf`; iOS `nm` gate `all 10 present`). Falsifiable, not asserted:
     `SecureBiometricsAbiUnchangedTests` pins 80 / offset 72 / ops 2+3, and the iOS struct-grow
     mutant **failed to COMPILE** (`missing argument 'mutantEleventhSlot'`, run 29636616995) — the
     type system enforces the freeze. *Pay once, reuse thrice — now demonstrated three times.*
   - **The OS-key binding — the security crux, and WHERE it can be proven** (this phase's real
     lesson). Owner chose **OS-key-level** binding (the OS refuses plaintext without a fresh auth,
     immune to a control-flow bypass) over app-level. **Android PROVES it** — the AVD's software
     keystore enforces `setUserAuthenticationRequired(true)`: a plain get of an auth-bound secret
     returns AuthFailed, mutation-airtight (drop `setUserAuthenticationRequired` → the binding test
     reds alone). **iOS asserts the CONTRACT with OS-enforcement UNPROVEN** — the simulator has NO
     Secure Enclave and treats `.biometryCurrentSet` as a no-op, so CI asserts the shell REQUESTS the
     ACL (construction seam) + enforces the contract from its own auth-bound cache, but the
     OS-enforced refusal is named **UNPROVEN-until-real-device**. The milestone's clearest example of
     a security property provable on one platform and not the other, recorded honestly.
   - **Two real platform asymmetries, both documented:** (1) an auth-bound `set` PROMPTS on Android
     (the AES per-use-auth key needs a fresh auth to encrypt) but does NOT on iOS (the Keychain ACL
     gates retrieval only) — same wire, different write-time UX; (2) the iOS simulator enforces no
     `SecAccessControl` (no Enclave) where the Android emulator's software keystore does. Storage:
     raw AndroidKeyStore AES/GCM (no dep) + iOS Keychain (no dep). **`androidx.biometric:biometric:1.1.0`
     is the first new gradle dep of M9** (repo + template, now drift-ENFORCED). MainActivity became
     a `FragmentActivity` (BiometricPrompt's host) — a real shell change, mirrored, predictive-back
     unchanged (all 193 prior instrumented tests stayed green). `IBiometrics` + `ISecureStorage` in
     the existing **7th package** `BlazorNative.Device` (no 8th); `/secure` demo (`BnSecureDemo`, the
     12th routed page, sample-only). **The M5 secure-storage deferral CLOSES here** — a
     four-milestone-old ledger item retired. Secret-in-memory: the wire is intra-process/trusted
     (encryption at rest); non-zeroable plaintext copies are documented for the POC, the zeroable
     pass is M10; 8 KB cap at the .NET boundary (`SecretResult.MaxValueBytes`, oversize → Error,
     never crosses).
   - **Gate 3's 5-run iOS Keychain convergence, honestly** (Mac-less setup): entitlement →
     sim-no-Enclave → a plain-path regression (the sim returns a `kSecAttrAccessControl` attr even
     for plain items → misclassification, fixed by keying off the shell's own cache) → **a real bug**
     (the biometrics deny replied SYNCHRONOUSLY inside `hostCallBegin`, hanging the bounded await —
     the milestone's **denial-as-data LAW caught it** — fixed via the notifications deferred-reply
     pattern). The law found a genuine bug.
   - **Counts (all CI-asserted):** .NET **717/0** (560 + 132 + 25) · JVM **115/0** (**110 → 115**,
     +5 — `BiometricsSecureStorageTest.kt`) · Android instrumented **201/0** (**193 → 201**, +8 on
     the AVD: OS-key binding proven, gated round-trip, denial-no-hang) · iOS XCTest **218/0**
     (**189 → 218**, +29, run 29636347300; the 10-export `nm` gate unchanged) · publish gate **4
     IL2072 + 10 exports** (UNCHANGED — the reuse proof). Mutations: Gate 1 (auth-throws,
     getWithAuth-ignores-requireAuth, reuse-proof-81-bytes, oversize-crosses), Gate 2
     (drop-`setUserAuthenticationRequired`, cryptoObject-null, deny-throws — on the AVD), Gate 3
     (acl-dropped `29636589965`, deny-drops `29636604241` the 11.6s hang, struct-grow `29636616995`
     the compile-fail).
   - **PROVEN on CI** = both status matrices as data, the Keystore/Keychain round-trip, Android's
     OS-enforced AuthFail, the gated read via the seam, denial-no-hang. **UNPROVEN until the owner's
     physical Android phone** = the real fingerprint sensor + TEE-enforced auth-bound decrypt + real
     lockout (biometrics is THE least emulator-like capability). **iOS OS-enforced binding → real
     device, DEFERRED** (no Apple account — doubly gated). See
     [design](../plans/2026-07-17-phase-9.2-design.md) +
     [conclusion](../plans/2026-07-18-phase-9.2-conclusion.md).
- ✅ **Phase 9.3** — camera photo capture (DoD #5) — *complete (2026-07-18)*
   - **THE HEADLINE — the 9.0 ABI bet paid a FOURTH time: camera cost the ABI NOTHING, despite a
     multi-MB result.** One op (`Camera = 4`) + `CameraStatus` ride the **existing** `HostCallBegin`
     slot (offset 72) and `host_call_complete` export; the captured image rides the OPTIONAL payload
     the completion channel has carried since 9.0 (geolocation first, secure-storage/notifications
     second/third — camera is the FOURTH, the channel now proven generic across a coordinate, a
     status, a secret, and a **file path**). **Bridge stayed 80 bytes / 10 exports, no drift-pin
     moved, no gate arithmetic** — proven on all three RIDs (win-x64/bionic `dumpbin`+`readelf`; iOS
     `nm` gate `all 10 present`, run 29639956941). Falsifiable, not asserted: `CameraAbiUnchangedTests`
     pins 80 / offset 72 / `Camera == 4`, and the iOS struct-grow mutant **failed to COMPILE**
     (`missing argument 'cameraExtra'`, run 29640255862) — the type system enforces the freeze.
     *Pay once, reuse thrice — now demonstrated four times, the sharpest test yet (a multi-MB image
     is exactly what "obviously" needs a new export; it did not).*
   - **THE IMAGE HANDOFF — the phase's real decision.** A photo is 1–10 MB, but the payload NAMES a
     file — `{"path":"file://…","width","height","bytes"}` — it does not carry the bytes. Bytes-inline
     REJECTED (multi-MB through a `const char*` + a non-zeroable .NET string, ~1000× the secure-storage
     in-memory hazard, and not even a secret); a binary export REJECTED (it would grow the ABI). The
     temp file lands in the app-cache dir (OS-reclaimable), the app owns it after handoff (`BnImage`
     decodes async — no auto-delete race), the shell prunes its capture dir per-capture as a leak
     backstop; the .NET side never deletes it (ownership boundary stated).
   - **BnImage composition — capabilities composing across milestones, and a ledger DISCHARGED.** The
     captured `file://` path is a valid `BnImage.Src` (Coil/Kingfisher load locals); the `/camera`
     demo displays it in a DEFINITE 240×320 `BnImage` with `ContentMode="Contain"`. **This DISCHARGES
     the M6/M7 "revisit ContentMode with a real natural-size image" ledger item** — a real megapixel
     photo in a definite box, natural size never measured, no reflow. Proven on the AVD end-to-end AND
     on iOS via `BnImageLoader.naturalPixelSize` (the exact function the image node's Yoga measure func
     calls). Both shells NORMALIZE EXIF (bake rotation into upright pixels + reset the tag) so
     Coil/Kingfisher never double-rotate — Android via `ExifInterface`+`Matrix`, iOS via
     `UIImage.imageOrientation` redraw.
   - **The capture path + permission.** Android `ACTION_IMAGE_CAPTURE` to the system camera app —
     **NO runtime CAMERA permission** (the system app owns the sensor), only a `FileProvider` for the
     output URI (a `<provider>` + `res/xml/file_paths.xml` — a NEW manifest+resource drift class,
     mirrored to the template, file-count 32 → 33). iOS `UIImagePickerController(.camera)` +
     `NSCameraUsageDescription` + `AVCaptureDevice` auth. Denial/cancel is DATA (`CameraStatus`
     Captured/Cancelled/Denied/Unavailable/Error) — never a thrown exception across the boundary,
     never a hang. `ICamera` the **5th Device façade** in the existing **7th package** (no 8th);
     `/camera` demo (`BnCameraDemo`, the 13th routed page, sample-only).
   - **The emulator/simulator honesty (this phase's sharpest).** Android's fake-scene satisfies
     `ACTION_IMAGE_CAPTURE` but the system camera-app SHUTTER isn't CI-drivable — so the result is
     seam-driven, BUT the synthetic bytes are written THROUGH the real FileProvider `content://` URI
     (the authority exercised for real — proven by the mis-authority mutation). **iOS is worse: the
     simulator has NO camera at all** — `check → Unavailable` is asserted as the CORRECT sim result
     (not a workaround), and a real capture is DOUBLY UNPROVEN (no sim camera AND no Apple account).
     **SafeAreaView — flagged three phases as camera's likely trigger — is NOT tripped**: the capture
     UI is system chrome, not app-laid-out. A three-phase-carried ledger item resolved with a reason.
   - **Counts (all CI-asserted):** .NET **754/0** (597 + 132 + 25) · JVM **119/0** (**115 → 119**,
     +4 — `CameraTest.kt`) · Android instrumented **209/0** (**201 → 209**, +8 on the AVD: the
     capture→file→BnImage round-trip through the real FileProvider URI, EXIF-normalize, cancel-no-hang)
     · iOS XCTest **233/0** (**218 → 233**, +15, run 29639956941; the 10-export `nm` gate unchanged)
     · publish gate **4 IL2072 + 10 exports** (UNCHANGED — the reuse proof). Mutations: Gate 1
     (cancel-throws, reuse-proof-81-bytes, no-path-on-cancel), Gate 2 (no-EXIF-normalize,
     mis-authority-FileProvider — on the AVD), Gate 3 (no-normalize `29640247999`, cancel-path
     `29640250307`, cancel-drop `29640252796` the hang, struct-grow `29640255862` the compile-fail).
   - **PROVEN on CI** = both status matrices as data, the capture→file→BnImage composition (real
     FileProvider URI on Android, seam on iOS), EXIF-normalize, cancel-no-hang, iOS check→Unavailable.
     **UNPROVEN until the owner's physical Android phone** = the real camera UI + sensor + EXIF (the
     milestone's SECOND least-emulated capability, with biometrics). **iOS real capture → doubly
     deferred** (no sim camera + no Apple account). **This is M9's LAST capability — only DoD #6
     (hygiene + audit, Phase 9.4) remains.** See
     [design](../plans/2026-07-18-phase-9.3-design.md) +
     [conclusion](../plans/2026-07-18-phase-9.3-conclusion.md).
- ✅ **Phase 9.4** — hygiene + M9 final audit + close (DoD #6; no tag — the 8.6 rule) —
  *complete (2026-07-18)*
   - **M9 CLOSED — all 6 DoD PASS**, verified against live artifacts, not planning prose. See the
     [final audit](../plans/2026-07-18-milestone-9-final-audit.md). **.NET suite re-run live: 754/0/0**
     (Runtime 597 + Renderer 132 + Analyzers 25) — matches the `ci.yml` gate literal. **The ABI grew
     EXACTLY ONCE** (9.0, 72→80 bytes / 9→10 exports): re-proven at three mirrors (`BridgeProtocolNative.cs`
     80 bytes/HostCallBegin@72, `BlazorNativeRuntimeC.h` the same, 10 `[UnmanagedCallersOnly]` exports)
     + three falsifiable `*AbiUnchangedTests` suites (every iOS struct-grow mutant failed to COMPILE);
     `HostCallOp` has exactly the 5 ops (Geolocation=0 … Camera=4). **Counts reconcile gate ↔ README**:
     754 / 119 / 209 / 233 — no row left behind (the 9.2 lesson). **Ledger settled**: M5 secure storage
     CONSUMED (9.2), M6/M7 natural-size + SafeAreaView DISCHARGED (9.3); carried forward = real-device
     iOS/TestFlight (Apple account), FCM push (Firebase), inspector channel (3rd deferral). **Honest
     UNPROVEN**: camera sensor + biometrics on the physical phone, iOS Secure Enclave. **No tag** (8.6
     rule — closure is the audit). Git: PRs #118/#127/#128/#129 landed on `main` as `feat:` commits;
     `git tag -l` empty.

---

### ✅ Milestone 10 — Consolidation & Hardening  *(complete 2026-07-19; old P6 "Framework Hardening", **rescoped to correctness + accuracy debt**)*

**0.1.0 is published** (seven packages, 2026-07-19) — the library is *consumed*, and the
9.0 full-repo review found defects that ship inside it. M10 adds **no platform surface**;
it makes the published 0.1.x **honestly correct** and its **docs + README accurate**, then
stops. Backbone = the review findings **#119–#125**; scope + owner decisions in
[MILESTONE.md](MILESTONE.md).

The broader "old P6" hardening — **accessibility, i18n, performance/memory budgets, the
security model, and the P3 perf-hardening ledger (#8/#9/#12/#13)** — is **re-deferred as
*investment, not need*** (worth doing only if this heads toward real adoption). Real-device
iOS (no Apple account), FCM push (no Firebase), and the inspector channel stay out.

- ✅ **Phase 10.0** — the two correctness bugs (#121, #123) — *complete (2026-07-19),
  red-first; iOS-reports-Android fixed via an explicit init-struct `PlatformInfoKind` (both
  shells, frozen bridge untouched), and the false-green `Frames` drop closed. .NET 766 / iOS
  235. [conclusion](../plans/2026-07-19-phase-10.0-conclusion.md)*
- ✅ **Phase 10.1** — version governance (#120, #122) — *complete (2026-07-19);
  `Exports.VersionNumber` now a release-please-governed mirror + drift pin (red-first proved
  the `1.4.0-phase-5.4` staleness), `RuntimeFrameworkVersion` drift-pinned across sample +
  template (mutation-proven). .NET 768. [conclusion](../plans/2026-07-19-phase-10.1-conclusion.md)*
- ✅ **Phase 10.2** — docs/README accuracy sweep (#119 + owner ask) + `BnListWindow` precision
  (#124) + grouped cleanups (#125) — *complete (2026-07-19); site + README swept to the
  auto-publish/published-0.1.0 reality with the honest CI-coverage split; `BnListWindow` in
  `double` (red-first); six #125 items fixed-or-ledgered; actions SHA-pinned. .NET 780 / JVM
  120. [conclusion](../plans/2026-07-19-phase-10.2-conclusion.md)*
- ✅ **Phase 10.3** — hygiene + M10 final audit + close (no tag — the 8.6 rule) — *complete
  (2026-07-19); all 7 DoD PASS on live evidence — .NET re-run live at 780/0/0, the four gate
  literals ↔ README rows reconciled, the frozen bridge re-proven unmoved (80 bytes / 10
  exports / 5 ops; the init-input struct the only thing that grew), issues #119–#125 CLOSED /
  #126 correctly deferred. [final audit](../plans/2026-07-19-milestone-10-final-audit.md)*.

---

### 🔄 Milestone 11 — Production Readiness  *(active — opened 2026-07-20)*

From *works + published + hardened* to **production-grade**: an app author builds on the
published packages without footguns (0.2.0 at milestone-open; 0.4.0 by the time 11.1 walked it),
the emulator-faked capabilities are proven on real
Android hardware, and the public API is committed to with concrete 1.0 criteria. Owner direction
(2026-07-20) + the seed finding that the deep-link route map is the last hand-written
single-source-of-truth violation. All four pillars in; device proof is Android-only (iOS
real-device still gated on the Apple account). Full scope + owner decisions in
[MILESTONE.md](MILESTONE.md).

- ✅ **Phase 11.0** — deep-link route codegen + the consumer-footgun audit (DoD #1) — *complete
  (2026-07-20).* The hand-written `DEEP_LINK_COMPONENTS` map is **generated from `AppPages.All` at
  build time** by `BlazorNative.RouteGen` (Roslyn **source** analysis — arch-independent, the arm64
  pivot; emits `res/raw/blazornative_routes.json`), shipped inside the `BlazorNative.Runtime` package
  so a `dotnet new` app derives its own map; `RouteTableDriftTests` reshaped to guard the generator.
  The [footgun audit](../plans/2026-07-20-phase-11.0-footgun-audit.md) confirmed it was the last
  page-keyed hand-edit: capability manifest entries are template-supplied (DERIVED), the un-derivable
  rest (app identity, URI scheme, iOS usage-string copy, iOS source edits) DOCUMENTED; three stale
  consumer docs fixed. [Conclusion](../plans/2026-07-20-phase-11.0-conclusion.md).
- ✅ **Phase 11.1** — consumer dogfooding + the ZeroAlloc showcase (DoD #3) — *complete
  (2026-07-21).* Two apps built **outside the repo from nuget.org only**, no `ProjectReference` —
  `bn-baseline` on published **0.3.0**, `bn-zeroalloc-showcase` on published **0.4.0** and scaffolded
  from the **published template**. **0.4.0 is the milestone release**: the `ConfigureServices`
  app-service DI seam (#159), the KDoc sweep (#161), a nuget-preflight fix (#163), and — via #162 —
  the **first release ever to publish `BlazorNative.Templates`** (verified live by a real
  `dotnet new install`, closing the M8 carryover and making the docs' front-door claim true). Every
  publish across `win-x64` / `linux-bionic-x64` / `linux-bionic-arm64` held the **4-IL2072
  yardstick** — including with **11 ZeroAlloc packages** layered on (no `Microsoft.CodeAnalysis`
  diamond, no duplicate-generator emit); 1 package dropped with a written reason (`ZeroAlloc.Cache`
  fails at `csc` — upstream Cache#87). RouteGen derived the deep-link map **from the packages alone**
  and regenerated it on an added page with zero shell edits (11.0's claim, proven for a consumer);
  the seam was proven **at runtime** by an ABI harness P/Invoking the *published* NativeAOT binary
  (app-service output in the first frame's patches, all 6 pages `rc = 0`). **14 friction items**, each
  fixed / resolved / dismissed / ledgered / deferred — docs fixes in #157, the template DI `using` in
  flight as #165, the rc-0-on-faulted-render design gap filed as #164 → Phase 11.4. **Boundaries:**
  initial-frame render only (no UI event dispatched), DevHost bridge only, APK built but **not
  installed** — real hardware is DoD #2; iOS-sim deferred.
  [Conclusion](../plans/2026-07-21-phase-11.1-conclusion.md) ·
  [friction ledger](../plans/2026-07-21-phase-11.1-friction-ledger.md) ·
  [walkthrough](../plans/2026-07-21-phase-11.1-walkthrough.md).
- ✅ **Phase 11.2** — real-device Android validation (DoD #2) — *complete (2026-07-22).* Run by the
  owner on a **Xiaomi 24069PC21G, `arm64-v8a`, Android 16 / SDK 36** — newer than any CI lane —
  with both bionic publishes holding the **4-IL2072 yardstick** and a dual-ABI APK.
  **All four capabilities proven against real hardware:** a fused **geolocation** fix
  (`fix:‹lat›,‹lon›`, *identical to platform rounding* against `dumpsys location`); a real
  **notification** with `POST_NOTIFICATIONS` genuinely flipping `false → true`, surviving a process
  kill, and **cold** tap-through into a new pid on `BnNotificationsDemo`; a real **camera** capture
  via `ACTION_IMAGE_CAPTURE` at `3072x4096` round-tripped into `BnImage`; and **biometrics +
  secure storage** with three distinct `keystore2` challenges and a complete positive/negative pair
  (`Unlock + CANCEL → AuthFailed` vs `Unlock + FINGERPRINT → value:hunter2`) — **the OS**, not the
  app, refusing decryption without fresh Class-3 auth, on a StrongBox-reporting device. Phase
  11.0's generated route map resolved a **cold** deep link *before the .NET runtime loaded* and
  carried **all 13 routes** with the pid unchanged — no crash on any route. **Two findings:**
  [#178](https://github.com/MarcelRoozekrans/BlazorNative/issues/178) (`CapturePhotoAsync()`'s
  `options = default` bypasses `CaptureOptions`' documented defaults → quality 1, no downscale —
  invisible to the DevHost bridge) and the **DoD #2 "+ EXIF" clause amended as unsatisfiable** (the
  shell strips EXIF on purpose when it normalises orientation; real-sensor evidence is now the
  sensor-resolution dimensions + a real scene + the `ACTION_IMAGE_CAPTURE` path). **Boundaries:**
  warm notification tap-through and the location permission prompt **not exercised**, interaction
  smoke **PARTIAL** — **MIUI blocks `adb shell input` entirely**, so every tap was a human tap and
  nothing could be scripted. **Discharges the M9 physical-phone ledger item.**
  [Device proof](../plans/2026-07-22-phase-11.2-device-proof.md) ·
  [design](../plans/2026-07-21-phase-11.2-design.md) ·
  [device runbook, amended](../plans/2026-07-21-phase-11.2-device-runbook.md).
- **Phase 11.3** — API stability: mark the stable surface (a PublicAPI baseline gate) + write the
  1.0 criteria (DoD #4). 1.0 is DEFINED here, not necessarily CUT.
- **Phase 11.4** — logging discipline ([#155](https://github.com/MarcelRoozekrans/BlazorNative/issues/155)):
  one level-gated logging seam, **quiet-by-default in Release**, unified across both shells (DoD #6).
  Today logging is un-gated and split — iOS `NSLog` emits normal-path chatter in Release, Android
  discards `Console.Error` to `/dev/null`; no unified level control.
- **Phase 11.5** — hygiene + M11 final audit + close (DoD #5, no milestone tag).

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
- Each milestone closes with `audit-milestone` → `complete-milestone`. **The `→ tag vN.0`
  step this line used to carry is GONE from M9 onward** — Phase 8.6 retired the
  milestone-tag namespace (see the note at the top of this file). **A milestone closes on
  its audit**; that is what M8 did, and the tag was never what made a milestone complete.
