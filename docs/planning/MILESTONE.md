# Milestone 8 — Developer Ecosystem

**Status:** in progress — opened 2026-07-16
**Source:** the 2026-07-13 roadmap re-plan (capability before ecosystem) — the ecosystem
milestone, deliberately AFTER the capability that makes packaging worthwhile: M6 built the
layout engine, M7 built the components and the authoring story; M8 makes it something
another developer can actually consume.
**Predecessor:** Milestone 7 — complete 2026-07-16, tagged `v7.0`
([final audit](../plans/2026-07-16-milestone-7-final-audit.md): all 8 DoD PASS)

## Goal

After M7 a developer *inside this repo* can author a real app in `.razor` and run it on
both shells. After M8 a developer *outside this repo* can: `dotnet new` a BlazorNative
app (with a runnable Android shell), consume the libraries as packages that are one
manual go away from nuget.org, and learn the whole system from a public docs site — while
the repo's own demo app becomes the first true CONSUMER of the library instead of a
tenant inside it.

**Scoping decisions (owner, 2026-07-16):** publish-READY + manual go (the real nuget.org
push fires from a GitHub Release the owner publishes — no package is public until then);
the template ships the .NET app + the Android shell (iOS documented against the reference
shell); the docs site is **Docusaurus** mirroring the owner's AdoNet.Async setup
(`website/` + `docs.yml` → GitHub Pages), not DocFX — the API-reference generation gap is
closable with xmldoc2md if wanted; the `BlazorNative.Cli` is **ledgered**, not built —
`dotnet new` covers creation, and run/publish orchestration stays as documented scripts
until the workflow stabilizes enough to deserve a tool.

## The named risk (spike-shaped, first)

**The samples/library separation is a registration inversion, not a file move.**
`PageManifest.cs` lives in `BlazorNative.Runtime` and names the demo types — the library
hardcodes the app. Phase 8.0 inverts it: the app declares its pages through a public
registration API, the manifest becomes app-owned, and the shells embed the *sample app's*
publish. That inversion is exactly the consumer story every other M8 deliverable stands
on (the template IS "a minimal app that registers its own pages"; package purity IS "no
app types in the library"). If the inversion fights the trim laws (the mount lambdas are
the 4-IL2072 shape), 8.0 finds out before anything is packaged.

## Definition of Done

1. **The demo app is a consumer, not a tenant.** All nine demo pages + probes move out of
   `BlazorNative.Components` into a `samples/` app project; a public registration API
   replaces the library-owned `PageManifest` (the app declares route → component; the
   drift-test discipline survives — Android's mirror pins against the app's manifest);
   both shells embed the sample app's publish; **package purity is CI-asserted** (no
   `Bn*Demo`/probe types in any shipped assembly); every existing golden and device suite
   passes retargeted; publish gates hold (IL2072 count re-baselined only with analysis,
   never silently).
2. **Publish-ready packages.** The shipped set (Core, Runtime, Renderer, Components,
   Analyzers) packs clean with `PackageReadmeFile`, license, symbols + SourceLink,
   deterministic build; pack + local-feed consumer smoke asserted on CI every PR (the
   `samples/ConsumerSmoke` precedent, extended to the real package set); the versioning
   scheme decided and recorded.
3. **The release pipeline, manual go.** A release workflow that packs, validates, and
   pushes to nuget.org **triggered by a GitHub Release being published** (the
   AdoNet.Async `release.yml` pattern — publishing the Release IS the owner's go);
   a dry-run validation lane runs on CI without the key; `NUGET_API_KEY` documented as
   the one secret the owner adds; nothing publishes automatically from merges or tags.
4. **`dotnet new blazornative`.** The template produces the .NET app (using the DoD #1
   registration API) + the Android shell, runnable end-to-end on a machine with an
   Android SDK; template creation → build validated on CI; iOS shell setup documented
   against the repo's reference shell.
5. **The docs site.** Docusaurus in `website/`, deployed to GitHub Pages via a `docs.yml`
   mirroring AdoNet.Async; content: getting started (the template path), the architecture
   story (one Blazor app → NativeAOT → two shells; the wire, the ABI freeze, Yoga), the
   component reference (~20 components — hand-curated or xmldoc2md-generated, decided in
   the phase), the parity contract, and both shell setup guides.
6. **Hygiene + close:** every new surface CI-asserted (counts + gates with provenance);
   decision log per phase; final audit → tag **`v8.0`**.

## Out of scope for this milestone

- `BlazorNative.Cli` (`blazornative run android`, doctor, …) — **ledgered** (owner
  decision at milestone-open): templates cover creation; a CLI deserves its own design
  once the run workflow stabilizes.
- Actually pushing to nuget.org — the pipeline is ready; the push is the owner's Release.
- Camera/geolocation/biometrics/notifications, real-device iOS — **Milestone 9**.
- Accessibility, i18n, perf/security hardening — **Milestone 10**.
- Density-aware assets (`@2x`) — trigger is the first bundled/local-asset story; if the
  template or sample app grows bundled assets, this wakes up (the M7 ledger's terms).
- The iOS template shell, OTA, multi-window, desktop shells — backlog.

## Inherited from prior milestones (the ledger M8 consumes or carries)

- **From 7.6:** the M8 handoff ledger verbatim
  ([conclusion](../plans/2026-07-16-phase-7.6-conclusion.md)) — density assets (trigger
  above), SafeAreaView (M9-adjacent), the RN-survey rows, OnLoad/PlaceholderSrc/GIF,
  iOS out-of-range insert, the touch.view seam, android-build→ubuntu (pending billing
  evidence), horizontal scroll, the iOS lanes' duplicated publish, device-lanes→required
  (stability baseline), the true-move `@key` conversation.
- **From M4:** `PackageReadmeFile` as the recorded nuget prerequisite — DoD #2 closes it.
- **CI posture:** three required compile gates + advisory device lanes (ios lane:
  dispatch + push-main since 7.6; the restore condition lives in the workflow header).

## Initial phase plan

Tracked in `ROADMAP.md`. Approved at milestone-open:

- **Phase 8.0** — the samples/library separation + registration inversion (DoD #1) —
  *the named risk, verified first; everything else consumes its API*
- **Phase 8.1** — publish-ready packages + consumer smoke on the real set (DoD #2)
- **Phase 8.2** — the release pipeline, manual go (DoD #3)
- **Phase 8.3** — the `dotnet new` template: app + Android shell (DoD #4)
- **Phase 8.4** — the docs site: Docusaurus + GitHub Pages (DoD #5)
- **Phase 8.5** — hygiene + M8 final audit + close (DoD #6) → `v8.0`

Sequencing: 8.0 gates everything (the registration API is what 8.1 packages, 8.3
templates, and 8.4 documents); 8.1–8.2 make the packages real; 8.3–8.4 are the
outward-facing halves and can flex in order; 8.5 audits and tags.

## Why this milestone exists

M1–M7 proved the thesis to *us*: one Blazor codebase, two native shells, identical
frames, an authoring story a Blazor developer recognizes. None of that is consumable by
anyone else while the demo pages live inside the library, the packages exist only in a
local feed, and the knowledge lives in 40 design docs. M8 is the milestone where the
project stops being a proof and starts being a product surface — deliberately after the
capability was real, per the 2026-07-13 re-plan.
