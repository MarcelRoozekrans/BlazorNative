using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

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
//   2 = handler threw (detail ex.ToString() on stderr)
//   3 = malformed / NULL args JSON
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
        ReplaceTextPatch replace = Assert.Single(frames[1].Patches.OfType<ReplaceTextPatch>());
        Assert.Contains("taps: 1", replace.Text);
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

    // ── rc 3: malformed / NULL args ───────────────────────────────────────────

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
}
