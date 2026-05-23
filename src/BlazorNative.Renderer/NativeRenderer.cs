using System.Reactive.Subjects;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using BlazorNative.Core;

namespace BlazorNative.Renderer;

// ─────────────────────────────────────────────────────────────────────────────
// NativeRenderer
//
// A headless Blazor renderer that intercepts component render tree diffs and
// translates them into RenderPatch commands dispatched to the native shell
// via IMobileBridge.
//
// Blazor's Renderer base class handles all component lifecycle (OnInit,
// OnParametersSet, StateHasChanged, cascading values, etc). We only override
// the parts that touch the actual output — UpdateDisplayAsync — and redirect
// that output through our patch protocol instead of to a browser DOM.
//
// Threading note: WASI has no threads. All rendering is cooperative/single-
// threaded. The Dispatcher below reflects this — it runs synchronously on
// the WASI cooperative scheduler.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NativeRenderer : Renderer
{
    private readonly IMobileBridge _bridge;
    private readonly Subject<RenderFrame> _frames = new();
    private readonly NativeWidgetTree _tree = new();
    private int _frameId = 0;

    public IObservable<RenderFrame> Frames => _frames;

    public NativeRenderer(IMobileBridge bridge, IServiceProvider services)
        : base(services, new NativeRendererLoggerFactory())
    {
        _bridge = bridge;
    }

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>Mount a root component and begin rendering.</summary>
    public async Task<int> MountAsync<TComponent>(
        ParameterView parameters = default,
        CancellationToken ct = default)
        where TComponent : IComponent
    {
        var componentId = await Dispatcher.InvokeAsync(()
            => AddRootComponent(typeof(TComponent), "blazornative-root", parameters));
        return componentId;
    }

    // ── Renderer overrides ────────────────────────────────────────────────────

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        var patches = new List<RenderPatch>();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Process each updated component in the batch
        for (var i = 0; i < renderBatch.UpdatedComponents.Count; i++)
        {
            ref var componentDiff = ref renderBatch.UpdatedComponents.Array[i];
            ProcessRenderTreeDiff(ref componentDiff, patches);
        }

        // Process disposed components
        foreach (var disposedId in renderBatch.DisposedComponentIDs)
            patches.Add(new RemoveNodePatch(_tree.GetNodeId(disposedId)));

        // Emit commit boundary
        var frameId = Interlocked.Increment(ref _frameId);
        patches.Add(new CommitFramePatch(frameId, timestamp));

        var frame = new RenderFrame(frameId, timestamp, patches.ToArray());
        _frames.OnNext(frame);

        // Dispatch to native shell via bridge
        _ = DispatchFrameAsync(frame);

        return Task.CompletedTask;
    }

    protected override void HandleException(Exception exception)
        => Console.Error.WriteLine($"[BlazorNative.Renderer] {exception}");

    // ── Render tree walking ───────────────────────────────────────────────────

    private void ProcessRenderTreeDiff(
        ref RenderTreeDiff diff,
        List<RenderPatch> patches)
    {
        for (var i = 0; i < diff.Edits.Count; i++)
        {
            ref var edit = ref diff.Edits.Array[i];
            switch (edit.Type)
            {
                case RenderTreeEditType.PrependFrame:
                    ProcessFrame(ref diff, edit.ReferenceFrameIndex, edit.SiblingIndex, patches);
                    break;

                case RenderTreeEditType.RemoveFrame:
                    var nodeId = _tree.GetNodeIdBySibling(diff.ComponentId, edit.SiblingIndex);
                    if (nodeId >= 0) patches.Add(new RemoveNodePatch(nodeId));
                    break;

                case RenderTreeEditType.SetAttribute:
                    ProcessAttributeEdit(ref diff, edit.ReferenceFrameIndex, patches);
                    break;

                case RenderTreeEditType.RemoveAttribute:
                    var attrNodeId = _tree.GetNodeIdBySibling(diff.ComponentId, edit.SiblingIndex);
                    if (attrNodeId >= 0)
                        patches.Add(new UpdatePropPatch(attrNodeId, edit.RemovedAttributeName!, null));
                    break;

                case RenderTreeEditType.UpdateText:
                    ProcessTextEdit(ref diff, edit.ReferenceFrameIndex, edit.SiblingIndex, patches);
                    break;
            }
        }
    }

    private void ProcessFrame(
        ref RenderTreeDiff diff,
        int frameIndex,
        int siblingIndex,
        List<RenderPatch> patches)
    {
        ref var frame = ref diff.ReferenceFrames.Array[frameIndex];

        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
                var nodeId = _tree.AllocateNode(diff.ComponentId, siblingIndex);
                var nodeType = MapElementToNodeType(frame.ElementName);
                patches.Add(new CreateNodePatch(nodeId, nodeType));

                // Walk attributes
                for (var i = 1; i <= frame.ElementSubtreeLength - 1; i++)
                {
                    ref var child = ref diff.ReferenceFrames.Array[frameIndex + i];
                    if (child.FrameType == RenderTreeFrameType.Attribute)
                        ProcessAttribute(nodeId, ref child, patches);
                    else
                        break; // attributes always come first
                }
                break;

            case RenderTreeFrameType.Text:
                var textNodeId = _tree.AllocateNode(diff.ComponentId, siblingIndex);
                patches.Add(new CreateNodePatch(textNodeId, "text"));
                patches.Add(new ReplaceTextPatch(textNodeId, frame.TextContent));
                break;
        }
    }

    private static void ProcessAttribute(
        int nodeId,
        ref RenderTreeFrame frame,
        List<RenderPatch> patches)
    {
        // Map HTML-like attributes to native props / styles / events
        if (frame.AttributeName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            // Event handler — tell native shell to route this event back to WASM
            patches.Add(new AttachEventPatch(
                nodeId,
                frame.AttributeName[2..].ToLowerInvariant(), // "onclick" → "click"
                frame.AttributeEventHandlerId));
        }
        else if (StyleAttributes.Contains(frame.AttributeName))
        {
            patches.Add(new SetStylePatch(nodeId, frame.AttributeName, frame.AttributeValue?.ToString()));
        }
        else
        {
            patches.Add(new UpdatePropPatch(nodeId, frame.AttributeName, frame.AttributeValue?.ToString()));
        }
    }

    private void ProcessAttributeEdit(
        ref RenderTreeDiff diff,
        int frameIndex,
        List<RenderPatch> patches)
    {
        ref var frame = ref diff.ReferenceFrames.Array[frameIndex];
        var nodeId = _tree.GetNodeIdBySibling(diff.ComponentId, frameIndex);
        if (nodeId < 0) return;
        ProcessAttribute(nodeId, ref frame, patches);
    }

    private void ProcessTextEdit(
        ref RenderTreeDiff diff,
        int frameIndex,
        int siblingIndex,
        List<RenderPatch> patches)
    {
        ref var frame = ref diff.ReferenceFrames.Array[frameIndex];
        var nodeId = _tree.GetNodeIdBySibling(diff.ComponentId, siblingIndex);
        if (nodeId >= 0) patches.Add(new ReplaceTextPatch(nodeId, frame.TextContent));
    }

    // ── Bridge dispatch ───────────────────────────────────────────────────────

    private async Task DispatchFrameAsync(RenderFrame frame)
    {
        var json = JsonSerializer.Serialize(frame, RendererJsonContext.Default.RenderFrame);
        await _bridge.WriteStorageAsync("__render_frame__", json);
        await _bridge.FetchAsync(new BridgeHttpRequest(
            "blazornative://render",
            "POST",
            json));
    }

    // ── UI event ingestion (native shell → WASM) ──────────────────────────────

    /// <summary>
    /// Called when the native shell routes a UI event back into the renderer.
    /// Dispatches it to the correct Blazor event handler.
    /// </summary>
    public Task DispatchUiEventAsync(NativeUiEvent e)
        => Dispatcher.InvokeAsync(async () =>
        {
            var args = BuildEventArgs(e);
            await DispatchEventAsync(
                new WebEventData(e.HandlerId, args),
                null,
                CancellationToken.None);
        });

    private static EventArgs BuildEventArgs(NativeUiEvent e) => e.EventName switch
    {
        "click"  => new MouseEventArgs(),
        "change" => new ChangeEventArgs { Value = e.Payload },
        "focus"  => new FocusEventArgs(),
        "blur"   => new FocusEventArgs(),
        _        => EventArgs.Empty
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MapElementToNodeType(string elementName) => elementName.ToLowerInvariant() switch
    {
        "button"   => "button",
        "input"    => "input",
        "textarea" => "input",
        "img"      => "image",
        "ul" or "ol" or "div" or "section" or "article" or "main" or "nav" => "view",
        "p" or "span" or "label" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "text",
        "a"        => "button",   // mapped to tappable in native
        "select"   => "picker",
        "scroll" or "overflow" => "scroll",
        _          => "view"
    };

    private static readonly HashSet<string> StyleAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "color", "background", "backgroundColor", "fontSize",
        "fontWeight", "padding", "margin", "width", "height",
        "display", "flex", "flexDirection", "alignItems", "justifyContent"
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _frames.Dispose();
        base.Dispose(disposing);
    }
}
