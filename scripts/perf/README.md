# Perf measurement scripts

`tools/BlazorNative.PerfReport` turns device logs holding `BNPERF` marks into `samples.csv` and
`summary.md`. It compares two sides, HEAD and the `v0.15.0` baseline. Phase 19.3's perf report is
built on its output.

## CLI

```
dotnet run --project tools/BlazorNative.PerfReport -- --input <dir> --out <dir> --min-runs 20
```

`--min-runs` defaults to 20. Exit codes:

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | The data fails the run: a clock order violation, more than 5% of samples dropped, fewer samples than `--min-runs`, a malformed `BNPERF` line, or a side with no logs. The files are still written, for debugging. |
| 2 | Wrong invocation or input layout: a missing argument, a missing `meta.json`, a `.log` file not named `<kind>-<run>.log`. |

## Input layout

```
<input>/
  <side>/               one directory per side, any name; it becomes the `side` column
    meta.json           { "sdk": "10.0.100", "commit": "abc1234" }
    boot-<run>.log      one cold boot per file
    click-<run>.log     many serial clicks per file
```

`sdk` and `commit` are non-empty strings without commas, pipes or newlines. `<run>` is a number;
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

- The k-th `dispatch.post` has an id X. It takes the next unused `dispatch.enter` with id X and the next
  unused `dispatch.handler` with id X, in per-point order filtered by X. If either is missing, the
  sample is dropped.
- The click's frame is the one `frame.recv` with `handler.t <= recv.t < next post's t`. The last click
  has no upper bound. Zero or several such marks drop the sample. Its `frame.applied` is the first applied
  mark with `recv.t <= applied.t < next recv's t` (the last recv has no upper bound). A frame with no
  such mark, as when its batch threw, drops that one sample; an applied mark is never taken from
  another frame, and unequal numbers of `frame.recv` and `frame.applied` marks are not an error.
- In a boot log the first `boot.start` and the first `boot.content` are paired.

The triple is chosen by order and id, not by timestamp, so the clock check below still tests the
shared clock.

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
default: for `[1,2,3,4,100]` the median is 3, Q1 2, Q3 4 and the IQR 2. A last section lists the
dropped samples per side and why.

## Collectors

`scripts/perf/android-collect.ps1` writes the layout above from an Android device or emulator, one kind per
call: `boot`, `click` or `q1`. It needs pwsh 7 (it uses the .NET Core APIs `ProcessStartInfo.ArgumentList` and `Process.Kill` with a process tree), adb on PATH, and a device that
accepts `logcat -G 16M`. `boot` and `click` require `-Sdk` and `-Commit` and write `meta.json` from them,
under the parser's rule: non-empty, no commas, pipes or newlines. Runs are numbered 0 to `-Runs`, run 0
being warm-up. A rerun after a failed collection must delete `<Out>/<Side>` first, or pass `-Force`. `-Force` deletes the side directory's existing `<kind>-<n>.log` files for the kind being collected, and nothing else, before writing, so no run above the new `-Runs` survives from an earlier collection. `-Runs` must be at least 1. Failures go to stderr with exit 1. Every
adb call has a deadline, and any timeout or failed assertion exits 1. `q1` evidence goes to
`<Out>/q1-<side>-default.txt` and `-verbose.txt`, outside the side directory.

`scripts/perf/ios-collect.ps1` is the simulator counterpart, with the same layout, the same `-Sdk` and
`-Commit` rules, run numbering, refusal to overwrite (delete `<Out>/<Side>` or pass `-Force`), and
exit 1 on every failure. It needs pwsh 7, macOS with Xcode, and a booted simulator, and every `xcrun`
and `xcodebuild` call has a deadline. It enables debug persistence for the `io.blazornative` subsystem
and reads `log show --debug` over a window that starts before each run's launch.

- `boot` terminates the app and cold-launches it with `simctl launch`, passing
  `SIMCTL_CHILD_BN_LAUNCH_COMPONENT=PerfProbe` and `SIMCTL_CHILD_BN_LOG_LEVEL=Verbose`. The shell reads
  `BN_LAUNCH_COMPONENT` like `BN_LOG_LEVEL` (see `BnDeepLink.resolveLaunchComponent`), so no URL and no
  `Open in` sheet is involved. `blazornative://perf` still opens PerfProbe for manual runs.
- `click` runs `xcodebuild test-without-building -only-testing:BnHostTests/BnPerfMeasurementTests` with
  `TEST_RUNNER_PERF_CLICKS` and `TEST_RUNNER_BN_LOG_LEVEL=Verbose`, needs `-DerivedData` from a
  build-for-testing, and waits for every click's `dispatch.post` and `dispatch.handler` before dumping.
  The PR lane skips that class.
- `q1` launches PerfProbe the same way and asserts no `BNPERF` line and no debug or info line from the subsystem at the default level, with
  a control that the launched process logged, then all four marks at Verbose.

`BN_LOG_LEVEL` takes the name `Verbose`, not a digit: `BnLogLevel.fromName` reads names only.
