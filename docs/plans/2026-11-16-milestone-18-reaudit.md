# Milestone 18 re-audit — Acknowledged Bugs

**Date:** 2026-11-16
**Phase:** 18.13, [design](../superpowers/specs/2026-11-16-phase-18.13-design.md), [plan](../superpowers/plans/2026-11-16-phase-18.13-reaudit.md), [record](2026-11-16-phase-18.13-record.md)
**Audited:** `origin/main` at **`7d4253a8`**, which is #723, at 2026-11-16T20:25:31+01:00. Its code is the code of 18.12's squash `ed9e1e23` and the 0.22.1 release before it.
- **The range** is `09332c5e..HEAD`, where `09332c5e` is #713, the 18.11 squash. The M18 range as a whole is `5af84d3d..7d4253a8`, 58 commits.
- The re-audit branch `docs/18.13-reaudit` differs from `7d4253a8` only under `docs/`.
- **The graded lanes** are the PR #724 runs on `93c7acac`, recorded in [the 18.13 record](2026-11-16-phase-18.13-record.md) §5. This report is committed after them, on a later docs-only head; that head's lanes are recorded in the PR body and STATE.md, not here.

**Host for local runs:** Debian 13 x64, SDKs 10.0.403 and 11.0.100, `pwsh` installed as a dotnet tool.
**Red standard:** RS1, from [the 17.7 record](2026-10-17-phase-17.7-record.md) §4 "Rulings".
**Site unit for item 13:** the **written** rule, `docs/pin-standard.md` "Grading a pin: sites and reds", `:679` on HEAD. Nothing broader is graded here; that is #562.
**Precedent:** [the 18.11 audit](2026-11-15-milestone-18-audit.md), verdict FAIL on item 3, which this report re-grades and mirrors in shape.
- Its grades are a list of things to re-check, not evidence. Every number below was re-measured on the audited code, with the command shown beside it.
- **Item 3 is graded against the text as amended by the owner on 2026-11-16,** in `docs/planning/MILESTONE.md`. Its best possible grade is MET NARROWLY, because the title's "fixed at their cause" still does not hold literally for #407.

