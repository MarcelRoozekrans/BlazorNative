using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// AsyncFramesFaultTests — #213 item 2.
//
// #123 established the rule: NEVER discard the Frames task. Frames is the
// documented TEST channel, and AsyncEventHandler reports a subscriber fault
// THROUGH the returned task, so discarding it means a throw inside a frame
// subscriber (a failing assertion) is silently swallowed and the test goes green
// while actually failing.
//
// The fix for that satisfied "never discard" and then did the wrong thing with
// what it caught: for an ASYNC subscriber it called HandleException straight from
// a ThreadPool continuation. HandleException reads and writes three fields this
// renderer declares single-threaded — _uiEventDispatchDepth,
// _uiEventDispatchException, _reportedBindingFault — and under StrictErrors it
// rethrows via ExceptionDispatchInfo.Throw() ON THAT POOL THREAD, where nothing
// observes it. So the mechanism built to stop a fault being swallowed could
// corrupt renderer state AND swallow the fault a second way.
//
// The requirement was only ever "capture it". WHERE it is routed is a separate
// question, and the answer is the renderer thread: the fault is parked and
// drained at the top of the next UpdateDisplayAsync.
//
// WHY THE SYNCHRONOUS PATH WAS NEVER AFFECTED, and why that kept the impact low:
// a sequential subscriber completes inline, so `framesTask.IsCompleted` is true
// and the GetResult() branch rethrows into the surrounding catch — on the correct
// thread. Only a genuinely async subscriber reached the continuation, which is
// why no existing test could see this.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AsyncFramesFaultTests
{
    private sealed class Probe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, "probe");
            b.CloseElement();
        }
    }

    /// <summary>Strict, like every other renderer fixture (Task 6 / DoD #9) — and here it
    /// is load-bearing rather than convention: strict mode is precisely the configuration
    /// in which the old code rethrew on the pool thread.</summary>
    private static NativeRenderer BuildRenderer()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true;
        return renderer;
    }

    /// <summary>
    /// THE REGRESSION PIN. An ASYNC Frames subscriber that faults must not take the mount
    /// down, and its fault must be CAPTURED rather than lost.
    ///
    /// <para>On the pre-fix build the continuation called HandleException on a ThreadPool
    /// thread; with StrictErrors that reached <c>ExceptionDispatchInfo.Throw()</c> there.
    /// An exception thrown on a pool thread from a continuation nobody awaits does not
    /// reach this test — it is either swallowed or, depending on host policy, takes the
    /// process down. Neither is "the fault surfaced".</para>
    ///
    /// <para>The assertion is therefore on the PARKED fault, which is the property that
    /// actually matters: not swallowed, not thrown somewhere nobody is looking.</para>
    /// </summary>
    [Fact]
    public async Task AnAsyncFramesSubscriberFault_IsParked_NotThrownOnThePoolThread()
    {
        using var renderer = BuildRenderer();
        var subscriberEntered = new TaskCompletionSource();

        renderer.Frames += async (frame, ct) =>
        {
            subscriberEntered.TrySetResult();
            // Yield so the returned task is genuinely INCOMPLETE when the renderer
            // inspects it — this is what routes through the continuation instead of the
            // inline GetResult() branch. Without the yield this test would exercise the
            // path that was never broken.
            await Task.Yield();
            throw new InvalidOperationException("async subscriber fault");
        };

        // The mount itself must SURVIVE: the fault belongs to the subscriber, not the
        // render. Pre-fix, strict mode could rethrow this on the pool thread instead.
        await renderer.MountAsync<Probe>(ParameterView.Empty);
        await subscriberEntered.Task;

        // The continuation is scheduled, so give it a bounded chance to run rather than
        // assuming a particular scheduling order.
        Exception? parked = null;
        for (int i = 0; i < 100 && parked is null; i++)
        {
            parked = renderer.AsyncFramesFaultForTests;
            if (parked is null) await Task.Delay(10);
        }

        Assert.NotNull(parked);
        Assert.IsType<InvalidOperationException>(parked);
        Assert.Equal("async subscriber fault", parked!.Message);
    }

    /// <summary>
    /// The parked fault is routed on the RENDERER thread, at the next frame — and under
    /// StrictErrors that means it surfaces at a real boundary a caller is awaiting, which
    /// is the whole point of moving it off the pool thread.
    ///
    /// <para>Also asserts the park is DRAINED: a fault that surfaced once and then stayed
    /// parked would re-throw on every subsequent frame, turning one subscriber bug into a
    /// permanently broken renderer.</para>
    /// </summary>
    [Fact]
    public async Task TheParkedFault_SurfacesOnTheNextFrame_AndIsDrained()
    {
        using var renderer = BuildRenderer();
        var faulted = new TaskCompletionSource();

        ValueTask Handler(RenderFrame frame, CancellationToken ct) => new(FaultOnce());
        async Task FaultOnce()
        {
            await Task.Yield();
            faulted.TrySetResult();
            throw new InvalidOperationException("async subscriber fault");
        }

        renderer.Frames += Handler;
        int rootId = await renderer.MountAsync<Probe>(ParameterView.Empty);
        await faulted.Task;

        for (int i = 0; i < 100 && renderer.AsyncFramesFaultForTests is null; i++)
            await Task.Delay(10);
        Assert.NotNull(renderer.AsyncFramesFaultForTests);

        // Unsubscribe so the NEXT frame does not fault again — the assertion is about the
        // PARKED fault surfacing, and a second fault would make the source ambiguous.
        renderer.Frames -= Handler;

        // Strict mode: the drain routes through HandleException, which rethrows the
        // ORIGINAL exception — synchronously, on the thread driving the frame. Unmount is
        // void and synchronous, so the throw arrives right here: exactly the boundary
        // behaviour the pool-thread rethrow could never give.
        var surfaced = Assert.Throws<InvalidOperationException>(() => renderer.Unmount(rootId));
        Assert.Equal("async subscriber fault", surfaced.Message);

        Assert.Null(renderer.AsyncFramesFaultForTests);
    }
}
