# Milestone 3 — Final Audit

*Date: 2026-07-10*
*Audit by: Claude (Fable 5), at Marcel's request, as Phase 3.5 Gate 5*
*Triggered by: Phase 3.5 Task 7 — last gate before `complete-milestone` + tag `v3.0`*

## Verdict

**PASS — all 11 DoD criteria PASS. M3 is ready for `complete-milestone` + the `v3.0` tag.**

M3 turned M2's "a static Hello renders on an emulator" into "a real app can be built": a
two-page, interactive, themed, data-bound demo runs on the AVD through a NativeAOT `.so`
with a typed C-ABI. The milestone's defining event was architectural: Phase 3.0 committed to
NativeAOT-per-ABI, Phase 3.0b's Gate-4 RED forced a documented dual-runtime pivot, and Phase
3.0c **superseded that pivot** by proving the `linux-bionic-*` runtime-pack bypass works on
.NET 10 — one NativeAOT runtime on every platform, wasmtime and `.wasm` deleted entirely in
3.0e. On that foundation, 3.1–3.5 closed the application-layer DoD items one per phase, each
with on-device proof: the six bridge operations (3.1), bidirectional events (3.2),
composition-grade rendering + strict mode + the AppendChild decision (3.3), the `Bn*` library
+ `@bind` mechanics + cascading values (3.4), and navigation (3.5).

Four criteria carry wording-level honesty notes (justified per criterion below): #3 shipped
as host-registered C-ABI **callbacks** rather than literal exports, #5 closed the
`@bind` **mechanics** while the Razor syntax awaits M6 tooling, #6 rides a **corrected**
3.3 diagnosis, and #7's `BlazorNative.Navigation` package name is deferred to M6 while the
contract ships in Core. None of these is a scope miss — each is the DoD's substance delivered
with the wording honestly qualified in MILESTONE.md itself.

## Per-criterion verification (2026-07-10, after Phase 3.5 Gate 4)

### DoD #1 — Runtime architecture decision committed

> **Runtime architecture decision committed.** [M3 resolves the runtime-architecture question
> carried over from M2: pick (or confirm) the long-term build target — Mono-AOT
> wasi-experimental (status quo), componentize-dotnet (typed WIT, same runtime), or
> NativeAOT-per-ABI (drops .wasm entirely).]

**Verdict:** PASS

**Implementing phases:** 3.0 (decision) + 3.0a/3.0b/3.0c (proof) + 3.0d/3.0e (cutover + collapse)
- Decision committed 2026-05-28: **NativeAOT-per-ABI** — [Phase 3.0 design](2026-05-28-phase-3.0-design.md)
- Trim-safety prerequisite: [Phase 3.0a conclusion](2026-05-28-phase-3.0a-spike-conclusion.md)
- The honest detour: [Phase 3.0b conclusion](2026-05-31-phase-3.0b-spike-conclusion.md) — Gate 4
  RED (`linux-bionic-*` not supported by .NET 10 ILCompiler) → documented dual-runtime pivot
  (2026-06-01)
- The pivot superseded: [Phase 3.0c conclusion](2026-07-08-phase-3.0c-spike-conclusion.md) —
  runtime-pack bypass (`PublishAotUsingRuntimePack=true` + vendored `BionicNativeAot.targets` +
  NDK linker) produces working Android NativeAOT `.so`s from Windows; pinned combo enforced
  in-repo (`global.json` 10.0.3xx, ILCompiler/runtime packs 10.0.9, NDK 26.3 revision-checked)
- Cutover + WASM-era collapse: [Phase 3.0d conclusion](2026-07-09-phase-3.0d-conclusion.md) +
  [Phase 3.0e conclusion](2026-07-09-phase-3.0e-conclusion.md) — typed 48-byte patch ABI;
  wasmtime/WasiHost/`[FRAME]` stdout deleted (71 files, −2,823 lines); project renamed
  `BlazorNative.Runtime`

