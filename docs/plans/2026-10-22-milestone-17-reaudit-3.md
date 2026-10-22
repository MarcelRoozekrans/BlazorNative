# Milestone 17 third re-audit — Close the Dispatch Lane's Debt

**Date:** 2026-10-22
**Phase:** 17.12, [design](../superpowers/specs/2026-10-22-phase-17.12-design.md)
**Audited:** `origin/main` at **`a294e924`**, which is #567 at 2026-10-22T18:24:53+02:00.
- The M17 range is `23ffc4ab..a294e924`, where `23ffc4ab` is #499, the milestone start.
- The re-audit branch `feat/17.12-reaudit` differs from `a294e924` only under `docs/`.

**Host for local runs:** Debian 13 x64.
**Red standard:** RS1, from [the 17.7 record](2026-10-17-phase-17.7-record.md) §4 "Rulings".
**Site unit for item 11:** the **written** rule, `docs/pin-standard.md` "Grading a pin: sites and reds", added by 17.11 in #567. Nothing broader is graded here; that is #562.
**Precedent:** [the 17.10 re-audit](2026-10-22-milestone-17-reaudit-2.md), verdict FAIL, answered by 17.11, [record](2026-10-22-phase-17.11-record.md) and #567.
- Its verdicts are a list of things to re-check, not evidence.
- Every number below was measured on `a294e924`, with the command shown beside it.

