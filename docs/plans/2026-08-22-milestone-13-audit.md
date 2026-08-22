# Milestone 13 — Consumer Ergonomics: final audit

**Date:** 2026-08-22 · **Audited at:** `628fb00` (main)
**Milestone doc:** [`docs/planning/MILESTONE.md`](../planning/MILESTONE.md)
**Design:** [`docs/superpowers/specs/2026-08-19-milestone-13-design.md`](../superpowers/specs/2026-08-19-milestone-13-design.md)

**Verdict: PASS WITH FINDINGS.** Twelve of thirteen definition-of-done criteria are met on live
evidence. One is **partially met** and is a real miss, not a technicality: the milestone's headline
breaking change is absent from the release notes of the version that shipped it. Three findings in
total, all with concrete remedies; none requires code.

---

## 1. Definition of done

Every row was checked against a command run today or a file read today, not against a phase
conclusion's own claim about itself.

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | All planned phases complete | ✅ | ROADMAP 13.0–13.5 all `[status: complete]`; 13.6 is this audit |
| 2 | All tests passing, **both device lanes dispatched** | ✅ | See §2 |
| 3 | Item surface declared exactly once | ✅ | `LayoutSurfacePinTests.NoComponent_RedeclaresAnInheritedLayoutParameter` + `BnLayoutItem_DeclaresExactlyTheItemSurface` |
| 4 | Every component derives from `BnLayoutItem`, allowlist entries carry written reasons | ✅ | `EveryComponentInThePackage_DerivesFromBnLayoutItem`; `AllowedNonLayoutComponents_GrowingItIsADeliberateAct` asserts each reason is non-blank **and ≥40 chars**, so a placeholder reads as one |
| 5 | Every layout length typed; `Width="12px"` is a compile error | ✅ | `BnLengthTests.NoConversionFromString_Exists` — the absence *is* the mechanism |
| 6 | Frame tables byte-identical on both shells | ✅ | 13.0 conclusion §3: `BnDemoFrameTables.swift`, `BnLayoutDemoAndroidTest.kt` and `ShellFrameTableDriftTests` unedited across the phase |
| 7 | The wire is unchanged — verified, not asserted | ✅ | 13.0 conclusion §3: no Kotlin, no Swift, no `wire-vocabulary.json` change; style multiset pinned **before** any component moved, so it captures truth rather than the refactor's own output |
| 8 | Baselines re-shipped **with a migration note carried in the release notes** | ⚠️ **PARTIAL** | Baselines ✅, notes exist ✅ (`website/docs/migrating/typed-lengths.md`, `testing-harness.md`) — **but the release notes do not carry them.** See finding F1 |
| 9 | `CheckAccess()` resolved on measured evidence | ✅ | 13.2 conclusion: honest `CheckAccess()` **killed the process** with a stack overflow, 3 runs of 3, with the recursion cycle recorded. Detection shipped instead |
| 10 | #21 retitled, #22 closed, Styling/State deleted from `BACKLOG.md`, WASM-era APNs / App-Store rows re-worded | ✅ | #21 open and retitled; #22 `CLOSED/COMPLETED`; both backlog sections carry `RETIRED 2026-08-21` headers and struck-through entries; APNs (`:262`) and App Store (`:270`) both carry dated re-wording notes |
| 11 | Theme decision recorded, either way | ✅ | `docs/superpowers/specs/2026-08-22-phase-13.5-theme-design.md` — split verdict, PR #301 |
| 12 | Twin-divergence class closed **mechanically** | ✅ | `GeneratedSymbolShadowTests`, `DeepLinkVectorTests`, `BnDeepLinkVectors.g.cs`, `DeepLinkSeedDriftTests`, `NodeTypeCodomainDriftTests`, `TextCollapseParityDriftTests`, `WireVocabularyCodegenTests` — all present; 13.4 records a mutation proof per guard |
| 13 | Harness cannot pass what the device rejects (#280); no leak on `StrictErrors` (#281) | ✅ | `BnTestHostTests.AThrowingMount_DisposesTheProvider_RatherThanLeakingIt`; node-type map equality via `NodeTypeCodomainDriftTests`. Note the mechanism differs from the one originally specified — see §3 |

**Not applicable:** "release tagged in git". `CONVENTIONS.md` records `Milestone completion tags a
release: no` and `Released by: release-please`. Per the orchestration skill, this criterion is
**skipped**, not failed — the absence of a milestone tag is the correct state.

## 2. Test evidence

| Lane | Result | Where |
|---|---|---|
| .NET | **1070 passed / 0 skipped / 0 failed** | Required `ci.yml` → `build-test`, **success at `628fb00` = current HEAD**. The lane does not merely run the suite, it *asserts the exact triple* (`ci.yml:2036`) and throws on drift — so a green run is a stronger count check than reading a local summary. Corroborated by a local `dotnet test -c Release`, exit 0 |
| JVM | **161 passed, 0 failures** — matches the README pin exactly | `gradlew testDebugUnitTest`, exit 0, counted from `build/test-results/testDebugUnitTest/*.xml` |
| Android (instrumented) | **success** at `8fa10cf` | `android-instrumented.yml`, 2026-08-22T03:40Z |
| iOS (XCTest) | **success** at `628fb00` = current HEAD | `ios.yml`, 2026-08-22T13:01Z |

Both device lanes were dispatched and are green — the criterion that exists because a green
*required* set does not mean the advisory lanes ran (11.4's pump bug). The Android lane ran one
commit behind HEAD; the only intervening commit is `628fb00`, which is **docs-only**, so the result
transfers. Recorded rather than glossed.

## 3. Findings

### F1 — The milestone's headline breaking change is missing from the release notes

**This is the one criterion not fully met, and it is the most consumer-visible defect the audit
found.**

`cf8e956` is `feat(components)!: type the layout lengths (#289)` — the `!` marks it breaking, and it
is the change that makes `Width="12px"` stop compiling for every existing consumer. It merged after
`v0.11.0` and before `v0.12.0`:

```
$ git merge-base --is-ancestor cf8e956 v0.11.0   → NO (it belongs to 0.12.0)
$ grep -n "289\|BREAKING" CHANGELOG.md            → no matches, anywhere in the file
```

The 0.12.0 notes list only #292 and #299. **There is no `⚠ BREAKING CHANGES` section in the entire
changelog**, and typed lengths appear nowhere in it.

The omission happened **at generation time, not at merge**: release-please's own PR #293 body already
lacked the entry, so nobody deleted it.

**Cause not established.** Two hypotheses were tested and one was disproven:

- *Nested conventional-commit bullets in the squashed body confuse the parser* — **disproven.**
  `cf2a4e7` and `f7aa1b7` carry 21 such bullets each and both appear in the changelog.
- *The `!` breaking marker is implicated* — **not proven, but it is the only structural difference
  found.** Of the four commits compared, the one marked breaking is the one that vanished.

`release-please-config.json` has no `changelog-sections` override, so the defaults are in force and
nothing in the config explains it.

**Why this matters beyond one missing line:** if a `!` commit can be dropped silently, the release
notes cannot be trusted to announce breaks — and this project's whole pre-1.0 strategy is "break now
and tell people". Worth its own issue, because the next breaking change will hit the same path.

**Remedy:** add the entry to `CHANGELOG.md` under 0.12.0 with a link to
`website/docs/migrating/typed-lengths.md`, and open an issue to establish the cause before the next
breaking change ships.

### F2 — Four delivered issues are still open

PR #299 landed the guards for all of them, but **its body carried no closing keywords**, so only
#279 closed (manually):

| Issue | State | Delivered by |
|---|---|---|
| #278 deep-link parsers diverge | **OPEN** | `DeepLinkVectorTests` + `BnDeepLinkVectors.g.cs` |
| #280 harness bypasses the C-ABI encode | **OPEN** | `NodeTypeCodomainDriftTests` (re-scoped — see below) |
| #281 `BnTestHost.Mount` leaks on throw | **OPEN** | `AThrowingMount_DisposesTheProvider_RatherThanLeakingIt` |
| #282 iOS cold deep-link doesn't seed the route | **OPEN** | `DeepLinkSeedDriftTests` |
| #283 grouped low-severity remainder | OPEN — **correctly**, 13.4 deferred 6 of 7 items by design | — |

This is bookkeeping, not code: the work shipped and the pins exist. But it is the exact pattern that
bit this repo on 2026-07-25, when four done-but-open issues had to be closed retroactively. The
tracker currently says the twin-divergence class is open while the code says it is closed.

**Note on #280:** it closes by a *different mechanism* than the one originally specified. 13.4
measured its stated bug as unreachable — `MapElementToNodeType` ends in `_ => "view"`, coercing
unknown elements upstream of both harness and encoder — and replaced the encoder-gate with a
node-type map equality pin, which catches a dropped mapping arm that the briefed gate never could.
Any closing comment should say so rather than implying the original scenario was fixed.

**Remedy:** close #278/#280/#281/#282 with evidence comments naming the pin that closes each.

### F3 — No independent code-quality review on file for this milestone

`docs/pre-push-review-*.md` — **none found**. Per the orchestration skill this is a **warning, not a
hard fail**: M13 used per-phase task review and a whole-branch final review inside
subagent-driven-development, plus a per-phase code review, which is a different process rather than
no process.

Recorded so the gap is visible and the owner decides. It is not counted toward the verdict.

## 4. Minor observations (not gaps)

- `docs/BACKLOG.md:231` still describes Android FCM delivering `NativeEvent("push", payload)`
  **"into WASM"** — WASM-era language in a row the DoD did not name. #284 supersedes the row's
  substance; the wording is stale but harmless.
- `#291` (audit every website doc page and leave a mechanical guard) remains unscheduled. It was
  raised during 13.1 precisely because opportunistic one-at-a-time doc fixes keep recurring —
  #298 is another instance filed during 13.4.

## 5. Recommendation

Close the three findings' remedies first — they are minutes of work, none touching code — then run
`complete-milestone`:

1. Add the typed-lengths breaking entry to `CHANGELOG.md` 0.12.0 and file the release-please issue (F1).
2. Close #278/#280/#281/#282 with evidence comments (F2).
3. Record F3 as accepted debt, or schedule a `pre-push-review` pass.

M13 delivered six phases, and two of them overturned their own premise on measurement rather than
shipping the plan as written — 13.2 (an honest `CheckAccess()` kills the process) and 13.4 (#280's
stated bug is unreachable). Both are recorded with the measurement, which is the milestone's most
reusable output.

## 6. Audit trail

| Check | Command |
|---|---|
| .NET suite | `dotnet test BlazorNative.sln -c Release --nologo` |
| JVM suite | `gradlew testDebugUnitTest` (JDK 21), counts summed from the JUnit XML |
| Device lanes | `gh run list --workflow=android-instrumented.yml / ios.yml`, SHAs compared to HEAD |
| Issue states | `gh issue view` per issue; `gh pr view 299 --json body` for closing keywords |
| Changelog | `git merge-base --is-ancestor`, `git log v0.11.0..v0.12.0`, `grep` over `CHANGELOG.md`, `gh pr view 293` |
| Pins | `grep` over `tests/BlazorNative.Runtime.Tests/` for each named guard |
| Conventions | `docs/planning/CONVENTIONS.md:48-49` |
