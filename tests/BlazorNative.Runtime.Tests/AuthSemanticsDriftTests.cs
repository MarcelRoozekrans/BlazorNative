using System.Text;
using System.Text.Json;
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
    /// KNOWN LIMIT, not a claim: string literals are not parsed, so a `//` or `/*`
    /// inside a string is treated as a comment. Neither shell's source has one near
    /// an authenticator token; if that changes, occurrences after it go unseen and the
    /// anti-vacuity count is the only thing standing between that and a silent green.
    /// </summary>
    internal static string StripComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        int i = 0;

        while (i < source.Length)
        {
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
    /// second genuinely measures what the first scanned.</summary>
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
                string[] lines = StripComments(File.ReadAllText(path)).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    // Every occurrence of every token on this line, with the index it
                    // actually starts at. Indices, not line containment — see below.
                    var matches = new List<(string Token, int Start)>();
                    foreach (string token in AuthenticatorVocabulary)
                        for (int at = lines[i].IndexOf(token, StringComparison.Ordinal);
                             at >= 0;
                             at = lines[i].IndexOf(token, at + 1, StringComparison.Ordinal))
                            matches.Add((token, at));

                    foreach ((string token, int start) in matches)
                    {
                        // `.deviceOwnerAuthentication` is a prefix of
                        // `.deviceOwnerAuthenticationWithBiometrics`, so the short token also
                        // matches INSIDE the long one and would otherwise report a
                        // device-owner call at every biometrics site. Suppress by SPAN, never
                        // by line: an occurrence is a false echo only when it lies within a
                        // LONGER token's matched span. Asking merely whether some longer token
                        // appears SOMEWHERE on the line loses a real occurrence whenever both
                        // appear separately — a ternary or a ratchet between the weak and the
                        // strong LAPolicy on one line, which is exactly the #213 pair — and
                        // this pin would stay green through a reintroduction of the defect.
                        if (matches.Any(other =>
                                other.Token.Length > token.Length
                                && other.Start <= start
                                && start + token.Length <= other.Start + other.Token.Length))
                            continue;

                        found.Add(new Occurrence(token, file, i + 1));
                    }
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

        var ignored = new HashSet<string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("ignored", out JsonElement arr))
        {
            foreach (JsonElement ig in arr.EnumerateArray())
            {
                string file = Required(ig, "file", "an ignored occurrence");
                string token = Required(ig, "token", $"the ignored occurrence in '{file}'");
                Assert.False(
                    string.IsNullOrWhiteSpace(
                        Required(ig, "reason", $"ignored occurrence '{token}' in '{file}'")),
                    $"ignored occurrence '{token}' in '{file}' carries no reason — asymmetry is "
                    + "allowed, silence is not");
                ignored.Add($"{file}::{token}");
            }
        }

        foreach (Occurrence occ in ScanOccurrences())
        {
            bool declared = sites.Any(s => s.File == occ.File && s.Token == occ.Token);
            bool excused = ignored.Contains($"{occ.File}::{occ.Token}");

            Assert.True(declared || excused,
                $"UNDECLARED AUTHENTICATOR: '{occ.Token}' at {occ.File}:{occ.Line} appears in "
                + "neither `sites` nor `ignored` in src/auth-semantics.json. An authenticator "
                + "nobody declared is an opinion nobody reviewed — which is exactly how the "
                + "Apple shell came to disagree with itself. Declare it with a reason, or "
                + "ignore it with a reason.");
        }
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
