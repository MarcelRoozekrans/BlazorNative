# Milestone 16 audit — Unblock the Dispatch Lane

**Date:** 2026-10-01
**Auditor:** phase 16.5
**Milestone:** [M16](../planning/MILESTONE.md) · opened at `3757d0e` (#421)
**Measured against:** `origin/main` at `0779982dd1800a1ad7046119e8c3949ea2f7cf25`, fetched at the time of writing
**Spec:** [`docs/superpowers/specs/2026-10-01-phase-16.5-design.md`](../superpowers/specs/2026-10-01-phase-16.5-design.md)
**Verdict:** **FAIL** — seven criteria MET, two MET NARROWLY, one NOT MET: item 10, the pin
standard. See [the verdict](#verdict).

Every number below was measured for this audit, and the command that reproduces it sits beside it.
None was copied from a phase record, a ROADMAP outcome block or `STATE.md`, with one stated
exception: the test counts and lane runs for item 2 come from the controller's hand-off for this
phase, which ran them today on `0779982`. I re-read those runs' `headSha` myself. Phase records are
cited only as the place where a mutation is recorded, and I spot-checked what they claim.

Commands assume Git Bash on Windows, so `export MSYS_NO_PATHCONV=1` is needed before any
`git show <rev>:<path>`. This branch, `chore/16.5-audit-and-close`, differs from `origin/main` only
under `docs/` (`git diff --stat origin/main HEAD -- . ':!docs'` is empty), so a local run on the
branch is a run on `main`'s code.

---

## Scorecard

| # | DoD item | Verdict |
|---|---|---|
| 1 | All planned phases complete | **MET** |
| 2 | All tests passing, both device lanes dispatched, `headSha` compared | **MET NARROWLY** — #454, a local hang in a pre-M16 fixture |
| 3 | #345 is fixed: the lane pin flipped, the comment corrected first | **MET** |
| 4 | #346 is fixed: the JVM test flipped, no main-thread caller blocks, an iOS twin | **MET** — #440 adjacent, not contradicting |
| 5 | One thread owns the renderer, and a pin proves it | **MET** — #425 adjacent, not contradicting |
| 6 | The sync-mount contract survives | **MET** |
| 7 | #8 is fixed: late faults reach `onError`; the rc contract written once and agreed | **MET NARROWLY** — #455; #440 adjacent, not contradicting |
| 8 | #9 is re-assessed on measurement | **MET** |
| 9 | No ABI change; the new notice ops generated from `src/wire-vocabulary.json` | **MET** |
| 10 | Every new pin conforms to the pin standard on Rules 2–5 and 7, mutations recorded | **NOT MET** — 14 new pins have no recorded mutation. A further 23 were never assessed on Rules 2–5 by any record or by this audit; they are carried into the gap plan, not counted as failing |

---

## 1. All planned phases complete — **MET**

```bash
git fetch origin
git show origin/main:docs/planning/ROADMAP.md  | grep -n "#### Phase 16\."
git show origin/main:docs/planning/MILESTONE.md | grep -n "Phase 16\.[0-9] —"
git log --oneline 3757d0e..origin/main
```

| phase | ROADMAP on `origin/main` | MILESTONE on `origin/main` | merged as |
|---|---|---|---|
| 16.0 the render-thread spike | complete, `:3149` | complete | `f57d649` #422, `8fadff1` #423 |
| 16.1 the render thread | complete, `:3176` | complete | `5634ed1` #428, `3a9eb62` #430 |
| 16.2 back and navigation off the main thread | complete, `:3359` | complete | `f249b7b` #431, `ece89f8` #432 |
| 16.3 starvation, measured | complete, `:3536` | complete | `1397cf8` #437, `ed9a281` #439 |
| 16.4 the lost first tap | complete, `:3637` | complete | `3f432a3` #449, `4fefc17` #451 |
| 16.5 audit and close | pending on `main`, active on this branch | same | this audit |

Every phase before this one is complete on `main`. 16.5 is this audit, so it cannot be complete
until the report exists; M14 and M15 treated their audit phase the same way. The other commits in
the range are Renovate dependency bumps and one release-please commit, listed under item 9.

## 2. All tests passing, both device lanes dispatched, `headSha` compared — **MET NARROWLY**

All figures are on `main`'s HEAD `0779982`, from the controller's hand-off for this phase,
`.superpowers/16.5-handoff.md` (git-ignored). Both lanes were dispatched once each, attempt 1, with
no rerun.

| suite | count | evidence |
|---|---|---|
| .NET | **1295** passed, 0 failed, 0 skipped: Analyzers 27, Renderer 147, Runtime 1121 | local `dotnet test BlazorNative.sln -c Debug --blame-hang --blame-hang-timeout 6m --blame-hang-dump-type full`, exit 0, 46 s, the fifth of five runs; see the hang below. `ci` push run 36819304073 on `0779982`: the step ".NET tests (assert 1295 passed / 0 skipped)" succeeded |
| JVM | **191** passed, 0 failures, 0 errors, 0 skipped, 33 suites | local `./gradlew.bat --no-daemon testDebugUnitTest --rerun-tasks` under JDK 21.0.11, rc 0. It ran against a win-x64 dll published today: the first publish failed with rc 1 because `vswhere.exe` was off `PATH`, and its stale 2026-09-27 dll was **not** used. The clean republish exited 0 with 4 IL2072, and its dll is stamped 2026-10-01 13:17 local, after HEAD's 07:20 local commit time, so #424's stale-dll trap did not apply. The same `ci` run: the step "JVM tests … assert 191 passed / 0 failed" succeeded |
| Android instrumented | **233** passed, 0 failed | dispatched [36846545820](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36846545820), `workflow_dispatch`, `headSha` `0779982dd1800a1ad7046119e8c3949ea2f7cf25`, success; "Instrumented totals across 1 suite(s): tests=233 failures=0 errors=0 skipped=0" |
| iOS XCTest | **288** passed, 0 failed | dispatched [36846551321](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/36846551321), `workflow_dispatch`, `headSha` `0779982dd1800a1ad7046119e8c3949ea2f7cf25`, success; "XCTest cases: passed=288 failed=0". Every `BnNotificationsTests` case passed, so #444 did not fire |

```bash
gh run view 36846545820 --json headSha,conclusion,event,workflowName,attempt   # 0779982…, success, workflow_dispatch, android-instrumented, 1
gh run view 36846551321 --json headSha,conclusion,event,workflowName,attempt   # 0779982…, success, workflow_dispatch, ios, 1
gh run list --commit 0779982dd1800a1ad7046119e8c3949ea2f7cf25 --json databaseId,workflowName,event,conclusion,headSha
gh run view 36819304073 --json headSha,jobs --jq '.jobs[].steps[] | select(.name|test("assert|JVM";"i")) | "\(.name): \(.conclusion)"'
git show origin/main:.github/workflows/ci.yml | grep -n "1295\|191 passed"                   # :2323, :2341, :2727
git show origin/main:.github/workflows/android-instrumented.yml | grep -n "233 passed"       # :1117
git show origin/main:.github/workflows/ios.yml | grep -n "288"                               # :577, :1434
```

`gh run list --commit 0779982…` returns six runs on HEAD, all success: the two dispatches above, the
`ci`, `ios` and `release-please` push runs, and a scheduled `android-instrumented` run, 36845032155.

**The S1 margin, recorded as the owner's acceptance requires.** 16.4 left a known residual: a cold
`LAContext` start on the iOS simulator can delay the biometrics boot test's first tap. The 16.4
record logged one at **25681 ms** against the boot tests' **30 s** wait, 4.3 s inside it, and 2 of 10
verification runs above the 5000 ms warning threshold
(`grep -n "25681\|30 s" docs/plans/2026-09-28-phase-16.4-record.md`: `:395`, `:412`). A pre-fix run
logged 33381 ms (`:222`). The owner confirmed acceptance on 2026-10-01. It is tracked as **#453**,
filed for this audit. Today's iOS run logged no "slow LAContext" warning line, so no cold start above
5000 ms occurred on it. One green run says nothing about the margin's width; #453 is what keeps it
visible.

**Why MET NARROWLY: #454.** The local .NET suite did not finish in **2 of 5 runs** today. Run 1
produced nothing from `BlazorNative.Runtime.Tests` for about an hour and was killed. Run 2's hang
detector fired after 1108 tests had passed. Runs 3 to 5 passed, and run 5 is the counted run.
The dumps place the stall in `ReferenceFixtureBase`'s constructor
(`tests/BlazorNative.Runtime.Tests/ReferenceDriftTests.cs`), which waits with no timeout on
`pwsh scripts/generate-reference.ps1`. `pwsh` in turn is in `NativeCommandProcessor.Complete`,
waiting for EOF from a native command that had already exited.

I weighed three things before deciding it narrows the item rather than leaving it untouched:

- **It is not an M16 defect.** Neither file changed in the range
  (`git log --oneline 3757d0e..origin/main -- tests/BlazorNative.Runtime.Tests/ReferenceDriftTests.cs scripts/generate-reference.ps1`
  is empty). The fixture dates from #230 and earlier. The stall is in `pwsh`'s native-command wait,
  not in the render thread or any export.
- **CI runs the suite green** on HEAD, run 36819304073, so the 13 reference-drift tests do pass when
  they run.
- **But the item says "all tests passing", and in two of five local runs those 13 tests never ran
  and the suite never reported.** A hang is not a failure, but it is not a pass either. The spec
  treats a #444 hit, another flake that is not M16's fault, as narrowing this item. #454 is the same
  kind of fact about the local half of the evidence, and I record it the same way rather than
  counting only the run that worked.

The item holds on the counted run and on CI. It is narrowed because a local `dotnet test` is not yet
reliably conclusive. #454 stays open; its fix bounds the wait so a stall becomes a named failure.

## 3. #345 is fixed — **MET**

**The pin is flipped, not deleted.** The file still exists, and its one fact asserts that the export
**returns** while the host call is open:

```bash
git log --follow --format='%h %ad %s' --date=short origin/main -- tests/BlazorNative.Runtime.Tests/DispatchLaneBlockingTests.cs
#   5634ed1 2026-09-27 feat(16.1): give the renderer its own thread, ... (#428)
#   b32cdbe 2026-09-21 fix(14.1): apply the final review wave that missed the merge (#349)
#   31057e9 2026-09-21 fix(ios): split the dispatch twins ... (#339) (#347)
git show 3757d0e:tests/BlazorNative.Runtime.Tests/DispatchLaneBlockingTests.cs | grep -n "public void\|Assert.False"
#   :68  AnAsyncHandlerAwaitingAnOpenHostCall_STILL_BlocksTheDispatchLane
#   :111 Assert.False(returned.Wait(Budget),
git show origin/main:tests/BlazorNative.Runtime.Tests/DispatchLaneBlockingTests.cs | grep -n "public void\|Assert"
```

One history, created in #347 and modified in #428, with no delete-and-recreate. At `origin/main` the
fact is `AnAsyncHandlerAwaitingAnOpenHostCall_ReturnsWhileTheCallIsOpen` (`:67`), and its assertions
are:

```csharp
Assert.True(returned.Wait(TimeSpan.FromSeconds(1)),
    "dispatch_event did NOT return within 1s while the host call was still open " ...);   // :113
Assert.Equal(0, rc);                                                                         // :118
Assert.True(FakeShellHost.LastHostCallRequestId >= 0, ...);                                  // :122, the anchor
```

It sets `FakeShellHost.AutoCompleteHostCall = false` first, so the call really is open.

**Spot-check, run for this audit.** Making the export wait for the whole handler again, by adding
`outcome.Pending!.GetAwaiter().GetResult()` in the `Pending` arm of `Exports.DispatchEventCore`, reds
the pin with "dispatch_event did NOT return within 1s while the host call was still open
(requestId=2)". Reverted with `git checkout`, and the pin is green again:

```bash
dotnet test tests/BlazorNative.Runtime.Tests --filter "FullyQualifiedName~DispatchLaneBlockingTests"
```

**The false comment was corrected as the first commit of the fix.** The comment at
`Exports.cs:504-506` on `3757d0e` read *"GetAwaiter().GetResult() is the sync contract, not a
blocking wait: the InlineDispatcher completed the work before the Task was handed back"*
(`git show 3757d0e:src/BlazorNative.Runtime/Exports.cs | sed -n 504,506p`). The repo squash-merges,
so the order is read from PR #428's own commits:

```bash
gh pr view 428 --json commits --jq '.commits[] | "\(.oid[0:7]) \(.messageHeadline)"'
git log --reverse --format='%h %s' -S "is the sync contract, not a blocking" 3757d0e..38a8a5a -- src/BlazorNative.Runtime/Exports.cs
#   f13bcd8 docs(16.1): correct the dispatch comment that called a blocking wait non-blocking
git log --reverse --format='%h' 3757d0e..38a8a5a -- src tests | head -3
#   f13bcd8, 55290d2, 64ced82
git show --stat f13bcd8     # src/BlazorNative.Runtime/Exports.cs | 7 ++++---, and nothing else
```

PR #428 has 22 commits. The first three, `0e7c558`, `17c4869` and `86faee4`, touch only
`docs/planning/` and `docs/superpowers/`: the renumbering, the 16.1 design and the 16.1 plan. The
fourth, `f13bcd8`, is the comment correction and touches only `Exports.cs`. It is the **first commit
that touches `src` or `tests`**, ahead of `55290d2`, which introduces the render thread. That is the
reading the phase recorded as its intent before the work began. The 16.1 plan's global constraint
says *"The first commit of the fix corrects the false comment at `Exports.cs:504-506` and changes
nothing else"*, and its file list says *"This is the comment only, and it is the first commit"*,
after the plan's own bookkeeping
(`git show origin/main:docs/superpowers/plans/2026-09-26-phase-16.1-render-thread.md | sed -n '20p;54p'`). `38a8a5a` is the PR head, still fetchable at `refs/pull/428/head`.

**GitHub.** #345 is closed, `COMPLETED`, at 2026-09-27T04:29:45Z, by hand with a comment citing
#428 / `5634ed1`, the flipped pin, the iOS twin and `f13bcd8`
(`gh issue view 345 --json state,stateReason,closedAt,comments`).

## 4. #346 is fixed — **MET**

**The JVM test is flipped.**

```bash
git log --format='%h %ad %s' --date=short -S still_deadlocks origin/main -- src/BlazorNative.Jni
#   5634ed1 2026-09-27 feat(16.1) ... (#428)      ← the flip removed the name
#   31057e9 2026-09-21 fix(ios): split the dispatch twins ... (#347)   ← the name was introduced
git grep -n "still_deadlocks\|fun dispatchHostEventAndWait_returns" origin/main -- src/BlazorNative.Jni
```

At `3757d0e` it was `dispatchHostEventAndWait_still_deadlocks_behind_a_held_dispatch_lane`
(`HostEventTest.kt:307`). At `origin/main` it is
`HostEventTest.dispatchHostEventAndWait_returns_while_a_handler_holds_a_host_call` (`:308`), and it
asserts:

```kotlin
assertTrue(
    backReturned.get(),
    "dispatchHostEventAndWait did NOT return within 2 s while an async handler held a " + ...)
assertEquals(null, backThrew, "the back dispatch must return, not throw")
assertTrue(host.heldRequestId >= 0, "the held camera request id was never recorded")   // anchor
```

**No main-thread caller blocks on .NET.** I enumerated every shell call into .NET from a thread
that can be main, on both shells, at `origin/main`:

```bash
grep -n "dispatchHostEvent\|onNewIntent\|fun handleBack\|OnBackPressedCallback" src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt
grep -rn "navigateDispatcher\|dispatchHostEvent" src/BlazorNative.Apple/BnHost/*.swift
grep -rn "dispatchEventBlocking\|dispatchEventAndWait(" src/BlazorNative.Apple/BnHost src/BlazorNative.Jni/src/main src/BlazorNative.Jni/src/androidMain templates/BlazorNative.Templates/content --include=*.kt --include=*.swift
```

| caller | shell | what it calls | blocks? |
|---|---|---|---|
| back, `handleBack` via the AndroidX `OnBackPressedCallback` | Android | `runtime.dispatchHostEvent(BnHostEvent.Back)`, `MainActivity.kt:621` | no: `dispatchLane.execute`, fire-and-forget (`BlazorNativeRuntime.kt:316-327`) |
| warm deep link, `onNewIntent` | Android | `dispatchHostEvent(BnHostEvent.Navigate, route)`, `:577` | no, same |
| lifecycle `onResume` / `onPause` / `onDestroy`, safe-area | Android | `dispatchHostEvent`, `:400`, `:405`, `:415`, `:503` | no, same |
| a tap, a change or a scroll | Android | `runtime.dispatchEvent(...)`, `:292`, `:300` | no: `dispatchLane.execute` (`BlazorNativeRuntime.kt:203-222`) |
| boot and mount | Android | `runtime.start`, on `thread(name = "BlazorNative-Runtime-Boot")`, `:348` | not main |
| deep-link and notification `navigateDispatcher` | iOS | `self?.dispatchHostEvent(.navigate, payload:)`, `BnRuntime.swift:319-328` | no: `dispatchLane.async` (`:349-358`) |
| lifecycle, safe-area | iOS | `dispatchHostEvent`, `BnAppLifecycle.swift:74`, `HostViewController.swift:88` | no, same |
| a tap or a scroll | iOS | `dispatchEvent(...)`, `BnRuntime.swift:155`, `:165` | no: `dispatchLane.async` (`:206-217`) |
| boot and mount | iOS | `runtime.start`, inside `DispatchQueue.global(qos: .userInitiated).async`, `HostViewController.swift:171-187` | not main |

The blocking entry points that remain are test seams or are off main by contract:
`dispatchHostEventAndWait`, which is `internal` in both shells and is the subject of the scan below;
`dispatchEventBlocking`, a test seam with no production caller (the third grep finds none outside
declarations and comments); and Kotlin's public `dispatchEventAndWait`, the Inspector's JVM-host-only
seam, called from HTTP handler threads.

**The caller scan 16.2 made test-only, run for this audit:**

```bash
dotnet test tests/BlazorNative.Runtime.Tests --filter "FullyQualifiedName~NoShippedShellSource_CallsTheBlockingHostEventDispatch|FullyQualifiedName~OffendingCallDetector_MatchesACall_AndNotTheDeclaration"
#   Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2
```

`GeneratedSymbolShadowTests.NoShippedShellSource_CallsTheBlockingHostEventDispatch` (`:1162`) scans
every production Kotlin source, both `MainActivity.kt` copies included, and every Swift file in
`BnHost/`, and fails on any call to `dispatchHostEventAndWait`. It anchors on three named files.
`OffendingCallDetector_MatchesACall_AndNotTheDeclaration` is its positive control.

**The iOS XCTest twin exists:**
`src/BlazorNative.Apple/BnHostTests/BnDispatchLaneTests.swift`, test
`testADispatchReturnsWhileAHostCallIsOpen` (`:47`), added by `5634ed1`
(`git log --format='%h %s' --diff-filter=A origin/main -- src/BlazorNative.Apple/BnHostTests/BnDispatchLaneTests.swift`).
It holds a camera capture in flight, dispatches Take Photo through `dispatchEventBlocking` on a
background queue, and asserts a return within 2 s (`:86`), `rc == 0` (`:95`), and the in-flight
anchor (`:99`). It ran green in today's iOS lane, 288/0. Item 10 records that it has never been run
red.

**GitHub.** #346 is closed, `COMPLETED`, at 2026-09-27T15:06:59Z, by hand with a comment citing
#431 / `f249b7b`, the JVM flip, the device test and its D10 mutation run, and the caller scan.

**#440 judged against this item: adjacent, not contradicting.** #440 says Android runs KeyStore and
`BiometricManager` work synchronously inside `hostCallBegin`, which since 16.1 runs on the .NET
render thread. That work holds the **render thread**, and through a dispatch's synchronous part the
**dispatch lane**. It does not hold **main**: every main-thread caller in the table above posts to
the lane and returns, and nothing on main waits for the lane or the render thread. The item's wording
is "no main-thread caller blocks on .NET", and that holds. #440 is a real defect against the
host-call contract, judged under item 7, and it stays open.

## 5. One thread owns the renderer, and a pin proves it — **MET**

**The DoD names `ReportIfNotTheRenderOwnerThread`. That method no longer exists by that name.**
16.1 renamed it to `AssertOnTheRenderThread` and made it throw:

```bash
git log -S ReportIfNotTheRenderOwnerThread --oneline 3757d0e..origin/main -- src     # 5634ed1 (#428); in the PR, 55290d2
grep -n "AssertOnTheRenderThread" -A20 src/BlazorNative.Renderer/NativeRenderer.cs
```

At `3757d0e` it logged at Warn under `StrictErrors` and at Debug otherwise, and never threw. At
`origin/main` (`NativeRenderer.cs:354-368`) it compares the current thread with `RenderThreadId`;
under `StrictErrors` it **throws** `InvalidOperationException`, otherwise it warns. It is called first
in `UpdateDisplayAsync` (`:380`), the one place a batch reaches the tree.

**It fails under test.**
`tests/BlazorNative.Renderer.Tests/RenderThreadWarningTests.cs:142`,
`ARenderFromANonOwnerThread_ThrowsUnderStrictErrors_AndNamesBothThreads`, flipped in 16.1 from
`…_WarnsUnderStrictErrors_…`:

```csharp
dispatcher.ClaimEveryThreadForTests = true;   // make CheckAccess() lie, as 13.2's inline dispatcher did
thrown = Assert.Throws<InvalidOperationException>(() => OnAnotherThread(() =>
{
    otherThreadId = Environment.CurrentManagedThreadId;
    renderer.TriggerRootRenderForTests(rootId);
}));
Assert.Contains($"driven from thread {otherThreadId}", thrown.Message, StringComparison.Ordinal);
Assert.Contains($"owned by thread {ownerThreadId}", thrown.Message, StringComparison.Ordinal);
```

Its positive control runs the same off-thread call without the lie, which is marshalled and does not
throw. `WithoutStrictErrors_TheSameConditionWarns` pins the other branch. The other half of the
item, "every renderer mutation happens on the render thread", is carried by `OnRenderThread`
marshalling at every mutating entry point (`grep -n "OnRenderThread" src/BlazorNative.Renderer/NativeRenderer.cs`:
mount `:217`, unmount `:258`, `RunAfterDispatch` `:288-290`, the root render `:701-703`, dispose
`:734`), and pinned by `RenderThreadDispatcherTests` (8 facts),
`MountSyncTests.Renderer_mounts_synchronously_on_its_render_thread`, which asserts the emitting
thread equals `RenderThreadId`, and `HostEventArmThreadTests` for every `host_event` arm.

**#425 judged against this item: adjacent, not contradicting.** `retire()` drains only the Kotlin
lane and never closes .NET's frame gate, so a continuation that resumes after `retire()` can deliver
a frame to a retired runtime's `onFrame` (`gh issue view 425`, pinned as a hazard by
`RetireLateContinuationTest.kt`). That frame is still **emitted on the render thread**: the
continuation resumes there, and `AssertOnTheRenderThread` would throw under test if it did not. The
defect is **when** and **to whom** the frame is delivered, not **where** it is emitted. The item's
wording, "every frame emission and every renderer mutation happens on the render thread", holds.
#425 stays open as a recreation-lifecycle defect.

## 6. The sync-mount contract survives — **MET**

```bash
grep -n "InlineDispatcher" tests/BlazorNative.Renderer.Tests/MountSyncTests.cs
#   :84, a comment only: "Flipped in 16.1: was Renderer_uses_inline_dispatcher_so_mount_chain_completes_synchronously,
#        asserting the type name "InlineDispatcher" and CheckAccess() == true on the calling thread."
grep -n "public void" tests/BlazorNative.Renderer.Tests/MountSyncTests.cs
grep -n "DisposeDuringInvokeAsync_DoesNotRecurse_TenRuns" tests/BlazorNative.Renderer.Tests/RenderThreadDispatcherTests.cs   # :309
```

The only `InlineDispatcher` in the file is that comment; no assertion names a type. The flipped fact,
`Renderer_mounts_synchronously_on_its_render_thread` (`:82`), pins the **behaviour**: `CheckAccess()`
is false on the test thread, `Mount` returns a non-negative id, at least one frame was emitted
before `Mount` returned, and the emitting thread is `renderer.RenderThreadId`. Its siblings still
pin the other half: `Mount_throws_when_component_has_async_lifecycle` (`:62`).

**13.2's `Dispose → InvokeAsync` recursion** is the named regression
`RenderThreadDispatcherTests.DisposeDuringInvokeAsync_DoesNotRecurse_TenRuns` (`:309`). Its probe's
`Dispose` calls `InvokeAsync(StateHasChanged)`; the fact mounts, unmounts and disposes ten
renderers on a dedicated thread with a bounded join, so a stack overflow is a failed join, not a
hang.

## 7. #8 is fixed — **MET NARROWLY**

**A fault after the first await reaches `onError`, on the JVM and on XCTest:**

- JVM: `FaultNoticeTest.a_fault_after_the_first_await_reaches_onError_and_the_notice_is_completed`
  (`src/BlazorNative.Jni/src/test/kotlin/io/blazornative/jni/FaultNoticeTest.kt:66`).
- XCTest: `BnFaultNoticeTests.testAFaultNoticeRoutesToOnErrorAndCompletesOk`
  (`src/BlazorNative.Apple/BnHostTests/BnFaultNoticeTests.swift:70`), with
  `testAnUnknownOpStillTakesTheErrorBranch_Control` as its control.
- .NET, the sending half: `FaultNoticeTests.AFaultAfterTheFirstAwait_SendsAFaultNotice`, in
  production mode.

Both device suites were green today. The XCTest half has no recorded mutation; item 10 counts that.

```bash
git grep -n "fun a_fault_after\|func test" origin/main -- 'src/BlazorNative.Jni/src/test/kotlin/**/FaultNoticeTest.kt' 'src/BlazorNative.Apple/BnHostTests/BnFaultNoticeTests.swift'
```

**The rc contract is written in one wording**, in two ABI places and quoted once in the app-author
guide:

```bash
git grep -n -i "rc reports the synchronous part" origin/main -- src website
#   src/BlazorNative.Runtime/Exports.cs:470
#   src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h:133
#   website/docs/guides/threading.md:39
diff <(grep -A4 "rc reports the SYNCHRONOUS" src/BlazorNative.Runtime/Exports.cs | sed 's#^\s*///\s\?##') \
     <(grep -A4 "rc reports the SYNCHRONOUS" src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h | sed 's#^//\s\?##')   # no output: identical
```

> rc reports the SYNCHRONOUS part of the handler: 0 = it ran and did not fault before its
> first await (the handler may still be running); 2 = it faulted before yielding. A fault
> after the first await is delivered later through the FaultNotice host-call op, never as
> an rc. Frames from the synchronous part are delivered before this returns; frames from a
> continuation are delivered later, from the render thread.

The 16.1 design defines "written once" as one wording, placed in `BlazorNativeRuntimeC.h` and in
`Exports.cs`
(`grep -n "written once" docs/superpowers/specs/2026-09-26-phase-16.1-design.md`: `:77`). The two
copies are identical today.

**Every export's rc table, checked against the contract** (`Exports.cs` and `BlazorNativeRuntimeC.h`
at `origin/main`):

