# BlazorNative — GitHub Project Setup Guide

This document explains how the GitHub project structure (labels, milestones,
issues, branch protection) is created and maintained — ready for community
contributions. Refreshed for the post-3.0e NativeAOT architecture at Phase 4.0.

---

## Prerequisites

**GitHub CLI** — install and authenticate:
```powershell
# Windows
winget install GitHub.cli
gh auth login
```

```bash
# macOS / Linux
brew install gh
gh auth login
```

---

## One-command setup

From the repo root, after pushing to GitHub:

```bash
bash scripts/create-github-issues.sh
```

This creates:
- **37 labels** (phase, type, contributor difficulty)
- **7 milestones** — M1–M3 created **closed** (their descriptions link the
  final-audit docs in `docs/plans/`), M4–M7 open with the ROADMAP one-liners
- **25 open-work issues** (see issue scope below)

---

## Flags

```bash
# Preview what would be created without actually creating anything
# (needs no remote, no auth — prints the full inventory and exits 0)
bash scripts/create-github-issues.sh --dry-run

# Only create labels and milestones, skip issues
bash scripts/create-github-issues.sh --labels-only

# Override the target repo (default: MarcelRoozekrans/BlazorNative)
bash scripts/create-github-issues.sh --repo=OWNER/REPO
```

---

## Label system

### Phase labels

Phase labels map to milestones (see `docs/planning/ROADMAP.md`):

| Label | Colour | Milestone |
|---|---|---|
| `phase/p0` | 🔴 Red | M1 — Runtime boots end-to-end *(complete)* |
| `phase/p1` | 🟠 Orange | M2 — First end-to-end demo on Android *(complete)* |
| `phase/p2` | 🟡 Amber | M3 — Real apps can be built *(complete)* |
| `phase/p3` | 🩷 Pink | M4 — Production-shippable *(complete)* |
| `phase/p4` | 🟨 Cream | M5 — Full platform coverage *(complete)* |
| `phase/p5` | 🟢 Light green | M6 — Real-UI foundation: layout + scroll + image *(in progress)* |
| `phase/p6` | 🔵 Light blue | M7 — Components + Razor |
| `phase/p7` | 🟣 Lavender | M8 — Developer ecosystem |
| `phase/future` | ⚪ Grey | Long-term vision |

The milestones after M5 were **renumbered when M6 opened**: real-UI capability
(layout/scroll/image) was promoted ahead of the developer ecosystem, which moved
from M6 to M8. The table above reflects the renumbering;
`docs/planning/ROADMAP.md` is the source of truth.

Two consequences, recorded rather than silently left to be discovered:

- **M9 (Platform breadth + real device) and M10 (Framework hardening) have no
  phase label yet** — the scheme stops at `phase/p7`. They get one when they
  approach.
