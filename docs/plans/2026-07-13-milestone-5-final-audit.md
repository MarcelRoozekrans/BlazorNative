# Milestone 5 — Final Audit

*Date: 2026-07-13*
*Audit by: Claude (Opus 4.8), at Marcel's request, as Phase 5.5 Gate 1*
*Triggered by: Phase 5.5 — the last gate before `complete-milestone` + tag `v5.0`*

## Verdict

**PASS — all 8 DoD criteria PASS. M5 is ready for `complete-milestone` + the `v5.0` tag.**

M5 turned "per-platform NativeAOT + a thin native shell" from an architecture claim
with exactly one shell into a **two-platform fact**: the same `BnDemo` two-page app
runs interactively on the **iOS simulator** (a Swift/UIKit shell over a NativeAOT
static `.a`) and on the **Android AVD** (the Kotlin/JNA shell over a bionic `.so`),
from one runtime and one nine-export C-ABI. The named risk — could .NET 10 NativeAOT
even produce a linkable iOS artifact on free CI — was retired GREEN in the first
phase (and *exceeded*: the runtime boots on the simulator). On top of the second
shell, the Android shell gained real host-initiated lifecycle/back/deep-link ingress
(closing the `NativeEvents` fork the 4.2 triage routed here), and clipboard + share
landed on both platforms through a **documented, size-negotiated bridge-extension
pattern** — the real DoD #6 deliverable, not API breadth.

Six criteria carry honesty notes, justified per criterion below: #1's device probe
being secondary to the simulator bar, #2/#3's simulator-only posture (device/signing
deferred — no Apple Developer account), #4's **two-job-vs-single-job wording
reconcile** (the shipped `ios.yml` is a single macOS job; the substance is fully
delivered), #5's shell-side deep-link resolution + the instrumented predictive-back
gesture-commit limitation, and #6's share-sheet-unassertable seam bar + the Android
clipboard-read focus gate. None is a scope miss — each is the DoD's substance
delivered with the boundary honestly recorded. The audit also corrects two count
strings that lagged the shipped CI gates (iOS XCTest **13** not 12; .NET **0** skips
not 2) and the MILESTONE DoD #4 "two-job" wording — flagged under "Honesty fixes
applied in this gate" below.

## Per-criterion verification (2026-07-13)

### DoD #1 — iOS feasibility spike verdict committed

> A time-boxed spike on a free macOS runner proves (or refutes) that .NET 10
> NativeAOT produces a linkable iOS-simulator artifact carrying the `blazornative_*`
> C-ABI (symbol dump as evidence; device `ios-arm64` probed secondarily). [...] this
> criterion passes with EITHER verdict; what it demands is the *committed evidence*.

**Verdict:** PASS (GREEN — exceeded)

**Implementing phase:** Phase 5.0 — [spike conclusion](2026-07-12-phase-5.0-spike-conclusion.md)
(PR #47, `16a637a`)

**Evidence:**
- On a free public-repo `macos-latest` runner, the **runtime-pack bypass** (the same
  mechanism 3.0c used for `linux-bionic`: `PublishAotUsingRuntimePack=true` +
  `DisableUnsupportedError=true` + `RuntimeFrameworkVersion=10.0.9`, RID-gated in
  `BlazorNative.Runtime.csproj`) publishes a linkable `.dylib` for **both**
  `iossimulator-arm64` (load-bearing) and `ios-arm64` (secondary device probe).
- `nm -gU` dumps **all eight** then-current `blazornative_*` exports; `file`/`lipo`
  confirm a Mach-O arm64 simulator slice; a ~30-line C-stub **link probe** builds a
  simulator executable (`clang` exit 0).
- **Beyond the bar:** the stub was `simctl spawn`-run on a booted simulator and the
  runtime executed end-to-end — `blazornative_init` returned status 0 and
  `blazornative_version` returned the correct version cstring through the C-ABI. The
  runtime *boots*, not just links. The pre-decided fallback ladder (Mono-AOT,
  milestone re-scope) was **not triggered**.

**Honesty note:** the DoD names the simulator artifact as the bar and the device
(`ios-arm64`) as a *secondary* probe — both published and passed the symbol/link
checks, but device runtime execution was never in scope (needs an Apple Developer
account + hardware). The spike shipped a `.dylib`; the production app-embed shape (a
static `.a`) was correctly deferred to 5.2 as a packaging decision, not a feasibility
question. The criterion demanded committed evidence of a verdict; it got a GREEN
verdict that exceeded the linkability bar.

### DoD #2 — Swift shell boots and renders

> The Kotlin shell's Swift twin (bindings, frame adapter, widget mapper over native
> views) boots the dll on the CI simulator and renders BnDemo's widget tree.