| export | `Exports.cs` | header | agrees? |
|---|---|---|---|
| `dispatch_event` | `:468-488`: 0 incl. a still-suspended handler / 1 / 2 the synchronous part faulted / 3 | `:133-139`: same, "2 faulted before yielding" | yes, with the defect below |
| `host_event` | `:801-824`; back `:870-876` and navigate `:933-940` add "if it ever yields, this reports rc 0 and a later fault is sent … as a FaultNotice" | `:214-218`: 0 / 1 / 2 / 3, "waits for its synchronous part" | yes. The header omits the "if it yields" sentence; an omission, not a contradiction |
| `mount` | `:370-375`: 0 / 1 / 2 / 3; the first render completes synchronously | `:114-117`: same | yes. Mount waits for completion by design (sync-mount contract) |
| `host_call_complete` | `:730-733`: 0 delivered / 1 unknown id / 2 internal bridge failure | `:202-203`: 0 / 1 / 2 "invalid call" | yes on meaning. The rc 2 label differs, and it differed already at `3757d0e`; it does not touch the contract |
| `fetch_complete` | `:665-669`: 0 / 1 / 2 | not declared; the Swift shell does not call it (`:20`) | n/a |
| `register_bridge`, `register_frame_callback` | `:633`, `:348`: 0 / 2 | `:190`, `:111` | yes; no handler code runs |
| `init`, `shutdown`, `version` | no rc table: a struct, `void`, a string | same | n/a |

