using BlazorNative.Runtime;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RouteMenuDriftTests — #204: the sample's capability menu cannot silently fall
// behind its manifest.
//
// Before the menu existed, the sample was a hub-and-spoke with no hub: eight
// pages already carried "← Back" → "/", but nothing in the app linked TO them.
// They were reachable only by deep link or an Intent extra, and NOTHING SAID SO
// — adding a routed page and never linking it looked exactly like adding a
// routed page and linking it. That is the failure this file makes loud.
//
// The pin is deliberately BIDIRECTIONAL. Coverage alone (every route has a
// button) would let a menu row point at a route no page serves, which taps
// through to a failed navigation. The reverse alone (every button has a page)
// would let a new page go unreachable, which is the original defect. Both
// directions, or the guard only half exists.
//
// "/" and "/settings" are the two documented exemptions, asserted rather than
// assumed: "/" is BnDemo itself (a menu row to the page you are on) and
// "/settings" is reached by the pinned "Settings →" button that IS the DoD #7
// navigation proof. If either ever stops being exempt-worthy — BnDemo moved off
// "/", say — the exemption assertions below fail rather than quietly widening.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RouteMenuDriftTests
{
    /// <summary>The routes the menu deliberately does not carry, and why.</summary>
    private const string SelfRoute = "/";           // BnDemo — the page the menu is ON
    private const string SettingsRoute = "/settings"; // reached by the pinned "Settings →" button

    private static string[] RoutedPages() =>
        [.. SampleAppPages.All
            .Where(p => p.Route is not null)
            .Select(p => p.Route!)];

    [Fact]
    public void EveryRoutedPage_ExceptTheTwoExemptions_HasAMenuRow()
    {
        var inMenu = BnDemo.Destinations.Select(d => d.Route).ToHashSet(StringComparer.Ordinal);

        var missing = RoutedPages()
            .Where(r => r != SelfRoute && r != SettingsRoute)
            .Where(r => !inMenu.Contains(r))
            .ToArray();

        Assert.True(missing.Length == 0,
            "these routed pages are in SampleAppPages.All but have no row in BnDemo.Destinations, "
            + "so nothing in the app can reach them — they are deep-link/Intent-only, which is the "
            + $"exact defect #204 fixed: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryMenuRow_PointsAtARoutedPage()
    {
        var routed = RoutedPages().ToHashSet(StringComparer.Ordinal);

        var dangling = BnDemo.Destinations
            .Select(d => d.Route)
            .Where(r => !routed.Contains(r))
            .ToArray();

        Assert.True(dangling.Length == 0,
            "these BnDemo.Destinations rows name a route no page in SampleAppPages.All serves, so "
            + $"tapping them navigates nowhere: {string.Join(", ", dangling)}");
    }

    [Fact]
    public void TheTwoExemptions_AreStillWhatTheyClaimToBe()
    {
        // "/" must still be BnDemo — the menu omits it because it is THIS page.
        var self = Assert.Single(SampleAppPages.All, p => p.Route == SelfRoute);
        Assert.Equal("BnDemo", self.Name);

        // "/settings" must still exist as a routed page — the "Settings →" button
        // reaches it, which is why the menu does not repeat it.
        Assert.Contains(SampleAppPages.All, p => p.Route == SettingsRoute);

        // …and neither may ALSO appear in the menu, or the exemption is a fiction
        // and the page is reachable twice by two different mechanisms.
        Assert.DoesNotContain(BnDemo.Destinations, d => d.Route == SelfRoute);
        Assert.DoesNotContain(BnDemo.Destinations, d => d.Route == SettingsRoute);
    }

    [Fact]
    public void MenuRows_AreUniqueAndLabelled()
    {
        // A duplicate route is two buttons doing the same thing; a duplicate label
        // is two buttons a device suite cannot tell apart (both suites select
        // buttons BY LABEL, so this one is load-bearing for those tests, not
        // cosmetic).
        Assert.Equal(
            BnDemo.Destinations.Length,
            BnDemo.Destinations.Select(d => d.Route).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            BnDemo.Destinations.Length,
            BnDemo.Destinations.Select(d => d.Label).Distinct(StringComparer.Ordinal).Count());

        Assert.All(BnDemo.Destinations, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Label));
            Assert.StartsWith("/", d.Route, StringComparison.Ordinal);
        });
    }
}