**Verdict:** PASS

**Implementing phase:** Phase 5.2 — [conclusion](2026-07-12-phase-5.2-conclusion.md)
(`229d3e3`)

**Evidence:**
- `src/BlazorNative.Apple/` is the imperative twin of the Android `WidgetMapper`:
  `BnFrameAdapter` reads rt→shell frames at the exact 48/24-byte offsets
  (`loadUnaligned`, strings copied in-callback — arena-safe); `BnWidgetMapper` maps
  `view→UIStackView`, `text→UILabel`, `button→UIButton`, `input→UITextField`, with
  the text-collapse and mid-list-insert idioms; `BnRuntime` drives
  init→register_frame_callback→mount via singleton-routed `@convention(c)` callbacks.
  Swift's native C interop replaces the JNA layer entirely (no reflection).
- The **load-bearing discovery** (the reason the phase existed): linking a NativeAOT
  **static `.a`** into an Xcode app — `bootstrapperdll.o` as a *direct* object (its
  `__attribute__((constructor))` inits the runtime; as an archive member it is never
  pulled → SIGSEGV in `blazornative_init`) + `-force_load` the app `.a` + a merged
  on-demand `libBnRuntimeSupport.a` + the 5.0 spike frameworks. This foundation 5.3
  and any device build inherit.
- A hosted **XCTest** (`BnRenderTests.testBnDemoRendersCanonicalTree`) boots→mounts→
  asserts the **real `UIView` tree**: 6 arranged subviews in order, the mid-list echo
  panel at index 2, `#FFEEAA` background, button titles, title fontSize 24. Simulator
  log shows `native init ok — BlazorNative.Runtime 1.3.0-phase-5.1 → mounted BnDemo`.
  Green run [29196852504](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/29196852504).

**Honesty note:** the shell boots and renders on the **simulator** only; a device
build is deferred (DoD scope). No Runtime source changed — the only .NET delta is
`<NativeLib>Static</NativeLib>` in the iOS-only RID PropertyGroup (behavior-neutral
for win-x64/bionic, verified) — so the version stayed `1.3.0-phase-5.1`, honestly
recorded.

### DoD #3 — Two-page demo parity on the simulator

> The headline: bound input + live echo, button events, cascading theme, and
> Settings ⇄ Back navigation, all on the iOS simulator, mirroring the Android v3.0 bar.

**Verdict:** PASS

**Implementing phase:** Phase 5.3 — [conclusion](2026-07-12-phase-5.3-conclusion.md)
(`3996e9e`)

**Evidence:**
- A serial `DispatchQueue("BlazorNative-Dispatch")` crosses taps to
  `blazornative_dispatch_event`; `BnWidgetMapper` wires `AttachEvent` to UIControl
  targets (`UIButton .touchUpInside` / `UITextField .editingChanged`, last-wins,
  identity cleanup) via a retained `BnControlTarget`; `BnFlatJson` writes the args
  byte-exact to the Kotlin `FlatJson`. `AppleShellBridge` supplies all six
  `@convention(c)` callbacks (navigate/current-route real via the `-needed` buffer
  protocol; storage in-memory; fetch fails synchronously), registered before mount.
