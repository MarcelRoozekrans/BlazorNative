using System.ComponentModel;
using System.Reflection;
using BlazorNative.Renderer;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NotApiEditorBrowsableTests — the guard the M11 final audit's finding F4 said
// was missing (docs/plans/2026-07-22-milestone-11-final-audit.md, "F4").
//
// Phase 11.3 Gate C marked every NOT-API type [EditorBrowsable(Never)] so it
// drops out of a consumer's IntelliSense. But NOTHING asserted the marks: a
// PublicAPI.Shipped.txt baseline records SIGNATURES, not attributes, so deleting
// an [EditorBrowsable(Never)] reds no baseline and no test — a silent change in a
// repo whose whole M11 thesis is "an unguarded mechanism rots." This test closes
// that hole with reflection over the shipped assemblies.
//
// DERIVATION OF THE LIST. The NOT-API tier is the durable record in
// docs/plans/2026-07-21-phase-11.3-api-tiers.md §7.1 (the marking ledger). Of the
// 29 marked types it names, 26 live in the seven shipped assemblies this test host
// can reflection-load (Components, Core, Device, Http, Renderer, Runtime, Testing —
// Phase 15.7 widened this from Runtime + Renderer alone; see the Row 17 disclosure
// below). The other 3 are the analyzers (MobilePolicyAnalyzer /
// InteropBoundaryAnalyzer / BridgeAsyncHandlerAnalyzer): that package targets
// netstandard2.0 and references Roslyn, so runtime-loading it here would drag in
// its dependency closure for nothing (the PackagePurityTests rationale), AND its
// real contract is the diagnostic-ID roster, already guarded by
// AnalyzerDiagnosticRosterTests. The 2 remaining NOT-API types (the ZeroAlloc.Inject
// registration extensions, and Razor's _Imports) are generated source nobody can
// attribute — §7.2 — so they are correctly NOT on this list.
//
// THE TEST BITES BOTH WAYS. MarkedSet_EqualsExpected asserts the set of exported
// types actually carrying the attribute EQUALS this expected set, so removing a mark
// from a NOT-API type reds it AND adding a mark to a STABLE type reds it. The
// non-vacuity test pins the count and proves two known STABLE types are neither on
// the list nor marked — so the list can never quietly collapse to empty or swallow a
// public-API type. Proven by mutation in the PR: delete one [EditorBrowsable] and
// this goes red.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//
// - SEVEN ASSEMBLIES, NOT EVERYTHING SHIPPED. Direction 1 of
//   MarkedSet_EqualsExpectedNotApiSet_InBothDirections ("nothing outside the list
//   is marked") used to sweep only Runtime and Renderer, so a stray
//   [EditorBrowsable(Never)] on a STABLE type in Core, Device, Components or Http
//   went green with nothing to say so. Phase 15.7 widened ShippedAssemblies to all
//   seven shipped packages — each anchored by a typeof of a public type so a
//   rename cannot silently narrow the sweep back down — and it stays green on the
//   real tree because none of the five newly-added assemblies carries the
//   attribute today. The three analyzer assemblies and anything outside these
//   seven (a sample app, a third-party consumer) are still never scanned, for the
//   reasons given above. Within the seven, only GetExportedTypes is scanned, so a
//   mark on a non-exported type (internal, or nested in a non-public type) is
//   invisible either way — such a type cannot reach a consumer's IntelliSense in
//   the first place, so this is not a gap in what the fact is FOR.
//
// - THE MUTATION IS A TEST-ASSEMBLY FIXTURE, NOT A PRODUCT ONE.
//   MarkedTypeNames_ReportsAPlantedStrayMark_FromTheAssemblyItIsGiven drives the
//   exact MarkedTypeNames helper direction 1 calls, but over PlantedStrayMarkFixture
//   — a type living in THIS test assembly — because planting a stray
//   [EditorBrowsable(Never)] in Components, Core, Device or Http is a workaround
//   this phase does not take. It proves the helper reports whatever mark the
//   assemblies it is given actually carry, and does not silently ignore that
//   parameter; it does not, by itself, prove a real stray mark landing in one of
//   the five newly-swept assemblies tomorrow would be caught — only that the
//   mechanism the fact is built from works when handed one.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NotApiEditorBrowsableTests
{
    /// <summary>The NOT-API types Gate C marked, all of which live in Runtime and
    /// Renderer (api-tiers §7.1) — the other five swept assemblies contribute none
    /// today. 12 in Runtime + 14 in Renderer = 26 — #256 added ScrollToPatch, the
    /// 14th Renderer NOT-API type.</summary>
    private static readonly string[] ExpectedNotApiMarked =
    [
        // BlazorNative.Runtime — 12 (Exports + the 9 C-ABI structs/enums + the two
        // on-device implementations composed across the Runtime→Core boundary).
        "BlazorNative.Runtime.Exports",
        "BlazorNative.Runtime.BlazorNativeBridgeCallbacks",
        "BlazorNative.Runtime.BlazorNativeFetchRequest",
        "BlazorNative.Runtime.BlazorNativeFetchResponse",
        "BlazorNative.Runtime.BlazorNativeInitOptions",
        "BlazorNative.Runtime.BlazorNativeInitResult",
        "BlazorNative.Runtime.BlazorNativePatch",
        "BlazorNative.Runtime.BlazorNativeFrame",
        "BlazorNative.Runtime.BlazorNativePatchKind",
        "BlazorNative.Runtime.BlazorNativeNodeType",
        "BlazorNative.Runtime.NativeShellBridge",
        "BlazorNative.Runtime.NativeNavigationManager",

        // BlazorNative.Renderer — 14 (NativeRenderer + the in-memory patch model +
        // the reflection-over-Blazor-internals exception).
        "BlazorNative.Renderer.NativeRenderer",
        "BlazorNative.Renderer.RenderPatch",
        "BlazorNative.Renderer.CreateNodePatch",
        "BlazorNative.Renderer.RemoveNodePatch",
        "BlazorNative.Renderer.UpdatePropPatch",
        "BlazorNative.Renderer.ReplaceTextPatch",
        "BlazorNative.Renderer.SetStylePatch",
        "BlazorNative.Renderer.AttachEventPatch",
        "BlazorNative.Renderer.DetachEventPatch",
        "BlazorNative.Renderer.CommitFramePatch",
        "BlazorNative.Renderer.ScrollToPatch",
        "BlazorNative.Renderer.RenderFrame",
        "BlazorNative.Renderer.NativeUiEvent",
        "BlazorNative.Renderer.BlazorVersionMismatchException",
    ];

    /// <summary>The seven shipped assemblies whose NOT-API surface this test
    /// covers (Phase 15.7 Row 17: widened from Runtime + Renderer alone). Reached
    /// through a type in each so a rename of the assembly cannot silently drop it
    /// from the sweep. The three analyzer assemblies stay out — see the header's
    /// DERIVATION note.</summary>
    private static readonly Assembly[] ShippedAssemblies =
    [
        typeof(BlazorNativeApp).Assembly,                        // BlazorNative.Runtime
        typeof(NativeRenderer).Assembly,                         // BlazorNative.Renderer
        typeof(BlazorNative.Components.BnButton).Assembly,       // BlazorNative.Components
        typeof(BlazorNative.Core.BnLog).Assembly,                // BlazorNative.Core
        typeof(BlazorNative.Device.ICamera).Assembly,            // BlazorNative.Device
        typeof(BlazorNative.Http.ServiceCollectionExtensions).Assembly, // BlazorNative.Http
        typeof(BlazorNative.Testing.BnTestHost).Assembly,        // BlazorNative.Testing
    ];

    private static bool IsBrowsableNever(Type t)
    {
        EditorBrowsableAttribute? a = t.GetCustomAttribute<EditorBrowsableAttribute>(inherit: false);
        return a is not null && a.State == EditorBrowsableState.Never;
    }

    private static Type Resolve(string fullName)
    {
        foreach (Assembly asm in ShippedAssemblies)
        {
            Type? t = asm.GetType(fullName, throwOnError: false);
            if (t is not null)
                return t;
        }

        throw new Xunit.Sdk.XunitException(
            $"NOT-API type '{fullName}' was not found in the shipped assemblies — the tier list "
            + "and the shipped surface have diverged (a rename or a removal).");
    }

    /// <summary>Every type on the NOT-API list still carries the mark — the F4
    /// direction (a deleted [EditorBrowsable] reds here).</summary>
    [Fact]
    public void EveryNotApiType_CarriesEditorBrowsableNever()
    {
        foreach (string fullName in ExpectedNotApiMarked)
        {
            Type t = Resolve(fullName);
            Assert.True(
                IsBrowsableNever(t),
                $"{fullName} is a NOT-API type (api-tiers §7.1) but is missing "
                + "[EditorBrowsable(EditorBrowsableState.Never)]. A PublicAPI baseline records "
                + "signatures, not attributes, so nothing else guards this mark (finding F4).");
        }
    }

    /// <summary>Direction 1's detector, extracted so
    /// MarkedTypeNames_ReportsAPlantedStrayMark_FromTheAssemblyItIsGiven below can
    /// drive the exact code this fact runs, rather than restating its logic against
    /// a fixture (Rule 8) — the gap Rows 19/20 name in DefaultStructTrapSweepTests'
    /// control.</summary>
    private static List<string> MarkedTypeNames(IEnumerable<Assembly> assemblies)
    {
        var names = new List<string>();
        foreach (Assembly asm in assemblies)
        {
            foreach (Type t in asm.GetExportedTypes())
            {
                if (IsBrowsableNever(t))
                    names.Add(t.FullName!);
            }
        }

        return names;
    }

    /// <summary>The set of exported types actually marked EQUALS the expected NOT-API
    /// set — so ADDING a mark to a STABLE/other type reds too, not just removing one.
    /// (The two generated registration extensions and Razor's _Imports are NOT-API but
    /// unmarkable — §7.2 — so they are neither expected nor marked, and pass both ways.)</summary>
    [Fact]
    public void MarkedSet_EqualsExpectedNotApiSet_InBothDirections()
    {
        var expected = new HashSet<string>(ExpectedNotApiMarked, StringComparer.Ordinal);
        List<string> actualMarked = MarkedTypeNames(ShippedAssemblies);

        // Direction 1: nothing outside the list is marked (an EditorBrowsable(Never)
        // sneaked onto a STABLE type would surface here, naming it). Phase 15.7 Row
        // 17: this now sweeps all seven shipped assemblies, not just two — see the
        // header's Rule 5 disclosure for what still sits outside it.
        foreach (string name in actualMarked)
        {
            Assert.True(
                expected.Contains(name),
                $"{name} carries [EditorBrowsable(Never)] but is not on the NOT-API list — either "
                + "a STABLE type was wrongly hidden, or a new NOT-API type needs adding to the tier "
                + "table and this list.");
        }

        // Direction 2: everything on the list is marked (same as the per-type test,
        // asserted as a set so a divergence in count is caught).
        foreach (string name in expected)
            Assert.Contains(name, actualMarked);

        Assert.Equal(expected.Count, actualMarked.Count);
    }

    /// <summary>A type living in THIS test assembly, marked exactly like a real
    /// NOT-API type would be, so the mutation test below can drive
    /// MarkedTypeNames without planting a stray [EditorBrowsable(Never)] in a
    /// shipped assembly — a workaround this phase does not take. It is never on
    /// ExpectedNotApiMarked.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class PlantedStrayMarkFixture;

    /// <summary>MUTATION PROOF for Row 17's widened sweep: drives the SAME
    /// MarkedTypeNames helper direction 1 calls (Rule 8), varying only the
    /// assemblies argument. Handed this test assembly, it reports
    /// PlantedStrayMarkFixture — the helper does not silently ignore what it is
    /// given. Handed the real ShippedAssemblies (the control), it does not,
    /// because the fixture lives nowhere in those seven.</summary>
    [Fact]
    public void MarkedTypeNames_ReportsAPlantedStrayMark_FromTheAssemblyItIsGiven()
    {
        List<string> withTestAssembly = MarkedTypeNames([typeof(NotApiEditorBrowsableTests).Assembly]);
        Assert.Contains(typeof(PlantedStrayMarkFixture).FullName!, withTestAssembly);

        List<string> withShippedAssemblies = MarkedTypeNames(ShippedAssemblies);
        Assert.DoesNotContain(typeof(PlantedStrayMarkFixture).FullName!, withShippedAssemblies);
    }

    /// <summary>Non-vacuity: the list is the exact Gate-C count for Runtime and
    /// Renderer, and two known STABLE types are neither on it nor marked — so the
    /// test cannot pass by having an empty list or by swallowing a public-API type.</summary>
    [Fact]
    public void ExpectedList_IsNonVacuous_AndExcludesKnownStableTypes()
    {
        Assert.Equal(26, ExpectedNotApiMarked.Length);

        var expected = new HashSet<string>(ExpectedNotApiMarked, StringComparer.Ordinal);

        // BlazorNativeApp and BlazorNativePage are STABLE (api-tiers §3.5): the app
        // author's startup contract. They must never be on the NOT-API list nor marked.
        Assert.DoesNotContain(typeof(BlazorNativeApp).FullName!, expected);
        Assert.DoesNotContain(typeof(BlazorNativePage).FullName!, expected);
        Assert.False(IsBrowsableNever(typeof(BlazorNativeApp)));
        Assert.False(IsBrowsableNever(typeof(BlazorNativePage)));
    }
}
