# Milestone 10 — Consolidation & Hardening

**Status:** 🔄 **active — opened 2026-07-19; 2/7 DoD closed** — Phase 10.0 fixed the two
correctness bugs (#121, #123), red-first, no frozen-ABI change
([conclusion](../plans/2026-07-19-phase-10.0-conclusion.md)).
**Predecessor:** Milestone 9 — complete 2026-07-18
([final audit](../plans/2026-07-18-milestone-9-final-audit.md), all 6 DoD PASS; the ABI
grew exactly once in 9.0 and held for three more capabilities; no tag — the 8.6 rule,
closure is the audit).
**Source:** the full-repo review at `3866410` (Phase 9.0, 2026-07-17), which filed eight
concrete findings — issues **#119–#126** — plus M9's own earmark: *"Accessibility, i18n,
perf/security hardening — Milestone 10,"* and the owner's ask (2026-07-19) to **sweep the
docs and README** now that the release model and published state have changed.

## Goal

**0.1.0 is published** (seven packages on nuget.org, 2026-07-19). The library is now
*consumed*, not just built — and the 9.0 review found **real defects that ship inside it**:
an iOS app that reports itself as Android through a public API, a test channel that can
swallow a failing assertion (a false-green risk), and a version string frozen four
milestones back that consumers can read. M10 does **not add platform surface**. It makes
the published 0.1.x **honestly correct**, and makes the **docs and README tell the truth**
about what the project is now (published, auto-publishing, M9-complete) rather than what it
was mid-build. It needs **no device, no Apple account, no Firebase** — everything is
reachable from the setup already in hand.

**This is a deliberately small, unglamorous milestone, and that is the point.** The POC
proved its thesis across M1–M9; adding features on top of known defects and stale docs is
worse than paying them down. M10 leaves the published library trustworthy and its front
door accurate — a legitimate place to wind down.

## Scoping decisions (owner, 2026-07-19)

1. **No new platform surface.** Real-device iOS / TestFlight stays **out** — the owner has
   no iPhone and no Apple Developer account; the trigger is unchanged (acquire the
   account). **FCM push** stays ledgered (needs a Firebase project). **The inspector
   channel** stays ledgered (fourth deferral).
2. **Accessibility, i18n, and typography parity (#126) are OUT — reclassified as
   *investment, not need*.** They are "make it a real product people use" work, worth
   doing only if this heads toward real adoption; M9 earmarked them for M10, and M10
   **re-defers** them with that reasoning stated. This milestone is **correctness +
   accuracy debt only.**
3. **Docs and README are in scope as a first-class deliverable** (owner ask, 2026-07-19).
   Today's release-model change (draft-publish → auto-publish, PR #136) and the 0.1.0
   publish mean the Docusaurus site and `README.md` describe a world that no longer
   exists. A correctness milestone that fixes the code but leaves the front door lying is
   only half honest.
4. **The proof surface is CI + the existing suites.** No new external dependency, no new
   secret, no device lane. Fixes land behind the five required gates like everything else.
5. **A red-first proof per real bug.** Each correctness fix (DoD #1, #2) lands a test that
   FAILS against the current code first — the standing discipline, and doubly so for #123,
   whose whole nature is that a broken thing looks green.

## Definition of Done

1. **iOS no longer reports Android** (#121). ✅ **Closed by Phase 10.0** — an explicit
   `PlatformInfoKind` on the init-input struct (24→32 bytes; frozen bridge untouched), both
   shells pass their own kind, unset → `DevHost` not `Android`; iOS XCTest + .NET red-first.
   `PlatformKind` in the shared runtime's
   `PlatformInfo` / `GetPlatformInfoAsync` must reflect the *actual* shell, not a
   hardcoded `Android`. The kind comes from the shell (like the `os` string already does)
   — an init-option or bridge-supplied value — so the iOS `.a` and the Android runtime
   report their own platform. Proven on both shells (the iOS XCTest asserts `iOS`, not
   `Android`).

2. **The test channel cannot swallow a failure** (#123). ✅ **Closed by Phase 10.0** — the
   discarded `Frames` `InvokeAsync` task is now observed and its fault routed through
   `HandleException` / `StrictErrors`; a throw-in-subscriber test FAILED on unfixed `main`
   and passes after (red-first, the finding being false-green). `NativeRenderer` must observe the
   `Frames` `InvokeAsync` task so a fault in a frame subscriber routes through
   `HandleException` / surfaces under `StrictErrors` instead of being dropped. **Red-first
   is mandatory here:** a test that throws inside a `Frames` subscriber must FAIL before
   the fix and pass after — the false-green risk is the finding, so the proof must be that
   green now means green.

3. **No stale version reported to consumers** (#120). `Exports.VersionNumber` (the
   ungoverned 8th literal, frozen at `1.4.0-phase-5.4`) is brought **into** the version
   apparatus — mirrored from the manifest/props like the other literals (drift-pinned) or
   removed if nothing needs it. A consumer reading the runtime version gets the real one.

4. **The one load-bearing version is guarded** (#122). `RuntimeFrameworkVersion` (`10.0.9`,
   duplicated in the sample + template with no pin — the version that makes bionic/iOS
   NativeAOT compile at all) gets a drift pin linking its occurrences, matching the
   discipline every cosmetic literal already gets.

5. **Precision + cleanups triaged** (#124, #125). `BnListWindow.Compute` either uses exact
   integer arithmetic or the documented item-count bound is *enforced* (a value past which
   `float` drifts must not silently mis-window). The grouped low-severity items (#125) are
   each fixed **or** re-ledgered with a written reason — none left silently open.

6. **The docs and README tell the truth** (owner ask + #119). A full accuracy sweep of the
   **Docusaurus site** (`website/docs/**`) and **`README.md`**:
   - **The release model is current** — every mention of the retired *draft-Release +
     manual-publish click* flow is rewritten to the **auto-publish-on-merge** reality
     (PR #136): merging a release PR publishes from `release-please.yml`'s own `push` job,
     no draft, no click. (`GITHUB-SETUP.md` was updated in #136; the site + README are
     swept for the same.)
   - **The published state is current** — the docs reflect that **0.1.0 is live on
     nuget.org** (seven packages), with a correct install/getting-started path a stranger
     can follow.
   - **No overclaim survives** (#119) — the README's "all four test counts asserted in CI"
     line is corrected to say which gates actually gate a PR (`.NET` + the two compile
     gates) vs. which are dispatch/nightly (Android + iOS instrumented), and every cited
     count is refreshed to the live baseline (.NET 754 / JVM 119 / Android 209 / iOS 233).
   - **No stale milestone/version prose** — references to mid-build state (M8/M9 in
     progress, draft flow, pre-publish) are brought to "M9 complete, published, hardening."
   - Where practical, a drift guard is added for any doc claim that a test can pin (counts,
     export names), so the docs can't silently re-drift.

7. **Hygiene + close.** Every fix CI-asserted (counts + gates with provenance); a decision
   log per phase; the closed issues closed on GitHub with the fixing commit; a **final
   audit** verifying all six above against live evidence. **No milestone tag** — closure is
   the audit (the 8.6 rule). Fixes land as `fix:` commits, so release-please walks the
   patch version (0.1.0 → 0.1.1 → …) and the changelog writes itself; doc-only changes ride
   as `docs:`/`chore:` and don't bump.

## Out of scope for this milestone

- **Real-device iOS / TestFlight** — deferred; trigger = the Apple Developer account.
- **FCM push** — ledgered; trigger = a Firebase project.
- **The inspector channel** — ledgered (fourth deferral).
- **Accessibility, internationalization, typography/font parity (#126)** — investment, not
  need; re-deferred with reasoning (scoping decision 2).
- **The P5 feature epics** — State (#22), Styling (#21), Navigation package (#23), CLI
  tool (#24), component-library expansion (#20). Genuine future work, not correctness debt.

## Inherited from prior milestones (the ledger M10 carries)

- **From the 9.0 review:** issues #119–#125 — **this milestone's backbone** (DoD #1–#6);
  #126 (font parity) re-deferred as investment.
- **From M8:** the KDoc sweep + map extraction — **trigger fired** (0.1.0 published), but
  the template *pack* is not on nuget.org (separate feed), so it stays ledgered until that
  feed publishes; `BionicNativeAot.targets` → the Runtime package's `build/`; density
  assets (trigger: the first bundled-asset story).
- **From M5:** FCM push (carried, trigger above).
- **CI posture:** five required contexts (`build-test`, `android-build`, `ios-build`,
  `pr-title`, `footer-check`); advisory device lanes unchanged. **Auto-publish is live**
  (PR #136, 2026-07-19): merging a release PR publishes to nuget.org from
  `release-please.yml`'s own `push` job — so DoD #7's `fix:` commits walk 0.1.x on merge.

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open:

- **Phase 10.0** — the two correctness bugs (DoD #1, #2): iOS-platform-kind and the
  swallowed-`Frames`-fault. *Test-integrity first — #123 protects every other proof in the
  repo, so it goes first, red-first.*
- **Phase 10.1** — version governance (DoD #3, #4): the stale `Exports.VersionNumber` and
  the unguarded `RuntimeFrameworkVersion`, both into the pin apparatus.
- **Phase 10.2** — docs + README accuracy sweep (DoD #6) + precision & cleanups (DoD #5):
  the site and README brought current (release model, published state, counts, no
  overclaim), plus `BnListWindow` and the grouped #125 items.
- **Phase 10.3** — hygiene + M10 final audit + close (DoD #7).

## Why this milestone exists

M1–M9 built and shipped: a rendering engine, an authoring story, an ecosystem, five native
capabilities, and seven packages on nuget.org. The 9.0 review then found that some of what
shipped is *wrong* — an iOS app calling itself Android, a test lane that can lie green, a
version literal four milestones stale — and today's release-model change left the docs and
README describing a flow that no longer exists. Now that the library is public, those are
things a consumer can hit and read, not internal notes. M10 pays that debt down, makes the
front door accurate, and stops — the honest bookend to a POC that has already proven what
it set out to prove.
