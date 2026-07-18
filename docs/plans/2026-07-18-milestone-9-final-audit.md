# Milestone 9 — Final Audit (Phase 9.4)

**Date:** 2026-07-18
**Auditor:** Phase 9.4, against `phase-9.4-m9-close` (branched from `main` at `c8dcb28` — the
audited tip; no tag follows, the 8.6 rule).
**Predecessor:** [M8 final audit](2026-07-17-milestone-8-final-audit.md) (PASS on all six; no tag —
the milestone-tag namespace was retired in Phase 8.6, and a milestone closes on its audit).
**Contract audited:** [`docs/planning/MILESTONE.md`](../planning/MILESTONE.md) — the six M9 DoD
criteria.

## Method

The house standard, unchanged since M6: **"asserted, not observed"** and **"a mechanism nobody
tested is a mechanism nobody knows."** Applied to the audit itself — **no criterion is accepted
because a conclusion doc says so.** Every verdict below is checked against git history, the live
source, the CI gate literals (the `if`/`-ne` lines that DECIDE, not the step `name:` prose), and the
ABI mirrors — and everything locally runnable was **re-run live at the audited tip**:

- **.NET suite, re-run live:** `dotnet test BlazorNative.sln -c Release` — **754 passed / 0 skipped /
  0 failed** (Runtime.Tests **597** + Renderer.Tests **132** + Analyzers.Tests **25** = 754). Matches
  the `ci.yml` gate literal exactly.
- **The ABI, read at three mirrors:** the .NET struct (`BridgeProtocolNative.cs`), the Swift C header
  (`BlazorNativeRuntimeC.h`), and the pin tests — all agree: **80 bytes / HostCallBegin@72 / 10
  exports / 5 ops**.
- **The three lanes I cannot run here** (JVM Gradle, Android instrumented AVD, iOS XCTest — no Mac,
  no live AVD in this audit host) are verified by **gate-literal ↔ README reconciliation** plus the
  per-phase run-id provenance recorded in ROADMAP. This is stated as a finding (F1), not smoothed
  over — the same posture the M8 audit took for its device lanes.
- **Git, read live:** `git tag -l` → **empty** (0 tags); `git log --oneline` confirms the four M9
  feature PRs landed on `main` as `feat:` commits.

**Verdict: PASS on all six.** The milestone's central thesis — *the ABI grew exactly once (9.0),
generically, and then held for three more capabilities* — is **re-proven against live artifacts**,
not assumed. Non-blocking observations are recorded as findings, never rounded up.

---

## Verdict table

