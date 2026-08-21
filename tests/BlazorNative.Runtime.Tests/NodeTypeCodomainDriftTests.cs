using System.Reflection;
using System.Text.RegularExpressions;
using BlazorNative.Renderer;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NodeTypeCodomainDriftTests — Phase 13.4 (R6). THIS SUPERSEDES #280's ORIGINAL
// FRAMING, and the reason is worth keeping.
//
// #280 said: FrameEncoder.MapNodeType THROWS on an unmapped node type, that throw
// is the C-ABI gate every on-device frame passes, and BnTestTree.Apply accepted any
// string — so a component emitting an unmapped type would give a GREEN harness test
// and a frame that aborts rc 2 on device. The prescribed fix was to run the real
// encoder inside BnTestHost as a gate.
//
// THAT SCENARIO IS UNREACHABLE. NativeRenderer.MapElementToNodeType ends in a
// catch-all — `_ => "view"` — so a component CANNOT emit a node type outside the
// encoder's map. It was measured before this pin was written: an element named
// `marquee` projects as `view`, which the encoder maps happily. The coercion is
// what has kept the encoder's throw from ever firing in anger.
//
// SO WHAT IS THE REAL RISK? Not a component reaching an unmapped type — the
// renderer will not let it. It is THE TWO MAPS DRIFTING APART. They are separate
// switches in separate assemblies:
//
//     NativeRenderer.MapElementToNodeType   element name  → node type   (Renderer)
//     FrameEncoder.MapNodeType              node type     → wire enum   (Runtime)
//
// Add `"video" => "video"` to the first and forget the second, and the coercion
// stops protecting you: a real component emits `video`, the harness projects it
// happily (it accepts any string), and the device aborts rc 2 on the encoder's
// throw. That is #280's failure, reached by the door that is actually open.
//
// This pin closes it as a SET CONTAINMENT: everything the renderer can produce is
// something the encoder accepts. It needs no new package edge — Runtime.Tests
// already sees both assemblies — which is the other half of why the encoder-in-the-
// harness gate was the wrong shape.
//
// NEITHER SET IS RESTATED HERE, deliberately: a test carrying its own third copy of
// the node-type list would be the very defect this phase exists to close (see
// [[safety-claims-without-pins]] and the wire-vocabulary codegen). Instead:
//   · the DOMAIN is source-scanned out of MapElementToNodeType's own switch — only
//     the element names on the left of each arm, which is the one thing that cannot
//     be enumerated any other way (an element name is any string a component writes);
//   · the CODOMAIN is then computed by INVOKING THE REAL METHOD on each of them,
//     so no arm's result is ever transcribed — the parse only supplies candidate
//     inputs, the renderer itself does the classifying;
//   · the catch-all arm contributes through a name guaranteed not to be in the
//     switch, so the `_ =>` result is measured rather than assumed;
//   · ACCEPTANCE is decided by calling the real FrameEncoder.MapNodeType and seeing
//     whether it throws — the actual C-ABI gate, not a model of it.
//
// VACUITY IS ASSERTED, NOT ASSUMED (the house rule). Every assertion below is of
// the "found no offenders" shape, which is exactly what a BROKEN scan reports. So
// the scan proves it still sees its subject: the method must be found, the parse
// must yield a floor of element names, the codomain must be non-empty, and the
// encoder's own accepted set must be non-empty. A pin comparing two empty sets
// passes forever.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The renderer's element→node-type mapping, derived from the renderer
/// itself. Shared with <see cref="TextCollapseParityDriftTests"/>, which needs the
/// same inversion to name an element that produces a given node type.</summary>
internal static class RendererNodeTypeMap
{
    /// <summary>The renderer's element→node-type switch, reached reflectively: it is
    /// `private static`, and InternalsVisibleTo does not reach a private member.
    /// Invoking the REAL method is the point — it is what keeps this pin a reference
    /// to the renderer's rules rather than a copy of them.</summary>
    private static readonly MethodInfo MapElementToNodeType =
        typeof(NativeRenderer).GetMethod(
            "MapElementToNodeType",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(string)])
        ?? throw new InvalidOperationException(
            "NativeRenderer.MapElementToNodeType was not found. It was renamed or its signature "
            + "changed — re-point this pin deliberately rather than deleting it: it is the only "
            + "thing holding the renderer's node types inside the encoder's map.");

    internal static string Project(string elementName)
        => (string)MapElementToNodeType.Invoke(null, [elementName])!;

    /// <summary>Whether the real C-ABI gate accepts this node type. The gate IS the
    /// throw, so this asks it rather than modelling it.</summary>
    internal static bool Accepts(string nodeType)
    {
        try
        {
            _ = FrameEncoder.MapNodeType(nodeType);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>One element name per node type the renderer can produce — the switch,
    /// INVERTED by running it. Lets a test say "render something that becomes a
    /// checkbox" without knowing that the element for it is spelled `checkbox` while
    /// the one for `image` is spelled `img`.</summary>
    internal static IReadOnlyDictionary<string, string> RepresentativeElements()
    {
        var byNodeType = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string element in ElementNamesInTheSwitch())
        {
            string nodeType = Project(element);
            if (!byNodeType.ContainsKey(nodeType)) byNodeType[nodeType] = element;
        }

        return byNodeType;
    }

    /// <summary>The element names on the LEFT of MapElementToNodeType's arms, scanned
    /// out of the checkout.</summary>
    /// <remarks>Source text, not reflection, because there is no other handle: the
    /// domain is "every string a component might pass to OpenElement", which is not
    /// enumerable. Only the INPUTS are read here — every arm's RESULT comes from
    /// invoking the method, so this parse cannot silently encode a stale answer.
    /// Comments are stripped first so prose inside the switch cannot be mistaken for
    /// an arm.</remarks>
    internal static IReadOnlyCollection<string> ElementNamesInTheSwitch()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "BlazorNative.Renderer", "NativeRenderer.cs"));

        Match block = Regex.Match(
            source,
            @"private static string MapElementToNodeType\s*\([^)]*\)[^{]*\{(?<body>.*?)\n\s*\};",
            RegexOptions.Singleline);

        Assert.True(block.Success,
            "could not find MapElementToNodeType's switch body in NativeRenderer.cs. It moved or "
            + "was rewritten — re-point this scan deliberately; a pin that cannot see its subject "
            + "must never pass vacuously.");

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string rawLine in block.Groups["body"].Value.Split('\n'))
        {
            string line = Regex.Replace(rawLine, @"//.*$", string.Empty);
            int arrow = line.IndexOf("=>", StringComparison.Ordinal);
            if (arrow < 0) continue;

            foreach (Match literal in Regex.Matches(line[..arrow], "\"(?<name>[^\"]+)\""))
                names.Add(literal.Groups["name"].Value);
        }

        return names;
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}

