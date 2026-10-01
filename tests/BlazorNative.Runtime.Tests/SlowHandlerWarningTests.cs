using System.Diagnostics;
using System.Text.RegularExpressions;
using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 16.3 (#9) — the slow-handler warning.
//
// Since 16.1 and 16.2 an async handler frees the dispatch lane when it yields,
// and the main thread never waits on .NET. What can still starve the lane is a
// handler whose SYNCHRONOUS part is slow: the render thread runs it while the
// shell's dispatch lane waits, and every later event queues behind it. The
// phase record measured the cost as one-for-one: an event posted behind a
// handler whose synchronous part takes N ms waits about N ms.
//
// The renderer times that part — from opening the dispatch's DispatchScope to
// closing it — and the host-event arms, which run outside any scope, are timed
// in Exports around their render-thread work. Over
// NativeRenderer.SlowHandlerBudget it logs ONE BnLog.Warn per call site, keyed by
// the handler's owner method, or per event name for a host-event arm, per session. BnLog.DefaultLevel is Warn in
// every build, so that once-per-key rule is what keeps Release quiet.
//
// Every pin runs in PRODUCTION mode (StrictErrors = false) and captures BnLog at
// BnLog.DefaultLevel, so it sees exactly what a Release app logs. "Slow" is a
// fake clock the handler advances, set on the renderer under test through
// NativeRenderer.TimestampForTests: no pin waits for the budget, and every slow
// case sits at five times the budget, every quick one at a tenth of it, so none
// asserts near the boundary. One pin leaves the clock alone and really sleeps,
// at five times the budget, to prove the Stopwatch path.
//
// DOES NOT COVER:
//   - a host event with NO session: its arm runs on the calling thread, there is
//     no render thread to starve and no session to key the warning to, so it is
//     not timed. Fails safe: no warning, nothing blocked on the render thread;
//   - the time a post waits in the render-thread queue BEFORE its synchronous
//     part starts. That wait is the symptom, charged to the handler ahead of it,
//     never to the one waiting;
//   - the async remainder of a handler after its first await: it holds nothing;
//   - the FALLBACK key's collisions. A dispatch is keyed by its handler's owner,
//     the method its delegate runs, so a lambda that gets a new handler id on every
//     render still warns once, and BnButton or a BnView's ChildContent forwarding
//     many apps' handlers from one line warns for each. When no delegate can be
//     reached, or its method cannot be resolved, the key falls back to the tree
//     owner: component type, frame sequence and event. Under that fallback a
//     RenderFragment from a parent renders into the CHILD's tree with the PARENT's
//     sequence numbers, so handlers can merge. Fails quiet. The fallback is not
//     reached by any pin here; whether the NativeAOT build resolves the method is
//     measured on the JVM lane, by SlowHandlerProbeTest. The 32-warning cap per
//     session is the other quiet path, and it is pinned;
//   - a handler whose method is the FRAMEWORK's: Blazor's own (every @bind runs
//     Blazor's binder lambda), System's, or BlazorNative.Components'. Its owner key
//     is combined with the tree-owner key, so @bind handlers stay apart by call
//     site and the warning names the component holding the call site. When that
//     component is itself a BlazorNative.Components wrapper, the call site is the
//     wrapper's own line, so every instance in an app merges into one warning
//     naming the wrapper, not the app's handler. Exactly: BnInput's change
//     (HandleChange, sequence 102; pinned as a known limit), and by the same
//     shape BnCheckbox, BnPicker, BnSlider and BnSwitch change, and BnModal's
//     dismiss click (HandleDismissRequest). Fails quiet: one warning per wrapper
//     type per session. BnButton, BnImage's onerror and BnScroll's onscroll
//     forward the app's delegate itself and are keyed by the app's method;
//   - the navigate and safeAreaChanged arms by name. They share the timed helper
//     with back and the lifecycle multicast, which are pinned;
//   - the shells: this is the .NET side only.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class SlowHandlerWarningTests
{
    /// <summary>A "slow" synchronous part: five times the budget.</summary>
    private static readonly int SlowMs = 5 * NativeRenderer.SlowHandlerBudget;

    /// <summary>A "quick" synchronous part: a tenth of the budget.</summary>
    private static readonly int QuickMs = NativeRenderer.SlowHandlerBudget / 10;

    private const string PayloadMarker = "PAYLOAD-MARKER-16-3-c4e1";

    private static string Click => /*lang=json*/ """{"name":"click"}""";

    private static string Change(string payload) => $$"""{"name":"change","payload":"{{payload}}"}""";

    // ── Session and capture ─────────────────────────────────────────────────

    private sealed class Session : IDisposable
    {
        private readonly Func<NativeRenderer, int> _demo;
        private readonly Action<BnLogLevel, string, string>? _sink = BnLog.Sink;
        private readonly BnLogLevel _level = BnLog.Level;
        private readonly List<RenderFrame> _frames = new();
        private readonly List<(BnLogLevel Level, string Line)> _log = new();

        public NativeRenderer Renderer { get; private set; } = null!;

        public Session(bool fakeClock = true)
        {
            FakeShellHost.Reset();
            NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
            HostSession.ResetForTests();
            SlowProbe.ResetStatics();
            FakeClock.Reset();
            _demo = HostSession.ReplaceRegistryEntryForTests("BnDemo", r => r.Mount<SlowProbe>());
            Start(fakeClock);
            // What a Release app logs: the default level, every line.
            BnLog.Level = BnLog.DefaultLevel;
            BnLog.Sink = (level, category, message) => { lock (_log) _log.Add((level, $"{category}: {message}")); };
        }

        /// <summary>Builds a session and mounts the probe, in production mode.</summary>
        public void Start(bool fakeClock = true)
        {
            Renderer = HostSession.EnsureSession();
            Renderer.StrictErrors = false;
            if (fakeClock)
                Renderer.TimestampForTests = FakeClock.Now;
            lock (_frames) _frames.Clear();
            Renderer.Frames += (f, _) => { lock (_frames) _frames.Add(f); return ValueTask.CompletedTask; };
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
        }

        public string[] SlowLines()
        {
            lock (_log)
                return _log.Where(e => e.Line.Contains(NativeRenderer.SlowHandlerLogLabel)).Select(e => e.Line).ToArray();
        }

        public (BnLogLevel Level, string Line)[] Log() { lock (_log) return _log.ToArray(); }

        /// <summary>The handler id of the element carrying <paramref name="eventName"/>
        /// whose text child is <paramref name="label"/>, from the latest frame that
        /// created it; an input has no text child, so it is found by its event alone.</summary>
        public int Handler(string label, string eventName = "click")
        {
            List<RenderFrame> all;
            lock (_frames) all = _frames.ToList();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                RenderFrame f = all[i];
                if (eventName == "change")
                {
                    AttachEventPatch? change = f.Patches.OfType<AttachEventPatch>().FirstOrDefault(p => p.EventName == "change");
                    if (change is not null)
                        return change.HandlerId;
                    continue;
                }
                ReplaceTextPatch? text = f.Patches.OfType<ReplaceTextPatch>().FirstOrDefault(p => p.Text == label);
                if (text is null)
                    continue;
                CreateNodePatch? create = f.Patches.OfType<CreateNodePatch>().FirstOrDefault(p => p.NodeId == text.NodeId);
                if (create?.ParentId is not int button)
                    continue;
                AttachEventPatch? attach = f.Patches.OfType<AttachEventPatch>()
                    .FirstOrDefault(p => p.NodeId == button && p.EventName == eventName);
                if (attach is not null)
                    return attach.HandlerId;
            }
            throw new Xunit.Sdk.XunitException(
                $"no frame created a '{eventName}' handler for '{label}'. SlowProbe moved or was "
                + "relabelled; re-point this pin deliberately rather than deleting it.");
        }

        /// <summary>The live <paramref name="eventName"/> handler on the element whose
        /// placeholder is <paramref name="placeholder"/>.</summary>
        public int HandlerByPlaceholder(string placeholder, string eventName)
        {
            List<RenderFrame> all;
            lock (_frames) all = _frames.ToList();
            int? node = all.SelectMany(f => f.Patches).OfType<UpdatePropPatch>()
                .FirstOrDefault(p => p.Name == "placeholder" && p.Value == placeholder)?.NodeId;
            if (node is not int n)
                throw new Xunit.Sdk.XunitException(
                    $"no frame set placeholder '{placeholder}'. SlowProbe moved; re-point this pin deliberately.");
            for (int i = all.Count - 1; i >= 0; i--)
            {
                AttachEventPatch? attach = all[i].Patches.OfType<AttachEventPatch>()
                    .LastOrDefault(p => p.NodeId == n && p.EventName == eventName);
                if (attach is not null)
                    return attach.HandlerId;
            }
            throw new Xunit.Sdk.XunitException($"node {n} ('{placeholder}') never had a '{eventName}' handler.");
        }

        /// <summary>The node id of the button whose text is <paramref name="label"/>.</summary>
        public int NodeOf(string label)
        {
            List<RenderFrame> all;
            lock (_frames) all = _frames.ToList();
            foreach (RenderFrame f in all)
            {
                ReplaceTextPatch? text = f.Patches.OfType<ReplaceTextPatch>().FirstOrDefault(p => p.Text == label);
                if (text is null)
                    continue;
                if (f.Patches.OfType<CreateNodePatch>().FirstOrDefault(p => p.NodeId == text.NodeId)?.ParentId is int button)
                    return button;
            }
            throw new Xunit.Sdk.XunitException(
                $"no frame created a button labelled '{label}'. SlowProbe moved or was relabelled; "
                + "re-point this pin deliberately rather than deleting it.");
        }

        /// <summary>The LIVE click handler on <paramref name="node"/>: the latest attach,
        /// which a re-render's SetAttribute replaces when the delegate changed.</summary>
        public int LatestHandlerOn(int node)
        {
            List<RenderFrame> all;
            lock (_frames) all = _frames.ToList();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                AttachEventPatch? attach = all[i].Patches.OfType<AttachEventPatch>()
                    .LastOrDefault(p => p.NodeId == node && p.EventName == "click");
                if (attach is not null)
                    return attach.HandlerId;
            }
            throw new Xunit.Sdk.XunitException($"node {node} never had a click handler attached.");
        }

        public void Dispose()
        {
            BnLog.Sink = _sink;
            BnLog.Level = _level;
            Exports.HostBackWorkForTests = null;
            HostSession.ReplaceRegistryEntryForTests("BnDemo", _demo);
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
            SlowProbe.ResetStatics();
        }
    }

    /// <summary>A clock only the probe's handlers move, in Stopwatch ticks.</summary>
    private static class FakeClock
    {
        private static long s_ticks;
        private static int s_reads;

        public static long Now() { Interlocked.Increment(ref s_reads); return Interlocked.Read(ref s_ticks); }

        public static void Advance(int ms) => Interlocked.Add(ref s_ticks, ms * Stopwatch.Frequency / 1000);

        public static int Reads => Volatile.Read(ref s_reads);

        public static void Reset() { Interlocked.Exchange(ref s_ticks, 1_000_000); Interlocked.Exchange(ref s_reads, 0); }
    }

    private static int Dispatch(int handlerId, string args) => Exports.DispatchEventCore((ulong)handlerId, args);

    // ── Rule 3: the capture sees a Warn at the production level ──────────────

    [Fact]
    public void TheLogCapture_SeesAWarn_AtTheDefaultLevel_PositiveControl()
    {
        using var s = new Session();
        Assert.Equal(BnLogLevel.Warn, BnLog.DefaultLevel);

        BnLog.Warn("probe", $"{NativeRenderer.SlowHandlerLogLabel}: capture control");
        BnLog.Debug("probe", $"{NativeRenderer.SlowHandlerLogLabel}: below the default level");

        var line = Assert.Single(s.SlowLines());
        Assert.Contains("capture control", line);
        Assert.Contains(s.Log(), e => e.Level == BnLogLevel.Warn && e.Line.Contains("capture control"));
    }

    // ── The warning ─────────────────────────────────────────────────────────

    [Fact]
    public void AHandlerOverBudget_LogsExactlyOneWarn_NamingItsId_ItsEvent_AndTheMilliseconds()
    {
        using var s = new Session();
        int slowA = s.Handler("slow-a");

        Assert.Equal(0, Dispatch(slowA, Click));

        Assert.Equal(1, SlowProbe.RunsOf("slow-a")); // anchor: the handler ran
        var line = Assert.Single(s.SlowLines());
        Assert.Contains(s.Log(), e => e.Level == BnLogLevel.Warn && e.Line == line);
        Assert.Contains($"handler {slowA}", line);
        Assert.Contains("'click'", line);
        Assert.Contains($"{SlowMs} ms", line);
        Assert.Contains($"{NativeRenderer.SlowHandlerBudget} ms budget", line);
        Assert.Contains("Warned once per call site per session", line);
    }

    [Fact]
    public void AHandlerUnderBudget_LogsNoWarn()
    {
        using var s = new Session();
        int quick = s.Handler("quick");
        int readsBefore = FakeClock.Reads;

        Assert.Equal(0, Dispatch(quick, Click));

        // Anchors: the handler ran, and the dispatch read the renderer's clock, so
        // the timer was on the path and simply found nothing to report.
        Assert.Equal(1, SlowProbe.RunsOf("quick"));
        Assert.True(FakeClock.Reads - readsBefore >= 2,
            $"the dispatch read the fake clock {FakeClock.Reads - readsBefore} times; a timed "
            + "synchronous part reads it at least twice. The timing moved off DispatchSyncPart, "
            + "or TimestampForTests is no longer its clock, so this pin checked nothing.");
        Assert.Empty(s.SlowLines());
    }

    [Fact]
    public void TheSameHandlerSlowTwice_WarnsOnce_AndADifferentSlowHandler_WarnsItsOwn()
    {
        using var s = new Session();
        int slowA = s.Handler("slow-a");
        Assert.Equal(0, Dispatch(slowA, Click));
        Assert.True(s.Handler("slow-a") == slowA,
            "slow-a's handler id changed across its own re-render, so the second dispatch below "
            + "would not be the same handler. Re-point this pin deliberately.");
        Assert.Equal(0, Dispatch(slowA, Click));

        int slowB = s.Handler("slow-b");
        Assert.NotEqual(slowA, slowB);
        Assert.Equal(0, Dispatch(slowB, Click));

        Assert.Equal(2, SlowProbe.RunsOf("slow-a")); // anchor: both slow runs happened
        Assert.Equal(1, SlowProbe.RunsOf("slow-b"));
        string[] lines = s.SlowLines();
        Assert.True(lines.Length == 2,
            $"expected one warning for slow-a and one for slow-b, got {lines.Length}:\n{string.Join("\n", lines)}");
        Assert.Single(lines, l => l.Contains($"handler {slowA} "));
        Assert.Single(lines, l => l.Contains($"handler {slowB} "));
    }

    [Fact]
    public void TheWarn_NeverCarriesThePayload()
    {
        using var s = new Session();
        int change = s.Handler("", "change");

        Assert.Equal(0, Dispatch(change, Change(PayloadMarker)));

        // Anchor: the payload reached the handler, so it was there to leak.
        Assert.Equal(PayloadMarker, SlowProbe.LastPayload);
        var line = Assert.Single(s.SlowLines());
        Assert.Contains("'change'", line);
        Assert.DoesNotContain(PayloadMarker, line);
        Assert.DoesNotContain(s.Log(), e => e.Line.Contains(PayloadMarker));
    }

    [Fact]
    public void ASlowBackArm_IsWarned_KeyedByEventName_Once()
    {
        using var s = new Session();
        // Every clock read records its thread: the arm must be timed ON the render
        // thread, from the moment its work starts, so the time its post waited in the
        // queue is never charged to it. A read on the caller would include that wait.
        var readThreads = new List<int>();
        s.Renderer.TimestampForTests = () =>
        {
            lock (readThreads) readThreads.Add(Environment.CurrentManagedThreadId);
            return FakeClock.Now();
        };
        int backs = 0;
        Exports.HostBackWorkForTests = () =>
        {
            backs++;
            FakeClock.Advance(SlowMs);
            return Task.FromResult(true);
        };

        Assert.Equal(0, Exports.DispatchHostEventCore(BnHostEvents.Back, null));
        Assert.Equal(0, Exports.DispatchHostEventCore(BnHostEvents.Back, null));

        Assert.Equal(2, backs); // anchor: the arm's work ran, twice
        int[] reads;
        lock (readThreads) reads = readThreads.ToArray();
        Assert.True(reads.Length >= 4,
            $"the two back arms read the renderer's clock {reads.Length} times; timing each "
            + "reads it at least twice. The arm timing moved off TimestampForTests, so the "
            + "thread assertion below would check nothing.");
        int owner = s.Renderer.RenderThreadId;
        Assert.True(reads.All(t => t == owner),
            $"the back arm read its clock on threads [{string.Join(", ", reads)}], but the render "
            + $"thread is {owner}. A read off the render thread times the queue wait too, which "
            + "belongs to whatever ran ahead of the arm.");
        var line = Assert.Single(s.SlowLines());
        Assert.Contains($"host event '{BnHostEvents.Back}'", line);
        Assert.Contains($"{SlowMs} ms", line);
    }

    [Fact]
    public void ASlowLifecycleArm_IsWarned_KeyedByEventName_AndEachNameWarnsItsOwn()
    {
        using var s = new Session();
        SlowProbe.SlowLifecycle = true;

        Assert.Equal(0, Exports.DispatchHostEventCore("onPause", null));
        Assert.Equal(0, Exports.DispatchHostEventCore("onPause", null));
        Assert.Equal(0, Exports.DispatchHostEventCore("onResume", null));

        Assert.Equal(3, SlowProbe.RunsOf("lifecycle")); // anchor: the subscriber ran
        string[] lines = s.SlowLines();
        Assert.True(lines.Length == 2,
            $"expected one warning for onPause and one for onResume, got {lines.Length}:\n{string.Join("\n", lines)}");
        Assert.Single(lines, l => l.Contains("host event 'onPause'"));
        Assert.Single(lines, l => l.Contains("host event 'onResume'"));
    }

    [Fact]
    public void TheWarnedSet_ResetsWithTheSession()
    {
        using var s = new Session();
        Assert.Equal(0, Dispatch(s.Handler("slow-a"), Click));
        Assert.Single(s.SlowLines());

        HostSession.ResetForTests();
        s.Start();
        Assert.Equal(0, Dispatch(s.Handler("slow-a"), Click));

        Assert.Equal(2, SlowProbe.RunsOf("slow-a")); // anchor: the handler ran in both sessions
        string[] after = s.SlowLines();
        Assert.True(after.Length == 2,
            $"the same slow handler warned {after.Length} times across a session reset, not 2. "
            + "The warned set and the warning count belong to the session's renderer: a set that "
            + "survives the reset (a static field) silences the second session's first warning.");
    }

    [Fact]
    public void ACapturingLambda_ClickedThreeTimesAcrossReRenders_WarnsOnce()
    {
        using var s = new Session();
        int node = s.NodeOf("item-0");
        var ids = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            int id = s.LatestHandlerOn(node);
            ids.Add(id);
            Assert.Equal(0, Dispatch(id, Click));
        }

        // Anchors: every click ran, and Blazor really did hand the lambda a NEW handler
        // id on each render. Without that, keying by handler id would pass too.
        Assert.Equal(3, SlowProbe.RunsOf("select"));
        Assert.True(ids.Distinct().Count() == 3,
            $"the capturing lambda kept handler ids [{string.Join(", ", ids)}] across its "
            + "re-renders, so this pin no longer separates a call-site key from a handler-id "
            + "key. Has SlowProbe's item button stopped capturing per-render state?");
        var line = Assert.Single(s.SlowLines());
        Assert.Contains($"handler {ids[0]} 'click'", line);
        Assert.Contains(nameof(SlowProbe), line);
    }

    [Fact]
    public void TheCallSiteMap_StaysFlat_AcrossFiftyReRendersOfCapturingLambdas()
    {
        using var s = new Session();
        int mounted = s.Renderer.HandlerCallSiteCountForTests;
        // Anchor: the mount recorded the probe's handlers, so there is a map to watch.
        Assert.True(mounted >= 10,
            $"the mount recorded {mounted} call sites; SlowProbe attaches at least 10 handlers. "
            + "The call-site map is no longer written at the AttachEvent site, so this pin would "
            + "check nothing.");

        int node = s.NodeOf("item-0");
        var ids = new HashSet<int>();
        for (int i = 0; i < 50; i++)
        {
            int id = s.LatestHandlerOn(node);
            ids.Add(id);
            Assert.Equal(0, Dispatch(id, Click));
        }

        // Anchor: every render really did hand the capturing lambdas new ids, so a map
        // that never forgot a disposed id would have grown by several per render.
        Assert.Equal(50, SlowProbe.RunsOf("select"));
        Assert.True(ids.Count == 50,
            $"item-0 kept {50 - ids.Count + 1} ids across 50 re-renders; it must get a new one "
            + "on each, or this pin cannot see growth.");
        int after = s.Renderer.HandlerCallSiteCountForTests;
        Assert.True(after == mounted,
            $"the call-site map held {mounted} entries after the mount and {after} after 50 "
            + "re-renders. It must hold the live handlers only: disposed handler ids are pruned "
            + "per batch, or the map grows by one per capturing lambda per render.");
    }

    [Fact]
    public void TwoCallSites_WarnTwice_AndTwoItemsOfOneCallSite_WarnOnce()
    {
        using var s = new Session();
        Assert.Equal(0, Dispatch(s.LatestHandlerOn(s.NodeOf("item-0")), Click));
        Assert.Equal(0, Dispatch(s.LatestHandlerOn(s.NodeOf("item-1")), Click));
        Assert.Equal(0, Dispatch(s.LatestHandlerOn(s.NodeOf("other")), Click));

        Assert.Equal(3, SlowProbe.RunsOf("select")); // anchor: all three ran
        string[] lines = s.SlowLines();
        Assert.True(lines.Length == 2,
            $"expected one warning for the item loop's call site and one for 'other', got "
            + $"{lines.Length}:\n{string.Join("\n", lines)}");
    }

    [Fact]
    public void TwoBnButtons_WithDifferentSlowHandlers_WarnTwice_NamingTheAppsMethods_NeverBnButton()
    {
        using var s = new Session();
        int one = s.Handler("bn-one");
        int two = s.Handler("bn-two");
        Assert.NotEqual(one, two);

        Assert.Equal(0, Dispatch(one, Click));
        Assert.Equal(0, Dispatch(two, Click));

        Assert.Equal(1, SlowProbe.RunsOf("bn-one")); // anchor: both app handlers ran
        Assert.Equal(1, SlowProbe.RunsOf("bn-two"));
        string[] lines = s.SlowLines();
        Assert.True(lines.Length == 2,
            $"two BnButtons with different slow OnClick handlers gave {lines.Length} warnings, "
            + $"not 2. BnButton forwards every app's OnClick from one line, so a key built from "
            + $"the tree that holds the attribute merges them:\n{string.Join("\n", lines)}");
        Assert.Single(lines, l => l.Contains("BnSlowOne"));
        Assert.Single(lines, l => l.Contains("BnSlowTwo"));
        Assert.DoesNotContain(lines, l => l.Contains(typeof(BnButton).FullName!));
    }

    [Fact]
    public void TwoPageTypes_WithSlowButtonsInBnViewChildContent_AtTheSameSequence_WarnTwice()
    {
        using var s = new Session();
        int a = s.Handler("page-a");
        int b = s.Handler("page-b");

        Assert.Equal(0, Dispatch(a, Click));
        Assert.Equal(0, Dispatch(b, Click));

        Assert.Equal(1, SlowProbe.RunsOf("page-a")); // anchor: both pages' handlers ran
        Assert.Equal(1, SlowProbe.RunsOf("page-b"));
        string[] lines = s.SlowLines();
        Assert.True(lines.Length == 2,
            $"two pages' slow buttons inside BnView ChildContent gave {lines.Length} warnings, not "
            + $"2. The fragments render into BnView's tree with the pages' sequence numbers, so a "
            + $"tree-owner key merges them:\n{string.Join("\n", lines)}");
        Assert.Single(lines, l => l.Contains(nameof(ChildPageA)));
        Assert.Single(lines, l => l.Contains(nameof(ChildPageB)));
        Assert.DoesNotContain(lines, l => l.Contains(typeof(BnView).FullName!));
    }

    [Fact]
    public void TwoBindChangeHandlers_OfTheSameType_WarnTwice_NeverNamingBlazorInternals()
    {
        using var s = new Session();
        // Each binder is a new closure on every render, so each handler id is read just
        // before its own dispatch: the first dispatch's re-render replaces both.
        Assert.Equal(0, Dispatch(s.HandlerByPlaceholder("bind-a", "change"), Change("x")));
        Assert.Equal(0, Dispatch(s.HandlerByPlaceholder("bind-b", "change"), Change("y")));

        Assert.Equal(1, SlowProbe.RunsOf("bind-a")); // anchor: both binders' setters ran
        Assert.Equal(1, SlowProbe.RunsOf("bind-b"));
        string[] lines = s.SlowLines();
        // Every @bind of one value type runs Blazor's OWN binder lambda, so an owner key
        // alone merges every such handler in an app, and names Blazor's internals.
        Assert.True(lines.Length == 2,
            $"two @bind change handlers of one type gave {lines.Length} warnings, not 2. A handler "
            + $"whose method is the framework's must also be keyed by its call site:\n{string.Join("\n", lines)}");
        Assert.DoesNotContain(lines, l => l.Contains("EventCallbackFactoryBinderExtensions"));
        Assert.All(lines, l => Assert.Contains(nameof(SlowProbe), l));
    }

    /// <summary>A KNOWN LIMIT, pinned so it is exact rather than folklore. BnInput wraps
    /// the app's ValueChanged in its own HandleChange, so the delegate the renderer sees
    /// is BnInput's, and the call site that holds it is BnInput's own sequence 102. Every
    /// BnInput change in an app therefore shares ONE key and ONE warning, which names
    /// BnInput rather than the app's handler. When BnInput exposes the app's delegate, or
    /// the key walks to the parent component, this pin goes red: flip it to two warnings
    /// naming the app's methods, do not delete it.</summary>
    [Fact]
    public void TwoBnInputs_WithDifferentSlowHandlers_StillShareOneWarning_NamingBnInput_KnownLimit()
    {
        using var s = new Session();
        int a = s.HandlerByPlaceholder("bn-in-a", "change");
        int b = s.HandlerByPlaceholder("bn-in-b", "change");
        Assert.NotEqual(a, b);

        Assert.Equal(0, Dispatch(a, Change("x")));
        Assert.Equal(0, Dispatch(b, Change("y")));

        Assert.Equal(1, SlowProbe.RunsOf("bn-in-a")); // anchor: both app handlers ran
        Assert.Equal(1, SlowProbe.RunsOf("bn-in-b"));
        var line = Assert.Single(s.SlowLines());
        Assert.Contains($"in {typeof(BnInput).FullName} held", line);
        Assert.DoesNotContain("HandleChange", line);
    }

    [Fact]
    public void TheCap_Allows32Warnings_ThenOneSuppressionLine_ThenSilence()
    {
        using var s = new Session();
        SlowProbe.SlowLifecycle = true;
        int cap = NativeRenderer.SlowHandlerWarningCap;
        Assert.Equal(32, cap);

        for (int i = 0; i <= cap; i++) // cap + 1 distinct slow keys
            Assert.Equal(0, Exports.DispatchHostEventCore($"capProbe{i}", null));

        string[] lines = s.SlowLines();
        int warned = lines.Count(l => !l.Contains(NativeRenderer.SlowHandlerSuppressedLogText));
        Assert.True(warned == cap,
            $"{cap + 1} distinct slow keys gave {warned} warnings before the suppression line, not {cap}. "
            + "The cap must let exactly the first 32 through and announce the 33rd once.");
        int suppressions = lines.Count(l => l.Contains(NativeRenderer.SlowHandlerSuppressedLogText));
        Assert.True(suppressions == 1,
            $"the cap logged {suppressions} suppression lines for {cap + 1} distinct keys, not 1.");

        // A further distinct slow key, on each path: silence.
        Assert.Equal(0, Exports.DispatchHostEventCore($"capProbe{cap + 1}", null));
        Assert.Equal(0, Dispatch(s.Handler("slow-a"), Click));

        Assert.Equal(cap + 2, SlowProbe.RunsOf("lifecycle")); // anchor: every arm ran slow
        Assert.Equal(1, SlowProbe.RunsOf("slow-a"));
        int total = s.SlowLines().Length;
        Assert.True(total == cap + 1,
            $"after the suppression line two more distinct slow keys left {total} lines in all, "
            + $"not {cap + 1}. Once the cap is announced the warning is silent on every path: a missing "
            + "silent-after-suppression check logs a second suppression line or a new warning.");
    }

    [Fact]
    public void TheRealTimer_WarnsForASynchronousPartFiveTimesTheBudget()
    {
        using var s = new Session(fakeClock: false);
        Assert.Null(s.Renderer.TimestampForTests); // the real Stopwatch path
        SlowProbe.RealSleep = true;

        Assert.Equal(0, Dispatch(s.Handler("slow-a"), Click));

        Assert.Equal(1, SlowProbe.RunsOf("slow-a")); // anchor: the real sleep ran
        string[] lines = s.SlowLines();
        Assert.True(lines.Length == 1,
            $"a handler that really slept {SlowMs} ms gave {lines.Length} warnings, not 1. With no fake clock "
            + "the renderer must read Stopwatch.GetTimestamp at both ends of the synchronous part; a clock "
            + "that does not advance reads one value at both ends, so the elapsed time is zero and nothing is over budget.");
        string line = lines[0];
        Match ms = Regex.Match(line, @"for (\d+) ms");
        Assert.True(ms.Success, $"the warning no longer states its milliseconds as 'for N ms': {line}");
        // Over the budget, not ">= SlowMs": a Windows sleep can end a little early and the
        // milliseconds are truncated. The property proved is "the real timer saw it slow",
        // and the sleep gives it a five-fold margin.
        Assert.True(int.Parse(ms.Groups[1].Value) > NativeRenderer.SlowHandlerBudget,
            $"a {SlowMs} ms sleep was reported as {ms.Groups[1].Value} ms: {line}");
    }

    /// <summary>Buttons on method groups and on capturing lambdas, an input and a lifecycle subscriber. The slow ones
    /// advance <see cref="FakeClock"/> by <see cref="SlowMs"/>, or really sleep that
    /// long when <see cref="RealSleep"/> is set. Static slots are safe under the
    /// "host-session" collection.</summary>
    private sealed class SlowProbe : ComponentBase, IDisposable
    {
        private static readonly Dictionary<string, int> s_runs = new();
        public static volatile bool RealSleep;
        public static volatile bool SlowLifecycle;
        public static volatile string? LastPayload;

        public static int RunsOf(string tag) { lock (s_runs) return s_runs.GetValueOrDefault(tag); }

        public static void ResetStatics()
        {
            lock (s_runs) s_runs.Clear();
            RealSleep = false;
            SlowLifecycle = false;
            LastPayload = null;
        }

        [Inject] public IMobileBridge Bridge { get; set; } = default!;

        private static void Run(string tag, int ms)
        {
            lock (s_runs) s_runs[tag] = s_runs.GetValueOrDefault(tag) + 1;
            if (RealSleep)
                Thread.Sleep(ms);
            else
                FakeClock.Advance(ms);
        }

        protected override void OnInitialized() => Bridge.NativeEvents += OnNativeEvent;

        private void OnNativeEvent(NativeEvent _) { if (SlowLifecycle) Run("lifecycle", SlowMs); }

        private void SlowA() => Run("slow-a", SlowMs);
        private void SlowB() => Run("slow-b", SlowMs);
        private void Quick() => Run("quick", QuickMs);
        private void OnChange(ChangeEventArgs e) { LastPayload = e.Value as string; Run("change", SlowMs); }
        private int _selected;
        private void Select(int item) { _selected = item; Run("select", SlowMs); }
        private void BnSlowOne() => Run("bn-one", SlowMs);
        private string _bindA = "";
        private string _bindB = "";
        private void BnInSlowA(string _) => Run("bn-in-a", SlowMs);
        private void BnInSlowB(string _) => Run("bn-in-b", SlowMs);
        private void BnSlowTwo() => Run("bn-two", SlowMs);

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create(this, SlowA));
            b.AddContent(2, "slow-a");
            b.CloseElement();
            b.OpenElement(3, "button");
            b.AddAttribute(4, "onclick", EventCallback.Factory.Create(this, SlowB));
            b.AddContent(5, "slow-b");
            b.CloseElement();
            b.OpenElement(6, "button");
            b.AddAttribute(7, "onclick", EventCallback.Factory.Create(this, Quick));
            b.AddContent(8, "quick");
            b.CloseElement();
            b.OpenElement(9, "input");
            b.AddAttribute(10, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, OnChange));
            b.CloseElement();
            // One call site, two items: each render builds a new closure per item, so
            // Blazor gives each item's button a NEW handler id on every render.
            for (int i = 0; i < 2; i++)
            {
                int item = i;
                b.OpenElement(11, "button");
                b.AddAttribute(12, "onclick", EventCallback.Factory.Create(this, () => Select(item)));
                b.AddContent(13, $"item-{item}");
                b.CloseElement();
            }
            // A second capturing call site.
            int other = _selected + 100;
            b.OpenElement(14, "button");
            b.AddAttribute(15, "onclick", EventCallback.Factory.Create(this, () => Select(other)));
            b.AddContent(16, "other");
            b.CloseElement();
            // Two BnButtons: BnButton forwards both OnClicks from its own sequence 100.
            b.OpenComponent<BnButton>(20);
            b.AddComponentParameter(21, nameof(BnButton.Label), "bn-one");
            b.AddComponentParameter(22, nameof(BnButton.OnClick),
                EventCallback.Factory.Create<MouseEventArgs>(this, BnSlowOne));
            b.CloseComponent();
            b.OpenComponent<BnButton>(23);
            b.AddComponentParameter(24, nameof(BnButton.Label), "bn-two");
            b.AddComponentParameter(25, nameof(BnButton.OnClick),
                EventCallback.Factory.Create<MouseEventArgs>(this, BnSlowTwo));
            b.CloseComponent();
            // Two @bind change handlers of one value type: both run Blazor's binder lambda.
            b.OpenElement(40, "input");
            b.AddAttribute(41, "placeholder", "bind-a");
            b.AddAttribute(42, "onchange", EventCallback.Factory.CreateBinder(this,
                v => { _bindA = v ?? ""; Run("bind-a", SlowMs); }, _bindA));
            b.CloseElement();
            b.OpenElement(43, "input");
            b.AddAttribute(44, "placeholder", "bind-b");
            b.AddAttribute(45, "onchange", EventCallback.Factory.CreateBinder(this,
                v => { _bindB = v ?? ""; Run("bind-b", SlowMs); }, _bindB));
            b.CloseElement();
            // Two BnInputs: each wraps the app's ValueChanged in its own HandleChange.
            b.OpenComponent<BnInput>(50);
            b.AddComponentParameter(51, nameof(BnInput.Placeholder), "bn-in-a");
            b.AddComponentParameter(52, nameof(BnInput.ValueChanged),
                EventCallback.Factory.Create<string>(this, BnInSlowA));
            b.CloseComponent();
            b.OpenComponent<BnInput>(53);
            b.AddComponentParameter(54, nameof(BnInput.Placeholder), "bn-in-b");
            b.AddComponentParameter(55, nameof(BnInput.ValueChanged),
                EventCallback.Factory.Create<string>(this, BnInSlowB));
            b.CloseComponent();
            // Two page types, each with a slow button inside BnView ChildContent.
            b.OpenComponent<ChildPageA>(30);
            b.CloseComponent();
            b.OpenComponent<ChildPageB>(31);
            b.CloseComponent();
        }

        public void Dispose() => Bridge.NativeEvents -= OnNativeEvent;

        internal static void RunSlow(string tag) => Run(tag, SlowMs);
    }

    // Two page types whose BuildRenderTree is the same line for line: a BnView whose
    // ChildContent is a button at sequence 2. The button's attribute frame sits in
    // BnView's tree, at the same sequence for both, so only the handler's owner tells
    // them apart. The handler is a lambda capturing a local, so Blazor stores it as a
    // boxed EventCallback rather than a raw delegate: this pair covers the
    // EventCallback accessor path, and the BnButton pair the raw-delegate one.
    private sealed class ChildPageA : ComponentBase
    {
        private int _clicks;
        private void Slow(int clicks) { _clicks = clicks + 1; SlowProbe.RunSlow("page-a"); }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            int clicks = _clicks;
            b.OpenComponent<BnView>(0);
            b.AddComponentParameter(1, nameof(BnView.ChildContent), (RenderFragment)(c =>
            {
                c.OpenElement(2, "button");
                c.AddAttribute(3, "onclick", EventCallback.Factory.Create(this, () => Slow(clicks)));
                c.AddContent(4, "page-a");
                c.CloseElement();
            }));
            b.CloseComponent();
        }
    }

    private sealed class ChildPageB : ComponentBase
    {
        private int _clicks;
        private void Slow(int clicks) { _clicks = clicks + 1; SlowProbe.RunSlow("page-b"); }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            int clicks = _clicks;
            b.OpenComponent<BnView>(0);
            b.AddComponentParameter(1, nameof(BnView.ChildContent), (RenderFragment)(c =>
            {
                c.OpenElement(2, "button");
                c.AddAttribute(3, "onclick", EventCallback.Factory.Create(this, () => Slow(clicks)));
                c.AddContent(4, "page-b");
                c.CloseElement();
            }));
            b.CloseComponent();
        }
    }
}
