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

1. ✅ **The demo app is a consumer, not a tenant.** All nine demo pages + probes move out of
   `BlazorNative.Components` into a `samples/` app project; a public registration API
   replaces the library-owned `PageManifest` (the app declares route → component; the
   drift-test discipline survives — Android's mirror pins against the app's manifest);
   both shells embed the sample app's publish; **package purity is CI-asserted** (no
   `Bn*Demo`/probe types in any shipped assembly); every existing golden and device suite
   passes retargeted; publish gates hold (IL2072 count re-baselined only with analysis,
   never silently).
   **Closed by Phase 8.0** ([conclusion](../plans/2026-07-16-phase-8.0-conclusion.md)):
   `RegisterPages` + the `Routed<T>`/`Named<T>` DAM(All) factories; the app registers via
   `[ModuleInitializer]`; Runtime's Components `ProjectReference` deleted; the shells
   embed `samples/BlazorNative.SampleApp`'s publish under the frozen artifact names with
   **zero shell-code lines** changed; 16 types moved; purity pinned (roster both
   directions + pattern net + the six-assembly shipped set) in the required lane. The
   named trim risk MATERIALIZED and was closed: nativelib ILC trimmed the whole app
   (module initializers are NOT unconditional roots for a non-export assembly) — caught
   by the stop-and-analyze rule, fixed with `TrimmerRootAssembly` (the app roots itself;
   libraries stay trimmable), and pinned three ways (the IL2072==4 gate, the JVM real
   mount, the page-name presence probe). Evidence: .NET **553/0** · JVM **106/0** ·
   Android instrumented **184/0** · iOS **154/0** (run 29527121729); 4 IL2072 + 9 exports
   on all four RIDs.
