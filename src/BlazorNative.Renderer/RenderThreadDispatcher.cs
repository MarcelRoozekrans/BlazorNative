using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using BlazorNative.Core;
using Microsoft.AspNetCore.Components;

namespace BlazorNative.Renderer;

// ─────────────────────────────────────────────────────────────────────────────
// RenderThreadDispatcher — Phase 16.1.
//
// One dedicated background thread per NativeRenderer, with a single-threaded
// SynchronizationContext installed on it, and an HONEST CheckAccess(). It replaces
// the inline dispatcher, whose unconditional `CheckAccess() => true` made every
// export a blocking wait whenever a handler awaited the host (#345), and could not
// be made honest without a real thread to marshal to (13.2's stack overflow).
// The 16.0 spike proved the shape (docs/plans/2026-09-26-phase-16.0-spike-conclusion.md);
// this is its test-first rewrite, with requirements 9 and 10 applied.
//
// Contracts, each pinned by RenderThreadDispatcherTests:
//   - Work from another thread is QUEUED and runs on the render thread; work from the
//     render thread runs inline. An await inside a work item resumes on the render thread.
//   - Work posted after Shutdown completes its Task as CANCELLED. So does work that was
//     already running but suspended on an await when the thread exited: its continuation
//     is posted to a thread that no longer runs anything, so the dispatcher cancels every
//     cross-thread InvokeAsync still pending once the queue has drained and Run() exits,
//     or when Shutdown cannot join the thread. Nothing awaiting an InvokeAsync hangs.
//     A raw SynchronizationContext.Post with no InvokeAsync behind it has no Task to
//     complete; it is dropped with a warning. A dispatch's still-running handler Task is
//     such a case, so the export hands on a TrackUntilShutdown mirror instead, and the
//     mirror is cancelled with the rest.
//   - An exception escaping raw posted work, such as an async void that throws after an
//     await, goes to UnhandledExceptionSink. The default re-raises it as unhandled, so the
//     process terminates exactly as it does without a SynchronizationContext.
//   - SynchronizationContext.Send propagates the callback's exception to its caller.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The renderer's own thread: a queue, one thread draining it, and a
/// single-threaded <see cref="SynchronizationContext"/> on that thread.</summary>
internal sealed class RenderThreadDispatcher : Dispatcher, IDisposable
{
    /// <summary>Where an exception escaping raw posted work goes. The default re-raises it
    /// on a fresh foreground thread, which terminates the process — the same outcome as an
    /// <c>async void</c> that throws after an await with no SynchronizationContext. Tests
    /// replace it to assert that it fired; never swallow here in production.</summary>
    internal static Action<Exception> UnhandledExceptionSink = DefaultSink;

    private readonly BlockingCollection<(SendOrPostCallback Cb, object? State)> _queue = new();
    private readonly Thread _thread;

    /// <summary>Every cross-thread InvokeAsync whose Task is not yet complete, keyed by its
    /// TaskCompletionSource, with the action that cancels it. A work item suspended on an
    /// await stays here until it finishes; if the thread exits first, it is cancelled.</summary>
    private readonly ConcurrentDictionary<object, Action> _pending = new(ReferenceEqualityComparer.Instance);

