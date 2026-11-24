# Perf measurement scripts

`tools/BlazorNative.PerfReport` turns device logs holding `BNPERF` marks into `samples.csv` and
`summary.md`. It compares two sides, HEAD and the `v0.15.0` baseline. Phase 19.3's perf report is
built on its output.

## CLI

```
dotnet run --project tools/BlazorNative.PerfReport -- --input <dir> --out <dir> --min-runs 20
```

`--min-runs` defaults to 20. Once the arguments parse, an invocation first deletes `samples.csv` and
`summary.md` from `--out`, so an earlier invocation's files never survive it. An argument error (an
unknown argument, one without its value, a bad `--min-runs`, a missing `--input` or `--out`) exits 2
before that and leaves `--out` as it was. Exit codes:

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | The data fails the run: a clock order violation, more than 5% of samples dropped, fewer samples than `--min-runs`, a malformed `BNPERF` line or a duplicate ruler, or a side with no logs. The files are still written, for debugging: a malformed file adds no samples, and every other file is still read. |
| 2 | Wrong invocation or input layout: a missing argument, a missing `meta.json`, a `.log` file not named `<kind>-<run>.log` or whose run number does not fit a 32-bit integer, a side directory name or a `meta.json` value with a comma, pipe, double quote or newline, or a `meta.json` whose root is not an object. No files are written. |

## Input layout

```
<input>/
  <side>/               one directory per side; its name becomes the `side` column
    meta.json           { "sdk": "10.0.100", "commit": "abc1234" }
    boot-<run>.log      one cold boot per file
    click-<run>.log     many serial clicks per file
```

The side directory's name, `sdk` and `commit` are non-empty strings without commas, pipes, double quotes or newlines:
both outputs write them unquoted. `<run>` is a number;
run 0 is warm-up. Two files with the same kind and run number (`boot-1.log` and `boot-01.log`) are an error, exit 2. A kind (`boot` or `click`) present for one side must be present for every side.

## Mark lines

A mark is `BNPERF <point> seq=<n> t=<ns>`, plus `id=<handlerId>` on `dispatch.enter` and
`dispatch.handler`, and on `dispatch.post` too: the id is required on all three dispatch points, and a
post without one is a malformed mark. The parser finds `BNPERF ` anywhere in a
line, so a logcat or os_log prefix is fine, and ignores every line without it. A line with the marker
that is not well formed fails the run, naming file and line. Points: `boot.start`, `boot.content`,
`dispatch.post`, `dispatch.enter`, `dispatch.handler`, `frame.recv`, `frame.applied`.

Boot logs may also hold Android's `Fully drawn <activity>: +<n>ms` line (`+1s234ms` and
`+1m2s3ms` parse too) and `TotalTime: <n>`.

## Pairing

Log position across points means nothing. On a device the .NET marks (`dispatch.enter`,
`dispatch.handler`) reach the log late: they go through the `BnPerfMarks` writer thread, then
`BnLog`'s queue, then stderr and the stderr pump. The shell marks are logged directly. So a .NET line
can land after the frame lines of its own click, or after the next click's post. Within ONE point, log
order is emit order, because each point has one writer. The parser therefore splits a click log's marks
by point, keeps each point's log order, and pairs by order and id:

- The k-th `dispatch.post` has an id X. It takes the next unused `dispatch.enter` with id X, and that
  enter takes the next unused `dispatch.handler` with id X, in per-point order filtered by X. If either
  is missing, the sample is dropped.
- When two of those points have unequal counts for an id, a mark is missing, and order alone would hand
  every later click its neighbour's marks. Then at most the difference in counts is skipped: a post or
  enter whose next partner is stamped at or after the next mark of its own point (of any id) has lost
  its partner, and a partner stamped before the current post or enter belongs to one that was lost.
  With equal counts nothing is skipped.
