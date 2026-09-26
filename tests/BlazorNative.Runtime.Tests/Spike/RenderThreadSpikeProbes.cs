using System.Diagnostics;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using BlazorNative.SampleApp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Runtime.Tests.Spike;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 16.0 — THROWAWAY spike probes (branch spike/16.0-render-thread, never
// merged). They measure G4, G5 and the thread-hop cost against the criteria
// fixed in docs/superpowers/specs/2026-09-26-phase-16.0-design.md.
// Run on the INLINE dispatcher first (the baseline), then on the render thread.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class RenderThreadSpikeProbes
{
    private readonly ITestOutputHelper _output;

    public RenderThreadSpikeProbes(ITestOutputHelper output) => _output = output;

    private static readonly TimeSpan Cleanup = TimeSpan.FromSeconds(10);

    private static int ClickHandlerForLabel(RenderFrame mount, string label)
    {
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == label);
        int buttonNode = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == text.NodeId).ParentId!.Value;
        return Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == buttonNode && p.EventName == "click").HandlerId;
    }

    // ── G5: #345 freed, and ownership holds ──────────────────────────────────

    [Fact]
    public void G5_DispatchReturnsWhileHostCallOpen_AndContinuationRunsOnTheRenderThread()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();

        var returned = new ManualResetEventSlim(false);
        Thread? worker = null;
        var gate = new object();
        var frames = new List<(RenderFrame Frame, int ThreadId)>();
        var frameArrived = new ManualResetEventSlim(false);
        bool completedByProbe = false;

        try
        {
            NativeRenderer renderer = HostSession.EnsureSession();
            renderer.Frames += (f, _) =>
            {
                lock (gate)
                    frames.Add((f, Environment.CurrentManagedThreadId));
                frameArrived.Set();
                return ValueTask.CompletedTask;
            };
            Assert.Equal(0, HostSession.TryMount("BnCameraDemo"));
            RenderFrame mount;
            lock (gate)
            {
                Assert.NotEmpty(frames);
                mount = frames[0].Frame;
            }

            int take = ClickHandlerForLabel(mount, "Take Photo");

            // The device condition: the host call stays open.
            FakeShellHost.AutoCompleteHostCall = false;

            int rc = -1;
            worker = new Thread(() =>
            {
                rc = Exports.DispatchEventCore((ulong)take, """{"name":"click"}""");
                returned.Set();
            })
            { IsBackground = true, Name = "dispatch-probe" };
            worker.Start();

            // THE FLIP of DispatchLaneBlockingTests: the lane must be free.
            Assert.True(returned.Wait(TimeSpan.FromSeconds(1)),
                "dispatch_event did NOT return within 1s while the host call was open "
                + $"(requestId={FakeShellHost.LastHostCallRequestId}) — the dispatch lane is still "
                + "blocked by an async handler (#345).");
            Assert.Equal(0, rc);

            long requestId = FakeShellHost.LastHostCallRequestId;
            Assert.True(requestId >= 0, "the click should have begun a host call");

            int framesBefore;
            lock (gate)
                framesBefore = frames.Count;
            frameArrived.Reset();

            // The user answers the permission sheet: the shell completes the call.
            const string path = "file:///cache/blazornative_captures/spike.jpg";
            int completeRc = NativeShellBridge.CompleteHostCall(
                requestId, (int)CameraStatus.Captured,
                $$"""{"path":"{{path}}","width":"1600","height":"1200","bytes":"204800"}""");
            completedByProbe = true;
            Assert.Equal(0, completeRc);

            // Bounded wait for the continuation's re-render frame (it is asynchronous by
            // design now — the export returned before the handler finished).
            var deadline = Stopwatch.StartNew();
            (RenderFrame Frame, int ThreadId) after = default;
            bool gotFrame = false;
            while (deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                lock (gate)
                {
                    if (frames.Count > framesBefore)
                    {
                        after = frames[^1];
                        gotFrame = true;
                    }
                }
                if (gotFrame)
                    break;
                frameArrived.Wait(TimeSpan.FromMilliseconds(100));
            }
            Assert.True(gotFrame, "no frame was emitted after the host call completed");

            // The continuation's frame carries the captured path.
            Assert.Contains(after.Frame.Patches.OfType<UpdatePropPatch>(),
                p => p.Name == "src" && (string?)p.Value == path);

            int frameThreadId = after.ThreadId;
            _output.WriteLine(
                $"G5 frame thread={frameThreadId} renderThread={NativeRenderer.RenderThreadId?.ToString() ?? "null"} "
                + $"test thread={Environment.CurrentManagedThreadId} worker={worker.ManagedThreadId}");
            Assert.NotEqual(worker.ManagedThreadId, frameThreadId);
            Assert.NotEqual(Environment.CurrentManagedThreadId, frameThreadId);
            Assert.Equal(NativeRenderer.RenderThreadId, frameThreadId);
        }
        finally
        {
            FakeShellHost.AutoCompleteHostCall = true;
            if (!completedByProbe && FakeShellHost.LastHostCallRequestId >= 0)
            {
                NativeShellBridge.CompleteHostCall(
                    FakeShellHost.LastHostCallRequestId, (int)CameraStatus.Cancelled, null);
            }
            returned.Wait(Cleanup);
            worker?.Join(Cleanup);
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }

    // ── G4: 13.2's Dispose → InvokeAsync recursion does not recur ────────────

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

    [Fact]
    public void G4_DisposeDuringInvokeAsync_DoesNotRecurse_TenRuns()
    {
        const string ProbeName = "CompositionProbe";
        const string AwayName = "HelloComponent";
        const int Runs = 10;

        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        DisposeInvokesProbe.Disposed = 0;
        DisposeInvokesProbe.InvokeCompleted = 0;
        DisposeInvokesProbe.InvokeFault = null;

        Func<NativeRenderer, int> original = HostSession.ReplaceRegistryEntryForTests(
            ProbeName, r => r.Mount<DisposeInvokesProbe>());
        int completedRuns = 0;
        Exception? failure = null;
        try
        {
            // On a separate thread with a bounded join, so a stack overflow is a
            // failed join or a crashed test host — never a hang.
            var runner = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < Runs; i++)
                    {
                        HostSession.ResetForTests();
                        HostSession.EnsureSession();
                        if (HostSession.TryMount(ProbeName) != 0)
                            throw new InvalidOperationException($"run {i}: probe mount failed");

                        // The swap path: unmounts the probe → Dispose → InvokeAsync.
                        HostSession.SwapRoot(AwayName);
                        if (Volatile.Read(ref DisposeInvokesProbe.Disposed) != i + 1)
                            throw new InvalidOperationException(
                                $"run {i}: probe disposed {DisposeInvokesProbe.Disposed} time(s), expected {i + 1}");

                        // 13.2's literal loop: Renderer.Dispose() from a thread that is not
                        // the render thread (this runner) — CheckAccess answers false here.
                        HostSession.ResetForTests();
                        completedRuns++;
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }, maxStackSize: 0)
            { IsBackground = true, Name = "g4-runner" };
            runner.Start();

            Assert.True(runner.Join(TimeSpan.FromSeconds(30)),
                $"G4 runner did not finish within 30s (completed {completedRuns}/{Runs})");
            Assert.Null(failure);
            Assert.Equal(Runs, completedRuns);
            Assert.Equal(Runs, Volatile.Read(ref DisposeInvokesProbe.Disposed));

            // Every Dispose's InvokeAsync settled without a fault.
            var settle = Stopwatch.StartNew();
            while (Volatile.Read(ref DisposeInvokesProbe.InvokeCompleted) < Runs
                   && DisposeInvokesProbe.InvokeFault is null
                   && settle.Elapsed < TimeSpan.FromSeconds(5))
                Thread.Sleep(10);
            Assert.Null(DisposeInvokesProbe.InvokeFault);
            Assert.Equal(Runs, Volatile.Read(ref DisposeInvokesProbe.InvokeCompleted));
        }
        finally
        {
            HostSession.ReplaceRegistryEntryForTests(ProbeName, original);
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }

    // ── Timing: the thread hop's cost, recorded not gated ────────────────────

    [Fact]
    [Trait("Category", "Spike")]
    public void Timing_OneThousandSyncDispatches()
    {
        const int N = 1000;
        HostSession.ResetForTests();
        try
        {
            NativeRenderer renderer = HostSession.EnsureSession();
            RenderFrame? first = null;
            renderer.Frames += (f, _) =>
            {
                first ??= f;
                return ValueTask.CompletedTask;
            };
            renderer.Mount<HelloComponent>();
            Assert.NotNull(first);
            AttachEventPatch attach = Assert.Single(
                first!.Patches.OfType<AttachEventPatch>(), p => p.EventName == "click");
            Assert.True(attach.HandlerId > 0);
            ulong handlerId = (ulong)attach.HandlerId;

            var samples = new double[N];
            double tickToUs = 1_000_000.0 / Stopwatch.Frequency;
            for (int i = 0; i < N; i++)
            {
                long start = Stopwatch.GetTimestamp();
                int rc = Exports.DispatchEventCore(handlerId, """{"name":"click"}""");
                long end = Stopwatch.GetTimestamp();
                Assert.Equal(0, rc);
                samples[i] = (end - start) * tickToUs;
            }

            Array.Sort(samples);
            double median = (samples[N / 2 - 1] + samples[N / 2]) / 2.0;
            double p95 = samples[(int)Math.Ceiling(0.95 * N) - 1];
            _output.WriteLine($"SPIKE-TIMING median_us={median:0} p95_us={p95:0} n={N}");
        }
        finally
        {
            HostSession.ResetForTests();
        }
    }
}
