using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// TrimSafetyTests — Phase 3.0a Gate 3 regression test
//
// HISTORY: the componentize-dotnet spike on 2026-05-28 RED'd because
// RenderTreeFrame.ElementName returned null under NativeAOT-LLVM trim. That
// caused NativeRenderer's CreateNodePatch.NodeType to receive a null/empty
// value (MapElementToNodeType would crash on null), breaking the patch
// stream the native shell consumes.
//
// Phase 3.0a's Gate 2 work added [DynamicallyAccessedMembers(All)] to the
// Mount<T> / MountAsync<T> / AddComponentAsync(Type) surface (eliminating
// IL2087) plus a Type.GetType-based VerifyAccessors refactor (eliminating
// IL2026). Phase 3.0a Gate 3 (this file) locks in that pass with a
// behavioral assertion at the .NET layer.
//
// SCOPE CAVEAT: this test runs on the CURRENT toolchain (Mono-AOT under
// wasi-experimental — what `dotnet test` exercises on the .NET host CLR).
// It does NOT prove NativeAOT trim safety — that's Phase 3.0b Gate 2's job.
// Its purpose is:
//   1. Regression guard against future code drift that breaks ElementName
//      flow on Mono-AOT (would be unexpected, but the test catches it).
//   2. Sanity check that Gate 2's annotation pass didn't accidentally break
//      behavior on Mono-AOT.
//
// PROBE COMPONENT: TrimSafetyProbe mirrors HelloComponent's shape (the M2
// "Hello demo" component in src/BlazorNative.WasiHost). HelloComponent is
// `internal sealed` in WasiHost; rather than add a WasiHost ProjectReference
// to the test project (and create a project-graph dependency for one
// internal type), we mirror its layout here: outer LinearLayout container
// (div with backgroundColor + padding style attrs) holding a TextView
// (inner div with fontSize + text content), a Button, and an input with
// a placeholder. That matches Phase 2.8's "Hello demo" element vocabulary
// without introducing a project-reference dependency.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TrimSafetyTests
{
    private readonly ITestOutputHelper _log;
    public TrimSafetyTests(ITestOutputHelper log) => _log = log;

    /// <summary>
    /// Mirrors HelloComponent's shape (src/BlazorNative.WasiHost/HelloComponent.cs)
    /// without taking a ProjectReference dependency on WasiHost. Same element
    /// vocabulary (div / button / input), same attribute mix (backgroundColor,
    /// padding, fontSize, placeholder), same nesting depth.
    /// </summary>
    private sealed class TrimSafetyProbe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "backgroundColor", "#FFEEAA");
            b.AddAttribute(2, "padding", "16");

            b.OpenElement(10, "div");
            b.AddAttribute(11, "fontSize", "24");
            b.AddContent(12, "Hello, BlazorNative!");
            b.CloseElement();

            b.OpenElement(20, "button");
            b.AddContent(21, "Tap");
            b.CloseElement();

            b.OpenElement(30, "input");
            b.AddAttribute(31, "placeholder", "Type here...");
            b.CloseElement();

            b.CloseElement();
        }
    }

    private static async Task<RenderFrame> CaptureFirstFrame(
        NativeRenderer renderer,
        Func<Task> mountAction,
        TimeSpan? timeout = null)
    {
        // Same pattern as RendererBlazorAPICoverage.CaptureFirstFrame —
        // subscribe to NativeRenderer.Frames (the strongly-typed in-memory
        // channel) instead of Console.SetOut (which is fragile under
        // parallel test runs).
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

    [Fact]
    public async Task Mount_HelloComponentShape_FirstCreateNodeHasNonNullElementName()
    {
        // Build the same DI surface as production WasiHost (Core + Renderer).
        // We don't need Http services for this test — Mount only touches
        // renderer + core. AddBlazorNativeRenderer is a convenience overload
        // that calls AddBlazorNativeRendererServices internally.
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        using var renderer = services.BuildServiceProvider()
            .GetRequiredService<NativeRenderer>();

        var frame = await CaptureFirstFrame(
            renderer,
            () => renderer.MountAsync<TrimSafetyProbe>(ParameterView.Empty));

        // Stronger than the plan's string-contains shape: parse the
        // strongly-typed patch list, find the first CreateNodePatch, and
        // assert its NodeType is a non-null non-empty mapped value.
        //
        // CreateNodePatch.NodeType is produced by NativeRenderer's
        // MapElementToNodeType(frame.ElementName!) call — if ElementName
        // were null (the componentize bug shape), MapElementToNodeType
        // would NRE on .ToLowerInvariant(). So if this assert holds, the
        // ElementName flow is intact on Mono-AOT today.
        _log.WriteLine($"frame has {frame.Patches.Length} patches:");
        foreach (var p in frame.Patches) _log.WriteLine($"   - {p}");

        var firstCreate = frame.Patches.OfType<CreateNodePatch>().FirstOrDefault();
        Assert.NotNull(firstCreate);
        Assert.False(string.IsNullOrEmpty(firstCreate!.NodeType),
            "CreateNodePatch.NodeType must be a non-empty mapped element name. " +
            "If null/empty here, RenderTreeFrame.ElementName flow is broken " +
            "(componentize-dotnet 2026-05-28 bug shape on Mono-AOT — would be a surprise).");

        // The outer div maps to "view" via MapElementToNodeType. Assert that
        // mapping took effect — proves both that ElementName flowed AND that
        // the mapping ran on a real string (not a null).
        Assert.Equal("view", firstCreate.NodeType);

        // Sanity check: every CreateNodePatch in the frame has a non-empty
        // NodeType. Catches a future drift where one element's name is
        // dropped while others survive.
        foreach (var create in frame.Patches.OfType<CreateNodePatch>())
        {
            Assert.False(string.IsNullOrEmpty(create.NodeType),
                $"CreateNodePatch at NodeId={create.NodeId} has empty NodeType.");
        }

        _log.WriteLine("✅ TrimSafetyTests PASS: ElementName → NodeType flow intact on Mono-AOT");
    }
}
