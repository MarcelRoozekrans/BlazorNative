using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 16.1 — shutdown QUIESCES, and teardown neither leaks nor waits under a lock.
//
// Once frames can come from a render-thread continuation rather than only from
// inside a host call, "clear the callback pointer" is no longer enough to make
// blazornative_shutdown safe. Two holes, both 16.0 spike requirements:
//   - requirement 3: a callback already IN FLIGHT when the pointer is cleared still
//     calls the host's trampoline after shutdown returned, and the host may have
//     freed it by then;
//   - requirement 11: a render thread that outlives shutdown keeps rendering, and
//     its frames reach whatever callback the host registers next.
// HostSession.Shutdown closes a counted frame gate and drains it, clears the
// pointer, and joins the render thread, bounded at 5 s. Requirement 4 is the
// teardown half: ResetForTests joins every session's thread, and never waits
// while it holds s_lock.
//
// Every pin drives the REAL function-pointer path: an [UnmanagedCallersOnly]
// callback registered through SetFrameCallback, which is exactly what the shells
// hand blazornative_register_frame_callback. No managed override of the sink was
// needed. blazornative_shutdown is called through its own function pointer too.
//
// The post-shutdown pins RE-REGISTER a counting callback after shutdown returns,
// as a host starting a replacement runtime would. That makes them independent of
// the pointer clear: a late frame from the old session would otherwise be dropped
// by the null pointer alone and the pin could not tell whether anything quiesced.
//
// Every wait is bounded, and every export that can block runs on a worker: a
// regression is a failed assertion, never a hung job.
//
// DOES NOT COVER:
//   - a callback that blocks for longer than the 5 s budget: shutdown, or a
//     re-registration, then returns with it still in flight, and logs a warning;
//   - two re-registrations racing each other;
//   - the host-event arms' pending Tasks: only dispatch_event's pending handler is
//     handed on as a shutdown-tracked mirror;
//   - renderers that tests build directly and never dispose. Only threads owned by
//     HostSession are joined by ResetForTests;
//   - the Kotlin and Swift sides of quiescence.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed unsafe class ShutdownQuiescenceTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    // ── The real frame callbacks ─────────────────────────────────────────────

    private static int s_frames;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CountFrame(BlazorNativeFrame* frame) => Interlocked.Increment(ref s_frames);

    private static readonly ManualResetEventSlim s_callbackEntered = new(false);
    private static readonly ManualResetEventSlim s_callbackRelease = new(false);

    /// <summary>A callback that stays IN FLIGHT until the test releases it.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void BlockingFrame(BlazorNativeFrame* frame)
    {
        s_callbackEntered.Set();
        s_callbackRelease.Wait(TimeSpan.FromSeconds(30));
    }

    private static void RegisterCounter()
    {
        delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void> fn = &CountFrame;
        HostSession.SetFrameCallback((IntPtr)fn);
    }

    private static int Frames => Volatile.Read(ref s_frames);

    /// <summary>blazornative_shutdown, called through its own function pointer.</summary>
    private static void ShutdownExport()
    {
        delegate* unmanaged[Cdecl]<void> shutdown = &Exports.Shutdown;
        shutdown();
    }

    /// <summary>Runs <see cref="ShutdownExport"/> on a worker and waits up to
    /// <paramref name="budget"/>. Returns whether it returned, and how long it took.</summary>
    private static (bool Returned, TimeSpan Elapsed, Thread Worker) ShutdownBounded(TimeSpan budget)
    {
        var done = new ManualResetEventSlim(false);
        var sw = Stopwatch.StartNew();
        var worker = new Thread(() => { ShutdownExport(); done.Set(); })
        { IsBackground = true, Name = "shutdown-probe" };
        worker.Start();
        bool returned = done.Wait(budget);
        sw.Stop();
        return (returned, sw.Elapsed, worker);
    }

    private static Thread RenderThreadOf(NativeRenderer renderer)
    {
        Thread t = renderer.Dispatcher.InvokeAsync(() => Thread.CurrentThread).GetAwaiter().GetResult();
        Assert.Equal(renderer.RenderThreadId, t.ManagedThreadId);
        return t;
    }

    private static bool StopsWithin(Thread t, TimeSpan budget)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < budget)
        {
            if ((t.ThreadState & System.Threading.ThreadState.Stopped) != 0)
                return true;
            Thread.Sleep(10);
        }
        return (t.ThreadState & System.Threading.ThreadState.Stopped) != 0;
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan budget)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < budget)
        {
            if (condition())
                return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    private static int ClickHandlerForLabel(RenderFrame mount, string label)
    {
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == label);
        int buttonNode = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == text.NodeId).ParentId!.Value;
        return Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == buttonNode && p.EventName == "click").HandlerId;
    }

    private static NativeRenderer StartSession(List<RenderFrame> frames)
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        Interlocked.Exchange(ref s_frames, 0);
        RegisterCounter();
        NativeRenderer renderer = HostSession.EnsureSession();
        renderer.Frames += (f, _) => { lock (frames) frames.Add(f); return ValueTask.CompletedTask; };
        return renderer;
    }

    private static void TearDown()
    {
        FakeShellHost.AutoCompleteHostCall = true;
        BlockingProbe.Release.Set();
        s_callbackRelease.Set();
        s_secondRelease.Set();
        HostSession.SetFrameCallback(IntPtr.Zero);
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    // ── 1. A continuation after shutdown never reaches the callback ─────────

    /// <summary>A Take Photo handler suspended on an open host call; the call is
    /// completed only AFTER shutdown (or, for the control, without one).</summary>
    private static Task RunHeldCall(bool shutdown)
    {
        var frames = new List<RenderFrame>();
        var pending = new List<Task>();
        Action<ulong, string, Task>? previousObserver = Exports.PendingDispatchObserver;
        Exports.PendingDispatchObserver = (_, _, t) => { lock (pending) pending.Add(t); };
        long requestId = -1;
        try
        {
            NativeRenderer renderer = StartSession(frames);
            Assert.Equal(0, HostSession.TryMount("BnCameraDemo"));
            // Rule 2 anchor: the counting callback sees frames at all.
            Assert.True(Frames > 0, "mounting BnCameraDemo delivered no frame to the registered callback");
            Thread renderThread = RenderThreadOf(renderer);

            FakeShellHost.AutoCompleteHostCall = false;
            int take;
            lock (frames) take = ClickHandlerForLabel(frames[0], "Take Photo");
            int rc = -1;
            var returned = new ManualResetEventSlim(false);
            new Thread(() => { rc = Exports.DispatchEventCore((ulong)take, ClickArgs); returned.Set(); })
            { IsBackground = true, Name = "dispatch-probe" }.Start();
            Assert.True(returned.Wait(Budget), "dispatch_event did not return while the host call was open");
            Assert.Equal(0, rc);
            requestId = FakeShellHost.LastHostCallRequestId;
            Assert.True(requestId >= 0, "Take Photo never began a host call; has BnCameraDemo's button moved?");
            Task handler;
            lock (pending)
            {
                Assert.True(pending.Count == 1 && !pending[0].IsCompleted,
                    $"expected one suspended handler after Take Photo, got {pending.Count}");
                handler = pending[0];
            }

            if (shutdown)
            {
                ShutdownExport();
                // Quiescence: the handler was suspended, not running, so the join had
                // nothing to wait for. The thread that emits frames is gone.
                Assert.True((renderThread.ThreadState & System.Threading.ThreadState.Stopped) != 0,
                    $"blazornative_shutdown returned while the render thread was still {renderThread.ThreadState}. "
                    + "The session keeps rendering after shutdown, so a continuation can still emit a frame.");
                // A host starting a replacement runtime registers a callback again.
                RegisterCounter();
            }

            int before = Frames;
            NativeShellBridge.CompleteHostCall(requestId, (int)CameraStatus.Cancelled, null);
            requestId = -1;

            if (shutdown)
            {
                Thread.Sleep(500);
                Assert.True(Frames == before,
                    $"{Frames - before} frame(s) reached the frame callback AFTER blazornative_shutdown "
                    + "returned. The held handler's continuation ran on a session that should be quiet.");
            }
            else
            {
                // Rule 3 positive control: the same completion, without shutdown, DOES
                // deliver a frame through the same callback.
                Assert.True(WaitUntil(() => Frames > before, Budget),
                    "completing the held call delivered no frame even without shutdown, so the "
                    + "post-shutdown pin's 'no frame' proves nothing. Has BnCameraDemo stopped re-rendering "
                    + "on a cancelled capture?");
            }
            return handler;
        }
        finally
        {
            if (requestId >= 0)
                NativeShellBridge.CompleteHostCall(requestId, (int)CameraStatus.Cancelled, null);
            if (!shutdown)
            {
                Task[] snapshot;
                lock (pending) snapshot = pending.ToArray();
                Task.WaitAll(snapshot.Select(t => t.ContinueWith(_ => { })).ToArray(), Budget);
            }
            Exports.PendingDispatchObserver = previousObserver;
            TearDown();
        }
    }

    [Fact]
    public void NoFrameReachesTheCallback_AfterShutdownReturns() => RunHeldCall(shutdown: true);

    [Fact]
    public void NoFrameReachesTheCallback_AfterShutdownReturns_PositiveControl() => RunHeldCall(shutdown: false);

    // ── 1b. Decision 5: the pending handler's Task ends CANCELLED at shutdown ─
    //
    // The Task handed to PendingDispatchObserver (and to the interim late-fault
    // logger) is a mirror the dispatcher tracks until shutdown. The handler's own
    // continuation is dropped with the thread, so without the mirror that Task
    // stayed WaitingForActivation forever: measured by the Task 4 review, 3 s after
    // the held call completed.

    [Fact]
    public void APendingHandler_EndsCancelled_WhenItsCallCompletesAfterShutdown()
    {
        Task handler = RunHeldCall(shutdown: true);
        Assert.True(WaitUntil(() => handler.IsCompleted, TimeSpan.FromSeconds(2)),
            $"the pending handler's Task was still {handler.Status} 2 s after its held call completed "
            + "post-shutdown. Anything awaiting it hangs forever: decision 5 says it completes as cancelled.");
        Assert.True(handler.IsCanceled, $"the pending handler's Task ended {handler.Status}, not Canceled");
    }

    [Fact]
    public void APendingHandler_EndsCancelled_WhenItsCallCompletesAfterShutdown_PositiveControl()
    {
        // Rule 3: without shutdown, the same observed Task completes NORMALLY, so the
        // mirror carries the handler's real outcome and the pin above is not reading
        // a Task that is always cancelled.
        Task handler = RunHeldCall(shutdown: false);
        Assert.True(WaitUntil(() => handler.IsCompleted, Budget),
            $"the pending handler's Task never completed without shutdown: {handler.Status}");
        Assert.Equal(TaskStatus.RanToCompletion, handler.Status);
    }

    // ── 2. A handler that never yields: bounded shutdown, and the gate holds ─

    /// <summary>Mounts <see cref="BlockingProbe"/> and dispatches its click on a worker,
    /// returning once the handler is blocked on the render thread.</summary>
    private static (Thread Worker, Func<int> Rc, Thread RenderThread) HoldTheRenderThread(List<RenderFrame> frames)
    {
        NativeRenderer renderer = StartSession(frames);
        BlockingProbe.Reset();
        renderer.Mount<BlockingProbe>();
        Thread renderThread = RenderThreadOf(renderer);
        int click;
        lock (frames) click = ClickHandlerForLabel(frames[0], "block");

        int rc = -1;
        var worker = new Thread(() => rc = Exports.DispatchEventCore((ulong)click, ClickArgs))
        { IsBackground = true, Name = "dispatch-probe" };
        worker.Start();
        Assert.True(BlockingProbe.Entered.Wait(Budget), "the blocking handler never started");
        return (worker, () => rc, renderThread);
    }

    private static void RunNeverYields(bool shutdown)
    {
        var frames = new List<RenderFrame>();
        Thread? worker = null;
        try
        {
            (worker, Func<int> rc, Thread renderThread) = HoldTheRenderThread(frames);

            if (shutdown)
            {
                // 6 s: the 5 s join budget plus slack. A regression to an unbounded join
                // is a failed assertion here, not a hung job.
                var (returned, elapsed, _) = ShutdownBounded(TimeSpan.FromSeconds(6));
                Assert.True(returned,
                    $"blazornative_shutdown did not return within 6 s ({elapsed.TotalSeconds:0.0} s) while a "
                    + "handler held the render thread. The join is unbounded.");
                // Decision 5: the export waiting on the handler's synchronous part is
                // cancelled when the join gives up; it does not wait for a thread that may
                // never come back.
                Assert.True(worker.Join(Budget),
                    "the dispatch export was still blocked after shutdown gave up on the render thread");
                Assert.Equal(2, rc());
                RegisterCounter();
            }

            int before = Frames;
            BlockingProbe.Release.Set();
            // Anchor: the handler really returned and re-rendered, so a frame WAS produced.
            Assert.True(BlockingProbe.Returned.Wait(Budget), "the blocking handler never returned after release");

            if (shutdown)
            {
                Assert.True(StopsWithin(renderThread, Budget),
                    $"the render thread was still {renderThread.ThreadState} after its last work item "
                    + "finished. Shutdown did not close its queue.");
                Assert.True(Frames == before,
                    $"{Frames - before} frame(s) from the handler that outlived blazornative_shutdown "
                    + "reached the callback registered after it. The frame gate is not closed.");
            }
            else
            {
                // Rule 3 positive control: without shutdown, the handler's re-render DOES
                // reach the callback.
                Assert.True(WaitUntil(() => Frames > before, Budget),
                    "releasing the handler delivered no frame even without shutdown, so the "
                    + "post-shutdown 'no frame' assertion proves nothing. Has BlockingProbe stopped re-rendering?");
            }
        }
        finally
        {
            BlockingProbe.Release.Set();
            worker?.Join(Budget);
            TearDown();
        }
    }

    [Fact]
    public void Shutdown_ReturnsWithinBudget_WhenAHandlerNeverYields() => RunNeverYields(shutdown: true);

    [Fact]
    public void Shutdown_ReturnsWithinBudget_WhenAHandlerNeverYields_PositiveControl() => RunNeverYields(shutdown: false);

    // ── 3. The gate drains a callback already in flight ─────────────────────

    [Fact]
    public void Shutdown_WaitsForACallbackAlreadyInFlight()
    {
        // The frame sink is invoked from a worker, not the render thread, so the join
        // cannot be what holds shutdown: only the gate's drain can. In production every
        // frame comes from the render thread, where the join covers it too; this
        // separates the two mechanisms so each is pinned on its own.
        var frames = new List<RenderFrame>();
        s_callbackEntered.Reset();
        s_callbackRelease.Reset();
        Thread? sinkWorker = null;
        Thread? shutdownWorker = null;
        try
        {
            NativeRenderer renderer = StartSession(frames);
            Assert.Equal(0, HostSession.TryMount("HelloComponent"));
            RenderFrame frame;
            lock (frames) frame = Assert.Single(frames);
            Action<RenderFrame> sink = renderer.FrameSink
                ?? throw new Xunit.Sdk.XunitException("EnsureSession installed no FrameSink");

            delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void> blocking = &BlockingFrame;
            HostSession.SetFrameCallback((IntPtr)blocking);
            sinkWorker = new Thread(() => sink(frame)) { IsBackground = true, Name = "sink-probe" };
            sinkWorker.Start();
            Assert.True(s_callbackEntered.Wait(Budget), "the in-flight callback never started");

            (bool returned, _, shutdownWorker) = ShutdownBounded(TimeSpan.FromMilliseconds(500));
            Assert.False(returned,
                "blazornative_shutdown returned while a frame callback was still IN FLIGHT. The host may "
                + "free its trampoline once shutdown returns, so this is a use-after-free.");

            // Rule 3 positive control, through the same detector: once the callback
            // leaves, shutdown does return.
            s_callbackRelease.Set();
            Assert.True(shutdownWorker.Join(Budget),
                "blazornative_shutdown never returned after the in-flight callback left");
        }
        finally
        {
            s_callbackRelease.Set();
            sinkWorker?.Join(Budget);
            shutdownWorker?.Join(Budget);
            TearDown();
        }
    }

    // ── 3b. Re-registration waits out callbacks that may hold the OLD pointer ─
    //
    // Android Activity recreation re-registers the frame callback WITHOUT shutdown,
    // and the old JNA callback object becomes collectable once registration returns.
    // So SetFrameCallback must not return while a callback holding the old pointer is
    // still in flight. The Task 4 review measured it returning in 0.0 ms.
    //
    // As in 3, the sink is invoked from workers so no render-thread join can stand in
    // for the gate. DOES NOT COVER: two registrations racing each other, and an old
    // callback in flight for longer than the 5 s budget, after which registration
    // returns with a warning.

    private static readonly ManualResetEventSlim s_secondEntered = new(false);
    private static readonly ManualResetEventSlim s_secondRelease = new(false);

    /// <summary>A second in-flight callback, independent of <see cref="BlockingFrame"/>.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SecondBlockingFrame(BlazorNativeFrame* frame)
    {
        s_secondEntered.Set();
        s_secondRelease.Wait(TimeSpan.FromSeconds(30));
    }

    /// <summary>Re-registers the counting callback from INSIDE a frame callback.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReRegisteringFrame(BlazorNativeFrame* frame) => RegisterCounter();

    /// <summary>Mounts HelloComponent and returns its sink and first frame, so a test
    /// can drive the sink from its own threads.</summary>
    private static (Action<RenderFrame> Sink, RenderFrame Frame) SinkAndFrame()
    {
        var frames = new List<RenderFrame>();
        NativeRenderer renderer = StartSession(frames);
        Assert.Equal(0, HostSession.TryMount("HelloComponent"));
        RenderFrame frame;
        lock (frames) frame = Assert.Single(frames);
        return (renderer.FrameSink ?? throw new Xunit.Sdk.XunitException("EnsureSession installed no FrameSink"),
            frame);
    }

    private static Thread Start(string name, ThreadStart body)
    {
        var t = new Thread(body) { IsBackground = true, Name = name };
        t.Start();
        return t;
    }

    private static void SetBlocking()
    {
        delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void> fn = &BlockingFrame;
        HostSession.SetFrameCallback((IntPtr)fn);
    }

    private static void SetSecondBlocking()
    {
        delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void> fn = &SecondBlockingFrame;
        HostSession.SetFrameCallback((IntPtr)fn);
    }

    [Fact]
    public void SetFrameCallback_DoesNotReturn_WhileAnOldCallbackIsInFlight()
    {
        s_callbackEntered.Reset();
        s_callbackRelease.Reset();
        Thread? sinkWorker = null, register = null;
        try
        {
            var (sink, frame) = SinkAndFrame();
            SetBlocking();
            sinkWorker = Start("sink-probe", () => sink(frame));
            Assert.True(s_callbackEntered.Wait(Budget), "the old callback never started");

            using var registered = new ManualResetEventSlim(false);
            register = Start("register-probe", () => { RegisterCounter(); registered.Set(); });
            Assert.False(registered.Wait(TimeSpan.FromMilliseconds(500)),
                "SetFrameCallback returned while a callback holding the OLD pointer was still in flight. "
                + "A host that frees its old callback on re-registration, as Android Activity recreation "
                + "does, is then inside freed memory.");

            // Rule 3, through the same detector: once the old callback leaves, it returns.
            s_callbackRelease.Set();
            Assert.True(registered.Wait(Budget), "SetFrameCallback never returned after the old callback left");
        }
        finally
        {
            s_callbackRelease.Set();
            sinkWorker?.Join(Budget);
            register?.Join(Budget);
            TearDown();
        }
    }

    [Fact]
    public void SetFrameCallback_ReturnsPromptly_WithNoCallbackInFlight_PositiveControl()
    {
        // Rule 3: registration is not simply always slow, so the red above means "waited".
        try
        {
            SinkAndFrame();
            var sw = Stopwatch.StartNew();
            RegisterCounter();
            Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500),
                $"SetFrameCallback took {sw.ElapsedMilliseconds} ms with nothing in flight");
        }
        finally
        {
            TearDown();
        }
    }

    [Fact]
    public void SetFrameCallback_IsNotStarved_ByANewCallbackInFlight()
    {
        // The two-slot epoch: a callback that entered AFTER the swap holds the NEW
        // pointer, so registration must not wait for it. A single counter would.
        s_callbackEntered.Reset();
        s_callbackRelease.Reset();
        s_secondEntered.Reset();
        s_secondRelease.Reset();
        Thread? oldWorker = null, newWorker = null, register = null;
        try
        {
            var (sink, frame) = SinkAndFrame();
            SetBlocking();
            oldWorker = Start("old-sink-probe", () => sink(frame));
            Assert.True(s_callbackEntered.Wait(Budget), "the old callback never started");

            using var registered = new ManualResetEventSlim(false);
            register = Start("register-probe", () => { SetSecondBlocking(); registered.Set(); });
            // The swap happens before the wait; start the new entry only once it has.
            delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void> second = &SecondBlockingFrame;
            IntPtr secondPtr = (IntPtr)second;
            FieldInfo? slot = typeof(HostSession).GetField("s_frameCallback", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.True(slot is not null,
                "HostSession.s_frameCallback was not found; this pin reads it by name to know the swap happened. "
                + "Re-point it deliberately.");
            Assert.True(WaitUntil(() => (IntPtr)slot!.GetValue(null)! == secondPtr, Budget),
                "SetFrameCallback never swapped the pointer before waiting");
            newWorker = Start("new-sink-probe", () => sink(frame));
            Assert.True(s_secondEntered.Wait(Budget), "the new callback never started");
            Assert.False(registered.IsSet, "registration returned while the old callback was in flight");

            s_callbackRelease.Set();
            Assert.True(registered.Wait(TimeSpan.FromSeconds(2)),
                "SetFrameCallback kept waiting after the old callback left, for a callback that entered "
                + "AFTER the swap and holds the NEW pointer. A steady stream of new frames would starve it.");
        }
        finally
        {
            s_callbackRelease.Set();
            s_secondRelease.Set();
            oldWorker?.Join(Budget);
            newWorker?.Join(Budget);
            register?.Join(Budget);
            TearDown();
        }
    }

    [Fact]
    public void SetFrameCallback_FromInsideACallback_DoesNotDeadlock()
    {
        Thread? sinkWorker = null;
        try
        {
            var (sink, frame) = SinkAndFrame();
            delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void> fn = &ReRegisteringFrame;
            HostSession.SetFrameCallback((IntPtr)fn);

            using var done = new ManualResetEventSlim(false);
            sinkWorker = Start("sink-probe", () => { sink(frame); done.Set(); });
            // 2 s, under the 5 s budget: a registration waiting on its own callback would
            // time out at 5 s, and this reds before that.
            Assert.True(done.Wait(TimeSpan.FromSeconds(2)),
                "a callback that re-registers the frame callback did not return within 2 s: registration "
                + "waited for the very callback it was called from");

            // Anchor: the re-registration really happened, so the next frame is counted.
            int before = Frames;
            sink(frame);
            Assert.True(Frames > before, "the callback's re-registration never took effect");
        }
        finally
        {
            sinkWorker?.Join(Budget);
            TearDown();
        }
    }

    // ── 4. What EnsureSession does after Shutdown ───────────────────────────
    //
    // Measured before choosing: with the session left in place, the renderer whose
    // thread shutdown joined stays the session, and the next TryMount, dispatch_event
    // and host_event each answer rc 2, for the life of the process. Before 16.1 a
    // mount after shutdown worked on the same session. Detaching keeps it working,
    // on a fresh one.

    [Fact]
    public void TryMount_AfterShutdown_BuildsAFreshSession()
    {
        var frames = new List<RenderFrame>();
        try
        {
            NativeRenderer first = StartSession(frames);
            Assert.Equal(0, HostSession.TryMount("HelloComponent"));
            int firstThread = first.RenderThreadId;

            ShutdownExport();
            Assert.True(HostSession.CurrentRenderer is null,
                "blazornative_shutdown left its renderer as the session. That renderer's thread is gone, "
                + "so every later mount, dispatch and host event answers rc 2 for the life of the process "
                + "(measured in Task 4). Shutdown must detach the session.");

            RegisterCounter();
            int before = Frames;
            Assert.Equal(0, HostSession.TryMount("HelloComponent"));
            NativeRenderer second = HostSession.CurrentRenderer!;
            Assert.NotSame(first, second);
            Assert.NotEqual(firstThread, second.RenderThreadId);
            Assert.True(Frames > before, "the fresh session's mount delivered no frame");
        }
        finally
        {
            TearDown();
        }
    }

    // ── 5. ResetForTests joins every session's thread ───────────────────────

    [Fact]
    public void ResetForTests_JoinsTheRenderThread_AndLeaksNone()
    {
        var threads = new List<Thread>();
        try
        {
            for (int i = 0; i < 5; i++)
            {
                HostSession.ResetForTests();
                Thread t = RenderThreadOf(HostSession.EnsureSession());
                // Rule 3 positive control: the detector sees a LIVE thread as not stopped.
                Assert.False((t.ThreadState & System.Threading.ThreadState.Stopped) != 0,
                    "a live session's render thread already reads as Stopped, so the assertion below "
                    + "could not tell a leak from a join");
                HostSession.ResetForTests();
                threads.Add(t);
            }

            // Rule 2 anchor: five sessions, five distinct threads.
            Assert.Equal(5, threads.Select(t => t.ManagedThreadId).Distinct().Count());

            var sw = Stopwatch.StartNew();
            List<Thread> leaked = threads
                .Where(t => !StopsWithin(t, TimeSpan.FromSeconds(5) - sw.Elapsed))
                .ToList();
            Assert.True(leaked.Count == 0,
                $"{leaked.Count} of 5 render threads were still alive 5 s after ResetForTests: "
                + string.Join(", ", leaked.Select(t => $"{t.ManagedThreadId}={t.ThreadState}"))
                + ". Reset does not join the session's thread.");
        }
        finally
        {
            HostSession.ResetForTests();
        }
    }

    // ── 6. ResetForTests never waits while it holds s_lock ──────────────────

    /// <summary>Reads <c>HostSession.Components</c> on a fresh thread, with its view
    /// dropped first so the read must take <c>s_lock</c>. Returns whether it completed
    /// within one second.</summary>
    private static bool LockedReadCompletesWithinOneSecond()
    {
        using var done = new ManualResetEventSlim(false);
        var reader = new Thread(() => { _ = HostSession.RegisteredComponentsForTests.Count; done.Set(); })
        { IsBackground = true, Name = "lock-probe" };
        reader.Start();
        return done.Wait(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ResetForTests_DoesNotWaitWhileHoldingTheSessionLock()
    {
        var frames = new List<RenderFrame>();
        Thread? worker = null;
        Thread? resetter = null;
        try
        {
            (worker, _, _) = HoldTheRenderThread(frames);

            // Drop the materialized view so the probe read below must take s_lock.
            HostSession.ResetComponentsViewForTests();

            resetter = new Thread(HostSession.ResetForTests) { IsBackground = true, Name = "reset-probe" };
            resetter.Start();
            // The reset is now waiting for the held render thread. Wait until it blocks,
            // wherever it blocks, before probing the lock.
            Assert.True(WaitUntil(() => (resetter.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                Budget), "ResetForTests never blocked on the held render thread");
            Thread.Sleep(100);

            Assert.True(LockedReadCompletesWithinOneSecond(),
                "a Components read that needs s_lock did not complete within 1 s while ResetForTests was "
                + "waiting for a held render thread. The reset waits WHILE HOLDING the session lock, so any "
                + "render-thread work that needs the lock deadlocks the teardown.");
        }
        finally
        {
            BlockingProbe.Release.Set();
            resetter?.Join(Budget);
            worker?.Join(Budget);
            TearDown();
        }
    }

    [Fact]
    public void ResetForTests_DoesNotWaitWhileHoldingTheSessionLock_PositiveControl()
    {
        // Rule 3: the probe read DOES stall when s_lock is really held, so the pin above
        // can see a lock held across the reset. Rule 4: s_lock is reached by name, and
        // its absence reds rather than passing vacuously.
        FieldInfo? field = typeof(HostSession).GetField("s_lock", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field is not null,
            "HostSession.s_lock was not found. The lock pin's control reaches it by name; it moved or was "
            + "renamed, so re-point this control deliberately.");
        object gate = field!.GetValue(null)!;

        HostSession.ResetComponentsViewForTests();
        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var holder = new Thread(() =>
        {
            lock (gate)
            {
                held.Set();
                release.Wait(Budget);
            }
        })
        { IsBackground = true, Name = "lock-holder" };
        holder.Start();
        try
        {
            Assert.True(held.Wait(Budget), "the lock holder never took s_lock");
            Assert.False(LockedReadCompletesWithinOneSecond(),
                "the probe read completed while s_lock was held, so it does not need the lock and the "
                + "lock pin above proves nothing");
        }
        finally
        {
            release.Set();
            holder.Join(Budget);
        }
    }

    // ── Probe ────────────────────────────────────────────────────────────────

    /// <summary>"block" runs a SYNCHRONOUS handler that blocks the render thread until
    /// the test releases it, then changes its text so the re-render emits a frame.
    /// Static slots are safe under the "host-session" collection.</summary>
    private sealed class BlockingProbe : ComponentBase
    {
        public static readonly ManualResetEventSlim Entered = new(false);
        public static readonly ManualResetEventSlim Release = new(false);
        public static readonly ManualResetEventSlim Returned = new(false);

        private int _clicks;

        public static void Reset()
        {
            Entered.Reset();
            Release.Reset();
            Returned.Reset();
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () =>
            {
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
                _clicks++;
                Returned.Set();
            }));
            b.AddContent(2, _clicks == 0 ? "block" : $"blocked {_clicks}");
            b.CloseElement();
        }
    }
}
