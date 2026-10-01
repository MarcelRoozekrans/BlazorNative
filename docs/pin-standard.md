# The pin standard

**What a drift pin must do to be trusted.**

This repo defends most of its invariants with **drift pins** — tests that read the repository tree
at runtime and assert that two copies of one truth still agree. The C ABI is frozen by them; the
wire vocabulary, the dispatch surface, the auth semantics, the logging seams and the README's own
count table are all held in place by them. They are the reason a green CI run means anything at
all.

They accumulated across a dozen milestones with **no shared standard**, each written to catch the
bug in front of its author, and the cost is on record. Milestone 14's newest pin was defeated by
**five successive reviews**, every round finding a shape the previous round had not tried, and
every fix was local to the instance. A comment stripper with a live hole was duplicated into
**six** copies; they were found by **three different people looking at three different things**,
and one of them was quietly defeating a security guard.

This document is the standard those pins lacked. It lives in the repo rather than in a milestone
doc on purpose — milestone docs get archived, and this has to outlive the milestone that wrote it.

Several of the rules below were **discovered by being wrong**, and each one keeps its scar
attached. A rule with its scar attached is one people follow.

---

## Rule 1 — A pin is defined by what it DOES, not by what it is called

**A pin is a test that reads the repository tree at runtime and asserts on its contents.** That is
the whole definition. It is not a naming convention, and any process that treats it as one will
undercount.

`Drift`, `Pin`, `Sweep` and `Roster` are all in use as class suffixes in this repo, so a
convention test keyed on one of them misses roughly a quarter of the population.

### Enumerate a population by behaviour, never by a naming convention

This is the rule that has been proven most expensively, and it is **four-for-four in a single
milestone**:

| The count | Counted by | The truth |
|---|---|---|
| "sixteen pins" | class-name suffix | four suffixes are in use; the count was short |
| "23 copies of `RepoRoot()`" | method name | `BnImageDemoTests.ShellSource` did the identical walk under another name — it was **24** |
| "25 callers" | prediction from the design | the call graph had **26** at the time of the migration |
| "the comment stripper" | the names `Strip` and `CodeLines` | **six** copies, found by three people looking at three different things |

Every one of those counts was wrong in the same direction, for the same reason, and every one was
corrected by counting **what the code does** instead: the walk expression, the call graph, the
behaviour of the helper. Counting by behaviour is the only method that has held up.

The practical consequence: when you write a guard over a population, define the population by an
expression the code must contain or a method it must call — never by a suffix, a prefix, or a
directory that authors are merely expected to use.

---

## Rule 2 — It must fail when it scans nothing

**Nothing in CI checks this rule. It is on you, at review time** — see *The enforcement verdict*
below for why a mechanical check was considered, costed and rejected. The four facts the 15.0 census
found that could pass while scanning nothing **all already carried an anti-vacuity assertion**, and
that is the whole of why the cheap check fails. Phase 15.1 closed all four; the argument is about
assertion shape rather than about those four, so it did not close with them.

**Vacuity is a property of the assertion shape, not of the input.** An assertion of the form *for
every X, assert Y* passes trivially when there are no X — and it does not matter whether X came
from a regex over source, a directory walk, a manifest parse, or an in-memory collection.

`RouteMenuDriftTests` scans **no files at all** and had the shape until 15.4 (#375):
`EveryRoutedPage_ExceptTheTwoExemptions_HasAMenuRow` passed over an empty page list. "It doesn't
read files" is not an exemption from this rule.

So every pin needs an assertion that its subject was actually present:

```csharp
Assert.True(scanned >= 100,
    $"scanned only {scanned} test files, and there are roughly 129 — the walk has stopped "
    + "seeing its subject, so the assertion below is checking almost nothing");
```

This is not a formality. It was **demonstrated**, not argued: `PinPopulationTests` was mutated to
scan for `*.nonsense` instead of `*.cs` with the anti-vacuity assertion removed, and it went
**green** — the offender list was empty because the loop never ran. The floor is the only thing
standing between that state and a passing suite.

### Corollary — a floor must be measured, and must not argue with build state

Two ways to get the number wrong. In this repo they turned out to be **the same incident**, which
is why the rule has two halves rather than one.

**A floor set against a wrong denominator is theatre.** A plan specified `scanned >= 20` believing
the population was ~151 files. That denominator was itself wrong: it counted `bin/` and `obj/`, so
it was never a count of test files at all — it read 129 on a clean checkout and 151 after a build,
moving with nothing more meaningful than whether someone had run `dotnet build`. Against the true
figure of ~131 hand-written files, `>= 20` is a guard that passes while seeing about **15%** of its
subject, and it would never have fired for any realistic breakage.

So the floor was **both** far too low **and** judged against a number that could not hold still.
Measure the true denominator first — print it from a deliberately-failing run if you have to —
then set the floor against *that*, and state the headroom you left.

**And a floor that moves for irrelevant reasons gets argued down rather than fixed.** Had the
`bin/`-polluted count survived into the pin, the guard's value would have depended on build state,
and a number that changes for a reason nobody can connect to a source change is one the next person
to hit it will weaken rather than investigate. Exclude build output, generated trees, and anything
else that moves without a source change. `PinPopulationTests` skips `/bin/` and `/obj/` for exactly
this reason, and was measured at **129** both before and after a full solution build — verified,
not assumed.

---

## Rule 3 — It must ALSO have a positive control

**This is the rule we nearly missed, and it is the one most existing pins fail.**

A count-based anti-vacuity floor proves that the **walk** works — that the pin found files. It
proves nothing at all about whether the **detector** works. A regex that no longer matches its
subject, a manifest key that was renamed, a marker list that silently narrowed: all of these leave
the file count untouched and the pin green forever.

**The two properties are different and a trusted pin asserts both.**

`NSLogDriftTests` is the worked example and the best pin design in the repo. It has two halves:
`BnHost/` must contain **zero** bare `NSLog` calls, and the exempt test bundle `BnHostTests/` must
still contain **some** — a fixed point that the detector is required to hit:

> *A non-empty file set proves the walk works; it does not prove the REGEX does. The exempt test
> bundle is the fixed point that proves the detector detects — it is the one place under
> `BnHostTests/` that MUST still contain live `NSLog` calls. Reword the pattern past its subject
> and this reds, instead of the pin quietly going green forever.*

A positive control does not need a second tree. Any of these work, in rough order of strength:

- **A known-matching subject** the detector must still hit — a sibling directory, an exempt file, a
  fixture the pin ships alongside itself.
- **A known-mismatching subject** the detector must still reject, if the pin's failure mode is
  over-matching rather than under-matching.
- **A structural assertion about the parse** — the manifest must yield exactly these keys, the
  regex must capture this many groups, the roster must contain this named entry.

`DeepLinkSeedDriftTests` is a positive-match pin by construction: it asserts a match exists rather
than that none does, so its detector is exercised on every run.

### The corollary — when the subject must be EMPTY by construction, the anchor is a FIXTURE

**The list above quietly assumes a fixed point exists somewhere in the tree. Sometimes none can.**

The scar is `ReleaseWorkflowPinTests.TheReleaseWorkflow_NeverOverridesTheVersion`. It asserts that
no build path in the release workflow overrides the package version, and **a live `-p:Version=`
cannot exist while the pin is true** — its subject tree is *required* to be empty of the pattern.
That is the structural difference from `NSLogDriftTests`, which works only because it has an
**exempt subtree** that must keep holding live violations. A pin with nothing exempt has no such
half to borrow.

Anchoring to the four places that *do* spell the shape was considered and refused: all four are
prose — a script comment, a setup doc, two archived plans, and `ci.yml`'s own narration — and a
control anchored to a sentence buys a false red on a docs edit. Rule 2's corollary about a floor
that moves for irrelevant reasons applies to a control just as hard: a guard that reds for a reason
nobody can connect to a real change is one the next person weakens rather than investigates.

**That refusal is about DIRECTION and DISTANCE, not a ban on prose — and it has to be said, because
this same phase shipped two controls anchored to prose.** The scar is that the paragraph above,
read literally, describes a tree that does not exist.

- **Forbidden: prose propping up an ABSENCE half.** Delete the sentence and the pin hands you a
  **green**. Nothing announces it, and the pin is then a comment claiming a safety property — the
  bug class this repo has paid for three times in one week. The Apple row in
  `ShellStyleTableDriftTests` is the worked example of what that looks like after the fact, and the
  fourth outcome below is what the phase did about it: disclosure and a named repair, rather than
  leaving the green standing.
- **Weak, not forbidden: prose on the POSITIVE half.** It can only ever cost a **false red**, and a
  false red gets investigated and re-pointed rather than silently believed. So it sits below all
  three anchor models listed next — reach for it when they are exhausted, not first — and it ships
  with a re-point instruction at the assertion saying what to do when the prose legitimately
  changes. `ComponentReferenceDriftTests`' tree anchor names three phrases that
  live in internal maintainer doc comments; `BnSafeAreaCoverageTests` anchors one
  `// #338 …: wrapped in BnSafeArea` line per demo page. Both are weak on purpose and both say so.
- **Distance is the tiebreak, and it is why `ReleaseWorkflowPinTests` still gets a fixture.** Its
  four candidate sentences live in a setup doc, two archived plans and a script comment — artefacts
  nobody touches *because of* the release workflow, so the red would arrive detached from any change
  to the pin's subject, which is precisely the "moves for irrelevant reasons" failure. The two
  controls above anchor inside the artefact their pin already scans, so an edit that removes the
  anchor IS an edit to the subject. **Where a fixture is available and the prose is remote, take the
  fixture.** That is a preference between a weak anchor and a strong one, and Rule 3's requirement —
  every detector has a fixed point it must still hit — is untouched by it.

**So the answer is a fixture — and what makes it worth anything is that it is driven through the
pin's own detector.** `Offenders` is one implementation called by both the pin and the control, so
the fixture provably exercises the production path rather than a restatement of it. That is Rule 8
paying for itself somewhere nobody designed it to. Build the fixture as close to the real thing as
it will go: this one splices the reference implementation's own `pack -p:PackageVersion=$VERSION`
into the **real** workflow text immediately above the **real** `dotnet nuget push` line, and demands
exactly one offender **at the line the splice landed on** — line fidelity, not merely a regex hit —
with the near-miss spellings `-p:VersionPrefix=` and `-p:VersionSuffix=` that a widened pattern must
still refuse.

**State what the fixture cannot buy, beside it.** It proves the detector recognises the shape; it
never proves the detector is pointed at anything real. That second half is the Rule 4 subject-moved
guard's job, and the two are only worth anything read together — which is exactly the distinction
the old *"THE POSITIVE CONTROL, first"* label collapsed.

### The order to look in — three anchor models, then disclosure

Phase 15.1 produced three, and they are in decreasing order of what they buy:

1. **A live subtree the scan deliberately excludes.** `NSLogDriftTests`' exempt test bundle is the
   original; `PinPopulationTests` is the same design in reverse, with `BnRepo.cs` — the one file the
   offender scan skips — as the one file the detector must still hit.
2. **The exclusion list itself, read as a source of anchors.** Broader than 1, because an exclusion
   does not have to be a *violation* — only something the detector must be able to see. This one
   kept being rediscovered and is worth stating outright: **anything a scan deliberately excludes is
   something the detector must still be able to see**, so the exclusion list is the *first* place to
   look, not the last. The
   census predicted a fixture would be needed in three cases and was wrong in two of them — the
   excluded Kotlin unit-test source set must still call both host-event seams; the unpublished half
   of the shipped XML still carries live instances of the banned prose patterns; the sample-app
   assembly the purity net does not scan is guaranteed to hold a match for every alternation. **"The
   anchor would have to be built" is a claim to re-check against the exclusions before you believe
   it.**
3. **A fixture, when the subject is empty by construction** — the corollary above.

And a fourth outcome that is not an anchor at all. **Sometimes none of the three is available, and
then the answer is disclosure plus a named repair, not a weaker control dressed up as a strong
one.** `ShellStyleTableDriftTests`' Apple row is the worked example: three of its four fixed points
are comment-derived and went trivially true the moment the parse started stripping comments, and the
fourth is removed by the outermost-depth filter. No same-depth live-code replacement exists, and
that was *measured* rather than assumed — Swift puts `case` at `switch` depth, so every live quoted
string in that body sits at least one level deeper than the arm labels, while Kotlin puts its `when`
arms level with a live guard and an `else ->` fallback, which is precisely why the Kotlin row keeps
full teeth. The row still rules out a comment-collecting extractor and no longer rules out a widened
arm grammar; both directions were verified by mutation. The repair is a fixture harness the parser
can be run against — **named, and deliberately not built in a fix round**, because that is a new
control design rather than a patch. It is written at the row, where the rows are.

**Be honest about where we are.** Every anti-vacuity assertion added during the milestone that
wrote this standard — including its author's own floor of 100 — proves only the walk. That **was** a
real gap across the existing population, and closing it is why phase 15.1 exists: the census found
nine uncontrolled detectors, every one of them an absence assertion, and all nine now carry a fixed
point. The rule stays at full strength anyway, because the next pin will arrive without one — and
because one of those nine controls lost most of its teeth on one of its three rows to a fix landed
in the same phase, which the paragraph above says out loud rather than averaging away.

---

## Rule 4 — It must fail when its subject moves

A pin whose subject has been renamed, restructured or deleted must **red**, not shrug.

The failure modes are ordinary rather than exotic: a file gets renamed in a refactor, a manifest
grows a nesting level, a method signature the regex anchors on is reformatted onto two lines, a
directory the walk depends on is moved into a subproject. In every case the honest answer is *this
pin no longer knows what it is guarding*, and the honest response is to fail and be re-pointed
deliberately.

The pattern the repo already uses:

```csharp
Assert.True(match.Success,
    $"could not find the {name} pin in {file} (pattern: {pattern}). It moved or was "
    + "rewritten — a pin that cannot see its subject must never pass vacuously, so this "
    + "reds. Re-point it deliberately.");
```

`BnRepo.Root()` throws rather than returning a plausible-but-wrong directory for the same reason.
So does `NSLogDriftTests` when `BnHostTests/` is absent: *"either the test bundle moved — then
re-point the exemption deliberately — or it is gone, in which case delete the exemption rather
than keeping it as folklore."*

Note the shape of a good message: it names the subject, names the pattern, says what probably
happened, and tells the reader to re-point rather than to delete. A pin that reds with
`Assert.NotNull(dir)` and no message teaches the next person to reach for the delete key.

---

## Rule 5 — It must state what it does NOT cover

**A pin that implies completeness it lacks is worse than one that admits a gap**, because the gap
then gets treated as covered ground by everyone downstream.

Write the limit in the pin's own doc comment, in the terms a future reader will need: which trees,
which spellings, which file kinds, which failure shapes are out of reach. `PinPopulationTests`
catches the **accidental** bypass — the copy-pasted walk — and not a determined one; a bypass built
by string concatenation, or one reaching `tests/` through
`Assembly.GetExecutingAssembly().Location` and a fixed path climb, contains neither marker and
stays invisible. That is written down where the pin is, with a worked demonstration, rather than
inferred.

### The subtle half — re-draw the limit when a demonstration moves it

Disclosing a limit is not a one-time act. **When someone shows you the boundary was optimistic,
move the boundary — do not defend where you drew it.**

The scar: an author disclosed that markers inside string literals could evade the scan and filed
that under *adversarial*. A reviewer then produced a marker in **live code** made invisible by an
ordinary URL in an unrelated literal on the same line. The author's own correction is the rule:

> *"I filed markers-inside-string-literals under 'adversarial'. The reviewer's line puts that
> boundary in the wrong place — a marker in **live code** was invisible because of a URL in an
> unrelated literal on the same line. **Ordinary, not adversarial. The stripper was the weak part,
> not the marker list.**"*

The limit was real; its placement was wrong; and the disclosure had made the wrong placement look
considered.

### A disclosed safe limit is not the same thing as an undisclosed unsafe one

Not every unhardened helper is a defect, and this distinction matters more than a blanket rule.

`TemplateDriftTests.StripLineComments` is a deliberately simple stripper with no string-literal
awareness, and it is **fine**, for three reasons that must all hold together:

1. **It is disclosed in its own doc comment**, with the conditions under which it would need
   revisiting — *"if that body ever grows either, this stripper is the thing to revisit"*.
2. **Its scope is bounded by inspection, not by luck** — it is applied to a four-statement method
   body that contains no string literal and no block comment in either copy.
3. **It fails safe.** Over-stripping costs the pin text and reds on a missing call. It can produce
   a false red. It cannot produce a false green.

Change any one of those and it becomes the other thing. Six copies of a *different* stripper were
undisclosed, unbounded, and failed **unsafe** — a false green over a tree that genuinely contained
a bare undeclared `NSLog` in the biometrics file, on a guard whose own header notes it has been
handed keychain keys.

**The direction of the failure is the question.** A limit that can only cost you a red is a
footnote. A limit that can hand you a green is a defect, whether or not it is written down.

---

## Rule 6 — It must reach the tree through `BnRepo.Root()`

Any pin that reaches the checkout must go through `tests/Shared/BnRepo.cs`:

```csharp
string root = BnRepo.Root();
```

This is not style. **Callers of that method are the pin population, exactly**, and that is what
makes the population enumerable at all — Rule 1 having established that the names cannot be. A
test that walks to `BlazorNative.sln` by hand is invisible to enforcement: it will not appear in a
census, a conformance sweep, or any future guard written over the population.

`PinPopulationTests` enforces this — and **this rule is the only one in this document that a machine
checks.** It enforces *reachability*, which is not anti-vacuity: a pin can route through
`BnRepo.Root()`, pass `PinPopulationTests`, and still scan nothing. It scans every hand-written
`.cs` file under `tests/` for the
two ways a test could reach the tree unaided — the `BlazorNative.sln` sentinel and
`AppContext.BaseDirectory` — and reds naming the offender. Two files are excluded **by name, never
by pattern**: `BnRepo.cs`, which is the one permitted implementation, and `PinPopulationTests.cs`
itself, because nothing can scan for a string it is forbidden to contain.

If you have a legitimate need for one of those markers, **give it a home in `BnRepo` rather than
an exemption**. `BnRepo.TestBinaryDirectory()` exists because `PackagePurityTests` genuinely needed
its own build output rather than the repo tree; routing it through the helper kept the marker list
at its original width and added no exemption. That is the shape to copy.

Two consequences worth stating, since both have surprised someone:

- **Borrowing a neighbour's helper is no longer possible**, and that is deliberate.
  `ComponentReferenceFixture` and `RendererNodeTypeMap` no longer expose a repo-root accessor.
  `BnRepo.Root()` is the only door.
- **Reaching the tree does not by itself make a test a pin, and the unit of classification is the
  FACT, not the file.** A pin compares two copies of one truth. A golden assertion checks a value
  against a recorded expectation. Both are worth having and they are not the same thing, so the
  caller list is a complete *population* and not a finished *classification*.

  `BnImageDemoTests` is the case that proves the unit. It is named as a golden and mostly is one —
  its mount golden, its frame-table arithmetic and its fixture-contract facts assert against
  `BnImageDemo`'s own constants and **never touch the checkout at all**. But two of its facts,
  `TheAndroidFixtureServer_ServesExactlyBnImageDemosNaturalPixelSizes` and its iOS twin, compare
  four C# constants against the Kotlin and Swift fixture servers' own declarations — in the file's
  own words, *"three copies of four numbers, pinned rather than trusted"*. Those two are drift
  pins by every rule here, down to `KotlinIntConst` and `SwiftIntConst` failing loudly when a
  constant is renamed rather than passing over the miss. **Classifying that file as one thing, in
  either direction, gets it wrong.**

  The contrast is `TextCollapseParityDriftTests`, which is a pin end to end and one of the
  strongest in the repo: it derives the Android shell's text-bearing node types by parsing the
  Kotlin widget factory, derives the harness's answer **behaviourally** by mounting a probe rather
  than reading a private field, carries two named vacuity guards and a separate anchor fact holding
  the Kotlin predicate to the shape the derivation assumes, checks the template mirror in the same
  pass, and writes down the three things it cannot cover.

**What this population is, and what it is not.** Callers of `BnRepo.Root()` are the population of
pins that read the tree — *tree-readers*, by construction. That is the only population a machine can
enumerate here. It is **not** every pin. A test that compares two in-memory collections can hold two
copies of one truth and red when they diverge, which makes it a pin by every other rule in this
document, while never calling `BnRepo.Root()`. No derived key finds those: *compares two copies of
one truth* is the binding problem that sank mechanical Rule 2 enforcement, and a marker attribute
would be declared rather than derived, the pattern this milestone rejected four times.

**Decided in 15.4 (owner, 2026-09-24): they are listed by hand, below.** The cost is stated rather
than hidden: **this register grows only when someone remembers to add to it.** A new in-memory pin
that nobody lists is invisible to every mechanism here, exactly as `RouteMenuDriftTests` was until
the 15.0 census walked past it.

#### The register — pins that do not read the tree

**15.7 measured this population instead of waiting for someone to remember it.** The sweep record,
[`plans/2026-09-26-phase-15.7-nontree-sweep.md`](plans/2026-09-26-phase-15.7-nontree-sweep.md),
read every test file that holds a fact and never reaches the tree: 93 files, 15 of them holding
pins, 40 pin facts. Every one of those 15 files has a row below, and the "sweep rows" in each
first cell are that record's row numbers. The verdicts are per rule and **per fact where the facts
differ**, and they describe the code as it stands after 15.7's fixes, not as the sweep found it. A
verdict marked *partial* names what is still missing. Rule 7 cites mutations recorded somewhere a
reader can find them. Every 15.7 mutation table lives in
[`plans/2026-09-26-phase-15.7-record.md`](plans/2026-09-26-phase-15.7-record.md), transcribed there
from the phase's now-deleted SDD reports; the row cites it rather than a commit message. The last
four rows record the sweep's ambiguous groups that 15.7 decided.

**Rule 4 was added to every row in 15.8.** 15.7 assessed Rules 2, 3, 5 and 7 only, and the
milestone re-audit ruled that silence on Rule 4 — one of the three clauses the milestone's
definition of done names — is not an assessment against the standard. Each Rule 4 cell now names
the mechanism, checked in the code, that makes the pin red when its subject is renamed, moved or
deleted. Where no such mechanism can be shown, the cell says *partial, named* and says what moving
the subject would do. The 15.8 re-audit review also found one pin the sweep missed,
`BnActivityIndicatorTests`, which now has its own row; its provenance is an addendum in the sweep
record. The 15.8 mutations are in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md), under
*15.8 re-audit review*.

