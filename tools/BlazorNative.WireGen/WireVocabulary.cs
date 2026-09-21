using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorNative.WireGen;

// ─────────────────────────────────────────────────────────────────────────────
// The manifest model + its validation.
//
// Validation is not decoration here. The manifest is now the ONLY place these
// names exist, so a mistake in it propagates silently into four languages at
// once — the exact failure the hand-written copies at least made visible in a
// diff. Every rule below is one that used to be enforced by a drift test
// reading the copies back out; enforcing them at the SOURCE means a bad
// manifest cannot be emitted at all.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StyleGroup
{
    [JsonPropertyName("name")]  public string Name { get; init; } = "";
    [JsonPropertyName("names")] public string[] Names { get; init; } = [];
}

public sealed class StyleTable
{
    [JsonPropertyName("groups")] public StyleGroup[] Groups { get; init; } = [];

    /// <summary>Every name, in declaration order — the order the emitters use, so
    /// the generated files read like the manifest rather than like a hash set.</summary>
    public IEnumerable<string> Names => Groups.SelectMany(g => g.Names);
}

public sealed class NameList
{
    [JsonPropertyName("names")] public string[] Names { get; init; } = [];
}

public sealed class NodeType
{
    [JsonPropertyName("id")]       public int Id { get; init; }
    [JsonPropertyName("enum")]     public string Enum { get; init; } = "";
    /// <summary>The string the renderer emits for this widget class, or null for
    /// ids that exist on the wire but are never emitted (id 0 = None).</summary>
    [JsonPropertyName("wireName")] public string? WireName { get; init; }
}

public sealed class NodeTypeTable
{
    [JsonPropertyName("fallbackName")] public string FallbackName { get; init; } = "?";
    [JsonPropertyName("types")]        public NodeType[] Types { get; init; } = [];

    /// <summary>The shells' ordinal array: index IS the wire id, and the entry for
    /// a non-emitted id is the fallback the index guard also returns for anything
    /// past the end.</summary>
    public IEnumerable<string> ShellNames => Types.Select(t => t.WireName ?? FallbackName);
}

public sealed class HostEvent
{
    /// <summary>The name as it crosses the wire. This exact string is what
    /// blazornative_host_event receives and what DispatchHostEventCore matches.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    /// <summary>"reserved" — .NET intercepts it in DispatchHostEventCore and routes
    /// it. "passthrough" — .NET never names it; it reaches the app multicast as an
    /// opaque string. Both misspelling failure modes are SILENT, which is why the
    /// tier is data rather than a comment.</summary>
    [JsonPropertyName("tier")] public string Tier { get; init; } = "";

    /// <summary>The PascalCase spelling the emitters use for an enum case or a
    /// constant. Derived, never authored: a second spelling in the manifest would
    /// be a second copy of the same truth, which is the defect this phase closes.</summary>
    public string EnumCase => char.ToUpperInvariant(Name[0]) + Name[1..];
}

public sealed class HostEventTable
{
    [JsonPropertyName("events")] public HostEvent[] Events { get; init; } = [];

    /// <summary>Every name, in declaration order — the order the emitters use.</summary>
    public IEnumerable<string> Names => Events.Select(e => e.Name);

    /// <summary>The names DispatchHostEventCore must have a routing arm for.</summary>
    public IEnumerable<string> Reserved =>
        Events.Where(e => e.Tier == ReservedTier).Select(e => e.Name);

    public const string ReservedTier = "reserved";
    public const string PassthroughTier = "passthrough";
}

