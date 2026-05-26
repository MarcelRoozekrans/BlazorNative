# Milestone 2 — P1: First End-to-End Demo on Android

**Status:** active
**Started:** 2026-05-24
**Source:** maps to BACKLOG.md "P1 — First end-to-end demo"
**Predecessor:** Milestone 1 — complete 2026-05-24, tagged `v1.0` ([final audit](../plans/2026-05-24-milestone-1-final-audit.md))

## Goal

Render a Blazor component as native Android widgets via a Kotlin shell that embeds Wasmtime and loads our WASI module.

This milestone closes the loop from .NET source code to a pixel on a real Android device. After M2 ships, the architecture diagram in `README.md` is no longer aspirational — it's executable.

## Definition of Done

1. **Mono-WASI async trap resolved.** The `Task.InternalWaitCore PlatformNotSupportedException` concern carried from M1 (BACKLOG bullet "Mono-WASI async trap will fire on first real bridge event") is addressed via one of the three documented remediation options:
   - (a) queue-and-drain pattern in the bridge — `[UnmanagedCallersOnly]` callback enqueues; a sync `pump` export drains;
   - (b) sync-callable bridge interface — no `await` anywhere on the WASM → host path;
   - (c) move renderer execution into the Android shell process — WASM module becomes pure logic/data.

   Decision committed to `docs/plans/`; remediation implemented; existing `ExportSmoke` test extended to confirm a bridge round-trip with at least one subscriber doesn't trap.

2. **Android Kotlin shell scaffold exists** at `src/BlazorNative.Shell.Android/` with a minimal `MainActivity.kt` that loads `BlazorNative.WasiHost.wasm` from app assets. Builds via Gradle, installs to an emulator or device. *(Implemented in Phase 2.2 after Phase 2.1 proves the runtime layer on desktop JVM.)*

3. **JVM ↔ libwasmtime JNI integration** via JNA bindings against `libwasmtime` (cross-compiled per ABI from wasmtime source via cargo + NDK). Module loads successfully on Android; `mobile_bridge` import symbols can be wired to Kotlin implementations. *(Original DoD referenced `dev.wasmtime:wasmtime-java:latest` — that artifact does not exist. Strategy revised during Phase 2.1 brainstorm to Strategy G: cross-compile `libwasmtime` ourselves, bind via JNA. RN-Hermes pattern. See [Phase 2.1 design](../plans/2026-05-26-phase-2.1-design.md).)*

4. **All seven `mobile_bridge` symbol exports implemented** on the Android side:
   - `shell_navigate(routePtr, routeLen)`
   - `shell_current_route(buf, bufLen) → int`
   - `shell_storage_read(keyPtr, keyLen, valBuf, valBufLen) → int`
   - `shell_storage_write(keyPtr, keyLen, valPtr, valLen)`
   - `shell_storage_delete(keyPtr, keyLen)`
   - `shell_fetch(reqPtr, reqLen, resBuf, resBufLen) → int`
   - `shell_platform_info(buf, bufLen) → int`

5. **Render-frame consumer.** WASM-side: the renderer's `DispatchFrameAsync` write hits a code path the Android shell can intercept. Android-side: receives the `RenderFrame` JSON (via storage hook, dedicated export, or another path settled during Phase 2.3 brainstorm), parses to `RenderPatch[]`, applies patches to its native widget tree.

6. **Native widget mapper** — Android implements the `NodeType` → widget mapping table from BACKLOG:

   | NodeType | Android widget |
   |---|---|
   | `view` | `FrameLayout` / `LinearLayout` |
   | `text` | `TextView` |
   | `button` | `Button` |
   | `input` | `EditText` |
   | `image` | `ImageView` |
   | `scroll` | `ScrollView` |
   | `picker` | `Spinner` |

7. **End-to-end demo runs on a real Android device (or emulator).** A `Hello`-style Blazor component renders correctly with the expected `[BOOT]` markers in logcat and the expected widgets on screen. Evidence captured as a screenshot or short recording in `docs/plans/`.

