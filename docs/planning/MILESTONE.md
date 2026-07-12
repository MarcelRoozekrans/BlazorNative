# Milestone 4 — P3: Production-Shippable

**Status:** complete — closed 2026-07-12, tag `v4.0` (post-merge on `main`).
[Final audit](../plans/2026-07-12-milestone-4-final-audit.md): **PASS, all 8 DoD
criteria** (honesty notes on #1/#2/#4/#5/#6/#7 recorded per criterion).
**Source:** maps to BACKLOG.md "P3 — Production readiness"
**Predecessor:** Milestone 3 — complete 2026-07-10, tagged `v3.0` ([final audit](../plans/2026-07-10-milestone-3-final-audit.md): PASS, all 11 DoD criteria)

## Goal

M3 proved real apps can be built. M4 makes the project *shippable and public*: the
repo lives on GitHub with CI as the safety net for every change, the framework's own
guardrails (the BN analyzers) are re-attached and tested instead of detached and
untested, the known-issues ledger becomes a deliberate engineering decision rather
than an accumulation, and the packages + dev inner loop exist that would let someone
who isn't the author build an app on BlazorNative.

Scope boundary decided at milestone-open (2026-07-11): **Windows + Android only** —
the iOS Swift shell is deferred to M5 (Full Platform Coverage), where it lands as a
NativeAOT `ios-arm64` static lib + Swift shell mirroring the Kotlin one (the
BACKLOG's WasmKit framing is obsolete post-3.0e).

## Definition of Done

The criteria below are the initial M4 contract drafted at milestone-open. Subject to
refinement during the Phase 4.0 brainstorm.

1. **Public GitHub repo with issue structure.** The repo is pushed to GitHub and
   public. Labels, milestones, and issues are created per a refreshed
   `docs/GITHUB-SETUP.md` (the script and guide are updated first — their current
   P0–P3 framing still references `.wasm` validation and other WASI-era items that
   died in 3.0e). Branch protection on `main` per the guide. README current with the
   NativeAOT architecture. ✅ **CLOSED 2026-07-11 (Phase 4.0):** public at
   github.com/MarcelRoozekrans/BlazorNative (home revised from ZeroAlloc-Net at
   brainstorm — personal POC home); 37 labels / 7 milestones (M1–M3 closed with
   audit links) / 25 open-work issues; protection (require PR + `build-test`,
   admins included) applied post-flip — private personal repos need Pro, a
   documented deviation. See [Phase 4.0 conclusion](../plans/2026-07-11-phase-4.0-conclusion.md).

2. **CI green on every PR.** GitHub Actions pipeline: build + analyzers + full .NET
   test suite + JVM `testDebugUnitTest` on every push/PR; NativeAOT publish for all
   three RIDs (win-x64, linux-bionic-x64, linux-bionic-arm64) with the eight-export
   verification, artifacts uploaded; the Android instrumented suite as a scheduled or
   manually-triggered job (emulator-on-Actions needs a KVM Linux runner and the NDK —
   too slow for per-PR). CI badge in the README. ✅ **CLOSED 2026-07-11 (Phase 4.0):**
   `ci`/`build-test` asserts (not observes) 177/2/0 + 4 IL2072s ×3 + eight-export
   set-equality ×3 + JVM 32/0 — green first attempt, 13m cold / 6m warm;
   `android-instrumented` nightly/manual (windows artifact → ubuntu KVM) 32/32 first
   attempt, informational until a green-nightly stability baseline; badge live.
   See [Phase 4.0 conclusion](../plans/2026-07-11-phase-4.0-conclusion.md).

3. **Analyzer suite is real.** The WASI-era BN rules are retired or reframed for the
   NativeAOT world; the analyzer project is re-attached to the runtime project graph
   (it has been detached since 3.0e); every surviving BN rule has
   `Microsoft.CodeAnalysis.Testing` coverage — fires on bad code, silent on correct
   code, fix verified where one exists. Release-tracking files (the RS2008 deferral
   from M1) land with the tests. ✅ **CLOSED 2026-07-11 (Phase 4.1):** 6 rules
   retired / 5 rescoped / 2 new `[UnmanagedCallersOnly]` interop rules (BN0020/21);
   analyzers re-attached to **all six src projects** with a zero-warning bar met
   (one justified BN0011 pragma in DevHostBridge; `Exports.cs` made conformant,
   ABI-parity verified); **23 analyzer tests**, TDD-first; release tracking real
   (`AnalyzerReleases.Shipped.md`/`Unshipped.md`, RS2008 NoWarn gone,
   `-warnaserror` clean); rule docs at `docs/analyzers.md`; .NET baseline 197/2/0
   asserted in CI. See [Phase 4.1 conclusion](../plans/2026-07-11-phase-4.1-conclusion.md).

4. **The runtime-hardening ledger is deliberate.** Every open ledger item — the
   3.2/3.3 carryovers (async-handler capture window, dispatch-lane starvation,
   focus/blur unwired, stale-watcher re-attach, RemoveComponent bucket scan,
   TranslateToViewIndex memoization), RouteChanged subscriber isolation (3.5), and
   the allocation-budget test deferred from M1 — is either **fixed with tests** or
   **explicitly re-ledgered with a written rationale** in a triage doc. Load-bearing
   minimum expected to be fixed rather than re-ledgered: focus/blur wiring, the
   subscriber-isolation decision, the allocation-budget test. ✅ **CLOSED 2026-07-11
   (Phase 4.2):** all three load-bearing items fixed with tests (focus/blur on real
   AVD views; RouteChanged isolated — contained under strict mode too, the
   documented posture decision; allocation test enabled with a **measured** 328
   B/frame baseline, 600 KB/900 bound) plus the stale-watcher leak fixed
   (exactly-one-dispatch pinned on-device) and the stale `NestedElements` skip
   cleaned up; the 5 remaining items re-ledgered with rationale + concrete revisit
   triggers in [the triage doc](../plans/2026-07-11-phase-4.2-hardening-triage.md)
   (the ledger of record — in-code breadcrumbs point there). Counts asserted in CI:
   .NET 203/0, JVM 34, Android 35; version `1.1.0-phase-4.2`. See
   [Phase 4.2 conclusion](../plans/2026-07-11-phase-4.2-conclusion.md).

5. **Dev inner loop exists and is measured.** File-watcher → incremental win-x64
   publish → JVM host reload as the fast lane, plus the ADB-push → app-restart story
   for on-device iteration. Documented honestly: NativeAOT cannot hot-patch — this is
   **fast-restart, not hot-reload** — and the measured round-trip times are recorded.
   ✅ **CLOSED 2026-07-11 (Phase 4.3):** the loop exists (`make devloop` /
   `make devloop-android` → `scripts/devloop.ps1`: FileSystemWatcher fast lane over
   the new `PreviewHost` console tree dump, scripted ADB lane to the logcat
   `[BOOT] mounted` marker), it is **measured** (warm, reproduced ≥2×: incremental
   win-x64 publish 8.2–9.4 s · PreviewHost boot-to-tree ~0.3 s · JVM cycle
   9.9–11.2 s · ADB cycle 14.1–14.5 s — recorded in the README's three-lane Dev
   experience rewrite), and it is **honestly named** (fast-restart, not hot-reload:
   NativeAOT can't hot-patch, Windows locks the loaded dll; the "no unload API"
   wording corrected in the conclusion). JVM suite 34 → **47** (TreeSnapshot
   WidgetMapper-parity TDD); .NET 203/0 and Android 35 untouched. See
   [Phase 4.3 conclusion](../plans/2026-07-11-phase-4.3-conclusion.md).

6. **DevTools render-tree inspector.** A dev-host surface showing the live patch
   stream, the current widget tree (collapsible), and the event log against a running
   session. ✅ **CLOSED 2026-07-11 (Phase 4.4):** the DoD wording met and exceeded —
   `make inspect` serves all three surfaces against a running **native** session
   (the real NativeAOT dll over `InspectorHost`, not a mock), plus interactive-lite
   **dispatch-from-the-page** (fire clicks, send change payloads → rc shown, logged);
   live updates ride a pull-model SSE. Collapsible `<details>` tree with props/
   styles/events per node; patch ring (500) + event log (200) with one global
   monotonic seq. Proven end-to-end on the Phase 3.5 navigation (page-driven
   BnDemo → Settings → back → change echo, in headless Chrome via CDP). JVM suite
   47 → **73**; .NET 203/0 and Android 35 untouched. See
   [Phase 4.4 conclusion](../plans/2026-07-11-phase-4.4-conclusion.md).

7. **NuGet packages restore into a blank consumer project.** `BlazorNative.Core`,
   `BlazorNative.Renderer`, `BlazorNative.Http`, `BlazorNative.Components`, and
   `BlazorNative.Analyzers` pack cleanly (local/CI feed; nuget.org publication is a
   separate decision at milestone close). The proof is a consumer smoke: a blank
   project referencing only the packages mounts a `Bn*` component and produces
   frames. The analyzers package ships its `.props`/`.targets` correctly.
   ✅ **CLOSED 2026-07-12 (Phase 4.5):** five packages pack clean (zero warnings)
   at `1.2.0-phase-4.5`; analyzers layout unzip-verified (`analyzers/dotnet/cs`,
   no `lib/`, DevelopmentDependency, pinned Roslyn deps) — props/targets verified
   **not needed** for plain DiagnosticAnalyzers, proven live in the consumer;
   `samples/ConsumerSmoke` (outside the sln) restores from the local feed only
   (provenance asserted from `.nupkg.metadata`), mounts BnView/BnText/BnButton,
   asserts the patch set, and pins analyzer activity both directions (BN0011 trip
   + zero-BN clean build) — green locally AND as a ci.yml step on every PR
   (PR #46). MIT LICENSE added (the public repo lacked one). Honesty:
   `BlazorNative.Runtime` deliberately not packaged (app-shape concern; M6
   template/story); nuget.org deferred to M6. See
   [Phase 4.5 conclusion](../plans/2026-07-12-phase-4.5-conclusion.md).

8. **Decision log committed.** Same pattern as M1–M3: design + plan + conclusion doc
   per phase, plus an M4 final-audit doc at close → tag `v4.0`.
   ✅ **CLOSED 2026-07-12 (Phase 4.5):** design + plan + conclusion per phase
   (4.0–4.5, plus the 4.2 hardening-triage ledger of record), two in-place
   wording corrections recorded honestly (4.3 unload-API, 4.5
   SuppressDependenciesWhenPacking), and the
   [M4 final audit](../plans/2026-07-12-milestone-4-final-audit.md) — PASS on all
   8 criteria; tag command recorded for post-merge.

## Out of scope for this milestone

- iOS Swift shell — Milestone 5 (decided at milestone-open; hardware + CI Mac runner)
- Host-initiated navigation (back button, deep links) — Milestone 5
- `BlazorNative.Navigation`/packaging lift, `.razor` compilation, `BlazorNative.Cli` — Milestone 6
- Security model, accessibility, i18n, OTA — Milestone 7
- nuget.org publication — decision at M4 close (packages themselves are DoD #7)
- Performance work beyond the allocation-budget test — as-triaged by DoD #4

## Inherited from M3

From the [M3 final audit](../plans/2026-07-10-milestone-3-final-audit.md) carryover
table and phase conclusions:

- **Analyzer rescope** (3.0e carryover) — covered by DoD #3.
- **Runtime-hardening ledger** (3.2/3.3/3.5 items listed above) — covered by DoD #4.
- **Diagnostics/host-error surface** — `NativeRenderer.StrictErrors` production
  default is false with a documented "diagnostics surface is M4+" posture (3.3);
  DoD #4's triage owns the decision on what production error surfacing looks like.
- **Allocation-budget test** (M1 Phase 1.1 deferral, explicitly "Milestone 4") —
  covered by DoD #4.
- **M6 packaging ledger stays M6** (stringly FontSize/Padding, theme colors ×4
  files, paired-harness extraction, one-type-per-file, `firstMatch` ×4) — NOT pulled
  forward; DoD #7 packages the API as-is at POC fidelity.

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open:

- **Phase 4.0** — GitHub publish + CI pipeline (DoD #1, #2)
- **Phase 4.1** — Analyzer rescope + unit tests (DoD #3)
- **Phase 4.2** — Runtime hardening: ledger triage + fixes (DoD #4)
- **Phase 4.3** — Dev inner loop / fast-restart (DoD #5)
- **Phase 4.4** — DevTools render-tree inspector (DoD #6)
- **Phase 4.5** — NuGet packaging + consumer smoke + M4 close (DoD #7, #8)

CI lands first so every later phase rides it; the pipeline gets extended in 4.1
(analyzer gates) rather than rebuilt. NuGet lands last, after hardening settles the
public API surface.

## Why this milestone exists

M1–M3 were about proving the idea to its author. M4 is about proving it to everyone
else: a public repo with green CI is the difference between a research artifact and a
project someone can evaluate, and packages + a dev loop are the difference between
"the demo works on the author's machine" and "you can try this yourself." Doing the
hardening triage now — while the ledger is small and each item's context is fresh —
is dramatically cheaper than letting it compound under M5's platform expansion.

## Closure (2026-07-12)

All six phases shipped; the [final audit](../plans/2026-07-12-milestone-4-final-audit.md)
walks each DoD criterion against evidence — **PASS on all 8**. Counts at close
(all CI-asserted): .NET **203/0**, JVM **73/0**, Android **35/35**; version
`1.2.0-phase-4.5`; the shipped C-ABI unchanged at exactly eight exports.

Honesty summary (details per criterion above and in the audit): repo home is the
personal account (revised from ZeroAlloc-Net) with protection applied post-flip;
the instrumented CI job stays informational pending a green-nightly baseline; five
hardening items remain deliberately re-ledgered (issues #8/#9/#12/#13 + the
NativeEvents → M5 fork) with the [triage doc](../plans/2026-07-11-phase-4.2-hardening-triage.md)
as ledger of record; the dev loop is fast-restart, not hot-reload; the inspector
session is JVM-hosted native (on-device channel = M5); `BlazorNative.Runtime` is
deliberately unpackaged and nuget.org is deferred to M6.

Tag (controller, post-merge of PR #46 on `main`):
`git tag -a v4.0 -m "Milestone 4: P3 — Production-Shippable complete"`.
Next: **M5 — P4: Full Platform Coverage** (pointer open in
[ROADMAP.md](ROADMAP.md); opens via `new-milestone`).
