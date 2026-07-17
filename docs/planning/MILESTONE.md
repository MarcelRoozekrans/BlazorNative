# Milestone 9 — Host APIs (Platform Breadth)

**Status:** in progress — opened 2026-07-17
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
permission-gated API (geolocation)** before anything heavier is built.

## Definition of Done

1. **The permission pattern, proven and documented.** A versioned extension of
   [bridge-extension.md](../bridge-extension.md): how a permission-gated call flows
   (request → OS prompt → grant/deny → completion callback), how denial reaches .NET as
   data, what re-request looks like, on both shells — written as the pattern the
   remaining phases copy, with the 5.4 worked-example discipline.
2. **Geolocation** (`BlazorNative.Device` or per-design naming): current position on
   both shells, permission story per DoD #1, emulator/simulator-mockable, device tests.
3. **Local notifications**: schedule / show / cancel + tap-through (the app opens to a
   route — the deep-link machinery from 5.1 is the landing path), permission story
   (POST_NOTIFICATIONS on Android 13+, UNUserNotificationCenter on iOS), device tests.
4. **Biometrics + secure storage**: BiometricPrompt / LocalAuthentication gating a
   Keystore / Keychain-backed store (set/get/delete secrets); the M5 secure-storage
   deferral closes; the emulator's fake-biometric path is CI's lane and the owner's
   phone is the real proof.
5. **Camera (photo capture)**: capture a photo via the native capture UI, hand the
   image across the wire (the design decides the handoff — file path vs bytes — and
   records why; density/`ContentMode` interplay per the M7 image work), permission
   story, emulator's fake feed in CI + the owner's phone as the real proof.
6. **Hygiene + close:** every new surface CI-asserted (counts + gates with provenance);
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

- **From M5:** secure storage (consumed by DoD #4); FCM push (carried, trigger above).
- **From M8:** the KDoc sweep + map extraction — **trigger: before the first Release
  that publishes the template pack** (may fire mid-M9 if the owner publishes; the
  release PR #115 is open); `BionicNativeAot.targets` → the Runtime package's `build/`;
  SafeAreaView/edge-to-edge (watch: the camera capture UI and notification tap-through
  are the likeliest phases to force it — if one does, it lands there with its named
  problem); density assets (trigger: the first bundled-asset story).
- **CI posture:** five required contexts (`build-test`, `android-build`, `ios-build`,
  `pr-title`, `footer-check`); advisory device lanes unchanged; the owner's phone is
  never a CI dependency.

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open:

- **Phase 9.0** — the permission pattern + geolocation (DoD #1, #2) — *the named risk,
  proven on the simplest permission-gated API before anything heavier*
- **Phase 9.1** — local notifications + tap-through (DoD #3)
- **Phase 9.2** — biometrics + secure storage (DoD #4)
- **Phase 9.3** — camera photo capture (DoD #5) — *last deliberately: heaviest, and it
  inherits mature permission machinery*
- **Phase 9.4** — hygiene + M9 final audit + close (DoD #6)

## Why this milestone exists

M1–M8 built a rendering engine, an authoring story, and an ecosystem — a stranger can
`dotnet new` an app that draws. It still can't do anything a *mobile* app exists to do:
no sensors, no camera, no secrets, no notifications. M9 is where the bridge-extension
pattern earns its name — four real capabilities through the same frozen ABI, with the
permission model (the thing clipboard never needed) proven once and reused three times.
