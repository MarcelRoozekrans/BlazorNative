using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using ZeroAlloc.AsyncEvents;
using ZeroAlloc.Collections;
using ZeroAlloc.Inject;
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

[Singleton]
public sealed class NativeRenderer : BlazorRenderer
{
    private AsyncEventHandler<RenderFrame> _frames = new(InvokeMode.Sequential);
    private readonly NativeWidgetTree _tree = new();
    private int _frameId;

    public event AsyncEvent<RenderFrame> Frames
    {
        add    => _frames.Register(value);
        remove => _frames.Unregister(value);
    }

    public NativeRenderer(IServiceProvider services)
        : base(services, new NativeRendererLoggerFactory())
    {
        // Force the BlazorInterop static ctor (version + accessor probe) to run
        // before the first frame is rendered so layout drift surfaces immediately.
        BlazorInterop.EnsureInitialized();
    }

    // Mono-WASI is single-threaded with no real scheduler — Dispatcher.CreateDefault()
    // returns a dispatcher whose async-state-machine continuations don't unwind
    // synchronously even when the wrapped work completes inline. Phase 2.4 Task 4
    // needs sync-mount to succeed in Main, so we route all work through an inline
    // dispatcher that simply runs the work item on the calling thread.
    public override Dispatcher Dispatcher { get; } = new InlineDispatcher();

    private sealed class InlineDispatcher : Dispatcher
    {
        public override bool CheckAccess() => true;

        public override Task InvokeAsync(Action workItem)
        {
            try { workItem(); return Task.CompletedTask; }
            catch (Exception ex) { return Task.FromException(ex); }
        }

        public override Task InvokeAsync(Func<Task> workItem)
        {
            try { return workItem() ?? Task.CompletedTask; }
            catch (Exception ex) { return Task.FromException(ex); }
        }

        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
        {
            try { return Task.FromResult(workItem()); }
            catch (Exception ex) { return Task.FromException<TResult>(ex); }
        }

        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
        {
            try { return workItem(); }
            catch (Exception ex) { return Task.FromException<TResult>(ex); }
        }
    }

    /// <summary>Convenience overload that explicitly passes <see cref="ParameterView.Empty"/>.
    /// Do NOT collapse this into a single overload with <c>ParameterView parameters = default</c>:
    /// on Blazor's ParameterView (any runtime, not just Mono-WASI AOT), <c>default(ParameterView)</c>
    /// throws NullReferenceException inside ComponentState.SupplyCombinedParameters, which the
    /// renderer's HandleException swallows silently — mount appears to "succeed" (returns a
    /// componentId) but no render fires and no [FRAME] line is emitted. Phase 2.7 Bug A fix
    /// (continuation of Phase 2.4 Task 4 defect #3 finding).</summary>
    public Task<int> MountAsync<TComponent>(CancellationToken ct = default)
        where TComponent : IComponent
        => MountAsync<TComponent>(ParameterView.Empty, ct);

    public Task<int> MountAsync<TComponent>(
        ParameterView parameters,
        CancellationToken ct = default)
        where TComponent : IComponent
        => Dispatcher.InvokeAsync(() => AddComponentAsync(typeof(TComponent), parameters));

    /// <summary>Synchronous mount entry point for hosts without a multi-threaded
    /// scheduler (Mono-WASI Main). Asserts the first render completes synchronously;
    /// throws with a clear diagnostic if the component has async lifecycle work
    /// that requires real scheduler threads. See Phase 2.4 design.</summary>
    ///
    /// Bypasses MountAsync entirely: even with an inline Dispatcher + stripped
    /// async lambda, the Task<int> returned by MountAsync is observed incomplete
    /// on Mono-WASI for a fully-sync component (Phase 2.4 Task 4 investigation —
    /// the async-state-machine wrapping AddComponentAsync's `await Render...` adds
    /// a continuation step that doesn't unwind on the single-threaded WASI scheduler).
    /// Calling Blazor's underlying primitives (InstantiateComponent +
    /// AssignRootComponentId + RenderRootComponentAsync) directly lets us inspect
    /// the inner Task's actual completion state without an extra async wrapper.
    /// <summary>Convenience overload that explicitly passes <see cref="ParameterView.Empty"/>.
    /// Do NOT collapse this into a single overload with <c>ParameterView parameters = default</c>:
    /// on Mono-WASI AOT, <c>default(ParameterView)</c> throws NullReferenceException inside
    /// ComponentState.SupplyCombinedParameters, which the renderer's HandleException swallows
    /// silently — mount appears to "succeed" (returns a componentId) but no render fires and
    /// no [FRAME] line is emitted. Phase 2.4 Task 4 investigation, defect #3.</summary>
    public int Mount<TComponent>() where TComponent : IComponent
        => Mount<TComponent>(ParameterView.Empty);

