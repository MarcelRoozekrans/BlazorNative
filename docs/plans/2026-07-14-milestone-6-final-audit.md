# Milestone 6 — Final Audit (Phase 6.4)

**Date:** 2026-07-14
**Auditor:** Phase 6.4, against `main` @ `b5581c9`
**Predecessor:** [M5 final audit](2026-07-13-milestone-5-final-audit.md) (PASS, tagged `v5.0`)
**Contract audited:** [`docs/planning/MILESTONE.md`](../planning/MILESTONE.md) — the eight M6 DoD criteria

## Method

Evidence-based, per the house rule: **"asserted, not observed"**, and **"a mechanism nobody
tested is a mechanism nobody knows."** Applied to the audit itself — **no criterion is accepted
because a conclusion doc says so.** Every verdict below is checked against git history, the live
GitHub API, actual CI run logs, and the code. Where a guard's own effectiveness was in question,
it was **mutation-tested** (see DoD #7).

**Verdict: PASS WITH ONE PARTIAL.** Seven of eight criteria PASS. **DoD #7 is PARTIAL** — the
counts are honest, the wiring is only half-fixed.

---

## Verdict table

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Yoga-integration spike verdict committed | **PASS** | [Spike conclusion](2026-07-13-phase-6.0-spike-conclusion.md) records **"Verdict — GREEN (both rungs)"**. Yoga **3.2.1** pinned in `src/BlazorNative.Jni/build.gradle.kts:57` *and* `.github/workflows/ios.yml` `YOGA_VERSION: 3.2.1`. The parity is enforced **in the required lane**: `ci.yml`'s "Assert Yoga version parity" step regex-parses both files and throws on drift. Run **29365559347** prints `Yoga pins: android(build.gradle.kts)=3.2.1  ios(ios.yml)=3.2.1`. |
| 2 | Flexbox layout on both platforms | **PASS** *(one drift-protection gap — see Findings F2)* | The **same frame table, number for number**, on both shells — all 16 nodes verified line-by-line: row section `(0,0,300,100)`, box A `(0,0,50,100)`, box B Grow=1 `(50,0,200,100)`, box C `(250,0,50,100)`, column section `(0,100,300,200)`, items at y=0/80/160, wrap section `(0,300,300,100)`, wrap 0-2 at x=0/90/180, wrap 3 at `(0,40)` (line 2), text row w=150, back row w=300, root hugging in height. Same 0.5 tolerance both sides. `BnLayoutDemoAndroidTest.kt` / `BnLayoutDemoTests.swift`. **No ABI change**: flex props ride `SetStyle`; still 9 exports × 3 RIDs. |
| 3 | Native measurement | **PASS** | Attached **by NodeType**, literally — `YogaLayout.kt:1110` `MEASURED_NODE_TYPES = setOf("text","button","input","image")`, gate at `:368`; `BnWidgetMapper.swift:205` `measuredNodeTypes: Set<String> = ["text","button","input","image"]`, gate at `:971`. **Neither shell consults `childCount` / `subviews.isEmpty` anywhere** (every `setMeasureFunc` site grepped; the only others are the *clears* on purge). The **independent oracle** is real on both (`FrameAssertions.kt:89` / `BnFrameAssertions.swift:193`): it builds a throwaway widget of the same class with the same text/font, asks it the same question, and demands the laid-out frame match within 1px/1pt. **A constant-size measure func fails it** — every *other* assertion in the demos (`height > 0`, "the row hugs the label", `lineCount > 1`) survives a constant stub, which is exactly why the oracle exists. |
| 4 | Real scrolling | **PASS** | Same numbers, and **derived rather than transcribed**: both compute `CONTENT_H = ROWS * ROW_H` = **800** over a **200** viewport, `SCROLL_RANGE = 600`, rows at `y = 80*i`. **It actually scrolls, on both.** Android drives `scrollTo(0, 10_000dp)` and observes the framework clamp it to exactly **600** *and* row 9 travel `-600` (via `getLocationOnScreen` — the framework's own accounting, not test arithmetic). iOS drives `setContentOffset(y: maxOffset)` and asserts `row9After - row9Before == -scrollRange` (via `view.convert`, which folds in `contentOffset`). Both then pin the visible window (row 7 half-clipped at ∓40, rows 0–6 off-screen). The **synthetic content node is asserted never to reach the wire** (`BnScrollDemoTests.cs:275` — the scroll node's wire children are *exactly* the ten rows), while both device tests see `scroll.childCount == 1`. |
| 5 | URL images | **PASS** | `BnImageDemo` on both shells. **The reflow is proven by a sibling band, before and after**: band I asserted at `y = 0` while the fixture server *holds* every response (and `imageResults == emptyList` / `imageTerminalCount == 0` proves nothing has landed), then at `y = Hi` once the bytes arrive, with the section grown by exactly `Hi`. The band is a 20dp `BnView` with both axes explicit, so it cannot move for its own reasons. **The unit rule (one file pixel = one dp/pt) is enforced**: Android reads `bitmap.width` with **no `intrinsicWidth` fallback** (`WidgetMapper.kt:1065`, Coil `.size(Size.ORIGINAL)`); iOS reads `cgImage.width` (`BnImageLoader.swift:212`) and — load-bearing — **asserts `scale == 1` on the image Kingfisher handed the shell** (`BnImageTests.swift:178`), not merely on the fixture the test decoded. The three fixture-constant transcriptions (.NET / Kotlin / Swift) are pinned to one number by two regex drift tests **in the required lane** (`BnImageDemoTests.cs:233`, `:258`). |
| 6 | Flex container components | **PASS** | `BnView` carries the typed flex surface (24 `[Parameter]`s — `FlexDirection? Direction`, `FlexJustify? Justify`, `FlexAlign? Align`/`AlignSelf`, `FlexWrap? Wrap`, `FlexPosition? Position`, `Gap`, …). `BnRow.cs` / `BnColumn.cs` ship as thin presets, pinned by **both** a reflective *declaration* test and a *forwarding* test (the latter is the one that bites — 6.1's Gate 1 review proved a reflective test alone stayed green through two deleted forwarding lines). **`BnStack` is absent** and the **deviation is consciously recorded** in [MILESTONE.md](../planning/MILESTONE.md) DoD #6, [6.1 design](2026-07-13-phase-6.1-design.md):30 and [6.1 conclusion](2026-07-13-phase-6.1-conclusion.md):161. |
| 7 | **Every new surface is CI-asserted** | **PARTIAL** | **The four counts are real, asserted, and green** (run IDs below). **But only 2 of the 4 are behind a required check.** Live API: `required_status_checks.contexts == ["build-test"]` — *that is the entire list*. See Findings F1. |
| 8 | Decision log committed | **PASS** | 6.0 [design](2026-07-13-phase-6.0-design.md) + [spike conclusion](2026-07-13-phase-6.0-spike-conclusion.md); 6.1/6.2/6.3 each with design + implementation-plan + conclusion (11 docs in `docs/plans/`); `ROADMAP.md` phase table current; **plus this audit**. |

---

## The four CI-asserted counts — with the runs that prove them

| Surface | Baseline | Asserted in | Proven green by | Quoted totals line |
|---|---|---|---|---|
| .NET (xUnit, 3 projects) | **319** passed / 0 skipped | `ci.yml` → **build-test (required)** | run **29365559347** | `dotnet test totals across 3 projects: passed=319 skipped=0 failed=0` |
| JVM (Gradle `testDebugUnitTest`) | **83** passed / 0 failed | `ci.yml` → **build-test (required)** | run **29365559347** | `JVM test totals across 16 suites: tests=83 failures=0 errors=0 skipped=0` |
| Android instrumented (AVD) | **111** passed / 0 failed | `android-instrumented.yml` (nightly/dispatch — *not required*) | run **29365382226** (on `main` @ `d6797de`) | `Starting 111 tests…` → `Finished 111 tests`, `(0 skipped) (0 failed)` |
| iOS XCTest (simulator) | **72** passed / 0 failed | `ios.yml` (PR path-filtered — *not required*) | run **29363525930** | `XCTest cases: passed=72 failed=0` |

**A note on which tree proved what.** Run **29365559347** is the load-bearing one: it is the
*first and only* green run that asserts **319 + 83 + `compileDebugAndroidTestKotlin` together**.
Neither PR could do it alone — #80 (Phase 6.3) predates #81's compile step, and **#81's own
gating run asserted 304 / 79**, i.e. the *pre-6.3* baselines, because #81 branched from a `main`
that did not yet contain 6.3 and merged 7 minutes after it. Both `push`-on-`main` runs for
`041d056` and `d6797de` were **cancelled** (superseded by the next push under
`cancel-in-progress`). 29365559347 tested PR #82 merged into `main@d6797de` — and that tree
*is* today's `main` (`b5581c9`). The `push` run on `b5581c9` (29366602846) was still in flight
at audit time; its PR twin on the identical tree was green.

### The ABI did not move — verified independently

- **9 exports, 3 RIDs, one run (29365559347):** `Exports found (9)` (win-x64, dumpbin via
  vswhere), `Dynamic symbols found (9)` (linux-bionic-x64 **and** linux-bionic-arm64,
  llvm-readelf from the pinned NDK 26.3.11579264). iOS: `SYMBOLS: all 9 blazornative_* present.`
  (`nm -gU` on the static archive, run 29363525930).
- **4 accepted IL2072s** on every one of the four publishes (3 in `ci.yml` + 1 in `ios.yml`).
- **The 72-byte / 9-callback bridge struct — all three mirrors exist:**
  `BridgeProtocolNativeTests.cs:24` (`Assert.Equal(72, Marshal.SizeOf<BlazorNativeBridgeCallbacks>())`),
  `ShellBridgeTest.kt` (`callbacks_struct_is_72_bytes`, JVM → **required lane**), and
  `BnDriftTests.swift:75` (`MemoryLayout<bn_bridge_callbacks>.size == 72`, iOS lane → *not*
  required).
- **Cross-shell drift tests run in the required lane.** `ShellStyleTableDriftTests.cs`
  (11 `[Fact]`s) lives in `Renderer.Tests` → `dotnet test` → **build-test**. It parses
  `YogaLayout.kt`, `BnYogaLayout.mm`, `BnYogaStyleParserTests.swift` and `BnWidgetMapper.swift`
  **out of the checkout as text** and pins set-equality, disjointness, the scroll ignore-lists,
  and Kotlin-vs-`.mm` equality. This is the single most important structural mitigation in the
  repo — see F1.

---

## Findings

### F1 — DoD #7: the counts were honest; the wiring is still only half-fixed *(PARTIAL)*

**The live branch protection on `main` is one check:**

```
required_status_checks.contexts = ["build-test"]     (strict: true, enforce_admins: false)
```

Mapping the four baselines onto that:

| Surface | Lane | Trigger | Gates a PR? |
|---|---|---|---|
| .NET 319 | `ci.yml` → `build-test` | push(main) + **every** PR | ✅ **required** |
| JVM 83 | `ci.yml` → `build-test` | push(main) + **every** PR | ✅ **required** |
| Android instrumented 111 | `android-instrumented.yml` | `workflow_dispatch` + nightly cron | ❌ **never runs on a PR** |
| iOS XCTest 72 | `ios.yml` | `workflow_dispatch` + PR **(path-filtered)** | ❌ runs, but **advisory** |

So the AGP 9 incident's lesson generalises: **a count can be asserted and still be structurally
unreachable.** `android-instrumented` asserted 111 tests while the *entire* Android shell
(`MainActivity`, `WidgetMapper`, `YogaLayout`) was not being compiled — and no required check
could see it, because that lane does not gate PRs. It landed green on `main` and sat there.

**What #81 fixed — and I verified the fix bites, rather than trusting it.**
#81 added `compileDebugAndroidTestKotlin` to the required lane. Applying the house rule to the
guard itself, I **mutation-tested it**: reverted the `kotlin.srcDirs("src/main/kotlin",
"src/androidMain/kotlin")` line in `build.gradle.kts` (reintroducing the exact AGP 9 bug) and ran
the step locally under JDK 21:

```
e: …/BnDemoAndroidTest.kt:70:51 Unresolved reference 'MainActivity'.
… 513 "Unresolved reference" errors — BUILD FAILED
```

**The guard is real.** It would have failed the AGP migration PR on the spot. The Android shell
is now **compiled** by a required check on every PR. (Tree restored; `git status` clean.)

**Is iOS in the same hole? Yes — and structurally deeper.**

- **No required check compiles a single line of Swift or ObjC++.** `build-test` runs on
  `windows-latest`; there is no Apple toolchain in it. The iOS shell's *only* compilation is
  `ios.yml`, which is **not a required check**.
- **`ios.yml` has no `push` trigger at all** (`workflow_dispatch` + `pull_request` only) — so it
  **never runs on `main`**. A regression that lands can sit undetected exactly as the AGP bug did.
- **`ios.yml` is path-filtered** (`BlazorNative.Apple`, `Runtime`, `Components`, `Renderer`,
  `Core`, `ios.yml`, `global.json`). A PR touching only `src/BlazorNative.Jni/**`, `tests/**`,
  `scripts/**` or docs that breaks the Swift shell **would not even run the lane**.
- Even when it *does* run and goes red, it **does not block merge**.

**The one thing that saves it:** the required lane reads the iOS shell's **source as text**.
`ShellStyleTableDriftTests` parses `BnYogaLayout.mm` / `BnWidgetMapper.swift` /
`BnYogaStyleParserTests.swift`, and `BnImageDemoTests` parses `BnImageFixtureServer.swift`. So
**style-table and fixture-constant drift on iOS is caught by a required check** — but a *compile*
error, a broken `.swift`, or a behavioural regression is not.

**Live demonstration, during this audit.** While I was auditing, Renovate merged **PR #82 —
Kingfisher v8**, a *major* version bump of the iOS image library that Phase 6.3 depends on. It
happened to be safe: it touched `src/BlazorNative.Apple/**`, so `ios` ran and was green before the
merge. But **nothing required that.** The same bump, arriving via a path the filter does not
match, merges on `build-test` alone — and `build-test` cannot see Kingfisher at all.

**Verdict on #7: PARTIAL.** *The counts were honest; the wiring wasn't.* #81 closed the **Android
half** (mutation-verified). The **iOS half is still open**: the Swift/ObjC++ shell can stop
compiling, or regress behaviourally, without any required check noticing.

### F2 — A surviving "claimed but not pinned": the two frame tables are transcribed, not pinned

DoD #2 is **met** — I compared every number and they match. But *how* they are kept matching is
the gap:

- The frame numbers are **hand-transcribed literals** in `BnLayoutDemoAndroidTest.kt` and
  `BnLayoutDemoTests.swift`. There is no shared constant, no generated table, no cross-suite
  diff test.
- The ".NET golden" they are said to derive from (`BnLayoutDemoTests.cs`) asserts **style patches
  only** — `("width","300")`, `("flexGrow","1")`, create-counts. It never computes a frame,
  because **.NET does not run Yoga**. There is no machine-checked golden *frame* table anywhere.
- Both files' comments claim the other platform "asserts THE SAME NUMBERS … line for line."
  **Nothing enforces that.** Today they agree — I checked — but the agreement is a property of
  careful transcription, not an invariant.

This is precisely the class of contract this repo pins everywhere else (the style tables, the
fixture constants, the 72-byte struct, the Yoga version). It is the one that got away. Note the
same shape recurs in the image tests: **cross-shell image parity rests entirely on the two .NET
regex drift tests** — each device suite asserts only against *its own* decoded fixture.

### F3 — Smaller residuals (all real, none blocking)

- **No gesture-driven scroll** on either platform. `scrollTo` / `setContentOffset` are
  programmatic; there is no fling, no velocity, no `onScroll` event to .NET. "Actually scrolls"
  means *driven and observed to move* — which is what DoD #4 asks and what the tests prove.
- **Android has no direct pin on Coil's `Size.ORIGINAL`** (the mirror of iOS's `scale == 1`).
  Protection is indirect via the decoded-bitmap frame, and it only bites because the AVD density
  is 2.625 — on a density-1.0 device `intrinsicWidth == bitmap.width` and the rule would pass
  vacuously.
- **No iOS twin of `childless_view_boxes_keep_their_yoga_widths`.** iOS gets the property only
  indirectly via `BnLayoutDemoTests`' box-B assertion. `YogaMeasureAndroidTest.kt`'s comment says
  "Gate 3 mirrors this file" — **it does not**; the assertions were redistributed across
  `BnYogaDirtyTests` / the demo test. The oracle *is* present on iOS, so DoD #3's substance holds.
- **`enforce_admins: false`** — protection is bypassable by an admin.

The conclusions were otherwise unusually clean on "comments asserting a guarantee no test
checks." The M6 phases hunted this pattern well: the codebase now *documents* two cases where a
comment-only claim was converted into a real assertion (`BnImageTests.swift:178`'s `scale == 1`;
`BnScrollDemoTests`' order-dependent cache-hit bug).

---

## What M6 actually delivered

A **real flexbox layout engine on two platforms from one runtime**, and the two stubbed leaf types
filled in — **with no C-ABI change.** Concretely:

- **Yoga 3.2.1 in both shells** (Android: prebuilt Maven artifact; iOS: built from source for the
  simulator), version-pinned across both by a required-lane drift test.
- **Flex props ride the existing `SetStyle` wire.** The ABI is untouched: still **9 exports** and
  the **72-byte / 9-callback** bridge struct, asserted on 4 RIDs. The renderer stayed thin; the
  layout computation is entirely shell-local.
- **Both shells compute identical frames from an identical tree** — a 16-node frame table asserted
  number-for-number on the AVD and the iOS simulator.
- **Native measurement** through Yoga's measure callback, attached **by NodeType**, pinned by an
  **independent oracle** that a constant-size measure func cannot pass.
- **Real scrolling** — a viewport over a *synthetic content node* (never on the wire) whose
  Yoga-computed height *is* the content size: 800 over a 200 viewport, and the rows demonstrably
  move.
- **URL images** — async load (Coil / Kingfisher), measured by Yoga, with the reflow proven by a
  sibling band and the unit rule (**one file pixel = one dp/pt**) enforced on both shells.
- **The component surface**: `BnView`'s typed flex params, `BnRow`/`BnColumn`, and five demo
  pages. **No `BnStack`** — consciously.
- **Test surface grew** to **319 .NET / 83 JVM / 111 Android instrumented / 72 iOS XCTest**.

Along the way M6 found and killed **five false causal stories** and several
"comment-asserts-a-guarantee-no-test-checks" defects — and, in #81, one *structurally unreachable*
assertion.

---

## The ledger carried into M7

| Item | Recorded in | Why deferred |
|---|---|---|
| `Placeholder` / `OnError` / `ContentMode` (the API) | 6.3 conclusion | Each changes **measurement** → each needs its own design. (The `contentMode` **default** was *not* deferred — both shells are aligned to aspect-fit.) |
| **Density-aware assets** (`@2x`/`@3x`, `srcset`) | 6.3 conclusion | The unit rule's natural successor. |
| **Horizontal scroll** | 6.2 conclusion | Android's `ScrollView` is vertical-only; horizontal is a *different widget class*, which would have to be chosen at `CreateNode` from a `flexDirection` arriving in a *later* `SetStyle`. An ordering problem for an axis nothing needs yet. |
| **`onScroll` / `scrollTo` / offset-restore** | 6.2 conclusion | `onScroll` fires at 60Hz — the first high-frequency producer on a wire designed for taps. Needs coalescing/throttling design. Its real customer is **M7's virtualized list**, which should own all three. |
| **`picker` is not flex-ed** | 6.1 / 6.2 / 6.3 conclusions | The last framework `ViewGroup` that runs its own layout over its children. |
| **`.razor` compilation** | MILESTONE.md out-of-scope | M7. |
| **Typed props** (stringly `FontSize` / `Padding` — `[Parameter] public string? Padding`) | M4 ledger, carried | M7 (component library). |
| `ContentPadding` / `ContentGap` | 6.2 conclusion | If composing a `BnColumn` inside proves insufficient. |
| Definite-height diagnostic false negative (`Height="100%"` vs indefinite parent) | 6.2 conclusion | Yoga does not expose whether a percent actually resolved. |
| Kotlin's dispatch pin is weaker than iOS's (source-level vs runtime `rc == 1`) | 6.1 conclusion | — |

Plus the two this audit adds (see Recommendation):

| Item | Source |
|---|---|
| **The iOS shell is not compiled by any required check** | F1 |
| **The two frame tables' parity is transcribed, not pinned** | F2 |

---

## Recommendation

### Is M6 ready to tag `v6.0`?

**Yes — the capability is done and honestly evidenced.** All six *capability* criteria (#1–#6)
PASS on real evidence, the ABI is provably unmoved, and the decision log (#8) is complete. Seven
of eight PASS.

**DoD #7 is PARTIAL, and I do not think that blocks the tag — but it must be recorded, not
papered over.** The criterion says *"every new surface is CI-asserted."* Read literally, it is
**met**: all four surfaces have asserted baselines and all four are green. What the AGP 9 incident
proved is that the criterion, *as worded*, was never sufficient — an assertion can be green and
structurally unreachable. M6 is the milestone that **discovered** that, and #81 fixed half of it.
Shipping `v6.0` with the other half **named in the ledger** is more honest than either pretending
#7 is clean or holding the tag hostage to a CI change that belongs in its own phase.

**Recommended: tag `v6.0`, with F1 and F2 carried into M7 as ledgered work.**

### What would fully close #7

1. **Put the iOS shell behind a required check.** The cheapest honest fix mirrors #81 exactly:
   a **compile-only** iOS job (no simulator, no XCTest → no sim flake) that builds the
   Swift/ObjC++ shell, made **required**. It closes the "an entire shell silently stops compiling"
   hole without importing the flake modes that keep `ios.yml` advisory.
2. **Add `push: branches: [main]`** to `ios.yml`, and **drop the path filter** (or widen it) — so
   iOS regressions on `main` are at least *visible*, and a PR that breaks the Swift shell from a
   non-Apple path still runs the lane.
3. **Promote `ios` and `android-instrumented` to required** once the stability baseline documented
   in `docs/GITHUB-SETUP.md` ("~10 consecutive green runs on main with no sim/emulator flake reds")
   is actually met. Both lanes are now mature enough that this should be measured rather than
   assumed.
4. **Pin the two frame tables to each other** (F2) — the same shape as `ShellStyleTableDriftTests`:
   parse the asserted frame literals out of `BnLayoutDemoAndroidTest.kt` and
   `BnLayoutDemoTests.swift` in the **required** lane and demand set-equality. This is the last
   hand-transcribed cross-shell contract in the repo that nothing checks.

None of these is a capability change; all four are CI/test wiring, and they are the natural
opening work of M7 — or a short Phase 6.5 if the tag should carry them.
