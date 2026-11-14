namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 18.9, #564 — the other-thread probe's dispatch bound under a held pool.
//
// FaultNoticeTests' other-thread probe has a 1 s dispatch bound. Its handler begins
// a shell call on another thread and joins it in its synchronous part. When that
// thread came from the pool, the join put the queue delay of the pool inside the
// bound, and xUnit 2.9 runs test bodies on that same pool, so on the Windows runner
// a busy pool overran it. The handler now begins the call on a dedicated thread.
// This file pins that its synchronous part no longer waits on the pool: with every
// worker thread held, the probe still returns within its bound, with rc 2.
//
// WHY ITS OWN COLLECTION. Holding every pool thread stalls every test that runs at
// the same time. The fact joins PoolStarvationCollection, which has parallelization
// disabled, so it runs after all the parallel collections, alone, and the hold can
// starve nothing but itself.
//
// DOES NOT COVER:
//   - starvation by I/O completion threads; only worker threads are held;
//   - pool independence of any other bounded fact. It proves it for this probe's
//     synchronous part only;
//   - the queue delay on the Windows runner itself. The hold is total, so it shows
//     that the probe needs no pool thread at all, not how late one would have been.
// ─────────────────────────────────────────────────────────────────────────────

[Collection(PoolStarvationCollection.Name)]
public sealed class DispatchBoundPoolTests
{
    /// <summary>How long a probe queued after the hold must stay unrun for the
    /// anchor to hold.</summary>
    private static readonly TimeSpan ProbeWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// PIN, #564: with every worker thread of the pool held, FaultNoticeTests' other-thread
    /// probe still returns within its 1 s dispatch bound and still faults synchronously with
    /// rc 2, so its synchronous part does not wait on the thread pool. ANCHOR: a probe queued
    /// after the hold does not run within <see cref="ProbeWindow"/>, so the pool really was
    /// held.
    /// </summary>
    [Fact]
    public Task TheOtherThreadProbe_WithEveryPoolThreadHeld_ReturnsWithinTheDispatchBound() => OffPool.Run(() =>
    {
        var hold = new PoolHold();
        bool held = hold.EveryWorkerHeld(ProbeWindow);
        (int Rc, List<BlazorNative.Renderer.RenderFrame> Frames, List<Task> Pending, Action<ulong, string, Task>? Previous)? probe = null;
        Exception? ex = Record.Exception(() => { probe = FaultNoticeTests.DispatchProbe(
            "thread-begin-then-throw", release: hold.Dispose); });
        // Free the pool before TearDown, so whatever it waits for can finish even if that needs
        // a pool thread. On an overrun DispatchProbe's own catch already ran TearDown, so it is
        // not run again here.
        hold.Dispose();
        Exception? teardown = ex is null && probe is { } result
            ? Record.Exception(() => FaultNoticeTests.TearDown(result.Pending, result.Previous))
            : null;

        Assert.True(held, $"anchor: a work item queued after the hold ran within {ProbeWindow.TotalMilliseconds} ms, "
            + "so the pool was not held and this fact shows nothing");
        Assert.True(ex is null,
            "the other-thread probe did not return within its 1 s dispatch bound with every pool worker held, "
            + "so its synchronous part waits on the thread pool and the Windows runner's busy pool can overrun it (#564): "
            + ex?.Message);
        Assert.True(probe?.Rc == 2, $"rc was {probe?.Rc}: the probe must still fault synchronously with rc 2 under the hold");
        Assert.True(teardown is null,
            "TearDown timed out after the probe, so something it started was still running and could leak a late "
            + "FaultNotice into the next fact (#564): " + teardown?.Message);
    });
}