**Verdict:** **PASS WITH FINDINGS**: 5 MET, 5 MET NARROWLY, item 2 **[filled in Task 4 from the PR's lanes, #551/#564 rules applied]**. See [the verdict](#verdict).
- Item 11 is MET NARROWLY, citing #568. Every site has a valid, fresh RS1 red with a quoted message: 212 sites under a strict reading of the unit, 224 with the 12 entries recorded beyond it. Nine of those quotes are recorded for the first time in [§11c](#11c-f1-nine-messages-quoted-here-for-the-first-time) of this report.
- One finding is filed: **#568**, four register cells that cite stale reds without naming them stale or citing a re-run. No cell claims more than its rows, but Rule 7's part 4 says a stale red is re-run before it is cited, so item 11 is narrowed on it.

## Scorecard

| # | DoD item | Verdict |
|---|---|---|
| 1 | All planned phases complete | **MET**: 17.1–17.11 complete; 17.12 is this re-audit |
| 2 | All tests passing, both device lanes dispatched, `headSha` compared | **[filled in Task 4 from the PR's lanes, #551/#564 rules applied]**. Local .NET 1372/0/0 on `a294e924` |
| 3 | #464 is fixed as decided | **MET** |
| 4 | #494 is closed | **MET NARROWLY**: the cancellation fact's rc assertion, `FaultNoticeTests.cs:729`, still has no red. The test names it, and so does `MILESTONE.md:84` |
| 5 | #440 is fixed | **MET** |
| 6 | #425 is fixed: a mount replaces the root | **MET NARROWLY**: `MainActivity` retires only a booted runtime (#511) |
| 7 | #426 is fixed | **MET** |
| 8 | #435 is fixed by a forwarding hook | **MET NARROWLY**: for a bound input with `:after`, the warning does not name the app's method (#522) |
| 9 | #454, #424 and #458 are fixed | **MET NARROWLY**: the stall names the script, not the stalled command inside it (#530) |
| 10 | No ABI change | **MET**: the same 10 exports, the 80-byte struct and `wire-vocabulary.json` are unchanged across the range |
| 11 | Every new pin conforms to the pin standard | **MET NARROWLY** (#568): 87 pins; every site has a valid, fresh RS1 red and a quoted message, 212 sites under a strict reading of the unit, 224 with the 12 entries recorded beyond it. Four register cells cite stale reds without the re-run Rule 7's part 4 requires. The three named limits are MET NARROWLY |

---

## The code is unchanged since 17.10

17.10 measured `d31e9989`. The only change since then outside the docs is a dependabot bump in the website's lock file:

```bash
git log --format='%h %s' d31e9989..origin/main
#   a294e924 docs(17.11): every M17 site has a fresh red, and the site unit is written into the pin standard (#567)
#   5601f274 build(deps): bump launch-editor from 2.14.1 to 2.14.2 in /website (#554)
#   836e7bf7 docs(17.10): re-audit milestone 17 against its definition of done (#563)
git diff --stat d31e9989 origin/main -- ':!docs' ':!*.md'
#    website/package-lock.json | 8 ++++----
#    1 file changed, 4 insertions(+), 4 deletions(-)
git diff --stat d31e9989 origin/main -- tests src templates samples scripts
#   (empty)
git diff --stat origin/main HEAD -- ':!docs'
#   (empty): this branch is docs-only against a294e924
```

**No product, test, template or script file changed.**
- So the code quotes of items 3–10 still hold. Each was re-read on HEAD below anyway, with its line number.
- All of them sit at the lines 17.10 recorded.

## 1. All planned phases complete — **MET**

> All planned phases complete

```bash
git show origin/main:docs/planning/ROADMAP.md | grep -n "Phase 17\."
#   3798:#### Phase 17.1 … [status: complete]   …   3916:#### Phase 17.10: Re-audit and close [status: complete]
#   3934:#### Phase 17.11: Pin-standard gaps, third pass [status: active]
#   3947:#### Phase 17.12: Re-audit and close [status: pending]
grep -n "Phase 17\.1[12]" docs/planning/ROADMAP.md      # on this branch
#   3934:#### Phase 17.11: … [status: complete]
#   3951:#### Phase 17.12: Re-audit and close [status: active]
```

**17.1–17.10 read `complete` on `a294e924`.**
- **17.11's work is merged.** #567 is `a294e924`, and #561 is `CLOSED` by #567 at 2026-10-22T16:24:56Z.
- **Its `complete` mark is only on this branch,** in `4495c467` `docs(17.12): complete phase 17.11 and plan the final M17 re-audit`, and reaches `main` with this PR.
- **17.12 is this re-audit.**

Every issue M17 set out to close is closed. So are the gap issues the audits filed:

```bash
for i in 464 494 440 425 426 435 454 424 458 537 538 557 561; do
  gh issue view $i --json state,closedAt,closedByPullRequestsReferences; done
#   #464, #494  CLOSED 2026-10-15  by #503       #440  CLOSED 2026-10-15  by #507
#   #425        CLOSED 2026-10-15  by #510       #426, #435  CLOSED 2026-10-16  by #523
#   #454, #424, #458  CLOSED 2026-10-17  by #528
#   #537, #538  CLOSED 2026-10-21  by #545       #557  CLOSED 2026-10-22  by #560
#   #561        CLOSED 2026-10-22  by #567
```

## 2. All tests passing, both device lanes dispatched, `headSha` compared — **[filled in Task 4 from the PR's lanes, #551/#564 rules applied]**

> All tests passing — .NET, JVM, **and both device lanes dispatched**, with each lane's `headSha`
> compared against the PR head

| lane | run | attempt | `headSha` | result | source |
|---|---|---|---|---|---|
| .NET, CI | **[filled in Task 4 from the PR's lanes, #551/#564 rules applied]** | | | | |
| JVM, CI | **[filled in Task 4 from the PR's lanes, #551/#564 rules applied]** | | | | |
| Android | **[filled in Task 4 from the PR's lanes, #551/#564 rules applied]** | | | | |
| iOS | **[filled in Task 4 from the PR's lanes, #551/#564 rules applied]** | | | | |
| .NET, local | detached worktree of `a294e924` | 1 | `a294e924` | **1372 / 0 / 0** | Analyzers 27, Renderer 147, Runtime 1198, exit 0; no #551 or #564 hit |

```bash
dotnet test BlazorNative.sln -c Debug --nologo      # one run, before any mutation
```

- **One local run, not five,** as in 17.10: the five-run soak existed for #454, and #454 is closed.
- **The device lanes** run on this phase's PR. It is docs-only against `a294e924`, so every lane exercises HEAD's code.

**The rules the grade is set by,** unchanged from 17.10:
- Only attempt 1 counts, and each lane's `headSha` must equal the PR head.
- If `build-test` is green at attempt 1, and `git diff --stat origin/main <head> -- ':!docs'` is empty, item 2 is **MET**.
- If `build-test` fails on #551's signature, `ShutdownQuiescenceTests.cs:300` at `:248`, "completing the held call delivered no frame even without shutdown", it is diagnosed against that signature and its job is re-run once. Both attempts are recorded, and item 2 is **MET NARROWLY**, citing #551.
- If it fails on #564's signature, a `FaultNoticeTests` dispatch missing its 1 s bound and leaking a notice into the next fact, the same applies, citing #564.
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
- **The pins** are in `FaultNoticeTests.cs`: `AnInlineContinuationsFault_IsItsOwnDispatchs_NotTheWindows`, `:953`; `AnInlineContinuationsFault_LeavesTheWindowsOwnFault_Rc2`, `:973`; `ACallBegunByAnInlineContinuation_DoesNotMarkTheWindow`, `:992`.
- **The 17.10 narrowing is lifted.** 17.10 held item 3 at MET on the two outcomes, with "under A's handler id" at `:963`, `:966` and B's Rc2 at `:979` open. 17.11 §2 N1, N2 and N3 red them, on `0f4892a5`, fresh against HEAD.

**Grade: MET.** Both outcomes and the DoD's "under A's handler id" each have a fresh RS1 red.

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
- **The contract** sits between `BEGIN rc-contract` and `END rc-contract` at `Exports.cs:473`–`:484`.
- **Both stale headers and the two `host_event` rc tables still match the contract.** The 17.8 re-audit §4 graded them on `36b2716f`, and the files that hold them are unchanged since, apart from the version string:

  ```bash
  git diff 36b2716f origin/main -- src/BlazorNative.Runtime/Exports.cs tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs | grep -E '^[-+][^-+]'
  #   -    internal const string VersionNumber = "0.20.1"; // x-release-please-version
  #   +    internal const string VersionNumber = "0.20.2"; // x-release-please-version
  ```

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
git show origin/main:templates/…/AndroidShellBridge.kt | cmp - <(git show origin/main:src/…/AndroidShellBridge.kt)   # identical
```

- **The begin returns at once.** Both ops hand the work to a single-thread executor.
- **Every denial hops through `afterAuth`,** onto the secure-storage executor.
- **The template copy is byte-identical.**
- **The pins:** `AndroidHostCallContractTest.kt:119`, `:123` and `:204`, with fresh reds in [§11](#11-every-new-pin-conforms-to-the-pin-standard--met-narrowly).

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
- **The twin pin** is `LogPrivacyDriftTests`. Every one of its sites has a fresh red, in §11.

## 8. #435 is fixed by a forwarding hook — **MET NARROWLY**

`#435`: `CLOSED` by #523. `src/BlazorNative.Renderer/NativeRenderer.cs:1758`–`:1761` on HEAD:

```csharp
if (Handler.Target is IForwardsUiEvent wrapper
    && wrapper.ForwardedHandler(EventName, Handler) is { } forwarded
    && OwnerOf(forwarded) is { Framework: false } app)
    return ($"{app.Declaring}::{app.Method}#{EventName}", $"{app.Declaring}.{app.Method}");
```

- **The flipped test** is `SlowHandlerWarningTests.cs:612`, and the five other wrappers' facts are at `:648`–`:680`.
- **Their sites have fresh reds,** including the four second anchor lines and the naming clauses 17.11 redded, in §11.

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

Every command was run over `23ffc4ab..origin/main`, with `origin/main` = `a294e924`.

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

# since 17.10
git diff --stat d31e9989 origin/main -- src/BlazorNative.Runtime/Exports.cs src/BlazorNative.Runtime/BridgeProtocolNative.cs \
  src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h src/wire-vocabulary.json                               # empty
git log --format='%h %s' 23ffc4ab..origin/main | grep "chore(main)"
#   c827b6c6 … 0.20.2 (#552)   5c8f29cb … 0.20.1 (#526)   bad7c3fd … 0.20.0 (#504)
```

- **The exports:** the same 10 at both ends, with the same names.
- **The struct:** the same 80-byte struct of 10 `IntPtr` fields.
- **The C header:** comment lines only.
- **The wire:** unchanged.
- **Since 17.10:** nothing.

---

## 11. Every new pin conforms to the pin standard — **MET NARROWLY**

> **Every new pin conforms to [`docs/pin-standard.md`](../pin-standard.md)** on Rules 2–5 and 7. Its
> mutations are recorded, **including its anchors**, and its register row or census cell is written
> **in the phase that adds it**.

**Shorthand:**
- R1–R5 are the 17.1–17.5 records, R7 the 17.7 record, R8 the [17.8 re-audit](2026-10-21-milestone-17-reaudit.md), R9 the [17.9 record](2026-10-22-phase-17.9-record.md), R10 the [17.10 re-audit](2026-10-22-milestone-17-reaudit-2.md), and "17.11" [its record](2026-10-22-phase-17.11-record.md).
- PS is `docs/pin-standard.md`, read on HEAD.

**The rule graded, and nothing broader.** PS "Grading a pin: sites and reds", `:608`–`:662`, identical on `origin/main` and this branch. A site is every labelled anchor, all of its lines, plus every assertion carrying a clause of the test's name. A site needs a red that:
- follows RS1;
- fails its own assertion, at its own line, with its own message. The 17.12 design §3.3 adds that the message be **quoted** in a record; the written section does not say it;
- is fresh: `git diff --stat <red-commit> HEAD -- <test> <subject>` is empty, on both mirror copies of a two-copy subject.

### 11a. The population, from git — 87

Built from the squashes with `git show <squash> -- tests src/BlazorNative.Jni/src/test src/BlazorNative.Jni/src/androidTest src/BlazorNative.Apple templates samples`: the added `[Fact]`, `[Theory]`, `@Test` and `func test` were grepped, renames were checked by hand, and every name was found on `origin/main` with `git grep`.

| PR | Squash | Added | Flipped or other | Rows |
|---|---|---|---|---|
| #503, 17.1 | `00976fd7` | 7 `FaultNoticeTests` | — | 7 |
| #507, 17.2 | `31af87ce` | 5 `AndroidHostCallContractTest` | — | 5 |
| #510, 17.3 | `6aa68096` | 14: 9 `RecreationTests`, 4 `RecreationAndroidTest`, 1 `RetireLateContinuationTest` | `retired_runtime_still_receives_…` → `a_retired_runtime_drops_a_frame_from_a_late_continuation` | 15 |
| #514 | `9380b301` | 0 | — | 0 |
| #523, 17.4 | `41af910c` | 44: 11 `SlowHandlerWarningTests`, 6 `ForwardingHookTests`, 13 `LogPrivacyDriftTests`, 9 `BnShellLogTest`, 3 `BnDiagnosticsTest`, 1 `SlowHandlerProbeTest`, 1 `FaultNoticeLogAndroidTest` | `TwoBnInputs_…_KnownLimit` → `…_GetTwoWarnings_NamingTheAppsHandlers` | 45 |
| #528, 17.5 | `2f889244` | 12: 5 `GeneratorRunTests`, 3 `NativeRuntimeInputDriftTests`, 2 `PollUntilDriftTests`, 2 `PollAndroidTest` | plus the Swift assertion `BnGeolocationTests.swift:219` | 13 |
| #545, 17.7 | `262177cc` | 2 | — | 2 |
| #560, 17.9 | `d31e9989` | 0 | — | 0 |
| #567, 17.11 | `a294e924` | 0, docs only | — | 0 |

**87 rows**: 84 added methods, 2 flipped and 1 Swift assertion. This matches R10 §11b row for row.

### 11b. Sites, coverage and freshness — 212 under the strict unit, 224 entries

**The sites were derived independently** from the 87 method bodies on HEAD, then compared with 17.11 §4.
- **The derivation reproduces 17.11 §4's 224 site entries.** Nothing under the written unit is missing from it.
- **12 of 17.11's entries are outside a strict reading of the unit,** and each has a valid, fresh red anyway, so they do no harm: `LogPrivacyDriftTests.cs:220` and `:278`; `GeneratorRunTests.cs:338`, `:341`, `:344`, `:359`, `:79` and `:209`; `BnDiagnosticsTest.kt:53` and `:55`; `PollUntilDriftTests.cs:64`; `BnShellLogTest.kt:296`.
- **`RetireLateContinuationTest.kt:47` is a labelled anchor:** the KDoc at `:18` names it.

**Freshness was recomputed for all 253 sweep rows of 17.11 §4,** with both mirror copies of every two-copy subject, the `androidMain` and `main` copy and the template's.
- It finds exactly 17.11's 13 stale rows, and no other.
- Each stale row has a fresh replacement in 17.11 §4, and each replacement is fresh too.
- 17.11's stale stats for `BnShellLog.kt` count one copy, "1 file changed", where both copies give "2 files changed". No row changes state.
- Every red commit resolves.

**The message check.** Each valid red was traced to its row in R1–R5, R7–R10 or 17.11, and the site's own message was looked for there.
- **215 of 224 sites** have their own message quoted in a record, or a message whose text is identical. `FaultNoticeTests.cs:978`, `:997` and `:1037` are in the identical-text case, under R7 §4 FN0's `:1069` quote.
- **9 sites, F1,** had their red recorded by line only. This audit re-ran all nine on HEAD and quotes them in §11c. With that, every site has a valid, fresh, quoted RS1 red: 212 sites under a strict reading of the unit, 224 with the 12 entries recorded beyond it.

**Twelve sampled reds, re-run on HEAD.** Ten come from pins 17.11 gave no new red.
- They ran on a detached worktree of `a294e924`, one exact-match mutation at a time, each restored with `git checkout -- .`, and `git status --short` was empty before the first mutation and after every restore.
- The six .NET classes ran green first, 92 of 92. `BnShellLogTest` ran 19 of 19 and `BnDiagnosticsTest` 3 of 3.
- **All 12 reproduce as recorded:** the same failure count, the same lines, and every earlier assertion passing first. None is a crash or an escaping exception.

| # | red | mutation on HEAD | result | pins |
|---|---|---|---|---|
| 1 | R7 §4 FN0 | `NativeRenderer.cs:1981` the dispatch → `task = Task.CompletedTask;` | 29 of 34 | P1–P6 |
| 2 | R7 §4 R2 | `NativeShellBridge.cs:552` `tcs.TrySetResult(…)` removed | 4 of 9 | P13, P19, P20, P21 |
| 3 | R7 §4 F2 | `EventCallbackDelegate.cs:14` `: null;` → `: (Action)(static () => { });` | 4 of 6 | P27–P30 |
| 4 | R4 §4 C1 | `println("planted")` appended to the `androidMain` `YogaLayout.kt` | 1 of 13 | P48 |
| 5 | R9 §2 B5 | `NativeRenderer.cs:227` the root render → `Task.CompletedTask` | 9 of 9 | P14 |
| 6 | R9 §2 B7 | `NativeRenderer.cs:1871` `if (!_slowHandlerWarned.Add(key))` → `if (_slowHandlerWarned.Add(key))` | 24 of 28 | P38 |
| 7 | 17.3-M5, re-run by R8 | `NativeRenderer.cs:501` `if (_dropDeliveryOfReplacedRoot)` → `if (false)` | 1 of 9 | P15 |
| 8 | R7 §4 W1 | `BnLog.swift:199`, `warn`'s default `.redacted` → `.safe` | 1 of 13 | P54 |
| 9 | R7 §4 U1 | `PollUntilDriftTests.cs:25`, the qualifier group dropped from the regex | 1 of 2 | P84 |
| 10 | R7 §4 L1 | `LogPrivacyDriftTests.cs:81`, the `::print` arm dropped | 1 of 13 | P49 |
| 11 | R7 §5 R1, JVM | `BnShellLog.kt:231`, both sinks always `PRIVATE` | 4 of 19 | P69, P74, P75 |
| 12 | R7 §5 E5, JVM | `BnDiagnostics.kt:10`, `BnPrivacy` gains `INTERNAL` | 1 of 3 | P78 |

The .NET command was `dotnet test tests/BlazorNative.Runtime.Tests -c Debug --nologo --filter "FullyQualifiedName~<class>"`. The JVM command was `bash ./gradlew testDebugUnitTest --tests '<class>' --rerun -x verifyNativeAssets` with JDK 25, read from the JUnit XML.

### 11c. F1: nine messages, quoted here for the first time

**The finding.** Nine sites in nine pins had an RS1 red recorded by line, but no record quoted the site's own message. 17.11 §4 accepted these rows. Its own header asks for the site's own message, so the sweep was looser than its rule here.

**What this audit did.** It re-ran each red on HEAD `a294e924`, and quotes each message below. This report is now the record that quotes them. That is measurement, not a fix: no test, subject or cell changed. Every one reds at its own line with its own message, so this was a record gap, not a pin gap.

| site | pin | recorded red | re-run on `a294e924` | message, quoted from the re-run |
|---|---|---|---|---|
| `FaultNoticeTests.cs:958` | P1 | R7 §4 FN0, which quotes only `:1069` | #1 above: `NativeRenderer.cs:1981` → `task = Task.CompletedTask;`; `dotnet test tests/BlazorNative.Runtime.Tests -c Debug --nologo --filter "FullyQualifiedName~FaultNoticeTests"` | `anchor: A's continuation did not run inline inside B's synchronous part, so this proves nothing.` |
| `FaultNoticeTests.cs:1015` | P4 | R7 §4 FN0 | the same run | `anchor: the no-flow continuation did not run inside B's window.` |
| `RecreationTests.cs:120` | P13 | R7 §4 R2, "also … `:120` and `:268`", no message | #2 above: `NativeShellBridge.cs:552` `tcs.TrySetResult(new HostCallResult(status, payloadJson));` removed; `… --filter "FullyQualifiedName~RecreationTests"` | `anchor: the old page's continuation never ran after its call was answered` |
| `RecreationTests.cs:268` | P19 | R7 §4 R2 | the same run | `anchor: the old page's continuation never reached its NavigateToAsync` |
| `ForwardingHookTests.cs:49` | P28 | R7 §4 F2, which quotes only `:40` | #3 above: `EventCallbackDelegate.cs:14` `: null;` → `: (Action)(static () => { });`; `… --filter "FullyQualifiedName~ForwardingHookTests"` | `empty half: a default bool callback did not return null` |
| `ForwardingHookTests.cs:58` | P29 | R7 §4 F2 | the same run | `empty half: a default int callback did not return null` |
| `ForwardingHookTests.cs:67` | P30 | R7 §4 F2 | the same run | `empty half: a default float callback did not return null` |
| `LogPrivacyDriftTests.cs:193` | P48 | R4 §4 C1, which gives no line and no message | #4 above: `println("planted")` appended to `src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt`; `… --filter "FullyQualifiedName~LogPrivacyDriftTests"` | `A CONSOLE WRITER IN THE SHIPPED SHELL — ART sends System.out, println and printStackTrace to logcat, unredacted:` / `src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt:1378  println("planted")` / `Write through BnDiagnostics.sink in src/main, or BnShellLog in androidMain.` |
| `BnShellLogTest.kt:335` | P75 | R7 §5 R1, "and … `:335`", no message | #11 above: `BnShellLog.kt:231` `sink(priority, tag, message) else throwableSink(priority, tag, message, t)` → both `PRIVATE`; `bash ./gradlew testDebugUnitTest --tests 'io.blazornative.shell.BnShellLogTest' --rerun -x verifyNativeAssets` | `expected: <faulted: secret stack> but was: <<private>>` |

**Grade of F1: closed by this report.** Each of the nine is fresh: its test and subject are unchanged since its red's commit, on both mirror copies: `06df173e` for FN0, `bf2ce369` for R2 and F2, `118d374e` for C1 and `9371c47d` for R1.

### 11d. Register cells — no cell claims more than its rows

Read on HEAD: PS `:493`–`:499`, `:501`–`:502`, `:537`, `:549` and `:552`–`:569`. Each red a cell claims is in the row it cites, with the line and the outcome that row records.
- **R10's three overclaims are gone.** `:558` no longer cites M5. `:552` calls M4 a crash and cites D4 and D1. `:501`–`:502` cite N1–N3 and X1071.
- **The cells that touch F1 claim no more than their rows:** `:493`–`:497` say FN0 reds `RanInsideB`; `:554` says R2 reds "the lifecycle probe's navigate anchor and the late render anchor", exactly R2's two quoted sites; `:557` says F2 reds the null fact "at `Assert.Null`"; `:559` says C1 "reds the scan"; `:560` says R1 reds "the anchor `:244`".

**Finding, filed as #568: four cells cite stale reds without naming them stale.** PS part 4 says a red taken before a later edit to its test or subject "is re-run before it is cited". Every site involved has a fresh red elsewhere, so this is not an overclaim and not a site gap. **It narrows item 11.** Part 4 is a duty in Rule 7, which the DoD names: `:552` and `:553` cite reds that were never re-run, and `:552` calls the never-re-run 17.2 M5 "its honest red", the same shape as the `:558` M5 that 17.10 caught as an equivalent mutant. Read on its words, that is a Rule 7 non-conformance in four pins' cells.

| cell | stale red cited | fresh red that covers the site |
|---|---|---|
| PS `:552` | 17.2's M5, "M5 is its honest red", pin 5's `:167`; `AndroidShellBridge.kt` changed since 17.2 | R9 §3 D2, run 39744336279, already cited in the same cell |
| PS `:553` | R7 §2's red-first run 39041110104 and M1, run 39043332941, at `:196`; base `edd1cc5b`, +6/−4 since in the test and both bridge copies | 17.2 M6, re-run by R8 §11d D1, run 39673079028, not cited |
| PS `:556` | R7 §3 M1, run 39045745027, "where it was `:163`", for `:170` | R7 §3 M1 re-run by R8 §11d D4, run 39677615625, not cited |
| PS `:563` | R5 §3 M1 and M2 | R10 §11g G-M1, `:91`, and G-M2, `:107`, not cited |

The fix is to name each stale red as stale beside its fresh red, or drop it. Cells `:537`, `:555`, `:560` and `:568` already do this.

**Not a finding: P73.** `BnShellLogTest` "a throwing throwable sink is swallowed", `:294`, has no assertion, so under the written unit it has no site. 17.11 §4 lists `:296` for it with R7 §5 T1, whose red is the sink's exception escaping; the call is at `:297`. Neither reading leaves a site gap. Whether an assertion-free pin should carry a site is #562 scope, and is noted on #568.

### 11e. The three named limits — MET NARROWLY

The section and R10 §11f name them. None was re-run: `git diff --stat 36b2716f origin/main -- tests src` shows only `Exports.cs` and `Directory.Build.props`, both version strings, and nothing in `tests`.

| Limit | Measured | Since |
|---|---|---|
| `GeneratorRunTests.cs:213`, R7 §4 G2 | 11 of 15, by R8 on `36b2716f` | unchanged |
| `GeneratorRunTests.cs:83`, R7 §4 G2s | 11 of 15, by R8 | unchanged |
| `RecreationAndroidTest.kt:116`, R7 §6 S5 | 6 of 6 samples; not a site under the unit | unchanged |

Each is a non-deterministic red, named in its cell with its reason, so each is **MET NARROWLY** within item 11. The other partials are named in their cells: `GeneratorRunTests.cs:107`, G-M2, the dev host's pwsh shim only; Swift `:219`, R5 S1, not specific; and `BnShellLogTest.kt:290`, whose error half cannot red by gating.

### 11f. Comparison with the 17.11 record §4

| # | 17.11 §4 says | measured here |
|---|---|---|
| D1 | `:958` and `:1015` → R7 §4 FN0, valid | valid and fresh, but FN0 quotes only `:1069`. F1, quoted in §11c |
| D2 | `RecreationTests.cs:120` and `:268` → R7 §4 R2, valid | valid and fresh; R2 names them with no message. F1, §11c |
| D3 | `ForwardingHookTests.cs:49`, `:58`, `:67` → R7 §4 F2, valid | valid and fresh; F2 quotes only `:40`. F1, §11c |
| D4 | `LogPrivacyDriftTests.cs:193` → R4 §4 C1, valid | valid and fresh; C1 gives no line and no message, the shape of R10's `:243`. F1, §11c |
| D5 | `BnShellLogTest.kt:335` → R7 §5 R1, "incidental, line named" | valid and fresh; no message. F1, §11c |
| D6 | P73's site `:296`, T1 valid | no assertion, so no site under the unit. #562 scope |
| D7 | `BnShellLog.kt` stale stats, "1 file changed" | both copies give "2 files changed"; no row changes state |
| D8 | 224 sites | the same 224; 12 are outside a strict reading of the unit, harmlessly |

Every other site's red, commit and freshness agrees with 17.11 §4.

### 11g. The grade

**MET NARROWLY**, citing #568.
- **87 pins; 212 sites under a strict reading of the unit, 224 with the 12 entries recorded beyond it.** Every site has a valid RS1 red that is fresh against HEAD on both mirror copies, and whose own message is quoted in a record: 215 in earlier records, 9 in §11c here.
- **No register cell claims more than its rows.** But four cells cite stale reds beside fresh ones without naming them stale or citing the re-run, which Rule 7's part 4 requires: #568. That is the narrowing. Every site still has a fresh red.
- **12 of 12 sampled reds reproduce on HEAD.**
- **The three named limits are MET NARROWLY:** `GeneratorRunTests.cs:213` and `:83`, and `RecreationAndroidTest.kt:116`.

---

## Carried debt

Each issue is judged against the item it touches. None blocks the close on its own. Every one is `OPEN` on 2026-10-22, from `gh issue view <n> --json state`.

| issue | title | item | judgement | M18 phase |
|---|---|---|---|---|
| #500 | A re-render or queued work from another dispatch's continuation is charged to the window it runs in | 3 | adjacent: item 3 is about a fault A's own code raises. Needs a decision | 18.1 |
| #506 | Android secure storage: an auth-bound set's encrypt runs after later requests on the same key | 5 | adjacent: the item says nothing about order across a prompt | 18.2 |
| #511 | Android: an Activity recreated during boot can leave the new Activity blank | 6 | **narrows item 6** | 18.3 |
| #522 | Slow-handler warning names a compiler lambda for a bound input | 8 | **narrows item 8** | 18.4 |
| #543 | jvm suite: a test that waits on a lost host call completion hangs the whole step for 45 minutes | 2 | adjacent | 18.5 |
| #544 | android back tests: a back that also finishes the activity is caught only when its destruction wins a poll | 6, 11 | adjacent: the same timing class as the `RecreationAndroidTest.kt:116` limit | 18.5 |
| #551 | flaky: ShutdownQuiescenceTests' positive control sees no frame for 10 s on the windows ci runner | 2 | narrows item 2 only if it fires on this phase's `build-test`; Task 4 applies the rule | 18.7; the owner may move this phase first |
| #564 | flaky: a FaultNoticeTests dispatch misses its 1 s bound on the windows ci runner and leaks a notice into the next fact | 2 | the same rule as #551 | 18.7; the owner may move this phase first |
| #530 | reference generator: something outlives dotnet publish and holds its output pipe | 9 | **narrows item 9** | 18.8 |
| #562 | m17 pins: per-statement coverage beyond the name and anchor unit, 30 sites with no recorded red | 11 | outside the written unit, so it does not narrow item 11. P73's assertion-free pin belongs here too | M18 |
| **#568** | m17 pin-standard cells: four register cells cite stale reds without naming them stale | 11 | **narrows item 11:** Rule 7's part 4 says a stale red is re-run before it is cited. No cell overclaims and no site is uncovered. Filed by this re-audit | M18 |
| #491 | The JVM dev loop runs on Windows only: Gradle hardcodes the win-x64 publish folder | 2 | adjacent: the reason the JNA tests do not run locally on Linux. It predates M17 | adjacent, not in the M18 phase list |

## What M17 taught

- **The unit had to be written down before an audit could converge.** Each pass from 17.6 to 17.10 graded a broader unit than the one before and found more. Only once 17.11 wrote the unit into `docs/pin-standard.md` could a re-audit grade against a fixed rule, and this one found no site gap.
- **Reds go stale.** A red recorded against a subject that later changes proves nothing about HEAD. The freshness check, each red's own commit against HEAD on both mirror copies, is what caught it; #568 is the residue in the cells.
- **A message must be quoted where it is recorded.** A row that names a line but not its message cannot be checked without re-running it. Nine such rows passed 17.11's sweep, whose own header asked for the message, before this audit re-ran them.

## Verdict

**PASS WITH FINDINGS**, provisional on item 2's lanes in Task 4.

- **Five items are MET:** 1, 3, 5, 7 and 10.
- **Five are MET NARROWLY,** each with its reason:
  - **item 4:** the cancellation fact's rc assertion, `FaultNoticeTests.cs:729`;
  - **item 6:** #511;
  - **item 8:** #522;
  - **item 9:** #530;
  - **item 11:** #568, four cells citing stale reds without their re-run. Every site still has a valid, fresh, quoted RS1 red, 212 sites under a strict reading of the unit, 224 with the 12 entries recorded beyond it. The three named limits are MET NARROWLY within it.
- **Item 2 is pending:** local .NET 1372/0/0 on `a294e924`; **[filled in Task 4 from the PR's lanes, #551/#564 rules applied]**.

**The findings:**
- **F1,** nine sites whose red was recorded by line only. Closed here by quoting their messages from a re-run on HEAD, §11c.
- **#568,** four register cells that cite stale reds without naming them stale. It narrows item 11, and the cell fix is carried to M18.

**Next:** if Task 4 leaves the verdict standing, `complete-milestone` runs as its own PR, then M18 is planned. No tag is cut: `CONVENTIONS.md` records `Milestone completion tags a release: no`.
