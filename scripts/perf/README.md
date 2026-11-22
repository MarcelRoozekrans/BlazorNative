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
run 0 is warm-up. A kind (`boot` or `click`) present for one side must be present for every side.

## Mark lines

A mark is `BNPERF <point> seq=<n> t=<ns>`, plus `id=<handlerId>` on `dispatch.enter` and
`dispatch.handler`. `dispatch.post` may carry an `id` too. The parser finds `BNPERF ` anywhere in a
line, so a logcat or os_log prefix is fine, and ignores every line without it. A line with the marker
that is not well formed fails the run, naming file and line. Points: `boot.start`, `boot.content`,
`dispatch.post`, `dispatch.enter`, `dispatch.handler`, `frame.recv`, `frame.applied`.

Boot logs may also hold Android's `Fully drawn <activity>: +<n>ms` line (`+1s234ms` and
`+1m2s3ms` parse too) and `TotalTime: <n>`.

## Pairing

"After" means log order within one run file. In a click log the k-th `dispatch.post` owns the window
up to the next post. Inside it the parser takes the first `dispatch.enter`, then the first
`dispatch.handler` after it with the same `id`, then the `frame.recv` and `frame.applied` after the
handler. A window with a missing point, no frame, or more than one frame is a dropped sample, never
paired. In a boot log the first `boot.start` and the first `boot.content` after it are paired.

## Checks

- Every sample, warm-up included, must satisfy `dispatch.post <= dispatch.enter <= dispatch.handler`
  and `frame.recv <= frame.applied`. One violation fails the run: it means the shell and .NET are not
  reading the same clock.
- Per side and kind (run 0 not counted), more than 5% dropped fails the run. Exactly 5% passes.
- Per side, fewer samples than `--min-runs` fails the run, for `boot` and for `dispatch`.

## samples.csv

Columns, exactly: `side,sdk,commit,metric,part,run,value_ms`. Invariant culture, `.` decimal point,
no quoting. Run 0 rows are included. `value_ms` has up to six decimals.

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
