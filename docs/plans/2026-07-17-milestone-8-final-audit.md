# Milestone 8 — Final Audit (Phase 8.5)

**Date:** 2026-07-17
**Auditor:** Phase 8.5, against `phase-8.5-close` (the `v8.0` candidate tree — the tag is applied
AFTER this PR merges, on the owner's go)
**Predecessor:** [M7 final audit](2026-07-16-milestone-7-final-audit.md) (PASS on all eight, tagged
`v7.0`)
**Contract audited:** [`docs/planning/MILESTONE.md`](../planning/MILESTONE.md) — the six M8 DoD
criteria

## Method

The M7 audit's method, verbatim, because it is the house standard: **"asserted, not observed"**, and
**"a mechanism nobody tested is a mechanism nobody knows."** Applied to the audit itself — **no
criterion is accepted because a conclusion doc says so.** Every verdict below is checked against git
history, the live GitHub API, actual CI run logs, and the code — and everything locally runnable was
**re-run live at the audited tip** rather than cited:

- **.NET suite, re-run live:** `dotnet test` (Release) — **580 passed / 0 skipped / 0 failed**
  (Renderer.Tests 132 + Analyzers.Tests 25 + Runtime.Tests 423). The tree arrived at **577**; this
  phase's own hygiene work added **+3** (below).
- **JVM suite, re-run live:** `gradlew testDebugUnitTest --rerun-tasks` — **106 passed / 0 failed /
  0 errors / 0 skipped across 19 suites** (summed from the JUnit XML, the ci.yml method).
- **Publish gate, re-run live** (clean native obj, full ILC pass): win-x64 shows **exactly 4
  IL2072**; `dumpbin /exports` (via vswhere) shows **exactly the 9 `blazornative_*` exports**; the
  **page-presence probe** finds `BnDemo`, `BnImagePolishDemo`, `BnFormDemo` in the native image
  (DLL **4217 KB** — 8.3's recorded figure, unmoved).
- **Consumer smoke, re-run live:** 6 nupkg + 5 snupkg at `1.0.0-preview.1`, zero pack warnings,
  purity + nuspec + inventory clean ×6, BN0011 trip, ConsumerSmoke PASS.
- **Reference generation, re-run live:** `Generation: 26 succeeded, 0 failed` → 27 files, components
  present.
- **`release-preflight.ps1 -SelfTest`, re-run live:** 8/8 rows green, exit 0 — including the row
  that matters this week.
- **Branch protection, read back live:** `required_status_checks.contexts == ["build-test",
  "ios-build", "android-build"]` (strict: true, enforce_admins: false — the M6-recorded residual,
  unchanged).
- **Every referenced run verified via `gh run view`** — existence AND conclusion, never trusted from
  a doc.

**Verdict: PASS on all six.** Findings that do not block are recorded as findings (the M6
discipline), never rounded up. **Two of the DoD's own texts do not describe what shipped, and one
ledger entry this audit inherited is off by ~150×** — all three are stated below rather than
smoothed over, because the audit is where that gets said out loud.

---

## Verdict table

| # | Criterion | Verdict | Evidence (re-verified live) |
|---|---|---|---|
| 1 | The demo app is a consumer, not a tenant | **PASS** | [8.0 conclusion](2026-07-16-phase-8.0-conclusion.md). Re-verified at the tip: **Runtime's Components `ProjectReference` is GONE** (`BlazorNative.Runtime.csproj:55` carries the deletion and its reason; the remaining three are Core/Renderer/Http + the Analyzers rider). The nine demo pages + probes live in **`samples/BlazorNative.SampleApp/*.razor`** — grep-verified. **Purity is CI-asserted, not asserted by grep:** `PackagePurityTests` holds the roster both directions, the pattern net (`(Demo$)\|(Probe$)\|(^SpikeRazor)`) and the **six-assembly shipped-set literal** — all in the live 580, in the required lane; the **nupkg-level** scanner re-interrogates every packed PE in the smoke (six packages, "types clean" ×6, each with a real sentinel — 8.1's two-arm positive control against the vacuous pass). Both shells embed the sample's publish: `build.gradle.kts`'s `winX64PublishPath` and `runtimePubRoot` point at `samples/BlazorNative.SampleApp/...` — **proven load-bearing by accident** (see F4). The named trim risk stays closed: the **page probe** is green on the live publish. |
| 2 | Publish-ready packages | **PASS** | [8.1 conclusion](2026-07-16-phase-8.1-conclusion.md). Smoke re-run live at the tip: **six** packages packed at **one version literal** (`src/Directory.Build.props` → `1.0.0-preview.1`; filenames agree), **zero pack warnings**, symbols **PAIRED** (5 snupkg; Analyzers embedded, no snupkg — correctly), **SourceLink stamped `repository@commit 6afd8af4…`** (the tip), MIT + readme in every nuspec, inventory shape ✓×6, restore from the local feed only with provenance ×6, **BN0011 tripped live**, ConsumerSmoke **PASS** — the 8.0 API's first out-of-repo consumer. **The DoD says five packages; SIX ship** — honesty row below. |
| 3 | The release pipeline, manual go | **PASS** | [8.2 conclusion](2026-07-16-phase-8.2-conclusion.md). **The row that matters this week, re-run live:** `release-preflight.ps1 -SelfTest` → **8/8 green, exit 0**, and its `v8.0` row reads **`SKIP (milestone) — M8's own close tag — the hazard this milestone creates; it must publish NOTHING`**. The audit itself precedes a `v8.0` tag; the classifier announces and skips. Nothing publishes except from a published Release: `release.yml`'s `push` job is `needs: validate` + `if: event_name == 'release'`, is the **sole** `NUGET_API_KEY` reference, and has **no checkout at all**. Verified live: the `release` workflow ran on `push` at the tip (**29554540199**'s sibling, success) and **published nothing** — there is no Release. **The DoD says "a dry-run validation lane"; `dotnet nuget push` has no `--dry-run`** — honesty row below. |
| 4 | `dotnet new blazornative` | **PASS** | [8.3 conclusion](2026-07-17-phase-8.3-conclusion.md). The proof is **generated output**, and it runs **inside the required lane** (`ci.yml` → `build-test` → `template-smoke.ps1`), so it gates this PR too. Verified live in run **29554045138** (`ci / build-test`, `pull_request`, success): template packed → **installed from the nupkg** → `dotnet new blazornative -n TemplateSmokeApp` (31 files, substitution fired) → restore (provenance ×3) → publish → **`[arm 1] IL2072 count: 4 ✓`**, **`[arm 2] Exports found (9) ✓`** (canonicalized from `TemplateSmokeApp.dll`), **`[arm 3] Page-presence probe 'BnStarterPage': present ✓ (3840 KB)`** → **`assembleDebug` BUILD SUCCESSFUL → APK 15590 KB**. Yoga parity in the same run: **`android=3.2.1 ios=3.2.1 ci=3.2.1 template=3.2.1`**. |
| 5 | The docs site | **PASS** | [8.4 conclusion](2026-07-17-phase-8.4-conclusion.md). Re-run live: **`Generation: 26 succeeded, 0 failed` → 27 markdown files**, and the components **are there** (`bnview`, `bntext`, `bnbutton`, `bnlist-1`, `bnmodal`, `bnimage`, …) — the 10-vs-26 defect stays closed, and only a count can see it. `docs.yml` builds on PRs (run **29554045167**, `pull_request`, **success**) and deploys on main. **⚠ The site is UNRENDERED until the owner clicks Settings → Pages → Source: GitHub Actions.** This is not a prediction — it is **observed**: `gh api …/pages` → **404**, and the `Deploy Documentation` run at main's tip (**29554540214**) is **`build: success` / `deploy: failure`** with exactly the legible error 8.4 promised: *"Failed to create deployment (status: 404) … Ensure GitHub Pages has been enabled"*. **U1 fails LOUD, as designed; U2 stays the quiet arrow.** **The DoD says "~20 components"** — honesty row below. |
| 6 | Hygiene + close | **PASS** | This phase. **(a) Every new surface CI-asserted with provenance:** the four bars match the observed lanes — `ci.yml` asserts **580** (.NET) and **106** (JVM), `android-instrumented.yml` **184**, `ios.yml` **154**; each count change carries a provenance block (this phase's 577 → 580 included). Branch protection read back live == exactly the three required contexts. **(b) The decision log:** 8.0–8.4 each with a design + conclusion (10 docs), ROADMAP.md's phase table current, **plus this audit**. **(c) The hygiene ledger:** four items decided, one **done** (the README's counts + Yoga literal, now gate-held by `ReadmeDriftTests`, five mutations run), three deferred **with reasons and triggers** — below. **(d) Tag criteria:** stated mechanically below. |

---

## The four counts — with the evidence that proves them

| Surface | Baseline | Asserted in | Proven by | Notes |
|---|---|---|---|---|
| .NET (xUnit, 3 projects) | **580** passed / 0 skipped | `ci.yml` → **build-test (required)** | **Re-run live:** 132 + 25 + 423 = 580/0/0 | **577 → 580 (+3)** this phase — `ReadmeDriftTests`, with provenance in `ci.yml`. The required-lane run for this tree is this PR's own gating run (`ci.yml` has no branch-push trigger) — F1 below, the M7 audit's F1 verbatim. |
| JVM (Gradle `testDebugUnitTest`) | **106** passed / 0 failed | `ci.yml` → **build-test (required)** | **Re-run live** with `--rerun-tasks` (27 tasks executed, not cached): tests=106 failures=0 errors=0 skipped=0 across 19 suites | Unmoved since 7.6. See F4 — the first run of this suite in the audit was **red by the auditor's own hand**, and what it proved is worth keeping. |
| Android instrumented | **184** passed / 0 failed | `android-instrumented.yml` (nightly/dispatch — advisory) | **8.0's local-AVD run**, recorded in the workflow's provenance | **⚠ No hosted run exists on ANY M8 commit** — the newest is **29474510761** at `371725f`, which `git merge-base` confirms **predates `v7.0`** (a Renovate PR). The lane never runs on PRs (by design since 4.0) and today's nightly had not fired at audit time (cron 03:00 UTC, historically ~2.5 h late; audit ran 04:26 UTC). **F2 below.** |
| iOS XCTest (simulator) | **154** passed / 0 failed | `ios.yml` (dispatch + push-main — advisory) | Run **29554540199** (`push`, **success**, verified live) **at `6afd8af` — main's exact tip and this branch's base**: `XCTest cases: passed=154 failed=0`, `IL2072 count: 4`, `SYMBOLS: all 9 blazornative_* present.` | **Better provenance than M7's audit had** (whose iOS run trailed the tip by two commits). This one is *at* the tip. |

### The ABI did not move — verified independently, and the brief's shorthand corrected

- **9 exports:** dumpbin (via vswhere) on the live win-x64 publish — exactly the nine
  (`dispatch_event, fetch_complete, host_event, init, mount, register_bridge,
  register_frame_callback, shutdown, version`). iOS: `SYMBOLS: all 9 blazornative_* present.` in run
  29554540199. The bionic RIDs are re-verified by `build-test`/`android-build` on this PR.
- **4 accepted IL2072s** on the live win-x64 publish AND on run 29554540199's iossimulator-arm64
  publish AND on the **generated** app (template smoke, run 29554045138). The count has not moved
  since Phase 3.0a — through the samples split, whose `Mount<T>` lambdas are the trim roots.
- **The 72-byte / 9-callback bridge struct — all three mirrors verified at the tip:**
  `BridgeProtocolNativeTests.cs:28` (`Assert.Equal(72, Marshal.SizeOf<BlazorNativeBridgeCallbacks>())`,
  in the live 580), `ShellBridgeTest.kt:48` (`callbacks_struct_is_72_bytes`, in the live 106 →
  **required lane**), `BnDriftTests.swift:83` (`MemoryLayout<bn_bridge_callbacks>.size == 72`, in run
  29554540199's 154).
- **NodeTypes stay THIRTEEN, three mirrors:** `FrameEncoderTests`' all-NodeTypes theory (`[InlineData]`
  rows through `("activityindicator", BlazorNativeNodeType.ActivityIndicator)`, .NET);
  `NativeFrameAdapterTest.kt:67`'s `nodeTypes_vocabulary_is_pinned_content_and_length` (JVM);
  `BnDriftTests.swift`'s thirteen-entry literal `["?", "view", … "activityindicator"]` + the 65 536
  patch ceiling (iOS). **M8 added no vocabulary at all.**
- **Yoga 3.2.1, FOUR files** (8.3 added the template): `build.gradle.kts:57`, `ios.yml:79`,
  `ci.yml:59`, `templates/…/android/build.gradle.kts:57` — grep-verified at the tip, and the
  required lane's parity step printed all four equal on run 29554045138. **A fifth copy in
  `README.md` was unheld and is now gate-held** (below).

**The brief for this phase said "M8 touched no wire/ABI/golden/shell code — prove it with a diff
over `src/BlazorNative.Jni`/`src/BlazorNative.Apple` from `v7.0`". The diff is NOT empty, and the
precise truth is better than the shorthand:**

```
git diff --stat v7.0..HEAD -- src/BlazorNative.Jni src/BlazorNative.Apple
 src/BlazorNative.Apple/project.yml    | 12 +++++++++---
 src/BlazorNative.Jni/build.gradle.kts |  8 ++++----

git diff --name-status v7.0..HEAD -- 'src/BlazorNative.Jni/**/*.kt' \
    'src/BlazorNative.Apple/**/*.swift' 'src/BlazorNative.Apple/**/*.h' 'src/BlazorNative.Apple/**/*.mm'
 (empty)
```

- **Zero shell SOURCE changed** — not one `.kt`, `.swift`, `.h`, `.mm` under `src/`. That is the
  claim the device baselines actually rest on, and it holds.
- **Two shell BUILD files changed.** `project.yml` is **comment-only** (8.4's sweep, correcting prose
  that said "Pinned to 7.x" while Renovate had moved Kingfisher to 8 — *the file stated 7 and meant
  8*). `build.gradle.kts` is **8.0's publish-head retarget** (`src/BlazorNative.Runtime/…` →
  `samples/BlazorNative.SampleApp/…`, plus the matching error strings) — functional, but a **fixture
  path**, not wire or ABI.
- **Therefore the device baselines stand on 8.0's provenance, not v7.0's** — which is exactly why
  **8.0 re-ran both lanes** (184 local AVD + iOS 154, run 29527121729) as the phase that moved the
  head. Phases 8.1–8.4 touched neither file. **The 14 changed `.kt` files since `v7.0` are all
  `templates/…` additions** (8.3's template-owned copies), not the repo's shell.

---

## M8's honesty rows — where the DoD's words and the shipped thing disagree

**These are not defects. They are places the contract's wording was written before the work knew
better, and the audit is where that is said out loud rather than quietly reconciled.**

### 1. DoD #2 named FIVE packages; **SIX** ship — and a **SEVENTH** publishable pack exists

`BlazorNative.Http` has packed and shipped through the consumer smoke **since 4.5**, and
`BlazorNative.Runtime` `ProjectReference`s it. The five-name list was **shorthand drift, not a
scoping decision** — corrected in 8.1, and now structurally pinned: `PackagePurityTests`'
`ShippedAssemblies` literal names **six**, mutation-guarded, and the smoke interrogates **six**
nupkgs. Verified live above.

**The seventh:** `templates/BlazorNative.Templates` is a real, publishable pack that 8.1's **"ONE
metadata home"** rule *structurally cannot reach*. 8.3 put it **outside `src/` deliberately** — a
seventh csproj inside **un-licenses the props home**, because that home is legal only while the
purity pin proves `src/` holds exactly the six, and the six's pins are assembly-shaped (a
content-only pack satisfies none of them).

**So the rule's true scope, recorded:** ***one home for the six; the seventh agrees by pin-less
discipline.*** 8.4 split its URLs to match (`RepositoryUrl` = where the source is; `PackageProjectUrl`
= where the docs are), verified in the **packed nuspec** — because the pack is what a user gets.
*Shipping 6-of-7 is not a rule; it is a coincidence waiting to be noticed by a user.*

### 2. DoD #3 says **"a dry-run validation lane"** — that lane cannot exist

**`dotnet nuget push` has no `--dry-run`.** Verified against the live CLI in 8.2; the full option set
has no simulation flag. **A push dry-run does not exist, so the lane could not be one.**

What shipped is a **nuget.org-state preflight** — key-free, on every PR touching the release
machinery or the props. It checks the only thing a real push can reject that no local step can know
(is the id/version free?); everything else is a local fact the smoke already owns. Its own vacuity
trap (all six ids 404 today, so *every* current answer to "is it free?" is "absent") is closed by a
two-arm positive control, mutation-proven.

***The DoD's word is wrong; the thing is better than the word.***

### 3. DoD #5 says **"~20 components"** — not a count of anything

**Measured, and independently reproduced by this audit:**

| Quantity | Value | How this audit checked it |
|---|---|---|
| Concrete `ComponentBase` types | **15** | `ci.yml`'s Pin 1 mutation text names *"MISSING 15 OF 15 COMPONENTS"*; `BnFlexPreset` is abstract |
| Public types (the reference's surface) | **26** | **Re-run live:** `Generation: 26 succeeded, 0 failed` → 27 files (26 + index) |
| `[Parameter]` properties | **196** | **Re-derived live:** `grep -cE '\[Parameter[],]'` → **98 `.cs` + 98 `.razor` = 196** |

**And the blind number reproduces exactly.** `grep -c '\[Parameter\]'` — the pattern that cannot see
`[Parameter, EditorRequired]` — returns **192** at this tip. The blind half is **exactly 50%**
(98/98), as 8.4's reflecting pin said and as the review's 51% did not. *A number is not measured
until the thing that reads it can see every form it takes.*

**"21 components"** was born TRUE (21 *documented types*) and was copied out of its meaning into three
files. The DoD's "~20" was never true of anything. **The reference generates 26.**

---

## The hygiene ledger — every item decided, with its reason

### ✅ DONE — the README's counts + the Yoga literals (ledgered 8.3 → 8.4 → here)

**The README ledgered this against itself, and its own paragraph is the indictment:**

> *"for four milestones this table read 333 / 83 / 111 / 72 while the gates asserted otherwise, and
> not one of the four was within 50% of reality."*

Four numbers, wrong by up to 5×, on the front page of a public repo. 8.4 measured the item correctly
(*"both are one cheap CI read from being gate-held"*) — **this is that read.** `ReadmeDriftTests`
(+3, Runtime.Tests, **the required lane**):

- **Both sides DERIVED.** The expectation is parsed from each workflow's **`if` condition — the line
  that DECIDES** — never the step's `name:` prose, which is a copy that has drifted before (*this
  file's own header read 92 JVM for four milestones*).
- **The roster knows its size** (8.3's I-1): the table's rows and the test's roster are compared as
  **sets, both directions**, so a fifth bar cannot arrive unpinned.
- **The Yoga subjects are pattern-derived, not rostered** — copy six is held the day it lands.

**Five mutations, run and quoted:** README 577→578 ⇒ RED (*"README says 578, ci.yml's gate asserts
577"*); README Yoga 3.2.1→3.3.0 ⇒ RED; a fifth `Fuzz | 9001` row ⇒ RED (*"Rows in the table that NO
gate here pins: Fuzz"*); the gate's `if` reworded to a variable ⇒ RED (*"found 0 … the number below
is not evidence"*); **the diagram's literal deleted ⇒ RED** (*"this pin now holds NOTHING"*).

**⚠ THE LAST MUTATION FAILED TO RED ON ITS FIRST RUN — and the reason is this milestone's own subject
biting the auditor.** The README paragraph written to explain that the Yoga literal has **one home**
had **named the version**: a third copy, minted inside the sentence about not copying. The pattern
matched it, so deleting the diagram's copy left the suite green. **Phase 8.4's Gate 3 author did this
exact thing while removing a different copy.** *The pull toward a fresh copy is not theoretical, and
only a mutation found it — both times.* The copy is gone; the paragraph now records why.

**A second self-catch:** the ambiguity guard reported *"found 2"* because this pin's **own provenance
block** quotes `$passed -ne 577` while explaining its mutation — and an unanchored pattern read the
**comment** as a second gate. The guard was right (two matches means it cannot know which decides);
the patterns are now anchored at `^\s*if [(\[]`, and mutation 5 still reds with the anchor in place,
proving the anchor did not defang it.

**Not pinned, deliberately:** the Milestone-6 checklist row read *"Yoga 3.2.1 linked into both shells
— Phase 6.0"*. Holding it would **force a falsehood on the next bump** — Phase 6.0 linked 3.2.1 and
always will have. **Its literal was deleted instead**: the row's subject is the linking; the version's
home is the Gradle pin. ***A pin that can only be satisfied by writing something untrue is the wrong
pin.*** 8.4 offered both options ("extend the parity step, or delete the numbers"); each is applied
where it fits.

### ⏸ DEFERRED to M9 — the KDoc sweep + the map extraction (ONE item)

**Confirmed on the evidence, and the brief's line numbers point at the *template's* copy, which is
the whole point:**

```
templates/…/android/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt
  :33  * BnDemo, or the [EXTRA_COMPONENT] Intent-extra override; 4 [BOOT]
  :58  * (a mount-registry name, HostSession.cs). Absent → "BnDemo" since
  :192            ?: "BnStarterPage"
```

A `dotnet new` user opens their `MainActivity.kt` and reads KDoc naming **a page their app does not
have**, contradicting their own code 134 lines down. 8.4's counter is real: **8.4 is what makes that
prose reachable by strangers.**

**Why it is still deferred — the third option was tried and it costs more than it saves.** The prose
at `:33`/`:58` is **byte-identical in both copies** and sits **outside** the three regions
`TemplateDriftTests` excises (the map block, the fallback literal, the `R` import — 8.3 already gave
the template its own prose *inside* the excised map block). So there are three ways out:

1. **Diverge the prose** ⇒ a **fourth excision** in a pin the file itself calls *"⚠ BRITTLE … an
   excision is a parser that can silently stop matching"*. 8.4's rule: *excisions we already need are
   free; new excisions are new holes.*
2. **Genericize the prose in both** ⇒ byte-identity holds with **no new excision** — but the repo's
   text is **load-bearing maintainer truth**: it explains *why* BnDemo is the launcher experience and
   that tests pin other shapes by passing `"HelloComponent"` / `"CompositionProbe"` explicitly. Making
   it generic **deletes engineering truth from the repo's shell to fix the template's copy** — which is
   precisely the anti-pattern 8.4's Pin 3 refused to build (*"a whole-file regex … would teach the next
   author to delete engineering truth to go green"*).
3. **Extract the map to its own file**, retiring the excision entirely — **the clean fix, named as
   such by the pin's own doc comment**, and a **Kotlin change + a 184-test device re-run**.

**(3) is right and (3) is M9's.** Doing it here would put a shell change under the `v8.0` tag
candidate, in an **audit** phase, for prose no consumer can reach yet.

**M9's trigger, named:** ***before the first Release that publishes `BlazorNative.Templates`.***
The exposure is **bounded by the same gate as everything else** — U5: the template pack is not on
nuget.org (`dotnet new search blazornative` → *"No templates found"*, 8.3's U1, closed), so **no
stranger can generate this file today**. The residual exposure is a public repo's `templates/`
directory being browsed — strictly weaker, and named rather than smuggled. *This bound is what 8.4's
"reachable by strangers" counter did not account for; it does not make the item wrong, it makes it
schedulable.*

### ⏸ DEFERRED — `BionicNativeAot.targets` → the Runtime package's `build/`

**Right architecture, wrong week.** 8.3 ledgered it with the cost attached: it breaks the smoke's
*"exactly one dll + its XML doc under `lib/net10.0`"* **inventory shape**, which is a live tooth in
the six-package interrogation that ran green above. Trading a working pin for an ergonomic gain **no
consumer has yet asked for**, at a milestone close, is a bad trade. **Trigger:** the first
out-of-repo bionic publish that is not the template's (the template ships the shim in its own tree
today, and 8.3's `.gitignore` finding proves that path is exercised).

### ✅ ACCEPTED — the "~15-min local Runtime lane" — **because the premise is false**

**This ledger entry was inherited as fact and it does not survive measurement.** 8.4 recorded:

> *"The Runtime test lane now takes ~15 min locally — the reference fixture runs a full publish +
> xmldoc2md per run. Not a regression; a real cost, worth knowing before CI."*

**Measured at this tip, cold** (`artifacts/docs-reference` deleted first, so the publish and the
generation both did real work — `Generation: 26 succeeded` confirms it):

| Measurement | Wall clock |
|---|---|
| `generate-reference.ps1` alone, **cold** | **4 s** |
| The fixture via the test lane, **cold** (`--no-build`) | **6 s** |
| The whole `ComponentReference` class, warm | **11 s** |

**Off by roughly 150×.** The script publishes **`BlazorNative.Components` only** — a plain
framework-dependent publish, not NativeAOT — and `IClassFixture` runs it **once per class**, not per
test. The ~15 minutes someone met was the **solution build** (`dotnet test` builds the SampleApp and
everything under it), which the fixture neither causes nor can avoid.

**Decision: accept, change nothing, and correct the ledger.** The proposed remedy — make the fixture
skippable locally — would have bought ~4 seconds at the price of the one pin that catches the
10-vs-26 defect, and an opt-out that lives in a developer's profile becomes an always-on skip: the
pin would then exist only on CI, which is the vacuous-pass shape this milestone hit four times.

**And the entry is itself the milestone's thesis, one level up.** *A cost that was never measured,
carried into a ledger, about to justify weakening a pin.* It is the sixth arrival of `10 succeeded`,
`return ,$names`, the blind roster, "21 components" and "192" — **this time in a document about the
others.** *A number is not a measurement until something counts.*

---

## Findings (recorded, none blocking)

- **F1 — the required-lane run for the audited tree is this PR's own.** `ci.yml` has no branch-push
  trigger, so `build-test`/`ios-build`/`android-build` had never run on `phase-8.5-close` before this
  PR opened. The .NET/JVM/publish numbers above were re-run live locally, and the three required
  gates must be green on this PR before merge — which branch protection enforces mechanically. **The
  M7 audit's F1, verbatim and unchanged.** Named so nobody reads "580 asserted in ci.yml" as "580
  observed by CI on this SHA" before the checks finish.
- **F2 — the instrumented 184/0 has NO hosted run on any M8 commit.** The newest is **29474510761**
  at `371725f`, which **predates `v7.0`** (verified with `git merge-base --is-ancestor`). The lane
  never runs on PRs (by design since 4.0); 184 stands on **8.0's local-AVD run**, and 8.0 is
  correctly the last phase that touched anything the lane covers (the publish-head retarget). Today's
  nightly had not fired at audit time (cron 03:00 UTC; the last two schedule runs landed 05:40 and
  05:30 UTC; the audit ran 04:26 UTC). **The nightly on `main` re-asserts 184 after merge** — the
  same posture the M7 audit recorded as F2, and the nightly has confirmed every prior local
  assertion.
- **F3 — the `Deploy Documentation` lane is RED on main right now, and that is the design working.**
  Run **29554540214** at `6afd8af`: `build: success`, `deploy: failure`, *"Failed to create
  deployment (status: 404) … Ensure GitHub Pages has been enabled"*. **U1 fails loud, as 8.4
  promised.** It is advisory — the three required contexts are untouched — and it goes green the
  moment the owner clicks. **Until then U2 is the quiet one: a wrong `baseUrl` builds green, deploys
  green, and serves a styleless site. The owner's first check is a LOOK, not a red.**
- **F4 — the auditor reddened the JVM suite by hand, and the failure proved a DoD #1 claim.** The
  first `--rerun-tasks` JVM run reported **30 failures / `UnsatisfiedLinkError`** across exactly the
  11 native-loading suites. Cause: this audit had deleted `samples/BlazorNative.SampleApp/…/win-x64/publish`
  to force a clean ILC pass, **while** the JVM tests load that very dll via `jna.library.path` — CI
  publishes *then* tests; the audit ran them concurrently. Not a repo defect; re-run after the publish
  restored the dll: **106/0**. **Worth keeping:** it is independent evidence that 8.0's
  `winX64PublishPath` retarget to `samples/` is **live and load-bearing** — the tests really do load
  the sample app's publish, and they say so loudly when it is absent.
- **F5 — the local-publish caveat is more precise than recorded, and the recorded form would have
  misled the next auditor.** The note says *"use pwsh, not Git Bash — vswhere isn't on Git Bash's
  PATH"*. **Running under `pwsh` is not sufficient**: this audit's first publish ran in `pwsh` and
  still died **after ILC finished**, with `error MSB3073: "'vswhere.exe' is not recognized …"`. The
  real condition is that **`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` must be on `PATH`**
  — the ILCompiler link step invokes `vswhere.exe` **by bare name**, which is why `ci.yml` has an
  explicit *"Resolve toolchain paths (NDK root + vswhere on PATH)"* step. Shell choice is
  incidental; the PATH entry is the fact. **Second local caveat, unrecorded until now:**
  `gradlew` needs **JDK 17+** (`JAVA_HOME` defaulted to a JRE 8 here → *"Gradle requires JVM 17 or
  later … currently configured to use JVM 8"*); CI pins **temurin 21**, which is what this audit used.
- **F6 — carried residuals:** `enforce_admins: false` (M6 F3, unchanged); the device lanes stay
  advisory (the M6 recommendation #3's stability-baseline condition — measure, then promote); H6/H7
  deferred by name; **`NUGET_API_KEY` is not set, no Release exists, nothing is published, and no tag
  is created by this phase.**

---

## What M8 actually delivered

**The milestone where the project stops being a proof and starts being a product surface — with
ZERO ABI change and ZERO shell-source change across the whole milestone.** Still 9 exports, still the
72-byte bridge, still thirteen NodeTypes; M8 added no wire vocabulary at all. Concretely:

- **The registration inversion** — `RegisterPages` + `Routed<T>`/`Named<T>`; the app declares its own
  pages, `Runtime`'s Components reference is gone, and the shells embed the *sample's* publish with
  **zero shell-code lines** changed. The named trim risk **materialized** (nativelib ILC trimmed the
  whole app — module initializers are not unconditional roots) and was closed with
  `TrimmerRootAssembly`, pinned three ways.
- **Six publish-ready packages** — one metadata home, **one version literal**, symbols + SourceLink,
  deterministic, and a consumer smoke that interrogates every packed PE and then *mounts a component
  from packages alone*.
- **The release pipeline** — ONE door: `validate` (never a key) → artifact → `push`
  (`if: event_name == 'release'`, no checkout). **The milestone's own worst input is double-guarded:
  `v8.0` publishes NOTHING**, and the classifier says so in the required lane.
- **`dotnet new blazornative`** — a real template pack whose **generated output** clears the publish
  bar and assembles an APK, proven on CI every PR.
- **The docs site** — 41 pages, a **generated** 26-page component reference that is never committed,
  five pins, and an analyzer help-link pin covering *the one link class no site build can see*.
- **Test surface grew** 539 → **580** .NET; JVM **106**, instrumented **184**, iOS **154** all
  unmoved — because M8 changed no shell source, which is itself the evidence.

Along the way M8 found and fixed defects **that were all green at the time**: a generated app that
**did not compile** (`R` resolution — only `assembleDebug` saw it), a `.gitignore` swallowing the NDK
shim (**a byte-identity pin cannot see a file git never committed**), a nupkg type scanner that
**passed while blind**, a reference generator that reported **`10 succeeded, 0 failed`** and shipped
zero components, a doc-coverage pin **wearing a pin's costume over a pin's absence**, and a roster
that **did not know its own size**. **Six arrivals of one failure mode, each caught only because
something counted** — and this audit found the seventh in a ledger entry about the other six.

---

## The ledger carried into M9 (consumed by `new-milestone`)

| Item | Recorded in | Why deferred / trigger |
|---|---|---|
| **The KDoc sweep + the map extraction** (ONE item) | 8.3 → 8.4 → **8.5 (decided)** | The clean fix retires the excision and is a Kotlin change + a **184 device re-run**. **Trigger: before the first Release that publishes `BlazorNative.Templates`** — until then U5 bounds the exposure (the pack is not on nuget.org). |
| **`BionicNativeAot.targets` → the Runtime package's `build/`** | 8.3 → **8.5 (decided)** | Costs the smoke's inventory-shape tooth. **Trigger:** the first out-of-repo bionic publish that is not the template's. |
| **CS1591 for the other five packages — 174** (Runtime 87 + Core 52 + Analyzers 16 + Renderer 13 + Http 6) | 8.4 | The mechanism is built (`BnEnforceDocCoverage`); flipping each is a per-package **editorial** job. |
| **`docs/parity-contract.md`** | 8.4 | Extract the normative text out of a dated plan doc that ~50 source citations treat as permanent. The site's parity page links it the day it exists. |
| **Local search** (`@easyops-cn/docusaurus-search-local`) | 8.4 | **Trigger:** >30 pages, or the owner asks. Algolia **cannot be applied for until U1 closes**. |
| **Versioned docs** | 8.4 | **Trigger:** a second published version. |
| **Density-aware assets** (`@2x`/`srcset`) | 6.3 + 7.5 → M8 | The wire has no scale channel. **Trigger:** the first bundled/local-asset story. |
| **`SafeAreaView`** | 7.4 survey | The named edge-to-edge problem — M9-adjacent. |
| **SectionList / Pressable-state / StatusBar / RefreshControl / KeyboardAvoidingView / Alert** | 7.4 survey table | Each row carries its reason in the committed table. |
| **`OnLoad` / `PlaceholderSrc` / `repeat`-GIF-SVG** | 7.5 | No customer / the 6.3 decode ledger / the named double-load problem. |
| **iOS out-of-range indexed insert (H6)** / **the `touch.view` read (H7)** | 7.4 | Malformed wire only, stacking cosmetics / the one `UITouch` line no hosted XCTest can construct. |
| **Horizontal scroll** | 6.2, re-affirmed 7.2 | No customer forced it — `BnList` is vertical. |
| **`android-build` → ubuntu port** | 7.5-era CI note | Pending billing evidence. |
| **iOS lanes' duplicated publish (~3 min)** | M7 open | A ledgered annoyance, not milestone work. |
| **Device lanes → required** | M6 audit rec. #3 | Stands on its stability-baseline condition. **F2 is the current shape of this cost.** |
| **True-move `@key` ABI conversation** | 7.2 | **Trigger:** the first component whose UX crosses surviving keys. |
| **`BlazorNative.Cli`** | M8 out-of-scope (owner) | Templates cover creation; a CLI deserves its own design once the run workflow stabilizes. |

---

## Recommendation

### Is M8 ready to tag `v8.0`?

**Yes.** All six DoD criteria PASS on evidence re-verified live, the ABI is provably unmoved (and the
shells' *source* provably untouched) across the whole milestone, the four count bars match the
observed lanes, branch protection is exactly the three required contexts, the decision log is complete
including this audit, and the hygiene ledger is decided item by item — one closed with a
mutation-proven pin, three deferred with reasons and named triggers.

**Tag criteria, restated mechanically:** this PR's three required gates green (branch protection
enforces it) → merge → **tag `v8.0` on the merge commit, on the owner's go** (the tag is deliberately
NOT applied by this phase — the M6/M7 pattern).

**And `v8.0` publishes NOTHING.** This is the one milestone where that sentence had to be
engineered rather than assumed: the `release` event carries no tag filter, so an AdoNet.Async-shaped
`${TAG#v}` would have pushed six packages at version "8.0" **permanently**. The classifier reads the
tag's *shape* (milestone → **announce and skip, exit 0** — reddening a legitimate announcement would
train the owner to ignore reds on release runs) and the assertion compares its *content* to the props;
**either alone stops it**. Re-run live for this audit: **`v8.0 → SKIP (milestone)`, 8/8 rows, exit 0.**
A `pkg/<semver>` Release is the only publishing shape.

### The owner's standing actions

1. **Settings → Pages → Build and deployment → Source: `GitHub Actions`.** **One click.** Until then
   the site does not exist and `Deploy Documentation` is red on main (**observed today**, F3). Every
   re-pointed link — the 7 packaged `helpLinkUri`s, the READMEs, the template — 404s until it. Verify
   with `gh api repos/MarcelRoozekrans/BlazorNative/pages --jq .build_type` → `workflow` (**a 404
   before the click IS the check**).
2. **Then LOOK at the page.** **U2 is the quiet arrow** — a wrong `baseUrl` is green in CI and broken
   in the browser. Confirm it has styling. *Green is not the evidence; your eyes are.*
3. **Delete the 7 scratch branches** from the 7.4/7.5 on-CI mutation runs (`scratch/7.4-gate3-mut-*`
   ×3, `scratch/7.5-gate3-mut-*` ×4) — verified present today.
4. **`NUGET_API_KEY` only when publishing** — unchanged from 8.2. Nothing published; no tag created;
   no secret added by this phase.
