# Milestone 17 second re-audit — Close the Dispatch Lane's Debt

**Date:** 2026-10-22
**Phase:** 17.10, [design](../superpowers/specs/2026-10-22-phase-17.10-design.md)
**Audited:** `origin/main` at **`d31e9989`**, which is #560 at 2026-10-22T11:15:02+02:00.
- The M17 range is `23ffc4ab..d31e9989`, where `23ffc4ab` is #499, the milestone start.
- The re-audit branch `feat/17.10-reaudit` differs from `d31e9989` only under `docs/`.

**Host for local runs:** Debian 13 x64.
**Red standard:** RS1, from [the 17.7 record](2026-10-17-phase-17.7-record.md) §4 "Rulings".
**Site unit for item 11:** the owner's ruling of 2026-10-22, quoted in [§11](#11-every-new-pin-conforms-to-the-pin-standard--not-met).
**Precedent:** [the 17.8 re-audit](2026-10-21-milestone-17-reaudit.md), verdict FAIL, answered by 17.9, [record](2026-10-22-phase-17.9-record.md) and #560.
- Its verdicts are a list of things to re-check, not evidence.
- Every number below was measured on `d31e9989`, with the command shown beside it.

**Verdict:** **FAIL**: 5 MET, 5 MET NARROWLY, 1 NOT MET (item 11). See [the verdict](#verdict).
- Item 2 is MET NARROWLY on #564, a new CI flake that needed one re-run.
- Next comes a small gap phase, 17.11, on #561, then a short re-audit. M17 stays open.

## Scorecard

| # | DoD item | Verdict |
|---|---|---|
| 1 | All planned phases complete | **MET**: 17.1–17.9 complete; 17.10 is this re-audit |
| 2 | All tests passing, both device lanes dispatched, `headSha` compared | **MET NARROWLY** (#564): Android 247/0 and iOS 288/0 at attempt 1 on the PR head `026777c0`. CI `build-test` failed at attempt 1 on a new flake, #564, and passed at attempt 2 with .NET 1372/0 and JVM 206/0. Local .NET 1372/0/0 on `d31e9989` |
| 3 | #464 is fixed as decided | **MET** |
| 4 | #494 is closed | **MET NARROWLY**: the cancellation fact's rc assertion, `FaultNoticeTests.cs:729`, still has no red. The test names it, and so does `MILESTONE.md:84` |
| 5 | #440 is fixed | **MET** |
| 6 | #425 is fixed: a mount replaces the root | **MET NARROWLY**: `MainActivity` retires only a booted runtime (#511) |
| 7 | #426 is fixed | **MET** |
| 8 | #435 is fixed by a forwarding hook | **MET NARROWLY**: for a bound input with `:after`, the warning does not name the app's method (#522) |
| 9 | #454, #424 and #458 are fixed | **MET NARROWLY**: the stall names the script, not the stalled command inside it (#530) |
| 10 | No ABI change | **MET**: the same 10 exports, the 80-byte struct and `wire-vocabulary.json` are unchanged across the range |
| 11 | Every new pin conforms to the pin standard | **NOT MET** (#561): 28 sites in 20 pins have no red under RS1, 24 name-claim sites and the 4 second lines of two-line anchors, and three register cells claim more than their rows. The three named limits are MET NARROWLY |

---

## The code is unchanged since 17.8

17.8 measured `36b2716f`. The only change since then outside the docs is release-please's 0.20.2 bump, #552:

```bash
git log --format='%h %s' 36b2716f..origin/main
#   d31e9989 docs(17.9): every M17 assertion site has a recorded red (#560)
#   01deceba chore(roadmap): complete phase 17.8 and add the second gap pass (#559)
#   c827b6c6 chore(main): release 0.20.2 (#552)
#   2df46027 docs(17.8): re-audit milestone 17 against its definition of done (#558)
git diff --stat 36b2716f origin/main -- ':!docs' ':!*.md'
#    .release-please-manifest.json                                       | 2 +-
#    src/BlazorNative.Runtime/Exports.cs                                 | 2 +-
#    src/Directory.Build.props                                           | 2 +-
#    templates/BlazorNative.Templates/BlazorNative.Templates.csproj      | 2 +-
#    .../content/BlazorNative.App/.template.config/template.json         | 4 ++--
#    .../content/BlazorNative.App/MyBlazorNativeApp.csproj               | 6 +++---
#    6 files changed, 9 insertions(+), 9 deletions(-)
git diff 36b2716f origin/main -- ':!docs' ':!*.md' | grep -E '^[-+][^-+]'
#   every line is 0.20.1 → 0.20.2 beside an x-release-please-version marker
```

**Version strings only.**
- `Exports.cs`'s changed line is `VersionNumber`, at `:152`, and no M17 pin asserts it.
- So the code quotes of items 3–10 still hold. Each was re-read on HEAD below anyway, with its line number.
- All of them sit at the lines 17.8 recorded.

## 1. All planned phases complete — **MET**

> All planned phases complete

```bash
git show origin/main:docs/planning/ROADMAP.md | grep -n "Phase 17\."
#   3798:#### Phase 17.1 … [status: complete]   …   3883:#### Phase 17.8: Re-audit and close [status: complete]
#   3899:#### Phase 17.9: Pin-standard gaps, second pass [status: active]
#   3911:#### Phase 17.10: Re-audit and close [status: pending]
grep -n "Phase 17\.9\|Phase 17\.10" docs/planning/ROADMAP.md      # on this branch
#   3899:#### Phase 17.9: … [status: complete]
#   3916:#### Phase 17.10: Re-audit and close [status: active]
```

**17.1–17.8 read `complete` on `d31e9989`.**
- **17.9's work is merged.** #560 is `d31e9989`, and #557 is `CLOSED` by #560 at 2026-10-22T09:15:04Z.
- **Its `complete` mark is only on this branch,** in `32613a40` `chore(roadmap): complete phase 17.9`, and reaches `main` with this PR.
- **17.10 is this re-audit.**

Every issue M17 set out to close is closed. So are the gap issues the audits filed:

```bash
for i in 464 494 440 425 426 435 454 424 458 537 538 557; do
  gh issue view $i --json state,closedAt,closedByPullRequestsReferences; done
#   #464, #494  CLOSED 2026-10-15  by #503       #440  CLOSED 2026-10-15  by #507
#   #425        CLOSED 2026-10-15  by #510       #426, #435  CLOSED 2026-10-16  by #523
#   #454, #424, #458  CLOSED 2026-10-17  by #528
#   #537, #538  CLOSED 2026-10-21  by #545       #557  CLOSED 2026-10-22  by #560
```

## 2. All tests passing, both device lanes dispatched, `headSha` compared — **MET NARROWLY** (#564)

> All tests passing — .NET, JVM, **and both device lanes dispatched**, with each lane's `headSha`
> compared against the PR head

| lane | run | attempt | `headSha` | result | source |
|---|---|---|---|---|---|
| .NET, CI | `ci.yml` `build-test` [39764450069](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/39764450069), PR #563 | 1 | `026777c0` | **1196 / 2 / 0**, red: `FaultNoticeTests` `:145` "dispatch_event for handler 15 did not return within 1s", then the next fact at `:200` saw handler 15's leaked notice. Filed as **#564** | `Failed!  - Failed: 2, Passed: 1196` |
| .NET, CI | same run | 2 | `026777c0` | **1372 / 0 / 0** | `dotnet test totals across 3 projects: passed=1372 skipped=0 failed=0` |
| JVM, CI | same run | 2 | `026777c0` | **206 / 0 / 0** | `JVM test totals across 34 suites: tests=206 failures=0 errors=0 skipped=0` |
| Android | `android-instrumented.yml` [39764450055](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/39764450055), PR #563 | 1 | `026777c0` | **247 / 0 / 0** | `Instrumented totals across 1 suite(s): tests=247 failures=0 errors=0 skipped=0` |
| iOS | `ios.yml` [39764450092](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/39764450092), PR #563 | 1 | `026777c0` | **288 / 0** | `XCTest cases: passed=288 failed=0` |
| .NET, local | detached worktree of `d31e9989` | 1 | `d31e9989` | **1372 / 0 / 0** | Analyzers 27, Renderer 147, Runtime 1198, exit 0 |

```bash
dotnet test BlazorNative.sln -c Debug --nologo      # one run, before any mutation
```

- **One local run, not five.** The five-run soak existed for #454, and #454 is closed. The design says one.
- **The device lanes** run on this phase's PR. It is docs-only against `d31e9989`, so every lane exercises HEAD's code.

**Grade: MET NARROWLY, citing #564.** Both device lanes are green at attempt 1 on the PR head `026777c0`, and `git diff --stat origin/main 026777c0 -- ':!docs'` is empty. `build-test` failed at attempt 1 on a signature that is not #551's. That failure was diagnosed: a 1 s dispatch bound at `FaultNoticeTests.cs:145` missed on the Windows runner, and the stuck dispatch leaked a notice into the next fact. The code passed `ci.yml` on run 39750959240, and the signature is in none of the 15 failed runs before it. It is filed as #564, and the failed job was re-run once and passed. By the rules below, any other failure is diagnosed before the grade is set. A test failure that needed a re-run narrows the item. The rules the grade was set by:
- Only attempt 1 counts, and each lane's `headSha` must equal the PR head.
- If `build-test` is green at attempt 1, and `git diff --stat origin/main <head> -- ':!docs'` is empty, item 2 is **MET**.
- If `build-test` fails on #551's signature, it is diagnosed against that signature and its job is re-run once. The signature is `ShutdownQuiescenceTests.cs:300` at `:248`, "completing the held call delivered no frame even without shutdown". Both attempts are recorded, and item 2 is **MET NARROWLY**, citing #551.
- Any other failure is diagnosed before the grade is set.

## 3. #464 is fixed as decided — **MET**

> a fault from a continuation of dispatch A that runs inline inside dispatch B's synchronous part
> is A's, delivered as A's late FaultNotice under A's handler id, and B keeps its own synchronous
> fault, rc 2. … **Both** outcomes are pinned and mutation-proven.

```bash
git show origin/main:src/BlazorNative.Renderer/NativeRenderer.cs | grep -n \
  "anotherDispatchs\|ReferenceEquals(s_flowingScope.Value, scope)\|scope.ShellCallBegun = true"
#   631:            bool anotherDispatchs = owner is not null
#   634:            if (!anotherDispatchs)
#   1641:        if (t_syncScope is { Fault: null } scope && ReferenceEquals(s_flowingScope.Value, scope))
#   1642:            scope.ShellCallBegun = true;
```

- **The checks.** Capture checks the flowing scope at `:631`–`:634`. The begun-call mark checks it at `:1641`.
- **The pins** are in `FaultNoticeTests.cs`:
  - `AnInlineContinuationsFault_IsItsOwnDispatchs_NotTheWindows`, `:953`;
  - `AnInlineContinuationsFault_LeavesTheWindowsOwnFault_Rc2`, `:973`;
  - `ACallBegunByAnInlineContinuation_DoesNotMarkTheWindow`, `:992`.
- **The reds.** 17.1-M1 and M2 were re-run on HEAD by 17.8, and the code has not changed since.

**Grade: MET.** Both outcomes are pinned and have recorded reds, so the item's words hold. Item 11 grades pin by pin whether every name clause of these pins has its own red. Two of these pins have open name-clause sites, at `:963`, `:966` and `:979`, and they are graded there, under #561.

`:963` and `:966` assert item 3's own wording, "delivered as A's late FaultNotice under A's handler id". If 17.11 reds them, item 3 holds without narrowing. Until then, item 3 is MET on the two outcomes the DoD asks to be mutation-proven: A's fault reaching A, by 17.1-M1 at `:981`, and B's own fault surviving, by 17.1-M2 at `:1000`. "Under A's handler id", at `:966` and `:984`, has no red until 17.11.

`#464`: `CLOSED` by #503.

## 4. #494 is closed — **MET NARROWLY**

`#494`: `CLOSED` by #503. The cancellation fact is unchanged:

```bash
git show origin/main:tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs | sed -n '717p;726,729p'
#   717  public void ACancellationAfterABegunHostCall_IsRc0_AndSendsNothing()
#   726  // Rule 7 limit, named in 17.1 from #494: no probe-side change can red this rc assertion,
#   727  // because turning the cancellation into a fault after the begun call is rc 0 too. The
#   728  // 16.8 re-audit's X2 reds the Assert.Empty below instead.
#   729  Assert.True(rc == 0, $"rc was {rc}: a handler cancelled in its synchronous part after it began a host "
```

**The narrowing:** `:729` has no recorded red. The test names that at `:726`–`:728`, and so does `MILESTONE.md:84`.

**The rest holds on HEAD:**
- **The rc claim is pinned.** `FaultNoticeTests.cs:415`'s "COVERED SINCE 17.1" note names its pin.
- **The contract** sits between `BEGIN rc-contract` and `END rc-contract` at `Exports.cs:473`–`:484`. Since 17.8 the only change to `Exports.cs` is `VersionNumber`, at `:152`.

## 5. #440 is fixed — **MET**

`#440`: `CLOSED` by #507. `#537`: `CLOSED` by #545.

```bash
git show origin/main:src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt | grep -n \
  'HostCallOp.BIOMETRICS -> biometricsExecutor\|HostCallOp.SECURE_STORAGE -> secureStorageExecutor\|"BlazorNative-Biometrics"\|"BlazorNative-SecureStorage"\|private fun afterAuth\|denial failed to complete\|getMainExecutor(appContext)'
#   284  Thread(r, "BlazorNative-Biometrics")        287  Thread(r, "BlazorNative-SecureStorage")
#   297  private fun afterAuth(…)
#   327  HostCallOp.BIOMETRICS -> biometricsExecutor.execute {
#   330  HostCallOp.SECURE_STORAGE -> secureStorageExecutor.execute {
#   857  afterAuth with "secure set('$key', auth) denial failed to complete"
#   920  afterAuth with "secure getWithAuth('$key') denial failed to complete"
#   969  activity, ContextCompat.getMainExecutor(appContext),
git show origin/main:templates/…/AndroidShellBridge.kt | cmp - <(git show origin/main:src/…/AndroidShellBridge.kt)   # no output
```

- **The begin returns at once.** Both ops hand the work to a single-thread executor.
- **Every denial hops through `afterAuth`,** onto the secure-storage executor.
- **The template copy is byte-identical.**
- **The pins:** `AndroidHostCallContractTest.kt:119`, `:123` and `:204`. Their reds are as 17.8 recorded them, and their files are unchanged.

**#506 is adjacent.** It concerns order across a prompt, and the item says nothing about order.

## 6. #425 is fixed: a mount replaces the root — **MET NARROWLY**

`#425`: `CLOSED` by #510.

```bash
git show origin/main:src/BlazorNative.Runtime/HostSession.cs | grep -n \
  "RootGeneration++\|UnmountAndMount(effective, renderer, replacedByTheShell: true)\|renderer.UnmountReplacedRoot(current)\|IsCodeOfAReplacedRoot("
#   294 IsCodeOfAReplacedRoot(…)   509, 518 renderer.RootGeneration++   512 UnmountAndMount(…)   626 renderer.UnmountReplacedRoot(current)
git show origin/main:src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/BlazorNativeRuntime.kt | grep -n "if (retired) {\|fun retire()\|retiredForTest"
#   76 if (retired) {   171 retiredForTest   565 fun retire(): Boolean {
git show origin/main:src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt | grep -n "if (booted) runtime.retire()\|#511"
#   474 // boot, while `booted` is false and the boot thread may still be inside start(): #511.
#   475 if (booted) runtime.retire()
git show origin/main:templates/…/MainActivity.kt | grep -n "if (booted) runtime.retire()"          # 488
git show origin/main:src/BlazorNative.Runtime/NativeNavigationManager.cs | grep -n FromAReplacedRoot  # 150, 231, 344
```

- **a. A mount disposes the old root,** at `HostSession.cs:509`–`:512` and `:626`.
- **b. A retired runtime drops a frame,** at `BlazorNativeRuntime.kt:76`.
- **c. Recreation is wired** in both `MainActivity` copies, `:475` and the template's `:488`. It is proven on device by the rotation pin, `RecreationAndroidTest.kt:157`.
- **d. A disposed root's navigation is dropped,** at `NativeNavigationManager.cs:150` and `:231`.

**The narrowing: #511**, which is `OPEN`. `MainActivity` retires only a booted runtime, as `:474`–`:475` says.

## 7. #426 is fixed — **MET**

`#426`: `CLOSED` by #523.

```bash
git show origin/main:src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/BnShellLog.kt | grep -n "privacy: BnPrivacy = BnPrivacy.REDACTED\|var reveal"
#   131 var reveal: Boolean = false
#   175, 181, 188, 194, 200, 218: privacy: BnPrivacy = BnPrivacy.REDACTED
git show origin/main:src/…/shell/MainActivity.kt | grep -n "BnShellLog.reveal =\|BnStderrLogcatPump.install()"
#   269 BnShellLog.reveal = (applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE) != 0
#   273 BnStderrLogcatPump.install()
```

- **The seam:** every entry point defaults to REDACTED, and `reveal` is set from the debuggable flag before the pump.
- **The template** sets `reveal` at its own `:282`.
- **The twin pin** is `LogPrivacyDriftTests`. Task 2 re-redded its facts on HEAD as L-M5, L-M6, L-N1, L-N2, L-E1 and L-RF, in §11d.

## 8. #435 is fixed by a forwarding hook — **MET NARROWLY**

`#435`: `CLOSED` by #523. `src/BlazorNative.Renderer/NativeRenderer.cs:1758`–`:1761` on HEAD:

```csharp
if (Handler.Target is IForwardsUiEvent wrapper
    && wrapper.ForwardedHandler(EventName, Handler) is { } forwarded
    && OwnerOf(forwarded) is { Framework: false } app)
    return ($"{app.Declaring}::{app.Method}#{EventName}", $"{app.Declaring}.{app.Method}");
```

- **The flipped test** is `SlowHandlerWarningTests.cs:612`, and the five other wrappers' facts are at `:648`–`:680`.
- **H-M1 and H-M2 re-red them on HEAD,** in §11d.

**The narrowing: #522**, which is `OPEN`. For a bound input, `forwarded` is the page's compiler lambda. With `:after`, the app's method is not named.

## 9. #454, #424 and #458 are fixed — **MET NARROWLY**

`#454`, `#424` and `#458`: `CLOSED` by #528.

```bash
git show origin/main:tests/BlazorNative.Runtime.Tests/ReferenceDriftTests.cs | grep -n "GenerationBound = \|BoundedScript.RunBounded"   # 39, 55
git show origin/main:tests/BlazorNative.Runtime.Tests/BoundedScript.cs | grep -n "WaitForExit(bound)\|did not finish within\|#530 tracks"
#   65 bool exited = p.WaitForExit(bound);   79 … did not finish within {bound}: …   83, 89 … #530 tracks …
git show origin/main:src/BlazorNative.Jni/build.gradle.kts | grep -n nativeRuntimeFiles           # 257; inputs at 267, 299, 397
git grep -n "fun pollUntil(" origin/main -- src templates                                         # only Poll.kt:21
```

**The narrowing: #530**, which is `OPEN`. The message names the script and which case it hit, but not the stalled command inside the script. Its own text, at `BoundedScript.cs:83` and `:89`, defers that to #530.

## 10. No ABI change — **MET**

Every command was run over `23ffc4ab..origin/main`, with `origin/main` = `d31e9989`.

```bash
# the exports, both ends
for r in 23ffc4ab origin/main; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -c '^\s*\[UnmanagedCallersOnly(EntryPoint'; done
#   10
#   10
for r in 23ffc4ab origin/main; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -o '\[UnmanagedCallersOnly(EntryPoint = "[a-z_]*"' | sort | md5sum; done
#   52ec8c5e37245dd1522729412c4b3c2a  -
#   52ec8c5e37245dd1522729412c4b3c2a  -
git grep -n 'UnmanagedCallersOnly(EntryPoint' origin/main -- 'src/*.cs' | grep -v Exports.cs
#   only InteropBoundaryAnalyzer.cs:60, the analyzer's message text

# the 80-byte struct
git diff --stat 23ffc4ab origin/main -- src/BlazorNative.Runtime/BridgeProtocolNative.cs                     # empty
for r in 23ffc4ab origin/main; do git show $r:src/BlazorNative.Runtime/BridgeProtocolNative.cs \
  | awk '/public struct BlazorNativeBridgeCallbacks/,/^}/' | grep -c "public IntPtr"; done                   # 10, 10
git show origin/main:src/BlazorNative.Runtime/BridgeProtocolNative.cs | grep -n "80 bytes"                  # :91, :96
git diff --stat 23ffc4ab origin/main -- src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeBindings.kt   # empty
git diff 23ffc4ab origin/main -- src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h \
  | grep -E "^[-+][^-+]" | grep -v -E "^[-+]\s*(//|\*|/\*)"                                                     # empty: 4 comment lines

# the wire
git diff --stat 23ffc4ab origin/main -- src/wire-vocabulary.json                                              # empty
git diff --stat 23ffc4ab origin/main -- '*.g.cs' '*.g.kt' '*.g.swift' '*.g.h' src/BlazorNative.Renderer/FrameEncoder.cs   # empty

# since 17.8
git diff --stat 36b2716f origin/main -- src/BlazorNative.Runtime/Exports.cs src/BlazorNative.Runtime/BridgeProtocolNative.cs \
  src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h src/wire-vocabulary.json
#   Exports.cs | 2 +-   : VersionNumber 0.20.1 → 0.20.2, release-please's c827b6c6
git log --format='%h %s' 23ffc4ab..origin/main | grep "chore(main)"
#   c827b6c6 … 0.20.2 (#552)   5c8f29cb … 0.20.1 (#526)   bad7c3fd … 0.20.0 (#504)
```

- **The exports:** the same 10 at both ends, with the same names.
- **The struct:** the same 80-byte struct of 10 `IntPtr` fields.
- **The C header:** comment lines only.
- **The wire:** unchanged.
- **Since 17.8:** only release-please's version string.

---

## 11. Every new pin conforms to the pin standard — **NOT MET**

> **Every new pin conforms to [`docs/pin-standard.md`](../pin-standard.md)** on Rules 2–5 and 7. Its
> mutations are recorded, **including its anchors**, and its register row or census cell is written
> **in the phase that adds it**.

**Shorthand:**
- R1–R5 are the 17.1–17.5 records, R7 is the 17.7 record, and "17.9" is its record.
- PS is `docs/pin-standard.md`, read on HEAD.
- T3 is 17.8's re-reds on HEAD.

All grading is under RS1. A red is an assertion that fails at its line, with that line's message.

### 11a. The site unit, by owner ruling, 2026-10-22

> The site unit for item 11 is every **labelled anchor**, plus the assertion or assertions that carry
> the claim in the test's **NAME**. Summary sub-clauses and sibling statements next to a redded
> statement are NOT graded.

**What it means here:**
- **A labelled anchor** is an assertion that the test's own comment or message calls an anchor. That is 17.8's definition.
  - A cell that calls a line an anchor does not make it one.
  - A label that names more than one statement, such as `// anchor: both … ran` on the first of two lines, labels every statement it names. Coordinator ruling, 2026-10-22.
- **`ForwardingHookTests.cs:94`, `:96` and `:98` are in the hard set by coordinator ruling,** 2026-10-22. Task 2 graded them as summary clauses. The ruling keeps them because the name's "TheHook" is claimed of every wrapper, and each wrapper implements its own `ForwardedHandler`.
- **A name clause** is each separate claim in the method name.
  - For example, `ShowsItAndResetsHistory` makes two claims. Each needs the assertion that carries it to have its own red.

**Why the ruling exists.** Each pass broadened the unit and found more:
- 17.6 graded pins;
- 17.8 graded anchors and one main assertion per pin;
- 17.9 graded per statement;
- 17.10's Task 2 graded per statement, including summary clauses and siblings.

The ruling fixes the unit so the audit converges, and 17.11 writes it into `docs/pin-standard.md` as a rule. Reds recorded under a broader unit still stand. 17.9's planted-call forms are one example. They are just not required.

### 11b. The population, from git

The population was built from the squashes with `git show <squash> -- tests src/BlazorNative.Jni/src/test src/BlazorNative.Jni/src/androidTest src/BlazorNative.Apple templates`:
- the added `[Fact]`, `[Theory]`, `@Test` and `func test` were grepped;
- then the added and removed method declarations were checked for renames;
- every name was checked on `origin/main` with `git grep`.

| PR | Squash | Added | Flipped | Rows |
|---|---|---|---|---|
| #503, 17.1 | `00976fd7` | 7 `FaultNoticeTests` | — | 7 |
| #507, 17.2 | `31af87ce` | 5 `AndroidHostCallContractTest` | — | 5 |
| #510, 17.3 | `6aa68096` | 14: 9 `RecreationTests`, 4 `RecreationAndroidTest`, 1 `RetireLateContinuationTest` | `retired_runtime_still_receives_…` → `a_retired_runtime_drops_a_frame_from_a_late_continuation` | 15 |
| #514 | `9380b301` | 0 | — | 0 |
| #523, 17.4 | `41af910c` | 44: 11 `SlowHandlerWarningTests`, 6 `ForwardingHookTests`, 13 `LogPrivacyDriftTests`, 9 `BnShellLogTest`, 3 `BnDiagnosticsTest`, 1 `SlowHandlerProbeTest`, 1 `FaultNoticeLogAndroidTest` | `TwoBnInputs_…_KnownLimit` → `…_GetTwoWarnings_NamingTheAppsHandlers` | 45 |
| #528, 17.5 | `2f889244` | 12: 5 `GeneratorRunTests`, 3 `NativeRuntimeInputDriftTests`, 2 `PollUntilDriftTests`, 2 `PollAndroidTest` | plus the Swift assertion `BnGeolocationTests.swift:219` | 13 |
| #545, 17.7 | `262177cc` | 2 | — | 2 |
| #560, 17.9 | `d31e9989` | 0 | — | 0 |

That gives **87 rows**: 84 added methods, 2 flipped and 1 Swift assertion. This matches 17.8's §11a row for row.

### 11c. The site map

Task 2 first mapped every pin under the broad unit. That unit covers every assertion statement that a pin's name, doc summary or in-body comment claims, counted per statement, with a helper's statement counted once for each calling pin. The result was **263 sites**:
- **205 have an RS1 red:** 202 OK, plus the 3 named limits in 11f.
- **58 have none**, in 37 pins:
  - 28 are name or summary clauses;
  - 29 are claimed siblings;
  - 1 is a red named without its line or message.

Each of the 58 was then graded under the ruling's unit:
- **28 are sites under the unit.** They are the hard set, filed as **#561** for 17.11.
- **30 are not.** They are filed as **#562** for M18.

A site with "none" has no row in R1–R5, R7, 17.9, the 17.6 audit or the 17.8 re-audit that enters its line with its own message. Each was checked by grepping every record for the line number in context.

| row | PR | pin, file:line | PS | sites | RS1 red | no red: #561 | no red: #562 |
|---|---|---|---|---|---|---|---|
| 1 | #503 | `FaultNoticeTests.AnInlineContinuationsFault_IsItsOwnDispatchs_NotTheWindows` `:953` | `:493` | 4 | 2 | `:963`, `:966` | — |
| 2 | #503 | `FaultNoticeTests.AnInlineContinuationsFault_LeavesTheWindowsOwnFault_Rc2` `:973` | `:494` | 4 | 2 | `:979` | `:986` |
| 3 | #503 | `FaultNoticeTests.ACallBegunByAnInlineContinuation_DoesNotMarkTheWindow` `:992` | `:495` | 3 | 3 | — | — |
| 4 | #503 | `FaultNoticeTests.AFaultWithNoDispatchFlow_InsideAWindow_IsTheWindows` `:1010` | `:496` | 3 | 3 | — | — |
| 5 | #503 | `FaultNoticeTests.AFinishedDispatchsFault_InsideAWindow_IsNotTheWindows` `:1027` | `:497` | 2 | 2 | — | — |
| 6 | #503 | `FaultNoticeTests.AFinishedDispatchsFault_InsideAWindow_IsLoggedInStrictModeToo` `:1056` | `:498` | 3 | 3 | — | — |
| 7 | #503 | `FaultNoticeTests.AChildsBegunCall_DuringTheReRender_MarksTheDispatch_SoALaterRenderFaultIsRc0` `:799` | `:499` | 4 | 4 | — | — |
| 8 | #507 | `AndroidHostCallContractTest.biometrics_begin_returns_before_the_check_runs` `:119` | `:552` | 3 | 3 | — | — |
| 9 | #507 | `AndroidHostCallContractTest.secure_storage_begin_returns_before_the_keystore_work_runs` `:123` | `:552` | 3 | 3 | — | — |
| 10 | #507 | `AndroidHostCallContractTest.secure_storage_keeps_request_order` `:128` | `:552` | 2 | 1 | — | `:138` |
| 11 | #507 | `AndroidHostCallContractTest.a_throw_on_the_executor_completes_error_never_a_hang` `:142` | `:552` | 2 | 1 | `:147` | — |
| 12 | #507 | `AndroidHostCallContractTest.the_after_auth_work_runs_on_the_secure_storage_executor` `:152` | `:552` | 2 | 2 | — | — |
| 13 | #510 | `RecreationTests.ASecondMount_DisposesTheFirstRoot_SoItsLateContinuationRendersNothing` `:106` | `:554` | 3 | 3 | — | — |
| 14 | #510 | `RecreationTests.ASecondMount_DisposesTheFirstRoot` `:128` | `:554` | 2 | 2 | — | — |
| 15 | #510 | `RecreationTests.AReplacingMount_SendsTheNewShellNoRemovalFrameForTheOldRoot` `:144` | `:554` | 2 | 2 | — | — |
| 16 | #510 | `RecreationTests.ASecondMount_StaysOnTheCurrentPage_WithItsHistory` `:170` | `:554` | 3 | 2 | `:186` | — |
| 17 | #510 | `RecreationTests.ASecondMount_ByAFreshShellAtTheRoot_ShowsTheRootAndResetsHistory` `:197` | `:554` | 3 | 2 | `:218` | — |
| 18 | #510 | `RecreationTests.ASecondMount_OfANamedPageByAFreshShell_ShowsItAndResetsHistory` `:225` | `:554` | 3 | 2 | `:244` | — |
| 19 | #510 | `RecreationTests.ANavigationFromADisposedRootsContinuation_IsDropped` `:253` | `:554` | 3 | 3 | — | — |
| 20 | #510 | `RecreationTests.ANavigationFromADisposedRootsLifecycleCode_IsDropped` `:282` | `:554` | 3 | 3 | — | — |
| 21 | #510 | `RecreationTests.AFrameOfTheOldRoot_AfterANewShellRegisters_IsNotDeliveredToIt` `:328` | `:554` | 4 | 4 | — | — |
| 22 | #510 | `RecreationAndroidTest.a_recreated_activity_never_shows_the_old_pages_frame` `:50` | `:555` | 4 | 3 | — | `:80` |
| 23 | #510 | `RecreationAndroidTest.a_recreated_activity_stays_on_its_page_and_back_goes_to_the_parent` `:91` | `:555` | 6 | 6 (1 NL) | — | — |
| 24 | #510 | `RecreationAndroidTest.a_recreated_deep_linked_activity_stays_on_the_page_it_navigated_to` `:121` | `:555` | 3 | 2 | — | `:136` |
| 25 | #510 | `RecreationAndroidTest.a_recreated_activity_on_an_unrouted_page_keeps_that_page` `:142` | `:555` | 1 | 1 | — | — |
| 26 | #510 | `RetireLateContinuationTest.a_retired_runtime_drops_a_frame_from_a_late_continuation (flipped)` `:29` | `:537` | 3 | 3 | — | — |
| 27 | #510 | `RetireLateContinuationTest.retire_waits_out_a_frame_callback_already_in_flight` `:90` | `:537` | 3 | 2 | — | `:111` |
| 28 | #523 | `SlowHandlerWarningTests.TwoBnInputs_WithDifferentSlowHandlers_GetTwoWarnings_NamingTheAppsHandlers (flipped)` `:612` | `:558` | 4 | 2 | `:623`, `:629` | — |
| 29 | #523 | `SlowHandlerWarningTests.ASlowBnCheckbox_NamesTheAppsHandler` `:648` | `:558` | 3 | 2 | — | `:644` |
| 30 | #523 | `SlowHandlerWarningTests.ASlowBnPicker_NamesTheAppsHandler` `:656` | `:558` | 3 | 2 | — | `:644` |
| 31 | #523 | `SlowHandlerWarningTests.ASlowBnSlider_NamesTheAppsHandler` `:664` | `:558` | 3 | 2 | — | `:644` |
| 32 | #523 | `SlowHandlerWarningTests.ASlowBnSwitch_NamesTheAppsHandler` `:672` | `:558` | 3 | 2 | — | `:644` |
| 33 | #523 | `SlowHandlerWarningTests.ASlowBnModalDismiss_NamesTheAppsHandler` `:680` | `:558` | 3 | 2 | — | `:644` |
| 34 | #523 | `SlowHandlerWarningTests.AWrapperWithNoAppCallback_StillKeysByItsCallSite` `:691` | `:558` | 4 | 3 | — | `:698` |
| 35 | #523 | `SlowHandlerWarningTests.AHookThatThrows_NeverFaultsTheDispatch_AndTheWarningFallsBackToTheTreeOwner` `:713` | `:558` | 5 | 2 | `:722` | `:721`, `:723` |
| 36 | #523 | `SlowHandlerWarningTests.TwoBoundBnInputs_OnTwoPages_GetTwoWarnings_NamingThePages` `:732` | `:558` | 4 | 2 | `:740`, `:746` | — |
| 37 | #523 | `SlowHandlerWarningTests.TwoBoundBnInputs_OnOnePage_GetTwoWarnings` `:758` | `:558` | 4 | 2 | `:765` | `:770` |
| 38 | #523 | `SlowHandlerWarningTests.TwoPlainBoundBnInputs_OnOnePage_GetTwoWarnings_NamingThePage` `:778` | `:558` | 4 | 2 | `:785`, `:790` | — |
| 39 | #523 | `SlowHandlerWarningTests.AGetSetBoundBnInput_NamesTheAppsSetMethod` `:797` | `:558` | 3 | 2 | — | `:803` |
| 40 | #523 | `ForwardingHookTests.EventCallbackDelegate_ReturnsTheAppsMethod` `:30` | `:557` | 1 | 1 | — | — |
| 41 | #523 | `ForwardingHookTests.EventCallbackDelegate_IsNullForAnEmptyCallback` `:38` | `:557` | 1 | 1 | — | — |
| 42 | #523 | `ForwardingHookTests.EventCallbackDelegate_WorksForBool` `:44` | `:557` | 2 | 2 | — | — |
| 43 | #523 | `ForwardingHookTests.EventCallbackDelegate_WorksForInt` `:53` | `:557` | 2 | 2 | — | — |
| 44 | #523 | `ForwardingHookTests.EventCallbackDelegate_WorksForFloat` `:62` | `:557` | 2 | 2 | — | — |
| 45 | #523 | `ForwardingHookTests.TheHook_IsNullForADelegateThatIsNotTheWrappersOwn` `:74` | `:557` | 6 | 3 | `:94`, `:96`, `:98` | — |
| 46 | #523 | `LogPrivacyDriftTests.NoAndroidLog_OutsideBnShellLog` `:108` | `:559` | 1 | 1 | — | — |
| 47 | #523 | `LogPrivacyDriftTests.TheAndroidLogScan_FindsAPlantedRawLogE` `:132` | `:559` | 2 | 2 | — | — |
| 48 | #523 | `LogPrivacyDriftTests.NoSystemErr_OutsideBnDiagnostics` `:145` | `:559` | 1 | 1 | — | — |
| 49 | #523 | `LogPrivacyDriftTests.TheSystemErrScan_FindsAPlantedWrite` `:167` | `:559` | 1 | 1 | — | — |
| 50 | #523 | `LogPrivacyDriftTests.NoOtherConsoleWriter_OutsidePreviewHost` `:178` | `:559` | 1 | 1 | — | — |
| 51 | #523 | `LogPrivacyDriftTests.TheConsoleWriterScan_FindsEachPlantedWrite` `:201` | `:559` | 2 | 2 | — | — |
| 52 | #523 | `LogPrivacyDriftTests.EverySafeArgument_IsOnALiteralMessageWithNoTemplate` `:226` | `:559` | 1 | 0 | `:243` | — |
| 53 | #523 | `LogPrivacyDriftTests.TheSafeCheck_FindsEachPlantedViolation` `:252` | `:559` | 2 | 2 | — | — |
| 54 | #523 | `LogPrivacyDriftTests.BnShellLog_DefaultsEveryEntryPointToRedacted` `:285` | `:559` | 2 | 1 | `:293` | — |
| 55 | #523 | `LogPrivacyDriftTests.BnStderrLogcatPump_ForwardsEveryLineRedacted` `:305` | `:559` | 4 | 2 | — | `:313`, `:317` |
| 56 | #523 | `LogPrivacyDriftTests.SwiftBnLog_DefaultsToRedacted_AndItsPumpTakesTheDefault` `:329` | `:559` | 3 | 2 | — | `:338` |
| 57 | #523 | `LogPrivacyDriftTests.MainActivity_SetsRevealFromDebuggable_BeforeThePump` `:351` | `:559` | 3 | 2 | `:365` | — |
| 58 | #523 | `LogPrivacyDriftTests.TheSharedJvmCodesReports_KeepTheirLevel` `:406` | `:559` | 3 | 3 | — | — |
| 59 | #523 | `BnShellLogTest, an unrevealed REDACTED message is written as private…` `:210` | `:560` | 2 | 2 | — | — |
| 60 | #523 | `BnShellLogTest, an unrevealed SAFE message keeps its text…` `:224` | `:560` | 2 | 2 | — | — |
| 61 | #523 | `BnShellLogTest, a revealed line is written as today…` `:236` | `:560` | 6 | 6 | — | — |
| 62 | #523 | `BnShellLogTest, redaction is the default privacy` `:253` | `:560` | 1 | 1 | — | — |
| 63 | #523 | `BnShellLogTest, info debug and verbose honour SAFE…` `:268` | `:560` | 2 | 2 | — | — |
| 64 | #523 | `BnShellLogTest, warn and error are never gated by level` `:282` | `:560` | 1 | 1 | — | — |
| 65 | #523 | `BnShellLogTest, a throwing throwable sink is swallowed` `:294` | `:560` | 1 | 1 | — | — |
| 66 | #523 | `BnShellLogTest, a WARN diagnostic reaches the shell as WARN…` `:301` | `:560` | 2 | 2 | — | — |
| 67 | #523 | `BnShellLogTest, the pump forwards a record as private…` `:325` | `:560` | 2 | 2 | — | — |
| 68 | #523 | `BnDiagnosticsTest, the default sink writes to System err` `:24` | `:561` | 3 | 3 | — | — |
| 69 | #523 | `BnDiagnosticsTest, the sink is replaceable` `:45` | `:561` | 4 | 3 | — | `:54` |
| 70 | #523 | `BnDiagnosticsTest, BnPrivacy has exactly the two members` `:59` | `:561` | 1 | 1 | — | — |
| 71 | #523 | `SlowHandlerProbeTest.a_slow_bncheckbox_callback_warns_naming_the_apps_method_not_the_wrapper` `:94` | `:549` | 3 | 2 | `:124` | — |
| 72 | #523 | `FaultNoticeLogAndroidTest.a_fault_notice_message_is_readable_in_a_debuggable_build` `:73` | `:562` | 4 | 4 | — | — |
| 73 | #528 | `GeneratorRunTests.AChildThatLeavesItsPipeHeld_FailsNamedWithinTheBound_AndLeavesNoProcess` `:57` | `:563` | 14 | 9 (1 NL) | — | `:94`, `:98`, `:99`, `:100`, `:101` |
| 74 | #528 | `GeneratorRunTests.AScriptStillRunningAtTheBound_FailsSayingItWasStillRunning` `:140` | `:564` | 6 | 3 | `:167` | `:168`, `:170` |
| 75 | #528 | `GeneratorRunTests.AScriptThatExitsWithItsPipeHeld_FailsSayingItExited` `:189` | `:564` | 9 | 6 (1 NL) | — | `:222`, `:224`, `:225` |
| 76 | #528 | `GeneratorRunTests.GenerateReference_RunsEveryMSBuildCallWithNodeReuseOff` `:301` | `:565` | 3 | 1 | `:318`, `:320` | — |
| 77 | #528 | `GeneratorRunTests.GenerateReference_NodeReusePin_CatchesAPlantedCall` `:330` | `:565` | 9 | 9 | — | — |
| 78 | #528 | `NativeRuntimeInputDriftTests.TheJvmTasksThatLoadTheRuntime_DeclareItAsAnInput` `:61` | `:566` | 1 | 1 | — | — |
| 79 | #528 | `NativeRuntimeInputDriftTests.TheTemplatesTestTask_DeclaresTheRuntimeAsAnInput` `:66` | `:566` | 1 | 1 | — | — |
| 80 | #528 | `NativeRuntimeInputDriftTests.APlantedBlockWithoutTheInput_IsReported` `:85` | `:566` | 3 | 2 | — | `:95` |
| 81 | #528 | `PollUntilDriftTests.NoPrivatePollUntil_RemainsInAndroidTest` `:35` | `:567` | 1 | 1 | — | — |
| 82 | #528 | `PollUntilDriftTests.APlantedPrivateCopy_IsReported` `:52` | `:567` | 2 | 2 | — | — |
| 83 | #528 | `PollAndroidTest.a_poll_on_a_destroyed_activity_fails_naming_it` `:29` | `:568` | 3 | 1 | `:40`, `:43` | — |
| 84 | #528 | `PollAndroidTest.a_predicate_that_throws_is_rethrown_unchanged` `:48` | `:568` | 2 | 1 | `:59` | — |
| 85 | #528 | `BnGeolocationTests.testUnknownOpCompletesErrorNeverCrashes, the 17.5 assertion` `BnGeolocationTests.swift:219` | `:569` | 1 | 1 | — | — |
| 86 | #545 | `AndroidHostCallContractTest.a_denied_storage_prompt_completes_on_the_secure_storage_executor` `:204` | `:553` | 6 | 6 | — | — |
| 87 | #545 | `RecreationAndroidTest.a_rotation_retires_the_previous_activitys_runtime` `:157` | `:556` | 4 | 4 | — | — |
| | | **total, 87 pins** | | **263** | **205** | **28** | **30** |

### 11d. The hard set: 28 sites in 20 pins, each graded under the unit

The hard set has two parts. Rows 1–22, 27 and 28 are name clauses, apart from one red named without its line, `:243`. Rows 23–26 are labelled anchors: the second line of each two-line anchor whose `// anchor: both …` comment names both lines. Every other labelled anchor in the 87 pins has an RS1 red. Fixes are in #561.

| # | file:line | pin | name clause | why it is a site, and why it has no red |
|---|---|---|---|---|
| 1 | `FaultNoticeTests.cs:963` | `AnInlineContinuationsFault_IsItsOwnDispatchs_NotTheWindows` | IsItsOwnDispatchs | `:961`, B's rc 0, carries NotTheWindows. IsItsOwnDispatchs, "A's late FaultNotice under A's handler id" in the DoD's words, is carried by `:963` and `:966`. M1 reds `:961` first |
| 2 | `FaultNoticeTests.cs:966` | the same | IsItsOwnDispatchs | the same |
| 3 | `FaultNoticeTests.cs:979` | `AnInlineContinuationsFault_LeavesTheWindowsOwnFault_Rc2` | Rc2 | `:979` is the only assertion on B's rc. 17.8 graded `:981`, A's notice, as this pin's main. R1 §2 records `:979` passing on the unfixed code, and M1 reds `:981` |
| 4 | `AndroidHostCallContractTest.kt:147` | `a_throw_on_the_executor_completes_error_never_a_hang` | never a hang | `:147`'s message is "left the request pending: a hang". R2 M4 and T3 D3 crash the process, and R7 §6b rules a crash is no red |
| 5 | `RecreationTests.cs:186` | `ASecondMount_StaysOnTheCurrentPage_WithItsHistory` | WithItsHistory | `:186`, "lost its back stack", is the only history assertion. M2 reds `:183`. 17.8 had listed it as secondary, O2 |
| 6 | `RecreationTests.cs:218` | `ASecondMount_ByAFreshShellAtTheRoot_ShowsTheRootAndResetsHistory` | ResetsHistory | the only history assertion. M6 reds `:215`. 17.8 listed it as O2 |
| 7 | `RecreationTests.cs:244` | `ASecondMount_OfANamedPageByAFreshShell_ShowsItAndResetsHistory` | ShowsIt | the only "shows" assertion. M6 and M7 red `:246` |
| 8 | `SlowHandlerWarningTests.cs:629` | `TwoBnInputs_…_GetTwoWarnings_NamingTheAppsHandlers` | NamingTheAppsHandlers | `:625` carries GetTwoWarnings, and H-M1 and H-M2 red it first |
| 9 | `SlowHandlerWarningTests.cs:722` | `AHookThatThrows_NeverFaultsTheDispatch_AndTheWarningFallsBackToTheTreeOwner` | FallsBackToTheTreeOwner | `:718` carries NeverFaultsTheDispatch. Nothing enters `:722` |
| 10 | `SlowHandlerWarningTests.cs:746` | `TwoBoundBnInputs_OnTwoPages_GetTwoWarnings_NamingThePages` | NamingThePages | the count at `:742` reds first |
| 11 | `SlowHandlerWarningTests.cs:790` | `TwoPlainBoundBnInputs_OnOnePage_GetTwoWarnings_NamingThePage` | NamingThePage | the count at `:787` reds first |
| 12 | `ForwardingHookTests.cs:94` | `TheHook_IsNullForADelegateThatIsNotTheWrappersOwn` | TheHook, BnPicker | the name claims it of the hook. Each wrapper implements its own `ForwardedHandler`, so no wrapper's assertion carries another's. M6, M7 and M8 cover BnModal, BnInput and BnCheckbox only. 17.8 listed this as O2 |
| 13 | `ForwardingHookTests.cs:96` | the same | TheHook, BnSlider | the same |
| 14 | `ForwardingHookTests.cs:98` | the same | TheHook, BnSwitch | the same |
| 15 | `LogPrivacyDriftTests.cs:243` | `EverySafeArgument_IsOnALiteralMessageWithNoTemplate` | the whole name | the name's only assertion. R7 §4 P1 says only "`EverySafeArgument_…` reds too", with no line and no message. S3's "at its floor" is `:237` |
| 16 | `LogPrivacyDriftTests.cs:293` | `BnShellLog_DefaultsEveryEntryPointToRedacted` | EveryEntryPoint | `:296` checks only the defaults its regex finds, so an entry point that lost its default is caught at `:293` alone. M5 reds `:296` |
| 17 | `LogPrivacyDriftTests.cs:365` | `MainActivity_SetsRevealFromDebuggable_BeforeThePump` | FromDebuggable | L-RF reds `:359`, SetsReveal, and M7 reds `:370`, BeforeThePump. Nothing enters `:365` |
| 18 | `SlowHandlerProbeTest.kt:124` | `a_slow_bncheckbox_callback_warns_naming_the_apps_method_not_the_wrapper` | not_the_wrapper | R7 §5 H1 reds `:120` first |
| 19 | `GeneratorRunTests.cs:167` | `AScriptStillRunningAtTheBound_FailsSayingItWasStillRunning` | Fails | `:169` carries SayingItWasStillRunning, and G-M5 reds it. Nothing makes `RunBounded` return normally |
| 20 | `PollAndroidTest.kt:40` | `a_poll_on_a_destroyed_activity_fails_naming_it` | naming it | R5 L1 lets a bare NPE escape the call at `:38`. Neither assertion fails at its line |
| 21 | `PollAndroidTest.kt:43` | the same | fails | the same |
| 22 | `PollAndroidTest.kt:59` | `a_predicate_that_throws_is_rethrown_unchanged` | rethrown | R5 L2 reds `:56`, unchanged. Nothing makes `pollUntil` swallow the exception |
| 23 | `SlowHandlerWarningTests.cs:623` | `TwoBnInputs_…_GetTwoWarnings_NamingTheAppsHandlers` | anchor, second line: `RunsOf("bn-in-b")` | the comment on `:622` reads "anchor: both app handlers ran". R4 A1 and R7 H6 red `:622` first. No row enters `:623` |
| 24 | `SlowHandlerWarningTests.cs:740` | `TwoBoundBnInputs_OnTwoPages_GetTwoWarnings_NamingThePages` | anchor, second line | "anchor: both :after methods ran" on `:739`, which R4 A1 and R7 H6 red. No row enters `:740` |
| 25 | `SlowHandlerWarningTests.cs:765` | `TwoBoundBnInputs_OnOnePage_GetTwoWarnings` | anchor, second line | "anchor: both :after methods ran" on `:764`, which R4 A1 and R7 H6 red. No row enters `:765` |
| 26 | `SlowHandlerWarningTests.cs:785` | `TwoPlainBoundBnInputs_OnOnePage_GetTwoWarnings_NamingThePage` | anchor, second line | "anchor: both setters ran" on `:784`, which R7 H6 reds. No row enters `:785` |
| 27 | `GeneratorRunTests.cs:318` | `GenerateReference_RunsEveryMSBuildCallWithNodeReuseOff` | Every…NodeReuseOff | `:309` checks only the calls `MsBuildCalls` recognises. The pin's DOES NOT COVER, `:293`–`:298`, names three call forms the recogniser cannot see, and ends: "For all of them the environment variable still covers it." So the name's "Every" rests on `:318`, the env line is set, for those calls. The same shape as `LogPrivacyDriftTests.cs:293`. G-M3 reds `:309` only |
| 28 | `GeneratorRunTests.cs:320` | the same | the same | `:320`, the env line comes before the first call, carries "Every" for the same unseen calls. No row enters it |

**Task 2's other findings, each decided under the unit.** Task 2 graded 28 FA, name or summary clauses; 29 FB, claimed siblings; and 1 FC, a red with no line. The hard set above takes:
- **21 of the FA:** each FA that states a clause of the name, `GeneratorRunTests.cs:318` and `:320` among them, for the name's "Every";
- **the FC,** `:243`;
- **6 of the FB:**
  - `SlowHandlerWarningTests.cs:623`, `:740`, `:765` and `:785`, the second lines of the two-line anchors, by the coordinator's ruling;
  - `LogPrivacyDriftTests.cs:293`, because only it carries the name's "Every";
  - `SlowHandlerProbeTest.kt:124`, because "not the wrapper" is in the name.

The rest are not sites under the unit:
- **`SlowHandlerWarningTests.cs:698`, the control's rc.**
  - It is the dispatch's rc. It is not labelled, and the name StillKeysByItsCallSite is carried by `:701`–`:702`, which 17.9's B7 and B8 red.
  - So `:698` is **not a site**, and its stale red is not a site finding.
  - **PS `:558` still overclaims it** (11e).
- **Summary-only clauses, not graded:**
  - `SlowHandlerWarningTests.cs:723` and `:770`;
  - `GeneratorRunTests.cs:170`, `:224` and `:225`;
  - `NativeRuntimeInputDriftTests.cs:95`.

  Each states a clause of the doc summary, not of the name. `:770`'s pin name, for instance, is `…_GetTwoWarnings`, with no NamingThePage.
- **Siblings beside a redded carrier of the same clause:**
  - `FaultNoticeTests.cs:986` asserts that B's own fault is not sent as a notice, a no-double-report property. It is an extra claim beside the name, not its clause: LeavesTheWindowsOwnFault_Rc2 is carried by `:979`'s rc. `:979` has no red today, and it is in #561.
  - `AndroidHostCallContractTest.kt:138`. `:136` carries keeps_request_order. PS `:552` calls `:138` an anchor, but the test does not.
  - `RecreationAndroidTest.kt:80`. It is the screen check, which R3 L2 showed is blind. `:84` carries never_shows. It may be better re-scoped than redded.
  - `RecreationAndroidTest.kt:136`; `:133` carries stays_on_the_page.
  - `RetireLateContinuationTest.kt:111`. `:107` carries waits_out: it does not return while a callback is in flight. `:111` is liveness.
  - `SlowHandlerWarningTests.cs:644`, the helper's "never the wrapper", for the 5 callers. `:640` carries NamesTheAppsHandler.
  - `SlowHandlerWarningTests.cs:721` and `:803`, `Assert.Single` beside the redded naming line.
  - `LogPrivacyDriftTests.cs:313` and `:317`, links beside `:312` and `:321`.
  - `LogPrivacyDriftTests.cs:338`. `:334`'s regex also matches `write`'s default, at `BnLog.swift:254`, and `:343` carries TakesTheDefault.
  - `BnDiagnosticsTest.kt:54`; `:52` carries replaceable.
  - `GeneratorRunTests.cs:94` and `:98`–`:101`, `:168` and `:222`. Each is beside a redded `Fails` and a redded message line.

All 30 are listed by file in #562.

### 11e. Register cells that claim more than their rows

- **PS `:558`** says "M5 the control's rc".
  - M5 is green on HEAD, 28 of 28, as H-M5 in 11g shows.
  - It has been an equivalent mutant since `0bc7e6ed`, 17.4's final wave, which made `Resolve` catch every exception.
  - 17.9 §4's map repeats the claim.
- **PS `:552`** says "M4, the guard removed, reds pin 4".
  - M4 crashes the process, and no assertion fails.
  - Pin 4's `:148` has only 17.9 §3 D1's incidental red, which the cell does not cite, and `:147` has none.
- **PS `:501`–`:502`** says "Every fact's main assertion and anchor has a red that RS1 accepts … No partial is left in these seven".
  - Hard-set rows 1–3 contradict it.

### 11f. The three named limits — MET NARROWLY

None was re-run. The design says to cite 17.8's measurement.

| Limit | Measured by 17.8 on `36b2716f` | Since |
|---|---|---|
| `GeneratorRunTests.cs:213`, G2 | 11 of 15 | unchanged: `git diff --stat 36b2716f origin/main -- tests/BlazorNative.Runtime.Tests` is empty |
| `GeneratorRunTests.cs:83`, G2s | 11 of 15 | unchanged |
| `RecreationAndroidTest.kt:116` | 6 of 6 samples, R7 §6 S5 | unchanged |

Each is a non-deterministic red, named in its cell with its reason, so each is **MET NARROWLY**.

### 11g. Moved pins

**Since 17.8, none moved.** The non-docs diff is version strings only, as shown above.
- The 17.9 red heads differ from HEAD in 0 non-version lines under `tests src templates samples scripts`. They are `a0909008`, `07229aa2`, `e6d9c8f5`, `c758156f`, `1d6da88f` and `ea5c4bba`.
- 17.7's squash `262177cc` differs from HEAD in 0 such lines too.

**Staleness within a phase, which 17.8's rule missed.**
- **The gap.** 17.8 compared each pin with its phase's squash. A red taken mid-phase, before a later edit to its test or subject file, passes that check.
- **The files.** `GeneratorRunTests.cs`, `BoundedScript.cs`, `LogPrivacyDriftTests.cs` and `NativeRenderer.cs` all changed after some of their reds.
- **The re-run.** Task 2 re-redded on HEAD every site whose only RS1 red predates such a change:
  - on a detached worktree of `d31e9989`, with the classes green first, 66 of 66;
  - one exact-match mutation at a time, each restored with `git checkout`;
  - `git status --short` empty before and after each mutation.

| id | mutation, on HEAD | site entered |
|---|---|---|
| G-M1 | `BoundedScript.cs:65-67`, every wait infinite | `GeneratorRunTests.cs:91` |
| G-M2 | `BoundedScript.cs:72`, `Kill(entireProcessTree: false)` | `:107`, dev host, the pwsh shim |
| G-M3 | `generate-reference.ps1:344`, `-nodeReuse:false` removed | `:309`, and `:338` through the real line |
| G-M5 | `BoundedScript.cs:80`, `exited` → `!exited` | `:97`, `:169`, `:223` |
| H-M1 | `NativeRenderer.cs:1758`, the hook bypassed | 10 of 28: `:625`, `:640` from 5 callers, `:742`, `:767`, `:787`, `:804` |
| H-M2 | `EventCallbackDelegate.cs:14`, `=> null;` | 14 of 34: the ten above and `ForwardingHookTests` `:34`, `:48`, `:57`, `:66` |
| **H-M5** | `NativeRenderer.cs:1759-1760`, R4's M5 | **green, 28 of 28: no red at `:698`** |
| H-M6a / H-M7b | `BnModal.razor:162`, the own-handler check dropped or weakened | `ForwardingHookTests.cs:88` |
| H-M6b / H-M7a | `BnInput.cs:90`, the same | `:90` |
| L-M5 | `BnShellLog.kt:194`, `warn`'s default `SAFE` | `LogPrivacyDriftTests.cs:296` |
| L-M6 | a raw `Log.e` planted in `WidgetMapper.kt` | `:123` |
| L-N1 | `NativeFrameAdapter.kt:198`, `WARN` → `ERROR` | `:414` |
| L-N2 | `BnShellLog.kt:210`, `diagnostic` always `error` | `:442` |
| L-E1 | `PreviewHost.kt:93`, `ERROR` → `WARN` | `:434` |
| L-RF | `MainActivity.kt`, the `reveal` line removed | `:359` |
| R-M10 | `HostSession.cs:632-633`, the hold release emptied | `RecreationTests.cs:354` |
| P-1 | a private `pollUntil` appended to `FocusBlurAndroidTest.kt` | `PollUntilDriftTests.cs:45` |
| N-M4b | `build.gradle.kts`, the Test block's `withPathSensitivity` removed | `NativeRuntimeInputDriftTests.cs:77` |

- **19 of 20 reproduce as recorded. H-M5 does not.**
- **Not re-run, because they need a device lane:** R7 §6 S2 at `:102` and S3 at `:114`, both taken before `f01037d9`, and R4 L1a at the anchor `FaultNoticeLogAndroidTest.kt:88`.
  - Each still enters its line by reading.
  - #561 asks 17.11 to re-run them on its device lane.

### 11h. Comparison with 17.8 and 17.9

| site | 17.8 or 17.9 said | measured here |
|---|---|---|
| `SlowHandlerWarningTests.cs:698` | 17.8: "`:698` rc → M5". 17.9 §4: M5, a subject red | M5 is green on HEAD. Not a site under the unit, but PS `:558` overclaims it |
| `AndroidHostCallContractTest.kt:147` | 17.8: "`:147` → M4, crashes the process", ok | a crash is no red. Hard set |
| `PollAndroidTest.kt:40` | 17.8: "`:40` → L1", ok | L1 is an escaping NPE. Hard set, with `:43` |
| `FaultNoticeTests.cs:979` | 17.8 graded `:981` as row 2's main | the name's Rc2 is `:979`. Hard set |
| `LogPrivacyDriftTests.cs:243` | 17.8: "`:243` → R7 P1" | P1 gives no line or message. Hard set |
| `ForwardingHookTests.cs:94/:96/:98`, `RecreationTests.cs:186/:218` | 17.8 O2, secondary | name claims. Hard set |
| `GeneratorRunTests.cs:318/:320` | 17.8 O2, secondary | the name's "Every" for calls the recogniser cannot see. Hard set |
| `NativeRuntimeInputDriftTests.cs:95`, `RetireLateContinuationTest.kt:111` | 17.8 O2, secondary | summary or sibling. #562 |

**Every other site that 17.8 or 17.9 graded as covered is covered here too.**
- 17.9 §4 counts `:105` and `:107` once for P4 and P5. Here they count once per pin.
- No covered site is dropped, except `:698`.

### 11i. The grade

**NOT MET** on #561:
- **28 sites in 20 pins have no red under RS1:**
  - 24 name-claim sites. Twenty-three have none at all, or only a crash or an escaping exception. One, `:243`, has a red named without its line or message;
  - 4 labelled anchors, the second lines of the two-line anchors at `SlowHandlerWarningTests.cs:623`, `:740`, `:765` and `:785`.
- **Three register cells claim more than their rows:** PS `:558`, `:552` and `:501`–`:502`.

**What holds:**
- Every labelled anchor outside those four has an RS1 red.
- Every pin has a cell and at least one recorded red.
- 19 of 20 in-phase re-reds reproduce on HEAD.
- The named limits stand.

---

## Carried debt

Each issue is judged against the item it touches. None blocks the close on its own.

| issue | title | item | judgement | M18 phase |
|---|---|---|---|---|
| **#561** | m17 pins: twenty-eight name-claim and anchor sites with no assertion red, and three register cells that claim more than their rows | 11 | **fails item 11.** Filed by this re-audit | **17.11**, the gap phase, before M18 |
| **#562** | m17 pins: per-statement coverage beyond the name and anchor unit, 30 sites with no recorded red | 11 | outside the ruled unit, so it does not narrow item 11. Filed by this re-audit | M18 |
| #551 | flaky: ShutdownQuiescenceTests' positive control sees no frame for 10 s on the windows ci runner | 2 | narrows item 2 only if it fires on this phase's `build-test`; Task 4 applies the rule. **It has hit 3 times. The owner may move it to the front of M18** | 18.7 |
| #564 | flaky: a FaultNoticeTests dispatch misses its 1 s bound on the windows ci runner and leaks a notice into the next fact | 2 | narrows item 2: it fired on this phase's PR at attempt 1, run 39764450069 | M18 18.7, beside #551 |
| #500 | A re-render or queued work from another dispatch's continuation is charged to the window it runs in | 3 | adjacent: item 3 is about a fault A's own code raises. Needs a decision | 18.1 |
| #506 | Android secure storage: an auth-bound set's encrypt runs after later requests on the same key | 5 | adjacent: the item says nothing about order across a prompt | 18.2 |
| #511 | Android: an Activity recreated during boot can leave the new Activity blank | 6 | **narrows item 6** | 18.3 |
| #522 | Slow-handler warning names a compiler lambda for a bound input | 8 | **narrows item 8** | 18.4 |
| #543 | jvm suite: a test that waits on a lost host call completion hangs the whole step for 45 minutes | 2 | adjacent | 18.5 |
| #544 | android back tests: a back that also finishes the activity is caught only when its destruction wins a poll | 6, 11 | adjacent: the same timing class as the `RecreationAndroidTest.kt:116` limit | 18.5 |
| #530 | reference generator: something outlives dotnet publish and holds its output pipe | 9 | **narrows item 9** | 18.8 |
| #491 | The JVM dev loop runs on Windows only: Gradle hardcodes the win-x64 publish folder | 2 | adjacent: the reason the JNA tests do not run locally on Linux. It predates M17 | adjacent, not in the M18 phase list |

Every one of these is `OPEN` on 2026-10-22, from `gh issue view <n> --json state`.

## What M17 taught

- **The unit kept widening until the owner fixed it.**
  - Each pass graded a broader unit than the one before and found more: pins, then anchors plus one main assertion, then statements, then summary clauses and siblings.
  - The gaps found were real, but the audit could not converge while the unit moved.
  - A grading unit has to be written down before the pass that uses it. 17.11 puts it into `docs/pin-standard.md`.
- **A recorded red can go stale when a later commit changes its subject.**
  - 17.4's M5 was taken before `0bc7e6ed`, in the same phase, and has been an equivalent mutant ever since. Three records and one cell went on citing it.
  - 17.8's moved-pin rule compared each pin with its phase's squash, which cannot see a change made inside the phase.
  - A moved-pin check must compare each red's own commit with HEAD.

## Verdict

**FAIL.** Item 11 is NOT MET, so M17 cannot close.

- **Five items are MET:** 1, 3, 5, 7 and 10.
- **Five are MET NARROWLY,** each with its reason:
  - **item 2:** #564, a new CI flake, diagnosed and re-run once;
  - **item 4:** the cancellation fact's rc assertion, `FaultNoticeTests.cs:729`;
  - **item 6:** #511;
  - **item 8:** #522;
  - **item 9:** #530.
- **Item 11 is NOT MET on #561:**
  - 28 sites in 20 pins have no RS1 red: 24 name-claim sites and 4 second lines of two-line anchors;
  - three register cells claim more than their rows: PS `:558`, `:552` and `:501`–`:502`.

  The three named limits are MET NARROWLY within item 11.

**Next: a small gap phase, 17.11, on #561, followed by a short re-audit.** 17.11 does four things:
1. **It writes the site unit into `docs/pin-standard.md` as a rule.**
2. **It records a red for each of the 28 sites,** each an assertion failing at its line with its message. #561 suggests a mutation for each.
3. **It corrects the three cells.**
4. **It re-runs the three pre-edit device reds,** S2, S3 and L1a, on its lane.

The broader per-statement coverage, #562, goes to M18.

M17 stays open, and no tag is cut: `CONVENTIONS.md` records `Milestone completion tags a release: no`.