- Interactive hosted XCTests grew **2 → 9**: bind+echo (the `héllo→世界` UTF-8 leg,
  input not clobbered), Clear, Theme flip **both directions** (form + echo panel
  `#FFEEAA ⇄ #334455`), Settings→ (no `UITextField` anywhere — the input left the
  screen), ←Back (fresh empty `BnDemo` remount). **Green on the first CI attempt**
  (3m20s, run [29199173344](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/29199173344)).
- **Zero shared-code change** — Swift + `ios.yml` only (`dispatch_event`/
  `register_bridge` already existed and are used by Android); `.NET 220 / JVM 78 /
  Android 38` untouched.

**Honesty note:** parity is reached on the **simulator**, mirroring the Android v3.0
bar — not on a device. The `@bind` write-back is genuinely simpler on iOS than
Android (UIKit doesn't fire `.editingChanged` on a programmatic `.text` set, so the
bind loop cannot form — no re-entrancy guard needed); this is a real platform
difference, recorded and pinned by `testBindLoopTypeEchoesAndInputNotClobbered`, not
a shortcut. The storage/fetch bridge callbacks are honest stubs (in-memory /
fail-synchronously) because `BnDemo` uses neither — real impls land when a component
needs them.

### DoD #4 — iOS CI lane

> A macOS two-job workflow (publish → simulator tests), informational-first with
> promotion criteria, mirroring the Android emulator lane's posture.

**Verdict:** PASS (with a wording reconcile — see the honesty note)

**Implementing phase:** Phase 5.2 — [conclusion](2026-07-12-phase-5.2-conclusion.md)
(`229d3e3`)

**Evidence:**
- `.github/workflows/ios.yml` on `macos-latest`: publish `-r iossimulator-arm64`
  (csproj bypass + `NativeLib=Static`; assert exactly **4 IL2072**) → `nm -gU` asserts
  the **9** `blazornative_*` exports → assemble the static-embed link inputs →
  XcodeGen + `xcodebuild test` on a **runner-selected** simulator (`simctl list … |
  jq` — robust to Xcode device-name drift) → **assert the XCTest baseline** (drift
  fails loudly).
- **INFORMATIONAL** (not a required check), mirroring `android-instrumented.yml`,
  with documented **promotion criteria** (≈10 consecutive green runs on main →
  promotable). Triggers: `workflow_dispatch` + `pull_request` (iOS paths);
  public-repo macOS minutes are free. Uploads everything each run (publish log, nm
  dump, xcodebuild log, crash reports, `.xcresult`). The 5.0 dispatch-only spike
  workflow + its verify script + the throwaway wrapper were retired (superseded).

**Honesty note — the two-job vs single-job reconcile (the tracked wording nit):**
the MILESTONE DoD #4 prose (and this criterion's open-work wording) estimated a
**"two-job (publish → simulator tests)"** workflow, mirroring Android's
Windows-publish → Ubuntu-emulator split. The shipped `ios.yml` is a **single macOS
job**: iOS both publishes AND tests on the *same* `macos-latest` runner, because —
unlike Android — the publish host and the test host are the same platform, so there
is nothing to hand off between jobs. The **substance** the DoD asked for is fully
delivered: an informational macOS lane that publishes, verifies the 9-export ABI +
the 4 IL2072s, runs the simulator XCTest suite, and carries promotion criteria in the
Android-lane posture. This is a job-topology deviation, not a capability gap. The
5.2/5.4 conclusions already flagged it for this audit; **the MILESTONE DoD #4 wording
is corrected to "single job" in this same gate** (see "Honesty fixes applied").

### DoD #5 — Host-initiated events land (Android + .NET)

> The `NativeEvents` redesign: lifecycle (`onPause`/`onResume`/`onDestroy`) flows into
> .NET as native events; predictive back triggers navigation-back; a deep link
> resolves to the startup route — proven on the AVD. Closes the 4.2-triaged fork.

**Verdict:** PASS

**Implementing phase:** Phase 5.1 — [conclusion](2026-07-12-phase-5.1-conclusion.md)
(`99783d1`)