**Evidence:** every subsequent phase publishes and tests against the NativeAOT runtime on all
three RIDs (win-x64 JVM dev loop, linux-bionic-x64/arm64 Android). At M3 close the C-ABI is
the eight-export surface; there is no second runtime, no `.wasm`, no wasmtime anywhere in the
repo. The decision is not just committed — its consequences are fully executed.

### DoD #2 — Bidirectional event flow

> **Bidirectional event flow.** `<button @onclick>` round-trips end-to-end: tap on Android
> widget → host invokes .NET event-dispatch C-ABI export → the Blazor component's handler
> fires → re-render emits a frame → widget tree updates.

**Verdict:** PASS

**Implementing phase:** Phase 3.2 — [conclusion](2026-07-09-phase-3.2-conclusion.md)

**Evidence:**
- Tap round-trip live on the AVD: tap → `WidgetMapper` click listener → single Kotlin dispatch
  lane → `blazornative_dispatch_event` → `@onclick` handler inside the trimmed `.so` →
  synchronous re-render → widget text updates (`taps: 1` → `taps: 2`, proving listener
  re-attach after re-render).
- The M2 "long-running-Main" concern retired structurally under NativeAOT (the library is
  always loaded).
- Change events (EditText `TextWatcher` → `ChangeEventArgs`, `applyingBatch` re-entrancy
  guard) shipped in the same phase as `@bind` groundwork; still exercised at M3 close by the
  3.4/3.5 demo suites on all three surfaces.

### DoD #3 — 6 deferred `mobile_bridge` exports implemented

> **6 deferred `mobile_bridge` exports implemented** (Phase 2.3 carryover): `shell_navigate`,
> `shell_current_route`, `shell_storage_read`, `shell_storage_write`, `shell_storage_delete`,
> `shell_fetch`. *Transport mechanism settled by Phase 3.0 decision: direct
> `[UnmanagedCallersOnly]` C-ABI exports + JNA callbacks.*

**Verdict:** PASS — with a wording-level honesty note

**Implementing phase:** Phase 3.1 — [conclusion](2026-07-09-phase-3.1-conclusion.md)

**Evidence:**
- All six operations ship and are exercised end-to-end: the host registers a 6-pointer struct
  once at boot via `blazornative_register_bridge`; `NativeShellBridge : IMobileBridge` invokes
  them (sync ops via caller-allocated buffers + the `-needed` retry protocol; fetch via the
  async completion pattern through `blazornative_fetch_complete`).
- Milestone moment: plain injected `HttpClient` works on Android (`BridgeHttpHandler` →
  `NativeShellBridge` → `AndroidShellBridge` with `SharedPreferences` storage and
  `HttpURLConnection` fetch).
- At M3 close the navigate/current-route pair is **load-bearing in production**: Phase 3.5's
  `NativeNavigationManager` notifies the host through `Navigate` and initializes from the
  host's `CurrentRoute` buffer.

**Honesty note:** the criterion's original 2.3-era wording says "exports". What shipped is
**host-registered callbacks** (host → struct of function pointers → .NET invokes), plus two
real exports (`register_bridge`, `fetch_complete`) — the direction is inverted from the
WASI-era framing because under NativeAOT the .NET side is the *library*, not the host. The
DoD's own amended text ("transport mechanism settled by Phase 3.0 decision... JNA callbacks")
records this. Substance — all six operations callable from .NET on Android — is fully
delivered; PASS.

### DoD #4 — `Bn*` component library

> **`Bn*` component library** — typed wrappers around the raw `NodeType`s from M2: `BnView`,
> `BnText`, `BnButton`, `BnInput`, plus parameters that flow through to widget properties
> (`BackgroundColor`, `FontSize`, `Padding`, `Placeholder`, `Enabled`, `OnClick`).

**Verdict:** PASS

