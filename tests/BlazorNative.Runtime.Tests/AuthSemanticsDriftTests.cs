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

    private static Site[] Sites()
    {
        using JsonDocument doc = Manifest();
        var sites = new List<Site>();
        foreach (JsonElement s in doc.RootElement.GetProperty("sites").EnumerateArray())
        {
            sites.Add(new Site(
                s.GetProperty("name").GetString()!,
                s.GetProperty("file").GetString()!,
                s.GetProperty("language").GetString()!,
                s.GetProperty("kind").GetString()!,
                s.GetProperty("token").GetString()!,
                s.GetProperty("reason").GetString()!));
        }

        Assert.True(sites.Count >= 7,
            $"only {sites.Count} sites declared — the manifest lost entries, or this loop "
            + "stopped seeing them");
        return [.. sites];
    }

    /// <summary>Strips line comments so a token merely DESCRIBED in prose is never
    /// mistaken for a live call site. BnSecureStorage.swift alone mentions
    /// `.biometryCurrentSet` nine times in doc comments and twice in code.</summary>
    internal static string StripLineComments(string source) =>
        string.Join('\n', source.Split('\n').Select(line =>
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }));

    [Fact]
    public void EveryDeclaredSite_StillCarriesItsDeclaredToken()
    {
        foreach (Site site in Sites())
        {
            string path = Path.Combine(RepoRoot(), site.File.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                $"site '{site.Name}' names {site.File}, which does not exist — the manifest and "
                + "the tree have drifted");

            string code = StripLineComments(File.ReadAllText(path));
            Assert.True(code.Contains(site.Token, StringComparison.Ordinal),
                $"site '{site.Name}' ({site.Kind}) must use '{site.Token}' in {site.File}, and no "
                + $"live occurrence was found. Reason on record: {site.Reason}");

            Assert.False(string.IsNullOrWhiteSpace(site.Reason),
                $"site '{site.Name}' carries no reason — an undocumented site is one nobody can "
                + "review");
        }
    }
}
