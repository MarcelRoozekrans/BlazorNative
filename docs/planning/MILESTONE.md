# Milestone 11 — Production Readiness

**Status:** 🔄 **active — opened 2026-07-20.** 1 / 6 DoD closed (#1 — Phase 11.0).
**Predecessor:** Milestone 10 — Consolidation & Hardening, complete 2026-07-19
([final audit](../plans/2026-07-19-milestone-10-final-audit.md), all 7 DoD PASS; no tag — 8.6 rule).
**Source:** owner direction (2026-07-20): *"work towards a production-grade framework,"* dogfood
the published packages, and *test on an actual Android device.* Seeded by a concrete finding —
the deep-link route map is the last hand-written single-source-of-truth violation.

## Goal

M1–M10 built, published (0.2.0 on nuget.org), and hardened the library — it *works*, and its
docs are honest. M11 takes it from *works* to **production-grade**: an app author can build a
real app on the published packages **without footguns**, the capabilities an emulator only
*pretends* to have are **proven on real Android hardware**, and the public API is understood
well enough to **commit to it** — with concrete criteria for a 1.0. This is the milestone where
the project stops being a proof-of-concept and starts being something a stranger can depend on.

## Scoping decisions (owner, 2026-07-20)

1. **All four pillars are in** (owner chose all): deep-link route codegen, real-device Android
   validation, consumer dogfooding, and API stability + a path to 1.0.
2. **Real-device Android proves ALL capabilities end-to-end** (owner has the phone): camera,
   biometrics, geolocation, and notifications, plus an interaction smoke — on the physical
   device over `adb`/USB, not a CI node. CI stays on the emulator/simulator lanes; the phone is
   the honesty check, and its results are RECORDED (a device-proof doc), not asserted in CI.
3. **iOS real-device stays deferred** — still no Apple Developer account. Device proof is
   **Android-only**; iOS remains simulator-scoped and labeled as such (unchanged since M5).
4. **Dogfooding consumes the PUBLISHED 0.2.0 packages** from nuget.org (not the in-repo
   `ProjectReference` sample) — the real "a stranger `dotnet new`s and ships" path.
5. **1.0 is DEFINED here, not necessarily CUT here.** M11 identifies + marks the stable API
   surface and writes the concrete 1.0 criteria; whether to actually graduate to 1.0.0 (a
   `Release-As: 1.0.0` package tag — NOT a milestone tag) is a separate owner go once the
   criteria are met. The `bump-minor-pre-major` graduation trap is respected.

## Definition of Done

1. **Deep-link routing derives end-to-end — the hand-written mirror is gone** (the seed
   finding). `MainActivity.kt`'s `DEEP_LINK_COMPONENTS` map is no longer hand-maintained: a
   **build-time step generates** the route→component map from the app's registered pages (the
   same `SampleAppPages.All` the drift test already parses) into a generated Android resource or
   Kotlin file that `MainActivity` reads at Intent-parse time (still before the .NET runtime
   loads — the runtime constraint holds; only the *source* changes from hand-written to
   generated). An app author who adds a routed page gets the deep-link mapping **for free** — no
   hand-edit, no silent wrong-screen footgun. The drift test flips from *checking* a
   hand-written map to *verifying the generated one* (or is retired if generation makes it
   vacuous, with a written rationale). **Plus a footgun audit:** enumerate every other place a
   consumer must hand-edit a shell file when adding a page/capability, and derive or document
   each.

   ✅ **Closed by Phase 11.0** (2026-07-20). **Mechanism:** `MainActivity`'s hand-written
   `DEEP_LINK_COMPONENTS` map is gone — `BlazorNative.RouteGen` parses the app's C# **source**
   (Roslyn, so it loads no per-RID dll and is **arch-independent** — the arm64 pivot away from an
   assembly-load approach that could not survive CI) for `Routed<T>(route, name)` rows and emits
   `res/raw/blazornative_routes.json` at build time; `MainActivity` reads it at Intent-parse. The
   generator ships **inside the `BlazorNative.Runtime` package**, so a `dotnet new` app derives its
   **own** map (template-smoke proves it). `RouteTableDriftTests` flipped from mirroring a
   hand-written map to **verifying the generated one** pair-for-pair; the Kotlin-text pin is retired
   (nothing left to drift), the default-fallback + content pins kept. **Audit outcome
   ([footgun audit](../plans/2026-07-20-phase-11.0-footgun-audit.md)):** the deep-link map was the
   *only* page-keyed shell hand-edit (no other `when`/map is per-page); every capability's Android
   manifest surface — permissions, camera `<queries>`, the FileProvider `<provider>` + `file_paths.xml`,
   the notification `<receiver>` — is **template-supplied (DERIVED)**, so nothing is hand-added to use
   a capability (the shell is copied source, no manifest-merge needed); the un-derivable rest (app
   identity, the per-app URI scheme, iOS usage-description *copy*, the iOS root-component source edits +
   csproj recipe) is **DOCUMENTED** where a consumer looks. Three stale consumer docs (quick-start,
   shells/android, shells/ios) were corrected.
   [Conclusion](../plans/2026-07-20-phase-11.0-conclusion.md).

