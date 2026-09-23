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
below for why a mechanical check was considered, costed and rejected, and why the four facts in this
repo that can pass while scanning nothing all already carry an anti-vacuity assertion.

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

**Be honest about where we are.** Every anti-vacuity assertion added during the milestone that
wrote this standard — including its author's own floor of 100 — proves only the walk. That is a
real gap across the existing population, and closing it is part of why this document exists.

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
writing. One `CommentStrippedSource`, 8 callers — **9 since phase 15.1**, and that growth is the
rule working rather than an exception to it. The caller count grows with the suite and should; the
count of *implementations* is the one that must stay at one.

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
facts in the population do exactly that today. Enumerability is what makes the population *countable*
so a human can judge it; it does not do the judging.

### Why the cheap mechanism fails, measured rather than argued

The obvious convention test is *"every pin fact must contain a count-style assertion"* — an
`Assert.NotEmpty`, a `Count >= n`, a floor of some shape. It is a few dozen lines and it would run in
milliseconds.

**Run against the four known defects, it scores zero.**

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

- `ShellStyleTableDriftTests` computes `routed.Except(dispatched)` and floors `dispatched` — the set
  being **subtracted**. `routed`, the set being **iterated**, is unfloored; empty it and all three
  facts go green.
- `DispatchSurfaceDriftTests`' bare fact floors the **manifest** it compares against. The **scanned**
  set — the `dispatch`-prefixed declarations the `foreach` walks — has no floor at all.

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

2. **The false-positive surface is 98 facts against 4 true positives.** Most conforming pins floor
   through a shared helper or a structural assertion that no dominance rule recognises. A check that
   reds 98 correct tests is suppressed within a week, and a suppressed analyzer is worse than no
   analyzer, because the suppressions look like considered exemptions.

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
is a judgement about cost and collateral measured against the population as it stands today — 4 true
positives, 98 facts flooring through shared helpers, one root reaching across an assembly boundary.
Change that population and the arithmetic can change with it. A future contributor is entitled to
revisit it, and the honest form of that is to **rewrite this section rather than leave it standing**:
if a mechanical anti-vacuity check ever lands, the sentences above become false and **nothing in CI
will red to say so.** A document asserting a safety property that nothing enforces is this repo's
most expensive bug class, and the only defence available here is that the section names the exact
check it rejects, so a reviewer has something specific to match the new one against.

### What this reshapes

15.1 is not *build the mechanism*. **It is nine fixed-point assertions**, six of them copyable from
`ConsoleErrorDriftTests`, `NSLogDriftTests` and `AndroidLogDriftTests`, plus a floor moved to the
iterated set in two files. One lesson, four worked examples already in the tree, nine places to apply
it. See `docs/plans/2026-09-22-phase-15.0-census.md` §7 for the sizing.

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

Known limits of the shared stripper, restated here because Rule 5 applies to it too: a raw or
multiline string literal, and a C# `'"'` char literal, toggle its string state wrongly until the
next newline resets it. No file currently holding a scanned marker contains either — but that is a
fact about today, and nothing pins it.
