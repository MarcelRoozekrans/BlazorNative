using BlazorNative.Renderer;
using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0b Gate 2 trim-validation tests.
//
// Each test mounts one of the 3 probe components on the .NET host CLR (the
// `dotnet test` runtime) and asserts the first frame contains the expected
// rendered text. On the host CLR Phase 3.0a's annotations are non-load-bearing
// (no trim runs in `dotnet test`), but these tests give us a managed-side
// signal that the probe shapes WORK before we ship them through the NativeAOT
// trimmer in the JVM-desktop / Bionic publish.
//
// The actual "do the annotations survive trim?" question is answered by the
// NativeAOT publish in Gate 1 + the JNA BootSmokeNativeTest in Task 2.6.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TrimValidationTests
{
    private readonly ITestOutputHelper _log;
    public TrimValidationTests(ITestOutputHelper log) => _log = log;

    [Fact]
    public async Task ParameterProbe_RendersWithParameters()
    {
        var renderer = BuildRenderer();
        var pv = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Label"] = "x",
            ["Count"] = 42,
        });

        var frame = await CaptureFirstFrame(renderer, () => Task.Run(() =>
            renderer.Mount<ParameterProbe>(pv)));

        Assert.Contains(frame.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "view");
        var replaceText = frame.Patches.OfType<ReplaceTextPatch>().FirstOrDefault();
        Assert.NotNull(replaceText);
        Assert.Equal("x=42", replaceText!.Text);
        _log.WriteLine($"ParameterProbe PASS: rendered '{replaceText.Text}'");
    }

    [Fact]
    public async Task CascadingProbe_ReceivesCascadingValue()
    {
        // Mount a CascadingValue<string> wrapping CascadingProbe and assert
        // the child sees the value. Builds a tiny TestHost component.
        var renderer = BuildRenderer();
        var frame = await CaptureFirstFrame(renderer, () => Task.Run(() =>
            renderer.Mount<CascadingTestHost>()));

        var replaceText = frame.Patches.OfType<ReplaceTextPatch>().FirstOrDefault();
        Assert.NotNull(replaceText);
        Assert.Equal("theme=dark", replaceText!.Text);
        _log.WriteLine($"CascadingProbe PASS: rendered '{replaceText.Text}'");
    }

    [Fact]
    public async Task InjectProbe_ResolvesService()
    {
        var renderer = BuildRendererWithService();
        var frame = await CaptureFirstFrame(renderer, () => Task.Run(() =>
            renderer.Mount<InjectProbe>()));

        var replaceText = frame.Patches.OfType<ReplaceTextPatch>().FirstOrDefault();
        Assert.NotNull(replaceText);
        Assert.Equal("test-service", replaceText!.Text);
        _log.WriteLine($"InjectProbe PASS: rendered '{replaceText.Text}'");
    }

    // ── Test plumbing ────────────────────────────────────────────────────

    private static NativeRenderer BuildRenderer()
    {
        var services = new ServiceCollection();
        services.AddBlazorNativeCoreServices();
        services.AddBlazorNativeRendererServices();
        services.AddBlazorNativeHttpServices();
        return services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
    }

    private static NativeRenderer BuildRendererWithService()
    {
        var services = new ServiceCollection();
        services.AddBlazorNativeCoreServices();
        services.AddBlazorNativeRendererServices();
        services.AddBlazorNativeHttpServices();
        services.AddSingleton<ITestService, TestService>();
        return services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
    }

    private static async Task<RenderFrame> CaptureFirstFrame(
        NativeRenderer renderer,
        Func<Task> mountAction,
        TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<RenderFrame>();
        ZeroAlloc.AsyncEvents.AsyncEvent<RenderFrame> handler = (f, _) =>
        {
            tcs.TrySetResult(f);
            return ValueTask.CompletedTask;
        };
        renderer.Frames += handler;
        try
        {
            await mountAction();
            return await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(2));
        }
        finally
        {
            renderer.Frames -= handler;
        }
    }
}

// Hosts CascadingProbe inside an unnamed CascadingValue<string>. The probe's
// [CascadingParameter] (no Name) matches by TYPE — so we deliberately omit
// CascadingValue.Name here. If we set Name="Theme" we'd also need
// [CascadingParameter(Name = "Theme")] on the probe to consume it.
internal sealed class CascadingTestHost : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent<CascadingValue<string>>(0);
        b.AddComponentParameter(1, nameof(CascadingValue<string>.Value), "dark");
        b.AddComponentParameter(2, nameof(CascadingValue<string>.ChildContent),
            (RenderFragment)(cb => { cb.OpenComponent<CascadingProbe>(0); cb.CloseComponent(); }));
        b.CloseComponent();
    }
}
