using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NavigationTests — Phase 3.5 Gate 1 (design §1, DoD #7): the navigation
// service swaps the root component through Blazor's RemoveRootComponent →
// the 3.3 disposal machinery (RemoveNode patches clear the screen) →
// TryMount's registry brings the next page. The Kotlin twin
// (NavigationTest.kt, Gate 2) mirrors the swap through the published dll.
//
// The FIRST test is the design's named-risk probe: RemoveRootComponent(int)
// exists on Blazor 10's Renderer (protected internal — reachable from our
// NativeRenderer subclass as NativeRenderer.Unmount) and its disposal batch
// emits RemoveNodePatches for the root's views. Everything else builds on
// that primitive.
//
// State note: navigation spans BOTH process-wide singletons — HostSession
// (renderer/current root) AND NativeShellBridge (the host Navigate/
// CurrentRoute callbacks, faked via FakeShellHost). All classes touching
// either singleton serialize via the shared "host-session" collection (the
// former "native-shell-bridge" collection merged into it in Phase 3.5 —
// see HostSessionTestCollection.cs).
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class NavigationTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    /// <summary>Registers the fake host bridge, resets the session, and wires
    /// a frame recorder. FakeShellHost.Route pre-seeds the host's CurrentRoute
    /// answer (the startup-route input); its Navigate callback records the
    /// route the runtime notified (the navigation output).</summary>
    private static (NativeRenderer Renderer, List<RenderFrame> Frames) StartSession(
        string hostRoute = "/")
    {
        FakeShellHost.Reset();
        FakeShellHost.Route = hostRoute;
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        return (renderer, frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    private static INavigationManager Nav()
        => Assert.IsAssignableFrom<INavigationManager>(HostSession.CurrentNavigationManager);

    // ── Frame pins (BnDemoTests conventions: structure, never raw nodeIds) ──

    /// <summary>NodeIds removed by a frame's RemoveNodePatches.</summary>
    private static HashSet<int> RemovedNodes(RenderFrame frame)
        => frame.Patches.OfType<RemoveNodePatch>().Select(p => p.NodeId).ToHashSet();

    /// <summary>The single parentless create — a mount frame's root view.</summary>
    private static CreateNodePatch Root(RenderFrame mount)
        => Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

    /// <summary>BnDemo's bound input — the shape pin BnSettingsPage lacks.</summary>
    private static CreateNodePatch InputNode(RenderFrame mount)
        => Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "input");

    private static bool HasText(RenderFrame frame, string text)
        => frame.Patches.OfType<ReplaceTextPatch>().Any(p => p.Text == text);

    /// <summary>NodeId of the element CONTAINING the given text.</summary>
    private static int ContainerOfText(RenderFrame frame, string text)
    {
        var t = Assert.Single(frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == text);
        var create = Assert.Single(frame.Patches.OfType<CreateNodePatch>(), p => p.NodeId == t.NodeId);
        return Assert.IsType<int>(create.ParentId);
    }

    private static int ClickHandlerOn(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == nodeId && p.EventName == "click").HandlerId;

    // ── The design's named risk, verified FIRST ──────────────────────────────

    [Fact]
    public void RemoveRootComponent_Probe_DisposalBatchEmitsRemoveNodesForRootViews()
    {
        HostSession.ResetForTests();
        try
        {
            NativeRenderer renderer = HostSession.EnsureSession();
            var frames = new List<RenderFrame>();
            renderer.Frames += (f, _) =>
            {
                frames.Add(f);
                return ValueTask.CompletedTask;
            };

            int rootComponentId = renderer.Mount<HelloComponent>();
            Assert.NotEmpty(frames);
            var rootViews = frames[0].Patches.OfType<CreateNodePatch>()
                .Where(p => p.ParentId is null)
                .Select(p => p.NodeId)
                .ToList();
            Assert.NotEmpty(rootViews);
            frames.Clear();

            renderer.Unmount(rootComponentId);

            // ONE synchronous disposal frame whose RemoveNodePatches cover
            // every host-root view the mount created — the screen clears.
            RenderFrame disposal = Assert.Single(frames);
            var removed = RemovedNodes(disposal);
            foreach (int view in rootViews)
                Assert.Contains(view, removed);
        }
        finally
        {
            HostSession.ResetForTests();
        }
    }

    // ── The swap: removes then creates, host notified ─────────────────────────

    [Fact]
    public async Task NavigateTo_SwapsRoot_RemovesThenCreates()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            RenderFrame demoMount = frames[0];
            int demoRoot = Root(demoMount).NodeId;
            int demoInput = InputNode(demoMount).NodeId;

            string? routeChanged = null;
            INavigationManager nav = Nav();
            nav.RouteChanged += r => routeChanged = r;
            frames.Clear();

            await nav.NavigateToAsync("/settings");

            // The host Navigate callback was invoked with the new route.
            Assert.Equal("/settings", FakeShellHost.Route);

            // ONE sequence: the disposal frame (old root's RemoveNodePatches —
            // the form div AND the input's node are gone, nodeId-pinned)…
            Assert.Equal(2, frames.Count);
            var removed = RemovedNodes(frames[0]);
            Assert.Contains(demoRoot, removed);
            Assert.Contains(demoInput, removed);

            // …then BnSettingsPage's creates: the settings title text exists
            // on a fresh node under a fresh themed root; no input anywhere.
            RenderFrame settingsMount = frames[1];
            Assert.True(HasText(settingsMount, "Settings"),
                "settings title text missing from the swap's mount frame");
            Assert.Equal("view", Root(settingsMount).NodeType);
            Assert.DoesNotContain(settingsMount.Patches.OfType<CreateNodePatch>(),
                p => p.NodeType == "input");

            Assert.Equal("/settings", nav.CurrentRoute);
            Assert.Equal("/settings", routeChanged);
        }
        finally
        {
            TearDown();
        }
    }

    // ── Back: BnDemo remounts FRESH (state does not survive the swap) ────────

    [Fact]
    public async Task NavigateBack_RemountsFresh()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            RenderFrame demoMount = frames[0];
            var changeHandler = Assert.Single(demoMount.Patches.OfType<AttachEventPatch>(),
                p => p.EventName == "change").HandlerId;

            // Seed state: type "hello" through the bind loop.
            Assert.Equal(0, Exports.DispatchEventCore((ulong)changeHandler,
                /*lang=json*/ """{"name":"change","payload":"hello"}"""));
            Assert.True(HasText(frames[^1], "hello"), "seed text never echoed");

            INavigationManager nav = Nav();
            await nav.NavigateToAsync("/settings");
            frames.Clear();

            await nav.NavigateToAsync("/");

            // The remount frame is a FRESH BnDemo: empty value prop + empty
            // echo, and the seeded text is nowhere in the new tree.
            RenderFrame remount = frames[^1];
            int input = InputNode(remount).NodeId;
            var valueProp = Assert.Single(remount.Patches.OfType<UpdatePropPatch>(),
                p => p.NodeId == input && p.Name == "value");
            Assert.Equal("", valueProp.Value);
            Assert.True(HasText(remount, ""), "fresh echo text missing");
            Assert.False(HasText(remount, "hello"), "stale state survived the remount");
            Assert.Equal("/", nav.CurrentRoute);
        }
        finally
        {
            TearDown();
        }
    }

    // ── Unknown route: surfaced per the strict conventions ───────────────────

    [Fact]
    public async Task UnknownRoute_Throws()
    {
        var (_, _) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();

            var ex = await Assert.ThrowsAsync<ArgumentException>(
                () => nav.NavigateToAsync("/nope").AsTask());
            Assert.Contains("/nope", ex.Message);

            // Nothing happened: the host was not notified, the route stands.
            Assert.Equal("/", FakeShellHost.Route);
            Assert.Equal("/", nav.CurrentRoute);
        }
        finally
        {
            TearDown();
        }
    }

    // ── Startup route: the host's restored route wins the first mount ────────

    [Fact]
    public async Task StartupRoute_HonorsHostCurrentRoute()
    {
        var (_, frames) = StartSession(hostRoute: "/settings");
        try
        {
            // The host mounts by NAME (the ABI is unchanged) — the routed
            // default entry resolves through the host-restored route.
            Assert.Equal(0, HostSession.TryMount("BnDemo"));

            RenderFrame mount = frames[0];
            Assert.True(HasText(mount, "Settings"),
                "first mount did not resolve to BnSettingsPage");
            Assert.DoesNotContain(mount.Patches.OfType<CreateNodePatch>(),
                p => p.NodeType == "input");

            INavigationManager nav = Nav();
            Assert.Equal("/settings", nav.CurrentRoute);

            // NavigateTo from the restored route behaves: back to "/".
            await nav.NavigateToAsync("/");
            Assert.Equal("/", FakeShellHost.Route);
            _ = InputNode(frames[^1]); // BnDemo's shape arrived
            Assert.Equal("/", nav.CurrentRoute);
        }
        finally
        {
            TearDown();
        }
    }

    [Fact]
    public void StartupRoute_UnknownHostRoute_FallsBackToDefault()
    {
        var (_, frames) = StartSession(hostRoute: "/not-a-route");
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));

            // Unknown host route → "/" → BnDemo itself.
            _ = InputNode(frames[0]);
            Assert.Equal("/", Nav().CurrentRoute);
        }
        finally
        {
            TearDown();
        }
    }

    // ── Navigating from INSIDE a click handler (the dispatch-window pin) ─────

    [Fact]
    public async Task Navigate_FromInsideClickHandler()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();
            await nav.NavigateToAsync("/settings");
            RenderFrame settingsMount = frames[^1];
            int backButton = ContainerOfText(settingsMount, "← Back");
            int backHandler = ClickHandlerOn(settingsMount, backButton);
            int settingsRoot = Root(settingsMount).NodeId;
            frames.Clear();

            // The page's own click handler performs the swap SYNCHRONOUSLY
            // with respect to the export: when DispatchEventCore returns, the
            // removes AND the new page's creates have already been delivered
            // (the dispatch-window interplay pin). Blazor completes the
            // event's OWN batch first (the handler's no-op re-render may
            // precede the swap frames) — the pin is ORDER, not frame count:
            // removes strictly before creates, all inside this dispatch.
            int rc = Exports.DispatchEventCore((ulong)backHandler, ClickArgs);
            Assert.Equal(0, rc);

            int removeIdx = frames.FindIndex(f => RemovedNodes(f).Contains(settingsRoot));
            Assert.True(removeIdx >= 0,
                "settings root was never removed during the dispatch");
            int createIdx = frames.FindIndex(f =>
                f.Patches.OfType<CreateNodePatch>().Any(p => p.NodeType == "input"));
            Assert.True(createIdx > removeIdx,
                $"BnDemo's creates must follow the removes (remove@{removeIdx}, create@{createIdx})");
            Assert.Equal("/", FakeShellHost.Route);
            Assert.Equal("/", nav.CurrentRoute);
        }
        finally
        {
            TearDown();
        }
    }
}
