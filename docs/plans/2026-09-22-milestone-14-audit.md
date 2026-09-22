# Milestone 14 audit — Twin Divergence, Closed Mechanically

**Date:** 2026-09-22
**Auditor:** phase 14.5
**Milestone:** [M14](../planning/MILESTONE.md) · opened 2026-09-21, five phases, closed over two days
**Verdict:** **PASS WITH FINDINGS**

---

## Verdict in one paragraph

M14 set out to close the twin-divergence class mechanically — *wherever the framework holds one
truth in two places, either generate the second copy or pin the two against each other.* It
delivered both mechanisms, fixed all three live instances that motivated it, cleared both P3
device-run blockers, and built a device CI lane that had never existed. Every criterion is met
except one, and that one is met **narrowly rather than fully**: the semantic pin makes a new
divergence red for eight proven shapes and demonstrably fails to for four more. The milestone's own
DoD anticipated this outcome and instructed that it be *"recorded explicitly, never a line to
quietly drop"*. This audit records it.

---

## Criterion-by-criterion

### 1. All planned phases complete — **MET**

`docs/planning/ROADMAP.md`, verified on `origin/main` at `656cc76`:

| phase | status | PR |
|---|---|---|
| 14.0 pin the host-event vocabulary | complete | #341 |
| 14.1 the dispatch twins | complete | #347 + #349 |
| 14.2 safe-area insets to .NET | complete | #351 |
| 14.3 auth semantics | complete | #355 |
| 14.4 device observability, docs, the lane | complete | #361 |

All five carry a **design spec and a plan** in `docs/superpowers/` — checked, 1 of each per phase.
14.4 grew a sixth task mid-phase by owner decision, recorded in the plan rather than only in the
ledger.

### 2. All tests passing, and both device lanes dispatched — **MET**

| suite | count | evidence |
|---|---|---|
| .NET | **1111** | `ci.yml`'s gate asserts exactly 1111; `build-test` **passed on the close commit**. A local run confirmed Runtime's 945 of that total |
| JVM | **162** | gate asserts 162; `build-test` green |
| iOS XCTest | **271** | `ios` lane **success on `main`'s current head `656cc76`** |
| Android instrumented | **226** | `android-instrumented` success at `f67282e6`, 14.4's head |

**Both device lanes were dispatched for every phase that changed shell source**, and — the part the
DoD is actually about — their `headSha` was compared against the PR head rather than their
conclusion being read alone. That trap was caught live on 14.2, where a green Android lane predated
the very `MainActivity.kt` change it appeared to validate.

**One honest gap, stated rather than glossed:** `android-instrumented`'s most recent run is at
`f67282e6`, 14.4's branch head, not at `main`'s current `656cc76`. The intervening commit is the
14.4 close — `ROADMAP.md`, `MILESTONE.md` and a plan file only — which cannot reach Android. The
reasoning is stated here rather than the gap being ignored.

### 3. The vocabulary sub-shape is closed by generation — **MET**

Host-event names emit from `src/wire-vocabulary.json` into all three languages, and both shells get
a generated `BnHostEvent` enum, so **a bare literal will not compile**. The public `BnHostEvents` is
deliberately **hand-written and pinned, never generated** — the `BlazorNativeNodeType` precedent: a
generator able to rewrite public API can silently move a frozen surface.

**#300 closed.**

### 4. The semantic sub-shape is closed by a pin, and a NEW divergence reds — **MET NARROWLY. This is the milestone's central claim and the one finding.**

A mechanical form **exists and works**. Two pins ship: `src/dispatch-surface.json` +
`DispatchSurfaceDriftTests` (14.1) and `src/auth-semantics.json` + `AuthSemanticsDriftTests`
(14.3, hardened in 14.4). The auth pin's completeness half genuinely reds on an undeclared
authenticator — **proven by mutation, repeatedly, not asserted.**

**Eight shapes are closed, each reproduced before the fix and red after:** same-line prefix
co-occurrence · a token wrapped across lines · a token hidden behind a `//` inside a string literal
· an `ignored` entry excusing more than one occurrence · a widened test-only guard · a new caller in
the counted file · a duplicate `ignored` key · a matrix leg skipped so the aggregate still reads
success.

