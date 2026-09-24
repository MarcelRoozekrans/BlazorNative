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

`RouteMenuDriftTests` scans **no files at all** and still has the shape:
`EveryRoutedPage_ExceptTheTwoExemptions_HasAMenuRow` passes over an empty page list. "It doesn't
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
   access — `RouteMenuDriftTests` reads no files, is vacuous-capable, and is outside Rule 6's
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
