using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using BlazorNative.Core;
using BlazorNative.Renderer;
using Xunit;

namespace BlazorNative.Renderer.Tests;

public sealed class MountSyncTests
{
    private sealed class SyncProbe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, "probe");
            b.CloseElement();
        }
    }

    private sealed class AsyncProbe : ComponentBase, IComponent
    {
        // SetParametersAsync is awaited by Renderer.RenderRootComponentAsync,
        // so overriding it with a never-completing await guarantees the
        // returned MountAsync task is observably incomplete when Mount<T>
        // inspects IsCompletedSuccessfully. (OnInitializedAsync isn't enough:
        // ComponentBase fire-and-forgets its continuation onto pending tasks
        // and the first render task completes anyway.)
        private static readonly TaskCompletionSource _neverCompletes = new();

        Task IComponent.SetParametersAsync(ParameterView parameters)
            => _neverCompletes.Task;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.CloseElement();
        }
    }

    private static NativeRenderer NewRenderer()
    {
        var services = new ServiceCollection();
        services.AddBlazorNativeCoreServices();
        services.AddBlazorNativeRendererServices();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<NativeRenderer>();
    }

    [Fact]
    public void Mount_returns_component_id_for_sync_component()
    {
        var renderer = NewRenderer();
        var id = renderer.Mount<SyncProbe>();
        Assert.True(id >= 0, $"expected non-negative component id, got {id}");
    }

    [Fact]
    public void Mount_throws_when_component_has_async_lifecycle()
    {
        var renderer = NewRenderer();
        var ex = Assert.Throws<InvalidOperationException>(() => renderer.Mount<AsyncProbe>());
        Assert.Contains("synchronously", ex.Message);
    }
}
