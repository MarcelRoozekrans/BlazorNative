using Microsoft.AspNetCore.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// #339 — THE DEADLOCK, REPRODUCED WITHOUT A DEVICE.
//
// An async handler awaiting an OPEN host call leaves DispatchUiEventAsync's Task
// incomplete, so Exports.DispatchEventCore's GetAwaiter().GetResult() blocks the
// serial dispatch lane until the host replies. On a device the host does not
// reply until the user taps Allow, and a permission sheet fires a lifecycle event
// that also wants the lane — so neither ever runs again.
//
// Exports.cs's own comment says GetAwaiter().GetResult() is "the sync contract,
// not a blocking wait". That is TRUE for a synchronous handler, whose work the
// InlineDispatcher completes on the calling thread, and FALSE here — which is
// the only case where it matters.
//
// WHY NOTHING CAUGHT THIS: FakeShellHost.AutoCompleteHostCall defaults to true
// and pushes a canned completion inline, so the handler never actually goes
// async. Four bridge suites DO hold calls open — CameraBridgeTests,
// GeolocationBridgeTests, NotificationBridgeTests, SecureStorageBridgeTests —
// but every one of them calls the bridge DIRECTLY. None goes through dispatch.
// One flag separated the suite from this bug.
//
// THIS TEST MUST FAIL, NEVER HANG. It runs the dispatch on a worker thread and
// asserts on a bounded wait, because a guard that hangs CI is worse than no
// guard at all.
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
    public void AnAsyncHandlerAwaitingAnOpenHostCall_DoesNotBlockTheDispatchLane()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();

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
            var returned = new ManualResetEventSlim(false);
            int rc = -1;
            var worker = new Thread(() =>
            {
                rc = Exports.DispatchEventCore((ulong)take, """{"name":"click"}""");
                returned.Set();
            })
            { IsBackground = true, Name = "dispatch-probe" };
            worker.Start();

            Assert.True(returned.Wait(Budget),
                $"dispatch_event did NOT return within {Budget.TotalSeconds:0}s while the host call "
                + $"was open (requestId={FakeShellHost.LastHostCallRequestId}). That is #339: the "
                + "handler went async, GetAwaiter().GetResult() blocked the serial lane, and on a "
                + "device the lifecycle event raised by the permission sheet can never run — so the "
                + "app is dead until force-quit. Do NOT 'fix' this by completing the host call in "
                + "the test; the open call IS the condition under test.");

            Assert.Equal(0, rc);
        }
        finally
        {
            FakeShellHost.AutoCompleteHostCall = true;
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }
}
