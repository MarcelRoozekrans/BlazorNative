# Milestone 17 re-audit — Close the Dispatch Lane's Debt

**Date:** 2026-10-22
**Phase:** 17.8, [design](../superpowers/specs/2026-10-21-phase-17.8-design.md),
[plan](../superpowers/plans/2026-10-21-phase-17.8-reaudit.md)
**Audited:** `origin/main` at **`36b2716f`** (#553, 2026-10-21T21:05:53+02:00). The M17 range is
`23ffc4ab..36b2716f`, where `23ffc4ab` is #499, the milestone start. The re-audit branch
`feat/17.8-reaudit` differs from `36b2716f` only under `docs/`.
**Host for local runs:** Debian 13 x64, .NET SDK 10.0.402, PowerShell 7.6.7, Temurin 25.
**Red standard:** RS1, [the 17.7 record](2026-10-17-phase-17.7-record.md) §4 "Rulings", ratified by
the owner on 2026-10-21.
**Precedent:** [the 17.6 audit](2026-10-17-milestone-17-audit.md), verdict FAIL. Its verdicts are a
list of what to re-check, never evidence. The phase records R1–R5 and R7 are cited only as the
place where a mutation is recorded. Every number below was measured on `36b2716f`, with its
command beside it.

**Verdict:** **FAIL**: 6 MET, 4 MET NARROWLY, 1 NOT MET (item 11, the pin standard). See
[the verdict](#verdict). Next: a small gap phase, 17.9, then a short re-audit. M17 stays open.

## Scorecard

| # | DoD item | Verdict |
|---|---|---|
| 1 | All planned phases complete | **MET**: 17.1–17.7 complete on `36b2716f`; 17.8 is this re-audit |
| 2 | All tests passing, both device lanes dispatched, `headSha` compared | **MET, provisional**: Android 247/0 and iOS 288/0 on `36b2716f`, both attempt 1; 5 of 5 local .NET runs 1372/0. The CI cell is filled in Task 5 |
| 3 | #464 is fixed as decided | **MET** |
| 4 | #494 is closed | **MET NARROWLY**: the cancellation fact's rc assertion still has no red; the test and `MILESTONE.md:84` now both name it |
| 5 | #440 is fixed | **MET**: a denied prompt now completes on the secure-storage executor (#537 closed by #545), pinned on device. The 17.6 narrowing is lifted |
| 6 | #425 is fixed: a mount replaces the root | **MET NARROWLY**: `MainActivity` retires only a booted runtime (#511). The `retire()` wiring is now proven on device, so that half of the 17.6 narrowing is lifted |
| 7 | #426 is fixed | **MET** |
| 8 | #435 is fixed by a forwarding hook | **MET NARROWLY**: for a bound input with `:after`, the warning does not name the app's method (#522) |
| 9 | #454, #424 and #458 are fixed | **MET NARROWLY**: the stall names the script, not the stalled command inside it (#530) |
| 10 | No ABI change | **MET**: the same 10 exports, the 80-byte struct and `wire-vocabulary.json` are unchanged across the range |
| 11 | Every new pin conforms to the pin standard | **NOT MET** (#557): 11 anchors or main assertions across 10 pins have no red under RS1, and two register cells claim more than their rows. Three named limits are MET NARROWLY |

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
#   3851:#### Phase 17.6: Audit and close [status: complete]
#   3861:#### Phase 17.7: Pin-standard gaps [status: complete]
#   3883:#### Phase 17.8: Re-audit and close [status: pending]
```

`MILESTONE.md`'s `## Phases` agrees on `36b2716f`, lines 84–90
(`git show origin/main:docs/planning/MILESTONE.md | grep -n "Phase 17"`). 17.8 is this re-audit.
It reads `pending` on `36b2716f` and `active` on the re-audit branch.

The 17.6 audit found one contradicted summary: line 84 said 17.1 left "all #494 anchors redded".
On HEAD, line 84 reads "every #494 anchor redded but one, the cancellation fact's rc assertion,
`FaultNoticeTests.cs:729`, which no probe-side change can red, as its comment at `:726-728` says".
That matches item 4 below.

Every issue M17 set out to close is closed, each by its phase's PR. The two issues 17.6 filed are
closed too:

```bash
for i in 464 494 440 425 426 435 454 424 458 537 538; do
  gh issue view $i --json state,closedAt,closedByPullRequestsReferences; done
#   #464, #494  CLOSED 2026-10-15T04:15  by #503
#   #440        CLOSED 2026-10-15T06:01  by #507
#   #425        CLOSED 2026-10-15T18:04  by #510
#   #426, #435  CLOSED 2026-10-16T13:42  by #523
#   #454, #424, #458  CLOSED 2026-10-17T05:19  by #528
#   #537, #538  CLOSED 2026-10-21T18:52  by #545
```

## 2. All tests passing, both device lanes dispatched, `headSha` compared — **MET, provisional**

> All tests passing — .NET, JVM, **and both device lanes dispatched**, with each lane's `headSha`
> compared against the PR head

| lane | run | attempt | `headSha` | result | source |
|---|---|---|---|---|---|
| .NET, CI | **[filled in Task 5 from the PR's ci.yml build-test run, #551 rule applied]** | | | | |
| JVM, CI | **[filled in Task 5 from the PR's ci.yml build-test run, #551 rule applied]** | | | | |
| Android | `android-instrumented.yml` [39667200369](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/39667200369), dispatched on `main` | 1 | `36b2716f` | **247 / 0 / 0** | `Instrumented totals across 1 suite(s): tests=247 failures=0 errors=0 skipped=0` |
| iOS | `ios.yml` [39668770271](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/39668770271), dispatched on `main` | 1 | `36b2716f` | **288 / 0** | `XCTest cases: passed=288 failed=0` |

```bash
gh workflow run android-instrumented.yml --ref main     # then, after it completed:
gh workflow run ios.yml --ref main
gh run view <id> --json status,conclusion,headSha,attempt,jobs
gh run view <id> --log | grep -E "Instrumented totals|XCTest cases"
```

The lanes ran one at a time, Android first. Both `headSha` values equal `36b2716f`. Both passed at
attempt 1, with no reruns. Android's gate step is "Assert instrumented baseline (247 passed / 0
failed)". The 247 is 17.6's 245 plus 17.7's two pins.

**Local .NET, five runs of the full suite** on a detached worktree of `36b2716f`:

```bash
env | grep -c MSBUILDDISABLENODEREUSE       # 1: MSBUILDDISABLENODEREUSE=1, node reuse OFF
timeout 1500 dotnet test BlazorNative.sln -c Debug --nologo --blame-hang --blame-hang-timeout 10m
```

| run | exit | wall | Analyzers | Renderer | Runtime | hang detector |
|---|---|---|---|---|---|---|
| 1 | 0 | 53 s | 27/27 | 147/147 | 1198/1198 | did not fire |
| 2 | 0 | 59 s | 27/27 | 147/147 | 1198/1198 | did not fire |
| 3 | 0 | 63 s | 27/27 | 147/147 | 1198/1198 | did not fire |
| 4 | 0 | 59 s | 27/27 | 147/147 | 1198/1198 | did not fire |
| 5 | 0 | 54 s | 27/27 | 147/147 | 1198/1198 | did not fire |

That is 1372 passed per run, with 0 failed and 0 skipped. No log has a hang, blame-crash or abort
line. The runs overlapped the iOS lane. Node reuse was off, so, as in 17.6, these runs say nothing
about #530's leading suspect.

**Local JVM.** These runs are not binding; CI's `build-test` is the JVM evidence.

```bash
export JAVA_HOME=$HOME/bn-tools/jdk-25 ANDROID_HOME=$HOME/bn-android
cd src/BlazorNative.Jni && bash ./gradlew testDebugUnitTest --rerun
```

- **Setup.** The first attempt failed in `verifyNativeAssets`, because the fresh worktree lacked the
  linux-bionic `.so`. Both bionic RIDs were then published with `dotnet publish
  samples/BlazorNative.SampleApp -c Release -r linux-bionic-{x64,arm64}`, and the run was repeated.
- **Result.** The rerun gave **206 tests, 150 passed, 56 failed**, with gradle exit 1. The counts
  are summed from the 34 files in `build/test-results/testDebugUnitTest/*.xml`.
- **All 150 pure-Kotlin tests passed.**
- **All 56 failures are native-library loads:**
  - 52 × `NoClassDefFoundError: Could not initialize class io.blazornative.jni.NativeBindings`;
  - 2 × `UnsatisfiedLinkError: Unable to load library 'BlazorNative.Runtime'`;
  - 2 tests whose failures are consistent with the runtime never loading:
    `BlazorNativeRuntimeTest.start_maps_unknown_component_to_IllegalStateException` and
    `RetireLateContinuationTest.retire_waits_out_a_frame_callback_already_in_flight`. They were not
    traced one by one.

  These are the JNA tests, and the cause is **#491**, open and adjacent: Gradle points
  `jna.library.path` at the win-x64 publish. The same shape appeared in 17.6.

**Grade: MET, provisional.** Both device lanes are green on HEAD at attempt 1, and the local .NET
suite passed five runs out of five. The grade waits on the CI cell above.
- If `build-test` is green at attempt 1 on a PR head whose `git diff --stat origin/main <head> --
  ':!docs'` is empty, item 2 is **MET**.
- If it fails on #551's signature, `ShutdownQuiescenceTests.cs:300` at `:248`, "completing the held
  call delivered no frame even without shutdown", then both attempts are recorded, and item 2 is
  **MET NARROWLY** citing #551.
- Any other failure is diagnosed before the grade is set.

## 3. #464 is fixed as decided — **MET**

> a fault from a continuation of dispatch A that runs inline inside dispatch B's synchronous part
> is A's, delivered as A's late FaultNotice under A's handler id, and B keeps its own synchronous
> fault, rc 2. Capture and the begun-call mark both check that the flowing scope matches the
> dispatch's scope. **Both** outcomes are pinned and mutation-proven: A's fault reaching A, and B's
> own fault surviving.

**The code on HEAD**, `src/BlazorNative.Renderer/NativeRenderer.cs`:

```csharp
bool anotherDispatchs = owner is not null          // :631, capture checks the flowing scope
...
if (!anotherDispatchs)                             // :634
...
if (t_syncScope is { Fault: null } scope && ReferenceEquals(s_flowingScope.Value, scope))   // :1641
    scope.ShellCallBegun = true;                                                            // :1642
```

```bash
git show origin/main:src/BlazorNative.Renderer/NativeRenderer.cs | grep -n \
  "anotherDispatchs\|ReferenceEquals(s_flowingScope.Value, scope)\|scope.ShellCallBegun = true"
git diff --stat 980abdbf origin/main -- src/BlazorNative.Renderer/NativeRenderer.cs   # empty
```

**The pins**, `tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs`:
- **A's fault reaching A:** `AnInlineContinuationsFault_IsItsOwnDispatchs_NotTheWindows`, `:953`.
- **B's own fault surviving:** `AnInlineContinuationsFault_LeavesTheWindowsOwnFault_Rc2`, `:973`.
- **The mark's own check:** `ACallBegunByAnInlineContinuation_DoesNotMarkTheWindow`, `:992`.

**Mutation-proven, re-run on HEAD.** `Exports.cs` changed after the 17.6 reds, by the version
constant only, so 17.1-M1 and 17.1-M2 were re-run on a detached worktree of `36b2716f`:
- **17.1-M1:** `if (!anotherDispatchs)` → `if (true)`. It reds 5 of 34 `FaultNoticeTests` facts,
  the two pins at `:961` and `:981` among them.
- **17.1-M2:** the mark without its flowing check. It reds 1 of 34, `:1000`: "rc was 0: the call A's
  continuation began marked B…".

See §11 for the full re-red table.

`#464`: `CLOSED` by #503.

## 4. #494 is closed — **MET NARROWLY**

> every anchor it lists has a recorded red, the unpinned rc claim at `FaultNoticeTests.cs:407-410`
> is pinned or rewritten as resting on reading, and both stale headers and the two `host_event` rc
> tables match the contract.

`#494`: `CLOSED` by #503.

**The cancellation fact is the narrowing.** It is unchanged, at
`tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs:717`:

```csharp
// Rule 7 limit, named in 17.1 from #494: no probe-side change can red this rc assertion,   // :726
// because turning the cancellation into a fault after the begun call is rc 0 too. The
// 16.8 re-audit's X2 reds the Assert.Empty below instead.                                  // :728
Assert.True(rc == 0, $"rc was {rc}: a handler cancelled in its synchronous part after it began a host "   // :729
```

- **No red on HEAD.** No red for `:729` is recorded in R1, R7 or this re-audit.
  `grep -n -i "cancellation\|:729" docs/plans/2026-10-17-phase-17.7-record.md` finds no row for it.
- **The test names it,** with its reason, at `:726`–`:728`.
- **The milestone summary names it too.** `MILESTONE.md:84` no longer overstates; it says the same
  thing.

So the words "every anchor it lists has a recorded red" are still not true of this one entry, and
the narrowing stands as 17.6 graded it.

**The rest of the item holds on HEAD:**
- **The rc claim is pinned.** The old `:407-410` block's "COVERED SINCE 17.1" note now names its
  pin, at `FaultNoticeTests.cs:415`–`:421`: "AChildsBegunCall_DuringTheReRender_MarksTheDispatch_SoALaterRenderFaultIsRc0
  pins that rc 0 half". The pin is at `:799`.
  - The stale DOES NOT COVER note that 17.6 flagged is gone. 17.7 reworded it, and
    `git diff 980abdbf origin/main -- tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs` is
    comment-only.
  - The pin's main assertion red, 17.1-M7, was re-run on HEAD at `:814`.
- **The `host_event` tables match the contract.** The contract is `Exports.cs:473`–`:484`, between
  `BEGIN rc-contract` and `END rc-contract`. Its sentences on a begun call are at `back` `:901` and
  `navigate` `:964`.
  - `git diff 980abdbf origin/main -- src/BlazorNative.Runtime/Exports.cs` changes only
    `VersionNumber`, 0.20.0 → 0.20.1, at `:152`.
- **The headers are unchanged** since the 17.6 audit graded them.

## 5. #440 is fixed — **MET**

> Android's biometrics `check` and every secure-storage path return from `hostCallBegin` at once
> and complete through `host_call_complete` from a background executor, as iOS has since 16.4. A pin
> shows `hostCallBegin` returns before the work runs.

`#440`: `CLOSED` by #507. **`#537`: `CLOSED` 2026-10-21T18:52:39Z by #545.**

**The begin returns at once**, `src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt:327`–`:332`:

```kotlin
HostCallOp.BIOMETRICS -> biometricsExecutor.execute {
    runJob(requestId, BiometricStatus.ERROR) { handleBiometrics(requestId, argsJson) }
}
HostCallOp.SECURE_STORAGE -> secureStorageExecutor.execute {
    runJob(requestId, SecureStorageStatus.ERROR) { handleSecureStorage(requestId, argsJson) }
}
```

The executors are single-thread daemons named `BlazorNative-Biometrics` (`:284`) and
`BlazorNative-SecureStorage` (`:287`). Biometrics `check` completes inside the executor, at `:763`.

**The hop that lifts the narrowing.** `afterAuth`, at `:297`–`:306`, sends work to
`secureStorageExecutor`. Both auth paths now route their **denial** through it, as well as their
grant:

```kotlin
onDenied = {                                                                    // :856, auth-bound set
    afterAuth(requestId, "secure set('$key', auth) denial failed to complete") {
        completeHostCall(requestId, SecureStorageStatus.AUTH_FAILED, null)
    }
},
...
onDenied = {                                                                    // :919, getWithAuth
    afterAuth(requestId, "secure getWithAuth('$key') denial failed to complete") {
        completeHostCall(requestId, SecureStorageStatus.AUTH_FAILED, null)
    }
},
```

The prompt still answers on main, through `ContextCompat.getMainExecutor(appContext)` at `:969` and
`onDenied(...)` at `:980`. The denial's completion now runs on the executor, not on main. The
no-activity branch at `:964` calls the same `onDenied`, so it goes through the hop too.

The template copy is byte-identical:

```bash
git show origin/main:templates/BlazorNative.Templates/content/BlazorNative.App/android/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt \
  | cmp - <(git show origin/main:src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt)   # no output
```

**Its pin, both halves**,
`src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/AndroidHostCallContractTest.kt`:
- `a_denied_storage_prompt_completes_on_the_secure_storage_executor`, `:204`.
  - It drives an auth-bound set, then a getWithAuth, through `denyOnMain`, `:176`.
  - `denyOnMain` captures the gate, denies it inside `runOnMainSync`, and asserts the denier ran on
    `main` (`:188`) and that the completion is `AUTH_FAILED`.
  - `assertOnTheExecutor`, `:195`–`:201`, asserts `done.thread != "main"` and then
    `"BlazorNative-SecureStorage"`.
- **Reds, from R7 §2:**
  - red first on the unfixed code, at the set's half, run 39041110104;
  - M1, the getWithAuth hop reverted, at the getWithAuth half, run 39043332941.
- **On HEAD:** this re-audit's D1 batch, M6 with the hop removed, also reds the pin at `:196`, in
  run 39673079028.
- **The begin pins:** `biometrics_begin_returns_before_the_check_runs`, `:119`, and
  `secure_storage_begin_returns_before_the_keystore_work_runs`, `:123`. They were re-redded on HEAD
  at `:107` by D1 and D2.

**Grade: MET.** Every secure-storage path now completes on its executor, the denied ones included,
and both shells' copies agree. The 17.6 narrowing rested on the denied path. That path is fixed, as
the code above shows, and pinned on device.
- **#506 is adjacent, not contradicting.** It is about order across a prompt, and the item does not
  speak to order. The contract comment at `:270`–`:281` names it.
- **Item 11 has open findings on two of these pins:** F2 at `:105` and F6 at `:115`. They are graded
  there; they do not contradict this item's words.

## 6. #425 is fixed: a mount replaces the root — **MET NARROWLY**

> - A mount disposes any root already mounted, so a late continuation of the old page renders
>   nothing.
> - A retired runtime drops any frame it still receives.
> - Recreation is wired: `MainActivity` retires the old runtime, in the reference shell and the
>   template, proven on the Android device lane by a rotation test.
> - **A recreated shell stays on its page with its back stack,** owner decision 2026-10-15, which
>   answers #425's comment. Navigation from a disposed root's late code is dropped. Both are pinned
>   on the same path.

`#425`: `CLOSED` by #510.

**a. A mount disposes the old root.** `src/BlazorNative.Runtime/HostSession.cs:509`
`renderer.RootGeneration++`, `:512` `UnmountAndMount(effective, renderer, replacedByTheShell: true)`,
and `:626` `renderer.UnmountReplacedRoot(current)`. `git diff --stat 980abdbf origin/main --
src/BlazorNative.Runtime/HostSession.cs` is empty. The pins are re-redded on HEAD by 17.3-M1, at
`RecreationTests.cs:122` and `:136`.

**b. A retired runtime drops any frame.**
`src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/BlazorNativeRuntime.kt:76`–`:77`:
`if (retired) { framesDroppedAfterRetire.incrementAndGet() …`. `retire()` is at `:565`.
- J2 was re-redded on HEAD through draft PR #556, `ci.yml` run 39673470136, at
  `RetireLateContinuationTest.kt:107`.
- J1's red, R7's R47, C1r and J1r, was taken after its file's last 17.7 change and stands.

**c. Recreation is wired, and proven on device.** Both copies of `MainActivity.onDestroy` call it:
- the reference shell, `src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt:475`,
  `if (booted) runtime.retire()`;
- the template, `templates/BlazorNative.Templates/content/BlazorNative.App/android/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt:488`.

**The 17.6 narrowing's second half is lifted.** Its claim was that no device test reds without
`retire()`. The rotation pin is `src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/RecreationAndroidTest.kt:157`,
`a_rotation_retires_the_previous_activitys_runtime`:

```kotlin
assertFalse("anchor: the first Activity's runtime is retired before any rotation",          // :165
    previous.get()!!.retiredForTest)
scenario.recreate()                                                                          // :168
scenario.awaitBooted()
assertTrue("the previous Activity's runtime was not retired after a rotation: MainActivity's " +   // :170
    "onDestroy did not call retire(), so a frame that still reaches it is not dropped. " +
    "That is #425, M17 DoD item 6.", previous.get()!!.retiredForTest)
```

- **The seam.** It reads the field through `retiredForTest`, `BlazorNativeRuntime.kt:171`, and
  `runtimeForTest`, `MainActivity.kt:192`. Both are `@VisibleForTesting internal`.
- **The reds.**
  - R7 §3's M1 removes `retire()` from the reference `MainActivity`. It reds the pin in run
    39045745027, with every other test green.
  - **Re-run on HEAD** as this re-audit's D4, run 39677615625, attempt 1, `headSha` `0275bb2d`, a
    scratch commit on `36b2716f`. It reds at `:170`: "the previous Activity's runtime was not
    retired after a rotation…".
- **The template's copy** is held to the reference by
  `TemplateMainActivity_EqualsTheRepos_ModuloTheFallbackAndTheImport`. That pin red locally when
  the template dropped `retire()`, per R7 §3.

**d. A recreated shell stays on its page; a disposed root's navigation is dropped.**
- `NativeNavigationManager.FromAReplacedRoot`, `src/BlazorNative.Runtime/NativeNavigationManager.cs:344`,
  is called at `:150` and `:231`. It uses `HostSession.IsCodeOfAReplacedRoot`, `HostSession.cs:294`.
- The .NET pins were re-redded on HEAD by 17.3-M2, M6, M7 and M11. The device pins `:50`, `:91`,
  `:121` and `:142` were re-redded by D1, D2 and D4. See §11.

**The narrowing that stands: #511.** "`MainActivity` retires the old runtime" holds only when
`booted` is true. The comment above `MainActivity.kt:475` says so: "NOT COVERED: a recreation during
boot, while `booted` is false and the boot thread may still be inside start(): #511." #511 is
`OPEN`. Graded on the sub-bullet's words, which have no such limit, the item is **MET NARROWLY**,
citing #511.

## 7. #426 is fixed — **MET**

> Android redacts the FaultNotice message the way iOS does, and a twin pin holds the two shells'
> redaction together. *Builder's call:* …

`#426`: `CLOSED` by #523. The seam is unchanged since the 17.6 audit:

```bash
git diff --stat 980abdbf origin/main -- src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/BnShellLog.kt \
  tests/BlazorNative.Runtime.Tests/LogPrivacyDriftTests.cs   # empty
```

The only `src/BlazorNative.Apple` changes in that window are the Kingfisher bump, in
`Package.resolved` and `project.yml`.

- **The seam.** `BnShellLog.kt`: every entry point defaults to `privacy: BnPrivacy =
  BnPrivacy.REDACTED`, at `:175`, `:181`, `:188`, `:194`, `:200` and `:218`. `reveal` is at `:131`.
- **The reveal.** `MainActivity.kt:269` sets `BnShellLog.reveal = (applicationInfo.flags and
  ApplicationInfo.FLAG_DEBUGGABLE) != 0`, before `BnStderrLogcatPump.install()` at `:273`.
  `onError`, at `:340`, is `{ msg, t -> BnShellLog.error(tag, msg, t) }`. The `MainActivity` lines
  moved by 17.7's seam.
- **The twin pin.** `LogPrivacyDriftTests` reads both shells. Its `MainActivity` fact was re-redded
  on HEAD, lp-M7 at `:370`. The device pin `FaultNoticeLogAndroidTest.kt:73` was re-redded on HEAD
  by D2, at `:101`.

17.6 found four twin facts with no red. Those findings sit under item 11, not this item; this
re-audit's item 11 finds none of them still open.

## 8. #435 is fixed by a forwarding hook — **MET NARROWLY**

> the slow-handler warning names the app's handler for `BnInput`, `BnCheckbox`, `BnPicker`,
> `BnSlider`, `BnSwitch` and `BnModal`. The KnownLimit test `TwoBnInputs_…_KnownLimit` is flipped as
> its doc says.

`#435`: `CLOSED` by #523. The hook is unchanged since the 17.6 audit
(`git diff --stat 980abdbf origin/main -- src/BlazorNative.Renderer src/BlazorNative.Components
src/BlazorNative.Core` touches only `BlazorNative.Core.csproj`, a dependency bump).
`src/BlazorNative.Renderer/NativeRenderer.cs:1758`–`:1761`:

```csharp
if (Handler.Target is IForwardsUiEvent wrapper
    && wrapper.ForwardedHandler(EventName, Handler) is { } forwarded
    && OwnerOf(forwarded) is { Framework: false } app)
    return ($"{app.Declaring}::{app.Method}#{EventName}", $"{app.Declaring}.{app.Method}");
```

The flipped test is
`TwoBnInputs_WithDifferentSlowHandlers_GetTwoWarnings_NamingTheAppsHandlers`,
`tests/BlazorNative.Runtime.Tests/SlowHandlerWarningTests.cs:612`. The five other wrappers' facts
are at `:648`–`:680`.

**The narrowing that stands: #522.** For a bound input, `forwarded` is the compiler lambda that
Razor declares on the page. So `OwnerOf(forwarded)` names the page's `<BuildRenderTree>b__…`, not
the app's method. With `@bind-Value:after`, the app's method sits inside that lambda and is not
named. A `:get`/`:set` binding does name it, `AGetSetBoundBnInput_NamesTheAppsSetMethod`, `:797`.
#522 is `OPEN` and needs a decision. Graded on "names the app's handler", the item is **MET
NARROWLY**, citing #522.

## 9. #454, #424 and #458 are fixed — **MET NARROWLY**

> - `ReferenceFixtureBase` waits on its child with a bound and fails naming the stalled command;
> - the JVM test task declares the native library as an input;
> - the androidTest `pollUntil` copies are one helper with named failure messages.

`#454`, `#424` and `#458`: `CLOSED` by #528. The relevant files are unchanged since the 17.6 audit:

```bash
git diff --stat 980abdbf origin/main -- tests/BlazorNative.Runtime.Tests/BoundedScript.cs \
  tests/BlazorNative.Runtime.Tests/ReferenceDriftTests.cs src/BlazorNative.Jni/build.gradle.kts \
  src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/Poll.kt   # empty
```

- **a. The bounded wait.**
  - `ReferenceDriftTests.cs:39` sets `GenerationBound = TimeSpan.FromMinutes(5)`, and `:55` calls
    `BoundedScript.RunBounded(…)`.
  - `BoundedScript.cs:65` waits with `p.WaitForExit(bound)`.
  - On expiry it throws the message built at `:79`–`:93`:
    `$"{script} {string.Join(' ', args)} did not finish within {bound}: "`. It then adds either "the
    script exited with code …" or "the script was still running at the bound…", and the last 20
    output lines.
- **b. The JVM input.** `build.gradle.kts:257` declares `nativeRuntimeFiles`, and the Test tasks
  take it as an input at `:267`, `:299` and `:397`.
- **c. One `pollUntil`.** `git grep -n "fun pollUntil(" origin/main -- src templates` finds only
  `Poll.kt:21`.

**The narrowing that stands: #530.** The message names the script, its arguments, which case it
hit, and the tail. It does not name the native command inside the script that stalled. Its own
text defers that to #530 at `BoundedScript.cs:83` and `:89`: "#530 tracks what that descendant is".
#530 is `OPEN`. On the words "fails naming the stalled command", the item is **MET NARROWLY**,
citing #530.

## 10. No ABI change — **MET**

> **No ABI change**, verified by diffing: the 80-byte bridge struct and the 10 exports. Any wire
> addition, such as the frame generation, lives in `src/wire-vocabulary.json` or the existing frame
> format, and is generated like the rest.

Every command run, over `23ffc4ab..origin/main` with `origin/main` = `36b2716f`:

```bash
# the exports, both ends
for r in 23ffc4ab origin/main; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -c '^\s*\[UnmanagedCallersOnly(EntryPoint'; done
#   10
#   10
for r in 23ffc4ab origin/main; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -o '\[UnmanagedCallersOnly(EntryPoint = "[a-z_]*"' | sort | md5sum; done
#   52ec8c5e37245dd1522729412c4b3c2a  -
#   52ec8c5e37245dd1522729412c4b3c2a  -
git show origin/main:src/BlazorNative.Runtime/Exports.cs | grep -o 'EntryPoint = "[a-z_]*"' | sort
#   blazornative_dispatch_event, _fetch_complete, _host_call_complete, _host_event, _init,
#   _mount, _register_bridge, _register_frame_callback, _shutdown, _version
git grep -n 'UnmanagedCallersOnly(EntryPoint' origin/main -- 'src/*.cs' | grep -v Exports.cs
#   only InteropBoundaryAnalyzer.cs:60, the analyzer's message text

# the 80-byte struct
git diff --stat 23ffc4ab origin/main -- src/BlazorNative.Runtime/BridgeProtocolNative.cs                       # empty
for r in 23ffc4ab origin/main; do git show $r:src/BlazorNative.Runtime/BridgeProtocolNative.cs \
  | awk '/public struct BlazorNativeBridgeCallbacks/,/^}/' | grep -c "public IntPtr"; done                     # 10, 10
git show origin/main:src/BlazorNative.Runtime/BridgeProtocolNative.cs | grep -n "80 bytes"
#   :91  byte layout (80 bytes, HostCallBegin at offset 72) …
#   :96  public struct BlazorNativeBridgeCallbacks          // 10 × IntPtr = 80 bytes
git diff --stat 23ffc4ab origin/main -- src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeBindings.kt   # empty, the JNA mirror
git diff --stat 23ffc4ab origin/main -- src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h                    # 4 insertions
git diff 23ffc4ab origin/main -- src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h \
  | grep -E "^[-+][^-+]" | grep -v -E "^[-+]\s*(//|\*|/\*)"                                                       # empty: all comment

# the wire
git diff --stat 23ffc4ab origin/main -- src/wire-vocabulary.json                                                # empty
git diff --stat 23ffc4ab origin/main -- '*.g.cs' '*.g.kt' '*.g.swift' '*.g.h' src/BlazorNative.Renderer/FrameEncoder.cs   # empty

# what 17.7 touched
git diff --stat 980abdbf origin/main -- src/BlazorNative.Runtime/Exports.cs src/BlazorNative.Runtime/BridgeProtocolNative.cs \
  src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h src/wire-vocabulary.json
#   Exports.cs | 2 +-   : VersionNumber 0.20.0 → 0.20.1, release-please's 5c8f29cb
```

- **The exports:** the same 10 at both ends, with identical names.
- **The 80-byte struct:** 10 `IntPtr` fields at both ends. `BridgeProtocolNative.cs` and its JNA
  mirror `NativeBindings.kt` are unchanged. The C header adds four comment lines only, the 17.1
  contract sentence.
- **The wire:** `src/wire-vocabulary.json`, the generated copies and `FrameEncoder.cs` are
  unchanged. 17.3's root generation is in-process state, and never crosses the wire.
- **17.7 touched no ABI file.** Since the 17.6 audit's `980abdbf`, the only change to the four
  files is release-please's version string.
- **Release-please commits in the range,** excluded by name: `bad7c3fd` `chore(main): release
  0.20.0 (#504)` and `5c8f29cb` `chore(main): release 0.20.1 (#526)`
  (`git log --format='%h %s' 23ffc4ab..origin/main | grep "chore(main)"`).

0.20.0's BREAKING change is a behaviour change, not an ABI change, as the 17.6 audit recorded. It
does not narrow the item.

## 11. Every new pin conforms to the pin standard — **NOT MET**

> **Every new pin conforms to [`docs/pin-standard.md`](../pin-standard.md)** on Rules 2–5 and 7. Its
> mutations are recorded, **including its anchors**, and its register row or census cell is written
> **in the phase that adds it**. Both are lessons from the 16.8 re-audit.

**Shorthand.**
- R1–R5 are the 17.1–17.5 records, and R7 is the 17.7 record.
- A6 is the 17.6 audit's §11c.
- T3 is this re-audit's re-red on HEAD.
- PS is `docs/pin-standard.md`, read on HEAD.
- **Anchor** is an assertion that the pin's comment or message calls an anchor.
- **Main** is the assertion that states the claim in the pin's name.

All grading is under RS1.

### 11a. The population, from git

The population was built from the squashes, not from the 17.6 audit's list or R7 §7:

```bash
git show <squash> -- tests src/BlazorNative.Jni/src/test src/BlazorNative.Jni/src/androidTest src/BlazorNative.Apple templates
```

Each added `[Fact]`, `[Theory]`, `@Test` and `func test` was mapped to its method name. Every name
was checked on `origin/main` with `git grep`, and each squash was grepped for removed test
declarations.

| PR | Squash | Added | Flipped in the PR | Rows |
|---|---|---|---|---|
| #503, 17.1 | `00976fd7` | 7 `[Fact]`, `FaultNoticeTests` | — | 7 |
| #507, 17.2 | `31af87ce` | 5 `@Test`, `AndroidHostCallContractTest` | — | 5 |
| #510, 17.3 | `6aa68096` | 14: 9 `RecreationTests`, 4 `RecreationAndroidTest`, 1 `RetireLateContinuationTest` | `retired_runtime_still_receives_a_frame_from_a_late_continuation` → `a_retired_runtime_drops_a_frame_from_a_late_continuation` | 15 |
| #514, test infra | `9380b301` | 0, a boot-wait helper only | — | 0 |
| #523, 17.4 | `41af910c` | 44: 11 `SlowHandlerWarningTests`, 6 `ForwardingHookTests`, 13 `LogPrivacyDriftTests`, 9 `BnShellLogTest`, 3 `BnDiagnosticsTest`, 1 `SlowHandlerProbeTest`, 1 `FaultNoticeLogAndroidTest` | `TwoBnInputs_…_KnownLimit` → `…_GetTwoWarnings_NamingTheAppsHandlers` | 45 |
| #528, 17.5 | `2f889244` | 12: 5 `GeneratorRunTests`, 3 `NativeRuntimeInputDriftTests`, 2 `PollUntilDriftTests`, 2 `PollAndroidTest` | plus the Swift assertion at `BnGeolocationTests.swift:219` | 13 |
| #545, 17.7 | `262177cc` | 2 `@Test`: `a_denied_storage_prompt_completes_on_the_secure_storage_executor`, `a_rotation_retires_the_previous_activitys_runtime` | — | 2 |

That is **87 rows**: the 17.6 audit's 85, matched row for row, plus 17.7's 2. No later M17 PR
renamed or removed one. No `[Theory]` and no `func test` was added.

### 11b. The register range

```bash
grep -n "#### The register" docs/pin-standard.md                                      # 396
awk 'NR>396 && /^\| /{print NR; exit}' docs/pin-standard.md                             # 491
awk 'NR>=396 && NR<=573 && /^\|/{last=NR} END{print last}' docs/pin-standard.md         # 569
grep -n "^## Rule 7" docs/pin-standard.md                                              # 573
git blame -s -L 489,571 origin/main -- docs/pin-standard.md
```

**Where the M17 block sits.** It runs from `:491` to `:569`:
- the 17.1 sub-table is `:491`–`:499`, with its closing paragraph at `:501`–`:502`;
- the main table has its header at `:504` and its last row at `:569`.

The M17 cells are `:493`–`:499`, `:537`, `:548`, `:549` and `:552`–`:569`.

**Who wrote them**, per blame:
- `00976fd7` wrote `:491`–`:497`, `:499` and `:501`;
- `41af910c` wrote `:548`;
- `2f889244` wrote `:565` and `:569`;
- `262177cc` wrote the rest, including `:553` and `:556` for its two new pins.

**Every M17 pin has a cell.** Each of the 87 pins maps to a cell below.

### 11c. Pin and anchor map

"→" gives the red and where it is recorded. **Bold** marks a finding. Labelled anchors and
name-level claims are graded. Clear secondary assertions with no red are listed as observations,
O2.

| # | PR | pin, file:line | anchors → red | main → red | PS | verdict |
|---|---|---|---|---|---|---|
| 1 | #503 | `FaultNoticeTests.AnInlineContinuationsFault_IsItsOwnDispatchs_NotTheWindows` `:953` | `:958` RanInsideB → R7 FN0 | `:961` → R1 M1; T3 | `:493` | ok |
| 2 | #503 | `…LeavesTheWindowsOwnFault_Rc2` `:973` | `:978` → FN0 | `:981` → M1; T3 | `:494` | ok |
| 3 | #503 | `ACallBegunByAnInlineContinuation_DoesNotMarkTheWindow` `:992` | `:997` → FN0; **`:998` geolocation begin → R1 A3 only, a test-side probe edit, no clause-3 argument** | `:1000` rc → R1 M2; T3 | `:495` | **F2** |
| 4 | #503 | `AFaultWithNoDispatchFlow_InsideAWindow_IsTheWindows` `:1010` | `:1015` → FN0; **`:1016` SawNoFlow → R1 A2 only, which inverts the probe's own read; no argument** | `:1018` → M5; T3 | `:496` | **F2** |
| 5 | #503 | `AFinishedDispatchsFault_InsideAWindow_IsNotTheWindows` `:1027` | `:1037` → FN0 | `:1039` → M1, M4; T3 M1 | `:497` | ok |
| 6 | #503 | `…IsLoggedInStrictModeToo` `:1056` | `:1069` → FN0 | `:1070` → R1 §7 R1/R2; T3 M1 | `:498` | ok |
| 7 | #503 | `AChildsBegunCall_DuringTheReRender_MarksTheDispatch_SoALaterRenderFaultIsRc0` `:799` | **`:809` begin → R1 M6 only, which removes the call from the test's own `BeginOnInitChild`; no argument. `:811` begin-before-throw order → no red at all** | `:814` → M7; T3 | `:499` | **F1, F2** |
| 8 | #507 | `AndroidHostCallContractTest.biometrics_begin_returns_before_the_check_runs` `:119` | **`:105` gate reached → R2 A1 only, the `gateReachedForTest` seam; no argument** | `:107` → R2 M1; T3 D1 | `:552` | **F2** |
| 9 | #507 | `secure_storage_begin_returns_before_the_keystore_work_runs` `:123` | **`:105` → R2 A1 only** | `:107` → M2; T3 D2 | `:552` | **F2** |
| 10 | #507 | `secure_storage_keeps_request_order` `:128` | — | `:136` → M3, a named race; T3 D1, red | `:552` | ok |
| 11 | #507 | `a_throw_on_the_executor_completes_error_never_a_hang` `:142` | — | `:147` → M4, crashes the process; T3 D3 | `:552` | ok |
| 12 | #507 | `the_after_auth_work_runs_on_the_secure_storage_executor` `:152` | `:162` → R7 MP | `:167` → M5, M6; T3 D1 | `:552` | ok |
| 13 | #510 | `RecreationTests.ASecondMount_DisposesTheFirstRoot_SoItsLateContinuationRendersNothing` `:106` | `:101` via `:112` → R7 R1; `:120` → R7 R2 | `:122` → M1; T3 | `:554` | ok |
| 14 | #510 | `ASecondMount_DisposesTheFirstRoot` `:128` | **`:134` Instances → R3 A3 only, a probe counter; no argument** | `:136` → M1; T3 | `:554` | **F2** |
| 15 | #510 | `AReplacingMount_SendsTheNewShellNoRemovalFrameForTheOldRoot` `:144` | `:155` → R7 R3 | `:161` → M5; T3 | `:554` | ok |
| 16 | #510 | `ASecondMount_StaysOnTheCurrentPage_WithItsHistory` `:170` | `:177` → R7 R4 | `:183` → M2; T3 | `:554` | ok |
| 17 | #510 | `ASecondMount_ByAFreshShellAtTheRoot_ShowsTheRootAndResetsHistory` `:197` | `:208` → R4 | `:215` → M6; T3 | `:554` | ok |
| 18 | #510 | `ASecondMount_OfANamedPageByAFreshShell_ShowsItAndResetsHistory` `:225` | `:237` → R4 | `:246` → M7, M6; T3 | `:554` | ok |
| 19 | #510 | `ANavigationFromADisposedRootsContinuation_IsDropped` `:253` | `:101` via `:263` → R1; `:268` → R7 R2 | `:270` → M3, M4, M11; T3 M11 | `:554` | ok |
| 20 | #510 | `ANavigationFromADisposedRootsLifecycleCode_IsDropped` `:282` | `:296` → R1; `:304` → R2 | `:306` → M9, M11; T3 M11 | `:554` | ok |
| 21 | #510 | `AFrameOfTheOldRoot_AfterANewShellRegisters_IsNotDeliveredToIt` `:328` | `:101` via `:338` → R1; `:347` → R2 | `:349` → M8, `:354` → M10; T3 M8 | `:554` | ok |
| 22 | #510 | `RecreationAndroidTest.a_recreated_activity_never_shows_the_old_pages_frame` `:50` | `:60` → R7 MC; `:74` → MR, MRr | `:84` → L2c; T3 D2 | `:555` | ok |
| 23 | #510 | `a_recreated_activity_stays_on_its_page_and_back_goes_to_the_parent` `:91` | `:95` → N1 | `:99` → R7 S1, T3 D4; `:102` S2; `:111` S4r; `:114` S3; `:116` S5, 6 of 6 samples | `:555` | ok; `:116` is a named limit |
| 24 | #510 | `a_recreated_deep_linked_activity_stays_on_the_page_it_navigated_to` `:121` | `:129` → N1 | `:133` → L4; T3 D1, and D4 under S1 | `:555` | ok |
| 25 | #510 | `a_recreated_activity_on_an_unrouted_page_keeps_that_page` `:142` | — | `:150` → L5; T3 D2 | `:555` | ok |
| 26 | #510 | `RetireLateContinuationTest.a_retired_runtime_drops_a_frame_from_a_late_continuation` `:29` | `:47` → R7 R47; `:64` → C1r | `:71` → J1r | `:537` | ok |
| 27 | #510 | `retire_waits_out_a_frame_callback_already_in_flight` `:90` | `:102` → F1 | `:107` → J2; T3 PR #556 | `:537` | ok |
| 28 | #523 | `SlowHandlerWarningTests.TwoBnInputs_WithDifferentSlowHandlers_GetTwoWarnings_NamingTheAppsHandlers` `:612`, flipped | `:622` → A1 | `:625` → M1, M2 | `:558` | ok |
| 29–33 | #523 | `ASlowBnCheckbox`, `ASlowBnPicker`, `ASlowBnSlider`, `ASlowBnSwitch`, `ASlowBnModalDismiss` `_NamesTheAppsHandler`, `:648`/`:656`/`:664`/`:672`/`:680` | `:638` → A2, R7 H1–H4 | `:640` → M1, M2 | `:558` | ok |
| 34 | #523 | `AWrapperWithNoAppCallback_StillKeysByItsCallSite` `:691` | **`:700` `RunsOf("bn-check") == 0`, "the other checkbox's app handler did not run" → no red** | `:701`–`:702` → M5 | `:558` | **F3** |
| 35 | #523 | `AHookThatThrows_NeverFaultsTheDispatch_AndTheWarningFallsBackToTheTreeOwner` `:713` | `:720` → H5 | red first | `:558` | ok |
| 36–37 | #523 | `TwoBoundBnInputs_OnTwoPages_…` `:732`, `TwoBoundBnInputs_OnOnePage_GetTwoWarnings` `:758` | `:739`, `:764` → A1 | M1, M2 | `:558` | ok |
| 38–39 | #523 | `TwoPlainBoundBnInputs_…` `:778`, `AGetSetBoundBnInput_…` `:797` | `:784`, `:802` → H6 | M2 | `:558` | ok |
| 40 | #523 | `ForwardingHookTests.EventCallbackDelegate_ReturnsTheAppsMethod` `:30` | — | `:34` → M2 | `:557` | ok |
| 41 | #523 | `EventCallbackDelegate_IsNullForAnEmptyCallback` `:38` | — | `:40` → R7 F2 | `:557` | ok |
| 42–44 | #523 | `EventCallbackDelegate_WorksForBool`/`Int`/`Float` `:44`/`:53`/`:62` | — | `:48`/`:57`/`:66` → M2; empty halves → F2 | `:557` | ok |
| 45 | #523 | `TheHook_IsNullForADelegateThatIsNotTheWrappersOwn` `:74` | — | `:88` → M6a, M7b; `:90` → M6b, M7a; `:92` → M8; `:94`/`:96`/`:98` → none (O2) | `:557` | ok, O2 |
| 46 | #523 | `LogPrivacyDriftTests.NoAndroidLog_OutsideBnShellLog` `:108` | — | `:123` → M6, red first | `:559` | ok |
| 47 | #523 | `TheAndroidLogScan_FindsAPlantedRawLogE` `:132` | — | **`:138`–`:139` planted hits → no red**; only the precondition `:135`, red first | `:559` | **F4** |
| 48 | #523 | `NoSystemErr_OutsideBnDiagnostics` `:145` | — | `:158` → red first; T3 lp-serr | `:559` | ok |
| 49 | #523 | `TheSystemErrScan_FindsAPlantedWrite` `:167` | — | **`:174` planted hit → no red**; only the precondition `:171`, red first | `:559` | **F4** |
| 50 | #523 | `NoOtherConsoleWriter_OutsidePreviewHost` `:178` | — | `:193` → C1 | `:559` | ok |
| 51 | #523 | `TheConsoleWriterScan_FindsEachPlantedWrite` `:201` | — | `:217` → R7 L1; `:220` → L2 | `:559` | ok |
| 52 | #523 | `EverySafeArgument_IsOnALiteralMessageWithNoTemplate` `:226` | — | `:243` → R7 P1; its floor → red first | `:559` | ok |
| 53 | #523 | `TheSafeCheck_FindsEachPlantedViolation` `:252` | — | `:270` → S1, S2; `:278` → S3 | `:559` | ok |
| 54 | #523 | `BnShellLog_DefaultsEveryEntryPointToRedacted` `:285` | — | M5 | `:559` | ok |
| 55 | #523 | `BnStderrLogcatPump_ForwardsEveryLineRedacted` `:305` | — | `:312` → P3; `:321`–`:324` → P1, P2 | `:559` | ok |
| 56 | #523 | `SwiftBnLog_DefaultsToRedacted_AndItsPumpTakesTheDefault` `:329` | — | `:334` → W1; `:343`–`:345` → W2 | `:559` | ok |
| 57 | #523 | `MainActivity_SetsRevealFromDebuggable_BeforeThePump` `:351` | — | `:370` → M7; T3 lp-M7 | `:559` | ok |
| 58 | #523 | `TheSharedJvmCodesReports_KeepTheirLevel` `:406` | — | N1, N2, E1 | `:559` | ok |
| 59 | #523 | `BnShellLogTest`, `an unrevealed REDACTED message is written as private…` `:210` | — | J1 | `:560` | ok |
| 60 | #523 | `an unrevealed SAFE message keeps its text…` `:224` | — | `:231` → R7 S1; `:232` → S2 | `:560` | ok |
| 61 | #523 | `a revealed line is written as today…` `:236` | `:244` → R1 | `:245` X245; `:246` X246; `:247` R2, R2b; `:248` R3; `:249` X249 | `:560` | ok |
| 62 | #523 | `redaction is the default privacy` `:253` | — | J1 | `:560` | ok |
| 63 | #523 | `info debug and verbose honour SAFE…` `:268` | — | `:272` G1; `:278` G2 | `:560` | ok |
| 64 | #523 | `warn and error are never gated by level` `:282` | — | `:290` → W1, R3; the error half cannot red by gating, named | `:560` | ok, named |
| 65 | #523 | `a throwing throwable sink is swallowed` `:294` | — | `:296` → T1 | `:560` | ok |
| 66 | #523 | `a WARN diagnostic reaches the shell as WARN…` `:301` | `:321` → R7 D1 | `:308` → N2 | `:560` | ok |
| 67 | #523 | `the pump forwards a record as private…` `:325` | — | J1, J2 | `:560` | ok |
| 68–70 | #523 | `BnDiagnosticsTest` `:24`, `:45`, `:59` | — | E1–E3; E4, E4b, E4c; E5 | `:561` | ok |
| 71 | #523 | `SlowHandlerProbeTest.a_slow_bncheckbox_callback_warns_naming_the_apps_method_not_the_wrapper` `:94` | `:109` → R7 A1, clause 2 with the clause-3 argument | `:120` → H1 | `:549` | ok |
| 72 | #523 | `FaultNoticeLogAndroidTest.a_fault_notice_message_is_readable_in_a_debuggable_build` `:73` | `:88` → L1a; `:93` → R7 MC; `:96` → MR | `:101` → L1b; T3 D2 | `:562` | ok, O1 at `:88` |
| 73 | #528 | `GeneratorRunTests.AChildThatLeavesItsPipeHeld_FailsNamedWithinTheBound_AndLeavesNoProcess` `:57` | `:79` → R5 A1; `:82` → R7 LS2; `:83` → G2s, timing-shaped, 11 of 15 today; `:85` → SD; `:86` → SA | M1, at the outer window; M5, the message; M2, shim-only, named | `:563` | named limit |
| 74 | #528 | `AScriptStillRunningAtTheBound_FailsSayingItWasStillRunning` `:140` | `:160` → G1; `:161` → SA | red first; M5 | `:564` | ok |
| 75 | #528 | `AScriptThatExitsWithItsPipeHeld_FailsSayingItExited` `:189` | `:209` → G1; `:212` → LS; `:213` → G2, timing-shaped, 11 of 15 today; `:215` → SA | red first; M5 | `:564` | named limit |
| 76 | #528 | `GenerateReference_RunsEveryMSBuildCallWithNodeReuseOff` `:301` | — | `:309` → M3; the env-line checks `:318`/`:320` → none (O2) | `:565` | ok, O2 |
| 77 | #528 | `GenerateReference_NodeReusePin_CatchesAPlantedCall` `:330` | — | **`:335`, the planted call is caught → no red**; M3 reds only the switched half, `:338` | `:565` | **F4** |
| 78 | #528 | `NativeRuntimeInputDriftTests.TheJvmTasksThatLoadTheRuntime_DeclareItAsAnInput` `:61` | — | M4a, M4b | `:566` | ok |
| 79 | #528 | `TheTemplatesTestTask_DeclaresTheRuntimeAsAnInput` `:66` | — | `:77` → R7 N1, N1b | `:566` | ok |
| 80 | #528 | `APlantedBlockWithoutTheInput_IsReported` `:85` | — | `:96` → N3; `:97` → N2; the accepted half `:95` → none (O2) | `:566` | ok |
| 81 | #528 | `PollUntilDriftTests.NoPrivatePollUntil_RemainsInAndroidTest` `:35` | — | `:45` → red first and a planted copy | `:567` | ok |
| 82 | #528 | `APlantedPrivateCopy_IsReported` `:52` | — | `:62` → U1, U2; `:64` → U3 | `:567` | ok |
| 83 | #528 | `PollAndroidTest.a_poll_on_a_destroyed_activity_fails_naming_it` `:29` | `:34` → R7 MD, clause 3, argued | `:40` → L1, re-run on the catch-only form | `:568` | ok |
| 84 | #528 | `a_predicate_that_throws_is_rethrown_unchanged` `:48` | — | `:56` → L2 | `:568` | ok |
| 85 | #528 | Swift `BnGeolocationTests.testUnknownOpCompletesErrorNeverCrashes`, the assertion at `BnGeolocationTests.swift:219` | — | S1, not specific to this assertion, named | `:569` | named partial |
| 86 | #545 | `AndroidHostCallContractTest.a_denied_storage_prompt_completes_on_the_secure_storage_executor` `:204` | `:182` set → P1; `:182` getWithAuth → MP; `:188` → P2, clause 3, argued | `:190` → P3, P3g; `:191` → P4, P4g; `:196` → red first for the set, M1 for getWithAuth; `:199` → P5, P5g | `:553` | ok |
| 87 | #545 | `RecreationAndroidTest.a_rotation_retires_the_previous_activitys_runtime` `:157` | `:165` → Q1′ | `:170` → M1, T3 D4; `:175` → Q2, seam, clause 3, argued; `:177` → Q3b | `:556` | ok |

**Counts:**
- **Pins:** 87.
- **Labelled anchors:** 67.
- **No red under RS1:** 11 anchors or main assertions, across 10 pins. These are F1–F4.
- **Register cells that claim more than their rows:** 2, F5 and F6.

**Comparison with R7 §7.** The coverage was re-derived from the register and the test files, then
compared with §7. Every red that §7 cites is in R7 with its run or row. These are the disagreements:

| # | R7 §7 says | Measured here |
|---|---|---|
| a | 47 of 47 audit entries redded, and "None of these is an audit entry without a red" | True for those 47 entries. But two labelled anchors have no red, and neither the 17.6 audit nor §7 lists them: `FaultNoticeTests.cs:811` (F1) and `SlowHandlerWarningTests.cs:700` (F3) |
| b | "Reds outside the subject" lists only R7's own non-subject reds | §7 still relies on the 17.1–17.3 test-side anchor reds R1 A2, A3 and M6, R2 A1, and R3 A3. None of them carries a clause-3 argument, and six anchors rest on them alone (F2) |
| c | §11c rows 8–12: "the subject's change is on the denied path, which these pins do not drive" | That is reasoning, not a run. All five were re-redded on HEAD, D1–D3, and each red as recorded |
| d | §11c rows 22, 24, 25, 72: "an import moved it one line", "body unchanged" | Re-redded on HEAD, D1 and D2, and each red as recorded |
| e | §11c row 27: "moved by the rework above it; body unchanged" | J2 was re-run on HEAD through PR #556, and it red at `:107` |
| f | §11c rows 1–7: "lines moved only" | Agreed. On HEAD, M1 reds row 3 at its notice, `:1002`, not at its rc, `:1000` (O3). The rc red is M2, re-run on HEAD at `:1000` |
| g | Entry 1 lists S1, S2 and S3 as reds of the stays-on-its-page test | All three predate the rework `f01037d9`. S1 was re-run on HEAD, D4, at `:99`. S2 and S3 are per-anchor stale (O1) |
| h | Entry 43: `:213` red in 13 of 15. X10: `:83` red in 12 of 15 | 11 of 15 each, today; see 11f |
| i | The two new pins have no unentered anchor | Agreed |

### 11d. Moved pins and re-reds on HEAD

**The rule.** A pin was re-redded when its test file or subject file had a non-comment change after
its red was taken. One recorded mutation was re-run on the pin's main assertion. The diffs were
measured with:

```bash
git diff --shortstat <baseline> origin/main -- <test file> <subject files>
```

| Pins | Baseline | Diff to HEAD | Action |
|---|---|---|---|
| 17.1 facts, rows 1–7 | `980abdbf` (A6) | `FaultNoticeTests.cs` comment-only; `Exports.cs` version only | re-red locally: 17.1 M1, M5, M7 |
| row 3's rc | `00976fd7` (R1 M2) | 2 files, +113/−18 | re-red locally: 17.1 M2 |
| `RecreationTests`, rows 13–21 | `980abdbf` | `Exports.cs` version only | re-red locally: 17.3 M1, M2, M5, M6, M7, M8, M11 |
| `AndroidHostCallContractTest` pins 1–5, rows 8–12 | `980abdbf`; `31af87ce` for pins 1–2 | +79/−9, and +85/−12 | device: D1, D2, D3 |
| `RecreationAndroidTest` 17.3 tests and `FaultNoticeLogAndroidTest`, rows 22, 24, 25, 72 | `980abdbf` | +45/−1 and +33/−7 | device: D1, D2 |
| stays-on-its-page, row 23 | `67c74b75` | +8/−1, `f01037d9` | device: D4, S1 |
| the retire pin, row 87 | `3bdd5777` (R7 §3 M1) | 3 files, +13/−4 | device: D4, M1 |
| `RetireLateContinuationTest` test 2, row 27 | `980abdbf` (A6 J2) | +19/−5 | JNA PR #556 |
| `LogPrivacyDriftTests` rows 48, 57 | `41af910c` | `MainActivity.kt` +5, the seam | re-red locally: lp-serr, lp-M7 |
| pin 6, row 86 | `edd1cc5b` | a KDoc comment only | stands |
| every other 17.4 and 17.5 pin | `41af910c`, `2f889244` | empty | stands |
| every R7 red taken after its file's last 17.7 change | `262177cc` | empty: `262177cc..origin/main` is docs only | stands |

**Local .NET re-reds.**
- **Setup.** Each ran on a detached worktree of `36b2716f`, one at a time, with
  `dotnet test tests/BlazorNative.Runtime.Tests -c Debug --nologo --filter "<filter>"`.
- **Discipline.** Each mutation was an exact-match replace that refused anything but one match.
  Each was restored with `git checkout -- .`, and `git status --short` was empty before every
  mutation and after every restore.
- **Baseline.** The target classes ran green first: 99 of 99.

| id | mutation | filter | result |
|---|---|---|---|
| lp-M7 | `MainActivity.kt:269`: `BnShellLog.reveal = …` moved after `BnStderrLogcatPump.install()` | `LogPrivacyDriftTests` | 1 of 13, at `:370` |
| lp-serr | `BlazorNativeRuntime.kt:49`: the default `onError` back to its pre-17.4 `System.err.println` | `LogPrivacyDriftTests` | 2 of 13: `NoSystemErr…` at `:158`, naming `BlazorNativeRuntime.kt:51`; `TheSharedJvmCodesReports…` at its floor, `:427` |
| 17.1-M1 | `NativeRenderer.cs` `if (!anotherDispatchs)` → `if (true)` | `FaultNoticeTests`, 34 | 5 of 34: rows 1, 2, 3, 5, 6 at `:961`, `:981`, `:1002`, `:1039`, `:1070` |
| 17.1-M2 | `NoteShellCallBegun` without `&& ReferenceEquals(s_flowingScope.Value, scope)` | `FaultNoticeTests` | 1 of 34: row 3 at `:1000` |
| 17.1-M5 | the null-flow case moved out of the window | `FaultNoticeTests` | 1 of 34: row 4 at `:1018` |
| 17.1-M7 | `scope.ShellCallBegun = true;` → no-op | `FaultNoticeTests` | 5 of 34: row 7 at `:814`, plus four 16.7 facts, as recorded |
| 17.3-M1 | `HostSession.cs` `UnmountReplacedRoot` → `{ }` | `RecreationTests\|BackStateNoticeTests\|ShutdownQuiescenceTests`, 47 | 2 of 47: `:136`, `:122` |
| 17.3-M2 | the default re-resolve limited to `!replacing` | the same | 2 of 47: `:183`, and the 16.2 back-state pin, as recorded |
| 17.3-M5 | `if (_dropDeliveryOfReplacedRoot)` → `if (false)` | the same | 1 of 47: `:161` |
| 17.3-M6 | the adopt removed | the same | 2 of 47: `:215`, `:246` |
| 17.3-M7 | the adopt limited to the default component | the same | 1 of 47: `:246` |
| 17.3-M8 | the hold's `Volatile.Write` → no-op | the same | 1 of 47: `:349` |
| 17.3-M11 | `RootGeneration++` removed | the same | 2 of 47: `:270`, `:306` |

**Device re-reds.**
- **Setup.** Each batch was one scratch commit on `36b2716f`, replayed from the original mutation
  commits, with L4 adapted to HEAD's `MainActivity`. Each ran with
  `gh workflow run android-instrumented.yml --ref scratch/17.8-<id>`, one at a time.
- **Reading.** Each run is attempt 1, with `headSha` equal to its scratch commit. Results were read
  from the `instrumented-test-results` JUnit XML. Each ref was deleted after its run.
- **Reach.** Each failure set is exactly the union of the reaches that R2, R3, R4 and R7 recorded.

| Batch | Mutations | Run, attempt | `headSha` | Result |
|---|---|---|---|---|
| D1 | 17.2 M1, M3, M6; 17.3 L4 | 39673079028, 1 | `b52db9e5` | 6 of 247: pin 1 `:107`; pin 3 `:136`, the race showed; pin 5 `:167`; pin 6 `:196` from `:210`, because M6 removes the hop the #537 denial now also uses; deep-linked `:133`; `BnNotificationsAndroidTest.warm_tap…` `:157`, L4's recorded second red |
| D2 | 17.2 M2; 17.3 L2c, L5; 17.4 L1b | 39674729653, 1 | `13a9f09e` | 4 of 247: pin 2 `:107`; old-frame `:84`; unrouted `:150`; `FaultNoticeLogAndroidTest` `:101` |
| D3 | 17.2 M4 | 39676716989, 1 | `b1e60643` | the process crashed: `tests=2 failures=1`, pin 4, `java.lang.IllegalStateException: job-fault` |
| D4 | 17.7 S1; 17.7 §3 M1, `retire()` removed | 39677615625, 1 | `0275bb2d` | 3 of 247: stays `:99`; deep-linked `:133`, S1's recorded reach; retire pin `:170` |

**JNA.**
- **The PR.** Throwaway draft PR #556 ran on `scratch/17.8-jna` = `e0aa441e`. It carried 17.3 J2,
  the in-flight wait in `retire()` removed in both mirror copies, with the JVM step narrowed to
  `RetireLateContinuationTest`.
- **The run.** `ci.yml` 39673470136, attempt 1, `headSha` `e0aa441e`. .NET passed 1372. The JVM
  step gave `2 tests completed, 1 failed`, at `:107`: "retire() returned while a frame callback was
  still inside onFrame…".
- **Cleanup.** PR #556 is closed with its branch deleted. `git ls-remote origin
  'refs/heads/scratch/*'` is empty.

### 11e. RS1 clause 3

**R7's own non-subject reds** each carry the written argument that clause 3 asks for:

| Red | Kind | No subject-side red, and how one was looked for | Not vacuous, own message | Verdict |
|---|---|---|---|---|
| §6 MD, `PollAndroidTest:34` | fixture | §6: `Poll.kt` is not reached first; `moveToState` cannot return another state; a throwing `onDestroy` crashes instead | own message; without it, `:43` names the wrong cause | carries it |
| §6b P2, pin 6 `:188` | fixture | §6b: the thread is the platform's; no bridge change can move it | own message | carries it |
| §6b Q2, retire pin `:175` | seam | §6b: the product shape Q2p crashed twice, 39081077429 and 39081511197, with no `:175` message | without `:175`, `:177` reds with the wrong cause | carries it |
| §5 A1, `SlowHandlerProbeTest:109` | planted input, argued as clause 3 | §5: only a dispatch writes the list | own message | carries it |
| §4 G2, SA, LS, LS2, SD, G2s | planted input, the stub, clause 2 | §4: looked for in `RunBounded`, each shape argued | own messages quoted | carries it |
| §4 FN1, §6b Q1 | corroboration only | a subject-side red exists: FN0, Q1′ | — | not counted, correctly |

**Outside R7's list.** These are test-side anchor reds in R1–R3 that the map still relies on. None
of them carries the argument, which is F2:
- R1 §3 A2, on `:1016`;
- R1 §3 A3, on `:998`;
- R1 §4 M6, on `:809`;
- R2 §3 A1, on `:105`, through the seam;
- R3 §3 A3, on `:134`.

R3's A1 and A2, and R1's A1 and A4, have subject-side replacements in R7, so they count against
nothing.

### 11f. The three named limits — MET NARROWLY

| Limit | Diff | Runs on HEAD | Measured | Recorded |
|---|---|---|---|---|
| `GeneratorRunTests.cs:213`, G2 | R7 §4's diff, applied to `TestData/hold-stdout.ps1`; change lines byte-identical to the record's | 15 × `dotnet test … --filter FullyQualifiedName~GeneratorRunTests.AScriptThatExitsWithItsPipeHeld_FailsSayingItExited --no-build` | **11 of 15 red at `:213`**, "the run returned before the anchor held, so the bound was not what ended it". The 4 misses, runs 5, 8, 11 and 15, red at `:221`, "No exception was thrown", a red of the fact | 13 of 15, PS `:564` |
| `GeneratorRunTests.cs:83`, G2s | R7 §4's final G2s diff, with the 300 ms hold; change lines byte-identical | 15 × `--filter FullyQualifiedName~GeneratorRunTests.AChildThatLeavesItsPipeHeld` | **11 of 15 red at `:83`**, the same message. The 4 misses, runs 3, 7, 9 and 12, red at `:82`, "the launcher never exited" | 12 of 15, PS `:563` |
| `RecreationAndroidTest.kt:116` | — | not re-run | **6 of 6 samples** red, R7 §6 S5, run 39080643942, attempt 1, `96494a76`: "a handled back must not finish the activity". Measured, not proven deterministic | 6 of 6 |

- **Hygiene.** The stub processes were killed after every run. One earlier G2 attempt was discarded,
  because two instances of the run script interleaved. The 15 runs above are a clean rerun from
  one instance, checked with `ps`.
- **Grade.** Each limit is a red that is not deterministic, named in its cell with its reason, so
  each is **MET NARROWLY**.
- **Drift.** Both `GeneratorRunTests` rates measure lower today than recorded: 11 of 15 against 13
  of 15 and 12 of 15. In each case a miss still reds the fact, one line later. The cells at PS
  `:563` and `:564` quote the old rates. The drift does not change the grade. It is noted on #557,
  and a gap phase can correct the rates while it is in that file.

### 11g. The grade

**NOT MET.** The clause "its mutations are recorded, **including its anchors**" fails on six
findings. All six are filed together as **#557**.

- **F1. An anchor with no red.** `tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs:811` is the
  order anchor of `AChildsBegunCall_DuringTheReRender_…`, "the begin must come before the fault".
  - R1 §4 M6 removes the begin, so it reds `:809` first.
  - N7 swaps the siblings of the 16.7 sibling fact only.
  - No row enters `:811`.
- **F2. Anchors whose only red is a test fixture or seam, with no clause-3 argument.** The anchors
  are:
  - `FaultNoticeTests.cs:809`, by R1 M6;
  - `:998`, by R1 A3;
  - `:1016`, by R1 A2;
  - `RecreationTests.cs:134`, by R3 A3;
  - `AndroidHostCallContractTest.kt:105`, the gate anchor of pins 1 and 2, by R2 A1 through the
    `gateReachedForTest` seam.

  All of these reds predate RS1, and no record makes the three arguments clause 3 asks for. No
  subject-side red is recorded for any of them.
- **F3. An anchor with no red.** `tests/BlazorNative.Runtime.Tests/SlowHandlerWarningTests.cs:700`
  is `RunsOf("bn-check") == 0`, "the other checkbox's app handler did not run". No row enters it.
- **F4. Main assertions with no red.** Each of these pins has a red, but only on a precondition or
  on its other half:
  - `LogPrivacyDriftTests.cs:138`–`:139`, whose only red is at the precondition `:135`;
  - `LogPrivacyDriftTests.cs:174`, whose only red is at the precondition `:171`;
  - `GeneratorRunTests.cs:335`, where M3 reds only the switched half, `:338`.

  The 17.6 audit called these "lesser notes, not counted". **On 2026-10-22 the owner ruled that a
  pin's main assertions need their own recorded red, the same as its anchors,** so F4 counts.
- **F5. A register cell that claims more than its rows.** `docs/pin-standard.md:501`–`:502` says
  "every fact's main assertion and anchor has a red … No partial is left in these seven".
  - F1 and F2 contradict it.
  - `:499`'s Rule 7 cell, "§4 M6 at the anchor, M7 at the rc", leaves out the order anchor.
- **F6. A register cell that claims more than its rows.** `docs/pin-standard.md:552`, Rule 4, says
  "a renamed executor reds pins 1, 2 and 5 at their thread assertion".
  - No row in R2 or R7 runs a renamed executor.
  - For pins 1 and 2, the thread assertion `AndroidHostCallContractTest.kt:115` has no red at all,
    because M1 and M2 red `:107` first.

**What holds:**
- Every pin has a cell, written in its phase or corrected in 17.7.
- Every pin has at least one recorded red.
- Every re-red on HEAD reproduced as recorded.
- R7's own clause-3 reds each carry their argument.

The item is graded on its words, and 11 assertions with no red under RS1, plus two overclaiming
cells, fail them.

**Observations, not graded:**
- **O1. Per-anchor staleness.** S2 (`:102`) and S3 (`:114`) predate `f01037d9`'s rework of the
  stays test, and L1a (`FaultNoticeLogAndroidTest:88`) predates 17.5's and 17.7's changes. Only
  each pin's main assertion was re-redded.
- **O2. Secondary assertions with no red:**
  - `ForwardingHookTests.cs:94`, `:96` and `:98`;
  - `RetireLateContinuationTest.kt:111`;
  - `RecreationTests.cs:186` and `:218`;
  - `GeneratorRunTests.cs:318` and `:320`;
  - `NativeRuntimeInputDriftTests.cs:95`.
- **O3. A record misstates a red.** R1 §3 says M1 reds row 3 "at its rc". On HEAD it reds `:1002`,
  the notice; the rc at `:1000` is M2's. A6 repeats the claim.
- **O4. Dependency bumps after the reds.** These include the ZeroAlloc `.csproj` bumps. A
  `.csproj` is not a test or subject file under the re-red rule.
- **O5. A claimed mutation not run.** PS `:553` says "a renamed executor reds it". That mutation was
  not run, but P5 and P5g enter the same assertion, `:199`, through a different thread name.
- **O6. Timing rates are lower today.** See 11f.

---

## Carried debt

Each issue is judged against the item it touches. None blocks a close on its own, because none
contradicts an item's words beyond the narrowings graded above. The M18 phases are the owner's plan
of 2026-10-21.

| issue | title | item | judgement | M18 phase |
|---|---|---|---|---|
| **#557** | m17 pins: eleven anchors and main assertions with no red under the red standard, and two register cells that claim more than their rows | 11 | **fails item 11.** Filed by this re-audit | **17.9**, the gap phase, before M18 |
| #500 | A re-render or queued work from another dispatch's continuation is charged to the window it runs in | 3 | adjacent, not contradicting: item 3 is about a fault A's own code raises; it needs a decision | 18.1 |
| #506 | Android secure storage: an auth-bound set's encrypt runs after later requests on the same key | 5 | adjacent, not contradicting: the item does not speak to order across a prompt | 18.2 |
| #511 | Android: an Activity recreated during boot can leave the new Activity blank | 6 | **narrows item 6**: `MainActivity.kt:475` retires only a booted runtime | 18.3 |
| #522 | Slow-handler warning names a compiler lambda for a bound input | 8 | **narrows item 8**: with `:after`, the app's method is not named | 18.4 |
| #543 | jvm suite: a test that waits on a lost host call completion hangs the whole step for 45 minutes | 2 | adjacent: a lost completion would show as a cancelled JVM job, hiding every JVM red. It did not fire on HEAD | 18.5 |
| #544 | android back tests: a back that also finishes the activity is caught only when its destruction wins a poll | 6, 11 | adjacent: the same timing class as the `RecreationAndroidTest.kt:116` named limit, in the 16.2 back pins | 18.5 |
| #551 | flaky: ShutdownQuiescenceTests' positive control sees no frame for 10 s on the windows ci runner | 2 | narrows item 2 only if it fires on this phase's `build-test`; Task 5 applies the rule | 18.7 |
| #530 | reference generator: something outlives dotnet publish and holds its output pipe | 9 | **narrows item 9**: the stall names the script, not the stalled command | 18.8 |
| #491 | The JVM dev loop runs on Windows only: Gradle hardcodes the win-x64 publish folder | 2 | adjacent: the reason 56 JNA tests cannot run locally on Linux, and why the JVM evidence is CI's. It predates M17 | not in the M18 phase list |

**The rest of the M18 plan** touches no M17 item:
- 18.6: #401, #396;
- 18.7, beside #551: #453, #444, #407.

## What M17 taught

- **Two rounds of review missed F1–F6 because both built their population from the audit's list,
  not from git.** The 17.6 audit graded the anchors it found. 17.7 redded exactly the audit's
  entries, and its §7 checked itself against the same list: "47 of 47 audit entries redded". No
  one re-derived the anchors from the test files. So the two anchors the audit missed, `:811` and
  `:700`, were missed twice. This re-audit built the 87 rows from the squashes and the anchors from
  the test files, and found them.
- **A standard written mid-milestone was applied forward, not back.** RS1 clause 3 was ratified in
  17.7. R7 §7 listed only 17.7's own reds outside the subject. The older 17.1–17.3 fixture and seam
  reds, which predate RS1, were never re-graded against it. That gap is F2. A new red standard
  needs a sweep of every red it now governs.
- **"Lesser notes" hid real gaps.** 17.6 set aside reds on a precondition or on a pin's other half.
  The owner's 2026-10-22 ruling makes them findings, F4. A grading boundary that is not written down
  becomes a place where gaps go uncounted.
- **A register cell still outruns its rows.** F6 claims a mutation that no record runs. That is the
  #538 lesson again, now in a cell 17.7 rewrote. A cell should name the row behind each red it
  claims.
- **Timing-shaped rates drift.** The two `GeneratorRunTests` limits measured 11 of 15 today,
  against 13 of 15 and 12 of 15 recorded. A rate is a measurement on one host on one day. A cell
  that quotes one should carry its date, and a re-audit should re-measure it rather than copy it.

## Verdict

**FAIL.** Item 11 is NOT MET, so M17 cannot close.

- **Six items are MET:** 1, 2 (provisional, pending Task 5's CI cell), 3, 5, 7 and 10.
- **Four are MET NARROWLY,** each with its reason:
  - **item 4:** the cancellation fact's rc assertion, `FaultNoticeTests.cs:729`, still has no red.
    It is named in the test at `:726`–`:728` and in `MILESTONE.md:84`.
  - **item 6:** #511. `MainActivity.kt:475` and the template's `:488` retire only a booted runtime.
  - **item 8:** #522.
  - **item 9:** #530.
- **Two 17.6 narrowings are lifted,** each on code quoted at HEAD:
  - **item 5,** #537. The denial hops through `afterAuth` at `AndroidShellBridge.kt:856` and
    `:919`, pinned at `AndroidHostCallContractTest.kt:204`.
  - **item 6's `retire()` wiring.** It is proven on device by the rotation pin
    `RecreationAndroidTest.kt:157`, red at `:170` without `retire()` on HEAD, D4.

**Item 11 fails on six findings, F1–F6, all filed as #557:**
- 11 anchors or main assertions, across 10 pins, have no red under RS1:
  - F1 and F3: two anchors with no red;
  - F2: five anchors resting on unargued fixture or seam reds;
  - F4: three main-assertion sites, per the owner's 2026-10-22 ruling.
- Two register cells claim more than their rows: `docs/pin-standard.md:499`/`:501`–`:502`, F5, and
  `:552`, F6.

The three named limits are MET NARROWLY within item 11:
- `GeneratorRunTests.cs:213`, 11 of 15 today against 13 of 15 recorded;
- `:83`, 11 of 15 against 12 of 15;
- `RecreationAndroidTest.kt:116`, 6 of 6 samples.

**Next: a small gap phase, 17.9, followed by a short re-audit.** 17.9 does the following, all
tracked in #557:
1. **Record the missing reds.** Record a red for F1 and F3. Record a red for each F4 site, by
   weakening its detector against the planted text. For F2, record a subject-side red, or write the
   RS1 clause-3 argument into the record and the cell.
2. **Correct the two cells,** `docs/pin-standard.md:499`, `:501`–`:502` and `:552`, to what their
   rows show. Either run a renamed executor, or reword the cell.
3. **In passing,** update the drifted rates at PS `:563` and `:564`.

M17 stays open, and no tag is cut: `CONVENTIONS.md:49` records `Milestone completion tags a
release: no`.