| # | Criterion | Verdict | Evidence (verified at `c8dcb28`) |
|---|---|---|---|
| 1 | The permission pattern, proven + documented | **PASS** | [9.0 conclusion](2026-07-17-phase-9.0-conclusion.md). `docs/bridge-extension.md` grew **section (f) "The Permission Pattern"** (line 268) with the request→prompt→result flow, the pending-registry rule, denial-as-data, the both-shells mapping table, and worked examples for all four capabilities (§7 geo, §8 notifications, §9 biometrics/secure, §10 camera). The denial-as-data contract is code, not prose: `s_pendingHostCalls : ConcurrentDictionary<long, TCS<HostCallResult>>` on `NativeShellBridge.cs:73` (the `s_pendingFetches` twin — `Interlocked` id, `RunContinuationsAsynchronously`, CT→`TrySetCanceled`, unknown-id→`return 1` never a throw in `CompleteHostCall`, line 494). Both shells route: **Android** recreation-survival split — the `requestCode→requestId` map is app-scoped/static so it survives Activity recreation mid-prompt (`AndroidShellBridge.kt:244-250`); **iOS** async `CLLocationManager` with a weak-delegate strong-held handler (`BnGeolocation.swift:15-74`). Pinned falsifiable by the three `*AbiUnchangedTests` (below). |
| 2 | Geolocation (`BlazorNative.Device`) | **PASS** | [9.0 conclusion](2026-07-17-phase-9.0-conclusion.md). `IGeolocation` in the 7th package (`src/BlazorNative.Device/IGeolocation.cs`) over `IMobileBridge.GetCurrentPositionAsync` (Core); `/geolocation` demo is `BnGeolocationDemo.cs` (the 10th routed page); `GeolocationBridgeTests` + `BnGeolocationDemoTests` in the live 754. Phase 9.0 merged as `feat(host): permission pattern + geolocation … (M9 DoD #1+#2) (#118)` (`3866410`). |
| 3 | Local notifications | **PASS** | [9.1 conclusion](2026-07-17-phase-9.1-conclusion.md). `INotifications` in the 7th package; schedule/show/cancel + both tap-through halves — the reserved `"navigate"` host-event name is `Exports.cs:490` (`NavigateEventName`), routed by `DispatchHostNavigate` (line 585) → `NavigateToAsync`; `/notifications` demo is `BnNotificationsDemo.cs` (11th page); `NotificationsAbiUnchangedTests` pins 80/72/op==1 in the live 754. Phase 9.1 merged as `feat(notifications): … the 9.0 ABI's first free reuse (M9 DoD #3) (#127)` (`17e6834`). |
| 4 | Biometrics + secure storage | **PASS** | [9.2 conclusion](2026-07-18-phase-9.2-conclusion.md). `IBiometrics` + `ISecureStorage` in the 7th package; AndroidKeyStore AES/GCM (`AndroidShellBridge.kt`) + iOS Keychain (`BnSecureStorage.swift`); `/secure` demo is `BnSecureDemo.cs` (12th page); `SecureBiometricsAbiUnchangedTests` pins 80/72/ops 2+3. `androidx.biometric:biometric:1.1.0` present in **BOTH** the repo gradle (`src/BlazorNative.Jni/build.gradle.kts:94`) AND the template mirror (`templates/…/android/build.gradle.kts:77`) — drift-enforced. **The M5 secure-storage deferral is marked CLOSED** (MILESTONE.md:146). Phase 9.2 merged as `feat(device): biometrics + OS-key-bound secure storage … (M9 DoD #4) (#128)` (`0bb3865`). |
| 5 | Camera (photo capture) | **PASS** | [9.3 conclusion](2026-07-18-phase-9.3-conclusion.md). `ICamera` is the **5th Device façade** (`src/BlazorNative.Device/ICamera.cs`); Android `ACTION_IMAGE_CAPTURE` + FileProvider, **no CAMERA permission** / iOS `UIImagePickerController` + `NSCameraUsageDescription`; `/camera` demo is `BnCameraDemo.cs` (13th page); `CameraAbiUnchangedTests` pins 80/72/`Camera==4`, and the doc-comment states the file-path handoff (payload NAMES the file, bytes stay on disk). **The M6/M7 natural-size-image ledger CLOSES** (MILESTONE.md:118) and **SafeAreaView is RESOLVED — NOT tripped** (MILESTONE.md:153). Phase 9.3 merged as `feat(device): camera photo capture … a 4th time (M9 DoD #5) (#129)` (`c8dcb28`). |
| 6 | Hygiene + close | **PASS** | This phase. **(a)** Every new surface CI-asserted with provenance: the four count bars match the observed gates (below), each with a provenance comment block in its workflow. **(b)** A demo page per capability: `BnGeolocationDemo` / `BnNotificationsDemo` / `BnSecureDemo` / `BnCameraDemo`, all sample-only. **(c)** A conclusion doc per phase: 9.0, 9.1, 9.2, 9.3 (four files, listed below) + designs. **(d)** This audit. **No tag** — closure is the audit. |

---

## The four counts — gate ↔ README reconciliation (the 9.2 lesson)

*A count row left behind reddens drift. Every literal below was read from the workflow's DECIDING
line and matched to its README row.*

