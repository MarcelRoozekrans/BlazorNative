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
//
// ── AND THE TABLE IS NOT THE WHOLE CONTRACT: THE DISPATCH BEHIND IT ──────────
// (Gate 3 review, I4.) A name being ON the table only says the shell ROUTES it to
// Yoga. Whether a SETTER exists at the other end is a separate fact, and in both
// shells a name with no arm behaves exactly like a rejected value: the `.mm`'s
// if/else chain falls through and returns 0, Kotlin's `when` lands on
// `else -> logIgnore("routing bug")`. A style .NET emits, the wire carries, and one
// shell silently drops. Two more `[Fact]`s below close it:
//
//   - iOS is pinned at RUNTIME (`BnYogaStyleParserTests.testEveryRoutedNameReachesASetter`
//     feeds every routed name a legal value and demands rc == 1); what is pinned HERE
//     is that test's own hand-written name list, so the pin cannot quietly stop
//     covering a name.
//   - Kotlin's `setStyle` returns Unit — there is no rc to demand — so its dispatch is
//     pinned at the SOURCE: every routed name must appear as a `when` literal.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ShellStyleTableDriftTests
{
    private const string KotlinYogaLayout =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt";

    private const string AppleYogaLayout =
        "src/BlazorNative.Apple/BnHost/BnYogaLayout.mm";

    private const string AppleStyleParserTests =
        "src/BlazorNative.Apple/BnHostTests/BnYogaStyleParserTests.swift";

    /// <summary>The Kotlin declaration — anchored at line start, so a mention of the
    /// name in a comment cannot be mistaken for the table itself.</summary>
    private const string KotlinYogaStylesDeclaration =
        @"(?m)^\s*private val YOGA_STYLES = setOf\((?<body>[^)]*)\)";

    /// <summary>The Objective-C++ declaration — anchored at line start for the same
    /// reason (the .mm's file header talks ABOUT the table at length).</summary>
    private const string AppleYogaStylesDeclaration =
        @"(?m)^static const char\* const kYogaStyles\[\] = \{(?<body>[^}]*)\}";

    /// <summary>The XCTest suite's own copy of the routing table (Gate 3 review, I4).</summary>
    private const string AppleParserTestNamesDeclaration =
        @"(?m)^\s*static let routedStyleNames: \[String\] = \[(?<body>[^\]]*)\]";

    /// <summary>Kotlin's `setStyle` body: from the declaration to the first line that
    /// is exactly a 4-space-indented `}` — the function's own closing brace (every
    /// brace inside it is indented deeper).</summary>
    private const string KotlinSetStyleBody =
        @"(?ms)^    fun setStyle\(nodeId: Int, property: String, value: String\?\) \{(?<body>.*?)^    \}";

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

    /// <summary>THE XCTEST'S OWN MAP MUST NOT DRIFT (Gate 3 review, I4).
    ///
    /// `BnYogaStyleParserTests.testEveryRoutedNameReachesASetter` is what pins the iOS
    /// dispatch chain — it feeds every routed name a legal value and demands the setter
    /// ACCEPT it, which is the only thing a missing if/else arm cannot do (the chain's
    /// fall-through returns 0, exactly as a rejected VALUE does, so every rejection
    /// test in that file passes whether the arm exists or not).
    ///
    /// But it iterates a list written by hand in Swift. If a name joins the routing
    /// table and not that list, the arm it needs goes untested and the pin quietly
    /// stops covering it. So the list is pinned here, in the one lane that can see both
    /// files — the same mechanism, and for the same reason, as the tables above.</summary>
    [Fact]
    public void AppleParserTestsRoutedNames_AreExactlyTheRenderersYogaHalf()
    {
        var names = ParseNameTable(AppleStyleParserTests, AppleParserTestNamesDeclaration);

        Assert.True(
            names.SetEquals(NativeRenderer.YogaStyleAttributes),
            "BnYogaStyleParserTests.routedStyleNames must mirror NativeRenderer.YogaStyleAttributes "
            + "exactly — it is the list the DISPATCH-CHAIN pin iterates.\n"
            + $"  only in .NET   : {Join(NativeRenderer.YogaStyleAttributes.Except(names))}\n"
            + $"  only in XCTest : {Join(names.Except(NativeRenderer.YogaStyleAttributes))}\n"
            + "A routed name missing from that list is a Yoga setter arm NOBODY asserts exists, "
            + "and a missing arm is a style silently dropped on iOS alone.");
    }

    /// <summary>THE SAME HOLE, ON THE KOTLIN SIDE — pinned at the SOURCE because Kotlin
    /// has no rc to pin it at runtime.
    ///
    /// `YogaLayout.setStyle` is a `when (property)` whose `else` arm logs and ignores
    /// ("routing bug — owns() said it was"). So a name in `YOGA_STYLES` with no `when`
    /// arm behind it does exactly what a name missing from the table does: nothing,
    /// quietly, on one platform. iOS pins this at runtime (the `.mm` returns an rc, and
    /// `testEveryRoutedNameReachesASetter` demands a 1); Kotlin's `setStyle` returns
    /// Unit, so the equivalent runtime assertion would need an instrumented device and
    /// a new signature. It is pinned here instead: every name the shell ROUTES to Yoga
    /// must appear as a dispatch literal inside `setStyle`'s own body.
    ///
    /// Weaker than the iOS pin, and honestly so: this proves the arm is WRITTEN, not
    /// that it reaches a setter. It still catches the failure that actually happens —
    /// a name added to the table and to .NET, and forgotten in the `when`.</summary>
    [Fact]
    public void AndroidSetStyleDispatch_HasAnArmForEveryYogaStyle()
    {
        var routed = ParseNameTable(KotlinYogaLayout, KotlinYogaStylesDeclaration);
        var dispatched = ParseNameTable(KotlinYogaLayout, KotlinSetStyleBody);

        var missing = routed.Except(dispatched).ToList();

        Assert.True(
            missing.Count == 0,
            $"YogaLayout.setStyle has no `when` arm for: {Join(missing)}.\n"
            + "The name is on YOGA_STYLES, so `owns()` routes it to the Yoga node — and the "
            + "`when` drops it on `else -> logIgnore(\"routing bug\")`. .NET emits the style, the "
            + "wire carries it, Android ignores it and iOS honours it: the two frame tables "
            + "disagree and the ENGINE gets the blame.");
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
