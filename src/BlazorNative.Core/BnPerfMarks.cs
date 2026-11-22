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
// OWN WRITER ON PURPOSE. The same file is copied verbatim onto the v0.15.0
// backport branch, whose BnLog writes stderr synchronously. So this type does
// not use BnLogQueue or anything newer than v0.15.0, only BnLog.IsEnabled,
// BnLog.Verbose and BCL types, and a mark costs the same on both sides: a clock
// read, a counter bump and a short lock. The line is written by one lazily
// started background thread, never on the caller's thread.
//
// NO INHERITED CONTEXT. The thread is started with UnsafeStart, as BnLogQueue
// does, so it does not root the first caller's AsyncLocals (dispatch scope,
// renderer) for the life of the process.
//
// BOUNDED. A full queue drops the line and counts it; the count is readable
// through DroppedForTest.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Verbose-gated perf mark writer used by the measurement harness.
/// Not part of the supported API surface.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BnPerfMarks
{
    private const int Capacity = 4096;

    private static readonly ConcurrentDictionary<string, long[]> s_seq = new();
    private static readonly Queue<string> s_queue = new();
    private static readonly object s_lock = new();
    private static Thread? s_thread;
    private static bool s_busy;
    private static long s_dropped;

    /// <summary>Test seam: receives each line instead of <see cref="BnLog.Verbose"/>,
    /// on the writer thread.</summary>
    internal static Action<string>? TestSink { get; set; }

    /// <summary>Test seam: replaces <see cref="Stopwatch.GetTimestamp"/>.</summary>
    internal static Func<long>? ClockForTest { get; set; }

    /// <summary>Lines dropped because the queue was full.</summary>
    internal static long DroppedForTest => Interlocked.Read(ref s_dropped);

    /// <summary>Records one mark. Does nothing unless Verbose logging is enabled.</summary>
    /// <param name="point">The mark point, for example <c>dispatch.enter</c>.</param>
    /// <param name="handlerId">The event handler id; 0 omits the id field.</param>
    public static void Mark(string point, ulong handlerId = 0)
    {
        if (!BnLog.IsEnabled(BnLogLevel.Verbose)) return;

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
            s_queue.Enqueue(line);
            if (s_thread is null)
            {
                var t = new Thread(Drain) { IsBackground = true, Name = "BnPerfMarks" };
                s_thread = t;
                t.UnsafeStart();
            }
            Monitor.PulseAll(s_lock);
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
                    s_busy = false;
                    Monitor.PulseAll(s_lock);
                    Monitor.Wait(s_lock);
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
                // A mark must never take the process down.
            }
        }
    }

    /// <summary>Waits until every queued line has been written, or the timeout elapses.</summary>
    internal static bool DrainForTest(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        lock (s_lock)
        {
            while (s_queue.Count > 0 || s_busy)
            {
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(s_lock, left);
            }
            return true;
        }
    }

    /// <summary>Clears the per-point sequence counters and the drop count.</summary>
    internal static void ResetForTest()
    {
        s_seq.Clear();
        Interlocked.Exchange(ref s_dropped, 0);
    }
}