**Evidence:**
- A **9th C-ABI export** `blazornative_host_event(name, payload)` fires the *real*
  `NativeShellBridge.NativeEvents` multicast (the 3.2 no-op is gone; `GetInvocationList`
  + per-subscriber try/catch isolation; rc 0/2/3). `BN0014` now guards a live
  `NativeEvents` contract.
- On the AVD (API 34): Android lifecycle (`onPause`/`onResume`/`onDestroy`) marshals
  into the export (each guarded by a `booted` flag; **onDestroy fires but never
  `shutdown`s** — the Activity-recreation trap, documented at the site); predictive
  back (`OnBackInvokedCallback` → `dispatchHostEventAndWait("back")`) routes through
  the reserved `"back"` name **mapped in .NET** (intercepted before the multicast; rc
  1 = not-handled → shell finishes) to a new `NavigateBackAsync` (single previous-route
  slot, cleared after a back — no ping-pong); a launch-time `blazornative://<route>`
  deep link starts the app on the linked page. `HostEventProbe` is the on-device
  consumer proving a host event reaches a mounted component and re-renders it.
- The 4.2-triaged `NativeEvents` fork is **closed**; counts `.NET 220 / JVM 78 /
  Android 38`; version `1.3.0-phase-5.1`; the ABI grew eight → nine in one gate
  (cheapest ripple, before the Swift shell existed).

**Honesty note:** two documented deviations, both robust and ledgered. (a)
**Deep-link component resolution is shell-side** — the .NET 3.5 first-mount route-honor
fires only on a session's first mount, which never holds under Activity recreation or
the shared-process instrumented session, so the shell resolves the target component
directly (still by name, per the design) while seeding the route slot for
`CurrentRoute` consistency; on a genuine cold launch the two mechanisms are mutually
exclusive and agree. The route→component map now lives in **both** .NET and the shell
— **unify at M6** when nav lifts into a package. (b) **Predictive-back is driven
through the single `onBackPressed()` entry** in the instrumented test, not a synthetic
gesture — both `KEYCODE_BACK` and `GLOBAL_ACTION_BACK` start a gesture that *cancels*
under instrumentation without committing to `onBackInvoked`; the test drives the
identical production logic the registered callback delegates to (registration
confirmed in logcat). Back-at-root → finish (rc 1) is covered by the JVM + .NET
routing tests rather than on-device. Launch-time-only deep links (`onNewIntent` for
already-running) and https App Links are out of scope (M5.3+/M7).

### DoD #6 — Clipboard + share on both platforms, bridge-extension pattern documented

> Clipboard + share on both platforms, with the bridge-extension pattern documented
> (how a new host API joins the C-ABI: struct slot vs new callback, versioning
> posture, per-platform impl shape).

**Verdict:** PASS

**Implementing phase:** Phase 5.4 — [conclusion](2026-07-12-phase-5.4-conclusion.md)
(`bc4f3d7`); pattern in [docs/bridge-extension.md](../bridge-extension.md)

**Evidence:**
- The shell bridge grew **6 → 9 callbacks** (`ClipboardRead@48`, `ClipboardWrite@56`,
  `Share@64`; struct 48 → 72 bytes) in lockstep across .NET + JVM + the Swift header —
  but through a **versioned, size-negotiated** `register_bridge`, not a raw lockstep
  edit: a leading `structSize` + `Math.Clamp(structSize,0,72)` min-copy + zero-fill +
  `RequireSlot`, so an unsupplied slot surfaces as `NotSupportedException`. The bridge
  is now forward- **and** backward-compatible (an old 48-byte shell interoperates with
  the new 72-byte runtime), pinned by the 48-byte old-shell and negative-structSize
  tests. **Exports unchanged at 9** — a struct grow + a one-symbol signature change,
  not an export grow.
- Clipboard is **real on both platforms** (`ClipboardManager` / `UIPasteboard`), share
  is **real** (`ACTION_SEND` / `UIActivityViewController`), each mounted through the
  `ClipboardProbe` scaffolding component and asserted at three layers (.NET in-process,
  JVM through the dll, instrumented/XCTest against the real platform API).
