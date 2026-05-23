using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using ZeroAlloc.AsyncEvents;
using ZeroAlloc.Collections;
using BlazorNative.Core;
using BlazorRenderer = Microsoft.AspNetCore.Components.RenderTree.Renderer;

namespace BlazorNative.Renderer;

// ─────────────────────────────────────────────────────────────────────────────
// NativeRenderer
//
// Headless Blazor renderer. All access to internal render-tree types goes
// through BlazorInterop.cs (Bn* ref struct wrappers). The renderer itself
// never references RenderTreeDiff / RenderTreeEdit / RenderTreeFrame /
// ArrayRange<T> directly — those names should appear nowhere else in this file.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NativeRenderer : BlazorRenderer
{
    private readonly IMobileBridge _bridge;
    private AsyncEventHandler<RenderFrame> _frames = new(InvokeMode.Sequential);
    private readonly NativeWidgetTree _tree = new();
    private int _frameId;

    public event AsyncEvent<RenderFrame> Frames
    {
        add    => _frames.Register(value);
        remove => _frames.Unregister(value);
    }

    public NativeRenderer(IMobileBridge bridge, IServiceProvider services)
        : base(services, new NativeRendererLoggerFactory())
    {
        _bridge = bridge;
    }

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    public Task<int> MountAsync<TComponent>(
        ParameterView parameters = default,
        CancellationToken ct = default)
        where TComponent : IComponent
        => Dispatcher.InvokeAsync(async ()
            => await AddComponentAsync(typeof(TComponent), parameters));

    private async Task<int> AddComponentAsync(Type t, ParameterView pv)
    {
        var component = InstantiateComponent(t);
        var componentId = AssignRootComponentId(component);
        await RenderRootComponentAsync(componentId, pv);
        return componentId;
    }

    // ── UpdateDisplayAsync ────────────────────────────────────────────────────

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        var batch = new BnRenderBatch(ref Unsafe.AsRef(in renderBatch));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frameId = Interlocked.Increment(ref _frameId);

        var patches = new PooledList<RenderPatch>(capacity: 32);

        try
        {
            // Updated components
            foreach (ref var diff in batch.UpdatedComponents)
            {
                var bnDiff = new BnRenderTreeDiff(ref diff);
                ProcessRenderTreeDiff(ref bnDiff, ref patches);
            }

            // Disposed components
            foreach (ref var disposedId in batch.DisposedComponentIDs)
            {
                var nodeId = _tree.GetNodeId(disposedId);
                if (nodeId >= 0)
                    patches.Add(new RemoveNodePatch(nodeId));
            }

            patches.Add(new CommitFramePatch(frameId, timestamp));

            var frame = new RenderFrame(frameId, timestamp, patches.AsSpan().ToArray());
            _ = _frames.InvokeAsync(frame, default);
            _ = DispatchFrameAsync(frame);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NativeRenderer] frame {frameId} failed: {ex}");
            HandleException(ex);
        }
        finally
        {
            patches.Dispose();
        }

        return Task.CompletedTask;
    }

    protected override void HandleException(Exception exception)
        => Console.Error.WriteLine($"[BlazorNative.Renderer] {exception}");

    // ── Render tree walking (typed against Bn* wrappers only) ─────────────────

    private void ProcessRenderTreeDiff(ref BnRenderTreeDiff diff, ref PooledList<RenderPatch> patches)
    {
        foreach (ref var edit in diff.Edits)
        {
            var bnEdit = new BnRenderTreeEdit(ref edit);
            switch ((RenderTreeEditType)bnEdit.Type)
            {
                case RenderTreeEditType.PrependFrame:
                    ProcessFrame(ref diff, bnEdit.ReferenceFrameIndex, bnEdit.SiblingIndex, ref patches);
                    break;

                case RenderTreeEditType.RemoveFrame:
                {
                    var nodeId = _tree.GetNodeIdBySibling(diff.ComponentId, bnEdit.SiblingIndex);
                    if (nodeId >= 0) patches.Add(new RemoveNodePatch(nodeId));
                    break;
                }

                case RenderTreeEditType.SetAttribute:
                    ProcessAttributeEdit(ref diff, bnEdit.ReferenceFrameIndex, ref patches);
                    break;

                case RenderTreeEditType.RemoveAttribute:
                {
                    var nodeId = _tree.GetNodeIdBySibling(diff.ComponentId, bnEdit.SiblingIndex);
                    if (nodeId >= 0 && bnEdit.RemovedAttributeName is not null)
                        patches.Add(new UpdatePropPatch(nodeId, bnEdit.RemovedAttributeName, null));
                    break;
                }

                case RenderTreeEditType.UpdateText:
                    ProcessTextEdit(ref diff, bnEdit.ReferenceFrameIndex, bnEdit.SiblingIndex, ref patches);
                    break;
            }
        }
    }

    private void ProcessFrame(ref BnRenderTreeDiff diff, int frameIndex, int siblingIndex, ref PooledList<RenderPatch> patches)
    {
        var frames = diff.ReferenceFrames;
        var frame = new BnRenderTreeFrame(ref frames[frameIndex]);

        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
            {
                var nodeId = _tree.AllocateNode(diff.ComponentId, siblingIndex);
                var nodeType = MapElementToNodeType(frame.ElementName!);
                patches.Add(new CreateNodePatch(nodeId, nodeType));

                for (var i = 1; i <= frame.ElementSubtreeLength - 1; i++)
                {
                    var child = new BnRenderTreeFrame(ref frames[frameIndex + i]);
                    if (child.FrameType == RenderTreeFrameType.Attribute)
                        ProcessAttribute(nodeId, ref child, ref patches);
                    else
                        break;
                }
                break;
            }

            case RenderTreeFrameType.Text:
            {
                var textNodeId = _tree.AllocateNode(diff.ComponentId, siblingIndex);
                patches.Add(new CreateNodePatch(textNodeId, "text"));
                patches.Add(new ReplaceTextPatch(textNodeId, frame.TextContent ?? ""));
                break;
            }
        }
    }

    private static void ProcessAttribute(int nodeId, ref BnRenderTreeFrame frame, ref PooledList<RenderPatch> patches)
    {
        var name = frame.AttributeName ?? "";
        var value = frame.AttributeValue?.ToString();

        if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            patches.Add(new AttachEventPatch(
                nodeId,
                name[2..].ToLowerInvariant(),
                (int)frame.AttributeEventHandlerId));
        }
        else if (StyleAttributes.Contains(name))
        {
            patches.Add(new SetStylePatch(nodeId, name, value));
        }
        else
        {
            patches.Add(new UpdatePropPatch(nodeId, name, value));
        }
    }

    private void ProcessAttributeEdit(ref BnRenderTreeDiff diff, int frameIndex, ref PooledList<RenderPatch> patches)
    {
        var frames = diff.ReferenceFrames;
        var frame = new BnRenderTreeFrame(ref frames[frameIndex]);
        var nodeId = _tree.GetNodeIdBySibling(diff.ComponentId, frameIndex);
        if (nodeId < 0) return;
        ProcessAttribute(nodeId, ref frame, ref patches);
    }

    private void ProcessTextEdit(ref BnRenderTreeDiff diff, int frameIndex, int siblingIndex, ref PooledList<RenderPatch> patches)
    {
        var frames = diff.ReferenceFrames;
        var frame = new BnRenderTreeFrame(ref frames[frameIndex]);
        var nodeId = _tree.GetNodeIdBySibling(diff.ComponentId, siblingIndex);
        if (nodeId >= 0) patches.Add(new ReplaceTextPatch(nodeId, frame.TextContent ?? ""));
    }

    // ── Bridge dispatch ───────────────────────────────────────────────────────

    private async Task DispatchFrameAsync(RenderFrame frame)
    {
        var json = JsonSerializer.Serialize(frame, RendererJsonContext.Default.RenderFrame);
        await _bridge.WriteStorageAsync("__render_frame__", json);
        await _bridge.FetchAsync(new BridgeHttpRequest("blazornative://render", "POST", json));
    }

    // ── Event ingestion ───────────────────────────────────────────────────────

    public Task DispatchUiEventAsync(NativeUiEvent e)
        => Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var args = BuildEventArgs(e);
                await BlazorInterop.DispatchEventViaAccessor(this, (ulong)e.HandlerId, args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"[NativeRenderer] stale handler {e.HandlerId}: {ex.Message}");
            }
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
        "a"        => "button",
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
}
