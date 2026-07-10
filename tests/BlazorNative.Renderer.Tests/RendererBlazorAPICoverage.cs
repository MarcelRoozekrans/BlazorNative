using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using BlazorNative.Renderer;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RendererBlazorAPICoverage
//
// Regression coverage for the Blazor component-API surface that BlazorNative's
// renderer must support. Each test mounts a probe component exercising one
// pattern (parameters, events, nested components, [Inject] DI, CascadingValue)
// and asserts the emitted RenderFrame contains the expected patch shape.
//
// Originally `Phase27HostElementSpike`: discovered that no "host element"
// abstraction was needed for M2 and surfaced two real renderer bugs (Bug A:
// MountAsync default ParameterView NRE; Bug B: Component-frame mis-attribution).
// Both fixed in Phase 2.7. Spike doc:
// docs/plans/2026-05-27-phase-2.7-host-element-spike.md.
//
// These tests run on the untrimmed .NET host CLR. Trimmed-runtime concerns
// are covered by the trim probes inside the published NativeAOT binary
// (Phase 3.0c Gate 4).
// ─────────────────────────────────────────────────────────────────────────────

public class RendererBlazorAPICoverage
{
    private readonly ITestOutputHelper _log;
    public RendererBlazorAPICoverage(ITestOutputHelper log) => _log = log;

