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
// ── GATE 3: EXTEND THIS FILE, DO NOT COPY IT ─────────────────────────────────
// When `src/BlazorNative.Apple/BnHost/BnYogaLayout.mm` lands with its own
// string→setter name table, add ONE arm: a `ParseNameTable(AppleYogaLayout, …)`
// call and the same two assertions against it. The parser below is deliberately
// source-format-agnostic (find the declaration, take every quoted name inside its
// braces/parens) so the `.mm`'s `static const char* kYogaStyles[] = { "…" };`
// needs no new machinery.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ShellStyleTableDriftTests
{
    private const string KotlinYogaLayout =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt";

    /// <summary>The Kotlin declaration — anchored at line start, so a mention of the
    /// name in a comment cannot be mistaken for the table itself.</summary>
    private const string KotlinYogaStylesDeclaration =
        @"(?m)^\s*private val YOGA_STYLES = setOf\((?<body>[^)]*)\)";

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
