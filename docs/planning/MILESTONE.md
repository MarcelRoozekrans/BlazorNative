# Milestone 8 — Developer Ecosystem

**Status:** ✅ complete — opened 2026-07-16, closed 2026-07-17
([final audit](../plans/2026-07-17-milestone-8-final-audit.md): all 6 DoD PASS).
**There is no `v8.0` tag and there will not be one** — Phase 8.6 retired the
milestone-tag namespace and gave `v<semver>` to release-please, so DoD #6's
"→ tag `v8.0`" named a **ritual, not a result**, and the ritual is cancelled rather
than pending. **M8 is complete on the audit, which is what the DoD actually wanted.**
**And its predecessors' tags are gone too** — `v1.0`–`v7.0` were **deleted on 2026-07-17**
as the last step of 8.6's close, so **this repo has no tags at all** until release-please
cuts the first `v1.0.0-preview.2`. The [ROADMAP](ROADMAP.md)'s standing note is the place
that explains why `git checkout v6.0` fails.
See the [M8 audit addendum](../plans/2026-07-17-milestone-8-audit-addendum.md) — it
records what 8.6 changed under this milestone, and **this document is not retrofitted
to match it**: the DoD texts and the closure evidence below were true when written and
are left standing.
**Opened:** 2026-07-16
**Source:** the 2026-07-13 roadmap re-plan (capability before ecosystem) — the ecosystem
milestone, deliberately AFTER the capability that makes packaging worthwhile: M6 built the
layout engine, M7 built the components and the authoring story; M8 makes it something
another developer can actually consume.
**Predecessor:** Milestone 7 — complete 2026-07-16
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
4. ✅ **`dotnet new blazornative`.** The template produces the .NET app (using the DoD #1
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
   **Closed by Phase 8.3** ([conclusion](../plans/2026-07-17-phase-8.3-conclusion.md)):
   `templates/BlazorNative.Templates` — a real template pack on its own feed
   (`artifacts/templates`), OUTSIDE the shipped six (a seventh csproj under `src/`
   un-licenses 8.1's props home, and the six's pins are assembly-shaped: a content-only
   pack satisfies none) and outside the release path. **Both named inputs DISCHARGED:**
   **(a)** the generated csproj carries `<TrimmerRootAssembly Include="$(AssemblyName)" />`
   — **`$(AssemblyName)`, not a substituted literal**, because MSBuild does not validate a
   root name (a bogus one passes with no error and no warning), so the literal form's
   failure mode IS the 8.0 silent trim from a new direction; verified on hostile names
   (`-n Weird.Name.With.Dots` → the right Identity). **(b)** the guard order is FLIPPED in
   **both** copies (the sample's and the template's) — one source-order pin covers both, so
   reference and template cannot drift apart on the exact line 8.0's review flagged;
   behaviour-identical claim verified at the mechanism (validation precedes assignment
   under a lock; no callback into app code; both callers are ModuleInitializers).
   **THE HEADLINE — two defects, both invisible to every pin, both found by DOING THE REAL
   THING.** (1) **The generated Android app did not compile**: `MainActivity` used a bare
   `R.layout.main` with no `import <namespace>.R`. AGP generates `R` into the `namespace`;
   Kotlin resolves a bare `R` against the FILE's package. The repo's two match **by
   coincidence**; the template's differ **by design** — and that divergence IS the
   byte-identity trick. **All nine .NET pins were green; only `assembleDebug` saw it** —
   which is why the design chose a real compile over `gradlew tasks`. **DoD #4's own
   "runnable end-to-end" was unmet until it landed.** *Byte-identity to a reference is not
   correctness when the reference's correctness depends on a property the copy deliberately
   changes.* (2) **`.gitignore` was swallowing `templates/.../build/BionicNativeAot.targets`**
   → every generated app would ship with no NDK shim → no bionic publish → no APK — and **a
   byte-identity pin cannot see a file git never committed**. Found by construction.
   **THE CENTRAL PROOF — the trim line, on GENERATED output** (a text pin proves the line is
   in the file; 8.0's own line was found missing by a publish, not by a read):
   `scripts/template-smoke.ps1` packs → installs **from the nupkg** → generates → restores
   (provenance ×3) → publishes → `assembleDebug`. Green: **4 IL2072 · 9 exports ·
   `BnStarterPage` present (3838 KB) · APK 15590 KB**. **The delete-the-line mutation reds
   arm 1 AND arm 3 (0 IL2072, page ABSENT, 3592 KB) — and arm 2 STAYS GREEN**, because the
   exports ride out of the *referenced* Runtime assembly and survive a whole-module trim of
   the app: **arms 1–2 are facts about the LIBRARIES; only the probe is a fact about the
   USER'S CODE.** **Review: 1 Critical + 1 Important + 3 Minor, all fixed.** **I-1 — the
   shell pin was a ROSTER and did not know how many files it SHOULD pin** (proven live with
   three mutations ALL GREEN, incl. **7 unrostered template files deleted** — a `dotnet new`
   app missing its wrapper JAR would have shipped), violating the file's own cited "never a
   roster" rule; the fix pass found **the reviewer's prescribed fix insufficient for the
   reviewer's own class** and closed it from three sides (a 32-name content manifest matching
   the pack's glob, the shell set DERIVED from the tree — **which widened it 15 → 19**,
   revealing four byte-identical copies **nothing compared at all** — and the repo-side pin).
   **The root cause the review missed: the design's split table filed the gradle wrapper as
   TEMPLATE-OWNED — a wrong row in a table became a hole in a pin.** Evidence: .NET
   **570/0** (23 + 415 + 132; 559 → 568 → 570) · JVM **106/0** untouched · publish gates
   **re-quoted** clean after the head moved (4 IL2072, 9 exports, pages present, DLL 4217
   KB — no re-baselining) · consumer smoke still exactly **6 nupkg + 5 snupkg** (the
   template rides its own feed) · template smoke **PASS** · actionlint clean · Yoga pinned
   across **FOUR** files now (android=ios=ci=template=3.2.1). Device lanes untouched
   (184/154). **DoD #4 closes with its arrows NAMED** (U2, U4, U5, U6 — **U1 CLOSED at
   Gate 3**: `dotnet new search blazornative` → "No templates found"; **U3 mooted** by the
   generated-output probe), and **U6 matters: proven on the CI runner, not on a stranger's
   laptop.** **The template does not publish until the owner's Release — the same gate as
   the packages.**
