# Milestone 9 — Host APIs (Platform Breadth)

**Status:** ✅ **complete — 2026-07-18**; **6/6 DoD closed** — Phase 9.4 shipped the hygiene +
[final audit](../plans/2026-07-18-milestone-9-final-audit.md) (all six DoD PASS; the ABI grew
exactly once in 9.0 and held for three more capabilities; no tag — the 8.6 rule, closure is the audit)
**Source:** BACKLOG "P4 — full platform coverage" (remainder). The roadmap called this
milestone "Platform Breadth + Real Device"; the second half is **deferred at
milestone-open** (below), so the name now says what the milestone actually is.
**Predecessor:** Milestone 8 — complete 2026-07-17
([final audit](../plans/2026-07-17-milestone-8-final-audit.md) all 6 DoD PASS +
[addendum](../plans/2026-07-17-milestone-8-audit-addendum.md); no tag — the
milestone-tag namespace was retired in 8.6, and milestones close on their audits).

## Goal

After M8 a stranger can consume the library, but an app built on it can only *render*:
no sensor, no capture, no secure secret, no notification — the bridge has grown exactly
once (clipboard/share, 5.4). After M9 an app can **ask where it is, take a photo, prove
who's holding the phone, keep a secret, and schedule a local notification** — each one a
real native capability behind the same C-ABI discipline, on both shells, permission
story included. The proof surface is the sample app plus **the owner's physical Android
phone** for the capabilities an emulator only pretends to have.

**The ABI, frozen since M1, grew once in Phase 9.0 — deliberately, argued, and
generically.** An honest async-permission completion could ride no existing export
(`fetch_complete` is fetch-typed; `host_event` is contractually synchronous), so the
bridge grew 72→80 bytes (+1 `HostCallBegin` slot, the `FetchBegin` twin) and 9→10
exports (+`blazornative_host_call_complete`, the `fetch_complete` twin). Shaped generically
(op-enum + flat-JSON) so 9.1/9.2/9.3 add an op constant with ZERO further struct/export/
gate/drift change — pay once, reuse thrice. This is the last ABI grow M9 plans.

## Scoping decisions (owner, 2026-07-17)

1. **Real-device iOS is DEFERRED — no Apple Developer account for now.** With no local
   Mac, the honest path to a physical iPhone is CI-signed IPA → TestFlight, which
   requires the account. The trigger is named: *the owner acquires the account* → a
   TestFlight phase opens (signing + provisioning in CI + upload via the App Store
   Connect API; ~2 new secrets). Until then iOS stays **simulator-scoped and labeled as
   such**, exactly as M5–M8 shipped it.
2. **All four host-API groups ship**: geolocation · camera (photo capture) · local
   notifications · biometrics + secure storage (the M5 secure-storage deferral rides
   with biometrics as a natural pair). **FCM push stays ledgered** — it needs a Firebase
   project (owner-owned external dependency, the NUGET_API_KEY shape) and a server-side
   story; local notifications land first.
3. **The owner has a physical Android phone** — each phase ships documented
   device-proof steps (USB debugging, `adb` over USB) for the capabilities the emulator
   simulates (camera feed, biometrics). CI stays on the emulator lanes; the phone is
   the honesty check, not a CI node.
4. **The on-device inspector channel is ledgered again** (4.4 carryover, third
   deferral) — developer tooling with no user-facing pull; trigger unchanged.

## The named risk (spike-shaped, first)

**Permissions are a NEW cross-cutting surface the bridge has never carried.** The one
bridge growth so far (clipboard, 5.4) needed no permission, no user prompt, no app
suspension. Every M9 API needs: a runtime-permission request (Android) / purpose string
+ system prompt (iOS), an async flow where **the OS suspends the app mid-call** to show
its dialog, a denial story (.NET must see "denied" as data, not an exception or a
hang), and a re-request/settings story. If the C-ABI's async callback shape (the 72-byte
bridge's completion path) can't carry "the user said no" cleanly, every later phase
inherits the flaw — so Phase 9.0 proves the permission machinery **on the simplest
permission-gated API (geolocation)** before anything heavier is built. **Proven: the
72-byte completion path could not carry it cleanly, so it grew to 80 bytes / 10 exports,
generically — the shape 9.1–9.3 reuse with no further ABI change.**

## Definition of Done

