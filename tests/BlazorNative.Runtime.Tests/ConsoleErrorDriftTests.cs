using System.Text.RegularExpressions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ConsoleErrorDriftTests — Phase 11.4 Gate A (M11 DoD #6, #155), design §8.1
// pin 6a: "no bare Console.Error in src/**/*.cs".
//
// WHY A DRIFT PIN AND NOT A CODE REVIEW. Gate A migrated 31 `Console.Error`
// sites onto `BnLog`. Without a pin, site 32 arrives next month, writes to a
// stderr that ANDROID DISCARDS (`ShellBridge.kt:280`: "stderr is /dev/null
// there"), and nothing is red — the message simply never appears on the one
// platform that matters, and the author has no way to know. That is the exact
// failure shape #164 documents, and it is invisible by construction.
//
// THE MECHANISM is `ShellStyleTableDriftTests`' and `ReadmeDriftTests`': a text
// scan of checkout files from the .NET suite, walking up from the test binary to
// the directory holding BlazorNative.sln. `build-test` is the one required lane
// where every source file is checkout-visible. No new infrastructure.
//
// NON-VACUITY IS ASSERTED, NOT ASSUMED (8.1's `return ,$names`, 8.4's "10
// succeeded"). A scan that matched nothing would pass forever while holding
// nothing at all — a `src` that moved, a glob that stopped matching, a regex
// reworded past its subject. So the scan asserts it FOUND files, that those
// files are the real ones (BnLog.cs and Exports.cs are both present), and that
// the pattern still matches where a match is KNOWN to exist.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ConsoleErrorDriftTests
{
    /// <summary>THE ONE FILE ALLOWED TO NAME `Console.Error`: the seam's own
    /// default sink. Every other write goes through it.</summary>
    private const string TheSeam = "BnLog.cs";

    /// <summary>THE SCOPED-OUT FILE, NAMED EXPLICITLY SO THE EXEMPTION IS VISIBLE
    /// RATHER THAN IMPLIED (design §9 / §12).
    ///
    /// `DevHostBridge.cs` holds all 18 `Console.Write*` / `Console.Out` uses in
    /// `src/`. It is the DESKTOP DEV HOST, where stdout genuinely IS the UI —
    /// quieting it would break the dev loop it exists to serve. Those 18 are out of
    /// scope for 11.4 and are NOT this pin's subject.
    ///
    /// Note what that exemption does NOT cover: the file's single `Console.Error`
    /// site was a DIAGNOSTIC on a subscriber-threw fault path — the exact twin of
    /// `NativeShellBridge`'s and `NativeNavigationManager`'s — and it MIGRATED with
    /// the other 30. So the stdout exemption buys the file no exemption here, and
    /// <see cref="TheDevHostStdoutExemption_IsRealAndScopedToStdout"/> holds both
    /// halves of that sentence true.</summary>
    private const string DevHostBridge = "DevHostBridge.cs";

    /// <summary>Matches a `Console.Error` reference in CODE. Comments are excluded
    /// (the seam and this phase's call sites talk ABOUT stderr at length, and a
    /// pattern that cannot tell prose from a call reports the wrong number — the
    /// mistake `ReadmeDriftTests` records having made against ci.yml).</summary>
    private const string ConsoleErrorReference = @"Console\s*\.\s*Error";

    // ── 1. The pin ───────────────────────────────────────────────────────────

    /// <summary>NOT ONE BARE `Console.Error` SURVIVES IN `src/**/*.cs`.
    ///
    /// The 31 sites Gate A migrated wrote to a file descriptor Android sends to
    /// `/dev/null` and iOS does not surface in an unattached Release build. Routing
    /// them through <c>BnLog</c> is what gives them a level, a category, and — once
    /// Gates B and C land the stdio pump — a platform log to arrive in. A new bare
    /// write silently opts out of all three.</summary>
    [Fact]
    public void NoBareConsoleError_SurvivesInSrc()
    {
        var offenders = new List<string>();

        foreach (string file in SourceFiles())
        {
            string name = Path.GetFileName(file);
            if (string.Equals(name, TheSeam, StringComparison.Ordinal)) continue;

            var hits = CodeLines(file)
                .Where(l => Regex.IsMatch(l.Text, ConsoleErrorReference))
                .Select(l => $"  {Relative(file)}:{l.Number}  {l.Text.Trim()}")
                .ToList();

            offenders.AddRange(hits);
        }

        Assert.True(offenders.Count == 0,
            "BARE Console.Error IN src/**/*.cs — it must go through BnLog.\n"
            + string.Join("\n", offenders)
            + "\n\nA bare write has no level (so it cannot be quieted in Release), no category, "
            + "and no route to a platform log: Android sends process stderr to /dev/null "
            + "(ShellBridge.kt:280) and iOS does not surface it in an unattached Release build. "
            + "That is the root cause of BOTH #155 and #164.\n"
            + $"Use BnLog.Error/Warn/Info/Debug/Verbose(category, message). Only {TheSeam} — the "
            + "seam's own default sink — may name Console.Error.\n"
            + $"({DevHostBridge}'s 18 Console.Write* calls are the dev host's UI and are scoped "
            + "out of 11.4 — but that exemption is about STDOUT and buys no exemption here.)");
    }

    // ── 2. Non-vacuity — the scan must be looking at something ───────────────

    /// <summary>THE SCAN ACTUALLY READS FILES, AND THE RIGHT ONES.
    ///
    /// Every assertion above is of the form "found nothing", which is exactly what a
    /// BROKEN scan also reports. This is the counterweight: the file set is
    /// non-empty, it is of a plausible size, and it contains the two files the pin
    /// most needs to be reading — the seam itself and the export surface that held
    /// 15 of the 31 migrated sites.</summary>
    [Fact]
    public void TheScan_IsNotVacuous()
    {
        List<string> files = SourceFiles().ToList();

        Assert.True(files.Count > 20,
            $"the src/**/*.cs scan found only {files.Count} files — it is reading the wrong tree "
            + "and NoBareConsoleError_SurvivesInSrc is passing while blind. Fix the walk, do not "
            + "delete the pin.");

        Assert.Contains(files, f => Path.GetFileName(f) == TheSeam);
        Assert.Contains(files, f => Path.GetFileName(f) == "Exports.cs");
        Assert.Contains(files, f => Path.GetFileName(f) == DevHostBridge);
    }

    /// <summary>…AND THE PATTERN STILL MATCHES WHERE A MATCH IS KNOWN TO EXIST.
    ///
    /// A file-set that is non-empty proves the walk works; it does not prove the
    /// REGEX does. `BnLog.cs` is the one file that must contain a live
    /// `Console.Error` call — the default sink — so it is the fixed point that
    /// proves the detector detects. Reword the pattern past its subject and this
    /// reds, instead of the pin quietly going green forever.</summary>
    [Fact]
    public void ThePattern_StillMatchesTheSeamsOwnDefaultSink()
    {
        string seam = SourceFiles().Single(f => Path.GetFileName(f) == TheSeam);

        var hits = CodeLines(seam)
            .Where(l => Regex.IsMatch(l.Text, ConsoleErrorReference))
            .ToList();

        Assert.True(hits.Count > 0,
            $"the Console.Error pattern matched NOTHING in {TheSeam}, which is the one file that "
            + "MUST contain a live Console.Error call (BnLog's default sink). Either the sink "
            + "moved — then re-point this pin deliberately — or the pattern no longer matches a "
            + "real call, in which case NoBareConsoleError_SurvivesInSrc is holding nothing.");
    }

    /// <summary>THE EXEMPTION, MADE VISIBLE. Two halves, both asserted:
    /// <list type="number">
    /// <item>`DevHostBridge.cs` still holds the `Console.Write*` sites that ARE the
    /// exemption — if they ever leave, the exemption is stale prose and should be
    /// deleted rather than kept as folklore;</item>
    /// <item>the exemption is scoped to STDOUT — the file's `Console.Error`
    /// diagnostic migrated with the other 30, and is not silently readmitted.</item>
    /// </list></summary>
    [Fact]
    public void TheDevHostStdoutExemption_IsRealAndScopedToStdout()
    {
        string file = SourceFiles().Single(f => Path.GetFileName(f) == DevHostBridge);
        List<(int Number, string Text)> code = CodeLines(file).ToList();

        int stdout = code.Count(l => Regex.IsMatch(l.Text, @"Console\s*\.\s*(Write|Out)"));
        Assert.True(stdout > 0,
            $"{DevHostBridge} no longer writes to stdout, so the 11.4 scope-out that names it is "
            + "stale. Delete the exemption rather than leaving prose that protects nothing.");

        Assert.DoesNotContain(code, l => Regex.IsMatch(l.Text, ConsoleErrorReference));
    }

    // ── the scanner ──────────────────────────────────────────────────────────

    /// <summary>Every `.cs` under `src/`, excluding build output. Fails loudly if
    /// `src/` is not there at all — a missing tree must break this test, not
    /// silently pass it with an empty set.</summary>
    private static IEnumerable<string> SourceFiles()
    {
        string src = Path.Combine(RepoRoot(), "src");
        Assert.True(Directory.Exists(src), $"src/ not found under the repo root: {src}");

        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                    StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                    StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    /// <summary>The file's lines that are CODE — line comments and the file's
    /// banner blocks are dropped, because this phase's own sources discuss
    /// `Console.Error` at length and a scanner that cannot tell prose from a call
    /// counts the documentation as offences.</summary>
    private static IEnumerable<(int Number, string Text)> CodeLines(string file)
    {
        bool inBlockComment = false;
        int number = 0;

        foreach (string raw in File.ReadLines(file))
        {
            number++;
            string line = raw;

            if (inBlockComment)
            {
                int close = line.IndexOf("*/", StringComparison.Ordinal);
                if (close < 0) continue;
                inBlockComment = false;
                line = line[(close + 2)..];
            }

            int open = line.IndexOf("/*", StringComparison.Ordinal);
            if (open >= 0)
            {
                inBlockComment = line.IndexOf("*/", open, StringComparison.Ordinal) < 0;
                line = line[..open];
            }

            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0) line = line[..slashes];

            if (line.Trim().Length == 0) continue;
            yield return (number, line);
        }
    }

    private static string Relative(string file)
        => Path.GetRelativePath(RepoRoot(), file).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>The repo root — the nearest ancestor of the test binary holding
    /// BlazorNative.sln. `src/` is not a build input of this project, so it is read
    /// from the checkout (which is what makes `build-test` the only lane that can
    /// host this test). Same walk as `ShellStyleTableDriftTests` and
    /// `ReadmeDriftTests`.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
