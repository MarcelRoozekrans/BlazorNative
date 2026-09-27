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
