namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// BnLogQueue — Phase 18.1, #564: BnLog's default sink never blocks its caller.
//
// THE BUG IT FIXES. The default sink wrote each line to stderr on the caller's
// thread. Stderr is a pipe, and a write to a full pipe blocks until its reader
// drains it. On the Windows runner the pipe is about 4 KiB and its reader,
// vstest.console, lags under load, so a fault burst filled it, and the render
// thread blocked inside `NativeRenderer.HandleException`'s `BnLog.Error`, in the
// middle of a dispatch, for over a second. The shells' stderr pumps are the
// reader on a device, so the same coupling exists there, only rarer.
//
// THE SHAPE, by owner decision on 2026-10-24:
//   - a caller only takes a short lock and adds the line to a bounded queue;
//   - one background thread drains the queue to the line's writer;
//   - a full queue drops the line and counts it, and the next line that fits
//     carries the count, so the drain writes one "N lines dropped" line first.
//     When the drain empties the queue with drops still counted, as at the end
//     of a burst followed by nothing, it writes that line itself, to the writer
//     of the line it just wrote;
//   - the drop line is always tagged W, whatever BnLog.Level is, so a lost
//     Error is still announced at the Error threshold (BnLog.Level says so);
//   - the default instance drains at process exit and on an unhandled exception,
//     best effort and bounded by `ExitDrainBudget`.
//
// NO INHERITED CONTEXT. The drain is started by whichever thread logs first, on
// a device usually the render thread inside a dispatch, whose AsyncLocals hold
// the dispatch scope and the renderer. `UnsafeStart` starts it without that
// thread's ExecutionContext, so it roots none of them for the life of the process.
//
// ONE LINE, ONE WRITE. The drain writes each line with one `WriteLine`, as the
// caller did, so a pump never sees half a line from one call. Lines keep the
// order their callers queued them in.
//
// THE WRITER TRAVELS WITH THE LINE. Each entry holds the writer that was
// `Console.Error` when the line was logged, not when it is drained, so a test
// that swaps `Console.Error` and swaps it back still receives its own lines. It
// must call `BnLog.Flush` before it reads them, because they now arrive later.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A bounded line queue drained to its writers by one background thread.
/// Queuing never blocks on I/O.</summary>
internal sealed class BnLogQueue
{
    /// <summary>The default instance's bound. A Runtime test run writes about 200 lines
    /// in all (the 18.1 investigation measured 195), so 1024 is several bursts deep.</summary>
    internal const int DefaultCapacity = 1024;

    /// <summary>How long the default instance waits for its queue to drain when the
    /// process exits or an exception goes unhandled.</summary>
    internal static readonly TimeSpan ExitDrainBudget = TimeSpan.FromMilliseconds(500);

    private readonly int _capacity;
    private readonly Queue<Entry> _queue = new();
    private readonly object _lock = new();

    // All guarded by _lock.
    private int _dropped;
    private long _queued;
    private long _written;
    private Thread? _drain;

    /// <summary>Creates a queue that holds at most <paramref name="capacity"/> lines
    /// waiting to be written, besides the one being written.</summary>
    internal BnLogQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>The queue behind BnLog's default sink, drained at exit.</summary>
    internal static BnLogQueue CreateDefault()
    {
        var queue = new BnLogQueue(DefaultCapacity);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => queue.Flush(ExitDrainBudget);
        AppDomain.CurrentDomain.UnhandledException += (_, _) => queue.Flush(ExitDrainBudget);
        return queue;
    }

    /// <summary>Queues <paramref name="line"/> for <paramref name="writer"/>, or drops it
    /// and counts it if the queue is full. Never waits for the writer.</summary>
    /// <returns><see langword="true"/> if the line was queued.</returns>
    internal bool TryEnqueue(TextWriter writer, string line)
    {
        lock (_lock)
        {
            if (_queue.Count >= _capacity)
            {
                _dropped++;
                return false;
            }

            _queue.Enqueue(new Entry(writer, line, _dropped));
            _dropped = 0;
            _queued++;
            _drain ??= StartDrain();
            Monitor.PulseAll(_lock);
            return true;
        }
    }

    /// <summary>Waits until every line queued before this call has been written, or
    /// until <paramref name="timeout"/> passes.</summary>
    /// <returns><see langword="true"/> if those lines were all written in time.</returns>
    internal bool Flush(TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        lock (_lock)
        {
            long target = _queued;
            while (_written < target)
            {
                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                    return false;
                Monitor.Wait(_lock, (int)Math.Min(remaining, int.MaxValue));
            }
            return true;
        }
    }

    private Thread StartDrain()
    {
        var thread = new Thread(Drain) { IsBackground = true, Name = "BlazorNative-LogDrain" };
        // UnsafeStart: no ExecutionContext is captured, so no caller's AsyncLocal is rooted.
        thread.UnsafeStart();
        return thread;
    }

    // The loop body is its own method on purpose: with it inlined, coverlet's IL
    // instrumentation, which ci.yml's coverage run uses, produced an invalid program
    // in this never-returning loop and crashed the test host (measured in 18.1).
    private void Drain()
    {
        while (true)
            DrainOne();
    }

    /// <summary>Writes the next queued line, waiting for one if the queue is empty.</summary>
    private void DrainOne()
    {
        Entry entry;
        lock (_lock)
        {
            while (_queue.Count == 0)
                Monitor.Wait(_lock);
            entry = _queue.Dequeue();
        }

        Write(entry.Writer, entry.DroppedBefore, entry.Line);

        // Drops counted while the queue was full and not yet carried by a later line:
        // if the queue is now empty, no later line may come, so report them here.
        int trailing;
        lock (_lock)
        {
            trailing = _queue.Count == 0 ? _dropped : 0;
            _dropped -= trailing;
        }
        if (trailing > 0)
            Write(entry.Writer, trailing, line: null);

        lock (_lock)
        {
            _written++;
            Monitor.PulseAll(_lock);
        }
    }

    private static void Write(TextWriter writer, int dropped, string? line)
    {
        try
        {
            if (dropped > 0)
                writer.WriteLine(DroppedLine(dropped));
            if (line is not null)
                writer.WriteLine(line);
        }
        catch
        {
            // A writer that throws loses its line and nothing else: the drain keeps
            // going, as BnLog's own catch kept a throwing sink from faulting a caller.
        }
    }

    /// <summary>The line that reports dropped lines, in BnLog's line format at Warn, so
    /// the shells' pumps map it like any other framework line. Always Warn, deliberately,
    /// and emitted whatever the threshold: Warn is the level for "something was dropped",
    /// and a threshold of Error must still learn that errors were lost.</summary>
    internal static string DroppedLine(int count)
        => BnLog.FormatLine(BnLogLevel.Warn, "BnLog",
            $"{count} {(count == 1 ? "line" : "lines")} dropped: the log queue was full");

    private readonly record struct Entry(TextWriter Writer, string Line, int DroppedBefore);
}
