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
// WHY THE SYNCHRONOUS PATH WAS NEVER AFFECTED: a sequential subscriber completes
// inline, so `framesTask.IsCompleted` is true and the GetResult() branch rethrows
// into the surrounding catch — on the correct thread. Only a subscriber whose task
// is still INCOMPLETE when the renderer inspects it reaches the continuation.
//
// ── THE GATE, AND WHY IT IS NOT DECORATION ───────────────────────────────────
// The first version of these tests used `await Task.Yield()` to make the
// subscriber "async". That is NOT a guarantee: it passed locally and FAILED IN
// CI, where the yielded continuation had already run by the time the renderer
// inspected the task — so `IsCompleted` was true, the GetResult() branch ran, and
// the mount threw. The test had been exercising the path that was never broken.
//
// A TaskCompletionSource the test controls removes the race entirely: the
// subscriber's task CANNOT complete until the test releases it, so the renderer is
// guaranteed to see an incomplete task and take the continuation branch. Timing
// assumptions in a test about threading are exactly the wrong place to be lucky.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AsyncFramesFaultTests
{
    private const string FaultMessage = "async subscriber fault";

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

    /// <summary>Polls for the parked fault. The continuation is scheduled, not inline, so
    /// the wait is real — but it is bounded and asserts, never silently gives up.</summary>
    private static async Task<Exception> AwaitParkedFault(NativeRenderer renderer)
    {
        for (int i = 0; i < 200; i++)
        {
            Exception? parked = renderer.AsyncFramesFaultForTests;
            if (parked is not null) return parked;
            await Task.Delay(10);
        }

        Assert.Fail("the async Frames fault was never parked — it was swallowed, which is "
            + "the defect #123 exists to prevent");
        return null!; // unreachable
    }

    /// <summary>
    /// THE REGRESSION PIN. An async Frames subscriber that faults must not take the mount
    /// down, and its fault must be CAPTURED rather than lost.
    ///
    /// <para>On the pre-fix build the continuation called HandleException on a ThreadPool
    /// thread; with StrictErrors that reached <c>ExceptionDispatchInfo.Throw()</c> there. An
    /// exception thrown on a pool thread from a continuation nobody awaits does not reach
    /// this test — it is swallowed, or takes the process down. Neither is "surfaced".</para>
    /// </summary>
    [Fact]
    public async Task AnAsyncFramesSubscriberFault_IsParked_NotThrownOnThePoolThread()
    {
        using var renderer = BuildRenderer();

        // The subscriber cannot complete until this is set — so the renderer is GUARANTEED
        // to see an incomplete task and take the continuation branch.
        var release = new TaskCompletionSource();
        renderer.Frames += (frame, ct) => new ValueTask(FaultWhenReleased());
        async Task FaultWhenReleased()
        {
            await release.Task.ConfigureAwait(false);
            throw new InvalidOperationException(FaultMessage);
        }

        // The mount must SURVIVE: the fault belongs to the subscriber, not the render — and
        // at this point the subscriber has not even faulted yet.
        await renderer.MountAsync<Probe>(ParameterView.Empty);
        Assert.Null(renderer.AsyncFramesFaultForTests);

        release.SetResult(); // now the subscriber faults, on a pool thread

        Exception parked = await AwaitParkedFault(renderer);
        Assert.IsType<InvalidOperationException>(parked);
        Assert.Equal(FaultMessage, parked.Message);
    }

    /// <summary>
    /// The parked fault is routed on the RENDERER thread, at the next frame — under
    /// StrictErrors that means it surfaces at a real boundary a caller is awaiting, which is
    /// the whole point of moving it off the pool thread.
    ///
    /// <para>Also pins the DRAIN: a fault that surfaced once and stayed parked would
    /// re-throw on every subsequent frame, turning one subscriber bug into a permanently
    /// broken renderer.</para>
    /// </summary>
    [Fact]
    public async Task TheParkedFault_SurfacesOnTheNextFrame_AndIsDrained()
    {
        using var renderer = BuildRenderer();

        var release = new TaskCompletionSource();
        ValueTask Handler(RenderFrame frame, CancellationToken ct) => new(FaultWhenReleased());
        async Task FaultWhenReleased()
        {
            await release.Task.ConfigureAwait(false);
            throw new InvalidOperationException(FaultMessage);
        }

        renderer.Frames += Handler;
        int rootId = await renderer.MountAsync<Probe>(ParameterView.Empty);
        release.SetResult();

        await AwaitParkedFault(renderer);

        // Unsubscribe so the NEXT frame cannot fault again — the assertion is about the
        // PARKED fault surfacing, and a second live fault would make the source ambiguous.
        renderer.Frames -= Handler;

        // Strict mode: the drain routes through HandleException, which rethrows the ORIGINAL
        // exception — synchronously, on the thread driving the frame. Unmount is void and
        // synchronous, so the throw arrives right here: exactly the boundary behaviour a
        // pool-thread rethrow could never give.
        var surfaced = Assert.Throws<InvalidOperationException>(() => renderer.Unmount(rootId));
        Assert.Equal(FaultMessage, surfaced.Message);

        Assert.Null(renderer.AsyncFramesFaultForTests);
    }
}
