using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BlazorNative.Core;

namespace BlazorNative.Runtime.Tests;

// Phase 19.2: the mark line is a contract the harness parser pairs, so the
// format is pinned here. BnLog.Level is process-wide, hence the collection;
// Dispose restores the level and nulls the seams.
[Collection("host-session")]
public sealed class BnPerfMarksTests : IDisposable
{
    private readonly BnLogLevel _originalLevel = BnLog.Level;
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly ConcurrentQueue<int> _threads = new();
    private readonly Action<BnLogLevel, string, string>? _originalSink = BnLog.Sink;

    public BnPerfMarksTests()
    {
        BnPerfMarks.ResetForTest();
        BnPerfMarks.TestSink = line =>
        {
            _threads.Enqueue(Environment.CurrentManagedThreadId);
            _lines.Enqueue(line);
        };
    }

    public void Dispose()
    {
        // The poll is restored before the drain, whose pulse makes a writer parked in a
        // long test poll wait again with the default one.
        BnPerfMarks.PollMillisecondsForTest = 0;
        BnPerfMarks.DrainForTest(TimeSpan.FromSeconds(5));
        BnPerfMarks.TestSink = null;
        BnLog.Sink = _originalSink;
        BnPerfMarks.ClockForTest = null;
        BnPerfMarks.StartForTest = null;
        BnPerfMarks.ResetForTest();
        BnLog.Level = _originalLevel;
    }

    private static void Drain() => Assert.True(BnPerfMarks.DrainForTest(TimeSpan.FromSeconds(5)));