- The **deliverable is the pattern** — [docs/bridge-extension.md](../bridge-extension.md)
  documents the bridge model, the size-negotiation, the step list to add a callback,
  the honest test bar, and clipboard+share as the fully-worked example. It is the M6+
  recipe for camera/geo/biometrics.

**Honesty note:** (a) **Share end-to-end is unassertable** — the system share sheet /
activity controller cannot be asserted under instrumentation (and popping it would
hang the test), so both platforms assert at the **callback-content bar** via a capture
seam (`shareLaunchHook` / `shareHook`): the `ACTION_SEND` `EXTRA_TEXT` + MIME / the
`UIActivityViewController` activity items, not the UI. This is documented as the
honest bar in the pattern doc. (b) **Android clipboard *reads* are focus-gated** —
Android 10+ denies `getPrimaryClip` without window focus, so the on-device test waits
on `hasWindowFocus()` and the lane sets `hide_error_dialogs 1` (a boot ANR would else
steal focus); the read path is *also* proven focus-free at the .NET/JVM layers. Both
boundaries are the platform's, honestly recorded.

### DoD #7 — Every new surface is CI-asserted

> Test counts recorded and asserted at each phase close (the M4 discipline continues);
> the iOS lane's counts join them when the lane stabilizes.

**Verdict:** PASS

**Implementing phases:** all of 5.0–5.4; closed by this audit.

**Evidence:**
- **Four count gates, all assert-don't-observe** (drift fails loudly, in the same
  commit as the change): `ci.yml` asserts **.NET 230 passed / 0 skipped / 0 failed**
  and **JVM 79 passed / 0 failed**; `android-instrumented.yml` asserts **40 passed /
  0 failed**; `ios.yml` asserts **13 passed / 0 failed** on the simulator. Every phase
  that grew a surface bumped the matching baseline in lockstep (5.1 bumped ci.yml ×3
  RID export arrays 8→9; 5.4 bumped all four count gates + the struct-drift pins).
- The iOS lane's counts **joined** the asserted set the moment the lane existed (5.2 =
  2, 5.3 = 9, 5.4 = 13), exactly as the DoD anticipated ("when the lane stabilizes").
- Export/trim invariants also asserted every publish: **9 `blazornative_*` exports**
  (win-x64 dumpbin + both bionic `llvm-readelf` + iOS `nm -gU`) and **4 IL2072** per
  RID, unchanged.

**Honesty note:** the iOS lane is **informational** (not a required check), matching
the DoD's own "when the lane stabilizes" language and the Android emulator lane's
posture — its counts are asserted-when-run, and promotion to a required check awaits
the recorded ≈10-consecutive-green baseline. This is the same informational-job
posture the M4 audit accepted for the Android instrumented lane.

### DoD #8 — Decision log committed

> Design + plan + conclusion per phase, plus the M5 final audit at close → tag `v5.0`.

**Verdict:** PASS

**Implementing phases:** all of 5.0–5.5.

**Evidence:**
- A **design + conclusion** (and an implementation plan for the non-spike phases) for
  every phase: 5.0 (spike — design + conclusion, spike convention, no separate plan),
  5.1, 5.2, 5.3, 5.4 — all under `docs/plans/2026-07-12-phase-5.*`; the size-negotiated
  bridge pattern captured as a standalone deliverable in `docs/bridge-extension.md`.
- **Honest corrections recorded in place:** 5.1's NavigateToAsync slot-cycle wording
  precisening, the 5.2/5.4 flags of the DoD #4 two-job wording for this audit, and the
  iOS XCTest count reconcile surfaced here.
- This audit doc: `2026-07-13-milestone-5-final-audit.md`, plus the Phase 5.5
  conclusion. Tag command recorded for post-merge:
  `git tag -a v5.0 -m "Milestone 5: P4 — Full Platform Coverage complete"`.

