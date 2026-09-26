using Microsoft.AspNetCore.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// #345 — FIXED IN PHASE 16.1. THIS TEST NOW PINS THE FIX.
//
// History: phase 14.1 fixed #339's SHELL-SIDE trigger, but not the .NET-side
// root cause shared by both shells. An async handler awaiting an OPEN host call
// left the dispatch Task incomplete, and Exports.DispatchEventCore's
// GetAwaiter().GetResult() blocked the serial dispatch lane until the host
// replied. On a device the host does not reply until the user taps Allow, and a
// permission sheet fires a lifecycle event that also wants the lane, so neither
// ever ran again. Until 16.1 this test asserted that the lane STILL blocked, as
// honest bookkeeping of the known defect, and said to flip it, not delete it,
// the day the root cause was fixed.
//
// 16.1 is that day. The renderer owns a render thread, and the export waits only
// for the handler's SYNCHRONOUS part: it returns rc 0 once the handler has
// yielded, while the host call is still open. So the assertion is inverted, and
// the rc is now asserted too. This closes #345.
//
// WHY NOTHING CAUGHT #345 FOR SO LONG: FakeShellHost.AutoCompleteHostCall
// defaults to true and pushes a canned completion inline, so the handler never
// actually goes async. Four bridge suites DO hold calls open, but every one calls
// the bridge DIRECTLY, never through dispatch. One flag separated the suite from
// the bug, and this test sets it.
//
// THIS TEST MUST FAIL FAST, NEVER HANG. It runs the dispatch on a worker thread
// and asserts on a bounded wait: a regression back to a blocking wait is a
// failed assertion within a second, not a hung CI job.
//
// DOES NOT COVER: the frames of the handler's continuation, a fault after the
// first await, which is FaultNotice's pin, or the host-event arms. The cascade,
// a second dispatch while this one is suspended, is DispatchWindowScopeTests.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class DispatchLaneBlockingTests
{
    /// <summary>Generous enough that a slow CI runner never flakes, short enough
    /// that a regression is a failed test rather than a hung job.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    private static int ClickHandlerForLabel(RenderFrame mount, string label)
    {
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == label);
        int buttonNode = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == text.NodeId).ParentId!.Value;
        return Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == buttonNode && p.EventName == "click").HandlerId;
    }

    [Fact]
    public void AnAsyncHandlerAwaitingAnOpenHostCall_ReturnsWhileTheCallIsOpen()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();

        // Declared here (not inside the try) so the finally block below can
        // complete the still-running worker instead of tearing state out from
        // under it — see the finally block's comment.
        var returned = new ManualResetEventSlim(false);
        Thread? worker = null;

        try
        {
            NativeRenderer renderer = HostSession.EnsureSession();
            var frames = new List<RenderFrame>();
            renderer.Frames += (f, _) => { frames.Add(f); return ValueTask.CompletedTask; };
            Assert.Equal(0, HostSession.TryMount("BnCameraDemo"));
            Assert.NotEmpty(frames);

            int take = ClickHandlerForLabel(frames[0], "Take Photo");

            // THE DEVICE CONDITION: the host call stays open, exactly as it does
            // while the OS permission sheet is up and the user has not answered.
            FakeShellHost.AutoCompleteHostCall = false;

            // Run the dispatch OFF the test thread so a regression is a timeout we
            // can assert on, not a hung test host.
            int rc = -1;
            worker = new Thread(() =>
            {
                rc = Exports.DispatchEventCore((ulong)take, """{"name":"click"}""");
                returned.Set();
            })
            { IsBackground = true, Name = "dispatch-probe" };
            worker.Start();

            // THE FIX, PINNED: the export returns while the host call is still open, and
            // reports rc 0, because rc covers only the synchronous part of the handler.
            // One second, not Budget: the export no longer waits on the host at all, so
            // it returns in milliseconds, and a slow-but-returning regression still reds.
            Assert.True(returned.Wait(TimeSpan.FromSeconds(1)),
                "dispatch_event did NOT return within 1s while the host call was still open "
                + $"(requestId={FakeShellHost.LastHostCallRequestId}). The export is blocking on "
                + "the whole handler again, which is #345: an async handler awaiting the host holds "
                + "the shell's dispatch lane until the user answers the permission sheet.");
            Assert.Equal(0, rc);

            // Anchor: the call really is open, so the return above was not the handler
            // simply finishing. A canned inline completion would leave nothing to await.
            Assert.True(FakeShellHost.LastHostCallRequestId >= 0,
                "the Take Photo click never began a host call, so this test proved nothing about "
                + "an OPEN call. Has the camera demo's button or FakeShellHost's hostCallBegin moved?");
        }
        finally
        {
            FakeShellHost.AutoCompleteHostCall = true;

            // CLEANUP, NOT PART OF THE MEASUREMENT (kept from the pre-16.1 pin, which
            // mirrored the JVM twin's cleanup in HostEventTest.kt). The export has
            // returned, but the HANDLER is still suspended on the open host call. Resetting
            // HostSession/NativeShellBridge under it would cancel the call's TCS, and with
            // RunContinuationsAsynchronously that continuation resumes AFTER this method
            // has returned, racing the next test in this [Collection("host-session")]. So:
            // complete the held call exactly as the shell would once the user answers the
            // permission sheet, wait for the worker, and only THEN reset. If the fix
            // regresses, the worker is still parked in the export, and this completion is
            // what releases it. Bounded throughout — a hanging teardown is worse than no
            // guard.
            if (FakeShellHost.LastHostCallRequestId >= 0)
            {
                NativeShellBridge.CompleteHostCall(
                    FakeShellHost.LastHostCallRequestId, (int)CameraStatus.Cancelled, null);
            }

            returned.Wait(Budget);
            worker?.Join(Budget);

            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }
}
