using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

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

    private static Method[] Surface()
    {
        string json = File.ReadAllText(Path.Combine(RepoRoot(), "src", "dispatch-surface.json"));
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

    /// <summary>Strips line and block comments so a KDoc mention of a method name
    /// never counts as a declaration — the same reason
    /// <c>GeneratedSymbolShadowTests.CodeLines</c> exists.</summary>
    private static string CodeText(string file)
    {
        var code = new List<string>();
        bool inBlock = false;
        foreach (string raw in File.ReadLines(file))
        {
            string line = raw;
            if (inBlock)
            {
                int end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) continue;
                line = line[(end + 2)..];
                inBlock = false;
            }
            int start = line.IndexOf("/*", StringComparison.Ordinal);
            if (start >= 0) { line = line[..start]; inBlock = true; }
            int slash = line.IndexOf("//", StringComparison.Ordinal);
            if (slash >= 0) line = line[..slash];
            if (line.Trim().Length > 0) code.Add(line);
        }
        return string.Join("\n", code);
    }

    private static string KotlinRuntime() => CodeText(Path.Combine(RepoRoot(),
        "src", "BlazorNative.Jni", "src", "main", "kotlin", "io", "blazornative", "jni",
        "BlazorNativeRuntime.kt"));

    private static string SwiftRuntime() => CodeText(Path.Combine(RepoRoot(),
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
    /// without matching the delegating declaration itself.</summary>
    private static Match FindOtherDeclaration(string source, string funcKeyword, string name, int excludeIndex)
    {
        var regex = new Regex($@"\b{funcKeyword}\s+{Regex.Escape(name)}\s*\(");
        foreach (Match m in regex.Matches(source))
        {
            if (m.Index != excludeIndex)
                return m;
        }
        return Match.Empty;
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
}
