# Milestone 15 audit — A Standard for Pins

**Date:** 2026-09-25
**Auditor:** phase 15.6
**Milestone:** [M15](../planning/MILESTONE.md) · opened at `76728c9` (#368), six phases, closed over four days
**Measured against:** `origin/main` at `bbef02a4234de851c156f91032b60fedcdcc37d9`, fetched at the time of writing
**Verdict:** **FAIL** — two criteria NOT MET, two MET NARROWLY, seven MET. See [the verdict](#verdict).

Every number below was re-measured for this audit. None was copied from a phase record, the
census or an earlier audit. The command that reproduces each number sits beside it. CI evidence
comes from the controller's hand-off. Each run's `headSha` was checked against `main`'s HEAD
before it was cited.

---

## Scorecard

| # | DoD item | Verdict |
|---|---|---|
| 1 | All planned phases complete | **MET** |
| 2 | All tests passing, both device lanes dispatched, `headSha` compared | **MET** |
| 3 | A written pin standard exists | **MET** |
| 4 | Every existing pin assessed and recorded | **NOT MET** — two named pins have no verdict anywhere |
| 5 | The standard is enforced mechanically, or the absence is recorded | **MET NARROWLY** |
| 6 | #364 answered, not merely fixed | **MET NARROWLY** |
| 7 | #296 closed by the standard, red before fixed | **MET** |
| 8 | #357, #297 and #302 closed | **NOT MET** — #357 is open on GitHub |
| 9 | #291 answered for prose | **MET** |
| 10 | The small corrections land: #298, #356, #365 | **MET** |
| 11 | No new public API, wire or ABI change | **MET** |

---

## 1. All planned phases complete — **MET**

```bash
git fetch origin
git show origin/main:docs/planning/ROADMAP.md  | grep -n "#### Phase 15\."
git show origin/main:docs/planning/MILESTONE.md | grep -n "Phase 15\.[0-9] —"
```

| phase | ROADMAP on `origin/main` | MILESTONE on `origin/main` | PRs |
|---|---|---|---|
| 15.0 the pin standard | complete | complete | #372, #373, close #374 |
| 15.1 close the nine gaps | complete | complete | #377, #381 |
| 15.2 define the auth pin's coverage | complete | complete | #382, close #383 |
| 15.3 the first live test: deep-link scheme | complete | complete | #384, close #385 |
| 15.4 the missing guards | complete | complete | #388, #392, close #394 |
| 15.5 prose, and the small corrections | complete | complete | #397, #399, close #400 |
| 15.6 audit and close | pending on `main`, active on this branch | same | this audit |

Every build phase is complete on `main`. 15.6 is this audit, so it cannot be complete until the
audit is written. The M14 audit treated its own audit phase the same way.

## 2. All tests passing, both device lanes dispatched, `headSha` compared — **MET**

All figures are on `main`'s HEAD `bbef02a`. They come from the controller's hand-off,
`.superpowers/sdd/2026-09-25-phase-15.6-audit-and-close/handoff.md`.

| suite | count | evidence |
|---|---|---|
| .NET | **1186** passed, 0 failed, 0 skipped. Analyzers 27, Renderer 140, Runtime 1019 | local `dotnet test` in a worktree at `bbef02a`. `ci` push run [36129287999](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36129287999) on `bbef02a`: step ".NET tests (assert 1186 passed / 0 skipped)" succeeded |
| JVM | **162** passed, 0 failed, 0 errors | local `./gradlew.bat testDebugUnitTest --rerun-tasks` under JDK 21.0.11, summed from the test-results XML. Same `ci` run: step "JVM tests … assert 162 passed / 0 failed" succeeded |
| iOS XCTest | **271** passed, 0 failed | dispatched [36151966386](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36151966386), `workflow_dispatch`, `headSha` `bbef02a4234de851c156f91032b60fedcdcc37d9`, success |
| Android instrumented | **228** passed, 0 failed | dispatched [36151957537](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36151957537), `workflow_dispatch`, `headSha` `bbef02a4234de851c156f91032b60fedcdcc37d9`, success; step "Assert instrumented baseline (228 passed / 0 failed)" succeeded |

```bash
gh run view 36151966386 --json headSha,conclusion,event
gh run view 36151957537 --json headSha,conclusion,event
gh run list --commit bbef02a4234de851c156f91032b60fedcdcc37d9 --json databaseId,workflowName,event,conclusion,headSha
gh run view 36129287999 --json jobs --jq '.jobs[].steps[] | select(.name|test("assert|JVM")) | "\(.name): \(.conclusion)"'
```

The workflow gates assert the same four constants. Check with
`git show origin/main:.github/workflows/ci.yml | grep -n "1186\|162 passed"`, and the same for
`android-instrumented.yml` with 228 and `ios.yml` with 271.

**One limit, stated as M14 stated its own.** The lanes ran on `main`'s HEAD, not on this audit
branch's head. The branch changes only `docs/planning/*.md` and `docs/plans/*.md`, which no
shell build reads. Both lanes were dispatched on the exact `main` commit, and each `headSha` was
compared rather than trusting the green conclusion alone.

## 3. A written pin standard exists — **MET**

The standard is `docs/pin-standard.md`, 754 lines, in the repo rather than in a milestone doc. It is identical on
`origin/main`: `git diff --quiet HEAD origin/main -- docs/pin-standard.md` exits 0. It states the
three minimum clauses the DoD names, one rule each:

- **It fails when it scans nothing.** Rule 2, `:57`: *"It must fail when it scans nothing …
  Vacuity is a property of the assertion shape, not of the input. An assertion of the form* for
  every X, assert Y *passes trivially when there are no X."*
- **It fails when its subject moves.** Rule 4, `:245`: *"A pin whose subject has been renamed,
  restructured or deleted must **red**, not shrug."*
- **It states what it does not cover.** Rule 5, `:275`: *"A pin that implies completeness it
  lacks is worse than one that admits a gap … Write the limit in the pin's own doc comment."*

It also carries Rule 1 (a pin is defined by behaviour), Rule 3 (a positive control), Rule 6
(reach the tree through `BnRepo.Root()`), Rule 7 (mutations exercise the pin's own paths),
Rule 8 (consolidate, do not port), an enforcement verdict and a checklist. See
`grep -n "^## " docs/pin-standard.md`.

## 4. Every existing pin assessed and recorded — **NOT MET**

There are two recorded sets, and a third population nobody recorded:

- **The tree-reader population is fully covered.** See §4a.
- **The non-tree register has one row**, and its wording overclaims per fact. See §4b.
- **Two pins that the milestone's own document names have no verdict anywhere.** See §4d, which
  is why this item is NOT MET.

This audit's first draft scored the item MET NARROWLY. Review found §4d, and the verdict was
corrected here rather than softened.

### 4a. The tree-reader population: 33 files, 170 facts, every one with a verdict

I enumerated the population with the census's own key and reconciliation. The census is
`docs/plans/2026-09-22-phase-15.0-census.md` §1, following its "Re-measured at the end of 15.5"
note:

```bash
grep -rl  "BnRepo.Root()"                  tests/ --include="*.cs" | grep -v "/bin/\|/obj/" | wc -l   # 33
grep -rlE "BnRepo\.Root\(\)|ShellSourceScan\." tests/ --include="*.cs" | grep -v "/bin/\|/obj/" | wc -l   # 34
```

The widened key returns 34. Two corrections reconcile that to the population:

- **Plus `NSLogDriftTests`.** The plain key misses it; the widened key includes it.
- **Minus `CommentStrippedSourceTests`.** Its only matches are comments saying it is outside the
  population. That leaves **33 files**.

**Is the key complete, or is there a third door?** I checked. I took every type declared in the
33 files and searched the other 104 test `.cs` files for references to them. That is
`find tests -name "*.cs"` minus `bin/`/`obj/`, 138 files, less the 34 the widened key returns. Every hit was a
comment, the shared helpers in `tests/Shared`, or a generated vector table. No second helper
wraps `BnRepo.Root()` the way `ShellSourceScan` does. Only one non-population file touches the
disk, `CameraFacadeTests`, and it checks a temp file the test wrote itself. No Kotlin or Swift
test reads the repository tree either; a grep for
`contentsOfFile|readText()|user.dir|rootDir|projectDir` finds only captured photos and prefs files.

**Facts.** Counting `[Fact]`/`[Theory]` attributes across the 33 files gives **170**:
`grep -cE "^\s*\[(Fact|Theory)\b" <file>`, summed. That equals the census's own 15.5 figure.
Checked file by file against scorecard rows 1–33 of census §3, every per-file count agrees,
including every split row: 4a+4b, 7a+7b, 10a+10b, 13a+13b, 14a+14b, 18a+18b, 23a+23b, 24a+24b
and 27a+27b.

**Every one of the 33 files has a scorecard row, and every row carries one of the DoD's three
verdicts.** The total is 170 facts: 151 pin facts plus 19 exempt, not pins, each named with its
reason in census §2.3. There are **0 gap** facts. How the 151 pin facts split between conforms and
fixed depends on the counting rule, and the two rules give different numbers:

- **Per defect, the census spread table's rule: 142 conforms, 9 fixed.** "Fixed" counts only the
  nine facts the census found as gaps: `ShellStyleTableDriftTests` ×3, `DispatchSurfaceDriftTests`,
  `ReleaseWorkflowPinTests`, `GeneratedSymbolShadowTests` ×2, `PinPopulationTests` and
  `BnSafeAreaCoverageTests`. Control facts added in the same rows count as conforms.
- **Per scorecard row label: 139 conforms, 12 fixed.** Summing the facts in the rows labelled
  *fixed* gives 4a 3, 9 3, 13b 1, 14b 2, 20 2, 23b 1, which is 12. The difference is rows 9 and 20:
  `BnSafeAreaCoverageTests`'s 3 theories and `PinPopulationTests`' 2 facts are labelled *fixed* as
  whole rows, while only one fact in each was a censused gap.

Either way, every pin fact carries conforms or fixed.

### 4b. The non-tree register: one pin, whose row overclaims per fact

`docs/pin-standard.md`, "The register — pins that do not read the tree", under Rule 6, has
**one row: `RouteMenuDriftTests`**, with 4 facts. The row exists, so the pin is assessed. Two
things in it are not right:

1. **Rule 2: the register row's wording overclaims per fact. The pin itself is not defective.**
   `pin-standard.md:400` says *"Rule 2 conforms since 15.4 — per-fact floors (#375) close the
   vacuity"*.
   - **The mutation.** In a scratch worktree at `origin/main` I replaced `BnDemo.Destinations`
     with an empty array. Then I ran
     `dotnet test tests/BlazorNative.Runtime.Tests --filter "FullyQualifiedName~RouteMenuDriftTests"`.
   - **The result: Failed 2, Passed 2.** The review reproduced the same result independently.
     The two comparison facts go red on `AssertBothSidesNonEmpty`, as 15.4's M1 recorded.
     `MenuRows_AreUniqueAndLabelled` passes over an empty menu: two `Assert.Equal(0, 0)` and an
     `Assert.All` over nothing. `TheTwoExemptions_…` also passes, and correctly, because an empty
     menu really does not contain the two exempt routes.
   - **So the gap is per fact, and the file still catches it.** An empty menu reds this file,
     through its siblings.
   - **The file's own header is accurate.** At line 28 it scopes its claim to *"each comparison
     fact"*. What overclaims is the register row, which drops that scope.
   - **This is a Rule 2 finding in the per-fact unit** the census uses, not a defective pin.
2. **Rule 3 is recorded as "no positive control", and nothing else is said.** There is no fix, no
   exemption reason, no disclosure with a named repair of the kind Rule 3's fourth outcome
   describes, and no issue. By the census's own vocabulary, *read, judged against a specific rule,
   found wanting, and left alone*, this rule is left at an open gap.

The worktree was removed after the run.

### 4c. Manifests

Neither `docs/pin-standard.md` nor the census has a sentence on how manifests are treated. The
milestone's Goal counts **four**; there are now **six**:

```bash
git ls-tree --name-only origin/main src/ | grep json        # five, shell-source-roots.json new in 7aab286 / #382
git ls-tree --name-only 76728c9 src/ | grep json            # four at milestone open
# plus scripts/commit-parse-check/action-version.json, new in 8ee4a9c / #388
```

A manifest is a pin's subject, not a pin, so I checked that every manifest is read by at least one
population pin that has a verdict:

```bash
for m in wire-vocabulary.json dispatch-surface.json auth-semantics.json deeplink-vectors.json \
         shell-source-roots.json action-version.json; do grep -l "$m" <the 33 files>; done
```

| manifest | read by, all with verdicts in census §3 |
|---|---|
| `src/wire-vocabulary.json` | `ShellStyleTableDriftTests`, `GeneratedSymbolShadowTests`, `NodeTypeCodomainDriftTests`, `TemplateDriftTests`, `WireVocabularyCodegenTests` |
| `src/dispatch-surface.json` | `DispatchSurfaceDriftTests` |
| `src/auth-semantics.json` | `AuthSemanticsDriftTests`, `ShellSourceRootsDriftTests` |
| `src/deeplink-vectors.json` | `DeepLinkSchemeDriftTests`, `WireVocabularyCodegenTests` |
| `src/shell-source-roots.json` | `AndroidLogDriftTests`, `AuthSemanticsDriftTests`, `NSLogDriftTests`, `ShellSourceRootsDriftTests` |
| `scripts/commit-parse-check/action-version.json` | `ReleaseParserVersionPinTests` |

Every manifest is covered. The treatment is implicit, not written down.

### 4d. Two named pins with no verdict anywhere — why this item is NOT MET

`docs/planning/MILESTONE.md:53–54`, in the milestone's own Goal, names five pins that do not carry
the `DriftTests` suffix. `LayoutSurfacePinTests` and `DefaultStructTrapSweepTests` are two of them.

**Neither reads the tree:**
- `grep -nE "BnRepo|File\.|Directory\." tests/BlazorNative.Runtime.Tests/LayoutSurfacePinTests.cs
  tests/BlazorNative.Runtime.Tests/DefaultStructTrapSweepTests.cs` returns nothing.
- So neither is in Rule 6's population, and neither is in census §3.

**Neither is in the register, and neither has a verdict anywhere in `docs/`:**
- `git grep -c "LayoutSurfacePinTests\|DefaultStructTrapSweepTests" -- docs/pin-standard.md
  docs/plans/2026-09-22-phase-15.0-census.md` returns no match.
- `git grep -n` over all of `docs/` finds both only in M13 records, in M15's Goal and in the 15.0
  design. None of those assigns a verdict.

**What each pin holds:**
- **`LayoutSurfacePinTests`**, 16 facts, holds two copies of one truth: a hand-written list of
  **17** item parameters, `ItemParameters`, and 9 container parameters, `ContainerParameters`,
  compared by reflection against what the component types actually declare. It compares two
  in-memory collections, which is exactly the shape Rule 6's register exists for.
- **`DefaultStructTrapSweepTests`**, 3 facts, is a standing reflection sweep over the public
  structs of named API assemblies. It is the #178/#181 guard.

**Why my first draft missed them.** It searched outward from the 33 population files, for
references to their types. That method cannot see a pin that never touches the population.
Census §9 warned about exactly this: *"How many non-tree pins exist is **unmeasured**"*. The
register decided in 15.4 closes the hole only as far as someone remembers to add rows, and nobody
added these two. The pin standard names this cost: *"this register grows only when someone
remembers to add to it."*

**This audit does not assign either pin a verdict.** Assessing them is gap work for the owner.
Neither can be the only case, either: the 104 test files outside the population were never swept
for in-memory two-copy comparisons.

**Verdict on the item: NOT MET.** The DoD says *"An unassessed pin is a gap … silence is not"*
acceptable. Two pins that the milestone itself names are unassessed. The 33 tree-reader files and
the register row are covered, as §4a–4c show, but that does not rescue the item.

## 5. The standard is enforced mechanically, or the absence is recorded — **MET NARROWLY**

**What reds mechanically:**

| mechanism | what it enforces | what it does not |
|---|---|---|
| `PinPopulationTests.NoTest_ReachesTheRepoTree_WithoutTheSharedHelper`, with its floor `scanned >= 100` | **Rule 6, reachability.** Every hand-written `.cs` under `tests/`, minus `bin/`/`obj/` and two files excluded by name, must not reach the checkout through `BlazorNative.sln` or `AppContext.BaseDirectory`. That keeps the population enumerable | anti-vacuity. A pin can pass this and still scan nothing |
| `PinPopulationTests.TheBypassMarkers_AreStillFoundInBnRepo_TheOnePermittedHomeOfTheWalk` | Rule 3 for the population guard itself | a second walk inside `BnRepo.cs`, a decision recorded in census §6.1 |
| `ShellSourceRootsDriftTests`, 11 facts | coverage of the three shell-scanning pins. Every consumer accounts for every roster set exactly once, and every `.kt`/`.swift` inside a `scanRoot` sits in a declared root | trees compiled from outside the `scanRoots`, and subtractions made in the build files: 15.2 residual 3 |

**Nothing reds a new pin that can pass while checking nothing.** The record says so plainly, in
`docs/pin-standard.md`, "The enforcement verdict — Rule 2 is NOT enforced mechanically, and will
not be", `:502`:

> *"Answer: no, and not for want of trying to find a way. Rule 2 is a review obligation carried
> by the checklist below. Nothing in CI checks it, nothing is planned to."*

**The evidence is recorded with the verdict, and it is measured:**

- **A presence-style check scores 0 of 4 against the four real defects.** All four already
  executed a floor, on the wrong side of the comparison. See the table at `:533`.
- **A binding-aware analyzer is decidable in principle**, but it has these problems:
  - It forces the floor duplication Rule 8 bans.
  - It would face 98 conforming facts against 4 defects. The record's own figure after 15.1 is
    0 true positives against 111 pin facts. That is the record's number at that time, not
    re-measured here; the pin-fact count is 151 today, per §4a.
  - It cannot see the non-tree register at all.
- The section names the exact check it rejects, so a future one can be compared against it
  (`:618`).

**Why narrowly.** The DoD's headline, *a new pin that can pass while checking nothing must red*,
is **not** delivered. The item is met only through its fallback clause, and even that fits loosely:
- **The fallback's condition does not quite hold.** The clause reads *"if no mechanical form
  exists"*. The record says a form does exist, a Roslyn analyzer, and was rejected on cost and
  collateral. That is recorded explicitly with its evidence, which is what the DoD forbids dropping.
  But it is a *"not worth building"*, not a *"does not exist"*.
- **The partial mechanism is a different one.** The risk table's acceptable honest outcome was
  "enforcing only the anti-vacuity half". What exists enforces reachability, which the standard
  itself says `:517` *"is not anti-vacuity"*.

This audit's own measurement in §4b bears the verdict out: one fact of the one registered non-tree pin passes over an
empty input, and nothing mechanical said so.

## 6. #364 answered, not merely fixed — **MET NARROWLY**

`gh issue view 364 --json state,closedAt` → **CLOSED 2026-09-24**. The timeline's `closed` event
carries commit `7aab286`, "test(15.2): define the auth pin's coverage (#382)".

**Where the coverage is written:**

- **The roster.** `src/shell-source-roots.json`, created in `7aab286`. Its `$doc` states the
  contract: *"every consumer accounts for EVERY set, exactly once, as consumes / delegated /
  excluded"*. It lists 7 sets and 3 `scanRoots`, each with its own `minFiles`, and names 3
  consumers, among them `AuthSemanticsDriftTests`.
- **The design table.** `docs/superpowers/specs/2026-09-23-phase-15.2-design.md`, "The four holes,
  as consequences".
- **The limits.** The auth pin's own fact comments, and the 15.2 ROADMAP outcome block.

**How the four holes closed.** Each is checked against the tree on `main`:

| hole | coverage dimension | closed by | a consequence of the definition? |
|---|---|---|---|
| F1, half the Android shell unscanned | **trees** | the roster, derived from `build.gradle.kts`/`project.yml`, plus `EveryShellSourceFile_IsInsideADeclaredRoot` | **yes.** Closes it for all three consumers |
| F2, caller count scoped to one file | **trees** | `TheTestOnlyCredentialBranch_IsStillGuarded` counts over every `.kt` in the roster scope. The code comment says *"FILE SCOPE IS NOW THE ROSTER"* | **yes.** The design says it "falls out of F1" |
| F4, an import alias defeats dotted tokens | **spellings** | bare names added, and the construct banned by `NoShellSource_AliasesAnAuthenticatorNamespace`, with its control | **yes.** It closes the class, not only the reproduced instance |
| F3, the stripper over-strips past a raw string | what the scan *sees* of a file | a parse fix in `tests/Shared/CommentStrippedSource.cs` | **no, by 15.2's own design**, which calls it *"a real parse fix … with its own control"*. It is a shared-helper repair, not a consequence of the roster |

`TheScannedExtensions_CoverEveryLanguageTheBuildCompiles` covers the **file kinds** dimension.

**Why narrowly.** The coverage is written down on all three axes the DoD names, and three of the
four holes close as consequences of it. F3 closed as a patch, which is the thing the DoD's
*"rather than as four patches"* set out to avoid. The definition also carries open residuals, all
disclosed at the pins in keeping with Rule 5, and all measured green on a live mutation:
- **residual 1**, raw platform integers: 978/978.
- **residual 2**, five ways to blind the machinery from inside: 32/32.
- **residual 3**, the derivation is one build call deep and subtractions have no backstop: 11/11.
- **residual 4**, the template's Gradle file is an unread second record.

Residuals 3 and 4 were named as new pins that were *"not built"*. **No issue carries either one.**
Nothing filed matches `setSrcDirs` in `gh issue list --state all --search`.

## 7. #296 closed by the standard, red before fixed — **MET**

`gh issue view 296 --json state,closedByPullRequestsReferences` → **CLOSED 2026-09-24**, by PR
**#384**, which is `66cd693` on `main`.

**The red.** The run is on the commit that added the shared vector without the fix:

```bash
gh run view 36004048485 --json headSha,conclusion,workflowName
# android-instrumented, pull_request, headSha 24917b401fe6d29025e57656a23b41409ac12060, failure
gh run download 36004048485 -n instrumented-test-results   # then sum the JUnit XML
# tests 228, failures 1:
# io.blazornative.shell.BnDeepLinkVectorTest.everyVector_parsesToTheSharedExpectation
# "deep-link vector: BLAZORNATIVE://settings expected:</settings> but was:<null>"
```

The failure is exactly the one the record describes. It is not an infrastructure red: I parsed the
JUnit XML, which lists 228 tests and one failure, the vector test.

**The green.** Commit `1587af42f7e8cc681c03390fc627dce941e09be8`:

| run | workflow | `headSha` | conclusion |
|---|---|---|---|
| [36030419938](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36030419938) | android-instrumented | `1587af4…` | success |
| [36030419936](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36030419936) | ios | `1587af4…` | success |
| [36030419937](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36030419937) | ci | `1587af4…` | success |

`git log --oneline -3 1587af4` shows `24917b4 → cfb147a → 1587af4`, so the red is the green's
direct ancestor, two commits back.

**Through the mechanism.** The scheme has one home, `src/deeplink-vectors.json`'s `scheme`.
`DeepLinkSchemeDriftTests` pins its six declaration sites and was already present at `24917b4`.
To be precise about which part redded: it was the **shared differential vector**, the M14-shape
behavioural twin pin, running on Android, not the tree pin. The tree pin holds the spelling. The
vector holds the behaviour. Both are part of the mechanism the phase built.

## 8. #357, #297 and #302 closed — **NOT MET**

```bash
for n in 357 297 302; do gh issue view $n --json number,state,closedAt,closedByPullRequestsReferences; done
```

| issue | GitHub state | closed by |
|---|---|---|
| **#357** 14.1's dispatch completeness pin has no anti-vacuity assertion | **OPEN** | — |
| #297 pin RenderPatch subclasses against FrameEncoder | CLOSED 2026-09-24 | PR #388, `8ee4a9c`: `PatchKindDriftTests` |
| #302 release-please dropped a breaking-change commit | CLOSED 2026-09-24 | PR #388, `8ee4a9c`: `check.js` plus `ReleaseParserVersionPinTests`, live red on scratch PR #389 |

**The #357 work is done, but the issue was never closed.** `d481dae`, "test(15.1): close the nine
gaps the pin census found (#377)", added
`DispatchSurfaceDriftTests.TheDispatchDeclarationScan_IsNotVacuous` at `:381`. Its summary begins
*"THE FLOOR ON THE SCANNED SET — issue #357"*, and its assertion is `found.Length >= minimum`.
The commit body names #357. Census row 13b records it as **fixed, 15.1 task 2**. But:

- the timeline's last event on #357 is a `referenced` event from `d481dae`, on 2026-09-23. There is
  no `closed` event;
- no PR in the range names #357 in a closing keyword. `closedByPullRequestsReferences` is empty;
- **but the census says it is closed, and that is false on GitHub.**
  `docs/plans/2026-09-22-phase-15.0-census.md:667` reads *"**CLOSED BY 15.1 TASK 2 — issue
  #357.**"*, and `:1164` reads *"the 14.1 residual and issue #357, closed"*. The work was done, but
  the record claims a closure that never happened. `grep -n "#357"` over ROADMAP and MILESTONE
  alone finds it only in the *Closes:* list and the goal lines. That is why this audit's first
  draft said no record claimed the closure. That was wrong, and it is corrected here.

**The fix itself fully meets what the issue asks for.** Review confirmed this, and I re-read it:
- `TheDispatchDeclarationScan_IsNotVacuous` floors the **scanned** set, not the manifest, and it
  does so **per shell**. `MinimumKotlinDispatchDeclarations = 6` at `:342` and
  `MinimumSwiftDispatchDeclarations = 4` at `:350`, against measured 8 and 5 per census `:1164`.
  So one emptied shell cannot hide behind the other's count.
- It is mutation-proven per the 15.1 record.

**The only thing missing is closing the issue.**

The DoD says **"are closed"**, and the phase spec for this audit requires each issue to be
**closed on GitHub and** backed by its commit. #357 is backed but not closed. The owner's
no-workarounds rule applies, so this is reported as NOT MET and not reworded into a pass. **The
gap is one action:** close #357 with a comment citing `d481dae` / #377 and
`TheDispatchDeclarationScan_IsNotVacuous`. Then re-measure this item.

## 9. #291 answered for prose — **MET**

`gh issue view 291` → **CLOSED 2026-09-25**, by PR **#399**. The work itself is in `0bdeb84` / #397.

**The three pairs are identified by inference, and the inference is recorded.**
`docs/superpowers/specs/2026-09-25-phase-15.5-design.md` item 3: *"The 'three unpinned
documentation transcription pairs' are named nowhere in the record … By elimination they are
**#365's three pure-documentation findings**"*, meaning F6, F7 and F8.

**How each pair was answered.** The answers are in the ROADMAP 15.5 outcome block, *"F6, F7 and
F8 — #365's three prose pairs — answered one by one"*:

- **F6:** *"Not pinned, not needed: the duplicate was removed, so the count now appears only in
  the assertion that holds it."*
  - Verified on `main`: `git grep -n -iE "all nine|nine symbols|the 9 exports|nine exports"
    origin/main -- .github/workflows/` returns one line, `ci.yml:815`, *"all nine facts are
    TemplateDriftTests"*, which is about test facts and not exports. #365's three live sites,
    `ci.yml:3102`/`:3106`/`:2578` at the time, are gone.
  - The `:334–:786` "9 exports" lines are the dated provenance changelog. #365 itself says to
    leave them alone.
  - `android-instrumented.yml:12` no longer states a count.
- **F7:** *"Pinned: no. This is prose against prose about CI state — a test would only restate
  whichever side it chose, and the truth lives in GitHub's live branch protection, not in
  anything the repo itself carries."* Verified: `git grep -n -i "already-required"` over
  `docs/GITHUB-SETUP.md` and `ios.yml` on `origin/main` finds nothing.
- **F8:** *"Pinned: no, for the same reason as F7: a claim about what CI state already exists,
  checked against no mechanism but a reader."*

So every pair has an answer, and each answer gives its reason, not only the conclusion. That is
what the DoD's clause asks for.

**The wider answer.** #291's own ask was *audit every doc page and leave a guard*. It was met by:

- the 20-page audit, `docs/plans/2026-09-25-phase-15.5-docs-audit.md`;
- `DocsSamplesDriftTests` and `DocsNameDriftTests`, census rows 32–33, both conforming;
- CI's "Docs samples compile" step, which succeeded on `bbef02a` in run 36129287999, and whose
  live red is run 36119508755.

## 10. The small corrections land: #298, #356, #365 — **MET**

| issue | GitHub state | the change on `main` |
|---|---|---|
| #298 BnSwitch's XML doc names SwitchMaterial | CLOSED 2026-09-25 by PR #399 | `0bdeb84` / #397 corrects `BnSwitch.razor` and five siblings, held by `TextCollapseParityDriftTests.ComponentDocs_NameTheAndroidWidgetClassTheShellActuallyBuilds`, census row 26 |
| #356 iOS secure-storage legend claims Unavailable means not enrolled | CLOSED 2026-09-25 by PR #399 | all three legend copies, `BnSecureStorage.swift`, `ShellBridge.kt` with its template mirror, and `IMobileBridge.cs`, corrected in `0bdeb84` with a follow-up in `ce0d714`. **The behaviour question was split to #396**, which is open and quotes #356 as its origin |
| #365 residual overclaims after 14.4 | CLOSED 2026-09-25 | the timeline's `closed` event carries commit `0bdeb84` / #397 |

```bash
for n in 298 356 365 396; do gh issue view $n --json state,closedAt,closedByPullRequestsReferences; done
gh api repos/MarcelRoozekrans/BlazorNative/issues/365/timeline --jq '.[] | select(.event=="closed") | .commit_id'
git diff 76728c9 origin/main -- src/BlazorNative.Components/BnSwitch.razor src/BlazorNative.Core/IMobileBridge.cs
```

## 11. No new public API, wire or ABI change — **MET**

```bash
git diff --stat 76728c9 origin/main -- 'src/*/PublicAPI.*.txt' src/wire-vocabulary.json   # empty
git ls-tree -r --name-only origin/main | grep -i PublicAPI                                # 14 files, all under src/<pkg>/, none nested
git show <rev>:src/BlazorNative.Runtime/Exports.cs | grep -o 'EntryPoint *= *"[^"]*"' | sort
#   76728c9: 10 entry points · origin/main: 10 entry points · diff: IDENTICAL
git diff 76728c9 origin/main -- src/BlazorNative.Runtime/Exports.cs                       # one line, VersionNumber
```

- **Public API:** all 14 `PublicAPI.Shipped/Unshipped.txt` files, for 7 packages, show **no diff**.
- **Wire:** `src/wire-vocabulary.json` shows **no diff**.
- **ABI:** the ten `[UnmanagedCallersOnly]` `EntryPoint` names are identical.
  - `grep -c "^\s*\[UnmanagedCallersOnly"` gives 10 on both revisions.
  - A plain `grep -c UnmanagedCallersOnly` gives 14 on both, because it also counts comments.
    M14 recorded the same artifact.

**The release-please commits in the range, listed by name and excluded explicitly.** Run
`git log 76728c9..origin/main --oneline --grep "chore(main): release"`:

| commit | release | files |
|---|---|---|
| `70d169f` | chore(main): release 0.16.0 (#362) | the 7 version-string files below |
| `6f25f15` | chore(main): release 0.16.1 (#380) | same 7 |
| `18245fc` | chore(main): release 0.16.2 (#386) | same 7 |
| `1b3f058` | chore(main): release 0.16.3 (#393) | same 7 |

The 7 files are:

- `.release-please-manifest.json`
- `CHANGELOG.md`
- `src/BlazorNative.Runtime/Exports.cs`, the `VersionNumber` line
- `src/Directory.Build.props`, the `<Version>` element
- `templates/BlazorNative.Templates/BlazorNative.Templates.csproj`
- the template's `template.json`
- the template's `MyBlazorNativeApp.csproj`

`git log --oneline 76728c9..origin/main -- src/BlazorNative.Runtime/Exports.cs` returns exactly
those four commits. The only change to `Exports.cs` in the range is `0.15.0 → 0.16.3`, spread over
those four releases.

**Everything else under `src/`, checked one hunk at a time.** Command:
`git diff --stat 76728c9 origin/main -- src | grep -v -i test`, then each hunk read. None of it is
API, wire or ABI:

- **Comments and XML docs only, 15.5:**
  - `BnCheckbox`, `BnImage`, `BnPicker`, `BnSafeArea`, `BnSlider`, `BnSwitch`, `BnView`
  - `BnPicker.razor.cs` and `BnSlider.razor.cs`: XML `<summary>` only, naming `BnSpinner` and
    `BnSliderView`
  - `IMobileBridge.cs`
  - `BnSecureStorage.swift`
  - `ShellBridge.kt`
  - `Directory.Build.targets`
  - `BridgeAsyncHandlerAnalyzer.cs`
- **An intended behaviour change, #296.** `MainActivity.kt` now compares
  `data.scheme?.lowercase()`. This is behaviour, not surface, and it is the point of 15.3.
- **Pin manifests.** `auth-semantics.json`, `deeplink-vectors.json`, and the new
  `shell-source-roots.json`. All are test inputs; none is wire.
- **Generated test vector data, 15.3.** Each copy gains one row,
  `BLAZORNATIVE://settings → /settings`, emitted from `deeplink-vectors.json`:
  - `src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/BnDeepLinkVectors.g.kt`,
    the androidTest source set
  - `src/BlazorNative.Apple/BnHostTests/BnDeepLinkVectors.g.swift`, the XCTest bundle

  Both are test data, not shipped shell code. My first `grep -v -i test` filter hid the Kotlin
  copy, because its path contains `androidTest`.
- **Build and tooling:**
  - `Package.resolved` and `project.yml`: #378, #379, the SwiftPM pin
  - the Gradle wrapper: #387
- **A package dependency bump.** Core, Http and Renderer move from `ZeroAlloc.Inject` and its
  generator 1.7.6 to **1.8.0**, in #398. It is not API, wire or ABI, and the unchanged
  `PublicAPI` baselines include the generated types, so the generator bump moved no public
  surface. It is still a change consumers see, in the packages' dependency closure, and it is
  noted here for that reason.

---

## Carried forward

Every issue below is open. Each was filed before this audit, or split during the milestone, and
each is outside M15's DoD:

- **#401** Twin divergence: the iOS stderr pump re-gates by the shell's log level, Android's does not. *A behaviour question the 15.5 docs audit raised. The DoD covers pins and prose, not logging semantics.*
- **#402** An IMobileBridge replacement in ConfigureServices is not rejected, while INavigationManager is. *A runtime policy gap, not a pin.*
- **#403** BN0004's diagnostic message names only the Kotlin dispatch lane. *Analyzer message wording, raised by the 15.5 audit.*
- **#404** CI's JDK step is named JDK 21 but provisions JDK 25; setup.ps1 installs 21. *A CI and setup hygiene item.*
- **#405** Compiled docs samples can still throw at render; add a render smoke. *It extends #291's guard beyond "compiles". #291 is answered without it.*
- **#406** Pin: every baselined public type is tiered or NOT-API. *Blocked on the owner tiering 9 types in `docs/plans/2026-07-21-phase-11.3-api-tiers.md` §8.*
- **#407** Flaky: BnCameraAndroidTest times out loading the captured photo into BnImage after 15s. *An infrastructure flake on the device lane, not a pin.*
- **#396** iOS secure storage on an unenrolled device may return Ok where Android returns Unavailable. *Split from #356. Blocked on a device run, which the milestone deliberately excluded.*

**Not filed, and recorded here so they are not lost.** The owner's "carry forward, filed" rule
reaches these too, so each needs an issue or an explicit decision:

- **#357, still open.** This is the §8 gap, not carried debt.
- **`LayoutSurfacePinTests` and `DefaultStructTrapSweepTests`, unassessed.** This is the §4 gap,
  not carried debt, and the rest of the non-population tests are unswept.
- **The register row for `RouteMenuDriftTests`.** Its Rule 2 wording overclaims per fact, and its
  Rule 3 has no disposition. See §4b.
- **15.2 residuals 3 and 4.** The "dual of `EveryRootList_IsExternallyDerived`" pin and the
  template-Gradle second record were both named as new pins, not built, and not filed.
- **15.2 residual 6.** Ten CS1570 doc-comment warnings in three test files. The ROADMAP says
  *"filed rather than fixed"* and names 15.5 as the natural home, but no issue matching CS1570
  exists (`gh issue list --state all --search CS1570`). The malformed `<Version>` fragments at
  `PackageVersionPinTests.cs:396` and `:512` are still present.
- **#395**, open since 2026-09-25. The ios XCTest baseline is broken by an interleaved log line.
  It was filed during 15.4, so it is tracked, but it is not in the hand-off's carried list.

Older open debt this milestone did not touch: #345 and #346 are pinned by tests that assert the
bug still exists, and the owner-accepted hardening ledger is #8, #9, #12 and #13.

---

## What this milestone taught

**Most new guards first shipped in a form that could pass while checking nothing, and review
caught it, not CI.** From the record:

- 15.1's own floors were placed on the wrong set twice: census rows 10b and 14b, *"overstated
  here from the census onward"*.
- 15.2's first fix for the stripper went blind to end of file behind `@"""`, with 1142 of 1145
  tests green.
- 15.5's samples pin shipped with indented fences invisible end to end.
- Its widget pin passed on #298's own regression.

This audit adds one more: the register row for `RouteMenuDriftTests` claims per-fact floors
that one of its four facts lacks. The standard's Rule 2 was right that assertion shape, not input,
is what makes a pin vacuous.

**The population nobody can enumerate stayed unenumerated.** Census §9 said non-tree pins were
*"unmeasured"*. 15.4 answered with a register that grows only when someone remembers it. The
register gained one row. The milestone's own Goal names two more non-tree pins,
`LayoutSurfacePinTests` and `DefaultStructTrapSweepTests`, and neither was ever assessed. This
audit's first draft missed them too, because it searched outward from the population it already
had, which is exactly the blindness §9 describes.

**Records drift from their subjects in the same way pins do, and one record here was false, not
merely silent.**
- The census says #357 is **"CLOSED BY 15.1 TASK 2"** at `:667`, and **"closed"** at `:1164`. On
  GitHub it has been open the whole time. A reader who trusted the census would have ticked this
  DoD item.
- 15.2 said *"filed"* for a warning set nobody filed.

Neither is a code defect. Both are claims about the outside world, a GitHub issue state and an
issue's existence, written into a document with no mechanism behind them. This is the pattern
this repo calls a safety claim without a pin, applied to bookkeeping.

**A guard fix is worth most when it is proven live.** #392 rewrote the release-notes parse guard
so it would stop redding GitHub's "Update branch" merge commits. PR #399's merge commit `4ac678e`,
"Merge branch 'main' into docs/15.5-record", is exactly that shape, and it passed the step "Every
commit must survive release-please's parser" in commitlint run 36125170475.

---

## Verdict

**FAIL.** Seven criteria are MET, two MET NARROWLY, and two NOT MET: items 4 and 8.

**What M15 delivered.** It did what it set out to do in most of its substance:
- **The standard.** A written standard with its reasons attached.
- **The census.** A tree-reader population enumerable by behaviour, with 170 of 170 facts carrying
  a verdict.
- **The auth pin.** Its coverage is defined on three axes.
- **The live divergence.** It was redded before it was fixed.
- **Prose.** It was answered with reasoning.
- **Surface.** No public, wire or ABI change.

**Why FAIL. Two criteria are not met, and the no-workarounds rule forbids rewording either:**
- **Item 4.** Two pins that the milestone's own Goal names, `LayoutSurfacePinTests` and
  `DefaultStructTrapSweepTests`, have no verdict anywhere. The DoD says an unassessed pin is a gap.
  The wider non-tree population was never swept, so these two may not be the only ones.
- **Item 8.** #357 is open on GitHub. Its fix, `d481dae`, fully meets the issue, and the census
  records it as closed, but the DoD and the audit spec require the issue itself to be closed.

**The two narrow items are honest partials, not defects to hide:**
- **§5:** the milestone's headline enforcement was rejected on cost, and the rejection is
  recorded with evidence.
- **§6:** F3 closed as a patch, not as a consequence.

## Path to close — a recommendation; the owner decides

Per the audit spec, FAIL goes to `plan-milestone-gaps`. Recommended gaps:

1. **Close #357 with its evidence:** `d481dae` / #377, `TheDispatchDeclarationScan_IsNotVacuous`,
   and the per-shell floors 6 and 4.
2. **Assess `LayoutSurfacePinTests` and `DefaultStructTrapSweepTests`** against Rules 2–5 and 7,
   and add them to the register. Then sweep the remaining non-population test files once for
   in-memory two-copy comparisons, so the register holds the whole set that is known today and not
   only the one pin someone remembered.
3. **Fix the `RouteMenuDriftTests` register row.** Scope its Rule 2 wording to the two comparison
   facts, as the file header already does, and give Rule 3 a disposition.
4. **File 15.2 residuals 3, 4 and 6** as issues.
5. **Re-audit.** Re-measure items 4 and 8; everything else can be carried from this audit only if
   `main` has not moved in a way that touches it.
