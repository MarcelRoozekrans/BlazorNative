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
//
// Each comparison fact carries its own floor (15.4, #375): an empty page list or an empty menu
// reds the fact that would otherwise compare nothing, not only its sibling. Since 15.7 the floor is
// the MEASURED size of each side (14 routed pages, 12 menu rows, zero headroom), not mere presence,
// and MenuRows_AreUniqueAndLabelled carries it too: its uniqueness counts and its Assert.All both
// passed on an empty menu, which the 15.6 audit's mutation showed.
//
// POSITIVE CONTROLS (Rule 3, 15.7). Both comparison facts are ABSENCE detectors: they pass when the
// list of offenders is empty, which is also what a detector that never detects returns. So the
// detection lives in Missing and Dangling, the facts call them, and two controls feed each helper
// one planted defect it must report: a ghost route that is routed but has no menu row, and a menu
// row that points at no routed page.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - Only the in-memory comparison between `SampleAppPages.All` and `BnDemo.Destinations` — as
//     a register entry outside Rule 6 (#375), it reads nothing on the tree.
//   - No third mechanism to reach a page: a route reachable by neither a menu row nor by one of
//     the two asserted exemptions (deep link, an Intent extra) is invisible to this file.
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

    /// <summary>The measured size of each side, at zero headroom. Adding a page or a row raises
    /// the count past the floor and nothing reds; removing one reds here, so the removal is a
    /// decision on the record, made by lowering the number in the same commit.</summary>
    private const int MeasuredRoutedPages = 14;
    private const int MeasuredMenuRows = 12;

    /// <summary>Rule 2, per fact: each comparison below is <i>for every X, assert Y</i>, which
    /// passes over an empty X. Until 15.4 only a SIBLING fact noticed an empty input; each fact now
    /// refuses to compare nothing on its own, naming the collection that came back short. Since 15.7
    /// the floor is the measured count rather than presence: one page and one row used to pass.</summary>
    private static void AssertBothSidesNonEmpty(string[] routed)
    {
        Assert.True(routed.Length >= MeasuredRoutedPages,
            $"SampleAppPages.All yields {routed.Length} routed pages, and {MeasuredRoutedPages} were "
            + "measured. An empty or gutted list makes this fact compare nothing and pass (Rule 2); "
            + "if a page was deliberately removed, lower the floor in the same commit.");
        Assert.True(BnDemo.Destinations.Length >= MeasuredMenuRows,
            $"BnDemo.Destinations has {BnDemo.Destinations.Length} rows, and {MeasuredMenuRows} were "
            + "measured. An empty or gutted menu makes this fact compare nothing and pass (Rule 2); "
            + "if a row was deliberately removed, lower the floor in the same commit.");
    }

    /// <summary>The routed pages, other than the two exemptions, that have no menu row. The
    /// detector of <see cref="EveryRoutedPage_ExceptTheTwoExemptions_HasAMenuRow"/>, extracted so
    /// <see cref="Missing_ReportsAPlantedGhostRoute"/> can drive the same code.</summary>
    private static string[] Missing(string[] routed, (string Label, string Route)[] menu)
    {
        var inMenu = menu.Select(d => d.Route).ToHashSet(StringComparer.Ordinal);

        return routed
            .Where(r => r != SelfRoute && r != SettingsRoute)
            .Where(r => !inMenu.Contains(r))
            .ToArray();
    }

    /// <summary>The menu rows whose route no page serves. The detector of
    /// <see cref="EveryMenuRow_PointsAtARoutedPage"/>, extracted so
    /// <see cref="Dangling_ReportsAPlantedDanglingRow"/> can drive the same code.</summary>
    private static string[] Dangling(string[] routed, (string Label, string Route)[] menu)
    {
        var routedSet = routed.ToHashSet(StringComparer.Ordinal);

        return menu
            .Select(d => d.Route)
            .Where(r => !routedSet.Contains(r))
            .ToArray();
    }

    [Fact]
    public void EveryRoutedPage_ExceptTheTwoExemptions_HasAMenuRow()
    {
        var routed = RoutedPages();
        AssertBothSidesNonEmpty(routed);

        var missing = Missing(routed, BnDemo.Destinations);

        Assert.True(missing.Length == 0,
            "these routed pages are in SampleAppPages.All but have no row in BnDemo.Destinations, "
            + "so nothing in the app can reach them — they are deep-link/Intent-only, which is the "
            + $"exact defect #204 fixed: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryMenuRow_PointsAtARoutedPage()
    {
        string[] routed = RoutedPages();
        AssertBothSidesNonEmpty(routed);

        var dangling = Dangling(routed, BnDemo.Destinations);

        Assert.True(dangling.Length == 0,
            "these BnDemo.Destinations rows name a route no page in SampleAppPages.All serves, so "
            + $"tapping them navigates nowhere: {string.Join(", ", dangling)}");
    }

    /// <summary>Rule 3 control for <see cref="Missing"/>: the real pages plus one planted ghost
    /// route, against the real menu. The helper must report the ghost and only the ghost; a
    /// detector that had stopped detecting would return nothing, and the fact it serves would
    /// stay green over exactly the defect #204 fixed.</summary>
    [Fact]
    public void Missing_ReportsAPlantedGhostRoute()
    {
        const string ghost = "/ghost-route-no-menu-row";
        string[] routed = [.. RoutedPages(), ghost];

        Assert.Equal([ghost], Missing(routed, BnDemo.Destinations));
    }

    /// <summary>Rule 3 control for <see cref="Dangling"/>: the real menu plus one planted row
    /// that points at no routed page, against the real pages. The helper must report that row
    /// and only that row.</summary>
    [Fact]
    public void Dangling_ReportsAPlantedDanglingRow()
    {
        const string nowhere = "/dangling-row-no-page";
        (string Label, string Route)[] menu = [.. BnDemo.Destinations, ("Dangling", nowhere)];

        Assert.Equal([nowhere], Dangling(RoutedPages(), menu));
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
        //
        // Rule 2 (15.7): the two counts below are Equal(0, 0) and the Assert.All passes on an empty
        // menu, so the floor comes first; and Camera is the named anchor row, the one
        // BnRenderTests.swift pins as the menu's last row.
        AssertBothSidesNonEmpty(RoutedPages());
        Assert.Contains(("Camera", "/camera"), BnDemo.Destinations);

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
