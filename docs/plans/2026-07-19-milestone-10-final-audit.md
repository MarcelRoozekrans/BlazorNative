# Milestone 10 — Final Audit (Phase 10.3)

**Date:** 2026-07-19
**Auditor:** Phase 10.3, against `phase-10.3-m10-close` (branched from `main` at `7ca1ac5` — the
audited tip; no tag follows, the 8.6 rule).
**Predecessor:** [M9 final audit](2026-07-18-milestone-9-final-audit.md) (PASS on all six; no tag —
the milestone-tag namespace was retired in Phase 8.6, and a milestone closes on its audit).
**Contract audited:** [`docs/planning/MILESTONE.md`](../planning/MILESTONE.md) — the seven M10 DoD
criteria.

## Method

The house standard, unchanged since M6: **"asserted, not observed"** and **"a mechanism nobody
tested is a mechanism nobody knows."** Applied to the audit itself — **no criterion is accepted
because a conclusion doc says so.** Every verdict below is checked against git history, the live
source, the CI gate literals (the `if`/`-ne` lines that DECIDE, not the step `name:` prose), the
issue tracker state, and the ABI mirrors — and everything locally runnable was **re-run live at the
audited tip**:

- **.NET suite, re-run live:** `dotnet test BlazorNative.sln -c Release` — **780 passed / 0 skipped /
  0 failed** (Runtime.Tests **617** + Renderer.Tests **138** + Analyzers.Tests **25** = 780). Matches
  the `ci.yml` gate literal (`$passed -ne 780`) exactly.
- **The frozen bridge, read at three mirrors:** the .NET struct (`BridgeProtocolNative.cs`), the Swift
  C header (`BlazorNativeRuntimeC.h`), and the pin tests — all agree: **80 bytes / 10 exports / 5
  ops**, UNCHANGED by M10. The struct that grew is the init-**input** (`BlazorNativeInitOptions`,
  24 → 32 bytes) — a different struct, not the callbacks bridge.
- **The three lanes I cannot run here** (JVM Gradle, Android instrumented AVD, iOS XCTest — no Mac,
  no live AVD in this audit host) are verified by **gate-literal ↔ README reconciliation** plus the
  per-phase run-id provenance recorded in ROADMAP. Stated as a finding (F1), not smoothed over — the
  same posture M8 and M9 took for their device lanes.