**Implementing phase:** Phase 3.4 — [conclusion](2026-07-10-phase-3.4-conclusion.md)

**Evidence:**
- New public `BlazorNative.Components` project: sealed `BnView` / `BnText` / `BnButton` /
  `BnInput` with hand-written gap-numbered `BuildRenderTree`; every listed parameter flows
  through to widget props/styles (`BackgroundColor`/`Padding` → SetStyle, `FontSize` →
  SetStyle, `Placeholder`/`Enabled` → UpdateProp, `OnClick` → click attach).
- Mount shapes pinned at the patch level on all three surfaces; live on the AVD via `BnDemo`
  (the launcher default), extended to two pages in 3.5 (`BnSettingsPage`).
- **Recorded caveat (not a gap in the criterion):** `FontSize`/`Padding` are string
  pass-through — a conscious POC decision, M6-ledgered because changing to numeric parameters
  later is source-breaking. The criterion requires the parameters to *flow through*; they do.

### DoD #5 — `@bind` two-way binding

> **`@bind` two-way binding** works for at least one form input — EditText `value` ↔
> component state. Triggers re-render on change.

**Verdict:** PASS — with the milestone's most explicit honesty note: **mechanics, not syntax**

**Implementing phase:** Phase 3.4 — [conclusion](2026-07-10-phase-3.4-conclusion.md)

**Evidence:**
- What closed is the `Value`/`ValueChanged` (`EventCallback<string>`) pair — **exactly what
  Razor's `@bind-Value` compiles to**. The loop is proven on-device both ways: typing into the
  bound `BnInput` (non-ASCII `héllo→世界` through the full IME/UTF-8/C-ABI leg) updates
  component state and the live echo, and the value echo does NOT clobber the input.
- The complementary-assertions proof: the JVM twin pins the write-back UpdateProp *arrives*
  through the ABI; the Android twin pins the EditText is *not overwritten* (inequality skip +
  `applyingBatch` guard). Arrival without suppression is an infinite echo; suppression without
  arrival is a dead loop; together they are the complete proof.
- **The honest boundary:** the literal `@bind` *syntax* requires `.razor` compilation, which
  is M6 (`BlazorNative.Cli`/tooling territory); until then parents wire the pair by hand, as
  `BnDemo` does. MILESTONE.md's closure note words this explicitly. The criterion's substance
  — EditText `value` ↔ component state, re-render on change — is met on-device; the sugar is
  out of M3 scope by design. PASS on that explicit basis.

### DoD #6 — Cascading values

> **Cascading values** propagate from a root-mounted parent component to nested child
> components, and a change in the parent triggers child re-render.

**Verdict:** PASS

**Implementing phase:** Phase 3.4 (with the 3.3 carryover note corrected) —
[conclusion](2026-07-10-phase-3.4-conclusion.md)

**Evidence:**
- `BnDemo`'s `CascadingValue<BnTheme>` cascades to the nested `BnThemedPanel`
  (`[CascadingParameter]` consumer); the Theme button's parent-state change re-renders the
  consuming children **on-device** — both themed backgrounds flip `#FFEEAA ⇄ #334455` and
  back on the AVD, plus JVM/.NET patch-level pins.
- `RendererBlazorAPICoverage` Probe5/5b assert the cascaded child's *rendering* and the
  cascaded-change re-render path, not just value propagation.

**Honesty note — the corrected diagnosis:** 3.3's carryover claimed Region subtrees
(`CascadingValue.ChildContent`) were "silently dropped" and made a Region-walk fix the
prerequisite for this criterion. That diagnosis was **empirically wrong** — Blazor 10
decomposes region inserts per-child and the old fall-through was accidentally transparent;
cascading values had been rendering all along. 3.4's Gate 1 converted the accident into an
explicit, sabotage-verified Region-arm contract (skip-without-descend sabotage turns 4/6
RegionWalkTests RED). The reversal is recorded in the 3.4 conclusion (Finding 1), the 3.3
conclusion, and ROADMAP — the record is honest end to end, and the criterion passes on
on-device evidence, not on the corrected prerequisite.