| Surface | Value | Gate literal (workflow) | README row | Match |
|---|---|---|---|---|
| .NET (xUnit, 3 projects) | **754** | `ci.yml:1442` (`$passed -ne 754`) + `:1424` step name | `README.md:241` | ✅ **re-run live: 597+132+25 = 754/0/0** |
| JVM (Gradle `testDebugUnitTest`) | **119** | `ci.yml:1726` (`$tests -ne 119`) | `README.md:242` | ✅ (gate ↔ README; not re-run here — F1) |
| Android instrumented (AVD) | **209** | `android-instrumented.yml:978` (`"$tests" -ne 209`) | `README.md:243` | ✅ (gate ↔ README; not re-run here — F1) |
| iOS XCTest (simulator) | **233** | `ios.yml:1040` (`"$passed" -ne 233`) | `README.md:244` | ✅ (gate ↔ README; not re-run here — F1) |

**No mismatch found.** The README's four rows and the four gate literals agree, and the one lane the
audit host can run reproduced its literal exactly. The 9.2 failure mode — a count moved in a gate but
not in the README, or vice-versa — is absent.

---

## The ABI grew exactly once — the concrete proof

**This is the milestone's thesis, and it is re-proven, not cited.** The bridge grew EXACTLY ONCE, in
Phase 9.0 (72→80 bytes, 9→10 exports); Phases 9.1/9.2/9.3 each added only an op-enum constant.

**1. The struct is 80 bytes with HostCallBegin at offset 72 — three mirrors agree.**

- **.NET:** `BridgeProtocolNative.cs:80` — `public struct BlazorNativeBridgeCallbacks // 10 × IntPtr
  = 80 bytes`, with `HostCallBegin` at `:97` (`// offset 72`).
- **Swift C header:** `BlazorNativeRuntimeC.h:145` — `} bn_bridge_callbacks; // 80 bytes`, with
  `bn_host_call_begin_cb hostCallBegin; // offset 72` at `:144`.
- **Pin test:** `BridgeProtocolNativeTests.cs:30` — `Assert.Equal(80, Marshal.SizeOf<…>())`, plus the
  per-slot `OffsetOf` ladder ending at HostCallBegin@72. In the live 754.

**2. Exactly 10 `[UnmanagedCallersOnly]` exports incl. `host_call_complete`.**

`grep -c UnmanagedCallersOnly src/BlazorNative.Runtime/Exports.cs` → **10**. The entry points:
`init, shutdown, version, register_frame_callback, mount, dispatch_event, register_bridge,
fetch_complete, host_event, host_call_complete`. The tenth (`blazornative_host_call_complete`,
`Exports.cs:450`) is the 9.0 addition — the `fetch_complete` twin. The three CI publish gates
(win-x64 `ci.yml:1473-1485`, bionic ×2, iOS `ios.yml:161-172` + `nm -gU`) each assert **exactly these
ten** symbols — no eleventh.

**3. The drift/pin tests assert 80 / offset-72 / 10 — falsifiably.** Each M9 phase shipped its own
reuse-proof suite, deliberately duplicating the abstract pin so the "adds zero ABI" claim sits next
to its feature:

- `NotificationsAbiUnchangedTests` — 80 / offset-72 / op==1; the "assert 81 bytes" mutation reds
  (`Actual: 80`), the iOS struct-grow mutant **failed to COMPILE** (`missing argument 'mutSlot11'`).
- `SecureBiometricsAbiUnchangedTests` — 80 / offset-72 / ops 2+3; iOS mutant compile-fail
  (`mutantEleventhSlot`, run 29636616995).
- `CameraAbiUnchangedTests` (read in full) — 80 / offset-72 / `Camera==4`; iOS mutant compile-fail
  (`cameraExtra`, run 29640255862). *The type system forbids a silent grow — the sharpest form of the
  proof, because a multi-MB image is exactly what "obviously" needs a new export, and it did not (the
  bytes cross as a file PATH).*

**4. The HostCallOp enum has exactly the 5 ops.** `NativeShellBridge.cs:87` —
`internal enum HostCallOp { Geolocation = 0, Notifications = 1, Biometrics = 2, SecureStorage = 3,
Camera = 4 }`. Five values, one per capability, carried on the existing `int op` field — wire
vocabulary, not a struct grow. `CameraAbiUnchangedTests` asserts all five values by ordinal.

