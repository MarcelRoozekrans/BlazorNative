using BlazorNative.Core;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// BnLogQueuedSink — Phase 19.2, on perf/v0.15.0-marks only.
//
// WHY. At v0.15.0, BnLog's default sink is a Console.Error.WriteLine on the
// caller's thread. At Verbose that is every FrameEncoder, renderer and Exports
// line, written synchronously on the dispatch lane and the init thread, inside the
// spans the harness measures. Main queues the same lines (BnLogQueue, 18.1). Owner
// decision 2026-11-23: the backport gets main's queue, so the comparison does not
// charge the old side for a log write the new side does not make.
//
// HOW. Install() puts a sink on the public BnLog.Sink that formats the line with
// BnLog.FormatLine on the caller's thread and hands it to a BnLogQueue, whose one
// background thread writes it to the Console.Error of the moment it was logged.
// Unconditional: main's queue is always on, so this one is not gated on a perf
// flag. A sink a consumer installed is left alone, as main bypasses its queue for
// a consumer's sink.
//
// THE MARKER. After the level is set, Exports.Init logs one Info line,
// MarkerMessage, through the queue. It carries no BNPERF token, so the harness
// parser ignores it, and it is below the default Warn level, so q1's default
// launch stays quiet. At Verbose it shows that the queued sink is the active one.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Installs <see cref="BnLogQueue"/> as <see cref="BnLog.Sink"/>.</summary>
internal static class BnLogQueuedSink
{
    internal const string MarkerCategory = "BnLog";
    internal const string MarkerMessage = "queued sink active: lines are written by the BlazorNative-LogDrain thread";

    private static readonly object s_lock = new();
    private static BnLogQueue? s_queue;
    private static Action<BnLogLevel, string, string>? s_sink;

    /// <summary>The installed sink, or null before <see cref="Install"/>.</summary>
    internal static Action<BnLogLevel, string, string>? InstalledSink => s_sink;

    /// <summary>Installs the queued sink unless <see cref="BnLog.Sink"/> already holds
    /// another one. Idempotent.</summary>
    /// <returns><see langword="true"/> if the queued sink is the active sink afterwards.</returns>
    internal static bool Install()
    {
        lock (s_lock)
        {
            if (s_sink is null)
            {
                BnLogQueue queue = BnLogQueue.CreateDefault();
                s_queue = queue;
                s_sink = (level, category, message) =>
                    // Console.Error is read here, on the caller's thread, so the line goes to the
                    // writer that was stderr when it was logged, as on main.
                    queue.TryEnqueue(Console.Error, BnLog.FormatLine(level, category, message));
            }
            Action<BnLogLevel, string, string>? current = BnLog.Sink;
            if (current is null) { BnLog.Sink = s_sink; return true; }
            return ReferenceEquals(current, s_sink);
        }
    }

    /// <summary>Waits until every line queued before this call is written, or the
    /// timeout passes. True when nothing was installed.</summary>
    internal static bool Flush(TimeSpan timeout)
    {
        BnLogQueue? queue;
        lock (s_lock) queue = s_queue;
        return queue is null || queue.Flush(timeout);
    }
}