2. **Real-device Android proof — all capabilities, recorded.** The app runs on the owner's
   physical Android phone; **camera** (real sensor + EXIF, no emulated shutter), **biometrics**
   (real fingerprint/face + a TEE-backed AndroidKeyStore, not the AVD's software keystore),
   **geolocation** (real GPS/fused location), and **notifications** (real post + tap-through,
   cold and warm) are each exercised and **recorded** in a device-proof doc (steps, `adb`
   invocations, observed results, screenshots/log excerpts). This **discharges the standing
   physical-phone ledger item** — the two least-emulated (camera, biometrics) get the honest
   proof the emulator couldn't give.

3. **Consumer dogfooding — a stranger can really ship.** A **fresh app outside this repo**
   consumes the **published 0.2.0** packages (`dotnet new blazornative` → the 7 `dotnet add
   package` refs, no `ProjectReference`), builds the Android (and iOS-sim) shell, and runs. The
   getting-started path is walked as a newcomer would; **every friction point found is fixed**
   (docs, template defaults, error messages, missing steps) or ledgered with a reason. The
   result: a written, reproducible "zero-to-running app" that does not touch the repo sources.

4. **API stability + the 1.0 path.** The **public API surface** of the shipped packages is
   reviewed and its stable core identified; unstable/experimental surface is **marked** (a
   public-API baseline — e.g. `Microsoft.CodeAnalysis.PublicApiAnalyzers` `PublicAPI.*.txt` — so
   an accidental breaking change reds a PR, and/or `[Experimental]` on genuinely-unstable bits).
   **Concrete 1.0 criteria** are written down (what must be true — API frozen, both shells
   real-device-proven where possible, docs complete, the deferred ledger resolved-or-accepted).
   The README's "API changes without notice" claim is updated to reflect the marked-stable
   surface.

5. **Hygiene + close.** Every new surface CI-asserted (the codegen output drift-guarded, the
   public-API baseline gated); a decision log per phase; the device-proof doc; a **final audit**
   verifying all of the above. **No milestone tag** (8.6 rule — closure is the audit); a 1.0.0
   *package* release, if the owner cuts it, is a separate release-please tag.

6. **Logging discipline — quiet-in-Release, level-gated, unified**
   ([#155](https://github.com/MarcelRoozekrans/BlazorNative/issues/155)). Diagnostic logging today
   is **not build-configuration gated** and behaves **differently per shell**: iOS `NSLog` emits
   normal-path chatter (`native init ok`, `mounted <component>`, the `[BnWidgetMapper] … ignored`
   volume) in **Release** builds; Android discards the .NET runtime's `Console.Error` to `/dev/null`
   and only surfaces the shell's explicit `Log.e`; the renderer `ILogger` is hard-coded to
   `Warning`. M11 gives the framework **one level-gated logging seam** the runtime + both shells
   route through, **quiet by default in Release** (Info/Debug/Verbose suppressed, Warn/Error only),
   preferring `os_log`/`Logger` over unconditional `NSLog` on iOS and the `Log` sink on Android — so
   the two shells log **consistently**, no internal exception detail/paths leak at default Release
   verbosity, and verbose diagnostics are opt-in. An end user sees none of it either way (no
   on-screen console); this is about a production binary not shipping developer chatter and about
   one honest, controllable logging story across platforms.

## Out of scope for this milestone

- **Real-device iOS / TestFlight** — still gated on the Apple Developer account (trigger
  unchanged).
- **FCM push** — still gated on a Firebase project.
- **New framework surface** — State (#22), Styling (#21), the Navigation package (#23), the CLI
  tool (#24), component expansion (#20) are *growth*, not *readiness*; a later milestone.
- **The inspector channel** (ledgered) — developer tooling, not consumer-facing.
- **Actually cutting 1.0.0** — M11 defines the criteria and marks the surface; the graduation is
  a separate owner decision once the criteria are met.

## Inherited from prior milestones (the ledger M11 consumes or carries)

- **The physical-phone proof** (M9 owner-owed) — ✅ **consumed by DoD #2** (all capabilities, on
  real Android hardware).
- **The KDoc sweep + map extraction** (M8) — trigger was "before the first Release that
  publishes the template pack"; the template pack is still not on nuget.org (separate feed), so
  it stays ledgered — but the DoD #3 dogfooding pass is the natural place to revisit it.
- **From M5:** FCM push (carried, trigger above).
- **The P3 perf-hardening ledger** (#8/#9/#12/#13) — deferred with revisit-triggers unfired;
  M11 may revisit under DoD #4 if a stability review surfaces one, else it stays ledgered.
- **CI posture:** five required contexts unchanged; advisory device lanes unchanged; the owner's
  phone is never a CI dependency.

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open:

- **Phase 11.0** — deep-link route codegen + the consumer-footgun audit (DoD #1) — *the seed
  finding; a concrete single-source-of-truth fix, first because dogfooding will lean on it.*
- **Phase 11.1** — consumer dogfooding on the published 0.2.0 (DoD #3) — *walk the newcomer
  path; the friction it finds feeds back into #1's audit and the docs.*
- **Phase 11.2** — real-device Android validation, all capabilities, recorded (DoD #2) —
  *owner-run over USB; the milestone's honesty check.*
- **Phase 11.3** — API stability review + the 1.0 criteria + public-API baseline (DoD #4).
- **Phase 11.4** — logging discipline (DoD #6, [#155](https://github.com/MarcelRoozekrans/BlazorNative/issues/155)):
  one level-gated logging seam, quiet-by-default in Release, unified across both shells.
- **Phase 11.5** — hygiene + M11 final audit + close (DoD #5).

## Why this milestone exists

The library is published and hardened, but "published" is not "dependable." A consumer today
hand-edits a Kotlin map when they add a page (and their deep link breaks silently if they slip);
the two capabilities that most need a real phone have only ever run on an emulator that fakes
them; nobody has actually built an app from the *published* packages start to finish; and the
API is officially "changes without notice." M11 closes each of those — the footgun, the
emulator honesty gap, the untested consumer path, and the unstable-API disclaimer — so the next
honest sentence about this project is not "it's a proof of concept" but "you can build on it."
