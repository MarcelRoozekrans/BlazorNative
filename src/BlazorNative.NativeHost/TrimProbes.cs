using BlazorNative.Core;
using BlazorNative.Renderer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.NativeHost;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0c Gate 4 — the 4 accepted IL2072 call paths (Phase 3.0a decision
// matrix) exercised INSIDE the NativeAOT-trimmed library, on-device. Probes
// duplicated from tests/BlazorNative.Runtime.Tests/TrimValidationProbes.cs —
// the test project is never AOT-published, so host-CLR test passes prove
// nothing about trim. Statically rooted via the generic MountAsync<T> calls
// below (no [DynamicDependency] needed).
// Fate of this file (delete vs. keep as diagnostics surface) is a 3.0d decision.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ParameterProbe : ComponentBase
{
    [Parameter] public string? Label { get; set; }
    [Parameter] public int Count { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddContent(1, $"{Label}={Count}");
        b.CloseElement();
    }
}

internal sealed class CascadingProbe : ComponentBase
{
    [CascadingParameter] public string? Theme { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddContent(1, $"theme={Theme}");
        b.CloseElement();
    }
}

internal sealed class InjectProbe : ComponentBase
{
    [Inject] public IProbeService Service { get; set; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddContent(1, Service?.Name ?? "null");
        b.CloseElement();
    }
}

internal interface IProbeService
{
    string Name { get; }
}

internal sealed class ProbeService : IProbeService
{
    public string Name => "probe-service";
}

// Hosts CascadingProbe inside an unnamed CascadingValue<string>. The probe's
// [CascadingParameter] (no Name) matches by TYPE — so we deliberately omit
// CascadingValue.Name here (mirrors CascadingTestHost in the test project).
internal sealed class CascadingHost : ComponentBase
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

internal static class TrimProbeRunner
{
    /// <returns>(failedCount, semicolon-joined failure details or "")</returns>
    public static (int Failed, string Detail) RunAll()
    {
        var failures = new List<string>();
        Run(failures, "parameter", r => r.MountAsync<ParameterProbe>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["Label"] = "x", ["Count"] = 42 })),
            expected: "x=42");
        Run(failures, "cascading", r => r.MountAsync<CascadingHost>(), expected: "theme=dark");
        Run(failures, "inject", r => r.MountAsync<InjectProbe>(), expected: "probe-service");
        return (failures.Count, string.Join("; ", failures));
    }

    private static void Run(List<string> failures, string name,
        Func<NativeRenderer, Task> mount, string expected)
    {
        try
        {
            var services = new ServiceCollection();
            services.AddBlazorNativeCoreServices();
            services.AddBlazorNativeRendererServices();
            services.AddBlazorNativeHttpServices();
            services.AddSingleton<IProbeService, ProbeService>();
            var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();

            var tcs = new TaskCompletionSource<RenderFrame>();
            ZeroAlloc.AsyncEvents.AsyncEvent<RenderFrame> handler = (f, _) =>
            {
                tcs.TrySetResult(f);
                return ValueTask.CompletedTask;
            };
            renderer.Frames += handler;
            try
            {
                // Sync-over-async is safe here: real threads under NativeAOT
                // (no Mono-WASI single-thread ceiling).
                mount(renderer).GetAwaiter().GetResult();
                var frame = tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                var text = frame.Patches.OfType<ReplaceTextPatch>().FirstOrDefault()?.Text;
                if (text != expected)
                    failures.Add($"{name}: expected '{expected}' got '{text ?? "<no ReplaceTextPatch>"}'");
            }
            finally
            {
                renderer.Frames -= handler;
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
