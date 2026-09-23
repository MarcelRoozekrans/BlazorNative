using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using BlazorNative.Tests.Shared;

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

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(
            File.ReadAllText(Path.Combine(BnRepo.Root(), "src", "auth-semantics.json")),
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

    [Fact]
    public void EveryDeclaredSite_StillCarriesItsDeclaredToken()
    {
        foreach (Site site in Sites())
        {
            string path = Path.Combine(BnRepo.Root(), site.File.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                $"site '{site.Name}' names {site.File}, which does not exist — the manifest and "
                + "the tree have drifted");

            string code = CommentStrippedSource.Strip(File.ReadAllText(path));
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

    // ── THIS PIN HOLDS NO ROOT LIST AND NO WALK ──────────────────────────────
    //
    // It held a two-entry array once, and `src/BlazorNative.Jni/src/main/kotlin`
    // was not one of the entries — HALF THE ANDROID SHELL. That is #364 F1. Three
    // review rounds then each moved the guard one link along the chain and each
    // was defeated by one line a step further down: a private accessor, then a
    // shared accessor called per site, then a record of what was ENUMERATED with
    // a content filter inserted before the read.
    //
    // `ShellSourceScan` is where that stopped. This pin names a CONSUMER; the
    // scan resolves the roots from the roster, reads each file, and records
    // coverage in the same expression that hands the bytes to a matcher. The
    // matcher is never told which file it is looking at, so the path-keyed filter
    // every one of those defeats used is not expressible here.
    //
    // ALL THREE ABSENCE-ASSERTING FACTS BELOW CARRY THEIR OWN COVERAGE, and that
    // is not symmetry for its own sake. This pin has TWO scans with different
    // subjects — the authenticator vocabulary over both shells, and the
    // credential-flag caller count over Kotlin — and for one round only the first
    // had a coverage assertion. Re-pointing the second left 35 passed / 0 failed
    // with a live second caller under `src/main/kotlin` that nothing else in the
    // repository guards.

    private sealed record Occurrence(string Token, string File, int Line);

    /// <summary>THE AUTHENTICATOR MATCHER, as a pure function of CONTENT.
    ///
    /// It is handed text and returns hits carrying a line number; it never learns
    /// which file it is reading, because <c>ShellSourceScan</c> stamps the path on
    /// afterwards. That is deliberate: every defeat of this pin's coverage across
    /// three review rounds was a path-keyed filter, and a matcher with no path
    /// cannot carry one.
    ///
    /// Matching is over the WHOLE stripped text with whitespace tolerated around
    /// each `.`, so a token wrapped across a line break is still seen — a Kotlin
    /// `Authenticators` / `.BIOMETRIC_WEAK` split over two lines is one occurrence,
    /// not none. Swift is immune only by accident: its tokens BEGIN with the dot,
    /// so a wrap carries it intact. The reported line is the one the token's first
    /// non-whitespace character sits on, so a wrapped token names the line it
    /// starts on rather than the line before it.</summary>
    private static IEnumerable<ShellSourceScan.RawHit> AuthenticatorHits(string source)
    {
        string stripped = CommentStrippedSource.Strip(source);

        // Each occurrence carries the MATCH's own geometry — offset AND length —
        // never the token's. A dot-leading token like `.userPresence` yields a
        // pattern that opens with `\s*`, so a wrapped match both STARTS earlier
        // than the token and RUNS LONGER than it; token arithmetic would then
        // mis-report the line and mis-describe the span.
        var matches = new List<(string Token, int Start, int Length)>();
        foreach (string token in AuthenticatorVocabulary)
        {
            string pattern = string.Join(@"\s*\.\s*", token.Split('.').Select(Regex.Escape));
            foreach (Match m in Regex.Matches(stripped, pattern))
                matches.Add((token, m.Index, m.Length));
        }

        foreach ((string token, int start, int length) in matches)
        {
            // `.deviceOwnerAuthentication` is a prefix of
            // `.deviceOwnerAuthenticationWithBiometrics`, so the short token also
            // matches INSIDE the long one and would otherwise report a device-owner
            // call at every biometrics site. Suppress by SPAN, never by line: an
            // occurrence is a false echo only when its MATCHED SPAN lies within a
            // LONGER token's MATCHED SPAN. Asking merely whether some longer token
            // appears SOMEWHERE nearby loses a real occurrence whenever both appear
            // separately — a ternary or a ratchet between the weak and the strong
            // LAPolicy on one line, which is exactly the #213 pair — and this pin
            // would stay green through a reintroduction of the defect.
            if (matches.Any(other =>
                    other.Token.Length > token.Length
                    && other.Start <= start
                    && start + length <= other.Start + other.Length))
                continue;

            // Report the line the TOKEN starts on, not the line the match starts on:
            // a leading `\s*` can pull the match back across a newline, which would
            // otherwise blame the previous line for a wrapped token.
            int at = start;
            while (at < start + length && char.IsWhiteSpace(stripped[at])) at++;

            int line = 1;
            for (int k = 0; k < at; k++) if (stripped[k] == '\n') line++;

            yield return new ShellSourceScan.RawHit(line, token, token);
        }
    }

    /// <summary>The authenticator scan, over every root this pin consumes. Each
    /// fact calls this itself so that it asserts coverage on its OWN record —
    /// a sibling's walk proves nothing about this one.</summary>
    private static ShellSourceScan.Hit[] ScanOccurrences() =>
        ShellSourceScan
            .Over(nameof(AuthSemanticsDriftTests), null, ShellSource, AuthenticatorHits)
            .HitsCoveringEveryDeclaredRoot();

    /// <summary>The two shells' source extensions.</summary>
    private static readonly string[] ShellSource = [".swift", ".kt"];

    /// <summary>Kotlin only — the credential flag is a Kotlin spelling.</summary>
    private static readonly string[] KotlinSource = [".kt"];

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
                // A duplicate pair would LAST-WRITE-WIN into the dictionary, so a second
                // entry carrying a larger count would silently raise the excuse ceiling
                // while the first entry's reason — the one a reviewer reads — stayed on
                // the page describing a narrower permission than is actually granted.
                Assert.False(ignoredCounts.ContainsKey(key),
                    $"duplicate ignored entry '{key}' in src/auth-semantics.json — two entries "
                    + "for one file and token silently collapse to whichever is listed last, "
                    + "so the reason a reviewer reads need not be the count that is enforced. "
                    + "Merge them into one entry with one count and one reason.");

                ignoredCounts[key] = c.GetInt32();
                ignoredSeen[key] = 0;
            }
        }

        foreach (ShellSourceScan.Hit occ in ScanOccurrences())
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
        // character. That is what the first two assertions below are for.
        //
        // THE THIRD ASSERTION EXISTS BECAUSE THE GUARD IS NOT THE ONLY WAY IN. A new
        // caller passing the flag widens the credential path in production exactly as
        // `if (true)` does, while writing neither the AUTH_DEVICE_CREDENTIAL token nor
        // either guard literal — so the count does not move and the guard still reads
        // as written. Demonstrated, not imagined: a two-line
        // `provisionKeyForRecovery` forwarding `allowDeviceCredentialForTest = true`
        // passed every other assertion in this file. The argument literal is countable
        // the same way the token is, so it is counted.
        //
        // WHAT IS STILL NOT COVERED, stated plainly rather than hedged, and the list
        // is one item SHORTER than it was because #364 F2 was a gap this block did not
        // name. It enumerated spelling variants only; the count itself was scoped to
        // ONE FILE, so a `BnRecovery.kt` added beside AndroidShellBridge.kt — the exact
        // spelling counted here, in the shipped shell — passed all four facts. FILE
        // SCOPE IS NOW THE ROSTER: the count below walks every `.kt` under every root
        // src/shell-source-roots.json says this pin consumes, so a new caller anywhere
        // in the Android shell reds. The template mirror is DELEGATED rather than
        // scanned — see the roster entry, and TheAuthBearingShellFile_IsStillInTheTemplateMirrorList,
        // which is what holds that delegation honest.
        //
        // WHAT REMAINS, unchanged and still real: the count is over ONE SPELLING of
        // the argument. A POSITIONAL call — `provisionKey(key, true, true)` — carries
        // no parameter name at all, and a named call written without spaces around
        // the `=` is a different string. Both slip past. Closing those needs a Kotlin
        // parser, not a scanner, and this pin does not have one. The cheap spellings
        // are pinned; the rest is a reviewer's job, and saying so here is the point.
        string repo = BnRepo.Root();
        string path = Path.Combine(repo,
            "src", "BlazorNative.Jni", "src", "androidMain", "kotlin", "io",
            "blazornative", "shell", "AndroidShellBridge.kt");
        Assert.True(File.Exists(path),
            $"{path} does not exist — a pin that cannot find its subject must fail loudly, "
            + "never vacuously");

        string code = CommentStrippedSource.Strip(File.ReadAllText(path));

        // The two guard assertions stay scoped to the DECLARING file on purpose: they
        // are about the declaration, which lives in exactly one place. Only the caller
        // count is a question about the whole tree.
        Assert.Contains("if (allowDeviceCredentialForTest)", code, StringComparison.Ordinal);

        // And the parameter must still default to false, or every caller gets the
        // credential path without asking for it.
        Assert.Contains("allowDeviceCredentialForTest: Boolean = false", code,
            StringComparison.Ordinal);

        // EXACTLY ONE caller may ask for the credential path: writeAuthBoundSecretForTest,
        // the instrumented test seam the ignore entry is written about. Counted over the
        // COMMENT-STRIPPED text, because the KDoc above provisionKey names the parameter
        // in prose and a raw-file count would score it. Exactly-one, not at-most-one:
        // if the seam is deleted, this entry and its manifest reason describe a caller
        // that no longer exists, and a stale excuse is a licence for the next one.
        //
        // EXACTLY-ONE IS ITS OWN NON-VACUITY FLOOR, which is why no separate file count
        // is asserted here: a walk that stops seeing its tree reports ZERO callers and
        // reds on the same line, with a message that says what zero means. A Swift root
        // in the same roster contributes nothing to a Kotlin spelling, by construction
        // and not by accident — it cannot take the count DOWN.
        // EXACTLY ONE caller may ask for the credential path: writeAuthBoundSecretForTest,
        // the instrumented test seam the ignore entry is written about. Counted over
        // COMMENT-STRIPPED text, because the KDoc above provisionKey names the parameter
        // in prose and a raw count would score it. Exactly-one, not at-most-one: if the
        // seam is deleted, this entry and its manifest reason describe a caller that no
        // longer exists, and a stale excuse is a licence for the next one.
        //
        // ── THIS FACT CARRIES ITS OWN COVERAGE, AND THAT IS NOT BOOKKEEPING ──────
        //
        // This pin runs TWO scans with different subjects, and for one review round
        // only the other one asserted coverage. Re-pointing THIS walk back at the old
        // private array left the whole repository at 35 passed / 0 failed with a live
        // second `allowDeviceCredentialForTest = true` caller under
        // `src/BlazorNative.Jni/src/main/kotlin` — a tree the other scan covers and
        // this one no longer read. Nothing else in the repository guards that literal,
        // so there was no second opinion to catch it.
        //
        // Exactly-one is a floor as well as a ceiling, and that is still true: a walk
        // that sees nothing reports ZERO and reds. What it could NOT tell you is
        // WHICH tree stopped being read, which is what the coverage assertion adds —
        // and a walk narrowed to the one tree that happens to hold the one legitimate
        // caller reports exactly one and looks perfect.
        // THROUGH THE SHARED PATTERN SCAN, NOT A MATCHER OF THIS PIN'S OWN. A
        // per-pin extractor is a function a single line inside can blind, and that
        // was demonstrated: `if (code.Contains("package io.blazornative.jni"))
        // yield break;` left the suite green with a live second caller. Routing
        // this through `ShellSourceScan.ForPattern` means there is no auth-specific
        // matching code here to put that line in — the matcher is shared with the
        // NSLog and AndroidLog pins and with both of their positive controls, which
        // is what makes a filter there something other facts are looking at.
        ShellSourceScan.Hit[] callers = ShellSourceScan
            .ForPattern(nameof(AuthSemanticsDriftTests), null, KotlinSource,
                        Regex.Escape(CallerLiteral))
            .HitsCoveringEveryDeclaredRoot();

        Assert.True(callers.Length == 1,
            $"expected exactly one caller passing `{CallerLiteral}` across the roots "
            + $"{nameof(AuthSemanticsDriftTests)} consumes in {ShellSourceRoots.ManifestPath}, "
            + $"found {callers.Length}:\n"
            + (callers.Length == 0
                ? "  (none)"
                : string.Join("\n", callers.Select(h => $"  {h.File}:{h.Line}")))
            + "\n\nThe ignore entry for AUTH_DEVICE_CREDENTIAL in src/auth-semantics.json rests "
            + "on there being ONE, writeAuthBoundSecretForTest, reachable only from the "
            + "instrumented suite. A second caller widens the credential path in production "
            + "without writing the token or touching the guard, so nothing else in this file "
            + "would notice — that is #364 F2, and it was demonstrated with a new file beside "
            + "the declaring one while this count still read a single path. Zero means the seam "
            + "was deleted and the manifest reason now describes code that is gone — and this "
            + "count is not blind, because it proved it read every Kotlin file the roster puts "
            + "in its scope before counting anything.");
    }

    /// <summary>The one spelling of the argument this pin counts. See the limit
    /// block in <see cref="TheTestOnlyCredentialBranch_IsStillGuarded"/>: a
    /// positional call and a spacing variant both slip past it.</summary>
    private const string CallerLiteral = "allowDeviceCredentialForTest = true";

    [Fact]
    public void TheCompletenessScan_IsNotVacuous()
    {
        // ITS OWN SCAN. `ScanOccurrences` asserts coverage before returning, so the
        // record this fact reasons about is the one its own call produced.
        ShellSourceScan.Hit[] scan = ScanOccurrences();

        // The sibling test above passes trivially if the scan finds nothing — a regex or
        // a path that stops matching turns the guard into a no-op that still reports
        // green. 14.1's equivalent completeness check shipped WITHOUT this assertion and
        // is a known open residual on main; this phase does not reproduce that hole.
        Assert.True(scan.Length >= 7,
            $"the authenticator scan found only {scan.Length} occurrences across both "
            + "shells, and there are at least 7 known live sites. The scan has stopped seeing "
            + "its subject — the completeness test above is now passing while checking "
            + "nothing.");

        Assert.Contains(scan, o => o.File.EndsWith(".swift", StringComparison.Ordinal));
        Assert.Contains(scan, o => o.File.EndsWith(".kt", StringComparison.Ordinal));
    }
}
