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
            $"{Prefix} dispatch.post seq={seq} t={post}",
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
        Assert.Equal(25, csv.Length - 1);
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
        var broken = $"{Prefix} dispatch.post seq=99 t=9000000000\n{Prefix} dispatch.enter seq=99 t=9001000000 id=7\n";
        var input = WriteSide("head", ("click-1.log", GoodClicks(40) + broken));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        var dispatchTotal = Csv().Count(l => l.Contains(",dispatch,total,"));
        Assert.Equal(40, dispatchTotal);
        Assert.Contains("dropped", File.ReadAllText(Path.Combine(_root, "out", "summary.md")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pairing_ExtraFrameInWindow_IsDropped()
    {
        // A click that produced two frames is never paired: its frame is ambiguous.
        var twoFrames = Click(9_000_000_000, 9_001_000_000, 9_002_000_000, 9_003_000_000, 9_004_000_000)
            + $"{Prefix} frame.recv seq=2 t=9005000000\n{Prefix} frame.applied seq=2 t=9006000000\n";
        var input = WriteSide("head", ("click-1.log", GoodClicks(40) + twoFrames));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        Assert.Equal(40, Csv().Count(l => l.Contains(",frame_apply,")));
        Assert.DoesNotContain(Csv(), l => l.Contains(",click_to_content,") && l.EndsWith(",9.004", StringComparison.Ordinal));
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
        // That frame belongs to B. A is dropped, never handed B's frame.
        var a = $"{Prefix} dispatch.post seq=1 t=9000000000\n{Prefix} dispatch.enter seq=1 t=9001000000 id=7\n{Prefix} dispatch.handler seq=1 t=9002000000 id=7\n";
        var b = Click(9_100_000_000, 9_101_000_000, 9_102_000_000, 9_103_000_000, 9_104_000_000, seq: 2);
        var input = WriteSide("head", ("click-1.log", GoodClicks(40) + a + b));

        var (code, err, _) = Run(input);

        Assert.True(code == 0, err);
        Assert.Equal(41, Csv().Count(l => l.Contains(",frame_apply,")));
        Assert.DoesNotContain("head,10.0.100,abc1234,click_to_content,total,1,", Csv().Where(l => l.EndsWith(",105", StringComparison.Ordinal)));
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
        Assert.Equal(ms, MarkLine.TryParseFullyDrawnMs(line));

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

    private static void CopyDirectory(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to));
    }
}