- The click's frame is the one `frame.recv` with `handler.t <= recv.t < next post's t`. The last click
  has no upper bound. Zero or several such marks drop the sample. Its `frame.applied` is the first applied
  mark with `recv.t <= applied.t`, below both the next recv's t and the next post's t (the last click
  has neither bound). The next post is a bound because the clicks are serial: both measurement tests
  wait until the click's frame shows the new counter before the next click, so its `frame.applied` is
  stamped before the next `dispatch.post`. A frame with no such mark, as when its batch threw, drops
  that one sample. An applied mark is never taken from another recv's frame, nor from the next click's frame when its recv line
  was lost, and unequal numbers of `frame.recv` and `frame.applied` marks are not an error.
- In a boot log the first `boot.start` and the first `boot.content` are paired.

The triple is chosen by order and id, not by timestamp, so the clock check below still tests the
shared clock. The resync looks at timestamps only for as many occurrences as marks are missing, so a
real clock offset still fails every other click.

## Checks

- Every sample, warm-up included, must satisfy `dispatch.post <= dispatch.enter <= dispatch.handler`.
  One violation fails the run: it means the shell and .NET are not
  reading the same clock.
- Per side and metric (run 0 not counted), more than 5% dropped fails the run. Exactly 5% passes.
- `--min-runs` counts samples per side per metric, not run files. A side with fewer than `--min-runs`
  samples of `boot`, of `dispatch` (and so of every click metric), or of a ruler fails the run. Drops
  count against it.
- At most one `Fully drawn` and one `TotalTime` per boot log; more is an error naming the file. Once any
  boot log of a side with run above 0 has a ruler (warm-up alone does not count), a boot log with run above 0 and no such line is a dropped sample for
  that metric. Both sides must report the same rulers; neither side reporting them, as on iOS, is fine.
- `boot.content` must not precede `boot.start`.

## samples.csv

Columns, exactly: `side,sdk,commit,metric,part,run,value_ms`. Invariant culture, `.` decimal point,
no quoting. Run 0 rows are included, with `run=0`: it is warm-up, and every consumer must exclude it. `value_ms` has up to six decimals.

| metric | definition | parts |
|---|---|---|
| `boot` | `boot.content - boot.start` | `total` |
| `boot_fully_drawn` | Android `Fully drawn` line, in ms | `total` |
| `boot_total_time` | Android `TotalTime`, in ms | `total` |
| `dispatch` | `dispatch.handler - dispatch.post` | `total`, plus `lane` (`enter - post`) and `dotnet` (`handler - enter`) |
| `frame_apply` | `frame.applied - frame.recv` for the click's frame | `total` |
| `click_to_content` | `frame.applied - dispatch.post` | `total` |

`part` is `total` for every row without a split, so the column is never empty and a group-by on
`(side, metric, part)` needs no special case. `dispatch` has three part values: `total` is the sum of
`lane` and `dotnet`.

## summary.md

Lists each side's `sdk` and `commit`, then n, median, Q1, Q3 and IQR per side, metric and part, in
milliseconds with three decimals. Run 0 is excluded. Quartiles use linear interpolation, numpy's
default: for `[1,2,3,4,100]` the median is 3, Q1 2, Q3 4 and the IQR 2. A `Per process` section
follows: the number of processes per side and kind (each `<kind>-<run>.log` is one process, run 0
excluded, counted whether or not any of its samples paired), and for every click metric one median per run file, so a slow mode confined to some
processes is visible instead of averaged away. A last section lists the dropped samples per side and why.

## Collectors

Both collectors dot-source `scripts/perf/perf-common.ps1` from their own directory. It holds the pure
functions they share: the mark counts, `Test-Q1Verbose`, `Test-ClickLogComplete`, `Test-SideId`,
`Test-MetaValue`, the run list and path rules, `Get-SideWriteError`, `Invoke-BoundedProcess`, through which
every adb, xcrun and xcodebuild call runs with one deadline for its exit and both output pipes, and
`Select-RunningAppJobs`, which `interleave.ps1` uses.
`tests/BlazorNative.Runtime.Tests/PerfCollectorTests.cs` feeds each detector a passing input and planted
violations, through `BoundedScript`, in the required .NET lane.

`scripts/perf/android-collect.ps1` writes the layout above from an Android device or emulator, one kind per
call: `boot`, `click` or `q1`. It needs pwsh 7 (it uses the .NET Core APIs `ProcessStartInfo.ArgumentList` and `Process.Kill` with a process tree), adb on PATH, and a device that
accepts `logcat -G 16M`. `boot` and `click` require `-Sdk` and `-Commit` and write `meta.json` from them.
Each must be non-empty, with no commas, pipes, double quotes or newlines. Runs are numbered 0 to `-Runs`, run 0
being warm-up. A rerun after a failed collection must delete `<Out>/<Side>` first, or pass `-Force`. `-Force` deletes the side directory's existing `<kind>-<n>.log` files for the kind being collected, and nothing else, before writing, so no run above the new `-Runs` survives from an earlier collection. `-Runs` must be at least 1. `-Run <k>` collects exactly run `k` and ignores `-Runs`, so `perf.yml` can alternate the two sides one run at a time; with `-Force` it replaces only that run's file. Under `-Force` the logs a call replaces are deleted before `meta.json` is rewritten, so a collection that fails cannot leave another build's log under the new label. Before anything is written, every requested run's log must be absent unless `-Force` is passed, and when any `<kind>-<n>.log` of either kind will remain in the side directory, its `meta.json` must already name the same `-Sdk` and `-Commit`, so a call cannot relabel samples it did not collect. Failures go to stderr with exit 1. Every
adb call has a deadline, and any timeout or failed assertion exits 1. `q1` evidence goes to
`<Out>/q1-<side>-default.txt`, `-default-click.txt` and `-verbose.txt`, outside the side directory. Android's
`q1` is: a default launch, asserting no `BNPERF` line and no V, D or I line from a `BlazorNative*` tag, with
a control that the launch's `Fully drawn` line, for the selected package and activity, parsed as a logcat line of its own; then `PerfMeasurementTest`
for `-Q1Clicks` clicks (default 3) with `perfLevel=default`, through the click kind's `am instrument` and
logcat, asserting no `BNPERF` line and the same tag check, with the androidTest sources' own tags
(`BlazorNative.Test`, `BlazorNativeTest`) excluded and a control that the test's `Displayed` line parsed;
then a Verbose launch asserting the four boot marks. The app and its test package are force-stopped
afterwards, also when a step failed.

`scripts/perf/ios-collect.ps1` is the simulator counterpart, with the same layout, the same `-Sdk` and
`-Commit` rules, run numbering, refusal to overwrite (delete `<Out>/<Side>` or pass `-Force`), and
exit 1 on every failure. It needs pwsh 7, macOS with Xcode, and a booted simulator, and every `xcrun`
and `xcodebuild` call has a deadline. It enables debug persistence for the `io.blazornative` subsystem
and reads `log show --debug` over a window that starts before each run's launch; q1's default-level reads
add `--info`. Every launch passes `--terminate-running-process`.

- `boot` terminates the app and cold-launches it with `simctl launch`, passing
  `SIMCTL_CHILD_BN_LAUNCH_COMPONENT=PerfProbe` and `SIMCTL_CHILD_BN_LOG_LEVEL=Verbose`. The shell reads
  `BN_LAUNCH_COMPONENT` like `BN_LOG_LEVEL` (see `BnDeepLink.resolveLaunchComponent`), so no URL and no
  `Open in` sheet is involved. `blazornative://perf` still opens PerfProbe for manual runs.