**16.1 added the rows after group B, and they have a different shape.** Most 16.1 pins do not compare
two copies of one truth. Each pins a **safety claim** that a comment used to make with nothing
behind it: "the export does not block", "no frame reaches the host after shutdown", "a late fault
is not lost". The standard applies to them unchanged, so each has a row. For these pins Rule 2
means an **anchor**, because there is no population to floor. The anchor proves the scenario
really happened, for example that a host call was really open or a Task really pending.

The 16.1 facts that DO read the tree are not listed here. They are
`WireVocabularyCodegenTests.TheHostCallOps_KeepTheirFrozenIds`,
`…TheEmittedHostCallOps_MatchTheManifest_InAllThreeLanguages` and
`GeneratedSymbolShadowTests.TheOpConstantShadowDetector_…`. They reach the tree through
`BnRepo.Root()`, so they are in the census `PinPopulationTests` enforces. Their Rules 2–5 cells,
written in 16.6, are in [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §8, pins 1 to 3, and
the mutations that redded them are in its §2 and §7. Since 16.6, census row 27a counts the two
`WireVocabularyCodegenTests` facts.

`CameraAbiUnchangedTests`' new `FaultNotice == 5` line belongs to group A: it is a golden, not a
pin. Each Rule 7 cell describes its mutations inline, because the phase's SDD reports are
gitignored. The phase's notable mutations are also listed in the 16.1 outcome block in
[`planning/ROADMAP.md`](planning/ROADMAP.md).

**16.2 added the six rows after the 16.1 rows, with the same shape.** Each pins a claim about back
and navigation leaving the main thread: the back state reaches the shell before the frame that
shows its page, a back .NET cannot handle is never swallowed, and no main-thread entry point waits
on .NET. Two of the six rows are device suites, the instrumented `BackAndroidTest` and the XCTest
`BnBackOffMainTests`. Neither can run on the machine that wrote them. Until 16.6 their Rule 7 cells
listed the mutations the phase's final review should run and recorded none, because a green lane
run proves the suite passes, not that it can fail. 16.6 ran most of those mutations on the lanes.
Both rows now cite its record and name the ones that were not run. The 16.2 facts that read the tree are in
the census, not here: `GeneratedSymbolShadowTests`' arm-only consumption rule and its two new
controls, and `DispatchSurfaceDriftTests.MethodsWithADeclaredVisibility_MatchBothShells`. Rows 13a
and 14a of the [15.0 census](plans/2026-09-22-phase-15.0-census.md) record what changed. The
phase's mutations are also listed in the 16.2 outcome block in
[`planning/ROADMAP.md`](planning/ROADMAP.md).

**16.6 ran the mutations that M16's audit found missing, and these rows cite its record.**
[the 16.6 record](plans/2026-10-01-phase-16.6-record.md) ran one mutation per run against the 39 pins
the audit found never seen red, or never assessed. The device runs went to `ios.yml` and
`android-instrumented.yml`, each on its own scratch ref with its `headSha` and attempt 1 checked;
the rest ran locally. Its §8 holds one cell per pin and rule. **A cell here claims no more than the
§8 cell for the same pin:** where §8 says *partial, named*, so does the row, naming the same gap.
16.6 rewrote the rows for `BnFaultNoticeTests`, `BnDispatchLaneTests`,
`DispatchHostEventAndWaitVisibilityTest`, `BackAndroidTest` and `BnBackOffMainTests` from it. It
also added three rows. The 16.2 caller scan and its control read the tree, but no census or
register row had assessed them. The 16.3 slow-handler pins, `SlowHandlerWarningTests` and the JVM
`SlowHandlerProbeTest`, had no row either.

| Pin | What it compares | Verdict (Rules 2–5, 7) |
|---|---|---|
| `RouteMenuDriftTests` (sweep rows 1–4) | `SampleAppPages.All` ↔ `BnDemo.Destinations`, both directions, with two asserted exemptions | **Rule 2 conforms, per fact, since 15.7** — the two comparison facts got per-fact presence floors in 15.4 (#375); since 15.7 the shared floor is the measured size of each side (14 routed pages, 12 menu rows), and `MenuRows_AreUniqueAndLabelled`, which had no floor at all and passed on an empty menu, now carries it too. `TheTwoExemptions_…` asserts presence with `Single`/`Contains`, so it cannot pass on empty input. **Rule 3, per fact** — the two comparison facts are absence detectors, and since 15.7 each has a positive control that plants a defect through the same extracted helper: `Missing_ReportsAPlantedGhostRoute` and `Dangling_ReportsAPlantedDanglingRow`. `TheTwoExemptions_…` is a positive-match fact, so its detector runs on every pass. `MenuRows_AreUniqueAndLabelled` has a named anchor row, `Camera`, but **no planted duplicate**, so its uniqueness check has no control. **Rule 5 conforms since 15.4** — the header carries a Rule 5 block. **Rule 4 conforms, assessed in 15.8.** Both subjects are compile-time symbols, `SampleAppPages.All` and `BnDemo.Destinations`, so a rename or deletion is a build break. A page or row that moves out of either list reds the measured floors of 14 routed pages and 12 menu rows, and a moved `Camera` row reds its named anchor. The exemption fact finds `/` and `/settings` with `Single` and `Contains`, which red if either route moves. **Rule 7**: M1–M4 recorded in 15.4 (M1 empties `Destinations`, M2 empties `RoutedPages()`, M3 removes one fact's floor alone, M4 adds a ghost route); 15.7 adds P3–P5 (an empty menu reds `MenuRows_…` on its floor; each helper returning empty reds its control) and the reviewer's own duplicate-Camera-row mutation, which reds `MenuRows_AreUniqueAndLabelled` alone — all in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md). |
| `LayoutSurfacePinTests` (sweep rows 5–11, group D) | `ItemParameters` (17) and `ContainerParameters` (9) ↔ what `BnLayoutItem` and `BnLayoutContainer` declare. Every exported non-abstract component ↔ the two-entry `AllowedNonLayoutComponents`. Every type deriving from `BnLayoutItem` ↔ the surface it must not redeclare. `BnList<>` and `BnView`'s own names ↔ hand lists | **Rule 2 conforms since 15.7, except group D.** The derivation sweep is floored at the measured 16 components and the redeclaration sweep at the measured 15 derived types, both with 0 headroom and `BnView` as the named anchor. The exact-set facts compare against non-empty literals, and the allowlist fact asserts its count of 2. Since the 15.7 final review, `RazorEmitters_GrowingItIsADeliberateAct` holds the `RazorEmitters` list at exactly 4, set-equal to a reasons dictionary that argues each member. That list is the band pin's skip list and the collision pin's splat excuse, so before the anchor a one-line `typeof(BnView)` silenced two pins for `BnView` with nothing red. The three group D facts, `BnImage_TakesTheItemSurfaceFromTheBase…`, `RazorEmitter_…` and `BnFlexPreset_…`, assert an empty `Intersect` and have no floor of their own. They are non-vacuous only because `BnLayoutItem_DeclaresExactlyTheItemSurface` holds `ItemParameters` non-empty, which the header states. **Rule 3 conforms since 15.7.** The exact-set facts run their detector on every pass. `NonLayoutDetector_WithTheAllowlistRemoved_FindsExactlyTheTwoArguedExceptions` runs the derivation detector with no allowlist and demands exactly `BnList<>` and `BnModal`. `RedeclarationDetector_ReportsTheShadowingProbe` runs the in-file `ShadowingHeightProbe` through the redeclaration detector. It proves the detector, not the population filter, since the probe does not derive from `BnLayoutItem`, and the floor covers the population. Group D has no control of its own and restates the redeclaration pin. **Rule 5 conforms since 15.7.** The file had no header before 15.7. Its block now says: names only, not types; exported types only; a stale allowlist entry naming a non-exported type is unseen; group D restates the item surface; declaration, not behaviour. **Rule 4 conforms, assessed in 15.8.** Every subject is reached through `typeof`: `BnLayoutItem`, `BnLayoutContainer`, `BnView`, `BnImage`, `BnFlexPreset`, `BnList<>` and the four `RazorEmitters`, so renaming or deleting one is a build break. A parameter renamed or moved on either base reds the exact-set comparisons against the non-empty `ItemParameters` (17) and `ContainerParameters` (9) literals. Components that move out of the swept assembly red the measured floors of 16 and 15 and the `BnView` anchor. The two string lookups, `GetProperty("Height")!` on `BnList<string>` and `BnLayoutItem`, dereference with `!`, so a moved property throws. Group D reaches its three types through `typeof` too. **Rule 7**: 15.7's L7, L8, L9 and L9b each red the target fact alone. The final review's M3, `typeof(BnView)` added to `RazorEmitters`, survived before the anchor and reds it after. Both tables are in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md). |
| `LayoutSurfaceSequenceBandTests` (sweep rows 12–15) | What each layout component EMITS when mounted ↔ the sequence rules and bands `BnLayoutItem` declares normative. Its own emission rosters `ItemNames` and `ContainerNames` ↔ `LayoutSurfacePinTests`' rosters | **Rule 2 conforms since 15.8; it was partial through 15.7.** Since 15.7: the collision sweep asserts at least one attribute region per row. The band sweep asserts a non-empty root attribute run for every row that declares a parameter. The container emission fact, which returns early on non-containers, has its rows floored at the measured 4 in `TheContainerRows_AreThereToBeChecked`, with `BnView` as the anchor. Through 15.7 the number of theory rows was floored only by xUnit's refusal of empty `MemberData`, so at least 1, and the 15.8 review showed what that cost: a container-only filter dropped 10 of the 14 rows with every fact green. Since 15.8 `TheLayoutRows_AreThereToBeChecked` floors the rows at the measured 14, no headroom, with `BnText` as the leaf anchor. **Rule 3 conforms since the 15.7 final review.** `TheEmissionRosters_AreExactlyTheDeclaredSurface` is exact equality, and it chains the roster, the file's third hand copy of the surface, to the declaration. The emission facts check presence per name. Since 15.7 the collision and band detectors are extracted, and each has a control that feeds synthetic `RenderTreeBuilder` frames through the same helper. The band control plants one offender per detector arm: `left` at 100 for the item arm, `padding` at 5 for the container arm, `ChildContent` at 150 for the ChildContent arm, and `fontSize` at 18 for the fallback. 150 is inside the fallback's 100+, so it also reds if the ChildContent arm is deleted outright. Until the final review the control planted only `fontSize` and `left`, and the container and ChildContent arms could be set to always-pass with every fact green. **Rule 5 conforms since 15.7.** The header block says: the wire half of the rosters is not pinned here, and it fails safe; one mount with sample values only; root-contiguous runs only; `BnList<>` and `BnModal` are outside; presence, not value; the exemptions, whose list is held at 4 by `LayoutSurfacePinTests.RazorEmitters_GrowingItIsADeliberateAct`. **Rule 4 conforms since 15.8.** The rows are the exported `BnLayoutItem` components of `typeof(BnLayoutItem).Assembly`. Since 15.8, `TheLayoutRows_AreThereToBeChecked` floors them at the measured 14 with `BnText` as a named leaf anchor, and `TheContainerRows_AreThereToBeChecked` floors the containers at 4 with `BnView`. Rows that move out of the assembly or the filter red, which the 15.8 review showed they did not before: a container-only filter dropped 10 of the 14 rows with every fact green. `TheEmissionRosters_AreExactlyTheDeclaredSurface` holds `ItemNames` and `ContainerNames` equal to the declared surface, so a renamed parameter reds there. The frame accessor `GetCurrentRenderTreeFrames` is looked up by string and dereferenced with `!`, so a Blazor-side rename throws on first use. **Rule 7**: the 13.0 conclusion records `fontSize` 100→18. 15.7's L1–L6b are in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md): L4, the vacuity contrast, stays green with the container floor deleted. L1 and L2 red more than the roster fact, and L1b, a duplicated roster entry, reds it alone. The reviewer's own region-filter mutation against the row-12 collision detector reds 15 facts, proving the row-12 per-row floor load-bearing. The final review's M1, each of the container and ChildContent arms forced to `true`, survived before the new plants and reds the band control alone after. The 15.8 review's R2, the container-only filter, survived before the new floor and reds it alone after; it is in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md), under *15.8 re-audit review*. |
| `DefaultStructTrapSweepTests` (sweep rows 19–20, group F) | The rule "an all-optional record struct with a meaningful default must give `new T()` its declared defaults" ↔ every public value type in Core, Runtime, Device and Components | **Rule 2 conforms, on a population of one.** `examined >= 1` is the measured count, 0 headroom, and a second assertion requires `CaptureOptions` to be both swept and examined. **Today the sweep examines exactly one struct, `CaptureOptions`.** The floor comment says so, and since the 15.7 final review so does the Rule 5 block. **Rule 3 conforms since 15.7.** Before 15.7 the sweep checked only that an explicit parameterless ctor existed, so `: this(Value: 0)` passed. Now `Offenders` compares `new T()` with the primary ctor called on its declared defaults, as values. `TheSweep_Detects_TheTrap_AndTheWrongValue_AndClears_TheFix` drives that same helper over the Trapped, Fixed, WrongValue and RequiredField fixtures. `TheVisibilityFilter_SeesNestedPublic_AndSkipsAHiddenChain` controls the visibility filter. `Activator_RunsTheExplicitParameterlessCtor_AndZeroInitsWithoutOne` proves the premise the comparison stands on. **Rule 5 conforms since the 15.7 final review.** The block covers: one type examined today, `CaptureOptions`, so the sweep guards against a new trapped type more than it covers the existing surface; enum-typed parameter defaults are unverified, since `ParameterInfo.DefaultValue` may be the underlying integer, and this is latent; the four assemblies only; the most-parameters primary-ctor heuristic and its unspecified tie order; record structs in effect, since the comparison uses `Equals`; defaults held in field initialisers are never examined; `default(T)` is unguarded. No fixture covers the enum-default path. Group F, `NewCaptureOptions_CarriesTheDocumentedDefaults_NotZero`, stays as a named anchor that the sweep now implies. **Rule 4 conforms, assessed in 15.8.** The four swept assemblies are reached through one `typeof` anchor each, `IMobileBridge`, `BlazorNativePage`, `IGeolocation` and `BnLength`, so a renamed anchor is a build break. `CaptureOptions` is referenced by `typeof` and must be both swept and examined, so moving it out of the four assemblies, or reshaping it past the all-optional filter, reds the second floor assertion. **Rule 7**: 15.7's S1, S2, S3 and S6a are in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md), along with the Activator proof. S3 cannot red the production fact, because there are 0 nested public structs in the four assemblies, so only the fixture contrast proves the nesting branch. V4 shows the floor is load-bearing. The reviewer's own mutation of the enclosing-chain check reds exactly the visibility control. |
| `BnLengthTests.cs`, class `LengthParameterNullabilityPinTests` (sweep rows 21–22) | Every `[Parameter]` in Components that mentions `BnLength` or `BnAutoLength` ↔ the rule that it must be exactly `BnLength?` or `BnAutoLength?` | **Rule 2 conforms since 15.7.** Both facts floor at `MeasuredLengthParameters = 22`, 0 headroom. Before 15.7 the nullability fact leaned on its sibling for its floor. The reach fact carried a stale 18, which left 4 unstated headroom since 14.2. It names one anchor from each of the four declaring types. **Rule 3 conforms since 15.7.** `TheDetector_RejectsABareLength_AndClearsTheNullableShape` runs `LengthShapeFixture` through the fact's own `LengthParametersIn` and `Offenders`. The float must not be swept, and exactly Bare, BareAuto and Wrapped must be rejected. **Rule 5 conforms since 15.7.** Public types only; `[Parameter]` properties only; float "lengths" such as `BnList`'s Height and ItemHeight are out of scope by design, so a new bare-float length is invisible. **Rule 4 conforms, assessed in 15.8.** The sweep is floored at the measured 22 and carries four named anchors, `BnLayoutItem.MinWidth`, `BnLayoutContainer.Gap`, `BnModal.ContentWidth` and `` BnList`1.Width ``, asserted with `Contains`, so renaming or moving any of them reds by name. The swept assembly is reached through `typeof`. **Rule 7**: the 13.1 conclusion records a bare `BnLength` on MinWidth. 15.7's S4, S6b and V2 are in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md). |
| `ParameterBindingFaultTests` (sweep rows 28–29) | `BlazorInterop.ParameterBindingFrames` ↔ the Blazor assembly by reflection, and ↔ a hand two-element list | **Rule 2 conforms since 15.7.** `TheAllowListedFrames_StillNameRealBlazorMethods` counts what it resolves and floors that at the measured 2. Before 15.7 an empty allow-list passed it. `TheAllowList_IsTheBindingWriter_…` is an exact literal. **Rule 3 conforms.** The first fact is a positive match per frame, and the second is exact equality. **Rule 5 conforms since 15.7.** The first fact's doc says it checks that each name still resolves. It does not check that the name is still the throw site, which `ABadParameterBinding_AbortsTheMount_WithTheExistingRc2` covers. It does not check the signature either: any overload with the name passes. **Rule 4 conforms, assessed in 15.8.** Each allow-listed frame is a string, resolved against the Blazor assembly with `Assert.True(type is not null, …)` and `Assert.True(overloads.Length > 0, …)`, so a Blazor rename reds naming the frame, and the resolved count is floored at 2. The second fact compares the compile-time symbol `BlazorInterop.ParameterBindingFrames` with an exact literal. **Rule 7**: 15.7's S6c and V1 are in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md). |
| `BnComponentTests` (sweep rows 32–35) | Rows 32–34: `BnView`'s parameters, minus the stated exclusions ↔ `BnRow`, `BnColumn`, `BnScroll` and `BnImage`. Row 35: the Flex, Image or Bn prefix rule ↔ the exported enums of Components | **Rows 32–34 conform on Rules 2, 3 and 5.** They compare exact sets, 32 by `Name: Type`. 33 and 34 carry the exclusion-list control, every excluded name must exist on `BnView`, and block comments state that declaration is not forwarding. **Row 35 conforms on all three since 15.7.** The enum fact is floored at the measured 8 with `FlexAlign` as the anchor. `ThePrefixDetector_RejectsABareEnumName_AndClearsAPrefixedOne` runs a planted `Align` and `BnPlantedAlign` through the extracted `UnprefixedEnums`. Its Rule 5 says: Components' exported enums only; Core's unprefixed `CameraStatus` and `GeolocationStatus` are out of scope; enum type names only. **Rule 4 conforms, assessed in 15.8.** Rows 32–34 reach their types through `typeof` and their names through `nameof`, so a rename is a build break, and they compare exact `Name: Type` sets over non-empty sides. The exclusion-list control asserts every excluded name still exists on `BnView` with `NotNull(GetProperty)`, so a moved exclusion reds. Row 35's enum sweep is floored at 8 with `typeof(FlexAlign)` as the anchor. **Rule 7**: none is recorded for 32–34 as pins. The 6.1 Gate 1 mutations exercised the forwarding facts, not these. For 35, 15.7's S5, S6d and V3 are in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md). |
| `StyleAttributePartitionTests` (sweep rows 37–40) | The Yoga half ↔ the Visual half of the SetStyle routing table. Named box and paint names ↔ the half that owns each | **Row 37, `StyleAttributes_AreExactlyTheUnionOfTheYogaAndVisualHalves`, was RETIRED in 15.7 as a tautology.** `NativeRenderer` defines `StyleAttributes` as that union. Every other candidate pair is a generated table against its own source, which `WireVocabularyCodegenTests` already pins. The provenance is written where the fact was, following the NavigationTests 7.6 precedent. **Row 38, `YogaAndVisualStyleAttributes_AreDisjoint`: Rule 2 conforms since 15.7.** Each half is floored at its measured count, 26 and 3. Those constants copy a generated count on purpose, so removing a style name moves the floor in the same change. **Rule 3, partial.** It is an absence detector, overlap empty. No planted overlap is run through the `Intersect`. Rows 39 and 40 anchor named names in each half and assert each one is absent from the other half. **Rule 5, partial.** The header states the routing purpose and the retirement, not the reach. **Rows 39–40 conform on 2, 3 and 5.** They check presence and absence per row against the generated partition. **Rule 4 conforms, assessed in 15.8.** Row 38's subjects are the compile-time symbols `NativeRenderer.YogaStyleAttributes` and `NativeRenderer.VisualStyleAttributes`, so a rename is a build break, and each half is floored at its measured 26 and 3. Rows 39 and 40 assert each named style present in its half with `Contains`, so a renamed or moved style reds its own row. **Rule 7**: 15.7's P1 and P2 are in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md), which also carries row 37's provenance trace and retirement decision; P2, an empty Visual half, reds row 38 alone, on its floor. None is recorded for 39–40. |
| `NotApiEditorBrowsableTests` (sweep rows 16–18) | `ExpectedNotApiMarked` (26, hand) ↔ which exported types carry `[EditorBrowsable(Never)]`, both directions | **Rule 2 conforms.** The literal is 26, and `ExpectedList_IsNonVacuous_…` pins the count and two STABLE negatives. Since the 15.7 final review, `ShippedAssemblies_AreExactlyTheSevenShippedPackages` floors the swept assembly list at exactly 7 distinct assemblies, named. Before it, deleting the five assemblies 15.7 added narrowed the sweep back to two with every fact green. **Rule 3 conforms, strengthened in 15.7.** The facts check presence per name and exact set equality. `MarkedTypeNames_ReportsAPlantedStrayMark_FromTheAssemblyItIsGiven` drives direction 1's own `MarkedTypeNames` helper over a mark planted in the test assembly. With the shipped assemblies as input, the same helper must not see that mark. **Rule 5 conforms since 15.7, after a widening.** Direction 1 swept only Runtime and Renderer, so a stray mark on a stable type elsewhere went green unsaid. It now sweeps all seven shipped packages, each anchored by a `typeof`. The block states the rest: the three analyzer assemblies and anything outside the seven are unscanned; only exported types are scanned; the control proves the mechanism with a test-assembly fixture, not a real stray mark. **Rule 4 conforms, assessed in 15.8.** Each of the 26 names goes through `Resolve`, which throws naming the type when it is renamed or removed from the shipped assemblies. The seven assemblies are reached through one `typeof` each, and their count is held at exactly 7. **Rule 7**: "delete one mark", recorded when the pin landed. 15.7's row-17 widening and its mutation proof, including the temporarily-parameter-ignoring helper reproduction, are in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md). So is the final review's M2, deleting those five assemblies, which survived before the floor and reds it after. |
| `SpikeRazorTests` (sweep row 23) | The `.razor`-compiled `SpikeRazor` ↔ the hand-written `SpikeRazorTwin`, as patch streams over one lifecycle | **Rule 2 conforms**, through a `NotEmpty` fresh mount and count equality. **Rule 3 conforms**, through exact equality. **Rule 5 conforms since 15.7.** The block states the common-mode blindness: both halves go through one `NativeRenderer` and one dispatch script, so a defect they share passes. **Rule 4 conforms, assessed in 15.8.** Both halves are driven by type, `Drive<SpikeRazor>()` and `Drive<SpikeRazorTwin>()`, so a rename is a build break. The fresh mount asserts `NotEmpty`, so a component that stops rendering reds, and the streams are compared exactly. **Rule 7**: the 7.0 spike conclusion's mutation table. |
| `BnItemsJsonTests` (sweep rows 24–26) | `BnItemsJson.Write` ↔ `NativeShellBridge.WriteFlatJsonObject`, two separate .NET writers, over a fixed matrix | **Rules 2 and 3 conform.** The rows are fixed, and the comparison is exact equality. **Rule 5 conforms since 15.7, after a correction.** The header called this file the drift-catcher for the shells' parsers, and it reads no Kotlin or Swift copy. The header now says both sides are .NET writers in one assembly, and the block adds the matrix limit. **Rule 4 conforms, assessed in 15.8.** Both writers are called directly, `BnItemsJson.Write` and `NativeShellBridge.WriteFlatJsonObject`, so a rename or signature change is a build break, and each fixed row compares the two outputs as exact strings. **Rule 7**: none recorded. |
| `ScrollCommandTests` (sweep row 27) | `NativeRenderer.ScrollTo*` ↔ `BnScroll`'s own copies: two .NET string literals in two assemblies | **Rule 2 is n/a**, since the comparison is scalar. **Rule 3 conforms**, through equality. **Rule 5 conforms since 15.7.** The block says the fact reaches neither the Kotlin and Swift literal spellings nor the nonce grammar, which only the parsing facts exercise. **Rule 4, partial, named, assessed in 15.8.** The four constants are compile-time symbols, `NativeRenderer.ScrollToAttributeName`/`ScrollToEndTarget` and `BnScroll`'s copies, so renaming or deleting one is a build break. But the subject the fact is named for is the seam, and the constants only stand in for it. If either side stopped reading its constant and compared an inline literal instead, this fact would keep comparing two constants nothing uses and stay green. Only the file's parse and end-to-end facts could show that, and this pin would not. **Rule 7**: none recorded. |
| `BnModalTests` (sweep row 30) | A hand list of 8 `Name: Type` pairs ↔ `BnModal`'s declared parameters | **Rules 2 and 3 conform**, through a non-empty literal and exact equality. **Rule 5 conforms since 15.7.** The block says the fact covers declaration only and names the facts that cover forwarding: `ContentBox_ForwardsTheDeclaredSurface`, `ScrimColor_AlwaysEmitted`, and the mount and dismissal facts. **Rule 4 conforms, assessed in 15.8.** The literal is built from `nameof(BnModal.X)` and `typeof(...)` for all 8 pairs, so a renamed or retyped parameter is a build break, and the reflected surface is compared by exact equality. **Rule 7**: none recorded. |
| `BnFormControlTests` (sweep row 31) | `BnView`'s parameters minus `NotOnALeafControl`, plus each control's own hand surface ↔ the four form controls' declared parameters | **Rules 2, 3 and 5 conform.** The comparison is exact equality over non-empty sides, and a misspelled exclusion fails safe. The I3 block states that declaration is not forwarding. **Rule 4 conforms, assessed in 15.8.** `BnView` and the four controls are reached through `typeof` in the `MemberData`, so a rename is a build break, and each control is compared by exact equality with its expected surface. **Rule 7**: none recorded for this fact. |
| `ForwardedParameterNameTests` (sweep row 36) | The hand `FullSurface` (26) ↔ `ItemParameters ∪ ContainerParameters` | **Rules 2, 3 and 5 conform.** The literal is non-empty, the comparison is exact equality, and the header states the `nameof` limit. **Rule 4 conforms, assessed in 15.8.** `FullSurface` is compared by exact equality with `LayoutSurfacePinTests.ItemParameters ∪ ContainerParameters`, both compile-time symbols, over a non-empty 26-entry literal, so a moved or renamed name reds. **Rule 7**: none recorded. |
| `BnActivityIndicatorTests.DeclaresNoOwnParameters_ButInheritsTheFullItemSurface` (sweep addendum, found by the 15.8 re-audit review) | `BnActivityIndicator`'s own declared `[Parameter]`s ↔ none, and each name in `LayoutSurfacePinTests.ItemParameters` ↔ a property on `BnActivityIndicator` | **Group C's shape, and missed by the 15.7 sweep, which classed the file as not a pin.** **Rule 2, partial.** The name loop has no floor of its own; it iterates `ItemParameters`, which `LayoutSurfacePinTests.BnLayoutItem_DeclaresExactlyTheItemSurface` holds at a non-empty 17. The `DoesNotContain` over declared-only properties would pass on a type with no properties at all. **Rule 3, partial.** The name loop is a positive match per name, so its detector runs on every pass. The no-own-parameters half is an absence detector with no planted control in this file; `LayoutSurfacePinTests.RedeclarationDetector_ReportsTheShadowingProbe` controls the redeclaration shape, not this one. **Rule 4 conforms.** `BnActivityIndicator` and `BnLayoutItem` are reached through `typeof`, so a rename is a build break, and each roster name is asserted with `NotNull(GetProperty(...))`, so a renamed item parameter reds naming it. **Rule 5, partial.** The summary states the claim, not its reach: it does not say the name loop is near-tautological, because the type inherits the surface from the base the roster is pinned to, as group C's note does. **Rule 7**: the 15.8 review's mutation, a public `[Parameter]` added to `BnActivityIndicator`, reds this fact alone, 1 of the file's 3; it is in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md), under *15.8 re-audit review*. |
| Group C: `ForwardedParameterNameTests.ForwardTarget_DeclaresEveryItemParameter` and `…ContainerParameter` | Each roster name ↔ a property on `BnView` and `BnScroll` | **Near-tautological, and recorded as such in 15.7.** Both targets inherit the surface from the bases the roster is pinned to. So these facts red only if a target stops deriving, or shadows a roster name with a property that is not a `[Parameter]`. **Rule 2, partial**: no floor of its own. It iterates rosters that `LayoutSurfacePinTests` holds non-empty. **Rule 3**: a positive match per name. **Rule 5 conforms since 15.7.** The in-file note says the facts cannot see a deleted or misnamed forward, which Half 2 catches, and that the `BnScroll` row guards no forward at all. **Rule 4 conforms, assessed in 15.8.** The targets are reached through `typeof`. Each roster name is looked up with `GetProperty` and asserted with `Assert.True(p is not null, …)` and `NotNull` on its `[Parameter]`, so a renamed parameter on a target reds naming it. **Rule 7**: none recorded. |
| Group E: `BnImagePolishDemoTests.TheDemosNumbers_AreTheContractsArithmetic` | `BnImagePolishDemo.DeclaredWidthDp`/`DeclaredHeightDp` ↔ `BnImageDemo.FixedWidthDp`/`FixedHeightDp`: 200 × 120, two separate literals in two pages | **One genuine two-literal pin inside an arithmetic golden, kept.** 15.7 deleted four fixture equalities, `FixtureOrigin`, `ErrorSrc`, `IntrinsicSrc` and `ModeSrc`, because the page defines each as `= BnImageDemo.X`, so they could never red. **Rule 2 is n/a**, since the comparison is scalar. **Rule 3 conforms**: editing either literal alone reds it. **Rule 5, partial.** An inline comment says why the pair is a pin and why the four were dropped. The file has no Rule 5 block. **Addendum, 15.7 final review:** the same tautology sat in `BnScrollDemoTests`, `Assert.Equal(BnImageDemo.FixedSrc, BnScrollDemo.RowImageSrc)`, where the page defines `RowImageSrc = BnImageDemo.FixedSrc`. The sweep missed it; the final review found it, and it was deleted with a one-line comment on the same precedent. **Rule 4 conforms, assessed in 15.8.** All four values are compile-time constants, `BnImageDemo.FixedWidthDp`/`FixedHeightDp` and `BnImagePolishDemo.DeclaredWidthDp`/`DeclaredHeightDp`, so a rename is a build break, and the pair is compared by exact equality. **Rule 7**: 15.7's P6, `FixedWidthDp = 201`, reds exactly the kept line. It is in [the 15.7 record](plans/2026-09-26-phase-15.7-record.md). |
| Group A: the cross-language scalar half-pins, about 25 facts in `BridgeProtocolNativeTests`, `PatchProtocolNativeTests`, the three `*AbiUnchangedTests`, `NativeShellBridgeTests`, `FrameEncoderTests` and `BnLogTests` | A hand constant that copies a Kotlin, Swift or C-header value ↔ the .NET declaration | **Golden, not a pin — never reads the Kotlin/Swift/C copy.** The truth these facts are named for, ".NET agrees with the shell", is enforced only by a separately run JVM or iOS suite holding its own constant, so the pair fails common-mode. Not counted in the 40. The tree-reading differential pin that would replace them is **#414**. **Rule 4 is n/a**, since these are not pins. |
| Group B: `DeepLinkVectorTests` (4 facts) | The shared `BnDeepLinkVectors.All` table ↔ the rows its consumers load-bear on | **Fixed points for pins that live in Kotlin/Swift; not pins.** They are the Rule 3 anchors for the Kotlin and Swift parser suites that consume this table. `EveryVector_IsWellFormed` has a `NotEmpty` floor. Not counted in the 40. **Rule 4 is n/a**, since these are not pins. |
| `RenderThreadDispatcherTests` (16.1, Renderer, 8 facts) | The dispatcher's honesty claims ↔ what the renderer under test does: `CheckAccess`, where `InvokeAsync` runs, where an exception goes, what shutdown does to posted and in-flight work, and 13.2's `Dispose → InvokeAsync` recursion | **Rule 2 conforms, through anchors.** There is no population. Instead, each fact first shows that its scenario happened. `CheckAccess` is asserted false on the test thread before it is asserted true inside `InvokeAsync`. The recursion fact asserts that the thread was shut down on each of its 10 runs. **Rule 3 conforms.** Every fact carries a named positive control: the thread still runs work after the sink fired; `Send` runs on the render thread; the same call before shutdown ends `RanToCompletion`; the in-flight await without the dispose completes. **Rule 4 conforms.** Every subject is a compile-time symbol on `NativeRenderer` or its dispatcher, so a rename is a build break. Every thread check compares with `renderer.RenderThreadId`, the renderer under test. **Rule 5 conforms.** The header names four things the file does not cover. One is the production sink's process termination, which the tests replace. **Rule 7 conforms.** These mutations went red. `CheckAccess() => true` reds 91 of 146. No cancel of posted work after shutdown reds `WorkPostedAfterShutdown_…`. A `Send` that swallows reds `SendPropagates…`. A `Run` that swallows instead of calling the sink reds `AThrowFromPostedWork_…`. With no `SynchronizationContext` on the render thread, the test host crashed. With no `CancelPending()` on exit, `AnAwaitInFlightAtShutdown_…` reds. |
| The Task 2 flips: `RenderThreadWarningTests` (3 facts), `MountSyncTests.Renderer_mounts_synchronously_on_its_render_thread` and `RendererSpike.RenderWalk_IsAllocationFree_OnSteadyState` (16.1, Renderer) | An off-thread render ↔ the rule that it throws under `StrictErrors` and warns without it. Mount ↔ "the first frame is emitted before `Mount` returns, from the render thread". The allocation budget ↔ the walk as the render thread performs it | **Rule 2 conforms.** The flip closed two silent vacuities. `TheOrdinarySingleThreadedPath_IsSilent` filtered on the test thread's id, which can no longer own a renderer, so it now uses `renderer.RenderThreadId`. The allocation pin measured from the test thread, where `GetAllocatedBytesForCurrentThread` no longer sees the walk. It now runs on the render thread and asserts both `measuredOn == RenderThreadId` and a floor above 100,000 bytes. Measured: 324,000 bytes per 900 re-renders. **Rule 3, partial, named.** The warning flips have a control: without the `ClaimEveryThreadForTests` bypass, the same off-thread call is marshalled and does not throw. The mount pin asserts `CheckAccess` false first, so its detector can answer no. It has no planted defect of its own, so only the "Mount not marshalled" mutation below proves it. **Rule 4 conforms.** `ClaimEveryThreadForTests`, `RenderThreadId` and `Mount` are compile-time symbols. **Rule 5, partial, named.** `RenderThreadWarningTests` has a Rule 5 block. The mount flip and the allocation pin carry only a `Flipped in 16.1` note and do not say what they do not cover. **Rule 7 conforms.** Removing `AssertOnTheRenderThread()` from `UpdateDisplayAsync` reds both flipped warning facts. Leaving `Mount` unmarshalled reds 8 facts, among them `MountSyncTests` ×3, the three warning facts and the allocation budget. |
| `DispatchLaneBlockingTests.AnAsyncHandlerAwaitingAnOpenHostCall_ReturnsWhileTheCallIsOpen` (16.1 flip, #345) | The rc-0 return of `dispatch_event` ↔ a handler still suspended on an open host call | **Rule 2 conforms.** An anchor asserts that a host call was really begun and is still open when the export returns. A handler that never went async cannot pass. **Rule 3, partial, named.** No in-file fact shows that the bounded wait can see a block. The "reinstate `GetResult`" mutation proves the detector, but a mutation is not a control that runs on every pass. **Rule 4 conforms.** The export is called directly, and `FakeShellHost.AutoCompleteHostCall` is a compile-time symbol. **Rule 5 conforms.** The header puts the continuation's frames, the late fault and the host-event arms out of scope. **Rule 7 conforms.** Putting back `DispatchUiEventAsync(…).GetAwaiter().GetResult()` in `DispatchEventCore` reds the pin with "did NOT return within 1s". |
| `DispatchWindowScopeTests` (16.1, 10 facts) | Each dispatch's capture window ↔ its own synchronous part. The file covers its navigation swap, its fault, its late fault in production mode, a fire-and-forget fault after it finishes, a #164 binding fault after an await, and the restore of the flowing scope | **Rule 2 conforms, through anchors.** The cascade pin first asserts that exactly one dispatch is pending and incomplete. The attribution pin asserts that A returned rc 0 and is pending. The fire-and-forget pins assert either that nothing is pending, or that the handler finished cleanly while its work is still gated. **Rule 3 conforms.** The cascade has `…WithNoSuspendedHandler_…_PositiveControl`, through `IsBnDemoMenu`. The production late fault has a strict-mode control. The restore pin asserts that each continuation does see its own scope. The three absence assertions, `DoesNotContain(log, NativeRenderer.LateFaultLogLabel)` in the two fire-and-forget pins and the #164 pin, have their control in `ALateFault_InProductionMode_FaultsThePendingTask`, which asserts that the attributed path DOES log that label. This was partial until the 16.1 final fix wave. **Rule 4 conforms.** Everything is reached through compile-time symbols and the real exports. The log label is the constant `NativeRenderer.LateFaultLogLabel`, used by the log call and by all four assertions, so rewording it moves them together; a literal in the log call reds the positive assertion. Until the final fix wave the assertions matched the literal text of the log line, and a reword left all three passing while checking nothing. **Rule 5 conforms.** The header lists what the file covers and four things it does not. **Rule 7 conforms.** Keeping the window open across the await reds the cascade pin. A renderer-wide fault slot reds the attribution pin. Dropping the `AsyncLocal` attribution branch reds the production pin. Dropping the nested hand-off reds the nested pin. Removing the three `Done` assignments reds the synchronous fire-and-forget pin. Dropping the binding-fault guard reds the #164 pin. Removing the `AsyncLocal` restore reds the restore pin. Removing `Done` from `AwaitWholeHandler` reds the pending fire-and-forget pin and nothing else. A detector that looks for "Explorer" reds the positive control. Logging the attributed late fault with a literal, "render fault (late, attributed)", instead of `LateFaultLogLabel` reds the production late-fault pin. |
| `HostEventArmThreadTests` (16.1, 2 facts) | The thread each `host_event` arm runs component code on ↔ `renderer.RenderThreadId`. For `back` and `navigate`, the thread of the host's Navigate callback too | **Rule 2 conforms.** For each arm, anchors come before any thread is compared. The arm's own record must exist, and for the nav arms the Navigate callback must have run at all. **Rule 3 conforms.** `TheThreadDetector_SeesSubscriberCodeRunOffTheRenderThread_PositiveControl` raises the multicast and the safe-area report from the test thread, and the probe records the test thread. **Rule 4 conforms.** The reserved arms are driven through the public `BnHostEvents` constants, so a renamed arm is a build break, and the probe is swapped into the registry by type. The multicast arm is driven with the literal `"onPause"`, which cannot move: any unreserved name falls through to the multicast. **Rule 5 conforms.** The header states why component records alone cannot see the nav arms, and names what is out of scope. **Rule 7, partial, named.** Running the multicast off the render thread reds the pin, and so does unmarshalling `back`. No mutation of the `navigate` or `safeAreaChanged` arm is recorded. |
| `ShutdownQuiescenceTests` (16.1, 16 facts and 2 theories, 20 cases) | `blazornative_shutdown`, `SetFrameCallback` and `ResetForTests` ↔ four claims. No frame reaches a callback after shutdown returns. No old callback is in flight when re-registration returns. Pending work ends `Canceled`. Reset joins every thread and never waits under `s_lock` | **Rule 2 conforms, through anchors.** The post-shutdown pins re-register a counting callback, so a null pointer alone cannot pass them. The lock pin first resets the `Components` view, so its probe cannot take the lock-free path. The host-event theory asserts that the arm handed on one pending Task under its own name. The starvation pin reads `s_frameCallback` by reflection, and a missing field reds it. **Rule 3, partial, named.** Six pins have a `_PositiveControl` twin. The straddle pin controls itself through the same detector, since registration 2 returns once P1 is released. Five pins have no in-file control: `Shutdown_WaitsForACallbackAlreadyInFlight`, `TryMount_AfterShutdown_BuildsAFreshSession`, `ResetForTests_JoinsTheRenderThread_AndLeaksNone`, `SetFrameCallback_IsNotStarved_…` and `SetFrameCallback_FromInsideACallback_…`. The join pin was green BEFORE its implementation, because Task 2's `Dispose` already joined, so only its mutation shows it is load-bearing. **Rule 4 conforms.** Every export is called through its real function pointer, and the callback is a real `[UnmanagedCallersOnly]` method. The two reflection reads, `s_lock` and `s_frameCallback`, red when the field is missing. **Rule 5 conforms.** The header names six gaps. Among them are concurrent re-registrations, a real yielding back or navigate, never-disposed renderers and the shells' side. **Rule 7, partial, named.** Each of these went red: dispose inside `s_lock`; reset skips the join; the drain does not wait; `TryEnter` ignores `closed`; shutdown skips the join; shutdown does not detach; no `Track`; no wait in `SetFrameCallback`; no inside-the-gate skip; no epoch flip; no epoch re-check. The `back` and `navigate` arms without the `Track` each red their own case. **One equivalent mutant stays green:** clearing the pointer BEFORE the drain, because the sink reads the pointer inside the gate. |
| `FaultNoticeTests` (16.1, 8 facts and 1 theory, 10 cases) | A fault after a handler's first await ↔ one `FaultNotice` host call, op 5, in production mode, carrying exactly `handlerId`, `event`, `type` and `message` | **Rule 2 conforms.** The probe awaits a real held geolocation call. The notice's key set is asserted EXACT, at four fields. The timeout pin asserts one pending call before the timeout fires. **Rule 3 conforms.** A strict-mode control covers one direction, and `ASynchronousFault_IsRc2_AndSendsNoFaultNotice` covers the other. **Rule 4 conforms.** The op is read as `HostCallOp.FaultNotice`, a generated compile-time symbol. The notice is read back through `FakeShellHost.HostCalls()`, as the shell receives it. **Rule 5 conforms.** The header names five gaps, among them what the shells do with the notice and, since the 16.1 final fix wave, a late fault that races shutdown: dropped, or delivered after shutdown returns. **Rule 7, partial, named.** Removing `DeliverLateFault` from `ObservePendingDispatch` reds 5 of the 10 cases. Removing it from `ObservePendingHostEvent` reds both theory cases. Removing the timeout reds the timeout pin. No mutation is recorded against the two `SendFaultNotice_NeverThrows_…` facts, such as removing the try/catch. |
| `WireVocabularyCodegenTests.TheManifest_RejectsADuplicateOpId` (16.1) | `WireVocabulary.Load` ↔ one fixture manifest with a duplicate op id and one with a duplicate op name | **A control of the manifest's validation, not a pin. It is listed here because it does not read the tree.** It loads an in-memory fixture. Its positive control is a manifest with distinct ids and names, which must load. **Rule 7: none recorded for this fact.** The frozen-id mutations, Camera 4 → 5 and 4 → 6, red the tree-reading `TheHostCallOps_KeepTheirFrozenIds` instead, and so do 16.6's D1 and D3, in [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §2. |
| JVM `FaultNoticeTest.kt` (16.1, 2 tests) | A FaultNotice begun by the published dll ↔ Kotlin's `BridgeRegistrar` handing it to `onError` and completing it, never passing it to the app's handlers | **Rule 2 conforms.** `BnCameraDemo`'s Take Photo returns rc 0 while the camera call is held. The malformed `{bad` payload then makes its continuation throw after the await, in the dll's production mode. **Rule 3, partial, named.** `fault_notice_is_op_five` is a golden scalar. The routing test asserts a message prefix and that "the handlers never saw op 5". No control shows that the handlers WOULD see an op the registrar does not intercept. **Rule 4 conforms.** The op is the generated `HostCallOp.FAULT_NOTICE`. The Take Photo handler is found in the mount frame by its text, through `checkNotNull`, so a renamed button throws rather than passing. **Rule 5 conforms.** The KDoc says it does not cover the iOS arm or the notice's exact args. **Rule 7 conforms.** Disabling the `BridgeRegistrar` intercept reds the routing test with "nothing reached onError within 10s". |
| JVM `FrameProducerTest.kt` (16.1, 3 tests) | The frames `BlazorNativeRuntime`'s callback wrapper sees ↔ "one producer thread per runtime" | **Rule 2 conforms.** The wiring test's anchor asserts that two distinct thread identities were seen. **Rule 3 conforms.** `frames_from_one_thread_report_nothing` is the negative, and `frames_from_two_threads_report_once` the positive. **Rule 4 conforms.** `checkFrameProducer` is a compile-time symbol. The wiring test drives the real wrapper through `blazornative_shutdown` and a second `start()`. **Rule 5 conforms.** The KDoc says `WidgetMapper` itself is not covered, because it is `androidMain` only. **Rule 7 conforms.** A wrapper that stops calling the check reds the wiring test. A disabled detector reds two tests. |
| JVM `ShutdownQuiescenceTest.kt` (16.1, 2 tests) | A late continuation frame after `shutdown()` ↔ the retired runtime's callback and a REPLACEMENT runtime's callback | **Rule 2 conforms.** The replacement runtime B is the anchor. The vacuity contrast measured that without it, the pre-16.1 shutdown passes. **Rule 3 conforms.** In `…_PositiveControl`, with no shutdown, the release frames `status:Cancelled`. **Rule 4 conforms.** It drives the real exports through JNA. **Rule 5 conforms.** The KDoc states that it guards the gate and the join TOGETHER, and names the .NET pins that guard each one. **Rule 7, partial, named.** The pre-16.1 shutdown body reds it, and so does losing both the gate close and the join. **Two equivalent mutants stay green:** losing only the gate close, and losing only the join. Either mechanism alone still quiesces this scenario. |
| JVM `RetireLateContinuationTest.kt` (16.1, 1 test) | A late continuation frame after `retire()` ↔ the retired runtime's `onFrame` | **A HAZARD pin: it passes while the hazard exists, #425.** **Rule 2 conforms.** Its anchors are that `retire()` returned true, and that no frame arrived between `retire()` and the release. **Rule 3 conforms.** It is a positive match: the frame must arrive within 5 s. **Rule 4 conforms.** It uses the real lane and exports. **Rule 5 conforms.** The KDoc says the replacement-runtime half is not covered. It also says to invert the pin, never delete it, when `retire()` is made to quiesce. **Rule 7 conforms.** Making `retire()` also call `blazornative_shutdown`, which is the direction of the fix, reds the pin with its own message. |
| JVM flips: `DispatchEventTest.async_dispatchEvent_rerenders_serially_on_one_non_lane_thread` and `HostEventTest.dispatchHostEventAndWait_returns_while_a_handler_holds_a_host_call` (16.1) | Frame threads ↔ one `Thread` identity named `BlazorNative-Render`, never the lane, and never two frames at once. `dispatchHostEventAndWait` ↔ a return within 2 s while a handler holds the lane's call | **Rule 2 conforms.** The frame flip needs at least 2 frames. The #346 flip needs the held call to be in flight. **Rule 3 conforms.** The frame flip asserts that `onError` is empty, which is the real-path negative of `FrameProducerTest`. **Rule 4 conforms.** Both flips use the real runtime, and `RENDER_THREAD_NAME` is a compile-time symbol. **Rule 5, partial, named.** `HostEventTest` has a DOES NOT COVER block, but the frame flip has none of its own. The #346 flip deliberately asserts no rc, because in the never-reset JVM session the rc depends on test order. **Rule 7 conforms.** A wrapper that delivers every other frame from a fresh thread reds the frame flip. So does removing `CallbackThreadInitializer`, which proves the init block is load-bearing. A lane task that sleeps 5 s after the export reds the #346 flip. |
| XCTest `BnFaultNoticeTests.swift` (16.1, 4 tests; 16.6 pins 26 to 29) | A FaultNotice begun on `AppleShellBridge` ↔ `onError` receiving a `BnFaultNotice`, and the call completing with status 0 and no payload | **The cells come from [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §8, pins 26 to 29, and the runs from its §5 and §7.** Every run is an `ios.yml` run on its own scratch ref, attempt 1, with its reds read from `xcodebuild-test.log`. **Rule 2, per fact.** It is n/a for `testFaultNoticeIsOpFive`, a scalar. It conforms for `testAFaultNoticeRoutesToOnErrorAndCompletesOk`, which asserts counts, `errors.count == 1` and `captured.count == 1`; A3 reds it at the `onError` count. It is **partial, named** for `testAnUnknownOpStillTakesTheErrorBranch_Control` and `testAFaultNoticeWithNoRuntimeIsStillCompletedOk`. Each asserts a status list, so an empty capture would red, by reading the assertion; no row empties it, so that is not measured. **Rule 3.** It is **partial, named** for the id fact: there is no control, because the expected value is a literal. The control conforms as the control, through A7 and F18-A7, and the routing fact and the no-runtime fact conform through it. **Rule 4 conforms.** The op is the generated `BnHostCallOp.faultNotice`, and A1 moved the generated value. A3 drops the routing, A4 and F18-A4 change the status, and A13 deletes the no-runtime log line. **Rule 5 conforms.** The header says that sending the notice is the .NET suite's job, and names the booted round trip and the Android arm. Since 16.6 Task 7 the no-runtime fact asserts the "logged through BnLog" the header claims. **Rule 7, per fact.** `testFaultNoticeIsOpFive` conforms: A1, run 36919855881, reds it with `("6") is not equal to ("5")`. `testAFaultNoticeRoutesToOnErrorAndCompletesOk` is **partial, named, after F18**. A3, run 36919865146, reds the routing. A4, 36920495384, and F18-A4, 36923551504, red the status, the second naming status 0 as OK and 5 as Error. A17, 36931019825, reds the `hostCallBegin` rc. Entered by no row: the `payload` nil assertion, and `a routed notice must not also be logged by the bridge`. `testAnUnknownOpStillTakesTheErrorBranch_Control` is **partial, named**. A7, 36921401411, and F18-A7, 36926894663, are the negative, with every notice pin green, and A17 enters the rc after F22. Not entered: its `errors.isEmpty` half. `testAFaultNoticeWithNoRuntimeIsStillCompletedOk` is **partial, named**. A4, F18-A4 and A13, 36919569551, red it, and A12, 36919565079, is the unfixed pin's green over the same deletion. A17 enters the rc. Not entered: the whole-message assertion, which holds the notice's payload fields; A13 reaches it only with no line logged. |
| XCTest `BnDispatchLaneTests.testADispatchReturnsWhileAHostCallIsOpen` (16.1, 1 test; 16.6 pin 30) | `dispatchEventBlocking` ↔ an rc-0 return within 2 s while `BnCamera` holds a capture in flight. This is the Swift twin of the .NET #345 pin | **The cells come from [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §8, pin 30, and the runs from its §5.** **Rule 2 conforms, observed.** An anchor at `:99` asserts that the camera had a request in flight. A11a, run 36923304090, makes the capture complete at once, and the pin reds at that anchor alone, `the camera op never recorded an in-flight request`, while its 2 s return is green. A11b, run 36923847199, is A11a with the anchor commented out, and the pin passes. The anchor is load-bearing. **Rule 3, partial, named.** It has no control of its own in the suite. The A11a and A11b pair is a contrast run in 16.6, not a pin. **Rule 4 conforms.** It uses the real runtime and the real camera delegate. A10, run 36923300329, puts the pre-16.1 wait back in `Exports.cs`, `outcome.Pending!.GetAwaiter().GetResult()`, and rebuilds `BlazorNative.Runtime.a` from the scratch commit, proved from the log. The pin reds with `dispatch_event did NOT return within 2 s … This is #345`, and at the rc that was never written. **Rule 5 conforms.** The header puts the continuation's frames out of scope. **Rule 7 conforms.** A10 is the plain case, and A11a with A11b is the vacuity contrast, each on `ios.yml` at attempt 1. Until 16.6 this cell said the pin had never been run red. |
| `BackStateNoticeTests` (16.2, Runtime, 18 facts) | Host calls and frames in ONE interleaved log ↔ the rule that a navigation's `BackState` notice, op 6, is the LAST one before the frame that shows its page, on the direct, back and click-handler paths. Every rc-1 back ↔ exactly one `BackUnhandled`, op 7: at the root, with no session, and a back that yields and then resolves false. Every mount ↔ a resend of the back state, even when it is unchanged | **Rule 2 conforms, through anchors.** `FirstFrame` reds with a named message when the page's frame never appears, so an order check cannot pass on an empty log. The two yielding-back pins first assert rc 0 and that no op 7 was sent before the work completes. Every fact that has a session starts it through `StartSession`, which asserts `StrictErrors` false on the live renderer, so none of them can pass in strict mode alone. The no-session and no-bridge facts have no renderer to assert on. **Rule 3 conforms.** `TheOrderedLog_OrdersBothKindsOfEntry` is the log's positive control, in both directions: a hand-sent op 6 before a bare swap must land before its frame, and an op 7 after it must land after. Its synthetic `[true, false, frame]` case must fail for `true` and pass for `false`, which proves the check reads the LAST notice. The negatives are `AHandledBack_SendsNoBackUnhandled` and `ABackThatYields_ThenResolvesHandled_SendsNoBackUnhandled`, the latter with a bounded 500 ms wait. **Rule 4 conforms.** The ops are read as the generated `HostCallOp.BackState` and `HostCallOp.BackUnhandled`, and the notices are read back from `FakeShellHost`'s `hostCallBegin` record, as a shell receives them. The page frames are found by content, through `FirstFrame`, which reds by name when the content moves. **Rule 5 conforms.** The header names four things it does not cover: what a shell does with the notices, the same-batch apply, a back cut off by shutdown, and the delivery machinery. It records the faulted back, rc 2, as a DECISION rather than a gap. One bullet still said the shell arms did not exist yet; it was corrected while this row was written. **Rule 7, partial, named.** These went red, each run alone. Sending in `afterSwap` instead of `beforeSwap` reds the three ordering pins and `AFailedSwap_…`. Dropping the unchanged guard reds `AnUnchangedValue_SendsNoNotice` alone. Skipping `SendBackUnhandled` reds `BackAtRoot_…` and `BackWithNoSession_…`. Frames that stop draining host calls, the log's own path, red 5. Dropping `swapFailed` reds `AFailedSwap_…` alone. Dropping the first-mount publish reds 6. Removing the continuation's send reds `ABackThatYields_ThenResolvesUnhandled_…` alone. Moving the slot clear back after the swap reds `TwoBacksFromOneClickHandler_DoNotLeaveAReBackTrail` alone, while the other 35 BackState and Navigation facts stay green. A `LastBackStateBeforeIs` that matches any notice reds the log control. A mount resend that deduplicates reds `ASecondMount_ResendsTheUnchangedBackState_SoANewShellLearnsIt`. **No mutation is recorded** against `TheNotices_NeverThrow_WithNoBridgeRegistered`, such as removing the try/catch, or against the args-shape fact. No equivalent mutant was recorded. |
| JVM `BackNoticeTest.kt` (16.2, 7 tests) | Ops 6 and 7 begun by the published dll ↔ Kotlin's `BridgeRegistrar` routing them to `onBackState` and `onBackUnhandled`, completing each OK, and never passing either to the host's handlers. A back's rc 1 ↔ no `onError`. The dll's REAL frames fed through `BackStateBuffer` ↔ the value riding the frame that mounts the page, not the removal, including a page whose first render is empty. `BackHoldProbe` ↔ a blocking dispatch that returns only after the held call is released | **Rule 2 conforms, through anchors.** Each session test first drains to the root with one back. The two real-frame pins assert the frame shape they are about before they judge it: the swap is a removal frame followed by a page frame, and the empty page mounts with a lone CommitFrame. Either assertion reds with a "re-point the pin" message if the shape changes. The probe test asserts that Hold began the camera call, and that the release fired before the blocking back returned. **Rule 3, partial, named.** `a_navigate_that_is_not_handled_still_raises_onError` is the control for the rc-1 exemption: without it, a runtime that routed nothing to `onError` would pass. The probe test is itself the positive control for the device #346 pin, since it proves the old blocking back cannot return while Hold holds the lane. `the_back_notices_are_ops_six_and_seven` is a golden scalar with no control. **Rule 4 conforms.** The ops are the generated `HostCallOp.BACK_STATE` and `BACK_UNHANDLED`, and events are the generated `BnHostEvent`. The Hold button is found as the single click wire of the mount frame, through `checkNotNull`, so a changed probe throws. The exports are called through JNA. **Rule 5 conforms.** The KDoc names what it does not cover: the Android shell's use of the notices, the buffer in isolation, the iOS arms, and the args and order, which .NET pins. The probe test has its own DOES NOT COVER. **Rule 7 conforms.** Routing op 6 to `deliverBackUnhandled` reds two tests. Removing the rc-1 exemption reds the root test. Widening it to every rc 1 reds the navigate control. A `takeForBatch` that ignores the rule, and one that drops the removal-only skip, each red the removal pin. The superseded parentless-node rule reds the empty-render pin. Making the probe's Hold handler `async`, with the dll republished, reds the probe test, because the blocking back then returns before the release. |
| JVM `BackStateBufferTest.kt` (16.2, 11 tests) | Hand-built batches ↔ the rule "hold an offered back state until the first batch that is not removal-only". `handBackToPlatform` ↔ "the callback is disabled while the press is re-dispatched, then restored to the state read AFTER the dispatch" | **Rule 2 is n/a:** the inputs are fixed literal batches, not a population. Each null assertion is followed by a positive one on the same buffer, for example the page batch must still carry the value after the removal carried none. **Rule 3 conforms.** `a_swaps_removal_batch_…` carries its own positive control. `the_restore_reads_the_state_after_the_dispatch` is the restore's negative. **Rule 4, partial, named.** `BackStateBuffer`, `handBackToPlatform` and `RenderPatch` are compile-time symbols, so a rename is a build break. But the batches are hand-built, so a change in the dll's real frame shape is invisible here; `BackNoticeTest`'s real-frame pins are what red on that. **Rule 5 conforms.** The KDoc names WidgetMapper's wiring and the same-runnable apply, which need Android, the real frame shapes, and the .NET order. **Rule 7, partial, named.** A buffer that applies on arrival reds `an_offered_value_is_not_applied_until_a_page_carries_it`. Ignoring the rule, or dropping the removal-only skip, reds `a_swaps_removal_batch_…`. Putting back the parentless-node rule reds `an_empty_mount_batch_carries_the_value` and `any_batch_that_is_not_removal_only_carries_the_value`, and those two, with `BackNoticeTest`'s empty-render pin, were the three red on `ebab023`, before the fix. A `handBackToPlatform` that stops restoring reds all three restore tests. **No mutation is recorded** against `a_value_is_carried_by_exactly_one_page`, `the_latest_offer_before_a_page_wins`, `applying_the_value_already_applied_reports_no_change` or `a_batch_that_carries_nothing_leaves_the_applied_value_alone`. |
| JVM `DispatchHostEventAndWaitVisibilityTest.kt` (16.2, 2 tests; 16.6 pins 6 and 7) | `BlazorNativeRuntime.dispatchHostEventAndWait`'s visibility as `kotlin-reflect` reads it from `@Metadata` ↔ `KVisibility.INTERNAL` | **The cells come from [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §8, pins 6 and 7, and the runs from its §3 and §7.** Every run is local, `./gradlew.bat testDebugUnitTest --rerun-tasks --tests "*DispatchHostEventAndWaitVisibilityTest"` under JDK 21, with its reds read from the JUnit XML. **Rule 2 conforms, since F9 and F10.** The pin filters the members named `dispatchHostEventAndWait` and asserts there is exactly one, with the count and the visibilities in its message. C2-after reds a second, public overload with `found 2: [PUBLIC, INTERNAL]`, and C4 reds a rename with `found 0: []`. C5 shows that without the count assertion the red is a bare `NoSuchElementException`. The control asserts that its list of `dispatchEvent` members is not empty, and C6 reds a rename there with `found 0`. Before F9 the lookup was `singleOrNull`, and a second overload redded with a message saying the member was missing, C2. **Rule 3.** The pin conforms through the control. C3 shows the control reds when a `dispatchEvent` overload reads INTERNAL, and C1 shows the pin reds on a real PUBLIC, `expected: <INTERNAL> but was: <PUBLIC>`. The control is **partial, named, with no control of its own**. Since F10 it checks every `dispatchEvent` overload, and C3prime-203 and C3prime-175 each red it, but no fact that runs on every pass shows that its own check can fail. Before F10 it read only the first-declared overload, so C3prime-203 left it green. **Rule 4 conforms.** C1 reds a visibility change, C2-after an added overload and C4 a rename, each naming its cause. C3, the C3prime rows and C6 move the control's subject. **Rule 5 conforms.** The KDoc says it covers the JVM side only: Swift's keyword and the manifest comparison belong to `DispatchSurfaceDriftTests`. It also says why the bytecode `ACC_PUBLIC` flag cannot be used: an `internal` member compiles `public final`, measured with `javap`. **Rule 7: every branch redded, from the §3 rows, each verdict as expected.** Removing `internal` from the method alone fails `compileDebugKotlin`, because a public function cannot expose the internal `BnHostEvent`. So C1 makes both public, and the pin reds at `:68`. C2-after, C4 and C5 enter the count branches, and C3prime-203-after, C3prime-175-after and C6 enter the control's. Until 16.6 this cell said the pin had never been seen red. |
| Instrumented `BackAndroidTest.kt` (16.2, 5 tests; 16.6 pins 37 and 38) | The real `MainActivity` on the AVD ↔ five claims. Back from a page navigates to its parent, and the callback turns on exactly once, inside the batch runnable, with the page already applied. Back at the root finishes an activity launched without an action. A press while a synchronous handler holds the render thread returns in under 1 s, and the navigation lands only after the release. A back pressed in the main-thread turn that first shows a page navigates and does not exit. A modal is dismissed by back and the callback turns off afterwards | **Pins 37, `back_with_a_modal_open_dismisses_the_modal`, and 38, `back_at_root_finishes_the_activity`, take their cells from [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §8, and their runs from its §6 and §7.** Every run is an `android-instrumented.yml` run on its own scratch ref, with its reds read from the JUnit XML. The other three tests keep 16.2's Rules 2–5 below; 16.6 did not re-assess them, and their reds are in §6, moved from PR #431. **Rule 2.** For the other three it conforms, through anchors. Every test first polls for its page. The #346 pin asserts that BackHoldProbe rendered, that back is enabled on it, and that Hold began the camera call, before it times the press. Before the release it asserts that the page has not changed and that the release has not fired yet. It is **partial, named** for 37: its three anchors, `BnModalDemo` rendered, back disabled before "Show modal" and the modal open, are entered by no row. B1 reds the pin's own enable assertion, not an anchor. It is **partial, named** for 38: `goForwardAndBackToTheRoot` asserts the swap, the enabled callback, the return and the disabled callback before the pin presses back, and no 16.6 row enters those anchors, `:349` included. **Rule 3, partial, named.** `assertEnabledOnceWithThePage` catches both a toggle that comes too early, with no settings page, and one that comes too late, outside the batch runnable. The #346 pin's control lives on the JVM, `BackNoticeTest.back_hold_probe_holds_the_lane_…`, not in this suite. Neither 37 nor 38 has a control in the suite; `BnModalDemoAndroidTest` reds with 37 under B1 and B2, from the same subject. **Rule 4.** It conforms for all but 38. It drives the real `MainActivity` through `onBackPressedDispatcher.onBackPressed()` and reads `hasEnabledCallbacks()`. The same-runnable check reads the compile-time seam `WidgetMapper.inBatchRunnableForTest`, and the camera hold uses `AndroidShellBridge.cameraCaptureHook`. B1 and B2 move 37's subject. It is **partial, named** for 38: production mutations red it only at the harness anchor, and none reaches its own DESTROYED assertion. **Rule 5 conforms.** The KDoc names the predictive-back animation and real gestures, API levels below 33, the .NET order and the buffer in isolation. Since F21 it no longer claims that a launch through `ActivityScenario.launch(Class)` cannot reach DESTROYED, which B3r measured false on the API 34 lane. **Rule 7, partial, named; each of the five has a recorded red since 16.6.** 37: B1, run 36921374024, drops `hasOpenModal` from `backEnabled`, and the pin reds at its enable assertion, `an open modal must enable back, or back finishes the app from under it`. B2, run 36921378980, removes the modal consult in `handleBack`, and the activity finishes. Before F19 that red was ActivityScenario's bare NPE. Since F19, `pollUntil` names it: F19-B2, run 36923692209, `the activity was DESTROYED while the test still expected it on screen`. No negative and no vacuity contrast was run. 38: B3b, run 36925080962, deletes the pin's own back press, a test-side mutation, and the pin reds at its own assertion, `the scenario is RESUMED, not DESTROYED`. **B3's harness-anchor finding:** no production mutation reds 38 at its own DESTROYED assertion. As the milestone audit recorded, production mutations red it only at the harness anchor in `goForwardAndBackToTheRoot`, `:349` after F19 and F21, `back stayed enabled at the root after the draining back`, and 16.6 did not re-run them. B3r, run 36931012893, the brief's ACTION_MAIN launch, stayed green at attempt 1: on the API 34 lane that launch still reaches DESTROYED. B3, run 36921386542, is history only, because its evidence is from attempt 2. The #346 pin: D10, run 36323787569 on `5143d76`, reverts `handleBack` to `dispatchHostEventAndWait`, and the pin reds with `the back press held the main thread for 3028 ms while the render thread was held`. `back_from_a_page_navigates_to_its_parent` and `navigate_then_back_at_once_neither_exits_nor_swallows`: D8, run 36323790875 on `55781f7`, applies the back state outside the batch, and both red with `back turned on OUTSIDE the batch runnable`. D10 and D8 ran in 16.2, for PR #431, and 16.6 read them again from their artifacts. **Not run, named:** applying the back state on arrival, and dropping `publishBackEnabled()` from `applyBatch`. **Inspection only, since no test drives them:** dropping the restore in the pre-boot or `BackUnhandled` path, and removing `if (!backCallback.isEnabled) return`. `onBackUnhandled` doing nothing is expected to stay GREEN on the device, because at the root the callback is disabled and the platform default finishes; the `BackUnhandled` hand-off is pinned on the JVM only. |
| XCTest `BnBackOffMainTests.swift` (16.2, 6 tests; 16.6 pins 31 to 36) | `BnDeepLink.handle` on main ↔ a return in under 1 s while BackHoldProbe holds the lane, with the navigate landing after the release. `AppleShellBridge`'s `backState` and `backUnhandled` arms ↔ status 0 with no payload, rc 0, and nothing reaching `onError` | **The cells come from [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §8, pins 31 to 36, and the runs from its §5 and §7.** Every run is an `ios.yml` run on its own scratch ref, attempt 1, with its reds read from `xcodebuild-test.log`. The pins are 31 `testADeepLinkNavigateReturnsWhileTheLaneIsHeld`, 32 `testTheHoldProbeHoldsTheLane_Control`, 33 `testTheBackOpsAreSixAndSeven`, 34 `testABackStateNoticeCompletesOkAndDoesNothingElse`, 35 `testABackUnhandledNoticeCompletesOkAndDoesNothingElse` and 36 `testTheOpAfterBackUnhandledStillTakesTheErrorBranch_Control`. **Rule 2.** 31 conforms, through anchors. A9 reds the Settings anchor. A14b, A15b and F20-A14b red the release-order anchor, and A11a and A11b the Hold-began anchor. A16 shows the pin green with the release anchors removed. 32 conforms through its Hold-began anchor at `:128`, which A11a and A11b red. 33 is n/a, since it compares scalars. 34, 35 and 36 are **partial, named**. Each asserts a captured list or count, so an empty capture would red, by reading the assertion; no row empties it, so that is not measured. **Rule 3.** 31 conforms through 32. A15 shows it reds while the control holds, and A15b shows its 1 s return goes green without the hold, so that assertion's red depends on the control. 32 conforms as the control: A14b and A15b red it, and since F20 it names the unheld lane. 33 is **partial, named**: there is no control, because the ids are literals. 34 and 35 conform through 36, and 36 conforms as the control, through A7 and F18-A7. **Rule 4.** It conforms for all but 32. A8 restores the blocking navigator in the real `BnRuntime.swift`, and A9 drops the navigate. A2 moves the generated `backState`. A5, A6 and A7 move the arms' completions, and A1 sends op 6 to the FaultNotice arm. 32 is **partial, named**. A14b moves the probe. A14, the brief's `Task.Yield` mutation, kept the hold, which is measured. Why it kept the hold is read from the code, not measured: the yield resumes on the render thread, where the synchronous capture still blocks the lane. Only a handler that awaits the capture removes the hold. **Rule 5 conforms.** The header names the notification navigator under a held lane, which is pinned only by reading its code, the .NET send of ops 6 and 7, and the Android arms. The control's doc comment says it is the twin of the JVM control. **Rule 7, per fact.** 31 conforms. A8, run 36922005592, reds the 1 s return at `3.046574115753174 s` and the released-early anchor, and A9, 36922096293, reds the Settings anchor. The vacuity contrast is observed: A15b, 36927316506, with no hold, reds the pin only at its release-order anchor, and A16, 36931016408, the same with the two release anchors removed, is green. 32 is **partial, named, after F20**. A14b, 36925692173, and F20-A14b, 36928156813, red it. A14, 36925687221, and A15, 36927312660, are its greens under the hold the brief's mutation kept. The ordering assertion `XCTAssertGreaterThanOrEqual(b, r)` is entered by no row. 33 is **partial, named**. A2, 36919860556, enters `backState == 6`, and `backUnhandled == 7` is entered by no device row. 34 is **partial, named, after F18**. A5, 36920952341, and F18-A5, 36923728779, red the status, A1, 36919855881, reds `errors.isEmpty`, and A17, 36931019825, reds the rc. The payload-nil assertion is not entered. 35 is **partial, named, after F18**. A6, 36920957179, F18-A6, 36926848375, and A17 red it. The payload and `errors.isEmpty` are not entered. 36 is **partial, named**. A7, 36921401411, and F18-A7, 36926894663, are the negative, with every arm pin green, and A17 enters the rc. Its `errors.isEmpty` half is not entered. **16.2's list for the final review, as run:** the blocking navigator is A8, the no-op navigator A9, an arm calling `completeUnknownOp` A5 and A6, and an `async` Hold A14 and A14b. Not run as written: deleting the `backState` or `backUnhandled` arm outright, and removing the Hold tap. |
| `GeneratedSymbolShadowTests.NoShippedShellSource_CallsTheBlockingHostEventDispatch` and its control `OffendingCallDetector_MatchesACall_AndNotTheDeclaration` (16.2, 2 facts; 16.6 pins 4 and 5) | Every line of the shipped Kotlin and Swift shell sources ↔ the rule that none calls the blocking `dispatchHostEventAndWait`. The control: the line detector ↔ three call cases, an expression-bodied forwarder, and the two real declarations read from the tree | **A tree-reader, listed here because no census or register row assessed it before 16.6.** It reaches the tree through `BnRepo.Root()`, so `PinPopulationTests` counts it, but census rows 14a and 14b predate it. **The cells come from [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §8, pins 4 and 5, and the runs from its §2 and §7.** Every run is local, `dotnet test tests/BlazorNative.Runtime.Tests` filtered to `WireVocabularyCodegenTests` and `GeneratedSymbolShadowTests`, unless the row says otherwise. **Rule 2 conforms, after F7.** Three named anchors assert that both `MainActivity.kt` copies and `BnRuntime.swift` were scanned, and every Kotlin scan root and the Swift `BnHost` directory is asserted to exist. F7-before measured the hole: with both `src/main/kotlin` roots renamed, `BlazorNativeRuntime.kt` left the scan and the pin stayed green. F7-after reds with the missing root named. D15b step 2 reds the repo anchor on a broken `androidMain` root, and D15b step 1 is the vacuity contrast, green with the anchors off. D15 step 1 depends on the host: Windows still walks a lower-cased root. The control reads each real declaration from the tree and asserts it found exactly one, and F2-anchor and F2-anchor-swift red it with `found 0`. **Rule 3 conforms.** The control controls the pin. D10, D11 and D13 are positive cases against real files in both languages, D14 against the template, and D12 is the negative: a call in a comment stays green. In the control, three call cases return true, the two real declarations false, and the forwarder true. D16 reds the declaration case with the real Kotlin declaration quoted, and D17 reds the call cases. **Rule 4 conforms, after F4 and F5.** D10, D11, D13 and D14 each red with a path and line. Since F4 the path is repo-relative, so the template copy reads `templates\…\MainActivity.kt:634` and the repo copy `src\…\MainActivity.kt:621`. Since `2d3beb3` the control's declarations come from `BlazorNativeRuntime.kt` and `BnRuntime.swift`, not a hand copy: F2-anchor and F2-anchor-swift red a renamed declaration, and F2-real a declaration that gains a call. Since F5 each call case names itself in its message. **Rule 5 conforms.** The pin's DOES NOT COVER block names `dispatchEventAndWait`, `src/jvmHost/kotlin`, reflection and stored references, and Objective-C++. Since F23 the control has a block of its own. It says the detector reads one line at a time, so a call split across lines is not matched, nor a reference with no call parentheses. It also says the declaration token is stripped whatever modifier precedes it, so any declaration of the name counts as a declaration, and that only the two real declarations are fed as negatives. **Rule 7: every path redded, from the §2 and §7 rows.** F1-before is the vacuous green that 16.6 fixed: an expression-bodied forwarder on the declaration line passed the unfixed scan, which skipped the whole line. F1-after reds it, naming `BlazorNativeRuntime.kt:368`, after commit `dda5582`. D10 to D14 enter the plain path and the comment suppression, D15b the vacuity contrast, D16 and D17 the control's two halves, and F7 the dropped root. |
| `SlowHandlerWarningTests` (16.3, Runtime, 17 facts; 16.6 pins 8 to 24) | A slow synchronous part, of a UI handler or of a back or lifecycle arm, timed by the renderer's clock ↔ one `BnLog` Warn per call site per session. The Warn names the handler, its event, its owner and the milliseconds, never the payload. Warnings are capped at 32, then one suppression line, then silence | **The cells come from [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §8, pins 8 to 24, and the runs from its §4 and §7.** Every run is local, `dotnet test tests/BlazorNative.Runtime.Tests --filter "FullyQualifiedName~SlowHandlerWarningTests"` unless the row says otherwise. §4's fact-to-red map names the rows that red each fact. **Rule 2 conforms for 16 of 17.** Each fact that needs a logged line asserts a count or a `Single`, most behind a `RunsOf` anchor, and E1 or E5 reds it at zero lines. `AHandlerUnderBudget_LogsNoWarn` asserts that the dispatch read the clock at least twice, and E5 reds it there. It is **partial, named** for `TheCallSiteMap_StaysFlat_…`. E21 reds its floor anchor, and E22 shows it green with the floor deleted, but its second anchor, 50 distinct ids, is entered by no row. Also entered by no row, in facts whose Rule 2 cell still conforms: the `RunsOf == 1` anchor of `AHandlerOverBudget_…`, the distinct-ids anchor of `ACapturingLambda_…` and the `RunsOf` anchors of `TheCap_…`. **Rule 3 conforms.** `TheLogCapture_SeesAWarn_AtTheDefaultLevel_PositiveControl` is the class's control. E1 reds it at the level, `Expected: Warn, Actual: Error`, and E19 at the capture branch. It has no control of its own, since the `BnLog` sink is the layer the rest trusts. The other facts are positive in themselves, or have their contrast in a row: E3 for the warned set, E12 for the payload, E13 for a key by handler id, E20 for BnInput's known limit, and E9 for the call-site map. **Rule 4 conforms for 14 of 17.** It is **partial, named** for three. For `AHandlerOverBudget_…`, E2 reds the milliseconds and E1 and E5 the line, but no row changes `handler {id}`, `'click'`, `{budget} ms budget` or the last sentence. For `TheWarnedSet_…`, E3 makes both fields static together, and since F11 the red names the surviving set, but neither field alone is mutated. For `TheCap_…`, E8 and E8b each enter one cap check, and since F13 each red names its rule, but the constant `SlowHandlerWarningCap` moving is not mutated, since the pin asserts the literal 32. **Rule 5 conforms.** The class header's DOES NOT COVER list names the no-session arm, the queue wait, the async remainder, the fallback key's collisions, the framework-owned handlers and BnInput's limit, the arms not named, the cap as a quiet path, and the shells. Since F17 it also names host-event payloads, what a reset is, and the call-site map beyond its bound. **Rule 7: every fact redded, with named gaps.** E1 to E26 re-ran 16.3's mutations on current code and added more, and every fact has a recorded red. Four rows redded at an assertion that did not name its cause, E3, E4, E8 and E8b; F11 to F13 fixed them, and F15 fixed E18's two bare reds. **Not fixed, disclosed:** the 13 facts that red under E1 and the 11 that red under E5 name the symptom, a missing line, and not the cause. The cause is named in the same run by the control under E1 and by `AHandlerUnderBudget_…`'s clock anchor under E5. 16.3's mutation 7 does not reproduce on current code, E14; E26 covers its over-merge direction. |
| JVM `SlowHandlerProbeTest.kt` (16.3, 1 test; 16.6 pin 25) | The published win-x64 dll's `SlowHandlerProbe` page, three `BnButton`s with different slow handlers, the third a capturing lambda ↔ three warnings, each naming the app's own method, never `BnButton` | **The cells come from [the 16.6 record](plans/2026-10-01-phase-16.6-record.md) §8, pin 25, and the runs from its §4 and §7.** Each run is local, from a published win-x64 dll, with the exit code and the dll's timestamp in the row. **Rule 2, partial, named.** E23, E23-after and E24 enter the lambda assertion and the size assertion. The echo-starts-at-zero anchor and the handler-id-changed anchor are entered by no row. **Rule 3 conforms.** E24 shows the pin tells the owner key from the tree-owner fallback in the published dll, `gave 1 warnings, not 3`. E23 shows it tells the capturing lambda's closure method from the rest. E25 is the clean dll, green. **Rule 4 conforms.** E23 moves the third button's key, and E24 all three, both on current code. The EventCallback accessor path alone is not mutated in the dll: E16 covers it on CoreCLR only. **Rule 5 conforms, after F14.** The KDoc names the ChildContent shape, the budget, the cap, the other platforms' metadata policy and the shared process-global session. Before F14 it also listed the lambda shape, which the probe does cover. **Rule 7, from the E rows.** E23 reds the lambda assertion, at first with a bare `NoSuchElementException`, F16-before. Since F16 the red names the unresolved method and the accessor, E23-after. E24 reds the size assertion, repeating 16.3's `10-AOT` on current code. E25 is the clean contrast. |
| XCTest `BnBiometricsTests.swift`, the 16.4 pins (16.4, 3 tests) | `testTheBootHarnessWaitsForTheArmedReplyNotTheInFlightFlag`: the boot tests' `awaitArmedReply` ↔ the rule "return only once the seam holds the reply", called inside the window between the record and the arming, which `beforeEvaluationArmedHookForTest` holds open. `testHostCallBeginReturnsWhileContextCreationIsBlocked` and `…ForCheck`: `hostCallBegin` for authenticate and for check, called off main ↔ a return within 5 s while a blocked `contextFactoryForTest` holds `LAContext` creation, then exactly one completion after the release | **Rule 2 conforms, through anchors.** Pin 1 asserts that the queue reached the window, that the flag is already true and that no reply is armed yet, then that the returned reply completes the call Cancelled. The two contract pins assert that the factory was entered, and that nothing completed before the release. **Rule 3, partial, named.** The window and the blocked factory are the forcing seams, and each pin proves its interleaving happened. There is no separate control showing the harness or the begin path red on a known-bad fixture inside the passing suite; the single-mutation runs stand in for it. **Rule 4 conforms.** The seams and `drainForTest` are compile-time symbols on `BnBiometrics`, the ops are the generated `BnHostCallOp.biometrics`, and the calls go through the real `AppleShellBridge.hostCallBegin`. **Rule 5 conforms.** Each header states it: pin 1 pins the harness, not LocalAuthentication, and not how long a cold `LAContext()` takes; the contract pins prove the calling thread is free, not that the queue is fast. **Rule 7 conforms, on device, one mutation per run.** The runs are in [the 16.4 record](plans/2026-09-28-phase-16.4-record.md), section 8. M1, the harness back on the in-flight flag, reds pin 1 and both boot tests. M2, the authenticate context created on the calling thread, reds the authenticate pin alone. M3, check run synchronously, reds the check pin alone. **The vacuity contrast is observed.** A handler that ignores `contextFactoryForTest` reds both contract pins on the `entered` anchor, and removing the `beforeEvaluationArmedHookForTest` call reds pin 1 on its window anchor. With the seam bypassed and M2 and M3 applied, deleting the `entered` anchor still leaves both pins red on the second anchor, "no outcome before the context exists". Deleting that one too turns them green, so the two anchors together are load-bearing. An earlier run with all four mutations in one commit, `35697b7`, is superseded. |
| XCTest `BnSecureStorageTests.swift`, the 16.4 pins (16.4, 3 tests) | `testHostCallBeginReturnsWhileContextCreationIsBlocked` and `…ForGetWithAuth`: `hostCallBegin` for an auth-bound set, and for getWithAuth of an auth-bound item, called off main ↔ a return within 5 s while a blocked `contextFactoryForTest` holds `LAContext` creation, then Ok, and the value for the read, after the release. `testACompletionDoesNotReleaseAnotherRequestsRetainedContext`, from the final review: a getWithAuth held at its gate ↔ its context still retained after a plain get completes, and released on its own completion | **Rule 2 conforms, through anchors.** Each asserts that the factory was entered and that nothing completed before the release, then the status, and for the read the `{"value":…}` payload. **Rule 3, partial, named.** The blocked factory is the forcing seam; there is no in-suite control, and the single-mutation runs stand in for one. The retention pin anchors on the gate being held and the context being retained before the plain get, and its last assertion is the release on its own completion, so a funnel that never releases reds too. **Rule 4 conforms.** The seam and `drainForTest` are compile-time symbols on `BnSecureStorage`, the op is the generated `BnHostCallOp.secureStorage`, and the calls go through the real `AppleShellBridge.hostCallBegin` and the real Keychain. **Rule 5 conforms.** The header says only context creation is blocked: the Keychain calls moved to the same queue but no seam makes them slow, so they are covered by construction. It also says the pin proves the calling thread is free, not that the queue is fast. **Rule 7 conforms, on device, one mutation per run.** The runs are in [the 16.4 record](plans/2026-09-28-phase-16.4-record.md), section 8. M4, secure storage's `begin` running the action inline, reds both contract pins on the 5 s return. R, `complete` clearing every retained context as the pre-review shared slot did, reds the retention pin alone. **The vacuity contrast is observed**, as for the biometrics row: bypassing the factory seam reds both contract pins on `entered`; with M4 applied and `entered` deleted they stay red on "no outcome before the context exists"; with both anchors deleted they pass. |

---

## Rule 7 — Its mutations must exercise the PIN's own code paths, not only its subject

A pin is only as trustworthy as the mutations that were run against it, and a mutation set built
from *plausible subject behaviours* tests the wrong thing. **Reason about the pin's coverage, not
its assertions.**

The scar, from milestone 14: **six mutations missed a real hole because every single one placed its
token alone on its own line**, and so never entered the branch where the bug lived. Each mutation
was a perfectly reasonable thing for the subject to do. Collectively they exercised one path
through the pin.

**The suppression path matters most**, because that is where a pin is *designed* to go quiet and so
where it can go quiet by accident. Comment stripping, allow-lists, exemption filters, `#if`
handling, ignore manifests — every one of those is a branch whose job is to return "nothing to see
here", and every one of them needs a mutation that lands inside it rather than beside it.

A mutation set that earns trust covers, at minimum:

- **The plain case** — the bug alone, where the pin obviously should red.
- **Each suppression branch, entered** — the token *inside* a comment, *after* a string literal on
  the same line, inside an allow-listed entry, adjacent to an ignore marker. Not on its own line.
- **The negative** — something the pin must *not* flag, so the suppression still works and you have
  not just made it red at everything.
- **The vacuity contrast** — break the walk or the pattern, remove the anti-vacuity assertion, and
  confirm the pin goes green. This is the one that turns "it has a floor" into "the floor is
  load-bearing", and it has to be observed rather than asserted.

### The protocol

Run mutations **one at a time and revert between them**, so each red names one cause. When fixing a
pin that was demonstrated broken, **reproduce the green first against the unfixed code**, then show
the red after — a fix claimed without the before is a fix nobody can check. When a mutation reds,
check the reported `file:line` is the one a human would open; line-number fidelity through a
stripper is exactly the kind of thing that breaks silently during a refactor.

---

## Rule 8 — Consolidate, do not port

When you find a second copy of a pin's machinery — a walk, a stripper, a parser, a roster — the fix
is to **merge the copies**, not to carry the patch across.

Two copies that agree today are the *precondition* for the twin-divergence class, not evidence of
its absence. This is the entire lesson of milestone 14 applied to the tooling that was supposed to
prevent it: 14.4 hardened the comment stripper in front of it, which had **one** caller, and never
knew the shared copy with **three** callers existed. The fix landed on the less-used copy while the
widely-used one kept the bug, and four more copies were still undiscovered.

One `BnRepo.Root()`, called by every test that reaches the checkout — 27 files at the time of
writing. One `CommentStrippedSource`, 8 callers — **10 since phase 15.1** and **12 since 15.2**,
which added `ShellSourceRootsDriftTests` and a unit-test class over the helper itself that is not
a pin, and that growth is the rule working rather than an exception to it.

**And the `BnRepo.Root()` figure is now a proxy that has come apart from the thing it proxies.**
15.2 routed `NSLogDriftTests` through `ShellSourceScan`, so it reads the checkout through a door
in another file and the grep no longer returns it — **a pin missing from a name-based
enumeration, in the document that scores name-based substitutes four-for-four**. The enumeration
used elsewhere is widened to
`grep -rlE "BnRepo\.Root\(\)|ShellSourceScan\." tests/ --include="*.cs"`, which returns 29 and
restores it. Whether the population key should be the call graph instead is **#375**, ruled to
15.4. The caller count grows with the suite and should; the
count of *implementations* is the one that must stay at one.

### "One implementation" is a claim about REACHABILITY, not about file count

The tenth caller is the one that makes the point. `CommentStrippedSource` lived in
`tests/BlazorNative.Runtime.Tests`, and neither of the other two test projects referenced it, so
there was exactly one implementation and **two of the three test projects could not call it**.
By test count that is the smaller share, and the count is the wrong measure: what was out of reach
was not a fraction of the assertions but every pin either of those projects will ever carry. That is not a tidiness problem. `ShellStyleTableDriftTests` in `BlazorNative.Renderer.Tests` carried a
**disclosed false green** over block-commented dispatch arms whose own comment named the fix and
named the blocker: the helper was in the wrong project. It cost nothing to move it to
`tests/Shared` and link it through `tests/Directory.Build.props` the way `BnRepo.cs` is linked, and
the gap closed on the next run.

So when Rule 8 says *merge the copies*, the merged thing has to land somewhere every caller can
reach. A shared helper a project cannot reference is a copy waiting to be written.

### The corollary Rule 8 does NOT state, and 15.1 had to decide

**A NEW GRAMMAR IS NOT A SECOND COPY, BUT IT STILL BELONGS IN THE SAME HOME.**
`BnSafeAreaCoverageTests` needed Razor's `@* … *@`, which `CommentStrippedSource` did not know. The
usual argument for consolidating did not apply — a Razor stripper cannot diverge from a C#/Kotlin/
Swift one, because they implement different grammars and share no truth. What *does* apply is
discoverability: the next author who needs to scan a `.razor` file looks in the one comment
stripper, and finding nothing there is exactly how a ninth copy gets written.

So it went in — as `StripRazor`, **a sibling entry point rather than a mode flag on `Strip`**. The
distinction is load-bearing and is the other half of the ruling. A `razor: true` parameter would
thread through `Lines` and `NumberedCodeLines` and would need a default for eight callers who must
never get Razor behaviour; a defaulted flag on shared machinery is the mechanism by which a helper
change leaves the whole-suite count correct while quietly moving what ONE pin sees. Adding a
sibling left `Strip`'s body unedited, so those eight are unchanged **by construction** rather than
by re-verification — which is the property worth engineering for when the shared thing has this
many dependants.

---

## The enforcement verdict — Rule 2 is NOT enforced mechanically, and will not be

**The open question milestone 15 was opened to answer: can *"a pin cannot pass while checking
nothing"* be enforced by a machine?**

**Answer: no, and not for want of trying to find a way.** Rule 2 is a review obligation carried by
the checklist below. Nothing in CI checks it, nothing is planned to, and this section exists so that
nobody downstream mistakes the guard that *does* exist for the one that does not.

### What IS enforced, precisely

`PinPopulationTests` enforces **reachability** — Rule 6. Every test that reaches the checkout must do
so through `BnRepo.Root()`, so the population stays enumerable. That is a genuinely mechanical
property and it is genuinely enforced.

**Reachability is not anti-vacuity.** A pin can route through `BnRepo.Root()`, appear in every
census, satisfy the one enforced rule in this document, and still pass while scanning nothing. Four
facts in the population did exactly that when the 15.0 census measured them, and nothing mechanical
said so in either direction — a human reading assertions is what found them, and phase 15.1 is what
closed them. Enumerability is what makes the population *countable* so a human can judge it; it does
not do the judging.

### Why the cheap mechanism fails, measured rather than argued

The obvious convention test is *"every pin fact must contain a count-style assertion"* — an
`Assert.NotEmpty`, a `Count >= n`, a floor of some shape. It is a few dozen lines and it would run in
milliseconds.

**Run against the four defects the 15.0 census found, it scores zero.** They are the illustration
and they are closed; the scoring is what does not decay.

| The defect | Does the fact execute a floor? | A presence check says |
|---|---|---|
| `ShellStyleTableDriftTests`, all 3 facts | **yes** — `ParseNameTable` ends `Assert.NotEmpty(names)` | conforms |
| `DispatchSurfaceDriftTests.EveryDispatchNamedDeclaration_IsDeclaredOrIgnored` | **yes** — `Surface()` asserts `methods.Count >= 4` | conforms |

Every one of the four facts that could pass while scanning nothing **already had an anti-vacuity
assertion, and executed it**. *(All four were closed by phase 15.1, tasks 1 and 2. The figures in
this section — 4 true positives against 98 facts flooring through shared helpers — are the
population as 15.0 censused it, and the cost argument below is made against that population. See
the closing note on why this verdict is revisitable rather than settled.)* The census found this and it is the sharpest thing it found: the useful
distinction is not *has a floor* but **which side of the comparison the floor guards**.

- `ShellStyleTableDriftTests` computed `routed.Except(dispatched)` and floored `dispatched` — the set
  being **subtracted**. `routed`, the set being **iterated**, was unfloored; empty it and all three
  facts went green. *(15.1 task 1 floored the iterated set. Both halves of the mutation were run:
  with the floor in place the fact reds on the count, with it removed the same tree goes green.)*
- `DispatchSurfaceDriftTests`' bare fact floored the **manifest** it compares against. The **scanned**
  set — the `dispatch`-prefixed declarations the `foreach` walks — had no floor at all. *(15.1 task 2
  floored the scanned set, per shell. This is issue #357.)*

A mechanism that detects the presence of a count assertion would score both as conforming and hand
out a green over the only demonstrated false-green channel in the repo. By this document's own Rule 5
test — *a limit that can only cost you a red is a footnote; a limit that can hand you a green is a
defect* — such a check is not a weak guard. It is the forbidden shape, with the extra harm that its
name would tell readers the property was covered.

It would also be the **fifth** instance of Rule 1's four-for-four scar: a population judged by a
proxy for the thing rather than by the thing, wrong in the same direction as all four before it.

### Why the expensive mechanism is not worth building either

The version that *would* catch all four is binding-aware: for each iterated expression inside a fact,
resolve the roots of that expression and require a floor dominating **each root**, in the same method.
`routed` is a root with no floor; `DispatchNamedDeclarations(source)` is a root with no floor. Both
are flagged. A Roslyn analyzer over test method bodies can do this. So the property is decidable in
principle, and the honest verdict is about cost and collateral rather than impossibility.

Three things sink it:

1. **Tractability requires the duplication Rule 8 bans.** In-method dominance is checkable; the
   floors in this repo are not in-method. `ParseNameTable` and `Surface()` floor inside a shared
   helper *on purpose* — one implementation, several callers, which is the target shape Rule 8
   demands. An analyzer that only sees the method body forces every caller to grow its own copy of a
   floor the helper already performs. Making the analyzer interprocedural instead means resolving
   floors across helpers, across partial classes, and in one live case across an assembly boundary:
   `routed` is `NativeRenderer.YogaStyleAttributes`, a **generated** product-assembly static, and
   whether it can be empty is a fact about the generator, not about the test.

2. **The false-positive surface dwarfs the true one: 98 conforming facts against 4 defects, as the
   15.0 census measured the population.** Most conforming pins floor through a shared helper or a
   structural assertion that no dominance rule recognises. A check that reds 98 correct tests is
   suppressed within a week, and a suppressed analyzer is worse than no analyzer, because the
   suppressions look like considered exemptions. **The ratio is what carries the argument, not the
   pair of numbers** — and the ratio has since got worse, not better. Phase 15.1 closed all four
   defects and added nine control facts of its own, so the population is now **111 pin facts with
   zero known true positives**: the only thing such a check could still produce is the false half.

3. **The population it can see is the wrong population.** Rule 2 is about assertion shape, not file
   access — `RouteMenuDriftTests` reads no files, was vacuous-capable until 15.4 (#375), and is outside Rule 6's
   population by construction. Any mechanism keyed on callers of `BnRepo.Root()` cannot see it. The
   set Task 1 made enumerable and the set Rule 2 governs are not the same set, and the gap is
   invisible from inside the mechanism.

### What replaces it

Nothing that claims to be enforcement. Three honest things instead:

- **The checklist, at review time.** Rule 2 and Rule 3 are the two lines a reviewer must actually
  check by reading. That is the cost of the property, and it is now written down rather than assumed.
- **Rule 7's vacuity contrast, performed and recorded.** Break the walk, remove the floor, confirm the
  pin goes green, put it back. It is **observed, not asserted**, and no static check substitutes for
  running it. What can be mechanised is the *recording*, never the observation.
- **An inventory guard rather than a judgement guard, if 15.1 wants one.** The population is
  enumerable, so a pin that reds when the caller list changes without the census being updated is
  decidable and honest: it catches *a new pin arrived and nobody judged it*, which is a different and
  achievable claim from *this pin is floored*.

And one trap for whatever sweep looks for the gaps: **read the assertion, never the comment above
it.** `ReleaseWorkflowPinTests` labelled an assertion **"THE POSITIVE CONTROL, first"** and that
assertion is a Rule 4 subject-moved guard — it proves the file is still the release path and proves
nothing about the `VersionOverride` regex that is the actual detector. A grep for the phrase would
have scored it as done. *(Phase 15.1 corrected the label and gave that detector a real control. The
lesson outlives the instance, which is why it stays: the mislabel was found by reading the
assertion, and nothing mechanical would have found it.)*

### This verdict is a judgement about cost, and nothing pins it

**Stated plainly, because Rule 5 applies to this section as much as to a pin.** The conclusion above
is a judgement about cost and collateral measured against the population **as the 15.0 census
measured it** — 4 true positives, 98 facts flooring through shared helpers, one root reaching across
an assembly boundary. Change that population and the arithmetic can change with it.

**And it has already changed once.** Phase 15.1 closed all four true positives and added nine
control facts, so the same measurement today reads **0 true positives against 111 pin facts**. The
verdict did not move, because it never rested on the count: a presence-style check still cannot tell
which side of a comparison a floor guards, and an interprocedural one still has to resolve floors
across helpers and one assembly boundary. What moved is the incentive, and it moved the wrong way
for the analyzer. **A future contributor is entitled to revisit it**, and the honest form of that is
to **rewrite this section rather than leave it standing**:
if a mechanical anti-vacuity check ever lands, the sentences above become false and **nothing in CI
will red to say so.** A document asserting a safety property that nothing enforces is this repo's
most expensive bug class, and the only defence available here is that the section names the exact
check it rejects, so a reviewer has something specific to match the new one against.

### What this reshapes

15.1 is not *build the mechanism*. **It is nine fixed-point assertions**, six of them copyable from
`ConsoleErrorDriftTests`, `NSLogDriftTests` and `AndroidLogDriftTests`, plus a floor moved to the
iterated set in two files. One lesson, four worked examples already in the tree, nine places to apply
it. See `docs/plans/2026-09-22-phase-15.0-census.md` §7 for the sizing.

**That is what 15.1 did, and the sizing held.** All nine landed, plus the two uncontrolled detectors
outside the pin population, for **twelve** new control facts and one real bug fix — nine inside the
pin population, which is the `102 + 9 = 111` the enforcement verdict above counts, and three more
from those two detectors, which are `exempt — not pins` and never entered that arithmetic. Two of
the nine needed something the shape above does not describe — see Rule 3's corollary — and one
could not be given a tree anchor at all; that one ships disclosed at the pin with the repair named. **Do not read this
paragraph as saying coverage is now total.**

---

## A checklist for a new pin

**Only the first box is checked by CI.** Every other line is a human reading the assertion, for the
reasons set out directly above.

- [ ] It reaches the tree through `BnRepo.Root()`. *(Rule 6 — enforced by `PinPopulationTests`)*
- [ ] It asserts its subject was found — a measured floor with stated headroom, not a round number,
      and not one that moves with build state. *(Rules 2, 2-corollary)*
- [ ] It asserts its **detector** still detects — a positive control, a fixed point, or a
      structural assertion about the parse. *(Rule 3)*
- [ ] It reds, with a message naming the subject and the pattern, when its subject moves.
      *(Rule 4)*
- [ ] Its doc comment states what it does **not** cover, and whether an uncovered case fails safe
      or fails green. *(Rule 5)*
- [ ] Its mutation set enters every suppression branch, includes a negative, and includes the
      vacuity contrast — run one at a time, reverted between. *(Rule 7)*
- [ ] It reuses the shared machinery rather than growing a private copy of it. *(Rule 8)*

---

## Where the machinery lives

| Helper | Job |
|---|---|
| `tests/Shared/BnRepo.cs` | `Root()` — the one walk to the checkout. `TestBinaryDirectory()` — the test's own build output, which is a different job |
| `CommentStrippedSource` | `Strip`, `Lines`, `NumberedCodeLines` — string-literal-aware comment removal for C#, Kotlin and Swift, with nesting block comments and one-based line numbers against the original file. `StripRazor` (15.1) adds Razor's `@* … *@` for `.razor` sources and then composes `Strip` for the C# forms a `@code` block carries — a sibling, deliberately not a mode on `Strip`; see Rule 8's corollary |
| `PinPopulationTests` | enforces Rule 6 — reachability only — and is therefore the thing that keeps the population enumerable. It does **not** enforce Rule 2 |

Known limits of the shared stripper, restated here because Rule 5 applies to it too.

**The paragraph that used to stand here was wrong, and the way it was wrong is the lesson.** It
said a raw or multiline string literal "toggles its string state wrongly until the next newline
resets it", and filed that under *bounded*. Issue #364's **F3** is the counterexample: the newline
reset put the raw string's **body** back into **code** state, where a `/*` opened a block comment
that ran to the next `*/` — arbitrarily far away, in another declaration entirely — and deleted the
live code between them from the scan. The reset did not bound that mis-parse. It **caused** it.
That is the over-strip direction, which is a false **green**, and it is the same direction the
stripper's own unterminated-opener rule already existed to avoid.

**And then the FIX for it did the same thing with the sign flipped, which is the part worth your
attention.** Its first cut tracked raw state with no bound at all. C# spells a verbatim string `@"`
and escapes an embedded quote by **doubling** it, so a verbatim literal beginning with a quote is
`@"""` — three consecutive quotes that open a fence nothing ever closes.
`ShellFrameTableDriftTests.cs:296` and `TemplateDriftTests.cs:1344` each went blind to **end of
file** behind one, 358 lines between them, inside `PinPopulationTests`' own walk, and the whole
suite stayed green.

> **Measured 2026-09-23, phase 15.2**, under a mutation restoring the defective state: of the
> **1145** tests in the .NET suite, **1142 passed** — Runtime 975/978, Renderer **140/140**,
> Analyzers 27/27. The three failures were the three facts 15.2 added for this defect, and nothing
> else in the suite noticed. The Renderer project *contains* one of the two blinded files and was
> **fully green**.

That block is a **dated measurement, not a standing fact** — read it as what the suite did on that
day, and do not maintain the numbers. Its point survives the counts going stale: a defect that
switched comment stripping off across 358 lines, inside the walk of the pin that enumerates the pin
population, was invisible to every test in the repository except the ones written for it. Review
caught this, not CI. *(The figure was restated once, from "1115 of 1118" — Runtime plus Renderer
only — against the whole suite. Naming the population made it stronger, not weaker.)*

So the rule the stripper now holds is neither *strip more* nor *strip less*. It is: **no input may
make the stripper blind past the construct that confused it.** Raw state is entered only when a
closing fence already exists ahead, found with the same predicate the closing arm uses — and
*literally* the same one: the ordinary-string conjunct sits inside the **opening** disjunct so the
closing arm carries no extra condition, which is what keeps the termination argument from resting
on an unstated lemma about when that flag can change. A state that is entered is left, **by
construction, for all inputs**. That is a stronger claim than the newline reset ever supported, and
it is the reason this section can say what follows without hedging on today's tree.

**Read the failure directions before the limits, because the natural assumption about them is
wrong.** Over-stripping is a false **green** — a pin's subject is deleted before it is looked for.
Under-stripping is a false **red** for an *absence* pin, which is most of them, and a false
**green** for a *presence* pin — and this document mandates presence pins by design, because every
Rule 3 fixed point is one. `AndroidLogDriftTests`' `Assert.True(hits > 0, …)` is satisfied by a
commented-out `Log.i` that under-stripping left visible; that was **measured going 0 → 1**, not
argued. *"It only ever costs a red"* is not available as a defence for either direction, and the
first cut of the 15.2 fix claimed it in exactly those words — in the commit whose entire purpose was
deleting an unenforced safety claim.

What remains, each with its direction:

- **C#'s fence-width rule is not implemented.** A run of three or more quotes opens, and any later
  run of three or more closes — so a C# `""""` fence, which the language requires to close on four
  or more, would close here on three. The runs that exist in the tree are `ItemsJsonTest.kt:91` and
  `:106`, Kotlin rather than C#, and both parse correctly because whole runs are consumed. **Either
  direction**, bounded by the construction above. The narrowness is deliberate: a general
  raw-string parser is how this pin family got into trouble.
- **Swift's custom delimiters `#"…"#` and `#"""…"""#` are not understood.** The first *is* live —
  `BnWidgetMapper.swift:3461`, inside both `ShellStyleTableDriftTests`' and `NSLogDriftTests`'
  walks, and again across `BnHostTests/*.swift` — and costs nothing, because `#"` presents only a
  one-quote run and reads as an ordinary literal exactly as it did before 15.2. The second does not
  occur in the tree. **Red for absence pins, green for presence pins**, bounded to the file.
- **A C# `'"'` char literal still toggles the ordinary string state once**, and an unbalanced quote
  in an ordinary literal still mis-parses. Those *are* bounded by the newline reset, which applies
  to the ordinary flag and not to the raw one.

The two wider spellings above **do** occur in scanned files, and the previous version of this
section said they did not. That sentence is gone rather than softened: a limit disclosed against a
factual claim that is wrong is worse than an undisclosed one, because the disclosure makes the
wrong placement look considered. What replaces it is not another fact about today — it is the
entry precondition, which does not decay when someone adds a file.