    public RenderThreadDispatcher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "BlazorNative-Render" };
        _thread.Start();
    }

    /// <summary>The render thread's managed id.</summary>
    public int ThreadId => _thread.ManagedThreadId;

    /// <summary>True once <see cref="Shutdown"/> has closed the queue.</summary>
    public bool IsShutDown => _queue.IsAddingCompleted;

    /// <summary>Test-only: makes <see cref="CheckAccess"/> claim EVERY thread, which is
    /// what the inline dispatcher did. It is the one condition under which a render batch
    /// can reach the tree off the render thread, so it is how the renderer's independent
    /// ownership check (RenderThreadWarningTests) is driven. Never set in production.</summary>
    internal volatile bool ClaimEveryThreadForTests;

    public override bool CheckAccess()
        => Environment.CurrentManagedThreadId == _thread.ManagedThreadId || ClaimEveryThreadForTests;

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new RenderThreadContext(this));
        foreach (var (cb, state) in _queue.GetConsumingEnumerable())
        {
            try
            {
                cb(state);
            }
            catch (Exception ex)
            {
                // Never swallowed (16.0 requirement 10): the spike logged and continued here,
                // which turned an async void crash into a silently lost exception.
                UnhandledExceptionSink(ex);
            }
        }

        // The queue is closed and drained. Any InvokeAsync still pending is suspended on an
        // await whose continuation can no longer run here: complete it as cancelled.
        CancelPending();
    }

    private void Track(object tcs, Action cancel) => _pending[tcs] = cancel;

    private void Untrack(object tcs) => _pending.TryRemove(tcs, out _);

    /// <summary>Set once the dispatcher has started cancelling what is pending, so a Task
    /// tracked after that point is cancelled at once instead of waiting forever.</summary>
    private int _pendingCancelled;

    /// <summary>Returns a mirror of <paramref name="task"/> that completes with its outcome,
    /// or as CANCELLED when this dispatcher shuts down first (decision 5). For a handler
    /// suspended on an await: its continuation is posted to this thread, so once the
    /// thread is gone the handler's own Task never completes, and anything awaiting it
    /// would hang. The export hands the mirror onward, never the raw Task.</summary>
    internal Task TrackUntilShutdown(Task task)
    {
        if (task.IsCompleted)
            return task;

        var mirror = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Track(mirror, () => mirror.TrySetCanceled());
        task.ContinueWith(
            t =>
            {
                Untrack(mirror);
                if (t.IsFaulted)
                    mirror.TrySetException(t.Exception!.InnerExceptions);
                else if (t.IsCanceled)
                    mirror.TrySetCanceled();
                else
                    mirror.TrySetResult();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // Tracked after CancelPending already swept: cancel now. CancelPending sets the
        // flag before it sweeps, so either the sweep sees this entry or this sees the flag.
        if (Volatile.Read(ref _pendingCancelled) != 0)
            CancelPending();
        return mirror.Task;
    }

    private void CancelPending()
    {
        Interlocked.Exchange(ref _pendingCancelled, 1);
        foreach (object key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out Action? cancel))
                cancel();
        }
    }

    /// <summary>Queues raw work. Returns false when the queue is shut down; callers then
    /// complete their Task as cancelled, never drop it.</summary>
    internal bool TryPost(SendOrPostCallback cb, object? state)
    {
        try
        {
            _queue.Add((cb, state));
            return true;
        }
        catch (InvalidOperationException)
        {
            // Adding completed: the queue is shut down.
            return false;
        }
    }

    // The inline branch keeps the old dispatcher's exception shape: a throw becomes a
    // faulted Task rather than escaping InvokeAsync, which is what Blazor callers expect.

    public override Task InvokeAsync(Action workItem)
    {
        if (CheckAccess())
        {
            try { workItem(); return Task.CompletedTask; }
            catch (Exception ex) { return Task.FromException(ex); }
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Track(tcs, () => tcs.TrySetCanceled());
        if (!TryPost(_ =>
            {
                try { workItem(); tcs.TrySetResult(); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { Untrack(tcs); }
            }, null))
        {
            Untrack(tcs);
            tcs.TrySetCanceled();
        }
        return tcs.Task;
    }

    public override Task InvokeAsync(Func<Task> workItem)
    {
        if (CheckAccess())
        {
            try { return workItem() ?? Task.CompletedTask; }
            catch (Exception ex) { return Task.FromException(ex); }
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Track(tcs, () => tcs.TrySetCanceled());
        if (!TryPost(async _ =>
            {
                try { await (workItem() ?? Task.CompletedTask); tcs.TrySetResult(); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { Untrack(tcs); }
            }, null))
        {
            Untrack(tcs);
            tcs.TrySetCanceled();
        }
        return tcs.Task;
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
    {
        if (CheckAccess())
        {
            try { return Task.FromResult(workItem()); }
            catch (Exception ex) { return Task.FromException<TResult>(ex); }
        }

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Track(tcs, () => tcs.TrySetCanceled());
        if (!TryPost(_ =>
            {
                try { tcs.TrySetResult(workItem()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { Untrack(tcs); }
            }, null))
        {
            Untrack(tcs);
            tcs.TrySetCanceled();
        }
        return tcs.Task;
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
    {
        if (CheckAccess())
        {
            try { return workItem(); }
            catch (Exception ex) { return Task.FromException<TResult>(ex); }
        }

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Track(tcs, () => tcs.TrySetCanceled());
        if (!TryPost(async _ =>
            {
                try { tcs.TrySetResult(await workItem()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { Untrack(tcs); }
            }, null))
        {
            Untrack(tcs);
            tcs.TrySetCanceled();
        }
        return tcs.Task;
    }

    /// <summary>Closes the queue, lets the thread finish what is already queued, and joins
    /// it within <paramref name="joinBudget"/>. Returns whether it joined. Called ON the
    /// render thread it cannot join itself and returns false; the thread still exits once
    /// the queue drains, and Run() cancels what is pending then. If the join times out,
    /// because a work item never yields, whatever is still pending is cancelled here, so no
    /// awaiter waits on a thread that may never come back.</summary>
    public bool Shutdown(TimeSpan joinBudget)
    {
        _queue.CompleteAdding();
        // No cancel on the render thread's own shutdown: the work item running it has not
        // finished yet, and cancelling its Task would report a completing dispose as cancelled.
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
            return false;
        bool joined = _thread.Join(joinBudget);
        if (!joined)
            CancelPending();
        return joined;
    }

    public void Dispose() => Shutdown(TimeSpan.FromSeconds(5));

    private static void DefaultSink(Exception ex)
    {
        // Same outcome as main: an exception escaping render work terminates the process.
        var edi = ExceptionDispatchInfo.Capture(ex);
        new Thread(() => edi.Throw()) { IsBackground = false, Name = "BlazorNative-Unhandled" }.Start();
    }

    private sealed class RenderThreadContext(RenderThreadDispatcher owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            if (!owner.TryPost(d, state))
                BnLog.Warn("RenderThread", "continuation posted after shutdown was dropped");
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (owner.CheckAccess())
            {
                d(state);
                return;
            }

            ExceptionDispatchInfo? fault = null;
            using var done = new ManualResetEventSlim();
            if (!owner.TryPost(s =>
                {
                    try { d(s); }
                    catch (Exception ex) { fault = ExceptionDispatchInfo.Capture(ex); }
                    finally { done.Set(); }
                }, state))
                throw new OperationCanceledException("render thread is shut down");
            done.Wait();
            fault?.Throw();
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