**The basis of the narrowing.** The DoD's phrase, "rc reports the synchronous part", holds, and
every rc table agrees with the contract as written. The item is narrowed because the contract's
wording is false in substance for one path, under the spec's risk row *"A DoD item is met in letter
but not in substance"*.

**Why MET NARROWLY: #455, filed for this audit.** The contract's third sentence, "a fault after the
first await is delivered later through the FaultNotice host-call op, **never as an rc**", is false
whenever the awaited host call completes **inside** `hostCallBegin`. The `await` then finds a
completed Task and does not yield, so the throw after it is still in the synchronous part, and the
export returns rc 2. I measured it with a throwaway xUnit probe, not committed: a click handler that
does `await Bridge.CheckGeolocationPermissionAsync()` and then throws, dispatched through
`Exports.DispatchEventCore` with `FakeShellHost.AutoCompleteHostCall = true`. Result: **`rc=2`, 0
FaultNotice host calls**. `DispatchLaneBlockingTests.cs:121` already says as much in a comment: *"A
canned inline completion would leave nothing to await."*

This is the normal path on Android for every op that completes inside `hostCallBegin`: the geolocation
`check` arm (`AndroidShellBridge.kt:297-301`), the biometrics `check` arm (`:680`), and the plain
secure-storage arms (`:711-718`). The fault still reaches `onError`, because both shells report a
non-zero rc there (`BlazorNativeRuntime.kt:208-210`, `BnRuntime.swift:211-214`). So the DoD's quoted
phrase, "rc reports the synchronous part", holds, and the first sentence of this item holds. What
does not hold is the written contract's claim about **which channel** a post-await fault takes, and
that is the text a third-party shell author reads. The header's own Return line already uses the
right word, "faulted before **yielding**". #455 asks for the contract to say "yield" throughout, and
for a pin. Two smaller items found in the same place are grouped into #455:

- `website/docs/guides/threading.md:32-36` says the two copies are verbatim "so the two can't drift
  apart". Nothing enforces that; they agree today because they were written that way.
- The milestone's risk table promised the rc change would be "called out in the changelog as a
  behaviour change". The 0.17.0 entry in `CHANGELOG.md` lists the three features and says nothing
  about rc (`git show origin/main:CHANGELOG.md | sed -n 1,12p`).

**#440 judged against this item: adjacent, not contradicting.** #440 is that Android does KeyStore
and `BiometricManager` work synchronously inside `hostCallBegin`, on the render thread. That work is
part of the handler's synchronous part, and the rc reports it as such: a slow KeyStore call makes the
synchronous part longer and the rc arrives later, but the rc still describes exactly the synchronous
part. #440 does not make any rc table disagree with the contract, and a late fault still reaches
`onError`. Two of #440's arms, biometrics `check` and the plain secure-storage arms, are among the
inline completers that expose #455, but so is geolocation `check`, which #440 does not cover. #455
narrows this item; #440 on its own does not.

**GitHub.** #8 is closed, `COMPLETED`, at 2026-09-27T04:29:48Z, by hand with a comment citing #428 /
`5634ed1` and the pins above (`gh issue view 8 --json state,stateReason,closedAt,comments`).

