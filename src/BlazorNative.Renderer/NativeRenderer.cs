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

    /// <summary>Test-only view of the slot bookkeeping (Phase 3.3): DiffCursor /
    /// SlotList tests assert slot order and view-index translation directly.
    /// Never used by production hosts.</summary>
    internal NativeWidgetTree WidgetTree => _tree;

    // ── Event-handler registry (Phase 3.3 Task 5, carryover e) ────────────────
    //
    // nodeId → (eventName → handlerId), maintained at the AttachEvent emission
    // site (ProcessAttribute). Blazor's RemoveAttribute edit carries only the
    // attribute name — resolving the ORIGINAL handlerId for DetachEventPatch
    // needs this. Re-attach overwrites (last wins, mirroring Blazor's handler
    // table); entries clean on node removal (RemoveFrame + subtree purge) and
    // component disposal, so the registry cannot leak across teardowns.
    private readonly Dictionary<int, Dictionary<string, int>> _eventHandlers = new();

    /// <summary>Test-only: live (node, event) registrations — cleanup tests
    /// assert the registry doesn't accrete after node/component teardown.</summary>
    internal int EventRegistrationCount => _eventHandlers.Values.Sum(e => e.Count);

    private void RegisterEventHandler(int nodeId, string eventName, int handlerId)
    {
        if (!_eventHandlers.TryGetValue(nodeId, out var events))
            _eventHandlers[nodeId] = events = new Dictionary<string, int>();
        events[eventName] = handlerId;
    }

    private void RemoveNodeEventRegistrations(int nodeId)
        => _eventHandlers.Remove(nodeId);

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

    /// <summary>Phase 3.3 Task 6 (DoD #9). When true, exceptions Blazor routes
    /// to <see cref="HandleException"/> OUTSIDE the 3.2 event-dispatch capture
    /// window rethrow synchronously (ExceptionDispatchInfo — original stack)
    /// at the caller boundary (mount / batch) instead of logging; renderer
    /// contract violations (poisoned cursor, out-of-range diff-provided
    /// sibling index) raise through the same switch. INSIDE the dispatch
    /// window the 3.2 capture still wins (the dispatch task faults → export
    /// rc 2; no double-report).
    /// Default FALSE — the deliberate production POC posture: renderer errors
    /// log to stderr rather than crash the host process (a diagnostics
    /// surface is M4+ work). ALL test harnesses enable it: this silent
    /// swallow hid Bug A, Bug B, and the 3.2 diff-cursor bug for days each.
    /// Threading: set before mount, or from the renderer thread; the property
    /// is not synchronized, so a cross-thread mid-render flip races (same
    /// contract as <see cref="FrameSink"/>).</summary>
    public bool StrictErrors { get; set; }

    public NativeRenderer(IServiceProvider services)
        : base(services, new NativeRendererLoggerFactory())
    {
        // Force the BlazorInterop static ctor (version + accessor probe) to run
        // before the first frame is rendered so layout drift surfaces immediately.
        BlazorInterop.EnsureInitialized();

        // Quiet-fallback WARNINGS from the tree's host-index translation
        // (trimmed-slot → append): visible under strict mode, silently
        // tolerated otherwise — a warning, not a violation (the fallback is
        // still applied), so it never throws.
        _tree.ContractWarning = message =>
        {
            if (StrictErrors)
                Console.Error.WriteLine($"[NativeRenderer] contract warning: {message}");
        };
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

            // Disposed components (Phase 3.3 Task 3): remove their views,
            // their sibling slot, and their bookkeeping.
            foreach (ref var disposedId in batch.DisposedComponentIDs)
                ProcessDisposedComponent(disposedId, ref patches);

            patches.Add(new CommitFramePatch(frameId, timestamp));

            var frame = new RenderFrame(frameId, timestamp, patches.AsSpan().ToArray());
            _ = _frames.InvokeAsync(frame, default);
            DispatchFrame(frame);
        }
        catch (Exception ex)
        {
            // Non-strict only: strict mode promises SURFACING over logging —
            // HandleException rethrows (or the dispatch window captures and
            // logs), so logging here too would double-report the same
            // exception. Non-strict keeps the frame-id context line.
            if (!StrictErrors)
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
        // The window wins over strict mode: the fault surfaces ONCE, at the
        // dispatch boundary (export rc 2) — never from this stack.
        if (_uiEventDispatchDepth > 0)
        {
            _uiEventDispatchException ??= exception;
            Console.Error.WriteLine($"[BlazorNative.Renderer] {exception}");
            return;
        }

        // Strict mode (Phase 3.3 Task 6, DoD #9): rethrow with the original
        // stack — the exception surfaces synchronously at whatever boundary
        // invoked the render work (mount, batch). See StrictErrors doc for
        // why production stays on the log path below.
        if (StrictErrors)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();

        Console.Error.WriteLine($"[BlazorNative.Renderer] {exception}");
    }

    /// <summary>Renderer contract violations (Phase 3.3 Task 6): a poisoned
    /// cursor or an out-of-range diff-provided sibling index is a genuine bug
    /// once Tasks 1-3 fixed the legitimate causes. Strict: throw (inside a
    /// render batch the throw routes through UpdateDisplayAsync's catch →
    /// <see cref="HandleException"/>, so window/boundary semantics match any
    /// other renderer error). Non-strict: stderr + drop, the POC posture.</summary>
    private void ReportContractViolation(string message)
    {
        if (StrictErrors)
            throw new InvalidOperationException($"[NativeRenderer] contract violation: {message}");
        Console.Error.WriteLine($"[NativeRenderer] contract violation (dropped): {message}");
    }

    /// <summary>Test-only: routes a message through the contract-violation
    /// switch. Poisoned-cursor / clamp situations are not constructible from
    /// legal Blazor diffs anymore, so StrictModeTests injects here — the same
    /// path the production guards call.</summary>
    internal void InjectContractViolationForTests(string message)
        => ReportContractViolation(message);

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
    /// unknown container resolves to nothing (GetSlotAt misses) and
    /// PrependFrame breaks out explicitly — nothing may alias onto the
    /// component root or create a live child-order bucket under the
    /// sentinel.</summary>
    private const int PoisonedCursor = int.MinValue;

    /// <summary>Diff-cursor state (Phase 3.3 Task 3). <paramref name="ComponentId"/>
    /// selects whose slot lists the cursor addresses (StepIn into a component
    /// slot descends into THAT component's root-level list).
    /// <paramref name="Container"/> keys the slot-list bucket (null = the
    /// cursor component's root level; PoisonedCursor after a failed StepIn).
    /// <paramref name="EmitParent"/> is the HOST node new views attach to —
    /// it differs from Container only at a component's root level, where slots
    /// live in the component's own root bucket but views attach to the parent
    /// component's container node (null = host root).</summary>
    private readonly record struct DiffCursor(int ComponentId, int? Container, int? EmitParent)
    {
        public bool Poisoned => Container == PoisonedCursor;
    }

    /// <summary>Host node a component's root-level views attach to: the
    /// container recorded when the parent's diff walked this component's
    /// frame (component-parent map, DoD #8) — or the host root for the
    /// root component.</summary>
    private int? ResolveComponentEmitParent(int componentId)
        => _tree.TryGetComponentParent(componentId, out var parent) ? parent.ParentNodeId : null;

    private void ProcessRenderTreeDiff(
        ref BnRenderTreeDiff diff,
        ref BnArrayRange<RenderTreeFrame> referenceFrames,
        ref PooledList<RenderPatch> patches)
    {
        var componentId = diff.ComponentId;

        // Phase 3.2 diff cursor, completed in 3.3 Task 2: Blazor addresses
        // re-render edits through a walk — StepIn(siblingIndex) descends into
        // a child of the current container, StepOut pops back, and positional
        // edits (PrependFrame/RemoveFrame/UpdateText/SetAttribute/
        // RemoveAttribute) carry a SiblingIndex RELATIVE to the current
        // container. EVERY node resolution goes through this cursor:
        // ReferenceFrameIndex is BATCH-relative (each RenderBatch builds its
        // own ReferenceFrames array) and is never a node key — the 3.2-era
        // (componentId, frameIndex) sibling map that SetAttribute leaned on
        // is deleted. Before this cursor existed, Hello's counter UpdateText
        // resolved to the OUTER div (node 1) and the on-screen text never
        // changed (Android Gate 3 caught it; the JVM tests only asserted
        // patch TEXT, not nodeId).
        //
        // Phase 3.3 Task 3: the cursor tracks (ComponentId, Container,
        // EmitParent) — this component's diff addresses ITS slot lists, but
        // its root-level views attach to the parent component's container
        // node (DoD #8). Container == null means the cursor component's root
        // level.
        var rootCursor = new DiffCursor(componentId, Container: null,
            EmitParent: ResolveComponentEmitParent(componentId));
        var cursor = rootCursor;
        var cursorStack = new Stack<DiffCursor>();

        foreach (ref var edit in diff.Edits)
        {
            var bnEdit = new BnRenderTreeEdit(in edit);
            switch ((RenderTreeEditType)bnEdit.Type)
            {
                case RenderTreeEditType.PrependFrame:
                    // Under a poisoned cursor a prepend must never be applied —
                    // emitting it would ship CreateNodePatch(parent=
                    // PoisonedCursor) (Android falls back to widget_root) AND
                    // the slot insert would turn the poison sentinel into a
                    // live slot-list bucket, cross-container aliasing later
                    // cursor lookups. Task 6: a genuine contract violation now
                    // (Tasks 1-3 fixed the legitimate causes) — strict throws,
                    // non-strict drops the edit.
                    if (cursor.Poisoned)
                    {
                        ReportContractViolation(
                            $"PrependFrame at sibling {bnEdit.SiblingIndex} under a poisoned cursor " +
                            $"(component {cursor.ComponentId}) — edit dropped");
                        break;
                    }
                    // Phase 3.3 Task 2: the edit's SiblingIndex is the slot
                    // position — mid-list prepends insert there, not at the end.
                    ProcessFrame(cursor.ComponentId, ref referenceFrames, bnEdit.ReferenceFrameIndex,
                        slotContainer: cursor.Container, emitParent: cursor.EmitParent,
                        insertAtSlot: bnEdit.SiblingIndex, ref patches);
                    break;

                case RenderTreeEditType.RemoveFrame:
                {
                    // Phase 3.3 Task 2: trim the slot so later sibling indices
                    // in this and future diffs keep resolving. A removed
                    // COMPONENT slot emits no patch here — the child's views
                    // and bookkeeping are torn down by DisposedComponentIDs
                    // in this same batch.
                    var removed = _tree.RemoveSlot(cursor.ComponentId, cursor.Container, bnEdit.SiblingIndex);
                    if (removed.IsNode)
                    {
                        patches.Add(new RemoveNodePatch(removed.NodeId));
                        // Event registrations die with the node + its subtree
                        // (RemoveNode subsumes detach — no DetachEventPatch).
                        RemoveNodeEventRegistrations(removed.NodeId);
                        _tree.PurgeNodeSubtree(cursor.ComponentId, removed.NodeId, RemoveNodeEventRegistrations);
                    }
                    break;
                }

                case RenderTreeEditType.SetAttribute:
                {
                    // Phase 3.3 Task 2: resolve through the cursor — the edit's
                    // SiblingIndex addresses the element in the CURRENT
                    // container; the reference frame only carries the new
                    // attribute value. (The old batch-relative
                    // (componentId, frameIndex) sibling map is deleted: a
                    // ReferenceFrameIndex is only meaningful within its own
                    // batch and was never a node key.)
                    var slot = _tree.GetSlotAt(cursor.ComponentId, cursor.Container, bnEdit.SiblingIndex);
                    if (slot.IsNode)
                    {
                        var attrFrame = new BnRenderTreeFrame(ref referenceFrames[bnEdit.ReferenceFrameIndex]);
                        ProcessAttribute(slot.NodeId, ref attrFrame, ref patches);
                    }
                    break;
                }

                case RenderTreeEditType.RemoveAttribute:
                {
                    var slot = _tree.GetSlotAt(cursor.ComponentId, cursor.Container, bnEdit.SiblingIndex);
                    if (slot.IsNode && bnEdit.RemovedAttributeName is { } removedName)
                    {
                        if (removedName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                        {
                            // Phase 3.3 Task 5 (carryover e): an on* removal is
                            // a genuine detach. Resolve the ORIGINAL handlerId
                            // through the registry (the edit carries only the
                            // name); no registry entry means the attach never
                            // reached the host — emit nothing.
                            var eventName = removedName[2..].ToLowerInvariant();
                            if (_eventHandlers.TryGetValue(slot.NodeId, out var events)
                                && events.Remove(eventName, out var handlerId))
                            {
                                patches.Add(new DetachEventPatch(slot.NodeId, handlerId, eventName));
                                if (events.Count == 0)
                                    _eventHandlers.Remove(slot.NodeId);
                            }
                        }
                        else
                        {
                            patches.Add(new UpdatePropPatch(slot.NodeId, removedName, null));
                        }
                    }
                    break;
                }

                case RenderTreeEditType.UpdateText:
                {
                    var slot = _tree.GetSlotAt(cursor.ComponentId, cursor.Container, bnEdit.SiblingIndex);
                    ProcessTextEdit(slot.IsNode ? slot.NodeId : -1, ref referenceFrames, bnEdit.ReferenceFrameIndex, ref patches);
                    break;
                }

                case RenderTreeEditType.StepIn:
                {
                    cursorStack.Push(cursor);
                    var stepped = _tree.GetSlotAt(cursor.ComponentId, cursor.Container, bnEdit.SiblingIndex);
                    // Node slot: descend into that view's child list. Component
                    // slot: descend into THAT component's root-level slot list
                    // (its views attach at its recorded host container). A
                    // failed StepIn poisons the cursor (see PoisonedCursor):
                    // node-targeting edits inside the unknown container miss
                    // their GetSlotAt lookups and PrependFrame breaks out via
                    // its explicit guard above — nothing aliases onto the
                    // component root.
                    cursor = stepped.Kind switch
                    {
                        SlotKind.Node => cursor with { Container = stepped.NodeId, EmitParent = stepped.NodeId },
                        SlotKind.Component => new DiffCursor(stepped.ComponentId, Container: null,
                            EmitParent: ResolveComponentEmitParent(stepped.ComponentId)),
                        _ => cursor with { Container = PoisonedCursor },
                    };
                    break;
                }

                case RenderTreeEditType.StepOut:
                    cursor = cursorStack.Count > 0 ? cursorStack.Pop() : rootCursor;
                    break;
            }
        }
    }

    /// <summary>Walks the reference-frame subtree at <paramref name="frameIndex"/>,
    /// emitting create/text/attribute patches. <paramref name="insertAtSlot"/> is
    /// the slot position for the subtree ROOT in its container's slot list
    /// (a PrependFrame edit's SiblingIndex); -1 = append (the recursive child
    /// walk — creation order IS sibling order inside a fresh subtree).
    /// Returns the number of SIBLING SLOTS the frame consumed in the current
    /// container: 1 for element/text/component, the transparent child sum for
    /// a Region (regions occupy no slot of their own), 0 otherwise. The return
    /// value is only CONSUMED on the DEFENSIVE region-root-prepend path (a
    /// Region arriving with insertAtSlot >= 0, where nested regions must
    /// advance consecutive insert positions) — a path Blazor 10.0.x never
    /// emits, since RenderTreeDiffBuilder decomposes region inserts per-child;
    /// see the Region arm's reality-check note (Phase 3.4 Task 1).</summary>
    /// <remarks><paramref name="slotContainer"/> keys the slot-list bucket the
    /// subtree ROOT's slot goes into (null = the component's root level);
    /// <paramref name="emitParent"/> is the HOST node its view attaches to.
    /// They differ only at a component's root level (see <see cref="DiffCursor"/>);
    /// inside the walk both become the enclosing element's node.</remarks>
    private int ProcessFrame(
        int componentId,
        ref BnArrayRange<RenderTreeFrame> frames,
        int frameIndex,
        int? slotContainer,
        int? emitParent,
        int insertAtSlot,
        ref PooledList<RenderPatch> patches)
    {
        var frame = new BnRenderTreeFrame(ref frames[frameIndex]);

        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
            {
                var nodeId = _tree.AllocateNode();
                // Host insert position BEFORE the slot goes in (the new slot
                // must not count itself): -1 for appends — both the recursive
                // subtree walk (insertAtSlot -1) and diff inserts with nothing
                // after them anywhere in the host container (Task 4, DoD #10).
                var insertIndex = insertAtSlot >= 0
                    ? _tree.TranslateToHostInsertIndex(componentId, slotContainer, insertAtSlot)
                    : -1;
                AddSlot(componentId, slotContainer, insertAtSlot, Slot.ForNode(nodeId));
                var nodeType = MapElementToNodeType(frame.ElementName!);
                patches.Add(new CreateNodePatch(nodeId, nodeType, emitParent, insertIndex));

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
                        // Phase 3.3 Task 3 (carryover b + DoD #8): the component
                        // OCCUPIES a sibling slot here, and the component-parent
                        // map records where its own diff must root its views.
                        _tree.AppendSlot(componentId, nodeId, Slot.ForComponent(child.ComponentId));
                        _tree.RegisterComponentParent(child.ComponentId, componentId, nodeId);
                        i += child.ComponentSubtreeLength - 1;
                    }
                    else
                    {
                        // Non-attribute, non-component child frame inside the subtree —
                        // recurse to emit its create/text patches as a child of this
                        // element (slot appended: creation order IS sibling order in a
                        // fresh subtree). Pass this element's nodeId as the child's
                        // container AND host parent so the host-side widget mapper
                        // attaches the child inside this element's view (Phase 2.5).
                        // A Region child (Phase 3.4 Task 1) descends transparently
                        // via ProcessFrame's Region arm — its children land in THIS
                        // element's slot list, exactly as if written inline.
                        ProcessFrame(componentId, ref frames, frameIndex + i, slotContainer: nodeId, emitParent: nodeId, insertAtSlot: -1, ref patches);
                        // Skip the child's own subtree so we don't double-walk it.
                        // (Before 3.4 a Region child was skipped by only 1 here —
                        // the walk then iterated INTO the region's children, which
                        // happened to produce transparent numbering by accident.
                        // With the explicit Region arm descending, failing to skip
                        // the full region subtree would double-create its content.)
                        i += SubtreeLength(in child) - 1;
                    }
                }
                return 1;
            }

            case RenderTreeFrameType.Text:
            {
                var textNodeId = _tree.AllocateNode();
                // Same insert-position rule as the Element case above.
                var insertIndex = insertAtSlot >= 0
                    ? _tree.TranslateToHostInsertIndex(componentId, slotContainer, insertAtSlot)
                    : -1;
                AddSlot(componentId, slotContainer, insertAtSlot, Slot.ForNode(textNodeId));
                patches.Add(new CreateNodePatch(textNodeId, "text", emitParent, insertIndex));
                patches.Add(new ReplaceTextPatch(textNodeId, frame.TextContent ?? ""));
                return 1;
            }

            case RenderTreeFrameType.Component:
            {
                // Phase 3.3 Task 3: a component frame prepended directly into
                // the current container (e.g. a root-level child component).
                // It occupies a sibling slot but owns no view — no patch. Its
                // own diff (later in this batch, or any future one) roots its
                // views at emitParent via the component-parent map. Attribute
                // frames in its subtree are its parameters — not walked.
                AddSlot(componentId, slotContainer, insertAtSlot, Slot.ForComponent(frame.ComponentId));
                _tree.RegisterComponentParent(frame.ComponentId, componentId, emitParent);
                return 1;
            }

            case RenderTreeFrameType.Region:
            {
                // Phase 3.4 Task 1 (the 3.3 MUST-FIX carryover). Blazor emits
                // Region frames for RenderFragment / CascadingValue ChildContent:
                // grouping markers that occupy NO sibling slot — their children
                // number as if inline in the enclosing container (region-
                // transparent sibling numbering), and regions nest. Descend with
                // the SAME slot container + emit parent; each slot-occupying
                // child advances the insert position, so region content arriving
                // as a mid-list insert lands at CONSECUTIVE slot positions.
                //
                // Reality check against Blazor 10.0.x (RenderTreeDiffBuilder):
                // region inserts/removes are decomposed into per-child edits and
                // same-sequence regions diff via transparent recursion, so a
                // PrependFrame's reference root is never a Region there — this
                // arm is reached for Region frames nested inside a prepended
                // ELEMENT subtree (the recursive walk above), and defensively
                // covers a region-root prepend should a future Blazor emit one.
                // Before 3.4 this frame type hit no arm at all: the element
                // walk's fall-through happened to iterate into region children
                // (accidental transparency) — now the descent is explicit.
                var slotsConsumed = 0;
                var childSlot = insertAtSlot;
                var end = frameIndex + frame.RegionSubtreeLength;
                var childIndex = frameIndex + 1;
                while (childIndex < end)
                {
                    var child = new BnRenderTreeFrame(ref frames[childIndex]);
                    var consumed = ProcessFrame(componentId, ref frames, childIndex,
                        slotContainer, emitParent, childSlot, ref patches);
                    slotsConsumed += consumed;
                    if (childSlot >= 0)
                        childSlot += consumed;
                    childIndex += SubtreeLength(in child);
                }
                return slotsConsumed;
            }
        }

        // Frame types the walk deliberately ignores (e.g. ElementReferenceCapture,
        // NamedEvent) consume no sibling slot.
        return 0;
    }

    /// <summary>Total frame count of the subtree rooted at <paramref name="frame"/>
    /// — the walk-skip distance past a child frame. Subtree-less frame types
    /// (Text, Attribute, ElementReferenceCapture, …) are 1. THE one place to
    /// extend when a future frame type with a subtree joins the walk.</summary>
    private static int SubtreeLength(in BnRenderTreeFrame frame) => frame.FrameType switch
    {
        RenderTreeFrameType.Element   => frame.ElementSubtreeLength,
        RenderTreeFrameType.Component => frame.ComponentSubtreeLength,
        RenderTreeFrameType.Region    => frame.RegionSubtreeLength,
        _ => 1,
    };

    /// <summary>Slot bookkeeping for a freshly created slot: insert at the
    /// diff-provided sibling position, or append for subtree-walk children.
    /// Task 6: an out-of-range DIFF-PROVIDED index is a Blazor-contract
    /// violation — strict throws, non-strict clamps and continues (the
    /// InsertSlotAt clamp). Appends never hit this check (see AppendSlot).</summary>
    private void AddSlot(int componentId, int? slotContainer, int insertAtSlot, Slot slot)
    {
        if (insertAtSlot >= 0)
        {
            var count = _tree.GetSlotCount(componentId, slotContainer);
            if (insertAtSlot > count)
                ReportContractViolation(
                    $"diff-provided sibling index {insertAtSlot} exceeds slot count {count} " +
                    $"(component {componentId}, container {slotContainer?.ToString() ?? "root"}) — clamped");
            _tree.InsertSlotAt(componentId, slotContainer, insertAtSlot, slot);
        }
        else
        {
            _tree.AppendSlot(componentId, slotContainer, slot);
        }
    }

    /// <summary>Handles one entry of RenderBatch.DisposedComponentIDs (Phase
    /// 3.3 Task 3): emits RemoveNodePatch for the component's ROOT-level views
    /// (their subtrees ride along on the host), trims its sibling slot from
    /// the recorded parent container (no-op when the parent's RemoveFrame edit
    /// already trimmed it), and purges its slot lists + component-parent map
    /// entry. Components disposed together each appear in the array and clean
    /// themselves — nested markers need no recursion here.</summary>
    /// <remarks>HOST CONTRACT: hosts must tolerate RemoveNodePatch for nodes
    /// inside already-removed subtrees. When an ANCESTOR element containing a
    /// child component is removed (RemoveFrame → RemoveNodePatch for the
    /// ancestor), the child's disposal in the same batch still emits
    /// RemoveNodePatch for its root views — views the host already detached
    /// with the ancestor's subtree. Treat unknown node ids in RemoveNode as a
    /// no-op (WidgetMapper does). Suppressing the redundant patches renderer-
    /// side would require host-subtree tracking the slot model deliberately
    /// doesn't keep.</remarks>
    private void ProcessDisposedComponent(int componentId, ref PooledList<RenderPatch> patches)
    {
        var rootSlots = _tree.GetSlotCount(componentId, parentNodeId: null);
        for (var i = 0; i < rootSlots; i++)
        {
            var slot = _tree.GetSlotAt(componentId, parentNodeId: null, i);
            if (slot.IsNode)
                patches.Add(new RemoveNodePatch(slot.NodeId));
        }

        if (_tree.TryGetComponentParent(componentId, out var parent))
            _tree.RemoveComponentSlot(parent.ParentComponentId, parent.ParentNodeId, componentId);

        // Event registrations for every node the component still owned die
        // with its buckets (Task 5 registry cleanup).
        _tree.RemoveComponent(componentId, RemoveNodeEventRegistrations);
    }

    private void ProcessAttribute(int nodeId, ref BnRenderTreeFrame frame, ref PooledList<RenderPatch> patches)
    {
        var name = frame.AttributeName ?? "";
        var value = frame.AttributeValue?.ToString();

        if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            var eventName = name[2..].ToLowerInvariant();
            var handlerId = (int)frame.AttributeEventHandlerId;
            // THE AttachEvent emission site — the detach registry records the
            // handlerId here (a SetAttribute re-attach overwrites: last wins,
            // so a later detach carries the LIVE handlerId). Task 5.
            RegisterEventHandler(nodeId, eventName, handlerId);
            patches.Add(new AttachEventPatch(nodeId, eventName, handlerId));
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
    /// window — still open after 3.3; revisit in 3.4+ when Bn* components
    /// make async handlers likely.
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
