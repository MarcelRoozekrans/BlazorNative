using System.Text.RegularExpressions;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ShellStyleTableDriftTests — Phase 6.1 Gate 2 review (finding I1), REDUCED BY
// #255 to the one thing codegen cannot do.
//
// ── WHAT THIS FILE USED TO BE, AND WHY MOST OF IT IS GONE ────────────────────
// The SetStyle allow-list is the shells' ROUTING TABLE, and it used to exist as
// four hand-written copies: .NET's set, Kotlin's `YOGA_STYLES`, the `.mm`'s
// `kYogaStyles`, and the Swift test suite's own `routedStyleNames`. This file
// held seven [Fact]s that parsed those literals back out of the source with
// regexes and asserted they agreed — plus two more for the scroll-ignore pair.
//
// That was the right pin for hand-written mirrors, and it caught real drift. But
// it could only ever catch divergence AFTER somebody wrote it, and it required
// every copy to keep a parser-friendly shape — the reason those files carried
// instructions like "keep the declaration a plain `setOf` of quoted names,
// declared at the start of its line".
//
// #255 made the divergence unrepresentable instead: src/wire-vocabulary.json is
// the only place the names live, and tools/BlazorNative.WireGen emits every
// copy. Set-equality between copies is no longer a fact worth asserting — they
// are the same bytes from the same source, and WireVocabularyCodegenTests
// (BlazorNative.Runtime.Tests) proves it by re-running the emitters in-process
// and byte-comparing the committed output.
//
// So the seven mirror-comparison facts are DELETED rather than re-pointed. A
// test asserting that two generated files agree would assert only that the
// generator is deterministic, which is not the property anybody was worried
// about.
//
// ── WHAT SURVIVES, AND WHY IT MUST ───────────────────────────────────────────
// One fact, because it pins something the manifest cannot: **being ON the table
// only says the shell ROUTES the name to Yoga — whether a SETTER exists at the
// other end is a separate fact.** A routed name with no dispatch arm behaves
// exactly like a rejected one: Kotlin's `when` lands on
// `else -> logIgnore("routing bug")`, and .NET emits the style, the wire carries
// it, Android drops it and iOS honours it. Two frame tables disagree and the
// ENGINE gets the blame.
//
// Codegen makes that failure MORE likely, not less: adding a name to the
// manifest now updates four tables in one command, and the one thing it cannot
// write for you is the implementation. This test is what makes that half loud.
//
// iOS's equivalent is pinned at RUNTIME, in its own lane, by
// BnYogaStyleParserTests.testEveryRoutedNameReachesASetter — which feeds every
// routed name a legal value and demands rc == 1. Since #255 that suite reads the
// generated BnWireVocabulary rather than its own hand-copy, so it can no longer
// quietly stop covering a name.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ShellStyleTableDriftTests
{
    private const string KotlinYogaLayout =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt";

    /// <summary>The VISUAL dispatch lives in the widget mapper, not the Yoga layout —
    /// the two halves of the partition are routed in two different files, which is a
    /// large part of why only one of them had a pin.</summary>
    private const string KotlinWidgetMapper =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/WidgetMapper.kt";

    /// <summary>Kotlin's `setStyle` body: from the declaration to the first line that
    /// is exactly a 4-space-indented `}` — the function's own closing brace (every
    /// brace inside it is indented deeper).</summary>
    private const string KotlinSetStyleBody =
        @"(?ms)^    fun setStyle\(nodeId: Int, property: String, value: String\?\) \{(?<body>.*?)^    \}";

    /// <summary>THE DISPATCH PIN — the half of the contract the routing table does
    /// not carry (Gate 3 review, I4; kept and re-argued by #255).
    ///
    /// Kotlin's `setStyle` returns Unit, so there is no return code to demand the way
    /// the iOS parser test does. Its dispatch is pinned at the SOURCE instead: every
    /// name the manifest routes to Yoga must appear as a `when` literal inside
    /// `setStyle`'s own body.
    ///
    /// Weaker than the iOS pin, and honestly so: this proves the arm is WRITTEN, not
    /// that it reaches a setter. It still catches the failure that actually happens —
    /// and #255 makes that failure easier to cause, because adding a name to the
    /// manifest now updates every TABLE in one command while leaving every
    /// IMPLEMENTATION untouched.</summary>
    [Fact]
    public void AndroidSetStyleDispatch_HasAnArmForEveryYogaStyle()
    {
        // The routed set comes from the GENERATED vocabulary now, not from a regex
        // over Kotlin source: the Kotlin table is `BnWireVocabulary.YOGA_STYLES`, so
        // there is no literal left in that file to parse. Reading .NET's set is the
        // same question asked of the same source of truth.
        var routed = NativeRenderer.YogaStyleAttributes;
        var dispatched = ParseNameTable(KotlinYogaLayout, KotlinSetStyleBody);

        var missing = routed.Except(dispatched).ToList();

        Assert.True(
            missing.Count == 0,
            $"YogaLayout.setStyle has no `when` arm for: {Join(missing)}.\n"
            + "The name is in the wire vocabulary, so `owns()` routes it to the Yoga node — and "
            + "the `when` drops it on `else -> logIgnore(\"routing bug\")`. .NET emits the style, "
            + "the wire carries it, Android ignores it and iOS honours it: the two frame tables "
            + "disagree and the ENGINE gets the blame.\n"
            + "Adding a name to src/wire-vocabulary.json updates every TABLE; it cannot write the "
            + "SETTER for you, and this is the test that says so.");
    }

    /// <summary>Kotlin's `handleSetStyle` body — the VISUAL dispatch, which routes
    /// every name `owns()` did NOT claim.</summary>
    private const string KotlinHandleSetStyleBody =
        @"(?ms)^    private fun handleSetStyle\(p: RenderPatch\.SetStyle\) \{(?<body>.*?)^    \}";

    /// <summary>THE VISUAL-HALF DISPATCH PIN — the hole this file did not cover, and
    /// the reason three names sat in the table for months doing nothing.
    ///
    /// The Yoga half has been pinned since Phase 6.1 by the fact above. The VISUAL
    /// half never was — and an audit found the consequence: `color`, `fontWeight`,
    /// `background` and `style` were all in `VisualStyleAttributes`, all pinned there
    /// by StyleAttributePartitionTests as "belonging to the view", and **none of them
    /// had an arm in either shell**. Every use was accepted by the routing table and
    /// then dropped on `else -> "not yet supported"`. That is exactly the failure the
    /// wire-vocabulary manifest's own documentation says the apparatus exists to
    /// prevent, and it was live the whole time because the assertion covered one half
    /// of a two-half partition.
    ///
    /// So this is the missing symmetry, not a new idea: **every VISUAL name must have
    /// a dispatch arm too.** A name with no producer is a defensible thing to ledger;
    /// a name the table *accepts* with no arm is not.</summary>
    [Fact]
    public void AndroidSetStyleDispatch_HasAnArmForEveryVisualStyle()
    {
        var visual = NativeRenderer.VisualStyleAttributes;
        var dispatched = ParseNameTable(KotlinWidgetMapper, KotlinHandleSetStyleBody, "handleSetStyle");

        var missing = visual.Except(dispatched).ToList();

        Assert.True(
            missing.Count == 0,
            $"WidgetMapper.handleSetStyle has no arm for: {Join(missing)}.\n"
            + "The name is in VisualStyleAttributes, so the renderer routes it to the SetStyle "
            + "wire and `owns()` declines it — which lands it on "
            + "`else -> \"SetStyle … not yet supported\"`. The style is accepted and then DROPPED, "
            + "silently, on every frame.\n"
            + "Either implement the arm in BOTH shells, or remove the name from "
            + "src/wire-vocabulary.json. A name in the table with no arm is the one state that is "
            + "not allowed.");
    }

    // ── The parser ───────────────────────────────────────────────────────────

    /// <summary>Every quoted name inside the declaration <paramref name="pattern"/>
    /// matches in the shell source at <paramref name="relativePath"/>. Fails loudly
    /// when the declaration cannot be found: a moved function must break this test,
    /// not silently pass it with an empty set.</summary>
    private static HashSet<string> ParseNameTable(string relativePath, string pattern, string what = "setStyle")
    {
        var source = ReadShellSource(relativePath);
        var match = Regex.Match(source, pattern, RegexOptions.Singleline);

        Assert.True(match.Success,
            $"could not find `{what}` in {relativePath} (pattern: {pattern}). It moved or its "
            + "signature changed — this dispatch pin IS the contract, so re-point it deliberately "
            + "rather than deleting it.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match name in Regex.Matches(match.Groups["body"].Value, "\"([^\"]+)\""))
            names.Add(name.Groups[1].Value);

        Assert.NotEmpty(names);
        return names;
    }

    private static string ReadShellSource(string relativePath)
    {
        var file = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"shell source not found: {file}");
        return File.ReadAllText(file);
    }

    /// <summary>The repo root — the nearest ancestor of the test binary holding
    /// BlazorNative.sln. The shells' sources are not build inputs of this project,
    /// so they are read from the checkout (which is what makes `build-test` the only
    /// lane that can host this test).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Join(IEnumerable<string> names)
    {
        var list = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }
}
