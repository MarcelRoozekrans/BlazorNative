using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.AsyncEvents;
using BlazorNative.Core;
using BlazorNative.Renderer;

namespace BlazorNative.Renderer.Tests;

public class RendererSpike
{
    private sealed class HelloComponent : ComponentBase
    {
        [Parameter] public string Name { get; set; } = "World";
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, $"Hello {Name}");
            b.CloseElement();
        }
    }

    [Fact]
    public async Task FirstFrame_HasExpectedPatches()
    {
        // Arrange
        var services = new ServiceCollection().AddBlazorNativeRenderer().BuildServiceProvider();
        using var bridge = new DevHostBridge();
        using var renderer = new NativeRenderer(bridge, services);

        var tcs = new TaskCompletionSource<RenderFrame>();
        AsyncEvent<RenderFrame> handler = (frame, ct) =>
        {
            tcs.TrySetResult(frame);
            return ValueTask.CompletedTask;
        };
        renderer.Frames += handler;

        try
        {
            // Act
            await renderer.MountAsync<HelloComponent>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Name"] = "BlazorNative" }));
            var frame = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Assert
            Assert.NotNull(frame);
            Assert.NotEmpty(frame.Patches);

            // First create patches, then the text, then a commit. The exact sequence depends on the
            // walk order — assert by type/content rather than position to keep this resilient.
            Assert.Contains(frame.Patches, p => p is CreateNodePatch c && c.NodeType == "view");
            Assert.Contains(frame.Patches, p => p is CreateNodePatch c && c.NodeType == "text");
            Assert.Contains(frame.Patches, p => p is ReplaceTextPatch r && r.Text == "Hello BlazorNative");
            Assert.Contains(frame.Patches, p => p is CommitFramePatch);
        }
        finally
        {
            renderer.Frames -= handler;
        }
    }
}
