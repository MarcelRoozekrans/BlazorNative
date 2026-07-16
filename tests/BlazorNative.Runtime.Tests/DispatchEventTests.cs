using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.2 Task 2 — blazornative_dispatch_event contract, in-process.
//
// Exercises Exports.DispatchEventCore (the managed core the
// [UnmanagedCallersOnly] wrapper delegates to — same split as
// fetch_complete → CompleteFetch) against the REAL HostSession renderer:
//
//   0 = dispatched (INCLUDING stale handlerId — at-most-once delivery; the
//       renderer already catches the ArgumentException + logs)
//   1 = no session / nothing mounted
//   2 = dispatch faulted — the handler, the resulting re-render, or frame
//       delivery threw (detail ex.ToString() on stderr)
//   3 = malformed / NULL args JSON (incl. handlerId beyond int range)
//
// Synchronous contract: the handler, the re-render, AND frame delivery all
// complete before the core returns (InlineDispatcher) — the tests assert the
// re-render frame is ALREADY captured when the call returns, no waiting.
//
// State note: HostSession is a process-wide singleton; every test here resets
// it (ResetForTests) and the class shares the "host-session" collection with
// HostSessionTests so the statics never race across classes.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class DispatchEventTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    /// <summary>Fresh session renderer with every frame captured from the
    /// FIRST render on (subscription happens before mount — the renderer is
    /// born here via EnsureSession, not inside TryMount).</summary>
    private static (NativeRenderer Renderer, List<RenderFrame> Frames) CreateCapturingSession()
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        return (renderer, frames);
    }

    private static int HarvestHandlerId(RenderFrame firstFrame, string eventName)
    {
        AttachEventPatch attach = Assert.Single(
            firstFrame.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == eventName);
        Assert.True(attach.HandlerId > 0, "runtime-assigned handlerId must be positive");
        return attach.HandlerId;
    }

    private static string CaptureStderr(Action action)
    {
        TextWriter original = Console.Error;
        using var capture = new StringWriter();
        Console.SetError(capture);
        try { action(); }
        finally { Console.SetError(original); }
        return capture.ToString();
    }

    // ── rc 0: real dispatch ───────────────────────────────────────────────────

    [Fact]
    public void Dispatch_Click_IncrementsCounter_AndFiresReRenderFrame()
    {
        var (renderer, frames) = CreateCapturingSession();
        renderer.Mount<HelloComponent>();
        int handlerId = HarvestHandlerId(frames[0], "click");

        int rc = Exports.DispatchEventCore((ulong)handlerId, ClickArgs);

        Assert.Equal(0, rc);
        // Synchronous contract: the re-render frame is already here — the
        // handler ran, the counter mutated, and the frame was delivered
        // before DispatchEventCore returned.
        Assert.True(frames.Count >= 2,
            $"expected the re-render frame to be delivered synchronously, got {frames.Count} frame(s)");
        // Single-with-predicate: exactly ONE text patch carries the updated
        // counter (its nodeId is pinned against the mount frame below); any
        // other text patches Hello's re-render might produce are ignored by
        // the predicate rather than failing the count.
        ReplaceTextPatch reRenderText = Assert.Single(
            frames[1].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text.Contains("taps: 1"));
        // Gate 3 lesson: the re-render ReplaceText must target the SAME node
        // the mount frame created for the counter text. Before the Phase 3.2
        // ProcessTextEdit fix, UpdateText resolved by the diff's relative
        // SiblingIndex and hit the OUTER div (node 1) — the text-only
        // assertion above stayed green while every real widget host silently
        // dropped the update (Android: (nodes[1] as? TextView) == null).
        ReplaceTextPatch mountText = Assert.Single(
            frames[0].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text.Contains("taps: 0"));
        Assert.Equal(mountText.NodeId, reRenderText.NodeId);
    }

    [Fact]
    public void Dispatch_Change_BuildsChangeEventArgs()
    {
        var (renderer, frames) = CreateCapturingSession();
        ChangeProbe.LastValue = null;
        renderer.Mount<ChangeProbe>();
        int handlerId = HarvestHandlerId(frames[0], "change");

        int rc = Exports.DispatchEventCore(
            (ulong)handlerId,
            /*lang=json*/ """{"name":"change","payload":"hello"}""");

        Assert.Equal(0, rc);
        Assert.Equal("hello", ChangeProbe.LastValue);
    }

    // ── rc 0: stale handler (at-most-once contract) ───────────────────────────

    [Fact]
    public void Dispatch_StaleHandler_Returns0_Logged()
    {
        var (renderer, frames) = CreateCapturingSession();
        renderer.Mount<HelloComponent>();
        Assert.NotEmpty(frames);

        int rc = 999; // sentinel
        string stderr = CaptureStderr(() =>
            rc = Exports.DispatchEventCore(999999UL, ClickArgs));

        // A stale tap is NOT an error: delivery is at-most-once, the renderer
        // catches the ArgumentException from Blazor's handler table and logs.
        Assert.Equal(0, rc);
        Assert.Contains("stale handler", stderr);
    }

    // ── rc 1: no session ──────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_NoSession_Returns1()
    {
        HostSession.ResetForTests();

        int rc = Exports.DispatchEventCore(1UL, ClickArgs);

        Assert.Equal(1, rc);
    }

    // ── rc 2: handler threw ───────────────────────────────────────────────────

    [Fact]
    public void Dispatch_HandlerThrows_Returns2_WithVisibleDetail()
    {
        var (renderer, frames) = CreateCapturingSession();
        renderer.Mount<ThrowingClickProbe>();
        int handlerId = HarvestHandlerId(frames[0], "click");

        int rc = 999; // sentinel
        string stderr = CaptureStderr(() =>
            rc = Exports.DispatchEventCore((ulong)handlerId, ClickArgs));

        Assert.Equal(2, rc);
        // ex.ToString() lands on stderr — the type + message must be visible
        // so a device-side handler crash is diagnosable from logcat.
        Assert.Contains("boom-click", stderr);
        Assert.Contains(nameof(InvalidOperationException), stderr);
    }

    [Fact]
    public void Dispatch_NestedDispatchInsideHandler_OuterThrowStillReturns2()
    {
        // Pins the depth-counter fix in NativeRenderer's capture window: a
        // handler that ITSELF dispatches (nested DispatchUiEventAsync) must
        // not discard the outer capture — the outer handler's throw still
        // maps to rc 2. (With a boolean window flag, the inner dispatch
        // closed the window and reset the slot → outer throw yielded rc 0.)
        var (renderer, frames) = CreateCapturingSession();
        NestedDispatchProbe.Renderer = renderer;
        NestedDispatchProbe.InnerRan = false;
        renderer.Mount<NestedDispatchProbe>();
        int outerHandlerId = HarvestHandlerId(frames[0], "click");
        NestedDispatchProbe.InnerHandlerId = HarvestHandlerId(frames[0], "change");

        int rc = 999; // sentinel
        string stderr = CaptureStderr(() =>
            rc = Exports.DispatchEventCore((ulong)outerHandlerId, ClickArgs));

        Assert.Equal(2, rc);
        Assert.True(NestedDispatchProbe.InnerRan, "the nested (inner) dispatch should have run");
        Assert.Contains("outer-boom", stderr);
    }

    // ── rc 3: malformed / NULL args ───────────────────────────────────────────

    [Fact]
    public void Dispatch_HandlerIdBeyondIntRange_Returns3()
    {
        // Silent (int) truncation could alias onto a LIVE handler and
        // dispatch the wrong event — the export rejects it as malformed.
        var (renderer, frames) = CreateCapturingSession();
        renderer.Mount<HelloComponent>();
        Assert.NotEmpty(frames);

        int rc = Exports.DispatchEventCore((ulong)int.MaxValue + 1, ClickArgs);

        Assert.Equal(3, rc);
    }

    [Theory]
    [InlineData(null)]                      // NULL args pointer → null string
    [InlineData("")]                        // empty
    [InlineData("not json at all")]         // garbage
    [InlineData("{\"name\":")]              // truncated
    [InlineData("{\"payload\":\"x\"}")]     // valid JSON but no "name"
    public void Dispatch_MalformedArgs_Returns3(string? argsJson)
    {
        // Session mounted so rc 3 is unambiguously "bad args", not "no session".
        var (renderer, frames) = CreateCapturingSession();
        renderer.Mount<HelloComponent>();
        int handlerId = HarvestHandlerId(frames[0], "click");

        int rc = Exports.DispatchEventCore((ulong)handlerId, argsJson);

        Assert.Equal(3, rc);
    }

    // ── Probe components ──────────────────────────────────────────────────────

    private sealed class ChangeProbe : ComponentBase
    {
        /// <summary>Static capture slot — safe because the "host-session"
        /// collection serializes every test that mounts this probe.</summary>
        public static string? LastValue;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "input");
            b.AddAttribute(1, "onchange",
                EventCallback.Factory.Create<ChangeEventArgs>(this, e => LastValue = e.Value?.ToString()));
            b.CloseElement();
        }
    }

    private sealed class ThrowingClickProbe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(
                this, () => throw new InvalidOperationException("boom-click")));
            b.CloseElement();
        }
    }

    /// <summary>Click handler runs a NESTED dispatch (benign change handler),
    /// then throws — exercises the outer capture surviving the inner window.
    /// Static slots are safe under the "host-session" collection.</summary>
    private sealed class NestedDispatchProbe : ComponentBase
    {
        public static NativeRenderer? Renderer;
        public static int InnerHandlerId;
        public static bool InnerRan;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () =>
            {
                // Inline dispatcher: the nested dispatch (handler + any
                // re-render) completes synchronously right here.
                Renderer!.DispatchUiEventAsync(
                        new NativeUiEvent(0, InnerHandlerId, "change", "nested"))
                    .GetAwaiter().GetResult();
                throw new InvalidOperationException("outer-boom");
            }));
            b.CloseElement();

            b.OpenElement(10, "input");
            b.AddAttribute(11, "onchange",
                EventCallback.Factory.Create<ChangeEventArgs>(this, _ => InnerRan = true));
            b.CloseElement();
        }
    }
}