public sealed class NodeTypeCodomainDriftTests
{
    private static string Project(string elementName) => RendererNodeTypeMap.Project(elementName);

    private static bool Accepts(string nodeType) => RendererNodeTypeMap.Accepts(nodeType);

    private static IReadOnlyCollection<string> ElementNamesInTheSwitch()
        => RendererNodeTypeMap.ElementNamesInTheSwitch();

    // ── The pin ──────────────────────────────────────────────────────────────

    [Fact]
    public void EveryNodeTypeTheRendererCanProduce_IsOneTheEncoderAccepts()
    {
        IReadOnlyCollection<string> elements = ElementNamesInTheSwitch();

        // VACUITY GUARD 1 — the parse still sees the switch. A scan that silently
        // matched nothing would make every assertion below pass over an empty set.
        Assert.True(elements.Count >= 12,
            $"parsed only {elements.Count} element names out of MapElementToNodeType's switch — "
            + "the parse stopped seeing its subject (the method was reformatted, or the arms "
            + "moved). Fix the scan; do not let this pin run on an empty domain.");

        var codomain = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string element in elements)
            codomain.Add(Project(element));

        // The CATCH-ALL arm is part of the codomain too, and no literal in the source
        // names it. Measured through the real method with a name that cannot be in the
        // switch, so `_ => …` is observed rather than assumed to still be "view".
        codomain.Add(Project("bn-no-such-element-" + Guid.NewGuid().ToString("N")));

        // VACUITY GUARD 2 — the renderer produced something.
        Assert.True(codomain.Count > 0,
            "the renderer's node-type codomain came back EMPTY. Invoking MapElementToNodeType "
            + "produced nothing, so the containment below would hold vacuously.");