**Conclusion:** the ABI is at **80 bytes / 10 exports** and has been since Phase 9.0. Four
capabilities landed on it. *Pay once, reuse thrice — proven four times, the last against a multi-MB
result.*

---

## The milestone ledger

**Consumed (retired here):**
- **M5 secure storage** — the four-milestone-old deferral, CLOSED by Phase 9.2 (DoD #4). OS-key-level
  binding: Android PROVES it on the AVD software keystore; iOS asserts the contract with OS
  enforcement UNPROVEN (no simulator Secure Enclave). Marked closed at MILESTONE.md:146.

**Discharged (resolved here):**
- **M6/M7 "revisit ContentMode with a real natural-size image"** — DISCHARGED by Phase 9.3: the
  captured photo is a valid `BnImage.Src` in a definite 240×320 box with `ContentMode="Contain"`,
  EXIF normalized on both shells. MILESTONE.md:118.
- **SafeAreaView / edge-to-edge** — flagged three phases as camera's likely trigger; RESOLVED **NOT
  tripped** (the capture UI is system chrome, not app-laid-out). The three-phase watch closes with a
  reason. MILESTONE.md:153.

**Carried forward (triggers named, unchanged):**
- **Real-device iOS / TestFlight** → trigger: the owner acquires an **Apple Developer account**
  (CI-signed IPA + provisioning + App Store Connect upload).
- **FCM push** → trigger: a **Firebase project** (owner-owned external dependency) + a server-side
  story; local notifications landed first.
- **The on-device inspector channel** → ledgered a **third** time; trigger unchanged (developer
  tooling, no user-facing pull).

---

## The honest UNPROVEN surfaces

**Stated plainly, because the emulator honesty is the sharpest part of M9.** These are proven on CI
mocks/seams and named UNPROVEN on real hardware, per each phase's own conclusion:

- **The camera sensor + real capture UI + real EXIF** — the owner's physical Android phone. Android's
  shutter is not CI-drivable (seam-driven result, but written THROUGH the real FileProvider
  `content://` URI); the **iOS simulator has NO camera at all** (`check → Unavailable` is the correct
  sim result), so a real iOS capture is DOUBLY UNPROVEN (no sim camera AND no Apple account).
- **Biometrics on the physical phone** — the real fingerprint sensor + TEE-enforced auth-bound
  decrypt + real lockout. THE least emulator-like capability; the AVD proves the software-keystore
  contract, the sensor itself is UNPROVEN.
- **iOS Secure Enclave OS-enforced binding** — the simulator no-ops `.biometryCurrentSet`; the
  OS-enforced refusal is UNPROVEN-until-real-device (doubly gated: no Enclave AND no Apple account).

None of these block the DoD: each criterion asked for the capability + permission story + both shells
+ CI/emulator device tests, and every one of those is present. The real-hardware proofs are the
owner's honesty check, explicitly out of CI by design.

---

## Findings (recorded, none blocking)

- **F1 — three lanes are verified by gate↔README reconciliation, not re-run in this audit.** The
  audit host is Windows with no live AVD and no Mac, so JVM (119) / Android instrumented (209) / iOS
  (233) are checked against their gate literals + README rows + the per-phase run-id provenance in
  ROADMAP (e.g. iOS run 29639956941 for 9.3), not executed live. The `ci.yml`/`android-instrumented.yml`/`ios.yml`
  gates enforce them mechanically on the merge. The .NET 754 WAS re-run live. This is the M8 audit's
  device-lane posture, unchanged.
- **F2 — the brief's "ci.yml for instrumented" is imprecise; the literal lives in
  `android-instrumented.yml`.** The instrumented 209 is asserted at `android-instrumented.yml:978`
  (an advisory nightly/dispatch lane), and README row 243 correctly points there — not at `ci.yml`.
  No count is unpinned; the reconciliation holds. Named so the next reader is not sent to the wrong
  file.
- **F3 — carried residuals, unchanged:** `NUGET_API_KEY` is not set; nothing is published; release
  PRs #115 (0.1.0) and #116 (0.2.0) merged but **no package has been published** (the manual pipeline
  has not run); the device lanes stay advisory; and **no tag is created by this phase** — closure is
  this audit.