- `click` runs `xcodebuild test-without-building -only-testing:BnHostTests/BnPerfMeasurementTests` with
  `TEST_RUNNER_PERF_CLICKS`, `TEST_RUNNER_PERF_LEVEL=Verbose` and `TEST_RUNNER_BN_LOG_LEVEL=Verbose`, needs `-DerivedData` from a
  build-for-testing, and waits for every click's `dispatch.post`, `dispatch.enter` and `dispatch.handler` before dumping.
  The PR lane skips that class. Its log is not the subsystem alone. .NET's Console is NSLog on iOS, and
  under XCTest no stderr pump runs on either side: both shells install it in
  `HostViewController.viewDidLoad` after the XCTest guard. So the .NET marks reach the unified log only as
  the app's own NSLog records, outside the subsystem. The click predicate takes the subsystem's `Perf`
  category for the shell marks and the app's non-subsystem records carrying `BNPERF ` for the .NET marks.
  It excludes the subsystem's `native` category, where a running pump relays NSLog's stderr copy, as it does
  in the boot and q1 launches. Each dispatch point must appear exactly once per click, or the run fails; a
  failed wait writes every `BNPERF` record of the window, with its subsystem and category, to
  `<Out>/click-diag-<side>-<run>.txt`.
- `q1` launches PerfProbe the same way and reads the whole launched process, not only the subsystem. It
  removes `SIMCTL_CHILD_BN_LOG_LEVEL` and `TEST_RUNNER_BN_LOG_LEVEL` from the child's environment at the default
  level, so a level set in the caller's environment cannot reach the default-level runs. It
  asserts no `BNPERF` line, no debug or info line from the subsystem, and no .NET line below Warn at the
  default level, with a control that the launched process logged. The .NET lines carry no level in the
  unified log: NSLog records them as the app's Default records and the stderr pump relays them as Default
  records of category `native`, so a record whose text holds `[BN|I|`, `[BN|D|` or `[BN|V|` is the
  violation. It then runs the measurement class for `-Q1Clicks` clicks (default 3) at the default level,
  reads the window with the click kind's predicate and asserts no `BNPERF` line, and reads the test host's
  whole window, `<Out>/q1-<side>-default-click-host.txt`, and asserts no `BNPERF` line and no .NET line below Warn there, with a
  control that it holds the host's records. Last, it asserts all four boot marks at Verbose. That the
  dispatch marks are present at Verbose is the click kind's exactly-once check. The q1 kind needs
  `-DerivedData` too.