## Honesty fixes applied in this gate

Docs-only, flagged for the controller (no code/test/workflow touched):

1. **MILESTONE.md DoD #4 wording: "two-job" → "single job".** The DoD #4 open-work
   prose estimated a two-job workflow; the shipped `ios.yml` is a single macOS job
   (publish + test on one runner). Corrected in place — a one-line honesty fix
   authorized for this gate. The CLOSED note already carried the "Single job"
   deviation; this aligns the DoD headline sentence with it.
2. **iOS XCTest count string: 12 → 13** (MILESTONE.md DoD #6). The shipped `ios.yml`
   **asserts 13 passed** (5.2 render pin + 5.3 wire-drift guard + BnInteractionTests ×5
   + BnBridgeTests ×2 = 9; 5.4 adds BnClipboardTests ×3 = 12; **plus** the
   `bn_bridge_callbacks` struct-drift pin `BnDriftTests` case = 13). The 5.4 conclusion
   prose (and MILESTONE/bridge-extension/ROADMAP) said "12" because the 13th test
   (commit `c9ac4f2`) landed in PR #51 *after* the 5.4 conclusion (70c5df4) was written.
   CI is the ground truth → **13**. Corrected the MILESTONE DoD #6 count string; the
   historical conclusion/ROADMAP prose stays as period record (this audit is the
   reconcile of record). This audit's "counts at close" uses **13**.
3. **.NET skip count.** Some in-session shorthand carried a "2 skipped" for .NET; the
   shipped `ci.yml` asserts **0 skipped** (zero skips since the M4 close, when 4.2's
   allocation test consumed the last M1-era skip). This audit's counts use **0
   skipped** — matching what CI actually gates. No doc edit needed (MILESTONE/ROADMAP
   already carry the correct raw counts).

## Scaffolding ledger

Test seams and probes alive at M5 close — all internal/test-only, none on the shipped
C-ABI (still exactly the nine `blazornative_*` exports, CI-asserted across win-x64 +
both bionic RIDs + the iOS simulator):

| Artifact | Location | Status / rationale |
|---|---|---|
| `HostEventProbe` | registry component (`"HostEventProbe"`, Runtime) | **STAYS** — 5.1's on-device host-event consumer: a mounted component that subscribes `NativeEvents` (sync, BN0014-clean) and renders a nodeId-pinned echo, proving a host event lands and re-renders. Never a shipped path. |
| `ClipboardProbe` | registry component (`"ClipboardProbe"`, Runtime) | **STAYS** — 5.4's bridge-capability consumer (`[Inject] IMobileBridge`, Copy/Paste/Share); the component all three shells mount to drive the clipboard round-trip + share seam. |
| `FocusProbe` | `src/BlazorNative.Runtime/FocusProbe.cs` | **STAYS** (M4 ledger reaffirmed) — 4.2's focus/blur on-device regression surface. |
| `CompositionProbe` | registry component | **STAYS** (M3/M4 ledger reaffirmed) — the on-device multi-component regression surface. |
| `TrimValidationProbes` | test project | **STAYS** (M3/M4 ledger reaffirmed) — host-CLR trim-shape regression coverage; never a shipped surface. |
| `shareLaunchHook` (Android) / `shareHook` (iOS) | `AndroidShellBridge.kt` / `AppleShellBridge.swift` | **STAYS** — 5.4 share-capture seams: the only honest, non-hanging way to assert a share affordance under instrumentation (the system sheet is unassertable). Process-static/test-scoped; production path launches the real chooser/controller. |
| `ReplaceRegistryEntryForTests` | `HostSession` (internal) | **STAYS** (M4 ledger reaffirmed) — registry-swap seam; production ABI never calls it. |
| `TriggerRootRenderForTests` | `NativeRenderer` (internal) | **STAYS** (M4 ledger reaffirmed) — the allocation-budget deterministic-re-render seam. |
| `dispatchEventBlocking` / `dispatchHostEventAndWait` (shells) | Swift `BnRuntime` / Kotlin runtime | **STAYS** — inline/blocking test-and-back-decision seams; the async lane is the production path. |

The shipped export surface grew exactly once in M5 — **8 → 9** (5.1's
`blazornative_host_event`, a deliberate platform-neutral ABI addition before the Swift
shell existed). 5.4's bridge growth was a *struct* grow (6 → 9 callbacks / 48 → 72
bytes), **not** an export grow — the export count held at 9.

