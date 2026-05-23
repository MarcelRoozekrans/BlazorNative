# BlazorNative — Roadmap

*Source of truth for milestone and phase state. Updated by project-orchestration sub-skills.*

## Milestones

### 🔄 Milestone 1 — P0: Runtime Boots End-to-End  *(active, started 2026-05-23)*

Toolchain produces a `.wasm` that loads under wasmtime with correct exports and a working cooperative scheduler. Renderer internal-API strategy decided.

Definition of done: see [MILESTONE.md](MILESTONE.md).

Phases:
- ✅ **Phase 1.1** — Renderer internal-API spike — *complete (2026-05-23)*
   - Verdict: `BlazorInterop.cs` isolation layer with `Bn*` ref-struct wrappers. Against Blazor 10, most render-tree members turned out to be public — `[UnsafeAccessor]` is only needed for `Renderer.DispatchEventAsync` (uses internal `EventFieldInfo`). See [design](../plans/2026-05-23-renderer-internal-api-design.md) + [implementation plan](../plans/2026-05-23-phase-1.1-implementation-plan.md).
   - Side effects: full retarget to .NET 10; `System.Reactive` → `ZeroAlloc.AsyncEvents`; `List<RenderPatch>` → `ZeroAlloc.Collections.PooledList`; smoke test (`FirstFrame_HasExpectedPatches`) passes; allocation budget test deferred to Milestone 4.
   - Discovered: .NET 10's `wasi-experimental` workload provides **Mono-AOT**, not NativeAOT, for `wasi-wasm`. Design's "Native AOT + WASI" framing was wrong; updated.
- ⏳ **Phase 1.2** — WASI entry point + cooperative scheduler bootstrap — *pending*
   - Prerequisite: install `wasi-sdk` (Clang/LLVM toolchain) and set `WASI_SDK_PATH` env var. Phase 1.1 produced `dotnet.wasm` runtime + AppBundle, but the IL→WASM AOT step (which yields the app-specific `BlazorNative.Core.wasm`) needs wasi-sdk.
   - Replace the no-op `WasiEntryPoint.cs::Main` with the .NET 10 cooperative scheduler bootstrap + DI registration + root component mount.
- ⏳ **Phase 1.3** — `[UnmanagedCallersOnly]` export verification — *pending*
- ⏳ **Phase 1.4** — `DispatchEventAsync` signature fix — *pending* — *partial credit already taken in Phase 1.1 (`BlazorInterop.DispatchEventViaAccessor`).*
- ⏳ **Phase 1.5** — Analyzer scoping for non-WASI projects — *pending*

---

### ⏳ Milestone 2 — P1: First Pixel on Android  *(pending)*

Android Kotlin shell scaffold + wasmtime-java + `mobile_bridge` symbol exports + native widget mapper. Goal: a `BnText` rendered on a real Android device.

Maps to BACKLOG.md "P1 — First end-to-end demo".

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
