# Milestone 4 — Final Audit

*Date: 2026-07-12*
*Audit by: Claude (Fable 5), at Marcel's request, as Phase 4.5 Gate 3*
*Triggered by: Phase 4.5 Task 3 — last gate before `complete-milestone` + tag `v4.0`*

## Verdict

**PASS — all 8 DoD criteria PASS. M4 is ready for `complete-milestone` + the `v4.0` tag.**

M4 turned M3's "a real app runs on the author's machine and AVD" into "a project
someone else can evaluate, extend, and consume": the repo is public with branch
protection and a CI pipeline that *asserts* every baseline (test counts, trim-warning
counts, the eight-export ABI) instead of observing them; the BN analyzer suite tells
the truth about the NativeAOT runtime and rides every src build under a zero-warning
bar; the runtime-hardening ledger was triaged into fixed-with-tests or
re-ledgered-with-rationale; a measured fast-restart dev loop and a live-session
DevTools inspector exist; and the five NuGet packages are proven from the outside by
a consumer smoke that restores from the pack feed alone, runs on every PR.

Six criteria carry honesty notes (justified per criterion below): #1's repo home and
protection timing, #2's informational instrumented job, #4's five deliberately
re-ledgered items, #5's fast-restart-not-hot-reload naming, #6's JVM-session
inspector, and #7's unpackaged Runtime + deferred nuget.org. None is a scope miss —
each is the DoD's substance delivered with the boundary honestly recorded, most of
them in MILESTONE.md's own closure notes.

## Per-criterion verification (2026-07-12, after Phase 4.5 Gate 2)

### DoD #1 — Public GitHub repo with issue structure

> The repo is pushed to GitHub and public. Labels, milestones, and issues are created
> per a refreshed `docs/GITHUB-SETUP.md` [...]. Branch protection on `main` per the
> guide. README current with the NativeAOT architecture.

**Verdict:** PASS

**Implementing phase:** Phase 4.0 — [conclusion](2026-07-11-phase-4.0-conclusion.md)

**Evidence:**
- Public at **github.com/MarcelRoozekrans/BlazorNative**; 37 labels, 7 milestones
  (M1–M3 closed with audit links), 25 open-work issues at creation; README rewritten
  to the NativeAOT architecture (extended again in 4.3/4.4 with the dev-loop and
  inspector lanes) with the CI badge.