### DoD #7 — Navigation service

> **Navigation service** (`BlazorNative.Navigation`) — at minimum,
> `INavigationManager.NavigateTo(route)` triggers a root-component swap. Wired through the
> `shell_navigate` / `shell_current_route` exports from DoD #3.

**Verdict:** PASS — with a package-location honesty note

**Implementing phase:** Phase 3.5 — [conclusion](2026-07-10-phase-3.5-conclusion.md)

**Evidence:**
- `INavigationManager.NavigateToAsync(route)` triggers a real root-component swap: `Unmount`
  (Blazor 10's `RemoveRootComponent`, verified callable FIRST per the design's named risk) →
  the 3.3 disposal machinery emits the RemoveNode sweep → fresh `TryMount` → afterSwap route
  state + `RouteChanged`. Route table `/` → `BnDemo`, `/settings` → `BnSettingsPage`.
- Wired through the DoD #3 surface exactly as specified: `NavigateToAsync` notifies the host
  via the 3.1 `Navigate` callback; session startup resolves the first mount of the routed
  default component through the host's `CurrentRoute` buffer.
- **Closed on-device** (`9c93339`): `NavigationAndroidTest` taps "Settings →" → the whole
  screen swaps (settings title visible, the BnDemo input GONE from the tree); "← Back" →
  `BnDemo` remounts fresh. Mirrored at patch level in `NavigationTests.cs` (.NET) and
  `NavigationTest.kt` (JVM through the dll), including the from-inside-a-click-handler
  swap (deferred via `RunAfterDispatch`, drained before the export returns).
- Mid-dispatch swaps, failed-swap route-state posture, and startup-route narrowing are all
  pinned and their boundaries documented (3.5 conclusion "honest boundaries").

**Honesty note:** the criterion names a `BlazorNative.Navigation` package. The contract ships
in `BlazorNative.Core` (beside `IMobileBridge`) with the impl in `BlazorNative.Runtime` — a
locked 3.5 brainstorm decision; the package lift is M6 alongside NuGet packaging (BACKLOG P5's
`BlazorNative.Navigation` theme). The criterion's own "at minimum" clause is about the
behavior, which is fully delivered; host-initiated navigation (back button, deep links) is
explicitly M5 scope per the design. PASS.

### DoD #8 — Multi-component composition

> **Multi-component composition.** Components compose other components; nested-component
> `PrependFrame` parenting works correctly (Phase 2.5 Task 1 review finding fixed).

**Verdict:** PASS

**Implementing phase:** Phase 3.3 — [conclusion](2026-07-10-phase-3.3-conclusion.md); hardened
by Phase 3.4's component-chain fix ([conclusion](2026-07-10-phase-3.4-conclusion.md), Finding 2)

**Evidence:**
- Slot-list model: component frames occupy slots; the component-parent map roots a child
  component's diff at the parent's view, not the host root (the exact Phase 2.5 finding);
  disposal really tears down (the old path silently no-oped on a never-populated map).
- Proven on-device: `CompositionProbe`'s interleaved `ItemComponent` badge renders visibly at
  child index 1, keyed insert/remove reorder correctly, the child's own handler fires.
- 3.4's `BnThemedPanel` → `BnView` chaining surfaced the wrong-parent-bucket registration —
  caught by 3.3's `ContractWarning` **working as designed** and fixed with
  `ComponentChainTests` pinning mid-list insertion. Every Bn* composite since (BnDemo,
  BnSettingsPage) rides these paths under strict mode.

### DoD #9 — `HandleException` strict-mode opt-in

> **`HandleException` strict-mode opt-in.** Renderer exceptions are surfaced rather than
> silently swallowed to `Console.Error`.

**Verdict:** PASS

