using BlazorNative.Renderer;
using BlazorNative.Runtime;
using BlazorNative.WireGen;
using Xunit;

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

public sealed class WireVocabularyCodegenTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;
        Assert.True(dir is not null,
            "could not find BlazorNative.sln above the test binary — this suite reads repo source "
            + "off disk, and a pin that cannot find its subject must fail loudly, never vacuously");
        return dir!.FullName;
    }

    private static WireVocabulary LoadManifest()
        => WireVocabulary.Load(File.ReadAllText(Path.Combine(RepoRoot(), Emitters.ManifestPath)));

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
        string root = RepoRoot();
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
}
