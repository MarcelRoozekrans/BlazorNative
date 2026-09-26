using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// #339 — THE SEMANTIC TWIN PIN.
//
// M13 set the criterion that a NEW twin divergence must red, and named two
// mechanisms: "a generated twin, OR a pin that compares the two copies". Only
// the generator was ever built. #339 is the proof the second clause mattered:
// dispatchHostEvent existed in both shells, spelled identically, and blocked in
// one. A name generator cannot see that -- the names agree and the BEHAVIOUR
// does not.
//
// WHAT THIS PIN IS: a STRUCTURAL check. It reads src/dispatch-surface.json and
// asserts each shell declares the methods it should, and that each method's
// LANE CALL matches its declared semantics -- Kotlin `execute` vs `submit`/
// `.get()`, Swift `.async` vs `.sync`.
//
// WHAT THIS PIN IS NOT: proof of runtime behaviour. It never observes blocking.
// A behavioural cross-shell pin would need a JVM test and an XCTest agreeing
// with each other, which is an unpinned twin in its own right. Do not describe
// this test as proving the shells behave identically; it proves they SAY the
// same thing, which is the strongest claim a single-host test can make.
//
// POSITIVE EVIDENCE ONLY (the reason this file's semantics check does not
// match brief text verbatim): Kotlin's `dispatchHostEvent` is an expression
// body that DELEGATES to `dispatchHostEventUnchecked` -- the actual
// `dispatchLane.execute` call lives one hop away, in the callee. A check that
// treats "the blocking pattern is absent from THIS method's body" as proof of
// fire-and-forget passes on a delegating method by accident, whether or not
// the callee blocks. So this pin requires POSITIVE evidence of ONE lane call
// shape (Kotlin `dispatchLane.execute` / Swift `dispatchLane.async` for
// fire-and-forget; Kotlin `dispatchLane.submit`/`.get()` / Swift
// `dispatchLane.sync` for blocking) in the declared method's own body, and
// when a body makes no lane call at all, follows exactly ONE hop of same-file
// delegation to the method it calls before giving up. A method this pin
// cannot classify -- no lane call, no resolvable one-hop delegate, or a
// delegate whose body ALSO makes no lane call -- reds loudly naming the
// method, rather than passing vacuously. That is the exact failure mode #339
// exposed in the first place: a pin that goes quiet on what it cannot see.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DispatchSurfaceDriftTests
{
    private sealed record Method(string Name, string Semantics, string[]? Platforms, string? Reason);
    private sealed record IgnoredMethod(string Name, string[]? Platforms, string? Reason);

    private static Method[] Surface()
    {
        string json = File.ReadAllText(Path.Combine(BnRepo.Root(), "src", "dispatch-surface.json"));
        using JsonDocument doc = JsonDocument.Parse(json,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var methods = new List<Method>();
        foreach (JsonElement m in doc.RootElement.GetProperty("methods").EnumerateArray())
        {
            string[]? platforms = m.TryGetProperty("platforms", out JsonElement p)
                ? [.. p.EnumerateArray().Select(x => x.GetString()!)]
                : null;
            methods.Add(new Method(
                m.GetProperty("name").GetString()!,
                m.GetProperty("semantics").GetString()!,
                platforms,
                m.TryGetProperty("reason", out JsonElement r) ? r.GetString() : null));
        }

        Assert.True(methods.Count >= 4,
            $"only {methods.Count} methods declared — the manifest lost entries, or this loop "
            + "stopped seeing them");
        return [.. methods];
    }

    /// <summary>The manifest's OTHER half (fix round 2, Important #2): every method
    /// name here is a <c>dispatch*</c>-prefixed declaration that exists in one or
    /// both runtime sources but is deliberately NOT part of the guarded dispatch
    /// surface — a private helper or a test seam. Optional; an absent <c>ignored</c>
    /// key reads as an empty list rather than failing the manifest parse, so a
    /// manifest predating this guard still loads (and then reds honestly, naming
    /// every unmentioned method, the first time this test runs against it).</summary>
    private static IgnoredMethod[] Ignored()
    {
        string json = File.ReadAllText(Path.Combine(BnRepo.Root(), "src", "dispatch-surface.json"));
        using JsonDocument doc = JsonDocument.Parse(json,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        if (!doc.RootElement.TryGetProperty("ignored", out JsonElement arr))
            return [];

        var ignored = new List<IgnoredMethod>();
        foreach (JsonElement m in arr.EnumerateArray())
        {
            string[]? platforms = m.TryGetProperty("platforms", out JsonElement p)
                ? [.. p.EnumerateArray().Select(x => x.GetString()!)]
                : null;
            ignored.Add(new IgnoredMethod(
                m.GetProperty("name").GetString()!,
                platforms,
                m.TryGetProperty("reason", out JsonElement r) ? r.GetString() : null));
        }
        return [.. ignored];
    }

    /// <summary>Every <c>dispatch*</c>-named function/fun DECLARATION in
    /// <paramref name="source"/> (a comment-stripped runtime source), by distinct
    /// name — a method declared twice (an overload) counts once. This is a raw
    /// scan for the NAME only; it does not classify the lane call the way
    /// <see cref="ClassifyLane"/> does; that is <see cref="EveryMethodsLaneCall_MatchesItsDeclaredSemantics"/>'s
    /// job for methods the manifest already knows about. This one exists to find
    /// what the manifest does NOT yet know about.</summary>
    private static readonly Regex DispatchNamedDeclaration = new(@"\b(?:fun|func)\s+(dispatch\w*)\s*\(");

    private static IEnumerable<string> DispatchNamedDeclarations(string source) =>
        DispatchNamedDeclaration.Matches(source)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal);

    /// <summary>Strips comments via the shared <see cref="CommentStrippedSource"/> (fix
    /// round 1, Important #1: this method used to carry its own copy of the stripper,
    /// which dropped the same-line-block-comment check the shared one has — a divergent
    /// copy of one truth sitting inside the twin-divergence pin itself, and the same reason
    /// <c>GeneratedSymbolShadowTests.CodeLines</c> uses it too), then joins the non-blank
    /// lines so a KDoc mention of a method name never counts as a declaration.</summary>
    private static string CodeText(string file) =>
        string.Join("\n", CommentStrippedSource.Lines(file).Where(l => l.Trim().Length > 0));

    private static string KotlinRuntime() => CodeText(Path.Combine(BnRepo.Root(),
        "src", "BlazorNative.Jni", "src", "main", "kotlin", "io", "blazornative", "jni",
        "BlazorNativeRuntime.kt"));

    private static string SwiftRuntime() => CodeText(Path.Combine(BnRepo.Root(),
        "src", "BlazorNative.Apple", "BnHost", "BnRuntime.swift"));

    /// <summary>The body from a method's declaration to the next declaration — enough
    /// to see which lane call it makes, without parsing braces.</summary>
    private static readonly Regex NextDeclaration =
        new(@"\n\s*(?:internal\s+|private\s+)?(?:fun|func)\s+\w+");

    private static string BodyAfter(string source, Match declMatch)
    {
        string rest = source[(declMatch.Index + declMatch.Length)..];
        Match next = NextDeclaration.Match(rest);
        return next.Success ? rest[..next.Index] : rest;
    }

    /// <summary>A call-shaped token — identifier immediately followed by '(' — used only
    /// to find what a delegating one-liner calls. Control-flow keywords and the lane
    /// field itself are excluded so they are never mistaken for a delegate target.</summary>
    private static readonly Regex CallLike = new(@"\b([A-Za-z_]\w*)\s*\(");

    private static readonly HashSet<string> NotADelegate = new(StringComparer.Ordinal)
    {
        "if", "when", "try", "catch", "for", "while", "guard", "switch", "return", "dispatchLane",
    };

    private static string? FindDelegateCall(string body)
    {
        foreach (Match m in CallLike.Matches(body))
        {
            string id = m.Groups[1].Value;
            if (!NotADelegate.Contains(id))
                return id;
        }
        return null;
    }

    /// <summary>Finds a declaration of <paramref name="name"/> other than the one at
    /// <paramref name="excludeIndex"/> — used to resolve a same-named-overload delegate
    /// (Kotlin's two-arg <c>dispatchEvent</c> calls the four-arg <c>dispatchEvent</c>)
    /// without matching the delegating declaration itself.
    ///
    /// <para>UNASSERTED PRECONDITION, NOW ASSERTED (fix round 1, Important #2): this picks
    /// "the other declaration" by ELIMINATION — every same-named match that is not the
    /// original — which is unambiguous only when exactly ONE such match remains. That holds
    /// today only because every delegate this resolver has ever seen has exactly two
    /// same-file declarations (the caller and its single overload/callee), not because the
    /// resolver checks it. A third same-named overload would make elimination pick between
    /// two candidates silently — the "safety claim with no mechanism" shape this repo treats
    /// as its most dangerous bug class. So this asserts the count instead of assuming it: more
    /// than one remaining candidate reds naming the assumption, rather than resolving to
    /// whichever the regex happened to find first.</para></summary>
    private static Match FindOtherDeclaration(string source, string funcKeyword, string name, int excludeIndex)
    {
        var regex = new Regex($@"\b{funcKeyword}\s+{Regex.Escape(name)}\s*\(");
        List<Match> candidates = [.. regex.Matches(source).Where(m => m.Index != excludeIndex)];

        Assert.True(candidates.Count <= 1,
            $"resolving the delegate '{name}' found {candidates.Count} same-file declarations "
            + "other than the one delegating to it. This resolver picks \"the other declaration\" "
            + "by elimination, which is unambiguous only when exactly one candidate remains — a "
            + $"third same-named overload of '{name}' makes elimination meaningless (it could "
            + "silently resolve to the wrong twin). Extend the resolver deliberately (arity or "
            + "signature matching) instead of trusting the count.");

        return candidates.Count == 1 ? candidates[0] : Match.Empty;
    }

    private static (bool Blocks, bool Forgets) LaneEvidence(string body, bool kotlin)
    {
        bool blocks = kotlin
            ? body.Contains("dispatchLane.submit", StringComparison.Ordinal)
              || body.Contains(".get()", StringComparison.Ordinal)
            : body.Contains("dispatchLane.sync", StringComparison.Ordinal);
        bool forgets = kotlin
            ? body.Contains("dispatchLane.execute", StringComparison.Ordinal)
            : body.Contains("dispatchLane.async", StringComparison.Ordinal);
        return (blocks, forgets);
    }

    /// <summary>Classifies <paramref name="name"/>'s lane call by POSITIVE evidence only.
    /// If the method's own body makes no recognizable lane call, follows exactly ONE hop
    /// of same-file delegation to the method it calls. Fails loudly — never silently
    /// passes — when it cannot resolve a lane call within that one-hop budget.</summary>
    private static (bool Blocks, bool Forgets) ClassifyLane(string source, string shellLabel, string name)
    {
        string funcKeyword = shellLabel == "Kotlin" ? "fun" : "func";
        bool kotlin = shellLabel == "Kotlin";

        var declRegex = new Regex($@"\b{funcKeyword}\s+{Regex.Escape(name)}\s*\(");
        Match decl = declRegex.Match(source);
        Assert.True(decl.Success, $"no declaration of '{name}' found in the {shellLabel} source");

        string body = BodyAfter(source, decl);
        (bool blocks, bool forgets) = LaneEvidence(body, kotlin);
        if (blocks || forgets)
            return (blocks, forgets);

        // No direct lane call — this is exactly the dispatchHostEvent shape (an
        // expression-bodied delegate). Follow ONE hop, no more.
        string? callee = FindDelegateCall(body);
        Assert.True(callee is not null,
            $"{shellLabel} '{name}' makes no lane call and this pin can find nothing it "
            + "delegates to — it cannot classify this method, therefore cannot guard it.");

        Match calleeDecl = FindOtherDeclaration(source, funcKeyword, callee!, decl.Index);
        Assert.True(calleeDecl.Success,
            $"{shellLabel} '{name}' appears to delegate to '{callee}', but no other same-file "
            + $"declaration of '{callee}' was found (one-hop cap reached) — this pin cannot "
            + "classify it, therefore cannot guard it.");

        string calleeBody = BodyAfter(source, calleeDecl);
        (bool hopBlocks, bool hopForgets) = LaneEvidence(calleeBody, kotlin);
        Assert.True(hopBlocks || hopForgets,
            $"{shellLabel} '{name}' delegates to '{callee}', whose body ALSO makes no "
            + "recognizable lane call (one-hop cap reached) — this pin cannot classify it, "
            + "therefore cannot guard it.");
        return (hopBlocks, hopForgets);
    }

    [Fact]
    public void EveryDeclaredMethod_ExistsInEveryShellItClaims()
    {
        string kotlin = KotlinRuntime();
        string swift = SwiftRuntime();

        foreach (Method m in Surface())
        {
            bool wantKotlin = m.Platforms is null || m.Platforms.Contains("kotlin");
            bool wantSwift = m.Platforms is null || m.Platforms.Contains("swift");

            if (m.Platforms is not null)
                Assert.False(string.IsNullOrWhiteSpace(m.Reason),
                    $"'{m.Name}' restricts platforms without a written reason. Asymmetry is "
                    + "allowed; silence is not — state why one shell has no twin.");

            Assert.Equal(wantKotlin, Regex.IsMatch(kotlin, $@"\bfun\s+{Regex.Escape(m.Name)}\s*\("));
            Assert.Equal(wantSwift, Regex.IsMatch(swift, $@"\bfunc\s+{Regex.Escape(m.Name)}\s*\("));
        }
    }

    [Fact]
    public void EveryMethodsLaneCall_MatchesItsDeclaredSemantics()
    {
        string kotlin = KotlinRuntime();
        string swift = SwiftRuntime();

        foreach (Method m in Surface())
        {
            bool blocking = m.Semantics == "blocking";
            Assert.True(m.Semantics is "blocking" or "fire-and-forget",
                $"'{m.Name}' declares semantics '{m.Semantics}' — expected 'blocking' or "
                + "'fire-and-forget'. An unknown value leaves this pin with nothing to assert.");

            if (m.Platforms is null || m.Platforms.Contains("kotlin"))
            {
                (bool laneBlocks, bool laneForgets) = ClassifyLane(kotlin, "Kotlin", m.Name);
                Assert.True(laneBlocks ^ laneForgets,
                    $"Kotlin '{m.Name}' resolves to a body claiming BOTH a blocking and a "
                    + "fire-and-forget lane call — this pin cannot classify it, therefore "
                    + "cannot guard it.");
                Assert.True(blocking == laneBlocks,
                    $"Kotlin '{m.Name}' is declared {m.Semantics} but its lane call says "
                    + "otherwise. Declared blocking means dispatchLane.submit(...).get(); "
                    + "declared fire-and-forget means dispatchLane.execute { }.");
            }

            if (m.Platforms is null || m.Platforms.Contains("swift"))
            {
                (bool laneBlocks, bool laneForgets) = ClassifyLane(swift, "Swift", m.Name);
                Assert.True(laneBlocks ^ laneForgets,
                    $"Swift '{m.Name}' resolves to a body claiming BOTH dispatchLane.sync and "
                    + "dispatchLane.async — this pin cannot classify it, therefore cannot "
                    + "guard it.");
                Assert.True(blocking == laneBlocks,
                    $"Swift '{m.Name}' is declared {m.Semantics} but uses "
                    + $"dispatchLane.{(laneBlocks ? "sync" : "async")}. This is #339's exact "
                    + "shape: a method whose name matches its twin and whose behaviour does not.");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix round 2 (final whole-branch review), Important #2 — THE COMPLETENESS
    // GUARD. Both facts above only ever look at methods THIS MANIFEST ALREADY
    // NAMES. A new dispatch method added to one shell and never declared here is
    // simply never looked at — the most plausible route to a false green, and
    // less than what this phase's own header claims the pin does. This fact
    // closes that gap: it scans both runtime sources directly for `dispatch*`
    // declarations and asserts every one is accounted for, either in `methods`
    // (guarded) or in `ignored` (deliberately exempt, with a written reason).
    //
    // What it does NOT do: classify lane-call semantics for ignored methods (they
    // are, by definition, not part of the guarded surface) or catch a SECOND
    // overload of an already-declared name diverging from the first (see the
    // manifest's own `$doc` note — `ClassifyLane`/`FindOtherDeclaration` share
    // that limit).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>MEASURED, NOT GUESSED (pin standard Rule 2's corollary). The Kotlin
    /// runtime declares EIGHT distinct <c>dispatch*</c> names today — the four the
    /// manifest guards minus Swift-only asymmetry (<c>dispatchEvent</c>,
    /// <c>dispatchEventAndWait</c>, <c>dispatchHostEvent</c>,
    /// <c>dispatchHostEventAndWait</c>) plus the four it ignores (<c>dispatchCore</c>,
    /// <c>dispatchEventBlocking</c>, <c>dispatchHostEventUnchecked</c>,
    /// <c>dispatchHostEventBlocking</c>). The floor is SIX: two names of headroom, which
    /// is enough for the two Kotlin-only test seams to be retired without an argument
    /// about this number, and not enough for the scan to lose the guarded surface
    /// silently.</summary>
    private const int MinimumKotlinDispatchDeclarations = 6;

    /// <summary>FIVE distinct <c>dispatch*</c> names in the Swift runtime today —
    /// <c>dispatchEvent</c>, <c>dispatchEventBlocking</c>, <c>dispatchCore</c>,
    /// <c>dispatchHostEvent</c>, <c>dispatchHostEventAndWait</c>. The floor is FOUR: one
    /// name of headroom, because Swift carries one test seam rather than Kotlin's three
    /// and a smaller set has less slack to give away. Retiring a second is a deliberate
    /// act; re-point this with it.</summary>
    private const int MinimumSwiftDispatchDeclarations = 4;

    /// <summary>THE FLOOR ON THE SCANNED SET — issue #357, opened in 14.1 and closed on
    /// 2026-09-25 with this fact as its fix, and the last instance of census §4.2's shape in
    /// the pin population.
    ///
    /// <para><see cref="EveryDispatchNamedDeclaration_IsDeclaredOrIgnored"/> iterates
    /// <see cref="DispatchNamedDeclarations"/>, and the only anti-vacuity assertion it
    /// executed was <see cref="Surface()"/>'s <c>methods.Count &gt;= 4</c>. That floors
    /// the MANIFEST — the set the completeness check compares AGAINST — and leaves the
    /// SCANNED set, the one it walks, unfloored. Reword
    /// <see cref="DispatchNamedDeclaration"/> past its subject (a reformat putting a
    /// newline between the name and its paren would do it) and the <c>foreach</c> runs
    /// zero times with the manifest floor perfectly satisfied: a completeness guard,
    /// green over nothing. That is the failure mode this file's own header names — <i>a
    /// pin that goes quiet on what it cannot see</i> — sitting inside the pin written to
    /// close it.</para>
    ///
    /// <para>WHAT IT DOES NOT DO, stated rather than implied: the floor lives in THIS
    /// fact, not inside the completeness fact, which is
    /// <c>AuthSemanticsDriftTests.TheCompletenessScan_IsNotVacuous</c>' shape. So a
    /// broken scan still leaves <see cref="EveryDispatchNamedDeclaration_IsDeclaredOrIgnored"/>
    /// itself passing — the SUITE reds, that fact does not. The contrast is the evidence
    /// and it is deliberate: the completeness check cannot floor its own walk without
    /// asserting the thing it is trying to discover, so a sibling fact says what the walk
    /// must have seen and the two are read together.</para>
    ///
    /// <para>It floors the two shells SEPARATELY rather than summing them. A combined
    /// floor is satisfiable by one healthy shell: Kotlin's eight names alone would clear
    /// any total low enough for Swift's five to matter, so an emptied Swift scan would
    /// pass. Per shell, an emptied scan reds naming the shell.</para>
    ///
    /// <para>THE COUNT FLOOR ALONE WAS NOT ENOUGH, and the 15.8 re-audit review proved it.
    /// Its headroom, two names in Kotlin and one in Swift, was exactly the size of the
    /// <c>*AndWait</c> methods — the blocking half of the #339 split, the very methods
    /// this pin exists to guard. A <see cref="DispatchNamedDeclaration"/> made blind to
    /// <c>AndWait</c> took Kotlin from 8 to 6 and Swift from 5 to 4, landing ON both floors,
    /// and every fact stayed green. So the scan now also has NAMED ANCHORS, derived from
    /// the manifest rather than restated: every name <c>src/dispatch-surface.json</c>
    /// records for a shell, in <c>methods</c> or in <c>ignored</c>, must be one the scan
    /// actually sees in that shell's source. A pattern that goes blind to any one known
    /// name reds naming it, whatever the count. The count floor stays, as the check that
    /// still means something if the manifest itself is emptied.</para>
    ///
    /// <para>WHAT THE ANCHORS DO NOT COVER: a name the manifest does not yet know about.
    /// That is the completeness fact's job, and it is exactly the set the anchors cannot
    /// name in advance.</para></summary>
    [Fact]
    public void TheDispatchDeclarationScan_IsNotVacuous()
    {
        FloorTheScannedSet("Kotlin", KotlinRuntime(), MinimumKotlinDispatchDeclarations);
        FloorTheScannedSet("Swift", SwiftRuntime(), MinimumSwiftDispatchDeclarations);

        AnchorTheScannedSet("Kotlin", KotlinRuntime(), "kotlin");
        AnchorTheScannedSet("Swift", SwiftRuntime(), "swift");

        static void AnchorTheScannedSet(string shellLabel, string source, string platform)
        {
            string[] known =
            [
                .. Surface().Where(m => m.Platforms is null || m.Platforms.Contains(platform)).Select(m => m.Name),
                .. Ignored().Where(i => i.Platforms is null || i.Platforms.Contains(platform)).Select(i => i.Name),
            ];
            Assert.True(known.Length >= 4,
                $"the manifest records only {known.Length} dispatch names for {shellLabel}; the "
                + "anchors below would check too little. src/dispatch-surface.json lost entries.");
            Assert.Contains("dispatchHostEventAndWait", known);

            HashSet<string> seen = DispatchNamedDeclarations(source).ToHashSet(StringComparer.Ordinal);
            string[] unseen = [.. known.Where(n => !seen.Contains(n)).OrderBy(n => n, StringComparer.Ordinal)];

            Assert.True(unseen.Length == 0,
                $"the {shellLabel} runtime scan ({DispatchNamedDeclaration}) does not see "
                + string.Join(", ", unseen) + ", which src/dispatch-surface.json records for this "
                + "shell. Either the declaration pattern has gone blind to part of its subject, "
                + "which is how an *AndWait-blind pattern once passed the count floor, or the "
                + "method was retired and its manifest entry must go with it.");
        }

        static void FloorTheScannedSet(string shellLabel, string source, int minimum)
        {
            string[] found = [.. DispatchNamedDeclarations(source).OrderBy(n => n, StringComparer.Ordinal)];

            Assert.True(found.Length >= minimum,
                $"the {shellLabel} runtime scan found only {found.Length} distinct dispatch*-named "
                + $"declarations and at least {minimum} were expected — found: "
                + (found.Length == 0 ? "(none)" : string.Join(", ", found)) + ".\n"
                + "This is the set EveryDispatchNamedDeclaration_IsDeclaredOrIgnored ITERATES. Empty "
                + "it and that completeness guard runs its loop zero times and reports green, with "
                + "Surface()'s `methods.Count >= 4` — which floors the MANIFEST, not this scan — "
                + "still perfectly satisfied. A low count means the declaration pattern "
                + $"({DispatchNamedDeclaration}) has stopped seeing its subject: the runtime source "
                + "moved, was reformatted past the pattern, or the comment stripper ate it. Re-point "
                + "the scan; only lower this floor if dispatch methods were genuinely retired, and "
                + "then say so in the doc comment above.");
        }
    }

    [Fact]
    public void EveryDispatchNamedDeclaration_IsDeclaredOrIgnored()
    {
        HashSet<string> declared = Surface().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        IgnoredMethod[] ignored = Ignored();

        CheckShell("Kotlin", KotlinRuntime(), "kotlin", declared, ignored);
        CheckShell("Swift", SwiftRuntime(), "swift", declared, ignored);

        static void CheckShell(string shellLabel, string source, string platform,
            HashSet<string> declared, IgnoredMethod[] ignored)
        {
            foreach (string name in DispatchNamedDeclarations(source))
            {
                if (declared.Contains(name))
                    continue;

                IgnoredMethod? entry = ignored.SingleOrDefault(i => i.Name == name);
                Assert.True(entry is not null,
                    $"{shellLabel} declares '{name}' (a dispatch*-named method) that is NEITHER in "
                    + "src/dispatch-surface.json's 'methods' list NOR on its 'ignored' list. A new "
                    + "dispatch method nobody declared is invisible to the drift pin by construction — "
                    + "exactly the false-green route #339's review found. Add it to 'methods' (so its "
                    + "lane call gets checked) or to 'ignored' (with a written reason) — never leave "
                    + "it unmentioned.");

                bool appliesHere = entry!.Platforms is null || entry.Platforms.Contains(platform);
                Assert.True(appliesHere,
                    $"{shellLabel} declares '{name}', which IS on the ignored list but restricted to "
                    + $"platforms [{string.Join(", ", entry.Platforms ?? [])}] that do not include "
                    + $"'{platform}' — either widen its platforms or add a {shellLabel}-specific entry.");

                Assert.False(string.IsNullOrWhiteSpace(entry.Reason),
                    $"'{name}' is on the ignored list without a written reason. Asymmetry/omission is "
                    + "allowed; silence is not.");
            }
        }
    }
}