**#438, also required by the spec to be closed and backed.** Spec item 3 lists #438 with #8, #9,
#345 and #346. It is the 16.4 issue, not a DoD item of its own, so it has no verdict line. It is
checked here because it was found inside M16's work.

```bash
gh issue view 438 --json state,stateReason,closedAt     # CLOSED, COMPLETED, 2026-10-01T04:19:22Z
git show --stat --format='%h %s' 3f432a3                  # fix(16.4): the lost first tap, ... (#449)
git diff 3f432a3^ 3f432a3 -- src/BlazorNative.Apple/BnHostTests | grep -E "^[+-]\s*func test"
```

It was closed by hand with a comment citing #449, merged as `3f432a3`. That commit adds **six**
XCTests. They are the `+` lines of the diff, except `testTheLAContextIsRetainedDuringEvaluationThenReleased`,
which is in both the `-` and the `+` lines because its signature only gained `throws`:

- `BnBiometricsTests.testTheBootHarnessWaitsForTheArmedReplyNotTheInFlightFlag`
- `BnBiometricsTests.testHostCallBeginReturnsWhileContextCreationIsBlocked`
- `BnBiometricsTests.testHostCallBeginReturnsWhileContextCreationIsBlockedForCheck`
- `BnSecureStorageTests.testHostCallBeginReturnsWhileContextCreationIsBlocked`
- `BnSecureStorageTests.testHostCallBeginReturnsWhileContextCreationIsBlockedForGetWithAuth`
- `BnSecureStorageTests.testACompletionDoesNotReleaseAnotherRequestsRetainedContext`

They are the 16.4 group in item 10. Their mutations are recorded one per lane run in the 16.4
record §8, and item 10 spot-checks those runs. #438 is closed and backed.

## 8. #9 is re-assessed on measurement — **MET**

**GitHub.** #9, "Hardening ledger: dispatch-lane starvation", is closed, `COMPLETED`, at
2026-09-27T21:10:26Z, by hand. The closing comment cites #437, merged as `1397cf8`, and the
measurement in `docs/plans/2026-09-27-phase-16.3-record.md` §1: a 1,000 ms synchronous part delayed
the next event by about 1,004.7 ms, one-for-one with no fixed overhead. It closes the item as
**resolved by detection plus a documented contract**: a once-per-call-site Warn above a 100 ms budget,
and `website/docs/guides/threading.md`. That is "fixed", with the residual named as #435. It was not
closed silently.

```bash
gh issue view 9 --json state,stateReason,closedAt,comments
```

**The measurement, re-run for this audit.** The command is cheap, about 14 s, so I re-ran it rather
than citing the record's numbers. I saved the record's appendix as
`tests/BlazorNative.Runtime.Tests/StarvationMeasureHarness.cs`, ran it, then deleted the file and
rebuilt. **The harness is throwaway: do not commit it. Delete it right after the run and rebuild,
or the next suite run counts one extra test.**