2. ✅ **Publish-ready packages.** The shipped set (Core, Runtime, Renderer, Components,
   **Http**, Analyzers — **six**; this list said five until Phase 8.1 found the omission
   was shorthand drift, not a scoping decision: Http has packed and shipped through the
   consumer smoke since 4.5, Runtime `ProjectReference`s it, and the purity pin's
   mutation-guarded shipped-set literal says six) packs clean with `PackageReadmeFile`,
   license, symbols + SourceLink, deterministic build; pack + local-feed consumer smoke
   asserted on CI every PR (the `samples/ConsumerSmoke` precedent, extended to the real
   package set); the versioning scheme decided and recorded.
   **Closed by Phase 8.1** ([conclusion](../plans/2026-07-16-phase-8.1-conclusion.md)):
   `src/Directory.Build.props` is the ONE metadata home and the ONE version literal —
   licensed by the purity pin proving src/ holds exactly the six, which made the 4.5
   "per-csproj by design" rule obsolete BY CONSTRUCTION. Runtime becomes the sixth
   package (packable only because 8.0 evicted `PublishAot`); six READMEs written — **the
   M4-recorded `PackageReadmeFile` prerequisite is CLOSED**. Versioning:
   `1.0.0-preview.1`, hand-bumped, reset off the never-public `1.2.0-phase-4.5`;
   GitVersion/release-please rejected for now and re-addressed to 8.2 at the workflow
   layer (with the `v*` milestone-tag collision named). The Gate 1 review demonstrated a
   **vacuous pass** — the nupkg type scanner passed GREEN while blind — fixed with a
   two-arm positive control (non-empty + a real sentinel per package), the arms proven
   non-redundant by re-running the historical `return ,$names` bug. Evidence: .NET
   **557/0** (23 + 132 + 402) · JVM **106/0** · publish gates unmoved (4 IL2072 +
   9 exports + the page probe) · smoke green end to end (6 nupkg + 5 snupkg at
   `1.0.0-preview.1`, zero pack warnings, SourceLink `repository@commit` stamped,
   provenance ×6, BN0011 trip, ConsumerSmoke PASS — the 8.0 API's first out-of-repo
   consumer). Device lanes untouched (184/154 stand on 8.0's provenance).
3. ✅ **The release pipeline, manual go.** A release workflow that packs, validates, and
   pushes to nuget.org **triggered by a GitHub Release being published** (the
   AdoNet.Async `release.yml` pattern — publishing the Release IS the owner's go);
   a dry-run validation lane runs on CI without the key; `NUGET_API_KEY` documented as
   the one secret the owner adds; nothing publishes automatically from merges or tags.
   **Closed by Phase 8.2** ([conclusion](../plans/2026-07-16-phase-8.2-conclusion.md)):
   `release.yml` is THE ONE DOOR — `validate` (no key, ever) → artifact → `push`
   (`needs: validate`, `if: event_name == 'release'`, the sole `NUGET_API_KEY`
   reference, **no checkout at all** — "pushes only validated bytes" is structural).
   Every DECISION lives in `scripts/release-preflight.ps1` (an 8-row `-SelfTest` on
   every PR), because YAML firing once per Release is the least testable code in the
   repo. **THE HEADLINE — the `v8.0` hazard: this milestone creates the pipeline's own
   worst input.** DoD #6 tags `v8.0`; the `release` event has **no tag filter**, so an
   AdoNet.Async-shaped `${TAG#v}` would have pushed six packages at version "8.0"
   **permanently** (no hard delete on nuget.org). **Double-guarded** — the classifier
   reads the tag's *shape* (milestone → announce + skip, exit 0: reddening a legitimate
   announcement would train the owner to ignore reds on release runs), the assertion
   compares its *content* to the props — and **either alone stops it** (verified: with
   the milestone regex neutered, `v8.0` → unrecognized → RED). **`v8.0` publishes
   nothing, and the classifier says so in the required lane.**
   **DoD-wording honesty — `dotnet nuget push` has NO `--dry-run`** (verified against
   the live CLI; the full option set has no simulation flag). **A push dry-run does not
   exist, so the lane could not be one.** What shipped instead is a **nuget.org-state
   preflight** — the only thing a real push can reject that no local step can know
   (everything else is a local fact the smoke already owns on every PR) — key-free, on
   every PR touching the release machinery or the props. Its own **vacuity trap** (all
   six ids 404 today: every current answer to "is it free?" is "absent") is closed by a
   **two-arm positive control**, mutation-proven (already-published → RED; typo'd
   endpoint → RED "not evidence the id is free"; the offline arm proved itself when the
   sandbox's DNS failed mid-gate). **release-please: OUT** — the re-evaluation reversed
   8.1's *reason*, not its verdict: its payoff mechanism (merge release PR → cut
   Release) is verbatim what this DoD forbids. **DoD #3 closes as publish-READY with its
   arrows NAMED** — the phase ships a **PROVEN/UNPROVEN table** (U1–U8): eight fail
   safe, **seven fail loud, U8 (verdict propagation) is the quiet one**, so the owner's
   first-Release check is **"did `push` RUN?"**, not "was there a red?". Evidence: .NET
   **559/0** (23 + 132 + 404) · JVM **106/0** · publish head **byte-identical** (no
   `src/`/`samples/` change — the gates could not have moved) · smoke green with the new
   snupkg-**pairing** tooth · actionlint 1.7.7 clean ×4 with five mutants caught · **run
   29540566554** (`release / validate`, `pull_request`) **green — the self-proving lane
   proved itself on its own PR**. Device lanes untouched (184/154 stand on prior
   provenance). **Nothing published; no tag created; no secret added.**
4. **`dotnet new blazornative`.** The template produces the .NET app (using the DoD #1
   registration API) + the Android shell, runnable end-to-end on a machine with an
   Android SDK; template creation → build validated on CI; iOS shell setup documented
   against the repo's reference shell.
   **Named inputs from Phase 8.0** (the trim finding —
   [conclusion](../plans/2026-07-16-phase-8.0-conclusion.md)): (a) the template's csproj
   **must carry `<TrimmerRootAssembly Include="<AppAssembly>" />`** — a fresh app has
   exactly the shape nativelib ILC trims silently (exports in Runtime, pages in the app);
   (b) when copying the `EnsureRegistered` pattern, flip or note its guard order (Gate 1
   review M-1: the once-guard is set before `RegisterPages`, so a throwing registration
   silently no-ops on retry).
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
- **From M4:** `PackageReadmeFile` as the recorded nuget prerequisite — ✅ **CLOSED by
  Phase 8.1** (DoD #2): the mechanism lives in `src/Directory.Build.props` and six
  per-package READMEs supply the content; a missing one fails pack as NU5039.
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
