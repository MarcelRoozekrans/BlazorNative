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

    // ── Phase 5.1 named risk: SwapRoot's RunAfterDispatch drains OFF the ──────
    // dispatch lane (host-initiated back, no open dispatch batch). NavigateBack
    // rides this: a host event arriving between clicks calls the swap with
    // _uiEventDispatchDepth == 0, where RunAfterDispatch must run the action
    // IMMEDIATELY (not queue it — nothing would ever drain the queue off-lane).
    // The 3.5 KDoc frames deferral around the in-click case; this pins the
    // no-open-batch path the back button depends on.

    [Fact]
    public void SwapRoot_OffDispatchLane_NoOpenBatch_DrainsImmediately()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            RenderFrame demoMount = frames[0];
            int demoRoot = Root(demoMount).NodeId;
            int demoInput = InputNode(demoMount).NodeId;
            frames.Clear();

            bool afterSwapRan = false;
            // Called directly — NOT inside any blazornative_dispatch_event
            // window, so _uiEventDispatchDepth == 0 (host-initiated posture).
            HostSession.SwapRoot("BnSettingsPage", afterSwap: () => afterSwapRan = true);

            // The swap ran SYNCHRONOUSLY here (no queue, no pending dispatch to
            // unwind): the old root's removes AND BnSettingsPage's creates were
            // delivered before SwapRoot returned, and afterSwap fired.
            Assert.True(afterSwapRan, "afterSwap must run immediately off the dispatch lane");
            Assert.Equal(2, frames.Count);
            var removed = RemovedNodes(frames[0]);
            Assert.Contains(demoRoot, removed);
            Assert.Contains(demoInput, removed);
            Assert.True(HasText(frames[1], "Settings"),
                "the swap's mount frame must land synchronously off-lane");
        }
        finally
        {
            TearDown();
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

    // ── The two hand-maintained registries agree (Phase 6.3 Gate 1 review) ───

    /// <summary>THE ROUTE TABLE AND THE MOUNT REGISTRY ARE TWO HAND-MAINTAINED
    /// MIRRORS, and nothing asserted they agree until there were five demo pages.
    /// <para>Every value in <c>NativeNavigationManager.s_routes</c> must be a key in
    /// <c>HostSession.s_components</c>. It is not decorative: a route whose component
    /// name is a typo is accepted by <c>NavigateToAsync</c> (the route EXISTS), reaches
    /// <c>SwapRoot</c>, and throws there — "not in the mount registry" — as a runtime
    /// failure on a device, in a click handler, for a mistake a set test catches at
    /// build time. Every page added since 3.5 has had to touch both files; this is the
    /// line that notices when one of them was missed.</para>
    /// <para>Deliberately NOT the reverse: the mount registry legitimately holds
    /// UNROUTED entries (the probes — CompositionProbe, FocusProbe, HostEventProbe,
    /// ClipboardProbe, HelloComponent — which the shells mount by NAME, never by
    /// route). Routes ⊆ registry, not equality.</para></summary>
    [Fact]
    public void EveryRoute_ResolvesToAComponentTheMountRegistryKnows()
    {
        IReadOnlyCollection<string> registry = HostSession.RegisteredComponentsForTests;

        Assert.All(NativeNavigationManager.RoutesForTests, entry =>
            Assert.True(registry.Contains(entry.Value),
                $"""route "{entry.Key}" mounts "{entry.Value}", which is NOT in HostSession's """
                + $"mount registry ({string.Join(", ", registry)}) — navigating to it would "
                + "throw on a device. The two registries are mirrors; they move together."));

        // The six ROUTED pages, by name — so that deleting a route (rather than
        // mistyping one) is also a red test rather than a silent shrink.
        // (+BnListDemo, Phase 7.2 — "/list".)
        Assert.Equal(
            ["BnDemo", "BnImageDemo", "BnLayoutDemo", "BnListDemo", "BnScrollDemo", "BnSettingsPage"],
            NativeNavigationManager.RoutesForTests.Values.OrderBy(v => v, StringComparer.Ordinal));
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
    public void Navigate_FromInsideClickHandler()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();
            RenderFrame demoMount = frames[0];
            int demoRoot = Root(demoMount).NodeId;
            int settingsButton = ContainerOfText(demoMount, "Settings →");
            int settingsHandler = ClickHandlerOn(demoMount, settingsButton);
            frames.Clear();

            // The Settings button's OWN click dispatch performs the swap
            // SYNCHRONOUSLY with respect to the export: when DispatchEventCore
            // returns, the removes AND BnSettingsPage's creates have already
            // been delivered (the dispatch-window interplay pin). Blazor
            // completes the event's OWN batch first (the handler's no-op
            // re-render may precede the swap frames) — the pin is ORDER, not
            // frame count: removes strictly before creates, all inside this
            // dispatch.
            int rc = Exports.DispatchEventCore((ulong)settingsHandler, ClickArgs);
            Assert.Equal(0, rc);

            int removeIdx = frames.FindIndex(f => RemovedNodes(f).Contains(demoRoot));
            Assert.True(removeIdx >= 0,
                "BnDemo's root was never removed during the dispatch");
            int createIdx = frames.FindIndex(f => HasText(f, "Settings"));
            Assert.True(createIdx > removeIdx,
                $"BnSettingsPage's creates must follow the removes (remove@{removeIdx}, create@{createIdx})");
            Assert.Equal("/settings", FakeShellHost.Route);
            Assert.Equal("/settings", nav.CurrentRoute);
        }
        finally
        {
            TearDown();
        }
    }

    // ── Round trip via both buttons: Settings → then ← Back ──────────────────

    [Fact]
    public void Navigate_RoundTrip_ViaButtons()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            int settingsHandler = ClickHandlerOn(frames[0],
                ContainerOfText(frames[0], "Settings →"));
            frames.Clear();

            Assert.Equal(0, Exports.DispatchEventCore((ulong)settingsHandler, ClickArgs));
            RenderFrame settingsMount = frames[^1];
            Assert.True(HasText(settingsMount, "Settings"));
            int backHandler = ClickHandlerOn(settingsMount,
                ContainerOfText(settingsMount, "← Back"));
            frames.Clear();

            Assert.Equal(0, Exports.DispatchEventCore((ulong)backHandler, ClickArgs));
            _ = InputNode(frames[^1]); // BnDemo is back, fresh
            Assert.Equal("/", FakeShellHost.Route);
            Assert.Equal("/", Nav().CurrentRoute);
        }
        finally
        {
            TearDown();
        }
    }

    // ── NavigateBack: the previous-route slot (Phase 5.1, design §2) ─────────
    //
    // A single previous-route slot: NavigateToAsync records the `from` before
    // each swap; NavigateBackAsync swaps to it and CONSUMES the slot (a back is
    // not itself a forward step — a second consecutive back has no prior and
    // returns false, so the Android shell falls through to default finish). A
    // fresh forward navigation re-arms the slot. Extends to a stack later if
    // needed (design decision). Runs OFF the dispatch lane (host-initiated) —
    // it rides the RunAfterDispatch no-open-batch drain pinned above.

    [Fact]
    public async Task NavigateBack_AtRoot_NoPrior_ReturnsFalse()
    {
        var (_, _) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();

            // Fresh session at "/": nothing to go back to → not handled.
            Assert.False(await nav.NavigateBackAsync());
            Assert.Equal("/", nav.CurrentRoute);
        }
        finally { TearDown(); }
    }

    [Fact]
    public async Task NavigateBack_FromSettings_ReturnsToPrevious_ReturnsTrue()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();
            await nav.NavigateToAsync("/settings"); // records prior "/"
            Assert.Equal("/settings", nav.CurrentRoute);
            frames.Clear();

            bool handled = await nav.NavigateBackAsync();

            Assert.True(handled);
            // The back swapped to BnDemo (its bound input shape returns), "/".
            _ = InputNode(frames[^1]);
            Assert.Equal("/", nav.CurrentRoute);
            Assert.Equal("/", FakeShellHost.Route); // the host heard the back-nav
        }
        finally { TearDown(); }
    }

    [Fact]
    public async Task NavigateBack_ThenBack_SlotExhausted_ReturnsFalse()
    {
        var (_, _) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();
            await nav.NavigateToAsync("/settings"); // / → /settings, prior="/"
            Assert.True(await nav.NavigateBackAsync()); // back → "/", slot consumed
            Assert.Equal("/", nav.CurrentRoute);

            // A second consecutive back has no prior — the shell falls through
            // to default Android finish (false), no ping-pong to /settings.
            Assert.False(await nav.NavigateBackAsync());
            Assert.Equal("/", nav.CurrentRoute);
        }
        finally { TearDown(); }
    }

    [Fact]
    public async Task NavigateBack_ReForwardAfterBack_ReArmsSlot()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();
            await nav.NavigateToAsync("/settings");
            Assert.True(await nav.NavigateBackAsync());  // at "/", slot empty
            Assert.False(await nav.NavigateBackAsync()); // proven exhausted

            // A fresh FORWARD navigation re-arms the slot: back works again.
            await nav.NavigateToAsync("/settings");
            Assert.Equal("/settings", nav.CurrentRoute);
            frames.Clear();
            Assert.True(await nav.NavigateBackAsync());
            _ = InputNode(frames[^1]);
            Assert.Equal("/", nav.CurrentRoute);
        }
        finally { TearDown(); }
    }

    // ── "back" host event → NavigateBack (Phase 5.1, the shared ingress) ─────
    //
    // Predictive-back (Gate 3 Android) and the JVM test both drive the SAME
    // reserved "back" host event: Exports.DispatchHostEventCore intercepts it
    // and routes to NavigateBackAsync. rc 0 = handled / 1 = not handled (at
    // root — the shell finishes) / 2 = fault. The mapping lives in .NET so
    // every shell shares the semantics — pinned here headlessly.

    [Fact]
    public void HostBackEvent_AtRoot_Returns1_NotHandled()
    {
        var (_, _) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));

            // At "/" with no prior: the export reports "not handled" (rc 1) so
            // the shell falls through to default finish — no swap.
            int rc = Exports.DispatchHostEventCore("back", null);

            Assert.Equal(1, rc);
            Assert.Equal("/", Nav().CurrentRoute);
        }
        finally { TearDown(); }
    }

    [Fact]
    public async Task HostBackEvent_FromSettings_Returns0_BnDemoReturns()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();
            await nav.NavigateToAsync("/settings");
            Assert.Equal("/settings", nav.CurrentRoute);
            frames.Clear();

            // The reserved "back" host event navigates back to BnDemo.
            int rc = Exports.DispatchHostEventCore("back", null);

            Assert.Equal(0, rc);
            _ = InputNode(frames[^1]); // BnDemo's shape returned
            Assert.Equal("/", nav.CurrentRoute);
            Assert.Equal("/", FakeShellHost.Route);

            // Slot consumed: a second "back" is not handled (rc 1) — no
            // ping-pong back to /settings.
            Assert.Equal(1, Exports.DispatchHostEventCore("back", null));
            Assert.Equal("/", nav.CurrentRoute);
        }
        finally { TearDown(); }
    }

    [Fact]
    public void HostBackEvent_NoSession_Returns1()
    {
        HostSession.ResetForTests();

        // No mounted session → nothing to go back from (not handled).
        Assert.Equal(1, Exports.DispatchHostEventCore("back", null));
    }

    // ── RouteChanged subscriber isolation (Phase 4.2, DoD #4) ────────────────
    //
    // A THROWING RouteChanged subscriber is an APP-LISTENER bug, not a swap
    // failure: the screen already swapped when the event fires, so the fault
    // must not convert a successful navigation into rc 2 — and it must not
    // starve later subscribers (per-subscriber isolation, the
    // DevHostBridge.RaiseNativeEvent pattern). Faults are stderr-logged.

    [Fact]
    public void RouteChanged_ThrowingSubscriber_SecondStillRuns_SwapReportsSuccess()
    {
        var (_, frames) = StartSession();
        // NON-strict variant (the production posture); the module initializer
        // sets strict for this project, so flip it off and restore in finally.
        HostSession.StrictErrorsForTests = false;
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();

            string? secondSubscriberRoute = null;
            nav.RouteChanged += _ => throw new InvalidOperationException("test: listener bug");
            nav.RouteChanged += r => secondSubscriberRoute = r;

            int settingsHandler = ClickHandlerOn(frames[0],
                ContainerOfText(frames[0], "Settings →"));
            frames.Clear();

            // The navigation SUCCEEDED (screen swapped) — a listener's bug
            // must not fault the dispatch: rc 0, not 2.
            int rc = Exports.DispatchEventCore((ulong)settingsHandler, ClickArgs);
            Assert.Equal(0, rc);

            // The second subscriber still ran, route state tracks the screen.
            Assert.Equal("/settings", secondSubscriberRoute);
            Assert.Equal("/settings", nav.CurrentRoute);
            Assert.Contains(frames, f => HasText(f, "Settings"));
        }
        finally
        {
            HostSession.StrictErrorsForTests = true;
            TearDown();
        }
    }

    [Fact]
    public void RouteChanged_ThrowingSubscriber_StrictMode_StillContained_AndLogged()
    {
        // STRICT variant (the DoD posture decision): StrictErrors surfaces
        // RENDERER contract violations — an app listener's exception is not
        // one (same posture as the dispatch capture window treating handler
        // exceptions as rc 2 only when they fault the dispatch itself). The
        // fault stays contained under strict mode too, but is stderr-logged.
        var (_, frames) = StartSession(); // module initializer: strict is ON
        TextWriter originalError = Console.Error;
        var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();

            string? secondSubscriberRoute = null;
            nav.RouteChanged += _ => throw new InvalidOperationException("test: strict listener bug");
            nav.RouteChanged += r => secondSubscriberRoute = r;

            int settingsHandler = ClickHandlerOn(frames[0],
                ContainerOfText(frames[0], "Settings →"));

            int rc = Exports.DispatchEventCore((ulong)settingsHandler, ClickArgs);
            Assert.Equal(0, rc);
            Assert.Equal("/settings", secondSubscriberRoute);
            Assert.Equal("/settings", nav.CurrentRoute);

            // Contained is not swallowed: the fault reached stderr.
            Assert.Contains("test: strict listener bug", stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            TearDown();
        }
    }

    // ── FAILED swap: route state tracks the SCREEN, not the intent ───────────

    [Fact]
    public void FailedSwap_InsideClickHandler_FaultsDispatch_RouteStateUntouched()
    {
        var (_, frames) = StartSession();
        // Make the swap TARGET's mount throw (test-only registry override —
        // the route table is untouched, so NavigateToAsync still resolves).
        Func<NativeRenderer, int> original = HostSession.ReplaceRegistryEntryForTests(
            "BnSettingsPage",
            _ => throw new InvalidOperationException("test: settings mount refused"));
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Nav();
            bool routeChangedFired = false;
            nav.RouteChanged += _ => routeChangedFired = true;
            int settingsHandler = ClickHandlerOn(frames[0],
                ContainerOfText(frames[0], "Settings →"));

            // The deferred swap's mount throws → the fault joins the 3.2
            // dispatch capture → rc 2. Route state + RouteChanged ride the
            // swap unit (afterSwap), so neither moved: CurrentRoute still
            // agrees with what actually mounted.
            int rc = Exports.DispatchEventCore((ulong)settingsHandler, ClickArgs);
            Assert.Equal(2, rc);
            Assert.Equal("/", nav.CurrentRoute);
            Assert.False(routeChangedFired,
                "RouteChanged must not fire for a failed swap");

            // The documented host-notify-first divergence: the host heard the
            // new route BEFORE the swap failed (NavigateToAsync step 1
            // precedes step 2 by design — the host's @Volatile route is a
            // notification record, not the swap's outcome).
            Assert.Equal("/settings", FakeShellHost.Route);
        }
        finally
        {
            HostSession.ReplaceRegistryEntryForTests("BnSettingsPage", original);
            TearDown();
        }
    }
}