---

## The conclusion docs, listed (DoD #6c)

- Phase 9.0: [design](2026-07-17-phase-9.0-design.md) + [conclusion](2026-07-17-phase-9.0-conclusion.md)
- Phase 9.1: [design](2026-07-17-phase-9.1-design.md) + [conclusion](2026-07-17-phase-9.1-conclusion.md)
- Phase 9.2: [design](2026-07-17-phase-9.2-design.md) + [conclusion](2026-07-18-phase-9.2-conclusion.md)
- Phase 9.3: [design](2026-07-18-phase-9.3-design.md) + [conclusion](2026-07-18-phase-9.3-conclusion.md)
- Phase 9.4: this audit.

---

## What M9 actually delivered

**The milestone where the bridge-extension pattern earned its name.** Four real native capabilities —
geolocation, local notifications, biometrics + secure storage, camera photo capture — each on both
shells, each with the permission model the bridge had never carried, through an ABI that **grew
exactly once and then held**:

- **The permission machinery, proven on the simplest gated API first** (geolocation, 9.0) — denial is
  a status integer, never an exception, never a hang, tested on both shells within a bounded await.
  The OS-suspends-the-app risk is proven (Android Activity recreation, iOS async CLLocationManager),
  both routing to the same in-flight continuation.
- **The ABI grew 72→80 bytes / 9→10 exports ONCE** (9.0, the HostCallBegin slot + host_call_complete),
  shaped generically — and 9.1/9.2/9.3 each added only an op constant. The reuse is falsifiable: three
  `*AbiUnchangedTests` suites, and every iOS struct-grow mutant **failed to compile**.
- **The M5 secure-storage deferral retired**, the **M6/M7 natural-size ledger discharged**, and
  **SafeAreaView resolved without being tripped** — three inherited items closed with reasons.
- **Test surface grew** 583 → **754** .NET (re-run live); JVM 106 → **119**, instrumented 184 → **209**,
  iOS 154 → **233** — every increment carrying a provenance block in its gate.

The 7th package `BlazorNative.Device` now holds **five façades** (IGeolocation, INotifications,
IBiometrics, ISecureStorage, ICamera), no 8th package added. The honest UNPROVEN surfaces — the camera
sensor, biometrics on the physical phone, the iOS Secure Enclave — are named as the owner's real-device
checks, deliberately outside CI.

---

## Recommendation

### Is M9 complete?

**Yes.** All six DoD criteria PASS on evidence verified at the audited tip. The ABI is provably unmoved
since Phase 9.0 (80 bytes / 10 exports / 5 ops, three mirrors + three falsifiable pin suites), the
four count bars reconcile gate ↔ README (with .NET re-run live at 754), the decision log is complete
including this audit, and the milestone ledger is settled item by item — one consumed (M5), two
discharged (M6/M7 natural-size, SafeAreaView), three carried forward with named triggers.

### Closure, restated mechanically

This PR's five required gates green (`build-test`, `android-build`, `ios-build`, `pr-title`,
`footer-check` — branch protection enforces it) → merge → **M9 is closed on this audit**. **No tag** —
the milestone-tag namespace was retired in Phase 8.6, and `vN.0` will never be cut again. Nothing is
published; no `NUGET_API_KEY` is added by this phase.

### The owner's standing actions (unchanged from M8)

1. **Real-device Android** — the camera sensor, biometrics, and `DeniedPermanently`-across-restarts
   are the honesty checks CI cannot drive. USB debugging + `adb`, per each phase's documented steps.
2. **Apple Developer account** — the single trigger that opens real-device iOS / TestFlight and
   discharges the iOS Secure Enclave + real-capture UNPROVENs.
3. **A Firebase project** — the trigger for FCM push (local notifications shipped first).
4. **`NUGET_API_KEY` only when publishing** — nothing published; no tag created; no secret added here.
