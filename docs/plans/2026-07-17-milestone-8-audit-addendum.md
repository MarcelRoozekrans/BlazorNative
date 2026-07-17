# Milestone 8 — final audit ADDENDUM: two decisions the audit passed were reversed the same day, on purpose

**Status:** addendum (2026-07-17). Succeeds
[the M8 final audit](2026-07-17-milestone-8-final-audit.md) — **it does not amend it.**
Written in Phase 8.6 (`phase-8.6-release-automation`, Gate 3).

---

## Why this document exists

**Three shipped places cite it by name, and until this file existed all three resolved to a
404** — one of them inside a **RED message the owner reads at the Release UI**, at the exact
moment they have just done something wrong:

| Site | What it says |
|---|---|
| `scripts/release-preflight.ps1:57` | the classifier's header, explaining why the milestone arm reds |
| **`scripts/release-preflight.ps1:296`** | **the `legacy-milestone` RED reason string itself** — shipped text, read by a human at the worst possible moment |
| `docs/GITHUB-SETUP.md:450` | the `v8.0` REDS NOW box |

**A citation that 404s is worse than no citation:** it tells the reader there is an
explanation and then withholds it. That is the whole reason this file is Gate 3's first
deliverable rather than its last.

---

## The claim this addendum does NOT make

> ### **M8 IS COMPLETE. THE AUDIT'S 6/6 PASS STANDS AND IS NOT RE-OPENED.**
>
> Every fact in the audit was **re-verified live against the tree it audited**, on
> 2026-07-17. **A later phase changing that tree does not retroactively falsify an audit —
> it succeeds it.** This document is an **append**. Nothing in the audit is retrofitted,
> and the same rule that protects the ~50 dated records in `docs/plans` protects it:
> **a record edited to agree with today is not a record.**
>
> **What changed is the tree, not the audit's reading of it.**

---

## What Phase 8.6 reversed, and who owns the reversal

Phase 8.6 landed **hours** after the audit passed, and it reversed **two decisions the audit
had just passed**, plus a third the audit did not cover. **The owner made all three calls.**
They are recorded here as re-weightings by the owner — **not** as discoveries, and **not** as
the audit having been wrong. Recording them as anything else would be flattery.

| # | The audit passed | 8.6 decided | Whose call |
|---|---|---|---|
| 1 | **8.2 decision 3 — release-please REJECTED**, on a named three-part test ("all three must hold, not any") | **release-please is IN**, in `draft: true` mode | **The owner's.** The test passes **two of three** — precondition 2 ("releases become frequent enough that hand-writing the Release body is a chore") **does not hold**: **zero** Releases have ever existed, so the chore is hypothetical. **The owner waives it.** That waiver is an owner's valuation, not a fact the design discovered. |
| 2 | **8.2 decision 3 — no `CHANGELOG.md`**, with a named trigger: *"the second public release"* | **`CHANGELOG.md` arrives** | **A consequence, and named so it does not sneak in.** There have been **zero** public releases; the trigger never fired. `release-type: simple` writes the changelog with `createIfMissing: true`. |
| 3 | *(not covered — the audit predates it)* | **The milestone-tag namespace is retired**; `v<semver>` goes to release-please | **The owner's**, authorized explicitly. |

**8.2 and 8.6 do not disagree about a single fact.** 8.2 named the exact configuration 8.6
adopts — *"cut a **draft** Release that the owner publishes by hand — manual-go preserved —
but at that setting its remaining value is changelog + a bump PR"* — and judged that value
**insufficient**. The owner now judges it **sufficient**. **That is a re-weighting, and the
owner owns it.**

**The law the reversals did not touch:** nothing reaches nuget.org without the owner's click.
`release.yml` publishes on `release: types: [published]`; release-please cuts a **draft**;
GitHub does not fire `published` for a draft. **M8 DoD #3's law now holds structurally rather
than by promise.**

---

## THE FOURTH HONESTY ROW — DoD #6's `v8.0`