        // VACUITY GUARD 3 — the encoder accepts something. Derived by running the real
        // MapNodeType over the generated wire vocabulary, never by restating it.
        var accepted = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string name in BnWireVocabulary.NodeTypeNames)
        {
            if (name == "?") continue;   // wire id 0 = None, never emitted for a CreateNode
            if (Accepts(name)) accepted.Add(name);
        }

        Assert.True(accepted.Count >= 12,
            $"FrameEncoder.MapNodeType accepted only {accepted.Count} of the wire vocabulary's "
            + "node types. The encoder or the manifest regressed — a containment against a "
            + "collapsed accepted-set is not a pin.");

        // THE ASSERTION. Acceptance is decided by the real gate throwing, not by set
        // membership, so a name the encoder maps but the manifest has not heard of
        // cannot be reported as an offender here (WireVocabularyCodegenTests owns
        // that direction).
        var offenders = codomain.Where(nodeType => !Accepts(nodeType)).ToList();

        Assert.True(offenders.Count == 0,
            $"NativeRenderer can produce node type(s) FrameEncoder cannot encode: "
            + $"{string.Join(", ", offenders.Select(o => $"'{o}'"))}. "
            + "MapElementToNodeType and MapNodeType have drifted apart. What this costs: a "
            + "component emitting one of these renders fine in BnTestHost — the harness "
            + "projects any string — and then ABORTS THE FRAME ON DEVICE with export rc 2, "
            + "because MapNodeType throws and that throw is the C-ABI gate every on-device "
            + "frame passes. Fix it by adding the type to src/wire-vocabulary.json and its "
            + "arm to FrameEncoder.MapNodeType (plus the two shells' node-type arrays), or by "
            + "removing the arm from MapElementToNodeType. The encoder's accepted set is: "
            + $"{string.Join(", ", accepted)}.");

        // ── THE OTHER DIRECTION: A DROPPED ARM MUST BE LOUD ──────────────────
        //
        // Containment alone is one-directional, and that leaves a SILENT-NARROWING
        // channel. The switch holds 29 element literals but the floor above only
        // demands 12, so a reformat that stopped the parse seeing up to 17 of them
        // would still clear it — and one-directional containment gets EASIER as the
        // domain shrinks, so the pin would go quietly weaker while staying green.
        // The companion test below keeps it from degrading to nothing (it pins two
        // arms and the catch-all by name), but "not a vacuum" is a lower bar than
        // this phase should accept.
        //
        // Covering the accepted set closes it. Every node type the encoder accepts
        // is reachable from some element today, so equality is the true and
        // therefore the assertable state — and it is the strongest available form:
        // with the containment above, the two sets are now pinned EQUAL, and a
        // dropped arm removes a node type from the codomain and reds here.
        //
        // It also pins something real in its own right: a wire node type no element
        // can produce is a widget class the framework can encode and no component
        // can ever render.
        var unreachable = accepted.Except(codomain, StringComparer.Ordinal).ToList();

        Assert.True(unreachable.Count == 0,
            "FrameEncoder accepts node type(s) NativeRenderer can no longer produce: "
            + $"{string.Join(", ", unreachable.Select(u => $"'{u}'"))}. "
            + "Either an arm was dropped from MapElementToNodeType — in which case the "
            + "component that used to emit it now renders as a plain `view`, silently — or "
            + "the scan above stopped seeing part of the switch, in which case THIS PIN IS "
            + "NARROWING and the containment it asserts is getting easier rather than "
            + "truer. Check the parse before changing this assertion. "
            + $"The renderer's codomain is: {string.Join(", ", codomain)}.");
    }

    /// <summary>Non-vacuity, positively: the mechanism still classifies things it is
    /// KNOWN to classify. Guards 1-3 above prove the sets are populated; this proves
    /// they are populated with the right thing — a scan that returned twelve garbage
    /// strings would clear every floor and mean nothing.</summary>
    [Fact]
    public void TheScan_StillSeesItsSubject_OnKnownArms()
    {
        IReadOnlyCollection<string> elements = ElementNamesInTheSwitch();

        Assert.Contains("div", elements);
        Assert.Contains("button", elements);

        // The real method's answers for two arms and the catch-all.
        Assert.Equal("view", Project("div"));
        Assert.Equal("button", Project("button"));
        Assert.Equal("view", Project("bn-no-such-element"));
    }

}
