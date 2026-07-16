using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ConsumerSmoke;

// ─────────────────────────────────────────────────────────────────────────────
// ConsumerSmoke — Phase 4.5 Gate 2 (M4 DoD #7 proof), extended by Phase 8.1
// (M8 DoD #2): the six-package consumer.
//
// TWO proofs, side by side:
//
//   1. THE MOUNT (4.5, retained verbatim): SmokeRoot via the Renderer.Tests
//      harness shape — build the renderer's DI surface directly, capture
//      Frames, assert the patch set. This proves the renderer/components
//      surface from packages alone.
//   2. THE REGISTRATION (8.1, design decision 5): the FIRST consumer of
//      BlazorNativeApp.RegisterPages that is not this repo's sample app,
//      asserting the surface's OBSERVABLE laws from the shipped
//      BlazorNative.Runtime package (nothing internal is reached for):
//      the DAM(All) factories compile and run with concrete consumer-owned
//      types; a duplicate-route registration throws ArgumentException NAMING
//      the offending row (the validation strings shipped, not just the happy
//      path — sequenced FIRST, because register-once means only the first
//      call's argument set is validated); a second call throws (the
//      register-once law, observable from outside).
//      What this deliberately does NOT claim: an AOT/ILC pass over a package
//      consumer — that proof belongs to the repo's own publish gates today
//      and to 8.3's template end-to-end (recorded boundary).
//
// Exit code: 0 = PASS, 1 = FAIL (details on stderr-ish stdout lines).
// ─────────────────────────────────────────────────────────────────────────────

internal static class Program
{
    private static readonly List<string> Failures = [];

    private static void Check(bool condition, string expectation)
    {
        if (!condition) Failures.Add(expectation);
    }

    private static async Task<int> Main()
    {
#if ANALYZER_TRIP
        // Phase 4.5 Gate 2 analyzer-activity pin: compiled ONLY under
        // -p:AnalyzerTrip=true (consumer-smoke.ps1's trip build), where this
        // parameterless HttpClient MUST surface BN0011 from the packaged
        // BlazorNative.Analyzers — the script asserts the warning appears
        // (and that the normal build carries zero BN diagnostics).
        using var deliberateTrip = new HttpClient();
#endif

        // ── Phase 8.1: the RegisterPages block (BlazorNative.Runtime's proof) ─
        // Order is load-bearing: registration is once-per-process, so the
        // INVALID call goes first (validation happens before the store — a
        // rejected set registers nothing), then the valid registration, then
        // the register-once law.

        // (a) A duplicate route throws ArgumentException NAMING the offending
        //     row — the shipped validation strings, not just the happy path.
        try
        {
            BlazorNativeApp.RegisterPages(
                BlazorNativePage.Routed<SmokeRoot>("/", "First"),
                BlazorNativePage.Routed<SmokeProbePage>("/", "Second"));
            Check(false, "duplicate-route RegisterPages must throw ArgumentException");
        }
        catch (ArgumentException ex)
        {
            Check(ex.Message.Contains("duplicate route '/'"),
                $"the duplicate-route message names the sin, got: '{ex.Message}'");
            Check(ex.Message.Contains("row 1") && ex.Message.Contains("'Second'"),
                $"the duplicate-route message names the offending row (row 1 'Second'), got: '{ex.Message}'");
        }

        // (b) The valid registration succeeds — the DAM(All) factories, the
        //     params surface, and the trim annotations compile and RUN from
        //     the shipped package with concrete consumer-owned types.
        BlazorNativeApp.RegisterPages(
            BlazorNativePage.Routed<SmokeRoot>("/", nameof(SmokeRoot)),
            BlazorNativePage.Named<SmokeProbePage>(nameof(SmokeProbePage)));

        // (c) A second call throws — the register-once law, observable from
        //     outside. (Remove call (b) and THIS reddens: a single Named row
        //     is a legal first registration, so the Check below fires.)
        try
        {
            BlazorNativeApp.RegisterPages(BlazorNativePage.Named<SmokeProbePage>("Again"));
            Check(false, "a second RegisterPages call must throw (the register-once law)");
        }
        catch (InvalidOperationException)
        {
            // expected — registered once, at startup.
        }

        var services = new ServiceCollection().AddBlazorNativeRenderer();
        await using var provider = services.BuildServiceProvider();
        var renderer = provider.GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true;

        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };

        await renderer.MountAsync<SmokeRoot>(ParameterView.Empty);

        // The mount is synchronous under the InlineDispatcher, so every frame
        // captured here belongs to the initial render. Assert over the union.
        var patches = frames.SelectMany(f => f.Patches).ToList();
        var creates = patches.OfType<CreateNodePatch>().ToList();
        var texts = patches.OfType<ReplaceTextPatch>().ToList();
        var attaches = patches.OfType<AttachEventPatch>().ToList();

        Check(frames.Count > 0, "at least one render frame from the mount");

        var roots = creates.Where(p => p.ParentId is null).ToList();
        Check(roots.Count == 1,
            $"exactly one root create (BnView's div), got {roots.Count}");
        Check(roots.Count == 1 && roots[0].NodeType == "view",
            $"root node type 'view', got '{(roots.Count == 1 ? roots[0].NodeType : "<n/a>")}'");

        Check(creates.Any(p => p.NodeType == "text"),
            "a 'text' node create (BnText's span)");

        var buttons = creates.Where(p => p.NodeType == "button").ToList();
        Check(buttons.Count == 1,
            $"exactly one 'button' node create (BnButton), got {buttons.Count}");

        Check(texts.Any(t => t.Text == "Hello from packages"),
            "text content 'Hello from packages' (BnText.Text)");
        Check(texts.Any(t => t.Text == "Tap"),
            "text content 'Tap' (BnButton.Label)");

        Check(attaches.Count == 1,
            $"exactly one AttachEvent (the BnButton click), got {attaches.Count}");
        Check(attaches.Count == 1 && attaches[0].EventName == "click",
            $"AttachEvent event name 'click', got '{(attaches.Count == 1 ? attaches[0].EventName : "<n/a>")}'");
        Check(attaches.Count == 1 && buttons.Count == 1 && attaches[0].NodeId == buttons[0].NodeId,
            "the click AttachEvent targets the button node");

        Console.WriteLine(
            $"[ConsumerSmoke] frames={frames.Count} patches={patches.Count} " +
            $"creates={creates.Count} (view={creates.Count(p => p.NodeType == "view")}, " +
            $"text={creates.Count(p => p.NodeType == "text")}, " +
            $"button={creates.Count(p => p.NodeType == "button")}) " +
            $"replaceText={texts.Count} attachEvent={attaches.Count}");

        if (Failures.Count > 0)
        {
            Console.WriteLine($"[ConsumerSmoke] FAIL — {Failures.Count} unmet expectation(s):");
            foreach (var f in Failures)
                Console.WriteLine($"[ConsumerSmoke]   ✗ {f}");
            return 1;
        }

        Console.WriteLine("[ConsumerSmoke] PASS — Bn* mount + RegisterPages laws from the six packages alone (M4 DoD #7 / M8 DoD #2)");
        return 0;
    }
}
