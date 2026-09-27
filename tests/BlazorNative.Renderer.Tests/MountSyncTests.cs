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
        services.AddBlazorNativeRendererServices();
        var provider = services.BuildServiceProvider();
        var renderer = provider.GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)
        return renderer;
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
    public void Renderer_mounts_synchronously_on_its_render_thread()
    {
        // Flipped in 16.1: was Renderer_uses_inline_dispatcher_so_mount_chain_completes_synchronously, asserting the type name "InlineDispatcher" and CheckAccess() == true on the calling thread.
        //
        // Regression guard for the sync-mount contract the C ABI depends on: the first render
        // completes before Mount returns. It now holds because Mount posts to the renderer's
        // own render thread and waits, not because the dispatcher runs work inline — so this
        // pins the BEHAVIOUR, never the dispatcher's type.
        var renderer = NewRenderer();
        int frames = 0;
        int emittingThread = 0;
        renderer.Frames += (_, _) =>
        {
            Interlocked.Increment(ref frames);
            Volatile.Write(ref emittingThread, Environment.CurrentManagedThreadId);
            return ValueTask.CompletedTask;
        };

        // Honest CheckAccess: the test thread is not the render thread.
        Assert.False(renderer.Dispatcher.CheckAccess(),
            "CheckAccess() answered true on the test thread — the dispatcher is claiming a thread it does not own");

        var id = renderer.Mount<SyncProbe>();

        Assert.True(id >= 0, $"expected non-negative component id, got {id}");
        Assert.True(Volatile.Read(ref frames) >= 1, "Mount returned before its first frame was emitted");
        Assert.Equal(renderer.RenderThreadId, Volatile.Read(ref emittingThread));
    }
}
