# Milestone 5 — P4: Full Platform Coverage

**Status:** in progress — opened 2026-07-12
**Source:** maps to BACKLOG.md "P4 — Full platform coverage" (scoped subset — see below)
**Predecessor:** Milestone 4 — complete 2026-07-12, tagged `v4.0` ([final audit](../plans/2026-07-12-milestone-4-final-audit.md): PASS, all 8 DoD criteria)

## Goal

M4 made the project shippable and public. M5 makes it *cross-platform in fact, not in
architecture diagrams*: the same dll-per-platform design running the two-page demo on
the **iOS simulator** in CI, the Android shell handling real app lifecycle
(host-initiated events — the `NativeEvents` fork the Phase 4.2 triage routed here),
and the first cross-platform host APIs proving the bridge-extension pattern.

Scope boundaries decided at milestone-open (2026-07-12):

- **iOS without Mac hardware**: free public-repo GitHub macOS runners (Apple Silicon,
  Xcode preinstalled); **simulator-only** (no signing, no Apple Developer account —
  device + App Store validation deferred until an account exists, recorded honestly);
  **spike-first** (a feasibility RED reshapes the milestone early). The iOS inner loop
  is CI-only — minutes per cycle — so phase designs push all platform-neutral
  verification to the existing fast surfaces (win-x64 / JVM / .NET tests).
- **Android completeness = the host-initiated events cluster only** (lifecycle,
  predictive back, deep links). FCM push and secure-storage hardening stay in BACKLOG
  (M6/M7).
- **Cross-platform APIs = clipboard + share only** — permission-free, both platforms;
  the deliverable is the *documented bridge-extension pattern*, not API breadth.

## Definition of Done

Initial M5 contract drafted at milestone-open. Subject to refinement during the
Phase 5.0 brainstorm — and explicitly subject to the Phase 5.0 spike verdict.

1. **iOS feasibility spike verdict committed.** A time-boxed spike on a free macOS
   runner proves (or refutes) that .NET 10 NativeAOT produces a linkable
   iOS-simulator artifact carrying the eight-export `blazornative_*` C-ABI (symbol
   dump as evidence; device `ios-arm64` probed secondarily). A RED comes with a
   documented fallback decision, and the milestone re-scopes — this criterion passes
   with EITHER verdict; what it demands is the *committed evidence*.
2. **Swift shell boots and renders.** The Kotlin shell's Swift twin (bindings, frame
   adapter, widget mapper over native views) boots the dll on the CI simulator and
   renders BnDemo's widget tree.
3. **Two-page demo parity on the simulator** — the headline: bound input + live echo,
   button events, cascading theme, and Settings ⇄ Back navigation, all on the iOS
   simulator, mirroring the Android v3.0 bar.
4. **iOS CI lane.** A macOS two-job workflow (publish → simulator tests),
   informational-first with promotion criteria, mirroring the Android emulator lane's
   posture.
5. **Host-initiated events land (Android + .NET).** The `NativeEvents` redesign:
   lifecycle (`onPause`/`onResume`/`onDestroy`) flows into .NET as native events;
   predictive back triggers navigation-back; a deep link resolves to the startup
   route — proven on the AVD. Closes the 4.2-triaged fork (issue trail updated).
6. **Clipboard + share on both platforms**, with the bridge-extension pattern
   documented (how a new host API joins the C-ABI: struct slot vs new callback,
   versioning posture, per-platform impl shape).
7. **Every new surface is CI-asserted.** Test counts recorded and asserted at each
   phase close (the M4 discipline continues); the iOS lane's counts join them when
   the lane stabilizes.
8. **Decision log committed.** Design + plan + conclusion per phase, plus the M5
   final audit at close → tag `v5.0`.

## Out of scope for this milestone

- Real-device iOS, code signing, App Store validation — needs an Apple Developer
  account; deferred with the simulator-only honesty note
- FCM / push notifications, secure-storage hardening — M6/M7 per BACKLOG
- Cross-platform APIs beyond clipboard + share (camera, geolocation, biometrics,
  purchases, background tasks) — M6+
- nuget.org publication — still deferred (M6, PackageReadmeFile prerequisite)
- The M6 packaging/ecosystem ledger — untouched

## Inherited from M4

- **NativeEvents redesign** (4.2 triage → M5) — covered by DoD #5; `NativeShellBridge`
  stubs the event no-op today, BN0014 guards the contract.
- **Host-initiated navigation** (M3 audit carryover) — covered by DoD #5 (predictive
  back + deep links ARE host-initiated navigation).
- **On-device inspector channel** (4.4 carryover) — NOT pulled in; stays ledgered
  (revisit after the iOS lane exists; a cross-platform diagnostics channel is
  M6-shaped).
- **Open hardening issues #8/#9/#12/#13** — stay open per the 4.2 triage doc (ledger
  of record); revisit triggers unchanged.

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open:

- **Phase 5.0** — iOS feasibility spike (DoD #1) — *the named risk, verified first*
- **Phase 5.1** — Host-initiated events: lifecycle + back + deep links (DoD #5)
- **Phase 5.2** — Swift shell foundation: boot + tree render on simulator (DoD #2, #4)
- **Phase 5.3** — Swift shell interactivity: events + bridge + navigation parity (DoD #3)
- **Phase 5.4** — Clipboard + share + the bridge-extension pattern (DoD #6)
- **Phase 5.5** — M5 final audit + close (DoD #7, #8) → `v5.0`

Sequencing rationale: the spike's verdict gates everything iOS; 5.1 is local-iteration
Android work that proceeds regardless; 5.2/5.3 ride the spike's learnings; the API
phase lands last so it extends a settled two-platform bridge.

## Why this milestone exists

The architecture has claimed "per-platform NativeAOT + thin native shell" since M3 —
with exactly one shell. A second platform is the only honest test of that claim: it
forces the C-ABI to prove it's actually the portable seam, surfaces every
Android-shaped assumption in the renderer contract, and turns the bridge from "the
thing the Kotlin shell does" into a specified, extensible pattern. Doing it
simulator-first on free CI keeps the cost of being wrong small.
