using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 16.2 — #346: .NET publishes the back state to the shell.
//
// Android stops asking .NET "can you go back?" on the main thread. Instead .NET
// pushes the answer: a BackState notice, host-call op 6, whose flat-JSON args
// are {"canGoBack":"true"|"false"}, sent when the value changes and once for a
// session's first mount. When a back still reaches .NET with nothing to go back
// to, .NET sends BackUnhandled, op 7, so the shell finishes and the press is
// never swallowed. Both ride FaultNotice's fire-and-forget delivery on the
// existing hostCallBegin slot, with no ABI change.
//
// THE HEART OF IT IS ORDER (spec decision 2). The shell applies the back state
// in the same main-thread batch as the frame that shows the page, so the notice
// for a navigation must reach the shell BEFORE that frame. The frames of a swap
// are emitted INSIDE the swap unit, before its afterSwap callback runs, so the
// send cannot live where the route state is recorded: it is sent from the swap
// unit's beforeSwap step, with the value the navigation is about to produce.
// The ordering pins read ONE log in which host calls and frames are interleaved
// in the order the shell received them.
//
// EVERY PIN RUNS IN PRODUCTION MODE, StrictErrors = false. Phase 16.1 found a
// design that passed in strict mode while production stayed silent.
//
// DOES NOT COVER:
//   - what a shell DOES with the notices. Tasks 3 and 4 add the arms and their
//     tests; until then an unknown op takes the shells' unknown-op branch and
//     completes with Error, which is safe and fails SAFE: the shell keeps its
//     current back behaviour;
//   - that the shell applies BackState in the same batch as the frame. .NET can
//     only guarantee the notice arrives first; the batching is Android's, pinned
//     by the instrumented suite;
//   - a back whose work is still running when DispatchHostBack returns: it
//     reports rc 0 and, if it later resolves "not handled", sends nothing.
//     NavigateBackAsync completes synchronously today, so this path is reached
//     only through the HostBackWorkForTests hook. It fails UNSAFE — the press
//     would be swallowed — and is disclosed in the task report;
//   - a back that FAULTS, rc 2: no BackUnhandled is sent. The screen is in an
//     unknown state and the fault is logged;
//   - the notice's delivery machinery itself: FaultNoticeTests pins that an
//     answered notice leaves nothing pending and an unanswered one is dropped
//     after its timeout. Both notices share that code path.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BackStateNoticeTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    private static int BackStateOp => (int)HostCallOp.BackState;
    private static int BackUnhandledOp => (int)HostCallOp.BackUnhandled;

    // ── The ordered log: host calls and frames in ONE sequence ──────────────
    //
    // Host calls are recorded by FakeShellHost's hostCallBegin, synchronously, on the
    // thread that made the call. Frames are recorded by a Frames subscriber, synchronously,
    // on the render thread. Each frame first drains the host calls recorded since the last
    // entry, so every call made before a frame lands before it in the log. The positive
    // control, TheOrderedLog_OrdersBothKindsOfEntry, proves this in both directions.

    private sealed record Entry(int? Op, string? Args, RenderFrame? Frame);

    private sealed class OrderedLog
    {
        private readonly List<Entry> _entries = new();
        private int _callsSeen;

        public void OnFrame(RenderFrame frame)
        {
            lock (_entries)
            {
                DrainCalls();
                _entries.Add(new Entry(null, null, frame));
            }
        }

        private void DrainCalls()
        {
            var calls = FakeShellHost.HostCalls();
            for (; _callsSeen < calls.Count; _callsSeen++)
                _entries.Add(new Entry(calls[_callsSeen].Op, calls[_callsSeen].Args, null));
        }

        public List<Entry> Snapshot()
        {
            lock (_entries)
            {
                DrainCalls();
                return _entries.ToList();
            }
        }
    }

    private static OrderedLog StartSession()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        HostSession.StrictErrorsForTests = false; // production mode
        NativeRenderer renderer = HostSession.EnsureSession();
        Assert.False(renderer.StrictErrors, "the pin must run in production mode");
        var log = new OrderedLog();
        renderer.Frames += (f, _) => { log.OnFrame(f); return ValueTask.CompletedTask; };
        return log;
    }

    private static void TearDown(bool previousStrict)
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
        HostSession.StrictErrorsForTests = previousStrict;
    }

    private static void InSession(Action<OrderedLog> body)
    {
        bool previousStrict = HostSession.StrictErrorsForTests;
        OrderedLog log = StartSession();
        try
        {
            body(log);
        }
        finally
        {
            TearDown(previousStrict);
        }
    }

    private static INavigationManager Nav()
        => Assert.IsAssignableFrom<INavigationManager>(HostSession.CurrentNavigationManager);

    private static void Go(string route) => Nav().NavigateToAsync(route).AsTask().GetAwaiter().GetResult();

    private static bool Back() => Nav().NavigateBackAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>The canGoBack values of every BackState notice, in order.</summary>
    private static List<bool> BackStates()
        => FakeShellHost.HostCalls().Where(c => c.Op == BackStateOp)
            .Select(c => CanGoBackOf(c.Args)).ToList();

    private static bool CanGoBackOf(string? args)
    {
        Dictionary<string, string> parsed = NativeShellBridge.ParseFlatJsonObject(args);
        Assert.True(parsed.TryGetValue("canGoBack", out string? value),
            $"a BackState notice carried no canGoBack key: {args}");
        Assert.True(value is "true" or "false",
            $"canGoBack must be the string true or false, got '{value}' in {args}");
        return value == "true";
    }

    private static bool HasText(RenderFrame frame, string text)
        => frame.Patches.OfType<ReplaceTextPatch>().Any(p => p.Text == text);

    private static bool IsBnDemoMount(RenderFrame frame)
        => frame.Patches.OfType<CreateNodePatch>().Any(p => p.NodeType == "input");

    private static string Describe(List<Entry> log)
        => string.Join(", ", log.Select(e => e.Frame is null ? $"op{e.Op}" : "frame"));

    /// <summary>The index of the first frame matching <paramref name="shows"/>. Rule 2 and
    /// Rule 4: a page that never appears reds, rather than making the order check vacuous.</summary>
    private static int FirstFrame(List<Entry> log, Func<RenderFrame, bool> shows, string page)
    {
        int index = log.FindIndex(e => e.Frame is { } f && shows(f));
        Assert.True(index >= 0,
            $"no frame showing {page} was logged, so the order cannot be checked. The page's "
            + $"content changed or the frame subscriber stopped seeing it; re-point the "
            + $"predicate deliberately. Log: [{Describe(log)}]");
        return index;
    }

    /// <summary>The index of the LAST BackState notice with <paramref name="value"/> before
    /// <paramref name="before"/>, or -1.</summary>
    private static int LastBackStateBefore(List<Entry> log, bool value, int before)
        => log.Take(before).ToList().FindLastIndex(e => e.Op == BackStateOp && CanGoBackOf(e.Args) == value);

    // ── The value, and when it is sent ──────────────────────────────────────

    [Fact]
    public void TheFirstMount_SendsBackStateFalse()
    {
        InSession(log =>
        {
            Assert.Empty(BackStates()); // nothing before the mount
            Assert.Equal(0, HostSession.TryMount("BnDemo"));

            Assert.Equal([false], BackStates());
            // And it precedes the first frame, so the shell never shows a page with a
            // stale back state from a previous session.
            List<Entry> entries = log.Snapshot();
            int page = FirstFrame(entries, IsBnDemoMount, "BnDemo");
            Assert.True(LastBackStateBefore(entries, false, page) >= 0,
                $"the first mount's BackState must precede its frame. Log: [{Describe(entries)}]");
        });
    }

    [Fact]
    public void NavigatingAway_SendsBackStateTrue()
    {
        InSession(_ =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Go("/settings");
            Assert.Equal([false, true], BackStates());
        });
    }

    [Fact]
    public void BackToRoot_SendsBackStateFalse()
    {
        InSession(_ =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Go("/settings");
            Assert.True(Back(), "the back must have been handled");
            Assert.Equal("/", Nav().CurrentRoute);
            Assert.Equal([false, true, false], BackStates());
        });
    }

    [Fact]
    public void AnUnchangedValue_SendsNoNotice()
    {
        InSession(log =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Go("/settings");
            int before = BackStates().Count;
            Assert.Equal(2, before); // false for the mount, true for leaving the root

            Go("/layout"); // non-root to non-root: canGoBack stays true

            // Rule 3 control: the navigation really happened, so "no notice" is observed
            // on a real state change rather than on a navigation that never ran.
            Assert.Equal("/layout", Nav().CurrentRoute);
            Assert.Equal(before, BackStates().Count);
        });
    }

    [Fact]
    public void TheBackStateArgs_CarryExactlyTheKeyCanGoBack()
    {
        InSession(_ =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Go("/settings");

            var notices = FakeShellHost.HostCalls().Where(c => c.Op == BackStateOp).ToList();
            Assert.Equal(2, notices.Count);
            foreach (var notice in notices)
            {
                Dictionary<string, string> args = NativeShellBridge.ParseFlatJsonObject(notice.Args);
                Assert.Equal(["canGoBack"], args.Keys.ToArray());
            }
            Assert.Equal("""{"canGoBack":"false"}""", notices[0].Args);
            Assert.Equal("""{"canGoBack":"true"}""", notices[1].Args);
        });
    }

    // ── The order: the notice precedes the frame that shows the page ────────

    [Fact]
    public void TheOrderedLog_OrdersBothKindsOfEntry()
    {
        // Rule 3 positive control for the ordering pins: a call made before a frame is
        // logged before it, and a call made after a frame is logged after it. Driven by
        // hand, with a bare SwapRoot that sends no notice of its own.
        InSession(log =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            int baseline = log.Snapshot().Count;

            NativeShellBridge.SendBackState(true);
            HostSession.SwapRoot("BnSettingsPage");
            NativeShellBridge.SendBackUnhandled();

            List<Entry> entries = log.Snapshot().Skip(baseline).ToList();
            int sent = entries.FindIndex(e => e.Op == BackStateOp);
            int page = FirstFrame(entries, f => HasText(f, "Settings"), "BnSettingsPage");
            int after = entries.FindIndex(e => e.Op == BackUnhandledOp);
            Assert.True(sent >= 0 && after >= 0, $"both hand-sent calls must be logged: [{Describe(entries)}]");
            Assert.True(sent < page, $"a call made before a frame must be logged before it: [{Describe(entries)}]");
            Assert.True(after > page, $"a call made after a frame must be logged after it: [{Describe(entries)}]");
        });
    }

    [Fact]
    public void TheBackStateNotice_PrecedesTheFrameThatShowsThePage()
    {
        InSession(log =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Go("/settings");

            List<Entry> entries = log.Snapshot();
            int page = FirstFrame(entries, f => HasText(f, "Settings"), "BnSettingsPage");
            Assert.True(LastBackStateBefore(entries, true, page) >= 0,
                "the BackState(true) for the navigation to /settings reached the shell AFTER the "
                + "frame that shows the page. The shell would show the new page with the back "
                + "callback still disabled, and a back pressed in that window finishes the app: "
                + $"the stale window spec decision 2 exists to close. Log: [{Describe(entries)}]");
        });
    }

    [Fact]
    public void TheBackStateNotice_PrecedesTheFrame_WhenGoingBack()
    {
        InSession(log =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Go("/settings");
            int baseline = log.Snapshot().Count;

            Assert.Equal(0, Exports.DispatchHostEventCore("back", null));

            List<Entry> entries = log.Snapshot().Skip(baseline).ToList();
            int page = FirstFrame(entries, IsBnDemoMount, "BnDemo");
            Assert.True(LastBackStateBefore(entries, false, page) >= 0,
                "the BackState(false) for a back to the root reached the shell after the frame that "
                + $"shows the root. Log: [{Describe(entries)}]");
        });
    }

    [Fact]
    public void TheBackStateNotice_PrecedesTheFrame_ForANavigationFromAClickHandler()
    {
        // The deferred path: a navigation inside a handler swaps at the dispatch unwind.
        InSession(log =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            RenderFrame demo = log.Snapshot().Select(e => e.Frame).OfType<RenderFrame>().First(IsBnDemoMount);
            var label = Assert.Single(demo.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "Settings →");
            int button = Assert.IsType<int>(Assert.Single(demo.Patches.OfType<CreateNodePatch>(),
                p => p.NodeId == label.NodeId).ParentId);
            int handler = Assert.Single(demo.Patches.OfType<AttachEventPatch>(),
                p => p.NodeId == button && p.EventName == "click").HandlerId;
            int baseline = log.Snapshot().Count;

            Assert.Equal(0, Exports.DispatchEventCore((ulong)handler, ClickArgs));

            Assert.Equal("/settings", Nav().CurrentRoute);
            List<Entry> entries = log.Snapshot().Skip(baseline).ToList();
            int page = FirstFrame(entries, f => HasText(f, "Settings"), "BnSettingsPage");
            Assert.True(LastBackStateBefore(entries, true, page) >= 0,
                "a navigation from a click handler sent its BackState after the frame that shows "
                + $"the page. Log: [{Describe(entries)}]");
        });
    }

    [Fact]
    public void AFailedSwap_ResendsTheBackStateTheScreenHas()
    {
        // beforeSwap sends the value the navigation is ABOUT to produce. When the swap
        // throws, the route state is untouched, so the shell must be told the real value.
        InSession(_ =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            var original = HostSession.ReplaceRegistryEntryForTests("BnSettingsPage",
                _ => throw new InvalidOperationException("test: the target page's mount throws"));
            try
            {
                Assert.ThrowsAny<Exception>(() => Go("/settings"));
            }
            finally
            {
                HostSession.ReplaceRegistryEntryForTests("BnSettingsPage", original);
            }

            // Rule 3 control: the failed navigation really did announce true first.
            Assert.Equal([false, true, false], BackStates());
            Assert.Equal("/", Nav().CurrentRoute);
        });
    }

    // ── BackUnhandled: a back .NET cannot handle makes the shell finish ─────

    [Fact]
    public void BackAtRoot_SendsBackUnhandled_AndReturns1()
    {
        InSession(_ =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));

            Assert.Equal(1, Exports.DispatchHostEventCore("back", null));

            var unhandled = Assert.Single(FakeShellHost.HostCalls(), c => c.Op == BackUnhandledOp);
            Assert.Equal("{}", unhandled.Args);
        });
    }

    [Fact]
    public void AHandledBack_SendsNoBackUnhandled()
    {
        // Rule 3 negative: BackUnhandled is not simply sent for every back.
        InSession(_ =>
        {
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
            Go("/settings");

            Assert.Equal(0, Exports.DispatchHostEventCore("back", null));

            Assert.DoesNotContain(FakeShellHost.HostCalls(), c => c.Op == BackUnhandledOp);
        });
    }

    [Fact]
    public void BackWithNoSession_SendsBackUnhandled_AndReturns1()
    {
        // rc 1 means "the shell finishes". Under 16.2 the shell no longer reads the rc, so
        // every rc 1 must also be a BackUnhandled, or the press is swallowed.
        bool previousStrict = HostSession.StrictErrorsForTests;
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        try
        {
            Assert.Null(HostSession.CurrentNavigationManager);
            Assert.Equal(1, Exports.DispatchHostEventCore("back", null));
            Assert.Single(FakeShellHost.HostCalls(), c => c.Op == BackUnhandledOp);
        }
        finally
        {
            TearDown(previousStrict);
        }
    }

    [Fact]
    public void TheNotices_NeverThrow_WithNoBridgeRegistered()
    {
        NativeShellBridge.ResetForTests();
        NativeShellBridge.SendBackState(true);
        NativeShellBridge.SendBackUnhandled();
        Assert.Equal(0, NativeShellBridge.PendingHostCallCountForTests);
    }
}
