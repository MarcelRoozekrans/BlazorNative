# Milestone 11 — Final Audit (Phase 11.5)

**Date:** 2026-07-22
**Auditor:** Phase 11.5, against `phase-11.5-audit` (branched from `main` at `cf80930` — the audited
tip; no tag follows, the 8.6 rule).
**Predecessor:** [M10 final audit](2026-07-19-milestone-10-final-audit.md) (PASS on all seven; no
tag — the milestone-tag namespace was retired in Phase 8.6, and a milestone closes on its audit).
**Contract audited:** [`docs/planning/MILESTONE.md`](../planning/MILESTONE.md) — the six M11 DoD
criteria, **including DoD #2's 2026-07-22 EXIF amendment**, which is itself audited rather than
assumed.

---

## Verdict: **PASS WITH FINDINGS**

All six DoD criteria are met on evidence that can be pointed at. **Seven findings** are recorded,
**four of them stale-record contradictions this audit found and fixed**, and one of them —
**F4** — is a real, un-guarded surface that DoD #5's own words ("every new surface CI-asserted")
do not currently cover. None blocks closure; all are named rather than rounded off.

---

## Method — why this audit was written to be hostile to itself

M11 is the milestone that repeatedly proved **a green check is not evidence**:

- `CS1591` was *"read as ON and was OFF"* **twice** (`src/Directory.Build.targets:23-34`, `:49-65`)
  — an import-ordering bug that let a pin report itself enabled while suppressing its own
  diagnostic.