    public int Mount<TComponent>(ParameterView parameters)
        where TComponent : IComponent
    {
        var component = InstantiateComponent(typeof(TComponent));
        var componentId = AssignRootComponentId(component);
        var task = RenderRootComponentAsync(componentId, parameters);
        if (!task.IsCompletedSuccessfully)
        {
            var inner = task.Exception?.GetBaseException();
            throw new InvalidOperationException(
                $"Mount<T> requires RenderRootComponentAsync to complete synchronously. " +
                $"task.Status={task.Status}; task.Exception={inner?.Message ?? "<none>"}. " +
                "Common causes: component has truly async SetParametersAsync/OnInitializedAsync " +
                "work, or the Dispatcher is no longer inline (see Phase 2.4 Task 4 investigation).",
                inner);
        }
        return componentId;
    }

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
        var batch = new BnRenderBatch(in renderBatch);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frameId = Interlocked.Increment(ref _frameId);

        var patches = new PooledList<RenderPatch>(capacity: 32);

        try
        {
            // Updated components — pass the batch's ReferenceFrames in (in Blazor 10
            // ReferenceFrames lives on RenderBatch, not on RenderTreeDiff).
            var referenceFrames = batch.ReferenceFrames;
            foreach (ref var diff in batch.UpdatedComponents)
            {
                var bnDiff = new BnRenderTreeDiff(in diff);
                ProcessRenderTreeDiff(ref bnDiff, ref referenceFrames, ref patches);
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
            DispatchFrame(frame);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NativeRenderer] frame {frameId} failed: {ex}");
            HandleException(ex);
        }
        finally
        {
            // We cannot use `using var` here: PooledList<T> is a struct whose
            // Add() mutates internal state, so the helper methods take it by
            // `ref`. The C# compiler rejects passing a `using` local by ref
            // (CS1657), forcing the explicit try/finally pattern.
            patches.Dispose();
        }

