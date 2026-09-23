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
// frame.
//
// ── PHASE 15.1: THE DISCLOSED LIMIT WAS NOT SMALL, AND IT WAS NOT HYPOTHETICAL ──
//
// This header used to say the pin "cannot tell an active wrap from a wrap sitting
// inside a `@* comment *@` — that is a real, small gap", and claimed the pin "reds
// on a genuine deletion (the actual failure mode this class exists to catch)".
//
// THE SECOND HALF OF THAT SENTENCE WAS FALSE, for all five files, on the day it
// was written. Every one of the five names BnSafeArea in a COMMENT as well as in
// live code:
//
//   · BnStarterPage.razor       — its `@* … *@` header draws the component tree,
//                                 naming BnSafeArea three times
//   · the four capability demos — each carries `// #338 (phase 14.2 task 6):
//                                 wrapped in BnSafeArea (opt-in — …)` above the
//                                 builder call
//
// So the raw-text `Contains` the pin performed was satisfied by the prose in every
// case, and DELETING THE WRAP ENTIRELY left the pin green. That was demonstrated,
// not reasoned: the `<BnSafeArea>`/`</BnSafeArea>` tags were removed outright from
// BnStarterPage.razor and all five facts passed. The pin was not guarding a
// commented-out wrap; it was not guarding anything at all in the file it most
// needed to guard, because the documentation of the mechanism is indistinguishable
// from the mechanism to a text search.
//
// Pin standard Rule 5's subtle half — *when someone shows you the boundary was
// optimistic, move the boundary, do not defend where you drew it* — is why this
// paragraph replaces the old one rather than sitting beside it. The old wording
// made the wrong placement look considered.
//
// THE FIX: scan the COMMENT-STRIPPED projection, not the raw text.
// `CommentStrippedSource` is the repo's one comment stripper; phase 15.1 gave it a
// `StripRazor` sibling for `@* … *@` rather than a mode flag on `Strip`, and that
// type's own header records the reasoning. The four `.cs` files route through
// `Strip`, which already served eight pins unchanged.
//
// WHAT THIS STILL DOES NOT COVER, with the direction each one fails.
//  1. A wrap inside an `@if`/`@foreach` branch that never executes reads as live.
//     FAILS GREEN. Out of reach of any text scan — it needs a parse, and a parse
//     is not what this pin is. Named so it is not mistaken for covered ground.
//  2. A wrap inside an HTML comment, `<!-- … -->`, reads as live. FAILS GREEN.
//     Deliberately uncovered; see StripRazor's own note on why guessing at Razor's
//     handling of components inside HTML comments would be asserting a compiler
//     behaviour nobody here has measured.
//  3. MENTIONING is not WRAPPING. `BnSafeArea` appearing in live code proves the
//     name is used, not that it is an ANCESTOR of the page's content — a wrap
//     moved to the bottom of the tree, or applied to an empty view beside the real
//     content, passes. FAILS GREEN. This is the widest remaining gap and it is the
//     original design's, not the stripper's: a name match was chosen over a parse,
//     the same way RouteMenuDriftTests and TemplateDriftTests grep generated text
//     elsewhere in this project. Closing it means mounting each page and asserting
//     on the frame, which is a different and much larger pin.
//  4. Only these five files are scanned. A sixth page an author would copy from is
//     invisible until someone adds it to the roster below.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BnSafeAreaCoverageTests
{
    private const string Component = "BnSafeArea";

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
        string code = CodeOf(relativePath);

        Assert.True(Mentions(code) > 0,
            $"'{relativePath}' no longer mentions {Component} in LIVE CODE. This file is one of "
            + "the five the 14.2 design's safety argument names as PROOF the opt-in mechanism is "
            + "actually used — an app author who copies this page as their starting point must "
            + "land inside BnSafeArea, or content renders under the notch/status bar/home "
            + "indicator again (#338).\n\n"
            + "NOTE THE PHRASE 'IN LIVE CODE'. This scans the comment-stripped projection, so a "
            + "wrap that has been COMMENTED OUT reds here exactly like one that was deleted — "
            + "which is the whole point of phase 15.1's change to this pin, and the reason a "
            + "mention in the file's own header prose no longer answers for the wrap.\n\n"
            + "If this wrap was removed deliberately, update this test's file list "
            + "(BnSafeAreaCoverageTests.FilesThatMustWrapBnSafeArea) alongside the change — do "
            + "not just delete the assertion.");
    }

    /// <summary>
    /// THE POSITIVE CONTROL for the comment strippers (pin standard Rule 3), and the
    /// record of the false green they close.
    ///
    /// The fact above is a positive-match assertion, so ITS detector — the substring
    /// search — is exercised on every run and needs nothing. The stripper is the part
    /// that can go quiet: reword `StripRazor`'s `@*` opener, or break `Strip`'s `//`
    /// branch, and the projection silently becomes the raw text again, which is
    /// precisely the state this phase found and fixed. The fact above would stay green
    /// forever while guarding nothing.
    ///
    /// So each of the five files is required to be a fixed point in BOTH directions.
    /// This is a TREE ANCHOR rather than a fixture — an exempt-subtree control in
    /// NSLogDriftTests' shape, except that here the "exempt subtree" is the comment
    /// region of the very same file:
    ///
    ///   · RAW &gt; STRIPPED — the file must still contain at least one mention the
    ///     stripper REMOVES. Every one of the five carries one today (the starter
    ///     page's `@* … *@` tree diagram; the four demos' `// #338 …` line), and that
    ///     is not a coincidence — a file that documents the mechanism it uses is the
    ///     normal case, which is why the raw-text pin was green over five deletable
    ///     wraps.
    ///   · STRIPPED &gt; 0 — the stripper must not have eaten the live wrap as well.
    ///     This is the over-strip half, and it is what stops "make the projection
    ///     empty" from being a way to pass the control.
    ///
    /// IF THIS REDS ON THE FIRST HALF, the honest move is usually to re-point rather
    /// than to delete: check whether the file's explanatory comment was rewritten to
    /// stop naming the component. If a file legitimately no longer documents
    /// BnSafeArea in prose, it stops being an anchor — move it out of this control
    /// and say so here, rather than weakening the comparison.
    /// </summary>
    [Theory]
    [MemberData(nameof(FilesThatMustWrapBnSafeArea))]
    public void TheCommentStrip_IsLoadBearing_InEveryNamedFile(string relativePath)
    {
        string raw = SourceOf(relativePath);
        string code = CodeOf(relativePath);

        int rawMentions = Mentions(raw);
        int codeMentions = Mentions(code);

        Assert.True(rawMentions > codeMentions,
            $"'{relativePath}' mentions {Component} {rawMentions} time(s) in the raw text and "
            + $"{codeMentions} time(s) after comment stripping — the stripper removed NOTHING, so "
            + "this file is no longer proving that it strips.\n\n"
            + "This control exists because the pin above is an absence-free positive match: it "
            + "cannot tell a working stripper from a stripper that returns its input. Each of "
            + "these five files carries a mention of BnSafeArea in a COMMENT — the starter page's "
            + "`@* … *@` header tree, the demos' `// #338 …` line — and that commented mention is "
            + "the fixed point the stripper is required to hit.\n\n"
            + "Either the stripper stopped recognising this file's comment grammar (fix it — the "
            + "raw-text false green this replaced is documented in this file's header), or the "
            + "file's prose no longer names the component (then re-point this control "
            + "deliberately; do not relax the comparison).");

        Assert.True(codeMentions > 0,
            $"'{relativePath}' mentions {Component} {rawMentions} time(s) raw and ZERO times after "
            + "stripping — the stripper ate the live wrap along with the comments. That is the "
            + "OVER-STRIP direction, and it is why this control has two halves: a stripper that "
            + "returns the empty string would satisfy the first assertion perfectly. Over-stripping "
            + "fails red rather than green, which is the right direction, but it is still wrong.");
    }

    /// <summary>
    /// The census's exact scenario, executed rather than believed: COMMENT THE WRAP
    /// OUT and the pin must red.
    ///
    /// The control above proves the stripper removes SOMETHING from each file. It does
    /// not prove that commenting out the WRAP SPECIFICALLY is caught — a stripper that
    /// handled the starter page's header block but not an inline `@* … *@` would pass
    /// it. So this splices, in the shape the ReleaseWorkflowPinTests fixture uses:
    /// take the REAL file, comment out every line whose LIVE mention survives the
    /// strip, in that file's own grammar, and require the pin's own projection —
    /// <see cref="CodeOf"/>, not a copy of it — to go empty.
    ///
    /// Line indices line up because both strippers preserve every newline, so the
    /// projection has one entry per source line. The splice count is asserted non-zero
    /// first: a splice that commented nothing out would leave the assertion below
    /// testing the unmodified file.
    ///
    /// What a fixture cannot buy: this proves the grammar is handled at an arbitrary
    /// line, not that the four uncovered cases in the header are reachable. Gap 3
    /// there — mentioning is not wrapping — is untouched by this and by everything
    /// else in this file.
    /// </summary>
    [Theory]
    [MemberData(nameof(FilesThatMustWrapBnSafeArea))]
    public void ACommentedOutWrap_IsNotMistakenForALiveOne(string relativePath)
    {
        string raw = SourceOf(relativePath);
        bool razor = IsRazor(relativePath);

        string[] rawLines = raw.Split('\n');
        string[] codeLines = Project(raw, razor).Split('\n');

        Assert.True(rawLines.Length == codeLines.Length,
            $"the projection of '{relativePath}' has {codeLines.Length} lines against the source's "
            + $"{rawLines.Length}. Both strippers preserve every newline so the two line up "
            + "index-for-index; if that stopped being true the splice below would comment out the "
            + "wrong lines and this fixture would prove nothing.");

        int commented = 0;
        for (int i = 0; i < rawLines.Length; i++)
        {
            if (!codeLines[i].Contains(Component, StringComparison.Ordinal)) continue;
            rawLines[i] = razor ? "@*" + rawLines[i] + "*@" : "//" + rawLines[i];
            commented++;
        }

        Assert.True(commented > 0,
            $"found NO live {Component} line to comment out in '{relativePath}' — the splice would "
            + "have run against the unmodified file and this fixture would be vacuous. The pin fact "
            + "above should already have red for the same reason.");

        string spliced = Project(string.Join("\n", rawLines), razor);

        Assert.True(Mentions(spliced) == 0,
            $"commented out {commented} live {Component} line(s) in '{relativePath}' — in "
            + (razor ? "`@* … *@`" : "`//`")
            + $" — and the projection STILL reports {Mentions(spliced)} live mention(s). The "
            + "stripper does not handle this file's comment grammar at an arbitrary line, so a "
            + "wrap that an author comments out instead of deleting passes the pin above while the "
            + "page renders under the notch (#338). That is the exact false green phase 15.1 "
            + "closed; it has reopened.");
    }

    // ── the pin's own projection, used by every fact above ───────────────────

    /// <summary>A `.razor` file goes through <see cref="CommentStrippedSource.StripRazor"/>
    /// (Razor's `@* … *@` plus the C# forms a `@code` block can carry); everything else
    /// through <see cref="CommentStrippedSource.Strip"/>, the same call eight other pins
    /// make. The extension dispatch lives HERE rather than in the shared type because
    /// this is the only Razor caller in the repo; if a second one arrives, move it.</summary>
    private static string CodeOf(string relativePath)
        => Project(SourceOf(relativePath), IsRazor(relativePath));

    private static string Project(string source, bool razor)
        => razor ? CommentStrippedSource.StripRazor(source) : CommentStrippedSource.Strip(source);

    private static bool IsRazor(string relativePath)
        => relativePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);

    /// <summary>The file's text with newlines normalised to `\n`, so the splice fixture's
    /// line indices are not thrown off by CRLF. Reds naming the file if it is missing —
    /// a pin that cannot see its subject must never pass vacuously (Rule 4).</summary>
    private static string SourceOf(string relativePath)
    {
        string file = CheckoutPath(relativePath);
        Assert.True(File.Exists(file),
            $"checkout file not found: {file}. '{relativePath}' is one of the five files the "
            + "#338 safety argument names; if it moved, re-point "
            + "BnSafeAreaCoverageTests.FilesThatMustWrapBnSafeArea deliberately rather than "
            + "dropping the row.");
        return File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int Mentions(string text)
    {
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf(Component, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += Component.Length;
        }
        return count;
    }

    private static string CheckoutPath(string relativePath)
        => Path.Combine(BnRepo.Root(), relativePath.Replace('/', Path.DirectorySeparatorChar));
}
