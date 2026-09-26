using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 16.1 — the capture window is scoped to ONE dispatch's synchronous part.
//
// Before 16.1 the window was a renderer-wide depth counter, decremented in the
// dispatch lambda's finally. An async handler suspended on an await therefore
// held the counter at 1, and every dispatch that arrived meanwhile was treated
// as NESTED inside it:
//   - its navigation swap was queued behind the unrelated handler, so "← Back"
//     returned rc 0 while the screen stayed on /camera (the 16.0 spike's case C,
//     measured as rcBack=0 framesDuringBack=1 routeAfterBack=/camera);
//   - its fault landed in the shared capture slot, returned rc 0, and surfaced
//     later as the FIRST handler's fault.
// Under the inline dispatcher neither was observable, because the first export
// never returned. Now that it does (#345), both are. This is spike requirement 1.
//
// Each pin drives the real exports against the real HostSession renderer, and
// runs every export on a worker with a bounded wait, so a regression back to a
// blocking export is a failed assertion, never a hung job.
//
// A fault raised in a continuation after the first await is attributed to its
// own dispatch too, through the pending Task, in PRODUCTION mode as well as
// strict: ALateFault_InProductionMode_FaultsThePendingTask.
//
// DOES NOT COVER: two dispatches suspended at once; how a late fault reaches the
// SHELL, which is FaultNotice's pin; a fire-and-forget fault raised after the
// handler's Task completed, which takes the no-window path by design; or the
// host-event arms, which are HostEventArmThreadTests. A nested dispatch's FAULT
// is DispatchEventTests.Dispatch_NestedDispatchInsideHandler_*; its queued
// navigation is pinned here.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class DispatchWindowScopeTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    /// <summary>The export no longer waits on the host, so it returns in
    /// milliseconds. A second is generous for CI and still turns a regression
    /// into a failed assertion rather than a hang.</summary>
    private static readonly TimeSpan ExportBudget = TimeSpan.FromSeconds(1);

    /// <summary>For waits on work that really does take a while: a continuation
    /// completing after a host call is released.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    // ── Frame bookkeeping. Frames arrive on the render thread, and a continuation's
    // frames can arrive while the test thread reads, so every access takes the lock.

    private sealed class FrameLog
    {
        private readonly List<RenderFrame> _frames = new();

        public void Add(RenderFrame f) { lock (_frames) _frames.Add(f); }

        public int Count { get { lock (_frames) return _frames.Count; } }

        public List<RenderFrame> Since(int start)
        {
            lock (_frames) return _frames.Skip(start).ToList();
        }

        /// <summary>The click handler of the button whose label is
        /// <paramref name="label"/>, from the LATEST frame that created it.</summary>
        public int ClickHandlerForLabel(string label)
        {
            List<RenderFrame> all = Since(0);
            for (int i = all.Count - 1; i >= 0; i--)
            {
                RenderFrame f = all[i];
                ReplaceTextPatch? text = f.Patches.OfType<ReplaceTextPatch>()
                    .FirstOrDefault(p => p.Text == label);
                if (text is null)
                    continue;
                CreateNodePatch? create = f.Patches.OfType<CreateNodePatch>()
                    .FirstOrDefault(p => p.NodeId == text.NodeId);
                if (create?.ParentId is not int button)
                    continue;
                AttachEventPatch? attach = f.Patches.OfType<AttachEventPatch>()
                    .FirstOrDefault(p => p.NodeId == button && p.EventName == "click");
                if (attach is not null)
                    return attach.HandlerId;
            }
            throw new Xunit.Sdk.XunitException(
                $"no frame created a clickable button labelled '{label}'. The page this test drives "
                + "moved or was relabelled; re-point the test deliberately rather than deleting it.");
        }
    }

    private static bool HasText(RenderFrame frame, string text)
        => frame.Patches.OfType<ReplaceTextPatch>().Any(p => p.Text == text);

    /// <summary>BnDemo's capability menu: the "Explore" heading and its "Camera"
    /// row. Neither string appears on /camera, so a frame carrying both is the
    /// swap back to the menu.</summary>
    private static bool IsBnDemoMenu(RenderFrame frame)
        => HasText(frame, "Explore") && HasText(frame, "Camera");

    /// <summary>Runs one dispatch_event on a worker, with a bounded wait. Returns
    /// the rc, or fails naming the handler when the export does not return.</summary>
    private static int DispatchBounded(int handlerId, string what)
    {
        int rc = -1;
        using var returned = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            rc = Exports.DispatchEventCore((ulong)handlerId, ClickArgs);
            returned.Set();
        })
        { IsBackground = true, Name = "dispatch-probe" };
        worker.Start();
        Assert.True(returned.Wait(ExportBudget),
            $"dispatch_event for '{what}' did not return within {ExportBudget.TotalSeconds:0}s. "
            + "The export is waiting on something other than the handler's synchronous part.");
        return rc;
    }

    private static (NativeRenderer Renderer, FrameLog Frames) StartSession()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new FrameLog();
        renderer.Frames += (f, _) => { frames.Add(f); return ValueTask.CompletedTask; };
        return (renderer, frames);
    }

    /// <summary>Installs an observer that records every still-running dispatch, and
    /// returns the one it replaced so <see cref="TearDown"/> can restore it.</summary>
    private static Action<ulong, string, Task>? ObservePending(List<Task> pending)
    {
        Action<ulong, string, Task>? previous = Exports.PendingDispatchObserver;
        Exports.PendingDispatchObserver = (_, _, t) => { lock (pending) pending.Add(t); };
        return previous;
    }

    private static void TearDown(List<Task> pending, Action<ulong, string, Task>? previousObserver)
    {
        FakeShellHost.AutoCompleteHostCall = true;
        if (FakeShellHost.LastHostCallRequestId >= 0)
        {
            // Complete a held call as the shell would once the user answers. A call that
            // already completed answers rc 1 (unknown id), which is benign here.
            NativeShellBridge.CompleteHostCall(
                FakeShellHost.LastHostCallRequestId, (int)CameraStatus.Cancelled, null);
        }
        // Let every suspended handler finish before the renderer is disposed under it.
        Task[] snapshot;
        lock (pending) snapshot = pending.ToArray();
        Task.WaitAll(snapshot.Select(t => t.ContinueWith(_ => { })).ToArray(), Budget);
        Exports.PendingDispatchObserver = previousObserver;
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>Mounts BnDemo, navigates to /camera through the menu, and clicks
    /// "Take Photo". With <paramref name="holdTheCall"/> the host call stays open,
    /// so the Take Photo handler is left suspended on its await.</summary>
    private static void ReachCameraAndTakePhoto(FrameLog frames, bool holdTheCall, List<Task> pending)
    {
        Assert.Equal(0, HostSession.TryMount("BnDemo"));
        Assert.Equal(0, DispatchBounded(frames.ClickHandlerForLabel("Camera"), "Camera"));
        Assert.Equal(BnCameraRoute, HostSession.CurrentNavigationManager!.CurrentRoute);

        FakeShellHost.AutoCompleteHostCall = !holdTheCall;
        Assert.Equal(0, DispatchBounded(frames.ClickHandlerForLabel("Take Photo"), "Take Photo"));
        Assert.True(FakeShellHost.LastHostCallRequestId >= 0,
            "Take Photo never began a host call, so there is no await to be suspended on. Has "
            + "BnCameraDemo's button moved?");

        if (holdTheCall)
        {
            // Anchor: the handler really is suspended, so the next dispatch really does
            // arrive while another handler's await is open.
            lock (pending)
                Assert.True(pending.Count == 1 && !pending[0].IsCompleted,
                    $"expected exactly one still-running dispatch after Take Photo, got {pending.Count}");
        }
        else
        {
            // The positive control's premise: nothing is suspended when Back arrives.
            Task[] snapshot;
            lock (pending) snapshot = pending.ToArray();
            Assert.True(Task.WaitAll(snapshot, Budget), "Take Photo did not finish with the call auto-completed");
        }
    }

    private const string BnCameraRoute = "/camera";

    /// <summary>Clicks "← Back" and returns its rc and the frames that arrived
    /// DURING the export call.</summary>
    private static (int Rc, List<RenderFrame> During) ClickBack(FrameLog frames)
    {
        int back = frames.ClickHandlerForLabel("← Back");
        int before = frames.Count;
        int rc = DispatchBounded(back, "← Back");
        return (rc, frames.Since(before));
    }

    // ── The cascade: requirement 1 ────────────────────────────────────────────

    [Fact]
    public void ANavigationDuringAnotherHandlersSuspension_SwapsBeforeItsOwnExportReturns()
    {
        var pending = new List<Task>();
        var (_, frames) = StartSession();
        var previousObserver = ObservePending(pending);
        try
        {
            ReachCameraAndTakePhoto(frames, holdTheCall: true, pending);

            var (rc, during) = ClickBack(frames);

            Assert.Equal(0, rc);
            Assert.True(during.Any(IsBnDemoMenu),
                $"'← Back' returned rc 0, but none of the {during.Count} frames delivered during "
                + "its export carried BnDemo's menu. The swap was deferred behind the suspended "
                + "Take Photo handler: the capture window is spanning another dispatch's await "
                + "again, so back reports 'handled' while the screen stays on /camera.");
            Assert.Equal("/", HostSession.CurrentNavigationManager!.CurrentRoute);

            // Still suspended: the swap did not wait for the held call to finish either.
            lock (pending)
                Assert.False(pending[0].IsCompleted, "the held Take Photo handler finished early");
        }
        finally
        {
            TearDown(pending, previousObserver);
        }
    }

    [Fact]
    public void ANavigationWithNoSuspendedHandler_SwapsBeforeItsExportReturns_PositiveControl()
    {
        // Rule 3: the frame check above must be able to SEE the swap. Same flow, same
        // detector, nothing suspended, so the menu frame must arrive during the export.
        var pending = new List<Task>();
        var (_, frames) = StartSession();
        var previousObserver = ObservePending(pending);
        try
        {
            ReachCameraAndTakePhoto(frames, holdTheCall: false, pending);

            var (rc, during) = ClickBack(frames);

            Assert.Equal(0, rc);
            Assert.True(during.Any(IsBnDemoMenu),
                $"none of the {during.Count} frames delivered during '← Back' carried BnDemo's "
                + "menu even with nothing suspended, so IsBnDemoMenu no longer recognises the "
                + "swap and the cascade pin above would pass or fail for the wrong reason.");
            Assert.Equal("/", HostSession.CurrentNavigationManager!.CurrentRoute);
        }
        finally
        {
            TearDown(pending, previousObserver);
        }
    }

    // ── Fault attribution ─────────────────────────────────────────────────────

    [Fact]
    public async Task AFaultInASecondDispatch_IsAttributedToIt_NotToTheSuspendedFirst()
    {
        var pending = new List<Task>();
        var (renderer, frames) = StartSession();
        var previousObserver = ObservePending(pending);
        SuspendThenThrowProbe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SuspendThenThrowProbe.AResumed = false;
        try
        {
            renderer.Mount<SuspendThenThrowProbe>();

            // A: suspends on the gate.
            Assert.Equal(0, DispatchBounded(frames.ClickHandlerForLabel("A"), "A"));
            Task a;
            lock (pending)
            {
                Assert.True(pending.Count == 1,
                    $"A should be the one still-running dispatch, got {pending.Count}");
                a = pending[0];
            }
            Assert.False(a.IsCompleted, "A finished before its gate opened");

            // B: throws synchronously while A is suspended. Its fault is ITS rc.
            int rcB = DispatchBounded(frames.ClickHandlerForLabel("B"), "B");
            Assert.True(rcB == 2,
                $"B threw before yielding but its export returned rc {rcB}, not 2. Its fault was "
                + "captured somewhere other than B's own dispatch window — a shared slot that "
                + "only clears when no other dispatch is outstanding, which the suspended A is.");

            // A's completion is independent of B's fault: it runs to completion clean.
            SuspendThenThrowProbe.Gate.SetResult();
            await Task.WhenAny(a, Task.Delay(Budget));
            Assert.True(a.IsCompleted, "A never finished after its gate opened");
            Assert.True(SuspendThenThrowProbe.AResumed, "A's continuation never ran");
            Assert.True(a.Status == TaskStatus.RanToCompletion,
                $"A ended {a.Status} ({a.Exception?.GetBaseException().Message}). A itself never "
                + "faults, so a fault here is B's, surfacing as the suspended first handler's.");
        }
        finally
        {
            SuspendThenThrowProbe.Gate.TrySetResult();
            TearDown(pending, previousObserver);
        }
    }

    // ── A late fault, in production mode: C1 of the Task 3 review ───────────────
    //
    // Blazor's GetErrorHandledTask catches a handler's fault after its first await and
    // routes it to HandleException, and the Task Blazor returns then completes
    // SUCCESSFULLY. Before the fix, only StrictErrors turned that into a faulted Task:
    // in production, which is not strict, the fault was logged and the pending Task
    // ran to completion, so nothing downstream could ever deliver it.

    private static async Task<Task> RunLateFault(bool strict)
    {
        var pending = new List<Task>();
        var (renderer, frames) = StartSession();
        var previousObserver = ObservePending(pending);
        LateFaultProbe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            renderer.StrictErrors = strict;
            renderer.Mount<LateFaultProbe>();

            Assert.Equal(0, DispatchBounded(frames.ClickHandlerForLabel("late"), "late"));
            Task handler;
            lock (pending)
            {
                Assert.True(pending.Count == 1,
                    $"the late-fault handler should be the one still-running dispatch, got {pending.Count}");
                handler = pending[0];
            }
            Assert.False(handler.IsCompleted, "the handler finished before its gate opened");

            LateFaultProbe.Gate.SetResult();
            await Task.WhenAny(handler, Task.Delay(Budget));
            Assert.True(handler.IsCompleted, "the handler never finished after its gate opened");
            return handler;
        }
        finally
        {
            LateFaultProbe.Gate.TrySetResult();
            TearDown(pending, previousObserver);
        }
    }

    [Fact]
    public async Task ALateFault_InProductionMode_FaultsThePendingTask()
    {
        Task handler = await RunLateFault(strict: false);

        Assert.True(handler.IsFaulted,
            $"the handler threw after its first await, in production mode, and its pending Task "
            + $"ended {handler.Status}. The late fault was only logged: Blazor routed it to "
            + "HandleException, which found no dispatch to attribute it to, so no FaultNotice "
            + "could ever reach the shell.");
        Assert.Equal("late-boom", handler.Exception!.GetBaseException().Message);
    }

    [Fact]
    public async Task ALateFault_InStrictMode_FaultsThePendingTask_PositiveControl()
    {
        // Rule 3: strict mode faulted the pending Task even before the fix, through
        // HandleException's rethrow. So this proves the observer, the probe and the
        // assertions can see a late fault; the production-mode pin above is then about
        // the attribution alone.
        Task handler = await RunLateFault(strict: true);

        Assert.True(handler.IsFaulted, $"the pending Task ended {handler.Status} even in strict mode");
        Assert.Equal("late-boom", handler.Exception!.GetBaseException().Message);
    }

    // ── A nested dispatch's queued navigation: I1 of the Task 3 review ──────────

    [Fact]
    public void ANavigationQueuedInsideANestedDispatch_StillRunsBeforeTheOuterExportReturns()
    {
        var pending = new List<Task>();
        var (renderer, frames) = StartSession();
        var previousObserver = ObservePending(pending);
        NestedNavigateProbe.Renderer = renderer;
        try
        {
            renderer.Mount<NestedNavigateProbe>();
            NestedNavigateProbe.InnerHandlerId = FindChangeHandler(frames);

            int before = frames.Count;
            int rc = DispatchBounded(frames.ClickHandlerForLabel("outer"), "outer");
            List<RenderFrame> during = frames.Since(before);

            Assert.Equal(0, rc);
            Assert.True(NestedNavigateProbe.InnerRan, "the nested dispatch never ran");
            // Anchor: BnSettingsPage's title. It is not on the probe, so it can only
            // arrive with the swap.
            Assert.True(during.Any(f => HasText(f, "Settings")),
                $"none of the {during.Count} frames delivered during the outer export carried "
                + "BnSettingsPage. The navigation the NESTED dispatch queued never ran: its scope "
                + "closed while the outer one was still open, and its queued actions were not "
                + "handed to the outer scope.");
            Assert.Equal("/settings", HostSession.CurrentNavigationManager!.CurrentRoute);
        }
        finally
        {
            TearDown(pending, previousObserver);
        }
    }

    private static int FindChangeHandler(FrameLog frames)
    {
        RenderFrame mount = frames.Since(0)[0];
        return Assert.Single(mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "change").HandlerId;
    }

    /// <summary>"late" awaits a test-held gate, then throws. Static slots are safe
    /// under the "host-session" collection.</summary>
    private sealed class LateFaultProbe : ComponentBase
    {
        public static TaskCompletionSource Gate = new();

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, async () =>
            {
                await Gate.Task;
                throw new InvalidOperationException("late-boom");
            }));
            b.AddContent(2, "late");
            b.CloseElement();
        }
    }

    /// <summary>"outer" runs a NESTED dispatch of the input's change handler, which
    /// navigates. The navigation queues into the nested dispatch's scope, which closes
    /// while the outer one is still open.</summary>
    private sealed class NestedNavigateProbe : ComponentBase
    {
        public static NativeRenderer? Renderer;
        public static int InnerHandlerId;
        public static bool InnerRan;

        [Inject] public INavigationManager Navigation { get; set; } = default!;

        protected override void OnInitialized() => InnerRan = false;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () =>
            {
                // Already on the render thread, so this runs inline, nested.
                Renderer!.DispatchUiEventAsync(new NativeUiEvent(0, InnerHandlerId, "change", "go"))
                    .GetAwaiter().GetResult();
            }));
            b.AddContent(2, "outer");
            b.CloseElement();

            b.OpenElement(10, "input");
            b.AddAttribute(11, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, () =>
            {
                InnerRan = true;
                return Navigation.NavigateToAsync("/settings").AsTask();
            }));
            b.CloseElement();
        }
    }

    /// <summary>"A" awaits a test-held gate; "B" throws synchronously. Static slots
    /// are safe under the "host-session" collection.</summary>
    private sealed class SuspendThenThrowProbe : ComponentBase
    {
        public static TaskCompletionSource Gate = new();
        public static bool AResumed;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, async () =>
            {
                await Gate.Task;
                AResumed = true;
            }));
            b.AddContent(2, "A");
            b.CloseElement();

            b.OpenElement(10, "button");
            b.AddAttribute(11, "onclick", EventCallback.Factory.Create<MouseEventArgs>(
                this, () => throw new InvalidOperationException("b-boom")));
            b.AddContent(12, "B");
            b.CloseElement();
        }
    }
}
