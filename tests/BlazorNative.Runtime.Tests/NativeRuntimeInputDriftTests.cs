using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NativeRuntimeInputDriftTests — a changed native library reruns the JVM tests (#424).
//
// Gradle decides a Test task is UP-TO-DATE from its declared inputs. The native
// BlazorNative.Runtime library that JNA loads through `jna.library.path` is not
// a classpath entry, so unless the task declares it, a rebuilt library leaves
// the task up to date and the old result stands. The pin: every top-level block
// in src/BlazorNative.Jni/build.gradle.kts that sets `jna.library.path` also
// declares an `inputs.files(` or `inputs.dir(` call with relative path
// sensitivity.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - That the declared input is the directory `jna.library.path` points at. It
//     checks the declaration exists, not that the two name the same path.
//   - A block that loads the library through some other property than
//     `jna.library.path`.
//   - A block nested inside another top-level block; the scan splits at lines
//     that start with a closing brace in column one.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Every Gradle task block that sets jna.library.path declares the native runtime as an input.</summary>
public sealed class NativeRuntimeInputDriftTests
{
    private static readonly Regex BlockEnd =
        new(@"^}\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex InputCall =
        new(@"\binputs\.(files|dir)\(", RegexOptions.CultureInvariant);
    private static readonly Regex RelativeSensitivity =
        new(@"\.withPathSensitivity\(\s*PathSensitivity\.RELATIVE\s*\)", RegexOptions.CultureInvariant);

    /// <summary>Splits a script into top-level blocks, each ending at a closing brace in column one.</summary>
    private static List<string> Blocks(string gradle)
    {
        var blocks = new List<string>();
        var start = 0;
        foreach (Match end in BlockEnd.Matches(gradle))
        {
            blocks.Add(gradle[start..(end.Index + end.Length)]);
            start = end.Index + end.Length;
        }
        return blocks;
    }

    private static List<string> WithJnaPath(string gradle) =>
        Blocks(gradle).Where(b => b.Contains("jna.library.path", StringComparison.Ordinal)).ToList();

    private static List<string> Offenders(string gradle) =>
        WithJnaPath(gradle)
            .Where(b => !(InputCall.IsMatch(b) && RelativeSensitivity.IsMatch(b)))
            .Select(b => b.Split('\n').First(l => l.TrimEnd().EndsWith('{')).Trim())
            .ToList();

    /// <summary>The Test configuration and both JavaExec tasks declare the native runtime as an input.</summary>
    [Fact]
    public void TheJvmTasksThatLoadTheRuntime_DeclareItAsAnInput()
    {
        var gradle = File.ReadAllText(Path.Combine(BnRepo.Root(), "src", "BlazorNative.Jni", "build.gradle.kts"));

        Assert.True(WithJnaPath(gradle).Count >= 3,
            "fewer than 3 task blocks set jna.library.path in build.gradle.kts. The Test configuration and the "
            + "runPreviewHost and runInspectorHost tasks are expected, so the scan may be reading nothing.");

        var offenders = Offenders(gradle);
        Assert.True(offenders.Count == 0,
            "a task that sets jna.library.path does not declare the native runtime as an input (#424), so a rebuilt "
            + "library leaves it UP-TO-DATE: " + string.Join("; ", offenders)
            + ". Add inputs.files(...).withPathSensitivity(PathSensitivity.RELATIVE).");
    }

    /// <summary>Non-vacuity: a block without the input is reported, and one with it is not.</summary>
    [Fact]
    public void APlantedBlockWithoutTheInput_IsReported()
    {
        const string withInput = "tasks.withType<Test>().configureEach {\n"
            + "    systemProperty(\"jna.library.path\", p)\n"
            + "    inputs.files(fileTree(p)).withPathSensitivity(PathSensitivity.RELATIVE)\n}\n";
        const string withoutInput = "tasks.register<JavaExec>(\"planted\") {\n"
            + "    systemProperty(\"jna.library.path\", p)\n}\n";
        const string wrongSensitivity = "tasks.register<JavaExec>(\"planted2\") {\n"
            + "    systemProperty(\"jna.library.path\", p)\n    inputs.files(fileTree(p))\n}\n";

        Assert.Empty(Offenders(withInput));
        Assert.Single(Offenders(withInput + withoutInput));
        Assert.Single(Offenders(withInput + wrongSensitivity));
    }
}