- Phase 11.3 Gate B was therefore wired to prove itself **by mutation**, not by a passing build.
- Phase 11.4's Android transport **merged with its only device-level proof unrun**; when finally
  dispatched it went **red**, and it took three rounds to establish the cause was a *test* bug
  (`FileDescriptor.err` is an ART dup at fd 54, not the process's fd 2), not a transport bug.

So the standard here is the house one — *"asserted, not observed"* and *"a mechanism nobody tested
is a mechanism nobody knows"* — applied to **this audit's own conclusions**. **No criterion is
accepted because a conclusion doc says so.** Every verdict below is checked against live source, the
CI gate literals (the `if`/`-ne` lines that DECIDE, not the step `name:` prose), the run logs of the
lanes this host cannot execute, the issue tracker read live, and `git` read live. Anything that
could not be verified here is marked **UNVERIFIED** and named, not smoothed.

**A finding of "not actually proven" is a success of this audit, not a failure of it.** Four of the
seven findings below are places where a doc claimed a state that had since become false.

---

## The test counts, with their provenance

*Provenance is stated per lane because three of the four are not runnable on this host.*

| Surface | Count | Gate literal (the deciding line) | README row | Provenance |
|---|---:|---|---|---|
| .NET (xUnit, 3 projects) | **864** | `ci.yml:1521` (`$passed -ne 864`) | `README.md:292` | ✅ **RE-RUN LIVE at `cf80930`: 864 / 0 / 0** |
| JVM (Gradle `testDebugUnitTest`) | **148** | `ci.yml:1836` (`$tests -ne 148`) | `README.md:293` | ⚠ **UNVERIFIED locally** (F1) — CI-observed |
| Android instrumented (AVD) | **213** | `android-instrumented.yml:1040` (`"$tests" -ne 213`) | `README.md:294` | ✅ **CI-observed green** — run [29948996623](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/29948996623), `fix/191-android-pump-diagnostic` @ `c779294`: `tests=213 failures=0 errors=0 skipped=0` |
| iOS XCTest (simulator) | **242** | `ios.yml:1083` (`"$passed" -ne 242`) | `README.md:295` | ✅ **CI-observed green on the audited tip** — run [29949367282](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/29949367282), `main` @ `cf80930` |

**The .NET run, verbatim** (`dotnet test BlazorNative.sln -c Release`, this host, at `cf80930`):

```
Passed!  - Failed: 0, Passed:  27, Skipped: 0, Total:  27, Duration:  9 s  - BlazorNative.Analyzers.Tests.dll
Passed!  - Failed: 0, Passed: 138, Skipped: 0, Total: 138, Duration:  4 s  - BlazorNative.Renderer.Tests.dll
Passed!  - Failed: 0, Passed: 699, Skipped: 0, Total: 699, Duration: 17 m 45 s - BlazorNative.Runtime.Tests.dll
```

**27 + 138 + 699 = 864 passed / 0 skipped / 0 failed** — matches `ci.yml:1521`'s literal exactly.

**A bonus live proof fell out of that run**, and it is worth recording because it closes half of
**F2** by observation rather than by argument. The build log carries:

```
BlazorNative.RouteGen: wrote 13 routed rows to …/src/BlazorNative.Jni/src/androidMain/res/raw/blazornative_routes.json
```

So the MSBuild target **does** invoke the generator on an ordinary `dotnet test`, and it emits
**13** rows — the same 13 `RouteTableDriftTests` pins and the same 13 the device session deep-linked
in one pid. The generator-to-file wiring is therefore observed here; what remains uncovered by the
required lane is only the *resource-into-APK-and-read-by-`MainActivity`* leg.

**Gate ↔ README reconciliation: all four agree.** `ReadmeDriftTests` parses each README number and
compares it to the workflow `if` condition that actually decides, so this table is itself gated —
but it was also read by hand here, because a drift test that reads the same two files cannot catch
both being wrong together.

**The M11 deltas, corrected.** The suite moved **782 → 864** (.NET), **120 → 148** (JVM),
**210 → 213** (Android instrumented), **236 → 242** (iOS). Note the third: several docs recorded
*"210 → 212"*, which was true when Phase 11.4 Gate B landed and stopped being true when #193 added
the pump's honest-install assertion. See **F3**.

---

## Verdict table

| # | Criterion | Verdict | Evidence (verified at `cf80930`) |
|---|---|---|---|
| 1 | Deep-link codegen + footgun audit (Phase 11.0) | **PASS** | **The map is genuinely generated, not checked in.** `src/BlazorNative.Jni/src/androidMain/res/raw/blazornative_routes.json` exists on disk but is **`.gitignore`d** (`.gitignore:61`) and `git ls-files` on that directory returns **nothing** — so it cannot rot into a hand-maintained artifact, which is the failure mode the DoD was written against. `MainActivity.kt` carries **no live `DEEP_LINK_COMPONENTS` map**: every remaining repo hit for that identifier is a comment, a test name, or a historical note. The generator is `tools/BlazorNative.RouteGen` (Roslyn over **source**, arch-independent), shipped **inside** `BlazorNative.Runtime` via `_AddRouteGenToolToPackage` with an `<Error>` guard that fails the pack if the tool did not build (`BlazorNative.Runtime.csproj:141-149`) — so the package cannot ship without the codegen. **The drift guard exists and bites:** `RouteTableDriftTests.GeneratedRoutesJson_ReproducesTheManifestsRoutedRows_PairForPair` (`:94`) runs `RouteManifest.Extract` over the real SampleApp source tree, serializes it the way the build target does, parses it back the way `MainActivity` does, and compares **pair-for-pair** with an explicit non-vacuity guard (`Assert.NotEmpty(generated)`), plus the default-fallback literal pin (`:143`) and the 13-page baseline (`:167`). **Footgun audit written:** [2026-07-20-phase-11.0-footgun-audit.md](2026-07-20-phase-11.0-footgun-audit.md) — the deep-link map was the *only* page-keyed shell hand-edit; every capability's manifest surface is template-supplied; the un-derivable rest is documented. **Caveat, recorded not waived — F2.** |
| 2 | Real-device Android proof (Phase 11.2) | **PASS** | [Device proof](2026-07-22-phase-11.2-device-proof.md) exists and is **honest about what it did not do.** The not-exercised list is present, explicit, and complete against the brief: **warm** notification tap-through (`:347`), the **location permission prompt** (`:352`), the **interaction smoke is PARTIAL** (`:356`), and the standalone **`IBiometrics.AuthenticateAsync`** path (`:357`) — the last with the reasoning that a raw session-log `Authenticate → status:Ok` resolves against the code to `Set`, because `BiometricStatus` has **no `Ok` member**. That is a label the phase could have quietly accepted and instead disowned. **The EXIF sub-clause amendment is present and ARGUED, not waived** — `MILESTONE.md:81-98` and proof `:241-250`, `:398`: the clause is unsatisfiable *because the shell deliberately re-encodes the JPEG after baking EXIF rotation into the pixels* (`AndroidShellBridge.kt:1152-1156`, `:1300-1307`, cited to the shell's own comments), so demanding EXIF demands a regression. The *"no emulated shutter"* **burden is unchanged**; only the instrument moved — 3072×4096 (12.6 MP), a real photographed scene, via `MediaStore.ACTION_IMAGE_CAPTURE`. **This is an amendment with a cost stated, which is the correct shape.** The session paid for itself: [#178](https://github.com/MarcelRoozekrans/BlazorNative/issues/178). |
| 3 | Consumer dogfooding (Phase 11.1) | **PASS** | Two apps built **outside this repo** from **nuget.org only** — `bn-baseline` (0.3.0) and `bn-zeroalloc-showcase` (0.4.0, scaffolded from the **published** template). All three artefacts present: [conclusion](2026-07-21-phase-11.1-conclusion.md), [friction ledger](2026-07-21-phase-11.1-friction-ledger.md) (**15 rows** across the two parts, each with a disposition), [zero-to-running walkthrough](2026-07-21-phase-11.1-walkthrough.md). The strongest facts are the ones a doc could not fake: the 4-IL2072 yardstick held on `win-x64` + **both** bionic RIDs *with 12 ZeroAlloc packages layered*, and the `ConfigureServices` seam was proven **at runtime** by an ABI harness P/Invoking the **published** NativeAOT binary. Boundaries kept explicit (no UI event dispatched; DevHost bridge; APK built not installed; iOS-sim deferred) — which is what makes DoD #2 a separate criterion rather than a rounding-up of this one. |
| 4 | API stability + 1.0 (Phase 11.3) | **PASS** | **Six** `PublicAPI.Shipped.txt` baselines present (`src/{Components,Core,Device,Http,Renderer,Runtime}/`), **1 166 lines** total — matching the claim exactly. `BnEnforcePublicApi` is declared in **`src/Directory.Build.targets`** (`:121` `WarningsAsErrors`, `:142` `NoWarn RS0041`, `:145-150` the `PackageReference`) and **appears nowhere in `src/Directory.Build.props`** — the CS1591 lesson correctly re-applied, and the file's own header (`:71-76`) says why. `RS0016`/`RS0017`/`RS0037` are escalated to **errors**, opted in per package by all six csprojs. **Analyzer roster pin** present: `tests/BlazorNative.Analyzers.Tests/AnalyzerDiagnosticRosterTests.cs`. **Tier table** ([api-tiers](2026-07-21-phase-11.3-api-tiers.md), 88 types → 55/2/31), **`[Experimental]` policy** ([experimental-policy](2026-07-21-phase-11.3-experimental-policy.md)), and **1.0 criteria** ([one-point-oh-criteria](2026-07-22-phase-11.3-one-point-oh-criteria.md)) all present. **`[EditorBrowsable(Never)]` count verified by grep: exactly 28** across `src/**` — the claim is accurate. **Baseline mutation re-run in this audit — see the pasted output below.** |
| 5 | Hygiene + close (Phase 11.5) | **PASS WITH FINDINGS** | **(a) New surfaces CI-asserted:** the generated route map → `RouteTableDriftTests` (required `build-test`); the public-API baseline → RS0016/17/37 **as compile errors** in every shipped package; the analyzer contract → `AnalyzerDiagnosticRosterTests`; the logging seam → `BnLogTests` + `BnLogFormatDriftTests` + `ConsoleErrorDriftTests` + `NSLogDriftTests` + the `SizeOf==32 && OffsetOf(LogLevel)==28` pair (`NativeShellBridgeTests.cs:687`, `:707`) + `BnStderrPumpTest` (JVM) + `BnStderrPumpAndroidTest` (instrumented). **One surface is NOT guarded — F4.** **(b) Decision log per phase:** 11.0 design+conclusion+footgun-audit · 11.1 design+conclusion+ledger+walkthrough · 11.2 design+runbook+**device-proof** · 11.3 design+tiers+policy+criteria · 11.4 design+conclusion · 11.5 this audit. **11.3 has no single `-conclusion.md` — F5.** **(c) Device-proof doc:** present. **(d) This audit.** **(e) No milestone tag:** `git tag -l` → `v0.1.0 v0.1.1 v0.2.0 v0.3.0 v0.4.0 v0.4.1` — **six release tags, zero `vN.0` milestone tags.** The 8.6 rule holds and this phase creates none. |
| 6 | Logging discipline (Phase 11.4) | **PASS** | **The seam:** `src/BlazorNative.Core/BnLog.cs` — five levels + reserved ordinal 0, `volatile int` threshold, pluggable sink, exception redaction, default **`Warn`** and deliberately **not `#if DEBUG`** (`:78`). **The .NET migration is real:** grep for `Console.Error` across `src/**/*.cs` returns **three** hits — two are comments in `BnLog.cs`, the third is `BnLog.cs:172`, *the seam's own default writer*. **The iOS sweep is real:** grep for `NSLog(` under `src/BlazorNative.Apple` returns **21** hits, **all of them under `BnHostTests/`** (the test target); **zero** under the shipped `BnHost/`. **Both transports present:** `BnStderrLogcatPump.kt` (`Os.pipe()`+`Os.dup2()`, pure Kotlin) and `BnHost/BnLog.swift` (`os_log`/`Logger`). **Drift pins present** and scoped correctly (`NSLogDriftTests` scopes to `BnHost/`, which is why the test-target hits are not a violation). **#164's rc-2 fix landed** (#189) and **#164 is CLOSED** on GitHub. **The carve-out DoD #6 was closed under is RESOLVED — and the docs did not say so; this audit fixed them (F3).** |

---

## The DoD #4 baseline mutation, re-run in this audit

*The single strongest fact available for DoD #4 is a mutation, not a green build — so it was
re-run here rather than cited from Phase 11.3's conclusion.*

**Mutation:** `src/BlazorNative.Components/BnButton.cs:25` — `[Parameter] public string? Label` →
`[Parameter] public string? Text` (plus its use site at `:43`), then
`dotnet build src/BlazorNative.Components/BlazorNative.Components.csproj -c Release --no-restore`.

```
BnButton.cs(25,39): error RS0016: Symbol 'BlazorNative.Components.BnButton.Text.get -> string?'
    is not part of the declared public API
BnButton.cs(25,44): error RS0016: Symbol 'BlazorNative.Components.BnButton.Text.set -> void'
    is not part of the declared public API
PublicAPI.Shipped.txt(18,1): error RS0017: Symbol 'BlazorNative.Components.BnButton.Label.get -> string?'
    is part of the declared API, but is either not public or could not be found
PublicAPI.Shipped.txt(19,1): error RS0017: Symbol 'BlazorNative.Components.BnButton.Label.set -> void'
    is part of the declared API, but is either not public or could not be found

Build FAILED.
    0 Warning(s)
    4 Error(s)
```

**Reproduced exactly**: `error RS0016` ×2 (the added surface) + `error RS0017` ×2 (the removed
surface), **`Build FAILED`**, `4 Error(s)` / **`0 Warning(s)`** — the zero-warnings line matters,
because it is what distinguishes *"escalated to error"* from *"the analyzer ran and shrugged."* Note
the RS0017s carry a **`PublicAPI.Shipped.txt` line number** — the baseline is genuinely being read,
not merely present on disk. **The gate bites at the audited tip; Phase 11.3 Gate B's central claim
is re-proven, not cited.**

**Reverted immediately after** — `git checkout -- src/BlazorNative.Components/BnButton.cs`;
`git status --short` shows no modification to any `src/**` file, and `:25` reads
`[Parameter] public string? Label { get; set; }` again. **This audit changes no source.**

---

## The DoD #6 carve-out — resolved, and the docs were stale

**This is the finding the brief predicted, and it was real.**

DoD #6 was closed by PR #190 **while the Android transport's only device-level proof was red**. PR
#192 then corrected the 11.4 conclusion to say so honestly. That carve-out has **since been
resolved**, and until this audit **three documents still carried the stale wording**.

**What actually happened, verified from the run logs rather than the commit message:**

1. The lane's last green predated Gate A (`88e2b1c`, 2026-07-22 05:50Z) — so every 11.4 Android
   change had merged without it running. **True at the time.**
2. Dispatched on `f53f74a` → run [29928945461](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/29928945461)
   **FAILED**, 211/212, `BnStderrPumpAndroidTest.stderrWrites_reachTheSink_throughTheRealDup2`.
3. [#191](https://github.com/MarcelRoozekrans/BlazorNative/issues/191) established the cause was
   **test-side**: the probe wrote via `Os.write(FileDescriptor.err, …)`, and on ART
   `FileDescriptor.err` is a **dup sitting at fd 54**, not literal fd 2 — so the probe bypassed the
   pipe while the pump was fine. The run-2 diagnostics that disproved the transport hypothesis are
   quoted in the test's own header (`BnStderrPumpAndroidTest.kt:32-34`): `dup2Result=2 readFd=84
   writeFd(closed)=-1`, reader alive, and the explicit-fd-2 control write **passed**.
4. #193 (`cf80930`) replaced the instrument: the probe now writes through
   `ParcelFileDescriptor.fromFd(2)` — a **dup of fd 2**, which shares the open file description, so
   a write to it lands wherever fd 2 currently points. **The assertion was not weakened**: it
   remains a strict `assertEquals` on the whole `BnLogRecord` (priority, category, message), and
   the baseline went **212 → 213**.
5. Run [29948996623](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/29948996623) —
   `Instrumented totals across 1 suite(s): tests=213 failures=0 errors=0 skipped=0`.

**So `Os.pipe()` + `Os.dup2()` over fd 2 IS device-proven on a real Android runtime.** The audit
accepts the fix as a genuine resolution rather than a dodge, on the specific ground that the
*assertion* did not soften — only the *instrument used to reach fd 2* changed, and the substitution
is justified by a mechanism (dup shares the open file description) rather than by the result.

**One residual, stated:** run 29948996623 is on `c779294`, the **branch tip** of
`fix/191-android-pump-diagnostic`, not on the squash-merge commit `cf80930`. The tree content is
identical; the lane has not been dispatched on `main` since the merge. This is the standard advisory-lane
posture, not a gap introduced here — but it is named rather than rounded to "green on main."

---

## Findings

**F1 — the JVM 148 is UNVERIFIED on this host.** `./gradlew testDebugUnitTest` with `JAVA_HOME` on
JDK 21 **failed before running a single test**: `:verifyNativeAssets FAILED — Native build outputs
missing`, wanting **both** `linux-bionic-x64` and `linux-bionic-arm64`
`BlazorNative.Runtime.so` publishes. That prerequisite is by design (`build.gradle.kts:385-398`, and
`ci.yml:1648-1650` orders the CI step after both publishes for exactly this reason) and producing it
needs an NDK cross-publish this audit did not run. So **148 is verified by gate-literal ↔ README
reconciliation only** — the same posture M8/M9/M10 took for their un-runnable lanes, recorded rather
than papered over. It last ran green in `build-test` on the audited tip's own PR chain.

**F2 — the route-map drift test proves the GENERATOR, not the full BUILD WIRING (partially closed
by observation).** `RouteTableDriftTests` calls `RouteManifest.Extract`/`ToJson` **in-process** and
compares to the page manifest; it does **not** read the emitted
`res/raw/blazornative_routes.json` off disk. So on its own it could not catch an MSBuild target
that stopped invoking the tool, or a resource that stopped being packaged. **Partially closed
here:** this audit's own `dotnet test` build log shows `BlazorNative.RouteGen: wrote 13 routed rows
to …/res/raw/blazornative_routes.json`, so the target-invokes-generator-writes-file leg **is**
exercised on the required lane, just not *asserted* by it. The remaining leg — resource packaged
into the APK and read by `MainActivity` at Intent-parse — is covered by the **advisory**
instrumented lane (several `*AndroidTest`s launch pages *through* their deep-link route) and by the
device session's **13 routes in one pid**. Recorded so nobody reads a green `build-test` as an
assertion of end-to-end wiring; a file-on-disk assertion would be a cheap strengthening.

**F3 — three docs carried a stale state; fixed by this audit.** Named individually because "a
doc that describes a state which stopped being true" is the exact failure mode M11 kept hitting:
- `MILESTONE.md:337-340` + `:417` and `ROADMAP.md:1800-1802` still read *"the Android instrumented
  lane has not run since before Gate A … `BnStderrPumpAndroidTest` has **never executed** … the 212
  baseline is an expectation."* **All false since #193.** Corrected.
- `MILESTONE.md:320-321`, `ROADMAP.md:1798`, and
  [11.4 conclusion](2026-07-22-phase-11.4-conclusion.md) `:35`, `:39` recorded the Android delta as
  **"210 → 212"**. The gate literal is **213** (`android-instrumented.yml:1040`), and
  `git log -L` on that line confirms M11 entered at **210**. Corrected to **210 → 213**.
- `README.md:297-303` carried a ⚠ block reading *"The Android 213 has been run at 212/213, not yet
  at 213/213 … that has not been observed yet and needs one more dispatch."* It **has** now been
  observed. Corrected.
- The **11.4 conclusion** (`:236-262`) keeps its red-carve-out narrative — that is a **dated
  record** and the repo's discipline is not to retrofit those (M10 audit F2) — but it now carries a
  dated **RESOLVED** amendment pointing at run 29948996623, because its closing sentence
  *"Read DoD #6's closure with this carve-out"* is a live instruction to the reader, not a
  historical note.

**F4 — `[EditorBrowsable(Never)]` is a new surface with NO CI guard.** DoD #5 says *"every new
surface CI-asserted."* The 28 markings from Phase 11.3 Gate C are asserted by **nothing**: no test
under `tests/` references `EditorBrowsable` except one unrelated comment
(`SpikeRazorTwin.cs:16`), and `PublicAPI.Shipped.txt` **does not record the attribute** — its
grammar is signatures, not attributes. So deleting an `[EditorBrowsable(Never)]` from a NOT-API type
is a **silent** change that reds nothing, in a repo whose entire M11 thesis is that unguarded
mechanisms rot. **Not blocking** — the marking is a signpost, not a barrier (criterion S3 says so
explicitly), and the tier table remains the reviewed record. But it is the one place DoD #5's own
sentence is not literally true, and it should be a cheap follow-up (a reflection test over the
NOT-API list).

**F5 — Phase 11.3 has no single conclusion doc.** Every other M11 phase has a `-conclusion.md`. 11.3
has `design` + `api-tiers` + `experimental-policy` + `one-point-oh-criteria` and a very detailed DoD
#4 block in `MILESTONE.md`, so the *decisions are logged* — DoD #5 asks for "a decision log per
phase," which is satisfied in substance. Named because the docs index is now asymmetric and a
future reader looking for `phase-11.3-conclusion.md` will not find one. Phase 11.2's decision log is
the device-proof doc, which is the right artefact for that phase and is not a gap.

**F6 — a numeric contradiction between the 1.0 criteria doc and the two planning docs that cite
it.** [one-point-oh-criteria.md:25-26](2026-07-22-phase-11.3-one-point-oh-criteria.md) reads
**8 MET / 4 OPEN**; `ROADMAP.md:1762` and `MILESTONE.md:225-226` both read **"7 met / 5 open."** The
criteria doc is right — P1 flipped to MET when Phase 11.2's device session landed, and the citing
docs were written against the earlier draft. Corrected in both. *This is exactly the class of drift
`PackageVersionPinTests` exists to prevent for versions and nothing prevents for prose.*

**F7 — carried residuals, none new.** No milestone tag exists or is created. The device lanes stay
advisory and the owner's phone is never a CI dependency. iOS real-device stays deferred (Apple
Developer account), FCM push stays deferred (Firebase project), and the P3 hardening ledger
(#8/#9/#12/#13) stays deferred with triggers unfired — which is itself criterion **Q3**, and the
review it asks for has still not happened.

---

## The debt M11 created — named plainly

**M11 closed six DoD and opened five issues doing it.** That is a legitimate outcome — four of the
five were found by *doing the honest thing* (running on real hardware; reading a generated baseline
line by line) rather than by shipping carelessly. But the milestone must not be reported as
debt-neutral, so:

| Issue | State | Origin | What it costs |
|---|---|---|---|
| [#169](https://github.com/MarcelRoozekrans/BlazorNative/issues/169) | **OPEN** | Phase 11.2 | `BnGeolocationDemo` never displays `GeolocationPosition.Accuracy`. A **sample-app** defect, not a framework one. A fix attempt was **reverted**: three deliberate `Assert.Single` shape/node-id pins in `BnGeolocationDemoTests` plus **both** device suites split the echo on `,` and require exactly two doubles — so it must land as **one cross-shell PR with the device lanes dispatched**. Criterion **Q4**. |
| [#173](https://github.com/MarcelRoozekrans/BlazorNative/issues/173) | **OPEN** | Phase 11.3 | The generated API reference covers **1 of 7** packages — `scripts/generate-reference.ps1` runs xmldoc2md against `BlazorNative.Components.dll` only, so **26 of 88** public types have a reference page. The ~30 other STABLE types (Device's façades, Core's capability records, `BlazorNativeApp`) are documented only in xmldoc nothing publishes. Criterion **D3**. |
| [#178](https://github.com/MarcelRoozekrans/BlazorNative/issues/178) | **OPEN** | Phase 11.2, **on real hardware** | `CapturePhotoAsync()`'s `options = default` zero-initialises `CaptureOptions`, bypassing the record primary-constructor defaults — every consumer silently gets `Quality=0 → 1` and no downscale. **A defect the DevHost bridge could never have shown.** Criterion **Q5**. |
| [#181](https://github.com/MarcelRoozekrans/BlazorNative/issues/181) | **OPEN** | Phase 11.3, **by reading a `.txt`** | `default(BlazorNativePage)` yields a page with a **null mount thunk**, while the type's xmldoc claims the two factories are *"the only way in."* C# guarantees the public parameterless struct ctor. Sibling of #178. Criterion **Q5**. |
| [#191](https://github.com/MarcelRoozekrans/BlazorNative/issues/191) | **CLOSED** | Phase 11.4 | The Android pump's device proof went red; three rounds established a test-side descriptor mismatch. Closed by #193; lane now 213/213. **Resolved within the milestone.** |

**Plus [#155](https://github.com/MarcelRoozekrans/BlazorNative/issues/155)'s two remainders**, which
Phase 11.4 deliberately did **not** tidy away by closing the issue:

1. **The quiet-in-Release *observation* has not been made.** The mechanism is delivered and pinned;
   nobody has installed a Release-configured build on a device or simulator and watched the log go
   quiet. *"Inspection-only and NOT done"* is the phase's own wording, and it is correct.
2. **The level knob is absent from `website/docs/**`.** Verified live in this audit: grep for
   `BnLog` / `logLevel` / `LogLevel` across `website/docs/` returns **zero hits**. A consumer
   reading the published documentation cannot discover that the knob exists.

**Two of the four open issues (#178, #181) are the same trap** — a public struct/record whose
`default(T)` produces an instance the xmldoc says is impossible. They should be fixed together, and
correcting the docs is required whichever design call is made.

---

## The 1.0 tally, reassessed after Phase 11.4

The criteria doc's scoreboard reads **8 MET / 4 OPEN** (A5, Q1, Q2, Q3), written **before** Phase
11.4 landed. Reassessed at `cf80930`:

| Blocker | Was | **Now** | Why |
|---|---|---|---|
| **Q2** — a render error is surfaced; mount does not return `rc 0` on a faulted render | ⬜ open | ✅ **MET** | Phase 11.4 Gate D (#189). A parameter-binding fault **aborts the mount** via the already-documented **`rc 2`** — deliberately not a new rc, because `BlazorNativeRuntime.kt`'s mount `when` ends in `else -> throw`, so a "non-fatal rc 4" would hard-crash every consumer on an older **copied** shell. The classifier keys on **provenance** (two Blazor method identities in the stack), never message text, proven by a test feeding an **impostor carrying #164's message verbatim**; its fail direction is safe. **[#164](https://github.com/MarcelRoozekrans/BlazorNative/issues/164) is CLOSED.** |
| **Q1** — logging is level-gated **and quiet-in-Release** across both shells | ⬜ open | ⬜ **STILL OPEN** | The **level-gated** half is met and over-delivered: one seam, both transports, drift-pinned across C#, Kotlin and Swift, `Warn` by default, and the information-disclosure half closed by `Logger`'s private-by-default interpolation. The **quiet-in-Release** half is **not observed** — the criterion's own second clause. Its owner issue **#155 remains OPEN on exactly this**. Marking it MET would be the ceremonial move this audit exists to refuse. |
| **A5** — `PublicAPI.Shipped.txt` unchanged across ≥1 release cycle | ⬜ open | ⬜ **open** | Unchanged. The baselines landed after 0.4.1; the Unshipped→Shipped drain is an untried step until a release PR performs it. **Phase 11.4 in fact declared new API into `PublicAPI.Unshipped.txt` in two packages** — the baseline red-flagging it on its first live encounter, working as designed, but also confirming the drain is still pending. |
| **Q3** — the P3 hardening ledger resolved **or** each item accepted in writing | ⬜ open | ⬜ **open** | Unchanged. The *review* has still not happened. *"Accepted, triggers unfired" is a valid result — what is not valid is never looking.* |

### **Current tally: 9 MET / 3 OPEN of 12 blockers.**

**MET:** A1, A2, A3, A4, P1, **Q2**, D1, D2, D4. **OPEN:** A5, Q1, Q3.

**The honest reading:** M11 moved 1.0 from *7 met* through *8 met* to *9 met*, and the three
remaining blockers are of three different kinds — **A5 is time** (one release cycle must elapse),
**Q1 is one observation** (install a Release build, watch the log), **Q3 is one decision** (review
four ledgered issues and write down the accept). None is a build. **1.0 is blocked on an
observation, a decision, and a release cycle** — which is a materially different position from where
M11 opened.

Non-blocking criteria are unchanged except **D3**, which stays ⬜ partial and now has an issue
number ([#173](https://github.com/MarcelRoozekrans/BlazorNative/issues/173)), and **Q5**, which is
new and carries #178 + #181.

---

## The conclusion docs, listed (DoD #5)

- **Phase 11.0** — [design](2026-07-20-phase-11.0-design.md) · [conclusion](2026-07-20-phase-11.0-conclusion.md) · [footgun audit](2026-07-20-phase-11.0-footgun-audit.md)
- **Phase 11.1** — [design](2026-07-20-phase-11.1-design.md) · [conclusion](2026-07-21-phase-11.1-conclusion.md) · [friction ledger](2026-07-21-phase-11.1-friction-ledger.md) · [walkthrough](2026-07-21-phase-11.1-walkthrough.md)
- **Phase 11.2** — [design](2026-07-21-phase-11.2-design.md) · [device runbook](2026-07-21-phase-11.2-device-runbook.md) · [**device proof**](2026-07-22-phase-11.2-device-proof.md) *(the phase's decision log)*
- **Phase 11.3** — [design](2026-07-21-phase-11.3-design.md) · [api tiers](2026-07-21-phase-11.3-api-tiers.md) · [`[Experimental]` policy](2026-07-21-phase-11.3-experimental-policy.md) · [1.0 criteria](2026-07-22-phase-11.3-one-point-oh-criteria.md) *(no single conclusion doc — F5)*
- **Phase 11.4** — [design](2026-07-21-phase-11.4-design.md) · [conclusion](2026-07-22-phase-11.4-conclusion.md)
- **Phase 11.5** — this audit.

---

## What this milestone actually changed

**M11 is the milestone where the project stopped taking its own word for things.**

Every prior milestone added capability. M11 added **almost no user-facing feature** — one DI seam
(`ConfigureServices`, and only because dogfooding proved the composition root was sealed) — and
instead attacked the four ways the project was still asking to be trusted:

1. **A hand-written mirror became a derivation.** The deep-link route map was the last
   single-source-of-truth violation a consumer could silently break. It is now generated from the
   app's own source by a Roslyn tool that ships **inside** the runtime package, so a stranger's
   `dotnet new` app derives **its own** map — and the artefact is `.gitignore`d, so it cannot
   quietly become hand-maintained again. The footgun audit then proved it was the *only* page-keyed
   hand-edit, which is the part that turns a fix into a closed question.

2. **The emulator stopped being the last word.** Four capabilities ran on a Xiaomi 24069PC21G,
   Android 16 / SDK 36 — **newer than any CI lane**. The result that matters is not that they
   worked but that **the OS keystore was observed refusing to decrypt an auth-bound key without
   fresh Class-3 authentication and permitting it with** — enforcement, not an app-level status
   echo. It also found a DoD clause that was **wrong** (EXIF), and a defect no emulator could have
   produced (#178). *A device session that only confirms is a session that was not looking.*

3. **"Works on my repo" became "works from nuget.org."** Two apps built outside the repo from
   published packages only, one of them from the **published template** — which forced 0.4.0 to be
   the first release ever to publish `BlazorNative.Templates`, closing an M8 carryover and making
   the getting-started page's front-door claim true for the first time. The 4-IL2072 trim yardstick
   held on three RIDs *with twelve third-party packages layered on*.

4. **"API changes without notice" became a gate.** Eighty-eight public types classified in a
   reviewable table **before** any baseline was generated — the ordering is the point, because a
   baseline generated first acquires unearned authority. Six `PublicAPI.Shipped.txt` files now make
   a break **visible** (not impossible — `bump-minor-pre-major` is still on, and the audit records
   that distinction). And **reading** those files, rather than generating them, found two latent
   API traps (#178, #181) that no test and no bug report had.

5. **Diagnostics stopped going nowhere.** Before M11 the framework's own output reached **nobody on
   either shipping platform** — Android discarded process stderr and the repo *depended on that*;
   iOS emitted unconditional `NSLog` in Release. There is now one level-gated seam, both shells have
   a transport, and 31 + 78 call sites were swept with drift pins holding both — the iOS one with a
   **deletion guard**, because *"no bare NSLog"* is trivially satisfiable by deleting every
   diagnostic. And #164's other half: a parameter-binding fault now **aborts** the mount instead of
   reporting `rc 0` over a half-rendered screen.

**The load-bearing invariant held.** The frozen **80-byte callbacks bridge / 10 exports / 5 ops** is
untouched by M11. The only struct that moved is the init-**input** — and it did not even grow:
`LogLevel` landed at **offset 28 with `SizeOf` still 32**, inside tail padding alignment-8 had
already reserved, pinned by **both** assertions together because *neither alone distinguishes "free"
from "smuggled in at a cost."*

**What M11 did not do, stated:** it did not cut 1.0 (by design — scoping decision 5), did not
freeze the API (marked, not frozen), did not prove iOS on a device, did not observe a Release build
going quiet, and did not run the P3 ledger review. It also **created five issues while closing six
DoD**, four of which are real product debt. That is the honest ledger.

---

## Recommendation

### Is M11 complete?

**Yes — PASS WITH FINDINGS.** All six DoD are met on evidence verified at the audited tip: the route
map is generated and drift-guarded and the footgun audit is written; the device proof exists and is
honest about its four gaps with an EXIF amendment that is argued rather than waived; two apps were
built outside the repo from nuget.org with a friction ledger and a walkthrough; six PublicAPI
baselines gate six packages from `.targets` with RS0016/17/37 as errors and a mutation that
**reds**; every new surface but one is CI-asserted with a decision log per phase; and the logging
seam, both transports, the drift pins and #164's rc-2 fix are all live — with the Android transport's
device proof now **genuinely green at 213/213**, not carved out.

The seven findings are recorded, not waived. **F4** (no CI guard on `[EditorBrowsable(Never)]`) is
the only one that touches a DoD's literal wording and is the natural first follow-up. **F1** is the
one UNVERIFIED lane and is stated as such.

### Closure, restated mechanically

This PR's five required gates green (`build-test`, `android-build`, `ios-build`, `pr-title`,
`footer-check`) → merge → **M11 is closed on this audit**. **No tag** — the milestone-tag namespace
was retired in Phase 8.6 and `vN.0` will never be cut again; a 1.0.0 *package* release, if the owner
cuts it, is a separate release-please tag and a separate decision. This audit is docs/planning-only
(no source change); doc-only commits ride as `docs:` and do not bump.

### The owner's standing actions

1. **Observe a Release build going quiet on a device** — the single remaining half of criterion
   **Q1** and of [#155](https://github.com/MarcelRoozekrans/BlazorNative/issues/155). Cheapest
   blocker on the 1.0 list.
2. **Run the P3 ledger review** (#8/#9/#12/#13) — criterion **Q3**. *"Accepted, triggers unfired"*
   closes it; never looking does not.
3. **Let one release cycle drain `PublicAPI.Unshipped.txt`** — criterion **A5**; two packages are
   already carrying undrained entries from Phase 11.4.
4. **Fix #178 + #181 together** — the same `default(T)` trap on two types; criterion **Q5**.
5. **Apple Developer account** (real-device iOS) and **a Firebase project** (FCM push) — unchanged
   triggers.
