# Milestone 17 audit — Close the Dispatch Lane's Debt

**Date:** 2026-10-17
**Phase:** 17.6, [design](../superpowers/specs/2026-10-17-phase-17.6-design.md),
[plan](../superpowers/plans/2026-10-17-phase-17.6-audit.md)
**Audited:** `origin/main` at **`980abdbf`** (#533, 2026-10-17T08:12:02+02:00). The M17 range is
`23ffc4ab..980abdbf`, where `23ffc4ab` is #499, the milestone start. The audit branch
`feat/17.6-audit-and-close` differs from `980abdbf` only under `docs/`
(`git diff --stat origin/main HEAD`: `MILESTONE.md`, `ROADMAP.md`, the 17.6 plan and design).
**Host for local runs:** Debian 13.7 x64, .NET SDK 10.0.402, PowerShell 7.6.7, Temurin 25.
**Precedent:** the [M16 re-audit](2026-10-14-milestone-16-reaudit.md), verdict PASS WITH FINDINGS.
It is cited for its method, never as evidence. The five phase records are cited only as the place
a mutation is recorded. Every number below was measured on `980abdbf` today, with its command
beside it.

**Verdict:** **FAIL**: 5 MET, 5 MET NARROWLY, 1 NOT MET (item 11, the pin standard). See
[the verdict](#verdict). Next: `plan-milestone-gaps`. M17 stays open.

## Scorecard

| # | DoD item | Verdict |
|---|---|---|
| 1 | All planned phases complete | **MET**: 17.1–17.5 complete on `980abdbf`; 17.6 is this audit |
| 2 | All tests passing, both device lanes dispatched, `headSha` compared | **MET**: CI `build-test` on the PR head `a4e741f3`, .NET 1372/0/0 and JVM 206/0/0; Android 245/0 and iOS 288/0 on `980abdbf`, all at attempt 1; 5 of 5 local .NET runs 1372/0 |
| 3 | #464 is fixed as decided | **MET** |
| 4 | #494 is closed | **MET NARROWLY**: one entry #494 lists, the cancellation fact's rc assertion, has no red; the test and the 17.1 record name why |
| 5 | #440 is fixed | **MET NARROWLY**: a denied biometric prompt completes a secure-storage request from the main thread, not from a background executor (#537). #506 adjacent, not contradicting |
| 6 | #425 is fixed: a mount replaces the root | **MET NARROWLY**: `MainActivity` retires only a booted runtime (#511), and no device test reds without its `retire()` (17.3 L1, named; #538) |
| 7 | #426 is fixed | **MET** |
| 8 | #435 is fixed by a forwarding hook | **MET NARROWLY**: for a bound input the warning names a compiler lambda, and with `:after` not the app's method (#522) |
| 9 | #454, #424 and #458 are fixed | **MET NARROWLY**: the stall names the script, its arguments and the last output, not the stalled command inside it (#530) |
| 10 | No ABI change | **MET**: 0.20.0's BREAKING change is behaviour, not ABI |
| 11 | Every new pin conforms to the pin standard, mutations recorded with anchors, register row written in its phase | **NOT MET** (#538): 18 of 85 M17 pins have no recorded red; 26 more pins have an anchor no recorded mutation enters; three register cells claim more than their record |

---

## 1. All planned phases complete — **MET**

> All planned phases complete

```bash
git show origin/main:docs/planning/ROADMAP.md | grep -n "Phase 17\."
#   3798:#### Phase 17.1: Fault attribution (#464, #494) [status: complete]
#   3809:#### Phase 17.2: Android host-call contract (#440) [status: complete]
#   3819:#### Phase 17.3: Activity recreation (#425) [status: complete]
#   3832:#### Phase 17.4: Fault-path parity (#426, #435) [status: complete]
#   3842:#### Phase 17.5: Test-infrastructure honesty (#454, #424, #458) [status: complete]
#   3851:#### Phase 17.6: Audit and close [status: pending]
```

17.1 to 17.5 read `complete` on `980abdbf`, and MILESTONE.md's `## Phases` agrees
(`git show origin/main:docs/planning/MILESTONE.md | grep -n "Phase 17"`, lines 84–88). Each phase's
PR closed its issues; see items 3–9. One `## Phases` summary is contradicted by this audit: line 84
says 17.1 left "all #494 anchors redded", and item 4 finds one #494 entry with no red. The status
is right; the summary claim is wrong, and #538 tracks its correction. 17.6 is this audit. It reads `pending` on `980abdbf` and
`active` on the audit branch (`docs/planning/ROADMAP.md:3851`).

## 2. All tests passing, both device lanes dispatched, `headSha` compared — **MET**

> All tests passing — .NET, JVM, **and both device lanes dispatched**, with each lane's `headSha`
> compared against the PR head

| lane | run | attempt | `headSha` | result | source |
|---|---|---|---|---|---|
| .NET, CI | `ci.yml` [39033490187](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/39033490187), `pull_request` on this audit's PR, job `build-test` | 1 | `a4e741f3` | **1372 / 0 / 0**: Renderer 147, Analyzers 27, Runtime 1198 | `dotnet test totals across 3 projects: passed=1372 skipped=0 failed=0` |
| JVM, CI | the same run and job, `gradlew testDebugUnitTest` on Windows | 1 | `a4e741f3` | **206 / 0 / 0**, 34 suites, the JNA tests included | `JVM test totals across 34 suites: tests=206 failures=0 errors=0 skipped=0` |
| Android | `android-instrumented.yml` [39024547287](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/39024547287), dispatched on `main` | 1 | `980abdbf` | **245 / 0 / 0** | `Instrumented totals across 1 suite(s): tests=245 failures=0 errors=0 skipped=0` |
| iOS | `ios.yml` [39025235604](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/39025235604), dispatched on `main` | 1 | `980abdbf` | **288 / 0** | `XCTest cases: passed=288 failed=0`, job `simulator iossimulator-arm64`; job `device ios-arm64`, build and link only, also green |

```bash
gh workflow run android-instrumented.yml --ref main     # then, after it completed:
gh workflow run ios.yml --ref main
gh run view <id> --json status,conclusion,headSha,attempt,jobs
gh run view <id> --log | grep -E "Instrumented totals|XCTest cases"
```

```bash
gh run watch 39033490187 --exit-status                                   # exit 0, conclusion success
gh run view 39033490187 --json conclusion,headSha,attempt,event,jobs     # success, a4e741f3, 1, pull_request
gh run view 39033490187 --job 117398581412 --log | grep -E "dotnet test totals|JVM test totals|Passed!  -"
git diff --stat origin/main a4e741f3 -- ':!docs'                          # empty
```

The device lanes ran one at a time, Android first, dispatched on `main`. Each `headSha` equals
`980abdbf`. The DoD says "the PR head". The audit PR's head `a4e741f3` differs from `980abdbf` only
under `docs/`: `git diff --stat origin/main a4e741f3 -- ':!docs'` prints nothing. So no lane input
changed between the dispatched lanes' commit and the PR head that CI ran on. No lane needed a rerun.

**Local .NET, five runs of the full suite** on a detached worktree of `980abdbf`:

```bash
timeout 1500 dotnet test BlazorNative.sln -c Debug --nologo --blame-hang --blame-hang-timeout 10m
```

| run | exit | wall | Analyzers | Renderer | Runtime | hang detector |
|---|---|---|---|---|---|---|
| 1 | 0 | 53 s | 27/0 | 147/0 | 1198/0 | did not fire |
| 2 | 0 | 50 s | 27/0 | 147/0 | 1198/0 | did not fire |
| 3 | 0 | 50 s | 27/0 | 147/0 | 1198/0 | did not fire |
| 4 | 0 | 49 s | 27/0 | 147/0 | 1198/0 | did not fire |
| 5 | 0 | 50 s | 27/0 | 147/0 | 1198/0 | did not fire |

1372 passed per run, 0 skipped. The only blame output was the benign "All tests finished running,
Sequence file will not be generated." No hang dump.

**The environment, a deviation from the design.** Design Approach 4 says each run's environment is
read from the run's own process. This audit read it from the shell that launched all five runs
instead: `MSBUILDDISABLENODEREUSE=1` was set there (`env | grep -c MSBUILDDISABLENODEREUSE` = 1),
and every test process inherits it. The deviation does not change the grade, which rests on CI and
the lanes. Either way, these runs had MSBuild node reuse **off**, so they say nothing about #530's leading suspect,
which is a reused node holding the publish's pipe. This host is Linux; #454 was seen twice on
Windows and once on Linux (the 17.2 record, `docs/plans/2026-10-15-phase-17.2-record.md:10-17`).

**Local JVM**, not binding: CI's `build-test`, above, is the JVM evidence.

```bash
export JAVA_HOME=$HOME/bn-tools/jdk-25 ANDROID_HOME=$HOME/bn-android
cd src/BlazorNative.Jni && bash ./gradlew testDebugUnitTest --rerun
```

After producing the two bionic `.so` publishes the task requires, the run gave **206 tests, 150
passed, 56 failed**, gradle exit 1, summed from `build/test-results/testDebugUnitTest/*.xml`.

- **All 150 pure-Kotlin tests passed.**
- **All 56 failures are native-library loads**, not behaviour: 52 ×
  `NoClassDefFoundError: Could not initialize class io.blazornative.jni.NativeBindings`, 2 ×
  `UnsatisfiedLinkError: Unable to load library 'BlazorNative.Runtime'`, and 2 downstream of the same
  missing library. These are the JNA tests. `build.gradle.kts:250-252` points `jna.library.path` at
  the win-x64 publish folder, which a Linux host cannot build. That is **#491**, open and adjacent.

**Grade: MET.** CI's `build-test` is green on the PR head, with .NET 1372/0 and JVM 206/0, the JNA
tests included. Both device lanes are green on a commit whose only difference from that head is
docs. All of them passed at attempt 1, and the local .NET suite passed five times out of five.

## 3. #464 is fixed as decided — **MET**

> a fault from a continuation of dispatch A that runs inline inside dispatch B's synchronous part
> is A's, delivered as A's late FaultNotice under A's handler id, and B keeps its own synchronous
> fault, rc 2. Capture and the begun-call mark both check that the flowing scope matches the
> dispatch's scope. **Both** outcomes are pinned and mutation-proven: A's fault reaching A, and B's
> own fault surviving.

**Capture checks the flowing scope**, `src/BlazorNative.Renderer/NativeRenderer.cs:628-647` on
`980abdbf`:

```csharp
if (_currentScope is { } scope)
{
    DispatchScope? owner = s_flowingScope.Value;
    bool anotherDispatchs = owner is not null          // :631
        && !ReferenceEquals(owner, scope)
        && ReferenceEquals(owner.Owner, this);
    if (!anotherDispatchs)                             // :634
    {
        scope.Fault ??= exception;                     // the window's own fault
        ...
        return;
    }
    if (owner!.Done) { /* log, FinishedDispatchFaultLogLabel */ return; }
}
// falls through to the late-fault path: Interlocked.CompareExchange(ref flowing.LateFault, ...)
```

**The begun-call mark checks it too**, `NativeRenderer.cs:1641-1642`:

```csharp
if (t_syncScope is { Fault: null } scope && ReferenceEquals(s_flowingScope.Value, scope))
    scope.ShellCallBegun = true;
```

```bash
grep -n "anotherDispatchs\|ReferenceEquals(s_flowingScope.Value, scope)\|scope.ShellCallBegun = true" src/BlazorNative.Renderer/NativeRenderer.cs
```

**The two pins**, `tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs`:
- **A's fault reaching A:** `AnInlineContinuationsFault_IsItsOwnDispatchs_NotTheWindows` (`:950`).
  It asserts B's rc 0 (`:958`) and a single FaultNotice whose `handlerId` is A's (`:963`), with the
  `RanInsideB` anchor first.
- **B's own fault surviving:** `AnInlineContinuationsFault_LeavesTheWindowsOwnFault_Rc2` (`:970`).
  It asserts B's rc 2 (`:976`), A's notice under A's id, and no notice for B's own `b-own` (`:983`).

**Mutation-proven.** Both were red before the fix and under 17.1's M1 and A1/A4 (the 17.1 record,
`docs/plans/2026-10-14-phase-17.1-record.md:25-26`, `:52`, `:57`, `:60`). `NativeRenderer.cs` has
changed by 118 lines since 17.1's squash `00976fd7`, so M1 was **re-run on `980abdbf` today**:
`if (!anotherDispatchs)` → `if (true)` reds both, the first with `rc was 2: B's own synchronous part
did not fault. A's fault was charged to B's window, which is #464.` and the second with `no
FaultNotice for A's fault.` M1 mutates capture, not the mark. It also reds
`ACallBegunByAnInlineContinuation_…` (`:989`), but only because A's fault is lost, so it does not
prove the mark's own check. **17.1's M2 was therefore re-run on HEAD too.** It is the mark
without its flowing check: `NativeRenderer.cs:1641`
`if (t_syncScope is { Fault: null } scope && ReferenceEquals(s_flowingScope.Value, scope))` →
`if (t_syncScope is { Fault: null } scope)`. With
`dotnet test tests/BlazorNative.Runtime.Tests -c Debug --filter "FullyQualifiedName~FaultNoticeTests"`,
34 facts, **1 of 34 went red**: `ACallBegunByAnInlineContinuation_DoesNotMarkTheWindow`, `rc was 0:
the call A's continuation began marked B, so B's own synchronous fault became rc 0. Only B's own
synchronous part may mark B.` That matches R1 :53. Each mutation ran in a detached worktree of
`980abdbf` and was restored, and `git status --short` was empty after each.

**GitHub:** `gh issue view 464 --json state,closedAt,closedByPullRequestsReferences`: `CLOSED`,
2026-10-15T04:15:30Z, closed by PR #503, the 17.1 PR.

## 4. #494 is closed — **MET NARROWLY**

> every anchor it lists has a recorded red, the unpinned rc claim at `FaultNoticeTests.cs:407-410`
> is pinned or rewritten as resting on reading, and both stale headers and the two `host_event` rc
> tables match the contract.

**GitHub:** `gh issue view 494 --json state,closedAt,closedByPullRequestsReferences`: `CLOSED`,
2026-10-15T04:15:31Z, closed by PR #503.

**Every anchor #494 lists** (`gh issue view 494`), against its red in the 17.1 record
(`docs/plans/2026-10-14-phase-17.1-record.md`, "R1"):

| #494 entry | anchor on HEAD | recorded red |
|---|---|---|
| `AFaultAfterAnInlineAnsweredFetch_…`: `FetchesCompletedInsideBegin == 1` | `FaultNoticeTests.cs:488` | N1, R1 :78 |
| `AComponentAwaitingAnInlineAnsweredHostCall_…_MountsSynchronously`: the counter anchor | `:531` | N2, R1 :79 |
| `TheBackHoldProbe_…`: both hold anchors, then `:584` and `:590` | now `:590` and `:592` | N3 and N4, R1 :80-81 |
| `AFaultBeforeASiblingBeginsAShellCall_…`: its three order anchors | child-threw `:779`, order `:783`, sibling begin | N5, N7, and M6's second red, R1 :76, :82-83, :87 |
| `ANoticeDotNetSends_…`: the `notice` anchor | `:831` | N8, R1 :84 |
| `AFaultNoticeAfterABegunCall_IsNotBegunOnTheDispatchingThread`: the rc and thread-recorded anchors | `:453` fact; thread anchor `:468` | rc anchor M7, R1 :77; thread anchor N10, R1 :85 |
| JVM `FaultNoticeTest.a_fault_after_an_inline_answered_host_call_…`: `answeredInsideBegin` and `completeRc` | `FaultNoticeTest.kt:106` | J1 and J2, CI runs 38634551185 and 38634554515, R1 :109-110 |
| `ACancellationAfterABegunHostCall_IsRc0_AndSendsNothing`: its rc assertion | `FaultNoticeTests.cs:714`; the rc assertion `:726` | **none.** Named instead, in the test at `:723-725` and in R1 :93 |

Every listed anchor proper has a recorded red. These reds stand on HEAD: none of their files has
changed since 17.1.

```bash
git diff --stat 00976fd7 origin/main -- tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs \
  tests/BlazorNative.Runtime.Tests/FakeShellHost.cs src/BlazorNative.Runtime/NativeShellBridge.cs \
  src/BlazorNative.Jni/src/test/kotlin/io/blazornative/jni/FaultNoticeTest.kt           # empty
```

M7 was also re-run today with the 17.1 set, and it reds the same 5 facts as recorded, the thread
fact's rc anchor included.

**The cancellation entry is the narrowing.** #494 lists it under "anchors no mutation has ever
redded", and says itself that no probe change can red its rc assertion. 17.1 did not red it; it
named the limit, in the test:

```csharp
// Rule 7 limit, named in 17.1 from #494: no probe-side change can red this rc assertion,
// because turning the cancellation into a fault after the begun call is rc 0 too. The
// 16.8 re-audit's X2 reds the Assert.Empty below instead.                      // :723-725
```

Graded on the words, "every anchor it lists has a recorded red" is not true of this one entry. It is
a partial its record names, with a reason. By the 16.8 precedent that makes the item **MET
NARROWLY**.

**The rc claim at the old `:407-410` is pinned.** It now reads at `FaultNoticeTests.cs:408-412`,
and its rc 0 half is pinned by `AChildsBegunCall_DuringTheReRender_MarksTheDispatch_SoALaterRenderFaultIsRc0`
(`:796`). M7 reds it (R1 :77), and M7 was re-run today. One stale note remains. The comment still
sits in the 16.7 section's DOES NOT COVER block and still says "The ORDER is pinned", without
naming `:796`. The item's words ask for the claim to be pinned, and it is. The comment is a Rule 5
nit, filed with the other M17 pin work as #538.

**A record claim this contradicts.** MILESTONE.md's `## Phases` summary for 17.1, line 84, says
"all #494 anchors redded". The cancellation entry above has no red, so that summary overstates.
#538 tracks its correction.

```bash
git show 23ffc4ab:tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs | sed -n 400,412p   # the old text
sed -n 394,418p tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs                        # HEAD
```

**Both headers match the contract.**
- `FaultNoticeTests.cs:13-55`, the file header, describes 16.1 and lists the later sections, 16.7
  (#455) and 17.1 (#464), each "with its own limits".
- `FaultNoticeTest.kt:12-36`, the JVM class KDoc, now has a 16.7 paragraph for the inline-answered
  test, and its DOES NOT COVER names "a continuation of another dispatch running inline (17.1,
  #464)".

**The two `host_event` rc tables match the contract**, `src/BlazorNative.Runtime/Exports.cs`. The
contract, between `BEGIN rc-contract` and `END rc-contract` (`:473-484`), keys rc 0 on a begun host
call or fetch: "0 = it did not fault, or it faulted only after it began a host call or a fetch".
- `back` (`:895-903`): "If it ever yields, or begins a host call or a fetch, and faults after that,
  this reports rc 0 and the fault is sent to the shell as a FaultNotice, the dispatch_event
  contract."
- `navigate` (`:958-967`): the same sentence.

The 16.8 re-audit found these keyed on yielding alone. Both now name the begun call as well, which
is the contract's key; a fault after a yield is outside the synchronous part, so rc 0 is the
contract's answer there too.

## 5. #440 is fixed — **MET NARROWLY**

> Android's biometrics `check` and every secure-storage path return from `hostCallBegin` at once
> and complete through `host_call_complete` from a background executor, as iOS has since 16.4. A pin
> shows `hostCallBegin` returns before the work runs.

**`hostCallBegin` returns at once**, `src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt:317-328`:

```kotlin
// 17.2 (#440): enqueue and return; the work runs on the handler's own executor.
HostCallOp.BIOMETRICS -> biometricsExecutor.execute {
    runJob(requestId, BiometricStatus.ERROR) { handleBiometrics(requestId, argsJson) }
}
HostCallOp.SECURE_STORAGE -> secureStorageExecutor.execute {
    runJob(requestId, SecureStorageStatus.ERROR) { handleSecureStorage(requestId, argsJson) }
}
```

The executors are single-thread daemons, `BlazorNative-Biometrics` (`:281-283`) and
`BlazorNative-SecureStorage` (`:284-286`). Every action is dispatched inside the executor:
- biometrics `check`, `:758`: `completeHostCall(requestId, canAuthenticateStatus(), null)`, so it
  runs and completes on `BlazorNative-Biometrics`;
- secure storage `set`, plain `get` and `delete`, `:793-798`, complete inline on
  `BlazorNative-SecureStorage`;
- the two auth paths, an auth-bound `set` (`:828`) and `getWithAuth` (`:877`), hop back to the
  executor through `afterAuth` (`:292-302`) once the prompt succeeds (`:844`, `:901`).

The template copy is byte-identical:
`cmp src/BlazorNative.Jni/.../AndroidShellBridge.kt templates/.../AndroidShellBridge.kt` prints
nothing.

**The pins**, `src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/AndroidHostCallContractTest.kt`:
- `biometrics_begin_returns_before_the_check_runs` (`:118`) and
  `secure_storage_begin_returns_before_the_keystore_work_runs` (`:122`), both through
  `assertBeginReturnsBeforeTheWorkRuns` (`:96-115`). The work is gated, the anchor
  `gateReachedForTest` comes first, then `hostCallBegin did not return while the work was gated … That
  is #440.`, and the completion's thread is the handler's executor;
- `the_after_auth_work_runs_on_the_secure_storage_executor` (`:151`), the after-auth hop.

Re-redded on HEAD today, since `AndroidShellBridge.kt` changed after 17.2: A1, M3 and M6 in run
39029565241, M3 alone in 39030272142, and M4 in 39030947426. All at attempt 1, on scratch commits
over `980abdbf`. See item 11's re-red table.

**GitHub:** `gh issue view 440 …`: `CLOSED`, 2026-10-15T06:01:59Z, closed by PR #507.

**The narrowing, found by reading.** A secure-storage request whose biometric prompt is **denied**
does not complete from a background executor. Both auth paths pass
`onDenied = { completeHostCall(requestId, SecureStorageStatus.AUTH_FAILED, null) }` (`:850`, `:909`)
to `runBiometricGate`, which shows the prompt with `ContextCompat.getMainExecutor(appContext)`
(`:953-967`). `onAuthenticationError` then calls `onDenied` on the **main thread**, and the request
completes there. Only the success path hops back through `afterAuth`. This does no KeyStore or
BiometricManager work on main, so the harm #440 named, work inside the begin on the render thread,
is fixed. But the item's words are "every secure-storage path … complete … from a background
executor", and the denied auth path does not. No record names this, and no pin covers it. It was
found by reading, not measured. The biometrics `authenticate` action completes from the prompt
too, `:759-764`, but the item names only `check`. Filed as **#537**: hop the denied completion to
the executor, or accept main and reword the contract. Both copies have it, the reference shell
and the byte-identical template.

**#506 is adjacent, not contradicting.** An auth-bound `set` frees its executor while its prompt is
up, so later requests on the same key run before its encrypt. The item asks that the begin return
at once and that completions come from an executor; it says nothing about the order across a
prompt. The comment at `:271-280` names #506.

## 6. #425 is fixed: a mount replaces the root — **MET NARROWLY**

> - A mount disposes any root already mounted, so a late continuation of the old page renders
>   nothing.
> - A retired runtime drops any frame it still receives.
> - Recreation is wired: `MainActivity` retires the old runtime, in the reference shell and the
>   template, proven on the Android device lane by a rotation test.
> - **A recreated shell stays on its page with its back stack,** owner decision 2026-10-15, which
>   answers #425's comment. Navigation from a disposed root's late code is dropped. Both are pinned
>   on the same path.

**GitHub:** `gh issue view 425 …`: `CLOSED`, 2026-10-15T18:04:59Z, closed by PR #510.

`RT/` is `tests/BlazorNative.Runtime.Tests/`, `AT/` is
`src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/` and `JT/` is
`src/BlazorNative.Jni/src/test/kotlin/io/blazornative/`.

**a. A mount disposes the old root.** `src/BlazorNative.Runtime/HostSession.cs:507-512`: a second
mount runs as one render-thread unit that bumps `renderer.RootGeneration` and calls
`UnmountAndMount(effective, renderer, replacedByTheShell: true)`. That unmounts the old root,
`renderer.UnmountReplacedRoot(current)` (`:626`).
- Pins: `RT/RecreationTests.cs` `ASecondMount_DisposesTheFirstRoot_SoItsLateContinuationRendersNothing`
  (`:106`) and `ASecondMount_DisposesTheFirstRoot` (`:128`).
- Re-redded today by 17.3-M1, which removes `:626`'s call.

**b. A retired runtime drops any frame.** `src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/BlazorNativeRuntime.kt:75-78`:
`if (retired) { framesDroppedAfterRetire.incrementAndGet(); return }`. `retire()` (`:560-571`) sets
`retired` and waits out a callback already in flight.
- Pins: `JT/jni/RetireLateContinuationTest.kt` `a_retired_runtime_drops_a_frame_from_a_late_continuation`
  (`:29`) and `retire_waits_out_a_frame_callback_already_in_flight` (`:81`).
- Re-redded today, J1 and J2, through throwaway draft PR #535, `ci.yml` runs 39031400796 and
  39032029220.

**c. Recreation is wired.** `MainActivity.onDestroy` calls `if (booted) runtime.retire()`, in the
reference shell (`src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt:470`)
and in the template (`templates/BlazorNative.Templates/content/BlazorNative.App/android/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt:483`).
The device tests are `AT/RecreationAndroidTest.kt`'s four. They recreate with `scenario.recreate()`,
the destroy-and-recreate path a configuration change such as a rotation takes, rather than by
setting the orientation. The 17.3 design calls them "the rotation test".

**d. A recreated shell stays on its page; a disposed root's navigation is dropped.**
- **The page and its back stack:** `ResolveTheMount(name, replacing: true)` adopts the shell's route
  (`HostSession.cs:602-608`). Pins:
  - `RT/RecreationTests.cs` `ASecondMount_StaysOnTheCurrentPage_WithItsHistory` (`:170`);
  - `AT/RecreationAndroidTest.kt` `a_recreated_activity_stays_on_its_page_and_back_goes_to_the_parent`
    (`:90`), plus the deep-linked (`:113`) and unrouted (`:134`) variants.
- **The navigation drop:** `NativeNavigationManager.FromAReplacedRoot` (`src/BlazorNative.Runtime/NativeNavigationManager.cs:343-351`)
  drops and logs a navigation whose flowing root generation is not the current one
  (`HostSession.IsCodeOfAReplacedRoot`, `HostSession.cs:294-300`). Pins:
  `ANavigationFromADisposedRootsContinuation_IsDropped` (`:253`) and
  `ANavigationFromADisposedRootsLifecycleCode_IsDropped` (`:282`).
- Both run through the same replacing-mount unit, so they are pinned on the same path.

**The narrowing, two parts.**
1. **#511.** "`MainActivity` retires the old runtime" holds only when `booted` is true (`:470`,
   `:483`). The comment above it names the gap: "NOT COVERED: a recreation during boot, while
   `booted` is false and the boot thread may still be inside start(): #511." So a recreation
   during a cold boot skips `retire()`. The sub-bullet's words have no such limit.
2. **"Proven on the Android device lane" does not hold for `retire()` itself.** 17.3's L1 removed
   `retire()` from `MainActivity`, and the lane stayed 241/0 (the 17.3 record,
   `docs/plans/2026-10-15-phase-17.3-record.md:111`). The record gives the reason: with the .NET
   unmount, the old root renders nothing, so the retired runtime has nothing to drop on these
   paths. The JVM pins in (b) prove the drop; no device test proves the wiring. The record names
   this, with a reason. Filed with the other M17 pin work as **#538**: a gap phase either finds a
   device test that reds without `retire()` at `MainActivity.kt:470` and the template's `:483`,
   or drops the claim with that reason.

Also, one of the four device tests, `:90`, has no recorded red at all. That is item 11's finding 1,
graded there.

## 7. #426 is fixed — **MET**

> Android redacts the FaultNotice message the way iOS does, and a twin pin holds the two shells'
> redaction together. *Builder's call:* iOS's `privacy: .redacted` is not tied to a build
> configuration, so 17.4's design measures what iOS actually redacts, and when, before it settles
> where Android's redaction lives, the library or the template's `onError`.

**The builder's call was taken.** The 17.4 design,
`docs/superpowers/specs/2026-10-16-phase-17.4-design.md:18-33`, "What iOS actually does, measured",
records it:
- **What:** every message is `.redacted` unless its text is a compile-time constant (`:22`).
- **When:** in every build configuration, with `.private` text shown only under an attached
  debugger (`:25`).
- **The owner's decision:** mirror iOS fully (`:33`). Android's condition is "debuggable" in
  place of "debugger attached", recorded as a builder's call with its reason (`:48`).
- **Where:** in the library, a `BnShellLog` seam, not the template's `onError`.

The measurement is a reading of `BnLog.swift`, not a device log capture. That is what the design
calls measured.

**GitHub:** `gh issue view 426 …`: `CLOSED`, 2026-10-16T13:42:56Z, closed by PR #523.

**The seam.** `src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/BnShellLog.kt`:
- every entry point takes `privacy: BnPrivacy = BnPrivacy.REDACTED`: `info` `:175`, `debug` `:181`,
  `verbose` `:188`, `warn` `:194`, `error` `:200`, `relay` `:218`;
- unless `reveal` (`:131`) is set, a REDACTED text is written as `PRIVATE = "<private>"` (`:123`,
  `:233`), and the throwable is dropped.

`MainActivity.kt:264` sets `BnShellLog.reveal = (applicationInfo.flags and
ApplicationInfo.FLAG_DEBUGGABLE) != 0`, so a Release build redacts. Its `onError`, which receives the
FaultNotice, is `{ msg, t -> BnShellLog.error(tag, msg, t) }` (`:335`), REDACTED by default. iOS's
`BnLog.swift` defaults every call to `privacy: BnLogPrivacy = .redacted` (`:182`, `:199`).

**The twin pin.** `tests/BlazorNative.Runtime.Tests/LogPrivacyDriftTests.cs` reads both shells:
- `BnShellLog_DefaultsEveryEntryPointToRedacted` (`:285`);
- `SwiftBnLog_DefaultsToRedacted_AndItsPumpTakesTheDefault` (`:329`);
- `MainActivity_SetsRevealFromDebuggable_BeforeThePump` (`:351`);
- the scans that keep any raw `android.util.Log` or `System.err` outside the seam (`:108`, `:145`).

The device side is `AT/FaultNoticeLogAndroidTest.kt` `a_fault_notice_message_is_readable_in_a_debuggable_build`
(`:73`). It was re-redded today by L1b, run 39028712698.

The item asks for a twin pin, and one exists. Four of its facts have no recorded red, the Swift
twin among them. That is graded under item 11, not here.

**#522 is outside this item.** It concerns the slow-handler warning, which is item 8, not redaction.
It is judged there.

## 8. #435 is fixed by a forwarding hook — **MET NARROWLY**

> the slow-handler warning names the app's handler for `BnInput`, `BnCheckbox`, `BnPicker`,
> `BnSlider`, `BnSwitch` and `BnModal`. The KnownLimit test `TwoBnInputs_…_KnownLimit` is flipped as
> its doc says.

**GitHub:** `gh issue view 435 …`: `CLOSED`, 2026-10-16T13:42:57Z, closed by PR #523.

**The hook.** `src/BlazorNative.Core/IForwardsUiEvent.cs:7`, `internal interface IForwardsUiEvent`.
All six wrappers implement it:

```bash
grep -rn "IForwardsUiEvent" src --include=*.cs --include=*.razor
#   BnInput.cs:31, :89     BnCheckbox.razor:56, :80   BnPicker.razor:69, :144
#   BnSlider.razor:43, :84 BnSwitch.razor:24, :49     BnModal.razor:84, :161
```

The renderer consults it when the delegate's method is the framework's,
`NativeRenderer.cs:1758-1760`:

```csharp
if (Handler.Target is IForwardsUiEvent wrapper
    && wrapper.ForwardedHandler(EventName, Handler) is { } forwarded
    && OwnerOf(forwarded) is { Framework: false } app)
    return ($"{app.Declaring}::{app.Method}#{EventName}", $"{app.Declaring}.{app.Method}");
```

**The flipped KnownLimit test.** On `23ffc4ab` it was
`TwoBnInputs_WithDifferentSlowHandlers_StillShareOneWarning_NamingBnInput_KnownLimit`. Its doc said:
"When BnInput exposes the app's delegate … this pin goes red: flip it to two warnings naming the
app's methods, do not delete it." On HEAD it is
`TwoBnInputs_WithDifferentSlowHandlers_GetTwoWarnings_NamingTheAppsHandlers`
(`tests/BlazorNative.Runtime.Tests/SlowHandlerWarningTests.cs:612`). It asserts two warnings, after
its `RunsOf` anchors, and its doc says it "Was a known limit until Phase 17.4". It was flipped in
place: `git log --follow` on the file shows one history, with #523 last.

```bash
git show 23ffc4ab:tests/BlazorNative.Runtime.Tests/SlowHandlerWarningTests.cs | grep -n KnownLimit   # :586
grep -n "KnownLimit\|GetTwoWarnings_NamingTheAppsHandlers" tests/BlazorNative.Runtime.Tests/SlowHandlerWarningTests.cs
```

The five other wrappers each have a fact naming the app's method: `:648`, `:656`, `:664`, `:672` and
`:680`. All six facts were red before the fix (the 17.4 record,
`docs/plans/2026-10-16-phase-17.4-record.md:23-28`) and under M1 and M2 (`:86-87`). Their test file
and subjects are unchanged since `41af910c`, so the reds stand.

**The narrowing: #522.** For a **bound** input, `@bind-Value` or `@bind-Value:after`, Razor passes
the wrapper a lambda declared on the page. The warning then reads `SlowBindPageA.<BuildRenderTree>b__0_0`
(R4 :33-36; #522). That names the page, not the wrapper. But with `:after`, the app's own method is
inside the lambda, and the warning does not name it. Graded on "names the app's handler", a bound
input with `:after` is not met. A `:get`/`:set` binding does name the app's set method
(`AGetSetBoundBnInput_NamesTheAppsSetMethod`, `:797`). #522 is open, and it needs a decision.

## 9. #454, #424 and #458 are fixed — **MET NARROWLY**

> - `ReferenceFixtureBase` waits on its child with a bound and fails naming the stalled command;
> - the JVM test task declares the native library as an input;
> - the androidTest `pollUntil` copies are one helper with named failure messages.

**GitHub:** #454 `CLOSED` 2026-10-17T05:19:51Z, and #424 and #458 `CLOSED` 2026-10-17T05:19:52Z,
each closed by PR #528 (`gh issue view <n> --json state,closedAt,closedByPullRequestsReferences`).

**a. The bounded wait.** `ReferenceFixtureBase` (`tests/BlazorNative.Runtime.Tests/ReferenceDriftTests.cs:32`)
calls `BoundedScript.RunBounded(script, ["-Package", package, "-OutputPath", OutputDirectory],
GenerationBound)` (`:55-56`), with `GenerationBound = TimeSpan.FromMinutes(5)` (`:39`).
`ComponentReferenceDriftTests.cs:62-64` does the same. `BoundedScript.RunBounded`
(`tests/BlazorNative.Runtime.Tests/BoundedScript.cs`) waits `p.WaitForExit(bound)` and then for EOF
on both streams within the rest of the bound (`:65-68`). On expiry it kills the tree and throws a
message built at `:79-93`:

```csharp
string run = $"{script} {string.Join(' ', args)} did not finish within {bound}: ";
throw new XunitException(exited
    ? run + $"the script exited with code {p.ExitCode}, but its output never reached end of file. " + … + $"Last output:\n{tail}"
    : run + "the script was still running at the bound, and its process tree was killed. " + … + $"Last output:\n{tail}\n" + …);
```

`tail` is the last 20 lines of stdout and stderr, interleaved (`:77-78`).

**The stall's real text on HEAD.** It was captured by a temporary `Assert.Fail` added after
`GeneratorRunTests.cs:101` and run alone. The file was then restored and `git status --short` was
empty:

```
<wt>/tests/BlazorNative.Runtime.Tests/TestData/hold-stdout.ps1 -PidFile /tmp/bn-454-stub-3d3b…e593b.pids did not finish within 00:00:15: the script was still running at the bound, and its process tree was killed. Either a step is slow, such as a cold `dotnet tool restore`, or the script is in the #454 shape: it waits for end of file from a child command that has exited while a descendant of that child still holds the child's output pipe. #530 tracks what that descendant is. A descendant re-parented after its parent exited, such as an MSBuild node, is out of a tree kill's reach and may linger.
Last output:
stub started
A stray MSBuild node can be cleared with `dotnet build-server shutdown`.
```

`dotnet test tests/BlazorNative.Runtime.Tests --filter FullyQualifiedName~GeneratorRunTests` gives
5 of 5 on HEAD. `pgrep -fa hold-stdout` is empty afterwards.

**What it names:**
- the script, by its full path;
- its arguments, which for `ReferenceFixtureBase` are `-Package <name> -OutputPath <dir>`, so the
  package is named;
- which case it hit, still running or exited without EOF;
- the last 20 output lines.

It does **not** name the native command inside the script that stalled. The script echoes a `==>`
line before each step:
- `scripts/generate-reference.ps1:329`, `==> generate-reference: restoring the pinned generator …`;
- `:343`, `… publishing (the dependency-complete output)`;
- `:356`, `… xmldoc2md -> $outDir`.

So the step can be read from the tail when that line is among its last 20 lines. The message itself
does not say it.

**The words, decided.** Is the script "the stalled command"? The sentence names two things: the
fixture "waits on its child" and "fails naming the stalled command". If the second meant the child,
"naming it" would do. In #454's evidence, and in #530's, the thing that stalls is not the script.
The script is waiting for EOF from a native command, `dotnet publish`, whose pipe something still
holds. The message names the child and leaves the stalled command to the tail. So the words are not
fully met, and the item is **MET NARROWLY**, citing **#530**, which tracks what holds the pipe. The
stall fact also does not assert the script path or the tail; it asserts `did not finish within`,
the still-running case, `#454`, `#530` and the shutdown hint (`GeneratorRunTests.cs:94-101`).

**b. The JVM test task declares the native library.** `src/BlazorNative.Jni/build.gradle.kts`
declares `val nativeRuntimeFiles = fileTree(winX64PublishPath) { include("BlazorNative.Runtime.dll") }`
(`:257`). `tasks.withType<Test>().configureEach` adds `inputs.files(nativeRuntimeFiles)
.withPathSensitivity(PathSensitivity.RELATIVE).withPropertyName("nativeRuntime")` (`:267-269`), and
`runPreviewHost` (`:299-301`) and the inspector host (`:397-399`) declare the same input. The
template's `build.gradle.kts` does too (`:197`, `:207-209`).

Pin: `NativeRuntimeInputDriftTests.TheJvmTasksThatLoadTheRuntime_DeclareItAsAnInput` (`:61`), red
under M4a and M4b (R5 :152-153, :172). The input is still the win-x64 folder only; that is #491.

**c. One `pollUntil`.** `src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/Poll.kt:21`,
`internal fun pollUntil(…)`.
- It catches the NPE `onActivity` throws on a destroyed activity and rethrows it as
  `AssertionError(DESTROYED_WHILE_POLLED, e)` when the scenario's state is `DESTROYED`.
- It is the only declaration of that name: `grep -rn "fun pollUntil" --include=*.kt src templates`
  finds `Poll.kt:21`. The two other hits are differently named, `pollUntilIdle`, a wrapper over the
  shared helper (`BnModalDemoAndroidTest.kt:137-145`), and `pollUntilBackNavigatedAway` (`:475`).
  The second treats a destroyed activity as success, and the 17.5 record says it is not a copy
  (R5 :86).
- Pins: `PollUntilDriftTests.NoPrivatePollUntil_RemainsInAndroidTest` (`:35`) and
  `AT/PollAndroidTest.kt`'s two tests. All three have recorded reds (R5 :162-164, :177-181, :235).

## 10. No ABI change — **MET**

> **No ABI change**, verified by diffing: the 80-byte bridge struct and the 10 exports. Any wire
> addition, such as the frame generation, lives in `src/wire-vocabulary.json` or the existing frame
> format, and is generated like the rest.

```bash
for r in 23ffc4ab origin/main; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -c '^\s*\[UnmanagedCallersOnly(EntryPoint'; done                   # 10, 10
for r in 23ffc4ab origin/main; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -o '\[UnmanagedCallersOnly(EntryPoint = "[a-z_]*"' | sort | md5sum; done
#   52ec8c5e37245dd1522729412c4b3c2a, both ends
git diff --stat 23ffc4ab origin/main -- src/BlazorNative.Runtime/BridgeProtocolNative.cs                                                           # empty
git diff 23ffc4ab origin/main -- src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h | grep -E "^[-+][^-+]" | grep -v -E "^[-+]\s*(//|\*|/\*)"   # empty
git diff --stat 23ffc4ab origin/main -- src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h                                                        # 4 insertions, all comment
git diff --stat 23ffc4ab origin/main -- src/wire-vocabulary.json                                                                                    # empty
```

- **The exports:** the same 10 at both ends, with identical names: `blazornative_init`, `_shutdown`,
  `_version`, `_register_frame_callback`, `_mount`, `_dispatch_event`, `_register_bridge`,
  `_fetch_complete`, `_host_event`, `_host_call_complete`. `Exports.cs` is the only file that
  declares one; the other `UnmanagedCallersOnly` hits are a doc comment
  (`BridgeProtocolNative.cs:90`) and the analyzer.
- **The 80-byte struct:** `BlazorNativeBridgeCallbacks`, "10 × IntPtr = 80 bytes"
  (`BridgeProtocolNative.cs:96`), is unchanged across the range. The C header adds four comment
  lines only, the contract's 17.1 sentence: "A fault belongs to the dispatch whose own code raised
  it …".
- **The wire:** `src/wire-vocabulary.json` is unchanged. So are the generated copies and
  `FrameEncoder.cs`. The one change in `NativeFrameAdapter.kt`, in both mirror copies, is a log
  line moved from `System.err.println` to `BnDiagnostics.sink`. 17.3's root generation is in-process
  state (`NativeRenderer.RootGeneration`, `:1900`) and never crosses the wire.
- **Release-please commits in the range, excluded by name:** `bad7c3fd` `chore(main): release
  0.20.0 (#504)` (`git log --format='%h %s' 23ffc4ab..origin/main | grep "chore(main)"`). Its
  `Exports.cs` change is the version string.

**0.20.0's BREAKING change is behaviour, not ABI.** The CHANGELOG reads: "in #464's scenario the
window dispatch now returns its own rc, and the continuation's fault arrives as its own dispatch's
FaultNotice under its own handler id. It used to return rc 0 with the continuation's fault reported
under the window's id, and the window's own synchronous fault was lost." That is what item 3 asks
for. It changes which dispatch a fault is charged to, and which rc a shell sees in that scenario.
It does not change the struct, an export or the vocabulary. This item's words are those three,
verified by diffing, so the change is recorded here for visibility. It does not narrow the item.

## 11. Every new pin conforms to the pin standard — **NOT MET**

> **Every new pin conforms to [`docs/pin-standard.md`](../pin-standard.md)** on Rules 2–5 and 7. Its
> mutations are recorded, **including its anchors**, and its register row or census cell is written
> **in the phase that adds it**. Both are lessons from the 16.8 re-audit.

### 11a. The register range

```bash
grep -n "#### The register" docs/pin-standard.md                                   # 396
awk 'NR>396 && /^\| /{print NR; exit}' docs/pin-standard.md                          # 489
awk 'NR>=396 && NR<=569 && /^\|/{last=NR} END{print last}' docs/pin-standard.md      # 565
grep -n "^## Rule 7" docs/pin-standard.md                                           # 569
git blame -s -L 489,565 origin/main -- docs/pin-standard.md
```

`docs/pin-standard.md` is identical on HEAD and on the audit branch. The register's heading is at
`:396`. Its first table row is at `:489`, the header of the 17.1 sub-table. The main table runs from
its header at `:502` to its last row at `:565`. M17 wrote these rows, each blamed to its own
phase's squash:

| lines | written by | rows |
|---|---|---|
| `:489`–`:500` | `00976fd7` 17.1 | the 17.1 sub-table, one row per 17.1 fact, and its closing paragraph |
| `:535` | `6aa68096` 17.3 | JVM `RetireLateContinuationTest.kt` |
| `:546`, `:547` | `41af910c` 17.4 | `SlowHandlerWarningTests`, `SlowHandlerProbeTest.kt` |
| `:550` | `31af87ce` 17.2 | `AndroidHostCallContractTest.kt` |
| `:551`, `:552` | `6aa68096` 17.3 | `RecreationTests`, `RecreationAndroidTest.kt` |
| `:553`–`:558` | `41af910c` 17.4 | `ForwardingHookTests`, `SlowHandlerWarningTests` 17.4 facts, `LogPrivacyDriftTests`, `BnShellLogTest.kt`, `BnDiagnosticsTest.kt`, `FaultNoticeLogAndroidTest.kt` |
| `:559`–`:565` | `2f889244` 17.5 | `GeneratorRunTests` (3 rows), `NativeRuntimeInputDriftTests`, `PollUntilDriftTests`, `PollAndroidTest.kt`, the Swift assertion |

**Every M17 pin has a register row, written in its own phase.** That half of the item's last clause
holds. What the rows say is a different matter; see 11d.

### 11b. The population and its reds

The population is the test methods each M17 PR added or flipped, read from
`git show <squash> -- tests src/BlazorNative.Jni/src/test src/BlazorNative.Jni/src/androidTest src/BlazorNative.Apple templates`:
- #503 (17.1): 7;
- #507 (17.2): 5;
- #510 (17.3): 15;
- #514, test infrastructure: none, it adds a helper only;
- #523 (17.4): 45;
- #528 (17.5): 12, plus one Swift assertion whose message changed.

That is **85 rows**. Pre-M17 pins that M17 only modified are outside it.

**Baselines for "moved".** The records were written on PR branches that were squash-merged, so each
pin's diff runs from its phase's squash to HEAD. Where the test file or a subject file changed
after the red was taken, the recorded mutation was re-run on HEAD (design, Approach 5):
- **G1, 17.1:** `NativeRenderer.cs` 118 lines, `HostSession.cs` 113, `Exports.cs` 6. Re-red.
- **G2, 17.2:** `AndroidShellBridge.kt` 9 lines, `jni/ShellBridge.kt` 12. Re-red on device.
- **G3a, 17.3 .NET:** `NativeRenderer.cs` 54, `Exports.cs` 2. Re-red.
- **G3b, 17.3 Android:** `RecreationAndroidTest.kt` 14, `MainActivity.kt` 68, `WidgetMapper.kt` 98,
  `BlazorNativeRuntime.kt` 15. Re-red on device.
- **G3c, 17.3 JVM:** `BlazorNativeRuntime.kt` 15. Re-red through draft PR #535.
- **G4, 17.4:** 0 everywhere, except `FaultNoticeLogAndroidTest.kt`, −11 lines when 17.5 removed its
  private `pollUntil`. Device re-red for that one.
- **G5, 17.5:** no 17.5 test or subject changed; only CI, coverage and a testhelpers bump. The reds
  stand.

`R1`–`R5` are the five records:
- `docs/plans/2026-10-14-phase-17.1-record.md`;
- `…2026-10-15-phase-17.2-record.md`;
- `…2026-10-15-phase-17.3-record.md`;
- `…2026-10-16-phase-17.4-record.md`;
- `…2026-10-16-phase-17.5-record.md`.

`PS` is `docs/pin-standard.md`. Lines are on HEAD.

| # | phase | test, file:line | lane | record | mutations, anchors | PS | verdict |
|---|---|---|---|---|---|---|---|
| 1 | 17.1 | `AnInlineContinuationsFault_IsItsOwnDispatchs_NotTheWindows`, `RT/FaultNoticeTests.cs:950` | .NET | R1 :25, :52 | M1; `RanInsideB` A1, A4 | :491 | re-redded (17.1-M1) |
| 2 | 17.1 | `AnInlineContinuationsFault_LeavesTheWindowsOwnFault_Rc2`, `:970` | .NET | R1 :26, :52 | M1; A1, A4 | :492 | re-redded (17.1-M1) |
| 3 | 17.1 | `ACallBegunByAnInlineContinuation_DoesNotMarkTheWindow`, `:989` | .NET | R1 :27, :52-53 | M1, M2; A1, A3 | :493 | re-redded (17.1-M1) |
| 4 | 17.1 | `AFaultWithNoDispatchFlow_InsideAWindow_IsTheWindows`, `:1007` | .NET | R1 :28, :56 | M5; A1, A2, A4 | :494 | re-redded (17.1-M5) |
| 5 | 17.1 | `AFinishedDispatchsFault_InsideAWindow_IsNotTheWindows`, `:1024` | .NET | R1 :29, :52, :55, :145-146 | M1, M4, R1, R2; A1, A4 | :495 | re-redded (17.1-M1) |
| 6 | 17.1 | `AFinishedDispatchsFault_InsideAWindow_IsLoggedInStrictModeToo`, `:1053` | .NET | R1 :140-146 | red first; R1, R2 | :496 | re-redded (17.1-M1) |
| 7 | 17.1 | `AChildsBegunCall_DuringTheReRender_MarksTheDispatch_SoALaterRenderFaultIsRc0`, `:796` | .NET | R1 :64-68, :76-77 | M7; begin anchor M6 | :497 | re-redded (17.1-M7) |
| 8 | 17.2 | `biometrics_begin_returns_before_the_check_runs`, `AT/AndroidHostCallContractTest.kt:118` | Android | R2 :32, :54, :59 | M1; `gateReachedForTest` A1 | :550 | re-redded (A1, run 39029565241) |
| 9 | 17.2 | `secure_storage_begin_returns_before_the_keystore_work_runs`, `:122` | Android | R2 :33, :55, :59 | M2; A1 | :550 | re-redded (A1, run 39029565241) |
| 10 | 17.2 | `secure_storage_keeps_request_order`, `:127` | Android | R2 :34, :56 | M3, one run, named as not proven deterministic | :550 | re-redded (M3 alone, run 39030272142) |
| 11 | 17.2 | `a_throw_on_the_executor_completes_error_never_a_hang`, `:141` | Android | R2 :35, :57 | M4 | :550 | re-redded (M4, run 39030947426) |
| 12 | 17.2 | `the_after_auth_work_runs_on_the_secure_storage_executor`, `:151` | Android | R2 :36, :58, :70-72 | M5, M6 | :550 | re-redded (M6, run 39029565241) |
| 13 | 17.3 | `ASecondMount_DisposesTheFirstRoot_SoItsLateContinuationRendersNothing`, `RT/RecreationTests.cs:106` | .NET | R3 :21, :50 | M1; `ContinuationRan` A1 | :551 | re-redded (17.3-M1) |
| 14 | 17.3 | `ASecondMount_DisposesTheFirstRoot`, `:128` | .NET | R3 :22, :50 | M1; `Instances` A3 | :551 | re-redded (17.3-M1) |
| 15 | 17.3 | `AReplacingMount_SendsTheNewShellNoRemovalFrameForTheOldRoot`, `:144` | .NET | R3 :65-67 | red first; M5 | :551 | re-redded (17.3-M5) |
| 16 | 17.3 | `ASecondMount_StaysOnTheCurrentPage_WithItsHistory`, `:170` | .NET | R3 :23, :51 | M2; **`CanGoBack` anchor, no row enters it** | :551 | re-redded (17.3-M2); anchor unredded |
| 17 | 17.3 | `ASecondMount_ByAFreshShellAtTheRoot_ShowsTheRootAndResetsHistory`, `:197` | .NET | R3 :82-85 | red first; M6 | :551 | re-redded (17.3-M6) |
| 18 | 17.3 | `ASecondMount_OfANamedPageByAFreshShell_ShowsItAndResetsHistory`, `:225` | .NET | R3 :101-103 | red first; M7 | :551 | re-redded (17.3-M7, M6) |
| 19 | 17.3 | `ANavigationFromADisposedRootsContinuation_IsDropped`, `:253` | .NET | R3 :24, :52-53, :150 | M3, M4, M11; `ReachedNavigate` A2 | :551 | re-redded (17.3-M11) |
| 20 | 17.3 | `ANavigationFromADisposedRootsLifecycleCode_IsDropped`, `:282` | .NET | R3 :128, :148, :150 | red first; M9, M11 | :551 | re-redded (17.3-M11) |
| 21 | 17.3 | `AFrameOfTheOldRoot_AfterANewShellRegisters_IsNotDeliveredToIt`, `:328` | .NET | R3 :129, :147, :149 | red first; M8, M10 | :551 | re-redded (17.3-M8) |
| 22 | 17.3 | `a_recreated_activity_never_shows_the_old_pages_frame`, `AT/RecreationAndroidTest.kt:49` | Android | R3 :112, :114 | L2c; **anchors per PS :552, no row enters them** | :552 | re-redded (L2c, run 39026325328); anchors unredded |
| 23 | 17.3 | `a_recreated_activity_stays_on_its_page_and_back_goes_to_the_parent`, `:90` | Android | R3 :111, :115 | **none**: L1 and L3 green with reasons; L4 and L5 red other tests | :552 does not name it | **FINDING: no recorded red; unnamed** |
| 24 | 17.3 | `a_recreated_deep_linked_activity_stays_on_the_page_it_navigated_to`, `:113` | Android | R3 :97-98, :116 | L4 | :552 | re-redded (L4, run 39027028534) |
| 25 | 17.3 | `a_recreated_activity_on_an_unrouted_page_keeps_that_page`, `:134` | Android | R3 :117, :131 | L5 | :552 | re-redded (L5, run 39027930371) |
| 26 | 17.3 | `a_retired_runtime_drops_a_frame_from_a_late_continuation`, `JT/jni/RetireLateContinuationTest.kt:29` | JVM, JNA | R3 :110 | J1; vacuous first run 38709382575 | :535 | re-redded (J1, PR #535 run 39031400796) |
| 27 | 17.3 | `retire_waits_out_a_frame_callback_already_in_flight`, `:81` | JVM, JNA | R3 :118, :129 | J2; vacuous first run 38715305230 | :535 | re-redded (J2, PR #535 run 39032029220) |
| 28 | 17.4 | `TwoBnInputs_WithDifferentSlowHandlers_GetTwoWarnings_NamingTheAppsHandlers`, `RT/SlowHandlerWarningTests.cs:612` | .NET | R4 :23, :86-87, :90, :94 | M1, M2; `RunsOf` A1 | :554, :546 | red stands |
| 29 | 17.4 | `ASlowBnCheckbox_NamesTheAppsHandler`, `:648` | .NET | R4 :24, :86-87, :110 | M1, M2; A2 | :554 | red stands |
| 30 | 17.4 | `ASlowBnPicker_NamesTheAppsHandler`, `:656` | .NET | R4 :25, :86-87 | M1, M2 | :554 | red stands |
| 31 | 17.4 | `ASlowBnSlider_NamesTheAppsHandler`, `:664` | .NET | R4 :26, :86-87 | M1, M2 | :554 | red stands |
| 32 | 17.4 | `ASlowBnSwitch_NamesTheAppsHandler`, `:672` | .NET | R4 :27, :86-87 | M1, M2 | :554 | red stands |
| 33 | 17.4 | `ASlowBnModalDismiss_NamesTheAppsHandler`, `:680` | .NET | R4 :28, :86-87 | M1, M2 | :554 | red stands |
| 34 | 17.4 | `AWrapperWithNoAppCallback_StillKeysByItsCallSite`, `:691` | .NET | R4 :31, :109 | M5 | :554 | red stands |
| 35 | 17.4 | `AHookThatThrows_NeverFaultsTheDispatch_AndTheWarningFallsBackToTheTreeOwner`, `:713` | .NET | R4 :189 | red first only | :554 | red stands, red-first only |
| 36 | 17.4 | `TwoBoundBnInputs_OnTwoPages_GetTwoWarnings_NamingThePages`, `:732` | .NET | R4 :29, :46, :96 | M1, M2, A1 | :554 | red stands |
| 37 | 17.4 | `TwoBoundBnInputs_OnOnePage_GetTwoWarnings`, `:758` | .NET | R4 :30, :41, :46, :96 | M1, M2, A1 | :554 | red stands |
| 38 | 17.4 | `TwoPlainBoundBnInputs_OnOnePage_GetTwoWarnings_NamingThePage`, `:778` | .NET | R4 :48, :108, :120 | M2 | :554 | red stands |
| 39 | 17.4 | `AGetSetBoundBnInput_NamesTheAppsSetMethod`, `:797` | .NET | R4 :48, :108, :121 | M2 | :554 | red stands |
| 40 | 17.4 | `EventCallbackDelegate_ReturnsTheAppsMethod`, `RT/ForwardingHookTests.cs:30` | .NET | R4 :108, :116 | M2 | :553 | red stands |
| 41 | 17.4 | `EventCallbackDelegate_IsNullForAnEmptyCallback`, `:38` | .NET | none | **none**; M2 keeps it green; R4 :109's M5 reds a different class | :553 "M5 reds the null guard" | **FINDING: no recorded red; unnamed; cell overstates** |
| 42 | 17.4 | `EventCallbackDelegate_WorksForBool`, `:44` | .NET | R4 :116 | M2 | :553 | red stands |
| 43 | 17.4 | `EventCallbackDelegate_WorksForInt`, `:53` | .NET | R4 :116 | M2 | :553 | red stands |
| 44 | 17.4 | `EventCallbackDelegate_WorksForFloat`, `:62` | .NET | R4 :116 | M2 | :553 | red stands |
| 45 | 17.4 | `TheHook_IsNullForADelegateThatIsNotTheWrappersOwn`, `:74` | .NET | R4 :48, :111-112, :129-130, :193 | M6a, M6b, M7a, M7b, M8 | :553 | red stands |
| 46 | 17.4 | `NoAndroidLog_OutsideBnShellLog`, `RT/LogPrivacyDriftTests.cs:108` | .NET | R4 :69, :141 | M6 | :555 | red stands |
| 47 | 17.4 | `TheAndroidLogScan_FindsAPlantedRawLogE`, `:132` | .NET | R4 :70 | red first at its clean-file precondition only | :555 | red stands, precondition only |
| 48 | 17.4 | `NoSystemErr_OutsideBnDiagnostics`, `:145` | .NET | R4 :71 | red first | :555 | red stands |
| 49 | 17.4 | `TheSystemErrScan_FindsAPlantedWrite`, `:167` | .NET | R4 :72 | red first at its precondition only | :555 | red stands, precondition only |
| 50 | 17.4 | `NoOtherConsoleWriter_OutsidePreviewHost`, `:178` | .NET | R4 :190 | C1 | :555 | red stands |
| 51 | 17.4 | `TheConsoleWriterScan_FindsEachPlantedWrite`, `:201` | .NET | R4 :190, added with no red | **none** | :555 names no gap | **FINDING: no recorded red; unnamed** |
| 52 | 17.4 | `EverySafeArgument_IsOnALiteralMessageWithNoTemplate`, `:226` | .NET | R4 :73 | red first, at its floor | :555 | red stands |
| 53 | 17.4 | `TheSafeCheck_FindsEachPlantedViolation`, `:252` | .NET | R4 :76, green before the fix | **none** | :555 | **FINDING: no recorded red; unnamed** |
| 54 | 17.4 | `BnShellLog_DefaultsEveryEntryPointToRedacted`, `:285` | .NET | R4 :140 | M5 | :555 | red stands |
| 55 | 17.4 | `BnStderrLogcatPump_ForwardsEveryLineRedacted`, `:305` | .NET | R4 :76, green before the fix | **none**; J2 mutates the pump but ran only `BnShellLogTest` | :555 | **FINDING: no recorded red; unnamed** |
| 56 | 17.4 | `SwiftBnLog_DefaultsToRedacted_AndItsPumpTakesTheDefault`, `:329` | .NET | R4 :76, green before the fix | **none** | :555 | **FINDING: no recorded red; unnamed** |
| 57 | 17.4 | `MainActivity_SetsRevealFromDebuggable_BeforeThePump`, `:351` | .NET | R4 :74, :142 | M7 | :555 | red stands |
| 58 | 17.4 | `TheSharedJvmCodesReports_KeepTheirLevel`, `:406` | .NET | R4 :152-153, :163 | N1, N2, E1 | :555 | red stands |
| 59 | 17.4 | `an unrevealed REDACTED message is written as private, with its throwable dropped`, `JT/shell/BnShellLogTest.kt:210` | JVM | R4 :52-53, :171 | J1 | :556 | red stands |
| 60 | 17.4 | `an unrevealed SAFE message keeps its text and drops its throwable`, `:224` | JVM | compile error only | **none** | :556 "Named, not run" | **FINDING: no recorded red; named** |
| 61 | 17.4 | `a revealed line is written as today, text and throwable`, `:236` | JVM | compile error only | **none** | :556 named | **FINDING: no recorded red; named** |
| 62 | 17.4 | `redaction is the default privacy`, `:253` | JVM | R4 :171 | J1 | :556 | red stands |
| 63 | 17.4 | `info debug and verbose honour SAFE and stay level gated`, `:268` | JVM | compile error only | **none** | :556 named | **FINDING: no recorded red; named** |
| 64 | 17.4 | `warn and error are never gated by level`, `:282` | JVM | compile error only | **none** | :556 named | **FINDING: no recorded red; named** |
| 65 | 17.4 | `a throwing throwable sink is swallowed`, `:294` | JVM | compile error only | **none** | :556 named | **FINDING: no recorded red; named** |
| 66 | 17.4 | `a WARN diagnostic reaches the shell as WARN, and an ERROR one as ERROR`, `:301` | JVM | R4 :153, :171 | N2, J1 | :556 | red stands |
| 67 | 17.4 | `the pump forwards a record as private unrevealed and verbatim revealed`, `:325` | JVM | R4 :171-174 | J1, J2 | :556 | red stands |
| 68 | 17.4 | `the default sink writes to System err`, `JT/jni/BnDiagnosticsTest.kt:24` | JVM | compile error only | **none** | :557 "named, not run" | **FINDING: no recorded red; named** |
| 69 | 17.4 | `the sink is replaceable`, `:45` | JVM | compile error only | **none** | :557 named | **FINDING: no recorded red; named** |
| 70 | 17.4 | `BnPrivacy has exactly the two members`, `:59` | JVM | compile error only | **none recorded** | :557 "reds if a third privacy value is added" | **FINDING: no recorded red; the cell asserts one** |
| 71 | 17.4 | `a_slow_bncheckbox_callback_warns_naming_the_apps_method_not_the_wrapper`, `JT/jni/SlowHandlerProbeTest.kt:94` | JVM, JNA | R4 :189, :202 | **none** | :547 "Not mutated, named" | **FINDING: no recorded red; named** |
| 72 | 17.4 | `a_fault_notice_message_is_readable_in_a_debuggable_build`, `AT/FaultNoticeLogAndroidTest.kt:73` | Android | R4 :178-179 | L1a anchor, L1b message | :558 | re-redded (L1b, run 39028712698) |
| 73 | 17.5 | `AChildThatLeavesItsPipeHeld_FailsNamedWithinTheBound_AndLeavesNoProcess`, `RT/GeneratorRunTests.cs:57` | .NET | R5 :34-40, :96-99, :225 | M1, M2 (named), A1, M5 | :559 | red stands |
| 74 | 17.5 | `AScriptStillRunningAtTheBound_FailsSayingItWasStillRunning`, `:140` | .NET | R5 :206-210, :225 | red first; M5 | :560 | red stands |
| 75 | 17.5 | `AScriptThatExitsWithItsPipeHeld_FailsSayingItExited`, `:189` | .NET | R5 :206-210, :225 | red first; M5 | :560 | red stands |
| 76 | 17.5 | `GenerateReference_RunsEveryMSBuildCallWithNodeReuseOff`, `:301` | .NET | R5 :44-49, :98 | M3 | :561 | red stands |
| 77 | 17.5 | `GenerateReference_NodeReusePin_CatchesAPlantedCall`, `:330` | .NET | R5 :98 | M3, which reds only its switched-variant half | :561 | red stands, side-effect |
| 78 | 17.5 | `TheJvmTasksThatLoadTheRuntime_DeclareItAsAnInput`, `RT/NativeRuntimeInputDriftTests.cs:61` | .NET | R5 :53-54, :152-153, :172 | M4a, M4b | :562 | red stands |
| 79 | 17.5 | `TheTemplatesTestTask_DeclaresTheRuntimeAsAnInput`, `:66` | .NET | R5 :170-171, no red | **none** | :562 "Named, not run" | **FINDING: no recorded red; named** |
| 80 | 17.5 | `APlantedBlockWithoutTheInput_IsReported`, `:85` | .NET | R5 :55, passed | **none** | :562, the Rule 3 control only | **FINDING: no recorded red; unnamed** |
| 81 | 17.5 | `NoPrivatePollUntil_RemainsInAndroidTest`, `RT/PollUntilDriftTests.cs:35` | .NET | R5 :177-181, :190 | red first; planted copies | :563 | red stands |
| 82 | 17.5 | `APlantedPrivateCopy_IsReported`, `:52` | .NET | R5 :188-189 | **none** | :563, the Rule 3 control, no gap | **FINDING: no recorded red; unnamed** |
| 83 | 17.5 | `a_poll_on_a_destroyed_activity_fails_naming_it`, `AT/PollAndroidTest.kt:29` | Android | R5 :162-163, :235 | L1, re-run on the catch-only form | :564 | red stands |
| 84 | 17.5 | `a_predicate_that_throws_is_rethrown_unchanged`, `:48` | Android | R5 :164, :191 | L2 | :564 | red stands |
| 85 | 17.5 | Swift `BnGeolocationTests.testUnknownOpCompletesErrorNeverCrashes`, assertion `src/BlazorNative.Apple/BnHostTests/BnGeolocationTests.swift:219` | iOS | R5 :165 | S1, not specific: 6 tests red, named | :565 | red stands, named partial |

**Tally: 85 rows. 27 re-redded on HEAD today, 40 reds stand, and 18 have no recorded red.**

### 11c. The re-reds on HEAD

Local runs used one mutation at a time on a detached worktree of `980abdbf`, with
`dotnet test tests/BlazorNative.Runtime.Tests -c Debug --nologo --filter "<filter>"`. After each,
`git checkout -- <file>` restored it and `git status --short` was checked empty. Device runs used one
scratch commit per mutation on `scratch/17.6-<id>`, with `gh workflow run android-instrumented.yml
--ref …`, one run at a time. Each run's `headSha` was checked against its scratch commit, results
were read from the JUnit XML, and the ref was deleted afterwards. `git ls-remote origin
'refs/heads/scratch/*'` is empty.

| rows | id | mutation on HEAD | run | red |
|---|---|---|---|---|
| 1, 2, 3, 5, 6 | 17.1-M1 | `NativeRenderer.cs:634` `if (!anotherDispatchs)` → `if (true)` | local, `FaultNoticeTests`, 34 facts | 5 of 34, at the recorded messages |
| 4 | 17.1-M5 | `:631-633`, the null-flow case moved out of the window | local | 1 of 34: `rc was 0: a fault from code that flows no dispatch is the window's …` |
| 7 | 17.1-M7 | `:1642` `scope.ShellCallBegun = true;` → no-op | local | 5 of 34, as recorded |
| 13, 14 | 17.3-M1 | `HostSession.cs:626` `UnmountReplacedRoot` → `{ }` | local, 47 facts | 2 of 47 |
| 16 | 17.3-M2 | `:608`, the default re-resolve limited to `!replacing` | local | 2 of 47, as recorded |
| 15 | 17.3-M5 | `NativeRenderer.cs:501` `if (_dropDeliveryOfReplacedRoot)` → `if (false)` | local | 1 of 47 |
| 17, 18 | 17.3-M6 | `HostSession.cs:606-607`, the adopt removed | local | 2 of 47 |
| 18 | 17.3-M7 | the adopt limited to the default component | local | 1 of 47 |
| 21 | 17.3-M8 | `HostSession.cs:228`, the hold's `Volatile.Write` → no-op | local | 1 of 47 |
| 19, 20 | 17.3-M11 | `HostSession.cs:509` `RootGeneration++` removed | local | 2 of 47 |
| 22 | L2c | `HostSession.cs:626`, the replaced root not unmounted, the hold still released | 39026325328, attempt 1 | 1 of 245 |
| 24 | L4 | `MainActivity.kt:380`, `:389`, the old precedence | 39027028534, attempt 1 | 2 of 245, as recorded |
| 25 | L5 | `MainActivity.kt:389`, the restored route always picks the page | 39027930371, attempt 1 | 1 of 245 |
| 72 | L1b | `BnShellLog.kt:231`, the revealed branch writes `PRIVATE` | 39028712698, attempt 1 | 1 of 245, at the message; the anchor held, as recorded |
| 8, 9, 12 | 17.2 A1, M3 and M6, batched | `AndroidShellBridge.kt:690`, `:284`, `:293` | 39029565241, attempt 1 | 3 of 245; row 10 stayed green, since M3 is a race R2 :56 names |
| 10 | 17.2 M3 alone | `:284`, a pool of 2 | 39030272142, attempt 1 | 1 of 245 |
| 11 | 17.2 M4 | `runJob`'s catch rethrows, `:311-314` | 39030947426, attempt 1 | the test process crashed: 1 test, 1 failed, as recorded (R2 :57) |
| 26 | J1 | `BlazorNativeRuntime.kt:77`, the retired `return` removed, both copies | draft PR #535, `ci.yml` 39031400796, attempt 1 | 1 of 206 JVM; .NET 1372 green |
| 27 | J2 | `retire()`'s in-flight wait removed, `:569`, both copies | the same PR, 39032029220, attempt 1 | 1 of 206 JVM. PR #535 was then closed, its branch deleted, with no closing references |

**Every vacuity case recorded in M17 has a later red.**
- 17.3 J1, J2 and L2: their first runs passed vacuously, and later reds are recorded (R3 :110,
  :112, :114, :118). All three were re-redded today.
- 17.4 review round 2: the foreign-delegate fact and the JVM-codes fact (R4 :125, :159), redded by
  M7a/M7b/M8 and E1.
- 17.5 L1a (R5 :163): redded by L1 on the catch-only form, R5 :235.
- 17.2 pin 5's contaminated pre-fix red (R2 :36): honest reds M5 and M6.

None of these is open.

### 11d. The grade

**NOT MET.** The item has three clauses, and two of them fail on M17's own pins. Every finding
below is filed in one issue, **#538**.

**"Its mutations are recorded, including its anchors."**

**1. 18 of the 85 pins have no recorded red** on their own assertions. Under the audit's rule, a pin
with no recorded red on current code is **NOT MET**, whether or not it is named. The "MET NARROWLY"
reading is for a *partial*: a pin with a red that does not cover all of it, whose record names the
rest with a reason. These 18 have no red at all.

- **Nine are named** in their register cell as not run. The name does not rescue them:
  - `BnShellLogTest.kt` `:224`, `:236`, `:268`, `:282` and `:294`. Their only "red" is a compile
    error. PS :556 says "Named, not run" and gives no reason.
  - `BnDiagnosticsTest.kt` `:24` and `:45`. Compile error only, PS :557 "named, not run".
  - `SlowHandlerProbeTest.kt:94`. PS :547 says "Not mutated, named: it cannot run on Linux". That
    reason no longer holds: this audit redded two JNA pins on CI through draft PR #535, rows 26 and
    27.
  - `NativeRuntimeInputDriftTests.cs:66`. PS :562 says "Named, not run" and gives no reason.
- **Nine are unnamed.** No record and no register cell says they lack a red:
  1. `AT/RecreationAndroidTest.kt:90`, `a_recreated_activity_stays_on_its_page_and_back_goes_to_the_parent`.
     L1 and L3 left it green (R3 :111, :115), and L4 and L5 red other tests. PS :552 lists the reds
     of the other three tests and does not name this one.
  2. `RT/ForwardingHookTests.cs:38`, `EventCallbackDelegate_IsNullForAnEmptyCallback`. M2 keeps it
     green, and no row reds it.
  3. `RT/LogPrivacyDriftTests.cs:201`, `TheConsoleWriterScan_FindsEachPlantedWrite`. Added in the
     final wave with no red (R4 :190).
  4. `RT/LogPrivacyDriftTests.cs:252`, `TheSafeCheck_FindsEachPlantedViolation`. Green before the
     fix (R4 :76), never mutated.
  5. `RT/LogPrivacyDriftTests.cs:305`, `BnStderrLogcatPump_ForwardsEveryLineRedacted`. Green before
     the fix; J2 mutated the pump but ran only `BnShellLogTest` (R4 :172).
  6. `RT/LogPrivacyDriftTests.cs:329`, `SwiftBnLog_DefaultsToRedacted_AndItsPumpTakesTheDefault`.
     Green before the fix, never mutated. This is the iOS half of item 7's twin pin.
  7. `JT/jni/BnDiagnosticsTest.kt:59`, `BnPrivacy has exactly the two members`. Compile-error red
     only. PS :557 says it reds, but no record row or run shows that.
  8. `RT/NativeRuntimeInputDriftTests.cs:85`, `APlantedBlockWithoutTheInput_IsReported`. It passed
     before the fix (R5 :55) and was never mutated. PS :562 cites it only as the Rule 3 control.
  9. `RT/PollUntilDriftTests.cs:52`, `APlantedPrivateCopy_IsReported`. No mutation enters it.
     PS :563 cites it only as the Rule 3 control.

  PS :555 names none of findings 3–6 as a gap.

**2. 26 more pins have an anchor that no recorded mutation enters**, although each has a red on its
main assertion. This is the exact gap #494 was filed for in 16.7's pins.

How it was measured: `grep -n -i anchor` over every M17 test file, then each anchor checked against
its record's A rows, and against the scope and timing of every mutation that could reach it. A
mutation recorded before a fact was added does not count for that fact. Lines are on HEAD:

| rows | anchor, file:line | why no recorded mutation enters it |
|---|---|---|
| 6 | `RT/FaultNoticeTests.cs:1066`, `RanInsideB` | the fact was added in 17.1's final wave, after A1 and A4 ran (R1 :57, :60, :140) |
| 12 | `AT/AndroidHostCallContractTest.kt:161`, "the getWithAuth never reached the prompt" | 17.2's only A row, A1, gates the work in pins 8 and 9 (R2 :59) |
| 13, 19, 21 | `RT/RecreationTests.cs:101`, `ClickAndHold`'s geolocation-begin anchor, called at `:112`, `:263` and `:338` | 17.3's A1–A3 enter only `ContinuationRan`, `ReachedNavigate` and `Instances` (R3 :54-56) |
| 15 | `RT/RecreationTests.cs:155`, "the first mount delivered no frame" | the same |
| 16 | `:177`, `CanGoBack` | the same; R3 :23 says only that it passed |
| 17 | `:208`, `CanGoBack` | the same |
| 18 | `:237`, `CanGoBack` | the same |
| 20 | `:296` and `:304`, on `InitAwaitingProbe` | A2 sets `ReachedNavigate` on `AwaitingProbe`, a different probe |
| 21 | `:347`, "the old page's late continuation never rendered" | the same as `:155` |
| 22 | `AT/RecreationAndroidTest.kt:59` and `:73` | no L row enters them (R3 :111-117) |
| 24 | `AT/RecreationAndroidTest.kt:121`, "the swap to settings never completed" | the same |
| 26 | `JT/jni/RetireLateContinuationTest.kt:57`, "no frame reached the retired runtime" | J1's vacuous first run passed it, and J1's red is after it (R3 :110) |
| 27 | `JT/jni/RetireLateContinuationTest.kt:94`, "the mount frame never reached onFrame" | J2 reds after it (R3 :118) |
| 30, 31, 32, 33 | `RT/SlowHandlerWarningTests.cs:638`, `RunsOf(tag) == 1` in `AssertNamesTheAppsHandler`, for BnPicker, BnSlider, BnSwitch and BnModal (`:656-680`) | A1 drops only BnInput's invocation and A2 only BnCheckbox's (R4 :90, :110) |
| 35 | `RT/SlowHandlerWarningTests.cs:720`, `RunsOf("bn-check-throw")` | added in the final wave, after A2 (R4 :189) |
| 38 | `RT/SlowHandlerWarningTests.cs:784`, `RunsOf("bind-plain-a")` | added in review round 1, after A1 (R4 :48) |
| 39 | `RT/SlowHandlerWarningTests.cs:802`, `RunsOf("bind-get-set")` | the same |
| 66 | `JT/shell/BnShellLogTest.kt:315-319`, the revealed WARN line keeps its text | J1 changes only the unrevealed branch; N2 reds the first assertion, before it (R4 :153, :171) |
| 72 | `AT/FaultNoticeLogAndroidTest.kt:93` and `:96` | L1a enters only the `reveal` anchor, `:88` (R4 :178) |
| 74 | `RT/GeneratorRunTests.cs:160`, "the sleeper never recorded itself" | 17.5's A1 stops the launcher starting the sleeper, which is row 73's shape (R5 :99) |
| 75 | `RT/GeneratorRunTests.cs:210-214`, the stub's two roles and "the run returned before the anchor held" | the same |
| 83 | `AT/PollAndroidTest.kt:34`, "the scenario must be DESTROYED before the poll" | L1, L1a and L2 mutate `Poll.kt`, not the test's own precondition (R5 :162-164) |

That is 26 pins. Rows 23, 61 and 71 also have unentered anchors (`AT/RecreationAndroidTest.kt:94`,
`JT/shell/BnShellLogTest.kt:244` and `JT/jni/SlowHandlerProbeTest.kt:108`). They already count among
the 18 pins with no red at all.

**"Its register row or census cell is written in the phase that adds it."** Every row exists and
was written in its own phase (11a). Three cells claim more than their record:
- **PS :553**, `ForwardingHookTests`, **ambiguous**: "M5 reds the null guard." M5 has a red, R4
  :109, but in another class, `SlowHandlerWarningTests.AWrapperWithNoAppCallback_StillKeysByItsCallSite`.
  That red comes from the *renderer's* null guard in `Resolve`. Nothing reds this class's own null
  fact, `RT/ForwardingHookTests.cs:38`. Read in this cell, the sentence implies a red the class does
  not have.
- **PS :554**, `SlowHandlerWarningTests`' 17.4 facts: "every wrapper fact asserts `RunsOf == 1`, and
  A1 and A2 red that anchor." A1 and A2 red it only for BnInput and BnCheckbox. Rows 30–33 above
  have no anchor red.
- **PS :557**, `BnDiagnosticsTest.kt`: "The members test reds if a third privacy value is added." No
  record row and no run shows that red.

Two more cells read broader than their rows:
- PS :551, "A1 to A3 enter the anchors", leaves out the seven `RecreationTests` anchors above.
- PS :547 gives a reason, Linux, that this audit's CI route removes.

**Why the 16.8 fallback is not available.** In 16.8 §10c, the auditor named 16.7's unnamed partials
in the audit report and graded the item MET NARROWLY. This item was written to close that door. It
requires the cell to be written "in the phase that adds it", and the 16.8 re-audit is named as the
source of that lesson. An unnamed partial cannot be named now and count. And a pin with no red is
NOT MET whether it is named or not. So the 18 pins, the 26 pins' unentered anchors and the three
cells fail the item's words, and the item is **NOT MET**.

**Lesser notes, not counted as findings:**
- row 35's red is red-first only;
- rows 47 and 49 red only at their clean-file precondition, and no mutation enters their
  planted-hit assertions;
- row 77's red is a side effect on its switched-variant half;
- row 10's M3 is a race, named by its record;
- row 85's S1 is not specific, named;
- row 73's M2 discriminates only where `pwsh` is the global-tool shim, named.

---

## Carried forward

Open, judged and carried. Each line says whether the issue narrows a DoD item, and which. #537 and
#538 were filed by this audit.

| issue | title | judgement |
|---|---|---|
| #538 | m17 pins: no recorded red for 18 pins and many anchors, and register cells that claim more than their record | **fails item 11.** Also carries item 6's L1 wiring gap, the stale comment at `FaultNoticeTests.cs:408-412`, and the `MILESTONE.md:84` summary claim |
| #537 | android: a denied biometric prompt completes a secure-storage host call on the main thread | **narrows item 5.** It needs a decision: hop, or accept and reword |
| #500 | A re-render or queued work from another dispatch's continuation is charged to the window it runs in | **item 3: adjacent, not contradicting.** Item 3 is about a fault that A's *own code* raises. #500 is about a render or post-dispatch action A queues, which the contract assigns to the window that runs it (`Exports.cs:482-483`). It needs a decision |
| #506 | Android secure storage: an auth-bound set's encrypt runs after later requests on the same key | **item 5: adjacent, not contradicting.** The item does not speak to order across a prompt. It needs a decision. It does not cover #537's thread question, so #537 is separate |
| #511 | Android: an Activity recreated during boot can leave the new Activity blank | **narrows item 6.** `MainActivity` retires only a booted runtime |
| #522 | Slow-handler warning names a compiler lambda for a bound input | **narrows item 8.** With `:after`, the app's method is not named. It needs a decision. The plan put it against item 7; it concerns the slow-handler warning, not redaction |
| #530 | reference generator: something outlives dotnet publish and holds its output pipe | **narrows item 9.** The stall names the script, not the stalled command |
| #491 | The JVM dev loop runs on Windows only: Gradle hardcodes the win-x64 publish folder | **adjacent, item 2.** The reason 56 JNA tests cannot run locally on Linux, and why the JVM evidence is CI's. It predates M17 |

## What M17 taught

- **A register cell written in its phase is not therefore true.** 17.4 wrote its cells in-phase,
  as the M16 lesson asked. Three of them still claim more than their record (PS :553, :554, :557).
  The 16.8 lesson made the cells exist; it did not make them get checked against the record's rows.
- **"Named, not run" became an idiom, and a compile error was counted as a red.** Nine 17.4/17.5
  pins rest on a disclosure and nothing else. Seven of the JVM ones had only the Task 4 compile
  error as their "red" (R4 :52-53). A compile error shows the seam was missing. It does not show
  that any assertion can fail.
- **Anchors are still the last thing anyone mutates.** The M16 re-audit said so, and #494 fixed it
  for 16.7's pins. M17 left 26 pins with an anchor no mutation enters. Most were added after the
  phase's A rows ran, or share a helper whose other callers the A row did not reach.
- **Green-before-the-fix facts never got their mutation.** Three `LogPrivacyDriftTests` facts were
  green before 17.4's fix (R4 :76), and that is where they stayed. A fact that cannot red first
  needs a mutation by design, and this phase recorded none for them.
- **Blind device pins are still the hardest case, and the records caught them.** 17.3's L2 ran
  241/0 with the .NET unmount removed, because the mapper ignores unknown ids (R3 :112). J1 and J2
  passed vacuously on their first runs (R3 :110, :118). Each was fixed and re-proven, and all three
  went red again today.
- **A measurement can travel as an inference.** 17.5 first wrote #530's node-reuse state from a
  `~/.bashrc` timestamp. It was corrected from the dumps' environment blocks (#530's body). This
  audit's own local runs read that variable from the launching shell, not from each test process,
  so they too say nothing about node reuse.

## Verdict

**FAIL.** Item 11 is NOT MET, so M17 cannot close. Five items are MET: 1, 2, 3, 7 and 10. Five are
MET NARROWLY, each with its reason:
- **item 4:** one #494 entry, the cancellation rc assertion, is named rather than redded;
- **item 5:** a denied prompt completes a secure-storage request on main, found by reading (#537);
- **item 6:** #511, and no device test reds without `MainActivity`'s `retire()` (17.3 L1, named;
  #538);
- **item 8:** #522;
- **item 9:** #530.

Item 11 fails on M17's own words, as tracked in #538:
- 18 of 85 new pins have no recorded red, 9 of them named nowhere;
- 26 more pins have an anchor no recorded mutation enters;
- three register cells claim more than their record.

The 16.8 re-audit could name its partials in the report and grade MET NARROWLY. M17's DoD removed
that option on purpose: the cell must be written "in the phase that adds it". In any case, a pin
with no red is not a partial. The other items are graded on their own words, and the FAIL does not
change them.

**What a gap phase must do**, all tracked in **#538** unless noted:
1. **Record a red for each of the 18 pins** with none, and write it in that pin's register cell.
   That means the nine unnamed in 11d and the nine named. A name is not a red, and neither is a
   compile error.
   - The JNA pins, `SlowHandlerProbeTest.kt:94` among them, go through a throwaway draft PR on CI,
     as this audit's J1 and J2 did.
   - The device pins go through scratch refs on the Android lane.
   - A pin that truly cannot red is reworked until it can, or removed.
2. **Record a red for each of the 26 unentered anchors** in 11d's table. For each, write a mutation
   that makes its scenario not happen, not one that breaks the main assertion.
3. **Correct the three cells**: PS :553, what M5 reds; PS :554, which wrappers A1 and A2 cover; and
   PS :557, the members test's claimed red. Also correct PS :551's anchor claim and PS :547's
   Linux reason. Either run the mutation or reword the cell to what the record shows.
4. **Item 6's wiring:** find a device test that reds without `retire()` at `MainActivity.kt:470` and
   the template's `:483`, or drop the "proven on the device lane" claim for the wiring, with the
   reason that the .NET unmount makes it unobservable.
5. **In passing:**
   - reword the DOES NOT COVER note at `FaultNoticeTests.cs:408-412` to point at its pin, `:796`;
   - correct `MILESTONE.md:84`'s "all #494 anchors redded";
   - **#537:** the denied-prompt completion thread, `AndroidShellBridge.kt:850`, `:909` and
     `:953-966`, in the reference shell and the byte-identical template copy. Hop it to the
     executor, or put the wording question to the owner.

Next: `plan-milestone-gaps`. M17 stays open, and no tag is cut: `CONVENTIONS.md` records
`Milestone completion tags a release: no`.