```bash
awk 'NR>243 && /^```csharp/{f=1;next} f&&/^```/{f=0} f' docs/plans/2026-09-27-phase-16.3-record.md > tests/BlazorNative.Runtime.Tests/StarvationMeasureHarness.cs
dotnet test tests/BlazorNative.Runtime.Tests --filter "Category=Measure16.3" --logger "console;verbosity=detailed"
rm tests/BlazorNative.Runtime.Tests/StarvationMeasureHarness.cs   # always; never commit it
dotnet build tests/BlazorNative.Runtime.Tests -v q
```

| sync part (ms) | dispatch sync time, median / max (ms) | second event behind it | `back` behind it |
|---:|---:|---:|---:|
| 0 | 0.2 / 0.2 | 0.2 / 0.2 | 0.1 / 0.1 |
| 10 | 15.5 / 19.7 | 15.2 / 19.2 | 15.3 / 15.9 |
| 50 | 63.0 / 63.6 | 62.8 / 63.4 | 62.3 / 63.1 |
| 200 | 204.9 / 207.8 | 206.4 / 207.4 | 204.5 / 205.2 |
| 1,000 | 1,012.7 / 1,015.6 | 1,012.3 / 1,015.4 | 1,012.7 / 1,014.3 |

Today, Windows 11 Pro 10.0.26200, .NET 10.0.12, Debug. The record's conclusion reproduces: the
second event and `back` finish within about a millisecond of the slow dispatch itself, so the cost
is one-for-one. The 1,000 ms row is 8 ms slower than the record's 1,004.7 ms, within this host's
scheduler noise. The slow-handler Warn fired during the run, at 203 ms, as it should.

## 9. No ABI change — **MET**

```bash
git diff --stat 3757d0e origin/main -- src/BlazorNative.Runtime/BridgeProtocolNative.cs src/BlazorNative.Runtime/Exports.cs src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h
#   BlazorNativeRuntimeC.h | 22 +-;  Exports.cs | 320 ++++--;  BridgeProtocolNative.cs: no diff
git diff 3757d0e origin/main -- src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h | grep "^[+-]" | grep -v "^[+-]//\|^+++\|^---"   # no output
```

**The 80-byte bridge struct.** `BridgeProtocolNative.cs` has **no diff** across the range. The
struct is `BlazorNativeBridgeCallbacks`, `// 10 × IntPtr = 80 bytes` (`:96`): `Navigate` 0,
`CurrentRoute` 8, `StorageRead` 16, `StorageWrite` 24, `StorageDelete` 32, `FetchBegin` 40,
`ClipboardRead` 48, `ClipboardWrite` 56, `Share` 64, `HostCallBegin` 72. The C mirror
`bn_bridge_callbacks` in `BlazorNativeRuntimeC.h:173-184` lists the same ten fields at the same
offsets, `// 80 bytes`. Every changed line in the header is a comment. No other `StructLayout` file
in the Runtime changed (`git grep -l StructLayout origin/main -- src/BlazorNative.Runtime`; only
`Exports.cs` is in the diff, and it changes no struct).

**The 10 exports.** The brief's literal count is 14, because the file mentions the attribute in four
doc comments, which `Exports.cs:129` itself warns about. Anchored on the attribute line, it is 10 at
both ends, with the same names and the same signatures:

```bash
git show <rev>:src/BlazorNative.Runtime/Exports.cs | grep -c UnmanagedCallersOnly                 # 14 at 3757d0e, 14 at origin/main
git show <rev>:src/BlazorNative.Runtime/Exports.cs | grep -cE '^\s*\[UnmanagedCallersOnly'        # 10 at both
git show <rev>:src/BlazorNative.Runtime/Exports.cs | grep -o 'EntryPoint *= *"[^"]*"' | sort      # diff: identical
git show <rev>:src/BlazorNative.Runtime/Exports.cs | grep -A1 -E '^\s*\[UnmanagedCallersOnly' | grep "public static"   # diff: identical
```

The ten: `dispatch_event`, `fetch_complete`, `host_call_complete`, `host_event`, `init`, `mount`,
`register_bridge`, `register_frame_callback`, `shutdown`, `version`, each prefixed `blazornative_`.
The hand-off adds the binary-level check: `dumpbin /exports` on today's win-x64 publish lists exactly
these ten `blazornative_*` exports.

**The new notice ops live in `src/wire-vocabulary.json` and are generated.** The manifest's host-call
op table has `FaultNotice` id 5 (`:178`), `BackState` id 6 (`:179`) and `BackUnhandled` id 7 (`:180`).
They appear in exactly the four generated host-call-op copies, `BnHostCallOps.g.cs`,
`BnWireVocabulary.g.kt`, its template mirror, and `BnWireVocabulary.g.swift`, three names each, and
nowhere else among the `.g.*` files. The generator says every copy is current, and the codegen pins
pass:

```bash
grep -n -i "faultnotice\|backstate\|backunhandled" src/wire-vocabulary.json
git ls-files '*.g.*' | while read f; do printf "%s: " "$f"; grep -c -i "faultnotice\|fault_notice\|backstate\|back_state\|backunhandled\|back_unhandled" "$f"; done
dotnet run --project tools/BlazorNative.WireGen -- --check            # "wire-vocabulary: all generated files are up to date."
dotnet test tests/BlazorNative.Runtime.Tests --filter FullyQualifiedName~WireVocabularyCodegenTests   # Passed: 14, Failed: 0
```

The ops ride the existing `hostCallBegin` slot, so they are wire vocabulary, not ABI. The behaviour
change that does come with them, rc 0 for a handler still running, is item 7's subject.

**Release-please commits in the range, by name, excluded explicitly.**

```bash
git log --oneline 3757d0e..origin/main --grep "chore(main): release"
git log --oneline 3757d0e..origin/main -- src/BlazorNative.Runtime/Exports.cs src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h src/BlazorNative.Runtime/BridgeProtocolNative.cs
```

