using BlazorNative.PerfReport;
using BlazorNative.Tests.Shared;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// 19.2 Task 4. The parser behind the HEAD-versus-v0.15.0 numbers: it must fail loudly, never
// report a number it could not pair. Every test drives Program.Run in-process, the same entry
// point the CI step runs.
public sealed class PerfReportTests : IDisposable
{
    private const string Prefix = "11-22 10:00:00.123  1234  1250 V BlazorNative/Perf: BNPERF";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "perfreport-" + Guid.NewGuid().ToString("N"));

    public PerfReportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Fixtures =>
        Path.Combine(BnRepo.Root(), "tests", "BlazorNative.Runtime.Tests", "Fixtures", "perf");

    private static string Click(long post, long enter, long handler, long? recv, long? applied, long id = 7, int seq = 1)
    {
        var lines = new List<string>
        {
            $"{Prefix} dispatch.post seq={seq} t={post} id={id}",
            $"{Prefix} dispatch.enter seq={seq} t={enter} id={id}",
            $"{Prefix} dispatch.handler seq={seq} t={handler} id={id}",
        };
        if (recv is { } r) lines.Add($"{Prefix} frame.recv seq={seq} t={r}");
        if (applied is { } a) lines.Add($"{Prefix} frame.applied seq={seq} t={a}");
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>n well-formed clicks, 1 ms apart in each phase, spaced 100 ms apart.</summary>
    private static string GoodClicks(int n, int firstSeq = 1)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < n; i++)
        {
            long b = (firstSeq + i) * 100_000_000L;
            sb.Append(Click(b, b + 1_000_000, b + 2_000_000, b + 3_000_000, b + 4_000_000, seq: firstSeq + i));
        }
        return sb.ToString();
    }

    private string WriteSide(string side, params (string File, string Content)[] files)
    {
        var dir = Path.Combine(_root, "in", side);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "meta.json"), "{ \"sdk\": \"10.0.100\", \"commit\": \"abc1234\" }");
        foreach (var (name, content) in files) File.WriteAllText(Path.Combine(dir, name), content);
        return Path.Combine(_root, "in");
    }

    private (int Code, string Err, string Out) Run(string input, int minRuns = 1)
    {
        var err = new StringWriter();
        var info = new StringWriter();
        var code = Program.Run(["--input", input, "--out", Path.Combine(_root, "out"), "--min-runs", minRuns.ToString()], err, info);
        return (code, err.ToString(), info.ToString());
    }

    private string[] Csv() => File.ReadAllLines(Path.Combine(_root, "out", "samples.csv"));

    [Fact]
    public void KnownLog_ProducesKnownSamples()
    {
        var input = Path.Combine(_root, "known");
        CopyDirectory(Path.Combine(Fixtures, "known"), input);

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var csv = Csv();
        Assert.Equal("side,sdk,commit,metric,part,run,value_ms", csv[0]);
        Assert.Equal(27, csv.Length - 1);
        string[] expected =
        [
            "head,10.0.100,abc1234,boot,total,0,300",
            "head,10.0.100,abc1234,boot,total,1,250",
            "head,10.0.100,abc1234,boot_fully_drawn,total,1,1234",
            "head,10.0.100,abc1234,boot_total_time,total,1,842",
            "head,10.0.100,abc1234,dispatch,total,0,0.2",
            "head,10.0.100,abc1234,dispatch,total,1,0.5",
            "head,10.0.100,abc1234,dispatch,lane,1,0.3",
            "head,10.0.100,abc1234,dispatch,dotnet,1,0.2",
            "head,10.0.100,abc1234,frame_apply,total,1,0.8",
            "head,10.0.100,abc1234,click_to_content,total,1,1.8",
            "head,10.0.100,abc1234,dispatch,total,1,0.9",
            "head,10.0.100,abc1234,dispatch,lane,1,0.4",
            "head,10.0.100,abc1234,dispatch,dotnet,1,0.5",
            "head,10.0.100,abc1234,frame_apply,total,1,1.5",
            "head,10.0.100,abc1234,click_to_content,total,1,2.5",
            "base,10.0.100,def5678,boot,total,1,400",
            "base,10.0.100,def5678,boot_fully_drawn,total,1,900",
            "base,10.0.100,def5678,boot_total_time,total,1,700",
            "base,10.0.100,def5678,dispatch,total,1,1",
            "base,10.0.100,def5678,dispatch,lane,1,0.5",
            "base,10.0.100,def5678,dispatch,dotnet,1,0.5",
            "base,10.0.100,def5678,frame_apply,total,1,1",
            "base,10.0.100,def5678,click_to_content,total,1,2.1",
        ];
        foreach (var row in expected) Assert.Contains(row, csv);
        var summary = File.ReadAllText(Path.Combine(_root, "out", "summary.md"));
        Assert.Contains("abc1234", summary);
        Assert.Contains("def5678", summary);
        Assert.Contains("| head | dispatch | total | 2 | 0.700 | 0.600 | 0.800 | 0.200 |", summary);
    }

    // I1 a: a slow mode confined to some processes must be visible, so the summary gives the
    // process count per kind and one median per click run file next to the pooled table.
    [Fact]
    public void Summary_ReportsProcessCountsAndPerProcessClickMedians()
    {
        var input = Path.Combine(_root, "known");
        CopyDirectory(Path.Combine(Fixtures, "known"), input);

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var summary = File.ReadAllText(Path.Combine(_root, "out", "summary.md"));
        Assert.Contains("## Per process", summary);
        Assert.Contains("| head | boot | 1 |", summary);
        Assert.Contains("| head | click | 1 |", summary);
        Assert.Contains("| base | click | 1 |", summary);
        // head's click-1.log holds two clicks, 0.5 and 0.9 ms: one process, one median.
        Assert.Contains("| head | dispatch | total | 1 | 2 | 0.700 |", summary);
        Assert.Contains("| base | dispatch | total | 1 | 1 | 1.000 |", summary);
        // Run 0 is warm-up here too.
        Assert.DoesNotContain("| head | dispatch | total | 0 |", summary);
        // The pooled table is still there.
        Assert.Contains("| head | dispatch | total | 2 | 0.700 | 0.600 | 0.800 | 0.200 |", summary);
    }

    [Fact]
    public void ClockOrderViolation_FailsTheRun()
    {
        // handler is stamped BEFORE enter: the two sides cannot share a clock.
        var input = WriteSide("head", ("click-1.log", Click(1_000_000, 3_000_000, 2_000_000, 4_000_000, 5_000_000)));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("dispatch.handler", err);
        Assert.Contains("dispatch.enter", err);
    }

    [Fact]
    public void ClockOrderViolation_EnterBeforePost_NamesThePoints()
    {
        var input = WriteSide("head", ("click-1.log", Click(5_000_000, 1_000_000, 6_000_000, 7_000_000, 8_000_000)));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("dispatch.enter", err);
        Assert.Contains("dispatch.post", err);
    }

    [Fact]
    public void ClockOrderViolation_InWarmupRun_StillFails()
    {
        var input = WriteSide("head",
            ("click-0.log", Click(5_000_000, 4_000_000, 6_000_000, 7_000_000, 8_000_000)),
            ("click-1.log", GoodClicks(3)));

        Assert.Equal(1, Run(input).Code);
    }

    [Fact]
    public void MissingPoint_IsDroppedSample()
    {
        // 40 good clicks and one whose handler never arrived: 1/41 is under 5%, so the run passes and the click is dropped.
        var broken = $"{Prefix} dispatch.post seq=99 t=9000000000 id=7\n{Prefix} dispatch.enter seq=99 t=9001000000 id=7\n";
        var input = WriteSide("head", ("click-1.log", GoodClicks(40) + broken));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var dispatchTotal = Csv().Count(l => l.Contains(",dispatch,total,"));
        Assert.Equal(40, dispatchTotal);
        Assert.Contains("dropped", File.ReadAllText(Path.Combine(_root, "out", "summary.md")), StringComparison.OrdinalIgnoreCase);
    }

    // ---- CodeRabbit on #740: a missing middle mark must cost its own click, not misalign every
    // later occurrence of the id into a false clock violation or a cascade of drops.

    /// <summary>GoodClicks(41), all id 7, without the one line of click <paramref name="click"/> (1-based) at <paramref name="point"/>.</summary>
    private static string GoodClicksWithout(int click, string point) =>
        string.Join('\n', GoodClicks(41).Split('\n')
            .Where(l => !l.Contains($" {point} seq={click} ")));

    [Theory]
    [InlineData("dispatch.enter", 40, "1 of 41")]
    [InlineData("dispatch.handler", 40, "1 of 41")]
    // A lost post leaves click 20's frame in click 19's window: click 19 is dropped as ambiguous,
    // by the frame rule, and click 20 is not attempted at all.
    [InlineData("dispatch.post", 39, "1 of 40")]
    public void MissingMiddleMark_DropsOneClickOnly(string point, int samples, string dropped)
    {
        // 41 clicks of one id; click 20 lost one line. Pairing by order alone hands click 20 the
        // marks of click 21 and shifts every later click: a false violation or 21 drops.
        var input = WriteSide("head", ("click-1.log", GoodClicksWithout(20, point)));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var totals = Csv().Where(l => l.Contains(",dispatch,total,")).ToList();
        Assert.Equal(samples, totals.Count);
        Assert.All(totals, l => Assert.EndsWith(",2", l));
        Assert.Contains($"| head | {dropped} |", File.ReadAllText(Path.Combine(_root, "out", "summary.md")));
    }

    [Fact]
    public void MissingMark_DoesNotHideARealClockOffset()
    {
        // Every enter is stamped 50 ms before its post, and one enter is missing. The resync may
        // skip one occurrence, no more: the other clicks still report the violation.
        var sb = new System.Text.StringBuilder();
        for (var i = 1; i <= 41; i++)
        {
            long b = i * 100_000_000L;
            if (i != 20) sb.Append(Mk("dispatch.enter", b - 50_000_000, 7)).Append(Mk("dispatch.handler", b - 49_000_000, 7));
            sb.Append(Mk("dispatch.post", b, 7)).Append(Mk("frame.recv", b + 3_000_000)).Append(Mk("frame.applied", b + 4_000_000));
        }
        var input = WriteSide("head", ("click-1.log", sb.ToString()));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("dispatch.enter", err);
        Assert.Contains("precedes dispatch.post", err);
    }

    [Fact]
    public void Pairing_ExtraFrameInWindow_IsDropped()
    {
        // A click that produced two frames is never paired: its frame is ambiguous. Frame one would
        // give click_to_content 7, frame two 9; the 40 good clicks all give 4.
        var twoFrames = Click(9_000_000_000, 9_001_000_000, 9_002_000_000, 9_003_000_000, 9_007_000_000)
            + $"{Prefix} frame.recv seq=2 t=9005000000\n{Prefix} frame.applied seq=2 t=9009000000\n";
        var input = WriteSide("head", ("click-1.log", GoodClicks(40) + twoFrames));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var csv = Csv();
        Assert.Equal(40, csv.Count(l => l.Contains(",click_to_content,")));
        Assert.DoesNotContain("head,10.0.100,abc1234,click_to_content,total,1,7", csv);
        Assert.DoesNotContain("head,10.0.100,abc1234,click_to_content,total,1,9", csv);
    }

    [Fact]
    public void Pairing_ClickWithNoFrame_IsDropped()
    {
        var noFrame = Click(9_000_000_000, 9_001_000_000, 9_002_000_000, null, null);
        var input = WriteSide("head", ("click-1.log", GoodClicks(40) + noFrame));

        var (code, _, _) = Run(input);

        Assert.Equal(0, code);
        Assert.Equal(40, Csv().Count(l => l.Contains(",dispatch,total,")));
    }

    [Fact]
    public void Pairing_FrameAfterTheNextPost_IsNotPairedToTheEarlierClick()
    {
        // Click A has no frame of its own; the only frame in the log lands after click B's post.
        // That frame belongs to B (click_to_content 8). Handing it to A would give 108.
        var a = $"{Prefix} dispatch.post seq=1 t=9000000000 id=7\n{Prefix} dispatch.enter seq=1 t=9001000000 id=7\n{Prefix} dispatch.handler seq=1 t=9002000000 id=7\n";
        var b = Click(9_100_000_000, 9_101_000_000, 9_102_000_000, 9_103_000_000, 9_108_000_000, seq: 2);
        var input = WriteSide("head", ("click-1.log", GoodClicks(40) + a + b));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var csv = Csv();
        Assert.Equal(41, csv.Count(l => l.Contains(",frame_apply,")));
        Assert.Contains("head,10.0.100,abc1234,click_to_content,total,1,8", csv);
        Assert.DoesNotContain("head,10.0.100,abc1234,click_to_content,total,1,108", csv);
        Assert.Contains("dropped", File.ReadAllText(Path.Combine(_root, "out", "summary.md")));
    }

    [Fact]
    public void DropRateAboveFivePercent_FailsTheRun()
    {
        // 9 good clicks and one with no frame: 10% dropped.
        var input = WriteSide("head", ("click-1.log", GoodClicks(9) + Click(9_000_000_000, 9_001_000_000, 9_002_000_000, null, null)));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("drop", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DropRateAtExactlyFivePercent_Passes()
    {
        // 19 good clicks and one with no frame: 5% is not more than 5%.
        var input = WriteSide("head", ("click-1.log", GoodClicks(19) + Click(9_000_000_000, 9_001_000_000, 9_002_000_000, null, null)));

        Assert.Equal(0, Run(input).Code);
    }

    [Fact]
    public void FewerSamplesThanMinRuns_FailsTheRun()
    {
        var input = WriteSide("head", ("click-1.log", GoodClicks(5)));

        var (code, err, _) = Run(input, minRuns: 20);

        Assert.Equal(1, code);
        Assert.Contains("min-runs", err);
    }

    [Fact]
    public void WarmupRuns_AreExcludedFromSummary()
    {
        // run 0 is a 900 ms outlier; the summary must not see it. The CSV still has it.
        var warm = Click(0, 1_000_000, 900_000_000, 900_100_000, 900_200_000);
        var input = WriteSide("head", ("click-0.log", warm), ("click-1.log", GoodClicks(3)));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        Assert.Contains("head,10.0.100,abc1234,dispatch,total,0,900", Csv());
        var summary = File.ReadAllText(Path.Combine(_root, "out", "summary.md"));
        Assert.Contains("| head | dispatch | total | 3 | 2.000 | 2.000 | 2.000 | 0.000 |", summary);
    }

    [Fact]
    public void Summary_UsesMedianAndIqr()
    {
        var q = Stats.Compute([1m, 2m, 3m, 4m, 100m]);

        Assert.Equal(3m, q.Median);
        Assert.Equal(2m, q.Q1);
        Assert.Equal(4m, q.Q3);
        Assert.Equal(2m, q.Iqr);
    }

    [Fact]
    public void Summary_QuartilesInterpolateAndIgnoreInputOrder()
    {
        var q = Stats.Compute([4m, 1m, 3m, 2m]);

        Assert.Equal(2.5m, q.Median);
        Assert.Equal(1.75m, q.Q1);
        Assert.Equal(3.25m, q.Q3);
    }

    [Theory]
    [InlineData("I ActivityTaskManager: Fully drawn a.b/.Main: +850ms", 850)]
    [InlineData("I ActivityTaskManager: Fully drawn a.b/.Main: +1s234ms", 1234)]
    [InlineData("I ActivityTaskManager: Fully drawn a.b/.Main: +2s0ms", 2000)]
    [InlineData("I ActivityTaskManager: Fully drawn a.b/.Main: +1m2s3ms", 62003)]
    public void FullyDrawn_ParsesBothLogcatFormats(string line, long ms) =>
        Assert.Equal(ms, MarkLine.TryParseFullyDrawnMs(line, 1, "boot-1.log"));

    [Fact]
    public void MalformedMark_FailsNamingTheLine()
    {
        var input = WriteSide("head", ("click-1.log", $"{Prefix} dispatch.enter seq=1 t=12\n"));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("click-1.log:1", err);
    }

    [Fact]
    public void UnknownPoint_FailsLoudly()
    {
        var input = WriteSide("head", ("click-1.log", $"{Prefix} frame.nope seq=1 t=12\n"));

        Assert.Equal(1, Run(input).Code);
    }

    [Fact]
    public void UsageErrors_ExitTwo()
    {
        var err = new StringWriter();

        Assert.Equal(2, Program.Run(["--input"], err));
        Assert.Equal(2, Program.Run(["--input", Path.Combine(_root, "nope"), "--out", _root, "--min-runs", "1"], err));
    }

    [Fact]
    public void SideWithNoLogs_FailsLoudly()
    {
        var input = WriteSide("head");

        Assert.Equal(1, Run(input).Code);
    }

    // ---- 19.2 Task 4 fix round: pairing by per-point order and id, not by log position. On a
    // device the .NET marks reach the log late, so their position against shell marks means nothing.

    private string Mk(string point, long t, long? id = null) =>
        $"{Prefix} {point} seq=1 t={t}" + (id is { } i ? $" id={i}" : "") + "\n";

    [Fact]
    public void DotNetLinesAfterFrameLines_StillPair()
    {
        // Log order: post, recv, applied, enter, handler. A log-position pairer finds no frame after the handler.
        var log = Mk("dispatch.post", 1_000_000, 7) + Mk("frame.recv", 3_000_000) + Mk("frame.applied", 4_000_000)
            + Mk("dispatch.enter", 1_500_000, 7) + Mk("dispatch.handler", 2_000_000, 7);
        var input = WriteSide("head", ("click-1.log", log));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        // The whole file, line by line: a prefix or a wrong row cannot pass.
        Assert.Equal(
        [
            "side,sdk,commit,metric,part,run,value_ms",
            "head,10.0.100,abc1234,dispatch,total,1,1",
            "head,10.0.100,abc1234,dispatch,lane,1,0.5",
            "head,10.0.100,abc1234,dispatch,dotnet,1,0.5",
            "head,10.0.100,abc1234,frame_apply,total,1,1",
            "head,10.0.100,abc1234,click_to_content,total,1,3"
        ], Csv());
    }

    [Fact]
    public void DotNetLinesAfterNextPost_StillPair()
    {
        // Click 1's enter and handler are logged after click 2's post. A window bounded by the next post loses them.
        var log = Mk("dispatch.post", 1_000_000, 7) + Mk("frame.recv", 3_000_000) + Mk("frame.applied", 4_000_000)
            + Mk("dispatch.post", 10_000_000, 7) + Mk("frame.recv", 12_000_000) + Mk("frame.applied", 14_000_000)
            + Mk("dispatch.enter", 1_500_000, 7) + Mk("dispatch.handler", 2_000_000, 7)
            + Mk("dispatch.enter", 10_800_000, 7) + Mk("dispatch.handler", 11_500_000, 7);
        var input = WriteSide("head", ("click-1.log", log));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        Assert.Equal(
        [
            "side,sdk,commit,metric,part,run,value_ms",
            "head,10.0.100,abc1234,dispatch,total,1,1",
            "head,10.0.100,abc1234,dispatch,lane,1,0.5",
            "head,10.0.100,abc1234,dispatch,dotnet,1,0.5",
            "head,10.0.100,abc1234,frame_apply,total,1,1",
            "head,10.0.100,abc1234,click_to_content,total,1,3",
            "head,10.0.100,abc1234,dispatch,total,1,1.5",
            "head,10.0.100,abc1234,dispatch,lane,1,0.8",
            "head,10.0.100,abc1234,dispatch,dotnet,1,0.7",
            "head,10.0.100,abc1234,frame_apply,total,1,2",
            "head,10.0.100,abc1234,click_to_content,total,1,4"
        ], Csv());
    }

    [Fact]
    public void ForeignIdDispatch_IsNotPaired()
    {
        // An enter and handler for id 9 sit between the post of id 7 and its own lines. They are another handler's.
        var log = Mk("dispatch.post", 1_000_000, 7)
            + Mk("dispatch.enter", 1_100_000, 9) + Mk("dispatch.handler", 1_200_000, 9)
            + Mk("dispatch.enter", 1_500_000, 7) + Mk("dispatch.handler", 2_000_000, 7)
            + Mk("frame.recv", 3_000_000) + Mk("frame.applied", 4_000_000);
        var input = WriteSide("head", ("click-1.log", log));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var csv = Csv();
        Assert.Contains("head,10.0.100,abc1234,dispatch,total,1,1", csv);
        Assert.Contains("head,10.0.100,abc1234,dispatch,lane,1,0.5", csv);
        Assert.DoesNotContain("head,10.0.100,abc1234,dispatch,total,1,0.2", csv);
    }

    [Fact]
    public void ForeignIdDispatch_WithNoOwnLines_DropsTheClick()
    {
        var log = GoodClicks(40) + Mk("dispatch.post", 9_000_000_000, 5)
            + Mk("dispatch.enter", 9_001_000_000, 9) + Mk("dispatch.handler", 9_002_000_000, 9);
        var input = WriteSide("head", ("click-1.log", log));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        Assert.Equal(40, Csv().Count(l => l.Contains(",dispatch,total,")));
    }

    [Fact]
    public void PostWithoutId_IsMalformed()
    {
        var input = WriteSide("head", ("click-1.log", Mk("dispatch.post", 1_000_000)));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("click-1.log:1", err);
    }

    [Fact]
    public void ThrowingBatch_NoApplied_DropsOneSampleOnly()
    {
        // A frame whose batch threw has a recv and no applied. It costs its own click and nothing else:
        // the run still passes and the drop is reported.
        var thrown = Click(9_000_000_000, 9_001_000_000, 9_002_000_000, 9_003_000_000, null);
        var input = WriteSide("head", ("click-1.log", GoodClicks(40) + thrown));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var csv = Csv();
        Assert.Equal(40, csv.Count(l => l.Contains(",click_to_content,")));
        Assert.Equal(40, csv.Count(l => l.Contains(",dispatch,total,")) );
        Assert.Contains("dropped", File.ReadAllText(Path.Combine(_root, "out", "summary.md")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoAppliedForOneRecv_PairsTheFirst()
    {
        // Two applied marks inside one recv window: the first (4 ms) is the frame's, the second (9 ms) is not.
        var two = Click(9_000_000_000, 9_001_000_000, 9_002_000_000, 9_003_000_000, 9_007_000_000)
            + $"{Prefix} frame.applied seq=2 t=9012000000\n";
        var input = WriteSide("head", ("click-1.log", GoodClicks(40) + two));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var csv = Csv();
        Assert.Contains("head,10.0.100,abc1234,click_to_content,total,1,7", csv);
        Assert.DoesNotContain("head,10.0.100,abc1234,click_to_content,total,1,12", csv);
        Assert.Contains("head,10.0.100,abc1234,frame_apply,total,1,4", csv);
    }

    [Fact]
    public void BootContentBeforeStart_FailsTheRun()
    {
        var input = WriteSide("head", ("boot-1.log", Mk("boot.start", 500_000_000) + Mk("boot.content", 100_000_000)));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("boot.content", err);
        Assert.Contains("boot.start", err);
    }

    [Fact]
    public void DropsThatPushASideBelowMinRuns_ExitOne()
    {
        // 24 good clicks and one with no frame: 4% dropped passes the rate, but 24 samples is under 25.
        var input = WriteSide("head", ("click-1.log", GoodClicks(24) + Click(9_000_000_000, 9_001_000_000, 9_002_000_000, null, null)));

        var (code, err, _) = Run(input, minRuns: 25);

        Assert.Equal(1, code);
        Assert.Contains("min-runs", err);
        Assert.DoesNotContain("5% limit", err);
    }

    private static string BootLog(bool fullyDrawn, bool totalTime, bool twoFullyDrawn = false)
    {
        var s = $"{Prefix} boot.start seq=1 t=100000000\n{Prefix} boot.content seq=1 t=350000000\n";
        if (fullyDrawn) s += "I ActivityTaskManager: Fully drawn a.b/.Main: +850ms\n";
        if (twoFullyDrawn) s += "I ActivityTaskManager: Fully drawn a.b/.Main: +900ms\n";
        if (totalTime) s += "I ActivityManager: TotalTime: 842\n";
        return s;
    }

    private string WriteBoots(string side, int count, int? withoutFullyDrawn = null) =>
        WriteSide(side, Enumerable.Range(1, count)
            .Select(run => ($"boot-{run}.log", BootLog(fullyDrawn: run != withoutFullyDrawn, totalTime: true))).ToArray());

    [Fact]
    public void Ruler_TwoValuesInOneFile_FailsNamingTheFile()
    {
        var input = WriteSide("head", ("boot-3.log", BootLog(true, true, twoFullyDrawn: true)));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("boot-3.log", err);
        Assert.Contains("Fully drawn", err);
    }

    [Fact]
    public void Ruler_MissingInALaterRun_IsADroppedSample()
    {
        // 1 of 20 is exactly 5%: it passes, but the sample is dropped and reported.
        var input = WriteBoots("head", 20, withoutFullyDrawn: 7);

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var csv = Csv();
        Assert.Equal(19, csv.Count(l => l.Contains(",boot_fully_drawn,")));
        Assert.Equal(20, csv.Count(l => l.Contains(",boot_total_time,")));
        Assert.Contains("boot-7.log: no boot_fully_drawn", File.ReadAllText(Path.Combine(_root, "out", "summary.md")));
    }

    [Fact]
    public void Ruler_MissingInHalfTheRuns_FailsTheDropRule()
    {
        var input = WriteBoots("head", 2, withoutFullyDrawn: 2);

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("boot_fully_drawn", err);
        Assert.Contains("5% limit", err);
    }

    [Fact]
    public void Ruler_BelowMinRuns_FailsEvenWhenBootPasses()
    {
        // 20 boots meet --min-runs 20, but the ruler has 19.
        var input = WriteBoots("head", 20, withoutFullyDrawn: 7);

        var (code, err, _) = Run(input, minRuns: 20);

        Assert.Equal(1, code);
        Assert.Contains("boot_fully_drawn", err);
        Assert.Contains("min-runs", err);
    }

    [Fact]
    public void Ruler_OnOneSideOnly_FailsTheRun()
    {
        WriteBoots("head", 2);
        var input = WriteSide("base", ("boot-1.log", BootLog(false, false)), ("boot-2.log", BootLog(false, false)));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("boot_fully_drawn", err);
        Assert.Contains("not for every side", err);
    }

    [Fact]
    public void Ruler_OnNeitherSide_IsFine()
    {
        WriteSide("head", ("boot-1.log", BootLog(false, false)));
        var input = WriteSide("base", ("boot-1.log", BootLog(false, false)));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        Assert.DoesNotContain(Csv(), l => l.Contains(",boot_fully_drawn,") || l.Contains(",boot_total_time,"));
    }

    [Fact]
    public void DuplicateRunNumbers_FailNamingBothFiles()
    {
        // boot-1.log and boot-01.log are both run 1: their samples would double.
        var input = WriteSide("head", ("boot-1.log", BootLog(false, false)), ("boot-01.log", BootLog(false, false)));

        var (code, err, _) = Run(input);

        Assert.Equal(2, code);
        Assert.Contains("boot-1.log", err);
        Assert.Contains("boot-01.log", err);
    }

    [Fact]
    public void Ruler_SeenOnlyInWarmup_DoesNotDropLaterRuns()
    {
        // The warm-up boot has a ruler, runs 1 and 2 do not: no ruler is reported, so nothing is dropped.
        var input = WriteSide("head",
            ("boot-0.log", BootLog(true, true)), ("boot-1.log", BootLog(false, false)), ("boot-2.log", BootLog(false, false)));

        var (code, err, _) = Run(input, minRuns: 2);

        Assert.True(code == 0, err);
        Assert.DoesNotContain("no boot_fully_drawn", File.ReadAllText(Path.Combine(_root, "out", "summary.md")));
    }

    [Fact]
    public void Ruler_SeenOnlyInWarmupOnOneSide_IsNotASideMismatch()
    {
        WriteSide("head", ("boot-0.log", BootLog(true, true)), ("boot-1.log", BootLog(false, false)));
        var input = WriteSide("base", ("boot-0.log", BootLog(false, false)), ("boot-1.log", BootLog(false, false)));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
    }

    // ---- CodeRabbit on #740: a failed run still writes this run's partial artifacts, and never
    // leaves an earlier run's artifacts behind.

    private string Out(string file) => Path.Combine(_root, "out", file);

    private void PlantStaleArtifacts()
    {
        Directory.CreateDirectory(Path.Combine(_root, "out"));
        File.WriteAllText(Out("samples.csv"), "stale\n");
        File.WriteAllText(Out("summary.md"), "stale\n");
    }

    [Fact]
    public void MalformedLog_ExitOne_WritesThePartialArtifacts()
    {
        PlantStaleArtifacts();
        var input = WriteSide("head",
            ("click-1.log", GoodClicks(3)),
            ("click-2.log", $"{Prefix} dispatch.enter seq=1 t=12\n"));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("click-2.log:1", err);
        // click-1's samples, completed before the bad file, are in this run's artifacts.
        Assert.Equal(3, Csv().Count(l => l.Contains(",dispatch,total,1,")));
        Assert.DoesNotContain("stale", File.ReadAllText(Out("summary.md")));
    }

    [Fact]
    public void DuplicateRuler_ExitOne_WritesThePartialArtifacts()
    {
        PlantStaleArtifacts();
        var input = WriteSide("head", ("boot-1.log", BootLog(true, true)), ("boot-2.log", BootLog(true, true, twoFullyDrawn: true)));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("boot-2.log", err);
        Assert.Contains("head,10.0.100,abc1234,boot,total,1,250", Csv());
        Assert.DoesNotContain("stale", File.ReadAllText(Out("summary.md")));
    }

    [Fact]
    public void LayoutError_ExitTwo_LeavesNoStaleArtifacts()
    {
        PlantStaleArtifacts();
        var input = WriteSide("head", ("click-one.log", GoodClicks(3)));

        Assert.Equal(2, Run(input).Code);
        Assert.False(File.Exists(Out("samples.csv")));
        Assert.False(File.Exists(Out("summary.md")));
    }

    // ---- CodeRabbit on #740: the process rows count input files, not files that kept a sample.
    [Fact]
    public void Summary_ProcessCount_IncludesAFileWhoseSamplesAllDropped()
    {
        var noFrames = Click(100_000_000, 101_000_000, 102_000_000, null, null);
        var input = WriteSide("head",
            ("boot-1.log", BootLog(false, false)),
            ("boot-2.log", $"{Prefix} boot.start seq=1 t=100000000\n"),
            ("click-0.log", GoodClicks(3)),
            ("click-1.log", GoodClicks(3)),
            ("click-2.log", noFrames));

        Run(input);

        var summary = File.ReadAllText(Out("summary.md"));
        Assert.Contains("| head | boot | 2 |", summary);
        Assert.Contains("| head | click | 2 |", summary);
    }

    // ---- CodeRabbit on #740: the side name is written unquoted, so a delimiter in it is refused.
    // A comma is the one delimiter every platform allows in a directory name; Windows rejects a
    // pipe or a double quote there. The side name and meta.json's values share one character set,
    // and MetaValueWithADelimiter_ExitTwo covers every character of it on every platform.
    [Fact]
    public void SideNameWithADelimiter_ExitTwo()
    {
        var input = WriteSide("he,ad", ("click-1.log", GoodClicks(3)));

        var (code, err, _) = Run(input);

        Assert.Equal(2, code);
        Assert.Contains("side directory name", err);
    }

    // ---- CodeRabbit on #740, second review: sdk and commit are written unquoted too. A double
    // quote would open a quoted field for an RFC 4180 reader, so it is refused like a delimiter.
    [Theory]
    [InlineData("10.0,100")]
    [InlineData("10.0|100")]
    [InlineData("10.0\\\"100")]
    [InlineData("10.0\\n100")]
    public void MetaValueWithADelimiter_ExitTwo(string jsonSdk)
    {
        var input = WriteSide("head", ("click-1.log", GoodClicks(3)));
        File.WriteAllText(Path.Combine(input, "head", "meta.json"), $"{{ \"sdk\": \"{jsonSdk}\", \"commit\": \"abc1234\" }}");

        var (code, err, _) = Run(input);

        Assert.Equal(2, code);
        Assert.Contains("\"sdk\" must be", err);
    }

    // ---- CodeRabbit on #740, second review: a meta.json whose root is not an object is a layout
    // error, not an InvalidOperationException out of TryGetProperty.
    [Fact]
    public void MetaJsonRootNotAnObject_ExitTwo()
    {
        var input = WriteSide("head", ("click-1.log", GoodClicks(3)));
        File.WriteAllText(Path.Combine(input, "head", "meta.json"), "[\"10.0.100\", \"abc1234\"]");

        var (code, err, _) = Run(input);

        Assert.Equal(2, code);
        Assert.Contains("meta.json", err);
        Assert.Contains("JSON object", err);
    }

    // ---- CodeRabbit on #740, second review: a run number that overflows an int is a layout error
    // naming the file, not a crash.
    [Fact]
    public void RunNumberOutOfRange_ExitTwo_NamingTheFile()
    {
        var input = WriteSide("head", ("click-1.log", GoodClicks(3)), ("click-99999999999.log", GoodClicks(3)));

        var (code, err, _) = Run(input);

        Assert.Equal(2, code);
        Assert.Contains("click-99999999999.log", err);
        Assert.Contains("out of range", err);
    }

    // ---- CodeRabbit on #740, second review: an out-of-range ruler is a malformed line of its own
    // file, so the other files' samples are kept and the run exits 1, naming the file and line.
    [Theory]
    [InlineData("I ActivityManager: TotalTime: 99999999999999999999")]
    [InlineData("I ActivityTaskManager: Fully drawn a.b/.Main: +99999999999999999999ms")]
    // Each part parses; the sum in ms does not fit a long.
    [InlineData("I ActivityTaskManager: Fully drawn a.b/.Main: +999999999999999999h")]
    public void RulerOutOfRange_ExitOne_NamingTheLine_KeepsOtherFiles(string ruler)
    {
        var input = WriteSide("head",
            ("boot-1.log", BootLog(false, false)),
            ("boot-2.log", BootLog(false, false) + ruler + "\n"));

        var (code, err, _) = Run(input);

        Assert.Equal(1, code);
        Assert.Contains("head/boot-2.log:3", err);
        Assert.Contains("out of range", err);
        Assert.Contains("head,10.0.100,abc1234,boot,total,1,250", Csv());
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to));
    }
}
