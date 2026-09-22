using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// #213 item 1 — THE AUTH SEMANTICS PIN.
//
// `requireAuth: true` means BIOMETRY, on both shells. The Apple shell used to
// disagree with ITSELF: it stored under a `.biometryCurrentSet` ACL (biometry
// only) and read back under `.deviceOwnerAuthentication` (passcode OR
// biometry), so the effective gate was weaker than the stored ACL declared.
//
// This pin reads SOURCE. It asserts each declared site still carries its
// declared token, that no authenticator token appears anywhere undeclared, and
// that the scan is not vacuous. It NEVER observes an authentication at runtime
// — see src/auth-semantics.json's $doc for why a behavioural cross-shell pin
// would itself be an unpinned twin.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AuthSemanticsDriftTests
{
    private sealed record Site(
        string Name, string File, string Language, string Kind, string Token, string Reason);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;
        Assert.True(dir is not null,
            "could not find BlazorNative.sln above the test binary — a pin that cannot find its "
            + "subject must fail loudly, never vacuously");
        return dir!.FullName;
    }

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "auth-semantics.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

    /// <summary>Reads a required string field, failing with the field's name rather
    /// than a bare KeyNotFoundException. A missing `reason` and a blank one are the
    /// same defect — an undocumented entry — so they must produce the same legible
    /// failure, not an opaque throw for one and a named assertion for the other.
    /// </summary>
    private static string Required(JsonElement element, string field, string what)
    {
        Assert.True(element.TryGetProperty(field, out JsonElement value),
            $"{what} has no '{field}' field in src/auth-semantics.json — every entry must "
            + "carry one, and a missing field is the same defect as a blank one");
        return value.GetString() ?? string.Empty;
    }

    private static Site[] Sites()
    {
        using JsonDocument doc = Manifest();
        var sites = new List<Site>();
        foreach (JsonElement s in doc.RootElement.GetProperty("sites").EnumerateArray())
        {
            string name = Required(s, "name", "a site");
            sites.Add(new Site(
                name,
                Required(s, "file", $"site '{name}'"),
                Required(s, "language", $"site '{name}'"),
                Required(s, "kind", $"site '{name}'"),
                Required(s, "token", $"site '{name}'"),
                Required(s, "reason", $"site '{name}'")));
        }

        Assert.True(sites.Count >= 7,
            $"only {sites.Count} sites declared — the manifest lost entries, or this loop "
            + "stopped seeing them");
        return [.. sites];
    }

    /// <summary>Removes both comment forms Swift and Kotlin share, so a token merely
    /// DESCRIBED in prose is never mistaken for a live call site: `//` to end of line,
    /// AND `/* … */` blocks, which includes KDoc `/** … */` — AndroidShellBridge.kt
    /// carries 284 block openers, and the completeness scan reads the same stripped
    /// text, so an unstripped KDoc token would demand an `ignored` entry documenting
    /// nothing real. Blocks nest, as both languages define them. Newlines inside a
    /// stripped block are preserved so reported line numbers stay true. An
    /// UNTERMINATED opener is left in place rather than swallowing the rest of the
    /// file: over-stripping hides live call sites, which is a false green.
    /// String literals ARE parsed, simply: an unescaped `"` toggles in/out of a string
    /// and the state RESETS AT EVERY NEWLINE, so a `//` or `/*` inside a literal is no
    /// longer read as a comment — `://` already appears in both shells' literals, so
    /// this is a live shape, not a hypothetical. REMAINING BOUNDED LIMIT, not a claim:
    /// a `"""` multiline or raw string toggles three times and lands inside-string, and
    /// only the newline reset clears it. The reset is the point — it confines any
    /// mis-parse to a single line rather than letting one stray quote blind the rest of
    /// the file.
    /// </summary>
    internal static string StripComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        int i = 0;
        bool inString = false;

        while (i < source.Length)
        {
            // STRING LITERALS (F2). A `//` inside a string is not a comment — the
            // shells already contain `://` in literals, at BnDeepLink.swift and
            // BnCamera.swift, so this is a live shape and not a hypothetical.
            // Tracking is deliberately simple: toggle on an unescaped `"`, and RESET
            // AT EVERY NEWLINE. Neither Swift nor Kotlin lets an ordinary string span
            // lines, and the reset is what BOUNDS a mis-parse to one line rather than
            // letting one stray quote blind the rest of the file. A `"""` multiline or
            // raw string toggles three times and lands `true`, which the newline reset
            // then clears — imperfect, and bounded, which is the trade being made.
            if (source[i] == '"')
            {
                bool escaped = i > 0 && source[i - 1] == '\\'
                               && !(i > 1 && source[i - 2] == '\\');
                if (!escaped) inString = !inString;
                sb.Append(source[i]);
                i++;
                continue;
            }

            if (source[i] == '\n')
            {
                inString = false;
                sb.Append(source[i]);
                i++;
                continue;
            }

            if (inString)
            {
                sb.Append(source[i]);
                i++;
                continue;
            }

            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int depth = 0;
                int j = i;
                while (j < source.Length)
                {
                    if (source[j] == '/' && j + 1 < source.Length && source[j + 1] == '*')
                    {
                        depth++;
                        j += 2;
                    }
                    else if (source[j] == '*' && j + 1 < source.Length && source[j + 1] == '/')
                    {
                        depth--;
                        j += 2;
                        if (depth == 0) break;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (depth != 0)
                {
                    // Unterminated — emit the '/' verbatim and resume one character on.
                    // Swallowing to EOF would blind the scan to everything below it.
                    sb.Append(source[i]);
                    i++;
                    continue;
                }

                // Keep the block's newlines so line numbers survive the strip.
                for (int k = i; k < j; k++)
                    if (source[k] == '\n') sb.Append('\n');
                i = j;
                continue;
            }

            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            sb.Append(source[i]);
            i++;
        }

        return sb.ToString();
    }

    [Fact]
    public void EveryDeclaredSite_StillCarriesItsDeclaredToken()
    {
        foreach (Site site in Sites())
        {
            string path = Path.Combine(RepoRoot(), site.File.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                $"site '{site.Name}' names {site.File}, which does not exist — the manifest and "
                + "the tree have drifted");

            string code = StripComments(File.ReadAllText(path));
            Assert.True(code.Contains(site.Token, StringComparison.Ordinal),
                $"site '{site.Name}' ({site.Kind}) must use '{site.Token}' in {site.File}, and no "
                + $"live occurrence was found. Reason on record: {site.Reason}");

            Assert.False(string.IsNullOrWhiteSpace(site.Reason),
                $"site '{site.Name}' carries no reason — an undocumented site is one nobody can "
                + "review");
        }
    }

    /// <summary>Every authenticator token either platform offers. A token NOT in this
    /// list is invisible to the completeness scan, so adding a platform authenticator
    /// means adding it here — that is the one manual step this pin cannot remove.</summary>
    private static readonly string[] AuthenticatorVocabulary =
    [
        ".deviceOwnerAuthenticationWithBiometrics",
        ".deviceOwnerAuthentication",
        ".biometryCurrentSet",
        ".biometryAny",
        ".userPresence",
        "AUTH_BIOMETRIC_STRONG",
        "AUTH_BIOMETRIC_WEAK",
        "AUTH_DEVICE_CREDENTIAL",
        "Authenticators.BIOMETRIC_STRONG",
        "Authenticators.BIOMETRIC_WEAK",
        "Authenticators.DEVICE_CREDENTIAL",
    ];

    private static readonly string[] ShellSourceRoots =
    [
        Path.Combine("src", "BlazorNative.Apple", "BnHost"),
        Path.Combine("src", "BlazorNative.Jni", "src", "androidMain"),
    ];

    private sealed record Occurrence(string Token, string File, int Line);

    /// <summary>Scans both shells' non-test source for every vocabulary token,
    /// comments stripped. Shared by the completeness and anti-vacuity tests so the
    /// second genuinely measures what the first scanned. Matching is over the WHOLE
    /// stripped text with whitespace tolerated around each `.`, so a token wrapped
    /// across a line break is still seen — a Kotlin `Authenticators` / `.BIOMETRIC_WEAK`
    /// split over two lines is one occurrence, not none. The reported line is the one
    /// the token's first non-whitespace character sits on, so a wrapped token names the
    /// line it starts on rather than the line before it.</summary>
    private static Occurrence[] ScanOccurrences()
    {
        string root = RepoRoot();
        var found = new List<Occurrence>();

        foreach (string rel in ShellSourceRoots)
        {
            string dir = Path.Combine(root, rel);
            Assert.True(Directory.Exists(dir),
                $"shell source root '{rel}' does not exist — the scan would silently cover "
                + "nothing");

            foreach (string path in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                         .Where(p => p.EndsWith(".swift", StringComparison.Ordinal)
                                  || p.EndsWith(".kt", StringComparison.Ordinal)))
            {
                string file = Path.GetRelativePath(root, path).Replace('\\', '/');
                string stripped = StripComments(File.ReadAllText(path));

                // WHOLE-TEXT MATCHING (F1). The old scan split on '\n' first, so a
                // token wrapped across lines was invisible — and every dotted KOTLIN
                // token can wrap, because the dot leads the continuation. Swift is
                // immune only by accident: its tokens BEGIN with the dot, so a wrap
                // carries it intact. Matching whole-text with `\s*` around each dot
                // closes the Kotlin half without special-casing a language.
                //
                // Each occurrence carries the MATCH's own geometry — offset AND length
                // — never the token's. A dot-leading token like `.userPresence` yields
                // a pattern that opens with `\s*`, so a wrapped match both STARTS
                // earlier than the token and RUNS LONGER than it; token arithmetic
                // would then mis-report the line and mis-describe the span.
                var matches = new List<(string Token, int Start, int Length)>();
                foreach (string token in AuthenticatorVocabulary)
                {
                    string pattern = string.Join(@"\s*\.\s*",
                        token.Split('.').Select(Regex.Escape));
                    foreach (Match m in Regex.Matches(stripped, pattern))
                        matches.Add((token, m.Index, m.Length));
                }

                foreach ((string token, int start, int length) in matches)
                {
                    // `.deviceOwnerAuthentication` is a prefix of
                    // `.deviceOwnerAuthenticationWithBiometrics`, so the short token also
                    // matches INSIDE the long one and would otherwise report a
                    // device-owner call at every biometrics site. Suppress by SPAN, never
                    // by line: an occurrence is a false echo only when its MATCHED SPAN
                    // lies within a LONGER token's MATCHED SPAN. Asking merely whether
                    // some longer token appears SOMEWHERE nearby loses a real occurrence
                    // whenever both appear separately — a ternary or a ratchet between the
                    // weak and the strong LAPolicy on one line, which is exactly the #213
                    // pair — and this pin would stay green through a reintroduction of the
                    // defect. Both sides compare matched spans: a wrapped match is longer
                    // than its token, so `token.Length` no longer describes it.
                    if (matches.Any(other =>
                            other.Token.Length > token.Length
                            && other.Start <= start
                            && start + length <= other.Start + other.Length))
                        continue;

                    // Report the line the TOKEN starts on, not the line the match starts
                    // on: a leading `\s*` can pull the match back across a newline, which
                    // would otherwise blame the previous line for a wrapped token.
                    int at = start;
                    while (at < start + length && char.IsWhiteSpace(stripped[at])) at++;

                    int line = 1;
                    for (int k = 0; k < at; k++) if (stripped[k] == '\n') line++;

                    found.Add(new Occurrence(token, file, line));
                }
            }
        }

        return [.. found];
    }

    [Fact]
    public void EveryAuthenticatorOccurrence_IsDeclaredOrIgnored()
    {
        Site[] sites = Sites();
        using JsonDocument doc = Manifest();

        // An ignore is COUNTED, not open-ended. An uncounted `file::token` pair excuses
        // every occurrence of that token in that file for ever, which is indistinguishable
        // from switching the scan off for the pair — so a SECOND call site could be added
        // beside the excused one and this pin would stay green.
        var ignoredCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var ignoredSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("ignored", out JsonElement arr))
        {
            foreach (JsonElement ig in arr.EnumerateArray())
            {
                string file = Required(ig, "file", "an ignored occurrence");
                string token = Required(ig, "token", $"the ignored occurrence in '{file}'");
                string key = $"{file}::{token}";
                Assert.False(
                    string.IsNullOrWhiteSpace(
                        Required(ig, "reason", $"ignored occurrence '{token}' in '{file}'")),
                    $"ignored occurrence '{token}' in '{file}' carries no reason — asymmetry is "
                    + "allowed, silence is not");
                Assert.True(
                    ig.TryGetProperty("count", out JsonElement c)
                    && c.TryGetInt32(out int n) && n > 0,
                    $"ignored entry '{key}' has no positive integer `count` — an uncounted ignore "
                    + "excuses every occurrence of that token in that file, which is the same as "
                    + "disabling the scan for the pair");
                ignoredCounts[key] = c.GetInt32();
                ignoredSeen[key] = 0;
            }
        }

        foreach (Occurrence occ in ScanOccurrences())
        {
            bool declared = sites.Any(s => s.File == occ.File && s.Token == occ.Token);

            string ignoreKey = $"{occ.File}::{occ.Token}";
            bool excused = false;
            if (ignoredCounts.TryGetValue(ignoreKey, out int allowed))
            {
                ignoredSeen[ignoreKey]++;
                excused = ignoredSeen[ignoreKey] <= allowed;
            }

            Assert.True(declared || excused,
                $"UNDECLARED AUTHENTICATOR: '{occ.Token}' at {occ.File}:{occ.Line} appears in "
                + "neither `sites` nor `ignored` in src/auth-semantics.json. An authenticator "
                + "nobody declared is an opinion nobody reviewed — which is exactly how the "
                + "Apple shell came to disagree with itself. Declare it with a reason, or "
                + "ignore it with a reason.");
        }

        // EXACT, not at-most. A count left behind after its occurrence is deleted is a
        // stale excuse, and a stale excuse is a licence for the next occurrence to
        // arrive unreviewed.
        foreach ((string key, int allowed) in ignoredCounts)
            Assert.True(ignoredSeen[key] == allowed,
                $"ignored entry '{key}' declares count {allowed}, but the scan found "
                + $"{ignoredSeen[key]}. If the occurrence moved or was deleted, delete the "
                + "entry too — a count that outlives its occurrence is a standing licence "
                + "for the next one to arrive unreviewed.");
    }

    [Fact]
    public void TheTestOnlyCredentialBranch_IsStillGuarded()
    {
        // src/auth-semantics.json excuses one AUTH_DEVICE_CREDENTIAL occurrence on the
        // grounds that it is reachable only through `allowDeviceCredentialForTest`.
        // A token scanner cannot see a guard — widening `if (allowDeviceCredentialForTest)`
        // to `if (true)` leaves the token on the same line, in the same file, exactly
        // once, so neither the completeness scan nor its `count` changes by one
        // character. That excuse therefore rests on this assertion and nothing else.
        string path = Path.Combine(RepoRoot(),
            "src", "BlazorNative.Jni", "src", "androidMain", "kotlin", "io",
            "blazornative", "shell", "AndroidShellBridge.kt");
        Assert.True(File.Exists(path),
            $"{path} does not exist — a pin that cannot find its subject must fail loudly, "
            + "never vacuously");

        string code = StripComments(File.ReadAllText(path));

        Assert.Contains("if (allowDeviceCredentialForTest)", code, StringComparison.Ordinal);

        // And the parameter must still default to false, or every caller gets the
        // credential path without asking for it.
        Assert.Contains("allowDeviceCredentialForTest: Boolean = false", code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompletenessScan_IsNotVacuous()
    {
        Occurrence[] occurrences = ScanOccurrences();

        // The sibling test above passes trivially if the scan finds nothing — a regex or
        // a path that stops matching turns the guard into a no-op that still reports
        // green. 14.1's equivalent completeness check shipped WITHOUT this assertion and
        // is a known open residual on main; this phase does not reproduce that hole.
        Assert.True(occurrences.Length >= 7,
            $"the authenticator scan found only {occurrences.Length} occurrences across both "
            + "shells, and there are at least 7 known live sites. The scan has stopped seeing "
            + "its subject — the completeness test above is now passing while checking "
            + "nothing.");

        Assert.Contains(occurrences, o => o.File.EndsWith(".swift", StringComparison.Ordinal));
        Assert.Contains(occurrences, o => o.File.EndsWith(".kt", StringComparison.Ordinal));
    }
}
