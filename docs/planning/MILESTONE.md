# Milestone 11 — Production Readiness

**Status:** 🔄 **active — opened 2026-07-20.** 4 / 6 DoD closed (#1 — Phase 11.0; #2 — Phase
11.2; #3 — Phase 11.1; **#4 — Phase 11.3**).
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

   ⚠ **AMENDMENT (2026-07-22) — the camera clause's "+ EXIF" is WITHDRAWN as unsatisfiable, and
   replaced.** The clause as written asks the capture to carry camera EXIF (`Make` / `Model` /
   `DateTimeOriginal` / exposure) as the anti-emulated-shutter proof. **It cannot be satisfied
   without a regression**, and the reason is a deliberate framework behaviour rather than a
   device or session failure: the Android shell **normalises orientation** by baking the EXIF
   rotation into the pixels *and resetting the tag to identity*, then re-encoding the JPEG —
   precisely so EXIF-honouring decoders (Coil on Android, Kingfisher on iOS) do not rotate the
   image a second time (`src/BlazorNative.Jni/.../AndroidShellBridge.kt:1152-1156`, `:1300-1307`,
   where the shell says so in its own comments). A re-encoded JPEG carries **no camera EXIF at
   all**; the real capture reported `EXIF present: False`, `JFIF present: True`. Demanding EXIF
   would be demanding the shell stop doing the thing it is designed to do. **Replaced by — and
   this is what DoD #2 now requires as real-sensor evidence:** (a) the reported capture
   **dimensions match a genuine sensor resolution** (the session got **3072×4096**, 12.6 MP — no
   AVD shutter produces that), (b) the frame is a **real photographed scene**, recognisable, not
   a synthetic green/checkerboard, and (c) it arrived via **`MediaStore.ACTION_IMAGE_CAPTURE`** —
   the system camera app that owns the physical sensor — and round-tripped through FileProvider →
   path → C-ABI → .NET → `BnImage`. The clause is amended rather than waived: the *"no emulated
   shutter"* burden is unchanged, only the instrument that discharges it.

   ✅ **Closed by Phase 11.2** (2026-07-22 — the owner's device session, run manually on the
   phone). **Device:** Xiaomi 24069PC21G (`peridot`), `arm64-v8a`, **Android 16 / SDK 36** —
   *newer than any CI lane exercises* — reporting `android.hardware.strongbox_keystore=300`.
   Both bionic publishes held the **4-IL2072 yardstick** exactly; the APK carried both
   `lib/arm64-v8a/` (5.4 MB) and `lib/x86_64/` (5.2 MB) `.so`s.
   **Mechanism + evidence, per capability:** **Geolocation** — app echo
   `fix:‹lat›,‹lon›`, **identical to platform rounding** against `dumpsys location`'s
   fused *and* network last-known records, so the value round-tripped faithfully rather than
   merely looking plausible. **Notifications** — `POST_NOTIFICATIONS` genuinely flipped
   `granted=false` → `granted=true` (the API 33+ runtime prompt, on API 36); a real
   `NotificationRecord(pkg=io.blazornative.shell … id=7 … channel=blazornative_default …)` posted;
   it **survived a process kill** and a shade tap **cold-started a new pid (16535)** onto
   `[BOOT] mounted BnNotificationsDemo` + `arrived:/notifications`. **Camera** — no permission
   prompt, which is *correct* (`ACTION_IMAGE_CAPTURE` means the system camera owns the sensor —
   the Phase 11.0 footgun audit's claim, confirmed on hardware); echo
   `captured:3072x4096:93670`, the photo rendered back into `BnImage`. **Biometrics + secure
   storage** — **three distinct `keystore2` challenges**, `isStrongBiometric=true`, and a complete
   positive/negative pair: `Unlock + CANCEL → status:AuthFailed` versus
   `Unlock + FINGERPRINT → value:hunter2`. That is **the OS refusing to decrypt the auth-bound key
   without fresh Class-3 authentication and permitting it with** — enforcement, not the app
   choosing what to display, and a stronger result than the runbook had planned for (it expected
   to settle for a device self-report plus an AVD negative control, since nothing in the repo
   surfaces `KeyInfo.getSecurityLevel()`). **Deep link on hardware:** the **cold**
   `blazornative://notifications` resolved at Intent-parse **before the .NET runtime loaded**, and
   **all 13 routes** deep-linked in sequence with **pid 16535 unchanged — no crash on any route**,
   proving Phase 11.0's generated route map end-to-end on a device.
   **Finding filed:** [#178](https://github.com/MarcelRoozekrans/BlazorNative/issues/178) —
   `CapturePhotoAsync()`'s `options = default` zero-initialises `CaptureOptions`, bypassing its
   record primary-constructor defaults, so every consumer silently gets `Quality=0` → `1` and no
   downscale (`Camera.cs:19`, `IMobileBridge.cs:384`, `AndroidShellBridge.kt:1330`). A defect the
   DevHost bridge could never have shown.
   **NOT exercised, recorded rather than rounded off:** the **warm** notification tap-through (the
   warm re-route path it depends on *was* proven on the same hardware via a warm
   `blazornative://geolocation`); the **location permission prompt** (pre-granted on this device —
   the notification prompt *was* exercised); the **interaction smoke is PARTIAL** (the starter
   page's text-input echo was driven end-to-end; `/list` `/scroll` `/form` `/modal` mounted
   without crash but were not manually driven); and the **standalone `IBiometrics.AuthenticateAsync`
   path** (a raw session-log label reading `Authenticate → status:Ok` resolves against the code to
   **`Set`** — `BiometricStatus` has no `Ok` member, `SecureStorageStatus` does — so the biometrics
   clause rests **solely** on the `Set` / `Unlock+cancel` / `Unlock+fingerprint` triple, which is
   the stronger basis anyway since all three are the OS keystore enforcing rather than an app-level
   status echo). Cause of the interaction gaps: **MIUI blocks `adb shell input` entirely** — no
   `MotionEvent` reached the app — so **every tap was a human tap** and nothing could be scripted. Also unchanged: the key's security level is a **device report**, not
   an app observation; geolocation `Accuracy` is still not surfaced
   ([#169](https://github.com/MarcelRoozekrans/BlazorNative/issues/169)); one device, one OEM
   skin, one OS version; **iOS real-device remains deferred**. Screenshots and logcat dumps stayed
   in the session scratchpad **outside the repo** — the verbatim excerpts in the proof doc are the
   in-repo record.
   [Device proof](../plans/2026-07-22-phase-11.2-device-proof.md) ·
   [runbook, amended](../plans/2026-07-21-phase-11.2-device-runbook.md) ·
   [design](../plans/2026-07-21-phase-11.2-design.md).

3. **Consumer dogfooding — a stranger can really ship.** A **fresh app outside this repo**
   consumes the **published 0.2.0** packages (`dotnet new blazornative` → the 7 `dotnet add
   package` refs, no `ProjectReference`), builds the Android (and iOS-sim) shell, and runs. The
   getting-started path is walked as a newcomer would; **every friction point found is fixed**
   (docs, template defaults, error messages, missing steps) or ledgered with a reason. The
   result: a written, reproducible "zero-to-running app" that does not touch the repo sources.

   ✅ **Closed by Phase 11.1** (2026-07-21). **Mechanism:** two apps built **outside this repo**
   from **nuget.org only**, no `ProjectReference` — `bn-baseline` on published **0.3.0** and
   `bn-zeroalloc-showcase` on published **0.4.0**, the latter scaffolded from the **published**
   template. **0.4.0 is the milestone release**: it shipped the `ConfigureServices` seam
   ([#159](https://github.com/MarcelRoozekrans/BlazorNative/pull/159)), the KDoc sweep (#161), a
   nuget-preflight fix (#163) — and, via [#162](https://github.com/MarcelRoozekrans/BlazorNative/pull/162),
   it is the **first release ever to publish `BlazorNative.Templates`**, closing the M8 carryover and
   making the getting-started docs' front-door claim true (verified live: a real
   `dotnet new install BlazorNative.Templates` resolved `@0.4.0`). **Evidence:** every publish —
   `win-x64`, `linux-bionic-x64`, `linux-bionic-arm64`, both apps — emitted **exactly the 4 accepted
   IL2072s** and zero other trim/AOT warnings, *including with 11 ZeroAlloc packages layered on*
   (no `Microsoft.CodeAnalysis` diamond, no duplicate-generator emit); dual-ABI APKs built from
   `gradlew assembleDebug`; RouteGen derived the deep-link map **from the packages alone** and
   regenerated it on an added page with **zero shell hand-edits** (11.0's claim confirmed for a
   consumer); and the new DI seam was proven **at runtime** — an ABI harness P/Invoked the
   *published* NativeAOT binary, replayed `blazornative_init` → `register_frame_callback` → `mount`,
   and the first frame's patches carried the app service's own output (a shared-singleton count
   proving instance identity), all 6 pages `rc = 0`. One package dropped **with a written reason**
   (`ZeroAlloc.Cache` 1.1.15 — fails at `csc`, never reached the trim gate; upstream
   [Cache#87](https://github.com/ZeroAlloc-Net/ZeroAlloc.Cache/issues/87)). **Friction:** 14 items,
   each fixed / resolved / dismissed-with-investigation / ledgered-with-an-owner / deferred-by-decision
   — docs fixes in [#157](https://github.com/MarcelRoozekrans/BlazorNative/pull/157) (the "seven
   packages" line → 3 direct + 4 transitive; the version literal made version-*agnostic*; the
   desktop-dev-loop claim made honest with **no host invented**; the three env prerequisites), the
   template DI `using` in flight as **#165**, and the rc-0-on-faulted-render design gap filed as
   [#164](https://github.com/MarcelRoozekrans/BlazorNative/issues/164) → **Phase 11.4 / DoD #6**.
   **Boundaries kept explicit:** pages mount and render their *initial frame* (`OnInitialized` /
   `OnAfterRenderAsync` ran) but **no UI event was dispatched**; capabilities ran on the **DevHost
   bridge**, the APK was built **but not installed**, so real hardware stays **DoD #2 / Phase 11.2**;
   **iOS-sim deferred** per scoping decision #3.
   [Conclusion](../plans/2026-07-21-phase-11.1-conclusion.md) ·
   [friction ledger](../plans/2026-07-21-phase-11.1-friction-ledger.md) ·
   [zero-to-running walkthrough](../plans/2026-07-21-phase-11.1-walkthrough.md).

4. **API stability + the 1.0 path.** The **public API surface** of the shipped packages is
   reviewed and its stable core identified; unstable/experimental surface is **marked** (a
   public-API baseline — e.g. `Microsoft.CodeAnalysis.PublicApiAnalyzers` `PublicAPI.*.txt` — so
   an accidental breaking change reds a PR, and/or `[Experimental]` on genuinely-unstable bits).
   **Concrete 1.0 criteria** are written down (what must be true — API frozen, both shells
   real-device-proven where possible, docs complete, the deferred ledger resolved-or-accepted).
   The README's "API changes without notice" claim is updated to reflect the marked-stable
   surface.

   ✅ **Closed by Phase 11.3** (2026-07-22), across four gates.

   **Mechanism.** **Gate A** ([#176](https://github.com/MarcelRoozekrans/BlazorNative/pull/176))
   classified all **88** public types into **55 STABLE / 2 PROVISIONAL / 31 NOT-API** in a
   reviewable [tier table](../plans/2026-07-21-phase-11.3-api-tiers.md), decided the four
   contentious calls **in writing** (`NativeRenderer` = NOT-API, marked not moved; `DevHostBridge`
   = PROVISIONAL; `INavigationManager` stays in `.Core` with `[TypeForwardedTo]` as the recorded
   mitigation), and wrote the **consume-only interface-additions policy** into the shipped xmldoc
   of `IMobileBridge` and `INavigationManager` — *before* the first post-1.0 addition, which is
   the only thing that makes it honest. **Gate B**
   ([#180](https://github.com/MarcelRoozekrans/BlazorNative/pull/180)) landed **six**
   `PublicAPI.Shipped.txt` baselines (1 166 lines) with `RS0016`/`RS0017`/`RS0037` escalated to
   **errors** via a per-package `BnEnforcePublicApi` property in `src/Directory.Build.targets` —
   **`.targets`, not `.props`**, the CS1591 lesson re-applied — plus an analyzer **diagnostic-ID
   roster** pin (the 7 `BN00xx` ids, both directions) replacing a `.txt` baseline for the
   Analyzers package, whose real contract a `.txt` cannot express. **Gate C**
   ([#183](https://github.com/MarcelRoozekrans/BlazorNative/pull/183) — **open at the time Gate D
   was written**, so criterion A3 is the one blocker this milestone entry does not yet claim)
   marked **28 of 31** NOT-API
   types `[EditorBrowsable(Never)]` with a per-type reason, ledgered the **3 unmarkable**
   generated types rather than skipping them, and wrote the
   [`[Experimental]` policy](../plans/2026-07-21-phase-11.3-experimental-policy.md) — `BN1xxx`
   reserved, disjoint from `BN0xxx`, never reused — with the argued finding that the current
   surface warrants **zero** uses. **Gate D** produced the standalone
   [1.0 criteria](../plans/2026-07-22-phase-11.3-one-point-oh-criteria.md) (**12 blockers**, 7 met
   / 5 open, each open one owned), the consumer-facing
   [API-stability page + compatibility statement](../../website/docs/api-stability.md), and the
   README re-cut.

   **Evidence — the strongest single fact is a mutation, not a green build.** Renaming
   `BnButton.Label` → `BnButton.Text` produced **`error RS0016` ×2 + `error RS0017` ×2 and exit
   1**. A green `build-test` would have proven nothing here: CS1591 *"read as ON and was OFF"*
   **twice** (`src/Directory.Build.targets:51`–`:63`), and this gate was wired specifically to not
   reproduce that. The `.razor` risk was also closed empirically rather than assumed — `BnSlider`'s
   23 generator-produced parameters appear in the baseline, so the pin has no hole where the
   consumer surface is widest.

   **Findings the review produced, which is the point of reading a baseline rather than
   generating one.** [#181](https://github.com/MarcelRoozekrans/BlazorNative/issues/181) —
   `default(BlazorNativePage)` yields a page with a **null mount thunk**, because C# guarantees a
   public parameterless constructor on every struct and the type's xmldoc claims the two factories
   are *"the only way in."* Its sibling
   [#178](https://github.com/MarcelRoozekrans/BlazorNative/issues/178) is the same trap on
   `CaptureOptions`. Neither was found by a test or a bug report; both were found by reading a
   generated `.txt` file line by line. They are carried as criterion **Q5**.

   **Boundaries kept explicit:** the API is **marked, not frozen** — `bump-minor-pre-major` is
   still on and a minor may still break the surface *deliberately*; the baseline makes a break
   **visible**, not impossible. Making `NativeRenderer` + the patch model `internal` is a
   **breaking** change deliberately **not** made here (criterion S3), so a consumer can still bind
   to them — `[EditorBrowsable(Never)]` is a signpost, not a barrier. **1.0 is defined here, not
   cut here** (scoping decision 5).

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

- **The physical-phone proof** (M9 owner-owed) — ✅ **DISCHARGED 2026-07-22.** Consumed by DoD #2
  and now actually *done*: the owner ran the session on a Xiaomi 24069PC21G (Android 16 / SDK 36,
  `arm64-v8a`) and all four capabilities were exercised against real hardware — a real fused
  location fix, a real notification post + grant + cold tap-through, a real system-camera capture,
  and the **OS keystore** enforcing auth-bound decryption against a real enrolled fingerprint. The
  ledger item is closed; it immediately paid for itself with
  [#178](https://github.com/MarcelRoozekrans/BlazorNative/issues/178), a defect only a real
  encoder could have surfaced. [Device proof](../plans/2026-07-22-phase-11.2-device-proof.md).
- **The KDoc sweep + map extraction** (M8) — trigger was "before the first Release that
  publishes the template pack"; the map-extraction half is **done** (Phase 11.0 RouteGen retired the
  inline map + its excision), and the KDoc-correctness half + **publishing the template pack** +
  the **`ConfigureServices` DI seam** are the three changes bundled into the **0.4.0** release that
  11.1 Gate C/D depends on — see [0.4.0-prep design](../plans/2026-07-20-phase-0.4.0-prep-design.md).
  ✅ **DISCHARGED** — **0.4.0 published 2026-07-21** with all three (#161 KDoc, #162 template
  publish, #159 the seam; #163 the preflight fix). It is the **first release to publish
  `BlazorNative.Templates`**, verified live by a real `dotnet new install`.
- **From M5:** FCM push (carried, trigger above).
- **The P3 perf-hardening ledger** (#8/#9/#12/#13) — deferred with revisit-triggers unfired;
  M11 may revisit under DoD #4 if a stability review surfaces one, else it stays ledgered.
- **CI posture:** five required contexts unchanged; advisory device lanes unchanged; the owner's
  phone is never a CI dependency.

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open:

- **Phase 11.0** — deep-link route codegen + the consumer-footgun audit (DoD #1) — *the seed
  finding; a concrete single-source-of-truth fix, first because dogfooding will lean on it.*
- ✅ **Phase 11.1** — consumer dogfooding on the published packages (DoD #3) — *complete
  (2026-07-21); walked on 0.3.0 then 0.4.0, not 0.2.0, because both shipped mid-phase.* Dogfooding surfaced the
  **sealed composition root** as a real gap, so 11.1 also grows one framework `feat` — a public
  `BlazorNativeApp.ConfigureServices` app-service DI seam — shipping in **0.4.0** (cross-ref
  DoD #4 / the Phase 11.3 PublicAPI baseline). See
  [phase-11.1 design](../plans/2026-07-20-phase-11.1-design.md).
- ✅ **Phase 11.2** — real-device Android validation, all capabilities, recorded (DoD #2) —
  *complete (2026-07-22); owner-run on the phone, the milestone's honesty check.* All four
  capabilities exercised on a Xiaomi 24069PC21G (Android 16 / SDK 36); the generated route map
  proven across **all 13 routes** in one process; **cold** deep link + **cold** notification
  tap-through both landed on the right page. Two findings: **#178** (`CaptureOptions` defaults
  never apply) and the **DoD #2 EXIF-clause amendment** above. Runbook amended from the session
  (MIUI blocks `adb` input injection; `force-stop` clears notifications; `installDebug` fails
  `INSTALL_FAILED_USER_RESTRICTED`; the JDK/NDK env traps recurred).
  [Device proof](../plans/2026-07-22-phase-11.2-device-proof.md) ·
  [design](../plans/2026-07-21-phase-11.2-design.md) ·
  [device runbook](../plans/2026-07-21-phase-11.2-device-runbook.md).
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
