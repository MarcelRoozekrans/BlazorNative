using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using BlazorNative.WireGen;
using Xunit;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// #255 — THE PIN THAT MAKES "GENERATED" MEAN SOMETHING.
//
// The manifest (src/wire-vocabulary.json) is now the only place the style,
// scroll-ignore and node-type vocabularies exist; tools/BlazorNative.WireGen
// emits the C#, Kotlin, Objective-C++ and Swift copies. That removes the old
// failure — a name present in one hand-written table and missing from another —
// but it introduces two new ones, and this file exists for exactly those:
//
//   1. A GENERATED FILE EDITED BY HAND. The banner says not to; a banner is not
//      a mechanism. Re-running the emitters in-process and byte-comparing IS.
//   2. A MANIFEST EDITED WITHOUT REGENERATING. Same test, same failure — the
//      committed output no longer matches what the manifest produces.
//
// Both land in the required build-test lane, in the commit that causes them.
//
// It also keeps the ONE cross-language pin codegen cannot provide: the .NET
// enum BlazorNativeNodeType is public API with a PublicAPI baseline, so it is
// deliberately NOT generated — it is asserted against the manifest instead.
// ─────────────────────────────────────────────────────────────────────────────

// This class joins the "host-session" collection solely for
// EveryReservedHostEvent_IsRoutedRatherThanFallingThrough below: that test
// drives Exports.DispatchHostEventCore and asserts on HostSession's
// process-wide static CurrentNavigationManager being null (no session
// mounted). Every other class that mounts a session already serializes on
// this collection (see HostSessionTestCollection); without joining it too,
// this test would be free to run in a different collection IN PARALLEL with
// one of those, and a routed name could observe a live session and return 0
// instead of 1 — a pin that is flaky depending on test scheduling, not one
// that is wrong. None of the other tests in this file touch HostSession.
[Collection("host-session")]
public sealed class WireVocabularyCodegenTests
{
    private static WireVocabulary LoadManifest()
        => WireVocabulary.Load(File.ReadAllText(Path.Combine(BnRepo.Root(), Emitters.ManifestPath)));

