using Microsoft.AspNetCore.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// #339 — THE TRIGGER IS FIXED. THE ROOT CAUSE IS NOT. THIS TEST PINS THE GAP.
//
// Phase 14.1 fixed the SHELL-SIDE trigger: Swift's dispatchHostEvent no longer
// blocks its own lifecycle caller (see the ios split in this phase). It did NOT
// touch Exports.DispatchEventCore, which is the .NET-side root cause shared by
// BOTH shells and is explicitly out of scope here — see the "Do not touch"
// note in this phase's brief and docs/plans/2026-09-21-phase-14.1-conclusion.md.
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
// THIS TEST DELIBERATELY PINS A KNOWN DEFECT. It asserts that the dispatch
// lane STILL blocks under this condition — i.e. it PASSES on today's (broken)
// behaviour and will FAIL the day someone fixes Exports.DispatchEventCore.
// That is intentional: a fix changes the shape of this test (see #345, the
// follow-up issue filed for the root cause), it does not delete it. When the redesign
// lands, this test FLIPS — the assertion inverts back to "does not block" —
// it is not removed. Until then it is honest bookkeeping, not a passing green
// light: BlazorNative apps still freeze on a device when a permission sheet
// interrupts an async handler.
//
// THIS TEST MUST FAIL FAST, NEVER HANG. It runs the dispatch on a worker thread
// and asserts on a bounded wait, because a guard that hangs CI is worse than no
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
    public void AnAsyncHandlerAwaitingAnOpenHostCall_STILL_BlocksTheDispatchLane()
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

            // KNOWN DEFECT, PINNED ON PURPOSE: this assertion is inverted from what a
            // healthy dispatch lane should do. It asserts the lane is STILL blocked —
            // i.e. it passes on today's broken behaviour. See the root-cause issue
            // filed alongside this phase (#345) and docs/plans/2026-09-21-phase-14.1-conclusion.md.
            // rc is left at its sentinel (-1): DispatchEventCore never returns while
            // the host call stays open, so there is no return code to assert on.
            Assert.False(returned.Wait(Budget),
                $"dispatch_event RETURNED within {Budget.TotalSeconds:0}s while the host call was "
                + $"still open (requestId={FakeShellHost.LastHostCallRequestId}, rc={rc}). That is "
                + "unexpected: this test pins the KNOWN #339 root-cause defect that "
                + "Exports.DispatchEventCore's GetAwaiter().GetResult() blocks the dispatch lane on "
                + "an async handler. If this test now fails, the root cause has been fixed — invert "
                + "this assertion back (Assert.True) instead of deleting the test, and close #345.");
        }
        finally
        {
            FakeShellHost.AutoCompleteHostCall = true;
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }
}
