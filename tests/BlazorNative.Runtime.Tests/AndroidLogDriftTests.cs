using System.Text.RegularExpressions;
using BlazorNative.Core;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// AndroidLogDriftTests — issue #200: "no bare Log.i / Log.d / Log.v under the
// Android shell's source". The third pin in the family that already holds
// `Console.Error` (ConsoleErrorDriftTests) and `NSLog` (NSLogDriftTests).
//
// WHAT WENT WRONG, AND WHY NOTHING WAS RED. Phase 11.4 swept the .NET side (31
// `Console.Error` sites), swept the iOS side (78 `NSLog` sites), and built the
// Android stderr → logcat pump for the RUNTIME's fd-2 output. It never swept the
// KOTLIN SHELL'S OWN `android.util.Log` calls — they were never in scope. So on
// hardware (Xiaomi, Android 16, 0.5.1) a cold launch at the DEFAULT `Warn`
// threshold still printed:
//
//     I BlazorNative: [BOOT] native init ok — BlazorNative.Runtime 0.5.1
//     I BlazorNative: [BOOT] frame callback registered
//     I BlazorNative: [BOOT] shell bridge registered
//     I BlazorNative: [BOOT] mounted BnDemo
//
// …and ZERO lines carried the pump's `BlazorNative/<category>` tag shape. Four
// Info lines at a Warn threshold, because a threshold cannot suppress a call
// that never asks it. That is 1.0 criterion Q1 and the remaining half of #155.
//
// WHY A PIN AND NOT A REVIEW. The five sites were re-routed onto `BnShellLog`;
// site six arrives next month as a bare `Log.i`, prints in every consumer's
// Release build, and NOTHING in this repository notices — no unit test, no
// instrumented test (the instrumented lane asserts the pump DELIVERS, not that
// the shell is SILENT), and no CI lane can observe a device. The only mechanism
// that catches it before a human with a phone does is a source scan.
//
// ⚠ WHY THIS LIVES IN THE .NET SUITE AND NOT IN THE JVM LANE. Both lanes are
// required (`build-test` runs both), so that is not the tiebreaker. Three things
// are:
//   1. THE MECHANISM ALREADY LIVES HERE. ConsoleErrorDriftTests, NSLogDriftTests,
//      ShellStyleTableDriftTests, BnLogFormatDriftTests and TemplateDriftTests all
//      scan checkout source from this suite, with the same RepoRoot() walk. A
//      fourth copy of it in Kotlin would be a second mechanism to maintain.
//   2. IT COVERS THE TEMPLATE MIRROR IN THE SAME PASS. The generated app's shell
//      is a byte-identical copy under templates/**; a JVM test rooted in the
//      gradle project would be scanning one of the two trees.
//   3. IT NEEDS NO ANDROID TOOLCHAIN. `gradlew testDebugUnitTest` rides preBuild →
//      copyRuntimeSo → verifyNativeAssets, so the JVM lane cannot even start
//      without two NativeAOT bionic publishes. A text scan should not require an
//      NDK.
// The BEHAVIOUR of the gate — that Info is dropped at Warn and returns at Info —
// is pinned where it belongs, in Kotlin, by `BnShellLogTest` in the JVM lane.
//
// NON-VACUITY IS ASSERTED, NOT ASSUMED. Every assertion here is of the form
// "found nothing", which is exactly what a BROKEN scan reports. So the scan
// asserts that it found files, that they are the RIGHT files, that the pattern
// still matches where a match is KNOWN to exist (the instrumented test tree,
// which is exempt and still uses Log.i), and that the swept files still REACH
// the seam rather than having had their narration deleted.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AndroidLogDriftTests
{
    /// <summary>The shipped Android shell — the pin's subject. Both source roots
    /// the gradle `main` source set compiles (build.gradle.kts declares
    /// `kotlin.srcDirs("src/main/kotlin", "src/androidMain/kotlin")`).</summary>
    private static readonly string[] ShellRoots =
    [
        "src/BlazorNative.Jni/src/main/kotlin",
        "src/BlazorNative.Jni/src/androidMain/kotlin",
    ];

    /// <summary>The template's byte-identical mirror of the same shell — scanned
    /// in the same pass, because it is what a `dotnet new blazornative` app
    /// actually compiles. `TemplateDriftTests` holds the two byte-equal, so this
    /// is a second lock on the same door; it costs one array entry.</summary>
    private static readonly string[] TemplateRoots =
    [
        "templates/BlazorNative.Templates/content/BlazorNative.App/android/src/main/kotlin",
        "templates/BlazorNative.Templates/content/BlazorNative.App/android/src/androidMain/kotlin",
    ];

    /// <summary>THE ONE FILE ALLOWED TO CALL `Log` AT A GATED LEVEL: the seam
    /// itself, whose default sink is a `Log.println`.
    ///
    /// Named explicitly so the exemption is VISIBLE rather than implied — the rule
    /// `NSLogDriftTests` states for `BnHostTests` and `ConsoleErrorDriftTests` for
    /// `DevHostBridge.cs`. Note it does not even need the exemption for the
    /// PATTERN below (`Log.println` is not `Log.i`), and that is deliberate:
    /// the seam calls the priority-taking overload precisely because the priority
    /// is data there, not a hardcoded level. The name is kept anyway so a reader
    /// of a failure message knows where the sanctioned call lives.</summary>
    private const string TheSeam = "BnShellLog.kt";

    /// <summary>THE EXEMPT TREE, NAMED EXPLICITLY. `src/BlazorNative.Jni/src/
    /// androidTest/**` holds instrumented-test diagnostics (`[7.2-throughput]`
    /// measurements that a human reads out of a CI logcat dump, and a layout
    /// dump). They STAY: instrumented output is not shipped output — it runs in a
    /// test host on an emulator, never in a consumer's app, and gating it would
    /// mean a failing throughput assertion could print nothing about why.
    ///
    /// The exemption is STRUCTURAL, not a filter: `androidTest` is a SIBLING of
    /// the two scanned roots, so it is never walked. It is also this pin's
    /// non-vacuity fixed point — see
    /// <see cref="TheInstrumentedTestExemption_IsRealAndStillHoldsBareLogI"/>.</summary>
    private const string InstrumentedTests = "src/BlazorNative.Jni/src/androidTest";

    /// <summary>Matches a BARE `Log.i` / `Log.d` / `Log.v` CALL — the three levels
    /// the framework's threshold SUPPRESSES at the Release default.
    ///
    /// `Log.w` and `Log.e` are deliberately NOT matched: warnings and errors ship
    /// in Release by design (BnLog's own rule — the mapper's `ignored`/`skipped`
    /// lines are the only record that a wire the app author wrote was dropped),
    /// and a shell that could not report a boot failure without a threshold being
    /// raised would be strictly worse than one that is noisy.
    ///
    /// The leading boundary keeps `BnShellLog.info` and `BnLogFormat`-qualified
    /// mentions out; the trailing `\s*\(` is what makes it a CALL rather than a
    /// mention. Comments are excluded by <see cref="CodeLines"/> — this fix's own
    /// sources quote the offending lines at length.</summary>
    private const string BareLogCall = @"(?<![\w.])Log\s*\.\s*[idv]\s*\(";

    /// <summary>The files that held the five ungated sites, plus the seam. Each
    /// must still be found by the walk AND still route through
    /// <c>BnShellLog</c> — see <see cref="TheSweptFiles_StillNarrate"/>.</summary>
    private static readonly string[] SweptFiles =
    [
        "MainActivity.kt",       // 4 — the [BOOT] forEach + two [deep-link] lines
        "AndroidShellBridge.kt", // 1 — [bridge] navigate
    ];

    // ── 1. The pin ───────────────────────────────────────────────────────────

    /// <summary>NOT ONE BARE `Log.i`/`Log.d`/`Log.v` SURVIVES IN THE SHELL.
    ///
    /// A bare call is UNCONDITIONAL: it has no threshold, so it cannot be quieted
    /// in Release, and it never reaches `BnLog` — which is why the four `[BOOT]`
    /// lines of #200 printed at a `Warn` threshold on a real phone while the
    /// framework's own lines obeyed it. Route narration through
    /// <c>BnShellLog.info/debug/verbose</c> instead; it reads the SAME ordinal
    /// `MainActivity.resolveLogLevel()` hands the runtime at
    /// <c>BlazorNativeInitOptions.logLevel</c>, so there is one knob, not two.</summary>
    [Fact]
    public void NoBareLogInfoDebugOrVerbose_SurvivesInTheAndroidShell()
    {
        var offenders = new List<string>();

        foreach (string file in ShellFiles())
        {
            offenders.AddRange(CodeLines(file)
                .Where(l => Regex.IsMatch(l.Text, BareLogCall))
                .Select(l => $"  {Relative(file)}:{l.Number}  {l.Text.Trim()}"));
        }

        Assert.True(offenders.Count == 0,
            "BARE Log.i / Log.d / Log.v IN THE ANDROID SHELL — it must go through BnShellLog.\n"
            + string.Join("\n", offenders)
            + "\n\nA bare android.util.Log call asks no threshold, so it prints in every consumer's "
            + "Release build. That is issue #200, observed on hardware: at the DEFAULT Warn a cold "
            + "launch still emitted four Info `[BOOT]` lines, while ZERO lines carried the pump's "
            + "BlazorNative/<category> tag — proof they never touched the seam.\n"
            + "Use BnShellLog.info/debug/verbose(tag, message). Log.w and Log.e stay bare on "
            + "purpose: warnings and errors ship in Release.\n"
            + $"({InstrumentedTests}'s instrumented diagnostics are exempt BY DIRECTORY — that is "
            + "about test output on an emulator and buys no exemption here.)");
    }

    // ── 2. Non-vacuity — the scan must be looking at something ───────────────

    /// <summary>THE SCAN ACTUALLY READS FILES, AND THE RIGHT ONES.
    ///
    /// A scan that walked a renamed tree would report "no offenders" forever. The
    /// file set must be non-empty, of a plausible size, and must contain every
    /// file that held a swept site plus the seam and the template's mirrors.</summary>
    [Fact]
    public void TheScan_IsNotVacuous()
    {
        List<string> files = ShellFiles().ToList();

        Assert.True(files.Count > 20,
            $"the Android-shell scan found only {files.Count} Kotlin files — it is reading the "
            + "wrong tree and NoBareLogInfoDebugOrVerbose_SurvivesInTheAndroidShell is passing "
            + "while blind. Fix the walk, do not delete the pin.");

        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(TheSeam, names);
        foreach (string swept in SweptFiles)
            Assert.Contains(swept, names);

        // Both trees, not just the repo's: the template ships the shell a
        // generated app compiles, and a scan that silently stopped covering it
        // would leave the consumer-facing copy unguarded.
        foreach (string root in ShellRoots.Concat(TemplateRoots))
        {
            Assert.Contains(files, f => f.StartsWith(CheckoutPath(root), StringComparison.Ordinal));
        }
    }

    /// <summary>…AND THE PATTERN STILL MATCHES WHERE A MATCH IS KNOWN TO EXIST.
    ///
    /// A non-empty file set proves the WALK works; it does not prove the REGEX
    /// does. The exempt instrumented tree is the fixed point: it is the one place
    /// under `src/BlazorNative.Jni` that MUST still contain live bare `Log.i`
    /// calls (throughput and layout diagnostics, deliberately not swept). Reword
    /// the pattern past its subject and this reds, instead of the pin quietly
    /// going green forever.</summary>
    [Fact]
    public void TheInstrumentedTestExemption_IsRealAndStillHoldsBareLogI()
    {
        string tests = CheckoutPath(InstrumentedTests);
        Assert.True(Directory.Exists(tests),
            $"{InstrumentedTests} is missing, so the exemption this pin names protects nothing. "
            + "Either the instrumented tree moved — then re-point the exemption deliberately — or "
            + "it is gone, in which case delete the exemption rather than keeping it as folklore.");

        int hits = Directory.EnumerateFiles(tests, "*.kt", SearchOption.AllDirectories)
            .SelectMany(CodeLines)
            .Count(l => Regex.IsMatch(l.Text, BareLogCall));

        Assert.True(hits > 0,
            $"the bare-Log pattern matched NOTHING under {InstrumentedTests}, which is the one "
            + "tree that MUST still contain live Log.i calls (instrumented diagnostics, "
            + "deliberately not swept). Either those tests stopped using Log.i — then this fixed "
            + "point moved and should be re-pointed — or the pattern no longer matches a real "
            + "call, in which case the pin above is holding nothing.");
    }

    /// <summary>THE FIX RE-ROUTED THE NARRATION; IT DID NOT DELETE IT.
    ///
    /// "No bare Log.i" is trivially satisfiable by removing every narration line,
    /// and that would be worse than the state #200 found: the `[BOOT]` lines are
    /// how `scripts/devloop.ps1` knows a mount happened, and the two `[deep-link]`
    /// lines are named in the Phase 11.2 device runbook's PASS criteria. So each
    /// swept file must still reach the seam.</summary>
    [Fact]
    public void TheSweptFiles_StillNarrate()
    {
        var offenders = new List<string>();

        foreach (string name in SweptFiles)
        {
            foreach (string file in ShellFiles().Where(f => Path.GetFileName(f) == name))
            {
                bool routed = CodeLines(file)
                    .Any(l => Regex.IsMatch(l.Text, @"\bBnShellLog\s*\.\s*(info|debug|verbose)\s*\("));

                if (!routed) offenders.Add($"  {Relative(file)}");
            }
        }

        Assert.True(offenders.Count == 0,
            "THE #200 FIX WAS UNDONE BY DELETION, NOT BY GATING. These files narrated before the "
            + "fix and now reach no logging seam at all:\n" + string.Join("\n", offenders)
            + "\n\n'No bare Log.i' is trivially satisfied by deleting every narration line, and "
            + "that is the opposite of what #200 asked for — the issue says in as many words that "
            + "the lines are 'genuinely useful at higher verbosity'. scripts/devloop.ps1 waits for "
            + "'[BOOT] mounted <Component>' in logcat, and the Phase 11.2 runbook's deep-link PASS "
            + "criteria name the '[deep-link]' lines. If a file genuinely no longer narrates, "
            + "remove it from SweptFiles deliberately.");
    }

    // ── 3. One threshold, not two ────────────────────────────────────────────

    /// <summary>THE SHELL'S DEFAULT IS THE FRAMEWORK'S DEFAULT.
    ///
    /// `BnShellLog` necessarily holds its OWN copy of the threshold — Kotlin cannot
    /// read a C# static across the ABI, and the runtime exposes no getter. What
    /// keeps that from becoming a SECOND LEVEL CONCEPT is two things, and this pin
    /// is the second of them:
    /// <list type="number">
    /// <item>structurally, `MainActivity.onCreate` resolves the ordinal ONCE and
    /// feeds it to both `BnShellLog.setLevelFromOrdinal` and the runtime's
    /// `BlazorNativeInitOptions.logLevel` — one local, two uses;</item>
    /// <item>and the FALLBACK the two apply when the app declares nothing must be
    /// the same level, which is what is asserted here against the live
    /// <see cref="BnLog.DefaultLevel"/>.</item>
    /// </list>
    /// If they drifted, an app that configures nothing would get a quiet runtime
    /// and a chatty shell (or the reverse) — and the symptom would be exactly the
    /// one #200 reports, which is the hardest kind of bug to attribute.</summary>
    [Fact]
    public void TheShellsDefaultThreshold_IsTheFrameworksDefault()
    {
        string seam = ShellFiles().First(f => Path.GetFileName(f) == TheSeam);
        string source = File.ReadAllText(seam);

        Match m = Regex.Match(source, @"const\s+val\s+DEFAULT_LEVEL\s*:\s*Int\s*=\s*BnLogLevel\.(\w+)");
        Assert.True(m.Success,
            $"could not find `const val DEFAULT_LEVEL: Int = BnLogLevel.…` in {Relative(seam)} — "
            + "the constant moved or was renamed and this pin is holding nothing. Re-point it "
            + "deliberately rather than deleting it.");

        string kotlin = m.Groups[1].Value;                       // e.g. "WARN"
        string csharp = BnLog.DefaultLevel.ToString().ToUpperInvariant(); // "WARN"

        Assert.True(string.Equals(kotlin, csharp, StringComparison.Ordinal),
            $"SHELL/RUNTIME DEFAULT-LEVEL DRIFT. {Relative(seam)} falls back to "
            + $"BnLogLevel.{kotlin}; BnLog.DefaultLevel is {BnLog.DefaultLevel}.\n\n"
            + "An app that declares no level would then get one verbosity from the framework and "
            + "another from the shell — two thresholds wearing one name, which is precisely what "
            + "#200's fix exists to prevent. Change both in the same commit, or change neither.");
    }

    // ── the scanner ──────────────────────────────────────────────────────────

    /// <summary>Every `.kt` under the shell's two source roots AND the template's
    /// two mirrors. Fails loudly if a root is not there — a missing tree must
    /// break this test, not silently pass it with an empty set. The `androidTest`
    /// and `test` trees are SIBLINGS of these roots, so they are never walked:
    /// the exemption is structural, not a filter that could be edited away by
    /// accident.</summary>
    private static IEnumerable<string> ShellFiles()
    {
        foreach (string relative in ShellRoots.Concat(TemplateRoots))
        {
            string root = CheckoutPath(relative);
            Assert.True(Directory.Exists(root), $"{relative} not found under the repo root: {root}");

            foreach (string file in Directory
                .EnumerateFiles(root, "*.kt", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal))
            {
                yield return file;
            }
        }
    }

    /// <summary>The file's lines that are CODE. Line and block comments are
    /// dropped, because this fix's own sources quote the offending `Log.i` lines
    /// at length and a scanner that cannot tell prose from a call counts the
    /// documentation as offences. Same shape as
    /// <c>ConsoleErrorDriftTests.CodeLines</c>; KDoc's `/** … */` is covered by
    /// the block-comment rule and `///` by the `//` one.</summary>
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

    private static string CheckoutPath(string relativePath)
        => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Relative(string file)
        => Path.GetRelativePath(RepoRoot(), file).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>The repo root — the nearest ancestor of the test binary holding
    /// BlazorNative.sln. The Kotlin sources are not a build input of this project,
    /// which is what makes `build-test` the one lane that can host this pin. Same
    /// walk as `ConsoleErrorDriftTests`, `NSLogDriftTests` and
    /// `BnLogFormatDriftTests`.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