- **Git, read live:** `git tag -l` → **`v0.1.0` only** (the 0.1.0 release tag from PR #137); **none of
  `v1.0`–`v10.0`**. `git log --oneline` confirms the three M10 fix PRs landed on `main` as `fix:`
  commits (#141, #143, #144).
- **Issues, read live** (`gh issue view`): **#119–#125 all CLOSED; #126 OPEN** (deferred, correct).

**Verdict: PASS on all seven.** M10's thesis — *make the published 0.1.x honestly correct and its
front door accurate, adding no platform surface and touching the frozen bridge not at all* — is
**re-proven against live artifacts**, not assumed. Non-blocking observations are recorded as
findings, never rounded up.

---

## Verdict table

| # | Criterion | Verdict | Evidence (verified at `7ca1ac5`) |
|---|---|---|---|
| 1 | iOS no longer reports Android (#121) | **PASS** | [10.0 conclusion](2026-07-19-phase-10.0-conclusion.md). `BlazorNativeInitOptions.PlatformInfoKind` is the new init-input field (`Exports.cs:73`); `blazornative_init` maps it via `ToPlatformKind(opts->PlatformInfoKind)` (`Exports.cs:132`), whose unset/unknown/out-of-range default is **`PlatformKind.DevHost`, never Android** (`Exports.cs:171-174`). `NativeShellBridge` serves the **stored** kind, not a hardcoded constant: `GetPlatformInfoAsync` → `opts?.Kind ?? PlatformKind.DevHost` (`NativeShellBridge.cs:922`) and the `PlatformInfo` JSON emits `opts.Kind` (`:900`). Both shells pass their own: **Kotlin** `MainActivity.kt:280` `BnPlatformKind.ANDROID`, **Swift** `BnRuntime.swift:214` `BnPlatformKind.iOS` (ordinal 2). C header mirror `BlazorNativeRuntimeC.h:64` (`int32_t platformInfoKind; // offset 24`); template mirror `MainActivity.kt:238` `BnPlatformKind.ANDROID`. **.NET red-first tests:** `NativeShellBridgeTests.PlatformInfo_ReportsTheShellsKind_iOS_NotAndroid` (`:601` — asserts `iOS`), `PlatformInfo_UnsetKind_DefaultsToDevHost_NotAndroid` (`:643`), and the ordinal table `ToPlatformKind_MapsTheInitOptionsOrdinal_ByEnumValue` (`:662-674`, incl. out-of-range/negative → DevHost). Issue **#121 CLOSED**. |
| 2 | The test channel cannot swallow a failure (#123) | **PASS** | [10.0 conclusion](2026-07-19-phase-10.0-conclusion.md). `NativeRenderer` no longer does `_ = _frames.InvokeAsync(...)` (grep: zero bare-discard occurrences). It **observes** the task: `var framesTask = _frames.InvokeAsync(frame, default)` (`NativeRenderer.cs:391`); completed inline → `framesTask.GetAwaiter().GetResult()` rethrows into the existing `catch` → `HandleException`; still-running → an `OnlyOnFaulted` continuation routes to `HandleException` (`:392-398`). `FramesFaultSurfacingTests.cs` exists and asserts a throwing `Frames` subscriber surfaces under `StrictErrors` (FAILED on unfixed `main` per the conclusion, green in the live 780). Issue **#123 CLOSED**. |
| 3 | No stale version reported to consumers (#120) | **PASS** | [10.1 conclusion](2026-07-19-phase-10.1-conclusion.md). `Exports.cs:98` — `internal const string VersionNumber = "0.1.0"; // x-release-please-version` (the current props version `Directory.Build.props:112`, **not** `1.4.0-phase-5.4`). `Exports.cs` is in `release-please-config.json` `extra-files` (`:12`). Drift pin `PackageVersionPinTests.TheRuntimeVersionExport_AgreesWithTheProps` asserts `Exports.VersionNumber == propsVersion` (`:416`). **Grep: no `phase-5.4` anywhere under `src/`** (Kotlin/Swift included) — no shell test still asserts the stale string. Issue **#120 CLOSED**. |
| 4 | The one load-bearing version is guarded (#122) | **PASS** | [10.1 conclusion](2026-07-19-phase-10.1-conclusion.md). `PackageVersionPinTests.EveryRuntimeFrameworkVersion_AgreesAcrossSampleAndTemplate` parses every `RuntimeFrameworkVersion` occurrence (sample ×2 + template), **derives the canonical from occurrence #1** (no hardcoded second `10.0.9`), asserts the rest agree with a non-vacuity floor ≥ 3; mutation-proven it bites (10.1 conclusion). Issue **#122 CLOSED**. |
| 5 | Precision + cleanups triaged (#124, #125) | **PASS** | [10.2 conclusion](2026-07-19-phase-10.2-conclusion.md). **#124:** `BnListWindow.Compute` computes `maxOffset`/`clamped` (and the row math) in `double` (`BnListWindow.cs:83-84`, `(double)count * itemHeight`), large-list red-first witnesses per the conclusion. **#125, all six:** (1) `NarrowHandlerId` fail-loud narrow present in `NativeRenderer.cs`; (2) `BridgeJsonContext` **DELETED** — grep over the whole repo (excl. docs) returns **zero** hits, no references; (3) `FrameArena.GrownCapacity` overflow guard (`FrameArena.cs:156-163`, upper-bound `require`); (4) Kotlin null-patches guard `NativeFrameAdapter.kt:109` (`require(patchCount in 0..MAX_PATCHES)`) with a witness test `NativeFrameAdapterTest.kt:188`, template mirror byte-identical (`TemplateDriftTests` green); (5) #125.4 re-ledgered — `BnButton` unchanged, regression test pins the "Blazor elides an empty EventCallback" probe; (6) actions SHA-pinned — `release-please-action@45996ed1…` (`release-please.yml:248`) + `android-emulator-runner@a421e438…` (`android-instrumented.yml:205`), both with `# vX.Y.Z` comments. Issues **#124, #125 CLOSED**. |
| 6 | The docs and README tell the truth (#119 + owner ask) | **PASS** | [10.2 conclusion](2026-07-19-phase-10.2-conclusion.md). README states the honest split: **.NET (780) + JVM (120)** required, `build-test`, gate the PR; **Android (209) + iOS (235)** advisory (nightly/dispatch), do not gate (`README.md:237-251`). **Grep: no `draft`/`publish the draft` in `website/docs/**`** (zero hits). Published-0.1.0 install story present (10.2 sweep of `installation.md`/`quick-start.md`). Version numerology reconciled: the OLD strings (`six mirrors`, `N is 6`, `seven literals across four files`, `count of eight`) have **no live occurrence** in `Directory.Build.props` or `PackageVersionPinTests.cs` (the only surviving hit is `ROADMAP.md:1258`, a dated historical log entry — decision-3-historical, correctly left). Issue **#119 CLOSED**. **#126 (font parity) remains OPEN** — deferred as investment, correct. |
| 7 | Hygiene + close | **PASS** | This phase. **(a)** Every fix CI-asserted: `.NET -ne 780` (`ci.yml:1442`), `JVM -ne 120` (`ci.yml:1733`), `iOS -ne 235` (`ios.yml:1050`), `Android -ne 209` (`android-instrumented.yml:978`), each reconciled to its README row. **(b)** A conclusion doc per phase: 10.0, 10.1, 10.2 (+ designs) — listed below. **(c)** Issues #119–#125 CLOSED on GitHub with their fixing commits; #126 OPEN. **(d)** This audit. **No tag** — closure is the audit. |

---

## The four counts — gate ↔ README reconciliation

*A count row left behind reddens drift. Every literal below was read from the workflow's DECIDING
line and matched to its README row.*

| Surface | Value | Gate literal (workflow) | README row | Match |
|---|---|---|---|---|
| .NET (xUnit, 3 projects) | **780** | `ci.yml:1442` (`$passed -ne 780`) | `README.md:248` | ✅ **re-run live: 617+138+25 = 780/0/0** |
| JVM (Gradle `testDebugUnitTest`) | **120** | `ci.yml:1733` (`$tests -ne 120`) | `README.md:249` | ✅ (gate ↔ README; not re-run here — F1) |
| Android instrumented (AVD) | **209** | `android-instrumented.yml:978` (`"$tests" -ne 209`) | `README.md:250` | ✅ (gate ↔ README; not re-run here — F1) |
| iOS XCTest (simulator) | **235** | `ios.yml:1050` (`"$passed" -ne 235`) | `README.md:251` | ✅ (gate ↔ README; not re-run here — F1) |

**No mismatch found.** The README's four rows and the four gate literals agree, and the one lane the
audit host can run reproduced its literal exactly.

---

## The frozen bridge is STILL frozen — the concrete proof

**This is the milestone's load-bearing invariant, and it is re-proven, not cited.** M10 was
correctness + accuracy debt; it added **no platform surface** and touched the callbacks bridge not at
all.

**1. The callbacks struct is 80 bytes — three mirrors agree, live.**

- **.NET pin:** `BridgeProtocolNativeTests.cs:30` — `Assert.Equal(80, Marshal.SizeOf<BlazorNative
  BridgeCallbacks>())`, with `StorageDelete` at offset 32 (`:56`) as an interior spot-check. In the
  live 780.
- **Swift C header:** `BlazorNativeRuntimeC.h` — the `bn_bridge_callbacks` struct is unchanged; the
  M10 header edit was the **init-input** struct (`platformInfoKind @24`), a different type.
- The three `*AbiUnchangedTests` suites (Notifications/SecureBiometrics/Camera — the M9 reuse pins)
  remain green in the live 780.

**2. Exactly 10 `[UnmanagedCallersOnly]` exports.** `grep -c UnmanagedCallersOnly
src/BlazorNative.Runtime/Exports.cs` → **10** — unchanged since Phase 9.0. No eleventh export was
added by any M10 phase.

**3. The struct that grew is NOT the bridge.** `BlazorNativeInitOptions` went 24 → 32 bytes in Phase
10.0 to carry `PlatformInfoKind` (`Exports.cs:73`; C header `platformInfoKind @24`). This is the
init-**input** the host passes to `blazornative_init` — it is not a callback slot, not an export, and
not the 80-byte callbacks bridge. The `BridgeProtocolNativeTests` 80-byte / offset assertions and the
five-op `HostCallOp` enum are untouched. *The distinction is exactly the M10 discipline: fix the bug
by extending the input contract, never the frozen ABI.*

---

## The milestone ledger

**What M10 fixed (the 9.0 review backbone, retired here):**
- **#121** — iOS reported Android through the public `IMobileBridge.PlatformInfo` surface. Closed
  10.0: explicit `PlatformInfoKind`, both shells report their own, unset → DevHost not Android.
- **#123** — a `Frames` subscriber throw was silently dropped (false-green test risk). Closed 10.0:
  the task is observed, faults route through `HandleException`/`StrictErrors`.
- **#120** — `Exports.VersionNumber` frozen four milestones back at `1.4.0-phase-5.4`, consumer-
  visible. Closed 10.1: release-please-governed `0.1.0` mirror + drift pin.
- **#122** — `RuntimeFrameworkVersion 10.0.9` duplicated with no pin. Closed 10.1: drift test across
  sample + template, canonical derived, mutation-proven.
- **#124** — `BnListWindow` float precision drift on large lists. Closed 10.2: `double` arithmetic,
  red-first.
- **#125** — six grouped cleanups, each fixed or re-ledgered (handler-id, dead-code delete, arena
  overflow, Kotlin null-patches parity, #125.4 re-ledgered, actions SHA-pinned).
- **#119** — README overclaimed "all four counts asserted in CI." Closed 10.2: honest required/
  advisory split + refreshed counts + the docs/README auto-publish/published-0.1.0 sweep.

**What stays deferred (correct, reasoned):**
- **#126 font/typography parity** — OPEN; *investment, not need* (scoping decision 2). Bundle-one-
  font + tighten the frame-table contract is real-adoption work, not correctness debt.
- **Accessibility, i18n, performance/memory budgets, the security model** — the old-P6 hardening,
  re-deferred as investment (ROADMAP M10 block); the P3 perf ledger (#8/#9/#12/#13) rides with it.
- **Real-device iOS / TestFlight** — trigger: an **Apple Developer account** (owner has none).
- **FCM push** — trigger: a **Firebase project**.
- **The on-device inspector channel** — ledgered a fourth time; trigger unchanged (developer
  tooling, no user-facing pull).

The older epics/ledger (#8/#9/#12/#13/#16/#17/#18/#20–#27/#30) are **untouched** by M10 — genuine
future work, not this milestone's correctness scope.

---

## Findings (recorded, none blocking)

- **F1 — three lanes are verified by gate ↔ README reconciliation, not re-run in this audit.** The
  audit host is Windows with no live AVD and no Mac, so JVM (120) / Android instrumented (209) / iOS
  (235) are checked against their gate literals + README rows + per-phase run-id provenance, not
  executed live. The `.NET 780` WAS re-run live. Unchanged from the M8/M9 device-lane posture.
- **F2 — one stale-numerology string survives, correctly.** `ROADMAP.md:1258` still reads "six
  mirrors, seven literals, four files" — but it is a **dated historical log entry** (Phase 8.1's
  line), not a live source claim, and the "don't retrofit dated records" discipline (10.2 decision 3)
  leaves it. The **live** files (`Directory.Build.props`, `PackageVersionPinTests.cs`) carry the
  reconciled post-10.1 numerology. Named so the next reader is not misled.
- **F3 — carried residuals:** `v0.1.0` is the only tag (the 0.1.0 release, PR #137); no milestone tag
  is created by this phase; the device lanes stay advisory; #126 and the investment-class hardening
  stay deferred with named triggers.

---

## The conclusion docs, listed (DoD #7)

- Phase 10.0: [design](2026-07-19-phase-10.0-design.md) + [conclusion](2026-07-19-phase-10.0-conclusion.md)
- Phase 10.1: [design](2026-07-19-phase-10.1-design.md) + [conclusion](2026-07-19-phase-10.1-conclusion.md)
- Phase 10.2: [design](2026-07-19-phase-10.2-design.md) + [conclusion](2026-07-19-phase-10.2-conclusion.md)
- Phase 10.3: this audit.

---

## What M10 actually delivered

**The unglamorous, honest bookend.** Not a feature — a debt paydown. The 9.0 full-repo review found
that some of what shipped in 0.1.0 was *wrong* (an iOS app calling itself Android, a test lane that
could lie green, a version literal four milestones stale) and that the release-model change left the
docs describing a flow that no longer exists. M10 fixed exactly those and stopped:

- **Two correctness bugs, red-first** — iOS-reports-Android via an explicit init-struct
  `PlatformInfoKind` (both shells, unset → DevHost), and the false-green `Frames` drop closed by
  observing the task. *The false-green fix went first — it protects every other proof in the repo.*
- **Two version literals governed** — `Exports.VersionNumber` into release-please's `extra-files`
  with a drift pin; `RuntimeFrameworkVersion` drift-pinned across sample + template.
- **Precision + six cleanups** — `BnListWindow` in `double`, `BridgeJsonContext` deleted,
  `FrameArena`/handler-id/Kotlin-null-patches guards, actions SHA-pinned.
- **The front door made accurate** — the site + README swept to the published-0.1.0, auto-publish
  reality with the honest required/advisory CI split; counts refreshed to 780/120/209/235.

And the load-bearing constraint held: **the 80-byte callbacks bridge / 10 exports / 5 ops are
unchanged** — the only struct that grew is the init-input, exactly the seam M10's design chose.

---

## Recommendation

### Is M10 complete?

**Yes.** All seven DoD criteria PASS on evidence verified at the audited tip. The two correctness
bugs are fixed red-first, the two version literals are governed and drift-pinned, precision + the six
cleanups are triaged, the docs/README tell the published truth with the honest CI split, and the
frozen bridge is provably unmoved (80 bytes / 10 exports / 5 ops — the init-input grew, the bridge did
not). The .NET suite re-runs live at **780/0/0**, and issues #119–#125 are CLOSED with #126 correctly
OPEN.

### Closure, restated mechanically

This PR's five required gates green (`build-test`, `android-build`, `ios-build`, `pr-title`,
`footer-check` — branch protection enforces it) → merge → **M10 is closed on this audit**. **No tag**
— the milestone-tag namespace was retired in Phase 8.6, and `vN.0` will never be cut again. This
audit is docs-only (no source change); doc-only commits ride as `docs:`/`chore:` and do not bump.

### The owner's standing actions (unchanged)

1. **Real-device Android** — the honesty checks CI cannot drive (camera sensor, biometrics).
2. **Apple Developer account** — the single trigger that opens real-device iOS / TestFlight.
3. **A Firebase project** — the trigger for FCM push.
4. **The investment-class hardening (#126, a11y/i18n/perf/security)** — promote from deferred only if
   this heads toward real adoption.