**Implementing phases:** Phase 3.2 (dispatch-window capture) + Phase 3.3 (full strict mode) —
[3.2 conclusion](2026-07-09-phase-3.2-conclusion.md) ·
[3.3 conclusion](2026-07-10-phase-3.3-conclusion.md)

**Evidence:**
- Inside the dispatch window: the 3.2 depth-counted capture makes handler/re-render/
  frame-delivery exceptions visible as `dispatch_event` rc 2 + stderr detail (necessary
  because Blazor 10 swallows them — `DispatchEventAsync` completes successfully).
- Out of window (mount-time included): `NativeRenderer.StrictErrors` rethrows synchronously
  via `ExceptionDispatchInfo`; renderer contract violations raise through the same switch.
- The opt-in is real and *always on where it matters*: every .NET test fixture runs strict,
  and since 3.5's Gate 0 every instrumented process is strict **by construction**
  (`BlazorNativeTestRunner` sets `BLAZORNATIVE_STRICT=1` before any class loads — the
  filtered-run gap closed). Production default remains false — the documented POC posture
  (criterion says *opt-in*, which this is); the diagnostics surface is M4+.
- 3.5 extended the surfacing posture to navigation: deferred-swap faults map to rc 2 and
  every drain fault is stderr-logged. Residual log-only paths (non-strict drain faults) are
  recorded as honest boundaries in the 3.5 conclusion, consistent with the opt-in framing.

### DoD #10 — `AppendChild` patch emission decision

> **`AppendChild` patch emission decision.** Currently defined in `PatchProtocol.cs` but never
> emitted. M3 either makes it load-bearing for composition or removes the dead patch type.

**Verdict:** PASS — decided: **deleted**

**Implementing phase:** Phase 3.3 — [conclusion](2026-07-10-phase-3.3-conclusion.md)

