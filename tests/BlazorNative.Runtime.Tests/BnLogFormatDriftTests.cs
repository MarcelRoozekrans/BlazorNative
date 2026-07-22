using System.Text.RegularExpressions;
using BlazorNative.Core;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnLogFormatDriftTests — Phase 11.4 Gate B, design §8.1 pin 4 and RISK R1.
//
// R1, IN FULL, BECAUSE IT IS THE SHARPEST RISK IN THE PHASE: the line format
// `[BN|E|category] message` is WRITTEN by C# (`BnLog.FormatLine`) and PARSED by
// Kotlin (`BnLogFormat.parse`) — and, at Gate C, by Swift. Those are separate
// copies of one contract in separate languages, and they cannot be made a single
// source of truth without a code generator this phase is not buying.
//
// ⚠ THE FAILURE SHAPE IS A SILENT DOWNGRADE, NOT A CRASH. If the two drift by one
// character, every framework line stops matching the prefix and lands on the
// pump's UNPREFIXED FALLBACK — `Log.w`, tag `BlazorNative/native`. The pump keeps
// working. logcat keeps filling. Errors still appear. They just appear as
// WARNINGS, in the wrong tag, with the category folded into the message text, and
// there is no test, no exception and no log line anywhere that says so. A
// developer chasing a production fault would read a warning and move on.
//
// SO THE PIN READS BOTH SOURCES. It does not compare two literals written into
// this file — that would drift with them, in the same commit, and pass. It:
//
//   · takes the C# side LIVE, from the compiled `BnLog` (FormatLine/Tag/LinePrefix
//     as the runtime actually executes them);
//   · takes the Kotlin side FROM THE CHECKED-OUT SOURCE of BnLogFormat.kt and
//     NativeBindings.kt, by parsing out the constants those files declare;
//   · then RUNS THE KOTLIN PARSER'S RULES, reconstructed from what was parsed,
//     over lines the C# formatter actually produced.
//
// Only a change made in BOTH languages in the SAME commit can keep this green,
// which is exactly the property a two-copy contract needs. Same mechanism as
// `ShellStyleTableDriftTests` / `ConsoleErrorDriftTests`: a text scan of checkout
// files from `build-test`, the one required lane where every file is visible.
//
// EVERY PARSE ASSERTS NON-VACUITY. A regex that stopped matching would leave this
// comparing nothing and passing forever — the exact failure it exists to prevent,
// one level up.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BnLogFormatDriftTests
{
    private const string KotlinFormat =
        "src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/BnLogFormat.kt";
    private const string KotlinBindings =
        "src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeBindings.kt";
    private const string TemplateKotlinFormat =
        "templates/BlazorNative.Templates/content/BlazorNative.App/android/src/main/kotlin/"
        + "io/blazornative/jni/BnLogFormat.kt";

    /// <summary>The five levels that can reach a line. <c>Unset</c> is excluded
    /// deliberately — the gate rejects it, so it has no wire tag to pin.</summary>
    private static readonly BnLogLevel[] Levels =
    [
        BnLogLevel.Error, BnLogLevel.Warn, BnLogLevel.Info, BnLogLevel.Debug, BnLogLevel.Verbose,
    ];

    // ── 1. The prefix — one constant per language ────────────────────────────

    /// <summary>THE PREFIX MARKER IS THE SAME STRING IN BOTH LANGUAGES.
    ///
    /// This is the single character-for-character comparison R1 turns on: the
    /// parser's very first act is <c>line.startsWith(PREFIX)</c>, so a drift here
    /// sends 100% of framework lines to the fallback, at the wrong level, with
    /// nothing red anywhere.</summary>
    [Fact]
    public void TheLinePrefix_IsIdenticalInCSharpAndKotlin()
    {
        string kotlin = ParseKotlinString(ReadCheckoutFile(KotlinFormat), "PREFIX", KotlinFormat);

        Assert.True(BnLog.LinePrefix == kotlin,
            "LINE-FORMAT PREFIX DRIFT (design R1). BnLog.LinePrefix is "
            + $"\"{BnLog.LinePrefix}\"; {KotlinFormat} declares PREFIX = \"{kotlin}\".\n\n"
            + "⚠ NOTHING WOULD LOOK BROKEN. The Android pump would keep running and logcat "
            + "would keep filling — every framework line would simply stop matching the prefix "
            + "and land on the unprefixed fallback: Log.w, tag BlazorNative/native, category "
            + "folded into the message. Errors would show up as warnings, forever, silently.\n"
            + "Change both sides in the same commit, or change neither.");
    }

    // ── 2. The tag characters — the level, recovered ─────────────────────────

    /// <summary>EVERY LEVEL'S TAG CHARACTER AGREES, AND THE MAP IS TOTAL BOTH WAYS.
    ///
    /// `BnLog.Tag` is the writer; `BnLogFormat.priorityForTag` is the reader. The
    /// Kotlin side is read out of its `when` expression in the checked-out source,
    /// so a letter changed in one language reds here.
    ///
    /// The Kotlin priorities are `android.util.Log`'s ordinals (VERBOSE 2 … ERROR
    /// 6), NOT `BnLogLevel`'s (1…5) — two different numberings, deliberately, and
    /// this pin holds the mapping between them rather than assuming they match.</summary>
    [Fact]
    public void EveryLevelsTagCharacter_IsIdenticalInCSharpAndKotlin()
    {
        string source = ReadCheckoutFile(KotlinFormat);
        Dictionary<char, string> tagToPriority = ParseKotlinWhenArms(
            source, "priorityForTag", KotlinFormat);
        Dictionary<string, int> priorityValues = ParseKotlinIntConsts(source, KotlinFormat);

        // The Kotlin ordinals ARE android.util.Log's. Frozen platform constants, so
        // pinning them is pinning the mirror, not our numbering.
        var expectedAndroid = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["VERBOSE"] = 2, ["DEBUG"] = 3, ["INFO"] = 4, ["WARN"] = 5, ["ERROR"] = 6,
        };
        foreach ((string name, int value) in expectedAndroid)
        {
            Assert.True(priorityValues.TryGetValue(name, out int actual) && actual == value,
                $"{KotlinFormat} declares {name} = "
                + (priorityValues.TryGetValue(name, out int got) ? got.ToString() : "(absent)")
                + $", but android.util.Log.{name} is {value}. The pump calls Log.println(priority, …) "
                + "with these numbers directly — a wrong one lands every line of that level at the "
                + "wrong severity in logcat.");
        }

        // C#'s BnLogLevel ordinal → the android priority name it must map to.
        var expectedLevelName = new Dictionary<BnLogLevel, string>
        {
            [BnLogLevel.Error] = "ERROR",
            [BnLogLevel.Warn] = "WARN",
            [BnLogLevel.Info] = "INFO",
            [BnLogLevel.Debug] = "DEBUG",
            [BnLogLevel.Verbose] = "VERBOSE",
        };

        var offenders = new List<string>();
        foreach (BnLogLevel level in Levels)
        {
            char tag = BnLog.Tag(level);
            if (!tagToPriority.TryGetValue(tag, out string? kotlinPriority))
            {
                offenders.Add($"  {level}: C# writes tag '{tag}', which Kotlin's priorityForTag "
                    + "does not recognise at all → the line falls through to the WARN fallback");
                continue;
            }

            string want = expectedLevelName[level];
            if (!string.Equals(kotlinPriority, want, StringComparison.Ordinal))
                offenders.Add($"  {level}: C# writes tag '{tag}'; Kotlin maps '{tag}' to "
                    + $"{kotlinPriority}, but it must map to {want}");
        }

        Assert.True(offenders.Count == 0,
            "LINE-FORMAT TAG DRIFT (design R1) — the level a line CLAIMS is not the level the "
            + "pump gives it:\n" + string.Join("\n", offenders)
            + $"\n\nC# writes the tag in BnLog.Tag; {KotlinFormat} reads it back in "
            + "priorityForTag. They are two copies of one contract, and a mismatch is INVISIBLE "
            + "at runtime — the wrong severity is still a log line.");

        // NON-VACUITY: five arms, all distinct.
        Assert.True(tagToPriority.Count == 5,
            $"parsed {tagToPriority.Count} arms out of {KotlinFormat}'s priorityForTag, expected "
            + "5. The parse is no longer reading the map it claims to, and this pin is holding "
            + "nothing. A pin that cannot see its subject must never pass vacuously.");
    }

    // ── 3. THE ROUND TRIP — C#'s real output through Kotlin's real rules ─────

    /// <summary>WHAT C# EMITS IS WHAT THE PUMP PARSES — for every level.
    ///
    /// The two pins above compare CONSTANTS. This one compares BEHAVIOUR: it takes
    /// the line `BnLog.FormatLine` actually produces and runs the Kotlin parser's
    /// algorithm over it, with the prefix and the tag→priority map lifted out of
    /// the checked-out Kotlin source rather than written here. If either side's
    /// SHAPE changes — a space moved, the separator changed from `|`, the bracket
    /// dropped — the reconstruction stops recovering the triple and this reds,
    /// even though every individual constant still matches.
    ///
    /// STATED HONESTLY, because R1 deserves it: this is a REIMPLEMENTATION of the
    /// Kotlin parser in C#, parameterised by what the Kotlin source declares. It is
    /// not the Kotlin parser itself, and it cannot be — the two languages do not
    /// share a runtime in this suite. Two things keep that honest rather than
    /// decorative: the constants are READ, not copied, so the drift this pin is
    /// actually about (a changed letter, a changed prefix) cannot slip past; and
    /// the Kotlin parser's own behaviour is pinned INDEPENDENTLY, against the
    /// Kotlin formatter, by BnStderrPumpTest in the JVM lane. Between them, a
    /// change has to be made consistently in both languages to stay green.</summary>
    [Fact]
    public void EveryLevelsFormattedLine_ParsesBackToTheSameTriple()
    {
        string source = ReadCheckoutFile(KotlinFormat);
        string prefix = ParseKotlinString(source, "PREFIX", KotlinFormat);
        Dictionary<char, string> tagToPriority = ParseKotlinWhenArms(source, "priorityForTag", KotlinFormat);

        const string category = "renderer";
        const string message = "mount faulted: the component threw";

        var offenders = new List<string>();
        foreach (BnLogLevel level in Levels)
        {
            string line = BnLog.FormatLine(level, category, message);

            // ── the Kotlin parser's algorithm, verbatim, over READ constants ──
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                offenders.Add($"  {level}: \"{line}\" does not start with the parser's prefix \"{prefix}\"");
                continue;
            }

            int tagAt = prefix.Length;
            if (line.Length < tagAt + 3 || line[tagAt + 1] != '|')
            {
                offenders.Add($"  {level}: \"{line}\" has no '|' separator after the tag character "
                    + "— the parser bails to the unprefixed fallback");
                continue;
            }

            if (!tagToPriority.TryGetValue(line[tagAt], out string? priority))
            {
                offenders.Add($"  {level}: tag '{line[tagAt]}' is not in Kotlin's map");
                continue;
            }

            int close = line.IndexOf(']', tagAt + 2);
            if (close < 0)
            {
                offenders.Add($"  {level}: \"{line}\" has no closing ']'");
                continue;
            }

            string parsedCategory = line[(tagAt + 2)..close];
            string parsedMessage = close + 2 <= line.Length ? line[(close + 2)..] : "";

            if (parsedCategory != category)
                offenders.Add($"  {level}: category round-tripped as \"{parsedCategory}\", not \"{category}\"");
            if (parsedMessage != message)
                offenders.Add($"  {level}: message round-tripped as \"{parsedMessage}\", not \"{message}\"");

            string wantPriority = level switch
            {
                BnLogLevel.Error => "ERROR",
                BnLogLevel.Warn => "WARN",
                BnLogLevel.Info => "INFO",
                BnLogLevel.Debug => "DEBUG",
                _ => "VERBOSE",
            };
            if (priority != wantPriority)
                offenders.Add($"  {level}: recovered as {priority}, not {wantPriority}");
        }

        Assert.True(offenders.Count == 0,
            "LINE-FORMAT ROUND-TRIP BROKEN (design §8.1 pin 4, risk R1). What BnLog.FormatLine "
            + "emits is no longer what the Android pump parses back:\n" + string.Join("\n", offenders)
            + "\n\n⚠ THIS IS THE SILENT ONE. Nothing throws, nothing crashes, and logcat still "
            + "fills up — every framework line just arrives at Log.w under the tag "
            + "BlazorNative/native with its category glued into the message. Fix the FORMAT, on "
            + $"both sides ({KotlinFormat} and BnLog.cs), in one commit.");
    }

    // ── 4. The BnLogLevel wire ordinals ──────────────────────────────────────

    /// <summary>THE INIT-INPUT ORDINALS AGREE. The shell puts a
    /// <c>BnLogLevel</c> ordinal in <c>BlazorNativeInitOptions.logLevel</c> at
    /// offset 28 and the runtime decodes it with <c>BnLog.SetLevelFromOrdinal</c>.
    /// A renumbering on one side gives every app a level it did not ask for —
    /// including, at the wrong end, a Release build shipping Verbose.</summary>
    [Fact]
    public void TheBnLogLevelOrdinals_AreIdenticalInCSharpAndKotlin()
    {
        Dictionary<string, int> kotlin = ParseKotlinIntConsts(
            ReadCheckoutFile(KotlinBindings), KotlinBindings, scope: "object BnLogLevel");

        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["UNSET"] = (int)BnLogLevel.Unset,
            ["ERROR"] = (int)BnLogLevel.Error,
            ["WARN"] = (int)BnLogLevel.Warn,
            ["INFO"] = (int)BnLogLevel.Info,
            ["DEBUG"] = (int)BnLogLevel.Debug,
            ["VERBOSE"] = (int)BnLogLevel.Verbose,
        };

        Assert.True(kotlin.Count == expected.Count,
            $"parsed {kotlin.Count} constants out of {KotlinBindings}'s BnLogLevel object, "
            + $"expected {expected.Count} ({string.Join(", ", expected.Keys)}). Either the object "
            + "moved or this parse stopped seeing it — a pin that cannot see its subject must "
            + "never pass vacuously.");

        var offenders = expected
            .Where(e => !kotlin.TryGetValue(e.Key, out int v) || v != e.Value)
            .Select(e => $"  {e.Key}: C# = {e.Value}, Kotlin = "
                + (kotlin.TryGetValue(e.Key, out int v) ? v.ToString() : "(absent)"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "BnLogLevel WIRE-ORDINAL DRIFT. The shell's ordinal crosses the ABI at "
            + "BlazorNativeInitOptions offset 28 and is decoded by BnLog.SetLevelFromOrdinal:\n"
            + string.Join("\n", offenders)
            + "\n\nA renumbering gives every app a verbosity it never asked for — and the quiet "
            + "direction is the dangerous one, because a Release build shipping Verbose looks "
            + "fine until someone reads the logs.");

        // The default the whole back-compat argument rests on.
        Assert.True(BnLog.DefaultLevel == BnLogLevel.Warn,
            "the shell passes ordinal 0 (UNSET) when the app declares nothing, and every KDoc in "
            + "the Android shell tells the reader that resolves to Warn. Changing BnLog.DefaultLevel "
            + "makes that documentation false in both copies of the shell at once.");
    }

    // ── 5. The template's copy of the parser ─────────────────────────────────

    /// <summary>THE TEMPLATE'S PUMP PARSES THE SAME FORMAT. `TemplateDriftTests`
    /// already holds the template's Kotlin byte-identical to the repo's, so this
    /// is the second lock on the same door — and it is worth its four lines because
    /// a generated app that mis-levels every framework line is precisely the class
    /// of defect that surfaces on a stranger's laptop with nothing naming the
    /// cause.</summary>
    [Fact]
    public void TheTemplatesParser_DeclaresTheSamePrefix()
    {
        Assert.True(File.Exists(CheckoutPath(TemplateKotlinFormat)),
            $"{TemplateKotlinFormat} is missing — the template ships a shell whose stderr pump "
            + "cannot be compiled. Mirror BnLogFormat.kt into the template (TemplateDriftTests' "
            + "file-set pin says the same thing, louder).");

        Assert.Equal(
            BnLog.LinePrefix,
            ParseKotlinString(ReadCheckoutFile(TemplateKotlinFormat), "PREFIX", TemplateKotlinFormat));
    }

    // ── the Kotlin readers ───────────────────────────────────────────────────

    /// <summary>Lifts `const val NAME: String = "…"` out of Kotlin source.</summary>
    private static string ParseKotlinString(string source, string name, string where)
    {
        Match m = Regex.Match(source, $@"const\s+val\s+{Regex.Escape(name)}\s*:\s*String\s*=\s*""([^""]*)""");
        Assert.True(m.Success,
            $"could not find `const val {name}: String` in {where} — the constant moved or was "
            + "renamed, and this pin is now holding nothing. Re-point it deliberately.");
        return m.Groups[1].Value;
    }

    /// <summary>Lifts every `const val NAME: Int = n` out of Kotlin source,
    /// optionally only after the line matching <paramref name="scope"/>.</summary>
    private static Dictionary<string, int> ParseKotlinIntConsts(
        string source, string where, string? scope = null)
    {
        string body = source;
        if (scope is not null)
        {
            int at = source.IndexOf(scope, StringComparison.Ordinal);
            Assert.True(at >= 0, $"could not find `{scope}` in {where} — re-point this pin deliberately.");

            // From the scope's opening brace to its matching close.
            int open = source.IndexOf('{', at);
            Assert.True(open >= 0, $"`{scope}` in {where} has no body.");
            int depth = 0, end = open;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) { end = i; break; }
            }
            body = source[open..end];
        }

        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(body, @"const\s+val\s+(\w+)\s*:\s*Int\s*=\s*(-?\d+)"))
            found[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);

        Assert.True(found.Count > 0,
            $"parsed ZERO `const val …: Int` declarations out of {where}"
            + (scope is null ? "" : $" ({scope})")
            + " — this pin would compare an empty map and pass over it.");
        return found;
    }

    /// <summary>Lifts the `'X' -&gt; NAME` arms out of a named Kotlin `when`
    /// function body (`fun NAME(…) … = when (…) { … }`).</summary>
    private static Dictionary<char, string> ParseKotlinWhenArms(
        string source, string function, string where)
    {
        Match fn = Regex.Match(source, $@"fun\s+{Regex.Escape(function)}\s*\([^)]*\)[^=]*=\s*when\s*\([^)]*\)\s*\{{");
        Assert.True(fn.Success,
            $"could not find `fun {function}(…) = when (…)` in {where} — the parser's shape "
            + "changed and this pin stopped reading it. Re-point it deliberately rather than "
            + "letting the format go unguarded (design R1).");

        int open = source.IndexOf('{', fn.Index + fn.Length - 1);
        int depth = 0, end = open;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) { end = i; break; }
        }

        var arms = new Dictionary<char, string>();
        foreach (Match m in Regex.Matches(source[open..end], @"'(.)'\s*->\s*(\w+)"))
            arms[m.Groups[1].Value[0]] = m.Groups[2].Value;

        Assert.True(arms.Count > 0,
            $"parsed ZERO `'X' -> LEVEL` arms out of {where}'s {function} — this pin would "
            + "compare an empty map and pass over it.");
        return arms;
    }

    // ── the checkout ─────────────────────────────────────────────────────────

    private static string CheckoutPath(string relativePath)
        => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ReadCheckoutFile(string relativePath)
    {
        string path = CheckoutPath(relativePath);
        Assert.True(File.Exists(path), $"checkout file not found: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>The nearest ancestor of the test binary holding BlazorNative.sln —
    /// the same walk `ConsoleErrorDriftTests` and `ReadmeDriftTests` use. The
    /// Kotlin sources are not a build input of this project, which is what makes
    /// `build-test` the one lane that can host this pin.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
