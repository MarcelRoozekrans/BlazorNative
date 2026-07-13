using System.Text.RegularExpressions;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ShellStyleTableDriftTests — Phase 6.1 Gate 2 review (finding I1).
//
// The SetStyle allow-list is the SHELLS' ROUTING TABLE (see
// StyleAttributePartitionTests), and each shell carries its own HAND-WRITTEN
// mirror of the layout half: Kotlin's `YogaLayout.YOGA_STYLES` today, iOS's
// `BnYogaLayout.mm` name table from Gate 3. Three copies of one contract.
//
// A name that exists on the .NET side and is MISSING from a shell's mirror does
// not fail loudly. It falls through the shell's router into the VISUAL branch,
// where it lands on `else -> Log.w("not yet supported")` — i.e. the style is
// SILENTLY DROPPED, and the only symptom is a frame that is quietly wrong on one
// platform. That is precisely the class of contract this repo pins with drift
// tests and never with a human re-reading two files: the 48-byte patch struct,
// the nine-export surface, the Yoga version pin (ci.yml) are all held this way.
//
// It lives here, in the .NET suite, because `build-test` is the ONE required lane
// where every file is checkout-visible: neither shell's own lane can see the
// other's source (the Android lane has no Xcode, the iOS lane has no Gradle), and
// build-test can see them both. This is the pattern the implementation plan's Gate
// 4 Task 4.0 recommends for the FRAME TABLE — built here, one gate early, so the
// name table and the frame table are pinned the same way.
//
// ── GATE 3 LANDED: THE THIRD MIRROR IS PINNED HERE TOO ───────────────────────
// `src/BlazorNative.Apple/BnHost/BnYogaLayout.mm` carries the iOS shell's own
// string→setter name table (`kYogaStyles`), and it is asserted below by the SAME
// parser and the SAME two assertions as Kotlin's. The parser is deliberately
// source-format-agnostic (find the declaration, take every quoted name inside its
// braces/parens), so the `.mm`'s C array needed no new machinery — which is the
// point: a fourth shell adds two `[Fact]`s, never a fourth hand-copy nobody checks.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ShellStyleTableDriftTests
{
    private const string KotlinYogaLayout =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt";

    private const string AppleYogaLayout =
        "src/BlazorNative.Apple/BnHost/BnYogaLayout.mm";

    /// <summary>The Kotlin declaration — anchored at line start, so a mention of the
    /// name in a comment cannot be mistaken for the table itself.</summary>
    private const string KotlinYogaStylesDeclaration =
        @"(?m)^\s*private val YOGA_STYLES = setOf\((?<body>[^)]*)\)";

    /// <summary>The Objective-C++ declaration — anchored at line start for the same
    /// reason (the .mm's file header talks ABOUT the table at length).</summary>
    private const string AppleYogaStylesDeclaration =
        @"(?m)^static const char\* const kYogaStyles\[\] = \{(?<body>[^}]*)\}";

    /// <summary>THE DRIFT PIN. The Android shell's layout-style table must be
    /// exactly `NativeRenderer.YogaStyleAttributes` — no more (a name the renderer
    /// never emits is dead parser code), no less (a name the renderer DOES emit and
    /// the shell does not know is a silently dropped style).</summary>
    [Fact]
    public void AndroidYogaStyles_AreExactlyTheRenderersYogaHalf()
    {
        var kotlin = ParseNameTable(KotlinYogaLayout, KotlinYogaStylesDeclaration);

        Assert.True(
            kotlin.SetEquals(NativeRenderer.YogaStyleAttributes),
            "YogaLayout.kt's YOGA_STYLES must mirror NativeRenderer.YogaStyleAttributes exactly.\n"
            + $"  only in .NET  : {Join(NativeRenderer.YogaStyleAttributes.Except(kotlin))}\n"
            + $"  only in Kotlin: {Join(kotlin.Except(NativeRenderer.YogaStyleAttributes))}\n"
            + "A name .NET emits and the shell does not know is not a loud failure: the shell's "
            + "router sends it to the VISUAL branch, which logs 'not yet supported' and DROPS it. "
            + "Add the name to both, in the same commit — and to BnYogaLayout.mm (Gate 3).");
    }

    /// <summary>…and it must be DISJOINT from the visual half. A name in both is a
    /// double-apply: `padding` reaching both the Yoga node (which insets the
    /// children) and `view.setPadding` (which insets them again) is the exact bug
    /// the partition exists to prevent.</summary>
    [Fact]
    public void AndroidYogaStyles_ContainNoVisualStyleName()
    {
        var kotlin = ParseNameTable(KotlinYogaLayout, KotlinYogaStylesDeclaration);
        var overlap = kotlin.Intersect(NativeRenderer.VisualStyleAttributes, StringComparer.Ordinal);

        Assert.True(
            !overlap.Any(),
            $"a style name routed to Yoga must not ALSO be a visual name: {Join(overlap)}. "
            + "The shell would apply it twice — once as layout, once as paint.");
    }

    /// <summary>THE SAME DRIFT PIN, on the THIRD mirror (Phase 6.1 Gate 3).
    /// `BnYogaLayout.mm`'s `kYogaStyles` is what `BnWidgetMapper.handleSetStyle`
    /// routes on (via `bn_yoga_is_layout_style`), so a name missing from it lands in
    /// the iOS shell's VISUAL branch, is logged "not yet supported", and the style is
    /// SILENTLY DROPPED — on ONE platform. Which surfaces as "Android and iOS
    /// disagree", i.e. as the exact bug DoD #2 exists to catch, with the engine taking
    /// the blame for a hand-copy.</summary>
    [Fact]
    public void AppleYogaStyles_AreExactlyTheRenderersYogaHalf()
    {
        var apple = ParseNameTable(AppleYogaLayout, AppleYogaStylesDeclaration);

        Assert.True(
            apple.SetEquals(NativeRenderer.YogaStyleAttributes),
            "BnYogaLayout.mm's kYogaStyles must mirror NativeRenderer.YogaStyleAttributes exactly.\n"
            + $"  only in .NET : {Join(NativeRenderer.YogaStyleAttributes.Except(apple))}\n"
            + $"  only in the .mm: {Join(apple.Except(NativeRenderer.YogaStyleAttributes))}\n"
            + "A name .NET emits and the shell does not know is not a loud failure: the iOS "
            + "router sends it to the VISUAL branch, which logs 'not yet supported' and DROPS it. "
            + "Add the name to all three, in the same commit.");
    }

    /// <summary>…and the iOS table must be DISJOINT from the visual half, for the same
    /// double-apply reason: `padding` reaching both the Yoga node (which insets the
    /// children) and the UIView's `layoutMargins` (which insets them again) is exactly
    /// the bug the partition — and Gate 3's deletion of that arm — exists to prevent.</summary>
    [Fact]
    public void AppleYogaStyles_ContainNoVisualStyleName()
    {
        var apple = ParseNameTable(AppleYogaLayout, AppleYogaStylesDeclaration);
        var overlap = apple.Intersect(NativeRenderer.VisualStyleAttributes, StringComparer.Ordinal);

        Assert.True(
            !overlap.Any(),
            $"a style name routed to Yoga must not ALSO be a visual name: {Join(overlap)}. "
            + "The shell would apply it twice — once as layout, once as paint.");
    }

    /// <summary>The two shells' tables must be identical to each other, which follows
    /// from the two set-equality facts above — but stated directly, because THIS is the
    /// sentence DoD #2 rests on and a reader should not have to derive it. It is also
    /// the assertion that survives if the .NET set is ever refactored out from under
    /// the other two.</summary>
    [Fact]
    public void TheTwoShellsYogaTables_AreIdenticalToEachOther()
    {
        var kotlin = ParseNameTable(KotlinYogaLayout, KotlinYogaStylesDeclaration);
        var apple = ParseNameTable(AppleYogaLayout, AppleYogaStylesDeclaration);

        Assert.True(
            kotlin.SetEquals(apple),
            "the Android and iOS shells must route the SAME style names to Yoga.\n"
            + $"  only in Kotlin : {Join(kotlin.Except(apple))}\n"
            + $"  only in the .mm: {Join(apple.Except(kotlin))}\n"
            + "A name one shell honours and the other drops makes the two frame tables "
            + "disagree — the failure reads as 'the engine is broken' and is not.");
    }

    // ── The parser ───────────────────────────────────────────────────────────

    /// <summary>Every quoted name inside the declaration <paramref name="pattern"/>
    /// matches in the shell source at <paramref name="relativePath"/>. Fails loudly
    /// when the declaration cannot be found: a moved table must break this test, not
    /// silently pass it with an empty set.</summary>
    private static HashSet<string> ParseNameTable(string relativePath, string pattern)
    {
        var file = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"shell source not found: {file}");

        var match = Regex.Match(File.ReadAllText(file), pattern, RegexOptions.Singleline);
        Assert.True(match.Success,
            $"could not find the style-name table in {relativePath} (pattern: {pattern}). "
            + "It moved or was renamed — this drift test IS the contract, so re-point it "
            + "deliberately rather than deleting it.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match name in Regex.Matches(match.Groups["body"].Value, "\"([^\"]+)\""))
            names.Add(name.Groups[1].Value);

        Assert.NotEmpty(names);
        return names;
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