8. **Decision log committed** — design + implementation-plan doc per phase, plus an M2 final-audit doc once complete (same pattern as M1).

## Out of scope for this milestone

- iOS Swift shell — Milestone 4 / BACKLOG P3
- Component library (`Bn*` components, `@bind`, cascading values, navigation service) — Milestone 3 / BACKLOG P2
- Production hardening (security, accessibility, i18n, OTA updates) — Milestones 6/7
- NuGet packaging, CI pipeline, DevTools render-tree inspector — Milestone 4 / BACKLOG P3
- Multi-window support, MD3/HIG defaults — Milestone 8 / BACKLOG P7

## Initial phase plan

Tracked in `ROADMAP.md`. Subject to refinement via `add-phase` / `insert-phase`:

- **Phase 2.0 — Mono-WASI async-trap remediation** *(complete 2026-05-25; (b) sync-callable bridge interface chosen)*
- ✅ **Phase 2.1 — JVM desktop hosts `.wasm` via libwasmtime + JNA** *(complete 2026-05-26; GREEN CHECKPOINT met — 4 [BOOT] markers captured by `BootSmokeTest` via in-process JNA-bound libwasmtime. See [Phase 2.1 design](../plans/2026-05-26-phase-2.1-design.md) + [implementation plan](../plans/2026-05-26-phase-2.1-implementation-plan.md).)*
- ✅ **Phase 2.2 — Android port** *(complete 2026-05-26; GREEN CHECKPOINT met — BootSmokeAndroidTest passes on blazornative-pixel6-x86_64 AVD; same .wasm boots identically in wasmtime CLI subprocess, JVM in-process JNA, and Android in-process JNA. See [Phase 2.2 design](../plans/2026-05-26-phase-2.2-design.md) + [implementation plan](../plans/2026-05-26-phase-2.2-implementation-plan.md).)*
- **Phase 2.3 — `mobile_bridge` symbol implementations** (Android side)
- **Phase 2.4 — Render-frame consumer** (WASM-side dispatch + Android-side parse)
- **Phase 2.5 — Native widget mapper** (`NodeType` → Android widgets)
- **Phase 2.6 — `BlazorNativeHostElement` stub** (renderer-side host element descriptor satisfying Blazor's requirements without a real DOM)
- **Phase 2.7 — End-to-end demo + final audit** (Hello component on emulator/device; capture evidence; close milestone)

## Why this milestone exists

M1 proved the toolchain. M2 proves the architecture. After M2, every later milestone (P2 component library, P3 iOS shell, P4 platform APIs, ...) builds on a known-working Blazor → WASM → native widget pipeline. Skipping M2's end-to-end demo means downstream milestones inherit unknown unknowns from the unverified integration boundary.

## Risks identified at milestone start

| # | Risk | Mitigation |
|---|---|---|
| 1 | Mono-WASI async trap blocks every bridge call (see DoD #1) | Phase 2.0 explicitly addresses this BEFORE any native-shell scaffolding. Three options enumerated; brainstorm picks one. |
| 2 | `wasmtime-java` may not match wasmtime CLI's component-model support level | Verify in Phase 2.1; fallback options: pin to an older wasmtime-java version that supports our component shape, or switch to a different embedding (e.g. `chicory` for pure-JVM, or shell out to native wasmtime binary). |
| 3 | Render-frame transport (storage hook vs dedicated export) is undecided | Phase 2.3 brainstorm picks. Storage-hook is convenient but adds JSON-serialization cost; dedicated export is more efficient but requires Phase 2.0's async-trap fix to be solid. |
| 4 | Android device + Java 17+ + Android SDK on Marcel's dev machine | Verify before Phase 2.1; install Android SDK via Android Studio or `sdkmanager` CLI if missing. Probably a `setup.ps1` extension. |