5. ✅ **The docs site.** Docusaurus in `website/`, deployed to GitHub Pages via a `docs.yml`
   mirroring AdoNet.Async; content: getting started (the template path), the architecture
   story (one Blazor app → NativeAOT → two shells; the wire, the ABI freeze, Yoga), the
   component reference (~20 components — hand-curated or xmldoc2md-generated, decided in
   the phase), the parity contract, and both shell setup guides.
   **⚠ THE DoD's OWN "~20 components" IS NOT A COUNT OF ANYTHING — measured at Phase 8.4
   Gate 4: 15 concrete `ComponentBase` types, 26 public types, 196 `[Parameter]`
   properties.** Recorded here so 8.5's audit does not trip on the wording. The reference
   generates **26 pages** (the public surface), not 15 or 20. **"21 components" — the number
   that reached three files this phase — was born TRUE as *21 documented types* (27 − 6
   headless razor components) and was then copied out of its meaning**, which is the failure
   mode DoD #5 turned out to be about.
   **DoD-wording honesty — "both shell setup guides" named a document that did not exist.**
   There has never been an Android shell setup guide, because **the Android path IS the
   template** (`ios-shell-setup.md:3-6` says so). A hole in the DoD's wording, not in 8.3's
   delivery. Resolved by **writing** `website/docs/shells/android.md` (short, honest, and it
   does not transcribe the Gradle files) rather than by restating the DoD.
   **Closed by Phase 8.4** ([conclusion](../plans/2026-07-17-phase-8.4-conclusion.md)):
   `website/` (Docusaurus 3.10.2, scaffolded from the live AdoNet.Async mirror, 41 pages,
   `onBrokenLinks` **and `onBrokenAnchors`** = `'throw'`); **the component reference is
   GENERATED** (`scripts/generate-reference.ps1` → xmldoc2md against a **publish** output →
   27 MDX into a `.gitignore`d dir) **and never committed** — the `///` next to the code
   stays the one home; `docs.yml` builds on PRs, deploys on main, and is **advisory** (the
   three required gates keep their names and contexts); the icon shipped (**128×128 PNG**,
   packed to all six roots — **8.2's trigger fired**); `docs/ios-shell-setup.md` and
   `docs/analyzers.md` **MOVED**, repo copies deleted.
   **THE HEADLINE — a phase about stale copies that kept catching itself.** **(1) The
   generator lied during design**: xmldoc2md against `bin/` → **`Generation: 10 succeeded, 0
   failed`, exit 0, green — and ZERO components.** `Microsoft.AspNetCore.Components.dll` is
   not beside the assembly, so `ComponentBase` will not resolve, so **every derived type
   drops SILENTLY**. Against a publish output: **26**. ***10 vs 26 is invisible to anything
   that does not count*** — the fourth arrival of 8.1's `return ,$names` and 8.3's blind
   roster, by a new door. **(2) Pin 2 read as ON and was OFF, twice**: the `.props` imports
   *before* the project body, so `{BnEnforceDocCoverage:true, NoWarn:…;CS1591}` — true and
   suppressed anyway (fixed with `src/Directory.Build.targets`); then **still not a pin**,
   because `ci.yml` builds without `-warnaserror` — CS1591 would have been **8 warnings in a
   lane that exits 0**. ***A pin's costume on a pin's absence.*** **(3) Pin 2's guarantee
   covered exactly HALF the parameter surface** (review S1-1): the Razor generator emits
   `#pragma warning disable 1591` at **line 3 of every `*_razor.g.cs`**, so an `@code`-block
   `[Parameter]` is **structurally invisible** to CS1591 — deleting `BnSlider.Value`'s
   summary gave **0 errors, 4/4 pins green, and the property `@bind-Value` targets GONE from
   the shipped XML**. Closed by `EveryParameter_CarriesADocComment`. **(4)
   `onBrokenAnchors` defaulted to `'warn'`** — anchors are half the internal-link surface and
   **the reference is GENERATED from `<see cref>`→anchor links**; Gate 2 fixed five and
   nothing kept them fixed. Now `'throw'` (Docusaurus's own default carries
   `// TODO Docusaurus v4: change to throw`).
   **"192" WAS WRONG TOO** — it came from `grep '[Parameter]'`, which **cannot see
   `[Parameter, EditorRequired]`** (BnList declares four; the review read BnList as **2**
   where the assembly has **6**). **The fix pass's pin derived 196 on its first run, because
   it REFLECTS instead of grepping** — and the review had reproduced the design's number and
   reconciled to it, **inheriting the same blind pattern**. The blind half is **exactly 50%
   (98/98)**, not 51%. *A number is not measured until the thing that reads it can see every
   form it takes.*
   **THE DocFX ANSWER, as the owner asked for it:** **DocFX genuinely wins the free API
   reference** — native .NET, ingests assemblies + XML docs, no glue; **for a repo whose
   reference surface IS .NET types that is the most relevant feature either tool has, and
   Docusaurus lacks it at any price.** Docusaurus wins that **the owner already runs it** (one
   toolchain, one CI shape) and **a docs site is 80% prose**. **What MEASURING changed:** the
   XML docs were **already COMPLETE** (so nothing was traded away — **8.1's premise that "doc
   coverage is 8.4's editorial work" was WRONG; coverage was done, VOICE was the job**), and
   their content was **repo-voiced** — `BnView` said *"since Phase 3.4 Gate 1"*, *"the BnDemo
   goldens stay byte-identical"*, and *"Razor awaits .razor compilation (M6)"*, **which was
   not merely dated but FALSE** (`<BnInput @bind-Value="_text" />` is live in
   `BnDemo.razor:51`). **DocFX would have published that phase history exactly as
   faithfully.** **Both tools needed the same editorial pass on the same XML. DocFX saves the
   plumbing, not the work** — and against a toolchain the owner already knows, **plumbing is
   the cheaper thing to give up.**
   **THE ONE-HOME RULE AS SHIPPED — GENERATED / LINKED / OWNED; *"kept in sync is not a
   home"*. The site NEVER includes, it POINTS** (8.3's iOS-doc precedent generalized: *prose
   a gate does not keep true should point, not copy*). `docs/plans/` links **as a class**.
   **The site states NO number a gate owns** — reviewer-audited: no counts, no versions, **no
   Yoga literal**.
   **Gate 1's best catch — a permanent 404 INSIDE a package.** The design named **ONE**
   inbound link to the moved docs; there were **EIGHT**. The other seven are `helpLinkUri`
   values in the analyzer sources — *the string a consumer's IDE opens from a BN squiggle*.
   Deleting `docs/analyzers.md` per the fates table would have shipped **a permanent 404 to
   every user of every BN rule, at the moment they are already confused**. Re-pointed, and now
   **PINNED** (`AnalyzerHelpLinkDriftTests`, review S2-2): **they ship INSIDE a nupkg — the one
   link class a consumer's IDE resolves that no site build can ever see** (absolute external
   URLs are invisible to `onBrokenLinks`/`onBrokenAnchors`). Its `baseUrl` mutation **reds all
   seven at once — U2 from the package side**.
   **THE SEVENTH PACKAGE** (Gate 3's find, no ledger had noticed): 8.1 declared
   `src/Directory.Build.props` the ONE metadata home; **8.3 then created
   `templates/BlazorNative.Templates` OUTSIDE `src/` DELIBERATELY** (a seventh csproj inside
   **un-licenses** the props — the shipped-set pin is what makes that home legal), so it
   carries its own `PackageProjectUrl` **by necessity**, still pointing at the repo while the
   six moved. ***Shipping 6-of-7 is not a rule, it is a coincidence waiting to be noticed by a
   user.*** Split the same way: **RepositoryUrl = where the source is; PackageProjectUrl =
   where the docs are** — verified **in the PACKED nuspec, since the pack is what a user
   gets**. Also Gate 3's: the footer label **`Source: <repo>`** re-pointed at a docs site would
   read ***"Source: a site that is not the source"*** — **a fact made wrong by the act of
   fixing it**; now `Docs:` (nuget.org still renders Source from `<repository>`).
   **Review: PASS with 2 Important + 8 lesser — ALL applied, none deferred.** **THREE
   PRESCRIPTIONS THE FIX PASS REFUSED, CORRECTLY** (the reviewer is strong and was still wrong
   three times): reusing `PublicSurfaceDocs()` for S1-1 would have **reddened 124
   correctly-documented properties** (it returns `member.Value`; an **inheritdoc-only
   member's Value is the EMPTY STRING** — indistinguishable from undocumented); **`navbar.logo.to`
   is invalid** (the build refuses it: *"navbar.logo.to is not allowed"* — the diagnosis was
   right, `href: '/'` is the remedy); and the **"192" reconciliation**. **The 16th design row:
   the design asked for an IMPOSSIBLE mutation** (*"rename a component ⇒ Pin 1 red both
   ways"*) — **Pin 1 derives BOTH sides**, so a rename moves them together: **green, correctly,
   and by design**. Satisfying it would need **the roster 8.3's I-1 forbids**. ***STRUCK, with
   the reason.*** **Pin 3's own mutation corrected the pin** (`/\bgolden\b/` cannot match
   *"goldens"*, the plural the repo writes) and **Pin 3 scans the PUBLIC SURFACE, not the
   file** — a whole-file regex would red on maintainer docs doing their job and ***teach the
   next author to delete engineering truth to go green***.
   **FOUND ONLY BECAUSE SOMETHING FINALLY LOOKED:** the social card's tagline **overflowed its
   own 1200px viewBox**, clipping "views" — **invisible for as long as nothing rasterized it**;
   and **an SVG `og:image` renders NO preview on any social platform** (all require raster) —
   **decision 6 had already learned that for NuGet ("JPEG/PNG only") and it did not transfer**.
   Both fixed; the card is a PNG.
   Evidence: .NET **577/0** (Renderer 132 + Analyzers 25 + Runtime 420; **570 → 574 → 577**
   with provenance) · JVM **106/0** untouched · device lanes **untouched** (184/154 on prior
   provenance — **no wire/ABI/golden/shell change**) · publish gates unmoved **STRUCTURALLY**
   (`Directory.Build.props`/`.targets` exist **ONLY under `src/`**; the publish head is
   `samples/`, so **MSBuild's upward search finds nothing**) · smoke **PASS**: **6 nupkg + 5
   snupkg**, zero pack warnings, nuspec + inventory ×6, **`icon.png` at root**, and it printed
   the packaged link **live** (`warning BN0011: … /docs/analyzers#bn0011`) · fresh-clone
   `npm run build`: `Generation: 26 succeeded` → 27 files → **[SUCCESS], 41 pages, 0 warnings,
   0 broken links/anchors** · actionlint clean · **the icon: pack IS the pin, verified by
   running it** — **`NU5019 File not found`, exit 1**, ***not NU5046 as three places claimed***
   (the `<None>` carries no `Exists()` guard, so the item-level failure fires first).
   **DoD #5 closes with its arrows NAMED (U1–U7).** **U1/U2 stand and matter: nothing renders
   until the owner clicks Settings → Pages → Source: GitHub Actions, and U2 is THE QUIET ARROW
   — a wrong `baseUrl` builds GREEN and deploys a styleless site, so the owner's first check is
   a LOOK, not a red.** Every re-pointed link (7 `helpLinkUri` + the READMEs + the template)
   404s until that click — **bounded, and strictly better than the old targets, which 404
   FOREVER**. **U3 stands: Pin 3 catches phase history; it cannot catch dull, wrong-level or
   unhelpful — taste is unpinnable** (the review found `BnImage.cs:291`'s maintainer
   parenthetical shipping to a public page; cut). **Nothing published; no tag created; no
   secret added — this phase publishes a website and nothing else.**
6. ✅ **Hygiene + close:** every new surface CI-asserted (counts + gates with provenance);
   decision log per phase; final audit → tag **`v8.0`**.
   > **The `v8.0` clause is CANCELLED, and this DoD text is left as written** (Phase 8.6,
   > decision 8 — [addendum](../plans/2026-07-17-milestone-8-audit-addendum.md)). The tag
   > named a **ritual**; the **result** DoD #6 asked for — CI-asserted surfaces, the
   > decision log, the final audit — **shipped in full and the audit verified it**.
   > **M8's completion rests on the audit, not on a tag**, and no `v8.0` will ever exist.
   **Closed by Phase 8.5** ([final audit](../plans/2026-07-17-milestone-8-final-audit.md)):
   **all six DoD PASS on evidence re-verified LIVE, not cited** — .NET **580/0** (132 + 25 +
   423; 577 → 580, +3, this phase's own hygiene pin) · JVM **106/0** across 19 suites
   (`--rerun-tasks`, 27 executed — not a cached green) · the publish gate **4 IL2072 + the 9
   exports via dumpbin + the page probe** on a CLEAN win-x64 ILC pass (DLL 4217 KB, unmoved) ·
   consumer smoke **6 nupkg + 5 snupkg** at the one version literal with `repository@commit`
   at the tip · reference **`Generation: 26 succeeded`** with the components present ·
   `release-preflight -SelfTest` **8/8, `v8.0 → SKIP (milestone)`, exit 0** · the template's
   **generated output** clearing the bar + a 15590 KB APK (run 29554045138) · Yoga **3.2.1
   across FOUR pins**, printed equal by the required lane · branch protection read back ==
   exactly `["build-test","ios-build","android-build"]` · iOS **154/0 at `6afd8af`** — main's
   exact tip (run **29554540199**), better provenance than M7's audit had.
   **THE ABI DID NOT MOVE, AND THE SHELLS' SOURCE DID NOT EITHER:** `git diff v7.0..HEAD` over
   `src/BlazorNative.Jni`/`src/BlazorNative.Apple` touches **zero** `.kt`/`.swift`/`.h`/`.mm` —
   only two BUILD files (`project.yml`, **comment-only**; `build.gradle.kts`, 8.0's
   publish-head retarget). **So the device baselines stand on 8.0's provenance, not v7.0's** —
   which is exactly why 8.0 re-ran both lanes. 9 exports, the 72-byte bridge and thirteen
   NodeTypes verified on all three mirrors; **M8 added no wire vocabulary at all**.
   **THE HONESTY ROWS — two DoD texts do not describe what shipped, and the audit is where
   that is said out loud:** **DoD #2 named five packages; SIX ship** (Http, packed since 4.5;
   the five-name list was shorthand drift, corrected in 8.1) — plus a **SEVENTH** publishable
   pack (`templates/BlazorNative.Templates`) that 8.1's "ONE metadata home" rule
   **structurally cannot reach** (a seventh csproj under `src/` un-licenses the props; the
   shipped-set pin is what makes that home legal). **The rule's true scope: one home for the
   six; the seventh agrees by pin-less discipline.** **DoD #3 says "a dry-run validation
   lane" — `dotnet nuget push` has NO `--dry-run`**; what shipped is a nuget.org-state
   preflight. ***The DoD's word is wrong; the thing is better than the word.*** **DoD #5's
   "~20 components" is not a count of anything** — re-measured live: **15** concrete
   `ComponentBase` types, **26** public types (the reference generates 26), **196**
   `[Parameter]` properties — and **the blind grep reproduces at exactly 192**, the 50/50
   split confirmed.
   **THE HYGIENE LEDGER — four items, each decided.** **DONE:** the README's counts + Yoga
   literal are **gate-held** (`ReadmeDriftTests`, +3, required lane; both sides DERIVED from
   the `if` CONDITION that decides, never the step's prose; the roster knows its size; five
   mutations run and quoted). ⚠ **The mutation that deleted the Yoga literal FAILED TO RED on
   its first run — because the paragraph written to explain that the literal has ONE home had
   NAMED THE VERSION.** A third copy, minted inside the sentence about not copying; **8.4's
   Gate 3 author did the identical thing.** *The pull toward a fresh copy is not theoretical,
   and only a mutation found it — both times.* **DEFERRED with triggers:** the KDoc sweep +
   map extraction (**one item** — the clean fix retires the excision and costs a Kotlin change
   + a 184 device re-run; **trigger: before the first Release that publishes the template
   pack**, since U5 bounds the exposure — the pack is not on nuget.org, so no stranger can
   generate that file today); `BionicNativeAot.targets` → the Runtime package's `build/` (it
   costs the smoke's inventory tooth). **ACCEPTED — because the premise was FALSE:** 8.4's
   "~15-min local Runtime lane" measures **4 s cold** for the generator and **6 s** for the
   fixture in-lane — **off by ~150×**; the 15 minutes was the solution BUILD, which the
   fixture neither causes nor can avoid. Making it skippable would have bought 4 seconds by
   weakening the one pin that catches 10-vs-26. ***A cost that was never measured, carried
   into a ledger, about to justify weakening a pin — the sixth arrival of this milestone's own
   failure mode, this time in a document about the other five.***
   **U1 IS OBSERVED, NOT PREDICTED:** `gh api …/pages` → **404** and `Deploy Documentation` at
   main's tip is **`build: success` / `deploy: failure`** with exactly the legible error 8.4
   promised. **Nothing renders until the owner clicks Settings → Pages → Source: GitHub
   Actions; U2 stays the quiet arrow — the first check is a LOOK, not a red.**
   **Nothing published; no tag created; no secret added.**

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
- **Phase 8.5** — hygiene + M8 final audit + close (DoD #6) *(the `v8.0` tag this line
  planned was cancelled by Phase 8.6 — see the status line above)*
- **Phase 8.6** — the release automation ✅ *(2026-07-17, **added after the close** — it closes
  no DoD and **does not re-open the audit**: it succeeds it. See
  [design](../plans/2026-07-17-phase-8.6-design.md) ·
  [conclusion](../plans/2026-07-17-phase-8.6-conclusion.md) ·
  [addendum](../plans/2026-07-17-milestone-8-audit-addendum.md))*
   - **release-please authors the version** (`.release-please-manifest.json` decides; the props'
     `<Version>` is its first mirror and stays the build's only truth — **one author, six mirrors,
     seven literals, four files**), **the commit contract debuts at the PR title**, and **the seven
     milestone tags `v1.0`–`v7.0` are DELETED** — both tag lists are now empty, which is why
     `git checkout v6.0` fails (the ROADMAP's standing note is the place that explains it).
   - **THE HEADLINE — 8.2 wrote this phase's test in advance and the owner waived it.** Of 8.2's
     three reversal conditions (*all three must hold*), **two hold**; #2 — *releases frequent enough
     that hand-writing the body is a chore* — **does NOT: `gh api …/releases` → 0. Nobody has
     hand-written one, ever.** **The owner waives it. That is a VALUATION, not a discovery** — 8.2
     named this exact config (`draft: true`) and judged its value insufficient against a measurement
     still true today.
   - **What it actually buys is NOT the version:** at `1.0.0-preview.1`, `fix:`/`feat:`/`feat!:`
     **all** yield `1.0.0-preview.2` (`isPreMajor = major < 1`, so the reference's two `*-pre-major`
     flags are **dead no-ops** — at our major=1 **and at its own 1.3.3**). **The version is a
     counter; the commits compute the CHANGELOG.**
   - **P10 PROVEN — and with the key LIVE.** `NUGET_API_KEY` went live **mid-phase** (06:27:40Z), so
     the *"no key ⇒ no publish"* backstop vanished and **the guards became the only thing**. Gate 2
     **proved them** rather than trusting the key's absence: a draft fires **no** workflow
     (byte-identical runs; **0 release-triggered runs ever**), and **a draft does not even create the
     tag**. **DoD #3's law — nothing publishes without the owner's click — is now an OBSERVATION.**
   - **Two defects caught that would have shipped:** a **bare path** in `extra-files` let the file
     **extension** pick the updater → `.json` → strict `JSON.parse` → ***release-please would have
     crashed on every run, no release PR ever***; and **a line wrap declared a BREAKING CHANGE** —
     bodies are **not** discarded under squash (`COMMIT_MESSAGES`), and a release-as token at column
     0 of the last paragraph **sets the version outright**. Both pinned.
   - **Evidence:** .NET **582/0** (25 + 132 + 425) · JVM **106** untouched · device lanes
     **untouched** (184/154) · publish gates **unmoved** · `release-preflight -SelfTest` **9/9** (8
     RED arms, **0 SKIP** — `v8.0` inverted from announce-and-skip) · `footer-check -SelfTest`
     **14/14** · template-smoke **PASS** (31 files, APK 15590 KB) · `actionlint` clean on **7**
     workflows · required contexts read back exactly `["build-test","ios-build","android-build"]`
     (**commitlint's two are OWED, sequenced AFTER the merge — the file must be on main first or it
     wedges every PR**) · **0 releases, 0 release-triggered runs.**

Sequencing: 8.0 gates everything (the registration API is what 8.1 packages, 8.3
templates, and 8.4 documents); 8.1–8.2 make the packages real; 8.3–8.4 are the
outward-facing halves and can flex in order; 8.5 audits and tags. **8.6 was added after
the audit** — the owner asked for automation, and it **retired the very tag 8.5's line
planned.**

## Why this milestone exists

M1–M7 proved the thesis to *us*: one Blazor codebase, two native shells, identical
frames, an authoring story a Blazor developer recognizes. None of that is consumable by
anyone else while the demo pages live inside the library, the packages exist only in a
local feed, and the knowledge lives in 40 design docs. M8 is the milestone where the
project stops being a proof and starts being a product surface — deliberately after the
capability was real, per the 2026-07-13 re-plan.
