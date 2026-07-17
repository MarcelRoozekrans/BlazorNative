using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// HostNavigateTests — Phase 9.1 Gate 1 (M9 DoD #3): the reserved "navigate"
// host-event name — the WARM half of notification tap-through. The .NET end of a
// tap over a LIVE app: the shell dispatches host_event("navigate", route) over the
// EXISTING blazornative_host_event export, and DispatchHostEventCore maps it (like
// "back") to NativeNavigationManager.NavigateToAsync — wire vocabulary + a .NET
// branch, NOT an ABI change (no new export, no struct grow). The name→verb mapping
// lives in .NET so every shell (Kotlin/Swift, Gates 2/3) gets identical semantics.
//
// The rc contract (mirroring the "back" branch):
//   0 = navigated to the route (the swap's frames delivered before this returns)
//   1 = not handled (no session, an unknown/foreign route, or an empty payload)
//   2 = the navigation swap faulted (a host callback error) — surfaced for logging
//   3 = malformed: a NULL/empty event NAME (inherited from DispatchHostEventCore)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class HostNavigateTests
{
    private static (NativeRenderer Renderer, List<RenderFrame> Frames) StartSession(string hostRoute = "/")
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

    private static bool HasText(RenderFrame frame, string text)
        => frame.Patches.OfType<ReplaceTextPatch>().Any(p => p.Text == text);

    // ── rc 0: "navigate" re-routes a live session to the target route ─────────

    [Fact]
    public void Navigate_LiveSession_ReRoutesToTheRoute_Returns0()
    {
        var (_, frames) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            INavigationManager nav = Assert.IsAssignableFrom<INavigationManager>(
                HostSession.CurrentNavigationManager);
            string? routeChanged = null;
            nav.RouteChanged += r => routeChanged = r;
            frames.Clear();

            // The warm tap-through: host_event("navigate", "/settings") lands here.
            int rc = Exports.DispatchHostEventCore("navigate", "/settings");

            Assert.Equal(0, rc);
            Assert.Equal("/settings", routeChanged);            // RouteChanged raised to the target
            Assert.Equal("/settings", FakeShellHost.Route);     // the host was notified
            Assert.Contains(frames, f => HasText(f, "Settings")); // the target page mounted
        }
        finally { TearDown(); }
    }

    // ── rc 1: an unknown route is not handled (a stale/foreign deep link) ─────

    [Fact]
    public void Navigate_UnknownRoute_Returns1_NotHandled()
    {
        var (_, _) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));

            int rc = Exports.DispatchHostEventCore("navigate", "/does-not-exist");

            Assert.Equal(1, rc); // the live session cannot honour it; the app stays put
        }
        finally { TearDown(); }
    }

    // ── rc 1: no session → nowhere to navigate from ──────────────────────────

    [Fact]
    public void Navigate_NoSession_Returns1_NotHandled()
    {
        NativeShellBridge.ResetForTests();
        HostSession.ResetForTests();

        int rc = Exports.DispatchHostEventCore("navigate", "/settings");

        Assert.Equal(1, rc);
    }

    // ── rc 1: an empty payload is nothing to act on ──────────────────────────

    [Fact]
    public void Navigate_EmptyRoute_Returns1_NotHandled()
    {
        var (_, _) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Assert.Equal(1, Exports.DispatchHostEventCore("navigate", null));
            Assert.Equal(1, Exports.DispatchHostEventCore("navigate", ""));
        }
        finally { TearDown(); }
    }

    // ── rc 2: the navigation swap faults (a host callback error) ─────────────

    [Fact]
    public void Navigate_HostCallbackFault_Returns2()
    {
        var (_, _) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnSettingsPage")); // routed → _currentRoute set

            // The host tears its callbacks down mid-session: the next Navigate
            // callback throws InvalidOperationException (not an unknown-route
            // ArgumentException), so the swap faults and is surfaced as rc 2.
            NativeShellBridge.ResetForTests();

            TextWriter original = Console.Error;
            using var capture = new StringWriter();
            Console.SetError(capture);
            int rc;
            try { rc = Exports.DispatchHostEventCore("navigate", "/layout"); }
            finally { Console.SetError(original); }

            Assert.Equal(2, rc);
            Assert.Contains("navigate", capture.ToString());
        }
        finally { TearDown(); }
    }

    // ── "navigate" is intercepted BEFORE the multicast (a nav command) ───────

    [Fact]
    public void Navigate_IsIntercepted_DoesNotReachSubscribers()
    {
        var (_, _) = StartSession();
        try
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            var bridge = new NativeShellBridge();
            bool fired = false;
            Action<NativeEvent> handler = _ => fired = true;
            bridge.NativeEvents += handler;
            try
            {
                int rc = Exports.DispatchHostEventCore("navigate", "/settings");
                Assert.Equal(0, rc); // handled as a navigation
                Assert.False(fired, "the reserved 'navigate' event must not reach NativeEvents subscribers");
            }
            finally { bridge.NativeEvents -= handler; }
        }
        finally { TearDown(); }
    }
}
