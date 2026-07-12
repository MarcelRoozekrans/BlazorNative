using BlazorNative.Renderer;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ConsumerSmoke;

// ─────────────────────────────────────────────────────────────────────────────
// ConsumerSmoke — Phase 4.5 Gate 2 (M4 DoD #7 proof).
//
// Mounts SmokeRoot via the Renderer.Tests harness shape — build the renderer's
// DI surface directly, capture Frames, assert the patch set. Deliberately NO
// BlazorNative.Runtime/HostSession: the NativeAOT composition root is an
// app-shape concern (unpackaged by design, see the 2026-07-12 phase-4.5
// design); a managed consumer mounts with nothing but the five packages.
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

        Console.WriteLine("[ConsumerSmoke] PASS — Bn* component mounted from packages alone (M4 DoD #7)");
        return 0;
    }
}
