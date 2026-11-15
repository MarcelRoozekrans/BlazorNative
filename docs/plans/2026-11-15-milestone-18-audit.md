# Milestone 18 audit — Acknowledged Bugs

**Date:** 2026-11-15
**Phase:** 18.11, [design](../superpowers/specs/2026-11-14-phase-18.11-design.md), [plan](../superpowers/plans/2026-11-14-phase-18.11-audit.md)
**Audited:** `origin/main` at **`da6f8c1d`**, which is #712, the 18.10 squash, at 2026-11-14T20:35:47+01:00.
- The M18 range is `5af84d3d..da6f8c1d`, where `5af84d3d` is #573, the milestone start: 51 commits.
- The audit branch `feat/18.11-audit` differs from `da6f8c1d` only under `docs/`.
- **The graded lanes** are the PR #713 runs on `93f886e7`, recorded in [the 18.11 record](2026-11-14-phase-18.11-record.md) §8, per the design's call "Which head is graded". This report is committed after them, so the head that carries it is a later docs-only commit; its own lanes are recorded in the PR body and STATE.md, not here.

**Host for local runs:** Debian 13 x64, SDKs 10.0.403 and 11.0.100, `pwsh` from `~/.dotnet/tools`.
**Red standard:** RS1, from [the 17.7 record](2026-10-17-phase-17.7-record.md) §4 "Rulings".
**Site unit for item 13:** the **written** rule, `docs/pin-standard.md` "Grading a pin: sites and reds", `:679` on HEAD. Nothing broader is graded here; that is #562.
**Precedent:** [the 17.12 re-audit](2026-10-22-milestone-17-reaudit-3.md), verdict PASS WITH FINDINGS, which this report mirrors.
- Its verdicts are a list of things to re-check, not evidence.
- Every number below was measured on the audited code, with the command shown beside it.

