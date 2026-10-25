using System.Diagnostics;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 18.1, #551 — the positive control under a starved thread pool.
//
// ShutdownQuiescenceTests' positive control completes a held Take Photo call and
// waits for the frame. That frame crosses the thread pool once: the host-call
// TaskCompletionSource runs its continuations asynchronously, and
// NativeShellBridge.InvokeHostCallAsync awaits it with ConfigureAwait(false), so the
// handler resumes on a pool thread before it posts back to the render thread. On the
// Windows runner the test process held its own pool threads in blocking process
// waits, and the frame came more than 10 s late.
//
// THE INVARIANT. The frame path needs one free pool thread, by the product's
// design today, so a starved pool always delays it. The fix therefore keeps the
// TEST SUITE's blocking waits off the pool. BoundedScript.RunBounded refuses a pool
// thread, which GeneratorRunTests pins; the reference fixtures and GeneratorRunTests
// await RunBoundedAsync; and the two positive controls run on a dedicated thread
// through OffPool, since their sleep-poll waits for that same pool hop. This file
// pins the other half: when the pool really is starved, the positive control says
// so, and the frame was late, not lost. If the product stops hopping through the
// pool, the frame arrives under the hold and this fact still passes, by its first
// branch.
//
// WHY ITS OWN COLLECTION. Holding every pool thread stalls every test that runs at
// the same time. A collection with parallelization disabled runs after all the
// parallel ones, alone, so the hold can starve nothing but this fact.
//
// DOES NOT COVER:
//   - the 10 s magnitude seen on Windows; the hold is total, so any budget fails;
//   - which blocking wait starved the pool on the runner. The message reports a
//     starved pool, not its cause;
//   - starvation by I/O completion threads; only worker threads are held.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The collection for <see cref="PoolStarvationTests"/>: it runs alone,
/// after every parallel collection.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PoolStarvationCollection
{
    public const string Name = "pool-starvation";
}

[Collection(PoolStarvationCollection.Name)]
public sealed class PoolStarvationTests
{
    /// <summary>The frame wait under the hold. The hold is total, so a longer wait
    /// would only make the fact slower.</summary>
    private static readonly TimeSpan FrameBudget = TimeSpan.FromSeconds(2);

    /// <summary>How long a probe queued after the hold must stay unrun for the
    /// anchor to hold.</summary>
    private static readonly TimeSpan ProbeWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>Text of the positive control's starvation message.</summary>
    internal const string Starvation = "thread-pool starvation";

    internal const string ArrivedOnRelease = "arrived once the pool was released";

    /// <summary>
    /// PIN, #551: with every worker thread of the pool held on a gate from just before
    /// the completion, ShutdownQuiescenceTests' positive control either gets its frame,
    /// or fails naming thread-pool starvation; and in that case its message reports
    /// that the frame arrived once the pool was released. ANCHOR: a probe queued after
    /// the hold does not run within <see cref="ProbeWindow"/>, so the pool really was
    /// held.
    /// </summary>
    [Fact]
    public void ThePositiveControl_WithEveryPoolThreadHeld_GetsItsFrameOrNamesPoolStarvation_AndTheFrameArrivesOnRelease()
    {
        bool? held = null;
        Exception? ex = Record.Exception(() => { ShutdownQuiescenceTests.RunHeldCall(
            shutdown: false,
            holdPool: () =>
            {
                var hold = new PoolHold();
                held = hold.EveryWorkerHeld(ProbeWindow);
                return hold;
            },
            frameBudget: FrameBudget); });

        Assert.True(held == true, held is null
            ? "anchor: the positive control never reached the hold; it failed before the completion: " + ex
            : $"anchor: a work item queued after the hold ran within {ProbeWindow.TotalMilliseconds} ms, "
              + "so the pool was not starved and this fact shows nothing");
        Assert.True(ex is null || ex.Message.Contains(Starvation, StringComparison.Ordinal),
            "the positive control failed with every pool thread held, and its message does not name "
            + "thread-pool starvation, so a starved pool reads as a BnCameraDemo regression (#551): "
            + ex?.Message);
        Assert.True(ex is null || ex.Message.Contains(ArrivedOnRelease, StringComparison.Ordinal),
            "the positive control named thread-pool starvation, but the frame did not arrive once the "
            + "pool was released, so it was lost, not late: " + ex?.Message);
    }
}

/// <summary>Holds every worker thread of the pool on one gate until disposed. The
/// pool's maximum is capped at its minimum first, so it cannot inject a thread past
/// the gate; disposal opens the gate and restores the maximum.</summary>
internal sealed class PoolHold : IDisposable
{
    private readonly ManualResetEventSlim _gate = new(false);
    private readonly int _maxWorkers;
    private readonly int _maxIo;
    private int _disposed;

    public PoolHold()
    {
        ThreadPool.GetMaxThreads(out _maxWorkers, out _maxIo);
        ThreadPool.GetMinThreads(out int minWorkers, out _);
        int cap = Math.Max(minWorkers, Environment.ProcessorCount);
        if (!ThreadPool.SetMaxThreads(cap, _maxIo))
            throw new InvalidOperationException($"could not cap the thread pool at {cap} workers");
        // More items than threads that can run them: a thread that frees up takes a gate
        // item before anything queued later, so nothing queued after this can run.
        int items = 2 * Math.Max(cap, ThreadPool.ThreadCount);
        for (int i = 0; i < items; i++)
            ThreadPool.UnsafeQueueUserWorkItem(static g => ((ManualResetEventSlim)g!).Wait(), _gate);
    }

    /// <summary>Whether a work item queued now stays unrun for <paramref name="window"/>.</summary>
    public bool EveryWorkerHeld(TimeSpan window)
    {
        var ran = new ManualResetEventSlim(false);
        ThreadPool.UnsafeQueueUserWorkItem(static r => ((ManualResetEventSlim)r!).Set(), ran);
        return !ran.Wait(window);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _gate.Set();
        ThreadPool.SetMaxThreads(_maxWorkers, _maxIo);
    }
}

/// <summary>A work item queued on the thread pool that records how long it waited to
/// run: the pool's queue delay at the moment it was queued (#551).</summary>
internal sealed class PoolProbe
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _ranAtTicks = -1;

    public static PoolProbe Queue()
    {
        var probe = new PoolProbe();
        ThreadPool.UnsafeQueueUserWorkItem(
            static p => Interlocked.Exchange(ref ((PoolProbe)p!)._ranAtTicks, ((PoolProbe)p!)._clock.ElapsedTicks), probe);
        return probe;
    }

    public bool HasRun => Interlocked.Read(ref _ranAtTicks) >= 0;

    /// <summary>The queue delay in milliseconds, or that it has not run yet.</summary>
    public string Describe()
    {
        long ticks = Interlocked.Read(ref _ranAtTicks);
        return ticks < 0
            ? $"not run after {_clock.Elapsed.TotalMilliseconds:F0} ms"
            : $"{ticks * 1000.0 / Stopwatch.Frequency:F1} ms";
    }
}
