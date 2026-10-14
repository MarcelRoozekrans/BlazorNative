# Milestone 16 re-audit — Unblock the Dispatch Lane

**Date:** 2026-10-14
**Phase:** 16.8, [design](../superpowers/specs/2026-10-14-phase-16.8-design.md),
[plan](../superpowers/plans/2026-10-14-phase-16.8-reaudit.md)
**Audited:** `origin/main` at **`5dde7d3`** (#492, 2026-10-14 08:22 UTC). The M16 range is
`3757d0e..5dde7d3`, where `3757d0e` is #421, the milestone start.
**Host for local runs:** Debian 13 x64, .NET SDK 10.0.402, PowerShell 7.6.7, Temurin 25.
**Precedent:** the [16.5 audit](2026-10-01-milestone-16-audit.md), verdict FAIL. It is cited here
for its method and its findings, never as evidence. Every number below was measured today, with the
command beside it.

**Verdict:** **PASS WITH FINDINGS**: 8 MET, 2 MET NARROWLY (items 7 and 10). See [the verdict](#verdict).

## Scorecard

| # | DoD item | Verdict |
|---|---|---|
| 1 | All planned phases complete | **MET** |
| 2 | All tests passing, both device lanes dispatched, `headSha` compared | **MET**: CI, Android and iOS green on `5dde7d3` at attempt 1; 5 of 5 local runs clean, with the Linux host caveat for #454 |
| 3 | #345 is fixed | **MET** |
| 4 | #346 is fixed | **MET**: #440 adjacent, not contradicting |
| 5 | One thread owns the renderer, and a pin proves it | **MET**: #425 adjacent, not contradicting |
| 6 | The sync-mount contract survives | **MET** |
| 7 | #8 is fixed | **MET NARROWLY**: #464. #455, the 16.5 narrowing, is fixed |
| 8 | #9 is re-assessed on measurement | **MET** |
| 9 | No ABI change | **MET**: 16.7's rc change recorded, not an ABI change |
| 10 | Every new pin conforms to the pin standard on Rules 2–5 and 7, mutations recorded | **MET NARROWLY**: 56 pins with a red on `5dde7d3`; partials named, those 16.7 left unnamed in #494 |

**Compared with 16.5:**
- Item 2 moves from MET NARROWLY to MET.
- Item 10 moves from NOT MET to MET NARROWLY.
- Item 7 stays MET NARROWLY, for a different reason: #455 is fixed, and #464 is what narrows it now.

---

## 1. All planned phases complete — **MET**

```bash
git show 5dde7d3:docs/planning/ROADMAP.md | grep -n "Phase 16\.[0-9].*\[status"
git show 5dde7d3:docs/planning/MILESTONE.md | grep -n "^[0-9]\. Phase 16"
```

Phases 16.0 to 16.7 read `[status: complete]` in ROADMAP.md and `[complete]` in MILESTONE.md on
`5dde7d3`. 16.5 is complete as an audit phase whose verdict was FAIL. Its gap phases are 16.6, the
unreddened pins, and 16.7, the rc contract (#455), and both are complete. 16.8 is this re-audit. It
reads `pending` on `5dde7d3` and `active` on this branch.

## 2. All tests passing, both device lanes dispatched, `headSha` compared — **MET**

| lane | run | attempt | `headSha` | result | source |
|---|---|---|---|---|---|
| .NET, CI | `ci.yml` [38534619496](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/38534619496), push | 1 | `5dde7d3` | **1316 / 0 / 0**: Renderer 147, Analyzers 27, Runtime 1142 | the `build-test` step's `dotnet test totals across 3 projects: passed=1316 skipped=0 failed=0` |
| JVM, CI | the same run, `build-test` | 1 | `5dde7d3` | **192 / 0 / 0**, 33 suites, JDK 25.0.4, Gradle 9.8.1 | `JVM test totals across 33 suites: tests=192 failures=0 errors=0 skipped=0` |
| Android | `android-instrumented.yml` [38539687717](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/38539687717), dispatched | 1 | `5dde7d3` | **233 / 0 / 0** | `Instrumented totals across 1 suite(s): tests=233 failures=0 errors=0 skipped=0` |
| iOS | `ios.yml` [38541157562](https://github.com/MarcelRoozekrans/BlazorNative/actions/runs/38541157562), dispatched | 1 | `5dde7d3` | **288 / 0** | `XCTest cases: passed=288 failed=0` |

```bash
gh run view <id> --json headSha,attempt,conclusion
gh run view <id> --log | grep -E "dotnet test totals|JVM test totals|Instrumented totals|XCTest cases"
```

Both device lanes were dispatched on `main` one at a time, Android first. Each run's `headSha` was
read before waiting on it. Neither needed a rerun.

**Local, five runs of the full suite on a detached checkout of `5dde7d3`:**

```bash
timeout 1200 dotnet test BlazorNative.sln -c Debug --nologo --blame-hang --blame-hang-timeout 10m
```

| run | exit | wall | Renderer | Analyzers | Runtime | hang detector |
|---|---|---|---|---|---|---|
| 1 | 0 | 16 s | 147/0 | 27/0 | 1142/0 | did not fire |
| 2 | 0 | 17 s | 147/0 | 27/0 | 1142/0 | did not fire |
| 3 | 0 | 16 s | 147/0 | 27/0 | 1142/0 | did not fire |
| 4 | 0 | 17 s | 147/0 | 27/0 | 1142/0 | did not fire |
| 5 | 0 | 18 s | 147/0 | 27/0 | 1142/0 | did not fire |

**#454, re-measured and graded as found (owner decision, 2026-10-14).** No run hung. This host is
Linux, and #454 has only been observed on Windows: 2 hangs in 5 runs on 2026-10-01, in
`ReferenceFixtureBase`'s unbounded wait on a `pwsh` child. Five clean Linux runs are weaker evidence
that #454 is gone than five clean Windows runs would be, so #454 stays open. The binding evidence
for this item is CI and the two device lanes, all green on `5dde7d3` at attempt 1. The DoD names
.NET, JVM and the device lanes, not a host. **MET.**

**Adjacent, not a DoD item: #491.** The JVM lane cannot run on Linux or macOS, because
`build.gradle.kts` hardcodes the win-x64 publish folder. The JVM evidence is therefore CI's. #491
predates M16.

## 3. #345 is fixed — **MET**

**The pin is flipped, not deleted:**

```bash
git log --follow --format='%h %ad %s' --date=short 5dde7d3 -- tests/BlazorNative.Runtime.Tests/DispatchLaneBlockingTests.cs
#   5634ed13 2026-09-27 feat(16.1): give the renderer its own thread, ... (#428)
#   b32cdbe4 2026-09-21 fix(14.1): apply the final review wave that missed the merge (#349)
#   31057e99 2026-09-21 fix(ios): split the dispatch twins ... (#347)
git show 5dde7d3:tests/BlazorNative.Runtime.Tests/DispatchLaneBlockingTests.cs | grep -n "public void\|Assert\.\|AutoCompleteHostCall = false"
```

There is one history, with no delete-and-recreate. The fact is
`AnAsyncHandlerAwaitingAnOpenHostCall_ReturnsWhileTheCallIsOpen` (`:67`):
- it sets `FakeShellHost.AutoCompleteHostCall = false` (`:96`), so the call really is open;
- it asserts `returned.Wait(TimeSpan.FromSeconds(1))` (`:113`) and `rc == 0` (`:118`);
- its anchor is `FakeShellHost.LastHostCallRequestId >= 0` (`:122`).

The device twin, pin 30 below, was re-redded today by mutation A10, which restores the pre-16.1 wait
for the whole handler.

**The false comment was corrected as the first commit of the fix.** This is history, re-read today:

```bash
git log --reverse --format='%h %s' -S "is the sync contract, not a blocking" 3757d0e..38a8a5a -- src/BlazorNative.Runtime/Exports.cs
#   f13bcd88 docs(16.1): correct the dispatch comment that called a blocking wait non-blocking
git log --reverse --format='%h' 3757d0e..38a8a5a -- src tests | head -3     # f13bcd88, 55290d26, 64ced821
git show --stat f13bcd88                                                    # Exports.cs only, 4+ 3-
```

`f13bcd8` is the first commit of PR #428 to touch `src` or `tests`. `38a8a5a` is #428's head.

**GitHub:** #345 is `CLOSED`, `COMPLETED`, at 2026-09-27T04:29:45Z.

## 4. #346 is fixed — **MET**

**The JVM test is flipped:**

```bash
git log --format='%h %ad %s' --date=short -S still_deadlocks 5dde7d3 -- src/BlazorNative.Jni
#   5634ed13 2026-09-27 ... (#428)   the flip removed the name
#   31057e99 2026-09-21 ... (#347)   the name was introduced
git grep -n "fun dispatchHostEventAndWait_returns" 5dde7d3 -- src/BlazorNative.Jni/src/test
#   HostEventTest.kt:308  dispatchHostEventAndWait_returns_while_a_handler_holds_a_host_call
```

**No main-thread caller blocks on .NET.**

```bash
grep -n "dispatchHostEvent\|fun onNewIntent\|OnBackPressedCallback\|handleOnBackPressed" src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt
grep -rn "navigateDispatcher\|dispatchHostEvent" src/BlazorNative.Apple/BnHost/*.swift
grep -rn "dispatchEventBlocking(\|dispatchEventAndWait(\|dispatchHostEventAndWait(" src/BlazorNative.Apple/BnHost src/BlazorNative.Jni/src/main src/BlazorNative.Jni/src/androidMain templates/BlazorNative.Templates/content --include=*.kt --include=*.swift
```

- **Android:** back (`MainActivity.kt:621`), `onNewIntent`'s navigate (`:577`), the lifecycle arms
  (`:400`, `:405`, `:415`) and the safe-area push (`:503`) all call the fire-and-forget
  `dispatchHostEvent`.
- **iOS:** the deep-link and notification navigators (`BnRuntime.swift:320`, `:328`), the lifecycle
  arms (`BnAppLifecycle.swift:74`) and the safe-area push (`HostViewController.swift:88`) do the
  same.
- The third grep finds **no** call of a blocking dispatch in any shipped shell source, only their
  declarations.

The caller scan 16.2 made test-only passes on `5dde7d3`: 2 of 2, `--filter
"FullyQualifiedName~NoShippedShellSource_CallsTheBlockingHostEventDispatch|FullyQualifiedName~OffendingCallDetector_MatchesACall_AndNotTheDeclaration"`.
Re-redded today by D10 (`MainActivity.kt`) and D11 (`BnRuntime.swift`); see item 10.

**The iOS XCTest twin exists:** `BnDispatchLaneTests.testADispatchReturnsWhileAHostCallIsOpen`
(`src/BlazorNative.Apple/BnHostTests/BnDispatchLaneTests.swift:47`). It is green in iOS run
38541157562.

**#440 judged against this item: adjacent, not contradicting, unchanged since 16.5.** #440 is open.
Android runs KeyStore and BiometricManager work inside `hostCallBegin`, which holds the **render
thread**, not main. Every main-thread caller above posts and returns.

**GitHub:** #346 is `CLOSED`, `COMPLETED`, at 2026-09-27T15:06:59Z.

## 5. One thread owns the renderer, and a pin proves it — **MET**

The DoD names `ReportIfNotTheRenderOwnerThread`. 16.1 renamed it `AssertOnTheRenderThread`
(`git log -S ReportIfNotTheRenderOwnerThread --oneline 3757d0e..5dde7d3 -- src` gives `5634ed13`).
On `5dde7d3`, `NativeRenderer.cs:359` compares the current thread with `RenderThreadId`, and
**throws** `InvalidOperationException` under `StrictErrors`, otherwise it warns. It is called first
in `UpdateDisplayAsync` (`:385`).

**It fails under test, re-shown today.**
- Replacing the `throw` with an empty block reds
  `RenderThreadWarningTests.ARenderFromANonOwnerThread_ThrowsUnderStrictErrors_AndNamesBothThreads`,
  1 of 3. The file was restored, and `git status` was clean.
- On the unchanged code:
  - `RenderThreadWarningTests`, `RenderThreadDispatcherTests` and `MountSyncTests`: 15/15.
  - `HostEventArmThreadTests`: 2/2.

**Every mutating entry point marshals through `OnRenderThread`** (`NativeRenderer.cs:159`):
- mount, `:218`;
- unmount, `:259`;
- `RunAfterDispatch`, `:295`;
- the root render, `:709`;
- dispose, `:740`;
- the test-only call-site count, `:1666`.

16.7 added one member, `internal static void NoteShellCallBegun()`. It sets the dispatch's
begun-a-shell-call mark and touches no render state:

```bash
git diff a301c9c 5dde7d3 -- src/BlazorNative.Renderer/NativeRenderer.cs | grep -E "^\+\s*(public|internal|private|protected).*\("
```

**#425 judged against this item: adjacent, not contradicting, unchanged since 16.5.** #425 is open.
`retire()` does not quiesce .NET, so a late frame can arrive after it. The frame is still emitted on
the render thread. The defect is *when* the frame arrives, not *where* it is emitted.

## 6. The sync-mount contract survives — **MET**

```bash
grep -n "InlineDispatcher" tests/BlazorNative.Renderer.Tests/MountSyncTests.cs
#   :84, a comment only, recording the 16.1 flip from the type-name assertion
grep -n "public void" tests/BlazorNative.Renderer.Tests/MountSyncTests.cs
#   :54, :62, :70, :82 Renderer_mounts_synchronously_on_its_render_thread
grep -n "DisposeDuringInvokeAsync_DoesNotRecurse" tests/BlazorNative.Renderer.Tests/RenderThreadDispatcherTests.cs
#   :309 DisposeDuringInvokeAsync_DoesNotRecurse_TenRuns, 13.2's recursion as a named regression
```

No assertion names a type. All of them pass in the 15/15 above.

**16.7 kept it.** 16.7's first design forced a yield after begin, and that broke sync mount: a page
whose `OnInitializedAsync` awaits an inline-answered call no longer rendered synchronously. The
shipped design classifies instead, and
`FaultNoticeTests.AComponentAwaitingAnInlineAnsweredHostCall_InOnInitializedAsync_MountsSynchronously`
pins the case. The 16.7 record's M7 and M7-final rows red it by adding `await Task.Yield()` after
the begin.

## 7. #8 is fixed — MET NARROWLY

**A fault after the handler yields reaches `onError`, on the JVM and on XCTest:**
- JVM: `FaultNoticeTest.a_fault_after_the_first_await_reaches_onError_and_the_notice_is_completed`
  (`FaultNoticeTest.kt:131`), and 16.7's
  `a_fault_after_an_inline_answered_host_call_is_a_fault_notice_not_rc_2` (`:91`). Both are green
  in CI's 192.
- XCTest: `BnFaultNoticeTests.testAFaultNoticeRoutesToOnErrorAndCompletesOk`
  (`BnFaultNoticeTests.swift:95`). It is green in the 288, and was re-redded today by A17.
- .NET, the sending half: `FaultNoticeTests.AFaultAfterTheFirstAwait_SendsAFaultNotice`
  (`FaultNoticeTests.cs:206`).

**The rc contract is written once, in three copies, and pinned identical.** 16.7 rewrote it for #455
and added `RcContractCopiesTests`. That test compares the text between `BEGIN rc-contract` and
`END rc-contract` in:
- `Exports.cs`;
- `BlazorNativeRuntimeC.h`;
- `website/docs/guides/threading.md`.

It strips each file's own comment prefix first. The copy at `Exports.cs:471-478`:

> rc reports the SYNCHRONOUS part of the handler: 0 = it did not fault, or it faulted only after it
> began a host call or a fetch (it may still be running); 2 = it faulted before beginning one. A
> fault after the handler has begun a host call or a fetch arrives as a FaultNotice host-call op,
> never as an rc, whether the shell answers inside `hostCallBegin` or later. Frames from the
> synchronous part are delivered before the export returns; frames from a continuation are
> delivered later, from the render thread.

The return-code table under it (`:480-492`) agrees. `--filter
"FullyQualifiedName~RcContractCopiesTests|FullyQualifiedName~FaultNoticeTests"` gives 28/28 on
`5dde7d3`.

**#455 is fixed.** 16.5 graded this item MET NARROWLY because the old contract's "never as an rc"
was false for a host call the shell answered inside `hostCallBegin`. #465 closed #455
(`CLOSED`, `COMPLETED`, 2026-10-03T01:17:14Z). The 16.7 facts that pin it:
- `AFaultAfterAnInlineAnsweredHostCall_IsAFaultNotice_AndRc0`;
- `…InlineAnsweredFetch…`;
- `AFaultAfterBeginningAHostCall_WithoutAwaitingIt_…`.

Their reds are in the 16.7 record §3.

**#464 judged against this item (owner decision, 2026-10-14: grade it, do not block): it narrows
the item.** #464 is open, and 16.1 introduced it. Take dispatch A, whose continuation runs inline
inside dispatch B's synchronous part, because app code completed a `TaskCompletionSource` without
`RunContinuationsAsynchronously`.
- A's later fault is captured into **B's** window.
- If A's continuation began a shell call, B gets rc 0 and a FaultNotice carrying A's message under
  B's handler id.
- B's own synchronous fault is never reported.

That contradicts the contract's "2 = it faulted before beginning one" for B. The pattern needs app
code that completes its own completion source synchronously; the runtime's own sources all use
`RunContinuationsAsynchronously`. `FaultNoticeTests`' DOES NOT COVER block discloses it and cites
#464 (`FaultNoticeTests.cs:396-402`). **MET NARROWLY**, citing #464.

**#440: adjacent, not contradicting.** #440 holds the render thread inside `hostCallBegin`. It does
not change which rc a fault gets, or whether a FaultNotice is sent.

**GitHub:** #8 is `CLOSED`, `COMPLETED`, at 2026-09-27T04:29:48Z.

## 8. #9 is re-assessed on measurement — **MET**

**GitHub:** #9, "Hardening ledger: dispatch-lane starvation", is `CLOSED`, `COMPLETED`, at
2026-09-27T21:10:26Z. It was closed as resolved by detection plus a documented contract: a Warn
once per call site above a 100 ms budget, plus `website/docs/guides/threading.md`. The residual is
#435.

**The measurement, re-run today on `5dde7d3`.** The harness is the 16.3 record's appendix. It was
written, run and deleted, and `git status` was clean after:

```bash
awk 'NR>243 && /^```csharp/{f=1;next} f&&/^```/{f=0} f' docs/plans/2026-09-27-phase-16.3-record.md > tests/BlazorNative.Runtime.Tests/StarvationMeasureHarness.cs
dotnet test tests/BlazorNative.Runtime.Tests -c Debug --filter "Category=Measure16.3" --logger "console;verbosity=detailed"
rm tests/BlazorNative.Runtime.Tests/StarvationMeasureHarness.cs
```

| sync part (ms) | dispatch sync time, median / max (ms) | second event behind it | `back` behind it |
|---:|---:|---:|---:|
| 0 | 0.1 / 0.1 | 0.1 / 0.1 | 0.0 / 0.1 |
| 10 | 10.3 / 10.4 | 10.0 / 10.4 | 9.7 / 10.0 |
| 50 | 50.7 / 51.7 | 50.1 / 50.4 | 50.1 / 50.2 |
| 200 | 202.7 / 214.8 | 202.1 / 214.1 | 199.7 / 201.2 |
| 1,000 | 1,000.8 / 1,006.4 | 999.7 / 1,004.3 | 1,000.2 / 1,001.1 |

The cost is one-for-one with no fixed overhead, as the 16.3 record concluded. The slow-handler Warn
fired at 200 ms, naming `StarvationMeasureHarness+SlowProbe.Slow`.

## 9. No ABI change — **MET**

```bash
for r in 3757d0e 5dde7d3; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -c '^\s*\[UnmanagedCallersOnly(EntryPoint'; done                 # 10, 10
for r in 3757d0e 5dde7d3; do git show $r:src/BlazorNative.Runtime/Exports.cs | grep -o '\[UnmanagedCallersOnly(EntryPoint = "[a-z_]*"' | sort | md5sum; done   # identical
git diff --stat 3757d0e 5dde7d3 -- src/BlazorNative.Runtime/BridgeProtocolNative.cs                                                          # empty
git diff 3757d0e 5dde7d3 -- src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h | grep -E "^[-+][^-+]" | grep -v -E "^[-+]\s*(//|\*|/\*)"  # empty
```

- **Exports:** the same 10 at both ends:
  - `blazornative_init`, `_shutdown`, `_version`;
  - `_register_frame_callback`, `_mount`, `_dispatch_event`;
  - `_register_bridge`, `_fetch_complete`, `_host_event`, `_host_call_complete`.
- **The bridge struct:** `BlazorNativeBridgeCallbacks`, "10 × IntPtr = 80 bytes"
  (`BridgeProtocolNative.cs:96`), is byte-for-byte unchanged across the range.
- **The C header:** comment-only changes, the rc contract's new wording.
- **The notice ops** are in `src/wire-vocabulary.json`'s `hostCallOps`, which M16 added:
  - `FaultNotice` 5;
  - `BackState` 6;
  - `BackUnhandled` 7;
  - after the five frozen ops, `Geolocation` 0 to `Camera` 4.

  `WireVocabularyCodegenTests` gives 14/14 on `5dde7d3`, and the generated copies match the manifest.
- **Release-please commits in the range, excluded by name:**
  - `55a5cb1d` release 0.17.0 (#429);
  - `73436264` 0.17.1 (#450);
  - `06916986` 0.17.2 (#463);
  - `7087678f` 0.18.0 (#466);
  - `15afa0b9` 0.19.0 (#467).

**16.7's rc change is not an ABI change.** 0.18.0 is BREAKING because the meaning of rc changed for
a fault after a begun call: it is now rc 0 plus a FaultNotice, where an inline-answered call used to
give rc 2. That is the behaviour item 7 asks for, and the owner chose it on 2026-10-01. This item's
wording is the struct, the exports and the vocabulary, verified by diffing, and all three are
unchanged in shape. The change is recorded here so it is visible, not to narrow the item.

## 10. Every new pin conforms to the pin standard on Rules 2–5 and 7 — **MET NARROWLY**

The population is every pin M16 added: the 38 distinct pins of the 16.6 record, and the 18 that
16.7 added. The pins added after M16's phases, `IosSliceMatrixDriftTests`' new fact (#462),
`PowerShellScriptEncodingTests` and `JdkVersionDriftTests` (#490), are outside M16 and not graded
here.

**The register's range, re-measured.** The 16.5 audit read `docs/pin-standard.md` lines 396–498.
On `5dde7d3` the register heading is at `:396`, its table rows run `:468` to `:515`, and Rule 7's
heading follows at `:519`:

```bash
grep -n "#### The register" docs/pin-standard.md                                   # 396
awk 'NR>396 && /^\| /{print NR; exit}' docs/pin-standard.md                          # 468
awk 'NR>=396 && NR<=600 && /^\|/{last=NR} END{print last}' docs/pin-standard.md      # 515
```

### 10a. The 16.6 population: 38 pins, each re-checked against `5dde7d3`

A 16.6 red stands only if neither the pin's test file nor the files its recorded mutations edited
changed between `a301c9c`, the 16.6 squash, and `5dde7d3`. Where one changed, a recorded mutation
was re-run on `5dde7d3`, alone, and reverted. A diff that touches only comments leaves a red standing
for a pin that runs compiled code, but not for a pin that reads source text, pins 1 to 5. The
per-pin table was generated from the 16.6 record's mutation rows and pin-to-red maps:

```bash
git diff --quiet a301c9c 5dde7d3 -- <file>          # per mutated file and test file
git diff -U0 a301c9c 5dde7d3 -- <file> | grep -E '^[-+][^-+]' | grep -v -E '^[-+]\s*(//|\*|/\*)'   # the comment-only check
```

| pins | what changed since 16.6 | re-red on `5dde7d3` | result |
|---|---|---|---|
| 1, 2 | `wire-vocabulary.json` and the generated copies: FaultNotice's doc string | **D1**, BackUnhandled id 7→8 | red: `TheHostCallOps_KeepTheirFrozenIds`, `TheEmittedHostCallOps_…`, and `EveryGeneratedFile_…` |
| 3 | `AppleShellBridge.swift`: two message strings, read as text by the scan | **D5**, `static let camera: Int32 = 4` beside `let camera` | red: `NoGeneratedSymbol_IsShadowed…` and `TheOpConstantShadowDetector_…` |
| 4 | `BnRuntime.swift`, comments, read as text by the scan | **D10** at `MainActivity.kt`; **D11** at the deep-link navigator, `BnRuntime.swift:328` | each red: `NoShippedShellSource_CallsTheBlockingHostEventDispatch` alone |
| 5 | nothing | — | the 16.6 red stands |
| 6 | `BlazorNativeRuntime.kt`, comments only | **C3′-203-after**, one `dispatchEvent` overload made `internal`. Run with Gradle 9.8.1 on Linux, Temurin 25, after local `linux-bionic` publishes | red: `theVisibilityCheck_…`, "they read [PUBLIC, INTERNAL]" |
| 7 | the same, and `BnWireVocabulary.g.kt`, comments only | **C1**, `internal` removed from `dispatchHostEventAndWait` and `BnHostEvent` | red: `dispatchHostEventAndWait_isInternal`, "must stay internal and test-only" |
| 8 | nothing it depends on | **E1** also covers it | red |
| 9, 11–16, 18–24 | `NativeRenderer.cs` and `Exports.cs`, 16.7 | **E1**, `DefaultLevel` Warn→Error | 15 of 17 red, every one of these among them |
| 10 | the same | **E11**, `OverBudget` returns true | red: `AHandlerUnderBudget_LogsNoWarn` alone |
| 17 | the same | **E9**, the call-site removal dropped | red: `TheCallSiteMap_StaysFlat_…` alone |
| 25 | the same | **E23**, on `scratch/16.8-mut-E23` through throwaway draft PR #493 (closed). `ci.yml` has no `workflow_dispatch`. The scratch commit also set the .NET step `continue-on-error`, because E23 reds one .NET fact too and the JVM step would otherwise never run. Run 38541286405, `headSha` `fb522bee`, attempt 1 | red: `three_bnbuttons_…`, "no single warning named the capturing lambda's generated method", 1 of 192 |
| 26, 33 | `BnWireVocabulary.g.swift`, comments only; their own test lines are unchanged | — | the 16.6 reds A1 and A2 stand |
| 27, 28, 29, 34, 35, 36 | `AppleShellBridge.swift`, two message strings, and `BnFaultNoticeTests.swift`, the expected text for 27 and 29 | **A17**, `hostCallBegin`'s shared return made `op <= camera ? 0 : 1`. Run 38541334324, `headSha` `46f7b1a7`, attempt 1 | all six red at their rc assertion, `("1") is not equal to ("0")`; 8 failures of 288 |
| 29 | the log-message string | **A18**, the no-runtime `BnLog.error` deleted. Run 38541337756, `1351e51f`, attempt 1 | red at `:153` "must be logged exactly once" and `:154`; 287/1 |
| 30 | `Exports.cs`, 16.7 | **A10**, `outcome.Pending!.GetAwaiter().GetResult()` after `ObservePendingDispatch`. Run 38541340864, `2fee2cec`, attempt 1 | red at `:86` "did NOT return within 2 s … This is #345" and `:95`; 287/1 |
| 31 | `BnRuntime.swift`, comments only | — | the 16.6 reds stand |
| 32, 37, 38 | nothing | — | the 16.6 reds stand |

Every local mutation was run alone on a detached checkout of `5dde7d3`. `git status` was clean before
each next one. Each iOS mutation ran on its own scratch ref with `ios.yml` dispatched. Every scratch
ref and the draft PR were deleted afterwards. The verdicts per rule are unchanged from the 16.6
record §8, including the four pins that cell names *partial*: 2, 3, 25 and 38.

### 10b. The 18 pins 16.7 added

- 16 facts and 1 theory in `FaultNoticeTests.cs`.
- `RcContractCopiesTests.TheRcContract_IsTheSameInAllThreeCopies`, a tree reader.
- The JVM `FaultNoticeTest.a_fault_after_an_inline_answered_host_call_is_a_fault_notice_not_rc_2`.

```bash
git diff 2fea914 dc5b737 -- tests src/BlazorNative.Jni/src/test src/BlazorNative.Apple/BnHostTests | grep -E "^\+\s*(public void|fun |func test)"
```

**The finding.** The 16.7 record holds each pin's reds, but **no Rules 2–5 assessment**, for any of
the 18. The register was not updated either: its `FaultNoticeTests` row still describes 16.1's 8
facts. 16.5 graded unassessed pins as carried, not failing. A closing audit cannot carry them again,
so this audit assessed them, as 16.5's audit did for pins no record covered.

The cells, one per pin and rule with file:line, are in the appendix. The register's `FaultNoticeTests`
row now points to them.

**Rule 7: two pins had no red on their own assertions,** only on their anchors:
`ACancellationAfterABegunHostCall_IsRc0_AndSendsNothing` and
`ACancellationWithNoShellCallBegun_IsAlsoRc0_AndSendsNothing`. Their claims are rc 0 and no notice.
The record's one mutation of the cancelled-Task arm left both green, because Blazor completes a
cancelled handler's Task before the arm is reached. No production line decides "cancelled means
rc 0", so the audit mutated the probe instead. Each mutation ran on `5dde7d3`, alone, and was
reverted:

| id | mutation | command | red |
|---|---|---|---|
| X1 | `cancel-only` throws `InvalidOperationException("X1")` instead of `OperationCanceledException` (`FaultNoticeTests.cs:941`) | `--filter "FullyQualifiedName~FaultNoticeTests.ACancellation"` | `ACancellationWithNoShellCallBegun_…` at its rc assertion: "rc was 2: a cancelled handler is not a fault, so it is rc 0 and sends nothing." 1 of 2 |
| X2 | `begin-then-cancel` the same (`:935`) | the same, 3 runs | `ACancellationAfterABegunHostCall_…` at `Assert.Empty(Notices())`, 3 of 3, with a bare `Assert.Empty() Failure` |

Not entered, and named:
- `begin-then-cancel`'s rc assertion. A fault after its begun call is rc 0 too, so no probe-side
  change can make it fail.
- `cancel-only`'s `Assert.Empty`, which X1 stops before.

**Partials, named here because no record names them:**
- **Rule 7: anchors that no row enters.**
  - the fetch fact's `FetchesCompletedInsideBegin`;
  - the mount guard's counter anchor;
  - both `BackHoldProbe` hold anchors (`:584`, `:590`);
  - the sibling fact's three order anchors;
  - the notice fact's `notice` anchor;
  - the thread fact's rc and thread-recorded anchors;
  - the JVM test's `answeredInsideBegin` and `completeRc`.

  Each fact's main assertion has a recorded red. The anchors are what prove the scenario ran, and
  nothing shows they can fail.
- **Rule 5.**
  - `FaultNoticeTests.cs:407-410` states as fact that a call begun by a child component during the
    re-render marks the dispatch, so a later render fault is rc 0. Only the order is pinned. The
    rc 0 half is a claim with no pin and no "rests on reading" qualifier.
  - The JVM class KDoc (`FaultNoticeTest.kt:12-29`) describes only the 16.1 held-call flow, and the
    16.7 test has no limits of its own.
  - The `FaultNoticeTests` file header (`:13-48`) still describes 16.1 only. The section blocks
    carry the 16.7 limits.
- **Minor.**
  - `FaultNoticeTests.cs:806`, `DoesNotContain("after-notice")`, runs with no wait, so only that
    fact's rc assertion is load-bearing.
  - `RcContractCopiesTests.cs:45` reds a moved copy with a bare `FileNotFoundException`.

All of these are filed as #494.

### 10c. The grade

Every one of the 56 pins has a red on `5dde7d3`, either standing, re-run today, or recorded in the
16.7 record for code that has not changed since. Every partial is named:
- the 16.6 record's four;
- this audit's 10b list.

By the 16.5 precedent, a named partial makes the item **MET NARROWLY**.

**This audit's own call.** The plan's rule says a partial named by its record is MET NARROWLY and an
unnamed one is NOT MET. The 10b partials were named by no record before today. This audit names them
in the report, and in the register row where the standard keeps cells, and grades them MET NARROWLY.
The rule's purpose is disclosure, and the cells now exist where the standard says they live. Read
strictly, the item is NOT MET and M16 needs another gap phase. That reading is the owner's to take.

### Appendix to 10b: Rules 2–5 and 7, the 18 16.7 pins

`FNT` is `tests/BlazorNative.Runtime.Tests/FaultNoticeTests.cs`, `NSB` is `src/BlazorNative.Runtime/NativeShellBridge.cs`, `NR` is `src/BlazorNative.Renderer/NativeRenderer.cs` and `Rec` is the 16.7 record. Rule 2 means an anchor, per the register's 16.1 paragraph. Shared Rule 4 mechanism: `DispatchProbe` finds each probe handler by label through `HandlerFor`, which throws for a missing label (FNT:119), and ops and counters are compile-time symbols. The two cancellation rows' Rule 7 cells predate X1 and X2 above.

| pin | Rule 2 | Rule 3 | Rule 4 | Rule 5 | Rule 7 rows |
|---|---|---|---|---|---|
| `AFaultAfterAnInlineAnsweredHostCall_IsAFaultNotice_AndRc0` (FNT:420) | **Conforms.** The anchor `HostCallsCompletedInsideBegin == 1` (FNT:425) proves that the fake answered inside begin. The notice is asserted with `Single`, and so is its message `after-host`. | **Conforms.** The controls are `AFaultWithNoShellCallBegun_IsStillRc2_Control` and the refused-begin facts in the other direction. M8a/M8b show the fact is vacuous without the anchor, and M8c shows the anchor is load-bearing. | **Conforms.** The shared mechanisms apply. The session-survives half finds `next-ok` by label and asserts the `ok:1` text (FNT:441-446). | **Conforms.** The 16.7 block, FNT:390-417. | §3 "The red before the fix"; M1, M3, M3a, M8c (at the anchor; M8a/M8b are the vacuity contrast); §6 M5-default, M20, M20-frame. |
| `AFaultNoticeAfterABegunCall_IsNotBegunOnTheDispatchingThread` (FNT:452) | **Conforms.** The `rc == 0` anchor (FNT:459) and the "thread recorded" anchor `dispatchThread > 0 && noticeThread > 0` (FNT:465) prove that a classified notice was really sent and both threads were really seen. | **Conforms, with its contrast in rows.** It asserts two inequalities. There is no in-run control that a same-thread begin reads as equal. M19 (dispatch thread) and M21 (render thread) show both directions discriminate. | **Conforms.** `HostCallBeginThread` returns -1 for an unknown id (`NativeShellBridgeTests.cs:102-104`), and the anchor then reds. | **Conforms.** It is covered by the 16.7 block. The production comment at `Exports.cs:568-574` names this pin. | §6 M19; §7 M21. Neither anchor is entered by any row. |
| `AFaultAfterAnInlineAnsweredFetch_IsAFaultNotice_AndRc0` (FNT:482) | **Conforms.** The `FetchesCompletedInsideBegin == 1` anchor (FNT:487). | **Conforms.** It shares the class controls. `AFetchTheShellRefusesToBegin_…` is the fetch-side control in the other direction. | **Conforms.** The shared mechanisms apply. | **Conforms.** The 16.7 block. | §3 "The fetch, Task 3"; M2, M3, M3a. **Partial:** no row enters the fetch anchor, because M8 covers the host-call path only. |
| `AComponentAwaitingAnInlineAnsweredHostCall_InOnInitializedAsync_MountsSynchronously` (FNT:516) | **Conforms.** The counter anchor `HostCallsCompletedInsideBegin == 1` (FNT:526). `frames.Count > 0` (FNT:533). | **Conforms.** It is positive in itself: the first frame must carry `init:` (FNT:536). M7 shows the guard discriminates. | **Conforms.** `InitAwaitMountProbe` is a nested type, and `Mount<T>` throws on an incomplete first render. | **Conforms.** FNT:508-513 names the fetch-begin yield it does not see. | Rec §4 M7 early, M7; §6 M7-final and M7-before. M8a also reds it, at `Mount<T>`, before the anchor. **Partial:** the counter anchor itself is entered by no row. |
| `TheBackHoldProbe_HoldsTheRenderThread_AndTheShellsAnswerReleasesIt` (FNT:552) | **Conforms.** Two anchors: the camera call began (FNT:584), and the dispatch did NOT return within 200 ms while held (FNT:590). | **Conforms.** It is positive in itself: the dispatch must return, with rc 0, and render `released:Cancelled`. The JVM/device back tests drive the same probe. | **Conforms.** It references `BlazorNative.SampleApp.BackHoldProbe` and `ReleasedPrefix` (`samples/…/BackHoldProbe.cs:34,40`) as compile-time symbols. A handler moved off the first click reds at the camera anchor. | **Conforms.** FNT:508-513 says it covers the camera call only. | §4 M7 early, M7, F1-before/F1-after, R1-guard (cleanup); §6 M7-final, M7-before. **Partial:** neither hold anchor (FNT:584, :590) is entered by any 16.7 row. |
| `AHostCallTheShellRefusesToBegin_FaultsTheHandlerWithRc2_AndSendsNoNotice` (FNT:663) | **Conforms.** The begin-attempted anchor reads the host-call log, which keeps refused begins (FNT:669). | **Conforms.** It is the control for the "mark after accept" order. The positive half is the inline facts. | **Conforms.** The shared mechanisms apply. `HostCallBeginReturnCode` is a fake member. | **Conforms.** The 16.7 block. `NSB:484-491` names it. | §3 "A refused begin"; M6, F3-before/F3-after (rc and anchor), M16. |
| `AFetchTheShellRefusesToBegin_FaultsTheHandlerWithRc2_AndSendsNoNotice` (FNT:680) | **Conforms.** The anchor `LastFetchUrl == "https://refused.test/"` (FNT:687) proves that the begin was attempted. | **Conforms.** It is the fetch-side control. | **Conforms.** The shared mechanisms apply. | **Conforms.** The 16.7 block. `NSB:330-337`. | §4 M15-before (green, the gap), M15 (rc red and anchor red). |
| `AFaultAfterBeginningAHostCall_WithoutAwaitingIt_IsAFaultNotice_AndRc0` (FNT:699) | **Conforms, by a positive result.** rc 0 plus a notice whose message is `after-begin` (FNT:706) can only come from the classification. No begin anchor exists. | **Conforms.** The class's rc-2 control applies. | **Conforms.** The shared mechanisms apply. | **Conforms.** The 16.7 block. | §3 red before; M1, M3, M3a, M8b; §6 M5-default, M7-final. |
| `ACancellationAfterABegunHostCall_IsRc0_AndSendsNothing` (FNT:713) | **Conforms.** The `ReachedThrow` anchor (FNT:718) and the begin anchor from the log (FNT:720). | **Partial, named.** It asserts rc 0 and absence. The notice detector is controlled by the positive facts. No control or row shows that the harness would see a cancellation that is NOT rc 0. | **Conforms.** The shared mechanisms apply. | **Conforms.** FNT:401-404 discloses that rc 0 rests on a measurement and that the reason is not established. | §3 "A cancelled handler", fix round 2 (both anchors); F3-after (begin anchor). **No recorded red for its rc or no-notice assertions.** The only mutation of the arm left it green. |
| `ACancellationWithNoShellCallBegun_IsAlsoRc0_AndSendsNothing` (FNT:732) | **Conforms.** The `ReachedThrow` anchor (FNT:737). | **Partial, named.** It has the same shape as the row above. No contrast shows a non-zero rc would be seen. | **Conforms.** The shared mechanisms apply. | **Conforms.** FNT:401-404. | §3 "A cancelled handler", mutation 1 (anchor only). **No recorded red for its rc or no-notice assertions.** The arm-throw mutation stayed green. |
| `AFaultWithNoShellCallBegun_IsStillRc2_Control` (FNT:747) | **Conforms, by a positive result.** rc 2 needs a captured fault (`NR:1890-1895`). No `ReachedThrow` anchor exists. | **Conforms.** It is the class's control. | **Conforms.** The shared mechanisms apply. | **Conforms.** The 16.7 block. | §3, green as the expected control; M6 (red). |
| `AFaultBeforeASiblingBeginsAShellCall_IsStillRc2_AndSendsNoNotice` (FNT:764) | **Conforms.** Three anchors: the child threw, the sibling began, and they happened in that order (FNT:773-779). | **Conforms.** `AFaultAfterBeginningAHostCall_WithoutAwaitingIt_…` is the contrast, a fault after the handler's own begin, which still classifies. `NR:1576-1592` cites this fact. | **Conforms.** The child types are nested, and the order stamps are statics that `DispatchProbe` resets. | **Partial, named.** FNT:405-409 says the ORDER is pinned. It also states as fact, without a "rests on reading" qualifier, that a child's begin followed by a later render fault is rc 0. No pin covers that half. | §6 "The ordered mark" red before; M18. **Partial:** none of the three anchors is entered by any row. |
| `ANoticeDotNetSends_DoesNotMarkTheDispatch_SoAFaultAfterItIsRc2` (FNT:792) | **Conforms.** The anchor is that a notice with message `notice` was begun in the synchronous part (FNT:802). | **Conforms.** The contrast is the inline facts, where a begun call marks. M5/M5-param show it discriminates. | **Conforms.** The shared mechanisms apply. `SendFaultNotice` goes through `SendNotice`, which passes `markDispatch: false` (`NSB:653-657`). | **Conforms.** The 16.7 block. | §3 (green control); M5, M5-rerun, M6; §6 M5-param. **Partial:** no row removes the probe's `SendFaultNotice` to red the anchor. The `DoesNotContain("after-notice")` at FNT:806 is immediate, with no wait or `pending` check, so it is not independently load-bearing. |
| `ABackNoticeDotNetSends_DoesNotMarkTheDispatch_SoAFaultAfterItIsRc2` (theory, 2 cases, FNT:814) | **Conforms.** The op-6 or op-7 begin anchor (FNT:823). | **Conforms.** Per-case contrast: M5-param reds both. | **Conforms.** Ops are generated symbols. Each case is keyed to its own notice. | **Conforms.** The comment at FNT:816-820 explains the per-case split. | §4 F2-before (gap), F2-after (each case, plus the op-6 anchor), M5-rerun, M6, R1-anchor-bu (op-7 anchor); §6 M5-param. |
| `AShellCallBegunOnAnotherThread_DoesNotMarkTheDispatch_SoAFaultAfterItIsRc2` (FNT:838) | **Conforms.** Three anchors: begun, answered inside begin, on another thread (FNT:846-852). | **Conforms.** It is the contrast for the thread-bound mark. M4 shows the flowing-scope alternative reds. | **Conforms.** The shared mechanisms apply. | **Conforms.** FNT:391-394, and :395-400 discloses the unpinned inline-continuation case, #464. | §4 M4 (gap), M4-after, M4-anchor-a, M4-anchor-b, M6 (red at the anchor, which was the F3 defect), F3-after, M8a (red at the anchor). |
| `DispatchUiEventAsync_FaultsWithAFaultAfterABegunHostCall` (FNT:866) | **Conforms.** The anchor is that geolocation was begun (FNT:883). | **Conforms.** It is positive in itself: `IsFaulted`, with the exact type and the message `after-begin`. | **Conforms.** It calls `renderer.DispatchUiEventAsync` directly. The rethrow is at `NR:1953`. | **Conforms.** The comment at FNT:868-870 scopes it to the direct path. | §3 "The direct path" red before; M17. M1 and M3 do not reach it, which is correct. |
| `RcContractCopiesTests.TheRcContract_IsTheSameInAllThreeCopies` (`RcContractCopiesTests.cs:63`) | **Conforms.** The floor `reference.Length > 200` (:75) is measured (509) and its headroom stated. Exactly one BEGIN and one END per copy (:47-49). | **Conforms.** It is a positive equality match, so the detector runs every run. The negative (reflow stays green) is in row M13, not in-run. | **Conforms.** The marker assertion names the file and "compares nothing". A moved copy reds through `File.ReadAllLines`'s bare `FileNotFoundException` (:45), which has no re-point message. The tree is reached through `BnRepo.Root()`. | **Conforms.** The header at :17-21 names three limits, including that the same change in all three copies stays green. | §3 "The copies pin"; M9, M10, M11, M12a, M12b, M12c, M13 (negative), M14a (vacuity contrast), M14b. |
| JVM `FaultNoticeTest.a_fault_after_an_inline_answered_host_call_is_a_fault_notice_not_rc_2` (`FaultNoticeTest.kt:91`) | **Conforms.** The anchors `answeredInsideBegin == 1` and `completeRc == 0` (:109-110). | **Conforms, with its contrast in a row.** It is positive: rc 0, plus `onError` receiving the prefixed `FormatException`. There is no JVM-side rc-2 control in the suite. M1-JVM shows that it discriminates. | **Conforms.** `Take Photo` is found through `checkNotNull` (:182-197). The op is the generated `HostCallOp.CAMERA`. A changed onError prefix starves `faultSeen` and reds with the errors listed. | **Partial, named.** The class KDoc (:12-29) describes only the 16.1 held-call flow. It does not mention the inline case. The test has no doc comment and no limits of its own. The inherited "iOS arm / exact args" limits still apply. | §3 "Through the NativeAOT dll, Task 6"; §4 M1-JVM. **Partial:** neither anchor is entered by any row. |


## Carried forward

Open, judged and carried. Each item states whether it narrows a DoD item, and which.

| issue | title | judgement |
|---|---|---|
| #424 | JVM `testDebugUnitTest` does not declare the native DLL as an input | outside the DoD: a build-graph defect. It is the reason the JVM evidence here is CI's fresh run |
| #425 | `retire()` does not quiesce .NET | item 5: adjacent, not contradicting |
| #426 | FaultNotice message reaches Android logcat unredacted in Release | outside the DoD: a redaction defect in a channel item 7 does not name |
| #427 | Analyzer: flag `ConfigureAwait(false)` followed by a render | outside the DoD: a proposed analyzer |
| #435 | Slow-handler warning names the Bn wrapper, not the app handler, for forwarding controls | item 8: #9's documented residual |
| #440 | Android KeyStore and BiometricManager work runs inside `hostCallBegin`, on the render thread | items 4 and 7: adjacent, not contradicting |
| #444 | iOS `BnNotificationsTests` show test intermittently never completes Granted | item 2: did not fire on `5dde7d3` |
| #453 | iOS cold `LAContext` can exceed the biometrics boot test's 30 s wait | item 2: did not fire on `5dde7d3` |
| #454 | Local `dotnet test` can hang in `ReferenceFixtureBase` | item 2: did not reproduce in 5 Linux runs; only ever seen on Windows |
| #458 | androidTest `pollUntil` and `BnGeolocationTests`: bare failure messages | outside M16's population: pins outside the 38, consolidation |
| #464 | A fault from a continuation run inline inside another dispatch is attributed to that dispatch | **narrows item 7** |
| #494 | 16.7 pins: unentered anchors, an unpinned rc claim, stale headers | **narrows item 10** |

Adjacent and filed during this audit, not M16 debt:
- #491: the JVM dev loop is Windows-only.
- #495: `gradlew` is not executable in git, including the template's copy; a `ci.yml` step names a
  stale SDK band.

## What the gap phases taught

- **A pass is a snapshot, so the gap phases needed re-checking.** 16.6 left 38 pins red on its own
  tree. Two weeks later, 16.7 had changed a file that 33 of them test or mutate. Most of those changes
  were comment rewordings, but two message strings and the dispatch classification were real. A
  file-level diff per pin, plus the comment-only rule for compiled pins, found the 13 mutations
  worth re-running. Every one went red as recorded.
- **A record of reds is not an assessment.** 16.7 recorded thorough mutations and wrote no Rules 2–5
  cell for any of its 18 pins, and the register kept describing 16.1. The standard's own warning
  came true: the register grows only when someone remembers to add to it. A phase that adds pins
  should end with their register cells, the way 16.6 did.
- **Anchors are the last thing anyone mutates.** In both 16.6 and 16.7, the facts' main assertions
  were redded, and the anchors that prove the scenario ran were often left unentered. A pin standard
  that requires anchors (Rule 2) should expect their reds (Rule 7) too.
- **A Linux host reaches far more than expected.** The whole .NET suite, the bionic publish, and the
  reflection-only JVM pins all run on Linux. Only the JNA-loaded JVM tests, the Windows NativeAOT
  link, and anything run through a PR-only workflow needed CI. A `ci.yml` with no
  `workflow_dispatch` forced a throwaway PR for one mutation.

## Verdict

**PASS WITH FINDINGS.** Eight items are MET, measured today on `5dde7d3`. Two are MET NARROWLY,
each with its reason:
- **Item 7**, by #464, a misattribution 16.1 introduced. It needs app code that completes its own
  `TaskCompletionSource` synchronously. The owner chose on 2026-10-14 to grade it and not block on
  it.
- **Item 10**, by the named partials of 56 pins, each of which has a red on current code. The gaps
  this audit found and named in 16.7's pins are filed as #494.

The 16.5 audit's NOT MET, the pin standard, is closed by 16.6's reds, which hold on today's code,
plus this audit's re-reds and its assessment of 16.7's pins. Its MET NARROWLY on #455 is closed by
16.7.

**One call in this verdict is the auditor's, and the owner may overrule it.** Item 10 is graded MET
NARROWLY although 16.7's partials were named by this audit, not by their record (see 10c). Under the
strict reading, item 10 is NOT MET, and #494 would become a gap phase before M16 closes.

Next: `complete-milestone`, with no tag. `CONVENTIONS.md` records
`Milestone completion tags a release: no`, and release-please owns the version.
