using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0b Gate 2 trim-validation probes.
//
// Each probe exercises one of the 4 IL2072 ACCEPT call paths from Phase 3.0a:
//   - ParameterProbe   → ComponentProperties.SetProperties (IL2072 #5 + #6)
//   - CascadingProbe   → CascadingParameterState.FindCascadingParameters (#3)
//   - InjectProbe      → ComponentFactory.PerformPropertyInjection (#4)
//
// Mounted on JVM-desktop NativeAOT (where the trimmer runs) — proves the
// ACCEPT decisions hold at runtime, not just statically.
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
    [Inject] public ITestService Service { get; set; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddContent(1, Service?.Name ?? "null");
        b.CloseElement();
    }
}

internal interface ITestService
{
    string Name { get; }
}

internal sealed class TestService : ITestService
{
    public string Name => "test-service";
}
