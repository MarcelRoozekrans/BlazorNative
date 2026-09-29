using System.Collections.Concurrent;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 16.1, spike requirement 5 — every host-event arm runs its component code
// on the render thread of the renderer under test.
//
// With an honest CheckAccess, Blazor rejects StateHasChanged off the render
// thread. The lifecycle multicast and the safe-area report have subscribers that
// call it, so Exports.DispatchHostEventCore marshals EVERY arm — "back",
// "navigate", "safeAreaChanged" and the multicast fallthrough — onto the session
// renderer's render thread and waits for its synchronous part.
//
// The probe records Environment.CurrentManagedThreadId from every piece of
// component code an arm can reach: OnInitialized, BuildRenderTree, a RouteChanged
// subscriber, a BnSafeAreaInsets.Changed subscriber and a NativeEvents
// subscriber. Each arm is asserted against renderer.RenderThreadId — THIS
// renderer's thread, never a process-wide "last constructed" value (spike
// requirement 9).
//
// "back" and "navigate" reach COMPONENT code only through HostSession.SwapRoot
// and NativeRenderer.Mount, which marshal onto the render thread themselves, so
// component records alone cannot tell whether the ARM was marshalled. What can:
// the nav manager notifies the host FIRST, on whatever thread the arm runs on,
// before the swap marshals. So for those two arms the pin also asserts the thread
// of the host's Navigate callback (FakeShellHost.LastNavigateThread).
//
// DOES NOT COVER: thread identity for an arm with no session, which runs on the
// caller by design; or code an arm might run between the host notify and the
// swap, beyond the callback itself.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class HostEventArmThreadTests
{
    private const string SafeAreaPayload =
        /*lang=json*/ """{"top":"47","right":"0","bottom":"34","left":"0"}""";

    private static (NativeRenderer Renderer, Func<NativeRenderer, int> Demo, Func<NativeRenderer, int> Settings)
        StartSessionWithProbePages()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        BnSafeAreaInsets.ResetForTests();
        ThreadProbe.Records.Clear();
        NativeRenderer renderer = HostSession.EnsureSession();
        // "/" and "/settings" both mount the probe, so every arm lands in probe code.
        var demo = HostSession.ReplaceRegistryEntryForTests("BnDemo", r => r.Mount<ThreadProbe>());
        var settings = HostSession.ReplaceRegistryEntryForTests("BnSettingsPage", r => r.Mount<ThreadProbe>());
        Assert.Equal(0, HostSession.TryMount("BnDemo"));
        return (renderer, demo, settings);
    }

    private static void TearDown(Func<NativeRenderer, int> demo, Func<NativeRenderer, int> settings)
    {
        HostSession.ReplaceRegistryEntryForTests("BnDemo", demo);
        HostSession.ReplaceRegistryEntryForTests("BnSettingsPage", settings);
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
        BnSafeAreaInsets.ResetForTests();
        ThreadProbe.Records.Clear();
    }

    /// <summary>Runs one arm and asserts every probe record it produced carries this
    /// renderer's render thread, with the expected tag among them.</summary>
    private static void AssertArmRunsOnTheRenderThread(
        NativeRenderer renderer, string arm, string? payload, string expectedTag)
    {
        ThreadProbe.Records.Clear();
        int rc = Exports.DispatchHostEventCore(arm, payload);

        var records = ThreadProbe.Records.ToArray();
        // Anchor: the arm reached probe code at all, through the tag that is its own.
        Assert.True(records.Any(r => r.Tag == expectedTag),
            $"host_event '{arm}' produced no '{expectedTag}' record "
            + $"(got: {string.Join(", ", records.Select(r => r.Tag))}), so this pin checked "
            + "nothing for it. Has the arm, or the probe's subscription, moved?");
        int owner = renderer.RenderThreadId;
        var off = records.Where(r => r.Thread != owner).ToArray();
        Assert.True(off.Length == 0,
            $"host_event '{arm}' ran component code off the render thread: "
            + string.Join(", ", off.Select(r => $"{r.Tag} on thread {r.Thread}"))
            + $"; this renderer's render thread is {owner}. Every host-event arm must run on "
            + "it, because Blazor rejects StateHasChanged anywhere else.");
        // Checked after the thread, so an off-thread arm reds with the message that
        // names the cause, not with the rc 2 its rejected StateHasChanged produces.
        Assert.True(rc == 0, $"host_event '{arm}' returned rc {rc}, not 0");
    }

    /// <summary>For "back" and "navigate": the host's Navigate callback ran on the
    /// render thread, which it does only when the ARM itself was marshalled.</summary>
    private static void AssertHostNotifiedOnTheRenderThread(NativeRenderer renderer, string arm)
    {
        int notified = FakeShellHost.LastNavigateThread;
        Assert.True(notified >= 0,
            $"host_event '{arm}' never called the host's Navigate callback, so the arm's own "
            + "thread was not observed. Has the nav manager stopped notifying the host first?");
        Assert.True(notified == renderer.RenderThreadId,
            $"host_event '{arm}' notified the host on thread {notified}, but this renderer's "
            + $"render thread is {renderer.RenderThreadId}: the arm ran on the caller's thread. "
            + "Its component code still landed on the render thread only because SwapRoot "
            + "marshals by itself.");
    }

    [Fact]
    public void EveryHostEventArm_RunsItsComponentCode_OnTheRenderThread()
    {
        var (renderer, demo, settings) = StartSessionWithProbePages();
        try
        {
            FakeShellHost.LastNavigateThread = -1;
            AssertArmRunsOnTheRenderThread(renderer, BnHostEvents.Navigate, "/settings", "init");
            AssertHostNotifiedOnTheRenderThread(renderer, BnHostEvents.Navigate);
            Assert.Equal("/settings", HostSession.CurrentNavigationManager!.CurrentRoute);

            FakeShellHost.LastNavigateThread = -1;
            AssertArmRunsOnTheRenderThread(renderer, BnHostEvents.Back, null, "init");
            AssertHostNotifiedOnTheRenderThread(renderer, BnHostEvents.Back);
            Assert.Equal("/", HostSession.CurrentNavigationManager!.CurrentRoute);

            AssertArmRunsOnTheRenderThread(renderer, BnHostEvents.SafeAreaChanged, SafeAreaPayload, "safeArea");

            AssertArmRunsOnTheRenderThread(renderer, "onPause", null, "multicast");
        }
        finally
        {
            TearDown(demo, settings);
        }
    }

    [Fact]
    public void TheThreadDetector_SeesSubscriberCodeRunOffTheRenderThread_PositiveControl()
    {
        // Rule 3: the same probe, driven from the TEST thread without an arm, must
        // record a thread other than the render thread. Otherwise the pin above could
        // not tell the two apart. StateHasChanged then throws off-thread; both raisers
        // isolate subscribers, so the probe's record is made before that.
        var (renderer, demo, settings) = StartSessionWithProbePages();
        try
        {
            ThreadProbe.Records.Clear();
            NativeShellBridge.RaiseNativeEvent(new NativeEvent("onPause", null));
            BnSafeAreaInsets.Report(new BnSafeAreaInsets(1, 2, 3, 4));

            int owner = renderer.RenderThreadId;
            int here = Environment.CurrentManagedThreadId;
            Assert.NotEqual(owner, here);
            var records = ThreadProbe.Records.ToArray();
            Assert.Contains(records, r => r.Tag == "multicast" && r.Thread == here);
            Assert.Contains(records, r => r.Tag == "safeArea" && r.Thread == here);
        }
        finally
        {
            TearDown(demo, settings);
        }
    }

    /// <summary>Records the thread of every piece of its own code an arm can reach.
    /// Static slots are safe under the "host-session" collection.</summary>
    private sealed class ThreadProbe : ComponentBase, IDisposable
    {
        public static readonly ConcurrentQueue<(string Tag, int Thread)> Records = new();

        private static void Record(string tag) => Records.Enqueue((tag, Environment.CurrentManagedThreadId));

        [Inject] public IMobileBridge Bridge { get; set; } = default!;
        [Inject] public INavigationManager Navigation { get; set; } = default!;

        protected override void OnInitialized()
        {
            Record("init");
            Bridge.NativeEvents += OnNativeEvent;
            BnSafeAreaInsets.Changed += OnSafeArea;
            Navigation.RouteChanged += OnRouteChanged;
        }

        private void OnNativeEvent(NativeEvent _) { Record("multicast"); StateHasChanged(); }

        private void OnSafeArea(BnSafeAreaInsets _) { Record("safeArea"); StateHasChanged(); }

        private void OnRouteChanged(string _) => Record("route");

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            Record("render");
            b.OpenElement(0, "div");
            b.CloseElement();
        }

        public void Dispose()
        {
            Bridge.NativeEvents -= OnNativeEvent;
            BnSafeAreaInsets.Changed -= OnSafeArea;
            Navigation.RouteChanged -= OnRouteChanged;
        }
    }
}