public sealed class WireVocabulary
{
    [JsonPropertyName("yogaStyles")]                   public StyleTable YogaStyles { get; init; } = new();
    [JsonPropertyName("visualStyles")]                 public StyleTable VisualStyles { get; init; } = new();
    [JsonPropertyName("scrollIgnoredContainerStyles")] public NameList ScrollIgnoredContainerStyles { get; init; } = new();
    [JsonPropertyName("measuredNodeTypes")]            public NameList MeasuredNodeTypes { get; init; } = new();
    [JsonPropertyName("hostEvents")]                   public HostEventTable HostEvents { get; init; } = new();
    [JsonPropertyName("nodeTypes")]                    public NodeTypeTable NodeTypes { get; init; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses and VALIDATES. Throws <see cref="InvalidDataException"/> with
    /// a message naming the offending entry — never returns a half-valid model,
    /// because the caller's next act is to write four files from it.</summary>
    public static WireVocabulary Load(string json)
    {
        WireVocabulary v = JsonSerializer.Deserialize<WireVocabulary>(json, Options)
            ?? throw new InvalidDataException("wire-vocabulary.json deserialized to null");
        v.Validate();
        return v;
    }

    public void Validate()
    {
        string[] yoga = YogaStyles.Names.ToArray();
        string[] visual = VisualStyles.Names.ToArray();

        RequireNonEmpty(yoga, "yogaStyles");
        RequireNonEmpty(visual, "visualStyles");
        RequireNonEmpty(NodeTypes.Types, "nodeTypes");

        RequireNoDuplicates(yoga, "yogaStyles");
        RequireNoDuplicates(visual, "visualStyles");
        RequireNoDuplicates(ScrollIgnoredContainerStyles.Names, "scrollIgnoredContainerStyles");
        RequireNoDuplicates(MeasuredNodeTypes.Names, "measuredNodeTypes");

        // THE PARTITION. Both shells route a style name to exactly one of two
        // places, and "which one?" must not be answerable twice.
        string[] both = yoga.Intersect(visual, StringComparer.Ordinal).ToArray();
        if (both.Length > 0)
            throw new InvalidDataException(
                $"style names {string.Join(", ", both)} appear in BOTH yogaStyles and visualStyles. "
                + "The partition is the shells' routing table: a name in both is a name two "
                + "shells will each route somewhere, and not necessarily the same somewhere.");

        // THE SUBSET RULE. The scroll-ignore rule is only reached once the router
        // has decided a name belongs to Yoga; a name here but not there would
        // never reach it and would be silently dropped instead.
        string[] orphaned = ScrollIgnoredContainerStyles.Names
            .Except(yoga, StringComparer.Ordinal).ToArray();
        if (orphaned.Length > 0)
            throw new InvalidDataException(
                $"scrollIgnoredContainerStyles {string.Join(", ", orphaned)} are not in yogaStyles. "
                + "The ignore rule only runs for names the router already sent to Yoga, so such a "
                + "name would fall into the visual branch and be dropped rather than ignored.");

        // Wire ids are positional: the shells index an array by them.
        for (int i = 0; i < NodeTypes.Types.Length; i++)
        {
            if (NodeTypes.Types[i].Id != i)
                throw new InvalidDataException(
                    $"nodeTypes[{i}] has id {NodeTypes.Types[i].Id}. Ids must be dense and ordered "
                    + "from 0: the shells decode by INDEXING an array with the wire id, so a gap or "
                    + "a reorder silently builds the wrong widget for every node past it.");
        }

        RequireNoDuplicates(NodeTypes.Types.Select(t => t.Enum), "nodeTypes.enum");
        RequireNoDuplicates(
            NodeTypes.Types.Where(t => t.WireName is not null).Select(t => t.WireName!),
            "nodeTypes.wireName");

        // A measured node type that is not a node type at all would install a
        // measure function keyed on a string nothing ever emits — invisible.
        string[] unknownMeasured = MeasuredNodeTypes.Names
            .Except(NodeTypes.Types.Select(t => t.WireName).OfType<string>(), StringComparer.Ordinal)
            .ToArray();
        if (unknownMeasured.Length > 0)
            throw new InvalidDataException(
                $"measuredNodeTypes {string.Join(", ", unknownMeasured)} are not node types. "
                + "A measure function keyed on a name nothing emits is dead code that looks live.");

        RequireNonEmpty(HostEvents.Events, "hostEvents");
        RequireNoDuplicates(HostEvents.Names, "hostEvents");

        // THE TIER IS A ROUTING DECISION, not a label. An unknown tier would make
        // "does .NET intercept this?" unanswerable, and the dispatch-arm pin reads
        // this field to decide what it must assert.
        foreach (HostEvent e in HostEvents.Events)
        {
            if (e.Tier is not (HostEventTable.ReservedTier or HostEventTable.PassthroughTier))
                throw new InvalidDataException(
                    $"hostEvent '{e.Name}' has tier '{e.Tier}' — expected "
                    + $"'{HostEventTable.ReservedTier}' or '{HostEventTable.PassthroughTier}'. "
                    + "The tier decides whether DispatchHostEventCore must route the name or "
                    + "let it fall through to the app multicast; an unknown value makes that "
                    + "unanswerable and leaves the dispatch-arm pin with nothing to assert.");

            if (string.IsNullOrWhiteSpace(e.Name))
                throw new InvalidDataException("a hostEvent has an empty name");
        }
    }

    private static void RequireNonEmpty<T>(IReadOnlyCollection<T> items, string what)
    {
        if (items.Count == 0)
            throw new InvalidDataException(
                $"{what} is empty. An empty table emits four empty tables and every style name "
                + "silently stops routing — a manifest that cannot see its own subject must never "
                + "be emitted.");
    }

    private static void RequireNoDuplicates(IEnumerable<string> names, string what)
    {
        string[] dupes = names.GroupBy(n => n, StringComparer.Ordinal)
                              .Where(g => g.Count() > 1)
                              .Select(g => g.Key)
                              .ToArray();
        if (dupes.Length > 0)
            throw new InvalidDataException($"{what} contains duplicates: {string.Join(", ", dupes)}");
    }
}
