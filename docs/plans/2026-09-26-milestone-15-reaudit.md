# Milestone 15 re-audit — A Standard for Pins

**Date:** 2026-09-26
**Auditor:** phase 15.8
**Milestone:** [M15](../planning/MILESTONE.md) · opened at `76728c9` (#368)
**Measured against:** `origin/main` at `592d2d5683cb5b3b31eedc51c518df8de8ff5dfd`, fetched at the time of writing
**The audit it re-runs:** [`2026-09-25-milestone-15-audit.md`](2026-09-25-milestone-15-audit.md), **FAIL**. That report stays as it is.
**Verdict:** **PASS WITH FINDINGS** — eight criteria MET, three MET NARROWLY, none NOT MET. See [the verdict](#verdict).

Every number below was measured again for this re-audit, from scratch. None was copied from the
first audit, from 15.7's sweep, record or register, or from a ROADMAP block. The command that
reproduces each number sits beside it. CI and local-test evidence comes from the controller's
hand-off, which checked each run's `headSha` against `main`'s HEAD. I re-read those runs' `headSha`
myself as well.

Commands assume Git Bash on Windows, so `export MSYS_NO_PATHCONV=1` is needed before any
`git show origin/main:<path>`.

---

## Scorecard

| # | DoD item | Verdict |
|---|---|---|
| 1 | All planned phases complete | **MET** |
| 2 | All tests passing, both device lanes dispatched, `headSha` compared | **MET** |
| 3 | A written pin standard exists | **MET** |
| 4 | Every existing pin assessed and recorded | **MET NARROWLY**. Every pin has a verdict, but the non-tree register never assesses Rule 4 |
| 5 | The standard is enforced mechanically, or the absence is recorded | **MET NARROWLY** |
| 6 | #364 answered, not merely fixed | **MET NARROWLY** |
| 7 | #296 closed by the standard, red before fixed | **MET** |
| 8 | #357, #297 and #302 closed | **MET** |
| 9 | #291 answered for prose | **MET** |
| 10 | The small corrections land: #298, #356, #365 | **MET** |
| 11 | No new public API, wire or ABI change | **MET** |

---

## 1. All planned phases complete — **MET**

```bash
git fetch origin
git show origin/main:docs/planning/ROADMAP.md  | grep -n "#### Phase 15\."
git show origin/main:docs/planning/MILESTONE.md | grep -n "Phase 15\.[0-9] —"
git log --oneline 76728c9..origin/main
```

| phase | ROADMAP on `origin/main` | MILESTONE on `origin/main` | merged as |
|---|---|---|---|
| 15.0 the pin standard | complete | complete | `c45b5d8` #372, `ed6c55c` #373, `a35e760` #374 |
| 15.1 close the nine gaps | complete | complete | `d481dae` #377, `21a59db` #381 |
| 15.2 define the auth pin's coverage | complete | complete | `7aab286` #382, `5f3c2e4` #383 |
| 15.3 the first live test: deep-link scheme | complete | complete | `66cd693` #384, `2b59541` #385 |
| 15.4 the missing guards | complete | complete | `8ee4a9c` #388, `bd938d8` #392, `8f32813` #394 |
| 15.5 prose, and the small corrections | complete | complete | `0bdeb84` #397, `ce0d714` #399, `bbef02a` #400 |
| 15.6 audit and close | complete | complete | `4b79cec` #408, `1f2b3a7` #409 |
| 15.7 close the audit gaps | complete | complete | `5b536f4` #415, `592d2d5` #416 |
| 15.8 re-audit and close | pending on `main`, active on this branch | same | this re-audit |

Every phase before this one is complete on `main`. 15.8 is this re-audit, so it cannot be complete
until the report exists. The first audit and M14's audit treated their own audit phase the same way.

## 2. All tests passing, both device lanes dispatched, `headSha` compared — **MET**

All figures are on `main`'s HEAD `592d2d5`. They come from the controller's hand-off,
`.superpowers/sdd/2026-09-26-phase-15.8-reaudit-and-close/handoff.md`. The local runs started from a
clean rebuild, `dotnet build BlazorNative.sln --no-incremental`.

| suite | count | evidence |
|---|---|---|
| .NET | **1200** passed, 0 failed, 0 skipped: Renderer 139, Analyzers 27, Runtime 1034 | local `dotnet test --no-build` after the clean rebuild at `592d2d5`. `ci` push run 36231940154, `headSha` `592d2d5…`: the step ".NET tests (assert 1200 passed / 0 skipped)" succeeded |
| JVM | **162** passed, 0 failures, 0 errors | local `./gradlew.bat testDebugUnitTest --rerun-tasks` under JDK 21.0.11. The same `ci` run: the step "JVM tests … assert 162 passed / 0 failed" succeeded |
| iOS XCTest | **271** passed, 0 failed | dispatched [36234197045](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36234197045), `workflow_dispatch`, `headSha` `592d2d5683cb5b3b31eedc51c518df8de8ff5dfd`, success |
| Android instrumented | **228** passed, 0 failed | dispatched [36234195031](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36234195031), `workflow_dispatch`, `headSha` `592d2d5683cb5b3b31eedc51c518df8de8ff5dfd`, success; the step "Assert instrumented baseline (228 passed / 0 failed)" succeeded |

```bash
gh run view 36234197045 --json headSha,conclusion,event,workflowName
gh run view 36234195031 --json headSha,conclusion,event,workflowName
gh run list --commit 592d2d5683cb5b3b31eedc51c518df8de8ff5dfd --json databaseId,workflowName,event,conclusion,headSha
gh run view 36231940154 --json headSha,jobs --jq '.jobs[].steps[] | select(.name|test("assert|JVM";"i")) | "\(.name): \(.conclusion)"'
git show origin/main:.github/workflows/ci.yml | grep -n "1200\|162 passed"                     # :2275, :2293, :2653
git show origin/main:.github/workflows/android-instrumented.yml | grep -n "228 passed"         # :1113
git show origin/main:.github/workflows/ios.yml | grep -n "271"                                 # :577, :1408
```

The Renderer count moved from the 140 the first audit measured to 139. That is 15.7 retiring
`StyleAttributes_AreExactlyTheUnionOfTheYogaAndVisualHalves` as a tautology, and the fact is gone:
see §4b.

**One limit, stated as the first audit stated it.** The lanes ran on `main`'s HEAD, not on this
branch's head. The branch changes only `docs/`, which no shell build reads.

**One observation, which is not a DoD item.** The hand-off records that the clean build emits
warnings in the test tree only: 46 BL0006, the Blazor internal `RenderTree` API, 42 of them in
`LayoutSurfaceSequenceBandTests.cs`, and 2 CS8669. None is in `src`, and the release pack's
zero-warning bar is unaffected. The file's use of `RenderTree` grew in 15.7:
`git show <rev>:tests/BlazorNative.Runtime.Tests/LayoutSurfaceSequenceBandTests.cs | grep -c RenderTree`
gives 21 lines at `1f2b3a7` and 33 at `origin/main`, because 15.7's band and collision controls
build synthetic frames. Tests pass, so this does not bear on item 2. It is new finding N4 below.

## 3. A written pin standard exists — **MET**

The standard is `docs/pin-standard.md`, **784 lines** (`wc -l`), in the repo rather than in a
milestone doc. `git diff --quiet HEAD origin/main -- docs/pin-standard.md` exits 0. It states the
three clauses the DoD names, one rule each (`grep -n "^## " docs/pin-standard.md`):

- **It fails when it scans nothing.** Rule 2, `:57`, *"It must fail when it scans nothing"*.
- **It fails when its subject moves.** Rule 4, `:245`: *"A pin whose subject has been renamed,
  restructured or deleted must **red**, not shrug."*
- **It states what it does not cover.** Rule 5, `:275`: *"A pin that implies completeness it lacks
  is worse than one that admits a gap … Write the limit in the pin's own doc comment."*

It also carries Rules 1, 3, 6, 7 and 8, the enforcement verdict at `:532`, and the checklist at
`:684`, which lists Rule 4 as its own box at `:695`.

## 4. Every existing pin assessed and recorded — **MET NARROWLY**

Two populations, each enumerated here by its own key. Every member of both has a verdict. The item
is narrow, not clean, for the reason in §4d: the non-tree register assesses Rules 2, 3, 5 and 7, and
never Rule 4, which is one of the three clauses the DoD itself names.

### 4a. The tree-reader population: 33 files, 170 facts, every one with a verdict

The census key is `BnRepo.Root()` callers, widened by `ShellSourceScan.`:

```bash
git ls-files 'tests/*.cs' | xargs grep -l  "BnRepo.Root()"                        | wc -l   # 33
git ls-files 'tests/*.cs' | xargs grep -lE "BnRepo\.Root\(\)|ShellSourceScan\."   | wc -l   # 34
```

Reconciled as the census does:

- The widened key adds `NSLogDriftTests`, which reaches the tree through `ShellSourceScan`.
- It still includes `CommentStrippedSourceTests`, whose only matches are comments at `:10` and `:58`
  saying it is outside the population. Remove it.
- That leaves **33 files**.

**No third door.** `tests/Shared` holds `BnRepo.cs` and `CommentStrippedSource.cs`. `BnRepo`'s only
public members are `Root()` at `:27` and `TestBinaryDirectory()` at `:50`. The only caller of the
second is `PackagePurityTests.cs:448`, already in the population. `ShellSourceScan` is declared in
`ShellSourceRootsDriftTests.cs`, which is in the population. Commands:
`grep -n "public static" tests/Shared/BnRepo.cs` and
`grep -rn "BnRepo\.[A-Za-z]*(" tests --include=*.cs | grep -v "BnRepo.Root()"`.

**Facts: 170.** `grep -cE "^\s*\[(Fact|Theory)\b" <file>`, summed over the 33. I checked each file's
count against its row in census §3, `docs/plans/2026-09-22-phase-15.0-census.md:271–303`. All 33
agree, including the split rows 4a+4b, 7a+7b, 10a+10b, 13a+13b, 14a+14b, 18a+18b, 23a+23b, 24a+24b
and 27a+27b. None of the 33 files changed after the first audit:
`git diff --stat bbef02a origin/main -- <the 33 files>` is empty.

**Every file has a scorecard row, and every row has a verdict: conforms, fixed, or exempt with its
reason.** Exempt facts: 7b 5, 10b 5, 18b 4, 24b 3, 27b 2, which is 19. That leaves 151 pin facts.

**One scorecard row overclaims per fact.** Row 27a gives all nine manifest-reading facts in
`WireVocabularyCodegenTests` Rule 2 *yes* and **conforms**. But the census itself, in its 15.7
addendum at `:1306–1313`, names one of them,
`TheRenderersStyleSets_AreTheManifests`, as *"One known per-fact gap in THIS census's population,
found during 15.7 and not fixed there"*. It has no floor of its own; it leans on sibling floors in
another file. I read the fact at `WireVocabularyCodegenTests.cs:147`: two `Assert.Equal` set
comparisons and no count assertion, so the record's description is right. The fact is **assessed**,
and its gap is **named**, so it is not silence. But the scorecard row and the spread table's
*"0 gap"* disagree with the census's own text. `gh issue list --state all --search TheRenderersStyleSets`
returns nothing. This is new finding N2.

### 4b. The non-tree population: 93 files, 15 holding pins, every file and group with a register row

I re-ran the sweep's own population command on `origin/main`:

```bash
all=$(git ls-files 'tests/*.cs' | grep -v '/obj/\|/bin/')      # 138 files
for f in $all; do grep -qE "BnRepo\.Root\(\)|ShellSourceScan\." $f || echo $f; done \
  | xargs grep -lE "\[(Fact|Theory)" | wc -l                   # 93
```

**93 files, and the same 93 the sweep read.** I ran the same filter against `1f2b3a7`, the sweep's
base, through `git show 1f2b3a7:<file>`, and `diff` of the two lists is empty. 15.7 added no test
file: `git diff --name-status 1f2b3a7 origin/main -- tests` lists 16 files, all `M`.

**The 16 modified files are exactly the 15 pin files plus `BnScrollDemoTests`,** which is the
final-review addendum under group E. I diffed their fact names between `1f2b3a7` and `origin/main`
and checked that every added fact has a verdict:

| file | facts, `1f2b3a7` → `main` | added or removed | in the register? |
|---|---|---|---|
| `RouteMenuDriftTests` | 4 → 6 | + `Missing_ReportsAPlantedGhostRoute`, + `Dangling_ReportsAPlantedDanglingRow` | named, Rule 3 |
| `LayoutSurfacePinTests` | 16 → 19 | + `NonLayoutDetector_…`, + `RedeclarationDetector_…`, + `RazorEmitters_GrowingItIsADeliberateAct` | all three named |
| `LayoutSurfaceSequenceBandTests` | 4 → 8 | + `TheContainerRows_AreThereToBeChecked`, + `TheEmissionRosters_AreExactlyTheDeclaredSurface`, + `TheCollisionDetector_…`, + `TheBandDetector_…` | the first two named. The two controls are described, *"each has a control that feeds synthetic `RenderTreeBuilder` frames"*, not named |
| `DefaultStructTrapSweepTests` | 3 → 5 | the sweep fact renamed to `…_NewYieldsTheDeclaredDefaults`; + `TheSweep_Detects_…`, + `TheVisibilityFilter_…`, + `Activator_…`; − `TheSweepPredicates_…` | the three new controls named; the renamed sweep fact is the row's subject |
| `NotApiEditorBrowsableTests` | 3 → 5 | + `ShippedAssemblies_AreExactlyTheSevenShippedPackages`, + `MarkedTypeNames_ReportsAPlantedStrayMark_…` | both named |
| `BnLengthTests` | 16 → 17 | + `TheDetector_RejectsABareLength_…` | named |
| `BnComponentTests` | 42 → 43 | + `ThePrefixDetector_RejectsABareEnumName_…` | named |
| `StyleAttributePartitionTests` | 9 → 8 | − `StyleAttributes_AreExactlyTheUnionOfTheYogaAndVisualHalves` | recorded as **retired** |
| the other 8 files | unchanged | none | — |

The fact names were extracted by an `awk` over `git show <rev>:<file>` that takes the method after
each `[Fact]`/`[Theory]`. Its counts differ from `grep -c` only where an attribute and a method are
split across lines.

**Every one of the 15 pin files has a row in the register**, `docs/pin-standard.md` under Rule 6,
*"The register — pins that do not read the tree"*, `:396–431`:
`RouteMenuDriftTests`, `LayoutSurfacePinTests`, `LayoutSurfaceSequenceBandTests`,
`DefaultStructTrapSweepTests`, `BnLengthTests` as `LengthParameterNullabilityPinTests`,
`ParameterBindingFaultTests`, `BnComponentTests`, `StyleAttributePartitionTests`,
`NotApiEditorBrowsableTests`, `SpikeRazorTests`, `BnItemsJsonTests`, `ScrollCommandTests`,
`BnModalTests`, `BnFormControlTests`, `ForwardedParameterNameTests`.

**The sweep's ambiguous groups:**

| group | where its verdict is |
|---|---|
| A, cross-language scalar half-pins | its own register row: golden, not a pin; #414 is the replacement |
| B, `DeepLinkVectorTests` | its own register row: fixed points, not pins |
| C, `ForwardTarget_…` in `ForwardedParameterNameTests` | its own register row: near-tautological, Rule 2 partial |
| D, the three per-type `Intersect` facts in `LayoutSurfacePinTests` | inside that file's row: no floor of their own, non-vacuous through `BnLayoutItem_DeclaresExactlyTheItemSurface` |
| E, `BnImagePolishDemoTests` and the `BnScrollDemoTests` addendum | its own register row: one genuine pin kept, the tautologies deleted |
| F, `NewCaptureOptions_…` | inside `DefaultStructTrapSweepTests`' row: a named anchor |
| G, goldens whose numbers Kotlin or Swift suites transcribe | **no row and no issue.** The sweep classes them as goldens, not pins |
| H, `BridgeHttpEndToEndTests.BuildHostGraph` | **no row and no issue.** The sweep classes it as *"a copy without a pin"* |

G and H are not pins by the sweep's own test, so they do not make item 4 unmet. But each is a
recorded place where two copies exist and nothing compares them, and neither has a disposition. I
confirmed H is still live: `BridgeHttpEndToEndTests.cs:19–20` says *"If HostSession.EnsureSession's
registrations change, update BuildHostGraph below to match"*, and nothing enforces it.
`gh issue list --state all --search BuildHostGraph` returns nothing, and #414's body scopes itself
to group A. This is new finding N3.

**The first audit's two NOT MET pins now have verdicts:**

- **`LayoutSurfacePinTests`**: Rule 2 conforms except group D. Rule 3 conforms. Rule 5 conforms,
  and the file has a header block it lacked before. Rule 7 cites L7–L9b and the final review's M3.
- **`DefaultStructTrapSweepTests`**: Rule 2 conforms *"on a population of one"*, `CaptureOptions`,
  and says so. Rule 3 conforms, with the value comparison and three controls. Rule 5 conforms, with
  its limits listed. Rule 7 cites S1–S3 and S6a, plus the Activator proof.

I checked each Rule 7 citation resolves to a real section of
`docs/plans/2026-09-26-phase-15.7-record.md` with `grep -n "^#" docs/plans/2026-09-26-phase-15.7-record.md`:
Task 2 at `:14`, Task 3 at `:63`, Task 4 at `:120`, the Activator proof at `:184`, row 17 at `:200`
and the final-review round at `:248`.

### 4c. The partials the register names — judged

"Partial, named" is a verdict. The DoD's line is between *assessed* and *silent*, and each of these
is assessed, with what is missing written at the row. I judge all of them acceptable:

| row | rule | what is missing | judgement |
|---|---|---|---|
| sweep 12 and 15, `LayoutSurfaceSequenceBandTests` | Rule 2 | the number of theory rows is floored only by xUnit's refusal of empty `MemberData`, so at least 1 | acceptable. Per-row floors exist, and the reviewer's region-filter mutation in the 15.7 record reds 15 facts. The residual is one row surviving out of many, and the row says so |
| sweep 38, `YogaAndVisualStyleAttributes_AreDisjoint` | Rule 3 | no planted overlap through the `Intersect` | acceptable. Rows 39–40 anchor named members of each half, and each half is floored at its measured count |
| sweep 38 | Rule 5 | the header states purpose, not reach | acceptable, but it is the weakest of the set: a missing disclosure on a disjointness check is cheap to add |
| group C | Rule 2 | no floor of its own | acceptable. The facts are near-tautological and say so |
| group E | Rule 5 | no file-level block | acceptable. The inline comment covers the one kept pin |
| group D | Rules 2 and 3 | no floor or control of its own | acceptable. They restate the redeclaration pin, which is floored and controlled |
| sweep 4, `MenuRows_AreUniqueAndLabelled` | Rule 3 | no planted duplicate | acceptable. The sweep scored Rule 3 *n/a* for this single-copy invariant, the register still says it plainly, and it records the 15.7 reviewer's duplicate-Camera mutation redding the fact |

The ROADMAP 15.7 block counts *"3 remain partial"*: rows 12, 15 and 38. That count is the 40 pin
facts only. The group partials and the row 4 note sit outside it, and the ROADMAP block names C, D
and E separately. The numbers agree once that scope is read.

### 4d. Why narrowly: Rule 4 is never assessed for the non-tree pins

The register's verdict column is headed **"Verdict (Rules 2, 3, 5, 7)"**, at `docs/pin-standard.md:410`.
No row carries a Rule 4 verdict. The sweep's columns are R2, R3 and R5. The 15.7 plan asked for
*"a per-rule verdict for Rules 2, 3, 5 and 7"* at
`docs/superpowers/plans/2026-09-26-phase-15.7-close-the-audit-gaps.md:253`. But the 15.4 design that
created the register specified *"its verdict against Rules 2–5 and 7"*, at
`docs/superpowers/specs/2026-09-24-phase-15.4-design.md:81`. Rule 4 dropped out between the two,
and no document gives a reason:
`grep -rn "Rule 4" docs/pin-standard.md docs/plans/2026-09-26-phase-15.7-*.md docs/superpowers/specs/2026-09-26-phase-15.7-design.md`
finds only the rule itself and two cross-references in the standard's Rule 3 text and checklist.

Rule 4, *"it must fail when its subject moves"*, is one of the **three** clauses the DoD names as the
standard's minimum. The tree-reader scorecard does assess it: census §3 has a Rule 4 column, and
every row reads *yes*.

**How much this matters in substance, from my own spot check, which is not a verdict.** The 16
files that hold non-tree pins reach their subjects mostly through compile-time symbols, so a rename
is a build break rather than a shrug. Per file,
`grep -c 'typeof('` and `grep -c 'nameof('` give, for example, `BnComponentTests` 28/129,
`BnFormControlTests` 22/92, `LayoutSurfacePinTests` 41/2 and `DefaultStructTrapSweepTests` 23/0.
The few string lookups either dereference with `!` or assert `NotNull`, so they throw or red. The
reflection sweeps carry named anchors, such as `BnView`, `CaptureOptions`, `FlexAlign` and the seven
named assemblies, which red if the subject leaves the swept set. So Rule 4 probably holds for most
of these pins. But *probably, from an auditor's grep* is not the per-pin verdict the DoD asks for,
and the standard's own point is that silence on a rule is the failure mode.

**Why this is MET NARROWLY and not NOT MET.** The DoD's unit is the **pin**: *"recorded per pin as
conforms, fixed, or exempt … An unassessed pin is a gap."* Every pin in both populations has a
recorded verdict. None of them is unassessed, which was the first audit's defect. What is missing is
one rule of the standard, across the whole non-tree register. That is an incomplete assessment, not
an absent one. A stricter reading, that *"assessed against that standard"* means every rule, would
make this NOT MET. I record that reading here so the owner can apply it. This is new finding N1.

## 5. The standard is enforced mechanically, or the absence is recorded — **MET NARROWLY**

**What reds mechanically:**

| mechanism | what it enforces | what it does not |
|---|---|---|
| `PinPopulationTests.NoTest_ReachesTheRepoTree_WithoutTheSharedHelper`, `PinPopulationTests.cs:98`, floored at `scanned >= 100`, `:155` | **Rule 6, reachability.** No test reaches the checkout except through `BnRepo`, so the tree-reader population stays enumerable | anti-vacuity: a pin can pass it and scan nothing |
| `PinPopulationTests.TheBypassMarkers_AreStillFoundInBnRepo_…`, `:197` | Rule 3 for the population guard itself | — |
| `ShellSourceRootsDriftTests`, 11 facts | coverage of the three shell-scanning pins against `src/shell-source-roots.json` | subtractive build calls, now #411 |

**Nothing reds a new pin that can pass while checking nothing.** I looked for anything else that reads
test sources: `grep -rln '"tests"' tests --include=*.cs` finds four files. `PinPopulationTests` is the
reachability guard above. `ShellSourceRootsDriftTests:1631` and `:1690` walk `tests/` to resolve
test-method **names** cited by the roster, not to judge assertions. `AuthSemanticsDriftTests` reads two named test files, itself and `ShellSourceRootsDriftTests.cs`, at `:1324` and `:1400`, to check its own ban wiring. `DocsNameDriftTests:419` lists `tests` as one root for resolving doc names. None checks anti-vacuity.

**The absence is recorded with its evidence**, in `docs/pin-standard.md` at `:532`, *"The enforcement
verdict — Rule 2 is NOT enforced mechanically, and will not be"*:

> *"Answer: no, and not for want of trying to find a way. Rule 2 is a review obligation carried by
> the checklist below. Nothing in CI checks it, nothing is planned to"*

with the measured reasons at `:554` (a presence-style check scores zero against the four real
defects), at `:592` (a binding-aware analyzer is decidable but sunk by cost and collateral), and a
Rule 5 disclosure on the verdict itself at `:648`.

**Why narrowly, unchanged from the first audit because nothing here changed.** The DoD's headline is
not delivered: it is met through its fallback clause. And the fallback reads *"if no mechanical form
exists"*, while the record says a form exists and was rejected on cost. It is recorded explicitly,
which the DoD requires, but it is *"not worth building"*, not *"does not exist"*. The one partial
mechanism enforces reachability, which the standard itself says at `:547` *"is not anti-vacuity"*.
`git log --oneline 1f2b3a7..origin/main -- tests/BlazorNative.Runtime.Tests/PinPopulationTests.cs`
is empty, so 15.7 changed nothing here, as it was not asked to.

## 6. #364 answered, not merely fixed — **MET NARROWLY**

`gh issue view 364 --json state,closedAt` gives **CLOSED 2026-09-24T04:41:58Z**. The timeline's
`closed` event carries commit `7aab286`:
`gh api repos/MarcelRoozekrans/BlazorNative/issues/364/timeline --jq '.[] | select(.event=="closed") | .commit_id'`.

**Where the coverage is written.** `src/shell-source-roots.json` has top-level keys `$doc`, `sets`
(7), `scanRoots` (3) and `consumers` (3: `AuthSemanticsDriftTests`, `AndroidLogDriftTests`,
`NSLogDriftTests`), read with a short `json.load`. Its `$doc` opens *"SHELL SOURCE ROOTS -- one home
for the answer to 'what is the shell's source tree'"*. The design table is
`docs/superpowers/specs/2026-09-23-phase-15.2-design.md:98`, *"The four holes, as consequences"*.

**The four holes, each checked on `main`:**

| hole | axis | closed by, on `main` | a consequence of the definition? |
|---|---|---|---|
| F1, half the Android shell unscanned | trees | the roster, plus `ShellSourceRootsDriftTests.EveryShellSourceFile_IsInsideADeclaredRoot` | yes |
| F2, caller count scoped to one file | trees | `AuthSemanticsDriftTests.TheTestOnlyCredentialBranch_IsStillGuarded`, whose comment says *"FILE SCOPE IS NOW THE ROSTER: the count below walks every `.kt` under every root src/shell-source-roots.json says this pin consumes"* | yes |
| F4, an import alias defeats dotted tokens | spellings | `AuthSemanticsDriftTests.NoShellSource_AliasesAnAuthenticatorNamespace` bans the construct | yes |
| F3, the stripper over-strips past a raw string | what the scan sees | a parse fix in `tests/Shared/CommentStrippedSource.cs`; the design calls it *"A real parse fix … with its own control"* | **no**, by the design's own words |

The file-kinds axis is `AuthSemanticsDriftTests.TheScannedExtensions_CoverEveryLanguageTheBuildCompiles`.
Located with `grep -ln "void <name>" tests/BlazorNative.Runtime.Tests/*.cs`.

**Why narrowly.** Coverage is written on all three axes the DoD names, and three of the four holes
close as consequences of it. F3 closed as a patch, which is what *"rather than as four patches"*
set out to avoid. **What changed since the first audit:** the two unfiled residuals are now filed as
**#411**, subtractive build calls, and **#412**, the template's unread `build.gradle.kts`; see
`gh issue view 411` and `gh issue view 412`. That makes the definition's known limits tracked, but it
does not change F3, so the verdict stays narrow.

## 7. #296 closed by the standard, red before fixed — **MET**

`gh issue view 296 --json state,closedAt,closedByPullRequestsReferences` gives **CLOSED
2026-09-24T17:35:03Z**, by PR **#384**, which is `66cd693` on `main`.

**The red**, on the commit that added the shared vector without the fix:

```bash
gh run view 36004048485 --json workflowName,event,headSha,conclusion
# android-instrumented, pull_request, 24917b401fe6d29025e57656a23b41409ac12060, failure
gh run download 36004048485 -n instrumented-test-results   # then sum testsuite tests= and failures=
# tests 228, failures 1:
# io.blazornative.shell.BnDeepLinkVectorTest.everyVector_parsesToTheSharedExpectation
# "deep-link vector: BLAZORNATIVE://settings expected:</settings> but was:<null>"
```

I downloaded the artifact and summed the JUnit XML myself. One failure, and it is the vector test,
so the red is not infrastructure.

**The green**, on `1587af42f7e8cc681c03390fc627dce941e09be8`:

| run | workflow | `headSha` | conclusion |
|---|---|---|---|
| [36030419938](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36030419938) | android-instrumented | `1587af4…` | success |
| [36030419936](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36030419936) | ios | `1587af4…` | success |
| [36030419937](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36030419937) | ci | `1587af4…` | success |

`git log --oneline -3 1587af4` gives `24917b4 → cfb147a → 1587af4`, so the red is the green's
ancestor, two commits back. All three commits are on PR #384:
`gh pr view 384 --json commits --jq '.commits[].oid'`.

**Through the mechanism:** the scheme's home is `src/deeplink-vectors.json`, read by
`DeepLinkSchemeDriftTests` (5 facts, census row 29) and `WireVocabularyCodegenTests`. What redded
was the shared differential vector running on Android, the behavioural half of the mechanism.

## 8. #357, #297 and #302 closed — **MET**

```bash
for n in 357 297 302; do gh issue view $n --json number,state,closedAt,closedByPullRequestsReferences; done
```

| issue | GitHub state | closed by | backed by |
|---|---|---|---|
| **#357** 14.1's dispatch completeness pin has no anti-vacuity assertion | **CLOSED 2026-09-25T16:43:18Z** | PR #408's merge, by accident | `d481dae` / #377: `DispatchSurfaceDriftTests.TheDispatchDeclarationScan_IsNotVacuous` |
| #297 pin RenderPatch subclasses against FrameEncoder | CLOSED 2026-09-24T20:01:23Z | PR #388 | `8ee4a9c`: `PatchKindDriftTests`, census row 30 |
| #302 release-please dropped a breaking-change commit | CLOSED 2026-09-24T20:01:24Z | PR #388 | `8ee4a9c`: `ReleaseParserVersionPinTests`, census row 31 |

**How #357 closed, measured.**
- `gh api repos/MarcelRoozekrans/BlazorNative/issues/357/timeline` shows a `referenced` event from
  commit `4b79cec`, which is #408's squash, and a `closed` event at the same second,
  2026-09-25T16:43:18Z. `gh pr view 408 --json mergedAt` gives 2026-09-25T16:43:17Z.
- #408's body contains a next-steps line made of the word *close* followed by the issue number 357, and in the same body *"This PR
  closes no issue … #357 is 15.7's to close."* GitHub's keyword matching read the first line. That
  is the accident.
- The timeline's earlier `referenced` event from `d481dae`, on 2026-09-23, did not close it.
- A `commented` event on 2026-09-26 opens *"**Evidence for this closure.** GitHub closed this issue
  on 2026-09-25 when #408 (the M15 audit) merged. That was accidental …"*.

**The fix is on `main` and meets the issue.** `DispatchSurfaceDriftTests.cs:381` declares
`TheDispatchDeclarationScan_IsNotVacuous`, flooring the **scanned** set per shell:
`MinimumKotlinDispatchDeclarations = 6` at `:342` and `MinimumSwiftDispatchDeclarations = 4` at `:350`.
`git show --stat d481dae` is `test(15.1): close the nine gaps the pin census found (#377)`.

**Every record states the same thing.** Closed 2026-09-25 by #408's merge through an accidental
keyword; the fix is `d481dae` / #377; 15.7 did not close it:

| record | line | says |
|---|---|---|
| census | `docs/plans/2026-09-22-phase-15.0-census.md:667–671` | *"FIXED BY 15.1 TASK 2, `d481dae` / #377 … closed on 2026-09-25 by the merge of #408, and only by accident … 15.7 did not close it."* |
| census | `:1168`, §7 row 2 | *"the fix is `d481dae` / #377 … The merge of #408 closed it on 2026-09-25, through an accidental"* closing keyword *"in that PR's body."* |
| ROADMAP | `docs/planning/ROADMAP.md:3091–3093` | *"#357: closed 2026-09-25 by #408's merge, through an accidental"* closing *"keyword … The fix itself is d481dae / #377 … Do not credit 15.7 with the close."* |
| 15.7 record | `docs/plans/2026-09-26-phase-15.7-record.md:234–244` | *"#357 was closed 2026-09-25T16:43Z by the merge of #408 … The fix itself was d481dae / #377, in Phase 15.1 … It is not credited with the close."* |
| MILESTONE | `docs/planning/MILESTONE.md`, phase line 8 | *"#357 found already closed by #408's merge and recorded truthfully"* |

Found with `grep -n "#357"` over each file. The earlier census claim of a 15.1 closure, which the
first audit found false, is struck through and corrected in place. No record now claims a closure
that GitHub does not show.

**The DoD says "are closed", and all three are.** The close of #357 was accidental, but the issue is
closed, its fix is on `main`, and every record says truthfully how it closed. That meets the item
and the audit spec's *closed on GitHub and backed by the commit*.

## 9. #291 answered for prose — **MET**

`gh issue view 291` gives **CLOSED 2026-09-25T10:51:05Z**, by PR **#399**. The work is `0bdeb84` / #397.

**The three pairs** are named by inference, and the inference is recorded at
`docs/superpowers/specs/2026-09-25-phase-15.5-design.md:44–45`: *"The 'three unpinned documentation
transcription pairs' are named nowhere in the record … By elimination they are **#365's**"* three
pure-documentation findings, F6, F7 and F8.

**Each is answered with its reasoning**, at `docs/planning/ROADMAP.md:2955–2969`:

- **F6:** *"Not pinned, not needed: the duplicate was removed, so the count now appears only in the
  assertion that holds it."* Checked:
  `git grep -n -iE "all nine|nine symbols|the 9 exports|nine exports" origin/main -- .github/workflows/`
  gives one line, `ci.yml:815`, *"all nine facts are TemplateDriftTests"*, which is about test facts,
  not exports.
- **F7:** *"Pinned: no. This is prose against prose about CI state — a test would only restate
  whichever side it chose, and the truth lives in GitHub's live branch protection."* Checked:
  `git grep -n -i "already-required" origin/main -- docs/GITHUB-SETUP.md .github/workflows/ios.yml`
  finds nothing.
- **F8:** *"Pinned: no, for the same reason as F7."*

**The wider answer** is the 20-page audit, `docs/plans/2026-09-25-phase-15.5-docs-audit.md`, and the
guards `DocsSamplesDriftTests` and `DocsNameDriftTests`, census rows 32 and 33, both conforming. CI's
"Docs samples compile" step succeeded on `592d2d5` in `ci` run 36231940154.

## 10. The small corrections land: #298, #356, #365 — **MET**

| issue | GitHub state | the change on `main` |
|---|---|---|
| #298 BnSwitch's XML doc names SwitchMaterial | CLOSED 2026-09-25T10:51:04Z by PR #399 | `0bdeb84` / #397. `src/BlazorNative.Components/BnSwitch.razor:6` now reads *"Android: Switch — the framework"* widget. Held by `TextCollapseParityDriftTests.ComponentDocs_NameTheAndroidWidgetClassTheShellActuallyBuilds` at `:357`, census row 26 |
| #356 iOS secure-storage legend claims Unavailable means not enrolled | CLOSED 2026-09-25T10:51:05Z by PR #399 | the legend copies corrected in `0bdeb84` and `ce0d714`. **The behaviour question was split to #396**, which is open and cites #356 in its body |
| #365 residual overclaims after 14.4 | CLOSED 2026-09-25T09:25:49Z | the timeline's `closed` event carries commit `0bdeb84` / #397 |

```bash
for n in 298 356 365 396; do gh issue view $n --json number,state,closedAt,closedByPullRequestsReferences; done
gh api repos/MarcelRoozekrans/BlazorNative/issues/365/timeline --jq '.[] | select(.event=="closed") | .commit_id'
gh issue view 396 --json body --jq .body | grep -o "#356"
```

## 11. No new public API, wire or ABI change — **MET**

```bash
git diff --stat 76728c9 origin/main -- 'src/*/PublicAPI.*.txt' src/wire-vocabulary.json    # empty
git ls-tree -r --name-only origin/main | grep -c 'PublicAPI\.'                             # 14
git show <rev>:src/BlazorNative.Runtime/Exports.cs | grep -cE '^\s*\[UnmanagedCallersOnly' # 10 at both ends
git show <rev>:src/BlazorNative.Runtime/Exports.cs | grep -o 'EntryPoint *= *"[^"]*"' | sort   # diff: identical
git diff 76728c9 origin/main -- src/BlazorNative.Runtime/Exports.cs                        # one line, VersionNumber
```

- **Public API:** the 14 `PublicAPI.*.txt` files show **no diff** across the range.
- **Wire:** `src/wire-vocabulary.json` shows **no diff**.
- **ABI:** the ten `EntryPoint` names are identical at `76728c9` and `origin/main`. The only change to
  `Exports.cs` is `VersionNumber = "0.15.0"` → `"0.16.3"`.

**Release-please commits in the range, by name, excluded explicitly.**
`git log --oneline 76728c9..origin/main --grep "chore(main): release"`:

| commit | release |
|---|---|
| `70d169f` | chore(main): release 0.16.0 (#362) |
| `6f25f15` | chore(main): release 0.16.1 (#380) |
| `18245fc` | chore(main): release 0.16.2 (#386) |
| `1b3f058` | chore(main): release 0.16.3 (#393) |

`git log --oneline 76728c9..origin/main -- src/BlazorNative.Runtime/Exports.cs` returns exactly those
four. No release commit landed after the first audit.

**What moved under `src/` after the first audit**, `git diff --stat bbef02a origin/main -- src samples templates`:

- `src/BlazorNative.Renderer/BlazorNative.Renderer.csproj`: `ZeroAlloc.Collections` 1.1.7 → **1.1.8**,
  from `e456584` / #410, a Renovate bump. It is not API, wire or ABI. It is a change in the Renderer
  package's dependency closure, noted for that reason, as the first audit noted #398's
  `ZeroAlloc.Inject` bump.
- `src/BlazorNative.Renderer/NativeRenderer.cs`: an XML `<summary>` on the internal
  `StyleAttributes` only, from `5b536f4` / #415, recording that the union is not pinned because it is
  a definition. Comment-only, on an `internal` member.

Everything else under `src/` in the range is what the first audit listed hunk by hunk: comment and
XML-doc corrections from 15.5, the intended #296 behaviour change in `MainActivity.kt`, the pin
manifests, generated test vectors, SwiftPM and Gradle tooling, and #398's dependency bump. I
re-listed it with `git diff --stat 76728c9 origin/main -- src | grep -v -i test` and found no file
the first audit did not account for, apart from the two above.

---

## Compared with the first audit

| # | item | first audit, 2026-09-25 | re-audit | what changed |
|---|---|---|---|---|
| 1 | phases complete | MET | MET | 15.6 and 15.7 completed: `1f2b3a7` #409, `592d2d5` #416 |
| 2 | tests, device lanes, `headSha` | MET | MET | counts .NET 1186 → 1200 from 15.7's controls and one retirement, `5b536f4` #415; lanes re-dispatched on `592d2d5` |
| 3 | written standard | MET | MET | the register section grew from one row to the full non-tree population, `5b536f4` #415; the three minimum rules are unchanged |
| 4 | every pin assessed | **NOT MET** | **MET NARROWLY** | `LayoutSurfacePinTests` and `DefaultStructTrapSweepTests` now have register rows; the 93 non-tree files were swept and every pin file has a row, `5b536f4` #415. Narrow on Rule 4, never assessed for the non-tree pins, and on census row 27a |
| 5 | enforced mechanically | MET NARROWLY | MET NARROWLY | nothing changed, and nothing was asked to |
| 6 | #364 answered | MET NARROWLY | MET NARROWLY | residuals 3 and 4 filed as #411 and #412 during 15.7; F3 is still a patch |
| 7 | #296 red before fixed | MET | MET | nothing; re-measured, including the JUnit XML of the red run |
| 8 | #357, #297, #302 closed | **NOT MET** | **MET** | #357 closed at #408's merge, `4b79cec`, by an accidental keyword; evidence comment posted in 15.7; census, ROADMAP and the 15.7 record corrected to say so, `5b536f4` #415 |
| 9 | #291 answered for prose | MET | MET | nothing |
| 10 | #298, #356, #365 | MET | MET | nothing |
| 11 | no API, wire or ABI change | MET | MET | one dependency bump, `e456584` #410, and one internal comment, `5b536f4` #415; neither is surface |

The first audit's other unfiled items are now tracked. Its §4b register row for `RouteMenuDriftTests`
now scopes Rule 2 per fact and gives Rule 3 a per-fact disposition, with controls added in 15.7. 15.2
residuals 3, 4 and 6 are #411, #412 and #413.

---

## Carried forward

Every issue below is open (`gh issue view <n> --json state`), filed before this re-audit, and outside
M15's DoD:

- **#395** ci(ios): the XCTest baseline counts passes with a one-line grep, and an interleaved log line breaks it. *CI hygiene on the device lane, not a pin.*
- **#396** iOS secure storage on an unenrolled device may return Ok where Android returns Unavailable. *Split from #356; needs a device run, which the milestone deliberately excluded.*
- **#401** Twin divergence: the iOS stderr pump re-gates by the shell's log level, Android's does not. *Logging behaviour, raised by the 15.5 docs audit.*
- **#402** An IMobileBridge replacement in ConfigureServices is not rejected, while INavigationManager is. *Runtime policy, not a pin.*
- **#403** BN0004's diagnostic message names only the Kotlin dispatch lane. *Analyzer wording.*
- **#404** CI's JDK step is named JDK 21 but provisions JDK 25; setup.ps1 installs 21. *CI and setup hygiene.*
- **#405** Compiled docs samples can still throw at render; add a render smoke. *Extends #291's guard beyond "compiles"; #291 is answered without it.*
- **#406** Pin: every baselined public type is tiered or NOT-API. *Blocked on the owner tiering 9 types.*
- **#407** Flaky: BnCameraAndroidTest times out loading the captured photo into BnImage after 15s. *A device-lane flake, not a pin.*
- **#411** Shell source roster: subtractive build calls pass the derivation check. *A known limit of #364's coverage definition, disclosed at the pin.*
- **#412** The template's build.gradle.kts is an unread second record of the shell source dirs. *Same family as #411.*
- **#413** Ten CS1570 malformed-XML doc-comment warnings in the test tree. *Test-tree hygiene.*
- **#414** Differential pin: cross-language ABI and wire constants, read from Kotlin, Swift and C. *The replacement for group A's goldens, which the register classes as not pins.*

Older open debt this milestone did not touch: #345 and #346, pinned by tests that assert the bug
still exists, and the owner-accepted hardening ledger, #8, #9, #12 and #13.

### New findings, for the controller to file

This re-audit files nothing itself. Each of these is new, and none is tracked by an issue today:

- **N1. The non-tree register never assesses Rule 4.** Its column is *"Verdict (Rules 2, 3, 5, 7)"*,
  `docs/pin-standard.md:410`, while the 15.4 design that created it asked for Rules 2–5 and 7. No
  document gives a reason for the omission. Rule 4 is one of the DoD's three minimum clauses. The
  fix is a Rule 4 cell per row, or a written reason why reflection pins satisfy it by construction.
  This is §4d, and it is why item 4 is narrow.
- **N2. Census row 27a overclaims per fact.** It scores
  `WireVocabularyCodegenTests.TheRenderersStyleSets_AreTheManifests` Rule 2 *yes*, **conforms**, while
  the census's own 15.7 addendum at `:1306–1313` names that fact as an unfixed per-fact Rule 2 gap.
  The spread table's *"0 gap"* inherits the overclaim. The fix is a floor on the fact, or a row
  split marking it partial. This is §4a.
- **N3. The sweep's groups G and H have no disposition.** Both are recorded copies with nothing
  comparing them. H is `BridgeHttpEndToEndTests.BuildHostGraph`, a hand mirror of
  `HostSession.EnsureSession`'s registrations whose own comment says it must be updated by hand. G is
  the goldens that Kotlin and Swift suites transcribe: `HelloGoldenTests`, `FlatJsonTests`,
  `BnLayoutDemoTests`, `BnScrollDemoTests`, `BnListWindowTests`, `BnFormDemoTests`, `BnModalDemoTests`
  and `BnSafeAreaDemoTests`. #414 covers group A only. This is §4b.
- **N4. Test-tree compiler warnings: 46 BL0006 and 2 CS8669** on a clean build, per the hand-off, 42
  of the BL0006 in `LayoutSurfaceSequenceBandTests.cs`, partly grown by 15.7's synthetic-frame
  controls. Not a DoD item and not in shipped `src`; the same kind of test-tree warning debt as #413.
  This is §2.
- **N5. `docs/planning/MILESTONE.md`'s Audit History table still reads *"(not yet audited)"*,** though
  the FAIL audit of 2026-09-25 exists. This is bookkeeping for `complete-milestone` to correct with
  both rows, the FAIL and this re-audit, rather than an issue.

---

## Verdict

**PASS WITH FINDINGS.** Eight criteria are MET, three are MET NARROWLY, and none is NOT MET.

The first audit's two failures are closed on evidence measured today. #357 is closed on GitHub and
backed by `d481dae`. Every record now says truthfully that it closed by accident, at #408's merge. The
non-tree population was measured rather than assumed: 93 files, the same 93 on `main` as at the
sweep's base, and every one of the 15 pin files and every pin fact has a register verdict. That
includes the two pins the first audit found unassessed, and the partials are named where they sit. The
tree-reader population is unchanged at 33 files and 170 facts, each with a scorecard verdict. Three
items stay narrow, and none of them is hidden. Enforcement is met through its recorded fallback. #364's
F3 closed as a patch. And item 4 is narrow for a reason this re-audit found and the first did not: the
register that assesses the non-tree pins has no Rule 4 column. Rule 4 is one of the three clauses the
DoD names, and nothing records why it was left out. My own spot check suggests those pins mostly
satisfy Rule 4 through compile-time symbols and named anchors, but that is not a per-pin verdict. Every
pin still carries a verdict, which is the DoD's unit, so this is an incomplete assessment rather than
a missing one. That puts the item at MET NARROWLY, with N1 to file. If the owner reads *"assessed
against that standard"* as every rule, this item and so the milestone would be FAIL. The verdict is
stated so that reading can be applied. On PASS WITH FINDINGS the spec runs `complete-milestone`,
with **no tag**, per `CONVENTIONS.md`.
