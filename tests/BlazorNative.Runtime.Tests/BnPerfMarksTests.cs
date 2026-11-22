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
        BnPerfMarks.DrainForTest(TimeSpan.FromSeconds(5));
        BnPerfMarks.TestSink = null;
        BnPerfMarks.ClockForTest = null;
        BnPerfMarks.ResetForTest();
        BnLog.Level = _originalLevel;
    }

    private static void Drain() => Assert.True(BnPerfMarks.DrainForTest(TimeSpan.FromSeconds(5)));

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
}
