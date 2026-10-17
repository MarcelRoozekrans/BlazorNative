using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PollUntilDriftTests — one pollUntil in the instrumented tests (#458).
//
// Ten test classes each carried a private pollUntil, and only one of them named a
// destroyed activity; the other nine surfaced ActivityScenario's bare
// NullPointerException. The pin: no `fun pollUntil` is declared under
// src/BlazorNative.Jni/src/androidTest outside Poll.kt.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - That a caller uses Poll.kt's function and not a differently named loop of
//     its own; only the name pollUntil is scanned.
//   - A declaration whose name is backticked, or a function with another name.
//   - A declaration inside a comment, which reds as a false positive.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The instrumented tests share one pollUntil.</summary>
public sealed class PollUntilDriftTests
{
    private static readonly Regex Declaration =
        new(@"\bfun\s+(<[^>]*>\s*)?([\w<>?, .]+\.)?pollUntil\s*(<[^>]*>)?\s*\(", RegexOptions.CultureInvariant);

    private static readonly string[] SharedHome = ["Poll.kt"];

    private static List<string> Offenders(IEnumerable<(string Name, string Text)> files) =>
        files.Where(f => !SharedHome.Contains(f.Name, StringComparer.Ordinal) && Declaration.IsMatch(f.Text))
            .Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>No test class declares its own pollUntil.</summary>
    [Fact]
    public void NoPrivatePollUntil_RemainsInAndroidTest()
    {
        var dir = Path.Combine(BnRepo.Root(), "src", "BlazorNative.Jni", "src", "androidTest");
        var files = Directory.EnumerateFiles(dir, "*.kt", SearchOption.AllDirectories)
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f))).ToList();

        Assert.True(files.Any(f => f.Name == "Poll.kt" && Declaration.IsMatch(f.Text)),
            "Poll.kt does not declare pollUntil, so the scan may be reading the wrong tree.");

        var offenders = Offenders(files);
        Assert.True(offenders.Count == 0,
            "these instrumented tests declare their own pollUntil (#458); one that does not check for a destroyed activity "
            + "surfaces a bare NullPointerException: " + string.Join(", ", offenders) + ". Use Poll.kt.");
    }

    /// <summary>Non-vacuity: every spelling of a private copy is reported, a similar name is not, and the shared home is exempt.</summary>
    [Fact]
    public void APlantedPrivateCopy_IsReported()
    {
        string[] copies =
        [
            "    private fun pollUntil(\n        scenario: ActivityScenario<MainActivity>,\n",
            "    private fun <T> pollUntil(scenario: T): Boolean = false",
            "    private fun pollUntil<T>(scenario: T): Boolean = false",
            "    private fun ActivityScenario<MainActivity>.pollUntil(deadlineMs: Long): Boolean = false",
        ];
        foreach (var copy in copies)
            Assert.Equal(["Planted.kt"], Offenders([("Planted.kt", copy), ("Poll.kt", copy), ("Other.kt", "val x = 1")]));

        Assert.Empty(Offenders([("Similar.kt",
            "private fun pollUntilIdle(a: Int): Boolean = false\nprivate fun pollUntilBackNavigatedAway(a: Int) = false\nval x = pollUntil(s, 1) { true }")]));
    }
}