## Test counts at M5 close

```
.NET                           → 230 passed / 0 skipped / 0 failed   (CI-asserted, ci.yml)
JVM  (testDebugUnitTest)       →  79 passed / 0 failed               (CI-asserted, ci.yml)
Android (connectedAndroidTest) →  40 passed / 0 failed               (android-instrumented.yml; informational)
iOS  (XCTest, simulator)       →  13 passed / 0 failed               (ios.yml; informational)
```

Version `1.4.0-phase-5.4` everywhere (Runtime `Exports.cs`, both Kotlin assertions,
the five package csprojs). ABI: **9 exports**, the **72-byte 9-callback** size-negotiated
bridge. For scale: M4 closed at 203 / 73 / 35 (three surfaces); M5 closes at 230 / 79 /
40 / **13** — a **fourth, cross-platform** asserted surface (the iOS simulator lane),
the headline of the milestone.

## Operational observations

1. **The C-ABI proved it is the portable seam.** A second shell in a different
   language (Swift, native C interop, no JNA) read the *exact same* 48/24-byte frame
   wire and 72-byte bridge struct the Kotlin and .NET shells use — pinned by a **third**
   drift guard (`BnDriftTests`) alongside the JVM and .NET ones, so a protocol change
   now breaks all three shells loudly. The milestone's thesis ("a second platform is
   the only honest test of the per-platform-NativeAOT claim") held.