- `scripts/create-github-issues.sh` still *creates* `phase/p5`–`phase/p7` with
  their **pre-renumber descriptions** ("M6 — Developer ecosystem", "M7 —
  Framework hardening", "M8 — Enterprise readiness"). The labels themselves are
  already created on the repo, so this only bites on a fresh bootstrap; the
  descriptions want a pass the next time that script is touched.

### Type labels
`type/core` · `type/android` · `type/ios` · `type/renderer` · `type/components` · `type/styling` · `type/navigation` · `type/state` · `type/http` · `type/analyzer` · `type/tooling` · `type/testing` · `type/ci` · `type/docs` · `type/security` · `type/a11y` · `type/i18n` · `type/perf` · `type/memory` · `type/ota` · `type/compliance` · `type/nativeaot` · `type/nuget`

(`type/wit` was retired with the 3.0e architecture collapse; `type/nativeaot`
covers the NativeAOT publish pipeline and C-ABI export surface.)

### Contributor labels
| Label | Meaning |
|---|---|
| `good-first-issue` | Well-scoped, good for newcomers |
| `help-wanted` | Extra attention needed from community |
| `expert-needed` | Requires deep expertise (NativeAOT, Kotlin/JNA, Blazor internals) |
| `spike` | Research / investigation required before implementation |
| `blocked` | Blocked on another issue |

---

## Issue scope: open work only

The script creates issues for **open work only** — no retro-issues for
completed M1–M3 work (those milestones are created closed, and their
descriptions link the audit record):

- **Remaining M4 phases** (4.1–4.5) — one issue each, `phase/p3`, milestone M4,
  body carries the ROADMAP phase line and links the MILESTONE.md DoD.
- **M4 DoD #4 runtime-hardening ledger** — one issue per ledger item
  (`type/core` / `type/perf`): async-handler capture window, dispatch-lane
  starvation, focus/blur wiring, stale-watcher re-attach, RemoveComponent
  bucket scan, TranslateToViewIndex memoization, RouteChanged subscriber
  isolation, allocation-budget test. Phase 4.2 triages each into
  fixed-with-tests or re-ledgered-with-rationale.
- **M5/M6 headline items** — one issue per headline item from the ROADMAP
  one-liners (`phase/p4` / `phase/p5` respectively).

M7/M8 work stays in `docs/BACKLOG.md` until those milestones approach —
this keeps the tracker focused rather than showing 170 open issues on day one.
Community members can request issues be opened for specific backlog items they
want to work on.

---

## GitHub Projects board (manual setup)

After running the script:

1. Go to the repo owner's profile → **Projects** → **New project**
2. Choose **Board** view
3. Name it `BlazorNative Roadmap`
4. Add columns: `Backlog` · `Up Next` · `In Progress` · `Done`
5. Filter by milestone to see each phase separately
6. Pin the board to the repo

---

## Recommended repo settings

### Branch protection (main)

Applied at Phase 4.0 Gate 5, **immediately after the public flip**, via `gh api`.
(Gate 4 deviation, discovered live: branch protection on a *private* repo
requires GitHub Pro on personal accounts — on the free plan it is available
only for public repos. Protection therefore lands right after the repo goes
public, before the phase closes.)

- Require PR before merging (no direct pushes to `main`, admins included)
- Required status checks — **all three jobs of `.github/workflows/ci.yml`**:
  - **`build-test`** (windows-latest) — build + analyzers, the .NET test suite,
    the three NativeAOT publishes with nine-export verification, JVM
    `testDebugUnitTest`, consumer smoke, and the `.so` artifact uploads (kept so
    a **human** can download and inspect a build's binaries — **no job consumes
    them**; see the note under the three bullets).

    > **Local IL2072 counts: publish from clean.** The publish gates assert
    > **exactly 4** IL2072 trim warnings — but an *incremental* local
    > re-publish can show **0**, because ILC skips trim analysis when its
    > inputs are unchanged since the last publish. That 0 is not drift and
    > not a fix; delete the publish `obj/bin` (or `git clean`) and re-publish
    > to see the real count. CI is unaffected — every run is a clean
    > checkout. (Phase 8.0 review M-2, recorded here.)
  - **`android-build`** (windows-latest, **no `needs:`**) — the **Android
    shell's compile**. It **self-publishes** `linux-bionic-x64` the proven way
    (windows-latest with the pinned NDK) and points the verify/copy chain at its
    own publish tree via `-PciSoDir` — it downloads nothing. Then `gradlew
    compileDebugAndroidTestKotlin`, type-checking `src/androidMain/kotlin`
    (MainActivity, WidgetMapper, YogaLayout) **and** the instrumented
    `androidTest` source set. No emulator is booted; no test is run.
  - **`ios-build`** (macos-latest) — the **iOS shell's compile**: publish
    `iossimulator-arm64`, build the pinned Yoga from source, then `xcodebuild
    build-for-testing` (Swift + Objective-C++ compiled, app **and** XCTest bundle
    linked). No simulator is booted; no test is run.
- Require conversation resolution before merging
- No force pushes

> **The three required jobs are INDEPENDENT — `ci.yml` declares no `needs:` edges
> at all.** They run in parallel, and each does its own checkout and its own
> publish. Nothing in `ci.yml` downloads an artifact: `build-test`'s `.so`
> uploads are there to be downloaded by a *person*, not by a job. The repo's one
> long-standing cross-job artifact hand-off is in `android-instrumented.yml`
> (`publish-so` → `emulator`, the **advisory nightly** lane); `release-please.yml`'s
> `validate` → `push` is the second. *(Phase 8.2 Gate 1 review M-7: this section
> previously described `android-build` as "ubuntu-latest, `needs: build-test`",
> compiling against a `.so` `build-test` uploads. It has never been any of those
> things, and the same false belief is what produced review finding I-3.)*

> **All three check names are exactly the job ids** — `build-test`,
> `android-build` and `ios-build` — because no job declares a `name:` and none is
> a matrix.

> **`release.yml` adds no fourth required check, by design (Phase 8.2).** Its
> `validate` job runs on PRs that touch the release machinery or bump
> `src/Directory.Build.props`, and one of the things it does is ask nuget.org
> whether the version is still free. Making that **required** would put
> nuget.org's availability on the critical path of every such PR — an outage
> would red a required gate on a change that has nothing to do with nuget.org.
> Same posture as the device lanes: the required set stays at three, and their
> names and contexts are a standing constraint.

Each native shell now has a distinctly-named required compile gate: a red
`android-build` names the Android shell, a red `ios-build` names the iOS shell.
The Android compile used to ride inside `build-test` (PR #81), where an Android
shell that stopped compiling reddened the same check as a .NET build error, a
.NET test failure or a JVM unit-test failure — you could not tell which shell
broke. It was split into `android-build` for that legibility (symmetric with
`ios-build`), and the gate is otherwise identical: *a device is needed to RUN the
instrumented tests, not to COMPILE them*.

#### Why `ios-build` is required and `ios` is not

The AGP 9 migration proved that **an assertion can be green and structurally
unreachable**: `android-instrumented` asserted 111 passing tests while
`src/androidMain/kotlin` — the entire Android shell — was not being compiled at
all, because that lane does not gate PRs. It landed on `main` green. PR #81 closed
it by compiling the instrumented sources inside `build-test`; the M6 audit then
split that gate into its own required job, **`android-build`**, so a red check
names the Android shell directly (see the required-checks list above).

The M6 final audit (finding F1) found the iOS shell in the same hole and deeper:
`build-test` runs on Windows and contains no Apple toolchain, so **no required
check compiled a single line of Swift or Objective-C++**, and `ios.yml` is
advisory. `ios-build` is the mirror of #81's fix — *a device is needed to RUN the
tests, not to COMPILE them* — and it is deliberately the **honest intersection**:
the part with no simulator flake modes, made required. Simulator **execution**
stays in `ios.yml` and stays advisory.

#### The two advisory lanes, and what promotes them

The instrumented-emulator workflow (`android-instrumented.yml`, nightly
03:00 UTC + manual dispatch) is **informational, not a required check** —
emulator-on-CI has known flake modes; it stays advisory until a stability
baseline exists (several consecutive green nightly runs), at which point it
can be promoted to a required check. Only the device **execution** is advisory:
the Android shell's **compile** is required, in `android-build` (above), exactly
as the iOS shell's compile is required in `ios-build` while its simulator
execution stays advisory in `ios.yml`. Shape: a `publish-so` job on
windows-latest publishes the linux-bionic-x64 `.so` (same pinned-NDK,
IL2072 and nine-export assertions as `ci.yml`) and hands it as an artifact
to an `emulator` job on ubuntu-latest (KVM), which runs
`connectedAndroidTest -PciSoDir=<artifact dir>` on an API 34 google_apis
x86_64 Pixel 6 image — mirroring the local AVD `blazornative-pixel6-x86_64`
— and asserts the count pinned in the workflow itself (see the provenance
block in `android-instrumented.yml` for the current bar and its history).

The iOS-simulator workflow (`ios.yml`, `macos-latest`, on `push` to `main` +
manual dispatch — **the `pull_request` trigger was removed in Phase 7.6** on an
owner cost request; PRs keep the REQUIRED `ios-build` compile gate, the phase
process dispatches this lane at every iOS gate, and every merge runs it on
`main`; the workflow header records the condition for restoring the PR trigger)
is likewise **informational, not a required check** — simulator-on-CI has flake
modes (sim boot, test-host launch), so it stays advisory. Promotion mirrors the
emulator lane: after a stability baseline (≈10 consecutive green runs on `main`
with no sim-flake reds) it can be promoted to a required check. Shape: a
**single** job (iOS both publishes and tests on macOS) publishes the
`iossimulator-arm64` NativeAOT **static** archive (the runtime-pack bypass +
`NativeLib=Static`; 4 IL2072 + nine-export `nm -gU` assertions), assembles the
static-embed link inputs (`bootstrapperdll.o` direct-link + the merged support
archive), then runs the hosted XCTest suite via `xcodebuild test` on a
runner-selected simulator — asserting the count pinned in `ios.yml`'s own
provenance block. The suite covers the render pin and the wire-drift guard; the
interactive demo (bind/echo, Clear, Theme, Settings⇄Back, clipboard); the Yoga
layer (style parsing, node lifecycle, dirty-on-change, resize); and — the point of
M6 — the **computed-frame assertions** for `BnLayoutDemo`, `BnScrollDemo` and
`BnImageDemo`, which pin *the same numbers* the Android instrumented lane asserts.
(That "same numbers" is itself pinned, in the required lane, by
`ShellFrameTableDriftTests` — see below.)

> **`ios.yml` had no `push` trigger and a `paths:` filter until the M6-audit fix.**
> It therefore never ran on `main` at all, and a PR that broke the Swift shell from
> a non-Apple path (`tests/**`, `scripts/**`, `Directory.Build.props`) did not even
> run the lane. Both are gone. Public-repo macOS minutes are free; the filter bought
> nothing but a blind spot with a green tick on it.

#### The cross-shell contracts live in the required lane

Neither shell's own lane can see the other's source (the Android lane has no Xcode;
the iOS lane has no Gradle). `build-test` can see **both, plus .NET** — it is the
only lane where all three languages are checkout-visible — so every cross-language
contract in this repo is pinned there, as a test that parses the shells' sources as
text:

- `ShellStyleTableDriftTests` — the Yoga style routing tables and the scroll
  ignore-lists (Kotlin ↔ `.mm` ↔ `NativeRenderer`).
- `ShellFrameTableDriftTests` — **the demo pages' canonical frame tables**. Both
  shells declare them in one machine-readable file each
  (`BnDemoFrameTables.kt` / `BnDemoFrameTables.swift`); this demands they be equal,
  number for number. "Identical frames on both platforms" is M6's entire
  architectural claim, and until the audit's finding F2 it was held by careful
  transcription rather than by an invariant.
- `BnImageDemoTests` — the image fixtures' natural pixel sizes, across .NET, the
  Kotlin fixture server and the Swift one.
- The Yoga version pin — asserted equal across `build.gradle.kts`, `ios.yml` and
  `ci.yml`'s own `YOGA_VERSION` (which is what `ios-build` compiles).

### Secrets — `NUGET_API_KEY`, the one secret

**This repository needs exactly one secret, and it is the only thing standing
between the packages and nuget.org.** Everything else the release does is
computed from the tree.

**Minting it** (nuget.org → your avatar → **API Keys** → **Create**):

| Field | Value | Why |
|---|---|---|
| Key name | `BlazorNative CI` | anything; it is for your own audit trail |
| Scopes | **Push new packages and package versions** only | not *Unlist*; the workflow never unlists |
| Glob pattern | `BlazorNative.*` | the key cannot touch a package outside this project even if it leaks |
| Expiration | 365 days (max) | put the renewal in a calendar; an expired key surfaces as a 401 on a Release |

**Where it goes:** repo → **Settings** → **Secrets and variables** → **Actions**
→ **New repository secret** → name it **exactly** `NUGET_API_KEY` (the workflow
reads that name and nothing else).

**The standing law, and it is a test rather than a promise:** the key is
referenced by **exactly one job in exactly one workflow** — `release-please.yml`'s
`push` job, guarded on `needs.release-please.outputs.release_created == 'true'`.
`ReleaseWorkflowPinTests` reds the **required** `build-test` lane if a second
reference ever appears anywhere under `.github/workflows/`. So this is a complete
answer — grep for the **expression**, not the bare name (`release-please.yml`'s
own comments discuss the secret by name; only the `${{ … }}` form can actually
read it, and only that form is what the pin counts):

```bash
grep -rF '${{ secrets.NUGET_API_KEY }}' .github/   # exactly one hit — that hit is the door
```

> **Scoping, and the residual we accept.** A repo-level secret is readable by
> any workflow run on a branch by an actor with write access. A GitHub
> *environment* with a branch restriction would narrow that — it is deliberately
> **not** used, because the owner's law is *one secret, and the release-PR merge is
> the go*, and an environment gate is a **second** manual approval on an action
> that is already a deliberate merge. The mitigations that cost nothing are taken:
> one reference, one job, `if:`-guarded on `release_created`, pinned by a test.

---

### GitHub Pages — enabling the docs site

**This repository needs exactly one click, and it is the only thing standing
between `website/` and a live docs site.** There is no secret, no token, no
branch, and nothing to mint: `docs.yml` authenticates to Pages with an OIDC
`id-token` the workflow already requests. The `github-pages` environment is
created **automatically** on the first deploy.

**Where it goes:** repo → **Settings** → **Pages** → **Build and deployment** →
**Source** → select **`GitHub Actions`**.

That is the whole list. Verify it took:

```bash
gh api repos/MarcelRoozekrans/BlazorNative/pages --jq .build_type   # → workflow
```

Before the click that endpoint is a **404** (`has_pages: false`), which is itself
the check — a 404 means the setting has not been made. The target state is what
`AdoNet.Async` already shows: `build_type: "workflow"`.

**What happens after.** `docs.yml` `build` → `deploy` runs on every push to
`main`, and the site lands at
**<https://marcelroozekrans.github.io/BlazorNative/>**.

**Its paths filter is deliberately wider than `website/**`, and that is the part
to understand rather than to memorize.** The component reference is *generated*
from `src/BlazorNative.Components`' XML doc comments at build time — so a PR that
improves a `<summary>`, or that changes whether those doc comments exist at all,
changes the site without touching `website/`. Under a `website/**`-only filter
main would take that commit and never redeploy, serving a stale reference with
nothing red anywhere, for exactly as long as nobody looked. So the filter names
every input the reference is a function of: the components, the two props/targets
files that decide whether the XML is generated and whether CS1591 is an error,
the generator script, its tool manifest, and the workflow itself.

**The executable truth is `.github/workflows/docs.yml`, job `build` — read the
`paths:` lists there rather than a copy here.** There are two of them (the `push`
filter and the `pull_request` filter): GitHub Actions supports no YAML anchors, so
they are duplicated by construction and must be edited together. A transcription
on this page would be a third copy that rots the day either list moves.

`docs.yml` also builds on `pull_request` (a broken docs build is caught before it
lands), with `deploy` gated off `pull_request` so a PR never touches Pages, never
takes the `pages` concurrency group, and never needs `id-token`. It is
**advisory** — it is not a required check, and `build-test` / `android-build` /
`ios-build` keep their names and contexts.

**Until the click, `deploy` fails** — loudly, on `main`, with a legible Pages
error. Everything already re-pointed at the site 404s in that interval: the six
packages' READMEs, `PackageProjectUrl`, and the analyzers' `helpLinkUri` values.
That is bounded and it is strictly better than the alternative — nothing
publishes without your Release (so no consumer can reach those URLs first), and
the old targets are a 404 *forever*.

**The one link that argument does not cover is the root `README.md`'s
Documentation link**, because this repo is public: it 404s for anyone browsing
GitHub between the merge and the click. That is the honest cost of shipping the
site's inbound link with the site, and it is the shortest-lived item on this
page — but it is the reason to click sooner rather than later.

> ⚠ **The first check after clicking is a LOOK, not a red.** This is the quiet
> arrow, and it is the one thing on this page that CI cannot catch for you. The
> site's `baseUrl` (`'/BlazorNative/'` in `website/docusaurus.config.js`) is only
> correct relative to where Pages actually serves it. **A wrong `baseUrl` builds
> green, deploys green, and reports success** — and produces a page with dead CSS
> and 404 links. No local build and no workflow can see it; only a real fetch of
> the real URL can. So after the first deploy goes green, **open
> <https://marcelroozekrans.github.io/BlazorNative/> and confirm it has
> styling.** Green is not the evidence here; your eyes are.

---

### Publishing a release (auto-publish on merge)

**Merging the release PR is the go, and it is the only one.** That single merge
cuts the tag, publishes the GitHub Release, AND pushes the packages to nuget.org
— all in one `release-please.yml` workflow run. An ordinary merge that is *not* a
release-PR merge publishes nothing; a bare tag publishes nothing.

**Phase 8.6 automated everything up to a manual Publish click; the Phase 9.x
re-cut removed that last click too — "auto release of everything".** `release-please`
computes the version, writes `CHANGELOG.md`, and opens a release PR. When you
merge it, release-please cuts the tag and Release, and — in the **same run** — its
`validate` job packs and proves the packages with no key present, then its `push`
job (the one place `NUGET_API_KEY` lives) pushes them to nuget.org.

> **Why the publish had to move into `release-please.yml`.** It used to live in
> `release.yml`, gated on `release: types: [published]`. That never fired:
> release-please cuts the Release with `GITHUB_TOKEN`, and **GitHub does not fire
> workflow triggers for `GITHUB_TOKEN`-created events** (anti-recursion). So the
> Release and tag appeared, `release.yml` never ran, and **nothing ever reached
> nuget.org** — proven live, `v0.1.0` existed while every package 404'd. A human
> clicking Publish used to supply that event (a click is not a `GITHUB_TOKEN`
> action); removing the human removed the trigger. Publishing in the same run,
> gated on release-please's own `release_created` output, needs no second event.
> `release.yml` remains, keyless, as the PR-time validation lane.

**The tag namespace — this is the part to read before you need it:**

| Tag | Means | Publishes? |
|---|---|---|
| `v<semver>` — `v0.1.0` | **package release**, cut by release-please | **the only shape that does** |
| `v<N>.<M>` — `v1.0` … `v8.0` | **legacy milestone** — the namespace was **retired in Phase 8.6** | **never — it is now a RED** |
| `pkg/<semver>` — `pkg/0.1.0` | **legacy package** — 8.2's namespace, **retired in Phase 8.6** | **never — it is now a RED** |

**The two namespaces can never collide, and the reason is arithmetic rather than
convention:** `v<N>.<M>` has two components and **two components is not valid
SemVer**. So `v8.0` can never be a valid `v<semver>` tag — while `v8.0.0` can,
and *should* publish 8.0.0 if this repo ever gets there.

> **⚠ `v8.0` REDS NOW. It used to skip-and-say-so, and that changed on purpose.**
>
> Under Phase 8.2 a Release on `v8.0` announced a milestone and exited green, on
> one stated premise: *"a milestone Release is a legitimate thing the owner may
> do at M8's close."* **Phase 8.6 deleted that premise** — the milestone-tag
> namespace is **retired**, `v1.0`–`v7.0` **were deleted** on **2026-07-17**
> (the last step of 8.6's close; `git ls-remote --tags origin` is now empty, so
> **`git checkout v6.0` fails in a fresh clone** — `docs/planning/ROADMAP.md`'s
> standing note is the place that explains why), and `v8.0` was
> **cancelled** (M8 DoD #6's tag named a ritual, not a result — see
> `docs/plans/2026-07-17-milestone-8-audit-addendum.md`). **No `v<N>.<M>` will
> ever be cut again.** A Release on `v8.0` is no longer a legitimate action;
> **it is a mistake, and a green "nothing was published" would tell you all is
> well after you did something meaningless.**
>
> **The RED never waited for the deletion**, and that is why nothing here
> changed when the tags went. The namespace was retired by *decision*, and the
> classifier reds on the tag's **shape** — so `v8.0` red while all seven tags
> were live, exactly as it does now they are gone. **Retiring a namespace and
> emptying it are two different acts**, and only the first one ever had to have
> happened for this box to be true.

**The ritual — release-please's. You author nothing; the machine writes the
version, the tag and the changelog, and the merge does the rest:**

> Every step below is a **review or a go** — you *merge* work, you *approve* the
> release PR's checks, you *read* the changelog, and you *merge* the release PR.
> That final merge is the whole publish: tag, Release, and nuget.org, in one run.
> There is no draft to publish and no Publish click any more — removing it is the
> "auto release of everything" decision.

1. **Merge PRs with conventional titles.** The PR title becomes main's commit
   subject, and it is the only text release-please ever reads. See *The commit
   contract* below.
2. **release-please opens a release PR** — it bumps the version, writes the
   changelog, and updates **all seven version literals**. Read it; the changelog
   in it is the changelog your consumers get.
3. **Click "Approve workflows to run" on that PR.** **Its checks do not start by
   themselves, and this will look like a stuck PR if you are not expecting it.**
   release-please opens the PR with `GITHUB_TOKEN`, and when a workflow using
   `GITHUB_TOKEN` opens or updates a PR, GitHub creates the resulting runs in an
   **approval-required** state — a banner, and a click by someone with write
   access. **Until you click, `build-test` / `android-build` / `ios-build` have
   not run**, which also means the pin that audits release-please itself
   (`TheManifest_AgreesWithTheProps` — it reds if the manifest bumped and the
   props did not) **has not run either.** The click starts them; they then red a
   bad release PR *before* you can merge it.
   > **If you never click, nothing breaks — it stalls.** A required check that
   > never ran cannot be satisfied, so the PR cannot merge: no tag, no Release, no
   > publish. **The failure direction is safe.** This is the deliberate price of
   > keeping *one* secret in this repo; a PAT would start the checks
   > automatically and was rejected for being a second secret that expires.
4. **Merge the release PR** once it is green. **That merge is the go.** In the
   same `release-please.yml` run it cuts the tag, publishes the Release, and then
   — gated on `release_created` — `validate` packs and proves the seven packages
   with **no key in the run at all**, and only then does `push` see the secret and
   send them to nuget.org. Watch that run: the check is *"did `push` run?"*, since
   the one quiet failure mode (U8 below) skips `push` rather than reddening it.

**Four things that will otherwise surprise you:**

- **The commit type now MOVES the version — that is new in Phase 8.7.** Under the
  old `1.0.0-preview.N` scheme the version was a *counter*: `fix:`, `feat:` and
  `feat!:` all produced `preview.N+1` and the type changed nothing. **Dropping the
  preview suffix for 0.x turned inference back on.** Measured against
  release-please 17.10.3's own `DefaultVersioningStrategy`, from `0.1.0`:

  | commit | next version |
  |---|---|
  | `fix:` | **0.1.1** |
  | `feat:` | **0.2.0** |
  | `feat!:` | **0.2.0** — *the same as `feat:`* |

  **`feat!:` and `feat:` agree at 0.x, and that is deliberate.** `bump-minor-pre-major`
  keeps a breaking change on the minor while `major < 1` — semver's own rule for
  pre-1.0 — which is what stops a stray `feat!:` from silently shipping `1.0.0`.
  **A mis-typed subject is still cheap** (a patch where a minor belonged, or the
  reverse) and it **can never reach 1.0.0**. What *can* set the version outright is
  a `release-as` footer in a commit **body** — that is `footer-check`'s subject.
- **The tag is a claim; the props wins.** The workflow *asserts* that
  `v0.1.0` matches the props and **never overrides it**. Tag ahead
  of props, props ahead of tag, tag on the wrong commit — all three are RED,
  naming both values. (Overriding via `-p:Version=` would make the packages
  irreproducible from the commit they name; it is banned and pinned.)
- **Recovery from a partial push is an Actions re-run.** Three packages up and
  three failed is recoverable: **re-run the `push` job from the Actions UI** —
  `--skip-duplicate` makes the three that landed no-ops. (Re-running the whole
  `release-please.yml` run works too, but re-running just `push` is enough, and
  it re-uses the feed `validate` already uploaded.)
- **A published version can never be replaced.** nuget.org has **no hard
  delete** — only *unlist*. If a wrong version publishes, the recovery is
  unlist → bump → release again. It is never "fix it and re-push the same
  version". That single fact is why every check runs before the key.

#### ⚠ Graduating to `1.0.0` — and the danger now runs the OTHER way

**Phase 8.7 deleted the trap that used to live here.** Under the old
`1.0.0-preview.N` scheme, reaching `1.0.0` took a `Release-As:` commit **and** a
config change, and stopping at the commit silently re-entered preview. **That
scheme is gone, and its trap went with it.** Graduating is now one move:

| # | The move | Why that is all |
|---|---|---|
| 1 | A commit whose **body** carries a release-as footer naming `1.0.0` | `bump-minor-pre-major` / `bump-patch-for-minor-pre-major` are gated on `major < 1`. **At `1.0.0` they deactivate themselves.** There is nothing to remember and nothing to delete. |

**What changes at graduation is BEHAVIOUR, not config** — and this is the part to
know before you do it. Measured against release-please 17.10.3's own strategy:

```
        0.x (today)              1.0.0 and after
fix:    0.1.0 -> 0.1.1           1.0.0 -> 1.0.1
feat:   0.1.0 -> 0.2.0           1.0.0 -> 1.1.0
feat!:  0.1.0 -> 0.2.0    <--    1.0.0 -> 2.0.0    <-- THIS ONE MOVES
```

**`feat!:` is a minor at 0.x and a major at 1.x.** The day you graduate, the same
commit type starts costing a major version. Nothing warns; it is simply true.

> ### ⚠ **THE REAL HAZARD IS AN ACCIDENTAL GRADUATION, AND IT IS PERMANENT**
>
> **`bump-minor-pre-major: true` is the only thing keeping a `feat!:` off `1.0.0`.**
> Set it to `false` — which is exactly what *"mirror AdoNet.Async"* invites, because
> **the reference really does set it, and this repo mirrors that reference by
> habit** — and the **very next `feat!:` computes `1.0.0`**. Not a proposal you get
> to reconsider: a release PR, and then a version.
>
> **nuget.org has NO hard delete.** An accidental `1.0.0` is permanent — you cannot
> go back to `0.x`, only forward. **The recovery is unlist and keep walking.**
>
> **Nothing catches it.** The manifest and the props would *agree* on `1.0.0`, so
> `TheManifest_AgreesWithTheProps` is happy. The tag and the props would agree, so
> the classifier is happy. **Every pin in this repo stays green while the project
> graduates itself.** This box is the guard, which is why it is here — where the
> person editing that config is standing — and not in a design doc nobody opens.

**The same is true of the footer that does it on purpose.** A release-as footer at
the start of a line in the **last paragraph** of a commit body sets the version
outright — release-please reads it **before** it counts any commit types. It is
**case-insensitive** (measured: lowercase works), so it is easier to write by
accident than it looks. `scripts/footer-check.ps1` is the guard: your body may only
declare that footer if your **PR title names the same version**.

#### What the first Release is actually testing

Honesty about the machinery, because **the release mechanism is the one thing in
this repo that cannot be tested by using it** — there is no key, no throwaway
registry, and no publish until you publish. Everything provable is proven on
every PR; what remains is listed here rather than implied. **Every arrow below
fails in the safe direction — nothing publishes — and announces itself to you,
standing there having just merged the release PR.**

| # | Unproven until your first Release | If it is wrong, you see |
|---|---|---|
| U1 | release-please's `release_created` output actually gates the publish jobs to fire on a real release-PR merge | **Nothing happens** — the `validate` / `push` jobs skip. Safe: it cannot mis-publish. (`actionlint` proves the `if:`/`needs` expression chain is real; that the merge run actually emits `release_created == 'true'` and starts the jobs is what the first release proves.) |
| U2 | `NUGET_API_KEY` resolves — present and correctly named | A named RED: *"NUGET_API_KEY is unset or misnamed — see docs/GITHUB-SETUP.md"*, instead of a bare 401 from a CLI |
| U3 | nuget.org **accepts** these nupkgs — no reserved-prefix 403 | A 403 or an async validation failure on first push. All six ids are unregistered today, so nothing is reserved — but a reserved-prefix answer cannot be obtained without a key |
| U4 | The adjacent `.snupkg` reaches the symbol server | Packages publish, symbols silently do not. **Recoverable** — symbols can be pushed later |
| U5 | `--skip-duplicate` no-ops a real 409 | A re-run reds instead of skipping. Recoverable by hand |
| U6 | Six pushes across a minutes-wide async indexing window behave | A consumer restoring inside the window sees an unindexed dependency. **Self-healing** |
| U7 | The **artifact hand-off** from `validate` to `push` carries the packed feed | The upload is `release_created`-gated, so **no PR can exercise it** — only a real release-PR merge does. A loud RED: *"ZERO .nupkg found — the artifact hand-off from validate is broken"*. That guard exists so this fails **loudly** instead of pushing nothing and going green. Safe: nothing publishes. (Same actions and versions as `publish-so → emulator` in the nightly `android-instrumented.yml`, which runs this shape every night.) |
| U8 | `needs.validate.outputs.verdict` **reaches the `push` job** | This is the repo's **first** `needs.<job>.outputs` consumer — no other lane uses one. If it is wrong, the value is empty, the `if:` is false, and **`push` skips silently** — safe (nothing publishes) but **quiet**: you would see a *skipped* job, not a red one. So check that `push` actually ran. `actionlint` statically rules out every **typo** shape *in this chain* (job-output names, step ids, `needs.<job>.outputs.<name>`, and `needs:` itself — all mutant-tested), so "is it spelled right" is settled; what a Release proves is that the runtime does what the syntax says |

**The blast radius of the first firing is one unlistable preview of a package
nobody depends on yet** — which is the cheapest possible way to learn all eight of
these at once, and it is cheap *on purpose*.

> **All eight fail in the safe direction — nothing publishes — and seven of them
> say so loudly. U8 is the one quiet failure**: an empty `release_created` skips
> `push` rather than reddening it. So when you merge the first release PR, the
> check is **"did `push` actually run?"**, not just "was there a red?". *(U7 and U8 were
> added by the Phase 8.2 Gate 1 review, finding I-2: both are arrows no PR can
> exercise, and a table that omitted them was claiming more coverage than the
> lane has.)*

---

### PR-merge workflow (from Phase 4.1 onward)

Once branch protection is live, phases merge **via PR** instead of a local
`merge --no-ff`:

1. Work happens on a phase branch (`phase-N.N-description`), same as before.
2. `gh pr create` targeting `main`; the phase's review checkpoints happen
   against the PR.
3. CI (`ci / build-test` **and** `ci / ios-build`) must be green and
   conversations resolved.
4. Merge on GitHub (`gh pr merge --merge` — keep the merge commit; the
   per-phase merge points remain the project's history spine).

Same review rhythm as M1–M3 — the merge just happens on GitHub with CI as the
gate.

### Issue templates

Live in `.github/ISSUE_TEMPLATE/`:
- `bug_report.md` — for bugs
- `feature_request.md` — for new features outside the roadmap
- `platform_api.md` — for new platform API proposals (camera, GPS, etc.),
  framed around the host-registered C-ABI bridge

### PR template

Lives at `.github/pull_request_template.md`. The platform checklist matches the
surfaces every change must hold on — the iOS row joined in M5 once the Swift
shell landed (render in Phase 5.2, interactivity in Phase 5.3):

```markdown
## Platform tested
- [ ] JVM dev loop (`testDebugUnitTest`)
- [ ] Android emulator (`connectedAndroidTest`)
- [ ] iOS simulator (`ios.yml` — `xcodebuild test`)
```

---

## Attracting contributors

Once the repo is public with the issue structure:

1. **Write a good README** — the current `README.md` reflects the NativeAOT
   architecture; keep the badge and status banner current
2. **Add to awesome lists:**
   - [awesome-blazor](https://github.com/AdrienTorris/awesome-blazor)
   - [awesome-dotnet](https://github.com/quozd/awesome-dotnet)
3. **Post on:**
   - r/dotnet, r/Blazor, r/androiddev
   - Hacker News (Show HN — the two-page native demo app is the hook)
   - .NET Foundation Discord
   - Blazor Discord
4. **Tag issues with `good-first-issue`** — GitHub surfaces these to new contributors automatically
5. **Respond fast** — first-time contributors drop off if they don't hear back within 48 hours