    private static NativeRenderer NewRenderer(IServiceCollection? extraServices = null)
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        extraServices?.GetEnumerator().ToEnumerable().ToList().ForEach(s => services.Add(s));
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)
        return renderer;
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

    // ── Probe 1: Parameter passing ────────────────────────────────────────────

    private sealed class ParamProbe : ComponentBase
    {
        [Parameter] public string Greeting { get; set; } = "default";
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, Greeting);
            b.CloseElement();
        }
    }

    [Fact]
    public async Task Probe1_Parameters_Flow_Through_To_Component()
    {
        using var renderer = NewRenderer();
        var frame = await CaptureFirstFrame(renderer, () =>
            renderer.MountAsync<ParamProbe>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Greeting"] = "Hello from spike" })));

        _log.WriteLine($"frame has {frame.Patches.Length} patches");
        var textPatch = frame.Patches.OfType<ReplaceTextPatch>().Single();
        Assert.Equal("Hello from spike", textPatch.Text);
        _log.WriteLine("✅ Probe 1 PASS: parameters flow through correctly");
    }

    // ── Probe 2: Event handler attachment ─────────────────────────────────────

    private sealed class EventProbe : ComponentBase
    {
        public int ClickCount { get; private set; }
        private void OnClick() => ClickCount++;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            // The HTML-style "onclick" attribute name; the renderer routes via the
            // attribute starts-with "on" branch to AttachEventPatch.
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create(this, OnClick));
            b.AddContent(2, "tap");
            b.CloseElement();
        }
    }

    [Fact]
    public async Task Probe2_EventHandlers_Produce_AttachEventPatch()
    {
        using var renderer = NewRenderer();
        var frame = await CaptureFirstFrame(renderer, () => renderer.MountAsync<EventProbe>(ParameterView.Empty));

        _log.WriteLine($"frame patches: {string.Join(", ", frame.Patches.Select(p => p.GetType().Name))}");

        var attachPatches = frame.Patches.OfType<AttachEventPatch>().ToList();
        if (attachPatches.Count == 0)
        {
            _log.WriteLine("⚠ Probe 2 FAIL-ish: no AttachEventPatch emitted");
            _log.WriteLine("   Patches were: " + string.Join(", ",
                frame.Patches.Select(p => $"{p.GetType().Name}({p})")));
        }
        else
        {
            _log.WriteLine($"✅ Probe 2 PASS: {attachPatches.Count} AttachEventPatch(es), " +
                          $"eventName={attachPatches[0].EventName}, handlerId={attachPatches[0].HandlerId}");
        }
        Assert.NotEmpty(attachPatches);
        Assert.Equal("click", attachPatches[0].EventName);
        Assert.True(attachPatches[0].HandlerId > 0, "handlerId should be a positive opaque id");
    }

    // ── Probe 3: Nested components ────────────────────────────────────────────

    private sealed class NestedChild : ComponentBase
    {
        [Parameter] public string Label { get; set; } = "?";
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "span");
            b.AddContent(1, $"child:{Label}");
            b.CloseElement();
        }
    }

    private sealed class NestedParent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.OpenComponent<NestedChild>(1);
            b.AddComponentParameter(2, "Label", "A");
            b.CloseComponent();
            b.OpenComponent<NestedChild>(3);
            b.AddComponentParameter(4, "Label", "B");
            b.CloseComponent();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task Probe3_NestedComponents_Produce_Multi_Component_Frames()
    {
        using var renderer = NewRenderer();
        var frame = await CaptureFirstFrame(renderer, () => renderer.MountAsync<NestedParent>(ParameterView.Empty));

        _log.WriteLine($"frame {frame.FrameId} patches ({frame.Patches.Length}):");
        foreach (var p in frame.Patches) _log.WriteLine($"   - {p}");

        // What we want to know:
        //  - Are both children rendered? (Their text content visible?)
        //  - Do their patches have valid parentId pointing at the parent div?
        //  - Or do they arrive in a SEPARATE frame (one per child component)?
        var childATexts = frame.Patches.OfType<ReplaceTextPatch>()
            .Where(p => p.Text == "child:A").ToList();
        var childBTexts = frame.Patches.OfType<ReplaceTextPatch>()
            .Where(p => p.Text == "child:B").ToList();

        _log.WriteLine($"   child:A texts found = {childATexts.Count}");
        _log.WriteLine($"   child:B texts found = {childBTexts.Count}");

        // Phase 2.7 Bug B fix: assert NO UpdatePropPatches on the parent div for
        // child component parameters. Pre-fix, "Label"="A" and "Label"="B" were
        // mis-attributed to the parent (nodeId=1); post-fix, they should be on
        // their respective child components or not emitted in the parent's diff
        // at all (since child components render via separate diffs).
        var parentNodeId = frame.Patches.OfType<CreateNodePatch>()
            .First(p => p.NodeType == "view").NodeId;
        var labelPropsOnParent = frame.Patches.OfType<UpdatePropPatch>()
            .Where(p => p.NodeId == parentNodeId && p.Name == "Label")
            .ToList();
        Assert.Empty(labelPropsOnParent);  // Bug B fix: no mis-attributed props

        // Both children's text must still render correctly (proves the fix
        // doesn't break the children's own diffs).
        Assert.NotEmpty(childATexts);
        Assert.NotEmpty(childBTexts);

        _log.WriteLine("✅ Probe 3 PASS: both children rendered + no mis-attribution");
    }

    // ── Probe 4: Service injection ────────────────────────────────────────────

    private sealed class GreetingService
    {
        public string Hello(string name) => $"Hello, {name}!";
    }

    private sealed class InjectProbe : ComponentBase
    {
        [Inject] public GreetingService Svc { get; set; } = null!;
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, Svc.Hello("DI"));
            b.CloseElement();
        }
    }

    [Fact]
    public async Task Probe4_Inject_Resolves_From_ServiceProvider()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        services.AddSingleton<GreetingService>();
        var provider = services.BuildServiceProvider();
        using var renderer = provider.GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)

        var frame = await CaptureFirstFrame(renderer, () => renderer.MountAsync<InjectProbe>(ParameterView.Empty));

        var textPatch = frame.Patches.OfType<ReplaceTextPatch>().SingleOrDefault();
        if (textPatch is null)
        {
            _log.WriteLine($"⚠ Probe 4 FAIL: no ReplaceTextPatch. Patches: {string.Join(", ", frame.Patches.Select(p => p.GetType().Name))}");
        }
        Assert.NotNull(textPatch);
        Assert.Equal("Hello, DI!", textPatch.Text);
        _log.WriteLine("✅ Probe 4 PASS: [Inject] resolved GreetingService correctly");
    }

    // ── Probe 5: CascadingValue ───────────────────────────────────────────────

    private sealed class CascadeChild : ComponentBase
    {
        [CascadingParameter] public string? Theme { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, $"theme:{Theme ?? "null"}");
            b.CloseElement();
        }
    }

    private sealed class CascadeParent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<CascadingValue<string>>(0);
            b.AddComponentParameter(1, "Value", "dark");
            b.AddComponentParameter(2, "ChildContent", (RenderFragment)(cb =>
            {
                cb.OpenComponent<CascadeChild>(3);
                cb.CloseComponent();
            }));
            b.CloseComponent();
        }
    }

    [Fact]
    public async Task Probe5_CascadingValue_Reaches_Child()
    {
        using var renderer = NewRenderer();
        var frame = await CaptureFirstFrame(renderer, () => renderer.MountAsync<CascadeParent>(ParameterView.Empty));

        _log.WriteLine($"frame patches ({frame.Patches.Length}):");
        foreach (var p in frame.Patches) _log.WriteLine($"   - {p}");

        var themePatch = frame.Patches.OfType<ReplaceTextPatch>()
            .FirstOrDefault(p => p.Text.StartsWith("theme:"));

        Assert.NotNull(themePatch);
        Assert.Equal("theme:dark", themePatch.Text);

        // Phase 3.4 Task 2: assert RENDERING, not just the cascaded value.
        // The child's ChildContent travels through CascadingValue's Region
        // root — its div must actually be created, with the text node
        // parented under it (pre-3.4 this probe passed on the value text
        // alone and would have stayed green with the child's views dropped
        // or misrooted).
        var textCreate = Assert.Single(frame.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == themePatch.NodeId);
        var childDiv = Assert.Single(frame.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "view");
        Assert.Equal(childDiv.NodeId, textCreate.ParentId);
        _log.WriteLine($"✅ Probe 5 PASS: cascading value reached child AND rendered → {themePatch.Text}");
    }

    /// <summary>Phase 3.4 Task 2: the cascading probe must also prove the
    /// RE-RENDER path — a cascaded value CHANGE re-renders the consuming
    /// child (nodeId-pinned ReplaceText), the exact behavior DoD #6's theme
    /// toggle rides on.</summary>
    private sealed class CascadeToggleParent : ComponentBase
    {
        private string _theme = "dark";
        private void Toggle() => _theme = "light";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(this, Toggle));
            b.AddContent(2, "toggle");
            b.CloseElement();

            b.OpenComponent<CascadingValue<string>>(3);
            b.AddComponentParameter(4, "Value", _theme);
            b.AddComponentParameter(5, "ChildContent", (RenderFragment)(cb =>
            {
                cb.OpenComponent<CascadeChild>(0);
                cb.CloseComponent();
            }));
            b.CloseComponent();
        }
    }

    [Fact]
    public async Task Probe5b_CascadeChange_ReRendersConsumingChild()
    {
        using var renderer = NewRenderer();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };

        await renderer.MountAsync<CascadeToggleParent>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        var mountTheme = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "theme:dark");
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "click");

        // Toggle the cascaded value: CascadingValue (IsFixed=false) notifies
        // its subscribers → CascadeChild re-renders with the new theme.
        await renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null));
        Assert.True(frames.Count >= 2, "expected a re-render frame after the cascade change");

        var updated = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "theme:light");
        // nodeId-pinned: the SAME text node the mount created — the child
        // re-rendered in place, its edit resolving through the region-
        // transparent slot bookkeeping.
        Assert.Equal(mountTheme.NodeId, updated.NodeId);
        _log.WriteLine($"✅ Probe 5b PASS: cascade change re-rendered the child in place (node {updated.NodeId})");
    }

    // ── Bug A regression test ─────────────────────────────────────────────────

    [Fact]
    public async Task BugA_MountAsync_With_No_Args_Defaults_To_Empty_Parameters()
    {
        // Regression guard: Phase 2.4 Task 4 found that default(ParameterView)
        // NREs in Blazor's ParameterView enumerator (silently swallowed by
        // HandleException). The sync Mount<T> was fixed; MountAsync<T> still
        // had the latent bug per Phase 2.7 spike. This test calls MountAsync
        // with NO args and asserts a frame fires (i.e., the mount actually
        // produced patches, not silently failed).
        using var renderer = NewRenderer();
        var frame = await CaptureFirstFrame(renderer, () => renderer.MountAsync<ParamProbe>());
        _log.WriteLine($"Bug A test: got {frame.Patches.Length} patches");
        Assert.NotEmpty(frame.Patches);
        // ParamProbe with no Greeting param falls back to its default "default" value
        var textPatch = frame.Patches.OfType<ReplaceTextPatch>().Single();
        Assert.Equal("default", textPatch.Text);
    }
}

internal static class EnumerableExtensions
{
    public static IEnumerable<T> ToEnumerable<T>(this IEnumerator<T> e)
    {
        while (e.MoveNext()) yield return e.Current;
    }
}
