using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;
using RawHit = BlazorNative.Runtime.Tests.ShellSourceScan.RawHit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// LogPrivacyDriftTests — Phase 17.4, #426: the two shells redact their system
// log by one rule, and this pin holds the two copies of that rule together.
//
// THE RULE. A shell log line's text is written as `<private>` unless the call
// says SAFE or the app reveals it. iOS gets this from `os.Logger`'s `.private`
// interpolation, through `BnLog`; Android gets it from `BnShellLog`. A REDACTED
// default is what makes a forgotten call site private rather than public, so
// the defaults, the pumps and the one reveal switch are each asserted here.
//
// WHAT IT READS.
//   · The Android tree scans go through `ShellSourceScan`, as the roster's
//     `LogPrivacyDriftTests` consumer: the shipped Android shell, both source
//     directories, and the template's mirror of it.
//   · The single-file checks read named files: `BnLog.swift`,
//     `BnStderrPump.swift`, and each copy of `BnShellLog.kt`,
//     `BnStderrLogcatPump.kt` and `MainActivity.kt`.
//
// NON-VACUITY. Each tree scan asserts an absence, so each has a sibling fact
// that drives the same matcher over a real shell file with one planted
// violation, and the scan itself asserts that its sanctioned file still hits.
//
// DOES NOT COVER:
//   · SAFE reached without the token, such as `BnPrivacy.valueOf("SAFE")` or
//     `BnPrivacy.values()[i]`. The SAFE check finds the `SAFE` token, so a value
//     computed at run time passes it.
//   · SAFE inside BnShellLog.kt itself. The seam compares against SAFE, so the
//     whole file is filtered out of the SAFE check; a SAFE passed inside it is
//     not seen.
//   · stderr writers other than `System.err`, such as `printStackTrace()`. There
//     are none in the scanned trees today, and the System.err scan would not see
//     one.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class LogPrivacyDriftTests
{
    private const string Consumer = nameof(LogPrivacyDriftTests);

    private static readonly string[] KotlinSource = [".kt"];

    private const string ShellDir = "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/";
    private const string TemplateShellDir =
        "templates/BlazorNative.Templates/content/BlazorNative.App/android/src/androidMain/kotlin/"
        + "io/blazornative/shell/";

    private const string SwiftLog = "src/BlazorNative.Apple/BnHost/BnLog.swift";
    private const string SwiftPump = "src/BlazorNative.Apple/BnHost/BnStderrPump.swift";

    /// <summary>The one Android file allowed to touch <c>android.util.Log</c>.</summary>
    private const string LogSeam = "BnShellLog.kt";

    /// <summary>The one shared-JVM file allowed to touch <c>System.err</c>.</summary>
    private const string ErrSeam = "BnDiagnostics.kt";

    /// <summary>Any use of <c>android.util.Log</c>: the import or a qualified
    /// name, or a <c>Log.</c> member access, a call or a constant. The leading
    /// boundary keeps <c>BnShellLog.</c> and <c>BnLog.</c> out.</summary>
    private const string AndroidLogUse =
        @"\bandroid\s*\.\s*util\s*\.\s*Log\b|(?<![\w.])Log\s*\.\s*\w+";

    /// <summary>Any use of <c>System.err</c>.</summary>
    private const string SystemErrUse = @"\bSystem\s*\.\s*err\b";

    /// <summary>The <c>BnShellLog</c> entry points that take a privacy argument,
    /// with the zero-based position of each one's message argument.</summary>
    private static readonly Dictionary<string, int> MessageIndex = new(StringComparer.Ordinal)
    {
        ["info"] = 1, ["debug"] = 1, ["verbose"] = 1, ["warn"] = 1, ["error"] = 1, ["relay"] = 2,
    };

    /// <summary>The number of <c>BnShellLog</c> entry points with a privacy
    /// default: info, debug, verbose, warn, error and relay.</summary>
    private const int SeamPrivacyDefaults = 6;

    /// <summary>The number of <c>BnLog</c> entry points with a privacy default:
    /// error, warn, info, debug, verbose and write.</summary>
    private const int SwiftPrivacyDefaults = 6;

    private static readonly string[] ShellDirs = [ShellDir, TemplateShellDir];

    // ── 1. No android.util.Log outside the seam ──────────────────────────────

    [Fact]
    public void NoAndroidLog_OutsideBnShellLog()
    {
        ShellSourceScan.Hit[] hits = ShellSourceScan
            .ForPattern(Consumer, null, KotlinSource, AndroidLogUse)
            .HitsCoveringEveryDeclaredRoot();

        // The sanctioned file is still matched, in both copies, so the pattern
        // is still matching the calls the seam really makes.
        int seamHits = hits.Count(h => FileName(h.File) == LogSeam);
        Assert.True(seamHits >= 2,
            $"the android.util.Log pattern matched {seamHits} lines in {LogSeam}, the one file "
            + "that must still use it in both copies. The pattern has stopped matching real code, "
            + "so the absence below proves nothing.");

        var offenders = hits.Where(h => FileName(h.File) != LogSeam).ToList();
        Assert.True(offenders.Count == 0,
            "android.util.Log OUTSIDE BnShellLog.kt — every Android shell line must go through "
            + "BnShellLog, which redacts it in a Release build:\n"
            + string.Join("\n", offenders.Select(h => $"  {h.File}:{h.Line}  {h.Text}"))
            + "\n\nUse BnShellLog.warn/error/info/debug/verbose. A bare Log call writes the app's "
            + "text to logcat in Release, which iOS never does (#426).");
    }

    [Fact]
    public void TheAndroidLogScan_FindsAPlantedRawLogE()
    {
        string text = ReadCheckoutFile(ShellDir + "YogaLayout.kt");
        Assert.Empty(ShellSourceScan.PatternHits(text, AndroidLogUse));

        int planted = LineCount(text) + 1;
        RawHitAt(ShellSourceScan.PatternHits(text + "\nLog.e(TAG, \"planted\")\n", AndroidLogUse), planted);
        RawHitAt(ShellSourceScan.PatternHits(text + "\nimport android.util.Log\n", AndroidLogUse), planted);
    }

    // ── 2. No System.err outside the diagnostics sink ────────────────────────

    [Fact]
    public void NoSystemErr_OutsideBnDiagnostics()
    {
        ShellSourceScan.Hit[] hits = ShellSourceScan
            .ForPattern(Consumer, null, KotlinSource, SystemErrUse)
            .HitsCoveringEveryDeclaredRoot();

        int seamHits = hits.Count(h => FileName(h.File) == ErrSeam);
        Assert.True(seamHits >= 2,
            $"the System.err pattern matched {seamHits} lines in {ErrSeam}, whose default sink "
            + "must still write System.err in both copies. The pattern has stopped matching real "
            + "code, so the absence below proves nothing.");

        var offenders = hits.Where(h => FileName(h.File) != ErrSeam).ToList();
        Assert.True(offenders.Count == 0,
            "System.err OUTSIDE BnDiagnostics.kt — ART sends it to logcat under the tag "
            + "System.err, unredacted:\n"
            + string.Join("\n", offenders.Select(h => $"  {h.File}:{h.Line}  {h.Text}"))
            + "\n\nWrite through BnDiagnostics.sink, which the Android shell points at "
            + "BnShellLog.error at boot.");
    }

    [Fact]
    public void TheSystemErrScan_FindsAPlantedWrite()
    {
        string text = ReadCheckoutFile(
            "src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeFrameAdapter.kt");
        Assert.Empty(ShellSourceScan.PatternHits(text, SystemErrUse));

        int planted = LineCount(text) + 1;
        RawHitAt(ShellSourceScan.PatternHits(text + "\nSystem.err.println(\"planted\")\n", SystemErrUse), planted);
    }

    // ── 3. SAFE only on a literal ────────────────────────────────────────────

    [Fact]
    public void EverySafeArgument_IsOnALiteralMessageWithNoTemplate()
    {
        var (hits, _) = ShellSourceScan
            .Over(Consumer, null, KotlinSource, SafeSites)
            .CoveringEveryDeclaredRoot();

        // The seam compares against SAFE, and the enum declares it; neither is
        // a call site.
        var sites = hits.Where(h => FileName(h.File) is not (LogSeam or ErrSeam)).ToList();

        int accepted = sites.Count(h => h.Token == Accepted);
        Assert.True(accepted >= 2,
            $"found {accepted} accepted SAFE call sites in the Android shell. The shell marks its "
            + "constant boot-failure lines SAFE in both copies, so a count this low means the "
            + "call parser has stopped seeing them and the check below proves nothing.");

        var offenders = sites.Where(h => h.Token != Accepted).ToList();
        Assert.True(offenders.Count == 0,
            "BnPrivacy.SAFE WHERE THE RULE DOES NOT ALLOW IT:\n"
            + string.Join("\n", offenders.Select(h => $"  {h.File}:{h.Line}  {h.Token}: {h.Text}"))
            + "\n\nSAFE is allowed only as an argument to a BnShellLog call whose message is ONE "
            + "string literal with no `$`. An interpolated const, a node id or a return code makes "
            + "the line REDACTED, as iOS redacts it.");
    }

    [Fact]
    public void TheSafeCheck_FindsEachPlantedViolation()
    {
        string text = ReadCheckoutFile(ShellDir + "YogaLayout.kt");
        Assert.DoesNotContain(SafeSites(text), h => h.Token != Accepted);

        int at = LineCount(text) + 2;
        string[] planted =
        [
            "BnShellLog.warn(TAG, \"node $id\", privacy = BnPrivacy.SAFE)",
            "BnShellLog.error(TAG, \"a\" + b, null, BnPrivacy.SAFE)",
            "BnShellLog.relay(5, TAG, message, BnPrivacy.SAFE)",
            "val p = BnPrivacy.SAFE",
            "BnShellLog.info(TAG, \"\"\"raw\"\"\", SAFE)",
        ];
        foreach (string line in planted)
        {
            RawHit[] found = SafeSites(text + "\nfun planted() {\n" + line + "\n}\n")
                .Where(h => h.Token != Accepted).ToArray();
            Assert.True(found.Length == 1 && found[0].Line == at,
                $"the SAFE check did not flag exactly the planted line {at} for `{line}`; it "
                + $"flagged {found.Length}: "
                + string.Join("; ", found.Select(h => $"{h.Line} {h.Token}")));
        }

        RawHit[] good = SafeSites(text + "\nfun planted() {\n"
                + "BnShellLog.warn(TAG, \"a constant\", t, BnPrivacy.SAFE)\n}\n").ToArray();
        Assert.Contains(good, h => h.Token == Accepted && h.Line == at);
        Assert.DoesNotContain(good, h => h.Token != Accepted);
    }

    // ── 4. The defaults are REDACTED, on both shells ─────────────────────────

    [Fact]
    public void BnShellLog_DefaultsEveryEntryPointToRedacted()
    {
        foreach (string dir in ShellDirs)
        {
            string code = Code(dir + LogSeam);
            var defaults = Regex.Matches(code, @"privacy\s*:\s*BnPrivacy\s*=\s*BnPrivacy\s*\.\s*(\w+)")
                .Select(m => m.Groups[1].Value).ToList();

            Assert.True(defaults.Count == SeamPrivacyDefaults,
                $"{dir}{LogSeam} declares {defaults.Count} privacy defaults; its {SeamPrivacyDefaults} "
                + "entry points, info/debug/verbose/warn/error/relay, each carry one.");
            Assert.True(defaults.All(d => d == "REDACTED"),
                $"{dir}{LogSeam} has a privacy default other than REDACTED: "
                + string.Join(", ", defaults) + ". A call that says nothing must be private.");

            Assert.Matches(@"var\s+reveal\s*:\s*Boolean\s*=\s*false\b", code);
        }
    }

    [Fact]
    public void BnStderrLogcatPump_ForwardsEveryLineRedacted()
    {
        foreach (string dir in ShellDirs)
        {
            string code = Code(dir + "BnStderrLogcatPump.kt");
            string where = dir + "BnStderrLogcatPump.kt";

            Assert.Matches(@"var\s+sink\s*:[^=]*=\s*\{\s*record\s*->\s*toLogcat\s*\(\s*record\s*\)\s*\}", code);
            Assert.Matches(@"fun\s+toLogcat\s*\(\s*record\s*:\s*BnLogRecord\s*\)\s*=\s*forward\s*\(\s*record\s*\)", code);

            string forward = Body(code, "fun forward(", where);
            var calls = Calls(forward).Where(c => c.Method == "relay").ToList();
            Assert.True(calls.Count == 1,
                $"{where}'s forward makes {calls.Count} BnShellLog.relay calls, not one.");

            List<string> args = calls[0].Args;
            Assert.True(args.Count == 3 || (args.Count == 4 && IsRedacted(args[3])),
                $"{where}'s forward relays with privacy `{(args.Count > 3 ? args[3] : "")}`. No .NET "
                + "line is a shell constant, so the pump must forward REDACTED, as BnStderrPump.sink "
                + "does on iOS.");
        }
    }

    [Fact]
    public void SwiftBnLog_DefaultsToRedacted_AndItsPumpTakesTheDefault()
    {
        string log = Code(SwiftLog);
        var defaults = Regex.Matches(log, @"privacy\s*:\s*BnLogPrivacy\s*=\s*\.(\w+)")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.True(defaults.Count == SwiftPrivacyDefaults && defaults.All(d => d == "redacted"),
            $"{SwiftLog} declares privacy defaults [{string.Join(", ", defaults)}]; its "
            + $"{SwiftPrivacyDefaults} entry points each default to .redacted.");

        Assert.Matches(
            @"static\s+func\s+write\s*\(\s*_\s+level\s*:\s*Int32\s*,[^{]*?_\s+privacy\s*:\s*BnLogPrivacy\s*=\s*\.redacted\s*\)\s*\{",
            log);

        string pump = Code(SwiftPump);
        Assert.Matches(
            @"static\s+var\s+sink\s*:[^=]*=\s*\{\s*record\s+in\s+BnLog\s*\.\s*write\s*\(\s*record\.level\s*,\s*record\.category\s*,\s*record\.message\s*\)\s*\}",
            pump);
    }

    // ── 5. The reveal switch is "debuggable", set before the pump ────────────

    [Fact]
    public void MainActivity_SetsRevealFromDebuggable_BeforeThePump()
    {
        foreach (string dir in ShellDirs)
        {
            string where = dir + "MainActivity.kt";
            string onCreate = Body(Code(where), "override fun onCreate(", where);

            var reveals = Regex.Matches(onCreate, @"\bBnShellLog\s*\.\s*reveal\s*=(?!=)").ToList();
            Assert.True(reveals.Count == 1,
                $"{where}'s onCreate assigns BnShellLog.reveal {reveals.Count} times, not once.");

            Match reveal = Regex.Match(onCreate,
                @"\bBnShellLog\s*\.\s*reveal\s*=\s*\(\s*applicationInfo\s*\.\s*flags\s+and\s+"
                + @"ApplicationInfo\s*\.\s*FLAG_DEBUGGABLE\s*\)\s*!=\s*0\b");
            Assert.True(reveal.Success,
                $"{where} sets BnShellLog.reveal from something other than FLAG_DEBUGGABLE alone.");

            int pump = onCreate.IndexOf("BnStderrLogcatPump.install(", StringComparison.Ordinal);
            Assert.True(pump >= 0, $"{where}'s onCreate no longer installs BnStderrLogcatPump.");
            Assert.True(reveal.Index < pump,
                $"{where} sets BnShellLog.reveal AFTER BnStderrLogcatPump.install, so a debuggable "
                + "build redacts .NET's earliest boot lines.");

            Match sink = Regex.Match(onCreate,
                @"\bBnDiagnostics\s*\.\s*sink\s*=\s*\{\s*(\w+)\s*,\s*(\w+)\s*,\s*(\w+)\s*->\s*"
                + @"BnShellLog\s*\.\s*diagnostic\s*\(\s*\w+\s*,\s*\1\s*,\s*\2\s*,\s*\3\s*\)\s*\}");
            Assert.True(sink.Success && sink.Index < pump,
                $"{where}'s onCreate must point BnDiagnostics.sink at BnShellLog.diagnostic, passing "
                + "the report's level, before the pump, so the shared JVM code's diagnostics are "
                + "redacted too and keep their level.");
        }

        // Nothing else in the shell writes the switch.
        ShellSourceScan.Hit[] writes = ShellSourceScan
            .ForPattern(Consumer, null, KotlinSource, @"\breveal\s*=(?!=)")
            .HitsCoveringEveryDeclaredRoot();
        var elsewhere = writes.Where(h => FileName(h.File) is not ("MainActivity.kt" or LogSeam)).ToList();
        Assert.True(elsewhere.Count == 0,
            "BnShellLog.reveal is written outside MainActivity:\n"
            + string.Join("\n", elsewhere.Select(h => $"  {h.File}:{h.Line}  {h.Text}")));
    }

    // ── 6. The shared JVM code's reports keep their level ────────────────────

    /// <summary>The <c>src/main</c> reports that were warnings on <c>System.err</c>
    /// before #426, each with its file: they report at WARN, so the shell writes
    /// them as warnings. Every other call reports at ERROR.</summary>
    private static readonly (string File, int Calls)[] WarnReporters =
    [
        ("src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/ShellBridge.kt", 2),
        ("src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeFrameAdapter.kt", 1),
    ];

    [Fact]
    public void TheSharedJvmCodesReports_KeepTheirLevel()
    {
        foreach ((string file, int expected) in WarnReporters)
        {
            foreach (string path in new[] { file, MirrorOf(file) })
            {
                var levels = Regex.Matches(Code(path), @"\bBnDiagnostics\s*\.\s*sink\s*\(\s*([\w.]+)\s*,")
                    .Select(m => m.Groups[1].Value).ToList();
                Assert.True(levels.Count == expected && levels.All(l => l == "BnLogLevel.WARN"),
                    $"{path} reports through BnDiagnostics.sink at [{string.Join(", ", levels)}]; its "
                    + $"{expected} report(s) were warnings before #426 and must stay BnLogLevel.WARN.");
            }
        }

        // The shell routes a WARN report to warn: the one branch of diagnostic.
        foreach (string dir in ShellDirs)
            Assert.Matches(
                @"fun\s+diagnostic\s*\(\s*tag\s*:\s*String\s*,\s*level\s*:\s*Int\s*,\s*message\s*:\s*String\s*,\s*t\s*:\s*Throwable\?\s*\)\s*=\s*"
                + @"if\s*\(\s*level\s*==\s*BnLogLevel\.WARN\s*\)\s*warn\s*\(\s*tag\s*,\s*message\s*,\s*t\s*\)\s*else\s*error\s*\(\s*tag\s*,\s*message\s*,\s*t\s*\)",
                Code(dir + LogSeam));
    }

    private static string MirrorOf(string shellPath)
        => "templates/BlazorNative.Templates/content/BlazorNative.App/android/src/"
           + shellPath["src/BlazorNative.Jni/src/".Length..];

    // ── the SAFE call parser ─────────────────────────────────────────────────

    private const string Accepted = "accepted";

    private sealed record Call(string Method, int Start, int ArgsStart, int ArgsEnd, List<string> Args, List<int> ArgStarts);

    /// <summary>Every <c>SAFE</c> token in comment-stripped Kotlin, each one
    /// classified: <see cref="Accepted"/>, or the reason it breaks the rule.</summary>
    private static IEnumerable<RawHit> SafeSites(string text)
    {
        string code = CommentStrippedSource.Strip(text);
        List<Call> calls = Calls(code);

        foreach (Match m in Regex.Matches(code, @"\bSAFE\b"))
        {
            if (InsideString(code, m.Index)) continue;

            int line = LineAt(code, m.Index);
            string verdict = Verdict(code, m.Index, calls);
            yield return new RawHit(line, LineText(code, m.Index), verdict);
        }
    }

    private static string Verdict(string code, int at, List<Call> calls)
    {
        if (!Regex.IsMatch(code[..at], @"\bBnPrivacy\s*\.\s*$"))
            return "SAFE not written as BnPrivacy.SAFE";

        // The innermost BnShellLog call whose argument list holds this token as
        // a whole top-level argument.
        Call? call = calls
            .Where(c => c.ArgsStart <= at && at < c.ArgsEnd)
            .OrderByDescending(c => c.ArgsStart)
            .FirstOrDefault();
        if (call is null) return "BnPrivacy.SAFE outside a BnShellLog call";

        int arg = call.ArgStarts.FindLastIndex(s => s <= at);
        string privacy = call.Args[arg];
        if (!Regex.IsMatch(privacy, @"^(privacy\s*=\s*)?BnPrivacy\s*\.\s*SAFE$"))
            return "BnPrivacy.SAFE not passed as the privacy argument";

        int index = MessageIndex[call.Method];
        string? message = call.Args.FirstOrDefault(a => Regex.IsMatch(a, @"^message\s*="));
        message = message is not null
            ? Regex.Replace(message, @"^message\s*=\s*", "")
            : (call.Args.Count > index ? call.Args[index] : null);

        if (message is null) return "no message argument";
        if (!Regex.IsMatch(message, "^\"(?:[^\"\\\\$\\n]|\\\\[^$\\n])*\"$"))
            return "message is not one string literal without `$`";
        return Accepted;
    }

    /// <summary>Every <c>BnShellLog.&lt;method&gt;(</c> call in
    /// <paramref name="code"/>, its top-level arguments split, string-aware.</summary>
    private static List<Call> Calls(string code)
    {
        var calls = new List<Call>();
        foreach (Match m in Regex.Matches(code, @"\bBnShellLog\s*\.\s*(\w+)\s*\("))
        {
            if (!MessageIndex.ContainsKey(m.Groups[1].Value) || InsideString(code, m.Index)) continue;

            int open = m.Index + m.Length - 1;
            var args = new List<string>();
            var starts = new List<int>();
            int depth = 0, argStart = open + 1, i = open, end = -1;
            while (i < code.Length)
            {
                char c = code[i];
                if (c == '"') { i = SkipString(code, i); continue; }
                if (c == '\'') { i = SkipChar(code, i); continue; }
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}')
                {
                    if (--depth == 0) { end = i; break; }
                }
                else if (c == ',' && depth == 1)
                {
                    starts.Add(argStart);
                    args.Add(code[argStart..i].Trim());
                    argStart = i + 1;
                }
                i++;
            }
            if (end < 0) continue;
            if (code[argStart..end].Trim().Length > 0)
            {
                starts.Add(argStart);
                args.Add(code[argStart..end].Trim());
            }
            calls.Add(new Call(m.Groups[1].Value, m.Index, open + 1, end, args, starts));
        }
        return calls;
    }

    /// <summary>The index just past the Kotlin string literal opening at
    /// <paramref name="i"/>: a raw string, or a plain one with escapes, each
    /// skipping any <c>${…}</c> template as code.</summary>
    private static int SkipString(string code, int i)
    {
        bool raw = string.CompareOrdinal(code, i, "\"\"\"", 0, 3) == 0;
        i += raw ? 3 : 1;
        while (i < code.Length)
        {
            if (raw && string.CompareOrdinal(code, i, "\"\"\"", 0, 3) == 0)
            {
                i += 3;
                while (i < code.Length && code[i] == '"') i++;
                return i;
            }
            char c = code[i];
            if (!raw && c == '\\') { i += 2; continue; }
            if (!raw && c == '"') return i + 1;
            if (c == '$' && i + 1 < code.Length && code[i + 1] == '{')
            {
                int depth = 0;
                i++;
                while (i < code.Length)
                {
                    if (code[i] == '"') { i = SkipString(code, i); continue; }
                    if (code[i] == '{') depth++;
                    else if (code[i] == '}' && --depth == 0) break;
                    i++;
                }
            }
            i++;
        }
        return i;
    }

    private static int SkipChar(string code, int i)
    {
        int j = i + 1;
        if (j < code.Length && code[j] == '\\') j++;
        int close = code.IndexOf('\'', j + 1);
        return close < 0 ? i + 1 : close + 1;
    }

    /// <summary>Whether <paramref name="at"/> sits inside a string literal,
    /// scanning from the start of the code.</summary>
    private static bool InsideString(string code, int at)
    {
        int i = 0;
        while (i < at)
        {
            if (code[i] == '"')
            {
                int end = SkipString(code, i);
                if (at < end) return true;
                i = end;
                continue;
            }
            if (code[i] == '\'') { i = SkipChar(code, i); continue; }
            i++;
        }
        return false;
    }

    private static bool IsRedacted(string arg)
        => Regex.IsMatch(arg, @"^(privacy\s*=\s*)?BnPrivacy\s*\.\s*REDACTED$");

    // ── helpers ──────────────────────────────────────────────────────────────

    private static int LineAt(string code, int at)
    {
        int line = 1;
        for (int k = 0; k < at; k++) if (code[k] == '\n') line++;
        return line;
    }

    private static string LineText(string code, int at)
    {
        int s = code.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        int e = code.IndexOf('\n', at);
        return code[s..(e < 0 ? code.Length : e)].Trim();
    }

    private static int LineCount(string text) => text.Split('\n').Length;

    private static void RawHitAt(IEnumerable<RawHit> hits, int line)
    {
        RawHit[] all = hits.ToArray();
        Assert.True(all.Any(h => h.Line == line),
            $"the scan's matcher did not find the planted line {line}; it found "
            + (all.Length == 0 ? "nothing" : string.Join(", ", all.Select(h => h.Line))));
    }

    private static string FileName(string path) => path.Split('/')[^1];

    private static string Code(string relativePath)
        => CommentStrippedSource.Strip(ReadCheckoutFile(relativePath));

    /// <summary>The brace-balanced body after the first
    /// <paramref name="declaration"/>, as <c>BnLogFormatDriftTests</c> reads one.</summary>
    private static string Body(string source, string declaration, string where)
    {
        int at = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(at >= 0, $"could not find `{declaration}` in {where}.");

        int open = source.IndexOf('{', at);
        Assert.True(open >= 0, $"`{declaration}` in {where} has no body.");

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '"') { i = SkipString(source, i) - 1; continue; }
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        Assert.Fail($"`{declaration}` in {where} has an unbalanced body.");
        return "";
    }

    private static string ReadCheckoutFile(string relativePath)
    {
        string path = Path.Combine(BnRepo.Root(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"checkout file not found: {path}");
        return File.ReadAllText(path);
    }
}