**Four shapes remain open, and all four were reproduced as live green mutations** — filed as
**#364**:

| # | shape | severity |
|---|---|---|
| F1 | `ShellSourceRoots` omits `src/BlazorNative.Jni/src/main/kotlin`, which `build.gradle.kts` puts in the **same `main` source set** — twelve shipped shell files, including `ShellBridge.kt` and `BlazorNativeRuntime.kt`, are never scanned | HIGH |
| F2 | the caller count is scoped to one file, while `provisionKey` is `internal` and reachable module-wide | HIGH |
| F3 | `StripComments` over-strips: a raw-string body line carrying `/*` opens a block comment running arbitrarily far — the opposite of its own "confined to a single line" claim | MEDIUM |
| F4 | an `import … as Auth` alias defeats every dotted vocabulary token, and configures the prompt for weak-biometry-or-credential under a green pin | MEDIUM |

**The pattern is the finding, not the four instances.** This is the **fifth review across two
phases** to defeat this pin with a shape the previous round had not tried:

> same-line ternary → wrapped token → string literal → new caller → source root, file scope,
> raw-string block comment, import alias

That is not a run of bad luck. **It says the thing to reason about is the pin's *coverage* — which
trees, which spellings, which file kinds — rather than its assertions.** Fixing four more instances
and awaiting a sixth review would repeat the method that produced this list. #364 says so
explicitly.

**Verdict on this criterion:** the DoD asked for a mechanical form and said that if none existed,
that must be recorded as a finding. One exists, it works, and its coverage is incomplete in four
documented ways. **Met narrowly; recorded, not dropped.**

### 5. #339 fixed, and its invisibility fixed too — **MET**

The deadlock is gone. The divergence proved **structural before it was semantic**: Swift was
missing *both* of Kotlin's `AndWait` methods, so one method did two jobs, which is how the lifecycle
caller landed on a blocking path.

The invisibility half is met by more than the DoD required: `FakeShellHost.AutoCompleteHostCall`
defaulted **true** and completed host calls inline, so no .NET dispatch test could ever produce an
incomplete handler Task. Setting it false reproduces #339 **in about ten seconds on Windows, with
no device.**

**#339 closed.**

### 6. #338 fixed on BOTH shells — **MET**

Insets ride the existing `blazornative_host_event` export; Android's identical gap was closed in the
same pass. `BnSafeArea` is **opt-in**, following every peer framework except SwiftUI, with the
ergonomics closed by putting it in every snippet a user would copy plus a guard that reds if a
sample loses its wrap.

The phase's real finding was a **boot race present on both shells**, and it needed two halves —
don't record an inset you could not deliver, *and* force one re-report once boot completes.

**#338 closed.**

### 7. The auth semantics agree and are pinned — **MET**

`requireAuth: true` means **biometry**, one answer on both shells. The decision looked like a
three-way choice and was not: **seven** sites answer the question and the split was **6-to-1**, with
only the Apple storage *read* disagreeing — and it was the one that weakened the guarantee.

The read-side contract was found **already covered** on both shells at the line level; what was
missing was the ability to exercise it **on hardware**, so a button shipped rather than a third
assertion of the same property.

**#213 item 1 closed.**

### 8. `Debug` and `Verbose` observable on a real device, method recorded — **MET**

Every route was closed by the OS — `log config` has no `--device` flag, macOS 26's `log stream`
lost device support, `devicectl --console` carries only fd 1 and 2 — and the last one,
`OS_ACTIVITY_DT_MODE`'s mirror to fd 2, **was closed by our own `dup2`**. `BnStderrPump` now stands
aside when the mirror is on, and the method is recorded at `website/docs/shells/ios.md` §8 with a
pointer from `logging.md`.

### 9. The four documentation landmines fixed, `$(AppIdentifierPrefix)` among them — **MET, and exceeded**

Fourteen targets rather than four. `$(AppIdentifierPrefix)` is done, with a `codesign -d
--entitlements` verification command. The highest-value one was not on the original list: the
handover **instructed a device tester to exercise a passcode fallback that phase 14.3 had deleted**.

Two targets were correctly **refused**: `ROADMAP:209`'s "eight-export C-ABI" is historically
accurate — the ABI genuinely was eight at M3 close — and was annotated rather than falsified; and
the 13-versus-10 export count is a grep artifact, since only 10 lines apply the attribute.

