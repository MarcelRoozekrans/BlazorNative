using BlazorNative.Core;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 19.2, perf/v0.15.0-marks only: main's BnLogQueue, ported for the perf
// comparison, and the sink that installs it (BnLogQueuedSink).
//
// The first three facts are main's BnLogQueue pins from its BnLogTests, copied
// unchanged. The rest pin what is new on this branch: the installer.
//
// [Collection("host-session")]: BnLog.Sink, BnLog.Level and Console.Error are
// process-wide, as BnLogTests says.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnLogQueueTests
{
    /// <summary>18.1, #564: A FULL QUEUE DROPS AND COUNTS, AND THE NEXT WRITE REPORTS THE
    /// COUNT. The default sink's queue is bounded so a stderr that never drains cannot grow
    /// the heap, and a bound that drops silently would lose lines with nothing to say so.
    /// Two claims, one per clause of the name: lines past the bound are dropped, not written
    /// late; and the next line that fits is preceded by one line giving how many were lost.
    ///
    /// A private queue with a capacity of 4, not BnLog's own: the queue is process-wide, and
    /// tests in other collections log into it in parallel, so its count would not be exact.
    /// DOES NOT COVER: BnLog's default capacity, or that BnLog uses this type; the stderr
    /// pin above covers BnLog's path, not its bound.</summary>
    [Fact]
    public void AFullQueue_DropsLines_AndTheNextWriteReportsHowMany()
    {
        var queue = new BnLogQueue(capacity: 4);
        var stderr = new GatedWriter();
        TimeSpan budget = TimeSpan.FromSeconds(10);
        try
        {
            queue.TryEnqueue(stderr, "first");
            // anchor: the drain thread holds "first" and is parked inside the blocked writer, so
            // the queue behind it is empty and its 4 slots are all free.
            Assert.True(stderr.WaitEntered(budget), "anchor: the drain thread never reached the writer.");
            for (int i = 0; i < 4; i++)
                Assert.True(queue.TryEnqueue(stderr, $"fill {i}"), $"anchor: fill {i} did not fit in a queue of 4.");
            for (int i = 0; i < 3; i++)
                queue.TryEnqueue(stderr, $"dropped {i}");
        }
        finally { stderr.Open(); }

        Assert.True(queue.Flush(budget), "anchor: the queue did not drain once stderr opened.");
        queue.TryEnqueue(stderr, "after");
        Assert.True(queue.Flush(budget), "anchor: the queue did not drain the line after the drops.");
        List<string> lines = stderr.Lines;

        Assert.True(!lines.Any(l => l.StartsWith("dropped", StringComparison.Ordinal)),
            $"a line past the queue's bound was written anyway: [{string.Join(" | ", lines)}]. A full queue must drop.");
        Assert.True(lines.SequenceEqual(["first", "fill 0", "fill 1", "fill 2", "fill 3",
                BnLog.FormatLine(BnLogLevel.Warn, "BnLog", "3 lines dropped: the log queue was full"), "after"]),
            $"expected the 3 dropped lines to be reported once, just before the next line written; got: [{string.Join(" | ", lines)}].");
    }

    /// <summary>18.1, #564: A FULL QUEUE REPORTS ITS DROPS EVEN WHEN NOTHING IS LOGGED AFTER.
    /// A burst just before exit or a crash overflows the queue and is followed by nothing, so
    /// a count that waits for the next line is lost silently. The drain writes the count
    /// itself once the queue is empty.
    ///
    /// DOES NOT COVER: drops that happen after the drain has emptied the queue for the last
    /// time and gone idle; those still wait for the next line, or for no line at all.</summary>
    [Fact]
    public void AFullQueue_ReportsItsDrops_EvenWhenNothingIsLoggedAfter()
    {
        var queue = new BnLogQueue(capacity: 2);
        var stderr = new GatedWriter();
        TimeSpan budget = TimeSpan.FromSeconds(10);
        try
        {
            queue.TryEnqueue(stderr, "first");
            // anchor: the drain thread holds "first" and is parked inside the blocked writer.
            Assert.True(stderr.WaitEntered(budget), "anchor: the drain thread never reached the writer.");
            queue.TryEnqueue(stderr, "fill 0");
            queue.TryEnqueue(stderr, "fill 1");
            queue.TryEnqueue(stderr, "dropped 0");
            queue.TryEnqueue(stderr, "dropped 1");
        }
        finally { stderr.Open(); }

        Assert.True(queue.Flush(budget), "anchor: the queue did not drain once stderr opened.");
        // The drop line is written after the last queued line, so wait for it rather than race it.
        WaitUntil(() => stderr.Lines.Count >= 4, budget);
        List<string> lines = stderr.Lines;
        Assert.True(lines.SequenceEqual(["first", "fill 0", "fill 1",
                BnLog.FormatLine(BnLogLevel.Warn, "BnLog", "2 lines dropped: the log queue was full")]),
            $"the 2 lines dropped at the end of a burst were never reported, because nothing was logged after "
            + $"them; got: [{string.Join(" | ", lines)}].");
    }

    /// <summary>18.1, #564: THE DRAIN THREAD DOES NOT INHERIT THE FIRST CALLER'S EXECUTION
    /// CONTEXT. The drain is started by whichever thread logs first, on a device usually the
    /// render thread inside a dispatch, whose AsyncLocals hold the dispatch scope and the
    /// renderer. A drain thread that captured them would root that renderer for the life of
    /// the process, across shutdown and a remount.
    ///
    /// DOES NOT COVER: BnLog's own default queue, whose drain started long before this fact
    /// on whatever thread logged first; a private queue starts its drain here.</summary>
    [Fact]
    public void TheDrainThread_DoesNotSeeTheFirstCallersAsyncLocals()
    {
        var local = new AsyncLocal<string?>();
        var queue = new BnLogQueue(capacity: 4);
        var seen = new ProbeWriter(() => local.Value);
        local.Value = "renderer";
        try { queue.TryEnqueue(seen, "first"); }
        finally { local.Value = null; }

        Assert.True(queue.Flush(TimeSpan.FromSeconds(10)), "anchor: the queue did not drain its one line.");
        Assert.True(seen.Observed is null,
            $"the drain thread sees the first caller's AsyncLocal: '{seen.Observed}'. Started with Thread.Start, it "
            + "captures the starting thread's ExecutionContext and keeps it, and whatever it roots, for the life "
            + "of the process.");
    }

    /// <summary>Records what <paramref name="probe"/> reads on the thread that writes a line.</summary>
    private sealed class ProbeWriter(Func<string?> probe) : TextWriter
    {
        public string? Observed { get; private set; } = "never written";
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) => Observed = probe();
    }

    /// <summary>19.2: THE INSTALLED SINK NEVER BLOCKS ITS CALLER. With stderr blocked, a
    /// logged line returns at once and arrives once stderr opens, written on another thread.
    /// This is the property the perf comparison needs: no framework line is a synchronous
    /// stderr write on the measured thread.</summary>
    [Fact]
    public void TheInstalledSink_ReturnsWhileStderrIsBlocked_AndTheLineArrivesLater()
    {
        Action<BnLogLevel, string, string>? originalSink = BnLog.Sink;
        TextWriter originalErr = Console.Error;
        BnLogLevel originalLevel = BnLog.Level;
        var stderr = new GatedWriter();
        TimeSpan budget = TimeSpan.FromSeconds(10);
        try
        {
            BnLog.Sink = null;
            BnLog.Level = BnLogLevel.Verbose;
            Assert.True(BnLogQueuedSink.Install(), "anchor: with no sink set, the queued sink must install.");
            Console.SetError(stderr);

            // The call runs on its own thread, so a sink that writes on its caller's thread
            // fails this fact by timing out here instead of hanging the test run.
            var caller = new Thread(() => BnLog.Verbose("BnLogQueueTests", "logged-while-blocked")) { IsBackground = true };
            caller.Start();
            Assert.True(caller.Join(TimeSpan.FromSeconds(2)),
                "BnLog.Verbose did not return within 2 s with stderr blocked: the line was written on the caller's thread.");
        }
        finally
        {
            stderr.Open();
            Console.SetError(originalErr);
        }
        try
        {
            Assert.True(BnLogQueuedSink.Flush(budget), "anchor: the queue did not drain once stderr opened.");
            Assert.Contains("[BN|V|BnLogQueueTests] logged-while-blocked", stderr.Lines);
        }
        finally
        {
            BnLog.Sink = originalSink;
            BnLog.Level = originalLevel;
        }
    }

    /// <summary>19.2: A CONSUMER'S SINK IS LEFT ALONE, as main's queue is bypassed by one.</summary>
    [Fact]
    public void Install_LeavesAConsumersSinkInPlace()
    {
        Action<BnLogLevel, string, string>? originalSink = BnLog.Sink;
        Action<BnLogLevel, string, string> consumer = (_, _, _) => { };
        try
        {
            BnLog.Sink = consumer;
            Assert.False(BnLogQueuedSink.Install(), "Install reported the queued sink active over a consumer's sink.");
            Assert.Same(consumer, BnLog.Sink);
        }
        finally { BnLog.Sink = originalSink; }
    }

    /// <summary>19.2: INSTALL IS IDEMPOTENT. A second init must not stack a second queue.</summary>
    [Fact]
    public void Install_Twice_KeepsOneSink()
    {
        Action<BnLogLevel, string, string>? originalSink = BnLog.Sink;
        try
        {
            BnLog.Sink = null;
            Assert.True(BnLogQueuedSink.Install());
            Action<BnLogLevel, string, string>? first = BnLog.Sink;
            Assert.True(BnLogQueuedSink.Install());
            Assert.Same(first, BnLog.Sink);
            Assert.Same(BnLogQueuedSink.InstalledSink, BnLog.Sink);
        }
        finally { BnLog.Sink = originalSink; }
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan budget)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < budget)
        {
            if (condition())
                return true;
            Thread.Sleep(10);
        }
        return condition();
    }
}
