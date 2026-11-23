using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// BnPerfMarks — Phase 19.2: Verbose-gated perf marks for the measurement harness.
//
// THE LINE FORMAT IS A CONTRACT (the harness parser pairs these lines):
//   BNPERF <point> seq=<n> t=<ns>            and, on dispatch points,
//   BNPERF <point> seq=<n> t=<ns> id=<handlerId>
// Points: boot.start, boot.content, dispatch.post, dispatch.enter,
// dispatch.handler, frame.recv, frame.applied. The log category is "Perf".
//
// COST BELOW VERBOSE: one BnLog.IsEnabled check. No clock read, no formatting.
//
// OWN WRITER ON PURPOSE. The same file is copied onto the v0.15.0 backport
// branch, whose BnLog writes stderr synchronously. So this type does not use
// BnLogQueue or anything newer than v0.15.0, only BnLog.IsEnabled, BnLog.Verbose
// and BCL types, and a mark costs the same on both sides: a clock read, a
// counter bump and a short lock. The line is written by one lazily started
// background thread, never on the caller's thread. The backport's copy differs
// from this one only in its access: it is public there, with
// [EditorBrowsable(Never)], because that release's Core grants Renderer and
// Runtime no InternalsVisibleTo.
//
// THE WRITER IS NOT WOKEN BY A MARK. A mark sits inside a measured span, often
// just before the thread that took it must itself be woken, so a mark that
// pulsed the writer would wake a second thread at the worst moment. The writer
// instead polls: it waits up to `PollInterval` for lines, and a mark pulses it
// only once the queue is `WakeDepth` deep, half its capacity, so a burst cannot
// fill the queue between polls. Lines still drain in the order they were
// queued, by the one writer. While the writer is idle a line leaves within one
// poll interval; while it is writing a backlog, the line waits its turn.
//
// THE EXIT DRAIN IS BEST EFFORT. `Flush` wakes the writer and waits, bounded,
// until the queue is empty. The process-exit and unhandled-exception handlers
// call it, but a NativeAOT library may never see ProcessExit, and where it
// does, BnLog's own queue may have run its exit handler first, so a line handed
// over after that is not waited for. On main, blazornative_shutdown calls
// `Flush` before it flushes BnLog, which covers the exit the framework controls.
//
// NO INHERITED CONTEXT. The thread is started with UnsafeStart, as BnLogQueue
// does, so it does not root the first caller's AsyncLocals (dispatch scope,
// renderer) for the life of the process.
//
// BOUNDED, AND NEVER THROWS INTO A MARK. A full queue drops the line and counts
// it. A thread that fails to start drops the line the same way and leaves the
// thread unset, so the next mark retries the start. A failed write is
// swallowed on the writer thread. The count is readable through DroppedForTest.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Verbose-gated perf mark writer used by the measurement harness.
/// Not part of the supported API surface.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BnPerfMarks
{
    private const int Capacity = 4096;

    /// <summary>The queue depth from which a mark wakes the writer instead of leaving the
    /// line for the writer's next poll.</summary>
    internal const int WakeDepth = Capacity / 2;

    /// <summary>How long the writer waits for lines before it looks at the queue again.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>How long the exit handlers wait for the queue to drain.</summary>
    private static readonly TimeSpan ExitDrainBudget = TimeSpan.FromMilliseconds(500);

    private static readonly ConcurrentDictionary<string, long[]> s_seq = new();
    private static readonly Queue<string> s_queue = new();
    private static readonly object s_lock = new();
    private static Thread? s_thread;
    private static bool s_busy;
    private static bool s_exitHooked;
    private static long s_dropped;
    private static long s_wakes;

    /// <summary>Test seam: receives each line instead of <see cref="BnLog.Verbose"/>,
    /// on the writer thread.</summary>
    internal static Action<string>? TestSink { get; set; }

    /// <summary>Test seam: replaces <see cref="Stopwatch.GetTimestamp"/>.</summary>
    internal static Func<long>? ClockForTest { get; set; }

    /// <summary>Test seam: replaces <see cref="Thread.UnsafeStart()"/> for the writer thread.</summary>
    internal static Action<Thread>? StartForTest { get; set; }

    private static int s_pollMsForTest;

    /// <summary>Test seam: the writer's poll in milliseconds instead of <see cref="PollInterval"/>;
    /// 0 restores it. Read before each wait.</summary>
    internal static int PollMillisecondsForTest
    {
        get => Volatile.Read(ref s_pollMsForTest);
        set => Volatile.Write(ref s_pollMsForTest, value);
    }

    /// <summary>Lines dropped because the queue was full or the writer did not start.</summary>
    internal static long DroppedForTest => Interlocked.Read(ref s_dropped);

    /// <summary>How many marks woke the writer, because the queue was <see cref="WakeDepth"/> deep.</summary>
    internal static long WakesForTest => Interlocked.Read(ref s_wakes);

    /// <summary>Records one mark. Does nothing unless Verbose logging is enabled.</summary>
    /// <param name="point">The mark point, for example <c>dispatch.enter</c>.</param>
    /// <param name="handlerId">The event handler id; 0 omits the id field.</param>
    public static void Mark(string point, ulong handlerId = 0)
    {
        if (!BnLog.IsEnabled(BnLogLevel.Verbose)) return;

        // The clock is read before seq on purpose: the timestamp must not include the
        // counter's cost. Racing threads may get seq and t in opposite orders; the parser tolerates it.
        Func<long>? clock = ClockForTest;
        long ticks = clock is null ? Stopwatch.GetTimestamp() : clock();
        long[] counter = s_seq.GetOrAdd(point, static _ => new long[1]);
        long seq = Interlocked.Increment(ref counter[0]);

        long freq = Stopwatch.Frequency;
        long ns = ticks / freq * 1_000_000_000L + ticks % freq * 1_000_000_000L / freq;
        string line = handlerId == 0
            ? string.Create(CultureInfo.InvariantCulture, $"BNPERF {point} seq={seq} t={ns}")
            : string.Create(CultureInfo.InvariantCulture, $"BNPERF {point} seq={seq} t={ns} id={handlerId}");
        Enqueue(line);
    }

    private static void Enqueue(string line)
    {
        lock (s_lock)
        {
            if (s_queue.Count >= Capacity)
            {
                Interlocked.Increment(ref s_dropped);
                return;
            }
            if (s_thread is null)
            {
                try
                {
                    var t = new Thread(Drain) { IsBackground = true, Name = "BnPerfMarks" };
                    Action<Thread>? start = StartForTest;
                    if (start is null) t.UnsafeStart();
                    else start(t);
                    // Assigned only after a successful start, so a failed start is retried by the
                    // next mark. The lock keeps the start single.
                    s_thread = t;
                }
                catch
                {
                    // No writer, so the line has nowhere to go: drop and count it.
                    Interlocked.Increment(ref s_dropped);
                    return;
                }
                if (!s_exitHooked)
                {
                    s_exitHooked = true;
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(ExitDrainBudget);
                    AppDomain.CurrentDomain.UnhandledException += (_, _) => Flush(ExitDrainBudget);
                }
            }
            s_queue.Enqueue(line);
            if (s_queue.Count >= WakeDepth)
            {
                Interlocked.Increment(ref s_wakes);
                Monitor.PulseAll(s_lock);
            }
        }
    }

    private static void Drain()
    {
        while (true)
        {
            string line;
            lock (s_lock)
            {
                while (s_queue.Count == 0)
                {
                    // Retired by RetireWriterForTest: a new writer may take over.
                    if (!ReferenceEquals(s_thread, Thread.CurrentThread)) return;
                    s_busy = false;
                    Monitor.PulseAll(s_lock);
                    int pollMs = PollMillisecondsForTest;
                    Monitor.Wait(s_lock, pollMs > 0 ? TimeSpan.FromMilliseconds(pollMs) : PollInterval);
                }
                line = s_queue.Dequeue();
                s_busy = true;
            }
            try
            {
                Action<string>? sink = TestSink;
                if (sink is not null) sink(line);
                else BnLog.Verbose("Perf", line);
            }
            catch
            {
                // A failed write is dropped on this thread and never reaches a mark's caller.
            }
        }
    }

    /// <summary>Wakes the writer and waits until every queued line has been written, or the
    /// timeout elapses.</summary>
    internal static bool Flush(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        lock (s_lock)
        {
            Monitor.PulseAll(s_lock);
            while (s_queue.Count > 0 || s_busy)
            {
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(s_lock, left);
            }
            return true;
        }
    }

    /// <summary>Waits until every queued line has been written, or the timeout elapses.</summary>
    internal static bool DrainForTest(TimeSpan timeout) => Flush(timeout);

    /// <summary>Drains the queue, then retires the writer thread and waits for it to end, so the
    /// next mark starts a new one. Returns false if either step ran out of time.</summary>
    internal static bool RetireWriterForTest(TimeSpan timeout)
    {
        if (!Flush(timeout)) return false;
        Thread? old;
        lock (s_lock)
        {
            old = s_thread;
            s_thread = null;
            Monitor.PulseAll(s_lock);
        }
        return old is null || old.Join(timeout);
    }

    /// <summary>Clears the per-point sequence counters, the drop count and the wake count.</summary>
    internal static void ResetForTest()
    {
        s_seq.Clear();
        Interlocked.Exchange(ref s_dropped, 0);
        Interlocked.Exchange(ref s_wakes, 0);
    }
}
