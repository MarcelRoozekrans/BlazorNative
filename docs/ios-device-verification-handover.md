# Handover — iOS real-device verification (P3)

**Mission:** get BlazorNative's iOS shell running, signed, on a **real iPhone**, and
verify it there against the seven acceptance criteria below. This is the **last blocker
for 1.0** — every other 1.0 criterion is met; the project has deliberately refused to
ship "1.0" with iOS proven only in the simulator.

> **Read this first, then read [`website/docs/shells/ios.md`](../website/docs/shells/ios.md)
> and [`src/BlazorNative.Apple/project.yml`](../src/BlazorNative.Apple/project.yml).**
> `ios.md` is the authoritative build guide (heavily commented); `project.yml` is the
> XcodeGen spec. This document is the *device* delta on top of them plus the checklist.

---

## Who you are and what you're doing

You are an agent (or agents) working for an external iOS developer who has an **Apple
Developer account** and a **physical iPhone** — the two things this project has never
had. The project's own CI proves the iOS shell **compiles and its XCTests pass on the
simulator**; it has never signed, provisioned, deployed, or run on hardware. Your job is
to close that gap and then exercise the real-hardware behaviours a simulator cannot fake.

There are **two phases**, and phase A is real engineering, not "open the project and hit
Run":

- **Phase A — make a signed device build.** The reference shell targets
  `iossimulator-arm64` with `CODE_SIGNING_ALLOWED: NO` and no team. A device needs a
  device runtime (`ios-arm64`), a device-arch Yoga, code signing, provisioning, and a
  team-prefixed keychain group. `ios.md` says this outright: *"Real devices need a
  signing story, a device RID and an `ios-build` lane that has none of those."*
- **Phase B — verify on the device.** Run the demo app on the iPhone and work the
  checklist. The point is the things the simulator **cannot** prove.

---

## Context in one screen

**What BlazorNative is.** Blazor components compiled with **NativeAOT** into a
platform-native static library, rendered as **real UIKit views** — no WebView, no JS, no
wasm. A headless .NET renderer emits typed struct patches across a 10-export C-ABI; the
Swift/UIKit shell reads them and builds two mirrored trees: real `UILabel`/`UIButton`/…
views, and a **Yoga** node tree beside them that computes every frame. Layout style names
go to Yoga, visual names to the view.

**The iOS shell** lives in [`src/BlazorNative.Apple/BnHost/`](../src/BlazorNative.Apple/BnHost/):

- `AppDelegate.swift` / `HostViewController.swift` — a **runnable app**; it boots `BnDemo`
  (or a deep-link launch component) on a background thread. This is what you'll run on the
  device and tap through.
- `BnRuntime.swift` / `AppleShellBridge.swift` — the C-ABI seam and the host bridge.
- Capability bridges: `BnGeolocation`, `BnNotifications`, `BnBiometrics`,
  `BnSecureStorage`, `BnCamera` — each an OS integration behind a permission-gated async
  host call (`blazornative_host_call_complete`).
