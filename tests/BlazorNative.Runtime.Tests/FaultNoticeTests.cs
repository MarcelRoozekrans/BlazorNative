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

    /// <summary><paramref name="deliveringThreads"/>, when given, receives the managed thread
    /// each frame was delivered on, at the same index as the frame in <paramref name="frames"/>,
    /// under the same lock.</summary>
    private static NativeRenderer StartSession(bool strict, List<RenderFrame> frames, List<int>? deliveringThreads = null)
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        renderer.StrictErrors = strict;
        renderer.Frames += (f, _) =>
        {
            lock (frames)
            {
                frames.Add(f);
                deliveringThreads?.Add(Environment.CurrentManagedThreadId);
            }
            return ValueTask.CompletedTask;
        };
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

    // ── 16.7 (#455): a call the shell completes INSIDE begin still yields ─────
    //
    // Since 16.7 NativeShellBridge yields after every begin, unconditionally, so the
    // handler's await always suspends and a fault after it is a FaultNotice, never the
    // dispatch's rc. Each fact anchors that the fake really completed the call inside begin
    // (Rule 2). The Task.CompletedTask control pins the contract's own exception: an await
    // on a task the app completed itself does not yield (Rule 3). The mount fact pins the
    // accepted cost: a page whose OnInitializedAsync awaits such a call no longer mounts.
    //
    // DOES NOT COVER:
    //   - the real shells' inline arms. FakeShellHost completes the call on begin's own
    //     thread, the shape Android's inline arms have (#440); FaultNoticeTest.kt runs the
    //     host-call scenario through the NativeAOT dll, and no fact here drives a shell;
    //   - every host-call op and fetch shape. Only the geolocation check and one GET fetch
    //     are driven. The other ops go through the same InvokeHostCallAsync and its yield,
    //     by reading the code, not by a test;
    //   - a shell that completes the call from another thread while begin is still running.
    //     No fake here reproduces that timing. The yield has no IsCompleted check, so by
    //     reading the code there is no window for it to skip, but no fact measures it.

    private static (int Rc, int HandlerId, NativeRenderer Renderer, List<RenderFrame> Frames, List<int> Threads,
        List<Task> Pending, Action<ulong, string, Task>? Previous)
        DispatchInline(string label, bool autoCompleteFetch = false)
    {
        var frames = new List<RenderFrame>();
        var threads = new List<int>();
        var pending = new List<Task>();
        Action<ulong, string, Task>? previous = Exports.PendingDispatchObserver;
        Exports.PendingDispatchObserver = (_, _, t) => { lock (pending) pending.Add(t); };
        NativeRenderer renderer = StartSession(strict: false, frames, threads);
        renderer.Mount<InlineCompletionProbe>();
        int handlerId = HandlerFor(frames, "click", label);
        FakeShellHost.AutoCompleteHostCall = true;
        FakeShellHost.AutoCompleteFetch = autoCompleteFetch;
        int rc = DispatchBounded(handlerId, """{"name":"click"}""");
        return (rc, handlerId, renderer, frames, threads, pending, previous);
    }

    /// <summary>The discriminator for the yield itself: the dispatch observer records a
    /// dispatch only when its handler was still running at the export's return. Without the
    /// yield the inline-completed call lets the handler finish inside the export, and the
    /// count is 0.</summary>
    private static void AssertStillRunningAtTheReturn(List<Task> pending)
    {
        lock (pending)
            Assert.True(pending.Count == 1,
                $"{pending.Count} dispatch(es) were still running at the export's return, not 1: the await on "
                + "the host call or fetch the shell completed inside begin did not yield, so the handler finished "
                + "inside the export (#455).");
    }

    [Fact]
    public void AFaultAfterAnInlineCompletedHostCall_IsAFaultNotice_NotRc2()
    {
        var (rc, handlerId, _, _, _, pending, previous) = DispatchInline("host-throw");
        try
        {
            Assert.True(FakeShellHost.HostCallsCompletedInsideBegin == 1,
                $"anchor: the fake must answer the geolocation call INSIDE hostCallBegin, but it answered "
                + $"{FakeShellHost.HostCallsCompletedInsideBegin} that way; without it this fact tests nothing (#455).");
            Assert.True(rc == 0,
                $"rc was {rc}: a fault after an await on a host call the shell completed inside begin came back "
                + "as the dispatch's rc. The await did not yield, which is #455.");
            AssertStillRunningAtTheReturn(pending);
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget),
                "no FaultNotice reached the shell for the fault after the inline-completed host call.");
            var args = NativeShellBridge.ParseFlatJsonObject(Assert.Single(Notices()).Args);
            Assert.Equal(handlerId.ToString(CultureInfo.InvariantCulture), args["handlerId"]);
            Assert.Equal(typeof(InvalidOperationException).FullName, args["type"]);
            Assert.Equal("inline-host", args["message"]);
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void AnInlineCompletedHostCall_WithNoFault_IsRc0_AndTheHandlerIsStillRunningAtTheReturn()
    {
        var (rc, _, renderer, frames, threads, pending, previous) = DispatchInline("host-ok");
        try
        {
            Assert.True(FakeShellHost.HostCallsCompletedInsideBegin == 1,
                $"anchor: the fake answered {FakeShellHost.HostCallsCompletedInsideBegin} calls inside begin, not 1.");
            Assert.Equal(0, rc);
            AssertStillRunningAtTheReturn(pending);
            static bool IsDone(RenderFrame f) => f.Patches.OfType<ReplaceTextPatch>().Any(p => p.Text == "host-ok:done");
            Assert.True(WaitUntil(() => { lock (frames) return frames.Any(IsDone); }, Budget),
                "the handler's continuation never rendered 'host-ok:done' after the inline-completed call.");
            // The contract's last sentence: a continuation's frame is delivered from the render
            // thread. Asserted here because this fact runs non-strict, where the renderer's own
            // render-owner check only warns.
            int deliveredOn;
            lock (frames) deliveredOn = threads[frames.FindIndex(IsDone)];
            Assert.True(deliveredOn == renderer.RenderThreadId,
                $"the continuation's 'host-ok:done' frame was delivered on thread {deliveredOn}, not on the "
                + $"renderer's render thread {renderer.RenderThreadId}.");
            Assert.Empty(Notices());
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void AFaultAfterAnInlineCompletedFetch_IsAFaultNotice_NotRc2()
    {
        var (rc, handlerId, _, _, _, pending, previous) = DispatchInline("fetch-throw", autoCompleteFetch: true);
        try
        {
            Assert.True(FakeShellHost.FetchesCompletedInsideBegin == 1,
                $"anchor: the fake must answer the fetch INSIDE fetchBegin, but it answered "
                + $"{FakeShellHost.FetchesCompletedInsideBegin} that way; without it this fact tests nothing.");
            Assert.True(rc == 0,
                $"rc was {rc}: a fault after an await on a fetch the shell completed inside begin came back as "
                + "the dispatch's rc. The fetch await did not yield.");
            AssertStillRunningAtTheReturn(pending);
            Assert.True(WaitUntil(() => Notices().Count > 0, Budget),
                "no FaultNotice reached the shell for the fault after the inline-completed fetch.");
            var args = NativeShellBridge.ParseFlatJsonObject(Assert.Single(Notices()).Args);
            Assert.Equal(handlerId.ToString(CultureInfo.InvariantCulture), args["handlerId"]);
            Assert.Equal("inline-fetch", args["message"]);
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void AFaultAfterAwaitingACompletedTask_IsStillRc2_Control()
    {
        // The contract's exception, pinned: the app's own completed task does not yield.
        var (rc, _, _, _, _, pending, previous) = DispatchInline("completed-throw");
        try
        {
            Assert.True(rc == 2,
                $"rc was {rc}: an await on Task.CompletedTask yielded, or its fault was lost; the contract "
                + "says a fault after it is still in the synchronous part, rc 2.");
            // Bounded: nothing was still running at the return, so no late fault can follow
            // and an empty notice list now is final, not a check made too early.
            lock (pending)
                Assert.True(pending.Count == 0,
                    $"{pending.Count} dispatch(es) were still running at the return; the handler awaited only "
                    + "Task.CompletedTask, so it must have finished inside the export.");
            Assert.Empty(Notices());
        }
        finally { TearDown(pending, previous); }
    }

    [Fact]
    public void APageAwaitingAnInlineCompletedHostCallInOnInitializedAsync_NoLongerMounts()
    {
        // The accepted cost of the unconditional yield, owner decision 2026-10-02. Before
        // 16.7 an OnInitializedAsync whose only await was a call the shell answered inside
        // begin completed synchronously, so Mount<T> accepted the page on Android. iOS never
        // answers inside begin and already refused it. Now both refuse it.
        var frames = new List<RenderFrame>();
        InitAwaitProbe.Resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            NativeRenderer renderer = StartSession(strict: false, frames);
            FakeShellHost.AutoCompleteHostCall = true;

            // Rule 3 control: this harness mounts a page whose OnInitializedAsync awaits a task
            // the app completed itself, so the refusal below is about the shell call's yield.
            renderer.Mount<CompletedTaskInitProbe>();

            Exception? refused = Record.Exception(() => renderer.Mount<InitAwaitProbe>());

            // Rule 2 anchor: the page's call really was answered inside hostCallBegin.
            Assert.True(FakeShellHost.HostCallsCompletedInsideBegin == 1,
                $"anchor: the fake must answer the page's geolocation call INSIDE hostCallBegin, but it answered "
                + $"{FakeShellHost.HostCallsCompletedInsideBegin} that way; without it this fact tests nothing.");
            Assert.True(refused is InvalidOperationException,
                "a page whose OnInitializedAsync awaits a host call the shell answered inside begin mounted, or "
                + $"failed with something else: {refused?.GetType().FullName ?? "no exception"}. Since 16.7 the "
                + "await yields, so its first render cannot complete synchronously and Mount<T> must refuse it.");
            Assert.Contains("requires RenderRootComponentAsync to complete synchronously", refused!.Message, StringComparison.Ordinal);

            // The page's continuation still runs, later, from the dispatcher queue.
            Assert.True(WaitUntil(() => InitAwaitProbe.Resumed.Task.IsCompleted, Budget),
                "the refused page's OnInitializedAsync never resumed after its yield.");
        }
        finally
        {
            WaitUntil(() => InitAwaitProbe.Resumed.Task.IsCompleted, Budget);
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }

    [Fact]
    public void TheBackHoldProbe_HoldsTheRenderThread_AndTheShellsAnswerReleasesIt()
    {
        // The unconditional yield posts a host call's continuation to the render thread when
        // the call starts there. BackHoldProbe blocks the render thread on a camera call, so
        // it must start that call on the pool, or the block waits on itself forever. The JVM
        // BackNoticeTest and both device back tests drive this probe; this fact is the bounded
        // .NET pin for its no-deadlock claim, so a regression reds here instead of hanging there.
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

            // Rule 2 anchor: the capture really began and the handler really is held on it.
            Assert.True(WaitUntil(() => FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Camera), Budget),
                "the Hold handler never began its camera call.");
            Assert.False(returned.Wait(TimeSpan.FromMilliseconds(200)),
                "the Hold dispatch returned while its camera call was still held: the probe no longer holds the render thread.");

            long capture = FakeShellHost.HostCalls().Single(c => c.Op == (int)HostCallOp.Camera).RequestId;
            Assert.Equal(0, NativeShellBridge.CompleteHostCall(capture, 1, null)); // 1 = Cancelled

            Assert.True(returned.Wait(Budget),
                $"the Hold dispatch did not return within {Budget.TotalSeconds:0}s of the shell answering its camera "
                + "call. The render thread is blocked on a call whose continuation was posted to the render thread "
                + "itself: BackHoldProbe must start the call off the render thread.");
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
            // Only a released handler lets the session go; a deadlocked one is left to the
            // background thread, and the fact has already failed.
            if (returned.IsSet)
            {
                HostSession.ResetForTests();
                NativeShellBridge.ResetForTests();
            }
        }
    }

    /// <summary>16.7: a page that awaits a host call in OnInitializedAsync.</summary>
    private sealed class InitAwaitProbe : ComponentBase
    {
        public static TaskCompletionSource Resumed = new();
        [Inject] public IMobileBridge Bridge { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            await Bridge.CheckGeolocationPermissionAsync();
            Resumed.TrySetResult();
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "text");
            b.AddContent(1, "init-await");
            b.CloseElement();
        }
    }

    /// <summary>16.7, the mount fact's control: OnInitializedAsync awaits only a completed task.</summary>
    private sealed class CompletedTaskInitProbe : ComponentBase
    {
        protected override async Task OnInitializedAsync() => await Task.CompletedTask;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "text");
            b.AddContent(1, "init-completed");
            b.CloseElement();
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

    /// <summary>16.7 (#455): handlers whose awaited call the shell completes INSIDE begin.</summary>
    private sealed class InlineCompletionProbe : ComponentBase
    {
        [Inject] public IMobileBridge Bridge { get; set; } = default!;
        private string _status = "idle";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, async () =>
            {
                await Bridge.CheckGeolocationPermissionAsync();
                throw new InvalidOperationException("inline-host");
            }));
            b.AddContent(2, "host-throw");
            b.CloseElement();

            b.OpenElement(3, "button");
            b.AddAttribute(4, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, async () =>
            {
                await Bridge.FetchAsync(new BridgeHttpRequest("https://inline.test/"));
                throw new InvalidOperationException("inline-fetch");
            }));
            b.AddContent(5, "fetch-throw");
            b.CloseElement();

            b.OpenElement(6, "button");
            b.AddAttribute(7, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, async () =>
            {
                await Bridge.CheckGeolocationPermissionAsync();
                _status = "host-ok:done";
            }));
            b.AddContent(8, "host-ok");
            b.CloseElement();

            b.OpenElement(9, "button");
            b.AddAttribute(10, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, async () =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("completed-task");
            }));
            b.AddContent(11, "completed-throw");
            b.CloseElement();

            b.OpenElement(12, "text");
            b.AddContent(13, _status);
            b.CloseElement();
        }
    }
}
