using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RenderThreadDispatcherTests — Phase 16.1.
//
// The renderer's dispatcher used to be INLINE: CheckAccess() answered an
// unconditional true and every work item ran on the calling thread. That made
// every export a blocking wait whenever a handler awaited the host (#345). The
// replacement is a per-renderer render thread with a single-threaded
// SynchronizationContext and an HONEST CheckAccess().
//
// Every thread-identity assertion here compares against THE RENDERER UNDER TEST
// (renderer.RenderThreadId), never a process-wide "last constructed" value — the
// 16.0 spike's static RenderThreadId named another class's renderer under test
// parallelism (spike requirement 9).
//
// DOES NOT COVER (pin standard Rule 5):
//   - the exports' wait-for-the-synchronous-part contract (Task 3, DispatchLaneBlockingTests);
//   - shutdown quiescence of the frame callback (Task 4);
//   - fault delivery to the shell (Task 5);
//   - the production default sink's process termination. It re-raises on a fresh thread,
//     which would kill the test host, so the tests swap the sink and assert it FIRED.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RenderThreadDispatcherTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    private sealed class BoomException(string message) : Exception(message);

    private static NativeRenderer NewRenderer()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true;
        return renderer;
    }

    private static bool Settles(Task task, TimeSpan within)
        => ((IAsyncResult)task).AsyncWaitHandle.WaitOne(within);

    // ── Ownership ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAccess_IsTrueOnlyOnTheRenderThread()
    {
        using var renderer = NewRenderer();

        // Positive control FIRST: the detector can say false. An inline dispatcher answered
        // true here unconditionally, and that is the lie 13.2's stack overflow came from.
        Assert.False(renderer.Dispatcher.CheckAccess(),
            "CheckAccess() answered true on the test thread — the dispatcher is claiming a "
            + "thread it does not own");

        bool inside = await renderer.Dispatcher.InvokeAsync(() => renderer.Dispatcher.CheckAccess());
        Assert.True(inside, "CheckAccess() answered false on the render thread itself");

        Assert.NotEqual(Environment.CurrentManagedThreadId, renderer.RenderThreadId);
    }

    [Fact]
    public async Task InvokeAsync_FromAnotherThread_RunsOnTheRenderThread()
    {
        using var renderer = NewRenderer();
        Dispatcher d = renderer.Dispatcher;
        int expected = renderer.RenderThreadId;
        Assert.NotEqual(Environment.CurrentManagedThreadId, expected);

        // All four overloads: the brief requires every one to marshal, and each is a
        // separate implementation that can drift on its own.
        int viaAction = 0;
        await d.InvokeAsync(() => { viaAction = Environment.CurrentManagedThreadId; });

        int viaFuncTask = 0;
        await d.InvokeAsync(() =>
        {
            viaFuncTask = Environment.CurrentManagedThreadId;
            return Task.CompletedTask;
        });

        int viaFuncResult = await d.InvokeAsync(() => Environment.CurrentManagedThreadId);

        int viaFuncTaskResult = await d.InvokeAsync(() => Task.FromResult(Environment.CurrentManagedThreadId));

        Assert.Equal(expected, viaAction);
        Assert.Equal(expected, viaFuncTask);
        Assert.Equal(expected, viaFuncResult);
        Assert.Equal(expected, viaFuncTaskResult);
    }

    [Fact]
    public void AnAwaitInsideAWorkItem_ResumesOnTheRenderThread()
    {
        using var renderer = NewRenderer();
        int before = 0, after = 0;
        SynchronizationContext? contextAfter = null;

        Task work = renderer.Dispatcher.InvokeAsync(async () =>
        {
            before = Environment.CurrentManagedThreadId;
            // Task.Yield always posts the continuation to the current SynchronizationContext.
            // Without one on the render thread it would resume on the pool, off the thread
            // Blazor's AssertAccess requires.
            await Task.Yield();
            after = Environment.CurrentManagedThreadId;
            contextAfter = SynchronizationContext.Current;
        });

        Assert.True(Settles(work, Budget), "the awaiting work item never completed");
        Assert.Equal(TaskStatus.RanToCompletion, work.Status);
        Assert.Equal(renderer.RenderThreadId, before);
        Assert.Equal(renderer.RenderThreadId, after);
        Assert.NotNull(contextAfter);
    }

    // ── Exceptions ───────────────────────────────────────────────────────────

    private static async void ThrowAfterAnAwait(BoomException boom)
    {
        await Task.Yield();
        throw boom;
    }

    [Fact]
    public async Task AThrowFromPostedWork_ReachesTheUnhandledExceptionSink()
    {
        using var renderer = NewRenderer();
        var boom = new BoomException("thrown after an await in an async void");
        var reached = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<Exception> original = RenderThreadDispatcher.UnhandledExceptionSink;
        try
        {
            RenderThreadDispatcher.UnhandledExceptionSink = ex =>
            {
                if (ReferenceEquals(ex, boom))
                    reached.TrySetResult(ex);
                else
                    original(ex);
            };

            // An async void that throws after its first await: the exception is posted to the
            // captured SynchronizationContext — the render thread's — as a raw work item. On
            // main this crashes the process; the spike logged it and lost it (requirement 10).
            await renderer.Dispatcher.InvokeAsync(() => ThrowAfterAnAwait(boom));

            Assert.True(Settles(reached.Task, Budget),
                "the exception thrown by posted work never reached UnhandledExceptionSink — it was swallowed");
            Assert.Same(boom, await reached.Task);

            // Positive control: the render thread survived the throw and still runs work.
            int after = await renderer.Dispatcher.InvokeAsync(() => Environment.CurrentManagedThreadId);
            Assert.Equal(renderer.RenderThreadId, after);
        }
        finally
        {
            RenderThreadDispatcher.UnhandledExceptionSink = original;
        }
    }

    [Fact]
    public async Task SendPropagatesTheException_ToItsCaller()
    {
        using var renderer = NewRenderer();
        SynchronizationContext context = await renderer.Dispatcher
            .InvokeAsync(() => SynchronizationContext.Current!);

        var boom = new BoomException("thrown inside Send");
        var sinkSaw = new List<Exception>();
        Action<Exception> original = RenderThreadDispatcher.UnhandledExceptionSink;
        try
        {
            // Records only THIS test's exception, and forwards anything else to the original
            // sink: the sink is process-wide, and swallowing another test's fault would hide it.
            RenderThreadDispatcher.UnhandledExceptionSink = ex =>
            {
                if (ReferenceEquals(ex, boom))
                    lock (sinkSaw) sinkSaw.Add(ex);
                else
                    original(ex);
            };

            // Positive control: Send runs the callback on the render thread and returns.
            int ranOn = 0;
            context.Send(_ => ranOn = Environment.CurrentManagedThreadId, null);
            Assert.Equal(renderer.RenderThreadId, ranOn);

            var thrown = Assert.Throws<BoomException>(() => context.Send(_ => throw boom, null));
            Assert.Same(boom, thrown);

            // Propagated to the caller, and ONLY to the caller — not also reported as unhandled.
            lock (sinkSaw)
                Assert.DoesNotContain(boom, sinkSaw);
        }
        finally
        {
            RenderThreadDispatcher.UnhandledExceptionSink = original;
        }
    }

    // ── Shutdown ─────────────────────────────────────────────────────────────

    [Fact]
    public void WorkPostedAfterShutdown_CompletesCancelled_NeverHangs()
    {
        using var renderer = NewRenderer();
        var dispatcher = Assert.IsType<RenderThreadDispatcher>(renderer.Dispatcher);

        // Positive control: before shutdown the same call completes normally, so a cancelled
        // task below is caused by the shutdown and not by the call shape.
        Task before = dispatcher.InvokeAsync(() => { });
        Assert.True(Settles(before, Budget));
        Assert.Equal(TaskStatus.RanToCompletion, before.Status);

        Assert.True(dispatcher.Shutdown(Budget), "the render thread did not join after shutdown");
        Assert.True(dispatcher.IsShutDown);

        Task[] late =
        [
            dispatcher.InvokeAsync(() => { }),
            dispatcher.InvokeAsync(() => Task.CompletedTask),
            dispatcher.InvokeAsync(() => 1),
            dispatcher.InvokeAsync(() => Task.FromResult(1)),
        ];

        for (int i = 0; i < late.Length; i++)
        {
            Assert.True(Settles(late[i], TimeSpan.FromSeconds(1)),
                $"overload {i}: work posted after shutdown never completed — anything awaiting it hangs");
            Assert.True(late[i].IsCanceled, $"overload {i}: expected Canceled, was {late[i].Status}");
        }
    }

    /// <summary>The reviewer's probe, fix round 1: an InvokeAsync whose work item is
    /// suspended on an await when the renderer is disposed. Its continuation can never run —
    /// the render thread has exited — so the returned Task must complete as CANCELLED rather
    /// than wait forever for a continuation the dispatcher drops.</summary>
    [Fact]
    public void AnAwaitInFlightAtShutdown_CompletesCancelled_NeverHangs()
    {
        // Positive control: the same shape WITHOUT the shutdown completes normally once the gate
        // opens, so a cancelled task below is caused by the shutdown, not by the call shape.
        using (var control = NewRenderer())
        {
            var openGate = new TaskCompletionSource();
            Task running = control.Dispatcher.InvokeAsync(async () => await openGate.Task);
            Assert.False(Settles(running, TimeSpan.FromMilliseconds(100)), "the work item did not wait on its gate");
            openGate.SetResult();
            Assert.True(Settles(running, TimeSpan.FromSeconds(2)));
            Assert.Equal(TaskStatus.RanToCompletion, running.Status);
        }

        var gate = new TaskCompletionSource();
        Task inFlight;
        using (var renderer = NewRenderer())
        {
            inFlight = renderer.Dispatcher.InvokeAsync(async () => await gate.Task);
            Assert.False(Settles(inFlight, TimeSpan.FromMilliseconds(100)), "the work item did not wait on its gate");
        }   // Dispose: off the render thread, so it shuts the thread down and joins it

        gate.SetResult();   // its continuation is posted to a render thread that has exited

        Assert.True(Settles(inFlight, TimeSpan.FromSeconds(2)),
            $"an await in flight at shutdown never completed (status {inFlight.Status}) — anything awaiting it hangs");
        Assert.True(inFlight.IsCanceled, $"expected Canceled, was {inFlight.Status}");
    }

    // ── 13.2's regression ────────────────────────────────────────────────────

    /// <summary>13.2's shape: a component whose Dispose re-enters the dispatcher.</summary>
    private sealed class DisposeInvokesProbe : ComponentBase, IDisposable
    {
        public static int Disposed;
        public static int InvokeCompleted;
        public static Exception? InvokeFault;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, "dispose-probe");
            b.CloseElement();
        }

        public void Dispose()
        {
            Interlocked.Increment(ref Disposed);
            Task t = InvokeAsync(StateHasChanged);
            t.ContinueWith(static task =>
            {
                if (task.IsFaulted)
                    Interlocked.CompareExchange(ref InvokeFault, task.Exception!.GetBaseException(), null);
                else
                    Interlocked.Increment(ref InvokeCompleted);
            }, TaskScheduler.Default);
        }
    }

    /// <summary>13.2 measured that an honest CheckAccess() on an INLINE dispatcher recursed
    /// Renderer.Dispose → InvokeAsync → Dispose until the stack ended. With a real render
    /// thread the hop is real, so the loop cannot recur. Named regression, ten runs, on a
    /// dedicated thread with a bounded join — a stack overflow is a failed join or a dead
    /// test host, never a hang.</summary>
    [Fact]
    public void DisposeDuringInvokeAsync_DoesNotRecurse_TenRuns()
    {
        const int Runs = 10;
        DisposeInvokesProbe.Disposed = 0;
        DisposeInvokesProbe.InvokeCompleted = 0;
        DisposeInvokesProbe.InvokeFault = null;

        int completedRuns = 0;
        Exception? failure = null;
        var runner = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < Runs; i++)
                {
                    NativeRenderer renderer = NewRenderer();
                    using (renderer)
                    {
                        int id = renderer.Mount<DisposeInvokesProbe>();

                        // Unmount disposes the probe, whose Dispose re-enters the dispatcher.
                        renderer.Unmount(id);
                        if (Volatile.Read(ref DisposeInvokesProbe.Disposed) != i + 1)
                            throw new InvalidOperationException(
                                $"run {i}: probe disposed {DisposeInvokesProbe.Disposed} time(s), expected {i + 1}");

                        // 13.2's literal loop: Renderer.Dispose() — at the end of this block —
                        // from a thread that is not the render thread, so CheckAccess() is false.
                        if (renderer.Dispatcher.CheckAccess())
                            throw new InvalidOperationException($"run {i}: CheckAccess() true off the render thread");
                    }
                    if (!((RenderThreadDispatcher)renderer.Dispatcher).IsShutDown)
                        throw new InvalidOperationException($"run {i}: Dispose left the render thread running");
                    completedRuns++;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true, Name = "bn-13.2-regression" };
        runner.Start();

        Assert.True(runner.Join(TimeSpan.FromSeconds(30)),
            $"the runner did not finish within 30s (completed {completedRuns}/{Runs})");
        Assert.Null(failure);
        Assert.Equal(Runs, completedRuns);
        Assert.Equal(Runs, Volatile.Read(ref DisposeInvokesProbe.Disposed));

        var settle = Stopwatch.StartNew();
        while (Volatile.Read(ref DisposeInvokesProbe.InvokeCompleted) < Runs
               && DisposeInvokesProbe.InvokeFault is null
               && settle.Elapsed < Budget)
            Thread.Sleep(10);
        Assert.Null(DisposeInvokesProbe.InvokeFault);
        Assert.Equal(Runs, Volatile.Read(ref DisposeInvokesProbe.InvokeCompleted));
    }
}