- Branch protection on `main`: require PR + the `build-test` check, admins included —
  every phase since 4.0 merged through it (PRs through #46).
- Phase 4.5 closed a hygiene gap *inside* this criterion's spirit: the repo had been
  public **without a LICENSE file** since the flip; MIT (the packages' declared
  license) landed at Gate 1 (`d54c761`).

**Honesty notes:** (a) the repo home was **revised at the 4.0 brainstorm** from the
BACKLOG-era ZeroAlloc-Net org to the personal account — a deliberate POC-home
decision, transfer is cheap later; (b) protection was applied **post-flip**, not
before it, because private personal repos need GitHub Pro — a documented deviation
in the 4.0 conclusion's diagnosis ledger.

### DoD #2 — CI green on every PR

> GitHub Actions pipeline: build + analyzers + full .NET test suite + JVM
> `testDebugUnitTest` on every push/PR; NativeAOT publish for all three RIDs with the
> eight-export verification, artifacts uploaded; the Android instrumented suite as a
> scheduled or manually-triggered job [...]. CI badge in the README.

**Verdict:** PASS

**Implementing phase:** Phase 4.0 — [conclusion](2026-07-11-phase-4.0-conclusion.md);
extended by every later phase (baseline bumps 177→197→203 .NET / 32→34→47→73 JVM;
the 4.5 consumer-smoke step)

**Evidence:**
- `ci`/`build-test` (windows-latest) on every push/PR: build with analyzers →
  `dotnet test` asserting **203/0/0** → three NativeAOT publishes each asserting
  **exactly 4 IL2072s** → eight-export set-equality via dumpbin/llvm-readelf →
  JVM asserting **73/0** → consumer smoke (Phase 4.5) → artifacts for the
  instrumented workflow. Baselines are asserted, not observed — count drift fails
  loudly, proven by every phase having to bump ci.yml alongside its tests.
- `android-instrumented`: nightly + manual, windows `.so` artifact → ubuntu KVM
  emulator, 35/35.
- Latest PR evidence: PR #46 `build-test` green first attempt, 6m59s
  (run 29180322641).

**Honesty note:** the instrumented job is **informational** (not a required check) —
the DoD's own wording sanctions "scheduled or manually-triggered"; promotion to
required awaits a green-nightly stability baseline (the 4.0 conclusion's recorded
criteria). Per-PR device coverage rides local sweeps, which every phase's gates run.

### DoD #3 — Analyzer suite is real

> The WASI-era BN rules are retired or reframed for the NativeAOT world; the analyzer
> project is re-attached to the runtime project graph; every surviving BN rule has
> `Microsoft.CodeAnalysis.Testing` coverage [...]. Release-tracking files land with
> the tests.

**Verdict:** PASS

**Implementing phase:** Phase 4.1 — [conclusion](2026-07-11-phase-4.1-conclusion.md);
Phase 4.5 proved the packaged form live in a consumer

**Evidence:**
- 6 WASI-era rules retired (IDs never reused), 5 rescoped, 2 new interop rules
  (BN0020/BN0021, Error) for the `[UnmanagedCallersOnly]` boundary; rule docs at
  `docs/analyzers.md`.
- Re-attached to **all six src projects** (`OutputItemType="Analyzer"`), zero-warning
  bar met with one justified pragma (BN0011 in DevHostBridge — it IS the bridge);
  `Exports.cs` conforms to its own rules.
- **23 analyzer tests**, TDD-first, including two review-driven false-negative fixes;
  release tracking real (`AnalyzerReleases.Shipped.md`/`Unshipped.md`, RS2008 NoWarn
  gone, `-warnaserror` clean).
- Phase 4.5's consumer smoke extends the proof beyond the solution: the **packaged**
  analyzer (from `analyzers/dotnet/cs`) fires BN0011 in a blank consumer's build and
  stays silent on the clean build — asserted both directions on every PR.

### DoD #4 — The runtime-hardening ledger is deliberate

> Every open ledger item [...] is either **fixed with tests** or **explicitly
> re-ledgered with a written rationale** in a triage doc. Load-bearing minimum
> expected to be fixed: focus/blur wiring, the subscriber-isolation decision, the
> allocation-budget test.

**Verdict:** PASS

**Implementing phase:** Phase 4.2 —
[conclusion](2026-07-11-phase-4.2-conclusion.md) ·
[triage doc](2026-07-11-phase-4.2-hardening-triage.md) (the ledger of record)

**Evidence:**
- **All three load-bearing items fixed with tests:** focus/blur end-to-end on real
  AVD EditText transitions; RouteChanged subscriber isolation (contained under
  strict mode too — the documented posture decision); the M1 allocation-budget test
  enabled with a **measured** 328 B/frame steady-state baseline (600 KB/900-render
  bound). Plus the stale-watcher leak fixed (exactly-one-dispatch pinned on-device)
  and a stale skip cleaned up.
- **Five items re-ledgered with written rationale + concrete revisit triggers** in
  the triage doc; in-code breadcrumbs at all five sites point back at it.

**Honesty note — the re-ledgered five, tracked openly:** async-handler
exception-capture window (issue **#8**), dispatch-lane starvation (**#9**) — one
design, owned by the first real async `@onclick` consumer; RemoveComponent bucket
scan (**#12**) and TranslateToViewIndex memoization (**#13**) — await a keyed `Bn*`
list benchmark showing they matter; NativeEvents redesign — routed to **M5**
host-initiated lifecycle ingress (deleting it would orphan BN0014 + the DevHost
path). Status at M4 close: all five remain re-ledgered, none re-opened by later
phases — 4.3/4.4/4.5 added no new ledger items. The DoD explicitly permits this
outcome; the triage doc is the ledger of record.

### DoD #5 — Dev inner loop exists and is measured

> File-watcher → incremental win-x64 publish → JVM host reload as the fast lane, plus
> the ADB-push → app-restart story [...]. Documented honestly: NativeAOT cannot
> hot-patch — this is **fast-restart, not hot-reload** — and the measured round-trip
> times are recorded.

**Verdict:** PASS

**Implementing phase:** Phase 4.3 — [conclusion](2026-07-11-phase-4.3-conclusion.md)

**Evidence:**
- `make devloop` (FileSystemWatcher over the five .NET src projects → win-x64
  publish → `PreviewHost` tree dump with per-stage timings) and
  `make devloop-android` (bionic-x64 publish → `installDebug` → `am start` →
  logcat `[BOOT] mounted` marker).
- **Measured, each reproduced ≥2×:** incremental publish 8.2–9.4 s ·
  boot-to-tree ~0.3 s · JVM cycle 9.9–11.2 s · ADB cycle 14.1–14.5 s — recorded in
  the README's three-lane Dev experience section.
- `TreeSnapshot` (WidgetMapper-parity JVM node model) landed as TDD infrastructure
  (JVM 34 → 47), reused by 4.4's inspector.

**Honesty note:** the loop is named **fast-restart, not hot-reload**, in the README
and conclusion — NativeAOT cannot hot-patch and Windows locks the loaded dll; the
design's "no unload API" wording was itself corrected in the conclusion
(`NativeLibrary.dispose()` exists; the other two legs make restart correct
regardless). The DoD asked for exactly this honesty.

### DoD #6 — DevTools render-tree inspector

> A dev-host surface showing the live patch stream, the current widget tree
> (collapsible), and the event log against a running session.

**Verdict:** PASS

**Implementing phase:** Phase 4.4 — [conclusion](2026-07-11-phase-4.4-conclusion.md)

**Evidence:**
- `make inspect` serves all three DoD surfaces — live patch stream (bounded ring,
  500), collapsible `<details>` widget tree with props/styles/events, event log
  (200) with one global monotonic seq — **against a running native session**
  (`InspectorHost` boots the real NativeAOT dll; same C-ABI frames and dispatch lane
  the APK rides), plus dispatch-from-the-page (fire clicks, send change payloads)
  beyond the DoD's asking.
- Live updates via pull-model SSE; proven end-to-end on the 3.5 navigation in
  headless Chrome over CDP (page-driven BnDemo → Settings → back → change echo).
  JVM 47 → 73.

**Honesty notes:** (a) the "running session" is a **JVM-hosted native session**
(the real dll on the desktop), not an on-device channel — the Android-safe state/
JSON pieces already live in main/kotlin for the **M5** on-device channel; (b) the
older browser DevHost inspector runs a **mock-bridge session** and the README
labels the two sessions honestly (DevHost `/dev/*` = mock; `make inspect` =
native).

### DoD #7 — NuGet packages restore into a blank consumer project

> `BlazorNative.Core`, `BlazorNative.Renderer`, `BlazorNative.Http`,
> `BlazorNative.Components`, and `BlazorNative.Analyzers` pack cleanly (local/CI
> feed; nuget.org publication is a separate decision at milestone close). The proof
> is a consumer smoke: a blank project referencing only the packages mounts a `Bn*`
> component and produces frames. The analyzers package ships its `.props`/`.targets`
> correctly.

**Verdict:** PASS

**Implementing phase:** Phase 4.5 —
[conclusion](2026-07-12-phase-4.5-conclusion.md) (`d54c761` pack, `c4e4ac1` smoke)

**Evidence:**
- All five pack **clean** (zero warnings, NU5xxx included) at `1.2.0-phase-4.5`;
  layouts verified by unzip inspection — the analyzers package carries the dll in
  `analyzers/dotnet/cs` with **no `lib/`**, `developmentDependency=true`, pinned
  Roslyn deps; library nuspecs carry correct dependencies (ZeroAlloc + AspNetCore
  resolved concretes, inter-package refs at the shared version, no PrivateAssets
  leaks).
- **The consumer smoke is the DoD's own proof shape:** `samples/ConsumerSmoke`
  (outside the solution) restores from the local feed only — provenance asserted
  from NuGet's `.nupkg.metadata` (five packages ← `artifacts/packages`, transitives
  ← nuget.org) — mounts BnView/BnText/BnButton and **produces frames** (1 frame,
  10 patches, exactly one click AttachEvent asserted), exit 0. Green locally and as
  a `ci.yml` step on every PR (PR #46, smoke step ~21 s).
- The analyzers-live pin: BN0011 fires in the consumer's trip build, zero BN
  diagnostics clean — the packaged analyzer demonstrably loads.
- The DoD's props/targets sentence is answered by verification, not shipping:
  plain `DiagnosticAnalyzer`s need **no** props/targets (NuGet auto-loads
  `analyzers/dotnet/cs`) — "ships correctly" = ships none, proven live by the trip.

**Honesty notes:** (a) **`BlazorNative.Runtime` is deliberately NOT packaged** —
the NativeAOT composition root is an app-shape concern; consumers get it as an M6
template/story. The smoke mounts via the managed harness shape (renderer DI +
`Frames`), which is what packages-without-a-shell can honestly prove; the
native-consumer story remains proven by the repo's own shell. (b) **nuget.org is
deferred** per the milestone's own scope note ("a separate decision at milestone
close") — decided 2026-07-12: local/CI feed only, re-decide at M6;
`PackageReadmeFile` is the recorded prerequisite.

### DoD #8 — Decision log committed

> Same pattern as M1–M3: design + plan + conclusion doc per phase, plus an M4
> final-audit doc at close → tag `v4.0`.

**Verdict:** PASS

**Implementing phases:** all of 4.0 through 4.5.

**Evidence:**
- Design + implementation-plan + conclusion docs for every phase: 4.0, 4.1, 4.2
  (plus the hardening-triage ledger of record), 4.3, 4.4, 4.5 — all under
  `docs/plans/2026-07-1*-phase-4.*`.
- Honest corrections recorded in place: 4.3's "no unload API" wording fix, 4.5's
  design-doc `SuppressDependenciesWhenPacking` line corrected against the shipped
  analyzer metadata.
- This audit doc: `2026-07-12-milestone-4-final-audit.md`. Tag command recorded for
  post-merge: `git tag -a v4.0 -m "Milestone 4: P3 — Production-Shippable complete"`.

## Scaffolding ledger

Test seams and probes alive at M4 close — all internal/test-only, none on the
shipped C-ABI (still exactly the eight `blazornative_*` exports, CI-asserted ×3
RIDs):

| Artifact | Location | Status / rationale |
|---|---|---|
| `FocusProbe` | `src/BlazorNative.Runtime/FocusProbe.cs` | **STAYS** — 4.2's focus/blur on-device regression surface (registry component driven by FocusBlur tests on JVM + AVD). |
| `TriggerRootRenderForTests` | `NativeRenderer` (internal) | **STAYS** — 4.2's allocation-budget seam: deterministic re-renders without event dispatch; throws on non-ComponentBase roots (test-wiring bug surface, not a runtime path). |
| `ReplaceRegistryEntryForTests` | `HostSession` (internal) | **STAYS** — registry-swap seam (4.2, same posture); production ABI never calls it; consumers serialize via the host-session collection. |
| `CompositionProbe` | registry component | **STAYS** (M3 ledger decision reaffirmed) — the only on-device multi-component regression surface; Bn* components exercise overlapping but not identical shapes. |
| `TrimValidationProbes` | test project | **STAYS** (M3 ledger decision reaffirmed) — host-CLR trim-shape regression coverage; never shipped surface. |

Nothing was added to the shipped export surface in M4; 4.1's `Exports.cs`
conformance pass was behavior-neutral (dumpbin 8/8 unchanged).

## Test counts at M4 close

```
.NET                           → 203 passed / 0 skipped / 0 failed   (CI-asserted)
JVM  (testDebugUnitTest)       →  73 passed / 0 failed               (CI-asserted)
Android (connectedAndroidTest) →  35 passed / 0 failed               (blazornative-pixel6-x86_64; runner-strict)
```

Version `1.2.0-phase-4.5` everywhere (packages, `Exports.cs`, both Kotlin
assertions). For scale: M3 closed at 177/32/32 with 2 skips; M4 closes at
203/73/35 with **zero skips** (the last M1-era skip became 4.2's measured
allocation test) and every count asserted in CI rather than observed.

## Operational observations

1. **CI froze the baselines and the baselines held.** Every phase bumped ci.yml's
   asserted counts in the same commit as its tests; no drift ever reached main.
   The Phase 2.4b wasmtime flake family is structurally extinct (zero occurrences
   possible since 3.0e; none observed in any M4 sweep).
2. **The zero-warning bar survived contact with packaging.** 4.1's analyzer
   re-attach, 4.5's pack metadata, and the consumer build all hold zero warnings —
   with exactly one justified pragma (DevHostBridge BN0011) and the 4 accepted
   publish-time IL2072s ×3 RIDs unchanged since 3.0a.
3. **First-attempt greens became the norm:** 4.0's cold CI, 4.4's Gate 1 e2e, and
   4.5's smoke step + PR #46 all went green on first attempt — the
   assert-don't-observe posture and the mirror-gate discipline are doing their job.
4. **Two stale-wording corrections recorded in place** (4.3's unload-API claim,
   4.5's SuppressDependenciesWhenPacking line) — small, but the record stays honest
   at the sentence level, consistent with M3's reversal-recording practice.

## Carryovers to next milestones

| # | Carryover | Target | Source |
|---|---|---|---|
| 1 | iOS Swift shell (NativeAOT `ios-arm64` static lib + Swift twin — needs Mac hardware/CI) | M5 | milestone-open scope decision |
| 2 | Android shell completeness (lifecycle, permissions, FCM, secure storage, deep links, predictive back — issue #16) | M5 | BACKLOG P4 |
| 3 | Host-initiated navigation + lifecycle events (issue #19; the NativeEvents fork from the 4.2 triage) | M5 | [triage doc](2026-07-11-phase-4.2-hardening-triage.md) + M3 audit carryover |
| 4 | On-device inspector channel (state/JSON pieces already Android-safe) | M5 | [4.4 conclusion](2026-07-11-phase-4.4-conclusion.md) |
| 5 | Re-ledgered hardening items #8/#9/#12/#13 (revisit triggers in the triage doc) | as-triggered | [triage doc](2026-07-11-phase-4.2-hardening-triage.md) |
| 6 | nuget.org publication decision + `PackageReadmeFile`/icons; Runtime template/story; version-churn surface now five files | M6 | [4.5 conclusion](2026-07-12-phase-4.5-conclusion.md) |
| 7 | M6 packaging ledger unchanged (stringly FontSize/Padding, navigation package lift #23, `.razor` compilation, inspector page polish) | M6 | M3 audit + 3.4/3.5/4.4 conclusions |
| 8 | Instrumented-job promotion to required check (green-nightly baseline) | M5+ | [4.0 conclusion](2026-07-11-phase-4.0-conclusion.md) |

## Phase ledger

| Phase | Status | Headline outcome |
|---|---|---|
| 4.0 — GitHub publish + CI | ✅ complete | Public repo + protected main; assert-don't-observe CI on every PR; nightly instrumented job. |
| 4.1 — Analyzer rescope + tests | ✅ complete | BN rules truthful for NativeAOT (6 retired / 5 rescoped / 2 new interop rules); 23 tests; zero-warning bar on all six src projects. |
| 4.2 — Runtime hardening triage | ✅ complete | 4 fixed with tests (+1 cleanup) incl. all three load-bearing items; 5 re-ledgered with rationale + triggers; 328 B/frame measured. |
| 4.3 — Dev inner loop | ✅ complete | `make devloop`/`devloop-android`, measured warm cycles (JVM ~10 s, ADB ~14 s); honestly fast-restart. |
| 4.4 — DevTools inspector | ✅ complete | `make inspect` over a live native session: patch stream + collapsible tree + event log + dispatch-from-the-page. |
| 4.5 — NuGet + consumer smoke + close | ✅ complete | Five packages pack clean; blank-consumer smoke green on every PR; LICENSE; this audit. |

## Recommendation

Invoke `complete-milestone` — flip ROADMAP.md M4 to complete, MILESTONE.md status to
complete, open the M5 pointer (P4 — Full Platform Coverage), and after the
controller merges `phase-4.5-packaging` to `main` (PR #46), tag the merge commit:

```
git tag -a v4.0 -m "Milestone 4: P3 — Production-Shippable complete"
```

M5 (P4 — Full Platform Coverage) opens next, mapped to BACKLOG "P4 — Full platform
coverage" (iOS shell #17, Android completeness #16, host-initiated nav/lifecycle
#19, cross-platform APIs #18), with this audit's carryover table as triage input
for the M5 brainstorm.
