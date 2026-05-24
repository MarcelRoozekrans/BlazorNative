# Milestone 1 — Mid-Milestone Audit

*Run: 2026-05-24*
*Triggered by: user request (project-orchestration `audit-milestone`)*

## Verdict

**FAIL with known gaps — 6/8 DoD criteria met.** Both failing criteria are explicitly the remaining-but-unstarted phases of M1 (Phase 1.3 for `[UnmanagedCallersOnly]` export verification, Phase 1.5 for analyzer scoping). No surprises; the audit confirms the roadmap.

## Per-criterion verification

| # | DoD | Status | Evidence | Notes |
|---|---|---|---|---|
| 1a | `dotnet build BlazorNative.sln -c Debug` succeeds | ✅ | 0 errors, 0 warnings (run 2026-05-24) | After Phase 1.2's ZAI007 suppression + Phase 1.1's BL0006 suppression, all warnings are now silenced. |
| 1b | `dotnet build BlazorNative.sln -c Release` succeeds | ✅ | 0 errors, 0 warnings | Same |
| 1c | WASI publish produces app `.wasm` | ✅ (with stale wording) | `src/BlazorNative.WasiHost/bin/Release/net10.0/wasi-wasm/AppBundle/BlazorNative.WasiHost.wasm` exists, ~6 MB | **Criterion text still says "BlazorNative.Core.wasm" — needs updating to "BlazorNative.WasiHost.wasm" after the Phase 1.2 architectural split.** Also still mentions `run-wasmtime.sh` launcher script and `BlazorNative.Core.dll` side-by-side bundle — that was the pre-AOT shape; the AOT'd output is a single bundled `.wasm`. |
| 2 | Renderer internal-API verdict captured | ✅ | [docs/plans/2026-05-23-renderer-internal-api-design.md](2026-05-23-renderer-internal-api-design.md) committed; verdict logged in BACKLOG and ROADMAP | |
| 3 | WASI entry point exists | ✅ (with stale wording) | [src/BlazorNative.WasiHost/WasiEntryPoint.cs](../../src/BlazorNative.WasiHost/WasiEntryPoint.cs) defines `Main`, exits cleanly | **Criterion text says `src/BlazorNative.Core/WasiEntryPoint.cs` (wrong project, post-pivot) and ".NET 9 cooperative scheduler" (.NET 10 Mono-WASI handles this transparently). Update during Phase 1.5 close-out.** |
| 4 | `[UnmanagedCallersOnly]` export visible via `wasm-tools dump` | ❌ | `wasm-tools` is not installed on the dev machine. The criterion's verification command (`wasm-tools dump artifacts/wasi/BlazorNative.Core.wasm \| grep blazornative_dispatch_event`) has never been run. | **Phase 1.3 explicitly addresses this.** |
| 5 | WASI runtime + DI graph compose under wasmtime | ✅ | `tests/BlazorNative.Wasi.Tests/BootSmoke` — 2 passed, ~9s end-to-end (publish → wasmtime → exit) | Reframed from the original "await Task.Delay round-trip" wording due to Mono-WASI sync constraint, captured in MILESTONE.md update and BACKLOG.md "Cooperative async on Mono-WASI" + "Mono-WASI async trap" bullets. |
| 6 | `DispatchEventAsync` signature compiles | ✅ | `BlazorNative.Renderer` builds clean; `NativeRenderer.DispatchUiEventAsync` calls `BlazorInterop.DispatchEventViaAccessor` which uses `[UnsafeAccessor]` + `[UnsafeAccessorType("EventFieldInfo, ...")]` | Phase 1.4 work was implicitly completed during Phase 1.1's renderer rewrite. Worth a formal close-out in Phase 1.5's housekeeping. |
| 7 | Analyzers silent on non-WASI projects | ❌ | `BlazorNative.Analyzers` is referenced via NuGet by no other project today (it sits unused awaiting the analyzer-ship infrastructure); WasiThreadingAnalyzer / WasiBclGapsAnalyzer rules wouldn't fire elsewhere anyway. No `.editorconfig` scoping exists. | **Phase 1.5 explicitly addresses this.** The criterion may need refinement — the actual concern is "once analyzers are wired into the build graph, they only fire on wasi-wasm-targeting code". |
| 8 | Decision log committed | ✅ | 5 design/plan docs in `docs/plans/`: codebase map, renderer-internal-api-design, phase-1.1-impl-plan, phase-1.2-design, phase-1.2-impl-plan. Plus ROADMAP + BACKLOG + MILESTONE updates per phase. | |

## Phase status (mirrors ROADMAP.md)

| Phase | Status | Notes |
|---|---|---|
| 1.1 — Renderer internal-API spike | ✅ complete (2026-05-23) | BlazorInterop.cs seam, smoke test green, surprise finding: Blazor 10 internals mostly public |
| 1.2 — WASI entry point + DI bootstrap | ✅ complete (2026-05-24) | WasiHost project, ZA.Inject MS DI Extension mode, BootSmoke tests green |
| 1.3 — `[UnmanagedCallersOnly]` export verification | ⏳ not started | Needs `wasm-tools` install + verification flow |
| 1.4 — `DispatchEventAsync` signature fix | ⏳ partially done (Phase 1.1 covered the compilation; formal close-out pending) | |
| 1.5 — Analyzer scoping for non-WASI projects | ⏳ not started | Needs `.editorconfig` design + scoping |

## Punch list to close M1

1. **Phase 1.3** — install `wasm-tools`, verify `blazornative_dispatch_event` appears in the AOT'd `.wasm`'s export table, document the verification command somewhere it can be re-run (probably in `Makefile` as `make wasi-inspect`).
2. **Phase 1.4** — formally close out (the work is done; just needs the BACKLOG bullet marked `[x]` and a sentence in the audit doc confirming).
3. **Phase 1.5** — design + implement the `.editorconfig` analyzer-scoping rules; verify the existing renderer/http projects don't get false positives.
4. **Housekeeping** — update DoD wording in `MILESTONE.md`:
   - DoD #1c: `BlazorNative.Core.wasm` → `BlazorNative.WasiHost.wasm`; drop the `run-wasmtime.sh` launcher reference.
   - DoD #3: `src/BlazorNative.Core/WasiEntryPoint.cs` → `src/BlazorNative.WasiHost/WasiEntryPoint.cs`; ".NET 9 cooperative scheduler" → ".NET 10 Mono-WASI runtime (sync `Main`)".
5. **`complete-milestone`** invocation — only after the punch list above is done.

## Risks carried into M1's remaining phases

- **D1 (Phase 1.2 review finding):** Mono-WASI's async trap will fire on the first real bridge event in M2. The BACKLOG bullet "Mono-WASI async trap will fire on first real bridge event" tracks three remediation options; one should be picked **before M2 brainstorming starts**, not as M2 itself begins.
- **`wasi-experimental` workload pinning `wasi-sdk-25`** — when the workload bumps to a newer wasi-sdk pin, `setup.ps1` and `WASI_SDK_PATH` need to follow. Worth a calendar reminder rather than a tracking bullet.

## Next action

Per the user's preference (`b then A`): proceed into Phase 1.3 brainstorming directly. The two stale-wording fixes in MILESTONE.md can roll up with Phase 1.5's close-out housekeeping, not now.
