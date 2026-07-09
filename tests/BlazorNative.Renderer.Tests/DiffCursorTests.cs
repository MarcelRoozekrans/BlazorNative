using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DiffCursorTests — Phase 3.2 Gate 3 review follow-up.
//
// Pins the diff-cursor node resolution in NativeRenderer.ProcessRenderTreeDiff
// beyond the Hello shape: an UpdateText in the SECOND of two sibling
// containers forces the cursor through StepIn(1) → UpdateText(0) → StepOut,
// i.e. a NON-ZERO StepIn sibling index. Before the cursor existed (and under
// any regression to sibling-map/frame-index lookups), the ReplaceText would
// resolve to the wrong node while text-only assertions stayed green — the
// exact failure mode Android Gate 3 exposed on Hello.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DiffCursorTests
{
    /// <summary>Two sibling containers; the click handler mutates only the
    /// SECOND container's text. The lambda target/method pair is identical
    /// across renders, so re-renders diff to a pure UpdateText (no
    /// SetAttribute churn).</summary>
    private sealed class TwoContainers : ComponentBase
    {
        private int _n;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");                       // container A — root sibling 0
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _n++));
            b.AddContent(2, "static-a");
            b.CloseElement();

            b.OpenElement(3, "div");                       // container B — root sibling 1
            b.AddContent(4, $"count: {_n}");               // the ONLY text that changes
            b.CloseElement();
        }
    }

    [Fact]
    public async Task UpdateText_InSecondSiblingContainer_TargetsThatContainersTextNode()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        using var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();

        // Frames-event capture pattern (test channel; no FrameSink needed).
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };

        await renderer.MountAsync<TwoContainers>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // Mount frame: harvest the two text nodes + the click handler.
        ReplaceTextPatch mountCount = Assert.Single(
            frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "count: 0");
        ReplaceTextPatch mountStatic = Assert.Single(
            frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "static-a");
        Assert.NotEqual(mountStatic.NodeId, mountCount.NodeId); // distinct targets, or the test proves nothing
        AttachEventPatch attach = Assert.Single(
            frames[0].Patches.OfType<AttachEventPatch>(), p => p.EventName == "click");

        // Re-render via the real event path: handler → StateHasChanged → diff.
        await renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null));

        Assert.True(frames.Count >= 2,
            $"expected a synchronous re-render frame, got {frames.Count} frame(s)");
        ReplaceTextPatch updated = Assert.Single(
            frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "count: 1");

        // THE assertion: the cursor stepped into root sibling 1 (container B)
        // and resolved ITS text node — not container A's text, not a container.
        Assert.Equal(mountCount.NodeId, updated.NodeId);
    }
}