| commit | release | touches |
|---|---|---|
| `55a5cb1` | chore(main): release 0.17.0 (#429) | `Exports.cs` `VersionNumber` `"0.16.3"` → `"0.17.0"`, and version strings in the manifest, `Directory.Build.props` and the template |

It is the only release commit in the range. The other commits that touch these files are `5634ed1`,
`f249b7b` and `1397cf8`, the 16.1, 16.2 and 16.3 merges, and every hunk they make in the header is a
comment.

## 10. Every new pin conforms to the pin standard on Rules 2–5 and 7 — **NOT MET**

**The population.** Every test method whose name is new at `origin/main`, in every test file the
range touched:

```bash
names() { git show "$1:$2" 2>/dev/null | awk '/\[(Fact|Theory)|@Test/{t=1} t&&/(public .*|fun |func )[A-Za-z0-9_`]+ *\(/{if(match($0,/(void|Task|fun|func) `?[A-Za-z0-9_]+/)){s=substr($0,RSTART,RLENGTH);sub(/^[a-zA-Z]+ `?/,"",s);print s};t=0} /func test[A-Za-z0-9_]*\(/{match($0,/func test[A-Za-z0-9_]*/);print substr($0,RSTART+5,RLENGTH-5)}' | sort -u; }
for p in $(git diff --name-only 3757d0e origin/main -- tests src/BlazorNative.Jni/src/test src/BlazorNative.Jni/src/androidTest src/BlazorNative.Apple/BnHostTests); do
  comm -13 <(names 3757d0e "$p") <(names origin/main "$p") | sed "s|^|$p\t|"
done > pop.tsv
wc -l < pop.tsv                    # 148 new names
cut -f1 pop.tsv | sort -u | wc -l  # 29 files
sort pop.tsv | uniq -d | wc -l     # 0: no file and name pair counted twice
cut -f2 pop.tsv | sort | uniq -d   # testHostCallBeginReturnsWhileContextCreationIsBlocked, in two different files
```

The range touches 50 test files, and 29 of them gain a new name. The one name that occurs twice is
in two files, `BnBiometricsTests.swift` and `BnSecureStorageTests.swift`, and it is two distinct pins.

**148 new names in 29 files**: 70 from 16.1, 54 from 16.2, 18 from 16.3 and 6 from 16.4. Six of them
are flips that replace an old name: `DispatchLaneBlockingTests`, `MountSyncTests`, two in
`RenderThreadWarningTests`, JVM `HostEventTest` and JVM `DispatchEventTest`.

**Where each group is assessed and where its mutations are recorded.** 16.1 and 16.2 have no
separate record file. Their per-pin cells are in the register in `docs/pin-standard.md`, "The
register — pins that do not read the tree" (`:396`), its rows after group B, and in the census
`docs/plans/2026-09-22-phase-15.0-census.md`, rows 13a and 14a. 16.3's are in
`docs/plans/2026-09-27-phase-16.3-record.md` §4. 16.4's are in
`docs/plans/2026-09-28-phase-16.4-record.md` §8.

| group | phase, count | Rules 2–5 assessed in | Rule 7, mutations recorded in |
|---|---|---|---|
| `RenderThreadDispatcherTests`; the Task 2 flips; `DispatchLaneBlockingTests`; `DispatchWindowScopeTests`; `HostEventArmThreadTests`; `ShutdownQuiescenceTests`; `FaultNoticeTests` | 16.1, 51 | register | register cells, and the 16.1 outcome block's "Notable mutations" in `ROADMAP.md`; conforms or partial, named |
| JVM `FaultNoticeTest`, `FrameProducerTest`, `ShutdownQuiescenceTest`, `RetireLateContinuationTest`, the two JVM flips | 16.1, 10 | register | register; conforms or partial, named |
| `WireVocabularyCodegenTests.TheManifest_RejectsADuplicateOpId` | 16.1, 1 | register, as a control | n/a, a control |
| XCTest `BnFaultNoticeTests` | 16.1, 4 | register | **none: "Rule 7, not met, named"** |
| XCTest `BnDispatchLaneTests` | 16.1, 1 | register | **none: "Rule 7, not met, named. It was never run red."** |
| `WireVocabularyCodegenTests.TheHostCallOps_KeepTheirFrozenIds`, `…TheEmittedHostCallOps_MatchTheManifest_InAllThreeLanguages`; `GeneratedSymbolShadowTests.TheOpConstantShadowDetector_…` | 16.1, 3 | **nowhere** — the register excludes them as tree-readers (`:427-431`), and census row 27a still says 8 facts | the frozen-id mutation only, in the register's prose (`:480`); **none for the manifest-match fact** |
| `BackStateNoticeTests`; JVM `BackNoticeTest`, `BackStateBufferTest` | 16.2, 36 | register | register; conforms or partial, named |
| `GeneratedSymbolShadowTests` `OpArmPattern…`, `UnconsumedByDesign…`; `DispatchSurfaceDriftTests.MethodsWithADeclaredVisibility…` | 16.2, 3 | census 14a, 13a | census 14a, 13a |
| `GeneratedSymbolShadowTests.NoShippedShellSource_CallsTheBlockingHostEventDispatch` and its control `OffendingCallDetector…` | 16.2, 2 | **nowhere** | only in #346's closing comment ("restoring the blocking call in `onNewIntent` or in an iOS navigator turns it red at the exact line"); in no record or register |
| JVM `DispatchHostEventAndWaitVisibilityTest` | 16.2, 2 | register | **none: "Rule 7, not met, named. The pin has never been seen red."** |
| Instrumented `BackAndroidTest` | 16.2, 5 | register | register: "device, not run locally", a list of mutations "for the final review to run". Two were run, D10 and D8, recorded **only in PR #431's body**; the rest are unrecorded |
| XCTest `BnBackOffMainTests` | 16.2, 6 | register | **none**: register "device, not run locally", and no run is recorded in the register, ROADMAP or PR #431 |
| `SlowHandlerWarningTests`, JVM `SlowHandlerProbeTest` | 16.3, 18 | **nowhere** — no register row, and the 16.3 record gives mutations only | 16.3 record §4: 13 mutations plus 4b, 8b, 10-AOT and 12b, each red on named facts. `TheWarnedSet_ResetsWithTheSession` is named by none |
| XCTest `BnBiometricsTests`, `BnSecureStorageTests`, the 16.4 pins | 16.4, 6 | register | 16.4 record §8: M1–M4 and R one per run, and the V/VB/VB2 vacuity contrast; conforms |

```bash
awk 'NR>=396 && NR<=498' docs/pin-standard.md | grep "^| " | sed -n '22,44p'   # the 23 M16 register rows
grep -n "^| 13a\|^| 14a\|^| 27a" docs/plans/2026-09-22-phase-15.0-census.md
grep -rn "SlowHandlerWarningTests\|TheEmittedHostCallOps\|NoShippedShellSource" docs --include=*.md
gh pr view 431 --json body --jq .body | sed -n 43,45p
gh run list --workflow ios.yml --limit 200 --json headBranch,databaseId,conclusion --jq '.[] | select(.headBranch|test("16.2"))'   # no scratch/16.2-mut branch: no iOS mutation run
```

**Spot-checks of the recorded mutations.**

- 16.2's device mutations: run 36323787569 on `scratch/16.2-mut-1`, failure; run 36323790875 on
  `scratch/16.2-mut-2`, failure. Both match PR #431's claim.
- 16.4's: run 36385912185, the combined mutation, failure; 36772560765, M1, failure; 36773396413, R,
  failure; 36776492188, VB2-bio, success, the expected green of the vacuity contrast. All match the
  record.
- 16.1's #345 flip, run locally today, reds as the ROADMAP block says; see item 3.

```bash
for r in 36323787569 36323790875 36385912185 36772560765 36773396413 36776492188; do gh run view $r --json conclusion,headBranch,headSha; done
```

**Why NOT MET.** The item requires every new pin to conform on Rules 2–5 and 7, **with its mutations
recorded**, and says nothing about a disclosed gap counting as conformance. Measured against that:

1. **No recorded red, 14 pins:** XCTest `BnFaultNoticeTests` (4), XCTest `BnDispatchLaneTests` (1),
   JVM `DispatchHostEventAndWaitVisibilityTest` (2), and XCTest `BnBackOffMainTests` (6), plus
   `TheEmittedHostCallOps_MatchTheManifest_InAllThreeLanguages` (1). The register itself scores the
   first three "Rule 7, not met". That includes the iOS twin this milestone's DoD asks for in item 4,
   and the XCTest half of item 7's proof. The register's own text for the twin says its red "is
   argued by reading, not measured".
2. **Not assessed on Rules 2–5, 23 pins, carried into the gap plan:** the 18 16.3 pins, the three
   16.1 tree-reading facts, and the 16.2 caller scan with its control. No record has a per-rule cell
   for them, and this audit did not write one either. That is not evidence that they fail Rules 2–5,
   so they are **not counted against the item**. They are carried into the gap plan, to be assessed
   there along with the 14 above. `TheEmittedHostCallOps_MatchTheManifest_InAllThreeLanguages` is in
   both lists; it counts under point 1 for its missing mutation.
3. **Recorded only outside the repo:** `BackAndroidTest`'s two device mutations live in a PR body,
   and the caller scan's red in an issue comment. Rule 7 asks for mutations recorded where a reader
   can find them, which a PR body arguably meets, but the register row for `BackAndroidTest` still
   says "not run locally" and lists the mutations as to-do.
4. **Partials, named:** of the 23 register rows, 9 have Rule 3 partial, 2 Rule 5 partial, 1 Rule 4
   partial and 6 Rule 7 partial. Each is disclosed in its cell. They would make the item MET NARROWLY
   on their own; they are not why it fails.

**The NOT MET rests on point 1 alone.** The DoD asks for every new pin's mutations to be recorded,
and the spec says *"A pin whose mutations are not recorded counts as a gap"*. Fourteen pins have
none, which decides the item directly. Point 1 is not a disclosure of a limit: these pins have never
been seen to fail.

**Why I did not assess the 23 here.** Spec item 4 makes checking every pin this audit's job, so the
choice needs recording. The review offered two options: assess the 23 now, or restate them as not
assessed and carry them. I took the second, for two reasons. First, the verdict does not depend on
them: item 10 is NOT MET on point 1 whatever their cells say. Second, a sound Rules 2–5 cell needs
each pin read against its subject, and often a mutation run. Doing that for 23 pins inside the
audit, with no fix round of its own, would put unchecked verdicts into the register. The gap plan
has to run mutations for the 14 anyway, and it can assess the 23 with the same rigour. This defers
part of the audit's own check, and says so. It is not a finding against those pins.

---

## Carried forward

Every issue below is open (`gh issue view <n> --json state,title`), and stays open:

- **#424** JVM `testDebugUnitTest` does not declare the native DLL as an input, so a changed runtime can be skipped as UP-TO-DATE. *Build hygiene; item 2's JVM count was taken with `--rerun-tasks` on a dll newer than HEAD, so it does not bear on this audit.*
- **#425** `retire()` does not quiesce .NET: a late continuation can deliver a frame after it, on Activity recreation. *Judged under item 5: adjacent, not contradicting. Needs an owner decision on the recreation contract.*
- **#426** FaultNotice message reaches Android logcat unredacted in Release, where iOS redacts it. *Privacy of the late-fault message; item 7 asks that the fault reaches `onError`, which it does.*
- **#427** Analyzer: flag `ConfigureAwait(false)` followed by a render in a component. *A new diagnostic whose severity needs a decision; no DoD item asks for it.*
- **#435** Slow-handler warning names the Bn wrapper, not the app handler, for BnInput and other forwarding controls. *A named limit of #9's detection; pinned as a known limit, so item 8 holds.*
- **#440** Android breaks the host-call contract: KeyStore and BiometricManager work runs synchronously inside `hostCallBegin`, on the render thread. *Judged under items 4 and 7: adjacent, not contradicting either.*
- **#444** iOS: `BnNotificationsTests` show test intermittently never completes Granted. *A known device flake; it did not fire on today's lane.*
- **#453** iOS: a cold `LAContext` start can exceed the biometrics boot test's 30 s wait (25.7 s seen, 33.4 s pre-fix). *The owner-accepted S1 margin, recorded under item 2.*
- **#454** Local `dotnet test` can hang forever in `ReferenceFixtureBase`: an unbounded wait on `generate-reference.ps1`, 2 of 5 runs. *Narrows item 2.*
- **#455** The rc contract says a fault after the first await is never an rc, but an await on a host call completed inside `hostCallBegin` returns rc 2. *Filed by this audit. Narrows item 7.*

Item 10's gap is not filed as an issue. A FAIL routes it to `plan-milestone-gaps`, which plans it as
milestone work, per the spec's item 8.

## What this milestone taught

- **One test flag hid the milestone's subject twice.** `FakeShellHost.AutoCompleteHostCall`
  answers a host call inside `hostCallBegin`. 16.1 found it was the one flag that kept the whole
  suite from seeing #345, since no handler ever really went async. This audit found the same
  inline completion is how a real Android shell answers a `check`, and that the contract text was
  written as if it never happened (#455). An inline answer is not an edge case; it is a production
  path.
- **Strict mode hid a production fault path** (the 16.1 lesson, `ROADMAP.md` 16.1 block). Every
  harness ran with `StrictErrors = true`, so the first FaultNotice design passed every test and could
  never have fired on a device. Every fault pin since runs in production mode.
- **A device-only pin defers its own Rule 7.** All but one of the pins with no recorded red is an
  XCTest, an instrumented test, or a pin the compiler pre-empts. The 16.4 pins show it can be done, one
  mutation per lane run with `headSha` checked, but 16.1 and 16.2 wrote "for the final review to
  run" and the runs were not recorded. A mutation list that ends in a to-do is not a record.

---

## Verdict

**FAIL.** Seven criteria are MET, two are MET NARROWLY, and one is NOT MET: item 10.

M16's engineering is done and its tests prove it. The lane pin is flipped in its original file, with
the false comment corrected in the first code commit, and it reds today when the old blocking wait
is put back. No main-thread caller on either shell waits on .NET. A single render thread owns every
batch and throws under test when anything else drives one. #9's starvation numbers reproduce. The
bridge struct and the ten exports are unchanged, and the three new ops are generated. #425 and #440,
judged against the items they touch, are adjacent and do not contradict their wording. Two items
are narrowed: item 2 by #454, because a local suite run hung in two of five tries, and item 7 by
#455, because the written rc contract claims a post-await fault never becomes an rc, which this
audit measured to be false for any host call completed inside `hostCallBegin`. But item 10 asks that
**every** new pin's mutations be recorded, and 14 pins have never been seen red, among them the iOS
twin item 4 requires and the XCTest half of item 7's proof. That is a gap, not a disclosed limit. A
further 23 pins were never assessed on Rules 2–5, by any record or by this audit. They are carried
into the gap plan and do not count toward the FAIL. Per the spec, FAIL sends the milestone to `plan-milestone-gaps`, and M16 stays
open.