1. **The permission pattern, proven and documented.** ✅ **Closed by Phase 9.0.** A
   versioned extension of [bridge-extension.md](../bridge-extension.md) —
   section (f), the reusable pattern 9.1/9.2/9.3 copy: how a permission-gated call flows
   (request → OS prompt → grant/deny → completion callback), how denial reaches .NET as
   data, what re-request looks like, on both shells — written as the pattern the
   remaining phases copy, with the 5.4 worked-example discipline. Proven: denial is a
   status integer, never an exception or a hang (tested on both shells within a bounded
   await); the OS-suspends-the-app risk is proven (Android Activity recreation mid-prompt,
   iOS async CLLocationManager, both routing to the same in-flight continuation).
2. **Geolocation** (`BlazorNative.Device`): ✅ **Closed by Phase 9.0.** Current
   position on both shells (Android `LocationManager` + `requestPermissions`, iOS
   `CLLocationManager` when-in-use), permission story per DoD #1, `IGeolocation` in the
   new 7th package over `IMobileBridge.GetCurrentPositionAsync` (DevHostBridge mocks the
   tri-state headless), `/geolocation` demo in SampleApp, device tests on both lanes.
3. **Local notifications**: ✅ **Closed by Phase 9.1.** schedule / show / cancel + both
   tap-through halves (cold via the 5.1 launch deep-link, warm via `onNewIntent` /
   `didReceive` → the reserved `"navigate"` host event → `NavigateToAsync`), the permission
   story (POST_NOTIFICATIONS on Android 13+ with the implicit-grant fast path below API 33,
   `UNUserNotificationCenter` on iOS), `INotifications` in the existing 7th package,
   `/notifications` demo, device tests on both lanes. **The ABI stayed FROZEN — the pay-once
   payoff:** 9.1 added an op (`Notifications = 1`) and touched the ABI at nothing — bridge
   still 80 bytes, exports still 10, no drift-pin moved, proven falsifiable
   (`NotificationsAbiUnchangedTests`; the iOS struct-grow mutant failed to COMPILE).
4. **Biometrics + secure storage**: ✅ **Closed by Phase 9.2.** BiometricPrompt
   (Android, on a FragmentActivity host) / LocalAuthentication (iOS) gating an
   AndroidKeyStore AES/GCM · iOS Keychain store (set/get/delete secrets), `IBiometrics`
   + `ISecureStorage` in the existing 7th package `BlazorNative.Device`, `/secure` demo
   (the 12th routed page, sample-only). **The M5 secure-storage deferral CLOSES here** —
   a four-milestone-old ledger item retired. Owner chose **OS-key-level** binding (the OS
   refuses plaintext without a fresh auth) over app-level: **Android PROVES it** on the
   AVD (the software keystore enforces `setUserAuthenticationRequired` — a plain get of an
   auth-bound secret returns AuthFailed); **iOS asserts the CONTRACT with OS-enforcement
   UNPROVEN** (the simulator has no Secure Enclave and no-ops `.biometryCurrentSet` —
   real-device deferred with the Apple account). **The ABI stayed FROZEN — the pay-once
   payoff, a THIRD time:** 9.2 added two ops (`Biometrics = 2`, `SecureStorage = 3`) and
   touched the ABI at nothing — bridge still 80 bytes, exports still 10, no drift-pin
   moved, proven falsifiable (`SecureBiometricsAbiUnchangedTests`; the iOS struct-grow
   mutant failed to COMPILE). `androidx.biometric:biometric:1.1.0` is the first new gradle
   dep of M9 (repo + template, drift-enforced).
