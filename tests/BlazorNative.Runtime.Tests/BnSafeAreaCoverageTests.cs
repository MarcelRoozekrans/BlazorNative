using System.IO;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnSafeAreaCoverageTests — whole-branch review IMPORTANT 2 (Phase 14.2, #338).
//
// BnSafeArea is OPT-IN, like every other component (decision 1) — nothing forces
// an app author to wrap a page in it. The design's whole safety argument for that
// choice is that the mechanism is demonstrated everywhere a user would copy from:
// the `dotnet new` starter page and all four capability pages the milestone named
// (#338: camera, secure storage, geolocation, notifications — each one a page that
// puts a system permission prompt or a chunk of real content under the notch/home
// indicator if left unwrapped).
//
// Before this test, NOTHING enforced that claim. Unwrap one of the five files
// below — delete a `<BnSafeArea>` tag, or the `BnSafeArea` component-open call in
// a hand-written RenderTreeBuilder page — and every existing gate (build,
// PublicAPI baselines, ShellFrameTableDriftTests, RouteMenuDriftTests) stays
// green, because removing the wrapper is not a compile error and not a tracked
// frame. "Closed mechanically" is the milestone's own description of this phase;
// a text pin is the two lines that make it actually true instead of merely
// intended.
//
// A NAME MATCH, not a parse: this test greps for the literal "BnSafeArea" in each
// file, exactly like `RouteMenuDriftTests` and `TemplateDriftTests` already grep
// generated Razor/Kotlin/Swift text elsewhere in this project. It cannot tell an
// active wrap from a wrap sitting inside a `@* comment *@` — that is a real, small
// gap, but a text pin that reds on a genuine deletion (the actual failure mode
// this class exists to catch — an author or refactor removing the tag) is a large
// improvement over the ZERO coverage that existed before.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BnSafeAreaCoverageTests
{
    /// <summary>The five files the design's safety argument depends on: the
    /// `dotnet new` starter page, plus the four #338-named capability pages.
    /// (The website docs examples — `intro.md`, `guides/safe-area.md` — make the
    /// same claim but are prose, not shipped app code, and are out of scope for
    /// this mechanical guard.)</summary>
    public static TheoryData<string> FilesThatMustWrapBnSafeArea => new()
    {
        "templates/BlazorNative.Templates/content/BlazorNative.App/BnStarterPage.razor",
        "samples/BlazorNative.SampleApp/BnCameraDemo.cs",
        "samples/BlazorNative.SampleApp/BnSecureDemo.cs",
        "samples/BlazorNative.SampleApp/BnGeolocationDemo.cs",
        "samples/BlazorNative.SampleApp/BnNotificationsDemo.cs",
    };

    [Theory]
    [MemberData(nameof(FilesThatMustWrapBnSafeArea))]
    public void EveryNamedStarterOrCapabilityPage_StillMentionsBnSafeArea(string relativePath)
    {
        string file = CheckoutPath(relativePath);
        Assert.True(File.Exists(file), $"checkout file not found: {file}");
        string text = File.ReadAllText(file);

        Assert.True(text.Contains("BnSafeArea", StringComparison.Ordinal),
            $"'{relativePath}' no longer mentions BnSafeArea. This file is one of the five the "
            + "14.2 design's safety argument names as PROOF the opt-in mechanism is actually used "
            + "— an app author who copies this page as their starting point must land inside "
            + "BnSafeArea, or content renders under the notch/status bar/home indicator again "
            + "(#338). If this wrap was removed deliberately, update this test's file list "
            + "(BnSafeAreaCoverageTests.FilesThatMustWrapBnSafeArea) alongside the change — do "
            + "not just delete the assertion.");
    }

    private static string CheckoutPath(string relativePath)
        => Path.Combine(BnRepo.Root(), relativePath.Replace('/', Path.DirectorySeparatorChar));
}