`BN_LOG_LEVEL` takes the name `Verbose`, not a digit: `BnLogLevel.fromName` reads names only.

### Side and app id

Each side is installed under its own app id, so both stay installed on one device and runs alternate
without a reinstall. The backport branch adds the `.v015` suffix:

| `-Side` | Android `-Package` | iOS `-Bundle` |
|---|---|---|
| `head` | `io.blazornative.shell` (the default) | `io.blazornative.bnhost` (the default) |
| `v015` | `io.blazornative.shell.v015` | `io.blazornative.bnhost.v015` |

Both collectors refuse a mismatch with exit 1 before they create any directory: `-Side v015` with an id
that does not end in `.v015`, or `-Side head` with one that does (`Test-SideId`). The defaults are HEAD's
ids, so a `v015` run must pass its id explicitly; without the check it would silently measure HEAD's app.

### Interleaving and load

`scripts/perf/interleave.ps1` drives one collector for both sides, alternating which side goes first per
run or block, and stops the other side's app before every call. Once both sides are read, it stops both
apps at the end, also when a collection failed; a failure stopping one does not skip the other, and the
collection's error is the one reported. Click blocks are 5 clicks by default
(`-Clicks`), and there are `ceil(Runs / Clicks) + 1` blocks after the warm-up block 0, so `runs=20` gives
25 samples per side from 5 measured processes. Before every collector call it appends one line to
`<Out>/load.txt`: the UTC time, side, kind, run, and the host's load average, `/proc/loadavg` on Linux and
`sysctl -n vm.loadavg` on macOS. The parser ignores files at the root of `<Out>`.