5. **Camera (photo capture)**: ✅ **Closed by Phase 9.3.** `ACTION_IMAGE_CAPTURE` to the
   system camera app (Android, NO runtime CAMERA permission — a FileProvider for the
   output URI, a NEW manifest+resource drift class, template 32→33 files) /
   `UIImagePickerController(.camera)` + `NSCameraUsageDescription` (iOS); `ICamera` the
   5th Device façade in the existing 7th package, `/camera` demo (the 13th routed page,
   sample-only). **The image crosses as a file PATH, not bytes** — the payload NAMES the
   blob on the OPTIONAL `host_call_complete` payload (bytes-inline rejected: multi-MB
   through a non-zeroable string, and not a secret), the bytes stay on disk. **The ABI
   stayed FROZEN — the pay-once payoff, a FOURTH time, despite a multi-MB result:** 9.3
   added one op (`Camera = 4`) + `CameraStatus` and touched the ABI at nothing — bridge
   still 80 bytes, exports still 10, no drift-pin moved, proven falsifiable
   (`CameraAbiUnchangedTests`; the iOS struct-grow mutant failed to COMPILE). **The M6/M7
   "revisit ContentMode with a real natural-size image" ledger item CLOSES here** — the
   captured photo is a valid `BnImage.Src` displayed in a DEFINITE 240×320 box with
   `ContentMode="Contain"` (proven on the AVD end-to-end + iOS via `naturalPixelSize`);
   both shells NORMALIZE EXIF so Coil/Kingfisher never double-rotate. **Emulator honesty,
   the sharpest of M9:** Android's shutter isn't CI-drivable so the result is seam-driven
   but written THROUGH the real FileProvider URI; **the iOS simulator has NO camera at
   all** — `check → Unavailable` is the CORRECT sim result, a real capture DOUBLY UNPROVEN
   (no sim camera AND no Apple account). **UNPROVEN → the owner's physical Android phone:**
   the real camera UI + sensor + EXIF (the milestone's second least-emulated capability,
   with biometrics). **SafeAreaView, flagged three phases as camera's likely trigger, is
   NOT tripped** — the capture UI is system chrome, not app-laid-out.
6. **Hygiene + close:** ✅ **Closed by Phase 9.4** ([final audit](../plans/2026-07-18-milestone-9-final-audit.md) — all 6 DoD PASS, ABI grew exactly once re-proven, counts reconciled gate ↔ README, .NET 754 re-run live). Every new surface CI-asserted (counts + gates with provenance);
   the sample app grows a demo page per capability (the proof surface discipline);
   decision log per phase; final audit. **No milestone tag** — closure is the audit
   (the 8.6 rule). Release-please rides along: these phases land as `feat:` commits,
   so the changelog writes itself and the version walks 0.x as designed.

## Out of scope for this milestone

- **Real-device iOS / TestFlight** — deferred; trigger = the Apple Developer account.
- **FCM push** — ledgered; trigger = a Firebase project + the notifications base landing.
- **The inspector channel** — ledgered (third time); trigger unchanged.
- Video capture, gallery/picker, audio recording — camera is *photo capture* only.
- Background location, geofencing — foreground position only.
- Accessibility, i18n, perf/security hardening — **Milestone 10**.

## Inherited from prior milestones (the ledger M9 consumes or carries)

- **From M5:** secure storage — ✅ **CLOSED by Phase 9.2** (consumed by DoD #4, the
  four-milestone-old deferral retired); FCM push (carried, trigger above).
- **From M8:** the KDoc sweep + map extraction — **trigger: before the first Release
  that publishes the template pack** (may fire mid-M9 if the owner publishes; release
  PRs #115/#116 have merged — 0.1.0 and 0.2.0 tagged — and #117 (0.3.0) is open, but no
  package publishes until `NUGET_API_KEY` is live and the manual pipeline runs);
  `BionicNativeAot.targets` → the Runtime package's `build/`;
  SafeAreaView/edge-to-edge — ✅ **RESOLVED by Phase 9.3, NOT tripped** (the capture UI is
  system chrome, not app-laid-out; the three-phase watch closes with a reason); density
  assets (trigger: the first bundled-asset story).
- **CI posture:** five required contexts (`build-test`, `android-build`, `ios-build`,
  `pr-title`, `footer-check`); advisory device lanes unchanged; the owner's phone is
  never a CI dependency.

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open:

- **Phase 9.0** — the permission pattern + geolocation (DoD #1, #2) — *the named risk,
  proven on the simplest permission-gated API before anything heavier*
- **Phase 9.1** — local notifications + tap-through (DoD #3)
- **Phase 9.2** — biometrics + secure storage (DoD #4)
- ✅ **Phase 9.3** — camera photo capture (DoD #5) — *complete (2026-07-18); heaviest,
  last deliberately, and it inherited mature permission machinery — the ABI stayed frozen
  a FOURTH time despite a multi-MB result*
- **Phase 9.4** — hygiene + M9 final audit + close (DoD #6) — **the only one left; M9 is
  now at 5/6 DoD**

## Why this milestone exists

M1–M8 built a rendering engine, an authoring story, and an ecosystem — a stranger can
`dotnet new` an app that draws. It still can't do anything a *mobile* app exists to do:
no sensors, no camera, no secrets, no notifications. M9 is where the bridge-extension
pattern earns its name — four real capabilities through an ABI that grew exactly once
(9.0, generically) and then holds, with the permission model (the thing clipboard never
needed) proven once and reused three times.
