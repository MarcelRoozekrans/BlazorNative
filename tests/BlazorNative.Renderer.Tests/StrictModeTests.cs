using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// StrictModeTests — Phase 3.3 Task 6 (DoD #9).
//
// NativeRenderer.StrictErrors surfaces exceptions Blazor routes to
// HandleException instead of swallowing them to stderr — the hazard that cost
// multi-day hunts three times (Bug A, Bug B, the 3.2 diff-cursor bug).
// Contract:
//   • strict + outside the 3.2 event-dispatch capture window → rethrow
//     synchronously (ExceptionDispatchInfo, original stack) at the caller
//     boundary (mount / batch);
//   • INSIDE the dispatch window the 3.2 capture still wins — the dispatch
//     task faults with the ORIGINAL exception (export rc 2), no double-report;
//   • renderer contract violations (poisoned cursor, out-of-range
//     diff-provided sibling index) raise through the same switch;
//   • default is FALSE — the deliberate production POC posture (log to
//     stderr); ALL test fixtures opt in.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StrictModeTests
{
    private static NativeRenderer BuildRenderer(bool strict)
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = strict;
        return renderer;
    }

    private sealed class ThrowsOnBuild : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
            => throw new InvalidOperationException("boom-at-build");
    }

    private static string FlattenMessages(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            sb.Append(e.Message).Append(" | ");
        return sb.ToString();
    }

    [Fact]
    public void StrictErrors_DefaultsFalse_TheDocumentedProductionPosture()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        using var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        Assert.False(renderer.StrictErrors);
    }

    [Fact]
    public async Task Strict_BuildRenderTreeThrow_SurfacesAtMount()
    {
        using var renderer = BuildRenderer(strict: true);
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => renderer.MountAsync<ThrowsOnBuild>(ParameterView.Empty));
        Assert.Contains("boom-at-build", FlattenMessages(ex));
    }

    [Fact]
    public async Task NonStrict_BuildRenderTreeThrow_IsSwallowed()
    {
        using var renderer = BuildRenderer(strict: false);
        // The old (and production) behavior: HandleException logs to stderr,
        // mount appears to succeed — exactly the silent-swallow hazard strict
        // mode exists to expose in tests.
        var id = await renderer.MountAsync<ThrowsOnBuild>(ParameterView.Empty);
        Assert.True(id >= 0);
    }

    [Fact]
    public void Strict_ContractViolation_Throws()
    {
        // Poisoned-cursor / out-of-range-sibling situations are no longer
        // constructible from legal Blazor diffs (Tasks 1-3 fixed the causes),
        // so the reporting path is exercised via the internal injection point
        // both production guards call (see ReportContractViolation call
        // sites: the PrependFrame poison guard and AddSlot's clamp check).
        using var renderer = BuildRenderer(strict: true);
        var ex = Assert.Throws<InvalidOperationException>(
            () => renderer.InjectContractViolationForTests("test-injected violation"));
        Assert.Contains("test-injected violation", ex.Message);
    }

    [Fact]
    public void NonStrict_ContractViolation_LogsAndContinues()
    {
        using var renderer = BuildRenderer(strict: false);
        // Must not throw — stderr-only, the POC drop-and-continue posture.
        renderer.InjectContractViolationForTests("test-injected violation");
    }

    private sealed class ThrowingHandler : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(
                this, () => throw new InvalidOperationException("boom-in-handler")));
            b.AddContent(2, "tap");
            b.CloseElement();
        }
    }

    [Fact]
    public async Task Strict_InsideDispatchWindow_The32CaptureStillWins()
    {
        using var renderer = BuildRenderer(strict: true);
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        await renderer.MountAsync<ThrowingHandler>(ParameterView.Empty);
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // A handler fault DURING dispatch faults the dispatch task with the
        // ORIGINAL exception (the export maps this to rc 2). Strict mode must
        // not rethrow from inside the window — a double-report would surface
        // the exception from HandleException's stack instead of the dispatch
        // boundary, breaking the rc-2 contract.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null)));
        Assert.Equal("boom-in-handler", ex.Message);
    }
}
