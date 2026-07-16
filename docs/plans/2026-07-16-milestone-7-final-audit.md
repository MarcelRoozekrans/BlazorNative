# Milestone 7 — Final Audit (Phase 7.6, Gate 3)

**Date:** 2026-07-16
**Auditor:** Phase 7.6 Gate 3, against `phase-7.6-close` @ `7aaa424` (the `v7.0` candidate
tree — the tag is applied AFTER this PR merges, on the owner's go)
**Predecessor:** [M6 final audit](2026-07-14-milestone-6-final-audit.md) (PASS on all eight
after the 2026-07-15 amendment, tagged `v6.0`)
**Contract audited:** [`docs/planning/MILESTONE.md`](../planning/MILESTONE.md) — the eight
M7 DoD criteria

## Method

Evidence-based, per the house rule: **"asserted, not observed"**, and **"a mechanism nobody
tested is a mechanism nobody knows."** Applied to the audit itself — **no criterion is
accepted because a conclusion doc says so.** Every verdict below is checked against git
history, the live GitHub API, actual CI run logs, and the code — and everything locally
runnable was **re-run live at the audited tip** rather than cited:

- **.NET suite, re-run at `7aaa424`:** `dotnet test` (Release) — **539 passed / 0 skipped /
  0 failed** (Renderer.Tests 132 + Runtime.Tests 384 + Analyzers.Tests 23).
- **JVM suite, re-run at `7aaa424`:** `gradlew testDebugUnitTest` — **106 passed / 0 failed
  / 0 errors / 0 skipped across 19 suites** (summed from the JUnit XML, the ci.yml method).
- **Publish gate, re-run at `7aaa424`** (clean native obj, full ILC pass): win-x64 publish
  shows **exactly 4 IL2072** trim warnings; `dumpbin /exports` (via vswhere) shows **exactly
  the 9 `blazornative_*` exports** — `dispatch_event, fetch_complete, host_event, init,
  mount, register_bridge, register_frame_callback, shutdown, version`.
- **Branch protection, read back live:** `required_status_checks.contexts ==
  ["build-test", "ios-build", "android-build"]` (strict: true, enforce_admins: false — the
  M6-recorded residual, unchanged).
- **Every referenced device run verified via `gh run view`** — existence AND green
  conclusion, never trusted from a doc (the table below).

**Verdict: PASS on all eight.** Findings that do not block are recorded as findings (the M6
discipline), never rounded up.

---

## Verdict table

| # | Criterion | Verdict | Evidence (re-verified live) |
|---|---|---|---|
| 1 | Razor-compilation spike verdict committed | **PASS** | [7.0 spike conclusion](2026-07-15-phase-7.0-spike-conclusion.md) records **"Verdict — GREEN"** with the pinned recipe (`Microsoft.NET.Sdk.Razor` + `StaticWebAssetsEnabled=false`), the golden-vs-twin byte-identity proof (`SpikeRazorTests`), and the fallback ladder closed. **Trim evidence re-checked on the current publish:** the audited tip still publishes with exactly **4 IL2072 + 9 exports** (run live, above) — the SDK switch added nothing to the NativeAOT lane, still true at milestone close. |
| 2 | The demo app is authored in `.razor` | **PASS** | The five DoD pages (`BnDemo`, `BnSettingsPage`, `BnLayoutDemo`, `BnScrollDemo`, `BnImageDemo`) all exist as `.razor` — grep-verified at the tip, alongside the pages M7 itself added (`/list`, `/form`, `/modal`, `/imagepolish` — authored `.razor` from day one). `BuildRenderTree` survives only in hand-written **leaf components** (`BnButton`, `BnImage`, `BnInput`, `BnActivityIndicator` — the last deliberately: the 7.4 zero-attribute-element finding), which DoD #2 never covered. Parity was proven at conversion time with **zero golden edits, zero shell edits** ([7.1 conclusion](2026-07-15-phase-7.1-conclusion.md)); the two recorded pre-1.0 breaking changes (`BnThemedPanel` `internal`→`public`, `FontSize`/`Padding` `string?`→`float?`) stand recorded, no shim. Device runs on the converted app verified live: **29420994993** (android-instrumented, success, 111/0) and **29420996916** (ios, success, 72/0), both on `phase-7.1-razor-authoring@5961886`. |
| 3 | `BnList` + the `onScroll` wire | **PASS** | [7.2 conclusion](2026-07-15-phase-7.2-conclusion.md): shell-side conflation on the existing wire (no ABI change), throughput evidence on both platforms (Android burst 100 samples → 2 dispatches, 50:1; fling 78 → 35; final offset always delivered; iOS mirrors on the same `/list` numbers), counted liveness **11/15/11 of 500** asserted on .NET and both shells. Run **29435524820** verified live (ios, success, `phase-7.2-bnlist@7673d6c`). Conflation mutation-proven at the mechanism on both shells — the iOS mutations ran ON CI (29436079300, 29436083505 — red as expected). The suites carrying these pins are green at the tip (539/106 re-run live; the shell twins ride the device baselines below). |
| 4 | Form controls + a real `picker` | **PASS** | [7.3 conclusion](2026-07-15-phase-7.3-conclusion.md): NodeTypes **8/9/10 pinned on all three mirrors** — re-verified at the tip: `FrameEncoderTests`' all-NodeTypes theory + integer wire-id pin (.NET, in the 539), `NativeFrameAdapterTest.nodeTypes_vocabulary_is_pinned_content_and_length` (JVM, in the 106), `BnDriftTests`' thirteen-entry literal (iOS, in the 154). The normative clamp rule (the CLAMPED index is dispatched) asserted on both device lanes; the per-control guard asymmetry verified per platform. Run **29451417339** verified live (ios, success, `phase-7.3-form-controls@e9a7376`). |
| 5 | `BnModal` | **PASS** | [7.4 conclusion](2026-07-16-phase-7.4-conclusion.md): the overlay-in-the-existing-root decision (native dialog rejected with reasons — a second Yoga root + a self-dismissing window violating the state-owner law), the anchor+overlay model (the third index-mapping rule), **dismissal as a REQUEST** on the `click` wire (the shell never self-closes; Android back consults the modal stack first), the two-subtree fixpoint purge exactly-once. Run **29487073302** verified live (ios, success, `phase-7.4-modal@2e24d7d`). This phase's H4 closed the model's one named pin gap (the indexed insert at root — see the 7.6 section below). |
| 6 | `BnImage` polish | **PASS** | [7.5 conclusion](2026-07-16-phase-7.5-conclusion.md): **zero new measurement states** — the 6.3 contract re-proven verbatim with the features present (`/imagepolish`); `PlaceholderColor` paint-only (4-row state table normative both shells), `OnError` at-most-once per (src, generation) with the wire src verbatim as payload, `ContentMode` paint-only (four identical frames under four modes; default Contain diverging from RN's cover, recorded). The headline defer-mechanism double correction (decision-time DEFER capture; dispatch-table row order) fixed on both shells, adversarial orderings pinned. Run **29510920883** verified live (ios, success, 153/0 at `phase-7.5-image@20afbcb`). This phase's H5 closed the ledgered placeholder pair (below). |
| 7 | The React Native parity survey | **PASS** | The [7.4 design's survey table](2026-07-16-phase-7.4-design.md) — 19 RN core components mapped, **11 have-it**, with the ships/ledgered split: `BnModal` + `BnActivityIndicator` shipped in 7.4, Image polish shipped in 7.5 (verified: DoD #6 above), `SafeAreaView` ledgered with the named edge-to-edge problem, SectionList / Pressable-state / StatusBar / RefreshControl / KeyboardAvoidingView / Alert each ledgered **with its reason still attached** (grep-verified in the committed table). Carried into the M8 ledger below. |
| 8 | Hygiene + close | **PASS** | Four parts, each verified: **(a) typed props** — closed by 7.1 (recorded above). **(b) The route-registry unification** — `PageManifest.cs` (14 rows: 9 routed + 5 probes) is the single declaration; `HostSession.s_components` and `NativeNavigationManager.s_routes` are derived views; `MainActivity.kt`'s `DEEP_LINK_COMPONENTS` is the one surviving PINNED MIRROR, held by `RouteTableDriftTests` (3 facts, **in the required lane**, green at the tip inside the live 539): the pair-for-pair pin, the `?: "BnDemo"` fallback pin, the nine-page ordered baseline retargeted to the manifest. Five mutations run red-first (commit `90ac62c` quotes each redline, incl. the set-equality-blind wrong-page case); the tautology (`EveryRoute_ResolvesToAComponentTheMountRegistryKnows`) retired with a comment, not kept green. **(c) Every new surface CI-asserted** — the four bars in the workflows match the observed runs: ci.yml asserts **539** (.NET) and **106** (JVM), android-instrumented.yml asserts **184**, ios.yml asserts **154**; provenance blocks updated per phase (the standing law). Branch protection read back live == exactly the three required contexts. **(d) Decision log** — 7.0–7.6 each with design + conclusion (7.0's conclusion is the spike verdict; 13 docs), ROADMAP.md phase table current, **plus this audit**. |

---

## The four counts — with the evidence that proves them

| Surface | Baseline | Asserted in | Proven by | Notes |
|---|---|---|---|---|
| .NET (xUnit, 3 projects) | **539** passed / 0 skipped | `ci.yml` → **build-test (required)** | **Re-run live at `7aaa424`** for this audit: 132 + 384 + 23 = 539/0/0 | The required-lane run for this tree is this PR's own gating run (ci.yml triggers on `pull_request`; the branch pre-PR had none) — a tag criterion below. |
| JVM (Gradle `testDebugUnitTest`) | **106** passed / 0 failed | `ci.yml` → **build-test (required)** | **Re-run live at `7aaa424`**: tests=106 failures=0 errors=0 skipped=0 across 19 suites | Same note as above. |
| Android instrumented | **184** passed / 0 failed | `android-instrumented.yml` (nightly/dispatch — advisory) | **Local AVD run at Gate 2** (API 34 x86_64 Pixel 6, the workflow's mirror), recorded in the workflow's Phase 7.6 provenance block + commit `9108e7e` (182 → 184: +H4, +H5) | No hosted run exists on this branch (the lane never triggers on PRs — by design since 4.0). The nightly on `main` re-asserts 184 after merge. Same posture as 7.4/7.5 ("local AVD, asserted in android-instrumented.yml"). |
| iOS XCTest (simulator) | **154** passed / 0 failed | `ios.yml` (dispatch + push-main — advisory) | Run **29515968994** (success, verified live): `XCTest cases: passed=154 failed=0`, plus `IL2072 count: 4` and `SYMBOLS: all 9 blazornative_* present.` on iossimulator-arm64 | The run is at `9108e7e` — two commits behind the tip. Both are non-Swift by diff: `eee4551` (ios.yml triggers only) and `7aaa424` (GITHUB-SETUP.md + a MainActivity.kt KDoc). The required `ios-build` compiles the tip on this PR. |

### The ABI did not move — verified independently

- **9 exports:** dumpbin (via vswhere) on the live win-x64 publish at the tip — exactly the
  nine, named above. iOS: `nm -gU` in run 29515968994 — `SYMBOLS: all 9 blazornative_*
  present.` (The bionic RIDs are re-verified by `build-test`/`android-build` on this PR —
  the same llvm-readelf steps that have asserted them every phase.)
- **4 accepted IL2072s** on the live win-x64 publish AND on run 29515968994's
  iossimulator-arm64 publish. The count has not moved since Phase 3.0a — through the
  manifest move, whose `Mount<T>` lambdas are the trim roots (moved verbatim, commit
  `b794b3a`).
- **The 72-byte / 9-callback bridge struct — all three mirrors verified at the tip:**
  `BridgeProtocolNativeTests.cs:28` (`Assert.Equal(72, Marshal.SizeOf<BlazorNativeBridgeCallbacks>())`,
  in the live 539), `ShellBridgeTest.kt:48` (`callbacks_struct_is_72_bytes`, in the live
  106 → **required lane**), `BnDriftTests.swift:83` (`MemoryLayout<bn_bridge_callbacks>.size == 72`,
  in run 29515968994's 154).
- **NodeTypes stay THIRTEEN, three mirrors:** `FrameEncoderTests`' all-NodeTypes theory
  (12 named rows, view…activityindicator) + the integer wire-id pin (.NET);
  `NativeFrameAdapterTest.nodeTypes_vocabulary_is_pinned_content_and_length` (JVM);
  `BnDriftTests.swift:67`'s thirteen-entry literal `["?", "view", … "activityindicator"]`
  (iOS). M7 added 8/9/10/11/12 as **vocabulary on the existing int32 field** — never an ABI
  change.
- **Yoga 3.2.1, both shells, three files:** `build.gradle.kts:57`
  (`com.facebook.yoga:yoga:3.2.1`), `ios.yml` `YOGA_VERSION: 3.2.1`, `ci.yml`
  `YOGA_VERSION: 3.2.1` — grep-verified at the tip; the parity is asserted in the required
  lane's first step every run.

---

## The Phase 7.6 specifics (this phase's own work, audited)

1. **The manifest + the drift pin.** `PageManifest.cs` is the normative rule made real: *a
   page is declared ONCE*; both .NET registries are derived views (cannot drift);
   `DEEP_LINK_COMPONENTS` is the one surviving pinned mirror (consulted at Intent-parse
   time, before the `.so` loads — the 5.1 structural record), held pair-for-pair by
   `RouteTableDriftTests` in the required lane. The pin landed **green against the existing
   Kotlin map** — proving the tables already agreed — and five mutations ran red-first with
   the redlines quoted in `90ac62c`, including the wrong-page case set-equality cannot see.
   iOS: zero files, recorded and expected (no route surface; the day it grows one it joins
   the pin — stated in the test header so that day is a decision, not an accident).
2. **The five hygiene items, all landed (Gate 2):** H1 — both shells' lifecycle test headers
   repointed from the 7.2-split `ProcessDisposedComponent` to
   `EmitDisposedComponentRemoves`' host-contract remarks (`92ee30c`). H2 —
   `scrollDiagnostics` → `diagnostics` on both shells, the name stops lying (`753a87d`).
   H3 — `BnClipboardTests` bounded re-tap (3 attempts, each a NEW dispatch through the real
   chain, attempt count in the failure message, a green-after-retry logged via NSLog); the
   test stays against the real `UIPasteboard` (`383c79d`). H4 — the indexed insert at root
   with a live overlay, pinned (`5641600`; +1 instrumented). H5 — the matched placeholder
   pair (recolor repaints / null clears, request still open) on both shells (`c7a57a2`;
   +1 instrumented, +1 XCTest). H6 (iOS out-of-range insert) and H7 (`touch.view`) stay
   DEFERRED by name — carried in the ledger below.
3. **The ios.yml trigger change (owner cost request, `eee4551`).** The lane's
   `pull_request:` trigger was removed: a full simulator run on every push to every open PR
   was the single largest macOS consumer, duplicating coverage held three ways — the
   REQUIRED `ios-build` compile gate (unfiltered, every PR event), the per-gate
   `workflow_dispatch` runs this process performs at every iOS gate/fix/mutation, and the
   `push(main)` run on every merge. **The advisory lane's PR coverage therefore moved to
   per-gate dispatch + push-main.** Both M6-audit F1 fixes STAND (push-main kept, no
   `paths:` filter reintroduced), and the workflow header records the restore condition: if
   the per-gate dispatch discipline ever lapses, `pull_request:` comes back FIRST —
   main-only execution coverage plus a broken process is the AGP-9 shape again.
   GITHUB-SETUP.md was brought in line by the review (I-2, `7aaa424`).
4. **The combined review: PASS with two must-fixes, both applied in `7aaa424`.** I-1 —
   `MainActivity.kt`'s KDoc still promised "unification stays 7.6's job" in the phase that
   shipped it; it now names PageManifest as the source, itself as the pinned mirror, and
   `RouteTableDriftTests` as the pin (map body untouched; the drift tests re-ran green 3/3
   against the edited file). I-2 — GITHUB-SETUP.md described the ios lane as running on
   every `pull_request` (false since `eee4551`) and quoted M5-era bars; it now records the
   trigger change with the restore condition and defers counts to the workflows' provenance
   blocks. Two minors recorded, not fixed: M-1 — the drift parser's comment-line
   limitation (a commented-out pair *inside* the map's parens would parse as live; the
   line-start anchor covers the declaration itself) — the house pattern's shared limitation,
   `ShellStyleTableDriftTests`' parsers have the same property; M-2 — a green-after-retry in
   H3 is visible only in the run log (the NSLog line), not in any count — recorded as the
   accepted cost of keeping the test real.

---

## Findings (recorded, none blocking)

- **F1 — the required-lane run for the audited tree is this PR's own.** `ci.yml` has no
  branch-push trigger, so `build-test`/`ios-build`/`android-build` had never run on
  `phase-7.6-close` before this PR opened. The audit's .NET/JVM/publish numbers were re-run
  live locally (above) and the three required gates must be green on this PR before merge —
  which branch protection enforces mechanically. Not a gap; named so nobody reads "539
  asserted in ci.yml" as "539 observed by CI on this SHA" before the checks finish.
- **F2 — the instrumented 184/0 is a local-AVD assertion, not a hosted run.** The lane
  never runs on PRs (by design since 4.0); the Gate 2 run was on the mirror AVD (API 34
  x86_64 Pixel 6) and the workflow asserts 184 at the tip. The nightly on `main` re-asserts
  it after merge. Identical posture to 7.4 (166) and 7.5 (182), both of which the nightly
  subsequently confirmed.
- **F3 — the iOS 154/0 run trails the tip by two non-Swift commits** (`eee4551` = ios.yml
  triggers, `7aaa424` = docs + a Kotlin KDoc — diff-verified). The required `ios-build`
  compiles the actual tip on this PR.
- **F4 — carried residuals:** `enforce_admins: false` (M6 F3, unchanged); the device lanes
  stay advisory (the M6 recommendation #3's stability-baseline condition — measuring it is
  M8-adjacent CI work, out of scope by design); H6/H7 deferred by name (ledger below).

---

## What M7 actually delivered

**The authoring story and the components every real app opens with — on the M6 engine, with
ZERO ABI change across the whole milestone.** Still 9 exports, still the 72-byte bridge;
everything M7 added rode existing wires as vocabulary (five NodeType ids, two props, two
event names). Concretely:

- **`.razor` authoring end-to-end** — the Razor compiler's output rendering through
  `NativeRenderer` under NativeAOT, trim-clean (IL2072 stays 4); all nine pages authored in
  `.razor`; the supported subset normative and ledgered.
- **`BnList`** — windowed rendering with counted liveness (11/15/11 of 500) and the
  `onScroll` wire designed for it: shell-side conflation, throughput-evidenced on both
  platforms, no ABI change.
- **Form controls + a real `picker`** — the state-owner precedent (strict items JSON, the
  normative clamp rule), two-way bind on all four, the per-platform guard asymmetry
  verified.
- **`BnModal`** — the first overlay: anchor+overlay in the existing root, dismissal as a
  REQUEST, the fixpoint purge.
- **`BnImage` polish** — Placeholder/OnError/ContentMode with zero new measurement states.
- **The RN parity survey** — 11/19 have-it, the cheap wins shipped, the rest ledgered with
  reasons.
- **The route-registry unification** — one manifest, derived views, one pinned mirror under
  a required-lane pair pin; the last hand-duplicated declaration in the repo is gone.
- **Test surface grew** 324 → **539** .NET, 83 → **106** JVM, 111 → **184** Android
  instrumented, 72 → **154** iOS XCTest — every bar asserted in its lane.

Along the way M7 found and fixed **two 3.3-era renderer bugs** (7.2, red-first), **two iOS 26
platform findings** (7.3, on CI), the **defer mechanism's two construction errors** (7.5,
both review-caught), and recorded one honest **equivalent mutant** (7.5) instead of
rewording it.

---

## The ledger carried into M8 (consumed by `new-milestone`)

| Item | Recorded in | Why deferred / trigger |
|---|---|---|
| **Density-aware assets** (`@2x`/`srcset`) | 6.3 + 7.5 conclusions | The wire has no scale channel; mode is *paint*, density is *measurement*. **Trigger:** the first bundled/local-asset story (M8+ packaging). |
| **`SafeAreaView`** | 7.4 survey | The named edge-to-edge problem (Android 15 enforcement vs. iOS insets) — M9-adjacent. |
| **SectionList / Pressable-state / StatusBar / RefreshControl / KeyboardAvoidingView / Alert** | 7.4 survey table | Each row carries its reason in the committed table (gesture system, window chrome, host-API dependencies). |
| **`OnLoad` / `PlaceholderSrc` / `repeat`-GIF-SVG** | 7.5 conclusion | No customer / the 6.3 decode ledger / the named double-load problem. |
| **iOS out-of-range indexed insert lands after overlay (H6)** | 7.4 G3 review M2 | Malformed wire only; stacking cosmetics, not correctness — the ledgered clamp-tightening candidate. |
| **The `touch.view` property read (H7)** | 7.4 finding 4 | The one `UITouch` line no hosted XCTest can construct; everything around it is pinned. |
| **Horizontal scroll** | 6.2, re-affirmed 7.2 | No customer forced it — `BnList` is vertical; a `Horizontal` variant is the trigger. |
| **`android-build` → ubuntu port** | 7.5-era CI note | Deferred pending billing evidence (windows-latest minute multiplier vs. the port's risk). |
| **iOS lanes' duplicated publish (~3 min)** | M7 open, CI posture | A ledgered annoyance, not milestone work. |
| **Device lanes → required** | M6 audit rec. #3 | Stands on its stability-baseline condition; measure, then promote. |
| **True-move `@key` ABI conversation** | 7.2 conclusion | Trigger: the first component whose UX crosses surviving keys. |
| **nuget.org publication, CLI, templates, docs site** | MILESTONE.md out-of-scope | M8's whole point. |

---

## Recommendation

### Is M7 ready to tag `v7.0`?

**Yes.** All eight DoD criteria PASS on evidence re-verified live, the ABI is provably
unmoved across the fattest milestone yet, the four count bars match observed runs, branch
protection is exactly the three required contexts, and the decision log is complete
including this audit.

**Tag criteria, restated mechanically:** this PR's three required gates green (branch
protection enforces it) → merge → **tag `v7.0` on the merge commit, on the owner's go**
(the tag is deliberately NOT applied by this phase — the M6 pattern). The seven scratch
branches from the 7.4/7.5 on-CI mutation runs (`scratch/7.4-gate3-mut-*` ×3,
`scratch/7.5-gate3-mut-*` ×4) are owner cleanup, listed in the PR.
