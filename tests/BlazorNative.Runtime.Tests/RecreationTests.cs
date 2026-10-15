using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 17.3 (#425) — a mount replaces the root.
//
// A recreated Android Activity starts a new runtime, which mounts the default page again on the ONE
// process-global session. Before 17.3 the old root stayed alive: a handler suspended on a host call
// resumed after recreation, re-rendered, and its frame reached the new Activity carrying the old
// page's node ids. Since 17.3 a mount disposes any root already mounted, so that late continuation
// re-renders a disposed component and Blazor drops it; the recreated shell stays on the page the
// user was on, with its back stack; and a navigation from the disposed root's late code is dropped.
//
// The pages are swapped for probes through HostSession.ReplaceRegistryEntryForTests: "BnDemo" (the
// default, "/") and "BnSettingsPage" ("/settings").
//
// DOES NOT COVER:
//   - the shell: RecreationAndroidTest drives a real Activity recreation, and RetireLateContinuationTest
//     the retired runtime dropping frames;
//   - a recreation DURING boot, while the old boot thread is still inside start(): MainActivity
//     documents it, and nothing here builds it: #511;
//   - work the old page queued through RunAfterDispatch before its root was disposed (#500's class).
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class RecreationTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    private sealed record Session(NativeRenderer Renderer, List<RenderFrame> Frames,
        Func<NativeRenderer, int> Demo, Func<NativeRenderer, int> Settings);

    private static Session Start(Func<NativeRenderer, int> demo)
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        HostSession.StrictErrorsForTests = false;
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) => { lock (frames) frames.Add(f); return ValueTask.CompletedTask; };
        var originalDemo = HostSession.ReplaceRegistryEntryForTests("BnDemo", demo);
        var originalSettings = HostSession.ReplaceRegistryEntryForTests("BnSettingsPage", r => r.Mount<SettingsMarker>());
        AwaitingProbe.Reset();
        InitAwaitingProbe.ReachedNavigate = false;
        return new Session(renderer, frames, originalDemo, originalSettings);
    }

    private static void End(Session s)
    {
        FakeShellHost.AutoCompleteHostCall = true;
        foreach (var call in FakeShellHost.HostCalls())
            NativeShellBridge.CompleteHostCall(call.RequestId, 0, null);
        HostSession.ReplaceRegistryEntryForTests("BnDemo", s.Demo);
        HostSession.ReplaceRegistryEntryForTests("BnSettingsPage", s.Settings);
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    private static List<string> Texts(Session s, int from = 0)
    {
        lock (s.Frames)
            return s.Frames.Skip(from).SelectMany(f => f.Patches.OfType<ReplaceTextPatch>()).Select(p => p.Text).ToList();
    }

    private static int ClickHandler(Session s)
    {
        lock (s.Frames)
            return s.Frames.SelectMany(f => f.Patches.OfType<AttachEventPatch>()).Last(p => p.EventName == "click").HandlerId;
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Budget)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    /// <summary>Clicks the probe on the current page; its handler suspends on a geolocation call
    /// the fake holds open. Returns that call's request id.</summary>
    private static long ClickAndHold(Session s)
    {
        FakeShellHost.AutoCompleteHostCall = false;
        int rc = Exports.DispatchEventCore((ulong)ClickHandler(s), """{"name":"click"}""");
        Assert.True(rc == 0, $"the probe's click returned rc {rc}; it should suspend on the held call");
        Assert.True(WaitUntil(() => FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Geolocation)),
            "anchor: the probe never began its geolocation call");
        return FakeShellHost.HostCalls().Last(c => c.Op == (int)HostCallOp.Geolocation).RequestId;
    }

    [Fact]
    public void ASecondMount_DisposesTheFirstRoot_SoItsLateContinuationRendersNothing()
    {
        Session s = Start(r => r.Mount<AwaitingProbe>());
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            long held = ClickAndHold(s);

            int framesBefore;
            lock (s.Frames) framesBefore = s.Frames.Count;
            Assert.Equal(0, HostSession.TryMount("BnDemo")); // the recreated shell's mount
            NativeShellBridge.CompleteHostCall(held, 0, null);

            Assert.True(WaitUntil(() => AwaitingProbe.ContinuationRan),
                "anchor: the old page's continuation never ran after its call was answered");
            Thread.Sleep(200); // a late frame, if any, is posted to the render thread right after
            Assert.DoesNotContain("a-late", Texts(s, framesBefore));
        }
        finally { End(s); }
    }

    [Fact]
    public void ASecondMount_DisposesTheFirstRoot()
    {
        Session s = Start(r => r.Mount<AwaitingProbe>());
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Assert.True(AwaitingProbe.Instances == 1, $"anchor: {AwaitingProbe.Instances} probes mounted, not 1");
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Assert.True(WaitUntil(() => AwaitingProbe.Disposed == 1),
                $"the first root was not disposed by the second mount: {AwaitingProbe.Disposed} disposed of " +
                $"{AwaitingProbe.Instances} mounted. Every recreation would leak a root.");
        }
        finally { End(s); }
    }

    [Fact]
    public void AReplacingMount_SendsTheNewShellNoRemovalFrameForTheOldRoot()
    {
        // The old root's removal frame describes the OLD shell's views, which are gone with its
        // Activity. Sent to the new shell, it removes nodes that shell never had; a navigation swap
        // keeps its removal frame, because there the shell is the same.
        Session s = Start(r => r.Mount<DemoMarker>());
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            int framesBefore;
            lock (s.Frames) framesBefore = s.Frames.Count;
            Assert.True(framesBefore > 0, "anchor: the first mount delivered no frame");
            Assert.Equal(0, HostSession.TryMount("BnDemo")); // the recreated shell's mount

            List<RenderFrame> after;
            lock (s.Frames) after = s.Frames.Skip(framesBefore).ToList();
            Assert.True(after.Count > 0, "the replacing mount delivered no frame at all");
            Assert.True(after.All(f => !f.Patches.OfType<RemoveNodePatch>().Any()),
                $"the replacing mount sent the new shell a removal of the old root's nodes; frames after it: "
                + $"[{string.Join(" | ", after.Select(f => string.Join(",", f.Patches.Select(p => p.GetType().Name))))}]");
            Assert.Contains(after[0].Patches, p => p is CreateNodePatch);
        }
        finally { End(s); }
    }

    [Fact]
    public void ASecondMount_StaysOnTheCurrentPage_WithItsHistory()
    {
        Session s = Start(r => r.Mount<DemoMarker>());
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Nav().NavigateToAsync("/settings").AsTask().GetAwaiter().GetResult();
            Assert.True(Nav().CanGoBack, "anchor: after navigating to /settings there must be a page to go back to");

            int framesBefore;
            lock (s.Frames) framesBefore = s.Frames.Count;
            Assert.Equal(0, HostSession.TryMount("BnDemo")); // the recreated shell's mount
            List<string> after = Texts(s, framesBefore);
            Assert.True(after.Contains("settings-marker") && !after.Contains("demo-marker"),
                $"the recreated shell's mount showed [{string.Join(", ", after)}]: it must stay on /settings, " +
                "where the user was.");
            Assert.True(Nav().CanGoBack, "the recreated shell lost its back stack");

            int beforeBack;
            lock (s.Frames) beforeBack = s.Frames.Count;
            Assert.True(Nav().NavigateBackAsync().AsTask().GetAwaiter().GetResult(), "back was not handled");
            Assert.Contains("demo-marker", Texts(s, beforeBack));
        }
        finally { End(s); }
    }

    [Fact]
    public void ASecondMount_ByAFreshShellAtTheRoot_ShowsTheRootAndResetsHistory()
    {
        // A NEW shell in the same process, not a recreated one: an Activity launched fresh, whose
        // route is "/", while the session was left on /settings. The shell says which page it
        // wants through CurrentRoute; a recreated shell restores its route, a fresh one starts at
        // its own. So the mount shows the root, and the old page's back stack is not this shell's.
        Session s = Start(r => r.Mount<DemoMarker>());
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Nav().NavigateToAsync("/settings").AsTask().GetAwaiter().GetResult();
            Assert.True(Nav().CanGoBack, "anchor: the session must have a back stack to reset");

            FakeShellHost.Route = "/"; // the fresh shell's own route
            int framesBefore;
            lock (s.Frames) framesBefore = s.Frames.Count;
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            List<string> after = Texts(s, framesBefore);
            Assert.True(after.Contains("demo-marker") && !after.Contains("settings-marker"),
                $"a fresh shell at '/' was shown [{string.Join(", ", after)}]: it must start at its own route, not "
                + "the page an earlier shell left the session on.");
            Assert.False(Nav().CanGoBack, "a fresh shell inherited an earlier shell's back stack");
            Assert.Equal("/", Nav().CurrentRoute);
        }
        finally { End(s); }
    }

    [Fact]
    public void ASecondMount_OfANamedPageByAFreshShell_ShowsItAndResetsHistory()
    {
        // The same fresh launch, but the shell mounts a page by NAME, as a launch Intent's
        // EXTRA_COMPONENT or deep link does. The shell's route names that page. The session was
        // left on "/" with a back stack, so the shell's route differs: it is a fresh shell, and
        // the earlier shell's back stack is not its own.
        Session s = Start(r => r.Mount<DemoMarker>());
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Nav().NavigateToAsync("/settings").AsTask().GetAwaiter().GetResult();
            Nav().NavigateToAsync("/").AsTask().GetAwaiter().GetResult();
            Assert.True(Nav().CanGoBack, "anchor: the session must have a back stack to reset");

            FakeShellHost.Route = "/settings"; // the fresh shell's own route, naming its page
            int framesBefore;
            lock (s.Frames) framesBefore = s.Frames.Count;
            Assert.Equal(0, HostSession.TryMount("BnSettingsPage"));
            List<string> after = Texts(s, framesBefore);
            Assert.True(after.Contains("settings-marker"),
                $"the named page was not shown [{string.Join(", ", after)}]");
            Assert.False(Nav().CanGoBack, "a fresh shell's named mount inherited an earlier shell's back stack");
            Assert.Equal("/settings", Nav().CurrentRoute);
        }
        finally { End(s); }
    }

    [Fact]
    public void ANavigationFromADisposedRootsContinuation_IsDropped()
    {
        Session s = Start(r => r.Mount<AwaitingProbe>());
        var log = new List<string>();
        Action<BnLogLevel, string, string>? sink = BnLog.Sink;
        BnLog.Sink = (_, category, message) => { lock (log) log.Add($"{category}: {message}"); };
        try
        {
            AwaitingProbe.NavigateAfterAwait = true;
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            long held = ClickAndHold(s);
            Assert.Equal(0, HostSession.TryMount("BnDemo")); // the recreated shell's mount
            NativeShellBridge.CompleteHostCall(held, 0, null);

            Assert.True(WaitUntil(() => AwaitingProbe.ReachedNavigate),
                "anchor: the old page's continuation never reached its NavigateToAsync");
            Thread.Sleep(200);
            Assert.True(Nav().CurrentRoute == "/",
                $"the route is {Nav().CurrentRoute}: a disposed page's late code navigated the new shell.");
            lock (log) Assert.Contains(log, l => l.Contains("disposed root", StringComparison.Ordinal));
        }
        finally
        {
            BnLog.Sink = sink;
            End(s);
        }
    }

    [Fact]
    public void ANavigationFromADisposedRootsLifecycleCode_IsDropped()
    {
        // Not a UI event: the old page's OnInitialized starts a load that awaits a held call and
        // then navigates, as a splash page's "load, then go home" does. A rotation during that await
        // disposes the page; its late code must not navigate the recreated shell either.
        Session s = Start(r => r.Mount<InitAwaitingProbe>());
        FakeShellHost.AutoCompleteHostCall = false;
        var errors = new List<string>();
        Action<BnLogLevel, string, string>? sink = BnLog.Sink;
        BnLog.Sink = (level, category, message) => { if (level >= BnLogLevel.Error) lock (errors) errors.Add($"{category}: {message}"); };
        try
        {
            Assert.True(HostSession.TryMount("BnDemo") == 0, "the probe's mount failed: " + string.Join(" | ", errors));
            Assert.True(WaitUntil(() => FakeShellHost.HostCalls().Any(c => c.Op == (int)HostCallOp.Geolocation)),
                "anchor: the probe's OnInitialized load never began its geolocation call");
            long held = FakeShellHost.HostCalls().First(c => c.Op == (int)HostCallOp.Geolocation).RequestId;

            HostSession.ReplaceRegistryEntryForTests("BnDemo", r => r.Mount<DemoMarker>());
            Assert.Equal(0, HostSession.TryMount("BnDemo")); // the recreated shell's mount
            NativeShellBridge.CompleteHostCall(held, 0, null);

            Assert.True(WaitUntil(() => InitAwaitingProbe.ReachedNavigate),
                "anchor: the old page's OnInitialized load never reached its NavigateToAsync");
            Thread.Sleep(200);
            Assert.True(Nav().CurrentRoute == "/",
                $"the route is {Nav().CurrentRoute}: a disposed page's OnInitialized load navigated the new shell.");
        }
        finally
        {
            BnLog.Sink = sink;
            End(s);
        }
    }

    private static int s_nativeFrames;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void CountNativeFrame(BlazorNativeFrame* frame) => Interlocked.Increment(ref s_nativeFrames);

    private static unsafe void RegisterNativeCounter()
    {
        delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void> fn = &CountNativeFrame;
        HostSession.SetFrameCallback((IntPtr)fn);
    }

    [Fact]
    public void AFrameOfTheOldRoot_AfterANewShellRegisters_IsNotDeliveredToIt()
    {
        // A recreated shell registers its frame callback, and only then mounts. Between the two
        // the old root is still alive, so its late continuation can render. That frame is the old
        // page's, and the shell that would receive it is the NEW one.
        Session s = Start(r => r.Mount<AwaitingProbe>());
        try
        {
            RegisterNativeCounter();
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            long held = ClickAndHold(s);

            RegisterNativeCounter(); // the recreated shell's start(): registered, not yet mounted
            int deliveredBefore = Volatile.Read(ref s_nativeFrames);
            int framesBefore;
            lock (s.Frames) framesBefore = s.Frames.Count;
            NativeShellBridge.CompleteHostCall(held, 0, null);

            Assert.True(WaitUntil(() => Texts(s, framesBefore).Contains("a-late")),
                "anchor: the old page's late continuation never rendered, so nothing was exercised");
            Thread.Sleep(200);
            Assert.True(Volatile.Read(ref s_nativeFrames) == deliveredBefore,
                $"{Volatile.Read(ref s_nativeFrames) - deliveredBefore} frame(s) of the old page reached the new "
                + "shell between its registration and its mount: its views would get the old page's nodes.");

            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Assert.True(Volatile.Read(ref s_nativeFrames) > deliveredBefore,
                "the new shell's own mount frame was never delivered: the hold was not released by its mount");
        }
        finally
        {
            HostSession.SetFrameCallback(IntPtr.Zero);
            End(s);
        }
    }

    private static NativeNavigationManager Nav()
        => HostSession.CurrentNavigationManager ?? throw new Xunit.Sdk.XunitException("no navigation manager");

    /// <summary>"/" in the replacement facts: a button whose handler suspends on a held
    /// geolocation call, then re-renders "a-late", or navigates when told to.</summary>
    private sealed class AwaitingProbe : ComponentBase, IDisposable
    {
        public static int Instances, Disposed;
        public static volatile bool ContinuationRan, ReachedNavigate, NavigateAfterAwait;

        public static void Reset()
        {
            Instances = Disposed = 0;
            ContinuationRan = ReachedNavigate = NavigateAfterAwait = false;
        }

        [Inject] public IMobileBridge Bridge { get; set; } = default!;
        [Inject] public INavigationManager Navigation { get; set; } = default!;

        private string _text = "a-start";

        protected override void OnInitialized() => Interlocked.Increment(ref Instances);

        public void Dispose() => Interlocked.Increment(ref Disposed);

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, async () =>
            {
                await Bridge.CheckGeolocationPermissionAsync();
                ContinuationRan = true;
                if (NavigateAfterAwait)
                {
                    ReachedNavigate = true;
                    await Navigation.NavigateToAsync("/settings");
                }
                else
                {
                    _text = "a-late";
                }
            }));
            b.AddContent(2, _text);
            b.CloseElement();
        }
    }

    /// <summary>A page whose OnInitialized starts a load that awaits a held geolocation call,
    /// then navigates: lifecycle code, not a UI event. A root mounts synchronously, so the
    /// load runs beside the first render rather than inside an awaited OnInitializedAsync.</summary>
    private sealed class InitAwaitingProbe : ComponentBase
    {
        public static volatile bool ReachedNavigate;

        [Inject] public IMobileBridge Bridge { get; set; } = default!;
        [Inject] public INavigationManager Navigation { get; set; } = default!;

        protected override void OnInitialized() => _ = LoadThenGoOnAsync();

        private async Task LoadThenGoOnAsync()
        {
            await Bridge.CheckGeolocationPermissionAsync();
            ReachedNavigate = true;
            await Navigation.NavigateToAsync("/settings");
        }

        protected override void BuildRenderTree(RenderTreeBuilder b) => b.AddContent(0, "init-probe");
    }

    private sealed class DemoMarker : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "text");
            b.AddContent(1, "demo-marker");
            b.CloseElement();
        }
    }

    private sealed class SettingsMarker : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "text");
            b.AddContent(1, "settings-marker");
            b.CloseElement();
        }
    }
}
