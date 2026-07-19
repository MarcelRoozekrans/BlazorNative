using System.Text;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// FramesFaultSurfacingTests — Phase 10.0 Gate A (#123).
//
// The Frames event is the DOCUMENTED test channel: golden/component tests
// subscribe to it and assert on the emitted patches. Before this fix,
// NativeRenderer committed a frame with `_ = _frames.InvokeAsync(frame, default)`
// — the returned (ValueTask) was DISCARDED. AsyncEventHandler reports a
// subscriber fault THROUGH that task, so a throw inside a Frames subscriber
// (e.g. a failing assertion) was never observed: it never reached
// HandleException and StrictErrors could not surface it. A test could be GREEN
// while its frame assertion actually FAILED — a false-green measuring
// instrument. This test drives a mount (which commits a frame), throws from a
// Frames subscriber, and asserts the fault SURFACES under StrictErrors.
//
// Same session harness as BnComponentTests/CompositionProbeTests (HostSession
// renderer, which the StrictModeTestDefaults module initializer flips STRICT
// process-wide); serialized via the shared "host-session" collection.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class FramesFaultSurfacingTests : IDisposable
{
    public void Dispose()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>Minimal component that commits exactly one frame on mount — the
    /// vehicle for firing the Frames subscriber; its shape is irrelevant.</summary>
    private sealed class OneDiv : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.CloseElement();
        }
    }

    private static string Flatten(Exception ex)
    {
        var sb = new StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            sb.Append(e.Message).Append(" | ");
        return sb.ToString();
    }

    [Fact]
    public async Task Frames_SubscriberThrows_SurfacesUnderStrictErrors()
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        Assert.True(renderer.StrictErrors,
            "the host-session renderer must run strict in tests (StrictModeTestDefaults)");

        // A frame subscriber that faults — mirrors a failing assertion inside a
        // Frames handler, the exact false-green risk (#123).
        renderer.Frames += (_, _)
            => throw new InvalidOperationException("boom-in-frame-subscriber");

        // Mount commits a frame → the subscriber faults. Under StrictErrors the
        // fault must SURFACE (propagate out of the mount), not be swallowed.
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => renderer.MountAsync<OneDiv>(ParameterView.Empty));
        Assert.Contains("boom-in-frame-subscriber", Flatten(ex));
    }
}
