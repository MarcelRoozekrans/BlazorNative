namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DeepLinkVectorTests — the .NET third of the deep-link parity harness (#278).
//
// The other two suites assert a PARSER: Kotlin's BnDeepLinkVectorTest drives
// MainActivity.parseDeepLinkRouteForTest, Swift's BnDeepLinkVectorTests drives
// BnDeepLink.route(from:). .NET owns no deep-link parser at all — it owns the
// route TABLE, and the shells hand it a string — so there is nothing here to
// point the vectors at.
//
// What there IS here is the only lane that runs on EVERY pull request. The
// Kotlin suite needs an emulator (android-instrumented) and the Swift suite
// needs a macOS runner (ios); both are advisory lanes that can go stale for many
// commits. So a table that lost its interesting cases would leave two suites
// asserting nothing and neither of them would necessarily run to say so. These
// three facts are the fast tripwire for that: they assert the table's own
// coherence, from the lane that cannot be skipped.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The shared deep-link table, asserted from the one lane that runs on every PR.
/// The parsers live in Kotlin and Swift and are exercised by their own suites; this pins
/// that the TABLE stays meaningful — a vector table that lost its interesting cases would
/// leave both shells passing against nothing.</summary>
public sealed class DeepLinkVectorTests
{
    /// <summary>The case the whole phase was built around: Android returned <c>/settings</c>
    /// for <c>blazornative://settings/audio</c> and dropped the path. If this row is ever
    /// lost, both shell suites go green over a table that no longer covers the defect they
    /// exist to prevent — so its removal has to be a red test, not a quiet diff.</summary>
    [Fact]
    public void TheTable_StillContainsTheCaseThatDiverged()
    {
        Assert.Contains(BnDeepLinkVectors.All,
            v => v.Url == "blazornative://settings/audio" && v.Route == "/settings/audio");
    }

    /// <summary>Every vector is usable as a vector: a non-empty URL, and an expected route
    /// that is either null (the URL must be rejected) or an absolute route. A blank URL or a
    /// route missing its leading slash would be asserted faithfully by all three suites and
    /// mean nothing in any of them.</summary>
    [Fact]
    public void EveryVector_IsWellFormed()
    {
        Assert.NotEmpty(BnDeepLinkVectors.All);
        foreach ((string url, string? route) in BnDeepLinkVectors.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(url));
            if (route is not null)
                Assert.StartsWith("/", route, StringComparison.Ordinal);
        }
    }

    /// <summary>At least one vector is a foreign scheme that must produce NO route. Both
    /// platforms hand their delegate URLs the app never registered for, and a parser that
    /// coerced one into a route would navigate somewhere the user did not ask for — a table
    /// of only happy-path rows could not catch a parser that had stopped checking.</summary>
    [Fact]
    public void AWrongSchemeVector_ExpectsNoRoute()
        => Assert.Contains(BnDeepLinkVectors.All, v => !v.Url.StartsWith("blazornative://", StringComparison.Ordinal) && v.Route is null);
}
