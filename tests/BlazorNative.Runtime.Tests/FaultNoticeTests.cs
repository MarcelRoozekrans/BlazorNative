using System.Diagnostics;
using System.Globalization;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 16.1 — #8: a fault AFTER a handler's first await reaches the shell.
//
// Since #345's fix an export returns as soon as the handler's synchronous part
// has run, so a later fault can no longer be its rc 2. Task 3 made such a fault
// fault the handler's pending Task, in production mode too. This file pins the
// last hop: .NET turns that faulted Task into a FaultNotice host call, op 5 on
// the existing hostCallBegin slot, whose flat-JSON args carry the handler id, the
// event name, the exception type and its message. The shells route it to onError.
//
// EVERY PIN RUNS IN PRODUCTION MODE, StrictErrors = false. Task 3 found a design
// that passed in strict mode while production stayed silent, so strict mode
// appears here only as a control.
//
// The fault is driven through the real exports and the real FakeShellHost
// hostCallBegin, so the notice is read back as the shell would receive it.
//
// DOES NOT COVER:
//   - what a shell DOES with the notice: FaultNoticeTest.kt pins that Kotlin's
//     BridgeRegistrar hands it to BlazorNativeRuntime's onError, and
//     BnFaultNoticeTests.swift the iOS arm;
//   - a fault after the first await in a passthrough host event or in the app
//     multicast: those arms run their subscribers synchronously and have no
//     pending Task to hand on;
//   - fire-and-forget work a handler starts and does not await. Its fault takes
//     Blazor's no-window path once the handler has finished, and is logged only;
//   - a handler still pending at shutdown: its mirror ends Canceled, which is not
//     a fault and sends nothing. ShutdownQuiescenceTests pins the cancellation;
//   - a late fault that RACES shutdown: the handler resumes and throws, and
//     HostSession.Shutdown runs at once. AwaitWholeHandler resumes on the thread
//     pool, so either the mirror is cancelled first and no notice is sent, or the
//     notice arrives after Shutdown has returned. Measured by the final review,
//     over 30 runs: no notice in 28, a notice after Shutdown returned in 2. The
//     fault still reaches stderr in both. Shutdown is process exit only, and the
//     bridge callbacks live for the whole process, so this is documented on
//     HostSession.Shutdown rather than changed, and it is not pinned.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class FaultNoticeTests
{
    private static readonly TimeSpan ExportBudget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    /// <summary>A user's text-field input, carried as the change event's payload. It must
    /// never reach the notice.</summary>
    private const string SecretPayload = "secret-input-7f3a";

    private static int FaultNoticeOp => (int)HostCallOp.FaultNotice;

    // ── Harness ─────────────────────────────────────────────────────────────

    private static NativeRenderer StartSession(bool strict, List<RenderFrame> frames)
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        renderer.StrictErrors = strict;
        renderer.Frames += (f, _) => { lock (frames) frames.Add(f); return ValueTask.CompletedTask; };
        return renderer;
    }

    private static void TearDown(List<Task> pending, Action<ulong, string, Task>? previousObserver)
    {
        FakeShellHost.AutoCompleteHostCall = true;
        // Complete every call still open, as the shell would once the user answers. A call
        // that already completed answers rc 1, which is benign here.
        foreach (var call in FakeShellHost.HostCalls())
            NativeShellBridge.CompleteHostCall(call.RequestId, 0, null);
        Task[] snapshot;
        lock (pending) snapshot = pending.ToArray();
        Task.WaitAll(snapshot.Select(t => t.ContinueWith(_ => { })).ToArray(), Budget);
        Exports.PendingDispatchObserver = previousObserver;
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan budget)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < budget)
        {
            if (condition())
                return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    private static int HandlerFor(List<RenderFrame> frames, string eventName, string? label = null)
    {
        List<RenderFrame> all;
        lock (frames) all = frames.ToList();
        foreach (RenderFrame f in all)
        {
            foreach (AttachEventPatch attach in f.Patches.OfType<AttachEventPatch>().Where(p => p.EventName == eventName))
            {
                if (label is null)
                    return attach.HandlerId;
                ReplaceTextPatch? text = f.Patches.OfType<ReplaceTextPatch>().FirstOrDefault(p => p.Text == label);
                if (text is not null && f.Patches.OfType<CreateNodePatch>()
                        .Any(c => c.NodeId == text.NodeId && c.ParentId == attach.NodeId))
                    return attach.HandlerId;
            }
        }
        throw new Xunit.Sdk.XunitException($"no '{eventName}' handler{(label is null ? "" : $" labelled '{label}'")} was attached");
    }

    /// <summary>Runs one dispatch_event on a worker with a bounded wait.</summary>
    private static int DispatchBounded(int handlerId, string argsJson)
    {
        int rc = -1;
        using var returned = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            rc = Exports.DispatchEventCore((ulong)handlerId, argsJson);
            returned.Set();
        })
        { IsBackground = true, Name = "fault-notice-probe" };
        worker.Start();
        Assert.True(returned.Wait(ExportBudget),
            $"dispatch_event for handler {handlerId} did not return within {ExportBudget.TotalSeconds:0}s");
        return rc;
    }

    private static List<(long RequestId, int Op, string? Args)> Notices()
        => FakeShellHost.HostCalls().Where(c => c.Op == FaultNoticeOp).ToList();

    /// <summary>The late-fault flow: dispatch <paramref name="eventName"/> with the host call
    /// held open, answer the call, and return the FaultNotice the handler's later fault sent.
    /// <paramref name="completeNotices"/> false leaves the notice unanswered, as a shell that
    /// never completes it would.</summary>
    private static (int HandlerId, Dictionary<string, string> Args, long RequestId) RunLateFault(
        bool strict, string eventName, string argsJson, bool completeNotices = true)
    {
        var frames = new List<RenderFrame>();
        var pending = new List<Task>();
        // A TEST TAP only (ruling 1): delivery never goes through this static. It lets the
        // teardown wait for the handler before the renderer is disposed under it.
        Action<ulong, string, Task>? previousObserver = Exports.PendingDispatchObserver;
        Exports.PendingDispatchObserver = (_, _, t) => { lock (pending) pending.Add(t); };
        try
        {
            NativeRenderer renderer = StartSession(strict, frames);
            renderer.Mount<LateFaultNoticeProbe>();
            int handlerId = eventName == "click" ? HandlerFor(frames, "click", "late") : HandlerFor(frames, eventName);

            // The device condition: the capability call stays open.
            FakeShellHost.AutoCompleteHostCall = false;
            Assert.Equal(0, DispatchBounded(handlerId, argsJson));

            // Rule 2 anchor: the handler really is suspended on an open host call, and no
            // notice was sent for a handler that has not faulted yet.
            var geolocation = Assert.Single(FakeShellHost.HostCalls());
            Assert.Equal((int)HostCallOp.Geolocation, geolocation.Op);
            lock (pending)
                Assert.True(pending.Count == 1 && !pending[0].IsCompleted,
                    $"expected exactly one still-running dispatch, got {pending.Count}");

            // The user answers. The handler resumes on the render thread and throws.
            FakeShellHost.AutoCompleteHostCall = completeNotices;
            Assert.Equal(0, NativeShellBridge.CompleteHostCall(geolocation.RequestId, 0, null));

            Assert.True(WaitUntil(() => Notices().Count > 0, Budget),
                $"the handler threw after its first await and no FaultNotice reached the shell within "
                + $"{Budget.TotalSeconds:0}s (strict={strict}). The fault was only logged, which is #8: "
                + $"host calls seen were [{string.Join(", ", FakeShellHost.HostCalls().Select(c => c.Op))}].");

            var notice = Assert.Single(Notices());
            return (handlerId, NativeShellBridge.ParseFlatJsonObject(notice.Args), notice.RequestId);
        }
        finally
        {
            TearDown(pending, previousObserver);
        }
    }

    // ── The late fault reaches the shell ────────────────────────────────────

    [Fact]
    public void AFaultAfterTheFirstAwait_SendsAFaultNotice()
    {
        var (handlerId, args, _) = RunLateFault(strict: false, "click", """{"name":"click"}""");

        Assert.Equal(handlerId.ToString(CultureInfo.InvariantCulture), args["handlerId"]);
        Assert.Equal("click", args["event"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, args["type"]);
        Assert.Equal("late", args["message"]);
        // Ruling 3: a stack trace can carry user data, so it never crosses.
        Assert.False(args.ContainsKey("stack"), "the notice carries a stack trace");
        Assert.DoesNotContain(" at ", string.Join("|", args.Values), StringComparison.Ordinal);
    }

    [Fact]
    public void AFaultAfterTheFirstAwait_InStrictMode_SendsAFaultNotice_Control()
    {
        // Strict mode reached the pending Task even before Task 3's attribution fix, so
        // this control shows the harness can see a notice by a second route. The
        // production pin above is then about the production path alone.
        var (_, args, _) = RunLateFault(strict: true, "click", """{"name":"click"}""");
        Assert.Equal("late", args["message"]);
    }

    [Fact]
    public void AFaultNoticeCarriesNoPayloadField()
    {
        var (handlerId, args, _) = RunLateFault(strict: false, "change",
            $$"""{"name":"change","payload":"{{SecretPayload}}"}""");

        // Anchor: this really is the change handler's notice, so the absence below is about
        // the payload of the dispatch that faulted, not about some other notice.
        Assert.Equal("change", args["event"]);
        Assert.Equal(handlerId.ToString(CultureInfo.InvariantCulture), args["handlerId"]);

        Assert.Equal(["event", "handlerId", "message", "type"], args.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(SecretPayload, string.Join("|", args.Values), StringComparison.Ordinal);
    }

    [Fact]
    public void ASynchronousFault_IsRc2_AndSendsNoFaultNotice()
    {
        // The other branch. A fault before the first await is the export's own rc 2, and a
        // notice as well would report one fault twice.
        var frames = new List<RenderFrame>();
        try
        {
            NativeRenderer renderer = StartSession(strict: false, frames);
            renderer.Mount<LateFaultNoticeProbe>();

            int rc = DispatchBounded(HandlerFor(frames, "click", "sync"), """{"name":"click"}""");

            Assert.Equal(2, rc);
            // Nothing is pending, so nothing can arrive later; the short wait covers a
            // regression that sends a notice from a continuation.
            Thread.Sleep(200);
            Assert.Empty(Notices());
        }
        finally
        {
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }

    // ── The reserved host events, back and navigate ─────────────────────────

    [Theory]
    [InlineData("back")]
    [InlineData("navigate")]
    public void ALateFaultInAReservedHostEvent_SendsAFaultNotice_WithHandlerIdZero(string name)
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exports.HostBackWorkForTests = async () => { await held.Task; throw new InvalidOperationException("late-" + name); };
        Exports.HostNavigateWorkForTests = async _ => { await held.Task; throw new InvalidOperationException("late-" + name); };
        try
        {
            NativeRenderer renderer = StartSession(strict: false, new List<RenderFrame>());
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            renderer.StrictErrors = false;

            string? payload = name == BnHostEvents.Navigate ? "/settings" : null;
            Assert.Equal(0, Exports.DispatchHostEventCore(name, payload));
            Assert.Empty(Notices()); // anchor: nothing has faulted yet

            held.SetResult();
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget),
                $"the '{name}' arm's work faulted after its first await and no FaultNotice was sent");

            Dictionary<string, string> args = NativeShellBridge.ParseFlatJsonObject(Assert.Single(Notices()).Args);
            Assert.Equal("0", args["handlerId"]);
            Assert.Equal(name, args["event"]);
            Assert.Equal("late-" + name, args["message"]);
        }
        finally
        {
            held.TrySetResult();
            Exports.HostBackWorkForTests = null;
            Exports.HostNavigateWorkForTests = null;
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }

    // ── Ruling 4: fire-and-forget from .NET's side ──────────────────────────

    [Fact]
    public void AnAnsweredFaultNotice_LeavesNoPendingHostCall()
    {
        RunLateFault(strict: false, "click", """{"name":"click"}""");
        // RunLateFault's teardown has run; the notice was answered inline by the fake shell.
        Assert.Equal(0, NativeShellBridge.PendingHostCallCountForTests);
    }

    [Fact]
    public void AFaultNoticeTheShellNeverAnswers_IsDroppedAfterItsTimeout()
    {
        TimeSpan previous = NativeShellBridge.FaultNoticeTimeout;
        NativeShellBridge.FaultNoticeTimeout = TimeSpan.FromMilliseconds(300);
        var frames = new List<RenderFrame>();
        var pending = new List<Task>();
        Action<ulong, string, Task>? previousObserver = Exports.PendingDispatchObserver;
        Exports.PendingDispatchObserver = (_, _, t) => { lock (pending) pending.Add(t); };
        try
        {
            NativeRenderer renderer = StartSession(strict: false, frames);
            renderer.Mount<LateFaultNoticeProbe>();
            FakeShellHost.AutoCompleteHostCall = false; // the shell answers nothing
            Assert.Equal(0, DispatchBounded(HandlerFor(frames, "click", "late"), """{"name":"click"}"""));
            long geolocation = Assert.Single(FakeShellHost.HostCalls()).RequestId;
            NativeShellBridge.CompleteHostCall(geolocation, 0, null);

            Assert.True(WaitUntil(() => Notices().Count > 0, Budget), "no FaultNotice was sent");
            // Rule 3 control: while the timeout runs, the unanswered notice IS pending, so
            // the drop below is observed rather than assumed.
            Assert.Equal(1, NativeShellBridge.PendingHostCallCountForTests);

            Assert.True(WaitUntil(() => NativeShellBridge.PendingHostCallCountForTests == 0, Budget),
                "an unanswered FaultNotice stayed in the pending host-call table past its timeout. "
                + "Every late fault on a shell that never answers would leak one entry for the life "
                + "of the process.");
        }
        finally
        {
            NativeShellBridge.FaultNoticeTimeout = previous;
            TearDown(pending, previousObserver);
        }
    }

    [Fact]
    public void SendFaultNotice_NeverThrows_WithNoBridgeRegistered()
    {
        NativeShellBridge.ResetForTests();
        NativeShellBridge.SendFaultNotice(7, "click", new InvalidOperationException("late"));
        Assert.Equal(0, NativeShellBridge.PendingHostCallCountForTests);
    }

    [Fact]
    public void SendFaultNotice_NeverThrows_WhenTheShellRefusesTheBegin()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        try
        {
            FakeShellHost.HostCallBeginReturnCode = -1;
            NativeShellBridge.SendFaultNotice(7, "click", new InvalidOperationException("late"));
            // Anchor: the begin was really attempted, and refused.
            Assert.Equal(FaultNoticeOp, FakeShellHost.LastHostCallOp);
            Assert.True(WaitUntil(() => NativeShellBridge.PendingHostCallCountForTests == 0, Budget),
                "a refused FaultNotice begin left its entry in the pending host-call table");
        }
        finally
        {
            NativeShellBridge.ResetForTests();
        }
    }

    // ── 16.7 (#455): a fault after the handler began a shell call ──
    //
    // The rc contract's boundary is the BEGIN of a host call or a fetch, not the yield. A
    // handler that began one and then faulted in its synchronous part is a FaultNotice with
    // rc 0, whether the shell answered inside hostCallBegin or later. The fake shell answers
    // inside begin here, and the HostCallsCompletedInsideBegin anchor proves it did.
    //
    // DOES NOT COVER:
    //   - a shell call the handler starts on another thread, such as inside Task.Run. The
    //     mark is thread-bound to the synchronous part, so that call is not marked, and a
    //     synchronous fault after it stays rc 2. The contract does not promise this, but
    //     AShellCallBegunOnAnotherThread_DoesNotMarkTheDispatch_SoAFaultAfterItIsRc2 pins it,
    //     and with it the thread-bound choice: it is the fact the 16.7 record's M4 reds;
    //   - a continuation of ANOTHER dispatch that this handler's synchronous part runs inline,
    //     for example by completing a TaskCompletionSource that continuation awaits. Measured
    //     on 2026-10-02: a call that continuation begins marks THIS dispatch, because the mark
    //     is thread-bound, and its fault is captured in this dispatch's window. Not pinned:
    //     whether that outcome is right is open, see the 16.7 record, M4;
    //   - a handler cancelled in its synchronous part. Measured on 2026-10-02: it returns rc 0
    //     with nothing sent, after a begun call or before one, and with the cancelled-Task arm
    //     in DispatchSyncPart made to throw the cancel-only fact still returned rc 0, so that
    //     arm was not reached. Why is not established here; it rests on that measurement;
    //   - a host call or fetch begun by a CHILD component during the handler's re-render.
    //     The re-render is part of the handler's synchronous part, so that call marks the
    //     dispatch too, and a later render fault in the same synchronous part is a
    //     FaultNotice with rc 0, not rc 2.

    [Fact]
    public void AFaultAfterAnInlineAnsweredHostCall_IsAFaultNotice_AndRc0()
    {
        var (rc, _, pending, previous) = DispatchProbe("host-throw");
        try
        {
            Assert.True(FakeShellHost.HostCallsCompletedInsideBegin == 1,
                $"anchor: the fake must answer the geolocation call inside hostCallBegin, but it answered "
                + $"{FakeShellHost.HostCallsCompletedInsideBegin} that way; without it this fact tests nothing.");
            Assert.True(rc == 0,
                $"rc was {rc}: the handler faulted after it began a host call the shell answered inside begin, "
                + "and the fault came back as the dispatch's rc. That is #455.");
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget),
                "no FaultNotice reached the shell for a fault after a begun host call.");
            var args = NativeShellBridge.ParseFlatJsonObject(Assert.Single(Notices()).Args);
            Assert.Equal("click", args["event"]);
            Assert.Equal(typeof(InvalidOperationException).FullName, args["type"]);
            Assert.Equal("after-host", args["message"]);
            Assert.False(string.IsNullOrEmpty(args["handlerId"]), "the notice carries no handler id");
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void AFaultAfterAnInlineAnsweredFetch_IsAFaultNotice_AndRc0()
    {
        var (rc, _, pending, previous) = DispatchProbe("fetch-throw", autoCompleteFetch: true);
        try
        {
            Assert.True(FakeShellHost.FetchesCompletedInsideBegin == 1,
                $"anchor: the fake must answer the fetch inside fetchBegin, but it answered "
                + $"{FakeShellHost.FetchesCompletedInsideBegin} that way; without it this fact tests nothing.");
            Assert.True(rc == 0, $"rc was {rc}: a fault after the handler began a fetch the shell answered inside "
                + "begin came back as the dispatch's rc.");
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget), "no FaultNotice for a fault after a begun fetch.");
            var args = NativeShellBridge.ParseFlatJsonObject(Assert.Single(Notices()).Args);
            Assert.Equal("click", args["event"]);
            Assert.Equal(typeof(InvalidOperationException).FullName, args["type"]);
            Assert.Equal("after-fetch", args["message"]);
            Assert.False(string.IsNullOrEmpty(args["handlerId"]), "the notice carries no handler id");
        }
        finally { TearDown(pending, previous); }
    }

    // ── 16.7 regression guards: no scheduling change after begin ──
    //
    // 16.7 classifies a fault after a begun shell call and changes no scheduling. The first
    // 16.7 design added a yield after begin, and that broke exactly the two things below. Each
    // guard passes today and goes red if a later change puts a yield back after begin.

    [Fact]
    public void AComponentAwaitingAnInlineAnsweredHostCall_InOnInitializedAsync_MountsSynchronously()
    {
        // Guards against any future scheduling change after begin. A page whose
        // OnInitializedAsync awaits a host call the shell answers inside begin must still render
        // synchronously, because Mount<T> refuses a first render that is not complete. The first
        // 16.7 design yielded after begin and broke exactly this mount.
        var frames = new List<RenderFrame>();
        try
        {
            NativeRenderer renderer = StartSession(strict: false, frames);
            Assert.True(FakeShellHost.AutoCompleteHostCall, "the fake must answer host calls inside begin.");

            renderer.Mount<InitAwaitMountProbe>(); // 1: Mount does not throw

            Assert.True(FakeShellHost.HostCallsCompletedInsideBegin == 1, // 2: the counter anchor
                $"anchor: the fake must answer the page's geolocation call INSIDE hostCallBegin, but it answered "
                + $"{FakeShellHost.HostCallsCompletedInsideBegin} that way; without it this fact tests nothing.");

            // 3: the mount frame, the first frame, already carries the continuation's text.
            RenderFrame first;
            lock (frames)
            {
                Assert.True(frames.Count > 0, "Mount returned but no frame was delivered.");
                first = frames[0];
            }
            Assert.True(first.Patches.OfType<ReplaceTextPatch>().Any(p => p.Text.StartsWith("init:", StringComparison.Ordinal)),
                "the first frame lacks the text set after the awaited host call: the first render was not synchronous.");
        }
        finally
        {
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }

    [Fact]
    public void TheBackHoldProbe_HoldsTheRenderThread_AndTheShellsAnswerReleasesIt()
    {
        // Guards that blocking the render thread on an asynchronously answered host call still
        // releases when the shell answers. Any captured-context yield after begin posts the
        // call's continuation to the render thread, which is blocked waiting for it, and
        // deadlocks. The first 16.7 design did exactly that. The JVM BackNoticeTest and both
        // device back tests drive this probe; this fact is the bounded .NET pin, so a
        // regression reds here instead of hanging there.
        var frames = new List<RenderFrame>();
        int rc = -1;
        using var returned = new ManualResetEventSlim(false);
        try
        {
            NativeRenderer renderer = StartSession(strict: false, frames);
            FakeShellHost.AutoCompleteHostCall = false; // the shell holds the capture
            renderer.Mount<BlazorNative.SampleApp.BackHoldProbe>();
            int hold = HandlerFor(frames, "click");

            var worker = new Thread(() =>
            {
                rc = Exports.DispatchEventCore((ulong)hold, """{"name":"click"}""");
                returned.Set();
            })
            { IsBackground = true, Name = "back-hold-probe" };
            worker.Start();

            // Anchor: the capture really began and the handler really is held on it.
            Assert.True(WaitUntil(() => FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Camera), Budget),
                "the Hold handler never began its camera call.");
            Assert.False(returned.Wait(TimeSpan.FromMilliseconds(200)),
                "the Hold dispatch returned while its camera call was still held: the probe no longer holds the render thread.");

            long capture = FakeShellHost.HostCalls().Single(c => c.Op == (int)HostCallOp.Camera).RequestId;
            Assert.Equal(0, NativeShellBridge.CompleteHostCall(capture, 1, null)); // 1 = Cancelled

            Assert.True(returned.Wait(Budget),
                $"the Hold dispatch did not return within {Budget.TotalSeconds:0}s of the shell answering its camera "
                + "call. The render thread is blocked on a call whose continuation was posted back to the render "
                + "thread itself: something now yields after begin.");
            Assert.Equal(0, rc);
            Assert.True(WaitUntil(() =>
            {
                lock (frames)
                    return frames.SelectMany(f => f.Patches.OfType<ReplaceTextPatch>())
                        .Any(p => p.Text == BlazorNative.SampleApp.BackHoldProbe.ReleasedPrefix + "Cancelled");
            }, Budget), "the released Hold handler never rendered its echo.");
        }
        finally
        {
            // An assertion that failed before the answer leaves the camera call held, and the
            // Hold handler blocking the render thread on it. Answer every camera call still open,
            // as the shell would, so the handler can release; a call already answered gives rc 1,
            // which is benign here.
            foreach (var call in FakeShellHost.HostCalls().Where(c => c.Op == (int)HostCallOp.Camera))
                NativeShellBridge.CompleteHostCall(call.RequestId, 1, null);
            returned.Wait(Budget);
            // Only a released handler lets the session go; a deadlocked one is left to the
            // background thread, and the fact has already failed.
            if (returned.IsSet)
            {
                HostSession.ResetForTests();
                NativeShellBridge.ResetForTests();
            }
        }
    }

    private sealed class InitAwaitMountProbe : ComponentBase
    {
        private string _status = "pending";
        [Inject] public IMobileBridge Bridge { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            var s = await Bridge.CheckGeolocationPermissionAsync();
            _status = "init:" + s;
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "text");
            b.AddContent(1, _status);
            b.CloseElement();
        }
    }

    [Fact]
    public void AHostCallTheShellRefusesToBegin_FaultsTheHandlerWithRc2_AndSendsNoNotice()
    {
        var (rc, _, pending, previous) = DispatchProbe("refused-begin", hostCallBeginReturnCode: -1);
        try
        {
            // The log, not LastHostCallOp: a FaultNotice sent from the pool under a mutation would
            // overwrite LastHostCallOp and red this anchor instead of the rc. The log keeps refused begins.
            Assert.True(FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Geolocation),
                "anchor: the begin was never attempted, so this fact tests nothing.");
            Assert.True(rc == 2, $"rc was {rc}: a begin the shell refused throws into the handler before any shell "
                + "call is begun, so that fault must stay the dispatch's rc 2.");
            Assert.Empty(Notices());
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void AFaultAfterBeginningAHostCall_WithoutAwaitingIt_IsAFaultNotice_AndRc0()
    {
        var (rc, _, pending, previous) = DispatchProbe("begin-then-throw");
        try
        {
            Assert.True(rc == 0, $"rc was {rc}: a fault after the handler BEGAN a host call must be a FaultNotice, "
                + "even when it never awaited the call. The contract's boundary is the begin, not the await.");
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget), "no FaultNotice for a fault after a begun call.");
            Assert.Equal("after-begin", NativeShellBridge.ParseFlatJsonObject(Assert.Single(Notices()).Args)["message"]);
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void ACancellationAfterABegunHostCall_IsRc0_AndSendsNothing()
    {
        var (rc, _, pending, previous) = DispatchProbe("begin-then-cancel");
        try
        {
            Assert.True(ShellCallProbe.ReachedThrow, "anchor: the handler body never reached its throw, so rc 0 "
                + "and no notice would prove nothing.");
            Assert.True(FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Geolocation),
                $"anchor: the begin was not attempted; ops seen: [{string.Join(", ", FakeShellHost.HostCalls().Select(c => c.Op))}].");
            Assert.True(rc == 0, $"rc was {rc}: a handler cancelled in its synchronous part after it began a host "
                + "call must match a late cancellation: rc 0 and nothing sent.");
            lock (pending) Assert.True(pending.Count == 0, "nothing may still be running");
            // rc 0 and an empty pending list exclude every notice source, so no wait is needed.
            Assert.Empty(Notices());
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void ACancellationWithNoShellCallBegun_IsAlsoRc0_AndSendsNothing()
    {
        var (rc, _, pending, previous) = DispatchProbe("cancel-only");
        try
        {
            Assert.True(ShellCallProbe.ReachedThrow, "anchor: the handler body never reached its throw, so rc 0 "
                + "and no notice would prove nothing.");
            lock (pending) Assert.True(pending.Count == 0, "nothing may still be running");
            Assert.True(rc == 0, $"rc was {rc}: a cancelled handler is not a fault, so it is rc 0 and sends nothing.");
            Assert.Empty(Notices());
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void AFaultWithNoShellCallBegun_IsStillRc2_Control()
    {
        var (rc, _, pending, previous) = DispatchProbe("completed-throw");
        try
        {
            Assert.True(rc == 2, $"rc was {rc}: a handler that began no shell call and faulted in its synchronous "
                + "part must still return rc 2.");
            lock (pending) Assert.True(pending.Count == 0, "nothing may still be running after a synchronous fault");
            // No wait is needed. rc 2 and an empty pending list exclude every notice source:
            // the FaultedAfterShellCall arm returns rc 0, and DeliverLateFault attaches only
            // to a Pending dispatch. So the check below is immediate by construction.
            Assert.Empty(Notices());
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void ANoticeDotNetSends_DoesNotMarkTheDispatch_SoAFaultAfterItIsRc2()
    {
        var (rc, _, pending, previous) = DispatchProbe("notice-then-throw");
        try
        {
            // Rule 2 anchor: the probe's own notice really was begun inside the synchronous
            // part, so the rc below is about a dispatch that went through the notice path.
            var messages = Notices()
                .Select(n => NativeShellBridge.ParseFlatJsonObject(n.Args)["message"])
                .ToList();
            Assert.True(messages.Contains("notice"),
                $"anchor: the probe's FaultNotice was not begun; notices seen: [{string.Join(", ", messages)}]");
            Assert.True(rc == 2, $"rc was {rc}: a notice .NET sends is not a shell call the handler began, so a "
                + "fault after it must stay rc 2.");
            Assert.DoesNotContain("after-notice", messages);
        }
        finally { TearDown(pending, previous); }
    }

    [Theory]
    [InlineData("back-state-then-throw")]
    [InlineData("back-unhandled-then-throw")]
    public void ABackNoticeDotNetSends_DoesNotMarkTheDispatch_SoAFaultAfterItIsRc2(string label)
    {
        // The other two notices .NET sends itself. Each is its own arm of the exclusion in
        // InvokeHostCallAsync, so each needs a probe that enters it: dropping either arm alone
        // left every other fact green.
        int op = label == "back-state-then-throw" ? (int)HostCallOp.BackState : (int)HostCallOp.BackUnhandled;
        var (rc, _, pending, previous) = DispatchProbe(label);
        try
        {
            // Anchor: the probe's notice really was begun inside the synchronous part.
            Assert.True(FakeShellHost.HostCalls().Any(c => c.Op == op),
                $"anchor: the probe's notice, op {op}, was not begun; ops seen: "
                + $"[{string.Join(", ", FakeShellHost.HostCalls().Select(c => c.Op))}]");
            Assert.True(rc == 2, $"rc was {rc}: a notice .NET sends, op {op}, is not a shell call the handler began, "
                + "so a fault after it must stay rc 2.");
            lock (pending) Assert.True(pending.Count == 0, "nothing may still be running after a synchronous fault");
            // rc 2 and an empty pending list exclude every FaultNotice source, as in the control.
            Assert.Empty(Notices());
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void AShellCallBegunOnAnotherThread_DoesNotMarkTheDispatch_SoAFaultAfterItIsRc2()
    {
        // The mark is thread-bound to the synchronous part, never the flowing AsyncLocal scope.
        // A call begun inside Task.Run carries the flowing scope but runs on a pool thread, so
        // it must not mark the dispatch, and the synchronous fault after it stays rc 2.
        var (rc, _, pending, previous) = DispatchProbe("pool-begin-then-throw");
        try
        {
            // Anchors: the call really was begun, answered inside begin, on a thread other than
            // the handler's. Without them rc 2 would only show that no call was begun.
            Assert.True(FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Geolocation),
                $"anchor: the begin was not attempted; ops seen: [{string.Join(", ", FakeShellHost.HostCalls().Select(c => c.Op))}].");
            Assert.True(FakeShellHost.HostCallsCompletedInsideBegin == 1,
                $"anchor: the fake answered {FakeShellHost.HostCallsCompletedInsideBegin} calls inside begin, not 1.");
            Assert.True(ShellCallProbe.PoolBeginThread > 0 && ShellCallProbe.PoolBeginThread != ShellCallProbe.HandlerThread,
                $"anchor: the call was begun on thread {ShellCallProbe.PoolBeginThread}, the handler ran on "
                + $"{ShellCallProbe.HandlerThread}; the begin must be on another thread.");
            Assert.True(rc == 2, $"rc was {rc}: a call begun on another thread is not marked, so a synchronous fault "
                + "after it stays rc 2. rc 0 means the dispatch was marked although no call was begun on its synchronous "
                + "part's thread, for example because the mark followed the flowing scope.");
            lock (pending) Assert.True(pending.Count == 0, "nothing may still be running after a synchronous fault");
            // rc 2 and an empty pending list exclude every notice source, as in the control.
            Assert.Empty(Notices());
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void DispatchUiEventAsync_FaultsWithAFaultAfterABegunHostCall()
    {
        // DispatchUiEventAsync is the direct caller's path, BnTestHost's and the Renderer
        // tests'. It has no FaultNotice, so the classification must not swallow the fault:
        // its Task faults with it, exactly as for a plain synchronous fault.
        var frames = new List<RenderFrame>();
        var pending = new List<Task>();
        Action<ulong, string, Task>? previous = Exports.PendingDispatchObserver;
        try
        {
            NativeRenderer renderer = StartSession(strict: false, frames);
            renderer.Mount<ShellCallProbe>();
            int handlerId = HandlerFor(frames, "click", "begin-then-throw");

            Task dispatch = renderer.DispatchUiEventAsync(new NativeUiEvent(0, handlerId, "click", null));

            Assert.True(WaitUntil(() => dispatch.IsCompleted, Budget), "DispatchUiEventAsync did not complete");
            // Anchor: the bridge really began the geolocation call, so the dispatch was marked.
            Assert.Contains(FakeShellHost.HostCalls(), c => c.Op == (int)HostCallOp.Geolocation);
            Assert.True(dispatch.IsFaulted,
                $"DispatchUiEventAsync ended {dispatch.Status}: a fault after a begun host call was swallowed "
                + "on the direct path, which has no FaultNotice to carry it.");
            var fault = Assert.IsType<InvalidOperationException>(dispatch.Exception!.InnerException);
            Assert.Equal("after-begin", fault.Message);
        }
        finally { TearDown(pending, previous); }
    }

    /// <summary>16.7 (#455): handlers that fault after beginning a shell call, and controls.</summary>
    private sealed class ShellCallProbe : ComponentBase
    {
        /// <summary>Set immediately before a cancellation probe throws: proves the handler body ran.
        /// DispatchProbe resets it.</summary>
        public static volatile bool ReachedThrow;

        /// <summary>The threads pool-begin-then-throw ran its handler and its begin on.
        /// DispatchProbe resets both.</summary>
        public static volatile int HandlerThread, PoolBeginThread;

        [Inject] public IMobileBridge Bridge { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            Button(b, 0, "host-throw", async () =>
            {
                await Bridge.CheckGeolocationPermissionAsync();
                throw new InvalidOperationException("after-host");
            });
            Button(b, 10, "begin-then-throw", () =>
            {
                _ = Bridge.CheckGeolocationPermissionAsync();
                throw new InvalidOperationException("after-begin");
            });
            Button(b, 20, "completed-throw", async () =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("no-shell-call");
            });
            Button(b, 30, "notice-then-throw", () =>
            {
                // A notice .NET sends is not a shell call the handler began: it must not mark.
                NativeShellBridge.SendFaultNotice(0, "probe", new InvalidOperationException("notice"));
                throw new InvalidOperationException("after-notice");
            });
            Button(b, 60, "begin-then-cancel", async () =>
            {
                _ = Bridge.CheckGeolocationPermissionAsync();
                await Task.CompletedTask;
                ReachedThrow = true;
                throw new OperationCanceledException();
            });
            Button(b, 70, "cancel-only", async () =>
            {
                await Task.CompletedTask;
                ReachedThrow = true;
                throw new OperationCanceledException();
            });
            Button(b, 50, "refused-begin", async () =>
            {
                await Bridge.CheckGeolocationPermissionAsync();
            });
            Button(b, 80, "pool-begin-then-throw", () =>
            {
                HandlerThread = Environment.CurrentManagedThreadId;
                // The fake answers inside begin, so this wait never blocks on the shell.
                Task.Run(() =>
                {
                    PoolBeginThread = Environment.CurrentManagedThreadId;
                    return Bridge.CheckGeolocationPermissionAsync().AsTask();
                }).Wait();
                throw new InvalidOperationException("after-pool-begin");
            });
            Button(b, 90, "back-state-then-throw", () =>
            {
                NativeShellBridge.SendBackState(true);
                throw new InvalidOperationException("after-back-state");
            });
            Button(b, 100, "back-unhandled-then-throw", () =>
            {
                NativeShellBridge.SendBackUnhandled();
                throw new InvalidOperationException("after-back-unhandled");
            });
            Button(b, 40, "fetch-throw", async () =>
            {
                await Bridge.FetchAsync(new BridgeHttpRequest("https://inline.test/"));
                throw new InvalidOperationException("after-fetch");
            });
        }

        private void Button(RenderTreeBuilder b, int seq, string label, Func<Task> onClick)
        {
            b.OpenElement(seq, "button");
            b.AddAttribute(seq + 1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, onClick));
            b.AddContent(seq + 2, label);
            b.CloseElement();
        }

        private void Button(RenderTreeBuilder b, int seq, string label, Action onClick)
        {
            b.OpenElement(seq, "button");
            b.AddAttribute(seq + 1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, onClick));
            b.AddContent(seq + 2, label);
            b.CloseElement();
        }
    }

    private static (int Rc, List<RenderFrame> Frames, List<Task> Pending, Action<ulong, string, Task>? Previous)
        DispatchProbe(string label, bool autoCompleteFetch = false, int hostCallBeginReturnCode = 0)
    {
        var frames = new List<RenderFrame>();
        var pending = new List<Task>();
        Action<ulong, string, Task>? previous = Exports.PendingDispatchObserver;
        Exports.PendingDispatchObserver = (_, _, t) => { lock (pending) pending.Add(t); };
        NativeRenderer renderer = StartSession(strict: false, frames);
        renderer.Mount<ShellCallProbe>();
        int handlerId = HandlerFor(frames, "click", label);
        ShellCallProbe.ReachedThrow = false;
        ShellCallProbe.HandlerThread = ShellCallProbe.PoolBeginThread = 0;
        FakeShellHost.AutoCompleteHostCall = true;
        FakeShellHost.AutoCompleteFetch = autoCompleteFetch;
        FakeShellHost.HostCallBeginReturnCode = hostCallBeginReturnCode;
        int rc = DispatchBounded(handlerId, """{"name":"click"}""");
        return (rc, frames, pending, previous);
    }

    /// <summary>"click" awaits a held geolocation check, then throws; "change" does the
    /// same with a payload; "sync" throws before any await.</summary>
    private sealed class LateFaultNoticeProbe : ComponentBase
    {
        [Inject] public IMobileBridge Bridge { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, async () =>
            {
                await Bridge.CheckGeolocationPermissionAsync();
                throw new InvalidOperationException("late");
            }));
            b.AddContent(2, "late");
            b.CloseElement();

            b.OpenElement(3, "input");
            b.AddAttribute(4, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, async (ChangeEventArgs _) =>
            {
                await Bridge.CheckGeolocationPermissionAsync();
                throw new InvalidOperationException("late");
            }));
            b.CloseElement();

            b.OpenElement(5, "button");
            b.AddAttribute(6, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this,
                () => throw new InvalidOperationException("sync")));
            b.AddContent(7, "sync");
            b.CloseElement();
        }
    }
}