        return Task.CompletedTask;
    }

    protected override void HandleException(Exception exception)
        => Console.Error.WriteLine($"[BlazorNative.Renderer] {exception}");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Release any handlers registered against Frames. The underlying
            // AsyncEventHandler<T> struct holds delegate references in its
            // internal state; resetting to default releases them so per-test
            // closures don't leak across NativeRenderer instances.
            _frames = default;
        }
        base.Dispose(disposing);
    }

    // ── Render tree walking (typed against Bn* wrappers only) ─────────────────

    private void ProcessRenderTreeDiff(
        ref BnRenderTreeDiff diff,
        ref BnArrayRange<RenderTreeFrame> referenceFrames,
        ref PooledList<RenderPatch> patches)
    {
        var componentId = diff.ComponentId;
        foreach (ref var edit in diff.Edits)
        {
            var bnEdit = new BnRenderTreeEdit(in edit);
            switch ((RenderTreeEditType)bnEdit.Type)
            {
                case RenderTreeEditType.PrependFrame:
                    ProcessFrame(componentId, ref referenceFrames, bnEdit.ReferenceFrameIndex, bnEdit.SiblingIndex, parentNodeId: null, ref patches);
                    break;

                case RenderTreeEditType.RemoveFrame:
                {
                    var nodeId = _tree.GetNodeIdBySibling(componentId, bnEdit.SiblingIndex);
                    if (nodeId >= 0) patches.Add(new RemoveNodePatch(nodeId));
                    break;
                }

                case RenderTreeEditType.SetAttribute:
                    ProcessAttributeEdit(componentId, ref referenceFrames, bnEdit.ReferenceFrameIndex, ref patches);
                    break;

                case RenderTreeEditType.RemoveAttribute:
                {
                    var nodeId = _tree.GetNodeIdBySibling(componentId, bnEdit.SiblingIndex);
                    if (nodeId >= 0 && bnEdit.RemovedAttributeName is not null)
                        patches.Add(new UpdatePropPatch(nodeId, bnEdit.RemovedAttributeName, null));
                    break;
                }

                case RenderTreeEditType.UpdateText:
                    ProcessTextEdit(componentId, ref referenceFrames, bnEdit.ReferenceFrameIndex, bnEdit.SiblingIndex, ref patches);
                    break;
            }
        }
    }

    private void ProcessFrame(
        int componentId,
        ref BnArrayRange<RenderTreeFrame> frames,
        int frameIndex,
        int siblingIndex,
        int? parentNodeId,
        ref PooledList<RenderPatch> patches)
    {
        var frame = new BnRenderTreeFrame(ref frames[frameIndex]);

        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
            {
                var nodeId = _tree.AllocateNode(componentId, siblingIndex);
                var nodeType = MapElementToNodeType(frame.ElementName!);
                patches.Add(new CreateNodePatch(nodeId, nodeType, parentNodeId));

                var subtreeLen = frame.ElementSubtreeLength;
                for (var i = 1; i < subtreeLen; i++)
                {
                    var child = new BnRenderTreeFrame(ref frames[frameIndex + i]);
                    if (child.FrameType == RenderTreeFrameType.Attribute)
                    {
                        ProcessAttribute(nodeId, ref child, ref patches);
                    }
                    else if (child.FrameType == RenderTreeFrameType.Component)
                    {
                        // Component frame inside this element's subtree. We do NOT
                        // emit a patch here — the component's own ComponentRenderTreeDiff
                        // is in the same RenderBatch.UpdatedComponents array and handles
                        // its rendering separately. We MUST advance i past the component's
                        // subtree so its child Attribute frames (carrying its parameter
                        // values like Label="A") aren't mis-attributed to THIS element
                        // by the next loop iteration. Phase 2.7 Bug B fix.
                        i += child.ComponentSubtreeLength - 1;
                    }
                    else
                    {
                        // Non-attribute, non-component child frame inside the subtree —
                        // recurse to emit its create/text patches as a child of this element.
                        // Use the child's index as siblingIndex (positions within the
                        // parent's subtree are unique enough for the M1 mapping). Pass
                        // this element's nodeId as the child's parentNodeId so the host-
                        // side widget mapper can attach the child inside this element's
                        // view (Phase 2.5 design).
                        ProcessFrame(componentId, ref frames, frameIndex + i, frameIndex + i, parentNodeId: nodeId, ref patches);
                        // Skip the child's own subtree so we don't double-walk it.
                        if (child.FrameType == RenderTreeFrameType.Element)
                            i += child.ElementSubtreeLength - 1;
                    }
                }
                break;
            }

            case RenderTreeFrameType.Text:
            {
                var textNodeId = _tree.AllocateNode(componentId, siblingIndex);
                patches.Add(new CreateNodePatch(textNodeId, "text", parentNodeId));
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

    private void ProcessAttributeEdit(
        int componentId,
        ref BnArrayRange<RenderTreeFrame> frames,
        int frameIndex,
        ref PooledList<RenderPatch> patches)
    {
        var frame = new BnRenderTreeFrame(ref frames[frameIndex]);
        var nodeId = _tree.GetNodeIdBySibling(componentId, frameIndex);
        if (nodeId < 0) return;
        ProcessAttribute(nodeId, ref frame, ref patches);
    }

    private void ProcessTextEdit(
        int componentId,
        ref BnArrayRange<RenderTreeFrame> frames,
        int frameIndex,
        int siblingIndex,
        ref PooledList<RenderPatch> patches)
    {
        var frame = new BnRenderTreeFrame(ref frames[frameIndex]);
        var nodeId = _tree.GetNodeIdBySibling(componentId, siblingIndex);
        if (nodeId >= 0) patches.Add(new ReplaceTextPatch(nodeId, frame.TextContent ?? ""));
    }

    // ── Frame dispatch ────────────────────────────────────────────────────────
    //
    // Phase 2.4 transport: tagged [FRAME] line on stdout. Sync — Phase 2.0's
    // sync-contract decision forbids await/Task.Wait on any bridge path
    // (Mono-WASI Task.InternalWaitCore PNSE trap). The host (wasmtime CLI /
    // JVM JNA / Android JNA) captures stdout via wasi_config_set_stdout_file
    // and parses [FRAME] lines via io.blazornative.jni.FrameStreamParser.
    //
    // The previous DispatchFrameAsync awaited bridge.WriteStorageAsync +
    // bridge.FetchAsync — both of which throw NotImplementedException (the
    // shell-* deferred imports per Phase 2.3 BACKLOG). Removed in this phase,
    // along with the IMobileBridge dependency itself (no longer referenced).

    private void DispatchFrame(RenderFrame frame)
    {
        var json = JsonSerializer.Serialize(frame, RendererJsonContext.Default.RenderFrame);
        Console.WriteLine($"[FRAME] {json}");
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
