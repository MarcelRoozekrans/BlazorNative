using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BlazorNative.Core;
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

/// <summary>The headless Blazor renderer that turns render batches into native patches.</summary>
/// <remarks>Not part of the supported public API. It is public for one mechanical reason: its only
/// consumer is <c>internal static unsafe class HostSession</c> in <c>BlazorNative.Runtime</c>
/// (<c>HostSession.cs:31</c>), which holds it, sets <see cref="StrictErrors"/> and calls
/// <see cref="RunAfterDispatch"/> — an internal type in a <em>different assembly</em> cannot reach
/// an internal type here. The members exposed are renderer internals (frame sink, strict-error
/// switch, dispatch pump); no app author sets them. App code mounts through
/// <c>BlazorNativePage.Routed&lt;T&gt;</c>, whose mount thunk is itself <c>internal</c>. The
/// non-breaking fix (<c>internal</c> + <c>InternalsVisibleTo</c>) is recorded as 1.0 criterion S3.
/// Tier NOT-API.</remarks>
// Fully qualified: a file-level `using System.ComponentModel` would make IComponent ambiguous
// against Microsoft.AspNetCore.Components.IComponent, which this file uses throughout.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
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
    /// rc 2, or a FaultNotice with rc 0 after a begun shell call, 16.7; no
    /// double-report).
    /// Default FALSE — the deliberate production POC posture: renderer errors
    /// log to stderr rather than crash the host process (a diagnostics
    /// surface is M4+ work). ONE carve-out since Phase 11.4 Gate D (#164):
    /// PARAMETER-BINDING faults abort the mount (rc 2) even non-strict — they
    /// are author bugs, never transient, and log-and-continue leaves a
    /// half-rendered screen. See <see cref="HandleException"/>; this flag is
    /// still what governs every OTHER fault class.
    /// ALL test harnesses enable it: this silent
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
                BnLog.Warn("NativeRenderer", $"contract warning: {message}");
        };
    }

    // ── The render thread (Phase 16.1) ───────────────────────────────────────
    //
    // Each renderer owns ONE dedicated thread, "BlazorNative-Render", with a
    // single-threaded SynchronizationContext on it (RenderThreadDispatcher). Every
    // mutating entry point below — Mount, MountAsync, Unmount, RunAfterDispatch,
    // DispatchUiEventAsync, DispatchSyncPart and Dispose — runs its body on that thread: a call from
    // another thread posts the work and blocks until it completes, and a call from the
    // render thread runs inline. CheckAccess() answers honestly.
    //
    // This replaces the inline dispatcher, born on the retired Mono-WASI runtime, which
    // ran all work on the CALLING thread and answered CheckAccess() with an unconditional
    // true. That made every export a blocking wait whenever a handler awaited the host,
    // which is #345, and it could not be made honest without a real thread to marshal to:
    // 13.2 measured the honest answer recursing Dispose -> InvokeAsync -> Dispose into a
    // stack overflow. The 16.0 spike proved the render thread keeps the sync-mount contract
    // the C ABI needs — Mount still returns only after its first frame — and removes the
    // recursion, because the hop is now real (docs/plans/2026-09-26-phase-16.0-spike-conclusion.md).
    private readonly RenderThreadDispatcher _dispatcher = new();

    /// <summary>This renderer's render thread (Phase 16.1).</summary>
    public override Dispatcher Dispatcher => _dispatcher;

    /// <summary>The managed id of THIS renderer's render thread. An instance property on
    /// purpose: the 16.0 spike's static "last constructed" id named another renderer's
    /// thread under test parallelism (spike requirement 9), so every thread-identity pin
    /// compares against the renderer under test.</summary>
    internal int RenderThreadId => _dispatcher.ThreadId;

    /// <summary>Runs <paramref name="work"/> on the render thread and waits for it to finish.
    /// Blocking is safe: off the render thread the caller is never the thread that must run
    /// the work, and on it the work runs inline.</summary>
    private void OnRenderThread(Action work)
    {
        if (_dispatcher.CheckAccess())
            work();
        else
            _dispatcher.InvokeAsync(work).GetAwaiter().GetResult();
    }

    private T OnRenderThread<T>(Func<T> work)
        => _dispatcher.CheckAccess()
            ? work()
            : _dispatcher.InvokeAsync(work).GetAwaiter().GetResult();

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
        // Marshals through the dispatcher: InvokeAsync posts to the render thread when called
        // from any other thread, and the returned Task completes when the mount does.
        => Dispatcher.InvokeAsync(() => AddComponentAsync(typeof(TComponent), parameters));

    /// <summary>Convenience overload that explicitly passes <see cref="ParameterView.Empty"/>.
    /// Do NOT collapse this into a single overload with <c>ParameterView parameters = default</c>:
    /// on Blazor's ParameterView (any runtime — found on the retired Mono-WASI AOT),
    /// <c>default(ParameterView)</c> throws NullReferenceException inside
    /// ComponentState.SupplyCombinedParameters, which the renderer's HandleException swallows
    /// silently — mount appears to "succeed" (returns a componentId) but no render fires and
    /// no frame reaches the FrameSink / Frames event. Phase 2.4 Task 4 investigation, defect #3.</summary>
    public int Mount<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>() where TComponent : IComponent
        => Mount<TComponent>(ParameterView.Empty);

    /// <summary>Synchronous mount entry point for hosts that need the first render
    /// completed before the call returns (originally Mono-WASI Main; today HostSession's
    /// C-ABI mount path). Asserts the first render completes synchronously;
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
        => OnRenderThread(() => MountOnRenderThread<TComponent>(parameters));

    // The "must complete synchronously" check runs HERE, on the render thread, so it still
    // means what it always meant: the first render finished inside this call.
    private int MountOnRenderThread<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(ParameterView parameters)
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
                "work that yields before the first render completes.",
                inner);
        }
        return componentId;
    }

    /// <summary>Phase 3.5 (DoD #7): removes a root component previously
    /// mounted via <see cref="Mount{TComponent}()"/>/<see cref="MountAsync{TComponent}(CancellationToken)"/>.
    /// Thin wrapper over Blazor's <c>RemoveRootComponent(int)</c> (protected
    /// internal on the Renderer base — verified present on Blazor 10.0.x):
    /// Blazor enqueues the component (and transitively its descendants) for
    /// disposal and processes the render queue on the render thread, and a call
    /// from another thread waits for it, so the disposal batch has reached
    /// UpdateDisplayAsync when this returns, and the
    /// RemoveNode patches that clear the screen (the Phase 3.3 disposal
    /// machinery — today EmitDisposedComponentRemoves + its pass-2 delta and
    /// CleanupDisposedComponent) have already been delivered to
    /// Frames/FrameSink when this returns. Blazor throws for an id that is
    /// not a live root component — surfaced to the caller unchanged.
    /// MUST NOT be called from inside a UI-event dispatch window: Blazor
    /// holds <c>_isBatchInProgress</c> across the handler, and
    /// RemoveRootComponent's ProcessRenderQueue throws "Cannot start a batch
    /// when one is already in progress" — defer via
    /// <see cref="RunAfterDispatch"/> instead (the navigation swap does).</summary>
    public void Unmount(int componentId) => OnRenderThread(() => RemoveRootComponent(componentId));

    // ── Post-dispatch deferral (Phase 3.5, scoped per dispatch in 16.1) ──────
    //
    // Blazor's Renderer.DispatchEventAsync keeps its batch open across the
    // synchronous part of an event handler (state changes coalesce into ONE
    // re-render after the handler). Work that must start a NEW batch — the
    // navigation swap's RemoveRootComponent — therefore cannot run inside the
    // handler; it queues into the CURRENT dispatch's scope and runs when that
    // scope closes, which is when the dispatch's synchronous part returns its
    // Task — still inside DispatchSyncPart, so swap frames are delivered before
    // blazornative_dispatch_event returns (the dispatch-window pin).

    /// <summary>Runs <paramref name="action"/> immediately when no dispatch's
    /// synchronous part is running; otherwise queues it into that dispatch's
    /// <see cref="DispatchScope"/>, to run when the scope closes (still inside the
    /// dispatch export call). A queued action's exception — including strict-mode
    /// renderer errors from the frames it produces — is captured into that same
    /// scope, so it faults the dispatch exactly like a handler fault: export rc 2, or,
    /// when the handler had already begun a host call or a fetch, a FaultNotice with
    /// rc 0 (16.7). A call the action itself begins never marks the dispatch: the
    /// action runs after the scope closed.
    /// A handler SUSPENDED on an await holds no scope, so a dispatch arriving
    /// meanwhile queues into its own scope and swaps before its own export returns
    /// (16.0 spike requirement 1). Honest boundary (NON-strict mode): the drain
    /// runs after the scope closed, so a renderer error DURING a deferred action's
    /// own batches routes through <see cref="HandleException"/>'s log-only path —
    /// the action "succeeds" and the export returns 0. Only exceptions the action
    /// itself throws (or strict-mode rethrows) reach the scope. In-window faults
    /// are unaffected: rc 2 before a begun shell call, a FaultNotice with rc 0 after
    /// one (16.7).</summary>
    public void RunAfterDispatch(Action action)
    {
        // 16.1: reads the current scope, which only the render thread may touch.
        if (!_dispatcher.CheckAccess())
        {
            OnRenderThread(() => RunAfterDispatch(action));
            return;
        }
        if (_currentScope is not { } scope)
        {
            action();
            return;
        }
        (scope.PostDispatchActions ??= new List<Action>()).Add(action);
    }

    /// <summary>Drains a closed scope's queued post-dispatch work (see
    /// <see cref="RunAfterDispatch"/>). Runs after the scope closed — a drained
    /// action's Unmount/Mount batches process normally, and a RunAfterDispatch
    /// call DURING the drain executes immediately, so the while-loop is
    /// unreachable today: purely defensive against a future change that
    /// re-queues mid-drain. Action faults land in THIS scope (first one wins,
    /// matching the window contract) instead of escaping the calling finally;
    /// EVERY fault is logged to stderr — mirroring <see cref="HandleException"/>'s
    /// window path — so a second fault is never silently discarded when the slot
    /// is already taken.</summary>
    private static void DrainPostDispatchActions(DispatchScope scope)
    {
        while (scope.PostDispatchActions is { Count: > 0 } actions)
        {
            scope.PostDispatchActions = null;
            foreach (Action action in actions)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    BnLog.Error("BlazorNative.Renderer", "post-dispatch action threw", ex);
                    scope.Fault ??= ex;
                }
            }
        }
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

    // ── The render-owner check (Phase 13.2, made an assertion in 16.1) ───────
    //
    // Blazor's own Dispatcher.AssertAccess() rejects an off-thread render before a batch
    // starts, but it trusts CheckAccess(). This check does NOT: it compares the thread
    // that actually reached UpdateDisplayAsync against THIS renderer's RenderThreadId. So
    // it is a second, independent line that still fires if CheckAccess() ever lies again —
    // 13.2's inline dispatcher answered true for every thread, which is exactly how a batch
    // could reach the tree from the wrong thread.
    //
    // Under StrictErrors, which every test harness enables, it THROWS, so a violation fails
    // under test instead of scrolling past as a log line. Otherwise it warns. It names both
    // threads, because "wrong thread" alone cannot be acted on. The silent path allocates
    // nothing: the message is built only when the check fails.
    private void AssertOnTheRenderThread()
    {
        int current = Environment.CurrentManagedThreadId;
        int owner = RenderThreadId;
        if (current == owner)
            return;

        string message =
            $"render batch driven from thread {current}, but this renderer's batches are "
            + $"owned by thread {owner}, its render thread — the render tree is not safe to "
            + "drive concurrently. Marshal the work onto the render thread with "
            + "Dispatcher.InvokeAsync rather than calling into the renderer directly.";

        if (StrictErrors)
            throw new InvalidOperationException(message);
        BnLog.Warn("BlazorNative.Renderer", message);
    }

    // ── UpdateDisplayAsync ────────────────────────────────────────────────────

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        // 13.2, an assertion since 16.1. No renderer state is touched, so it sits ahead of
        // the drain without disturbing the drain-first rule below. Under StrictErrors the
        // throw routes through ProcessRenderQueue's catch into HandleException, which
        // rethrows it at the caller boundary.
        AssertOnTheRenderThread();

        // #213 item 2: route any Frames fault parked by a ThreadPool continuation. FIRST,
        // on the renderer thread — the only thread where HandleException's single-threaded
        // state is safe to touch, and the only place a strict-mode rethrow surfaces at a
        // boundary someone is actually awaiting.
        DrainAsyncFramesFault();

        var batch = new BnRenderBatch(in renderBatch);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var frameId = Interlocked.Increment(ref _frameId);

        var patches = new PooledList<RenderPatch>(capacity: 32);

        try
        {
            // Disposed components, pass 1 of 2 (Phase 7.2 — the ORDER is the
            // fix): emit their root-view RemoveNodePatches FIRST, before any
            // diff's patches. The diffs below trim the disposed children's
            // sibling slots as their RemoveFrame edits arrive, and every
            // later create's InsertIndex is translated against that TRIMMED
            // slot state — so a host applying the frame in patch order must
            // have detached the disposed views BEFORE it applies those
            // inserts. Pre-7.2 the removes were emitted at the END of the
            // frame, which was invisible until the first batch that both
            // disposes keyed child COMPONENTS and inserts new ones at later
            // positions in the same container — BnList's window slide, the
            // first real customer (KeyedWindowSlideTests pins the order;
            // BnListDemoTests' shell-mirror golden reddened first).
            //
            // Pass 1 reads the PRE-diff root buckets — usually the whole
            // story, because a disposed component normally gets no diff. The
            // exception (Gate 1 review, Important 1): a component can appear
            // in BOTH UpdatedComponents and DisposedComponentIDs when its
            // re-render was queued before the parent render that disposes it
            // (child diffed first, THEN disposed — constructible through a
            // child handler that calls StateHasChanged() before the parent's
            // remove callback; SameBatchRerenderDisposalTests). If that dying
            // diff ADDS a root-level view, pass 1 cannot have covered it —
            // the delta emission in pass 2 does. The set records what pass 1
            // emitted so pass 2 emits exactly the difference.
            HashSet<int>? pass1RemovedRoots = null;
            foreach (ref var disposedId in batch.DisposedComponentIDs)
            {
                pass1RemovedRoots ??= new HashSet<int>();
                EmitDisposedComponentRemoves(disposedId, ref patches, pass1RemovedRoots);
            }

            // Updated components — pass the batch's ReferenceFrames in (in Blazor 10
            // ReferenceFrames lives on RenderBatch, not on RenderTreeDiff).
            var referenceFrames = batch.ReferenceFrames;
            foreach (ref var diff in batch.UpdatedComponents)
            {
                var bnDiff = new BnRenderTreeDiff(in diff);
                ProcessRenderTreeDiff(ref bnDiff, ref referenceFrames, ref patches);
            }

            // Disposed components, pass 2 of 2, in two steps. Step ONE (the
            // Gate 1 review's delta emission): remove any root view a dying
            // same-batch diff created AFTER pass 1 read the buckets — without
            // this, that view is a zombie on the host no patch ever removes.
            // Emitting at the frame's TAIL is safe: the only InsertIndex
            // translation sites (ProcessFrame's Element/Text arms) run inside
            // the UpdatedComponents loop above, so nothing after the diffs
            // translates against any bucket — a tail remove can perturb no
            // already-emitted index, and RemoveNode is by-id, position-
            // independent. ALL deltas are emitted before ANY cleanup so every
            // delta reads buckets untouched since the diffs (cleanup of one
            // disposed component trims its Component slot from another's
            // bucket when they nest).
            foreach (ref var disposedId in batch.DisposedComponentIDs)
                EmitDisposedComponentRemovesDelta(disposedId, pass1RemovedRoots!, ref patches);

            // Step TWO (Phase 3.3 Task 3): trim their sibling slot and purge
            // their bookkeeping — AFTER the diffs, so a parent's RemoveFrame
            // edits (whose sibling indices assume the slots still present
            // until that edit) stay correct.
            foreach (ref var disposedId in batch.DisposedComponentIDs)
                CleanupDisposedComponent(disposedId);

            // Phase 16.3: forget the call sites of handler ids Blazor has disposed, so
            // the map holds only live handlers and never grows without bound. Pinned by
            // SlowHandlerWarningTests.TheCallSiteMap_StaysFlat_AcrossFiftyReRendersOfCapturingLambdas.
            foreach (ref var disposedHandler in batch.DisposedEventHandlerIDs)
                _handlerCallSites.Remove((int)disposedHandler);

            patches.Add(new CommitFramePatch(frameId, timestamp));

            var frame = new RenderFrame(frameId, timestamp, patches.AsSpan().ToArray());

            // OBSERVE the Frames task — never discard it (#123). Frames is the
            // documented TEST channel, and AsyncEventHandler reports a
            // subscriber fault THROUGH the returned task: a discarded task
            // silently swallows a throw inside a frame subscriber (a failing
            // assertion), so a test could be green while actually failing.
            // Route the fault through the SAME HandleException path the
            // synchronous body uses, so StrictErrors surfaces it. Sequential
            // subscribers complete inline (the common case) → the task is
            // already done, so GetResult() rethrows into the catch below; a
            // genuinely async subscriber gets a fault continuation so its
            // throw is still routed, never dropped.
            var framesTask = _frames.InvokeAsync(frame, default);
            if (framesTask.IsCompleted)
                framesTask.GetAwaiter().GetResult();
            else
                // #213 item 2 — PARK IT, DO NOT HANDLE IT HERE.
                //
                // This continuation runs on a ThreadPool thread. It used to call
                // HandleException directly, which reads and writes state this class
                // declares single-threaded (the current dispatch scope, and
                // _reportedBindingFault) and, under
                // StrictErrors, rethrows via ExceptionDispatchInfo.Throw() — on a pool
                // thread, where nothing observes it. So the mechanism meant to stop a
                // subscriber fault being SWALLOWED could both corrupt renderer state and
                // swallow the fault a second way.
                //
                // #123's actual requirement is only "never discard the task". That is met
                // by capturing the fault; WHERE it is routed is a separate question, and
                // the answer is the renderer thread (DrainAsyncFramesFault, called at the
                // top of the next UpdateDisplayAsync).
                //
                // Logged HERE as well, immediately and unconditionally: BnLog is
                // thread-safe, and the drain only runs if another frame arrives. Without
                // the log, a fault on the LAST frame of a run would be parked and never
                // reported — which is the swallow #123 exists to prevent.
                framesTask.AsTask().ContinueWith(
                    static (t, self) =>
                    {
                        var renderer = (NativeRenderer)self!;
                        Exception fault = t.Exception!.InnerException ?? t.Exception;
                        BnLog.Error("BlazorNative.Renderer",
                            "Frames subscriber faulted asynchronously", fault);
                        // First fault wins — the one that started the trouble, not the last.
                        Interlocked.CompareExchange(ref renderer._asyncFramesFault, fault, null);
                    },
                    this, TaskContinuationOptions.OnlyOnFaulted);
            DispatchFrame(frame);
        }
        catch (Exception ex)
        {
            // Non-strict only: strict mode promises SURFACING over logging —
            // HandleException rethrows (or the dispatch window captures and
            // logs), so logging here too would double-report the same
            // exception. Non-strict keeps the frame-id context line.
            if (!StrictErrors)
                BnLog.Error("NativeRenderer", $"frame {frameId} failed", ex);
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

    /// <summary>The last parameter-binding fault this renderer reported
    /// (Phase 11.4 Gate D). Renderer-scoped and single-threaded like every
    /// other field here; see the dedupe note in <see cref="HandleException"/>.</summary>
    private Exception? _reportedBindingFault;

    /// <summary>
    /// A <see cref="Frames"/> subscriber fault that arrived on a ThreadPool thread,
    /// parked for the renderer thread to route (#213 item 2).
    ///
    /// <para><b>The only field here that is deliberately cross-thread</b>, hence the
    /// Interlocked access. Every other field in this class — including the three
    /// <see cref="HandleException"/> touches — is single-threaded by contract, which is
    /// exactly what the old fault continuation broke: it called HandleException straight
    /// from the pool thread, reading and writing the dispatch capture window, then a
    /// depth counter and a shared slot, and <c>_reportedBindingFault</c> from off the
    /// renderer thread, and under <see cref="StrictErrors"/> it also rethrew via
    /// <c>ExceptionDispatchInfo.Throw()</c> on that pool thread — where nothing observes
    /// it.</para>
    ///
    /// <para>FIRST fault wins and is never overwritten: the surfaced exception should be
    /// the one that started the trouble, not whichever landed last.</para>
    /// </summary>
    private Exception? _asyncFramesFault;

    /// <summary>Test-only (#213 item 2): the parked async <see cref="Frames"/> fault, or
    /// null. Lets a test assert the fault was CAPTURED without having to drive a second
    /// frame to drain it — the assertion this fix most needs is "it was not swallowed".</summary>
    internal Exception? AsyncFramesFaultForTests => Volatile.Read(ref _asyncFramesFault);

    /// <summary>
    /// Routes any parked async <see cref="Frames"/> fault, ON THE RENDERER THREAD.
    /// Called at the top of <see cref="UpdateDisplayAsync"/> — the one place guaranteed to
    /// be on that thread — so <see cref="HandleException"/>'s single-threaded state is
    /// touched only from where it is safe, and a strict-mode rethrow surfaces at a real
    /// boundary instead of on a pool thread nobody is awaiting.
    /// </summary>
    private void DrainAsyncFramesFault()
    {
        Exception? fault = Interlocked.Exchange(ref _asyncFramesFault, null);
        if (fault is not null)
            HandleException(fault);
    }

    protected override void HandleException(Exception exception)
    {
        // Inside a dispatch's synchronous part, remember the first exception in
        // THAT dispatch's scope so DispatchSyncPart reports it (Blazor swallows
        // dispatch exceptions here otherwise — see DispatchScope). The window wins
        // over strict mode: the fault surfaces ONCE, at the dispatch boundary —
        // export rc 2, or a FaultNotice with rc 0 when the handler had begun a host
        // call or a fetch before it (16.7) — never from this stack. A handler suspended on
        // an await holds no scope, so a fault raised by ANOTHER dispatch meanwhile
        // is that dispatch's, never the suspended one's (16.0 requirement 1).
        if (_currentScope is { } scope)
        {
            scope.Fault ??= exception;
            BnLog.Error("BlazorNative.Renderer", "render fault (dispatch window)", exception);
            return;
        }

        // After a handler's first await: no scope is open, but the continuation still
        // carries its dispatch's scope in the execution context. Attribute the fault to
        // THAT dispatch, so its pending Task faults, in production mode as well as strict.
        // A scope that is Done belongs to a handler that already finished; a fault then is
        // fire-and-forget, and takes the no-window path below. A PARAMETER-BINDING fault
        // is never attributed here: #164's abort below must see it first.
        if (s_flowingScope.Value is { Done: false } flowing
            && ReferenceEquals(flowing.Owner, this)
            && !BlazorInterop.IsParameterBindingFault(exception))
        {
            Interlocked.CompareExchange(ref flowing.LateFault, exception, null);
            BnLog.Error("BlazorNative.Renderer", LateFaultLogLabel, exception);
            return;
        }

        // Strict mode (Phase 3.3 Task 6, DoD #9): rethrow with the original
        // stack — the exception surfaces synchronously at whatever boundary
        // invoked the render work (mount, batch). See StrictErrors doc for
        // why production stays on the log path below.
        if (StrictErrors)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();

        // Phase 11.4 Gate D (#164): a PARAMETER-BINDING fault is not a
        // recoverable render fault and never was. It is an author bug —
        // `@bind-Value` where the property is `Checked` — that cannot be
        // transient, fails identically on every run of every device, and whose
        // log-and-continue outcome is a half-rendered screen the user cannot
        // report. It aborts the mount instead, and the caller boundary
        // (HostSession.TryMount's catch) maps that to the ALREADY-DOCUMENTED
        // rc 2. No new rc: BlazorNativeRuntime.kt's `else -> throw` treats an
        // unknown rc as fatal, so a "non-fatal rc 4" would hard-crash every
        // consumer still on an older COPIED shell — the StrictErrors-in-
        // production outcome #164 rules out, arriving through the back door.
        // rc 2 both shells already handle correctly today.
        //
        // This is NOT the strict-mode flip: StrictErrors makes EVERY render
        // fault fatal, including the genuinely recoverable ones. The posture
        // below is not reversed, it is SCOPED — see the classifier's contract
        // (BlazorInterop.ParameterBindingFrames) for what is in and what is
        // deliberately out.
        if (BlazorInterop.IsParameterBindingFault(exception))
        {
            // Log ONCE per fault instance. The rethrow unwinds through Blazor's
            // own render machinery, which routes it back here (RenderRootComponentAsync
            // → SupplyCombinedParameters → HandleExceptionViaErrorBoundary, then again
            // as the parent's queued render tears down) — three identical
            // Exception.ToString() blobs in logcat for one author bug is exactly
            // the noise §4.3 and #155 are about. Reference identity, not equality:
            // a genuinely SECOND binding fault is a different instance and is
            // reported.
            if (!ReferenceEquals(exception, _reportedBindingFault))
            {
                _reportedBindingFault = exception;
                BnLog.Error("BlazorNative.Renderer",
                    "parameter-binding fault — aborting the mount (#164): a component was given a "
                    + "parameter it does not declare, or its setter threw. This is an author bug, "
                    + "not a transient fault, so the mount fails (rc 2) instead of half-rendering.",
                    exception);
            }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }

        BnLog.Error("BlazorNative.Renderer", "render fault", exception);
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
        // Warn, not Error: non-strict DROPS it and carries on, and design §4.3's
        // finding is that a silently-dropped wire is exactly the Warn class — real,
        // actionable, rare, and it must survive into Release.
        BnLog.Warn("NativeRenderer", $"contract violation (dropped): {message}");
    }

    /// <summary>Test-only: routes a message through the contract-violation
    /// switch. Poisoned-cursor / clamp situations are not constructible from
    /// legal Blazor diffs anymore, so StrictModeTests injects here — the same
    /// path the production guards call.</summary>
    internal void InjectContractViolationForTests(string message)
        => ReportContractViolation(message);

    /// <summary>Test-only (Phase 4.2, same posture as
    /// <c>HostSession.ReplaceRegistryEntryForTests</c>): triggers a
    /// steady-state re-render of a mounted root — the exact
    /// <c>StateHasChanged()</c> a component's own event handler would issue,
    /// resolved through Blazor's ComponentState, run on the render thread. The
    /// render batch (diff → UpdateDisplayAsync → frame delivery) has fully
    /// completed when this returns. Exists solely so the allocation-budget
    /// test (RendererSpike.RenderWalk_IsAllocationFree_OnSteadyState, the M1
    /// deferral) can measure the walk without re-mounting per iteration —
    /// production hosts re-render exclusively through event dispatch. Only
    /// ComponentBase roots are supported: anything else throws (a test
    /// wiring bug, not a runtime condition).</summary>
    internal void TriggerRootRenderForTests(int componentId)
    {
        if (!_dispatcher.CheckAccess())
        {
            OnRenderThread(() => TriggerRootRenderForTests(componentId));
            return;
        }
        if (GetComponentState(componentId).Component is not ComponentBase component)
        {
            throw new InvalidOperationException(
                $"TriggerRootRenderForTests: component {componentId} is not a ComponentBase — " +
                "the test seam only drives ComponentBase.StateHasChanged.");
        }
        BlazorInterop.StateHasChangedViaAccessor(component);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(disposing);
            return;
        }

        // Already disposed: the render thread is gone, and Blazor's own guard makes a second
        // dispose a no-op anyway. Returning here keeps a repeat Dispose from waiting on a
        // cancelled post.
        if (_dispatcher.IsShutDown)
            return;

        try
        {
            // The body runs ON the render thread, so Blazor's base Dispose sees CheckAccess()
            // true and never re-marshals: 13.2's Dispose -> InvokeAsync -> Dispose loop has
            // nowhere to recur.
            OnRenderThread(() =>
            {
                // Release any handlers registered against Frames. The underlying
                // AsyncEventHandler<T> struct holds delegate references in its
                // internal state; resetting to default releases them so per-test
                // closures don't leak across NativeRenderer instances.
                _frames = default;
                base.Dispose(disposing);
            });
        }
        finally
        {
            // Last: stop the render thread. Off the thread this joins it; on it, the thread
            // exits once the queue drains.
            _dispatcher.Dispose();
        }
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
    /// nearest ELEMENT container up the component-parent chain (component-
    /// parent map, DoD #8) — or the host root when the chain ends without
    /// one. Walking (not one hop) matters for component CHAINS (Phase 3.4:
    /// a wrapper whose entire tree is another component, e.g. BnThemedPanel
    /// → BnView): each link's record holds its SLOT container — null at a
    /// component's root level — so the host node may sit several links up.</summary>
    private int? ResolveComponentEmitParent(int componentId)
    {
        while (_tree.TryGetComponentParent(componentId, out var parent))
        {
            if (parent.ParentNodeId is { } containerNode)
                return containerNode;
            componentId = parent.ParentComponentId;
        }
        return null; // root component (or chain of them) at the host root
    }

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
                        ProcessAttribute(cursor.ComponentId, slot.NodeId, ref attrFrame, ref patches);
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
                        else if (removedName == ScrollToAttributeName)
                        {
                            // #256 — a COMMAND has no "off" state to reset to, so its
                            // removal emits nothing. Without this arm the else below
                            // would send UpdateProp(scrollTo, null), which every shell
                            // would log as an unknown prop — noise describing a command
                            // that was never state. Reachable whenever a BnScroll goes
                            // from having issued a scroll to not (AutoScrollToEnd
                            // switched off, or the first render after a restore).
                        }
                        else if (StyleAttributes.Contains(removedName))
                        {
                            // Phase 6.1 (the null-reset fix): a removed STYLE
                            // leaves on the STYLE wire. A null SetStyle value
                            // already means "reset to default" (PatchProtocol),
                            // and the shells route it to the node's Yoga
                            // property; the same null on the PROP wire — what
                            // this arm used to emit for every name — is a prop
                            // no shell routes to Yoga, so a conditionally-null
                            // flex prop (Grow = cond ? 1 : null) would keep its
                            // old value forever. Harmless before flex, fatal
                            // with it (design §"The null-reset bug").
                            patches.Add(new SetStylePatch(slot.NodeId, removedName, null));
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

                case RenderTreeEditType.UpdateMarkup:
                {
                    // Phase 7.0 review (F2): dynamic markup — @((MarkupString)x)
                    // whose CONTENT changes in place — diffs as UpdateMarkup
                    // (same-sequence Markup frames compare by content). The
                    // Markup slot stays exactly where it is (Markup slots own
                    // no host view, so there is nothing to patch), which keeps
                    // later sibling indices aligned. The mount-time contract
                    // (ProcessFrame's Markup arm) holds on the update path too:
                    // whitespace → whitespace is a wire no-op; NEW
                    // non-whitespace content is raw HTML — unrepresentable on
                    // a native widget tree — so it is the same contract
                    // violation: strict throws, non-strict logs and the frame
                    // keeps rendering as nothing. Before this arm existed the
                    // update path silently bypassed the strict contract.
                    var slot = _tree.GetSlotAt(cursor.ComponentId, cursor.Container, bnEdit.SiblingIndex);
                    var newMarkup = new BnRenderTreeFrame(ref referenceFrames[bnEdit.ReferenceFrameIndex]).MarkupContent;
                    if (!string.IsNullOrWhiteSpace(newMarkup))
                    {
                        ReportContractViolation(
                            $"UpdateMarkup to non-whitespace content is not representable on a native " +
                            $"widget tree (component {cursor.ComponentId}, sibling {bnEdit.SiblingIndex}, " +
                            $"slot kind {slot.Kind}): \"{newMarkup}\" — rendered as nothing");
                    }
                    break;
                }

                default:
                    // Phase 7.0 review (F2): an edit type this switch does not
                    // handle is a silent structural desync waiting to happen —
                    // the UpdateMarkup gap above was exactly this class. Route
                    // every unknown type through the contract-violation switch
                    // NAMING the type, so the whole class is unreintroducible.
                    // Known residents today: PermutationListEntry /
                    // PermutationListEnd (@key reorders) — pre-existing debt,
                    // reachable the moment 7.1 makes @key natural syntax; they
                    // now fail LOUDLY (strict throws; non-strict logs) instead
                    // of desyncing host child order silently.
                    ReportContractViolation(
                        $"unhandled render-tree edit type {(RenderTreeEditType)bnEdit.Type} " +
                        $"(component {cursor.ComponentId}, sibling {bnEdit.SiblingIndex}) — edit dropped");
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
                        ProcessAttribute(componentId, nodeId, ref child, ref patches);
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
                        // OCCUPIES a sibling slot here. Phase 3.4: nodeId is this
                        // element — the child's SLOT CONTAINER, which here
                        // coincides with its host node; see the corrected
                        // invariant in NativeWidgetTree's ledger (the Component
                        // arm below is the site where the two differ).
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
                // views through the component-parent map. Attribute frames in
                // its subtree are its parameters — not walked.
                // Phase 3.4 Task 4 fix: register the SLOT CONTAINER, not the
                // emit parent. The record keys IndexOfComponentSlot lookups
                // (host-index translation + disposal's RemoveComponentSlot),
                // which address the SLOT bucket — at a component's root level
                // that bucket is null while emitParent is the enclosing HOST
                // node, and recording the latter sent chained components'
                // (wrapper → inner, e.g. BnThemedPanel → BnView) mid-list
                // inserts into the append fallback (ComponentChainTests).
                // Emit-parent resolution now walks the chain instead
                // (ResolveComponentEmitParent).
                AddSlot(componentId, slotContainer, insertAtSlot, Slot.ForComponent(frame.ComponentId));
                _tree.RegisterComponentParent(frame.ComponentId, componentId, slotContainer);
                return 1;
            }

            case RenderTreeFrameType.Markup:
            {
                // Phase 7.0 (the Razor-compilation spike). The Razor compiler
                // preserves whitespace-only text BETWEEN sibling elements as
                // Markup frames (its .NET 5+ trimming only removes whitespace
                // leading/trailing within an element and around C# blocks), so
                // the FIRST .razor-compiled component armed this arm. A markup
                // frame IS a sibling in Blazor's diff numbering — it must
                // occupy a slot or every later sibling index in this container
                // desyncs (the echo-span-after-whitespace case) — but it owns
                // no host view: whitespace renders nothing on a native widget
                // tree, and the Markup slot kind translates to zero host
                // views. NON-whitespace markup is raw HTML — native shells
                // have no innerHTML, so it is a contract violation: strict
                // throws, non-strict logs and renders nothing. Either way the
                // slot is taken FIRST, so indices stay aligned even on the
                // tolerated path. HTML comments never arrive here — the Razor
                // compiler strips them at compile time; if one ever does (a
                // future compiler change, or a hand-built MarkupString), it is
                // non-whitespace and violates — deliberately.
                AddSlot(componentId, slotContainer, insertAtSlot, Slot.ForMarkup());
                if (frame.MarkupContent is { } markup && !string.IsNullOrWhiteSpace(markup))
                {
                    ReportContractViolation(
                        $"non-whitespace markup content is not representable on a native widget tree " +
                        $"(component {componentId}): \"{markup}\" — rendered as nothing");
                }
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

    /// <summary>Pass 1 of disposal handling (split from the 3.3-era
    /// ProcessDisposedComponent in Phase 7.2): emits RemoveNodePatch for the
    /// component's ROOT-level views (their subtrees ride along on the host).
    /// Runs BEFORE the batch's diffs are processed, so the emitted removes
    /// PRECEDE every create in the frame — the diffs trim the disposed
    /// children's sibling slots and translate later insert indices against
    /// the trimmed state, and a host applying patches in order must have
    /// detached these views by then (BnList's window slide is the shape:
    /// keyed component rows leave at the front while new ones insert before
    /// the trail spacer, in one batch). Components disposed together each
    /// appear in the array and clean themselves — nested markers need no
    /// recursion here.</summary>
    /// <remarks>HOST CONTRACT: hosts must tolerate RemoveNodePatch for nodes
    /// inside already-removed subtrees AND for subtrees whose ancestor is
    /// removed later in the same frame. When an ANCESTOR element containing a
    /// child component is removed (RemoveFrame → RemoveNodePatch for the
    /// ancestor), the child's disposal still emits RemoveNodePatch for its
    /// root views — since 7.2 those redundant removes arrive BEFORE the
    /// ancestor's own remove (they used to trail it). Either way: treat
    /// unknown node ids in RemoveNode as a no-op (WidgetMapper does), and
    /// removing a child view before its ancestor is a legal detach order.
    /// Suppressing the redundant patches renderer-side would require
    /// host-subtree tracking the slot model deliberately doesn't keep.</remarks>
    private void EmitDisposedComponentRemoves(
        int componentId, ref PooledList<RenderPatch> patches, HashSet<int> removedRoots)
    {
        var rootSlots = _tree.GetSlotCount(componentId, parentNodeId: null);
        for (var i = 0; i < rootSlots; i++)
        {
            var slot = _tree.GetSlotAt(componentId, parentNodeId: null, i);
            if (slot.IsNode && removedRoots.Add(slot.NodeId))
                patches.Add(new RemoveNodePatch(slot.NodeId));
        }
    }

    /// <summary>Pass 2 step 1 of disposal handling (Phase 7.2 Gate 1 review,
    /// Important 1): after the diffs, emits RemoveNodePatch for any root view
    /// of the disposed component that pass 1 did NOT cover. Non-empty only in
    /// the same-batch re-render + disposal shape — the component's re-render
    /// was queued before the parent render that disposed it, so its FINAL
    /// diff ran in this batch and may have ADDED root-level views after
    /// pass 1 read the bucket; without this delta those views are zombies on
    /// the host (SameBatchRerenderDisposalTests pins it). Runs at the frame's
    /// tail — safe because no InsertIndex translation happens after the
    /// UpdatedComponents loop, and RemoveNode is by-id.</summary>
    private void EmitDisposedComponentRemovesDelta(
        int componentId, HashSet<int> pass1RemovedRoots, ref PooledList<RenderPatch> patches)
    {
        var rootSlots = _tree.GetSlotCount(componentId, parentNodeId: null);
        for (var i = 0; i < rootSlots; i++)
        {
            var slot = _tree.GetSlotAt(componentId, parentNodeId: null, i);
            if (slot.IsNode && !pass1RemovedRoots.Contains(slot.NodeId))
                patches.Add(new RemoveNodePatch(slot.NodeId));
        }
    }

    /// <summary>Pass 2 of disposal handling (Phase 3.3 Task 3 bookkeeping):
    /// trims the component's sibling slot from the recorded parent container
    /// (no-op when the parent's RemoveFrame edit already trimmed it) and
    /// purges its slot lists + component-parent map entry. Runs AFTER the
    /// batch's diffs — a parent's RemoveFrame sibling indices assume the
    /// slots are present until that edit consumes them.</summary>
    private void CleanupDisposedComponent(int componentId)
    {
        if (_tree.TryGetComponentParent(componentId, out var parent))
            _tree.RemoveComponentSlot(parent.ParentComponentId, parent.ParentNodeId, componentId);

        // Event registrations for every node the component still owned die
        // with its buckets (Task 5 registry cleanup).
        _tree.RemoveComponent(componentId, RemoveNodeEventRegistrations);
        _componentTypes.Remove(componentId);
    }

    /// <summary>Narrows Blazor's <c>ulong</c> event-handler id to the <c>int</c>
    /// the AttachEvent wire and the renderer's int-indexed handler table use —
    /// FAILING LOUD at the SAME boundary the dispatch side rejects (#125.1).
    /// <para>
    /// The asymmetry this closes: <c>Exports.DispatchEventCore</c> rejects a
    /// <c>handlerId &gt; int.MaxValue</c> as malformed (rc 3) BEFORE its own
    /// <c>(int)</c> cast, but attach used to narrow with a bare <c>(int)</c> —
    /// silent truncation that could wrap negative or ALIAS onto a live handler,
    /// so the two sides disagreed about the very same id. They now agree: an id
    /// past <c>int.MaxValue</c> is unrepresentable in the table, so attach throws
    /// rather than emit an AttachEvent carrying a truncated id nothing can
    /// dispatch to. Unreachable from normal frames (Blazor's handler ids count
    /// from 1 and reaching 2³¹ needs &gt;2e9 attaches in one session) — a guard,
    /// pinned by a direct unit test, not a live path.</para></summary>
    internal static int NarrowHandlerId(ulong handlerId)
    {
        if (handlerId > int.MaxValue)
            throw new InvalidOperationException(
                $"event handler id {handlerId} exceeds the renderer's int-indexed handler table — "
                + "Blazor assigned an id past int.MaxValue, which neither the AttachEvent wire nor the "
                + "dispatch_event boundary (Exports.DispatchEventCore rejects the same range as rc 3) "
                + "can represent. Silently truncating it could alias onto a live handler.");
        return (int)handlerId;
    }

    private void ProcessAttribute(int componentId, int nodeId, ref BnRenderTreeFrame frame, ref PooledList<RenderPatch> patches)
    {
        var name = frame.AttributeName ?? "";
        var value = frame.AttributeValue?.ToString();

        if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            var eventName = name[2..].ToLowerInvariant();
            var handlerId = NarrowHandlerId(frame.AttributeEventHandlerId);
            // THE AttachEvent emission site — the detach registry records the
            // handlerId here (a SetAttribute re-attach overwrites: last wins,
            // so a later detach carries the LIVE handlerId). Task 5.
            RegisterEventHandler(nodeId, eventName, handlerId);
            RecordCallSite(componentId, handlerId, frame.Sequence, eventName,
                BlazorInterop.HandlerDelegate(frame.AttributeValue));
            patches.Add(new AttachEventPatch(nodeId, eventName, handlerId));
        }
        else if (name == ScrollToAttributeName)
        {
            // #256 — the COMMAND arm. See the ScrollTo note below the style
            // partition for why a command is an attribute at all, and why the
            // nonce this parses off never reaches the wire.
            if (TryParseScrollToCommand(value, out bool toEnd, out float offset))
                patches.Add(new ScrollToPatch(nodeId, toEnd, offset));
            else
                BnLog.Warn("NativeRenderer",
                    $"ignoring malformed {ScrollToAttributeName} command '{value}' on node {nodeId} "
                    + "(expected \"end#<nonce>\" or \"<offset>#<nonce>\")");
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

    // ── The ScrollTo command attribute (#256) ─────────────────────────────────
    //
    // WHY A COMMAND ARRIVES AS AN ATTRIBUTE. A component has no way to name its
    // own native node: node ids are the renderer's, allocated during the frame
    // walk, and Blazor hands a component neither its componentId nor its node.
    // An attribute is the ONE place where the renderer already holds both halves
    // — the node id it just resolved, and a value the component authored — so
    // routing the command through the render tree costs no addressing machinery,
    // no id registry, and nothing to clean up when the node dies.
    //
    // It also gets the ORDERING right for free, which is the whole point of
    // #256: the attribute rides the same diff as the content change beside it,
    // so "append rows, then scroll to the end" is one frame in one order. A
    // side channel (a host call, an out-of-batch queue) would race the frame
    // that created the rows.
    //
    // WHY THE VALUE CARRIES A NONCE. Blazor's diff emits an attribute edit only
    // when the VALUE CHANGES. That is exactly right for state and exactly wrong
    // for a command: two ScrollToEndAsync() calls in a row are two commands, and
    // a bare "end" would be diffed down to one. BnScroll therefore appends a
    // per-instance counter — "end#7" — so each call is a distinct value.
    //
    // The nonce is a RENDERER-SIDE artifact and is stripped here: it never
    // reaches ScrollToPatch and never crosses the wire. A shell must not be able
    // to observe, let alone depend on, how .NET makes its own diff fire.
    //
    // A malformed value is logged and dropped, never thrown: this attribute is
    // reachable through the raw-element hatch (OpenElement("scroll") +
    // AddAttribute("scrollTo", …)), so a typo is an author mistake in app code,
    // and the un-styled invariant says the wire says only what the author said.

    /// <summary>The attribute name BnScroll uses to issue a scroll command.
    /// Deliberately NOT a style name — it is checked before
    /// <see cref="StyleAttributes"/> and is disjoint from both halves of the
    /// partition (pinned by StyleAttributePartitionTests).</summary>
    internal const string ScrollToAttributeName = "scrollTo";

    /// <summary>Parses <c>"end#7"</c> / <c>"123.5#7"</c> into the wire's two
    /// fields, dropping the nonce. Returns false for anything else — including a
    /// null value, a missing nonce, and a non-numeric offset.</summary>
    internal static bool TryParseScrollToCommand(string? value, out bool toEnd, out float offset)
    {
        toEnd = false;
        offset = 0f;
        if (value is null) return false;

        int hash = value.LastIndexOf('#');
        if (hash <= 0) return false;                 // no nonce, or an empty target

        ReadOnlySpan<char> target = value.AsSpan(0, hash);
        if (target.SequenceEqual(ScrollToEndTarget))
        {
            toEnd = true;
            return true;
        }
        // InvariantCulture on BOTH sides of the wire: the OnScroll payload the
        // shells send back is parsed the same way (ParseScrollOffset), so a
        // comma-decimal locale cannot make a round-trip asymmetric.
        return float.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
    }

    /// <summary>The <see cref="ScrollToAttributeName"/> target meaning "the end of
    /// the content", whose offset only the shells can compute (content height is
    /// a Yoga result they hold).</summary>
    internal const string ScrollToEndTarget = "end";

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

    /// <summary>One dispatch's capture window (Phase 16.1): the first fault Blazor
    /// routes to <see cref="HandleException"/> during that dispatch's synchronous
    /// part, and the post-dispatch actions queued by <see cref="RunAfterDispatch"/>.
    ///
    /// <para>Why a window at all: Blazor's Renderer.DispatchEventAsync does NOT
    /// propagate a handler's exception to its caller — it goes to
    /// HandleExceptionViaErrorBoundary → (no error boundary here) →
    /// HandleException, and the returned task completes successfully. Without the
    /// capture, blazornative_dispatch_event could never honor its "2 = the
    /// synchronous part faulted before it began a host call or a fetch" contract
    /// (Phase 3.2, DoD #9 partial; the boundary is 16.7's). The capture is a
    /// WINDOW, not a handler hook: anything routed to HandleException while it is
    /// open is captured — the handler itself, the resulting re-render
    /// (UpdateDisplayAsync failures land here too), or frame delivery.</para>
    ///
    /// <para>Why per dispatch: until 16.1 the window was a renderer-wide depth
    /// counter decremented in the dispatch lambda's finally, so it stayed open
    /// across an async handler's await. A second dispatch arriving while the first
    /// was suspended was treated as nested inside it: its navigation swap waited
    /// for the unrelated handler, and its fault landed in the shared slot, returned
    /// rc 0, and surfaced later as the FIRST handler's fault (16.0 spike
    /// requirement 1). A scope is current only while its own dispatch's
    /// synchronous part runs on the render thread; <see cref="DispatchSyncPart"/>
    /// closes it when Blazor hands back the handler's Task.</para>
    ///
    /// <para>Genuinely NESTED dispatch — a handler that itself dispatches,
    /// synchronously, on the render thread — opens an inner scope and restores the
    /// outer one on close. The inner fault is the inner dispatch's; the inner
    /// scope's queued actions move to the outer scope, so they still run when the
    /// OUTERMOST synchronous part unwinds, as they always did.</para>
    ///
    /// <para>A fault raised after the first await, in a continuation, is attributed
    /// to its own dispatch too, in production mode as well as strict. Blazor catches
    /// it in GetErrorHandledTask and routes it to <see cref="HandleException"/>, then
    /// completes its Task SUCCESSFULLY, so before this the fault was only logged
    /// unless StrictErrors rethrew it. GetErrorHandledTask starts inside the
    /// synchronous part, so its continuation carries the execution context captured
    /// there, including <see cref="s_flowingScope"/>. HandleException finds the scope
    /// through it and records <see cref="LateFault"/>, and the pending Task that
    /// <see cref="DispatchSyncPart"/> returns faults with it. Pinned by
    /// DispatchWindowScopeTests.ALateFault_InProductionMode_FaultsThePendingTask.
    /// Once the handler has finished — at the synchronous return when it never
    /// yielded, or when its pending Task completes — the scope is <see cref="Done"/>,
    /// and a later fire-and-forget fault takes the no-window path: strict rethrow, or
    /// a log. A parameter-binding fault is never attributed this way; it takes #164's
    /// abort. Pinned by DispatchWindowScopeTests'
    /// AFireAndForgetFault_FromAHandlerThatFinishedSynchronously_TakesTheNoWindowPath,
    /// AFireAndForgetFault_FromAPendingHandler_AfterItCompletes_TakesTheNoWindowPath,
    /// AParameterBindingFault_AfterTheFirstAwait_StillTakesThe164Path and
    /// TheFlowingScope_IsRestored_SoUnrelatedRenderThreadWorkSeesNone.</para></summary>
    private sealed class DispatchScope(NativeRenderer owner)
    {
        public readonly NativeRenderer Owner = owner;
        public Exception? Fault;
        public List<Action>? PostDispatchActions;
        /// <summary>The first fault raised after the handler's first await.</summary>
        public Exception? LateFault;
        /// <summary>Set once the handler has finished: at DispatchSyncPart's return when
        /// it never yielded, otherwise when its pending Task completes.</summary>
        public volatile bool Done;
        /// <summary>Set when the handler began a host call or a fetch during the synchronous part (16.7).</summary>
        public bool ShellCallBegun;
    }

    /// <summary>The scope of the dispatch whose synchronous part is running now, or
    /// null. Render thread only. <see cref="RunAfterDispatch"/> and the in-window
    /// capture use THIS, never <see cref="s_flowingScope"/>: a continuation must not
    /// queue into a scope that has already closed, or the cascade returns.</summary>
    private DispatchScope? _currentScope;

    /// <summary>The scope of the synchronous part running on THIS thread, or null (16.7).
    /// Thread-bound on purpose: only the synchronous part may be marked, never a continuation,
    /// so this is never the flowing AsyncLocal scope. Set and restored with
    /// <see cref="_currentScope"/>.</summary>
    [ThreadStatic] private static DispatchScope? t_syncScope;

    /// <summary>Marks the dispatch whose synchronous part is running on the calling thread
    /// as having begun a shell call (16.7, #455). No-op anywhere else. Called by
    /// NativeShellBridge after the shell accepts the begin of a host call the handler began,
    /// or of a fetch; a notice .NET sends itself does not call it.
    /// The mark is ORDERED: a scope that has already captured a fault is not marked, so a
    /// fault raised before the first begun call stays rc 2 even when a later part of the
    /// same synchronous part, a sibling's OnInitialized for example, begins a call.
    /// Pinned by FaultNoticeTests' 16.7 facts, the ordering by
    /// AFaultBeforeASiblingBeginsAShellCall_IsStillRc2_AndSendsNoNotice.</summary>
    internal static void NoteShellCallBegun()
    {
        if (t_syncScope is { Fault: null } scope)
            scope.ShellCallBegun = true;
    }

    /// <summary>The same scope, flowed with the execution context into the handler's
    /// continuations. Used ONLY to attribute a late fault in
    /// <see cref="HandleException"/>. Set and restored alongside
    /// <see cref="_currentScope"/>: the restore is required, because the render
    /// thread does not flow a poster's execution context, so a value left set would
    /// leak into whatever work item ran next.</summary>
    private static readonly AsyncLocal<DispatchScope?> s_flowingScope = new();

    /// <summary>The log line for a fault attributed to a handler after its first await
    /// (Phase 16.1). A constant so the tests that assert a fault was NOT attributed
    /// this way, and the one that asserts it WAS, track the real text: a reworded
    /// literal would leave the absence assertions passing while checking nothing.</summary>
    internal const string LateFaultLogLabel = "render fault (after the handler's first await)";

    // ── The slow-handler warning (Phase 16.3, #9) ────────────────────────────
    //
    // Since 16.1 an async handler frees the dispatch lane when it yields, so what
    // can still starve the lane is a handler whose SYNCHRONOUS part is slow: the
    // render thread runs it while the shell's dispatch lane waits, and every later
    // event queues behind it, one-for-one (the measurement is in
    // docs/plans/2026-09-27-phase-16.3-record.md). This makes that stall
    // diagnosable: DispatchSyncPart times its scope, Exports times the host-event
    // arms, and a synchronous part over the budget logs ONE Warn per key per
    // renderer, at most SlowHandlerWarningCap of them. A dispatch is keyed by its
    // handler's call site, a host event by its name. A renderer lives exactly as
    // long as its HostSession, so the set and the cap reset with the session. BnLog.DefaultLevel is Warn in every build: the
    // once-per-key rule is what keeps Release quiet. Pinned by SlowHandlerWarningTests.

    /// <summary>The slow-handler budget, in milliseconds (Phase 16.3). A synchronous
    /// part longer than this logs <see cref="SlowHandlerLogLabel"/> once per handler
    /// call site, or per event name for a host-event arm, per session. Internal on purpose:
    /// it is a diagnostic threshold, not API. Chosen from the phase record's
    /// measurement; the record gives the reason.</summary>
    internal const int SlowHandlerBudget = 100;

    /// <summary>The leading text of the slow-handler warning. A constant so the pins
    /// that assert its absence track the real text.</summary>
    internal const string SlowHandlerLogLabel = "slow handler";

    /// <summary>The most slow-handler warnings one session logs. The next distinct slow
    /// key logs <see cref="SlowHandlerSuppressedLogText"/> once, and then the warning is
    /// silent until the session resets. A backstop for the Release-quiet promise: it
    /// also bounds the host-event keys, whose names the shell supplies.</summary>
    internal const int SlowHandlerWarningCap = 32;

    /// <summary>The one line logged when <see cref="SlowHandlerWarningCap"/> is reached.</summary>
    internal const string SlowHandlerSuppressedLogText = "further slow-handler warnings suppressed";

    private static readonly long s_slowHandlerBudgetTicks = SlowHandlerBudget * Stopwatch.Frequency / 1000;

    /// <summary>Test-only: replaces <see cref="Stopwatch.GetTimestamp"/> as THIS
    /// renderer's clock for the slow-handler timing, so a pin can make a synchronous
    /// part "slow" without waiting. Per renderer, never static: other renderers in
    /// the process keep the real clock. Null in production.</summary>
    internal Func<long>? TimestampForTests { get; set; }

    /// <summary>Keys already warned about, for the once-per-key rule. Render thread
    /// only in practice; locked anyway, because the lock is uncontended and a
    /// wrong-thread caller must not corrupt it.</summary>
    private readonly HashSet<string> _slowHandlerWarned = new(StringComparer.Ordinal);

    /// <summary>Slow-handler warnings logged so far this session, the suppression line
    /// included. Guarded by the <see cref="_slowHandlerWarned"/> lock.</summary>
    private int _slowHandlerWarnings;

    /// <summary>Where a live handler id was attached. Written at the AttachEvent
    /// emission site, pruned by the batch's disposed handler ids, and read by the
    /// slow-handler warning. Render thread only.</summary>
    private readonly Dictionary<int, HandlerCallSite> _handlerCallSites = new();

    /// <summary>Test-only: how many call sites the map holds. Pins that it holds the live
    /// handlers only, never one entry per render.</summary>
    internal int HandlerCallSiteCountForTests => OnRenderThread(() => _handlerCallSites.Count);

    /// <summary>A handler's identity for the once-per-handler rule. Blazor hands a
    /// lambda that captures per-render state a NEW handler id on every render, and
    /// every item of a <c>foreach</c> its own, so the id cannot key the rule.
    /// <para>The key is the handler's OWNER: the method its delegate runs, declaring
    /// type and name. The compiler emits one method per lambda call site, so that is
    /// stable across renders and items and unique per call site in code. It is NOT the
    /// component whose render tree holds the attribute: <c>BnButton</c> forwards every
    /// app's <c>OnClick</c> from one line, and a <c>BnView</c>'s ChildContent renders
    /// into BnView's tree with the page's sequence numbers, so a tree-owner key would
    /// merge every such handler in the app into one.</para>
    /// <para>The fallback, when no delegate can be reached or its method cannot be
    /// resolved, is the tree owner: the component type, the attribute frame's sequence
    /// number and the event name. The delegate is only read here; its method is
    /// resolved when a dispatch is already over budget, never on the render path.</para></summary>
    private readonly record struct HandlerCallSite(Type Component, int Sequence, string EventName, Delegate? Handler)
    {
        public (string Key, string Owner) Resolve()
        {
            if (Handler is not null)
            {
                try
                {
                    System.Reflection.MethodInfo method = Handler.Method;
                    if (method.DeclaringType is { FullName: { } declaring } type)
                    {
                        // A FRAMEWORK method is shared by every call site that uses it: each
                        // @bind of one value type runs Blazor's own binder lambda, and a
                        // BlazorNative.Components wrapper runs its own handler for every
                        // instance. Combined with the tree-owner key it stays per call site,
                        // and the warning names the component that holds the call site.
                        if (IsFrameworkAssembly(type.Assembly))
                            return ($"{declaring}::{method.Name}#{Component.FullName}#{Sequence}#{EventName}",
                                Component.FullName ?? Component.Name);
                        return ($"{declaring}::{method.Name}#{EventName}", $"{declaring}.{method.Name}");
                    }
                }
                catch (NotSupportedException)
                {
                    // A method with no reflection metadata under NativeAOT: fall back.
                }
            }
            return ($"{Component.FullName}#{Sequence}#{EventName}", Component.FullName ?? Component.Name);
        }
    }

    /// <summary>Whether <paramref name="assembly"/> is framework code for the slow-handler
    /// key: Blazor (<c>Microsoft.AspNetCore.Components*</c>), the BCL (<c>System*</c>), or
    /// <c>BlazorNative.Components</c>, whose wrappers run their own handler for every
    /// instance. Any other assembly, the sample app included, is app code.</summary>
    private static bool IsFrameworkAssembly(System.Reflection.Assembly assembly)
    {
        string name = assembly.GetName().Name ?? "";
        return name.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal)
            || name == "System" || name.StartsWith("System.", StringComparison.Ordinal)
            || name == "BlazorNative.Components";
    }

    private void RecordCallSite(int componentId, int handlerId, int sequence, string eventName, Delegate? handler)
    {
        if (_componentTypes.TryGetValue(componentId, out Type? component))
            _handlerCallSites[handlerId] = new HandlerCallSite(component, sequence, eventName, handler);
    }

    /// <summary>Each live component's type, for <see cref="RecordCallSite"/>. Kept here
    /// rather than read through <c>GetComponentState</c>: a component rendered and then
    /// disposed in the SAME batch still has its diff in the batch, but Blazor has already
    /// removed its state, and <c>GetComponentState</c> would throw mid-frame. Removed in
    /// <see cref="CleanupDisposedComponent"/>, after the batch's diffs. Render thread only.</summary>
    private readonly Dictionary<int, Type> _componentTypes = new();

    /// <inheritdoc/>
    protected override Microsoft.AspNetCore.Components.Rendering.ComponentState CreateComponentState(
        int componentId, IComponent component, Microsoft.AspNetCore.Components.Rendering.ComponentState? parentComponentState)
    {
        _componentTypes[componentId] = component.GetType();
        return base.CreateComponentState(componentId, component, parentComponentState);
    }

    /// <summary>The slow-handler key and subject of a UI dispatch: its call site when
    /// the attach was recorded, else its handler id. Formatted only once a dispatch
    /// is over budget, so the hot path allocates nothing for the warning.</summary>
    private static (string Key, string Subject) DispatchSubject(NativeUiEvent e, HandlerCallSite? site)
    {
        if (site is not { } s)
            return ($"handler {e.HandlerId}", $"handler {e.HandlerId} '{e.EventName}'");
        (string key, string owner) = s.Resolve();
        return (key, $"handler {e.HandlerId} '{e.EventName}' in {owner}");
    }

    /// <summary>The start timestamp of a timed synchronous part.</summary>
    internal long SyncPartTimestamp() => TimestampForTests?.Invoke() ?? Stopwatch.GetTimestamp();

    /// <summary>Called by Exports when a host-event arm's synchronous part, which runs
    /// outside any <see cref="DispatchScope"/>, has finished on the render thread.
    /// Keyed by <paramref name="eventName"/>. Never the payload.</summary>
    internal void NoteHostEventSyncPart(string eventName, long started)
    {
        if (OverBudget(started, out long elapsed))
            WarnSlowOnce($"host event {eventName}", $"host event '{eventName}'", elapsed);
    }

    /// <summary>Whether the part that began at <paramref name="started"/> ran over
    /// <see cref="SlowHandlerBudget"/>, and for how many ticks.</summary>
    private bool OverBudget(long started, out long elapsed)
    {
        elapsed = SyncPartTimestamp() - started;
        return elapsed > s_slowHandlerBudgetTicks;
    }

    /// <summary>Logs the slow-handler Warn, the first time only for
    /// <paramref name="key"/> and at most <see cref="SlowHandlerWarningCap"/> times per
    /// session. <paramref name="subject"/> names the handler; the caller never passes
    /// the payload, which can carry user input.</summary>
    private void WarnSlowOnce(string key, string subject, long elapsed)
    {
        lock (_slowHandlerWarned)
        {
            if (_slowHandlerWarnings > SlowHandlerWarningCap)
                return; // the cap was reached and announced: silent until the session resets
            if (!_slowHandlerWarned.Add(key))
                return;
            if (++_slowHandlerWarnings > SlowHandlerWarningCap)
            {
                BnLog.Warn("NativeRenderer",
                    $"{SlowHandlerLogLabel}: {SlowHandlerSuppressedLogText} for this session, "
                    + $"after {SlowHandlerWarningCap} distinct slow handlers.");
                return;
            }
        }
        long ms = elapsed * 1000 / Stopwatch.Frequency;
        BnLog.Warn("NativeRenderer",
            $"{SlowHandlerLogLabel}: {subject} held the render thread for {ms} ms, over the "
            + $"{SlowHandlerBudget} ms budget. The shell's dispatch lane waited with it, and every "
            + "event behind it waited too. Move the work after an await, or off the render thread. "
            + $"Warned once per call site per session, at most {SlowHandlerWarningCap} times.");
    }

    /// <summary>Test-only: whether a dispatch scope flows in the CURRENT execution
    /// context. DispatchWindowScopeTests reads it from unrelated render-thread work to
    /// pin that <see cref="DispatchSyncPart"/> restores it.</summary>
    internal bool FlowingScopeIsSetForTests => s_flowingScope.Value is not null;

    /// <summary>Runs one UI event's SYNCHRONOUS part — the handler up to its first
    /// incomplete await, its re-render and FrameSink delivery — inside its own
    /// <see cref="DispatchScope"/>, and reports how it ended. MUST be called on the
    /// render thread. Stale handler ids (ArgumentException from a handler that died
    /// in a re-render) are caught + logged — delivery is at-most-once, a stale tap is
    /// not an error — and report <see cref="DispatchOutcomeKind.Completed"/>.
    /// The scope closes when Blazor returns the handler's Task, and its post-dispatch
    /// actions (the navigation swap) run at that close, before this returns.
    /// Returns <see cref="DispatchOutcomeKind.Faulted"/> when the scope captured a
    /// fault or the returned Task is faulted or cancelled, but
    /// <see cref="DispatchOutcomeKind.FaultedAfterShellCall"/> for a fault, not a
    /// cancellation, raised after the handler began a host call or a fetch in its
    /// synchronous part (16.7, #455), <see cref="DispatchOutcomeKind.Completed"/>
    /// when the Task is complete, and <see cref="DispatchOutcomeKind.Pending"/> when the
    /// handler is still running. The pending Task is <see cref="AwaitWholeHandler"/>'s,
    /// not Blazor's: it also faults with a fault raised after the first await.</summary>
    internal DispatchOutcome DispatchSyncPart(NativeUiEvent e)
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                $"DispatchSyncPart ran on thread {Environment.CurrentManagedThreadId}, not on this "
                + $"renderer's render thread {RenderThreadId}. Post it through Dispatcher.InvokeAsync.");
        }

        var scope = new DispatchScope(this);
        DispatchScope? outer = _currentScope;
        DispatchScope? outerSync = t_syncScope;
        DispatchScope? outerFlowing = s_flowingScope.Value;
        // Phase 16.3: the slow-handler clock starts as the scope OPENS. The call site
        // is resolved NOW: the handler's own re-render can dispose its id, when its
        // delegate changed, and that prunes the id from the call-site map.
        HandlerCallSite? callSite = _handlerCallSites.TryGetValue(e.HandlerId, out HandlerCallSite found)
            ? found
            : null;
        long started = SyncPartTimestamp();
        _currentScope = scope;
        t_syncScope = scope;
        s_flowingScope.Value = scope;
        Task? task = null;
        try
        {
            var args = BuildEventArgs(e);
            task = BlazorInterop.DispatchEventViaAccessor(this, (ulong)e.HandlerId, args);
        }
        catch (ArgumentException ex)
        {
            // Warn: a stale handler id is a teardown race, tolerated by design.
            BnLog.Warn("NativeRenderer", $"stale handler {e.HandlerId}: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Anything else escaping Blazor's own dispatch is this dispatch's fault.
            scope.Fault ??= ex;
        }
        finally
        {
            // CLOSE the scope: the synchronous part has returned its Task. Inside the
            // finally so a faulted dispatch still drains — the queue must never leak
            // into another dispatch; action faults join this scope, never escape.
            _currentScope = outer;
            t_syncScope = outerSync;
            s_flowingScope.Value = outerFlowing;
            if (outer is null)
                DrainPostDispatchActions(scope);
            else if (scope.PostDispatchActions is { } queued)
                (outer.PostDispatchActions ??= new List<Action>()).AddRange(queued);
        }
        // ...and stops once it has CLOSED, its queued swap included: the whole
        // interval the shell's dispatch lane waits for. Keyed by the handler's call
        // site; the payload is never passed.
        if (OverBudget(started, out long slowElapsed))
        {
            (string slowKey, string slowSubject) = DispatchSubject(e, callSite);
            WarnSlowOnce(slowKey, slowSubject, slowElapsed);
        }

        // The handler is finished on these two returns, so the scope is Done at once:
        // fire-and-forget work it started must not have a later fault attributed to it.
        // Only the Pending return leaves marking Done to AwaitWholeHandler.
        if (scope.Fault is { } captured)
        {
            scope.Done = true;
            return new DispatchOutcome(
                scope.ShellCallBegun ? DispatchOutcomeKind.FaultedAfterShellCall : DispatchOutcomeKind.Faulted,
                captured, null);
        }
        if (task is null || task.IsCompletedSuccessfully)
        {
            scope.Done = true;
            return new DispatchOutcome(DispatchOutcomeKind.Completed, null, null);
        }
        if (task.IsCompleted)
        {
            scope.Done = true;
            // Faulted or cancelled before yielding: this dispatch's fault. A fault after a
            // begun shell call is classified apart (16.7). Both halves of this arm are defensive
            // and unmeasured: no test reaches it through Blazor. A handler fault arrives through
            // HandleException into scope.Fault above, and Blazor completes a cancelled handler
            // Task before it reaches here. See rows M3b and M3c of the 16.7 record.
            Exception fault = task.IsFaulted
                ? task.Exception!.InnerException ?? task.Exception
                : new TaskCanceledException(task);
            DispatchOutcomeKind kind = task.IsFaulted && scope.ShellCallBegun
                ? DispatchOutcomeKind.FaultedAfterShellCall
                : DispatchOutcomeKind.Faulted;
            return new DispatchOutcome(kind, fault, null);
        }
        return new DispatchOutcome(DispatchOutcomeKind.Pending, null, AwaitWholeHandler(task, scope));
    }

    /// <summary>The pending Task <see cref="DispatchSyncPart"/> hands out: completes when
    /// the handler does, then marks the scope <see cref="DispatchScope.Done"/> and faults
    /// with the late fault <see cref="HandleException"/> attributed to it, if any. Blazor's
    /// own Task would complete successfully for that fault.</summary>
    private static async Task AwaitWholeHandler(Task handler, DispatchScope scope)
    {
        try
        {
            await handler.ConfigureAwait(false);
        }
        finally
        {
            scope.Done = true;
        }
        if (Volatile.Read(ref scope.LateFault) is { } late)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(late).Throw();
    }

    /// <summary>Dispatches a host UI event into Blazor's handler table, for direct
    /// callers such as the test host. Marshalled onto the render thread (Phase 16.1)
    /// and run through <see cref="DispatchSyncPart"/>. The returned task completes when
    /// the WHOLE handler has completed, continuation included. It faults with the
    /// fault the dispatch's window captured, including one classified as
    /// <see cref="DispatchOutcomeKind.FaultedAfterShellCall"/>, or, for a handler still running after its
    /// synchronous part, with the fault its Task ends in. The export does not use
    /// this: it waits only for the synchronous part.</summary>
    public Task DispatchUiEventAsync(NativeUiEvent e)
        => Dispatcher.InvokeAsync(async () =>
        {
            DispatchOutcome outcome = DispatchSyncPart(e);
            // A fault after a begun shell call is still the dispatch's fault here: this path
            // has no FaultNotice to carry it (16.7).
            if (outcome.Kind is DispatchOutcomeKind.Faulted or DispatchOutcomeKind.FaultedAfterShellCall)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(outcome.Fault!).Throw();
            if (outcome.Kind == DispatchOutcomeKind.Pending)
                await outcome.Pending!;
        });

    private static EventArgs BuildEventArgs(NativeUiEvent e) => e.EventName switch
    {
        "click"  => new MouseEventArgs(),
        "change" => new ChangeEventArgs { Value = e.Payload },
        "focus"  => new FocusEventArgs(),
        "blur"   => new FocusEventArgs(),
        // Phase 7.2 (the onScroll wire): the payload is the shell-conflated
        // vertical content offset in dp/pt, as an invariant-culture number
        // (the same wire grammar the style values use — a Dutch shell must
        // never send "1,5"). The typed args live in Core (BnScrollEventArgs):
        // Components consumes them and does not reference this assembly.
        "scroll" => new BnScrollEventArgs { OffsetY = ParseScrollOffset(e.Payload) },
        // Phase 7.5 (the onError wire): the payload is the failed image's
        // wire `src`, VERBATIM — the URL is the only fact two loaders (Coil,
        // Kingfisher) share about the same failure, so it is the only payload
        // both shells can dispatch identically (a platform error message is
        // the one thing they will never agree on). The typed args live in
        // Core (BnImageErrorEventArgs) — BnScrollEventArgs's reason: the
        // renderer constructs, Components consumes, and Components does not
        // reference this assembly.
        "error"  => new BnImageErrorEventArgs { Src = ParseErrorSrc(e.Payload) },
        _        => EventArgs.Empty
    };

    /// <summary>Parses a <c>scroll</c> dispatch's payload — the offset in
    /// dp/pt, invariant-culture. A missing or unparseable payload is a SHELL
    /// contract violation, not user input: throw (FormatException), which the
    /// dispatch window surfaces as a loud rc-2 fault instead of dispatching a
    /// silently-wrong offset 0 that would snap every list to the top.</summary>
    private static float ParseScrollOffset(string? payload)
        => payload is null
            ? throw new FormatException(
                "scroll dispatch carried no payload — the wire contract requires the content offset in dp/pt")
            : float.Parse(payload, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Parses an <c>error</c> dispatch's payload — the failed
    /// image's wire <c>src</c>, verbatim. ParseScrollOffset's strict posture:
    /// a missing payload is a SHELL contract violation, and so is an EMPTY
    /// one — <c>""</c> never names a source (an empty <c>src</c> takes the
    /// 6.3 null path: never fetched, so it can never fail), so no honest
    /// shell can send it. Throw (FormatException) → the loud rc-2 fault,
    /// never an event about no image at all.</summary>
    private static string ParseErrorSrc(string? payload)
        => string.IsNullOrEmpty(payload)
            ? throw new FormatException(
                "error dispatch carried no payload — the wire contract requires the failed image's src URL, verbatim")
            : payload;

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
        // Phase 7.3 (design decision 1): dedicated ELEMENTS, not `input` + a
        // `type` prop — the widget class must be known at CreateNode (6.2's
        // ordering trap: props arrive in later patches).
        "checkbox" => "checkbox",
        "switch"   => "switch",
        "slider"   => "slider",
        // Phase 7.4 (design decisions 1 + 5): the overlay and the measured
        // leaf. `modal` materializes SHELL-side as anchor + overlay (the
        // third index-mapping rule); `activityindicator` is a measured leaf.
        "modal"             => "modal",
        "activityindicator" => "activityindicator",
        _          => "view"
    };

    // ── The SetStyle allow-list, PARTITIONED (Phase 6.1) ──────────────────────
    //
    // Names on this list ride the STYLE wire (patch kind 6) instead of the prop
    // wire. Membership is checked at BOTH emission sites: ProcessAttribute (set)
    // and the RemoveAttribute arm (reset — the null-reset fix), so a style that
    // goes away leaves on the same wire it arrived on.
    //
    // The partition is the SHELLS' ROUTING TABLE, not decoration. After 6.1 a
    // shell receiving a SetStyle must send each name to EXACTLY ONE of two
    // places, and "which one?" must not be a judgement call in two hand-written
    // parsers:
    //   • YogaStyleAttributes  → the node's YOGA node (a Yoga style setter).
    //   • VisualStyleAttributes → the View / UIView itself (paint, not placement).
    //
    // The sharp edge this exists to prevent: `padding` is LAYOUT. Yoga places a
    // container's children inside its padding box, so padding belongs to the
    // Yoga node — a shell that ALSO calls view.setPadding(...) (as Android does
    // today) double-applies it. Same for width/height/margin. Gate 2/3 must
    // delete those view-level calls; the plan says so explicitly.
    //
    // Comparer is ORDINAL, deliberately. Both shells match style names
    // case-SENSITIVELY, so an OrdinalIgnoreCase list here would classify
    // "FlexGrow" as a style that the shells then silently drop — .NET promising
    // routing it cannot deliver. Ordinal means a mis-cased name falls onto the
    // prop wire, where the shells already log "unknown prop".
    //
    // NOT on the list, on purpose (ledgered for a later phase): `alignContent`,
    // `rowGap`, `columnGap` — no typed BnView param and no producer, so
    // accepting them would only be two hand-written parsers implementing a name
    // nothing emits. (Note the wrap demo RELIES on Yoga's alignContent default
    // of flex-start; not setting it is precisely how it gets that.) Likewise
    // `display` and `flex`, dropped from the pre-6.1 list: nothing types them
    // and the shells never implemented them.

    // WHERE THE NAMES COME FROM (#255): src/wire-vocabulary.json, via
    // BnWireVocabulary.g.cs. They used to be written out here and mirrored by
    // hand into Kotlin, Objective-C++ and Swift, with a drift test parsing the
    // literals back out of each file to check they agreed. That caught
    // divergence AFTER it was written; generating every copy from one manifest
    // means a name present in one table and missing from another is no longer
    // REPRESENTABLE.
    //
    // The sets, the comparer and the reasoning stay HERE, deliberately. Only the
    // data moved: what "is a style" means to the renderer is renderer policy,
    // and a generated file is the wrong place to argue it.

    /// <summary>Style names that are LAYOUT: the shells route these to the
    /// node's Yoga node, never to the view. See the partition note above.</summary>
    internal static readonly HashSet<string> YogaStyleAttributes =
        new(BnWireVocabulary.YogaStyles, StringComparer.Ordinal);

    /// <summary>Style names that are VISUAL: the shells route these to the
    /// View / UIView (paint), never to Yoga. See the partition note above.</summary>
    internal static readonly HashSet<string> VisualStyleAttributes =
        new(BnWireVocabulary.VisualStyles, StringComparer.Ordinal);

    /// <summary>The union — what "is a style" MEANS to the renderer. It is this union by
    /// definition, so nothing pins it; the two halves are pinned disjoint in
    /// StyleAttributePartitionTests and equal to src/wire-vocabulary.json in
    /// WireVocabularyCodegenTests.</summary>
    internal static readonly HashSet<string> StyleAttributes =
        new(YogaStyleAttributes.Concat(VisualStyleAttributes), StringComparer.Ordinal);
}