**Evidence:**
- `AppendChildPatch` removed from `PatchProtocol.cs` and the Kotlin sealed classes;
  `CreateNodePatch.InsertIndex` is the placement mechanism (−1 = append, explicit via the
  wire's free AuxInt field; `WidgetMapper` places via `addView(view, index)`).
- Wire kind 2 stays reserved-dormant (no ABI break), pinned by a skip-arm assertion; moves
  are remove+insert at POC fidelity.
- The decision proved load-bearing immediately: 3.4's echo-panel mid-list `InsertIndex 2` and
  3.5's navigation remounts both ride InsertIndex placement.

### DoD #11 — Decision log committed

> **Decision log committed.** Same pattern as M1/M2: design + plan doc per phase, plus an M3
> final-audit doc at close.

**Verdict:** PASS

**Implementing phases:** all of 3.0 through 3.5.

**Evidence:**
- Design docs: `2026-05-28-phase-3.0-design.md`, `2026-05-31-phase-3.0b-design.md`,
  `2026-07-08-phase-3.0c-redesign.md`, `2026-07-09-phase-3.0d-design.md`,
  `2026-07-09-phase-3.0e-design.md`, `2026-07-09-phase-3.1-design.md`,
  `2026-07-09-phase-3.2-design.md`, `2026-07-10-phase-3.3-design.md`,
  `2026-07-10-phase-3.4-design.md`, `2026-07-10-phase-3.5-design.md`.
- Implementation plans with the `-implementation-plan` suffix for 3.0a, 3.0b, 3.0c, 3.0d,
  3.0e, 3.1, 3.2, 3.3, 3.4, 3.5.
- Conclusion docs per phase (spike-conclusion for 3.0a/3.0b/3.0c; conclusion for
  3.0d/3.0e/3.1/3.2/3.3/3.4/3.5) — including two honest reversals recorded in place (3.0b's
  Gate-4 RED → pivot → superseded; 3.3's Region diagnosis corrected in 3.4).
- This audit doc: `2026-07-10-milestone-3-final-audit.md`.

## Scaffolding ledger

Settled during Phase 3.5 per the design's locked decisions:

| Artifact | Fate | Rationale |
|---|---|---|
| `blazornative_run_trim_probes` export + `TrimProbes.cs` | **DELETED** (`a23114a`) | 3.0c Gate-4 diagnostic; its validation (the 4 accepted IL2072 trim paths inside the trimmed binary) is superseded by real components exercising the same paths under strict mode on all three surfaces. |
| `blazornative_run_bridge_probes` export + `BridgeProbes.cs` | **DELETED** (`a23114a`) | 3.1 diagnostic; superseded by production bridge use (navigation host-notify, storage, `HttpClient` fetch) under strict mode. |
| Probe Kotlin bindings + probe-calling tests | **DELETED** (`a23114a`) | Die with their subjects — with per-test judgment: `storage_persists_via_sharedpreferences` survives (drives handlers directly, never called the exports). |
| `TrimValidationProbes` (test project) | **STAYS** | Host-CLR trim-shape regression coverage; test scaffolding, not shipped surface. |
| `CompositionProbe` (registry) | **STAYS** | The only on-device multi-component regression surface (4 instrumented tests); Bn* components exercise overlapping but not identical shapes (keyed `@foreach`, interleaved insert/remove). |
| `BnDemo` / `BnSettingsPage` | **STAY** (not scaffolding) | The launcher demo and the on-device proof surface for DoD #4/#5/#6/#7. |

Result: the shipped C-ABI is the final **eight-export surface**, verified by dumpbin (win-x64)
+ `llvm-readelf --dyn-syms` (both bionic RIDs) — exactly the 8 `blazornative_*` symbols, probe
symbols gone.

## Test counts at M3 close

```
.NET                          → 177 passed / 2 skipped / 0 failed   (dotnet test; strict fixtures)
JVM  (testDebugUnitTest)      →  32 passed / 0 failed               (win-x64 NativeAOT dll in-process)
Android (connectedAndroidTest)→  32 passed / 0 failed               (blazornative-pixel6-x86_64; runner-strict)
```

The 2 skipped .NET tests are the same M1-era deferrals (allocation budget → M4). For scale:
M2 closed at ~23/11/19 across the three surfaces; M3 closes at 177/32/32 — with the entire
wasmtime layer deleted in between.

## Operational observations

1. **The M2 wasmtime flake is structurally gone.** Phase 2.4b's 1/N Android flake retired with
   the wasmtime layer in 3.0e; zero occurrences possible since. Demo cold-boot went from ~36 s
   (wasm) to ~1.6 s (NativeAOT) at the 3.0d cutover.
2. **Strict mode is now enforced by construction** on all three surfaces (fixtures + the 3.5
   `BlazorNativeTestRunner`); the per-class `Os.setenv` convention and its ordering contracts
   are deleted.
3. **Zero-iteration mirror gates became the norm** — 3.3 Gate 3, all of 3.4's Gates 3–4, and
   3.5's Gate 2 JVM golden re-pin closed with zero or single-expect-fail iterations; the
   structural-pin transliteration template between .NET/Kotlin twins is doing its job.
4. **Two reversed diagnoses were caught and recorded honestly** (3.0b's "bionic NativeAOT
   impossible" superseded by 3.0c; 3.3's Region-drop claim corrected by 3.4's TDD-refused-RED
   + sabotage testing). Both corrections are in the primary record, not just the conclusions.

## Carryovers to next milestones

| # | Carryover | Target | Source |
|---|---|---|---|
| 1 | Host-initiated navigation (back button, deep links) over the existing `Navigate`/`CurrentRoute` plumbing | M5 | [3.5 conclusion](2026-07-10-phase-3.5-conclusion.md) |
| 2 | M6 packaging ledger: stringly `FontSize`/`Padding` (source-breaking to change), stale-echo sequence-stamping, theme-color fixture single-sourcing, paired pin-harness extraction (+ `firstMatch` ×4 instrumented copies), one-type-per-file split, **navigation lift into `BlazorNative.Navigation`**, `.razor` compilation for real `@bind` syntax | M6 | [3.4](2026-07-10-phase-3.4-conclusion.md) + [3.5](2026-07-10-phase-3.5-conclusion.md) conclusions |
| 3 | Still-open runtime items: async-handler dispatch window, dispatch-lane starvation watch, focus/blur wiring, stale-watcher re-attach keying, `RemoveComponent` bucket-scan / `TranslateToViewIndex` memoization perf ledger | M4+ (as touched) | 3.2/3.3 conclusions, restated in [3.5](2026-07-10-phase-3.5-conclusion.md) |
| 4 | `RouteChanged` subscriber isolation + the navigation honest boundaries (non-strict drain faults log-only; failed-swap blank screen; host-route-first divergence) — a diagnostics/host-error surface | M4+ | [3.5 conclusion](2026-07-10-phase-3.5-conclusion.md) |
| 5 | Analyzer rescope (WASI-era rule names/messages) + analyzer unit tests | M4 (BACKLOG P3) | 3.0e conclusion |
| 6 | Pre-production bridge items: cleartext-overlay scope, text-only fetch bodies, header collapse | M4/M7 | 3.1 conclusion |

## Phase ledger

| Phase | Status | Headline outcome |
|---|---|---|
| 3.0 — Runtime architecture decision | ✅ complete | NativeAOT-per-ABI committed 2026-05-28 (componentize-dotnet spike RED'd same day). |
| 3.0a — Renderer trim safety | ✅ complete | Annotation pass; 4 library-deep IL2072s accepted + probed; trim regression test. |
| 3.0b — NativeAOT runtime | ⚠ partial → superseded | Desktop (win-x64) foundation GREEN; Gate 4 RED on `linux-bionic-*` → documented dual-runtime pivot, later superseded by 3.0c. |
| 3.0c — Android NativeAOT via runtime-pack bypass | ✅ complete | Working bionic `.so`s from Windows on .NET 10; pivot superseded — single runtime everywhere. |
| 3.0d — Native wire protocol + renderer cutover | ✅ complete | Typed 48 B patch ABI end-to-end on the AVD; ~22× faster demo boot. |
| 3.0e — WASM-era collapse | ✅ complete | wasmtime/WasiHost/JSON wire deleted (71 files); `BlazorNative.Runtime` rename. |
| 3.1 — Shell bridge C-ABI | ✅ complete | DoD #3: six ops as host-registered callbacks + async fetch; `HttpClient` on Android. |
| 3.2 — Bidirectional events | ✅ complete | DoD #2 (+#9 partial): tap round-trip on-device; dispatch capture window; diff-cursor fix. |
| 3.3 — Composition-grade renderer | ✅ complete | DoD #8/#9/#10: slot lists, real disposal, strict mode, AppendChild deleted. |
| 3.4 — `Bn*` library + `@bind` + cascading | ✅ complete | DoD #4/#5/#6: the quartet on-device; mechanics-not-syntax bind; corrected Region diagnosis. |
| 3.5 — Navigation + M3 close | ✅ complete | DoD #7 on-device (two-page demo); probe exports retired (eight-export ABI); this audit. |

## Recommendation

Invoke `complete-milestone` — flip ROADMAP.md M3 to complete, MILESTONE.md status to complete,
and after the controller merges `phase-3.5-navigation` to `main`, tag the merge commit:

```
git tag -a v3.0 -m "Milestone 3: P2 — Real Apps Can Be Built complete"
```

M4 (P3 — Production-Shippable) opens next, mapped to BACKLOG "P3 — Production readiness"
(analyzer tests + rescope, CI pipeline, iOS shell, DevTools inspector, NuGet packaging), with
this audit's carryover table as triage input for the M4 brainstorm.
