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
//   - Work posted after Shutdown completes its Task as CANCELLED. It is never dropped
//     silently, because anything awaiting a dropped InvokeAsync would hang forever.
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
        if (!TryPost(_ =>
            {
                try { workItem(); tcs.TrySetResult(); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null))
            tcs.TrySetCanceled();
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
        if (!TryPost(async _ =>
            {
                try { await (workItem() ?? Task.CompletedTask); tcs.TrySetResult(); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null))
            tcs.TrySetCanceled();
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
        if (!TryPost(_ =>
            {
                try { tcs.TrySetResult(workItem()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null))
            tcs.TrySetCanceled();
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
        if (!TryPost(async _ =>
            {
                try { tcs.TrySetResult(await workItem()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null))
            tcs.TrySetCanceled();
        return tcs.Task;
    }

    /// <summary>Closes the queue, lets the thread finish what is already queued, and joins
    /// it within <paramref name="joinBudget"/>. Returns whether it joined. Called ON the
    /// render thread it cannot join itself and returns false; the thread still exits once
    /// the queue drains.</summary>
    public bool Shutdown(TimeSpan joinBudget)
    {
        _queue.CompleteAdding();
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
            return false;
        return _thread.Join(joinBudget);
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