    private static DeepLinkVectors LoadVectors(string root)
    {
        string json = File.ReadAllText(Path.Combine(root, "src", "deeplink-vectors.json"));
        DeepLinkVectors? v = JsonSerializer.Deserialize<DeepLinkVectors>(
            json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });
        Assert.NotNull(v);
        return v!;
    }

    /// <summary>Line endings are normalized before comparing, and that is not a
    /// weakening of the pin. Both the emitted string and the committed file are
    /// text under this repo's <c>* text=auto</c> normalization, so the working
    /// copy is CRLF on Windows and LF on the CI runners; a raw byte comparison
    /// would fail on one OS and pass on the other, which is worse than no pin —
    /// it would be a pin that is green exactly where nobody is looking.</summary>
    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void EveryGeneratedFile_IsExactlyWhatTheManifestProduces()
    {
        string root = BnRepo.Root();
        WireVocabulary vocabulary = LoadManifest();

        var stale = new List<string>();
        int compared = 0;

        foreach ((string relative, string expected) in Emitters.EmitAll(vocabulary))
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                stale.Add($"{relative} — MISSING");
                continue;
            }

            compared++;
            string actual = File.ReadAllText(path);
            if (Normalize(actual) != Normalize(expected))
                stale.Add($"{relative} — differs from the emitter's output");
        }

        // NON-VACUITY, and it is not ceremony: EmitAll returning an empty
        // dictionary, or every path being wrong, would make the loop above assert
        // nothing at all while passing. The count is the subject.
        Assert.True(compared >= 5,
            $"compared only {compared} generated files — expected at least 5 (C#, two Kotlin "
            + "copies, the ObjC++ header and the Swift file). A pin that cannot see its subject "
            + "must never pass.");

        Assert.True(stale.Count == 0,
            "GENERATED FILES ARE STALE. Either a generated file was hand-edited, or "
            + "src/wire-vocabulary.json changed without regenerating. Run:\n\n"
            + "    dotnet run --project tools/BlazorNative.WireGen\n\n"
            + "…and commit the result. Offenders:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>The deep-link vector tables are exactly what the manifest produces —
    /// the same byte-comparison the wire vocabulary gets, extended to the vectors.
    /// Three copies of one table is the shape this phase exists to make safe.</summary>
    [Fact]
    public void EveryGeneratedVectorFile_IsExactlyWhatTheManifestProduces()
    {
        string root = BnRepo.Root();
        DeepLinkVectors vectors = LoadVectors(root);

        // NON-VACUITY, and it bites before anything else does: an empty `vectors`
        // array deserializes perfectly well and would emit three EMPTY tables —
        // three shells asserting nothing while every suite reports green. That is
        // the exact failure class this phase exists to close.
        Assert.True(vectors.Vectors.Length >= 7,
            $"the deep-link vector table has {vectors.Vectors.Length} cases. An empty or gutted "
            + "table emits three empty test tables and every suite passes while asserting NOTHING — "
            + "the vacuity this phase exists to remove. If a case was deliberately removed, lower "
            + "this floor in the same commit so the removal is a decision on the record.");

        int compared = 0;
        foreach ((string relative, string expected) in Emitters.EmitAllVectors(vectors))
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"generated vector file missing: {relative}");
            compared++;
            Assert.Equal(Normalize(expected), Normalize(File.ReadAllText(path)));
        }

        // THE SAME REFUSAL ONE LEVEL UP, and the floor above cannot stand in for
        // it: that one guards the MANIFEST, this one guards the EMITTED SET.
        // Dropping a language from EmitAllVectors shrinks the CLI's write set and
        // this loop's coverage TOGETHER — the dropped file goes stale on disk,
        // nothing reds, and that shell keeps asserting an out-of-date table
        // forever. Three, because there are three target languages; if one is
        // ever genuinely dropped, lowering this floor in the same commit makes it
        // a decision on the record rather than a silent loss of coverage.
        Assert.True(compared >= 3,
            $"compared only {compared} generated vector files — expected at least 3 (the C#, "
            + "Kotlin and Swift tables). A pin that cannot see its subject must never pass.");
    }

    [Fact]
    public void TheRenderersStyleSets_AreTheManifests()
    {
        // The renderer keeps the sets and the comparer; only the data is generated.
        // This asserts the join actually happened — a NativeRenderer that quietly
        // went back to its own literal would pass every other test in the repo.
        WireVocabulary v = LoadManifest();

        Assert.Equal(v.YogaStyles.Names.ToHashSet(StringComparer.Ordinal),
                     NativeRenderer.YogaStyleAttributes);
        Assert.Equal(v.VisualStyles.Names.ToHashSet(StringComparer.Ordinal),
                     NativeRenderer.VisualStyleAttributes);
    }

    [Fact]
    public void TheNodeTypeEnum_MatchesTheManifest_IdForId()
    {
        // THE ONE MIRROR CODEGEN DOES NOT OWN. BlazorNativeNodeType is public API
        // with a PublicAPI baseline, so generating it would mean a generator that
        // can move a frozen surface. It is pinned instead — and pinned through the
        // REAL mapping function rather than by reading the enum, so this fails if
        // either the enum or FrameEncoder's switch drifts from the manifest.
        WireVocabulary v = LoadManifest();

        int checkedTypes = 0;
        foreach (NodeType t in v.NodeTypes.Types)
        {
            if (t.WireName is null) continue;   // id 0 = None is never emitted
            checkedTypes++;

            BlazorNativeNodeType mapped = FrameEncoder.MapNodeType(t.WireName);
            Assert.Equal(t.Id, (int)mapped);
            Assert.Equal(t.Enum, mapped.ToString());
        }

        Assert.True(checkedTypes >= 12,
            $"only {checkedTypes} node types checked — the manifest lost entries, or this loop "
            + "stopped seeing them");

        // Both directions: an enum member the manifest does not know about would
        // be a widget class the shells have no name for.
        string[] enumNames = Enum.GetNames<BlazorNativeNodeType>();
        Assert.Equal(v.NodeTypes.Types.Select(t => t.Enum).ToArray(), enumNames);
    }

    [Fact]
    public void TheShellsNodeTypeArray_PutsTheFallbackWhereTheUnemittedIdIs()
    {
        // Index IS the wire id, so slot 0 (None — never emitted for a CreateNode)
        // must hold the same "?" a shell returns for an id past the end of its
        // array. If it held a real widget name instead, a corrupt or future id
        // would decode to a plausible-looking widget rather than an obvious one.
        WireVocabulary v = LoadManifest();

        Assert.Equal(v.NodeTypes.FallbackName, v.NodeTypes.ShellNames.First());
        Assert.Equal(v.NodeTypes.Types.Length, v.NodeTypes.ShellNames.Count());
    }

    [Fact]
    public void TheEmittedEnums_CarryEveryManifestHostEvent()
    {
        WireVocabulary v = LoadManifest();
        string kotlin = Emitters.EmitKotlin(v);
        string swift = Emitters.EmitSwift(v);

        foreach (HostEvent e in v.HostEvents.Events)
        {
            Assert.Contains($"{e.EnumCase}(\"{e.Name}\")", kotlin);
            Assert.Contains($"case {char.ToLowerInvariant(e.EnumCase[0])}{e.EnumCase[1..]} = \"{e.Name}\"", swift);
        }

        Assert.Contains("enum class BnHostEvent", kotlin);
        Assert.Contains("enum BnHostEvent: String", swift);
    }

    [Fact]
    public void TheManifest_DeclaresTheSixHostEvents_WithTiers()
    {
        WireVocabulary v = LoadManifest();

        // Phase 14.2 added "safeAreaChanged" (reserved) between "navigate" and the
        // passthrough tier — insertion order is preserved, not sorted, so it lands
        // exactly where the manifest places it.
        Assert.Equal(
            ["back", "navigate", "safeAreaChanged", "onResume", "onPause", "onDestroy"],
            v.HostEvents.Names.ToArray());

        // The reserved tier is the one .NET intercepts in DispatchHostEventCore.
        // Everything else falls through to the app multicast as an opaque string.
        Assert.Equal(["back", "navigate", "safeAreaChanged"], v.HostEvents.Reserved.ToArray());
    }

    [Fact]
    public void TheManifest_RejectsAnUnknownTier()
    {
        // Validation happens at the SOURCE: a bad manifest must not be emittable,
        // because emitting it propagates the mistake into three languages at once.
        const string bad = """
            {
              "yogaStyles":   { "groups": [ { "name": "G", "names": ["width"] } ] },
              "visualStyles": { "groups": [ { "name": "G", "names": ["color"] } ] },
              "nodeTypes":    { "fallbackName": "?", "types": [ { "id": 0, "enum": "None" } ] },
              "hostEvents":   { "events": [ { "name": "onPause", "tier": "sometimes" } ] }
            }
            """;

        var ex = Assert.Throws<InvalidDataException>(() => WireVocabulary.Load(bad));
        Assert.Contains("sometimes", ex.Message);
    }

    [Fact]
    public void AMalformedManifest_IsRefused_NotEmitted()
    {
        // The generator's validation is the thing standing between a typo and four
        // languages agreeing on the wrong answer, so it is asserted rather than
        // assumed. The subset rule is the sharpest of them: a scroll-ignore name
        // that is not a Yoga style would never reach the ignore rule at all — it
        // would fall into the visual branch and be silently dropped, which is the
        // exact failure class this whole issue is about.
        const string orphanedIgnore = """
            {
              "yogaStyles":   { "groups": [ { "name": "G", "names": ["width"] } ] },
              "visualStyles": { "groups": [ { "name": "V", "names": ["color"] } ] },
              "scrollIgnoredContainerStyles": { "names": ["gap"] },
              "measuredNodeTypes": { "names": [] },
              "nodeTypes": { "fallbackName": "?", "types": [ { "id": 0, "enum": "None", "wireName": null } ] }
            }
            """;
        var ex = Assert.Throws<InvalidDataException>(() => WireVocabulary.Load(orphanedIgnore));
        Assert.Contains("scrollIgnoredContainerStyles", ex.Message);

        const string overlappingPartition = """
            {
              "yogaStyles":   { "groups": [ { "name": "G", "names": ["width", "color"] } ] },
              "visualStyles": { "groups": [ { "name": "V", "names": ["color"] } ] },
              "scrollIgnoredContainerStyles": { "names": [] },
              "measuredNodeTypes": { "names": [] },
              "nodeTypes": { "fallbackName": "?", "types": [ { "id": 0, "enum": "None", "wireName": null } ] }
            }
            """;
        Assert.Contains("BOTH", Assert.Throws<InvalidDataException>(
            () => WireVocabulary.Load(overlappingPartition)).Message);

        const string renumberedIds = """
            {
              "yogaStyles":   { "groups": [ { "name": "G", "names": ["width"] } ] },
              "visualStyles": { "groups": [ { "name": "V", "names": ["color"] } ] },
              "scrollIgnoredContainerStyles": { "names": [] },
              "measuredNodeTypes": { "names": [] },
              "nodeTypes": { "fallbackName": "?", "types": [ { "id": 1, "enum": "View", "wireName": "view" } ] }
            }
            """;
        Assert.Contains("dense and ordered", Assert.Throws<InvalidDataException>(
            () => WireVocabulary.Load(renumberedIds)).Message);
    }

    [Fact]
    public void TheHostEventConstants_MatchTheManifest_BothWays()
    {
        // THE MIRROR CODEGEN DOES NOT OWN. BnHostEvents is public API with a
        // PublicAPI baseline, so it is hand-written and pinned — the same trade
        // TheNodeTypeEnum_MatchesTheManifest_IdForId makes, for the same reason.
        WireVocabulary v = LoadManifest();

        Dictionary<string, string> declared = typeof(BnHostEvents)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

        // Direction 1: every manifest name has a constant, spelled correctly.
        foreach (HostEvent e in v.HostEvents.Events)
        {
            Assert.True(declared.TryGetValue(e.EnumCase, out string? value),
                $"manifest hostEvent '{e.Name}' has no BnHostEvents.{e.EnumCase} constant — "
                + "apps cannot name an event the shells send");
            Assert.Equal(e.Name, value);
        }

        // Direction 2: no constant without a manifest entry. Without this the class
        // could grow a name no shell sends and the pin would still be green.
        Assert.Equal(
            v.HostEvents.Names.OrderBy(n => n, StringComparer.Ordinal),
            declared.Values.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryReservedHostEvent_IsRoutedRatherThanFallingThrough()
    {
        // A reserved name with no arm in DispatchHostEventCore does not throw — it
        // falls through to the app multicast and is SILENTLY IGNORED. That is the
        // failure this pin exists for: the direct analogue of a style name the
        // routing table accepts that no shell applies.
        WireVocabulary v = LoadManifest();

        int checkedNames = 0;
        foreach (string reserved in v.HostEvents.Reserved)
        {
            checkedNames++;

            // With no session mounted, a ROUTED name reports "nothing to route to"
            // (rc 1) because the nav manager is null. An UNROUTED name reaches the
            // multicast, which has no subscribers, and reports success (rc 0).
            // The two are distinguishable precisely because routing happens first.
            //
            // ASSUMPTION, NOT A DERIVED PROPERTY (14.0 final review, item 4): this
            // pin treats rc 1 as the universal signature of "routed but idle with no
            // session mounted". That holds for every reserved arm TODAY (Back routes
            // to a null nav manager, Navigate the same), but a future reserved arm
            // could legitimately report rc 0 with no session mounted — e.g. one that
            // only touches shell-local state and has nothing that needs a mounted
            // session to be "handled". Such an arm would red HERE even though it is
            // correct, because this assertion cannot distinguish "correctly routed,
            // rc 0" from "fell through to the multicast, rc 0". It fails LOUD, so it
            // is safe (nobody ships a silent miss) — but whoever adds the next
            // reserved event (phase 14.2's insets event looks like the next one)
            // should re-derive rc 1 for that arm rather than assume this pin already
            // covers it, and add a session-independent assertion if it does not.
            int rc = Exports.DispatchHostEventCore(reserved, payload: null);

            Assert.True(rc == 1,
                $"reserved host event '{reserved}' returned rc {rc} with no session mounted — "
                + "expected 1 (routed, but nothing to route to). rc 0 means it fell through "
                + "to the app multicast, i.e. DispatchHostEventCore has no arm for it and the "
                + "name is silently ignored on every device.");
        }

        Assert.True(checkedNames >= 2,
            $"only {checkedNames} reserved names checked — the manifest lost entries, or this "
            + "loop stopped seeing them");
    }

    // ── hostCallOps (Phase 16.1): the op integer on the ONE hostCallBegin slot ──
    //
    // Before 16.1 the op enum was hand-mirrored in three languages, agreeing only
    // because three per-capability tests asserted their own constant. It is now
    // generated. These three facts pin what generation alone cannot:
    //   - the ids are FROZEN, because a shipped shell switches on the integer;
    //   - every language's copy carries every op, read back out of the emitted AND
    //     the committed text, both ways;
    //   - the manifest refuses a duplicate id or name, which would route two
    //     capabilities to one arm in every shell at once.
    //
    // DOES NOT COVER: that each shell has an ARM for every op. An op a shell does
    // not route takes its unknown-op branch, which completes with Error and, on
    // Android, reaches onError. FaultNotice's routing is pinned by the shells' own
    // suites, FaultNoticeTest.kt and BnFaultNoticeTests.swift. Nor the ObjC++
    // header, which carries no op table.

    /// <summary>Frozen, not merely current: a released shell was compiled against these.</summary>
    private static readonly (string Name, int Id)[] FrozenHostCallOps =
    [
        ("Geolocation", 0), ("Notifications", 1), ("Biometrics", 2),
        ("SecureStorage", 3), ("Camera", 4), ("FaultNotice", 5),
    ];

    [Fact]
    public void TheHostCallOps_KeepTheirFrozenIds()
    {
        WireVocabulary v = LoadManifest();

        // Rule 2: the manifest must still hold at least the six ops this pins.
        Assert.True(v.HostCallOps.Ops.Length >= FrozenHostCallOps.Length,
            $"the manifest declares {v.HostCallOps.Ops.Length} host-call ops, fewer than the "
            + $"{FrozenHostCallOps.Length} frozen ones. An op was deleted, and a shipped shell still "
            + "switches on its integer.");

        Dictionary<string, int> declared = v.HostCallOps.Ops.ToDictionary(o => o.Name, o => o.Id, StringComparer.Ordinal);
        foreach ((string name, int id) in FrozenHostCallOps)
        {
            Assert.True(declared.TryGetValue(name, out int actual),
                $"host-call op '{name}' is gone from the manifest. Its id {id} is on the wire in every "
                + "released shell; renaming it is a break, and reusing the id is worse.");
            Assert.True(actual == id,
                $"host-call op '{name}' has id {actual} in the manifest, but {id} is frozen. A shell "
                + "compiled against the old id routes the call to the wrong capability.");
        }

        // The compiled .NET enum is the generated one, so it must agree too.
        foreach ((string name, int id) in FrozenHostCallOps)
            Assert.Equal(id, (int)Enum.Parse<HostCallOp>(name));
    }

    /// <summary>The op block of one generated file: from <paramref name="header"/> to
    /// the next line that is a lone closing brace.</summary>
    private static string OpBlock(string text, string header, string where)
    {
        string normalized = Normalize(text);
        int start = normalized.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{where}: no '{header}' block. The emitter stopped writing it, or renamed it.");
        int end = normalized.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{where}: the '{header}' block is never closed");
        return normalized[start..end];
    }

    /// <summary>name → id, read with <paramref name="pattern"/>, whose two groups are
    /// the spelling and the integer.</summary>
    private static Dictionary<string, int> ReadOps(string block, string pattern)
        => Regex.Matches(block, pattern, RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value,
                          m => int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                          StringComparer.Ordinal);

    [Fact]
    public void TheEmittedHostCallOps_MatchTheManifest_InAllThreeLanguages()
    {
        string root = BnRepo.Root();
        WireVocabulary v = LoadManifest();

        Dictionary<string, int> Expected(Func<HostCallOpEntry, string> spell)
            => v.HostCallOps.Ops.ToDictionary(spell, o => o.Id, StringComparer.Ordinal);

        // Per language: the committed copy, what the emitter produces now, the block
        // header, the line pattern, and the manifest spelled that language's way.
        var languages = new (string Lang, string Path, string Emitted, string Header, string Pattern, Dictionary<string, int> Want)[]
        {
            ("C#", "src/BlazorNative.Runtime/BnHostCallOps.g.cs", Emitters.EmitCSharpHostCallOps(v),
                "internal enum HostCallOp", @"^\s*(\w+)\s*=\s*(\d+),", Expected(o => o.Name)),
            ("Kotlin", "src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/BnWireVocabulary.g.kt", Emitters.EmitKotlin(v),
                "object HostCallOp", @"^\s*const val (\w+) = (\d+)$", Expected(o => o.KotlinName)),
            ("Swift", "src/BlazorNative.Apple/BnHost/BnWireVocabulary.g.swift", Emitters.EmitSwift(v),
                "enum BnHostCallOp", @"^\s*static let (\w+): Int32 = (\d+)$", Expected(o => o.SwiftName)),
        };

        int languagesChecked = 0;
        foreach (var l in languages)
        {
            string committed = File.ReadAllText(Path.Combine(root, l.Path.Replace('/', Path.DirectorySeparatorChar)));
            foreach ((string source, string text) in new[] { ("emitted", l.Emitted), ("committed", committed) })
            {
                string where = $"{l.Lang} ({source}, {l.Path})";
                Dictionary<string, int> read = ReadOps(OpBlock(text, l.Header, where), l.Pattern);

                // Rule 2: a pattern that stopped matching reads nothing and agrees with
                // nothing, so the floor and the anchor come before the comparison.
                Assert.True(read.Count >= 6,
                    $"{where}: read {read.Count} ops, expected at least 6. The block lost ops or the "
                    + "pattern no longer matches the emitted shape.");
                string camera = l.Want.Single(kv => kv.Value == 4).Key;
                Assert.True(read.TryGetValue(camera, out int cameraId) && cameraId == 4,
                    $"{where}: no '{camera} = 4'. The anchor op is missing or renumbered.");

                // Both ways: every manifest op is present with its id, and nothing else is.
                Assert.Equal(
                    l.Want.OrderBy(kv => kv.Key, StringComparer.Ordinal),
                    read.OrderBy(kv => kv.Key, StringComparer.Ordinal));
            }
            languagesChecked++;
        }
        Assert.Equal(3, languagesChecked);
    }

    [Fact]
    public void TheManifest_RejectsADuplicateOpId()
    {
        static string Manifest(int secondId, string secondName) => $$"""
            {
              "yogaStyles":   { "groups": [ { "name": "G", "names": ["width"] } ] },
              "visualStyles": { "groups": [ { "name": "V", "names": ["color"] } ] },
              "nodeTypes":    { "fallbackName": "?", "types": [ { "id": 0, "enum": "None", "wireName": null } ] },
              "hostEvents":   { "events": [ { "name": "back", "tier": "reserved" } ] },
              "hostCallOps":  { "ops": [
                { "name": "Geolocation", "id": 0, "doc": "the first op" },
                { "name": "{{secondName}}", "id": {{secondId}}, "doc": "the second op" }
              ] }
            }
            """;

        // Rule 3, the positive control through the same detector: distinct ids and names load.
        WireVocabulary ok = WireVocabulary.Load(Manifest(1, "Camera"));
        Assert.Equal(2, ok.HostCallOps.Ops.Length);

        var dupId = Assert.Throws<InvalidDataException>(() => WireVocabulary.Load(Manifest(0, "Camera")));
        Assert.Contains("hostCallOps.id", dupId.Message);

        var dupName = Assert.Throws<InvalidDataException>(() => WireVocabulary.Load(Manifest(1, "Geolocation")));
        Assert.Contains("hostCallOps.name", dupName.Message);
        Assert.Contains("Geolocation", dupName.Message);
    }
}
