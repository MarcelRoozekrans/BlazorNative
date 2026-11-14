using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 18.9 review fix (#710): FaultNoticeTests.TearDown promises that a dispatch which
// overran its budget and is still running does not hang the teardown. It must stop waiting
// after its bound, reset the observer, abandon the session, and throw a TimeoutException that
// names the dispatch. Disposing the renderer would run on the render thread the handler blocks
// and wait for it with no bound, so the teardown would hang and never throw.
//
// The handler here blocks the render thread on a gate that only this fact opens, and nothing
// in TearDown answers it, unlike a held host call, which TearDown completes itself.
//
// The teardown runs on its own thread, joined with an explicit bound, so a regression reds
// with a named message and not as a run that hangs until the CI job timeout.
//
// DOES NOT COVER: a dispatch whose render thread is NOT blocked but which is slow; the
// abandon path is the same one, and the dispose would succeed there.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class TearDownOverrunTests
{
    // Budget for the join inside TearDown, plus room for its remaining steps.
    private static readonly TimeSpan TearDownBound = FaultNoticeTests.Budget + TimeSpan.FromSeconds(15);

    [Fact]
    public Task ADispatchThatOverranAndIsStillRunning_DoesNotHangTearDown_AndTheNextSessionWorks()
        => OffPool.Run(Body);

    private static void Body()
    {
        var frames = new List<RenderFrame>();
        var gate = new ManualResetEventSlim(false);
        GatedProbe.Gate = gate;
        Thread? teardown = null;
        try
        {
            NativeRenderer first = FaultNoticeTests.StartSession(strict: false, frames);
            first.Mount<GatedProbe>();
            int hold = FaultNoticeTests.HandlerFor(frames, "click");

            // The handler blocks the render thread on the gate, so the dispatch overruns.
            var overran = Record.Exception(() => FaultNoticeTests.DispatchBounded(
                hold, """{"name":"click"}""", TimeSpan.FromMilliseconds(300)));
            Assert.True(overran is Xunit.Sdk.XunitException,
                "anchor: the dispatch returned within its budget, so no dispatch overran and this fact tests nothing.");

            Exception? thrown = null;
            teardown = new Thread(() =>
            {
                try { FaultNoticeTests.TearDown([], null); }
                catch (Exception ex) { thrown = ex; }
            })
            { IsBackground = true, Name = "teardown-overrun-probe" };
            teardown.Start();

            Assert.True(teardown.Join(TearDownBound),
                $"TearDown did not finish within {TearDownBound.TotalSeconds:0} s although the only thing running "
                + "was a dispatch that overran its budget: it disposed the session on the render thread that "
                + "dispatch blocks, and so hung before it could throw (#710).");
            var timeout = Assert.IsType<TimeoutException>(thrown);
            Assert.Contains("the dispatch that overran its budget has not returned", timeout.Message);

            // The session was abandoned, so the next one is fresh and works.
            FakeShellHost.Reset();
            NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
            NativeRenderer next = HostSession.EnsureSession();
            Assert.NotSame(first, next);
            var nextFrames = new List<RenderFrame>();
            next.Frames += (f, _) => { lock (nextFrames) nextFrames.Add(f); return ValueTask.CompletedTask; };
            next.Mount<GatedProbe>();
            lock (nextFrames)
                Assert.True(nextFrames.Count > 0, "the session after the abandoned one mounted no frame.");
        }
        finally
        {
            // Opens the gate so the abandoned render thread can exit.
            gate.Set();
            // A TearDown that hung on the dispose resumes once the gate opens, and then resets the
            // bridge itself. Wait for it, bounded, so that reset cannot land in the next fact.
            if (teardown is not null && !teardown.Join(TearDownBound))
                HostSession.AbandonForTests();
            else
                HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }

    /// <summary>A page whose click handler blocks the render thread on <see cref="Gate"/>.</summary>
    private sealed class GatedProbe : ComponentBase
    {
        public static volatile ManualResetEventSlim? Gate;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnButton>(0);
            b.AddComponentParameter(1, nameof(BnButton.Label), "Hold");
            b.AddComponentParameter(2, nameof(BnButton.OnClick),
                EventCallback.Factory.Create<MouseEventArgs>(this, () => Gate?.Wait()));
            b.CloseComponent();
        }
    }
}
