using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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

    /// <summary>Host-pluggable frame transport. The host installs the struct
    /// marshaller here; null means "no transport" (the <see cref="Frames"/>
    /// event remains the test channel). Synchronous by contract (Phase 2.0).
    /// Threading: set before mount, or from the renderer thread; the property
    /// is not synchronized, so a cross-thread mid-render swap races.</summary>
    public Action<RenderFrame>? FrameSink { get; set; }

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
    /// componentId) but no render fires and no frame reaches the FrameSink / Frames event. Phase 2.7 Bug A fix
    /// (continuation of Phase 2.4 Task 4 defect #3 finding).</summary>
    public Task<int> MountAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(CancellationToken ct = default)
        where TComponent : IComponent
        => MountAsync<TComponent>(ParameterView.Empty, ct);

    public Task<int> MountAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        ParameterView parameters,
        CancellationToken ct = default)
        where TComponent : IComponent
        => Dispatcher.InvokeAsync(() => AddComponentAsync(typeof(TComponent), parameters));

    /// <summary>Convenience overload that explicitly passes <see cref="ParameterView.Empty"/>.
    /// Do NOT collapse this into a single overload with <c>ParameterView parameters = default</c>:
    /// on Mono-WASI AOT, <c>default(ParameterView)</c> throws NullReferenceException inside
    /// ComponentState.SupplyCombinedParameters, which the renderer's HandleException swallows
    /// silently — mount appears to "succeed" (returns a componentId) but no render fires and
    /// no frame reaches the FrameSink / Frames event. Phase 2.4 Task 4 investigation, defect #3.</summary>
    public int Mount<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>() where TComponent : IComponent
        => Mount<TComponent>(ParameterView.Empty);

    /// <summary>Synchronous mount entry point for hosts without a multi-threaded
    /// scheduler (Mono-WASI Main). Asserts the first render completes synchronously;
    /// throws with a clear diagnostic if the component has async lifecycle work
    /// that requires real scheduler threads.</summary>
    /// <remarks>
    /// Bypasses <see cref="MountAsync{TComponent}(ParameterView, CancellationToken)"/> entirely:
    /// even with an inline Dispatcher + stripped async lambda, the <c>Task&lt;int&gt;</c> returned
    /// by MountAsync is observed incomplete on Mono-WASI for a fully-sync component (Phase 2.4
    /// Task 4 investigation — the async-state-machine wrapping <c>AddComponentAsync</c>'s
    /// <c>await Render…</c> adds a continuation step that doesn't unwind on the single-threaded
    /// WASI scheduler). Calling Blazor's underlying primitives (<c>InstantiateComponent</c> +
    /// <c>AssignRootComponentId</c> + <c>RenderRootComponentAsync</c>) directly lets us inspect
    /// the inner Task's actual completion state without an extra async wrapper.
    /// </remarks>
    public int Mount<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(ParameterView parameters)
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

    private async Task<int> AddComponentAsync(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type t,
        ParameterView pv)
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
    {
        // Inside a UI-event dispatch window, remember the first exception so
        // DispatchUiEventAsync can fault its task (Blazor swallows dispatch
        // exceptions here otherwise — see _uiEventDispatchException doc).
        if (_uiEventDispatchDepth > 0)
            _uiEventDispatchException ??= exception;
        Console.Error.WriteLine($"[BlazorNative.Renderer] {exception}");
    }

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

    /// <summary>Cursor value after a failed StepIn: never a real node id and
    /// never NativeWidgetTree's -1 root sentinel, so every edit under an
    /// unknown container resolves to nothing (GetChildAt misses) and
    /// PrependFrame breaks out explicitly — nothing may alias onto the
    /// component root or create a live child-order bucket under the
    /// sentinel.</summary>
    private const int PoisonedCursor = int.MinValue;

    private void ProcessRenderTreeDiff(
        ref BnRenderTreeDiff diff,
        ref BnArrayRange<RenderTreeFrame> referenceFrames,
        ref PooledList<RenderPatch> patches)
    {
        var componentId = diff.ComponentId;

        // Phase 3.2 diff cursor: Blazor addresses re-render edits through a
        // walk — StepIn(siblingIndex) descends into a child of the current
        // container, StepOut pops back, and positional edits (UpdateText)
        // carry a SiblingIndex RELATIVE to the current container. Node ids
        // must be resolved through this cursor: ReferenceFrameIndex is
        // BATCH-relative (each RenderBatch builds its own ReferenceFrames
        // array), so the mount-time (componentId, frameIndex) sibling map is
        // only valid for lookups within the SAME batch (SetAttribute uses it —
        // its edits so far only appear in mount-shaped batches). Before this
        // cursor existed, Hello's counter UpdateText resolved to the OUTER
        // div (node 1) and the on-screen text never changed (Android Gate 3
        // caught it; the JVM tests only asserted patch TEXT, not nodeId).
        // currentParent == null means the component's root level.
        int? currentParent = null;
        var parentStack = new Stack<int?>();

        foreach (ref var edit in diff.Edits)
        {
            var bnEdit = new BnRenderTreeEdit(in edit);
            switch ((RenderTreeEditType)bnEdit.Type)
            {
                case RenderTreeEditType.PrependFrame:
                    // Review follow-up: under a poisoned cursor a prepend must
                    // be dropped outright — emitting it would ship
                    // CreateNodePatch(parent=PoisonedCursor) (Android falls
                    // back to widget_root) AND AppendChildOrder would turn the
                    // poison sentinel into a live child-order bucket,
                    // cross-container aliasing later cursor lookups.
                    if (currentParent == PoisonedCursor) break;
                    ProcessFrame(componentId, ref referenceFrames, bnEdit.ReferenceFrameIndex, bnEdit.SiblingIndex, parentNodeId: currentParent, ref patches);
                    break;

                case RenderTreeEditType.RemoveFrame:
                {
                    var nodeId = _tree.GetChildAt(componentId, currentParent, bnEdit.SiblingIndex);
                    if (nodeId >= 0) patches.Add(new RemoveNodePatch(nodeId));
                    break;
                }

                case RenderTreeEditType.SetAttribute:
                    ProcessAttributeEdit(componentId, ref referenceFrames, bnEdit.ReferenceFrameIndex, ref patches);
                    break;

                case RenderTreeEditType.RemoveAttribute:
                {
                    var nodeId = _tree.GetChildAt(componentId, currentParent, bnEdit.SiblingIndex);
                    if (nodeId >= 0 && bnEdit.RemovedAttributeName is not null)
                        patches.Add(new UpdatePropPatch(nodeId, bnEdit.RemovedAttributeName, null));
                    break;
                }

                case RenderTreeEditType.UpdateText:
                {
                    var nodeId = _tree.GetChildAt(componentId, currentParent, bnEdit.SiblingIndex);
                    ProcessTextEdit(nodeId, ref referenceFrames, bnEdit.ReferenceFrameIndex, ref patches);
                    break;
                }

                case RenderTreeEditType.StepIn:
                {
                    parentStack.Push(currentParent);
                    var stepped = _tree.GetChildAt(componentId, currentParent, bnEdit.SiblingIndex);
                    // A failed StepIn poisons the cursor (see PoisonedCursor):
                    // node-targeting edits inside the unknown container miss
                    // their GetChildAt lookups and PrependFrame breaks out via
                    // its explicit guard above — nothing aliases onto the
                    // component root.
                    currentParent = stepped >= 0 ? stepped : PoisonedCursor;
                    break;
                }

                case RenderTreeEditType.StepOut:
                    currentParent = parentStack.Count > 0 ? parentStack.Pop() : null;
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
                // Phase 3.2: creation order = render-tree sibling order — the
                // diff cursor (StepIn/UpdateText) resolves children by it.
                _tree.AppendChildOrder(componentId, parentNodeId, nodeId);
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
                        // 3.3 carryover (b) on NativeWidgetTree._childOrderMap: this
                        // skip allocates no child-order slot, so a component
                        // interleaved between element siblings offsets the diff
                        // cursor's indices for everything after it.
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
                // Phase 3.2: see the Element case — cursor-order bookkeeping.
                _tree.AppendChildOrder(componentId, parentNodeId, textNodeId);
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

    /// <summary>Emits the ReplaceText for an UpdateText edit. The node was
    /// resolved by the caller through the diff cursor (see
    /// <see cref="ProcessRenderTreeDiff"/>); the reference frame only supplies
    /// the new text content (ReferenceFrameIndex is batch-relative and must
    /// never be used as a node key).</summary>
    private static void ProcessTextEdit(
        int nodeId,
        ref BnArrayRange<RenderTreeFrame> frames,
        int frameIndex,
        ref PooledList<RenderPatch> patches)
    {
        if (nodeId < 0) return;
        var frame = new BnRenderTreeFrame(ref frames[frameIndex]);
        patches.Add(new ReplaceTextPatch(nodeId, frame.TextContent ?? ""));
    }

    // ── Frame dispatch ────────────────────────────────────────────────────────
    //
    // Hands the frame to the host-installed FrameSink (the struct marshaller).
    // Sync — Phase 2.0's sync-contract decision. Null sink = no transport;
    // tests observe frames via the Frames event instead. The Phase 2.4
    // "[FRAME]" stdout fallback was deleted with the WASM era (Phase 3.0e).

    private void DispatchFrame(RenderFrame frame)
    {
        if (FrameSink is { } sink)
            sink(frame);
    }

    // ── Event ingestion ───────────────────────────────────────────────────────

    /// <summary>Captures the first exception Blazor routes to
    /// <see cref="HandleException"/> during a <see cref="DispatchUiEventAsync"/>
    /// window. Blazor's Renderer.DispatchEventAsync does NOT propagate such
    /// exceptions to its caller — they go to HandleExceptionViaErrorBoundary →
    /// (no error boundary here) → HandleException, and the returned task
    /// completes successfully. Without this capture,
    /// blazornative_dispatch_event could never honor its "2 = dispatch
    /// faulted" contract (Phase 3.2, DoD #9 partial). Note the capture is a
    /// WINDOW, not a handler hook: anything routed to HandleException while
    /// the window is open is captured — the handler itself, the resulting
    /// re-render (UpdateDisplayAsync failures land here too), or frame
    /// delivery.
    ///
    /// The depth counter (not a bool) keeps the window correct for NESTED
    /// dispatches (a handler that itself calls DispatchUiEventAsync): the
    /// slot is only cleared + rethrown when the OUTERMOST dispatch unwinds,
    /// and is never reset at nested-dispatch start — so an outer handler's
    /// throw cannot be discarded by an inner dispatch.
    ///
    /// These guarantees assume SYNCHRONOUS handlers; async handlers (await in
    /// @onclick) move continuations off the dispatch thread and out of this
    /// window — revisit in 3.3+.
    ///
    /// Instance fields are safe: all dispatch runs on the InlineDispatcher's
    /// calling thread (single-threaded post-boot contract).</summary>
    private Exception? _uiEventDispatchException;
    private int _uiEventDispatchDepth;

    /// <summary>Dispatches a host UI event into Blazor's handler table.
    /// Synchronous in effect (InlineDispatcher): handler, re-render, and
    /// FrameSink delivery have all completed when the returned task is
    /// observed. Stale handler ids (ArgumentException from a handler that
    /// died in a re-render) are caught + logged — delivery is at-most-once,
    /// a stale tap is not an error. A fault anywhere in the dispatch window —
    /// the handler, the resulting re-render, or frame delivery — faults the
    /// returned task (see <see cref="_uiEventDispatchException"/>) so the
    /// export can map it to return code 2.</summary>
    public Task DispatchUiEventAsync(NativeUiEvent e)
        => Dispatcher.InvokeAsync(async () =>
        {
            _uiEventDispatchDepth++;
            try
            {
                var args = BuildEventArgs(e);
                await BlazorInterop.DispatchEventViaAccessor(this, (ulong)e.HandlerId, args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"[NativeRenderer] stale handler {e.HandlerId}: {ex.Message}");
            }
            finally
            {
                _uiEventDispatchDepth--;
            }

            if (_uiEventDispatchDepth == 0 && _uiEventDispatchException is { } dispatchEx)
            {
                _uiEventDispatchException = null;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(dispatchEx).Throw();
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
