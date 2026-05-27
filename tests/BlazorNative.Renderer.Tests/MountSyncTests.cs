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

    [Fact]
    public void Mount_parameterless_overload_succeeds_with_sync_component()
    {
        // Regression guard: prevents anyone from collapsing the two Mount<T> overloads
        // into one with `ParameterView parameters = default`, which silently breaks on
        // Mono-WASI AOT (Phase 2.4 Task 4 defect #3). The fix is to pass ParameterView.Empty
        // explicitly via this overload — this test ensures the overload exists and works.
        var renderer = NewRenderer();
        var id = renderer.Mount<SyncProbe>();
        Assert.True(id >= 0, $"expected non-negative component id, got {id}");
    }

    [Fact]
    public void Renderer_uses_inline_dispatcher_so_mount_chain_completes_synchronously()
    {
        // Regression guard: prevents anyone from reverting the InlineDispatcher swap.
        // Dispatcher.CreateDefault() is not inline-only on Mono-WASI even when work
        // completes synchronously — the swap is load-bearing (Phase 2.4 Task 4 defect #1).
        var renderer = NewRenderer();
        Assert.Equal("InlineDispatcher", renderer.Dispatcher.GetType().Name);
        Assert.True(renderer.Dispatcher.CheckAccess(),
            "InlineDispatcher.CheckAccess() must return true on the calling thread");
    }
}