**Verdict:** **PASS WITH FINDINGS**: 11 MET, 2 MET NARROWLY, none NOT MET. See [the verdict](#verdict). It is provisional on [the sample re-run](#sample-re-run), which the controller fills in.
- Item 3, the flakes, is MET NARROWLY: every lane is green at attempt 1 with no signature hit, but that is one sample per lane, and two causes are only partly removed, #407's trigger and #453's cold start, with #580 open.
- Item 13, new pins, is MET NARROWLY: every graded site has a fresh, quoted RS1 red, but three register rows were written in 18.11 and not in the phase that added or altered the pin.
- Two issues are filed: **#716**, the `[#444]` trace that prints `NO completion` on every passing show since 18.10, and **#717**, the pre-M18 cells that now name their stale reds but have none fresh.

## Scorecard

| # | DoD item | Verdict |
|---|---|---|
| 1 | All planned phases complete | **MET**: 18.1–18.10 complete on `origin/main`; 18.11 is this audit. Every issue M18 closes is `CLOSED` except #568, which this PR closes |
| 2 | All tests passing, both device lanes, `headSha` compared | **MET**: on `93f886e7`, every run attempt 1 and on the PR head: .NET 2828/0/0, JVM 210/0, Android 273/0, iOS 307/0. Local .NET 2828/0/0, both TFMs |
| 3 | The CI flakes are fixed at their cause | **MET NARROWLY**: each fix is at its cause and pinned, no bound was raised, and every lane is clean at attempt 1. One clean sample per lane cannot prove an intermittent flake gone; #407's trigger is unproven and #453 leaves #580 |
| 4 | #500 is fixed as decided | **MET** |
| 5 | #506 is fixed as decided | **MET** |
| 6 | #511 is fixed, with #601 | **MET**; #621 is adjacent |
| 7 | #522 is fixed as decided | **MET** |
| 8 | #543 and #544 are fixed | **MET** |
| 9 | #401 and #396 are fixed | **MET** |
| 10 | #530 is fixed | **MET** |
| 11 | #568 is fixed: no register cell cites a stale red without naming it stale | **MET**, on the item's text. The residue, pre-M18 sites whose reds are all stale, is filed as #717 |
| 12 | No ABI change | **MET**: the same 10 exports with the same signatures, the same 80-byte struct, the C header and `wire-vocabulary.json` unchanged |
| 13 | Every new pin conforms to the grading rule | **MET NARROWLY**: 108 pins, 339 sites, every site with a fresh RS1 red and a quoted message. Three register rows were written in 18.11, not in the phase that added or altered the pin |

---

## The audited code

```bash
git rev-parse origin/main                                  # da6f8c1d05a5ff7c28f240de2023bf73579da720
git merge-base --is-ancestor da6f8c1d HEAD && echo ancestor # ancestor
git diff --stat da6f8c1d HEAD -- ':!docs'                  # (empty): the branch is docs-only against da6f8c1d
git diff --stat da6f8c1d 93f886e7 -- ':!docs'              # (empty): the graded head runs da6f8c1d's code
git log --format='%h %s' 5af84d3d..da6f8c1d | wc -l        # 51
```

**No product, test, template, script or workflow file differs from `da6f8c1d`** on this branch or on the graded head. So the lanes of `93f886e7` grade `da6f8c1d`'s code.

## 1. All planned phases complete — **MET**

> All planned phases complete

```bash
git show origin/main:docs/planning/ROADMAP.md | grep -n "Phase 18\.[0-9]*:"
#   3974:#### Phase 18.1 … [status: complete]    …    4067:#### Phase 18.10: iOS show completes when posted (#444) [status: complete]
#   4074:#### Phase 18.11: Audit and close (#568) [status: pending]
grep -n "Phase 18.11:" docs/planning/ROADMAP.md      # on this branch
#   4074:#### Phase 18.11: Audit and close (#568) [status: active]
```

**18.1–18.10 read `complete` on `da6f8c1d`,** and `MILESTONE.md`'s phase list agrees. **18.11 is this audit.**

Every issue M18 set out to close:

```bash
for i in 551 564 453 444 407 500 506 511 601 522 543 544 401 396 530 568; do
  gh issue view $i --json number,state,closedAt,closedByPullRequestsReferences; done
#   #551, #453, #407        CLOSED 2026-10-28  by #584
#   #564                    CLOSED 2026-11-14  by #584, #710   (reopened 2026-11-05, closed by 18.9)
#   #444                    CLOSED 2026-11-14  by #712
#   #500                    CLOSED 2026-10-31  by #596, #597
#   #506                    CLOSED 2026-10-31  by #599, #605
#   #511, #601              CLOSED 2026-11-03  by #605
#   #522                    CLOSED 2026-11-10  by #634
#   #543, #544              CLOSED 2026-11-11  by #659
#   #401, #396              CLOSED 2026-11-13  by #691
#   #530                    CLOSED 2026-11-14  by #707
#   #568                    OPEN                 to be closed by this phase's PR
```

**Grade: MET.**

## 2. All tests passing, both device lanes, `headSha` compared — **MET**

> All tests passing, .NET, JVM **and both device lanes**, with each lane's `headSha` compared against the PR head

```bash
gh run list --commit 93f886e7b227ed11925314d2acea8435bb82e4b0 --json databaseId,name,headSha,attempt,conclusion,event
#   43679768180 ci                    93f886e7 attempt 1 pull_request success
#   43679768259 android-instrumented  93f886e7 attempt 1 pull_request success
#   43679768165 ios                   93f886e7 attempt 1 pull_request success
#   43679768502 baseline-checks       93f886e7 attempt 1 pull_request success
#   43679768169 commitlint            93f886e7 attempt 1 pull_request success
```

| lane | run | attempt | `headSha` | result | quoted line, from the 18.11 record §8 |
|---|---|---|---|---|---|
| .NET, CI | `ci.yml` `build-test` [43679768180](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/43679768180), PR #713 | 1 | `93f886e7` | **2828 / 0 / 0** | `dotnet test totals across 6 projects: passed=2828 skipped=0 failed=0` |
| JVM, CI | same run | 1 | `93f886e7` | **210 / 0** | `JVM test totals across 37 suites: tests=210 failures=0 errors=0 skipped=0` |
| Android | `android-instrumented.yml` [43679768259](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/43679768259) | 1 | `93f886e7` | **273 / 0** | `Instrumented totals across 1 suite(s): tests=273 failures=0 errors=0 skipped=0` |
| iOS | `ios.yml` [43679768165](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/43679768165) | 1 | `93f886e7` | **307 / 0** | `Executed 307 tests, with 0 failures (0 unexpected)` |
| .NET, local | this worktree at `3a4ff8ca`, code equal to `da6f8c1d` | 1 | — | **2828 / 0 / 0** | per TFM: Analyzers 27, Renderer 147, Runtime 1240; exit 0 |

```bash
dotnet test BlazorNative.sln -c Release --nologo          # one run, on a clean tree, before any edit
#   Passed!  - Failed: 0, Passed: 1240, Skipped: 0, Total: 1240 - BlazorNative.Runtime.Tests.dll (net10.0)
#   Passed!  - Failed: 0, Passed: 1240, Skipped: 0, Total: 1240 - BlazorNative.Runtime.Tests.dll (net11.0)
#   … Analyzers 27 and Renderer 147 on each TFM; exit 0
```

- **Both device lanes carry the PR head's `headSha`,** so neither is a stale lane.
- **The expected counts** are the ones the gates assert: 2828, 210, 273 and 307. Each was found, not assumed.
- **The audit re-read the run list itself,** and fetched the iOS log, which still has `Executed 307 tests, with 0 failures` twice, the run line and the bundle summary.

**Grade: MET.**

## 3. The CI flakes are fixed at their cause — **MET NARROWLY**

> **The CI flakes are fixed at their cause:** #551, #564, #453, #444 and #407. … Each flake's cause is found and fixed. Each fix is pinned where a pin can hold it. No fix raises a bound to hide a slow path. The re-audit's own CI runs hit none of these signatures. Attempt 1 is green on every lane.

### 3a. Cause, fix and pin, per flake

| Flake | Cause, as recorded | Fix | Pin | Residue |
|---|---|---|---|---|
| #551 | The suite's own blocking waits held pool threads, so a positive control whose frame crosses the pool starved. 18.1 record §3 | `5a7ab54a`: `RunBounded` refuses a pool thread, `OffPool`, `IAsyncLifetime` fixtures, positive controls off the pool | `PoolStarvationTests` and `GeneratorRunTests.RunBounded_OnAPoolThread_…` | The product's two pool hops are #575, open, an owner decision. #603, a different `Canceled` outcome once, open |
| #564 | 18.1: a synchronous `Console.Error` write blocked the render thread. 18.9: the other-thread probe's synchronous part waited on the pool's queue delay. 18.9 record §1 | `5a7ab54a`, the `BnLog` queue; `a1bacd6b`, the probe begins on a dedicated thread | 18.1's `BnLogTests` and leak pins; `DispatchBoundPoolTests`, `TearDownOverrunTests` | #711, about 25 facts of #551's shape, open, filed by 18.9 by owner decision |
| #453 | A cold `LAContext()` start paid inside the tap's 30 s armed-reply wait. 18.1 record §4 | `5a7ab54a`: `warmUpContext()` at boot, a named warm-up step | `BnBiometricsTests.testBootWarmsOneLAContextOffTheBootPathBeforeTheFirstBiometricRequest` | **#580, open:** a cold start over about 40 s still fails the named step |
| #444 | `show` completed inside `add`'s completion, which waits on SpringBoard's `willPresent`, once 11.25 s late. 18.10 record §1 | `da6f8c1d`: `show` completes `Granted` when posted, as Android's does | `testShowCompletesGrantedWhilePresentationIsHeld` and the late-error pins | The `[#444]` trace is now stale, §3d, **#716** |
| #407 | `MainActivity` relaunched between the echo check and the image poll. 18.1 record §5 | `5a7ab54a`: `pollUntilOnSameActivity`, the settle step | `PollAndroidTest.a_poll_on_a_recreated_activity_fails_naming_it`, `BnCameraAndroidTest.a_recreation_before_the_image_poll_…` | **The trigger is unproven.** The record says "What 18.1 guarantees is the named failure, not a removed cause" |

### 3b. No bound was raised

```bash
for r in 5af84d3d da6f8c1d; do
  git show $r:tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs | grep -nE 'ExportBudget\s*=|TimeSpan Budget\s*='
  git show $r:tests/BlazorNative.Runtime.Tests/ShutdownQuiescenceTests.cs | grep -nE 'Budget\s*='
  git show $r:src/BlazorNative.Apple/BnHostTests/BnNotificationsTests.swift | grep -n 'func pollUntil'; done
#   both ends: ExportBudget 1 s, Budget 10 s, ShutdownQuiescenceTests Budget 10 s, pollUntil(deadline: 10)
git diff 5af84d3d da6f8c1d -- .github/workflows | grep -E '^[-+].*timeout-minutes'
#   +timeout-minutes: 15   +timeout-minutes: 20   (new job limits in the release and baseline workflows; none removed or raised)
git diff 5af84d3d da6f8c1d -- src/BlazorNative.Jni/build.gradle.kts | grep -E '^[-+].*(timeout|Timeout)'
#   + junit.jupiter.execution.timeout.default "120 s", thread mode SEPARATE_THREAD, task timeout 25 min: new, by 18.6 for #543
```

- **The biometrics boot tests keep their 30 s armed-reply waits** at both ends: `awaitArmedReply(…, deadline: 30)` at `BnBiometricsTests.swift:336` and `:353` on `5af84d3d`, `:340` and `:358` on `da6f8c1d`, and the unit test's `deadline: 10` at `:250` and `:252`. The warm-up step's 30 s, and `:560`'s 30 s in 18.1's new pin, are new waits of their own.
- **The Android camera polls keep 15 s and 10 s,** and the settle step's 300 s is a ceiling on a new wait, per the 18.1 record §5.
- Each record states the same, and the diffs agree: every bound M18 added is new, and none was raised.

### 3c. The graded runs hit no signature

From the 18.11 record §8, which grepped each run's full log for each signature:

| Signature | `ci` 43679768180 | `android-instrumented` 43679768259 | `ios` 43679768165 |
|---|---|---|---|
| #551 `completing the held call delivered no frame` | 0 | n/a | n/a |
| #564 `did not return within 1s`, `The collection contained 2 items` | 0, 0 | n/a | n/a |
| #453 `no reply armed within`, `A cold LocalAuthentication start` | n/a | n/a | 0, 0 |
| #444 `show never completed Granted` | n/a | n/a | 0 |
| #407 `never loaded into the BnImage\|Coil did not load` | n/a | 0 | n/a |

- **Every run is attempt 1,** and no retry marker appears in any log.
- **The local .NET run** of item 2 has no #551 or #564 message either: `grep -c 'did not return within 1s\|completing the held call delivered no frame'` gives 0.

### 3d. The `[#444]` trace is stale since 18.10 — finding, #716

The 18.11 record §8 found four `[#444] … NO completion` lines in the passing iOS run and left open what they mean. This audit examined it.

```bash
gh run view 43679768165 --log | grep -n 'NO completion'
#   [#444] unit show: captured statuses [0]; adds [add#5 '444004' called 05:21:30.203, NO completion]     logged 05:21:30.259
#   [#444] boot show: echo status:Granted, complete rc 0; adds [add#7 '7' called 05:21:30.380, NO completion]  logged 05:21:30.380
```

- **Why it prints `NO completion`.** Since `da6f8c1d`, `BnNotifications.runAction` calls `add` and then completes `Granted` at once, `BnNotifications.swift:463`–`:478`. The tests call `diagnose` as soon as the poll sees `Granted`, `BnNotificationsTests.swift:162` and `:444`. The add's completion follows `willPresent`, so at that moment it has almost never arrived: 56 ms after the call for the unit show, the same millisecond for the booted show.
- **Before 18.10** a show completed inside the add's completion, so the passing trace always had `completed … after N ms`. The 18.1 record §6 quotes 12 to 34 ms from such traces.
- **What is now false.** `BnNotificationsTests.swift:560`–`:563` says the trace "says whether the center ever called back" and that "A passing show logs the same line, so the latency is on record"; `:583` repeats the latency claim. No latency is logged for a passing show any more. `BnNotifications.swift:179` prints `NO completion` for a completion that has not arrived *yet*, which reads the same as one that never comes.
- **Why it matters.** A line that reads like a failure on every green run gets ignored, and it would read the same on a real lost completion.
- **Not a doc fix in this phase's surface:** the stale text is in test and product code. **Filed as #716,** with the three options and a proposal.

It does not narrow this item: no assertion depends on the trace, and #444's fix is pinned by 18.10's held-presentation pin. It is recorded here because it is #444's diagnostic.

### 3e. The grade

**MET NARROWLY.** Every bullet holds on its text: each cause is found and fixed, each fix is pinned, no bound was raised, and the graded runs are green at attempt 1 with no signature hit. The narrowing:
- **One sample per lane.** The Windows `build-test` ran once. #551, #564, #453, #444 and #407 were intermittent, at rates as low as about 1 in 10 runs, so one clean run cannot show they cannot recur. It shows only that this head did not hit them.
- **Two causes are only partly removed.** #407's trigger is unproven: 18.1 guarantees the failure is named, not that the relaunch cannot happen. #453 leaves #580: a cold start over about 40 s still fails the named warm-up step.
- **Adjacent and open,** each already filed: #575 and #711 for the pool hops, #603 for a different shutdown outcome. They are carried debt, below.

## 4. #500 is fixed as decided — **MET**

`#500`: `CLOSED` by #596 and #597.

```bash
grep -n 'public void RunAfterDispatch\|18.2 (#500)' src/BlazorNative.Renderer/NativeRenderer.cs | head -10
#   118, 314, 320, 333, 344, 358, 373, 401, 473, 481      (48 lines without head; 320 is public void RunAfterDispatch(Action action))
grep -n 'public void AnActionQueuedByAnInlineContinuation\|public void ATaggedReRender' tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs
#   1386 AnActionQueuedByAnInlineContinuation_IsItsOwnDispatchs_NotTheWindows
#   1413 ATaggedReRender_IsItsOwnDispatchs_NotTheWindows      1417 …_InStrictModeToo      1497 ATaggedReRendersFault_LeavesTheWindowsOwnFault_Rc2
```

- **A's re-render and A's `RunAfterDispatch` action** are charged to A: `:1413` and `:1386`.
- **The window keeps its own rc:** `:1497`.
- **Mutation-proven:** all 87 18.2 sites have fresh reds in the 18.11 record §3, item 13.

**Grade: MET.**

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

- **A delete and a plain set wait behind an auth set,** and another key is not blocked: `:327`, `:347`, `:386`.
- **iOS has the same hazard and the same fix,** `BnSecureStorage.swift:91` and `:173`, pinned at `:542` and `:651`.
- **Every site has a fresh red,** Android in the 18.11 record §5, iOS in §6.

**Grade: MET.** #600, a biometrics authenticate that can hang when its Activity finishes, is adjacent: it is the biometrics op, not secure storage's order.

## 6. #511 is fixed, with #601 — **MET**

`#511` and `#601`: `CLOSED` by #605.

```bash
grep -n '18.4 (#511)' src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt   # 168, 174, 178, 421, 428, 524
for t in RecreationDuringBootAndroidTest SecureHoldAcrossRecreationAndroidTest StartLockTest RetiredLaneDispatchTest; do
  grep -c '@Test' $(git ls-files | grep "/$t.kt"); done                                               # 2, 1, 1, 1
grep -n 'one per process\|process-wide' src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt   # 277, 283, 426, 2109
```

- **The device pin:** `RecreationDuringBootAndroidTest.a_recreation_while_the_first_boot_is_held_shows_the_new_ac…`, with fresh reds in the 18.11 record §5 batch B.
- **#601:** the secure-storage hold is process-wide, `AndroidShellBridge.kt:277`–`:283`, with the hold's code at `:426` and `:2109`, pinned by `SecureHoldAcrossRecreationAndroidTest` and the 18.4 two-bridge facts.

**Grade: MET.** #621, a second instance's final destroy denying another instance's orphaned prompt, is adjacent: it is a multi-instance edge the DoD bullet does not claim.

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
- **All 27 18.5 sites were fresh on HEAD,** from 18.5's final re-run on `b012cf93`, the 18.11 record §2.

**Grade: MET.**

## 8. #543 and #544 are fixed — **MET**

`#543` and `#544`: `CLOSED` by #659.

```bash
grep -n 'junit.jupiter.execution.timeout\|timeout.set' src/BlazorNative.Jni/build.gradle.kts
#   274 junit.jupiter.execution.timeout.default "120 s"   275 thread mode SEPARATE_THREAD   276 timeout.set(Duration.ofMinutes(25))
grep -n 'fun pressHandledBack' src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/Poll.kt     # 99
```

- **#543:** `BackNoticeTest`'s blocking back is joined with a 10 s bound and fails by name, `:362`, and the per-test and task timeouts hold the step. Fresh reds for `:362` and `:370` are 18.11's J4 and J5, the 18.11 record §4.
- **#544:** the back tests check in their own turn through `pressHandledBack`. 18.6's S4, `finish()` after the back, reds the five `BackAndroidTest` tests plus two more, 7 of 271, run twice, the 18.6 record §3; the 18.11 record §2 finds it fresh.

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

- **#401:** one manifest, generated into both suites, and a twin pin per shell that every vector is forwarded whatever the shell level.
- **#396:** iOS checks enrolment first, as Android does, `BnSecureStorage.swift:22`–`:24`, `:116` and `:392`–`:411`; pinned by the 18.7 `BnSecureStorageTests` and `SecureStorageAndroidTest` tests.
- **All 26 18.7 sites are fresh,** the 18.11 record §2.

**Grade: MET.**

## 10. #530 is fixed — **MET**

`#530`: `CLOSED` by #707.

```bash
grep -n -- '-nodeReuse:false' tests/BlazorNative.Runtime.Tests/GeneratorRunTests.cs | head -2       # 281, 290
grep -n '#530' tests/BlazorNative.Runtime.Tests/BoundedScript.cs                                    # 103, 111
```

- **The holder** is a reused MSBuild worker node; node reuse off is the source fix, pinned by `GenerateReference_RunsEveryMSBuildCallWithNodeReuseOff`. Its altered message has a fresh red, the 18.11 record §2 row 322.
- **The 17.5 bound still names it,** `BoundedScript.cs:103` and `:111`.

**Grade: MET.**

## 11. #568 is fixed — **MET**

> **#568 is fixed.** No register cell cites a stale red without naming it stale.

**How it was checked.** The 18.11 record §7 ran the staleness diff on **every** red each register cell cites, not only #568's three: 163 rows over the 108 cells that cite a red. 48 rows are fresh. 94 print output and are stale, and 18 have no resolvable commit and count as stale. 1, `BnGeolocationTests`' upper-bound row, is empty only from the squash and counts as stale with its unresolvable partner row. The 2 `headSha` rows of `HostEventAndroidTest` print output but are not a red's commit, so they count as neither; that test's red is the fresh `c52ffac7` row. 48 + 94 + 18 + 1 + 2 = 163, the record §7 "Count", `:3043`. Every stale row is named stale in its cell.

**Re-checked here.** The cells changed after the sweep's HEAD `afcbdf65`, in `eb3d02e9`, `ed7efa92`, `7709dce2` and `b1b2ac99`, but no code did:

```bash
git diff --stat afcbdf65 HEAD -- ':!docs'                                            # (empty)
grep -c 'Stale, named in 18.11' docs/pin-standard.md                                  # 39
# #568's three cells, their fresh reds as #568 named them, against HEAD:
AT=src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell
for c in 07229aa2 36b2716f; do git diff --shortstat $c HEAD -- $AT/AndroidHostCallContractTest.kt $AT/RecreationAndroidTest.kt \
  src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/{AndroidShellBridge,MainActivity}.kt \
  src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/BlazorNativeRuntime.kt; done
#   5 files changed, 1147 insertions(+), 151 deletions(-)    both: stale
```

- **#568's three cells,** PS `:601`, `:602` and `:607`, each name the stale red #568 named, and say that the fresh red #568 told them to cite, 17.9 §3 D2 and the 17.8 re-audit §11d D1 and D4, "has gone stale since #568 was filed".
- **#568's fourth cell,** the 17.5 held-pipe entries, was fixed in 18.8; its re-runs on `6c17145f` are fresh.
- **A sample of the sweep, re-run independently:** the `RecreationTests` cell, PS `:605`, cites reds up to the 17.11 squash; `git diff --shortstat a294e924 HEAD -- RecreationTests.cs Exports.cs` gives 1 file changed, and the cell carries "Stale, named in 18.11". `BnDiagnosticsTest`'s reds on `4bf73026` and `9371c47d` diff empty against both copies of `BnDiagnostics.kt` and the test, and its cell, `:615`, names nothing stale. Both agree with the sweep.

**Grade: MET,** on the item's text. **Not hidden:** the three sites #568 is about, and the sites of the 39 pre-M18 cells that newly name a stale red, have no fresh red now. They were not re-run, by the owner's decision of 2026-11-14 that M18 re-runs only M18 pins. A named stale red is honest, but it proves nothing about HEAD, so this is **filed as #717** for a decision on re-running them.

## 12. No ABI change — **MET**

Every command was run over `5af84d3d..da6f8c1d`.

```bash
# the exports, both ends
for r in 5af84d3d da6f8c1d; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -c '^\s*\[UnmanagedCallersOnly(EntryPoint'; done
#   10
#   10
for r in 5af84d3d da6f8c1d; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -o '\[UnmanagedCallersOnly(EntryPoint = "[a-z_]*"' | sort | md5sum; done
#   52ec8c5e37245dd1522729412c4b3c2a  -      (both)
for r in 5af84d3d da6f8c1d; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -A2 'UnmanagedCallersOnly(EntryPoint' \
  | grep -E 'static .*\(' | sed 's/^\s*//' | md5sum; done
#   f1ff67805e94e3a1c80ba630f0b4eb61  -      (both: the 10 signatures are unchanged)
git diff --stat 5af84d3d da6f8c1d -- src/BlazorNative.Runtime/Exports.cs
#   1 file changed, 62 insertions(+), 9 deletions(-)      bodies only: 18.1's shutdown drain, 18.2's queued-work attribution
git grep -n 'UnmanagedCallersOnly(EntryPoint' da6f8c1d -- 'src/*.cs' | grep -v Exports.cs
#   only InteropBoundaryAnalyzer.cs:60, the analyzer's message text

# the 80-byte struct
git diff --stat 5af84d3d da6f8c1d -- src/BlazorNative.Runtime/BridgeProtocolNative.cs                          # (empty)
for r in 5af84d3d da6f8c1d; do git show $r:src/BlazorNative.Runtime/BridgeProtocolNative.cs \
  | awk '/public struct BlazorNativeBridgeCallbacks/,/^}/' | grep -c "public IntPtr"; done                         # 10, 10
git show da6f8c1d:src/BlazorNative.Runtime/BridgeProtocolNative.cs | grep -n "80 bytes"                           # :91, :96
git diff --stat 5af84d3d da6f8c1d -- src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeBindings.kt  # (empty)
git diff --stat 5af84d3d da6f8c1d -- src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h                        # (empty)

# the wire
git diff --stat 5af84d3d da6f8c1d -- src/wire-vocabulary.json                                                    # (empty)
git diff --stat 5af84d3d da6f8c1d -- '*.g.cs' '*.g.kt' '*.g.swift' '*.g.h' src/BlazorNative.Renderer/FrameEncoder.cs
#   BnHostTests/BnStderrPumpVectors.g.swift | 35 +   shell/BnStderrPumpVectors.g.kt | 37 +   (added by 18.7, ff39e209)
```

- **The exports:** the same 10 names and the same 10 signatures at both ends. `Exports.cs` changed only inside method bodies.
- **The struct:** the same 80-byte struct of 10 `IntPtr` fields, and the file is unchanged.
- **The C header and `NativeBindings.kt`:** unchanged.
- **The wire:** `wire-vocabulary.json` and `FrameEncoder.cs` are unchanged. The only new generated files are 18.7's stderr-pump test vectors. Their header names `src/stderr-pump-vectors.json` as the source and `tools/BlazorNative.WireGen` as the generator; they are test vectors for the two pump suites, not wire, so no wire addition bypassed `wire-vocabulary.json`.

**Grade: MET.**

---

## 13. Every new pin conforms to the grading rule — **MET NARROWLY**

> **Every new pin conforms to the grading rule.**
> - Its sites, meaning the labelled anchors and name claims, have fresh reds that quote their messages.
> - Its register cell is written in the phase that adds it.
> - Staleness is checked against each red's own commit.

**Shorthand:** "the record" is [the 18.11 record](2026-11-14-phase-18.11-record.md); PS is `docs/pin-standard.md`, read on HEAD.

### 13a. The population, from git — 108 pins, 339 sites

The record §2 derived it from the ten squashes' test diffs, then compared it with the phase records and listed 17 disagreements, each resolved in the table or by leaving the site out with its reason:

| Phase | Squash | Pins | Sites |
|---|---|---|---|
| 18.1 | `5a7ab54a` | 22 | 61 |
| 18.2 | `d246778f` | 19 | 87 |
| 18.3 | `2eb3f22d` | 21 | 76 |
| 18.4 | `0b430432` | 10 | 38 |
| 18.5 | `2cc6801d` | 11 | 27 |
| 18.6 | `d7fdf937` | 5 | 6 |
| 18.7 | `ff39e209` | 12 | 26 |
| 18.8 | `d9bf0711` | 1 | 1 |
| 18.9 | `a1bacd6b` | 3 | 9 |
| 18.10 | `da6f8c1d` | 4 | 8 |
| **All** | | **108** | **339** |

### 13b. Fresh reds with quoted messages — every site

**Staleness against each red's own commit.** The record §2 ran `git diff --stat <red commit> HEAD -- <test file> <subject files>` for every site, with the red's commit defined as the tree the mutation was applied to. It found 108 fresh, 220 stale and 11 with no red.

**The 231 stale or unresolvable sites were re-run,** one mutation per site, each failing at its own line with its own message, quoted in the row:

| Route | Re-run | Red | No red | Where |
|---|---|---|---|---|
| .NET | 115 | 115 | 0 | record §3, batches A and B, both TFMs, locally |
| JVM | 5 | 5 | 0 | record §4, scratch PR #714, `ci.yml` attempt 1 |
| Android | 80 | 80 | 0 | record §5, batches A and B, `android-instrumented.yml` attempt 1 |
| iOS | 31 | 31 | 0 | record §6, three `ios.yml` runs, attempt 1, one mutation per xcodebuild invocation |

**The re-runs are fresh on HEAD.** Each batch's base commit has the audited code:

```bash
for c in ee5a8d1c e39e2260 9f01a127 d5b93d0f 35077687 7c7b7068 f7b8658a; do
  git diff --shortstat $c HEAD -- src tests templates samples scripts tools .github; done
#   (empty, all seven)
```

**The 108 fresh sites** cite reds from the phase records. This audit sampled seven, one per phase where possible, and found each site's own message quoted at its own line in the cited record:

| Row | Site | Red | Quoted in |
|---|---|---|---|
| 1 | `BnLogTests.cs:271` | 18.1 §2 M1, `28f02417` | 18.1 record `:119`, "BnLog.Error did not return within 1 s while stderr was blocked: …" |
| 255 | `StartLockTest.kt:81` | 18.4 fix-wave J1, J2 | 18.4 record `:440`, "a second start() entered while the first was inside it: two threads in the .NET session" |
| 267 | `SlowHandlerWarningTests.cs:841` | 18.5 F1, `b012cf93` | 18.5 record `:353` and `:560`, "anchor: the slow :after must remove its own input, …" |
| 296 | `BnStderrPumpTwinTest.kt:113` | 18.7 R3b | 18.7 record `:115` to `:138`, "… shell level 2 was not applied: …" |
| 305 | `BnSecureStorageTests.swift:307` | 18.7 R13, `24814193` | 18.7 record `:163` to `:165`, "a refused auth-bound set must store nothing: a plain get must be NotFound, …" at `:307` |
| 329 | `TearDownOverrunTests.cs:65` | 18.9 §5, `a0cad0e7` | the 18.9 record §5; `git diff --shortstat a0cad0e7 HEAD -- TearDownOverrunTests.cs FaultNoticeTests.cs HostSession.cs` empty |
| 1 to 8 | `BnLogTests.cs` | `28f02417` | `git diff --shortstat 28f02417 HEAD -- BnLogTests.cs BnLogQueue.cs BnLog.cs` empty |

That is a sample, not a sweep; 17.12 found nine fresh sites recorded by line only, and the same could hide here. [The sample re-run](#sample-re-run) is the independent check on the whole.

**This sub-bullet is met:** 339 of 339 sites have a fresh RS1 red with a quoted message, under the written unit.

### 13c. Register cells written in the phase that adds them — three were not

```bash
for s in 5a7ab54a d246778f 2eb3f22d 0b430432 2cc6801d d7fdf937 ff39e209 d9bf0711 a1bacd6b da6f8c1d; do
  git show $s -- docs/pin-standard.md | grep -c '^+[^+]'; done
#   13, 42, 2, 11, 4, 5, 17, 3, 4, 2      every M18 squash wrote register lines
git show a1bacd6b:docs/pin-standard.md | grep -c TearDownOverrun                      # 0
git show 5a7ab54a:docs/pin-standard.md | grep -c testShowGrantRoundTrips              # 0
git show 5a7ab54a:docs/pin-standard.md | grep -n take_photo_composes | cut -c1-60
#   575:| Instrumented `PollAndroidTest.a_poll_on_a_recreated_ac     (count 1)
```

The camera pin's one hit at `5a7ab54a` is the 18.1 #407 row, `:575` at that squash, which names `take_photo_composes_a_captured_file_into_a_measured_bnimage` as its Rule 3 positive control. It is a mention inside another pin's row, not a cell of its own, which is the "18.1 called it a positive control" below.

**How the 105 were checked.** For each of the 108 pins in the record §2 table, phase taken from its row range, a script looked for the pin's method name, then its class name, in `git show <phase squash>:docs/pin-standard.md`:

```bash
# rows: §2 table lines '| n | pin | …', with 〃 resolved to the pin above; phase by row: 1–61 18.1, 62–148 18.2,
# 149–224 18.3, 225–262 18.4, 263–289 18.5, 290–295 18.6, 296–321 18.7, 322 18.8, 323–331 18.9, 332–339 18.10
for each pin: txt=$(git show <squash>:docs/pin-standard.md); method in txt || class in txt || print pin
#   prints 2: BnNotificationsTests.testShowGrantRoundTrips… (18.1) and TearDownOverrunTests.… (18.9)
#   50 more are found by class name only: their phase's row names the class and a test count, e.g. "(18.3, 14 tests, #506)", not each method
```

It is a presence check: it finds a pin with no row at all, and the camera pin passes it only through the #407 row's mention, so it was read by hand. The three pins below were confirmed by reading their phase's register; 105 of the 108 pins have a cell from their own phase. **Three do not,** and 18.11 added rows for them, PS `:638` to `:640`, each saying "row added in 18.11":

| Pin | Added or altered by | Why no cell | Now |
|---|---|---|---|
| `TearDownOverrunTests.ADispatchThatOverranAndIsStillRunning_DoesNotHangTearDown_AndTheNextSessionWorks` | 18.9's review fix wave, a **new pin** | 18.9 wrote none | PS `:638`, citing the record §3 batch B rows 328 to 331 |
| `BnNotificationsTests.testShowGrantRoundTripsThroughTheRealHostCallComplete`, `:446` | 18.1 rewrote its assertion and message | 18.1 §6 said "No register rows and no pins" | PS `:639`, citing the record §6 row 54 |
| `BnCameraAndroidTest.take_photo_composes_a_captured_file_into_a_measured_bnimage`, `:257` | 18.1 moved its assertion into `assertPhotoLoads` | 18.1 called it a positive control | PS `:640`, citing the record §5 row 61 |

**This sub-bullet is not met on its words** for these three, and cannot be made met: the phase that adds a pin is past. The first is a genuinely new pin; the other two are pre-M18 tests whose sites an M18 phase altered, which the design's "Calls made for the builder" brings into the population. All three sites had no red at all before 18.11, so the missing cell went with a missing red: §2 rows 54, 61 and 328, 330, 331 were `unresolvable`.

### 13d. The grade

**MET NARROWLY.**
- **108 pins, 339 sites.** Every site has an RS1 red that is fresh against each red's own commit and quotes its message: 108 from the phase records, 231 re-run in 18.11.
- **The narrowing:** three register rows, one for a new pin and two for altered pre-M18 sites, were written in 18.11, not in the phase that added or altered them. They exist now, and they cite fresh reds. The gap was in the 18.1 and 18.9 phases' own close-out, whose reviews did not check a pin's row against the population.
- **Two readings, and what each means:**
  - **Strict, on the sub-bullet's words:** for these three pins the cell was not written in the phase that added or altered them, so the sub-bullet, and item 13, is **NOT MET**. That grade can never be repaired, because a past phase cannot write its cell retroactively, so under this reading the milestone verdict is **FAIL**, permanently, whatever is done next.
  - **On intent, the grade this audit gives:** every new pin has a conforming row on HEAD that cites fresh reds, and the late timing is named as the narrowing. Item 13 is **MET NARROWLY**, and the verdict is PASS WITH FINDINGS.
- **The owner decides** between them. This audit grades MET NARROWLY.
- **Not filed:** the rows are fixed in this phase. The lesson, that a review-wave pin needs its row in the same wave, is for the retro.

---

## Sample re-run

Pending: an independent re-run, filled in by the controller.

---

## Carried debt

Each issue is judged against the item it touches. None blocks the close on its own. States from `gh issue list --state open` on 2026-11-15.

| issue | title | item | judgement |
|---|---|---|---|
| #575 | runtime: a host-call completion crosses the thread pool twice before its handler resumes | 3 | adjacent: the product side of #551, an owner decision |
| #711 | FaultNoticeTests facts block a pool worker while their notice crosses the pool | 3 | adjacent: #551's shape in other facts, filed by 18.9 |
| #580 | iOS: a cold LAContext start over about 40 s still fails the named warm-up step | 3 | **narrows item 3**: #453's residue |
| #603 | ShutdownQuiescenceTests positive control ended Canceled once on the Windows runner | 3 | adjacent: a different outcome from #551's, seen once |
| #600 | Android biometrics authenticate can hang when its Activity finishes during the prompt | 5 | adjacent: the biometrics op, not secure storage's order |
| #621 | Android: a second instance's final destroy denies another instance's orphaned prompt | 6 | adjacent: a multi-instance edge #601's bullet does not claim |
| #593 | A batch that a render fault ends never delivers its remove patches | 4 | adjacent, found in 18.2 |
| #562 | m17 pins: per-statement coverage beyond the name and anchor unit | 13 | outside the written unit |
| **#716** | iOS notifications test: the #444 add trace prints NO completion on every passing show since 18.10 | 3 | **filed by this audit**; a stale diagnostic, no assertion depends on it |
| **#717** | Pin register: pre-M18 cells name their stale reds but have no fresh red | 11 | **filed by this audit**; the residue of #568 under the M18-only ruling |

## What M18 taught

- **Reds go stale inside a milestone.** 220 of 339 M18 sites were stale by its end, because later phases edited shared subjects such as `AndroidShellBridge.kt`, `FaultNoticeTests.cs` and `Exports.cs`. A phase that calls its own reds current is right only until the next phase touches a subject.
- **A changed meaning stales a diagnostic, not just a red.** 18.10 changed when a show completes, and the `[#444]` trace kept describing the old order. #716.
- **A pin added in a review wave still needs its row.** `TearDownOverrunTests` came from 18.9's CodeRabbit fix wave and reached `main` with no register row.

## Verdict

**PASS WITH FINDINGS,** provisional on [the sample re-run](#sample-re-run). If it reproduces, M18 can close; `complete-milestone` follows with no tag, since `CONVENTIONS.md` records `Milestone completion tags a release: no`.

- **Eleven items are MET:** 1, 2, 4, 5, 6, 7, 8, 9, 10, 11 and 12. Item 11 is MET on its text, with its residue filed as #717.
- **Two are MET NARROWLY,** each with its reason:
  - **item 3:** one clean sample per lane cannot prove an intermittent flake gone; #407's trigger is unproven and #453 leaves #580;
  - **item 13:** three register rows written in 18.11, not in the phase that added or altered the pin.

**The strict reading of item 13, for the owner's decision.** Read on its words, item 13's sub-bullet "Its register cell is written in the phase that adds it" is **NOT MET** for those three pins. That grade can never be repaired, because a past phase cannot write its cell retroactively, so under that reading the milestone verdict would be **FAIL**, permanently. This audit grades MET NARROWLY, on the rule's intent, as [13d](#13d-the-grade) argues; the owner decides.

**The findings:**
- **#716,** the `[#444]` trace printing `NO completion` on every passing show since 18.10, `BnNotificationsTests.swift:162`, `:444`, `:560`–`:563`, `:583` and `BnNotifications.swift:179`. Test and product code, so filed, not fixed here.
- **#717,** the pre-M18 cells that name their stale reds but have no fresh red, including #568's three. Filed for a decision on re-running them.
- **The three register rows** of item 13. Fixed in this phase by the rows at PS `:638` to `:640`; the timing cannot be fixed.

**Next:** the controller fills in the sample re-run. Then the final review, the bookkeeping, and the owner's call on `complete-milestone`.