2. **First-attempt greens continued.** 5.3's interactive iOS lane and 5.4's cross-
   platform bridge growth both went green on the first CI attempt — the
   assert-don't-observe posture and byte-exact contract survey did their job. The one
   hard multi-commit fight (5.2's static-`.a` link recipe) was a genuine discovery, not
   a process miss, and is now documented for device inheritance.
3. **The bridge grew without an ABI break.** The size-negotiation (min-copy +
   zero-fill + clamp + `RequireSlot`) let clipboard/share land with the export count
   unchanged and old/new shell↔runtime combinations interoperating — the export-count
   gates in all four workflows were untouched by the growth, exactly as designed.
4. **Three doc-vs-CI reconciles recorded** (the #4 two-job wording, the iOS XCTest
   12→13 count, the .NET skip count) — small, but the record stays honest at the
   sentence level, consistent with M3/M4's reversal-recording practice. All three are
   doc lags behind a correct shipped CI gate, not defects in the gate.

## Carryovers to next milestones

| # | Carryover | Target | Source |
|---|---|---|---|
| 1 | Real-device iOS, code signing, App Store validation (needs an Apple Developer account); the support-lib denylist → allowlist + RID generalization for the device build | M6+ / as-account-exists | milestone-open scope decision; [5.2](2026-07-12-phase-5.2-conclusion.md)/[5.3](2026-07-12-phase-5.3-conclusion.md) conclusions |
| 2 | More host APIs (camera, geolocation, biometrics, purchases, background tasks) via the [bridge-extension pattern](../bridge-extension.md) — the step list is the recipe; export count holds at 9 per added slot | M6+ | [5.4 conclusion](2026-07-12-phase-5.4-conclusion.md) |
| 3 | route→component registry unify (`NativeNavigationManager.s_routes` + the shell `DEEP_LINK_COMPONENTS` duplicate) when nav lifts into a package | M6 | [5.1 conclusion](2026-07-12-phase-5.1-conclusion.md) |
| 4 | `onNewIntent` (already-running) deep-link nav; https App Links / domain verification; back-stack beyond one slot | as-needed / M7 | [5.1 conclusion](2026-07-12-phase-5.1-conclusion.md) |
| 5 | iOS lane promotion to a required check (≈10 consecutive green runs on main); the Swift mapper's stubbed node types (image/scroll/picker) + storage/fetch bridge stubs → real when a component needs them | M6+ | [5.2](2026-07-12-phase-5.2-conclusion.md)/[5.3](2026-07-12-phase-5.3-conclusion.md) conclusions |
| 6 | On-device inspector channel (state/JSON pieces already Android-safe) — the iOS lane now exists, so a cross-platform diagnostics channel is revisitable | M6 | [4.4 conclusion](2026-07-11-phase-4.4-conclusion.md) (M4 carryover, unchanged) |
| 7 | Re-ledgered hardening items **#8/#9/#12/#13** (async-handler capture window + dispatch-lane starvation; RemoveComponent bucket scan + TranslateToViewIndex memoization) — revisit triggers unchanged; none re-opened by any M5 phase | as-triggered | [4.2 triage doc](2026-07-11-phase-4.2-hardening-triage.md) (M4 carryover, unchanged) |
| 8 | The M6 packaging/ecosystem ledger — `BlazorNative.Components`/`.Styling`/`.State`/`.Navigation` packages, the CLI global tool, a docs site, **nuget.org publication** (M4-deferred; `PackageReadmeFile` prerequisite), `.razor` compilation, stringly FontSize/Padding | M6 | M3/M4 audits + 4.5 conclusion (unchanged) |

## Phase ledger

| Phase | Status | Headline outcome |
|---|---|---|
| 5.0 — iOS feasibility spike | ✅ complete | GREEN + exceeded: the runtime-pack bypass produces a linkable iOS-simulator `.dylib` (all 8 exports) AND the runtime boots via the C-ABI on the sim. Fallback ladder not triggered. |
| 5.1 — Host-initiated events | ✅ complete | 9th export `blazornative_host_event` → real `NativeEvents`; lifecycle + predictive-back-→-NavigateBack + launch deep link, live on the AVD. The 4.2 `NativeEvents` fork closed. |
| 5.2 — Swift shell foundation | ✅ complete | First non-Kotlin shell: BnDemo renders on the CI simulator through a Swift/UIKit shell over the NativeAOT static `.a` (the direct-`bootstrapperdll.o` link recipe). New informational `ios.yml`. |
| 5.3 — Swift shell interactivity | ✅ complete | Interactive two-page BnDemo on the simulator — iOS reaches the Android v3.0 bar (bind/echo, Clear, theme, Settings⇄Back). Zero shared change; green first CI try. |
| 5.4 — Clipboard + share + pattern | ✅ complete | Clipboard + share real on both platforms through a size-negotiated bridge (6→9 callbacks, exports still 9); the pattern documented in `docs/bridge-extension.md`. |
| 5.5 — M5 final audit + close | ✅ complete | This audit (8/8 PASS); the two-platform milestone closed; ROADMAP/MILESTONE flipped; `v5.0` tag command recorded. |

## Recommendation

Invoke `complete-milestone` — flip ROADMAP.md M5 to complete, MILESTONE.md status to
complete + closure notes (with the DoD #4 wording and DoD #6 count honesty fixes),
open the M6 pointer (P5 — Developer Ecosystem), and after the controller merges
`phase-5.5-m5-audit` to `main`, tag the merge commit:

```
git tag -a v5.0 -m "Milestone 5: P4 — Full Platform Coverage complete"
```

M6 (P5 — Developer Ecosystem) opens next, mapped to BACKLOG "P5 — Developer experience
and ecosystem" (`BlazorNative.Components`/`.Styling`/`.State`/`.Navigation` packages,
the `BlazorNative.Cli` global tool, full test infrastructure, a CI/CD release
pipeline, a documentation site, **nuget.org publication** — the M4-deferred item — and
`.razor` compilation), with this audit's carryover table as triage input for the M6
brainstorm. M6/M7 are explicitly parallel with M5's now-closed scope.
