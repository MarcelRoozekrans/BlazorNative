using System.Text.RegularExpressions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NSLogDriftTests — Phase 11.4 Gate C (M11 DoD #6, #155), design §8.1 pin 6b:
// "no bare NSLog under src/BlazorNative.Apple/BnHost/**".
//
// ⚠ THIS PIN IS DOING MORE WORK THAN ITS ANDROID TWIN, AND THE REASON IS WORTH
// STATING. The Swift/Objective-C++ half of this framework is built by exactly one
// thing — .github/workflows/ios.yml, on a macOS runner. It is not built on a
// Windows or Linux dev machine, it is not built by `dotnet test`, and it is not
// built by the required `build-test` lane. So for anyone without a Mac, THE ONLY
// FEEDBACK ABOUT THE iOS SHELL THAT ARRIVES BEFORE CI IS A TEXT SCAN LIKE THIS
// ONE. That makes a source-scanning .NET test unusually valuable here rather than
// merely tidy: it is the sole pre-CI signal, and it runs everywhere the .NET
// suite runs.
//
// WHAT IT GUARDS. Gate C rewrote 78 shipped `NSLog` sites onto `BnLog` (os_log /
// os.Logger). `NSLog` is unconditional — no level, never compiled out, always
// written at the equivalent of `.default` — AND IT IS ALWAYS PUBLIC: every
// exception description, keychain key, image URL and file path it is handed is
// readable in any log collected off the device. That second property is the half
// of #155 that level gating cannot fix, and it is the half a re-introduced
// `NSLog` would silently give back. Site 79 must red the PR that adds it.
//
// THE MECHANISM is `ConsoleErrorDriftTests`' and `ShellStyleTableDriftTests`':
// a text scan of checkout files from the .NET suite, walking up from the test
// binary to the directory holding BlazorNative.sln. `build-test` is the one
// required lane where every source file is checkout-visible. No new
// infrastructure.
//
// NON-VACUITY IS ASSERTED, NOT ASSUMED. Every assertion here is of the form
// "found nothing", which is exactly what a BROKEN scan also reports — a moved
// tree, a glob that stopped matching, a regex reworded past its subject. So the
// scan asserts that it found files, that they are the RIGHT files (the four that
// held 66 of the 78 sites), that the pattern still matches where a match is KNOWN
// to exist (BnHostTests', which stay), and that the swept files did not simply
// have their diagnostics DELETED — each one must still route through the seam.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NSLogDriftTests
{
    /// <summary>The shipped iOS shell — the pin's subject.</summary>
    private const string BnHost = "src/BlazorNative.Apple/BnHost";

    /// <summary>THE EXEMPT DIRECTORY, NAMED EXPLICITLY SO THE EXEMPTION IS VISIBLE
    /// RATHER THAN IMPLIED (design §4.2 step 3, §12).
    ///
    /// `src/BlazorNative.Apple/BnHostTests/**` holds 24 `NSLog` sites and they
    /// STAY. XCTest output is not shipped output: it is read by a human watching a
    /// simulator run, it never reaches an end user's device, and it carries no
    /// information-disclosure risk because the process it runs in is a test host.
    /// Routing it through a level-gated seam would only mean a failing test could
    /// print nothing about why.
    ///
    /// The exemption is a DIRECTORY, not a pattern: this pin scans `BnHost/` and
    /// simply never walks `BnHostTests/`. It is named here — and asserted to be
    /// real by <see cref="TheTestBundleExemption_IsRealAndStillHoldsNSLog"/> —
    /// so that a reader of the failure message knows why their test file is not
    /// covered without having to infer it from a path.</summary>
    private const string BnHostTests = "src/BlazorNative.Apple/BnHostTests";

    /// <summary>Matches an `NSLog` CALL. Comments are excluded by
    /// <see cref="CodeLines"/> — this phase's own sources discuss `NSLog` at
    /// length (BnLog.swift's header explains for eight lines why it is not one),
    /// and a pattern that cannot tell prose from a call reports the wrong number.
    /// The trailing `\s*\(` is what makes it a call rather than a mention.</summary>
    private const string NSLogCall = @"\bNSLog\s*\(";

    /// <summary>The files that held 66 of the 78 swept sites, plus the seam. Each
    /// must still be found by the walk AND still route through <c>BnLog</c> — see
    /// <see cref="TheSweptFiles_StillRouteThroughTheSeam"/>.</summary>
    private static readonly string[] SweptFiles =
    [
        "BnWidgetMapper.swift",   // 54 — the bulk
        "BnRuntime.swift",        //  6 — the boot narration #155 names
        "BnYogaLayout.mm",        //  4 — Objective-C++, via the @_cdecl BnLogC shim
        "BnSecureStorage.swift",  //  4
        "AppleShellBridge.swift", //  4
        "BnCamera.swift",         //  3
        "HostViewController.swift",
        "BnYogaProbe.swift",
        "BnFrameAdapter.swift",
    ];

    // ── 1. The pin ───────────────────────────────────────────────────────────

    /// <summary>NOT ONE BARE `NSLog` SURVIVES UNDER `BnHost/`.
    ///
    /// The 78 sites Gate C migrated wrote unconditionally to the unified log, at
    /// one severity, with every interpolated value public. Routing them through
    /// <c>BnLog</c> is what gives them a level (so Release is quiet), a category
    /// (so `log stream` can filter), and — through `os.Logger`'s private-by-default
    /// interpolation — redaction of anything the app, the user, the OS or an
    /// `Error` supplied. A new bare `NSLog` opts out of all three at once, and
    /// nothing else in this repository would notice.</summary>
    [Fact]
    public void NoBareNSLog_SurvivesUnderBnHost()
    {
        var offenders = new List<string>();

        foreach (string file in ShellFiles())
        {
            offenders.AddRange(CodeLines(file)
                .Where(l => Regex.IsMatch(l.Text, NSLogCall))
                .Select(l => $"  {Relative(file)}:{l.Number}  {l.Text.Trim()}"));
        }

        Assert.True(offenders.Count == 0,
            "BARE NSLog UNDER " + BnHost + " — it must go through BnLog.\n"
            + string.Join("\n", offenders)
            + "\n\nNSLog is unconditional (no level, never compiled out, so it ships in Release), "
            + "and — the half that level gating cannot fix — EVERY VALUE IT INTERPOLATES IS "
            + "PUBLIC in any log collected off the device. os.Logger's interpolation is private "
            + "by default; that is the information-disclosure half of #155.\n"
            + "Use BnLog.error/warn/info/debug/verbose(category, message) from Swift, or BnLogC "
            + "(BnLog.h) from Objective-C++. The category is the bracketed tag the message "
            + "already carried, without the brackets.\n"
            + $"({BnHostTests}'s 24 sites are XCTest diagnostics and are exempt BY DIRECTORY — "
            + "that exemption is about test output and buys no exemption here.)");
    }

    // ── 2. Non-vacuity — the scan must be looking at something ───────────────

    /// <summary>THE SCAN ACTUALLY READS FILES, AND THE RIGHT ONES.
    ///
    /// A scan that walked the wrong tree would report "no offenders" forever. This
    /// is the counterweight: the file set is non-empty, of a plausible size, and
    /// contains every file that held a swept site plus the seam itself.</summary>
    [Fact]
    public void TheScan_IsNotVacuous()
    {
        List<string> files = ShellFiles().ToList();

        Assert.True(files.Count > 15,
            $"the {BnHost} scan found only {files.Count} files — it is reading the wrong tree and "
            + "NoBareNSLog_SurvivesUnderBnHost is passing while blind. Fix the walk, do not delete "
            + "the pin.");

        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BnLog.swift", names);
        foreach (string swept in SweptFiles)
            Assert.Contains(swept, names);
    }

    /// <summary>…AND THE PATTERN STILL MATCHES WHERE A MATCH IS KNOWN TO EXIST.
    ///
    /// A non-empty file set proves the walk works; it does not prove the REGEX
    /// does. The exempt test bundle is the fixed point that proves the detector
    /// detects — it is the one place under `BnHostTests/` that MUST still contain
    /// live `NSLog` calls. Reword the pattern past its subject and this reds,
    /// instead of the pin quietly going green forever.</summary>
    [Fact]
    public void TheTestBundleExemption_IsRealAndStillHoldsNSLog()
    {
        string tests = Path.Combine(RepoRoot(), BnHostTests.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(tests),
            $"{BnHostTests} is missing, so the exemption this pin names protects nothing. Either "
            + "the test bundle moved — then re-point the exemption deliberately — or it is gone, "
            + "in which case delete the exemption rather than keeping it as folklore.");

        int hits = Directory.EnumerateFiles(tests, "*.swift", SearchOption.AllDirectories)
            .SelectMany(CodeLines)
            .Count(l => Regex.IsMatch(l.Text, NSLogCall));

        Assert.True(hits > 0,
            $"the NSLog pattern matched NOTHING under {BnHostTests}, which is the one tree that "
            + "MUST still contain live NSLog calls (XCTest diagnostics, deliberately not swept). "
            + "Either the test bundle stopped using NSLog — then this fixed point moved and should "
            + "be re-pointed — or the pattern no longer matches a real call, in which case "
            + "NoBareNSLog_SurvivesUnderBnHost is holding nothing.");
    }

    /// <summary>THE SWEEP REWROTE THE DIAGNOSTICS; IT DID NOT DELETE THEM.
    ///
    /// "No bare NSLog" is trivially satisfiable by removing every log line in the
    /// shell, and that would be strictly worse than the state Gate C found — the
    /// mapper's `ignored`/`skipped` lines are the ONLY record that a wire the app
    /// author wrote was silently dropped (design §4.3, which is why they stay at
    /// Warn and ship in Release). So each swept file must still reach the seam:
    /// Swift through <c>BnLog.</c>, Objective-C++ through the <c>@_cdecl</c>
    /// <c>BnLogC</c> shim, which is the only way that language can call it.</summary>
    [Fact]
    public void TheSweptFiles_StillRouteThroughTheSeam()
    {
        var offenders = new List<string>();

        foreach (string name in SweptFiles)
        {
            string file = ShellFiles().Single(f => Path.GetFileName(f) == name);
            bool routed = CodeLines(file)
                .Any(l => Regex.IsMatch(l.Text, @"\bBnLog\.\w+\s*\(|\bBnLogC\s*\(|\bbn_log_\w+\s*\("));

            if (!routed) offenders.Add($"  {name}");
        }

        Assert.True(offenders.Count == 0,
            "THE SWEEP WAS UNDONE BY DELETION, NOT BY MIGRATION. These files held NSLog sites "
            + "before Gate C and now reach no logging seam at all:\n" + string.Join("\n", offenders)
            + "\n\n'No bare NSLog' is trivially satisfied by deleting every diagnostic, and that is "
            + "the opposite of what #155 asked for: the mapper's `ignored`/`skipped` lines are the "
            + "only record that a wire the app author wrote was silently dropped (design §4.3). If "
            + "a file genuinely no longer logs, remove it from SweptFiles deliberately.");
    }

    // ── the scanner ──────────────────────────────────────────────────────────

    /// <summary>Every Swift / Objective-C / Objective-C++ / header file under
    /// `BnHost/`. Fails loudly if the tree is not there — a missing tree must
    /// break this test, not silently pass it with an empty set. `BnHostTests/` is
    /// a SIBLING directory, so it is never walked: the exemption is structural,
    /// not a filter that could be edited away by accident.</summary>
    private static IEnumerable<string> ShellFiles()
    {
        string root = Path.Combine(RepoRoot(), BnHost.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(root), $"{BnHost} not found under the repo root: {root}");

        string[] extensions = [".swift", ".m", ".mm", ".h"];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    /// <summary>The file's lines that are CODE. Line and block comments are
    /// dropped, because this phase's own sources discuss `NSLog` at length and a
    /// scanner that cannot tell prose from a call counts the documentation as
    /// offences. Same shape as <c>ConsoleErrorDriftTests.CodeLines</c>; `///` is
    /// covered by the `//` rule.</summary>
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
    /// BlazorNative.sln. The Swift sources are not a build input of this project,
    /// which is what makes `build-test` the one lane that can host this pin. Same
    /// walk as `ConsoleErrorDriftTests`, `BnLogFormatDriftTests` and
    /// `ShellStyleTableDriftTests`.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