Residue is filed as **#365**, including one item worth an owner's eye: `GITHUB-SETUP.md` calls the
leg contexts *"not required"* at `:218` and *"already-required"* at `:316`, and that phrase was
copied into `ios.yml:16`. It is the sentence someone reads when deciding branch protection.

### 10. No ABI change — verified by diffing the export surface, not asserted — **MET**

Diffed `src/BlazorNative.Runtime/Exports.cs`'s `EntryPoint` set at `2437050` — the commit that
opened M14 — against `main`:

```
before: 10 exports | after: 10 exports | diff: IDENTICAL
```

The **wire vocabulary** was also compared by name set rather than by line count: **0 names removed,
15 added.** Purely additive, exactly as scoping decision 3 predicted.

### 11. Release tagging — **NOT APPLICABLE, correctly**

`docs/planning/CONVENTIONS.md` records **`Milestone completion tags a release: no`**, and
`Released by: release-please`. Per the Commit & Release Protocol this criterion is **skipped**, not
failed — the absence of a milestone tag is the correct state.

---

## Code-quality review — gap recorded

No `docs/pre-push-review-*.md` reports exist, so code quality was **not independently reviewed by
that mechanism** in this milestone.

**This is a warning, not a hard fail**, and in this case the substance was covered by a different
process: every one of the milestone's ~20 tasks received a spec-compliance and quality review from
a fresh agent, plus a whole-branch review per phase, with findings recorded in each phase's ledger
and material ones filed as issues. **Five of those reviews changed a design rather than polishing
one.** The gap is in the artifact, not the practice — but it is recorded so the owner decides.

---

## Open debt carried out of this milestone

Filed, not implied:

| issue | what |
|---|---|
| **#364** | the four reproduced pin holes, and the coverage-not-assertions argument |
| **#365** | residual overclaims, including the required-context self-contradiction |
| **#345** | `DispatchEventCore` blocks the lane when a handler goes async — **pinned by a test that asserts the bug still exists** |
| **#346** | Android predictive back deadlocks behind a held lane — measured and confirmed, same pinning approach |
| **#356** | the iOS status legend claims `Unavailable` means "not enrolled", which nothing maps; may be a real semantic divergence if the device answer goes one way |
| **#357** | 14.1's completeness pin still lacks the anti-vacuity assertion 14.3 and 14.4 both have |
| **#360** | **superseded by #364 and should be closed** — its four named holes were fixed in 14.4 |

Accepted deliberately, with reasons on record: the caller count covers one spelling; the device leg
is compile-and-link only, so real-device *execution* remains #17's external dependency;
`IosSliceMatrixDriftTests` is a load-bearing cross-file dependency; and three documentation
transcription pairs are unpinned — **which is the twin-divergence class in prose, inside the
milestone about twin divergence.**

---

## What this milestone actually taught

Three lessons earned by being wrong first, worth more than the fixes:

**A mutation set needs coverage of the pin's own code paths, not just plausible subject
behaviours** — and the suppression path most of all, since that is where a pin is *designed* to go
quiet and can go quiet by accident. Six mutations missed a real hole because every one placed its
token alone on its own line and never entered the branch where the bug lived.

**A differential pin has a common-mode failure.** A roster compared against a second copy passes
when *both* copies lose the same entry. A floor has to be absolute. This matters because M14 is
built on differential pins.

**Swapping one unpinned assertion for its inverse is the same bug.** When Apple's docs would not
settle whether `secureSet` fails early without an enrolment, the honest move was to mark it NOT
ESTABLISHED and put a falsifiable prediction on the device checklist — not to write the likelier
answer into the source.

---

## Recommendation

**PASS WITH FINDINGS.** M14 delivered its mechanisms, closed its three live instances, cleared both
P3 blockers, and left the repo with a device slice built in CI for the first time. Its central
claim is true for eight proven shapes and false for four documented ones, and that is written down
rather than rounded up.

Ready for `complete-milestone`. Before starting M15, **#364 deserves a decision rather than a
queue position** — not "fix these four", but whether to write down what the scan is supposed to
cover and pin that instead.