**Verdict:** **PASS WITH FINDINGS**: 11 MET, 2 MET NARROWLY, 0 NOT MET. See [the verdict](#verdict).
- Item 3, the flakes, is MET NARROWLY under the amendment: #407's cause stays unproven after 18.12 and #718 is open; #453 holds through #580, accepted and not fixed; one clean sample per lane; and the capture's sampler has no sampler-free control.
- Item 13, new pins, stays MET NARROWLY on the owner's 2026-11-15 decision: three register rows were written in 18.11.
- **Findings:** ten register cells cited reds that the 0.22.1 release made stale, fixed in this phase; #580's GitHub close reason says `COMPLETED` for a residual that was accepted, for the owner; and two of 18.11's command quotes were wrong, one of them a diff of a path that does not exist, corrected here.

## Scorecard

| # | DoD item | 18.11 grade | 18.13 grade | Why it changed or held |
|---|---|---|---|---|
| 1 | All planned phases complete | MET | **MET** | 18.1 to 18.12 read `complete` on `origin/main`; 18.13 is this re-audit. Every issue M18 closes is `CLOSED` except #718, open by design |
| 2 | All tests passing, both device lanes, `headSha` compared | MET | **MET** | On `93c7acac`, every run attempt 1 and on the PR head: .NET 2828/0/0, JVM 210/0, Android 273/0, iOS 307/0. Local .NET 2828/0/0, both TFMs |
| 3 | The CI flakes are fixed at their cause | NOT MET | **MET NARROWLY** | Changed by the owner's 2026-11-16 amendment and 18.12. #407: hunted for a phase, cause unproven, named failure plus a lane capture that ran on the graded lane, #718 open. #453: #580 accepted after 18.12 measured it, not fixed. #551, #564 and #444 hold literally. No bound raised; every signature 0 at attempt 1, one sample per lane |
| 4 | #500 is fixed as decided | MET | **MET** | Held: no code changed under it since 18.11 |
| 5 | #506 is fixed as decided | MET | **MET** | Held: no code changed under it |
| 6 | #511 is fixed, with #601 | MET | **MET** | Held; #621 is adjacent |
| 7 | #522 is fixed as decided | MET | **MET** | Held: no code changed under it |
| 8 | #543 and #544 are fixed | MET | **MET** | Held: no code changed under it |
| 9 | #401 and #396 are fixed | MET | **MET** | Held: no code changed under it |
| 10 | #530 is fixed | MET | **MET** | Held: no code changed under it |
| 11 | #568 is fixed: no register cell cites a stale red without naming it stale | MET | **MET** | Held, after a fix in this phase: ten cells cited reds stale since `fc7c0586` without saying so. They now name them and cite the 18.13 re-runs. #717 is still the residue |
| 12 | No ABI change | MET | **MET** | Held: the only change to `Exports.cs` is `VersionNumber`, which is not ABI. The 10 exports, the 80-byte struct, the C header and `wire-vocabulary.json` are unchanged |
| 13 | Every new pin conforms to the grading rule | MET NARROWLY | **MET NARROWLY** | Held: 339 of 339 sites fresh, 312 by diff and 27 re-run in 18.13. 18.12 added no pin. The three late register rows stay the narrowing, on the owner's 2026-11-15 decision |

---

## The audited code

```bash
git rev-parse origin/main                                    # 7d4253a867b7e15c4d9d394dec2b363e81364541
git merge-base --is-ancestor origin/main HEAD && echo ancestor   # ancestor
git merge-base --is-ancestor 09332c5e HEAD && echo ancestor      # ancestor
git diff --stat origin/main HEAD -- ':!docs'                 # (empty): the branch is docs-only against origin/main
git diff --stat 7d4253a8 93c7acac -- ':!docs'                # (empty): the graded head runs origin/main's code
git diff --stat da6f8c1d 09332c5e -- ':!docs' ':!*.md'       # (empty): 18.11's squash changed no code
git log --format='%h %s' 5af84d3d..7d4253a8 | wc -l          # 58
```

**What changed outside the docs since 18.11:**

```bash
git log --format='%h %s' 09332c5e..7d4253a8
#   7d4253a8 docs: add AGENTS.md for coding agents and reviewers (#723)
#   174e4187 chore(state): complete phase 18.12 and hand off to 18.13 (#722)
#   ed9e1e23 docs(18.12): #407 relaunch trigger not reproduced, capture added, #580 accepted
#   fc7c0586 chore(main): release 0.22.1 (#706)
#   5f83d166 chore: standardise release-please changelog sections (#719)
#   bd8c7159 chore(deps): update dependency coverlet.collector to 10.1.1 (#720)
git diff --stat 09332c5e HEAD -- ':!docs' ':!*.md' | tail -1
#   15 files changed, 111 insertions(+), 26 deletions(-)
git diff --stat 09332c5e HEAD -- src tests templates samples tools
#   src/BlazorNative.Runtime/Exports.cs | 2 +-     src/Directory.Build.props | 2 +-
#   templates/…/BlazorNative.Templates.csproj | 2 +-   …/.template.config/template.json | 4 +-
#   …/MyBlazorNativeApp.csproj | 6 +-              tests/Directory.Build.props | 2 +-
```

The other nine are the five workflows, `.release-please-manifest.json`, `release-please-config.json` and 18.12's two scripts, `scripts/android-emulator-settle.sh`, a comment only, and `scripts/android-gradle-captured.sh`, new. The full list is in [the record](2026-11-16-phase-18.13-record.md) §1.

- **`Exports.cs`:** one line, `VersionNumber`, `0.22.0` to `0.22.1`, in `fc7c0586`. The diff is under [item 12](#12-no-abi-change--met).
- **`tests/Directory.Build.props`:** `coverlet.collector` 10.1.0 to 10.1.1, in `bd8c7159`. It holds no test.
- **No test file was added or changed,** and no product file other than `Exports.cs`. Every line number 18.11 quoted under `src/` and `tests/` is therefore the same on HEAD, and the re-reads below confirm it.

## 1. All planned phases complete — **MET**

> All planned phases complete

```bash
git show origin/main:docs/planning/ROADMAP.md | grep -n "Phase 18\.[0-9]*:"
#   3974:#### Phase 18.1: … [status: complete]   …   4074:#### Phase 18.11: Audit and close (#568) [status: complete]
#   4083:#### Phase 18.12: #407's relaunch trigger at its cause (#718) [status: complete]
#   4092:#### Phase 18.13: Re-audit and close [status: pending]
grep -n "Phase 18.13:" docs/planning/ROADMAP.md      # on this branch
#   4092:#### Phase 18.13: Re-audit and close [status: active]
```

**18.1 to 18.12 read `complete` on `origin/main`,** and `MILESTONE.md`'s phase list agrees. **18.13 is this re-audit.**

Every issue M18 set out to close, plus #580, which item 3's amendment names:

```bash
for i in 551 564 453 444 407 718 580 500 506 511 601 522 543 544 401 396 530 568; do
  gh issue view $i --json state,stateReason,closedAt,closedByPullRequestsReferences; done
#   #551, #453, #407        CLOSED COMPLETED 2026-10-28  by #584
#   #564                    CLOSED COMPLETED 2026-11-14  by #584, #710
#   #444                    CLOSED COMPLETED 2026-11-14  by #712
#   #718                    OPEN                          open by design: a recurrence of #407's relaunch reopens the hunt
#   #580                    CLOSED COMPLETED 2026-11-16  by #721   accepted, not fixed: see 3a and finding F2
#   #500                    CLOSED COMPLETED 2026-10-31  by #596, #597
#   #506                    CLOSED COMPLETED 2026-10-31  by #599, #605
#   #511, #601              CLOSED COMPLETED 2026-11-03  by #605
#   #522                    CLOSED COMPLETED 2026-11-10  by #634
#   #543, #544              CLOSED COMPLETED 2026-11-11  by #659
#   #401, #396              CLOSED COMPLETED 2026-11-13  by #691
#   #530                    CLOSED COMPLETED 2026-11-14  by #707
#   #568                    CLOSED COMPLETED 2026-11-15  by #713
```

**Grade: MET.** #718 is on the milestone's "Closes" line but is open on purpose: the amendment to item 3 keeps it open for a recurrence.

## 2. All tests passing, both device lanes, `headSha` compared — **MET**

> All tests passing, .NET, JVM **and both device lanes**, with each lane's `headSha` compared against the PR head

```bash
gh run list --commit 93c7acac45a994ff755573e1660a4977ccca2a9b --json databaseId,name,headSha,attempt,conclusion,event
#   43963189351 ci                    93c7acac attempt 1 pull_request success
#   43963189243 android-instrumented  93c7acac attempt 1 pull_request success
#   43963189255 ios                   93c7acac attempt 1 pull_request success
#   43963190070 baseline-checks       93c7acac attempt 1 pull_request success
#   43963189228 commitlint            93c7acac attempt 1 pull_request success
```

| lane | run | attempt | `headSha` | result | quoted line, re-read from the run's log here |
|---|---|---|---|---|---|
| .NET, CI | `ci.yml` [43963189351](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/43963189351), PR #724 | 1 | `93c7acac` | **2828 / 0 / 0** | `dotnet test totals across 6 projects: passed=2828 skipped=0 failed=0` |
| JVM, CI | same run | 1 | `93c7acac` | **210 / 0** | `JVM test totals across 37 suites: tests=210 failures=0 errors=0 skipped=0` |
| Android | `android-instrumented.yml` [43963189243](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/43963189243) | 1 | `93c7acac` | **273 / 0** | `Instrumented totals across 1 suite(s): tests=273 failures=0 errors=0 skipped=0` |
| iOS | `ios.yml` [43963189255](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/43963189255) | 1 | `93c7acac` | **307 / 0** | `Executed 307 tests, with 0 failures (0 unexpected)`, twice: the run line and the bundle summary |
| .NET, local | this worktree at `23e7168b`, code equal to `7d4253a8` | 1 | — | **2828 / 0 / 0** | per TFM: Analyzers 27, Renderer 147, Runtime 1240; [the record](2026-11-16-phase-18.13-record.md) §4 "Cleanup" |

```bash
gh run view <id> --log > <id>.log      # for each of the three runs, then:
grep -oE 'dotnet test totals.*failed=[0-9]+' 43963189351.log     # passed=2828 skipped=0 failed=0
grep -oE 'JVM test totals.*skipped=[0-9]+' 43963189351.log       # tests=210 failures=0 errors=0 skipped=0
grep -oE 'Instrumented totals.*skipped=[0-9]+' 43963189243.log   # tests=273 failures=0 errors=0 skipped=0
grep -oE 'Executed 307 tests, with [0-9]+ failures \([0-9]+ unexpected\)' 43963189255.log | sort | uniq -c
#   2 Executed 307 tests, with 0 failures (0 unexpected)
```

- **Both device lanes carry the PR head's `headSha`,** so neither is a stale lane, and every run is attempt 1.
- **The expected counts** are the gates' baselines, 2828, 210, 273 and 307. 18.12 changed no test, so they are 18.11's.
- **This report re-read the logs itself;** it did not take the counts from the record.

**Grade: MET.**

## 3. The CI flakes are fixed at their cause — **MET NARROWLY**

> **The CI flakes are fixed at their cause:** #551, #564, #453, #444 and #407. …
> - Each flake's cause is found and fixed. Each fix is pinned where a pin can hold it.
>   - *Owner decision 2026-11-16:* a flake whose cause stays unproven after a phase spent hunting it (#407: 18.12, with #718 left open for a recurrence) meets this bullet with a fix that makes the failure name itself, plus a lane capture that would record the cause if it recurs. A residual the owner accepted after measuring it (#580, 18.12) counts as decided.
> - No fix raises a bound to hide a slow path.
> - The re-audit's own CI runs hit none of these signatures. Attempt 1 is green on every lane.

### 3a. Cause, fix and pin, per flake

Carried forward from 18.11 §3a. The rows for #551, #564 and #444 are unchanged, because no code under them changed. The rows for #453 and #407 are updated from [the 18.12 record](2026-11-15-phase-18.12-record.md).

| Flake | Cause, as recorded | Fix | Pin | Residue |
|---|---|---|---|---|
| #551 | The suite's own blocking waits held pool threads, so a positive control whose frame crosses the pool starved. 18.1 record §3 | `5a7ab54a`: `RunBounded` refuses a pool thread, `OffPool`, `IAsyncLifetime` fixtures, positive controls off the pool | `PoolStarvationTests` and `GeneratorRunTests.RunBounded_OnAPoolThread_…` | The product's two pool hops are #575, open, an owner decision. #603, a different `Canceled` outcome once, open |
| #564 | 18.1: a synchronous `Console.Error` write blocked the render thread. 18.9: the other-thread probe's synchronous part waited on the pool's queue delay. 18.9 record §1 | `5a7ab54a`, the `BnLog` queue; `a1bacd6b`, the probe begins on a dedicated thread | 18.1's `BnLogTests` and leak pins; `DispatchBoundPoolTests`, `TearDownOverrunTests` | #711, about 25 facts of #551's shape, open |
| #453 | A cold `LAContext()` start paid inside the tap's 30 s armed-reply wait. 18.1 record §4 | `5a7ab54a`: `warmUpContext()` at boot, a named warm-up step | `BnBiometricsTests.testBootWarmsOneLAContextOffTheBootPathBeforeTheFirstBiometricRequest` | **#580, accepted, not fixed.** 18.12 timed the first creation in 10 `ios.yml` runs: 613 to 6139 ms, none near 20000 ms, so the 30 to 51 s tail was not caught. The cost it did see is `launchd_sim` starting `coreauthd` on demand plus up to 0.9 s of client setup. The owner accepted the residual on 2026-11-15; the named failure at `awaitContextWarmUp` stays, and no bound was raised. 18.12 record §7 |
| #444 | `show` completed inside `add`'s completion, which waits on SpringBoard's `willPresent`, once 11.25 s late. 18.10 record §1 | `da6f8c1d`: `show` completes `Granted` when posted, as Android's does | `testShowCompletesGrantedWhilePresentationIsHeld` and the late-error pins | The `[#444]` trace is stale, **#716**, open |
| #407 | **Not found.** `MainActivity` relaunched between the echo check and the image poll, 18.1 record §5. 18.12 hunted the trigger: 22 counted install runs, 10 + 10 natural and 2 forced under load, and 4 trace events forced on demand. None relaunched it. The only full-signature relaunch was `overlay-shell`, a root-only mechanism probe. 18.12 record §3 | `5a7ab54a`: `pollUntilOnSameActivity`, the settle step, so the failure names itself. **18.12 added a lane capture:** `scripts/android-gradle-captured.sh` samples package, overlay and broadcast changes beside Gradle, and the events buffer is enlarged to 16M and dumped, all uploaded in `instrumented-logcat` on every run. 18.12 record §4 | `PollAndroidTest.a_poll_on_a_recreated_activity_fails_naming_it`, `BnCameraAndroidTest.a_recreation_before_the_image_poll_…`. The capture is a lane step and has no pin | **#718, open by design,** for a recurrence. The capture's sampler runs through the install window and could shift its timing; it has no sampler-free control. 18.12 record §4 |

**The capture ran on the graded lane:**

```bash
gh api repos/MarcelRoozekrans/BlazorNative/actions/runs/43963189243/artifacts --jq '.artifacts[]|[.name,.size_in_bytes]|@tsv'
#   instrumented-test-results  664981
#   blazornative-routes-resource  340
#   instrumented-logcat  1657402
#   BlazorNative.Runtime-linux-bionic-x64  2635641
```

[The record](2026-11-16-phase-18.13-record.md) §5 "The capture" downloaded `instrumented-logcat` and found `logcat.txt`, `package-sampler.txt` with 53279 lines and `logcat-events.txt` with 10529 lines, with the sampler's first `S` line quoted. So the capture the amendment relies on is present on the run graded here, not only on 18.12's own runs.

### 3b. No bound was raised

18.11 §3b covered `5af84d3d..da6f8c1d`, and `da6f8c1d..09332c5e` changed no code. This re-checks `09332c5e..HEAD`:

```bash
git diff --stat 09332c5e HEAD -- tests src/BlazorNative.Apple/BnHostTests src/BlazorNative.Jni/src/test \
  src/BlazorNative.Jni/src/androidTest src/BlazorNative.Jni/build.gradle.kts
#   tests/Directory.Build.props | 2 +-         coverlet.collector 10.1.0 to 10.1.1; no test, no bound
git diff 09332c5e HEAD -- .github/workflows | grep -E '^[-+].*timeout-minutes'
#   (empty)
git diff 09332c5e HEAD -- .github/workflows scripts | grep -nE '^[-+].*(timeout|Timeout|sleep|deadline)'
#   only + lines of the new scripts/android-gradle-captured.sh: each sampler read is `timeout 5`, then `sleep 1`
grep -n 'SETTLE_CEILING_S:-' scripts/android-emulator-settle.sh
#   49:CEILING_S=${SETTLE_CEILING_S:-300}      unchanged: the settle script's diff is a comment only
```

On HEAD, 18.11's bound quotes read the same: `ExportBudget` 1 s and `Budget` 10 s in `FaultNoticeTests.cs:62`–`:63`, `Budget` 10 s in `ShutdownQuiescenceTests.cs:67`, `pollUntil(deadline seconds: TimeInterval = 10` in `BnNotificationsTests.swift:714`, and `awaitArmedReply(…, deadline: 30)` at `BnBiometricsTests.swift:340`, `:358` and `:560`, with `deadline: 10` at `:252`.

- **The workflow diffs** are the SDK step's name in five workflows, and in `android-instrumented.yml` the capture: the events buffer, the call through `android-gradle-captured.sh`, and the artifact upload widened to `always()`.
- **The capture's `timeout 5` reads are new bounds** on new reads, and they cannot fail the step: each is guarded with `|| true`, and the step's exit status is Gradle's, per the 18.12 record §4.
- **#580's acceptance raised nothing.** The 18.12 record §7: "No test bound or timeout was raised, and no product or lane change is kept."

### 3c. The graded runs hit no signature

Each run's full log, from `gh run view <id> --log`, re-grepped here with `grep -c`. The results agree with [the record](2026-11-16-phase-18.13-record.md) §5.

| Signature | `ci` 43963189351 | `android-instrumented` 43963189243 | `ios` 43963189255 |
|---|---|---|---|
| #551 `completing the held call delivered no frame` | 0 | n/a | n/a |
| #564 `did not return within 1s`, `The collection contained 2 items` | 0, 0 | n/a | n/a |
| #453 `no reply armed within`, `A cold LocalAuthentication start` | n/a | n/a | 0, 0 |
| #444 `show never completed Granted` | n/a | n/a | 0 |
| #407 `never loaded into the BnImage\|Coil did not load` | n/a | 0 | n/a |

Every run is attempt 1. The logs are 8382, 1379 and 7983 lines long, so none of the zeros comes from an empty fetch.

### 3d. The grade

**MET NARROWLY,** against the amended text. Bullet by bullet:

- **"Each flake's cause is found and fixed. Each fix is pinned where a pin can hold it," with the owner's 2026-11-16 decision.**
  - **Literally, for #551, #564 and #444,** as in 18.11. Nothing under them changed.
  - **Under the amendment's second sentence, for #453.** The cause of the failure under about 40 s is found and fixed and pinned. The residual, #580, was measured in 18.12, 10 runs, and accepted by the owner on 2026-11-15. The amendment counts that as decided. It is accepted, not fixed: a cold start over about 40 s still fails at the named warm-up step.
  - **Under the amendment's first sentence, for #407.** It needs three things, and each holds:
    - a phase spent hunting the cause: 18.12, the 18.12 record §1 to §3;
    - a fix that makes the failure name itself: 18.1's `pollUntilOnSameActivity`, pinned by `PollAndroidTest` and `BnCameraAndroidTest`, sites 55 to 60, all fresh in [the record](2026-11-16-phase-18.13-record.md) §2;
    - a lane capture that would record the cause if it recurs: 18.12's capture, present on the graded run, 3a.
    The cause is not found. The title's "fixed at their cause" does not hold for #407, which is why MET is out of reach.
- **"No fix raises a bound to hide a slow path."** Holds literally, 3b.
- **"The re-audit's own CI runs hit none of these signatures. Attempt 1 is green on every lane."** Holds literally, 3c.

**The narrowings, by name:**
1. **#718 is open.** #407's trigger is unproven; the lane names the failure and records the conditions, but nothing removes the relaunch.
2. **#580 is accepted, not fixed.** #453 holds only below about 40 s of cold start. GitHub shows #580 as `CLOSED COMPLETED`, which reads as fixed; that is finding F2.
3. **One sample per lane.** The five flakes were intermittent, at rates as low as about 1 in 10 runs. One clean run per lane shows that this head did not hit them, not that they cannot recur.
4. **The capture's sampler has a timing limit.** It runs `dumpsys` reads through the install window, which could move the install-processing timing that #407 depended on. The capture has no sampler-free control, so whether it makes #407 rarer or more frequent is not known, per the 18.12 record §4. This limit affects how much a quiet lane means, not the grade.

**Why not MET.** The amendment changes what the first bullet accepts, not the item's title. The design and the plan cap item 3 at MET NARROWLY for that reason, and this report keeps 18.11's NOT MET as history in the scorecard.

## 4. #500 is fixed as decided — **MET**

`#500`: `CLOSED` by #596 and #597.

```bash
grep -n 'public void RunAfterDispatch\|18.2 (#500)' src/BlazorNative.Renderer/NativeRenderer.cs | head -10
#   118, 314, 320, 333, 344, 358, 373, 401, 473, 481      (320 is public void RunAfterDispatch(Action action))
grep -n 'public void AnActionQueuedByAnInlineContinuation\|public void ATaggedReRender' tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs
#   1386 AnActionQueuedByAnInlineContinuation_IsItsOwnDispatchs_NotTheWindows
#   1413 ATaggedReRender_IsItsOwnDispatchs_NotTheWindows      1417 …_InStrictModeToo      1497 ATaggedReRendersFault_LeavesTheWindowsOwnFault_Rc2
```

- **A's re-render and A's `RunAfterDispatch` action** are charged to A: `:1413` and `:1386`.
- **The window keeps its own rc:** `:1497`.
- **Mutation-proven:** all 87 18.2 sites have fresh reds, 75 fresh by diff and 12 re-run in 18.13 §3, [item 13](#13-every-new-pin-conforms-to-the-grading-rule--met-narrowly).

**Grade: MET.** Same lines as 18.11.

## 5. #506 is fixed as decided — **MET**

`#506`: `CLOSED` by #599 and #605.

```bash
grep -n 'fun a_same_key\|fun another_key' src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/AndroidHostCallContractTest.kt
#   327 a_same_key_delete_waits_behind_a_pending_auth_set     347 a_same_key_plain_set_waits_behind_a_pending_auth_set
#   386 another_key_is_not_blocked_by_a_pending_auth_set      404 a_same_key_delete_waits_behind_a_pending_getWithAuth
#   710, 733: the 18.4 two-bridge twins
grep -n 'func testASameKey\|func testAnotherKey' src/BlazorNative.Apple/BnHostTests/BnSecureStorageTests.swift
#   542 testASameKeyDeleteWaitsBehindAPendingGetWithAuth      651 testAnotherKeyIsNotBlockedByAPendingGetWithAuth
cmp src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt \
    templates/BlazorNative.Templates/content/BlazorNative.App/android/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt   # identical
```

- **A delete and a plain set wait behind an auth set,** and another key is not blocked: `:327`, `:347`, `:386`. The site at `:391`, row 162, in the `:386` test, is one of the four re-run in [the sample](#the-sample).
- **iOS has the same hazard and the same fix,** pinned at `:542` and `:651`.
- **Every 18.3 site is fresh,** 76 of 76 by diff, [the record](2026-11-16-phase-18.13-record.md) §2.

**Grade: MET.** #600 is adjacent: the biometrics op, not secure storage's order.

## 6. #511 is fixed, with #601 — **MET**

`#511` and `#601`: `CLOSED` by #605.

```bash
grep -n '18.4 (#511)' src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt   # 168, 174, 178, 421, 428, 524
for t in RecreationDuringBootAndroidTest SecureHoldAcrossRecreationAndroidTest StartLockTest RetiredLaneDispatchTest; do
  grep -c '@Test' $(git ls-files | grep "/$t.kt"); done                                               # 2, 1, 1, 1
grep -n 'one per process\|process-wide' src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt   # 277, 283, 426, 2109
```

- **The device pin:** `RecreationDuringBootAndroidTest`, fresh, as are all 38 18.4 sites by diff.
- **#601:** the secure-storage hold is process-wide, `AndroidShellBridge.kt:277`–`:283`, `:426` and `:2109`, pinned by `SecureHoldAcrossRecreationAndroidTest` and the 18.4 two-bridge facts.

**Grade: MET.** #621 is adjacent: a multi-instance edge #601's bullet does not claim.

## 7. #522 is fixed as decided — **MET**

`#522`: `CLOSED` by #634.

```bash
grep -n 'Phase 18.5 (#522)' src/BlazorNative.Renderer/NativeRenderer.cs                            # 2319
grep -n 'public void TwoBoundBnInputs_OnTwoPages\|public void AGetSetBoundBnInput\|public void ALambdaDeclaredInAHelper' \
  tests/BlazorNative.Runtime.Tests/SlowHandlerWarningTests.cs
#   750 TwoBoundBnInputs_OnTwoPages_GetTwoWarnings_NamingThePages   851 AGetSetBoundBnInput_NamesTheAppsSetMethod
#   899 ALambdaDeclaredInAHelper_NamesTheHelperAndThePosition
```

- **A bound input's warning names the page and position:** `:750` and its siblings.
- **The app's method is still named where one exists:** `:851`.
- **All 27 18.5 sites are fresh** by diff.

**Grade: MET.**

## 8. #543 and #544 are fixed — **MET**

`#543` and `#544`: `CLOSED` by #659.

```bash
grep -n 'junit.jupiter.execution.timeout\|timeout.set' src/BlazorNative.Jni/build.gradle.kts
#   274 junit.jupiter.execution.timeout.default "120 s"   275 thread mode SEPARATE_THREAD   276 timeout.set(Duration.ofMinutes(25))
grep -n 'fun pressHandledBack' src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/Poll.kt     # 99
```

- **#543:** `BackNoticeTest`'s blocking back is joined with a 10 s bound and fails by name, and the per-test and task timeouts hold the step.
- **#544:** the back tests check in their own turn through `pressHandledBack`.
- **All 6 18.6 sites are fresh** by diff.

**Grade: MET.**

## 9. #401 and #396 are fixed — **MET**

`#401` and `#396`: `CLOSED` by #691.

```bash
ls src/stderr-pump-vectors.json                                                     # the one manifest
grep -n 'fun `every_vector\|func testEveryVector' \
  src/BlazorNative.Jni/src/test/kotlin/io/blazornative/shell/BnStderrPumpTwinTest.kt \
  src/BlazorNative.Apple/BnHostTests/BnStderrPumpTwinTests.swift                    # :55 and :51, the twin pins
grep -n '#396' src/BlazorNative.Apple/BnHost/BnSecureStorage.swift                  # 22, 24, 116, 392, 396, 411
```

- **#401:** one manifest, generated into both suites, and a twin pin per shell.
- **#396:** iOS checks enrolment first, as Android does.
- **All 26 18.7 sites are fresh** by diff.

**Grade: MET.**

## 10. #530 is fixed — **MET**

`#530`: `CLOSED` by #707.

```bash
grep -n -- '-nodeReuse:false' tests/BlazorNative.Runtime.Tests/GeneratorRunTests.cs | head -2       # 275, 281
grep -n '#530' tests/BlazorNative.Runtime.Tests/BoundedScript.cs                                    # 103, 111
```

- **The holder** is a reused MSBuild worker node; node reuse off is the source fix, pinned by `GenerateReference_RunsEveryMSBuildCallWithNodeReuseOff`. Its site, row 322, is fresh by diff.
- **The 17.5 bound still names it,** `BoundedScript.cs:103` and `:111`.
- **A correction to 18.11's quote,** finding F3: 18.11 printed `281, 290` for the first command. It prints `275, 281`, on `da6f8c1d` and on HEAD alike. `:275` is the doc comment of the helper that lists the MSBuild calls without the switch, `:281` its filter, and `:290` the next hit. The code is unchanged; only the quote was wrong.

**Grade: MET.**

## 11. #568 is fixed — **MET**

> **#568 is fixed.** No register cell cites a stale red without naming it stale.

**What 18.11 checked.** Its record §7 ran the staleness diff on every red each register cell cites, 163 rows, on `afcbdf65`, whose code equals `da6f8c1d`. Every stale red was named stale in its cell.

**What changed since.** The register itself is unchanged since 18.11, and one subject is not:

```bash
git diff --stat 09332c5e 93c7acac -- docs/pin-standard.md        # (empty)
grep -c 'Stale, named in 18.11' docs/pin-standard.md              # 39, as in 18.11
```

`Exports.cs` changed in `fc7c0586`, so [the record](2026-11-16-phase-18.13-record.md) §2 found 27 M18 sites stale, all through `Exports.cs`. Their cited reds are 18.11 re-runs on `ee5a8d1c` and `e39e2260`, and ten register cells cite them with no word that they are stale:

| PS line | cell | stale sites, 18.13 record §2 rows |
|---|---|---|
| `:533` | `AnActionQueuedByAnInlineContinuation_IsItsOwnDispatchs_NotTheWindows` | `:1403`, `:1404`, rows 72, 73 |
| `:534` | `ATaggedReRender_IsItsOwnDispatchs_NotTheWindows` | `:1436`, `:1437`, rows 78, 79 |
| `:535` | `ATaggedReRender_IsItsOwnDispatchs_InStrictModeToo` | `:1436`, `:1437`, rows 84, 85 |
| `:536` | `TheReRenderAfterAnInlineHandlerCompletes_IsItsOwnDispatchs` | `:1487`, `:1488`, rows 93, 94 |
| `:543` | `ADisposalFaultCausedByATaggedRender_IsItsOwnDispatchs` | `:1663`, `:1664`, rows 125, 126 |
| `:544` | `AHandlerFaultWhileAHoldIsOutstanding_IsStillItsOwnLateFault` | `:1691`, `:1692`, rows 133, 134 |
| `:625` | `FaultNoticeTests` 18.1 facts | `:1996`, `:2002`, `:2026`, `:2045`, `:2050`, `:2057`, `:2090`, `:2096`, `:2100`, rows 14 to 22 |
| `:628` | `DispatchBoundPoolTests` | `:59`, row 327 |
| `:631` | `BnLogShutdownDrainTests` and `BnLogProcessExitDrainTests` | `:92`, `:93`, `:97`, `:101`, rows 23 to 26 |
| `:638` | `TearDownOverrunTests` | `:53`, row 328 |

That is the 27 sites, and so the text of item 11 did not hold on the audited head. Three of them, `:1996`, `:2002` and `:2057` at `:625`, were found only by the final review: their reds mutate `BnLog.cs`, `BnLogQueue.cs` and the test file, but the facts are named for the dispatch export and `Exports.PendingDispatchObserver`, both in `Exports.cs`, and 18.11's commands for them left that file out. The record's §2 Disagreements, item 5, applies 18.11's own "named for" term to them. **Finding F1, fixed in this phase:** each of the ten cells now says which of its reds are stale since `fc7c0586`, why, and cites their re-runs in [the record](2026-11-16-phase-18.13-record.md) §3. The 18.2 table's introduction, PS `:525`, which said each 18.2 cell's 18.11 clause cites a fresh red for every graded site, now says the same.

**Corroborating reds on these sites are stale too.** Staleness goes by the site's files, so a corroborating red on a stale site is stale whatever file it mutates. A search of the ten cells for every corroborating red id on the 27 sites finds three, each now named stale in its cell and not re-run, since a corroborating red grades no site: `18.1-L1` on `:2026` and `:2090` and `18.1-M5` on `:2050`, both on `ee5a8d1c`, at `:625`; and `18.9-R4` on `:59`, on `e39e2260`, at `:628`. Row 22's `M8` is an 18.1 red on `2f1f0c39` that the cell already named stale in 18.11. The other corroborating reds in these cells, at `:533` to `:536`, sit on sites that are fresh. No other red a cell cites changed status.

```bash
grep -n 'ee5a8d1c\|e39e2260\|9f01a127' docs/pin-standard.md | cut -d: -f1 | sort -u
#   525 533 534 535 536 543 544 577 625 626 628 631 638
grep -n 'since `fc7c0586`' docs/pin-standard.md | cut -d: -f1
#   533 534 535 536 543 544 625 628 631 638      and :525's introduction names fc7c0586 too
```

The two cells that cite the batch commits and were left alone cite only fresh sites: `:577`, `FaultNoticeTests` 16.1's 18.9 part, rows 323 to 325; and `:626`, `GeneratorRunTests`' pool pins, rows 31 to 37.

**The rest of 18.11's sweep.** Its 48 fresh rows are M18 cells, whose sites [the record](2026-11-16-phase-18.13-record.md) §2 diffed one by one, plus seven pre-M18 rows: `BnActivityIndicatorTests`, `BnImagePolishDemoTests` group E, `BnDiagnosticsTest` twice and `GeneratorRunTests` three times. None of those reads one of the 15 changed files: the `GeneratorRunTests` facts read `generate-reference.ps1`, `exit-code.ps1` and `hold-stdout.ps1`, and the only scripts that changed are 18.12's two Android scripts.

**Grade: MET,** on the item's text, after F1's fix. **Not hidden:** the residue 18.11 filed is unchanged: the pre-M18 cells name their stale reds but have no fresh red, #717, open.

## 12. No ABI change — **MET**

Every command was run over `09332c5e..HEAD`. 18.11 covered `5af84d3d..da6f8c1d`, and `da6f8c1d..09332c5e` changed no code.

```bash
git diff 09332c5e HEAD -- src/BlazorNative.Runtime/Exports.cs
#   @@ -149,7 +149,7 @@ public static class Exports
#        /// <summary>Single source of truth for the runtime version — the
#        /// JNA-visible version cstring and NativeShellBridge.PlatformInfo both
#        /// derive from it.</summary>
#   -    internal const string VersionNumber = "0.22.0"; // x-release-please-version
#   +    internal const string VersionNumber = "0.22.1"; // x-release-please-version

# the exports, both ends
for r in 09332c5e HEAD; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -c '^\s*\[UnmanagedCallersOnly(EntryPoint'; done
#   10
#   10
for r in 09332c5e HEAD; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -o '\[UnmanagedCallersOnly(EntryPoint = "[a-z_]*"' | sort | md5sum; done
#   52ec8c5e37245dd1522729412c4b3c2a  -      (both, and the same as 18.11's)
for r in 09332c5e HEAD; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -A2 'UnmanagedCallersOnly(EntryPoint' \
  | grep -E 'static .*\(' | sed 's/^\s*//' | md5sum; done
#   f1ff67805e94e3a1c80ba630f0b4eb61  -      (both, and the same as 18.11's: the 10 signatures are unchanged)
git grep -n 'UnmanagedCallersOnly(EntryPoint' HEAD -- 'src/*.cs' | grep -v Exports.cs
#   only InteropBoundaryAnalyzer.cs:60, the analyzer's message text

# the 80-byte struct
git diff --stat 09332c5e HEAD -- src/BlazorNative.Runtime/BridgeProtocolNative.cs                     # (empty)
for r in 09332c5e HEAD; do git show $r:src/BlazorNative.Runtime/BridgeProtocolNative.cs \
  | awk '/public struct BlazorNativeBridgeCallbacks/,/^}/' | grep -c "public IntPtr"; done                  # 10, 10
grep -n "80 bytes" src/BlazorNative.Runtime/BridgeProtocolNative.cs                                         # :91, :96

# the C header, the JNA bindings and the wire, over the whole of M18
for f in src/BlazorNative.Runtime/BridgeProtocolNative.cs src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeBindings.kt \
  src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h src/wire-vocabulary.json src/BlazorNative.Runtime/FrameEncoder.cs; do
  for r in 5af84d3d 09332c5e HEAD; do git cat-file -e $r:$f || echo "MISSING $r $f"; done; done          # (nothing: every path exists)
git diff --stat 5af84d3d HEAD -- src/BlazorNative.Runtime/BridgeProtocolNative.cs \
  src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeBindings.kt \
  src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h src/wire-vocabulary.json src/BlazorNative.Runtime/FrameEncoder.cs   # (empty)
git diff --stat 09332c5e HEAD -- '*.g.cs' '*.g.kt' '*.g.swift' '*.g.h'                                    # (empty)
```

- **The exports:** the same 10 names and the same 10 signatures. The one changed line is `VersionNumber`, an `internal const string`. It is not an export, a parameter or a struct field; it reaches the shells only as the text the version export returns, and that export's name and signature are in the hashes above. A version string is not ABI.
- **The struct:** the same 80-byte struct of 10 `IntPtr` fields, and the file is unchanged over all of M18.
- **The C header and `NativeBindings.kt`:** unchanged over all of M18.
- **The wire:** `wire-vocabulary.json`, `FrameEncoder.cs` and every generated file are unchanged since 18.11.
- **A correction to 18.11,** finding F4: 18.11's §12 diffed `src/BlazorNative.Renderer/FrameEncoder.cs` beside the generated files. That path exists at neither end, so the encoder was never checked: git prints nothing for it either way. The file is `src/BlazorNative.Runtime/FrameEncoder.cs`. The `cat-file` loop above confirms every path here exists, and the diff over `5af84d3d..HEAD` with the right path is empty, so 18.11's conclusion stands.

**Grade: MET.**

---

## 13. Every new pin conforms to the grading rule — **MET NARROWLY**

> **Every new pin conforms to the grading rule.**
> - Its sites, meaning the labelled anchors and name claims, have fresh reds that quote their messages.
> - Its register cell is written in the phase that adds it.
> - Staleness is checked against each red's own commit.

### 13a. The population — 108 pins, 339 sites, unchanged

18.12 added no pin: the 18.12 record §8 says so, and `09332c5e..HEAD` adds or changes no test file, [the audited code](#the-audited-code). The population is the 18.11 record §2 table, re-loaded by script in [the record](2026-11-16-phase-18.13-record.md) §2: 339 rows, 108 pins, the same per route.

### 13b. Fresh reds with quoted messages — every site

**Staleness against each red's own commit.** The record §2 ran `git diff --stat <red commit> HEAD -- <test file> <subject files>` for each of the 339 sites, every path a file. For a site 18.11 re-ran, the red commit is the re-run's, and the files include every file the re-run's mutation edits.

```bash
sed -n '/^### The table/,/^### Fresh/p' docs/plans/2026-11-16-phase-18.13-record.md | grep -E '^\| [0-9]+ \|' \
  | awk -F'|' '{print $(NF-2)}' | sort | uniq -c
#   312 fresh
#    27 stale
```

| Route | Sites | Fresh by diff | Stale | Re-run in 18.13 | Red |
|---|---|---|---|---|---|
| .NET | 170 | 143 | 27 | 27, both TFMs | 27 |
| JVM | 23 | 23 | 0 | — | — |
| Android | 94 | 94 | 0 | — | — |
| iOS | 52 | 52 | 0 | — | — |
| **All** | **339** | **312** | **27** | **27** | **27** |

- **The 27 stale sites** are stale only through `Exports.cs`, whose only change is `VersionNumber`. The record §3 re-ran each with its 18.11 mutation, its text taken from HEAD, on `23e7168b`, on `net10.0` and `net11.0`. Each failed at its own line with its own message, the same text as 18.11's apart from elapsed milliseconds in rows 24 and 25. The two timing-shaped reds, `18.9-S4` and `18.1-S1` on `TearDownOverrunTests`, red 5 of 5 runs per framework.
- **The 312 fresh sites** keep the reds 18.11 graded them on: 108 from the phase records and 204 from the 18.11 re-runs.
- **Five sites are stale only because the record added the re-run mutation's file,** rows 16, 18, 20, 327 and 328; 18.11's own file lists for them did not include `Exports.cs`. The record's Disagreements, item 1, resolves them as stale, the stricter reading.
- **Three sites are stale only because the record added the file their pin is named for,** rows 14, 15 and 19, found by the final review; 18.11's commands for them left out `Exports.cs`. The record's Disagreements, item 5, resolves them as stale, and its §3 re-runs `18.1-M1`, `M2` and `M6` on `23e7168b`, each red at its own line with 18.11's message on both frameworks, `M6` reported at `:2056` as in 18.11.

### The sample

To check that `fresh` by diff still means the red holds, the record §4 re-ran one fresh-by-diff red per route, from four different phases, on `23e7168b` or a scratch commit whose parent is `23e7168b`:

| Route | Row | Site | Phase | Run, attempt | Reproduces? |
|---|---|---|---|---|---|
| .NET | 323 | `FaultNoticeTests.cs:1024` | 18.9 | local, both TFMs | yes |
| JVM | 260 | `RetiredLaneDispatchTest.kt:31` | 18.4 | `ci.yml` 43959346307, draft PR #725, attempt 1 | yes |
| Android | 162 | `AndroidHostCallContractTest.kt:391` | 18.3 | `android-instrumented.yml` 43959202671, attempt 1 | yes |
| iOS | 53 | `BnBiometricsTests.swift:562` | 18.1 | `ios.yml` 43959251267, attempt 1 | yes |

**4 of 4 reproduce,** each at the site's own line with the message 18.11 quoted. Every scratch ref is deleted and #725 is closed.

**This sub-bullet is met:** 339 of 339 sites have a fresh RS1 red with a quoted message, and after F1 every register cell that cites one of the 27 re-run sites names its stale red and cites the fresh one.

### 13c. Register cells written in the phase that adds them — three were not

Unchanged from 18.11 §13c. 105 of the 108 pins have a cell from their own phase. Three have rows written in 18.11, PS `:638` to `:640`:

```bash
grep -n 'row added in 18.11' docs/pin-standard.md | cut -c1-70
#   638:| `TearDownOverrunTests.ADispatchThatOverranAndIsStillRunning_DoesNot…
#   639:| XCTest `BnNotificationsTests.testShowGrantRoundTripsThroughTheRealHo…
#   640:| Instrumented `BnCameraAndroidTest.take_photo_composes_a_captured_fi…
```

18.12 added no pin, so it owed no cell.

### 13d. The grade

**MET NARROWLY.**
- **108 pins, 339 sites.** Every site has a fresh RS1 red against its own commit, with a quoted message: 312 fresh by diff, 27 re-run in 18.13, and 4 of the fresh ones re-run as a sample.
- **The narrowing:** three register rows were written in 18.11, not in the phase that added or altered the pin. The owner decided on 2026-11-15 to read the item on intent, MET NARROWLY, and nothing since changes that.

---

## Carried debt

Each issue is judged against the item it touches. None blocks the close on its own. States from `gh issue view <n> --json state,stateReason` on 2026-11-16.

| issue | state | title | item | judgement |
|---|---|---|---|---|
| #718 | OPEN | android lane: find and fix the trigger of #407's MainActivity relaunch after install | 3 | **part of item 3's narrowing**, open by design, for a recurrence |
| #716 | OPEN | iOS notifications test: the #444 add trace prints NO completion on every passing show since 18.10 | 3 | filed by 18.11; a stale diagnostic, no assertion depends on it |
| #717 | OPEN | Pin register: pre-M18 cells name their stale reds but have no fresh red | 11 | filed by 18.11; the residue of #568 |
| #711 | OPEN | test: FaultNoticeTests facts block a pool worker while their notice crosses the pool | 3 | adjacent: #551's shape in other facts |
| #575 | OPEN | runtime: a host-call completion crosses the thread pool twice before its handler resumes on the render thread | 3 | adjacent: the product side of #551, an owner decision |
| #603 | OPEN | ShutdownQuiescenceTests positive control ended Canceled once on the Windows runner | 3 | adjacent: a different outcome from #551's, seen once |
| #621 | OPEN | Android: a second instance's final destroy denies another instance's orphaned secure-storage prompt | 6 | adjacent: a multi-instance edge |
| #600 | OPEN | Android biometrics authenticate can hang when its Activity finishes during the prompt | 5 | adjacent: the biometrics op |
| #593 | OPEN | A batch that a render fault ends never delivers its remove patches | 4 | adjacent, found in 18.2 |
| #582 | OPEN | android lane: flag an unexpected second MainActivity per test from the logcat | 3 | adjacent: a lane check beside #718's capture |
| #562 | OPEN | m17 pins: per-statement coverage beyond the name and anchor unit | 13 | **deferred** to a later testing milestone; outside the written unit |

#580 is not carried: it is closed, and accepted under item 3's amendment. Its close reason is finding F2.

## Findings

| # | Finding | Where | Disposition |
|---|---|---|---|
| F1 | Ten register cells cited reds stale since `fc7c0586` without naming them stale, so item 11's text did not hold on the audited head. The 0.22.1 release changed `Exports.cs`, a subject of 27 M18 sites, only in `VersionNumber`; the cells were last written in 18.11, before it. Three corroborating reds on those sites, `18.1-L1`, `M5` and `18.9-R4`, were stale and unnamed too | `docs/pin-standard.md:525`, `:533`–`:536`, `:543`, `:544`, `:625`, `:628`, `:631`, `:638` | **Fixed in this phase:** each cell names its stale reds and cites the 18.13 record §3 re-runs; the three corroborating reds are named stale, not re-run |
| F2 | #580 reads `CLOSED COMPLETED`, closed by #721's "Closes #580", but 18.12 accepted the residual and did not fix it. A reader of the issue takes it as fixed | #580; #721's PR body, line 3 | **For the owner, not changed here.** Proposal: set #580's close reason to "not planned" and comment that it is accepted, pointing to the 18.12 record §7 |
| F3 | 18.11 §10 quoted `281, 290` for `grep -n -- '-nodeReuse:false' … \| head -2`; the command prints `275, 281` on `da6f8c1d` and HEAD | `docs/plans/2026-11-15-milestone-18-audit.md:319` | **Fixed in this report,** item 10 quotes the real output. The 18.11 audit is left as written, as history |
| F4 | 18.11 §12 diffed `src/BlazorNative.Renderer/FrameEncoder.cs`, which does not exist, so its check of the frame encoder was vacuous | `docs/plans/2026-11-15-milestone-18-audit.md:382` | **Fixed in this report:** item 12 checks every path exists and diffs `src/BlazorNative.Runtime/FrameEncoder.cs` over `5af84d3d..HEAD`, empty. The 18.11 audit is left as history |

No GitHub issue is filed: F1, F3 and F4 are fixed here, and F2 is one state change on an existing issue that only the owner should make.

## What 18.13 taught

- **A release bump stales reds.** The 0.22.1 release changed one string in `Exports.cs` and made 27 sites and ten register cells stale under the mechanical rule. A milestone that stays open across a release has to re-check its cells, not only its sites.
- **A file list can miss the file a pin is named for.** 18.11's own rule took that file, yet three of its commands left out `Exports.cs`, and the re-audit copied them until the final review. Check each list against the pin's register row, not only against the mutated file.
- **A diff of a missing path is silently empty.** `git diff --stat A B -- <path>` prints nothing for a path that exists at neither end. Check that each path exists before reading an empty diff as "unchanged".

## Verdict

**PASS WITH FINDINGS.** No item is NOT MET, and two are MET NARROWLY, so PASS is out of reach and FAIL does not apply. [The sample](#the-sample) reproduced 4 of 4, and the 27 stale sites redded 27 of 27.

- **Eleven items are MET:** 1, 2, 4, 5, 6, 7, 8, 9, 10, 11 and 12. Item 11 is MET after F1's fix in this phase.
- **Two are MET NARROWLY:**
  - **Item 3,** graded against the owner's 2026-11-16 amendment. #551, #564 and #444 hold literally. #453 holds through #580, accepted and not fixed. #407 holds under the amendment: a phase spent hunting, a named failure, and a capture that ran on the graded lane, with #718 open. The narrowings are #718 open, #580 accepted, one sample per lane, and the capture's sampler with no sampler-free control. 18.11 graded it NOT MET, before the amendment.
  - **Item 13,** the three register rows written in 18.11, read on intent by the owner's 2026-11-15 decision.
- **The findings:** F1, fixed; F2, for the owner; F3 and F4, corrected here.

**Next:** per the design, the Audit History row is recorded and `complete-milestone` runs as its own PR, with no tag, after this phase's PR merges.