    private static bool WaitUntil(Func<bool> condition, TimeSpan bound)
    {
        DateTime end = DateTime.UtcNow + bound;
        while (DateTime.UtcNow < end)
        {
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return condition();
    }

    private string[] Seqs() => [.. _lines.Select(l => Regex.Match(l, @"seq=(\d+)").Groups[1].Value)];

    [Fact]
    public void Mark_AtWarn_ReadsNoClockAndEmitsNothing()
    {
        BnLog.Level = BnLogLevel.Warn;
        int reads = 0;
        BnPerfMarks.ClockForTest = () => { reads++; return 1; };

        BnPerfMarks.Mark("dispatch.enter");
        Drain();

        Assert.Equal(0, reads);
        Assert.Empty(_lines);
    }

    [Fact]
    public void Mark_AtVerbose_EmitsOneWellFormedLine()
    {
        BnLog.Level = BnLogLevel.Verbose;

        BnPerfMarks.Mark("dispatch.enter");
        Drain();

        string line = Assert.Single(_lines);
        Assert.Matches(new Regex(@"^BNPERF dispatch\.enter seq=1 t=\d+$"), line);
    }

    [Fact]
    public void Mark_DispatchPoint_CarriesId()
    {
        BnLog.Level = BnLogLevel.Verbose;

        BnPerfMarks.Mark("dispatch.handler", 42);
        Drain();

        string line = Assert.Single(_lines);
        Assert.EndsWith(" id=42", line);
        Assert.Matches(new Regex(@"^BNPERF dispatch\.handler seq=1 t=\d+ id=42$"), line);
    }

    [Fact]
    public void Mark_SeqIsPerPoint()
    {
        BnLog.Level = BnLogLevel.Verbose;

        BnPerfMarks.Mark("dispatch.enter");
        BnPerfMarks.Mark("dispatch.enter");
        BnPerfMarks.Mark("dispatch.handler");
        Drain();

        string[] seqs = _lines.Select(l => Regex.Match(l, @"seq=(\d+)").Groups[1].Value).ToArray();
        Assert.Equal(new[] { "1", "2", "1" }, seqs);
    }

    [Fact]
    public void Mark_DoesNotWriteOnCallerThread()
    {
        BnLog.Level = BnLogLevel.Verbose;

        BnPerfMarks.Mark("dispatch.enter");
        Drain();

        Assert.True(_threads.TryDequeue(out int writer));
        Assert.NotEqual(Environment.CurrentManagedThreadId, writer);
    }

    [Fact]
    public void Mark_ProductionPath_WritesVerbosePerfLineThroughBnLog()
    {
        BnLog.Level = BnLogLevel.Verbose;
        BnPerfMarks.TestSink = null;
        var seen = new ConcurrentQueue<(BnLogLevel Level, string Category, string Message)>();
        BnLog.Sink = (level, category, message) => seen.Enqueue((level, category, message));

        BnPerfMarks.Mark("dispatch.handler", 7);
        Drain();

        var entry = Assert.Single(seen);
        Assert.Equal(BnLogLevel.Verbose, entry.Level);
        Assert.Equal("Perf", entry.Category);
        Assert.Matches(new Regex(@"^BNPERF dispatch\.handler seq=1 t=\d+ id=7$"), entry.Message);
    }

    // I1(b): a mark inside a measured span must not wake the writer. The lines still reach
    // the sink, by the writer's own poll, with no DrainForTest pulse to help them.
    [Fact]
    public void Mark_BelowWakeDepth_DoesNotWakeWriter_AndThePollStillDrainsInOrder()
    {
        BnLog.Level = BnLogLevel.Verbose;

        BnPerfMarks.Mark("dispatch.enter");
        BnPerfMarks.Mark("dispatch.enter");
        BnPerfMarks.Mark("dispatch.enter");

        Assert.True(WaitUntil(() => _lines.Count == 3, TimeSpan.FromSeconds(5)),
            $"the writer's poll delivered {_lines.Count} of 3 lines within 5 s");
        Assert.Equal(0, BnPerfMarks.WakesForTest);
        Assert.Equal(new[] { "1", "2", "3" }, Seqs());
    }

    // I1(b): with the poll pushed out of reach, lines below WakeDepth wait, and the mark that
    // makes the queue WakeDepth deep wakes the writer, which then drains every line in order.
    [Fact]
    public void Mark_AtWakeDepth_WakesTheWriter_WhichDrainsEveryLineInOrder()
    {
        BnLog.Level = BnLogLevel.Verbose;
        BnPerfMarks.PollMillisecondsForTest = 600_000;
        Assert.True(BnPerfMarks.RetireWriterForTest(TimeSpan.FromSeconds(5)));

        // The first mark starts a writer that only ever waits with the long poll. Once the
        // drain reports idle, the writer is inside that wait.
        BnPerfMarks.Mark("dispatch.enter");
        Drain();
        Assert.Single(_lines);

        for (int i = 1; i < BnPerfMarks.WakeDepth; i++) BnPerfMarks.Mark("dispatch.enter");
        Thread.Sleep(300);
        Assert.Single(_lines);
        Assert.Equal(0, BnPerfMarks.WakesForTest);

        BnPerfMarks.Mark("dispatch.enter");
        Assert.True(WaitUntil(() => _lines.Count == BnPerfMarks.WakeDepth + 1, TimeSpan.FromSeconds(5)),
            $"the WakeDepth mark did not wake the writer: {_lines.Count} of {BnPerfMarks.WakeDepth + 1} lines within 5 s");
        Assert.Equal(1, BnPerfMarks.WakesForTest);
        Assert.Equal(
            Enumerable.Range(1, BnPerfMarks.WakeDepth + 1).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Seqs());
    }

    // M3: the writer's start can throw. The mark must not throw into its caller, the line is
    // counted as dropped, and the next mark starts the writer.
    [Fact]
    public void Mark_WhenTheWriterFailsToStart_DropsTheLine_AndTheNextMarkRetries()
    {
        BnLog.Level = BnLogLevel.Verbose;
        Assert.True(BnPerfMarks.RetireWriterForTest(TimeSpan.FromSeconds(5)));
        BnPerfMarks.StartForTest = _ => throw new OutOfMemoryException("planted start failure");

        Exception? thrown = Record.Exception(() => BnPerfMarks.Mark("dispatch.handler", 9));

        Assert.Null(thrown);
        Assert.Equal(1, BnPerfMarks.DroppedForTest);

        BnPerfMarks.StartForTest = null;
        BnPerfMarks.Mark("dispatch.handler", 9);
        Drain();

        string line = Assert.Single(_lines);
        Assert.Matches(new Regex(@"^BNPERF dispatch\.handler seq=2 t=\d+ id=9$"), line);
    }
}