*(The audit's own honesty-row section handles this exact shape **three** times: DoD #2 named
five packages and **six** ship; DoD #3 says "a dry-run validation lane" and `dotnet nuget
push` has **no** `--dry-run`; DoD #5 says "~20 components", which is not a count of anything.
**This is the fourth, and it is numbered 4 because the audit's are numbered 1–3.** Phase 8.6's
design called it "the third" and counted only two existing rows — **that was a miscount, and
this house counts precisely, so it is corrected here rather than inherited.**)*

### 4. DoD #6 says **"final audit → tag `v8.0`"**. There will never be a `v8.0`.

**The tag was "pending". It is now CANCELLED — not deferred.** Phase 8.6 retired the
milestone-tag namespace entirely and gave `v<semver>` to release-please. No `v<N>.<M>` will
ever be cut again.

> ### **DoD #6's word named a RITUAL, not a RESULT.**
>
> The **result** DoD #6 actually asked for — *"every new surface CI-asserted (counts + gates
> with provenance); decision log per phase; final audit"* — **shipped in full, and the audit
> verified it, row by row, live.** The tag was the ceremony that had been marking the result,
> and **the ceremony is not the result.**
>
> **M8's completion rests on the audit. It never rested on the tag** — which is the honest
> reading, and it is also why cancelling the tag costs the milestone nothing.

***The DoD's word is wrong; the thing is unaffected by the word.*** That is the third time
this phrasing has been needed in M8's paperwork, which is itself worth noticing: **a DoD is
written before the work knows better, and this repo says so out loud rather than quietly
reconciling.**

---

## What in the audit now reads oddly — named, so no one has to wonder

**Both of these are left standing.** They are the audit's evidence, they were true when
measured, and this section is the pointer that explains them.

### 1. The audit's DoD #3 row records `-SelfTest` **8/8** with **`v8.0 → SKIP (milestone)`**

**True on 2026-07-17, measured live, against the classifier as 8.2 shipped it.**

**Phase 8.6 inverted that arm.** The self-test is now **9/9** and **`v8.0 → RED`**. 8.2's SKIP
rested on **one** stated premise — *"a milestone Release is a **legitimate** thing the owner
may do at M8's close; reddening a legitimate action trains the owner to ignore reds on release
runs"* — and **8.6 deleted the premise**: after the namespace is retired, a Release on `v8.0`
is not a legitimate action, it is a **mistake**. There is nothing left for "don't red a
legitimate action" to protect.

**The count moved 8 → 9 deliberately** (rows are the proof; the new row is the retired `pkg/`
namespace getting its own named RED rather than a silent one).

**The hazard's defence did not weaken — it grew.** 8.2 had two guards between `v8.0` and six
packages published at version "8.0"; **8.6 has four**, and the fourth is structural: the tag
never feeds the pack (`ReleaseWorkflowPinTests.TheReleaseWorkflow_NeverOverridesTheVersion`).
**Converting the arm from SKIP to RED removed no guard — it made the first one louder.**

### 2. `MILESTONE.md:344` and `ROADMAP.md:1153` record the command `git diff v7.0..HEAD`

**This is the sharpest one, and it is a recorded *command* rather than a recorded claim.**

It is the M8 audit's **ABI provenance**: the diff was **run on 2026-07-17**, over
`src/BlazorNative.Jni` / `src/BlazorNative.Apple`, and returned **zero** `.kt`/`.swift`/`.h`/`.mm`
touched — only two build files. **That result is a fact about 2026-07-17 and it does not
expire.**

**Once the seven tags are deleted, the command stops working** while the finding it produced
stays true. **It is not rewritten** (it is a dated record's evidence). **The commit it truly
rests on is not going anywhere:** the comparison was against **`6afd8af`'s parentage**, and
commits are not tags. A reader who needs to re-run it uses the commit, not the tag.

---

## The tag deletion — STATE, stated precisely, because everything above is written around it

> ### ✅ **THE SEVEN TAGS (`v1.0`…`v7.0`) WERE DELETED ON 2026-07-17.**
>
> ```
> $ git push origin --delete v1.0 v2.0 v3.0 v4.0 v5.0 v6.0 v7.0   # + the locals
> $ git ls-remote --tags origin
> $ git tag -l
> ```
>
> **Both are empty.** This repo now has **no tags at all** until release-please cuts the
> first `v1.0.0-preview.2`. The owner gave a fresh go at the moment it would happen —
> deleting seven published tags is destructive, so the authorization was re-confirmed
> rather than inherited from the morning's approval-in-principle.

**This matters to how the rest of the machinery is worded, and the distinction is the point:**

> ### **RETIRING A NAMESPACE AND EMPTYING IT ARE TWO DIFFERENT ACTS.**
>
> **The retirement is a DECISION, and it happened first.** The classifier reds on a tag's
> **shape**, so **`v8.0` red while all seven tags were live, exactly as it does now they are
> gone.** **Nothing in the shipped machinery was load-bearing on the deletion** — which is
> why Gate 3 could ship the classifier's RED, this addendum, and the live-doc sweep *before*
> it, and why **not one line of the classifier changed when the tags went.** That is the
> claim's proof: the machine did not notice.

**Gate 1 shipped four sites asserting the deletion in the present tense** (`release-preflight.ps1`
`:55`, `:152`, `:296`; `docs/GITHUB-SETUP.md:448`) — **plus two its own review did not name**
(`.github/workflows/release.yml:189`, and `release-preflight.ps1`'s **self-test row for `v1.0`**,
whose note read *"a deleted tag's shape"* — a tense claim **inside the table that is the proof**).
At the time they were written the tags were live. **Phase 8.6's design pre-decided this exact
case**, and Gate 3 followed it, returning all six to the subjunctive:

> *"If Gate 3 is descoped, deferred, or split — **the text must come back to the subjunctive in
> the same change that defers it.** Shipping 'were deleted' over seven live tags is the exact
> shape of falsehood this phase's Gate 1 review exists to catch."*

**The deletion has now been taken, so the same box swings back** — this time truthfully, and in
the same change that took it.

### The deletion's checklist — as taken, and it was WRONG BY ONE

| # | Site | What changed |
|---|---|---|
| 1 | `docs/planning/ROADMAP.md` — the retirement note at the top of `## Milestones` | the **STATE** paragraph: "all seven still exist" → **they are gone**, and the note now answers `git checkout v6.0` in the past tense |
| 2 | `docs/GITHUB-SETUP.md` — the `v8.0` REDS NOW box | "to be deleted (… all seven still exist)" → **deleted 2026-07-17** |
| 3 | `scripts/release-preflight.ps1:~55` | the header's "TO BE DELETED" parenthetical |
| 4 | `scripts/release-preflight.ps1:~161` | the arm comment's "authorized, not yet taken" |
| 5 | `scripts/release-preflight.ps1:~308` | the RED string's "are being deleted" |
| 6 | `.github/workflows/release.yml:~190` | the classify step's comment |
| **7** | ⚠ **`scripts/release-preflight.ps1:~537` — the self-test row's own note.** **THIS ROW WAS MISSING FROM THE CHECKLIST**, and it is the one site this document called *"the sharpest"* two paragraphs above | Gate 3's fix left *"this row is true whether or not `v1.0` still exists **(it does, as of 2026-07-17)**"* — **an existence claim, and the deletion falsifies it** |
| 8 | **this file** — the STATE box above | it becomes the record of a deletion that happened |

> **⚠ THE CHECKLIST DROPPED THE SITE ITS OWN AUTHOR CALLED THE SHARPEST.** Gate 3 swept
> **six** sites and then enumerated a **different six**: it added `ROADMAP.md` (which it had
> just written) and **silently dropped `:537`** (which it had just fixed). The prose above it
> said *"five"*; the checklist said *"six"*; the truth was **seven**. **Following the checklist
> literally would have left a false existence claim inside the self-test table that is the
> classifier's proof** — the exact shape Gate 3 named when it found it.
>
> **The lesson is this phase's own, arriving one more time:** a list of what to change is a
> **copy** of the thing it lists, and it rots the same way. **The sweep that found `:537` was
> a grep for the claim; the checklist that lost it was a hand-written roster** — 8.3's I-1
> and 8.1's normative rule 2, in a document that cites both. **Caught at the close only
> because the deletion was swept by re-grepping rather than by reading this table.**

**Nothing else moved**, and that much was by design: the live-doc sweep had already removed every
claim that a milestone tag **exists**, so no document had to be re-swept when they went. **No
pre-existing `docs/plans` record was touched** — this file is 8.6's own.

---

## What a future reader should take from this

**The one place a reader learns why `git checkout v6.0` fails is
[`docs/planning/ROADMAP.md`](../planning/ROADMAP.md)**, in the note at the top of its milestone
history — the live doc a reader already consults to learn what the milestones *were*.
**`docs/GITHUB-SETUP.md`'s tag table is the second place**, because that is where someone
standing at the Release UI is looking.

**And the nuance that will make two readers disagree, both correctly:** an **existing clone or
fork keeps all seven tags forever** — `git fetch --prune` does **not** delete tags without
`--prune-tags`. So this breaks for **new** clones and silently **does not** for old ones.

> **The chapter record is `docs/planning/ROADMAP.md` and the milestone audits — it always was.**
> The tags were how the chapters were **marked**. The writing is what the chapters **were**.
> **M8 is complete because its audit says so, on evidence, row by row — and that sentence
> needs no tag to be true.**