- `BnYogaLayout.mm` / `BnYogaProbe.mm` — Yoga reached through Objective-C++ behind a
  plain-C surface (Yoga's C++ headers can never be visible to Swift), linked against a
  source-built `libyoga.a`.
- `BnStderrPump.swift` — routes the runtime's `Console.Error` (fd 2) into the unified log.

**The demo app** (`BnDemo` and its pages) is the thing to interact with. It has pages for
each capability — that's your test surface on the device.

---

## Phase A — build, sign, deploy

Follow `ios.md` for the exact publish/link mechanics; here is the **device-specific delta**
and the order to do it in. Treat each step as "make it work, then write down exactly what
you did" — the maintainer will fold your working recipe back into the repo as the
`ios-build`-on-device lane that does not exist yet.

### A0. Environment

- macOS with **Xcode** (current), a physical **iPhone**, and an **Apple Developer** team.
- **.NET 10 SDK** (see `global.json`) and **XcodeGen** (`brew install xcodegen`).
- Clone the repo; the sample app is `samples/BlazorNative.SampleApp` (the publish head).

### A1. Publish the runtime for the **device** RID

CI publishes `iossimulator-arm64`. You need **`ios-arm64`** (real-device arch):

```sh
dotnet publish samples/BlazorNative.SampleApp -c Release -r ios-arm64
```

- `PublishAot` + `NativeLib=Static` live in the csproj, **not** on the CLI. The output is
  a **static archive** (`.a`), not an app.
- The bionic/NativeAOT plumbing already has an `ios-` branch (`project.yml` and the build
  targets special-case `RuntimeIdentifier.StartsWith('ios-')` alongside `iossimulator`),
  so this may mostly work — **but the `ios-arm64` NativeAOT runtime pack is a different
  package than the simulator one**, and this path has never been exercised. Expect to
  chase: a missing/blocked `Microsoft.NETCore.App.Runtime.NativeAOT.ios-arm64` pack, the
  IL2072 trim-warning count (CI asserts exactly 4 for the simulator — confirm/annotate the
  device number), and the linker's device-vs-simulator target triple.
- **Verify the archive is device-arch, not simulator** (`lipo -info` / `nm`): a simulator
  slice on a device is a silent, confusing failure.

### A2. Build **`libyoga.a` for the device SDK**

The shell links a source-built Yoga (pinned **3.2.1** — do not change the version; a drift
test holds it). The simulator build compiles Yoga against the **simulator** SDK. Rebuild it
against the **device** SDK (`iphoneos`, arm64), merged with `libtool -static`, exactly as
`ios.md` describes for the simulator but with the device SDK/arch.

### A3. Signing, provisioning, entitlements

This is the part the simulator skips entirely.

- In `project.yml`, set **`DEVELOPMENT_TEAM`**, flip **`CODE_SIGNING_ALLOWED: YES`** /
  **`CODE_SIGNING_REQUIRED: YES`**, and provide a provisioning profile (automatic signing
  is fine to start). The current values are `NO`/`NO` with no team — deliberately, for the
  unsigned simulator lane. **Do not commit your team id / profile**; keep it in a local
  override or your own fork.
- **Entitlements — the keychain group must gain the team prefix.**
  [`BnHost.entitlements`](../src/BlazorNative.Apple/BnHost/BnHost.entitlements) declares a
  **bare** `keychain-access-groups` of `io.blazornative.bnhost` — deliberately, because the
  simulator honours an unsigned/simulated entitlement. On a **real device** a keychain
  group must be **`$(AppIdentifierPrefix)io.blazornative.bnhost`** (the team prefix), or
  every `SecItem*` call fails with `errSecMissingEntitlement (-34018)` and the secure store
  reports `Error`. This is the first thing that will break on device; fix it here.

  :::danger `$(AppIdentifierPrefix)` is a LITERAL. Do not substitute your Team ID.
  Type those 23 characters exactly as written, `$(` and `)` included, into the
  entitlements file. It is an Xcode **build variable**, expanded at build time from the
  app identifier prefix in your provisioning profile — and that prefix is **not always
  equal to your Team ID**. They coincide for most modern teams and differ for others,
  most commonly where an App ID predates the team it now lives under.

  Hand-substituting is therefore a coin flip, and when it loses, the symptom is
  indistinguishable from a code bug: **every** `SecItem*` call returns
  **`errSecMissingEntitlement (-34018)`**, the secure-storage page reports `Error` on
  every operation, and nothing anywhere names the entitlement. The September 2026 device
  run lost about an hour to this, debugging `BnSecureStorage.swift` — the wrong layer
  entirely.

  **Confirm the expansion rather than guessing it.** After a build, read the entitlements
  actually embedded in the signed app:

  ```bash
  codesign -d --entitlements :- /path/to/BnHost.app
  ```

  The `keychain-access-groups` array there must read
  `<PREFIX>.io.blazornative.bnhost` with a real prefix. If it still shows the literal
  `$(AppIdentifierPrefix)`, the variable did not expand — that is a project/profile
  problem, not something to fix by typing a value in.
  :::
- Bundle id / app id must match your provisioning profile.

### A4. Materialise the Xcode project and deploy

- `xcodegen generate` (from `src/BlazorNative.Apple/`) produces `BnHost.xcodeproj` from
  `project.yml`. Point the app target at the **device** static archive from A1 and the
  **device** `libyoga.a` from A2.
- Select your iPhone as the destination and **build + run the app target** (not just the
  test bundle). The app should boot and mount `BnDemo`.
- If you want the XCTests on-device too, run the test scheme against the device — but the
  primary deliverable is the **interactive app**, since the checklist is about real
  hardware and real permission dialogs.

**Phase A is done when the demo app launches on the iPhone and you can navigate its pages.**

---

## Phase B — the verification checklist

Work each item on the device. For every one, capture **evidence** (screen recording or
screenshots + the relevant unified-log lines) and a **PASS / FAIL / caveat** note. The
seven acceptance items come straight from the 1.0 criteria doc
([`docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md`](plans/2026-07-22-phase-11.3-one-point-oh-criteria.md));
the extra checks below them are the specific traps this project already knows about.

### The seven P3 acceptance items

1. **Camera capture from a real sensor.** Open the camera page, capture a photo. Check: the
   real capture UI appears, permission is requested, a captured image round-trips back into
   the app (a `BnImage`), and denial/cancel returns a *status* (not a hang or crash).
2. **Face ID / Touch ID against the Secure Enclave.** Trigger a biometric-gated action.
   Check: the **real** Face ID / Touch ID prompt appears; success returns a value; a
   cancel and a failed match each come back as a **status** (`AuthFailed`), never a hang.
   *(The simulator has no Secure Enclave — this was the first thing that had never truly
   run. It PASSED on hardware in September 2026. The sharp edge it exposed, the
   auth-bound-keychain mismatch, was fixed in Phase 14.3 — see the superseded ACL section
   below and `website/docs/migrating/auth-biometry.md`.)*
3. **Real-GPS geolocation.** Open the geolocation page. Check: the OS location-permission
   dialog appears; a real fix returns with plausible coordinates and accuracy; denial
   returns a *status*, not an exception or a hang. **Redact the coordinates** from anything
   you post publicly (see Reporting).
4. **APNs (push notifications).** Check notification permission + scheduling/showing a
   local notification, and — if wired — a remote push via APNs. Note honestly which half is
   exercised; local notifications are in scope, real APNs may need additional server setup.
5. **Universal links.** Open a universal link that targets the app. Check: the OS routes it
   into the app and the launch route **seeds the initial mount** (HostViewController
   resolves a deep-link launch component instead of the default `BnDemo`).
6. **Code-signing and provisioning.** Proven by the app running at all — but record the
   signing identity/profile used and any capability that required a specific entitlement.
7. **Thermal / background behaviour.** Background the app and return; leave it under load.
   Check: it resumes cleanly (the host session is idempotent; register/mount are last-wins),
   no crash on resume, no runaway CPU. Note any thermal or backgrounding surprises.

### The specific correctness bug to confirm — #213 item 1 (secure-storage ACL)

:::danger SUPERSEDED — this section describes behaviour that no longer exists
**Everything from here to the end of this subsection is a record of what was checked in
September 2026, kept for its history. Do not follow its instructions on a new device run.**

The mismatch it describes was **real, was confirmed on hardware, and was fixed in Phase
14.3**. Three things below are now false:

- **"Get, and choose passcode fallback at the prompt" cannot be done.** There is no
  passcode option at the prompt any more. The authorized read evaluates
  `.deviceOwnerAuthenticationWithBiometrics`, and LocalAuthentication offers no passcode
  fallback for that policy. A tester following that step will look for a control that was
  deleted.
- **The direction of the bug was the opposite of the prediction below.** The prediction
  was a spurious `AuthFailed` after a passcode unlock. What iOS 26 actually did was
  **permit** the read: a biometry-bound secret came back after a passcode entry. The gate
  was *weaker* than the stored access control declared, not stricter.
- **`requireAuth: true` now means biometry on BOTH shells** — Face ID / Touch ID on Apple,
  a Class 3 biometric on Android — and `AuthSemanticsDriftTests` holds the two shells to
  that one sentence.

**Read [`website/docs/migrating/auth-biometry.md`](../website/docs/migrating/auth-biometry.md)
before testing anything in this area.** What is still worth doing on a device is the
*read-side contract* — a plain get of an auth-bound item is refused as `AuthFailed` with no
value leaked, a cancel is `AuthFailed`, and neither hangs — plus the no-enrolled-biometric
checkbox that follows this subsection, which 14.3 did **not** answer.
:::

**This was the highest-value single check, because the simulator provably cannot catch it.**

- **The mechanism.** `BnSecureStorage` stores an auth-bound item with a `SecAccessControl`
  created **`.biometryCurrentSet`** (biometry-only, Secure-Enclave-bound). But
  `secureGetWithAuth` evaluates the gate with **`.deviceOwnerAuthentication`**, which
  **permits passcode fallback**. A passcode-only unlock does **not** satisfy a
  biometry-bound ACL, so on a real device `SecItemCopyMatching` can return **`AuthFailed`
  even after the user "authenticated" by passcode.**
- **Why the simulator hides it.** The simulator has no Secure Enclave and **does not
  enforce a `SecAccessControl`** — `.biometryCurrentSet` is a documented no-op there, so
  the shell's own contract logic runs and the mismatch never surfaces. `BnSecureStorage.swift`
  says this in its header.
- **What to do on device.** Set an auth-bound secret, then:
  - **Get with Face ID / Touch ID** → should succeed and return the value.
  - **Get, and choose passcode fallback at the prompt** → observe whether it spuriously
    returns `AuthFailed` while the user believes they authenticated. That is the bug.
  - Also confirm the **read-side contract**: a *plain* get of an auth-bound item is refused
    as `AuthFailed` (no value leaks), and denial is `AuthFailed` with no hang.
- **Expected outcome:** either confirm the bug reproduces (then the fix is to align the get
  evaluation with the stored ACL — biometry-bound get, or store with a policy that matches
  the intended fallback), or prove it doesn't and record why. File findings on
  [#213](https://github.com/MarcelRoozekrans/BlazorNative/issues/213).

- [ ] **Secure storage on a device with NO enrolled biometric.** Disable Face ID
      enrolment, then `Set` with `requireAuth: true` on the `/secure` page. Record
      whether the status is `Unavailable` — meaning the ACL creation refused, matching
      Android — or `Ok`, meaning the item was stored and only a later read will fail.
      `BnSecureStorage.swift` documents this as unestablished; this answers it.
      Apple's own reference does not state which call enforces the enrolment
      requirement, so **either result is a valid finding — record what you see, do not
      assume.** For what it is worth, the published Security sources build the access
      control as a pure policy dictionary with no enrolment query, which points at `Ok`;
      that is a prediction to test, not a fact to confirm.

### Q1 — is a Release build actually quiet on the device?

Build/run **Release**. The default log level is `Warn` (a runtime default, not `#if DEBUG`).
Watch the unified log (`Console.app` with the device, or `log stream --predicate
'subsystem CONTAINS "blazornative"'`). Check: on the happy path (mount, tap a button,
navigate) the app is **quiet** — errors/warnings only, no per-frame or per-event chatter.
Then set the level up (`Debug`/`Verbose` via the launch mechanism) and confirm the trace
appears (dispatch/mount/nav/bridge at Debug, per-frame at Verbose — added in #201). A
paired observation (silent at Warn, chatty at Verbose) is what closes Q1.

### Frame parity — device vs simulator/Android (a free, high-signal check)

The whole design claim is **identical frames everywhere**. The simulator XCTests assert
exact frame tables that match the Android instrumented lane. On the device, the layout demo
pages (`/layout`, `/scroll`, `/image`) should **look and place identically** to the
simulator. A visible layout difference on device is a real bug (a device-SDK Yoga build
difference, a measurement path, a scale/point-vs-pixel error) — capture it.

---

## Landmines (where the simulator lies, and other traps)

- **The Secure Enclave is absent in the simulator** — biometrics and the auth-bound
  keychain ACL are exactly the surfaces that have never truly run. Treat any
  biometrics/secure-storage "it worked in CI" as **unproven** until the device confirms it.
- **`errSecMissingEntitlement (-34018)`** on any secure-storage call → the keychain group is
  missing its team prefix (A3).
- **fd 2 is process-global and one-way.** If you add Crashlytics/Sentry NDK handlers, last
  writer of stderr wins; the `BnStderrPump` install order decides whose output survives.
- **The loser of that fight is usually YOUR CONSOLE.** The consequence the line above
  leaves out is the one that costs a device run time: `BnStderrPump` claims fd 2 with
  `dup2` inside `HostViewController.viewDidLoad`, immediately after the XCTest guard
  (`viewDidLoad:120`, guard at `:107` — not the first statement in the method), so
  anything the OS mirrors onto fd 2 lands in the pump's pipe instead of your terminal.
  On a real device
  that mirror is the **only** remaining route to `Debug` and `Verbose` — both map to
  `OSLogType.debug`, the unified log drops that unless the subsystem is enabled for
  capture, and `log config` has no `--device` flag. The escape is the environment variable
  **`OS_ACTIVITY_DT_MODE`**: when it is set, the pump now **stands aside** and returns
  without touching fd 2. The full recipe, including what you give up by doing it, is
  §8 of [`website/docs/shells/ios.md`](../website/docs/shells/ios.md) — do not re-derive
  it here.
- **Simulator vs device slices.** A simulator-arch static lib linked into a device app is a
  silent failure — verify arch with `lipo`/`nm` (A1).
- **Yoga version is pinned (3.2.1) and drift-tested.** Rebuild it for the device SDK; do
  **not** bump the version.
- **Don't commit signing secrets** (team id, profiles, provisioning) — keep them local.

---

## Reporting your findings

- **Per item:** PASS / FAIL / caveat, with evidence (recording or screenshots + log lines).
- **Where:** P3 overall is [#17](https://github.com/MarcelRoozekrans/BlazorNative/issues/17);
  the secure-storage ACL is [#213](https://github.com/MarcelRoozekrans/BlazorNative/issues/213).
  The device-build **lane** now exists — Phase 14.4 added an `ios-arm64` leg to both
  `ci.yml`'s `ios-build-slice` matrix and `ios.yml` — so a Phase A recipe no longer needs a
  PR to create one. What it should still produce is the **signing** half, which CI cannot
  hold: the team, profile and entitlement steps that turn a compiling slice into a running
  signed app. File one issue per
  genuine device bug you find, with a minimal repro.
- **Privacy — redact before anything public:** the geolocation page shows **real
  coordinates**; biometrics involve a real face/fingerprint; secure storage holds real
  secrets. Blur/crop coordinates, don't post biometric captures, and don't paste secret
  values into issues, PRs, logs, or commits.
- **The P3 hardening ledger** (#8, #9, #12, #13) is *accepted debt* — you don't have to fix
  it, but if a real device **triggers** one of those (dispatch-lane starvation, an
  exception-capture window, etc.), that's a notable finding worth recording on the issue.

---

## Definition of done

P3 can be called **met** when:

1. The demo app **runs, signed, on a real iPhone** (Phase A). The device-build lane
   already exists — Phase 14.4 added an `ios-arm64` leg to both `ci.yml`'s
   `ios-build-slice` matrix and `ios.yml` — so what remains is the device-execution
   item: someone actually runs the signed app on hardware and records the signing
   recipe (team, profile, entitlements) that CI cannot hold.
2. All **seven acceptance items** have been exercised with evidence and a PASS/caveat note.
3. The **secure-storage ACL** (#213 item 1) is confirmed-and-filed or proven-not-a-problem.
   **Met, and closed: confirmed on hardware in September 2026 and fixed in Phase 14.3.**
   What remains open in this area is only the no-enrolled-biometric question, the
   checkbox under the superseded ACL section.
4. **Q1** has a paired observation (Release quiet at `Warn`, trace at `Verbose`).
5. **Frame parity** on the demo layout pages is confirmed (or a difference is filed).

At that point the last 1.0 blocker is cleared and the maintainer can cut 1.0.

---

## Key files

| Path | What it is |
|---|---|
| `website/docs/shells/ios.md` | **The authoritative iOS build guide** — read it fully |
| `src/BlazorNative.Apple/project.yml` | XcodeGen spec: targets, signing knobs, link flags, SwiftPM deps |
| `src/BlazorNative.Apple/BnHost/` | the Swift/UIKit shell (app + capability bridges + Obj-C++ Yoga) |
| `src/BlazorNative.Apple/BnHost/BnHost.entitlements` | keychain group — needs the team prefix on device (A3) |
| `src/BlazorNative.Apple/BnHost/BnSecureStorage.swift` | the ACL bug's home; its header explains the simulator no-op |
| `samples/BlazorNative.SampleApp` | the publish head — what you publish for `ios-arm64` |
| `.github/workflows/ios.yml` | the iOS execution lane. Since Phase 14.4 a two-leg matrix: the simulator leg compiles and runs the XCTests, the device leg compiles `ios-arm64` and runs nothing |
| `.github/workflows/ci.yml` → `ios-build-slice` | the REQUIRED compile gate — both slices, every PR. `ios-build` aggregates it |
| `docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md` | the 1.0 criteria; P3 and the seven items |
