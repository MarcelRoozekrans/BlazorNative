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
    /// means adding it here — that is the one manual step this pin cannot remove.
    ///
    /// <para>THE THREE BARE NAMES AT THE BOTTOM ARE #364 F4, and they are not
    /// redundant with the dotted forms above them. An import alias —
    /// <c>import androidx.biometric.BiometricManager.Authenticators as Auth</c>,
    /// then <c>setAllowedAuthenticators(Auth.BIOMETRIC_WEAK or Auth.DEVICE_CREDENTIAL)</c>
    /// — writes a BiometricPrompt offering weak biometry or device credential
    /// without spelling one dotted token, and it was MEASURED 4/4 green.</para>
    ///
    /// <para>TWO SUPPRESSION RULES IN <see cref="AuthenticatorHits"/> ARE WHAT MAKE
    /// THEM SAFE TO ADD, and the numbers below are measured, not reasoned:</para>
    /// <list type="bullet">
    /// <item><description>with no suppression at all, the three bare names take the
    /// whole-tree occurrence count from <b>12 to 18</b> — six raw matches;</description></item>
    /// <item><description>the SPAN rule removes the five that sit inside a longer
    /// VOCABULARY token, exactly as <c>.deviceOwnerAuthentication</c> sits inside
    /// <c>.deviceOwnerAuthenticationWithBiometrics</c>;</description></item>
    /// <item><description>the IDENTIFIER-BOUNDARY rule removes the sixth, which sits
    /// inside a longer identifier the vocabulary does NOT know —
    /// <c>BiometricPrompt.ERROR_NO_DEVICE_CREDENTIAL</c>, an error code;</description></item>
    /// <item><description>so the live count is <b>9 before and 9 after</b>. Adding the
    /// three names moved nothing.</description></item>
    /// </list>
    ///
    /// <para>⚠ AN EARLIER VERSION OF THIS PARAGRAPH SAID "9 → 10 … and the one new
    /// occurrence is an <c>ignored</c> entry". Both halves are gone. The number was
    /// right for the code of the day and the <c>ignored</c> entry was the defect:
    /// a counted excuse is a LICENCE, and with the error arm deleted in the same
    /// commit a <c>typealias</c> spent it for a live
    /// <c>setAllowedAuthenticators(Auth.DEVICE_CREDENTIAL)</c> at <b>977 of 977
    /// green</b>. The boundary rule means there is nothing to excuse and no entry to
    /// spend. Its counterfactual figure was wrong too: it read "9 → 14" where the
    /// like-for-like figure is <b>15</b>, because the bare names contribute six raw
    /// matches and not five. That is its own small lesson — the 9 → 10 beside it WAS
    /// measured, and a number standing next to a measurement does not thereby become
    /// one. The three figures above are each a run: 12 and 18 are printed by a
    /// forced-fail of the completeness floor with the suppression rules commented
    /// out, and 9 is the same print with them in.</para>
    ///
    /// <para>ADDING THE NAMES CLOSES THE ALIAS AT THE USE SITE, which every binding
    /// construct shares. <see cref="NoShellSource_AliasesAnAuthenticatorNamespace"/>
    /// closes it at the BINDING site, which each of them varies, and is what covers
    /// an authenticator constant nobody has added to this list yet.</para></summary>
    private static readonly string[] AuthenticatorVocabulary =
    [
        ".deviceOwnerAuthenticationWithBiometrics",
        ".deviceOwnerAuthentication",
        ".biometryCurrentSet",
        ".biometryAny",
        ".userPresence",
        // `.devicePasscode` is the WEAKENING member of the same
        // SecAccessControlCreateFlags enum the three above come from, and it was
        // missing. MEASURED: adding it beside the declared flag —
        // `[.biometryCurrentSet, .devicePasscode]` in BnSecureStorage's
        // SecAccessControlCreateWithFlags call — was 978 of 978 GREEN, a
        // passcode-acceptable ACL under the #213 pin itself. A vocabulary that
        // carries three members of an enum and not its weakest one is a list
        // assembled from what the code does rather than from what the platform
        // offers.
        ".devicePasscode",
        "AUTH_BIOMETRIC_STRONG",
        "AUTH_BIOMETRIC_WEAK",
        "AUTH_DEVICE_CREDENTIAL",
        "Authenticators.BIOMETRIC_STRONG",
        "Authenticators.BIOMETRIC_WEAK",
        "Authenticators.DEVICE_CREDENTIAL",
        // The alias-defeating bare names. See the paragraphs above before touching
        // these: they are load-bearing for #364 F4 and their interaction with the
        // longer tokens is a SPAN suppression, not a coincidence.
        "BIOMETRIC_STRONG",
        "BIOMETRIC_WEAK",
        "DEVICE_CREDENTIAL",
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
            int end = start + length;
            while (at < end && char.IsWhiteSpace(stripped[at])) at++;

            // ── THE IDENTIFIER-BOUNDARY RULE — the SIBLING of the span rule ──────
            //
            // The span rule above suppresses a token that sits inside a LONGER
            // VOCABULARY token. This one suppresses a token that sits inside a
            // longer IDENTIFIER THE VOCABULARY DOES NOT KNOW, which is a different
            // set and was a live hole: `BiometricPrompt.ERROR_NO_DEVICE_CREDENTIAL`
            // is an error CODE, and bare `DEVICE_CREDENTIAL` matched inside it.
            //
            // WHY THIS IS THE ROOT FIX AND AN `ignored` ENTRY WAS NOT. The first
            // cut excused that echo with a counted `ignored` entry, and a counted
            // excuse is a LICENCE: delete the error arm and the count is free to be
            // spent by a real occurrence somewhere else in the same file. MEASURED,
            // on the tree that shipped it — `private typealias Auth =
            // BiometricManager.Authenticators` plus
            // `.setAllowedAuthenticators(Auth.DEVICE_CREDENTIAL)`, with the error
            // arm deleted and the template mirrored, was **977 of 977 GREEN** with a
            // device-credential-accepting prompt live in the shipped shell. A star
            // import of the same namespace was green the same way. Both red the
            // moment the licence is gone, because both end at a bare name.
            //
            // THE RULE APPLIES ONLY WHERE IT MEANS ANYTHING: a match that BEGINS
            // with an identifier character must not be PRECEDED by one, and a match
            // that ENDS with one must not be FOLLOWED by one. The Apple tokens
            // begin with `.` and are untouched — that is not an accident to be
            // relied on, it is why the condition is written on the matched text
            // rather than on the token: `LAPolicy.deviceOwnerAuthentication` must
            // keep matching, and it does, because the match starts at the dot.
            if (at < end
                && IsIdentifierChar(stripped[at])
                && at > 0 && IsIdentifierChar(stripped[at - 1]))
                continue;

            if (end > at
                && IsIdentifierChar(stripped[end - 1])
                && end < stripped.Length && IsIdentifierChar(stripped[end]))
                continue;

            int line = 1;
            for (int k = 0; k < at; k++) if (stripped[k] == '\n') line++;

            yield return new ShellSourceScan.RawHit(line, token, token);
        }
    }

    /// <summary>What counts as part of a Kotlin or Swift identifier, for the
    /// boundary rule in <see cref="AuthenticatorHits"/>. Both languages allow
    /// letters, digits and underscore; neither allows a dot, which is what makes
    /// `KeyProperties.AUTH_BIOMETRIC_STRONG` a boundary and
    /// `ERROR_NO_DEVICE_CREDENTIAL` not one.</summary>
    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The authenticator scan, over every root this pin consumes. Each
    /// fact calls this itself so that it asserts coverage on its OWN record —
    /// a sibling's walk proves nothing about this one.</summary>
    private static ShellSourceScan.Hit[] ScanOccurrences() =>
        ShellSourceScan
            .Over(nameof(AuthSemanticsDriftTests), null, ShellSource, AuthenticatorHits)
            .HitsCoveringEveryDeclaredRoot();

    /// <summary>The two shells' COMPILED source extensions — every language the
    /// build turns into shipped output from a declared root.
    ///
    /// <para>⚠ <c>.java</c> IS NOT DECORATION AND IT IS NOT DEFENSIVE. It was a
    /// measured hole: <c>build.gradle.kts</c> declares
    /// <c>java.srcDirs("src/main/kotlin", "src/androidMain/kotlin")</c>, so Java in
    /// those directories compiles into the AAR, and this list said
    /// <c>[".swift", ".kt"]</c>. An <c>AuthFlags.java</c> holding
    /// <c>Authenticators.DEVICE_CREDENTIAL</c>, with
    /// <c>.setAllowedAuthenticators(io.blazornative.jni.AuthFlags.DC)</c> in the
    /// shell, was <b>978 of 978 GREEN</b> in the shipped tree.</para>
    ///
    /// <para>AND THE COVERAGE GUARD COULD NOT SEE IT EITHER, which is the part worth
    /// keeping in mind. <c>ShellSourceScan</c>'s per-root set equality recomputes its
    /// candidate files from THIS SAME LIST, so narrowing the list narrows the walk
    /// and the proof that the walk was complete, together — the shape three review
    /// rounds removed from the ROOT list and nobody had removed from the EXTENSION
    /// list. <see cref="TheScannedExtensions_CoverEveryLanguageTheBuildCompiles"/> is
    /// what makes that list answer to the build file instead of to this line, the
    /// way the roster already makes root lists answer to it.</para></summary>
    private static readonly string[] ShellSource = [".swift", ".kt", ".java"];

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
        // matching code here to put that line in.
        //
        // ⚠ AND THAT IS THE WHOLE OF WHAT IT BUYS. This block used to add that the
        // shared matcher is "shared with the NSLog and AndroidLog pins and with
        // both of their positive controls, which is what makes a filter there
        // something other facts are looking at". BOTH HALVES WERE MEASURED FALSE.
        // A content-keyed filter INSIDE the shared matcher is green — content is a
        // proxy for a path, and one line hides a tree; and only ONE of those two
        // controls runs through this matcher at all, the other walking its own
        // roster-EXCLUDED tree and sharing nothing but the pattern. See limit A at
        // `ShellSourceScan`, which is where the measurement lives. Moving the
        // matching out of this pin removed a PIN-LOCAL place to put the filter; it
        // did not put a guard on the shared one.
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

    // ── #364 F4 — THE ALIAS ──────────────────────────────────────────────────

    /// <summary>THE ALIAS SHAPE, spelled ONCE. The ban reads it through the shared
    /// scan and the control reads it over a fixture, so the two cannot drift into
    /// guarding different patterns (pin standard, Rule 8).
    ///
    /// <para>SCOPE, deliberately narrow: only the namespaces that actually CARRY
    /// authenticator constants. <c>BiometricManager</c> nests <c>Authenticators</c>;
    /// <c>KeyProperties</c> holds the <c>AUTH_*</c> flags; <c>Authenticators</c> can
    /// be imported directly; <c>LocalAuthentication</c> is the Apple module. An
    /// <c>import … as</c> of anything else in shell source is none of this pin's
    /// business.</para>
    ///
    /// <para>The <c>alias</c> group exists so the failure message can name the
    /// binding a reader has to go and delete. It is read in the message only —
    /// the DETECTION is the match itself.</para></summary>
    /// <summary>The namespaces that CARRY authenticator constants, spelled once so
    /// the three construct arms below cannot come to disagree about which
    /// namespaces they are about.</summary>
    private const string AuthenticatorNamespace =
        @"(?:BiometricManager|KeyProperties|Authenticators|LocalAuthentication)";

    /// <summary>THE THREE KOTLIN CONSTRUCTS THAT BIND AN AUTHENTICATOR NAMESPACE
    /// TO A SHORTER NAME. Enumerated deliberately rather than patched one at a
    /// time — see the enumeration table on
    /// <see cref="NoShellSource_AliasesAnAuthenticatorNamespace"/>, where every row
    /// was measured.</summary>
    private const string AuthenticatorNamespaceAlias =
        // 1. `import a.b.BiometricManager.Authenticators as Auth`
        @"(?:import\s+(?:static\s+)?[\w.]*" + AuthenticatorNamespace + @"[\w.]*\s+as\s+(?<alias>\w+))"
        // 2. `import a.b.BiometricManager.Authenticators.*` — members unqualified,
        //    which binds every constant at once WITHOUT naming any of them. The
        //    `static` is Java's spelling of the same thing, and it is here because
        //    `.java` is now a scanned extension: `import static
        //    androidx.biometric.BiometricManager.Authenticators.*;` is exactly arm 2
        //    in the other language, and `[\w.]*` cannot cross the space after
        //    `static`.
        + @"|(?:import\s+(?:static\s+)?[\w.]*" + AuthenticatorNamespace + @"[\w.]*\s*\.\s*\*)"
        // 3. `typealias Auth = BiometricManager.Authenticators` — the sibling the
        //    first cut of this ban missed, and the one that was measured 977/977
        //    green with a device-credential prompt live.
        + @"|(?:typealias\s+(?<alias>\w+)\s*=\s*[\w.]*" + AuthenticatorNamespace + @"[\w.]*)";

    /// <summary>AN ALIAS MAKES EVERY DOTTED TOKEN UNSEEABLE, so the construct is
    /// refused rather than the spellings chased. #364 F4:
    /// <c>import androidx.biometric.BiometricManager.Authenticators as Auth</c>
    /// then <c>.setAllowedAuthenticators(Auth.BIOMETRIC_WEAK or Auth.DEVICE_CREDENTIAL)</c>
    /// was 4/4 GREEN — a prompt offering weak biometry or device credential, the
    /// #213 divergence itself, under a green pin.
    ///
    /// <para>Adding bare constant names closes THAT alias. It does not close the
    /// CLASS: the next binding invents a spelling nobody listed. This does, by
    /// making it not compile past review — 14.0's bar, in this repo's own words:
    /// divergence stops being REPRESENTABLE rather than merely detected.</para>
    ///
    /// <para>⚠ THE FIRST CUT OF THIS BAN MATCHED ONLY <c>import … as</c>, AND THAT
    /// WAS A GUESS DRESSED AS A RULE. Review measured the sibling: a Kotlin
    /// <c>typealias</c>, with the one excused bare occurrence freed, was
    /// <b>977 of 977 GREEN</b> with <c>.setAllowedAuthenticators(Auth.DEVICE_CREDENTIAL)</c>
    /// live in the shipped shell AND its template mirror. So the constructs are now
    /// ENUMERATED rather than collected one review at a time, and every row below
    /// was measured on the tree that shipped the first cut:</para>
    ///
    /// <list type="table">
    /// <item><description><b>import … as</b> — banned here, arm 1. Was RED already.</description></item>
    /// <item><description><b>import … .*</b> — banned here, arm 2. A star import binds
    /// every constant at once while naming none, and it was <b>977/977 GREEN</b>
    /// before this arm existed. It is the row that proves enumerating beats patching:
    /// a typealias-only repair would have left it open.</description></item>
    /// <item><description><b>typealias</b> — banned here, arm 3. The review's finding.</description></item>
    /// <item><description><b>import … .MEMBER</b>, no alias — NOT banned and does not
    /// need to be: the import line spells the constant, so the use site is a SECOND
    /// occurrence and the count reds. Measured RED.</description></item>
    /// <item><description><b>import … .MEMBER as X</b> — caught by arm 1, which does not
    /// care whether the aliased thing is a namespace or a member.</description></item>
    /// <item><description><b>a val or fun holding the qualified constant</b> — NOT banned
    /// and does not need to be: it must WRITE
    /// <c>Authenticators.DEVICE_CREDENTIAL</c> to read it. Measured RED.</description></item>
    /// <item><description><b>a .java file in a declared root</b> — not a construct at
    /// all. <b>978/978 GREEN</b> until <c>".java"</c> joined
    /// <see cref="ShellSource"/>; the walk never read the file, and neither did the
    /// coverage guard, which recomputes from the same list. Held to the build by
    /// <see cref="TheScannedExtensions_CoverEveryLanguageTheBuildCompiles"/>. Java's
    /// <c>import static … .*</c> is arm 2 in the other language and is matched
    /// there.</description></item>
    /// <item><description><b>a platform API that names nothing</b> —
    /// <c>setDeviceCredentialAllowed(true)</c> and two siblings, each <b>978/978
    /// GREEN</b>. Not a binding and not a constant; refused by
    /// <see cref="NoShellSource_WidensTheGateThroughAPlatformApi"/>.</description></item>
    /// <item><description><b>the raw platform integers</b> —
    /// <c>setAllowedAuthenticators(0x0000000F or 0x00008000)</c>. <b>NOT CLOSED, and
    /// not closeable by a token scanner:</b> there is no name to find. Measured
    /// <b>978/978 GREEN</b>, and it was green before this phase too. FAILS GREEN; see
    /// the limit list below.</description></item>
    /// </list>
    ///
    /// <para>AND THE CONSTRUCT BAN IS THE SECOND LINE, NOT THE FIRST. What actually
    /// closed the review's attack is that <see cref="AuthenticatorHits"/> no longer
    /// reports a bare name that is a FRAGMENT of a longer identifier, so
    /// <c>ERROR_NO_DEVICE_CREDENTIAL</c> needs no <c>ignored</c> entry and there is
    /// no counted licence left to spend. This ban is the second line, at the BINDING
    /// site, and it is what covers an authenticator constant nobody has added to the
    /// vocabulary yet.</para>
    ///
    /// <para>⚠ THE SENTENCE THAT USED TO JOIN THOSE TWO — <i>"that closes every route
    /// AT THE USE SITE, which all of them share"</i> — IS DELETED. It was measured
    /// false in the very next review: a <c>.java</c> file in a declared root held
    /// <c>Authenticators.DEVICE_CREDENTIAL</c> and the use site read
    /// <c>AuthFlags.DC</c>, which shares no name with anything — <b>978 of 978
    /// GREEN</b>. The route was not a construct at all; it was a FILE EXTENSION the
    /// walk did not read.</para>
    ///
    /// <para>DO NOT WRITE ITS REPLACEMENT. Four rounds of this pin have each closed
    /// the route they were shown and each ended by claiming the class was shut: "the
    /// bare names close it", "the use site closes it", and twice before that. The
    /// roster's own <c>$doc</c> carries the same warning about the airtight sentence
    /// and it applies here word for word. What is true is smaller and more useful —
    /// four mechanisms, each catching a NAMED set of shapes, each shape measured
    /// green before its mechanism existed, and everything unmeasured living in a
    /// residual list rather than inside a generalisation.</para>
    ///
    /// <para>SCOPE, deliberately narrow — see <see cref="AuthenticatorNamespace"/>.
    /// A ban that reds on unrelated code is one the next author weakens rather than
    /// obeys.</para>
    ///
    /// <para>WHAT THIS DOES NOT COVER (pin standard, Rule 5), each with its
    /// direction:</para>
    /// <list type="bullet">
    /// <item><description>SWIFT REACHES TWO OF THE THREE ARMS, not none — which is a
    /// correction, because the first cut of this bullet called the whole
    /// <c>.swift</c> half inert. Swift has no <c>import … as</c> and no member star
    /// import, so arms 1 and 2 are dead there; it very much has <c>typealias</c>,
    /// and arm 3 reds on <c>typealias Auth = LocalAuthentication.LAPolicy</c> in a
    /// <c>.swift</c> file — MEASURED, 1 red. The Apple vocabulary is dot-leading
    /// enum shorthand (<c>.deviceOwnerAuthentication</c>) and so was never hideable
    /// behind a Swift alias in the first place, which makes arm 3's Swift reach
    /// defence in depth rather than the load-bearing part. The walk covers
    /// <c>.swift</c> because the roster says this consumer reads both shells:
    /// narrowing an extension list to dodge a half you believe is empty is how a
    /// pin stops seeing a tree, and this bullet is the evidence that the belief can
    /// be wrong.</description></item>
    /// <item><description>⚠ THE SENTENCE THAT STOOD HERE — <i>"So the route is
    /// covered — by the BARE NAMES"</i> — WAS FALSE, AND IT IS DELETED RATHER THAN
    /// SOFTENED. It was written from a measurement that could not see the damage:
    /// the typealias was planted with <c>Auth.BIOMETRIC_WEAK</c>, which has no
    /// excuse count, so the red it produced said nothing whatever about
    /// <c>Auth.DEVICE_CREDENTIAL</c>, which had one. Review planted the same
    /// construct with the excused name and got <b>977 of 977 GREEN</b>. THE LESSON
    /// IS THE GENERAL ONE AND IT BELONGS HERE RATHER THAN IN A REPORT: a mutation
    /// proves nothing if the fixture cannot reach the state under test, and picking
    /// the variant that happens to red is the easiest way to prove nothing while
    /// feeling thorough. The same shape cost this phase an
    /// <c>android.util.Log.i</c> plant the pin deliberately ignores. <b>Choose the
    /// plant that is hardest for the pin to see, not the one nearest to
    /// hand.</b></description></item>
    /// <item><description>A KOTLIN <c>typealias</c> IS NOW BANNED, as arm 3 — see the
    /// enumeration above. So is a star import, as arm 2. Neither is a limit any
    /// more; both are listed here only because a reader arriving from the first cut
    /// of this pin will be looking for them.</description></item>
    /// <item><description>THE RAW PLATFORM INTEGERS ARE NOT CLOSED AND CANNOT BE BY
    /// THIS MECHANISM. <c>setAllowedAuthenticators(0x0000000F or 0x00008000)</c> is
    /// <c>BIOMETRIC_STRONG or DEVICE_CREDENTIAL</c> with no name anywhere for a
    /// token scanner to find. MEASURED <b>977 of 977 GREEN</b>, with the error arm
    /// left intact so the red could not come from somewhere else. It was green
    /// before this phase and it is green after; closing it needs a Kotlin
    /// type-resolving parser, which this repo does not have, and saying so is the
    /// point. FAILS GREEN.</description></item>
    /// <item><description>The scan is over comment-stripped code, so this text and
    /// any Kotlin KDoc describing the rule cannot satisfy it. It is NOT
    /// string-literal-blind: MEASURED, a <c>const val</c> holding the banned import
    /// as a STRING reds this fact. FAILS RED, which is the cheap direction — and it
    /// is the reason the banned spelling must not be written into shell source even
    /// as an example.</description></item>
    /// <item><description>A CONTENT-KEYED FILTER INSIDE THE SHARED MATCHER blinds
    /// this fact, and its own control only notices when the filter also blinds the
    /// control's FIXTURE. MEASURED both ways, inside
    /// <c>PatternHitsAcrossLines</c> with a live binding planted: keyed on
    /// <c>package io.blazornative.shell</c> — the file the fixture is spliced from —
    /// it reds <b>1</b>, the control, while the ban itself stays green; keyed on
    /// <c>package io.blazornative.jni</c>, with the binding under
    /// <c>src/main/kotlin</c> instead, the set is <b>31 of 31 GREEN</b>. A control's
    /// fixture only covers the tree its fixture comes from. FAILS GREEN. This is
    /// limit A at <c>ShellSourceScan</c>, not a new one, and it is repeated here
    /// because a pin's limits belong where the pin is.</description></item>
    /// <item><description>A FILTER IN <c>ForPatternAcrossLines</c>'S OWN LAMBDA —
    /// outside the matcher, so no control that calls the matcher directly can see it.
    /// <b>31 of 31 GREEN</b> with a live binding. The two text assertions in
    /// <see cref="AssertEveryBanIsWiredToTheDoorItsSubjectNeeds"/> hold the chain's
    /// first two links; this shape adds a condition INSIDE a body they only check the
    /// delegation of. FAILS GREEN.</description></item>
    /// <item><description>EVERY DENOMINATOR HERE NAMES ITS FILTER — the four
    /// <c>ShellSourceScan</c> consumers, AuthSemantics, ShellSourceRoots, NSLog and
    /// AndroidLog. An earlier figure was written from a run over a different filter
    /// and read as derived; a count with no filter beside it cannot be reproduced and
    /// will drift again.</description></item>
    /// </list></summary>
    [Fact]
    public void NoShellSource_AliasesAnAuthenticatorNamespace()
    {
        // ITS OWN SCAN, through the shared matcher — no auth-specific matching code
        // here to put a one-line filter in, and the call that proves coverage
        // returns the very array the assertion below consumes.
        ShellSourceScan.Hit[] aliases = ShellSourceScan
            .ForPatternAcrossLines(nameof(AuthSemanticsDriftTests), null, ShellSource,
                                   AuthenticatorNamespaceAlias)
            .HitsCoveringEveryDeclaredRoot();

        Assert.True(aliases.Length == 0,
            "A BINDING OF AN AUTHENTICATOR NAMESPACE TO A SHORTER NAME (#364 F4). Each one below "
            + "makes every dotted token in AuthenticatorVocabulary unseeable, so a BiometricPrompt "
            + "offering weak biometry or device credential — the #213 semantic divergence itself "
            + "— can be written without spelling one of them:\n"
            + string.Join("\n", aliases.Select(h => $"  {h.File}:{h.Line} — {Bound(h.Token)}"
                                                    + $"\n      {h.Token}"))
            + "\n\nTHE FIX IS TO DELETE THE BINDING and spell the namespace out at the call site "
            + "— `BiometricManager.Authenticators.BIOMETRIC_STRONG`, "
            + "`KeyProperties.AUTH_BIOMETRIC_STRONG`. IT IS NOT to widen "
            + "AuthenticatorVocabulary with whatever this binding happens to be called: that "
            + "closes one spelling and leaves the class open, which is exactly what #364 F4 "
            + "demonstrated about the bare names, and what a typealias then demonstrated about "
            + "the first cut of this ban. If a new namespace genuinely carries authenticator "
            + "constants, add it to AuthenticatorNamespace and to the vocabulary both, "
            + "deliberately, with a reason.");
    }

    /// <summary>How one matched construct is described in the failure message. Two
    /// of the three arms capture a name; the star import binds every member at once
    /// and captures none, and a message reading "binds ``" would send a reader
    /// looking for an identifier that is not there.</summary>
    private static string Bound(string construct)
    {
        string alias = Regex.Match(construct, AuthenticatorNamespaceAlias).Groups["alias"].Value;
        return alias.Length > 0
            ? $"binds `{alias}`"
            : "star-imports its members, so every constant is reachable unqualified";
    }

    /// <summary>THE POSITIVE CONTROL FOR <see cref="AuthenticatorNamespaceAlias"/>
    /// (pin standard, Rule 3). The name is used in its narrow sense: a fixed point
    /// the DETECTOR is required to hit.
    ///
    /// <para>NO TREE ANCHOR EXISTS AND NONE MAY. The pin's whole claim is that no
    /// shell source aliases an authenticator namespace, so its subject tree is
    /// REQUIRED to be empty of the pattern — the
    /// <see cref="ReleaseWorkflowPinTests.TheOverrideDetector_StillMatchesTheShapeItWasWrittenFor"/>
    /// situation exactly, and Rule 3's corollary says the answer is then a fixture.
    /// The roster's exclusion list was checked first, as Rule 3's second anchor
    /// model instructs, and it does not help: no excluded tree aliases one either,
    /// and planting one there to be an anchor would put the construct back in the
    /// repository for the sake of guarding against it.</para>
    ///
    /// <para>SO THE FIXTURE IS SPLICED INTO THE REAL FILE, and it is #364 F4
    /// verbatim: the finding's own import line goes immediately below the real
    /// <c>import androidx.biometric.BiometricManager</c> in the real
    /// AndroidShellBridge.kt, and the finding's own aliased call replaces the real
    /// <c>setAllowedAuthenticators</c> line. It is then driven through
    /// <see cref="ShellSourceScan.PatternHitsAcrossLines"/> — THE PRODUCTION MATCHER, the one
    /// <see cref="ShellSourceScan.ForPattern"/> itself calls — so this controls the
    /// path the pin uses rather than a restatement of it. Exactly one hit is
    /// demanded, AT THE LINE THE SPLICE LANDED ON, so line fidelity through the
    /// comment stripper is exercised and not merely the regex.</para>
    ///
    /// <para>WHAT A FIXTURE CANNOT BUY, said plainly: it proves the detector still
    /// recognises the shape, never that the detector is pointed at anything real.
    /// That second property is what
    /// <c>HitsCoveringEveryDeclaredRoot</c> buys inside
    /// <see cref="NoShellSource_AliasesAnAuthenticatorNamespace"/>, and the two are
    /// only worth anything read together.</para></summary>
    [Fact]
    public void TheAliasDetector_StillMatchesTheShapeItWasWrittenFor()
    {
        AssertEveryBanIsWiredToTheDoorItsSubjectNeeds();

        string[] lines = File.ReadAllText(AuthBearingShellFile())
            .Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        // ── the subject-moved guards (pin standard, Rule 4) ──────────────────
        int importAt = Array.FindIndex(lines, l =>
            l.Trim() == "import androidx.biometric.BiometricManager");
        Assert.True(importAt >= 0,
            "no bare `import androidx.biometric.BiometricManager` line in "
            + $"{AuthBearingShellFileRelative} to splice #364 F4's alias below. Either the import "
            + "block was rewritten or the shell stopped using BiometricManager — this control then "
            + "has nowhere realistic to put the alias it is testing for. Re-point it at the real "
            + "import block deliberately rather than falling back to a bare string, which would "
            + "stop exercising the real file altogether.");

        int callAt = Array.FindIndex(lines, l => l.Contains(
            ".setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG)",
            StringComparison.Ordinal));
        Assert.True(callAt >= 0,
            "no `setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG)` line "
            + $"in {AuthBearingShellFileRelative}. That call IS the prompt #364 F4 weakens, and "
            + "this control replaces it with the finding's aliased version. If the prompt moved, "
            + "re-point this control AND check src/auth-semantics.json's androidPromptInfo site "
            + "— they are describing the same line.");

        var spliced = lines.ToList();
        spliced[callAt] = "                .setAllowedAuthenticators("
            + "Auth.BIOMETRIC_WEAK or Auth.DEVICE_CREDENTIAL)";
        spliced.Insert(importAt + 1,
            "import androidx.biometric.BiometricManager.Authenticators as Auth");

        // The call sits BELOW the import, so the insert shifts it down one line.
        var found = ShellSourceScan
            .PatternHitsAcrossLines(string.Join("\n", spliced), AuthenticatorNamespaceAlias)
            .ToList();

        // SCOPED TO THE SPLICED LINE, NOT TO THE WHOLE FIXTURE, and that is a
        // decoupling rather than a weakening. The fixture is built from the REAL
        // file, so a genuine live alias — the very thing the sibling ban exists to
        // red on — would otherwise break this control's arithmetic too, and a
        // control that reds because its pin's subject is broken tells a reader
        // nothing about the detector. The over-match direction is covered below,
        // by a fixture built for it.
        var atSplice = found.Where(h => h.Line == importAt + 2).ToList();

        Assert.True(
            atSplice.Count == 1
            && Regex.Match(atSplice[0].Token, AuthenticatorNamespaceAlias).Groups["alias"].Value == "Auth",
            "THE ALIAS DETECTOR NO LONGER DETECTS. With #364 F4's own "
            + "`import androidx.biometric.BiometricManager.Authenticators as Auth` spliced into "
            + $"{AuthBearingShellFileRelative} at line {importAt + 2}, the shared matcher reported: "
            + (found.Count == 0
                ? "(nothing)"
                : string.Join("; ", found.Select(h => $"line {h.Line}: {h.Token}")))
            + $" — expected exactly one, at line {importAt + 2}, binding `Auth`.\n"
            + "  Zero matches means NoShellSource_AliasesAnAuthenticatorNamespace is an absence "
            + "claim over a pattern that can no longer see its subject: the exact import that made "
            + "a weak-biometry-or-device-credential prompt 4/4 green would pass, and NOTHING would "
            + "red. A match at the wrong line means the line numbering through the comment stripper "
            + "drifted, so the failure message would send a reader to the wrong line of the file "
            + "that holds the shell's whole auth surface.\n"
            + "  Fix the pattern. Do not green this by editing the expectation.");

        // ── EVERY ARM, NOT ONLY THE ONE THE FINDING WAS WRITTEN ABOUT ────────
        //
        // The first cut of this control exercised arm 1 alone, because arm 1 was
        // the arm that existed. That is how a ban ends up covering the spelling it
        // was shown: a control built around one instance cannot tell you the other
        // arms ever worked. Each arm is spliced at the SAME anchor and asserted at
        // the SAME line, so a reader can see that the three are held to one
        // standard rather than to three.
        foreach ((string construct, string expect) in new[]
                 {
                     ("import androidx.biometric.BiometricManager.Authenticators as Auth",
                      "binds `Auth`"),
                     ("import androidx.biometric.BiometricManager.Authenticators.*",
                      "star-imports its members, so every constant is reachable unqualified"),
                     ("private typealias Auth = BiometricManager.Authenticators",
                      "binds `Auth`"),
                     // JAVA'S SPELLING OF ARM 2, and it is here because a mutation
                     // found it missing: deleting `(?:static\s+)?` from arm 2 left
                     // all eleven facts GREEN. `.java` is a scanned extension now,
                     // so `import static …Authenticators.*;` is a live shape and
                     // `[\w.]*` cannot cross the space after `static`.
                     ("import static androidx.biometric.BiometricManager.Authenticators.*;",
                      "star-imports its members, so every constant is reachable unqualified"),
                     // A TYPEALIAS WRAPS TOO. Before the matcher lost its line
                     // boundary this shape reached the ban's own detector as
                     // nothing; only the vocabulary at the use site reddened, and
                     // the vocabulary is exactly the backstop a constant nobody has
                     // listed does not have.
                     ("private typealias Auth =\n    BiometricManager.Authenticators",
                      "binds `Auth`"),
                 })
        {
            var armed = lines.ToList();
            armed.Insert(importAt + 1, construct);

            var armHits = ShellSourceScan
                .PatternHitsAcrossLines(string.Join("\n", armed), AuthenticatorNamespaceAlias)
                .Where(h => h.Line == importAt + 2)
                .ToList();

            Assert.True(armHits.Count == 1 && Bound(armHits[0].Token) == expect,
                $"THE BAN NO LONGER REFUSES `{construct}`. Spliced into "
                + $"{AuthBearingShellFileRelative} at line {importAt + 2}, the shared matcher "
                + "reported: "
                + (armHits.Count == 0
                    ? "(nothing)"
                    : string.Join("; ", armHits.Select(h => $"{h.Token} → {Bound(h.Token)}")))
                + $" — expected exactly one, described as `{expect}`.\n"
                + "  EVERY ARM OF THIS BAN IS LOAD-BEARING AND EACH WAS MEASURED GREEN BEFORE IT "
                + "EXISTED. The typealias arm: 977 of 977 passing with "
                + "`setAllowedAuthenticators(Auth.DEVICE_CREDENTIAL)` live in the shipped shell. "
                + "The star-import arm: the same, reached a different way. Losing an arm does not "
                + "narrow this pin, it reopens a measured hole.");
        }

        // ── the negative: shapes the ban must NOT claim ──────────────────────
        // A ban that reds on ordinary imports is one the next author weakens rather
        // than obeys, so the narrowness is pinned and not merely promised.
        var benign = ShellSourceScan.PatternHitsAcrossLines(
            "import io.blazornative.jni.FlatJson as Json\n"
            + "import androidx.biometric.BiometricPrompt as BP\n"
            + "import kotlinx.coroutines.flow.Flow as KFlow\n"
            + "import androidx.biometric.BiometricManager\n"
            // One near miss per NEW arm, or the arms are only pinned in the
            // widening direction. A star import of the biometric PACKAGE is
            // ordinary Kotlin and binds no authenticator constant; a typealias of
            // a callback type is the commonest typealias in Android code and must
            // not be collateral.
            + "import androidx.biometric.*\n"
            + "import kotlinx.coroutines.flow.*\n"
            + "typealias AuthCallback = BiometricPrompt.AuthenticationCallback\n"
            + "typealias Completion = (Boolean) -> Unit\n",
            AuthenticatorNamespaceAlias).ToList();

        Assert.True(benign.Count == 0,
            "the alias ban claimed an import it must not: "
            + string.Join("; ", benign.Select(h => $"line {h.Line}: {h.Token}"))
            + ". Its scope is the namespaces that CARRY authenticator constants — BiometricManager, "
            + "KeyProperties, Authenticators, LocalAuthentication — and nothing else. Aliasing "
            + "BiometricPrompt hides no authenticator: the constants still come from "
            + "BiometricManager.Authenticators. An un-aliased import is not an alias at all. A ban "
            + "widened until it reds on ordinary Kotlin is a ban that gets deleted.");
    }

    /// <summary>THE POSITIVE CONTROL FOR THE THREE BARE NAMES, and for the SPAN
    /// rule that makes them safe to carry (pin standard, Rule 3).
    ///
    /// <para>Two of the three have no live fixed point and cannot have one: a bare
    /// <c>BIOMETRIC_WEAK</c> or <c>BIOMETRIC_STRONG</c> in shell source is either a
    /// declared site or a defect. The third DOES have one —
    /// <c>BiometricPrompt.ERROR_NO_DEVICE_CREDENTIAL</c>, held at exactly one
    /// occurrence by the `ignored` entry's `count`, which reds at zero if bare
    /// <c>DEVICE_CREDENTIAL</c> stops matching. That is Rule 3's second anchor
    /// model — the exclusion list read as a source of anchors — and it covers one
    /// name of three, so the other two get a fixture.</para>
    ///
    /// <para>IT ALSO PINS THE SPAN RULE, which is the thing that kept the
    /// occurrence count at 9 → 10 instead of 9 → 14 when the bare names were added.
    /// A bare name matching INSIDE a longer token must be suppressed as a false
    /// echo; a bare name matching after an ALIAS must survive. Both directions are
    /// asserted, because a suppression rule loosened in either one is silent: too
    /// eager and the alias goes unseen, too lax and every AUTH_-prefixed site is
    /// double-reported until somebody widens the manifest to make it stop.</para>
    /// </summary>
    [Fact]
    public void TheBareAuthenticatorNames_SurviveAnAlias_AndAreSuppressedInsideLongerTokens()
    {
        // Lines 1-2 are the shapes the bare names exist for; 3-6 are the longer
        // VOCABULARY tokens they must not double-report; 7 is the longer
        // NON-vocabulary identifier, which is a different suppression rule.
        //
        // THE _WEAK ARMS ARE HERE BECAUSE REVIEW FOUND THEM MISSING. The first cut
        // exercised the _STRONG and DEVICE_CREDENTIAL arms only, which pins the
        // span rule for the token lengths that happened to be live and says nothing
        // about the others. The rule is generic over length; the fixture now is too.
        var hits = AuthenticatorHits(
            "                .setAllowedAuthenticators(Auth.BIOMETRIC_WEAK or Auth.DEVICE_CREDENTIAL)\n"
            + "                .setAllowedAuthenticators(Auth.BIOMETRIC_STRONG)\n"
            + "                KeyProperties.AUTH_BIOMETRIC_STRONG or KeyProperties.AUTH_DEVICE_CREDENTIAL\n"
            + "                BiometricManager.Authenticators.BIOMETRIC_STRONG\n"
            + "                KeyProperties.AUTH_BIOMETRIC_WEAK\n"
            + "                BiometricManager.Authenticators.BIOMETRIC_WEAK or BiometricManager.Authenticators.DEVICE_CREDENTIAL\n"
            + "                BiometricPrompt.ERROR_NO_DEVICE_CREDENTIAL -> UNAVAILABLE\n"
            // Line 8 is the APPLE half, and it is here because a mutation found it
            // uncontrolled: deleting `.devicePasscode` from the vocabulary left all
            // eleven facts GREEN. It is also the real weakening shape rather than a
            // lone token — a SecAccessControl flag ARRAY, where the declared flag is
            // still present and the passcode flag has been added beside it, which is
            // exactly how a widening arrives in review looking like an addition.
            + "                [.biometryCurrentSet, .devicePasscode],\n").ToList();

        string report = hits.Count == 0
            ? "(nothing)"
            : string.Join("; ", hits.OrderBy(h => h.Line).ThenBy(h => h.Token, StringComparer.Ordinal)
                .Select(h => $"line {h.Line}: {h.Token}"));

        // THE SURVIVING HALF — the three bare names, each after an alias.
        Assert.True(
            hits.Count(h => h.Line == 1 && h.Token == "BIOMETRIC_WEAK") == 1
            && hits.Count(h => h.Line == 1 && h.Token == "DEVICE_CREDENTIAL") == 1
            && hits.Count(h => h.Line == 2 && h.Token == "BIOMETRIC_STRONG") == 1,
            "THE BARE AUTHENTICATOR NAMES NO LONGER MATCH AN ALIASED CALL. #364 F4's own "
            + "`Auth.BIOMETRIC_WEAK or Auth.DEVICE_CREDENTIAL` must produce one occurrence of each "
            + "bare name, and `Auth.BIOMETRIC_STRONG` one of the third. The matcher reported: "
            + report + ".\n"
            + "  Removing a bare name from AuthenticatorVocabulary, or tightening the span "
            + "suppression until it swallows them, restores exactly the hole #364 F4 walked "
            + "through: an alias, and every dotted token in the vocabulary unseeable behind it.");

        // THE SUPPRESSED HALF — same names inside longer tokens, reported ONCE each.
        // `AUTH_BIOMETRIC_STRONG` must not also read as a bare `BIOMETRIC_STRONG`,
        // and `Authenticators.BIOMETRIC_STRONG` must not read as both.
        Assert.True(
            hits.Count(h => h.Line == 3) == 2
            && hits.Count(h => h.Line == 3 && h.Token == "AUTH_BIOMETRIC_STRONG") == 1
            && hits.Count(h => h.Line == 3 && h.Token == "AUTH_DEVICE_CREDENTIAL") == 1
            && hits.Count(h => h.Line == 4) == 1
            && hits.Count(h => h.Line == 4 && h.Token == "Authenticators.BIOMETRIC_STRONG") == 1
            && hits.Count(h => h.Line == 5) == 1
            && hits.Count(h => h.Line == 5 && h.Token == "AUTH_BIOMETRIC_WEAK") == 1
            && hits.Count(h => h.Line == 6) == 2
            && hits.Count(h => h.Line == 6 && h.Token == "Authenticators.BIOMETRIC_WEAK") == 1
            && hits.Count(h => h.Line == 6 && h.Token == "Authenticators.DEVICE_CREDENTIAL") == 1,
            "THE SPAN SUPPRESSION STOPPED SUPPRESSING. A bare name matching INSIDE a longer "
            + "vocabulary token is a FALSE ECHO of that token, not a second occurrence: "
            + "`KeyProperties.AUTH_BIOMETRIC_STRONG` is ONE hit and "
            + "`BiometricManager.Authenticators.BIOMETRIC_STRONG` is ONE hit. The matcher "
            + "reported: " + report + ".\n"
            + "  Double-reporting is not a harmless extra red. Every `count` in "
            + "src/auth-semantics.json and every declared site is sized against these numbers, so "
            + "the repair a reader reaches for is widening the manifest until the pin is quiet — "
            + "which raises the excuse ceiling on the very tokens this pin exists to count.");

        // THE IDENTIFIER-BOUNDARY HALF — a bare name inside a longer identifier the
        // VOCABULARY DOES NOT KNOW. This is a different rule from the span rule and
        // neither subsumes the other: `ERROR_NO_DEVICE_CREDENTIAL` is not any
        // vocabulary token, so no span contains it, and the bare `BIOMETRIC_STRONG`
        // inside `Authenticators.BIOMETRIC_STRONG` is preceded by a dot, so no
        // boundary rejects it. Delete either and the other does not cover for it.
        // THE APPLE FLAG, both members of the array seen, neither swallowing the
        // other. `.devicePasscode` is the vocabulary's newest entry and the only
        // reason it is not a silent addition.
        Assert.True(
            hits.Count(h => h.Line == 8) == 2
            && hits.Count(h => h.Line == 8 && h.Token == ".biometryCurrentSet") == 1
            && hits.Count(h => h.Line == 8 && h.Token == ".devicePasscode") == 1,
            "THE APPLE ACCESS-CONTROL FLAGS ARE NOT BOTH SEEN. "
            + "`[.biometryCurrentSet, .devicePasscode]` must report ONE of each: the declared "
            + "flag AND the passcode flag added beside it. The matcher reported: " + report + ".\n"
            + "  `.devicePasscode` is the weakening member of the same "
            + "SecAccessControlCreateFlags enum the other three Apple tokens come from, and it "
            + "was missing from AuthenticatorVocabulary — measured 978 of 978 GREEN with a "
            + "passcode-acceptable ACL live in BnSecureStorage. Losing it here reopens that.");

        Assert.True(hits.Count(h => h.Line == 7) == 0,
            "THE IDENTIFIER-BOUNDARY SUPPRESSION STOPPED SUPPRESSING. "
            + "`BiometricPrompt.ERROR_NO_DEVICE_CREDENTIAL` is an ERROR CODE and must produce NO "
            + "authenticator occurrence at all — the matcher reported: " + report + ".\n"
            + "  THIS IS NOT A TIDINESS ASSERTION. The first cut of this pin let that echo "
            + "through and excused it with a counted `ignored` entry, and a counted excuse is a "
            + "LICENCE: with the error arm deleted in the same commit, a `typealias` spent it on "
            + "a live `setAllowedAuthenticators(Auth.DEVICE_CREDENTIAL)` at 977 of 977 green. If "
            + "this reds, DO NOT re-add the ignore entry — fix the boundary rule, or you are "
            + "re-issuing the licence.");
    }

    /// <summary>THE LIVE NEGATIVE ANCHOR for the identifier-boundary rule (pin
    /// standard, Rule 3, second model: a known-MISMATCHING subject the detector must
    /// still reject).
    ///
    /// <para>The three bare names have no live positive fixed point and must not
    /// have one — a bare <c>DEVICE_CREDENTIAL</c> in shell source is either a
    /// declared site or the defect. What the tree DOES hold is a live near miss:
    /// <c>BiometricPrompt.ERROR_NO_DEVICE_CREDENTIAL</c>. This fact pins BOTH halves
    /// of that — the text is still there, and the scan still reports nothing for it
    /// — so the anchor cannot quietly evaporate the way a fixture-only control can.
    /// If the boundary rule regresses, <c>EveryAuthenticatorOccurrence_IsDeclaredOrIgnored</c>
    /// reds as well, which is the point: the near miss is exercised on every run by
    /// the production scan, not only here.</para>
    ///
    /// <para>WHY IT IS A FACT AND NOT A COMMENT: without the presence half, deleting
    /// the error arm would silently remove the anchor and leave the boundary rule
    /// unexercised against anything live.</para></summary>
    [Fact]
    public void TheLiveErrorCodeNearMiss_IsStillThere_AndStillNotAnAuthenticator()
    {
        const string NearMiss = "BiometricPrompt.ERROR_NO_DEVICE_CREDENTIAL";

        string code = CommentStrippedSource.Strip(File.ReadAllText(AuthBearingShellFile()));

        Assert.True(code.Contains(NearMiss, StringComparison.Ordinal),
            $"`{NearMiss}` is no longer live code in {AuthBearingShellFileRelative}. It is the one "
            + "thing in the tree that exercises the identifier-boundary rule against something "
            + "real: a bare DEVICE_CREDENTIAL sitting inside a longer identifier the vocabulary "
            + "does not know. If the error arm was legitimately removed, this anchor is gone and "
            + "the boundary rule is left with fixtures only — say so deliberately and re-point "
            + "this fact at another near miss, rather than deleting it.");

        var onTheNearMiss = AuthenticatorHits($"            {NearMiss} ->\n").ToList();

        Assert.True(onTheNearMiss.Count == 0,
            $"the matcher reported an authenticator occurrence for `{NearMiss}`: "
            + string.Join("; ", onTheNearMiss.Select(h => h.Token))
            + ". It is an ERROR CODE. The identifier-boundary rule in AuthenticatorHits exists to "
            + "reject exactly this, and the alternative it replaced — a counted `ignored` entry — "
            + "was a licence that a typealias spent on a live device-credential prompt at 977 of "
            + "977 green. Fix the rule; do not excuse the occurrence.");
    }

    // ── THE THIRD DETECTOR: APIS THAT WIDEN THE GATE WITHOUT NAMING ANYTHING ──

    /// <summary>PLATFORM CALLS THAT SELECT THE AUTHENTICATOR CLASS WITHOUT NAMING A
    /// CONSTANT. A vocabulary cannot see these and neither can the binding ban:
    /// there is no constant to spell and no namespace to rebind. They are ordinary
    /// method names, which is why they cost one alternation each.
    ///
    /// <para>Each arm below was MEASURED 978 of 978 GREEN before it existed, planted
    /// in both shell copies with the error arm left intact so no red could arrive
    /// from somewhere else:</para>
    /// <list type="bullet">
    /// <item><description><c>setDeviceCredentialAllowed(true)</c> —
    /// <c>BiometricPrompt.PromptInfo.Builder</c>, deprecated in androidx.biometric
    /// 1.1.0 and still present at <c>build.gradle.kts:94</c>. It is the API a real
    /// author reaches for FIRST, because it is what the older documentation
    /// shows.</description></item>
    /// <item><description><c>setUserAuthenticationValidityDurationSeconds(n)</c> with
    /// any <c>n</c> other than <c>-1</c> — <c>KeyGenParameterSpec.Builder</c>. A
    /// positive window means the key unlocks on ANY device-credential authentication
    /// inside it, which is device credential by another name. <c>-1</c> is the
    /// per-use biometric form and is deliberately allowed, so this arm cannot be
    /// satisfied by writing the safe call.</description></item>
    /// <item><description><c>createConfirmDeviceCredentialIntent</c> —
    /// <c>KeyguardManager</c>. It does not weaken the prompt; it REPLACES it with the
    /// lock-screen credential sheet, which is the same outcome by a route that never
    /// touches BiometricPrompt at all.</description></item>
    /// </list>
    ///
    /// <para>WHAT THIS IS NOT: a general ban on weakening. It is three named calls,
    /// and a fourth will not announce itself. The honest statement of the boundary is
    /// at <see cref="NoShellSource_WidensTheGateThroughAPlatformApi"/>.</para></summary>
    private const string GateWideningApi =
        @"(?:setDeviceCredentialAllowed\s*\(\s*true\s*\))"
        // ATOMIC `(?>\s*)`, NOT `\s*`, and the negative control is what found it. A
        // greedy `\s*` before the lookahead BACKTRACKS to zero width, and then the
        // lookahead tests `-` against the space it declined to consume, succeeds in
        // being "not -1", and the arm claims the SAFE call
        // `setUserAuthenticationValidityDurationSeconds( -1 )`. Measured, on the
        // first cut of this pattern, by the assertion twelve lines below. An atomic
        // group cannot give the space back, so the lookahead sees the argument.
        + @"|(?:setUserAuthenticationValidityDurationSeconds\s*\((?>\s*)(?!-(?>\s*)1(?>\s*)\))[^)]*\))"
        + @"|(?:createConfirmDeviceCredentialIntent)";

    /// <summary>NO PLATFORM CALL MAY WIDEN THE AUTHENTICATOR GATE WITHOUT NAMING AN
    /// AUTHENTICATOR. The third detector, and the one that answers "the residual is
    /// the analyzer's territory" — which was true of the raw integers and false as a
    /// general claim.
    ///
    /// <para>WHY IT IS A SEPARATE FACT rather than a fourth arm on the binding ban:
    /// the binding ban is about NAMES being rebound and its failure message tells the
    /// reader to spell the namespace out. That advice is wrong here. These calls are
    /// not a naming problem; the fix is a different API, and the message says
    /// so.</para>
    ///
    /// <para>WHAT IT DOES NOT COVER, with the direction (pin standard, Rule 5):</para>
    /// <list type="bullet">
    /// <item><description>THE RAW PLATFORM INTEGERS.
    /// <c>setAllowedAuthenticators(0x0000000F or 0x00008000)</c> names nothing at
    /// all. MEASURED 978 of 978 GREEN. This one genuinely needs type resolution over
    /// an Android classpath — a Lint check or a compiler plugin, not a scanner — and
    /// it is the ONLY residual in this pin of which that is true. FAILS
    /// GREEN.</description></item>
    /// <item><description>A FOURTH API NOBODY HAS NAMED. Three were found by reading
    /// the platform surface rather than by waiting for a review, and that is not a
    /// completeness argument. FAILS GREEN.</description></item>
    /// <item><description>The same suppression and matcher limits as its two sibling
    /// detectors: comment-stripped so prose cannot satisfy it, not
    /// string-literal-blind, and blinded by a content-keyed filter inside the shared
    /// matcher. See the limit list on
    /// <see cref="NoShellSource_AliasesAnAuthenticatorNamespace"/>.</description></item>
    /// <item><description>RESIDUAL 16 — A DOOR THE PARTITION DOES NOT SPELL.
    /// <see cref="AssertEveryBanIsWiredToTheDoorItsSubjectNeeds"/> classifies a fact by
    /// which of the TWO <c>ShellSourceScan</c> doors its body calls, and every one of
    /// its four assertions reasons from the two lists that produces. A THIRD door —
    /// say a <c>ForPatternLenient</c> beside the others, delegating to the per-line
    /// matcher — puts a ban in neither list, and nothing fires: <b>32 of 32 GREEN</b>,
    /// with a wrapped <c>setWeakUnlockAllowed(</c> … <c>true</c> … <c>)</c> live in
    /// BOTH copies of the shell. Two narrower shapes of the same class, each separately
    /// measured at <b>32 of 32 GREEN</b> with the same offender live: a ban written as
    /// a parameterised <c>[Theory]</c>, which the method scan's
    /// <c>public void X()</c> pattern never matches, and a ban delegating through a
    /// private helper in this file that calls the door on its behalf. FAILS GREEN, all
    /// three. NOT CLOSED, deliberately — a fifth mechanism is the move this pin has
    /// made four times, each time closing the route it was shown and each time leaving
    /// a sentence claiming the class. What is written instead is the residual and the
    /// measurement.</description></item>
    /// </list></summary>
    [Fact]
    public void NoShellSource_WidensTheGateThroughAPlatformApi()
    {
        ShellSourceScan.Hit[] calls = ShellSourceScan
            .ForPatternAcrossLines(nameof(AuthSemanticsDriftTests), null, ShellSource,
                                   GateWideningApi)
            .HitsCoveringEveryDeclaredRoot();

        Assert.True(calls.Length == 0,
            "A PLATFORM CALL THAT WIDENS THE AUTHENTICATOR GATE WITHOUT NAMING AN AUTHENTICATOR:\n"
            + string.Join("\n", calls.Select(h => $"  {h.File}:{h.Line} — {h.Token}"))
            + "\n\n`requireAuth: true` means BIOMETRY on both shells. Each of these accepts the "
            + "device credential instead, and none of them writes a token any vocabulary could "
            + "hold — which is why they are matched by NAME.\n"
            + "  setDeviceCredentialAllowed(true)  -> use setAllowedAuthenticators with "
            + "BiometricManager.Authenticators.BIOMETRIC_STRONG. It is deprecated precisely "
            + "because it conflated the two.\n"
            + "  setUserAuthenticationValidityDurationSeconds(n) -> -1 is the per-use biometric "
            + "form; any window accepts any device-credential unlock inside it. On API 30+ use "
            + "setUserAuthenticationParameters with KeyProperties.AUTH_BIOMETRIC_STRONG.\n"
            + "  createConfirmDeviceCredentialIntent -> this is the lock-screen sheet, not a "
            + "biometric prompt. There is no authenticator argument to fix; the call is the "
            + "defect.\n\n"
            + "THE FIX IS A DIFFERENT API, not a different spelling — do not reach for "
            + "AuthenticatorVocabulary or AuthenticatorNamespace, neither of which is about this. "
            + "If a call here is genuinely correct, it needs a site in src/auth-semantics.json and "
            + "a reason a reviewer can read, and this pin needs an exemption written as "
            + "deliberately as the ignore entries are.");
    }

    /// <summary>THE FACTS ALLOWED THROUGH THE PER-LINE DOOR, named. A fact that calls
    /// <c>ShellSourceScan.ForPattern</c> and is not named here REDS — measured — so a
    /// ban cannot reach the shipped tree through the per-line door unnamed.
    ///
    /// <para>READ THAT AS THE NARROW SENTENCE IT IS. An earlier version of this summary
    /// said "a fact in neither half is a RED", which claims a TOTALITY the four
    /// assertions below do not have: every one of them keys off a fact that is already
    /// in one of the two lists, so a ban reaching the tree through a door this partition
    /// does not SPELL is in neither list and is simply invisible. MEASURED at
    /// <b>32 of 32 GREEN</b> — see the fourth-door residual on
    /// <see cref="NoShellSource_WidensTheGateThroughAPlatformApi"/>. The sentence was
    /// falsified in one run, in the commit whose subject was "require a ban to have a
    /// wiring check", which is this phase's failure mode arriving one more
    /// time.</para></summary>
    private static readonly string[] PerLineByDesign = new[]
    {
        nameof(TheTestOnlyCredentialBranch_IsStillGuarded),
    };

    /// <summary>THE WIRING, WHICH A FIXTURE CONTROL CANNOT REACH — and this exists
    /// because a mutation showed the gap rather than because it was designed in.
    ///
    /// <para>Both ban controls drive <c>PatternHitsAcrossLines</c> DIRECTLY, because
    /// their subject tree is required to be empty and a fixture is the only anchor
    /// available. That proves the MATCHER matches across lines. It proves nothing
    /// about whether the BAN is wired to that matcher. MEASURED: re-pointing
    /// <c>ForPatternAcrossLines</c> back at the per-line <c>PatternHits</c> — one
    /// token — is <b>11 of 11 GREEN</b>, and with a wrapped
    /// <c>setDeviceCredentialAllowed(\n true \n)</c> live in the shipped shell it is
    /// <b>31 of 31 GREEN</b>. The controls notice nothing, because they never go
    /// through the door they are vouching for.</para>
    ///
    /// <para>That is the "guard watches producer A, assertion consumes producer B"
    /// shape this pin family spent three review rounds removing from the ROOT list,
    /// reappearing one level down in the MATCHER. So the wiring is asserted directly,
    /// as text, over this file's own source.</para>
    ///
    /// <para>WHAT A TEXT ASSERTION BUYS AND WHAT IT DOES NOT. It closes the cheap
    /// version — the one-token re-point measured above — and it is the same shape as
    /// <c>EveryConsumer_ReadsItsRootsFromTheRoster</c>, which this repo already
    /// accepts for exactly this job. It does NOT survive a determined rewiring: a new
    /// helper named <c>ForPatternAcrossLines</c> that delegates to the per-line
    /// matcher would satisfy it. FAILS GREEN for that shape, and it is listed as a
    /// residual rather than described as coverage.</para>
    ///
    /// <para>AND THE SUBJECT IS ENUMERATED, NOT PASSED IN. The first cut took the
    /// fact's name as an argument, so the check's subject was an unpinned parameter:
    /// nothing required a ban to HAVE a wiring check at all. MEASURED — a third ban
    /// added to this file through the per-line door, with no call naming it and a
    /// wrapped <c>setWeakUnlockAllowed(</c> … <c>true</c> … <c>)</c> live in both
    /// copies of the shell, was <b>43 of 43 GREEN</b>. That is an OMISSION mode: there
    /// is no artefact for a reviewer to look at, because the defect is the thing that
    /// was never written. So the facts are now read out of this file and PARTITIONED —
    /// across-lines, or named in <see cref="PerLineByDesign"/> — and a fact that calls
    /// the PER-LINE door without being named reds. It is the same move the roster made
    /// for trees, one level down.</para>
    ///
    /// <para>AND HERE IS WHAT THAT DOES NOT BUY, because the first draft of this
    /// paragraph claimed it did. It said "a ban in neither half reds". It does not: all
    /// four assertions below reason FROM the two lists, so a ban is only ever caught by
    /// walking through a door this partition spells. Add a fourth door and route a ban
    /// through it and the ban is in neither list and nothing fires —
    /// <b>32 of 32 GREEN</b>, with a live wrapped offender in both copies of the shell.
    /// The partition closes the two doors that exist; it does not close the one somebody
    /// adds. That is residual 16, written at the gate-widening fact, and it is disclosed
    /// rather than chased: a fifth mechanism is the move this pin has made four times
    /// already, and each one closed the route it was shown.</para>
    /// </summary>
    private static void AssertEveryBanIsWiredToTheDoorItsSubjectNeeds()
    {
        string source = CommentStrippedSource.Strip(File.ReadAllText(Path.Combine(
            BnRepo.Root(), "tests", "BlazorNative.Runtime.Tests", "AuthSemanticsDriftTests.cs")));

        var acrossLines = new List<string>();
        var perLine = new List<string>();

        foreach (Match m in Regex.Matches(source, @"\n    public void (\w+)\(\)"))
        {
            string name = m.Groups[1].Value;

            int end = source.IndexOf("\n    }", m.Index, StringComparison.Ordinal);
            Assert.True(end > m.Index,
                $"could not find the end of `{name}` — no closing brace at method indentation "
                + "after it. The file's formatting changed under this check, so it cannot read "
                + "that method's body. Re-point it deliberately rather than letting it SKIP a "
                + "fact — skipping is the shape that let a ban go unclassified in the first "
                + "place.");

            string body = source[m.Index..end];

            if (body.Contains(".ForPatternAcrossLines(", StringComparison.Ordinal))
            {
                acrossLines.Add(name);
            }

            if (body.Contains(".ForPattern(", StringComparison.Ordinal))
            {
                perLine.Add(name);
            }
        }

        Assert.True(acrossLines.Count >= 2,
            $"found only {acrossLines.Count} facts in this file scanning through "
            + "ShellSourceScan.ForPatternAcrossLines, and there are two bans that must. Either a "
            + "ban was deleted — then delete this floor in the same commit and say why — or the "
            + "method scan has stopped seeing its subject, in which case every remaining ban is "
            + "being approved without being read. This floor is the anti-vacuity half: a regex "
            + "that matches nothing classifies nothing, and classifying nothing passes.");

        string[] bothDoors = acrossLines.Intersect(perLine, StringComparer.Ordinal).ToArray();
        Assert.True(bothDoors.Length == 0,
            "FACTS CALLING BOTH ShellSourceScan DOORS:\n"
            + string.Join("\n", bothDoors.Select(n => "  " + n))
            + "\n\nOne subject, one door. A fact that calls both cannot be classified here, and "
            + "the per-line result is the one that silently misses a wrapped call.");

        string[] unclassified = perLine.Except(PerLineByDesign, StringComparer.Ordinal).ToArray();
        Assert.True(unclassified.Length == 0,
            "FACTS WALKING THROUGH THE PER-LINE ShellSourceScan.ForPattern WITHOUT SAYING SO:\n"
            + string.Join("\n", unclassified.Select(n => "  " + n))
            + "\n\nThe per-line door is correct for NSLogDriftTests, AndroidLogDriftTests and "
            + "this pin's credential-caller count, whose subjects are single-line DECLARATIONS "
            + "and whose messages print the line text. It is wrong for a ban on a CALL, because "
            + "a call wraps: the shell's own Kotlin opens a paren at end of line 96 times, and "
            + "`setDeviceCredentialAllowed(` with `true` on the next line was measured 31 of 31 "
            + "GREEN through it with the weakened prompt live.\n"
            + "  IF THE PER-LINE DOOR IS GENUINELY RIGHT for this fact, add it to "
            + "PerLineByDesign with a reason a reviewer can read. Do not widen this check.");

        string[] stale = PerLineByDesign.Except(perLine, StringComparer.Ordinal).ToArray();
        Assert.True(stale.Length == 0,
            "PerLineByDesign NAMES FACTS THAT NO LONGER CALL THE PER-LINE DOOR:\n"
            + string.Join("\n", stale.Select(n => "  " + n))
            + "\n\nEither the fact was renamed, or it was re-pointed at the across-lines door. "
            + "Either way the exemption now approves nothing, and an exemption nobody can see "
            + "expire is how the `ignored` licence in src/auth-semantics.json shipped a hole. "
            + "This assertion is also the scan's live anchor: if the method regex stops matching, "
            + "every name here goes stale at once and this reds.");

        // ── THE SECOND LINK, AND IT IS WHERE THE MEASUREMENT LANDED ──────────
        //
        // Naming the door is not enough, because the DOOR can be re-pointed. The
        // mutation that exposed this changed `ForPatternAcrossLines`'s own body to
        // delegate to the per-line `PatternHits` — one token, in the other file —
        // and the ban kept naming the right door while going through the wrong
        // matcher. 11 of 11 green, and 31 of 31 with a wrapped offender live.
        string scan = CommentStrippedSource.Strip(File.ReadAllText(Path.Combine(
            BnRepo.Root(), "tests", "BlazorNative.Runtime.Tests", "ShellSourceRootsDriftTests.cs")));

        int door = scan.IndexOf("internal static Result ForPatternAcrossLines(", StringComparison.Ordinal);
        Assert.True(door >= 0,
            "could not find `ForPatternAcrossLines` in ShellSourceRootsDriftTests.cs. The door "
            + "this pin's bans walk through was renamed or removed; re-point this check "
            + "deliberately rather than letting it vouch for a method it did not read.");

        int doorEnd = scan.IndexOf(";", door, StringComparison.Ordinal);
        Assert.True(doorEnd > door, "ForPatternAcrossLines' expression body has no terminator.");

        Assert.True(
            scan[door..doorEnd].Contains("PatternHitsAcrossLines", StringComparison.Ordinal),
            "ShellSourceScan.ForPatternAcrossLines no longer delegates to "
            + "PatternHitsAcrossLines.\n"
            + "  MEASURED: pointing it at the per-line `PatternHits` instead is 11 of 11 GREEN on "
            + "a clean tree and 31 of 31 GREEN with `setDeviceCredentialAllowed(` wrapped onto two "
            + "lines in the shipped shell. Both ban controls stayed green through it, because "
            + "they drive the matcher DIRECTLY and never walk through this door.\n"
            + "  THE CHAIN IS THREE LINKS AND EACH HAS ITS OWN INSTRUMENT: the ban names the door "
            + "(asserted above), the door names the matcher (asserted here), and the matcher's "
            + "behaviour across lines is held by the wrapped fixtures in this fact. A fourth "
            + "shape — a NEW helper called ForPatternAcrossLines that delegates to the per-line "
            + "matcher — satisfies both text assertions and is listed as a residual rather than "
            + "described as covered.");
    }

    /// <summary>THE POSITIVE CONTROL FOR <see cref="GateWideningApi"/> (pin standard,
    /// Rule 3). Subject empty by construction, so it is a fixture driven through the
    /// production matcher — the same shape as
    /// <see cref="TheAliasDetector_StillMatchesTheShapeItWasWrittenFor"/>, and for the
    /// same reason.
    ///
    /// <para>EVERY ARM INDIVIDUALLY, because a control built around one instance
    /// cannot tell you the others ever worked — that is not a hypothetical here, it
    /// is what the first cut of the binding ban did.</para>
    ///
    /// <para>AND A NEGATIVE PER ARM, because two of the three have a legitimate
    /// neighbour that must not be collateral: <c>setDeviceCredentialAllowed(false)</c>
    /// is the safe call, and
    /// <c>setUserAuthenticationValidityDurationSeconds(-1)</c> is the per-use
    /// biometric form the pre-API-30 path is entitled to use.</para></summary>
    [Fact]
    public void TheGateWideningDetector_StillMatchesTheShapesItWasWrittenFor()
    {
        AssertEveryBanIsWiredToTheDoorItsSubjectNeeds();

        foreach (string offender in new[]
                 {
                     "                .setDeviceCredentialAllowed(true)",
                     "            spec.setUserAuthenticationValidityDurationSeconds(30)",
                     "            spec.setUserAuthenticationValidityDurationSeconds(0)",
                     "            km.createConfirmDeviceCredentialIntent(null, null)",
                     // WRAPPED, because a call wraps and the detector used to match
                     // per line. Both of these were 31 of 31 GREEN with a
                     // device-credential-accepting prompt live in the shipped shell;
                     // the shell's own Kotlin opens a paren at end of line 96 times,
                     // so this is the ordinary formatting of the codebase rather
                     // than an evasion anybody had to invent.
                     "                .setDeviceCredentialAllowed(\n                    true\n                )",
                     "            spec.setUserAuthenticationValidityDurationSeconds(\n                30\n            )",
                 })
        {
            var hit = ShellSourceScan.PatternHitsAcrossLines(offender + "\n", GateWideningApi).ToList();
            Assert.True(hit.Count == 1,
                $"THE GATE-WIDENING DETECTOR NO LONGER MATCHES `{offender.Trim()}` — it reported "
                + (hit.Count == 0 ? "(nothing)" : string.Join("; ", hit.Select(h => h.Token)))
                + ", expected exactly one.\n"
                + "  Every arm of this pattern was measured 978 of 978 GREEN before it existed. "
                + "Losing one does not narrow this pin, it reopens a hole that was demonstrated "
                + "with a live weakened prompt.");
        }

        var safe = ShellSourceScan.PatternHitsAcrossLines(
            "                .setDeviceCredentialAllowed(false)\n"
            + "            spec.setUserAuthenticationValidityDurationSeconds(-1)\n"
            + "            spec.setUserAuthenticationValidityDurationSeconds( -1 )\n"
            + "            spec.setUserAuthenticationParameters(0, types)\n"
            + "            spec.setUserAuthenticationRequired(true)\n"
            // THE SAFE CALLS, WRAPPED. Removing the line boundary is what makes the
            // offenders above reachable, and the same change is what could have made
            // these collateral. The reviewed alternative — an arm matching a call
            // opened and not closed on the line — would have reddened BOTH of these,
            // which is a false red on the correct call and the thing that teaches a
            // reader the safe spelling and the unsafe one are the same.
            + "                .setDeviceCredentialAllowed(\n                    false\n                )\n"
            + "            spec.setUserAuthenticationValidityDurationSeconds(\n                -1\n            )\n",
            GateWideningApi).ToList();

        Assert.True(safe.Count == 0,
            "the gate-widening ban claimed a call it must not: "
            + string.Join("; ", safe.Select(h => $"line {h.Line}: {h.Token}"))
            + ". `setDeviceCredentialAllowed(false)` is the safe call and "
            + "`setUserAuthenticationValidityDurationSeconds(-1)` is the per-use biometric form "
            + "the pre-API-30 path is entitled to use. A ban that reds on the correct call is one "
            + "the next author deletes rather than obeys — and worse, it teaches that the safe "
            + "spelling and the unsafe one are the same thing.");
    }

    /// <summary>THE EXTENSION LIST ANSWERS TO THE BUILD, the way the roster already
    /// makes ROOT lists answer to it. This is the repair for #364's fourth route: it
    /// is what makes <c>ShellSource</c> stop being a free literal, and it reaches
    /// exactly the spelling <c>&lt;lang&gt;.srcDirs(…)</c> — no further. The residual
    /// list below is not a formality; two other spellings of the same Gradle
    /// declaration are measured green in it.
    ///
    /// <para>⚠ THE SENTENCE THAT STOOD HERE — <i>"and this closes the class"</i> — IS
    /// DELETED. It was a new universal, written six hundred lines below this file's
    /// own instruction not to write one, and review measured it false three ways
    /// before the commit was a day old. That is the phase's signature defect
    /// committed by the person warning about it, so the warning is repeated here
    /// where the next author will be standing: <b>state the spelling the mechanism
    /// reaches, and put everything else in the residual list.</b></para>
    ///
    /// <para>THE DEFECT IT ANSWERS. <c>build.gradle.kts</c> declares the Gradle `main`
    /// source set with BOTH <c>java.srcDirs(…)</c> and <c>kotlin.srcDirs(…)</c> over
    /// the same two directories, so Java compiles into the shipped AAR from a root the
    /// roster declares. The scan's extension list said <c>[".swift", ".kt"]</c>. An
    /// <c>AuthFlags.java</c> holding <c>Authenticators.DEVICE_CREDENTIAL</c>, used from
    /// the shell as <c>setAllowedAuthenticators(io.blazornative.jni.AuthFlags.DC)</c>,
    /// was <b>978 of 978 GREEN</b>.</para>
    ///
    /// <para>AND THE COVERAGE GUARD COULD NOT CATCH IT, which is why a one-word fix is
    /// not enough on its own. <c>ShellSourceScan</c> recomputes its per-root candidate
    /// set from the SAME extension list the walk used, so narrowing that list narrows
    /// the walk and the proof of the walk together. That is precisely the shape three
    /// review rounds removed from the ROOT list — a guard and its subject derived from
    /// one source move together — and nobody had removed it from the EXTENSION list.
    /// This fact is the independent source: the build file, which is not a test and
    /// does not move when a pin is narrowed.</para>
    ///
    /// <para>IT IS OPEN-WORLD ON PURPOSE. A <c>&lt;lang&gt;.srcDirs</c> this test does
    /// not recognise is a RED, not a shrug — an unclassified entry is exactly how
    /// <c>java</c> stayed invisible. Classify it into one of the two lists below
    /// deliberately.</para>
    ///
    /// <para>WHAT IT DOES NOT COVER, each measured, each FAILS GREEN (pin standard,
    /// Rule 5):</para>
    /// <list type="bullet">
    /// <item><description><c>groovy.srcDir("…")</c> — Gradle's <b>singular</b>
    /// <c>srcDir</c>, which is a real API and not a typo. <b>31 of 31 GREEN.</b> The
    /// dotted plural is the control and reds.</description></item>
    /// <item><description><c>groovy { srcDirs("…") }</c> — the configuration-block
    /// form, where the language name is not a receiver at all. <b>31 of 31
    /// GREEN.</b></description></item>
    /// <item><description>NOT CLOSED BY WIDENING, DELIBERATELY. <c>srcDirs?</c> plus a
    /// block-form scan would catch these two and the third spelling would arrive with
    /// the next Gradle release. Four rounds of this pin have been spent chasing
    /// spellings; the cost of the gap is that a NEW LANGUAGE declared in an unusual
    /// form goes unscanned, and the cost of chasing is a reader who believes the
    /// list is complete. Both are disclosed here so the choice is visible.</description></item>
    /// <item><description>THE APPLE SIDE HAS NO DECLARATION TO READ. <c>project.yml</c>
    /// lists target PATHS, not languages, so an Objective-C or C file inside
    /// <c>BnHost</c> would compile and go unscanned and nothing here would say so. The
    /// Android half is the half where the build states its languages, so it is the
    /// half that can be pinned at all.</description></item>
    /// </list></summary>
    [Fact]
    public void TheScannedExtensions_CoverEveryLanguageTheBuildCompiles()
    {
        // Gradle source-set members that DECLARE COMPILED SOURCE, with the extension
        // each one contributes. If a language arrives here, it also arrives in the
        // AAR, which is what makes this list the requirement rather than a preference.
        var compiled = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["java"] = ".java",
            ["kotlin"] = ".kt",
        };

        // Members that are NOT compiled source. Resources and manifests ship, but no
        // authenticator token in them reaches a gate, and a scanner over XML would
        // red on the prose in a string resource.
        string[] notSource =
            ["res", "assets", "jniLibs", "aidl", "renderscript", "shaders", "mlModels",
             "baselineProfiles", "resources", "manifest"];

        string gradle = Path.Combine(BnRepo.Root(),
            "src", "BlazorNative.Jni", "build.gradle.kts");
        Assert.True(File.Exists(gradle),
            $"{gradle} does not exist — it is the only place that says which LANGUAGES the "
            + "Android shell compiles, and without it the scan's extension list is a free "
            + "literal again. Re-point this fact deliberately rather than deleting it.");

        string code = CommentStrippedSource.Strip(File.ReadAllText(gradle));

        // The `main` source set only: androidTest and test are roster-EXCLUDED for
        // this consumer, so what they compile is not this pin's subject.
        int at = code.IndexOf("getByName(\"main\")", StringComparison.Ordinal);
        Assert.True(at >= 0,
            "could not find `getByName(\"main\")` in src/BlazorNative.Jni/build.gradle.kts "
            + "(pattern: getByName(\"main\")). The source-set block moved or was rewritten, so "
            + "this fact can no longer tell which languages the shipped AAR compiles — which "
            + "is the whole of what it checks. Re-point it deliberately.");

        int end = code.IndexOf("getByName(\"androidTest\")", at, StringComparison.Ordinal);
        string main = end > at ? code[at..end] : code[at..];

        var declared = Regex.Matches(main, @"(?<member>\w+)\s*\.\s*srcDirs\s*\(")
            .Select(m => m.Groups["member"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        // ANTI-VACUITY, AS AN IDENTITY RATHER THAN A COUNT — and that distinction was
        // measured, not preferred. The floor used to read `declared.Count >= 2` while
        // its own message named java and kotlin, and `kotlin`, `res`, `assets` and
        // `jniLibs` satisfy a count of two on their own: DELETING
        // `java.srcDirs(...)` FROM `main` OUTRIGHT WAS 11 OF 11 GREEN. A message that
        // names two members while the assertion counts any four is a message
        // describing a file rather than an assertion.
        string[] required = ["java", "kotlin"];
        var absent = required.Where(r => !declared.Contains(r, StringComparer.Ordinal)).ToList();

        Assert.True(absent.Count == 0,
            "src/BlazorNative.Jni/build.gradle.kts no longer declares "
            + string.Join(" or ", absent) + " in the `main` source set — parsed ["
            + string.Join(", ", declared) + "].\n"
            + "  BOTH are load-bearing and neither is bookkeeping. `kotlin.srcDirs` is the line "
            + "whose ABSENCE once stopped the entire Android shell from being compiled at the "
            + "AGP 9 migration, unnoticed on main — the build file's own comment tells that "
            + "story. `java.srcDirs` is what makes .java a compiled language in a declared root, "
            + "which is #364's fourth route.\n"
            + "  If a language genuinely stopped being compiled here, remove its extension from "
            + "ShellSource and its entry from the map above in the same edit, deliberately. Do "
            + "not weaken this assertion back into a count: a count is what let the java "
            + "declaration be deleted with every test green.");

        var unclassified = declared
            .Where(d => !compiled.ContainsKey(d) && !notSource.Contains(d, StringComparer.Ordinal))
            .ToList();

        Assert.True(unclassified.Count == 0,
            "src/BlazorNative.Jni/build.gradle.kts declares a source-set member this pin does not "
            + "recognise: " + string.Join(", ", unclassified) + ".\n"
            + "  An UNCLASSIFIED member is how `.java` stayed invisible: the build compiled it "
            + "into the AAR and the auth scan's extension list did not mention it, so a "
            + "DEVICE_CREDENTIAL constant in a declared root was read by nobody. Decide which it "
            + "is — compiled source, which means adding its extension to ShellSource and to this "
            + "map, or not source, which means adding it to notSource with that decision on "
            + "record. Do not delete this assertion to make it quiet.");

        var missing = declared
            .Where(d => compiled.ContainsKey(d))
            .Select(d => compiled[d])
            .Where(ext => !ShellSource.Contains(ext, StringComparer.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "THE BUILD COMPILES A LANGUAGE THE AUTH SCAN DOES NOT READ: "
            + string.Join(", ", missing) + ".\n"
            + $"  build.gradle.kts declares [{string.Join(", ", declared)}] over the `main` source "
            + $"set; ShellSource is [{string.Join(", ", ShellSource)}].\n"
            + "  This is #364's fourth route and it was measured: a .java file in a declared root "
            + "holding Authenticators.DEVICE_CREDENTIAL, used from the shell, was 978 of 978 "
            + "GREEN — invisible to the walk AND to the per-root coverage guard, which recomputes "
            + "its candidates from this same list. Add the extension to ShellSource. Narrowing "
            + "this map instead makes the pin agree with itself about a tree it no longer reads, "
            + "which is the #364 F1 shape one level down.");

        // ── THE ROSTER'S OWN EXTENSION LIST, WHICH IS THE SECOND COPY ────────
        //
        // `ShellSource` is not the only unpinned extension literal. Each
        // `scanRoots[]` entry in src/shell-source-roots.json carries its own, and
        // that list is what EveryShellSourceFile_IsInsideADeclaredRoot uses to ask
        // the OTHER question — is there a source file here that no declared root
        // contains? MEASURED: with a compiled `AuthFlags2.java` under AGP's default
        // `src/main/java` — a directory no `sets` entry declares — that fact reds and
        // names the file; narrow the two Android scanRoots back to `[".kt"]` and the
        // same tree is 31 of 31 GREEN. Two literals, one truth, and only one of them
        // was answering to the build.
        //
        // KEYED ON `.kt` RATHER THAN ON A NEW ROSTER FIELD, on purpose. A container
        // that scans Kotlin is an Android source container, and the Gradle source set
        // that compiles its Kotlin compiles its Java from the same directories. That
        // keeps the roster unchanged — it is a manifest of trees, not of languages,
        // and giving it a language field to satisfy this fact would be redesigning
        // the roster to please a test.
        var thin = ShellSourceRoots.ScanRoots()
            .Where(r => r.Extensions.Contains(".kt", StringComparer.OrdinalIgnoreCase))
            .Select(r => (r.Path, Missing: declared
                .Where(d => compiled.ContainsKey(d))
                .Select(d => compiled[d])
                .Where(ext => !r.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                .ToList()))
            .Where(x => x.Missing.Count > 0)
            .ToList();

        Assert.True(thin.Count == 0,
            "A scanRoots ENTRY IN " + ShellSourceRoots.ManifestPath + " DOES NOT LIST EVERY "
            + "LANGUAGE THE BUILD COMPILES:\n"
            + string.Join("\n", thin.Select(x => $"  {x.Path} — missing {string.Join(", ", x.Missing)}"))
            + "\n\nThese containers are how EveryShellSourceFile_IsInsideADeclaredRoot finds a "
            + "source file that no declared root contains — the AGP-default `src/main/java` case, "
            + "which is compiled and which no `sets` entry names. An extension missing here makes "
            + "that whole question unaskable for that language, silently: measured, a live "
            + "Authenticators.DEVICE_CREDENTIAL in such a file is 31 of 31 GREEN with these lists "
            + "narrowed to `.kt`.\n"
            + "  Add the extension to the scanRoots entry. If a language genuinely stopped being "
            + "compiled, take it out of the map above in the same edit — there is one truth here "
            + "and three places that have to agree with it.");
    }

    /// <summary>The Android shell file the auth surface lives in, repo-relative.
    /// Named once so a message and the reader are looking at one string.</summary>
    private const string AuthBearingShellFileRelative =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt";

    /// <summary>Its absolute path, with existence asserted — a control that cannot
    /// find its fixture source must red, never splice into nothing.</summary>
    private static string AuthBearingShellFile()
    {
        string path = Path.Combine(
            BnRepo.Root(), AuthBearingShellFileRelative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path),
            $"{AuthBearingShellFileRelative} does not exist — it is the file every Android site in "
            + "src/auth-semantics.json names and the one this control splices its fixture into. A "
            + "pin that cannot find its subject must fail loudly, never vacuously.");
        return path;
    }
}
