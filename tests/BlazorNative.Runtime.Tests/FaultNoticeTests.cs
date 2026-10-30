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
// Later phases added sections below, each with its own limits:
//   - 16.7 (#455): a fault after the handler BEGAN a host call or a fetch is a FaultNotice with
//     rc 0, whether the shell answered inside hostCallBegin or later;
//   - 17.1 (#464): a fault belongs to the dispatch whose own code raised it, even when that code
//     is another dispatch's continuation running inline inside this dispatch's window;
//   - 18.1 (#564): a dispatch whose render thread logs into a blocked stderr still returns
//     within its budget, and a dispatch that overruns it is torn down by its own fact.
//
// DOES NOT COVER, for the 16.1 facts:
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
        // 18.1 (#564): every late FaultNotice send is recorded, so TearDown can wait for it.
        // Installed before the reset and never removed, so a send from an earlier fact that
        // is still in flight is recorded too, wherever it lands.
        Exports.LateFaultNoticeObserver = (_, _, send) => { lock (s_noticeSends) s_noticeSends.Add(send); };
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        renderer.StrictErrors = strict;
        renderer.Frames += (f, _) => { lock (frames) frames.Add(f); return ValueTask.CompletedTask; };
        return renderer;
    }

    /// <summary>Tears a fact down. <paramref name="release"/> frees whatever the fact holds
    /// open, such as a gate a handler waits on, so a dispatch that overran its budget can
    /// finish. When the last <see cref="DispatchBounded"/> overran, this first waits for that
    /// dispatch to return. Then it waits for every late FaultNotice send to complete, so each
    /// notice lands in this fact and not in the next one (18.1, #564).</summary>
    private static void TearDown(List<Task> pending, Action<ulong, string, Task>? previousObserver,
        Action? release = null)
    {
        release?.Invoke();
        if (s_overrunDispatch is { } overrun)
        {
            s_overrunDispatch = null;
            // Once it has returned, the dispatch has handed its notice send to the tap.
            overrun.Join(Budget);
        }
        WaitForNoticeSends();
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

    /// <summary>Every late FaultNotice send <see cref="Exports.LateFaultNoticeObserver"/>
    /// has handed on and <see cref="WaitForNoticeSends"/> has not yet seen complete.</summary>
    private static readonly List<Task> s_noticeSends = [];

    /// <summary>Waits, bounded by <see cref="Budget"/>, until every recorded FaultNotice send
    /// has completed, so its notice is in the fake's log. A send whose task is complete has
    /// begun its host call, because SendNotice begins it synchronously.</summary>
    private static void WaitForNoticeSends()
    {
        Task[] sends;
        lock (s_noticeSends) sends = s_noticeSends.Where(t => !t.IsCompleted).ToArray();
        s_waitingForNoticeSends?.Invoke(sends.Length);
        Task.WaitAll(sends.Select(t => t.ContinueWith(_ => { })).ToArray(), Budget);
        lock (s_noticeSends) s_noticeSends.RemoveAll(t => t.IsCompleted);
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

    /// <summary>The managed thread the last <see cref="DispatchBounded"/> called
    /// DispatchEventCore on, the shell's dispatch lane in these facts.</summary>
    private static volatile int s_lastDispatchThread;

    /// <summary>18.1 (#564): called by <see cref="WaitForNoticeSends"/>, from TearDown or a
    /// fact, just before it waits, with the number of sends still in flight that it is about
    /// to wait for. A fact sets it to learn that a send it holds is being waited on.</summary>
    private static Action<int>? s_waitingForNoticeSends;

    /// <summary>The worker of the last <see cref="DispatchBounded"/> that overran its budget
    /// and may still be running, until <see cref="TearDown"/> waits for it.</summary>
    private static Thread? s_overrunDispatch;

    /// <summary>Runs one dispatch_event on a worker with a bounded wait.</summary>
    private static int DispatchBounded(int handlerId, string argsJson)
    {
        int rc = -1;
        // Not disposed on an overrun: the worker still sets it when the dispatch returns.
        var returned = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            s_lastDispatchThread = Environment.CurrentManagedThreadId;
            rc = Exports.DispatchEventCore((ulong)handlerId, argsJson);
            returned.Set();
        })
        { IsBackground = true, Name = "fault-notice-probe" };
        worker.Start();
        if (!returned.Wait(ExportBudget))
            s_overrunDispatch = worker;
        else
            returned.Dispose();
        Assert.True(s_overrunDispatch != worker,
            $"dispatch_event for handler {handlerId} did not return within {ExportBudget.TotalSeconds:0}s");
        return rc;
    }

    /// <summary>Every text a ReplaceTextPatch has set so far, in delivery order.</summary>
    private static List<string> FrameTexts(List<RenderFrame> frames)
    {
        lock (frames)
            return frames.SelectMany(f => f.Patches.OfType<ReplaceTextPatch>()).Select(p => p.Text).ToList();
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
        // The other branch. A synchronous fault before the handler began a host call or a
        // fetch is the export's own rc 2, and a notice as well would report one fault twice.
        // This handler begins none, so its fault is rc 2.
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
    //     and with it the thread-bound choice: the 16.7 record's M4-after is its red under M4;
    //   - a continuation of ANOTHER dispatch that this handler's synchronous part runs inline.
    //     That was #464; the 17.1 section pins it now;
    //   - a handler cancelled in its synchronous part. Measured on 2026-10-02: it returns rc 0
    //     with nothing sent, after a begun call or before one, and with the cancelled-Task arm
    //     in DispatchSyncPart made to throw the cancel-only fact still returned rc 0, so that
    //     arm was not reached. Why is not established here; it rests on that measurement;
    //   - a host call or fetch begun by a NAVIGATION SWAP the handler queued. The swap runs
    //     when the synchronous part's scope closes, still inside the export, so the rc counts
    //     its faults; but the scope is no longer this thread's synchronous scope then, so a
    //     call the new page begins never marks the dispatch, and a later fault in the swap
    //     stays rc 2 unless the handler itself had begun a call. That rests on reading
    //     DispatchSyncPart's finally, which restores the thread-bound scope before it drains.
    //
    // COVERED SINCE 17.1, and listed above as a gap until 17.7: a host call or fetch begun by
    // a CHILD component during the handler's re-render. The re-render is part of the
    // handler's synchronous part, so that call marks the dispatch too, and a later render
    // fault in the same synchronous part is a FaultNotice with rc 0, not rc 2.
    // AChildsBegunCall_DuringTheReRender_MarksTheDispatch_SoALaterRenderFaultIsRc0 pins that
    // rc 0 half, and AFaultBeforeASiblingBeginsAShellCall_IsStillRc2_AndSendsNoNotice pins
    // the order: a fault captured before that call stays rc 2.

    [Fact]
    public void AFaultAfterAnInlineAnsweredHostCall_IsAFaultNotice_AndRc0()
    {
        var (rc, frames, pending, previous) = DispatchProbe("host-throw");
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

            // The session survives the reclassified fault: the next dispatch, a handler that
            // begins no shell call and completes, returns rc 0 and its frame arrives before the
            // export returns, as a synchronous part's frames always do.
            Assert.DoesNotContain(FrameTexts(frames), t => t.StartsWith("ok:", StringComparison.Ordinal));
            int next = DispatchBounded(HandlerFor(frames, "click", "next-ok"), """{"name":"click"}""");
            Assert.True(next == 0, $"rc was {next}: the dispatch after a reclassified fault did not complete normally.");
            Assert.True(FrameTexts(frames).Contains("ok:1"),
                "the dispatch after a reclassified fault returned, but its frame never arrived: the session did not survive.");
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void AFaultNoticeAfterABegunCall_IsNotBegunOnTheDispatchingThread()
    {
        // Exports sends the FaultedAfterShellCall notice from the pool, so the shell is never
        // re-entered from its own dispatch lane: a shell that serialises hostCallBegin behind
        // the dispatch it is inside would otherwise wait on itself.
        var (rc, _, pending, previous) = DispatchProbe("host-throw");
        try
        {
            Assert.True(rc == 0, $"anchor: rc was {rc}, so the fault was not classified as after a begun call "
                + "and no notice of this kind was sent.");
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget), "no FaultNotice reached the shell.");
            int dispatchThread = s_lastDispatchThread;
            int noticeThread = FakeShellHost.HostCallBeginThread(Assert.Single(Notices()).RequestId);
            int renderThread = HostSession.EnsureSession().RenderThreadId;
            Assert.True(dispatchThread > 0 && noticeThread > 0,
                $"anchor: a thread was not recorded: dispatch {dispatchThread}, notice {noticeThread}.");
            Assert.True(noticeThread != dispatchThread,
                $"the FaultNotice was begun on thread {noticeThread}, the thread that called DispatchEventCore: "
                + "the shell was re-entered from its own dispatch lane.");
            // The notice must also be off the render thread: a notice sent synchronously from the
            // classification, which runs on the render thread, is not on the dispatch thread
            // either, so the check above alone would pass it.
            Assert.True(renderThread > 0 && noticeThread != renderThread,
                $"the FaultNotice was begun on thread {noticeThread}, the render thread {renderThread}: "
                + "it was sent synchronously from the classification instead of from the pool.");
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
    //
    // DOES NOT COVER:
    //   - a yield after a FETCH begin. The mount guard's page awaits a host call only, so a
    //     yield added after BeginFetch alone leaves it green;
    //   - a held host call other than the camera call. The hold guard drives BackHoldProbe,
    //     which blocks on a camera capture; another op with its own path would need its own
    //     probe. Both rest on what the two probes call, read from their code.

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
        // device back tests drive this probe; this fact is the bounded .NET pin. Under such a
        // regression it reds within its budget, and its finally abandons the deadlocked session
        // through HostSession.AbandonForTests instead of disposing it, so the next fact gets a
        // fresh session and the rest of the run completes. Only the deadlocked render thread
        // stays blocked, a background thread left until the test process exits. The dispatch
        // worker is released: AbandonForTests shuts the dispatcher down with a zero join
        // budget, the join fails, and CancelPending cancels the post the worker waits on, so
        // DispatchEventCore takes its catch and returns rc 2. The 16.7 record's M7 row
        // measured this with the CI test command.
        var frames = new List<RenderFrame>();
        int rc = -1;
        bool started = false;
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
            started = true;

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
            // Hold handler blocking the render thread on it. Until the dispatch returns, within
            // the budget, answer every camera call seen so far, as the shell would. The log is
            // read again on each pass, so a begin that lands late is answered too. Each call is
            // answered once; one the body already answered gives rc 1, which is benign here.
            // Skipped when the worker never started: then nothing is held.
            if (started)
            {
                var answered = new HashSet<long>();
                var sw = Stopwatch.StartNew();
                do
                {
                    foreach (var call in FakeShellHost.HostCalls().Where(c => c.Op == (int)HostCallOp.Camera))
                    {
                        if (answered.Add(call.RequestId))
                            NativeShellBridge.CompleteHostCall(call.RequestId, 1, null);
                    }
                }
                while (!returned.Wait(TimeSpan.FromMilliseconds(50)) && sw.Elapsed < Budget);
            }
            // A released handler, or one never dispatched, lets the session go normally. A
            // deadlocked one would hang ResetForTests, whose dispose waits on the blocked render
            // thread with no bound, and with it every later fact. So it is abandoned instead:
            // detached without a dispose, its render thread left blocked while the cancelled
            // post releases the dispatch worker with rc 2. The fact has already failed.
            if (!started || returned.IsSet)
                HostSession.ResetForTests();
            else
                HostSession.AbandonForTests();
            NativeShellBridge.ResetForTests();
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
    public void AFetchTheShellRefusesToBegin_FaultsTheHandlerWithRc2_AndSendsNoNotice()
    {
        var (rc, _, pending, previous) = DispatchProbe("refused-fetch", fetchBeginReturnCode: -1);
        try
        {
            // FetchBeginFn records the url before it checks its return code, so this proves the
            // begin was attempted, and refused.
            Assert.True(FakeShellHost.LastFetchUrl == "https://refused.test/",
                $"anchor: the fetch begin was never attempted, the last fetch url was '{FakeShellHost.LastFetchUrl}', "
                + "so this fact tests nothing.");
            Assert.True(rc == 2, $"rc was {rc}: a fetch begin the shell refused throws into the handler before any shell "
                + "call is begun, so that fault must stay the dispatch's rc 2.");
            lock (pending) Assert.True(pending.Count == 0, "nothing may still be running after a synchronous fault");
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
            // Rule 7 limit, named in 17.1 from #494: no probe-side change can red this rc assertion,
            // because turning the cancellation into a fault after the begun call is rc 0 too. The
            // 16.8 re-audit's X2 reds the Assert.Empty below instead.
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
    public void AFaultBeforeASiblingBeginsAShellCall_IsStillRc2_AndSendsNoNotice()
    {
        // The classification is ordered, not window-wide: a fault captured BEFORE any shell
        // call was begun stays rc 2, even when a later part of the same synchronous part
        // begins one. Here the handler's re-render mounts two siblings: the first throws in
        // OnInitialized, then the second begins a host call in OnInitialized.
        var (rc, _, pending, previous) = DispatchProbe("child-throw-then-sibling-begin");
        try
        {
            // Anchors: the first child really threw, the sibling really began its call, and
            // in that order. Without them rc 2 would only show that nothing was begun.
            Assert.True(ShellCallProbe.ChildThrowSeq > 0,
                "anchor: the first child never reached its throw, so this fact tests nothing.");
            Assert.True(FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Geolocation),
                $"anchor: the sibling's begin was not attempted; ops seen: [{string.Join(", ", FakeShellHost.HostCalls().Select(c => c.Op))}].");
            Assert.True(ShellCallProbe.SiblingBeginSeq > ShellCallProbe.ChildThrowSeq,
                $"anchor: the fault must come before the begin, but the child threw at {ShellCallProbe.ChildThrowSeq} "
                + $"and the sibling began at {ShellCallProbe.SiblingBeginSeq}.");
            Assert.True(rc == 2, $"rc was {rc}: the fault was captured before any shell call was begun, so it is the "
                + "dispatch's rc 2. A begin later in the same synchronous part must not reclassify an earlier fault.");
            lock (pending) Assert.True(pending.Count == 0, "nothing may still be running after a synchronous fault");
            // rc 2 and an empty pending list exclude every FaultNotice source, as in the control.
            Assert.Empty(Notices());
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void AChildsBegunCall_DuringTheReRender_MarksTheDispatch_SoALaterRenderFaultIsRc0()
    {
        // 17.1, from #494: the re-render is part of the handler's synchronous part and flows its
        // scope, so a call a CHILD begins there marks the dispatch, and a render fault after it
        // in the same synchronous part is a FaultNotice with rc 0. The mirror of the sibling-order
        // fact above, and a guard on 17.1's flowing check in NoteShellCallBegun: a child's render
        // must still mark.
        var (rc, _, pending, previous) = DispatchProbe("child-begin-then-sibling-throw");
        try
        {
            Assert.True(FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Geolocation),
                $"anchor: the child's begin was not attempted; ops seen: [{string.Join(", ", FakeShellHost.HostCalls().Select(c => c.Op))}].");
            Assert.True(ShellCallProbe.ChildThrowSeq > ShellCallProbe.SiblingBeginSeq && ShellCallProbe.SiblingBeginSeq > 0,
                $"anchor: the begin must come before the fault, but the child began at {ShellCallProbe.SiblingBeginSeq} "
                + $"and the sibling threw at {ShellCallProbe.ChildThrowSeq}.");
            Assert.True(rc == 0, $"rc was {rc}: the child's begun call marks the dispatch, so the later render fault "
                + "is a FaultNotice with rc 0, not the dispatch's rc 2.");
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget), "no FaultNotice for the render fault after the begun call.");
            Assert.Equal("child-after-begin", NativeShellBridge.ParseFlatJsonObject(Assert.Single(Notices()).Args)["message"]);
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
            // 17.1, from #494: read the notices again after the rc, with nothing pending. rc 2 and an
            // empty pending list exclude every notice source, as in the control, so this check is
            // immediate by construction; reading the list taken before the rc checked nothing.
            lock (pending) Assert.True(pending.Count == 0, "nothing may still be running after a synchronous fault");
            Assert.DoesNotContain("after-notice",
                Notices().Select(n => NativeShellBridge.ParseFlatJsonObject(n.Args)["message"]));
        }
        finally { TearDown(pending, previous); }
    }

    [Theory]
    [InlineData("back-state-then-throw")]
    [InlineData("back-unhandled-then-throw")]
    public void ABackNoticeDotNetSends_DoesNotMarkTheDispatch_SoAFaultAfterItIsRc2(string label)
    {
        // The other two notices .NET sends itself. Each reaches InvokeHostCallAsync through
        // SendNotice, which passes markDispatch false. Before that parameter the exclusion was
        // an op list with one arm per notice, and dropping either arm alone left every other
        // fact green; each notice keeps its own case so a notice that bypasses SendNotice reds.
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

    // ── 17.1 (#464): a fault belongs to the dispatch whose own code raised it ──
    //
    // Dispatch A awaits a TaskCompletionSource the app made WITHOUT RunContinuationsAsynchronously.
    // Dispatch B's handler completes it, so A's continuation runs inline inside B's synchronous
    // part, on the render thread, inside B's window. Its execution context still flows A's scope,
    // and that is what tells the two apart: a fault A's own code raises is A's, a late FaultNotice
    // under A's handler id, and B keeps its own rc. A call A's continuation begins never marks B.
    // Code that flows no dispatch scope is still the window's, and a fault from a dispatch that has
    // already finished goes to the no-window path, never to the window. Each fact's anchor,
    // InlineContinuationProbe.RanInsideB, proves the continuation really ran inside B's window.
    //
    // DOES NOT COVER:
    //   - a RE-RENDER that A's continuation queues inside B's window, by StateHasChanged. Blazor
    //     renders it in B's batch, under B's scope, so a render fault from it is B's. Render
    //     faults belong to the batch that renders them; per-render attribution is #500;
    //   - work A's continuation queues through RunAfterDispatch, a navigation for example. It
    //     runs when B's window closes, and a fault in it is B's. Also #500;
    //   - a flowing scope that another renderer owns: treated as no flow, so the window's. That
    //     rests on reading HandleException; a second renderer in one process has no test here;
    //   - a nested dispatch: B's own fire-and-forget continuation running inline inside a window C
    //     opened while B's synchronous part was still open. Not built here; it rests on reading.

    [Fact]
    public void AnInlineContinuationsFault_IsItsOwnDispatchs_NotTheWindows()
    {
        var run = DispatchTwo("a-await-then-throw", "b-complete");
        try
        {
            Assert.True(InlineContinuationProbe.RanInsideB,
                "anchor: A's continuation did not run inline inside B's synchronous part, so this proves nothing.");
            Assert.True(run.RcA == 0, $"A returned rc {run.RcA}; it suspended on the gate, so its synchronous part was clean.");
            Assert.True(run.RcB == 0, $"rc was {run.RcB}: B's own synchronous part did not fault. A's fault was charged to "
                + "B's window, which is #464.");
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget),
                "no FaultNotice: A's fault was lost instead of reaching A as a late fault.");
            var args = NativeShellBridge.ParseFlatJsonObject(Assert.Single(Notices()).Args);
            Assert.Equal(run.HandlerA.ToString(CultureInfo.InvariantCulture), args["handlerId"]);
            Assert.Equal("a-late", args["message"]);
        }
        finally { TearDown(run.Pending, run.Previous); }
    }

    [Fact]
    public void AnInlineContinuationsFault_LeavesTheWindowsOwnFault_Rc2()
    {
        var run = DispatchTwo("a-await-then-throw", "b-complete-then-throw");
        try
        {
            Assert.True(InlineContinuationProbe.RanInsideB, "anchor: A's continuation did not run inside B's window.");
            Assert.True(run.RcB == 2, $"rc was {run.RcB}: B faulted in its own synchronous part after completing the gate, "
                + "and that fault must be its rc 2, not lost behind A's.");
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget), "no FaultNotice for A's fault.");
            var notices = Notices().Select(n => NativeShellBridge.ParseFlatJsonObject(n.Args)).ToList();
            var single = Assert.Single(notices);
            Assert.Equal(run.HandlerA.ToString(CultureInfo.InvariantCulture), single["handlerId"]);
            Assert.Equal("a-late", single["message"]);
            Assert.DoesNotContain(notices, n => n["message"] == "b-own");
        }
        finally { TearDown(run.Pending, run.Previous); }
    }

    [Fact]
    public void ACallBegunByAnInlineContinuation_DoesNotMarkTheWindow()
    {
        var run = DispatchTwo("a-await-then-begin-and-throw", "b-complete-then-throw");
        try
        {
            Assert.True(InlineContinuationProbe.RanInsideB, "anchor: A's continuation did not run inside B's window.");
            Assert.True(FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Geolocation),
                "anchor: A's continuation never began its geolocation call.");
            Assert.True(run.RcB == 2, $"rc was {run.RcB}: the call A's continuation began marked B, so B's own "
                + "synchronous fault became rc 0. Only B's own synchronous part may mark B.");
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget), "no FaultNotice for A's fault.");
            var args = NativeShellBridge.ParseFlatJsonObject(Assert.Single(Notices()).Args);
            Assert.Equal(run.HandlerA.ToString(CultureInfo.InvariantCulture), args["handlerId"]);
        }
        finally { TearDown(run.Pending, run.Previous); }
    }

    [Fact]
    public void AFaultWithNoDispatchFlow_InsideAWindow_IsTheWindows()
    {
        var run = DispatchTwo("a-register-no-flow", "b-complete");
        try
        {
            Assert.True(InlineContinuationProbe.RanInsideB, "anchor: the no-flow continuation did not run inside B's window.");
            Assert.True(InlineContinuationProbe.SawNoFlow,
                "anchor: the continuation was meant to flow no dispatch scope, but the renderer saw one.");
            Assert.True(run.RcB == 2, $"rc was {run.RcB}: a fault from code that flows no dispatch is the window's, "
                + "so nothing that is B's today becomes unreported.");
            lock (run.Pending) Assert.True(run.Pending.Count == 0, "nothing may still be running");
            Assert.Empty(Notices());
        }
        finally { TearDown(run.Pending, run.Previous); }
    }

    [Fact]
    public void AFinishedDispatchsFault_InsideAWindow_IsNotTheWindows()
    {
        var log = new List<string>();
        Action<BnLogLevel, string, string>? sink = BnLog.Sink;
        BnLog.Sink = (_, category, message) => { lock (log) log.Add($"{category}: {message}"); };
        try
        {
            var run = DispatchTwo("a-register-flowing", "b-complete");
            try
            {
                Assert.True(InlineContinuationProbe.RanInsideB, "anchor: A's continuation did not run inside B's window.");
                Assert.True(run.RcA == 0, $"A returned rc {run.RcA}; it finished cleanly, so its scope is Done.");
                Assert.True(run.RcB == 0, $"rc was {run.RcB}: A's code raised the fault after A finished. It is A's, and "
                    + "A can no longer report it, so it is logged and never charged to B's window.");
                lock (run.Pending) Assert.True(run.Pending.Count == 0, "nothing may still be running");
                Assert.Empty(Notices());
                lock (log)
                {
                    string line = Assert.Single(log, l => l.Contains("a-orphan", StringComparison.Ordinal));
                    Assert.True(line.Contains(NativeRenderer.FinishedDispatchFaultLogLabel, StringComparison.Ordinal),
                        $"the fault was logged, but not as a finished dispatch's fault inside another window: {line}");
                }
            }
            finally { TearDown(run.Pending, run.Previous); }
        }
        finally { BnLog.Sink = sink; }
    }

    [Fact]
    public void AFinishedDispatchsFault_InsideAWindow_IsLoggedInStrictModeToo()
    {
        // From the 17.1 final review: in strict mode the no-window path rethrows, and inside B's
        // window that throw lands in a Task nobody observes, so the fault vanished with no log.
        // Strict mode exists to make faults loud; a finished dispatch's fault must still be logged.
        var log = new List<string>();
        Action<BnLogLevel, string, string>? sink = BnLog.Sink;
        BnLog.Sink = (_, category, message) => { lock (log) log.Add($"{category}: {message}"); };
        try
        {
            var run = DispatchTwo("a-register-flowing", "b-complete", strict: true);
            try
            {
                Assert.True(InlineContinuationProbe.RanInsideB, "anchor: A's continuation did not run inside B's window.");
                Assert.True(run.RcB == 0, $"rc was {run.RcB}: a finished dispatch's fault is never charged to B's window.");
                lock (log) Assert.True(log.Any(l => l.Contains("a-orphan", StringComparison.Ordinal)
                        && l.Contains(NativeRenderer.FinishedDispatchFaultLogLabel, StringComparison.Ordinal)),
                    "the finished dispatch's fault vanished in strict mode: nothing logged it, nothing reported it.");
            }
            finally { TearDown(run.Pending, run.Previous); }
        }
        finally { BnLog.Sink = sink; }
    }

    // ── 18.2 (#500): charge a fault in queued work to the dispatch that queued it ──
    //
    // Blazor's ComponentBase keeps a private flag, _hasPendingQueuedRender. StateHasChanged sets it
    // when it enqueues a render, and the component's render fragment clears it as that render
    // starts. 18.2 reads it through BlazorInterop to see which components a dispatch dirtied.

    [Fact]
    public void HasPendingQueuedRender_ReadsBlazorsFlag()
    {
        var frames = new List<RenderFrame>();
        // TearDown requires both arguments; this fact dispatches nothing, so they stay empty.
        var pending = new List<Task>();
        Action<ulong, string, Task>? previous = Exports.PendingDispatchObserver;
        try
        {
            NativeRenderer renderer = StartSession(strict: false, frames);
            PendingFlagProbe.Reset();
            renderer.Mount<PendingFlagHost>();
            Assert.NotNull(PendingFlagProbe.Target);
            Assert.NotNull(PendingFlagProbe.Trigger);

            // B's render runs inside an open batch, so A's StateHasChanged only enqueues.
            renderer.Dispatcher.InvokeAsync(() => BlazorInterop.StateHasChangedViaAccessor(PendingFlagProbe.Trigger!))
                .GetAwaiter().GetResult();

            Assert.True(PendingFlagProbe.FlagInsideBatch,
                "the accessor did not see StateHasChanged set Blazor's pending-render flag");
            bool after = renderer.Dispatcher.InvokeAsync(
                () => BlazorInterop.HasPendingQueuedRender(PendingFlagProbe.Target!)).GetAwaiter().GetResult();
            Assert.False(after, "the flag stayed set after the render");
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void PendingQueuedRenderFieldProblem_NamesTheFieldWhenItIsMissingOrNotBool()
    {
        Assert.Null(BlazorInterop.PendingQueuedRenderFieldProblem(typeof(ComponentBase)));
        string? missing = BlazorInterop.PendingQueuedRenderFieldProblem(typeof(NoFlagType));
        Assert.True(missing is not null && missing.Contains("_hasPendingQueuedRender"),
            $"a type without the field was not reported by name: {missing ?? "null"}");
        string? wrongType = BlazorInterop.PendingQueuedRenderFieldProblem(typeof(IntFlagType));
        Assert.True(wrongType is not null && wrongType.Contains("_hasPendingQueuedRender"),
            $"a non-bool field of that name was not reported by name: {wrongType ?? "null"}");
    }

    [Fact]
    public void AnActionQueuedByAnInlineContinuation_IsItsOwnDispatchs_NotTheWindows()
    {
        // A's continuation runs inline inside B's window and queues an action through
        // RunAfterDispatch. The action runs when B's window closes, after A's handler Task has
        // already completed inside B's synchronous part. It is A's: A's completion waits for it,
        // and its fault reaches A as a late FaultNotice, never B's rc.
        var run = DispatchTwo("a-await-then-queue-throwing", "b-complete");
        try
        {
            Assert.True(InlineContinuationProbe.RanInsideB,
                "anchor: A's continuation did not run inline inside B's synchronous part, so this proves nothing.");
            Assert.True(run.RcB == 0, $"rc was {run.RcB}: the action A's continuation queued was charged to B's window, "
                + "which is #500.");
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget)
                    && Notices().Select(n => NativeShellBridge.ParseFlatJsonObject(n.Args)).ToList() is [var single]
                    && single["handlerId"] == run.HandlerA.ToString(CultureInfo.InvariantCulture)
                    && single["message"] == "a-queued",
                "no FaultNotice: the queued action's fault never reached A.");
        }
        finally { TearDown(run.Pending, run.Previous); }
    }

    private sealed class NoFlagType { }

    private sealed class IntFlagType
    {
#pragma warning disable CS0169
        private int _hasPendingQueuedRender;
#pragma warning restore CS0169
    }

    private sealed class PendingFlagHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<PendingFlagProbe>(0);
            b.AddComponentParameter(1, nameof(PendingFlagProbe.IsTrigger), false);
            b.CloseComponent();
            b.OpenComponent<PendingFlagProbe>(2);
            b.AddComponentParameter(3, nameof(PendingFlagProbe.IsTrigger), true);
            b.CloseComponent();
        }
    }

    /// <summary>Target renders text; Trigger, re-rendered alone, dirties Target from inside its own
    /// render, which is inside an open batch, and reads the flag there.</summary>
    private sealed class PendingFlagProbe : ComponentBase
    {
        public static PendingFlagProbe? Target;
        public static PendingFlagProbe? Trigger;
        public static volatile bool FlagInsideBatch;
        private static bool s_armed;

        [Parameter] public bool IsTrigger { get; set; }

        public static void Reset() { Target = null; Trigger = null; FlagInsideBatch = false; s_armed = false; }

        protected override void OnInitialized()
        {
            if (IsTrigger) Trigger = this; else Target = this;
        }

        protected override void OnAfterRender(bool firstRender)
        {
            if (IsTrigger && firstRender) s_armed = true;
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            if (IsTrigger && s_armed)
            {
                s_armed = false;
                BlazorInterop.StateHasChangedViaAccessor(Target!);
                FlagInsideBatch = BlazorInterop.HasPendingQueuedRender(Target!);
            }
            b.AddContent(0, IsTrigger ? "trigger" : "target");
        }
    }

    // ── 18.1 (#564): a dispatch that overruns its budget ──
    //
    // On the Windows runner a FaultNotice dispatch took about 1.05 s instead of 2 ms, and its
    // late notice landed in the next fact. Two causes, one pin each. The render thread logged
    // its render fault through BnLog into a full stderr pipe and blocked there, inside the
    // dispatch. And DispatchProbe threw before it returned the state its caller's teardown
    // needs, so nothing tore the stuck fact down.
    //
    // DOES NOT COVER: a real pipe. GatedWriter stands in for one; squeeze.sh, in the 18.1
    // investigation's appendix A, drives a real 4 KiB pipe, and its runs are in the 18.1
    // record. The queue's drop counting is pinned in BnLogTests.

    [Fact]
    public void AFaultLoggedWhileStderrIsBlocked_DoesNotHoldTheDispatchPastItsBudget()
    {
        // The #564 dispatch: a child begins a host call, then its sibling faults in the same
        // synchronous part. NativeRenderer.HandleException logs that fault on the render thread
        // while the dispatch waits for it, and Exports logs it again on the dispatch lane.
        Action<BnLogLevel, string, string>? sink = BnLog.Sink;
        TextWriter original = Console.Error;
        var stderr = new GatedWriter();
        (int Rc, List<RenderFrame> Frames, List<Task> Pending, Action<ulong, string, Task>? Previous)? probe = null;
        Exception? overran;
        try
        {
            BnLog.Sink = null;
            Console.SetError(stderr);
            overran = Record.Exception(() => probe = DispatchProbe("child-begin-then-sibling-throw", release: stderr.Open));
        }
        finally
        {
            stderr.Open();
            Console.SetError(original);
            BnLog.Sink = sink;
        }
        try
        {
            Assert.True(overran is null,
                $"the dispatch did not return within its budget while stderr was blocked: {overran?.Message} "
                + "The render fault is logged on the render thread inside the dispatch, so a BnLog sink that "
                + "writes synchronously holds the dispatch for as long as stderr stays full (#564).");
            // anchor: the dispatch really logged its render fault into the blocked stderr. Without
            // it the dispatch returning in time would prove nothing about the sink.
            Assert.True(WaitUntil(() => stderr.Text.Contains("child-after-begin", StringComparison.Ordinal), Budget),
                $"anchor: the render fault never reached stderr once it drained; lines seen: [{string.Join(" | ", stderr.Lines)}].");
        }
        finally
        {
            if (probe is { } p)
                TearDown(p.Pending, p.Previous);
        }
    }

    [Fact]
    public void ADispatchPastItsBudget_LeavesTheNextFactOnlyItsOwnNotice_AndRestoresItsObserver()
    {
        // One dispatch is forced past ExportBudget: its handler begins a host call, then waits on
        // a gate in its synchronous part, then throws. That fault is a FaultNotice, sent from the
        // pool once the dispatch finally returns, so a fact that did not wait for it hands it to
        // whichever fact runs next.
        Action<ulong, string, Task>? outer = Exports.PendingDispatchObserver;
        ShellCallProbe.Gate.Reset();
        Thread? lateReader = null;
        try
        {
            Exception? overran = Record.Exception(() => DispatchProbe("gated-begin-then-throw", release: ShellCallProbe.Gate.Set));
            // anchor: the gated dispatch really overran its budget. Without it there is nothing to leak.
            Assert.True(overran is not null && overran.Message.Contains("did not return within", StringComparison.Ordinal),
                $"anchor: the gated dispatch did not overrun its budget; got: {overran?.Message ?? "no exception"}.");

            // The late reader. Like a stderr reader that drains only once the next fact has begun,
            // it opens the gate after the next fact's StartSession has cleared the host-call log.
            // When the overrun fact tore itself down, the gate is open already and this stands down
            // at once: the next fact's empty log can be too brief to see.
            lateReader = new Thread(() =>
            {
                WaitUntil(() => ShellCallProbe.Gate.IsSet || FakeShellHost.HostCalls().Count == 0, Budget);
                ShellCallProbe.Gate.Set();
            })
            { IsBackground = true, Name = "fault-notice-late-reader" };
            lateReader.Start();

            // The next fact: a handler that begins a call and throws sends one notice of its own.
            var (_, _, pending, previous) = DispatchProbe("begin-then-throw");
            try
            {
                Assert.True(WaitUntil(() => Notices().Any(n => MessageOf(n.Args) == "after-begin"), Budget),
                    "anchor: the next fact's own FaultNotice never arrived, so it cannot show what else did.");
                // Every notice send recorded so far has landed before the log is read.
                WaitForNoticeSends();
                List<string> messages = Notices().Select(n => MessageOf(n.Args)).ToList();
                Assert.True(messages.All(m => m == "after-begin"),
                    $"the next fact saw a FaultNotice that is not its own: [{string.Join(", ", messages)}]. "
                    + "'after-gate' is the overrun dispatch's fault: its fact did not tear down when "
                    + "DispatchBounded threw, so its late notice landed here (#564).");
            }
            finally { TearDown(pending, previous); }

            Assert.True(ReferenceEquals(outer, Exports.PendingDispatchObserver),
                "PendingDispatchObserver still points at the overrun fact's tap: DispatchProbe threw before "
                + "it returned its teardown state, so nothing restored the observer (#564).");
        }
        finally
        {
            ShellCallProbe.Gate.Set();
            lateReader?.Join(Budget);
            Exports.PendingDispatchObserver = outer;
        }
    }

    [Fact]
    public void ADispatchPastItsBudget_WhoseNoticeIsSlowToBegin_StillLeavesTheNextFactOnlyItsOwnNotice()
    {
        // The leak's narrow half. The overrun dispatch's FaultNotice is sent from the pool once
        // the dispatch returns, and here the fake shell holds that notice's begin at a gate, as a
        // pool item scheduled late or a shell slow to take the call would. TearDown must wait
        // for the send itself: the gate opens only when a wait on at least one live send
        // begins, or else when the next fact's StartSession resets the fake, which logs the
        // held notice into the next fact.
        Action<ulong, string, Task>? outer = Exports.PendingDispatchObserver;
        using var noticeGate = new ManualResetEventSlim(false);
        ShellCallProbe.Gate.Reset();
        s_waitingForNoticeSends = live => { if (live > 0) noticeGate.Set(); };
        try
        {
            Exception? overran = Record.Exception(() => DispatchProbe("gated-begin-then-throw", release: () =>
            {
                FakeShellHost.FaultNoticeBeginGate = noticeGate;
                ShellCallProbe.Gate.Set();
            }));
            // anchor: the gated dispatch really overran its budget. Without it there is nothing to leak.
            Assert.True(overran is not null && overran.Message.Contains("did not return within", StringComparison.Ordinal),
                $"anchor: the gated dispatch did not overrun its budget; got: {overran?.Message ?? "no exception"}.");

            var (_, _, pending, previous) = DispatchProbe("begin-then-throw");
            try
            {
                Assert.True(WaitUntil(() => Notices().Any(n => MessageOf(n.Args) == "after-begin"), Budget),
                    "anchor: the next fact's own FaultNotice never arrived, so it cannot show what else did.");
                WaitForNoticeSends();
                List<string> messages = Notices().Select(n => MessageOf(n.Args)).ToList();
                Assert.True(messages.All(m => m == "after-begin"),
                    $"the next fact saw a FaultNotice that is not its own: [{string.Join(", ", messages)}]. "
                    + "'after-gate' is the overrun dispatch's fault, whose send was still in flight when its "
                    + "fact's TearDown finished: TearDown did not wait for the send (#564).");
            }
            finally { TearDown(pending, previous); }
        }
        finally
        {
            s_waitingForNoticeSends = null;
            noticeGate.Set();
            ShellCallProbe.Gate.Set();
            Exports.PendingDispatchObserver = outer;
        }
    }

    private static string MessageOf(string? noticeArgs)
        => NativeShellBridge.ParseFlatJsonObject(noticeArgs).GetValueOrDefault("message", "");

    /// <summary>17.1 (#464): two dispatches, A then B, against one InlineContinuationProbe.</summary>
    private static (int RcA, int RcB, int HandlerA, int HandlerB, List<Task> Pending, Action<ulong, string, Task>? Previous)
        DispatchTwo(string labelA, string labelB, bool strict = false)
    {
        var frames = new List<RenderFrame>();
        var pending = new List<Task>();
        Action<ulong, string, Task>? previous = Exports.PendingDispatchObserver;
        Exports.PendingDispatchObserver = (_, _, t) => { lock (pending) pending.Add(t); };
        try
        {
            NativeRenderer renderer = StartSession(strict, frames);
            InlineContinuationProbe.Reset();
            InlineContinuationProbe.FlowingScopeIsSet = () => renderer.FlowingScopeIsSetForTests;
            InlineContinuationProbe.Renderer = renderer;
            renderer.Mount<InlineContinuationProbe>();
            int a = HandlerFor(frames, "click", labelA);
            int b = HandlerFor(frames, "click", labelB);
            FakeShellHost.AutoCompleteHostCall = true;
            int rcA = DispatchBounded(a, """{"name":"click"}""");
            int rcB = DispatchBounded(b, """{"name":"click"}""");
            return (rcA, rcB, a, b, pending, previous);
        }
        catch
        {
            // The caller's finally never sees this state: tear down here (18.1, #564).
            TearDown(pending, previous);
            throw;
        }
    }

    /// <summary>17.1 (#464): A's handlers wait on an app-made gate; B's handlers complete it inline.</summary>
    private sealed class InlineContinuationProbe : ComponentBase
    {
        /// <summary>Made WITHOUT RunContinuationsAsynchronously, as the app code in #464 does.</summary>
        public static TaskCompletionSource Gate = new();
        private static volatile bool s_inB;
        /// <summary>The anchor: a continuation of A ran while B's handler was completing the gate.</summary>
        public static volatile bool RanInsideB;
        /// <summary>The no-flow anchor: that continuation saw no execution context.</summary>
        public static volatile bool SawNoFlow;

        /// <summary>Reads the renderer's flowing scope for the no-flow anchor. DispatchTwo sets it.</summary>
        public static Func<bool> FlowingScopeIsSet = () => true;
        /// <summary>18.2 (#500): the renderer the probe is mounted in, for the RunAfterDispatch
        /// buttons. DispatchTwo sets it, and Reset clears it.</summary>
        public static NativeRenderer? Renderer;
        private static ExecutionContext? s_mountContext;

        protected override void OnInitialized() => s_mountContext = ExecutionContext.Capture();

        public static void Reset()
        {
            Gate = new TaskCompletionSource();
            s_inB = false;
            RanInsideB = false;
            SawNoFlow = false;
            Renderer = null;
        }

        [Inject] public IMobileBridge Bridge { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            Button(b, 0, "a-await-then-throw", async () =>
            {
                await Gate.Task;
                RanInsideB = s_inB;
                throw new InvalidOperationException("a-late");
            });
            Button(b, 10, "a-await-then-begin-and-throw", async () =>
            {
                await Gate.Task;
                RanInsideB = s_inB;
                _ = Bridge.CheckGeolocationPermissionAsync();
                throw new InvalidOperationException("a-late");
            });
            Button(b, 20, "a-register-no-flow", () =>
            {
                // Code that flows no dispatch scope: it runs in the execution context captured at
                // mount, when no dispatch was open, not in whatever context completes the gate.
                ExecutionContext mountContext = s_mountContext!;
                _ = Gate.Task.ContinueWith(_ => ExecutionContext.Run(mountContext.CreateCopy(), _ =>
                {
                    RanInsideB = s_inB;
                    SawNoFlow = !FlowingScopeIsSet();
                    _ = DispatchExceptionAsync(new InvalidOperationException("no-flow"));
                }, null), TaskContinuationOptions.ExecuteSynchronously);
            });
            Button(b, 30, "a-register-flowing", () =>
            {
                _ = Gate.Task.ContinueWith(_ =>
                {
                    RanInsideB = s_inB;
                    _ = DispatchExceptionAsync(new InvalidOperationException("a-orphan"));
                }, TaskContinuationOptions.ExecuteSynchronously);
            });
            Button(b, 60, "a-await-then-queue-throwing", async () =>
            {
                await Gate.Task;
                RanInsideB = s_inB;
                Renderer!.RunAfterDispatch(() => throw new InvalidOperationException("a-queued"));
            });
            Button(b, 40, "b-complete", () => CompleteGate());
            Button(b, 50, "b-complete-then-throw", () =>
            {
                CompleteGate();
                throw new InvalidOperationException("b-own");
            });
        }

        private static void CompleteGate()
        {
            s_inB = true;
            try { Gate.SetResult(); }
            finally { s_inB = false; }
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

    /// <summary>16.7 (#455): handlers that fault after beginning a shell call, and controls.</summary>
    private sealed class ShellCallProbe : ComponentBase
    {
        /// <summary>Set immediately before a cancellation probe throws: proves the handler body ran.
        /// DispatchProbe resets it.</summary>
        public static volatile bool ReachedThrow;

        /// <summary>The threads pool-begin-then-throw ran its handler and its begin on.
        /// DispatchProbe resets both.</summary>
        public static volatile int HandlerThread, PoolBeginThread;

        /// <summary>18.1 (#564): gated-begin-then-throw waits on this in its synchronous part.
        /// Open unless a fact closes it; that fact opens it again in its finally.</summary>
        public static readonly ManualResetEventSlim Gate = new(true);

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
            Button(b, 110, "refused-fetch", async () =>
            {
                await Bridge.FetchAsync(new BridgeHttpRequest("https://refused.test/"));
            });
            Button(b, 40, "fetch-throw", async () =>
            {
                await Bridge.FetchAsync(new BridgeHttpRequest("https://inline.test/"));
                throw new InvalidOperationException("after-fetch");
            });
            Button(b, 120, "child-throw-then-sibling-begin", () => { _showChildren = true; });
            Button(b, 130, "next-ok", () => { _okCount++; });
            Button(b, 160, "child-begin-then-sibling-throw", () => { _showBeginFirst = true; });
            Button(b, 180, "gated-begin-then-throw", () =>
            {
                // 18.1 (#564): holds the render thread inside the synchronous part, as a full
                // stderr pipe did, after a begun call, so the fault it then throws is a notice.
                _ = Bridge.CheckGeolocationPermissionAsync();
                Gate.Wait(Budget);
                throw new InvalidOperationException("after-gate");
            });
            if (_okCount > 0)
            {
                b.OpenElement(140, "text");
                b.AddContent(141, "ok:" + _okCount.ToString(CultureInfo.InvariantCulture));
                b.CloseElement();
            }
            if (_showChildren)
            {
                // Siblings, rendered in this order by the handler's own re-render: the first
                // faults, then the second begins a host call. Both run in the synchronous part.
                b.OpenComponent<ThrowOnInitChild>(150);
                b.CloseComponent();
                b.OpenComponent<BeginOnInitChild>(151);
                b.CloseComponent();
            }
            if (_showBeginFirst)
            {
                // The mirror: the first child begins a host call, then its sibling faults.
                b.OpenComponent<BeginOnInitChild>(170);
                b.CloseComponent();
                b.OpenComponent<ThrowAfterSiblingBeganChild>(171);
                b.CloseComponent();
            }
        }

        private bool _showChildren;
        private bool _showBeginFirst;
        private int _okCount;

        /// <summary>Order stamps for child-throw-then-sibling-begin: each child takes the next
        /// value of <see cref="Sequence"/>. DispatchProbe resets all three.</summary>
        public static int Sequence, ChildThrowSeq, SiblingBeginSeq;

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

    /// <summary>Faults in its synchronous OnInitialized, before its sibling begins a call.</summary>
    private sealed class ThrowOnInitChild : ComponentBase
    {
        protected override void OnInitialized()
        {
            ShellCallProbe.ChildThrowSeq = Interlocked.Increment(ref ShellCallProbe.Sequence);
            throw new InvalidOperationException("child-before-begin");
        }
    }

    /// <summary>Faults in its synchronous OnInitialized, after its sibling began a call (17.1).</summary>
    private sealed class ThrowAfterSiblingBeganChild : ComponentBase
    {
        protected override void OnInitialized()
        {
            ShellCallProbe.ChildThrowSeq = Interlocked.Increment(ref ShellCallProbe.Sequence);
            throw new InvalidOperationException("child-after-begin");
        }
    }

    /// <summary>Begins a host call in its synchronous OnInitialized, after its sibling faulted.</summary>
    private sealed class BeginOnInitChild : ComponentBase
    {
        [Inject] public IMobileBridge Bridge { get; set; } = default!;

        protected override void OnInitialized()
        {
            ShellCallProbe.SiblingBeginSeq = Interlocked.Increment(ref ShellCallProbe.Sequence);
            _ = Bridge.CheckGeolocationPermissionAsync();
        }
    }

    private static (int Rc, List<RenderFrame> Frames, List<Task> Pending, Action<ulong, string, Task>? Previous)
        DispatchProbe(string label, bool autoCompleteFetch = false, int hostCallBeginReturnCode = 0,
            int fetchBeginReturnCode = 0, Action? release = null)
    {
        var frames = new List<RenderFrame>();
        var pending = new List<Task>();
        Action<ulong, string, Task>? previous = Exports.PendingDispatchObserver;
        Exports.PendingDispatchObserver = (_, _, t) => { lock (pending) pending.Add(t); };
        try
        {
            NativeRenderer renderer = StartSession(strict: false, frames);
            renderer.Mount<ShellCallProbe>();
            int handlerId = HandlerFor(frames, "click", label);
            ShellCallProbe.ReachedThrow = false;
            ShellCallProbe.HandlerThread = ShellCallProbe.PoolBeginThread = 0;
            ShellCallProbe.Sequence = ShellCallProbe.ChildThrowSeq = ShellCallProbe.SiblingBeginSeq = 0;
            FakeShellHost.AutoCompleteHostCall = true;
            FakeShellHost.AutoCompleteFetch = autoCompleteFetch;
            FakeShellHost.HostCallBeginReturnCode = hostCallBeginReturnCode;
            FakeShellHost.FetchBeginReturnCode = fetchBeginReturnCode;
            int rc = DispatchBounded(handlerId, """{"name":"click"}""");
            return (rc, frames, pending, previous);
        }
        catch
        {
            // 18.1 (#564): the caller's try/finally starts only once this returns, so a throw
            // here, a dispatch past its budget above all, must tear its own fact down.
            // Otherwise the observer, the session and the dispatch's late notice carry over
            // into the next fact. `release` frees what the fact holds, so the dispatch can end.
            TearDown(pending, previous, release);
            throw;
        }
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
