using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NSLogDriftTests — Phase 11.4 Gate C (M11 DoD #6, #155), design §8.1 pin 6b:
// "no bare NSLog under src/BlazorNative.Apple/BnHost/**".
//
// ⚠ THIS PIN IS DOING MORE WORK THAN ITS ANDROID TWIN, AND THE REASON IS WORTH
// STATING. The Swift/Objective-C++ half of this framework is built by exactly one
// thing — .github/workflows/ios.yml, on a macOS runner. It is not built on a
// Windows or Linux dev machine, it is not built by `dotnet test`, and it is not
// built by the required `build-test` lane. So for anyone without a Mac, THE ONLY
// FEEDBACK ABOUT THE iOS SHELL THAT ARRIVES BEFORE CI IS A TEXT SCAN LIKE THIS
// ONE. That makes a source-scanning .NET test unusually valuable here rather than
// merely tidy: it is the sole pre-CI signal, and it runs everywhere the .NET
// suite runs.
//
// WHAT IT GUARDS. Gate C rewrote 78 shipped `NSLog` sites onto `BnLog` (os_log /
// os.Logger). `NSLog` is unconditional — no level, never compiled out, always
// written at the equivalent of `.default` — AND IT IS ALWAYS PUBLIC: every
// exception description, keychain key, image URL and file path it is handed is
// readable in any log collected off the device. That second property is the half
// of #155 that level gating cannot fix, and it is the half a re-introduced
// `NSLog` would silently give back. Site 79 must red the PR that adds it.
//
// THE MECHANISM is `ConsoleErrorDriftTests`' and `ShellStyleTableDriftTests`':
// a text scan of checkout files from the .NET suite, walking up from the test
// binary to the directory holding BlazorNative.sln. `build-test` is the one
// required lane where every source file is checkout-visible. No new
// infrastructure.
//
// NON-VACUITY IS ASSERTED, NOT ASSUMED. Every assertion here is of the form
// "found nothing", which is exactly what a BROKEN scan also reports — a moved
// tree, a glob that stopped matching, a regex reworded past its subject. So the
// scan asserts that it found files, that they are the RIGHT files (the four that
// held 66 of the 78 sites), that the pattern still matches where a match is KNOWN
// to exist (BnHostTests', which stay), and that the swept files did not simply
// have their diagnostics DELETED — each one must still route through the seam.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NSLogDriftTests
{
    /// <summary>THE PIN'S SUBJECT, FROM THE ROSTER (#364 F1) — the shipped iOS
    /// shell, the `appleShell` set of `src/shell-source-roots.json`.
    ///
    /// This was a `const string` here. It was the right answer, and a private
    /// copy of a right answer is the precondition for a twin divergence rather
    /// than a defence against it (pin standard, Rule 8): two other pins held
    /// their own answers to "what is the shell's source tree", one of them
    /// missing half the Android shell, and nothing compared them. The roster is
    /// now the one home, derived from XcodeGen's own `BnHost` target source list.
    ///
    /// THIS PIN NAMES ITS SETS ONE AT A TIME rather than taking the flattened
    /// list, because it treats its two consumed sets DIFFERENTLY — `appleShell`
    /// is scanned for offenders and `appleTestBundle` is the positive control
    /// that must still hold live `NSLog` calls. Flattening them is exactly what
    /// would lose that distinction, and the overload still checks each name
    /// against this pin's own `consumes` list.</summary>
    /// THERE IS DELIBERATELY NO `AppleShellRoots()` HELPER FOR THE WALKS TO
    /// SHARE. A single wrapper is a SINGLE LINE that reverts all of this: point
    /// one private method at a hard-coded path and the walk AND the assertion
    /// written to catch the walk read the reverted answer together, while
    /// `EveryConsumer_ReadsItsRootsFromTheRoster` stays green because the file
    /// still names `SetsFor` somewhere. Every site calls the loader for itself,
    /// so the coverage assertion in `TheScan_IsNotVacuous` reads the roster
    /// INDEPENDENTLY of what the walk read and disagrees with it rather than
    /// moving with it. One implementation, several callers, is Rule 8's shape.
    /// </summary>
    private static string BnHost => string.Join(
        " + ", ShellSourceRoots.SetsFor(nameof(NSLogDriftTests), "appleShell"));

    /// <summary>THE EXEMPT DIRECTORY, NAMED EXPLICITLY SO THE EXEMPTION IS VISIBLE
    /// RATHER THAN IMPLIED (design §4.2 step 3, §12).
    ///
    /// `src/BlazorNative.Apple/BnHostTests/**` holds 24 `NSLog` sites and they
    /// STAY. XCTest output is not shipped output: it is read by a human watching a
    /// simulator run, it never reaches an end user's device, and it carries no
    /// information-disclosure risk because the process it runs in is a test host.
    /// Routing it through a level-gated seam would only mean a failing test could
    /// print nothing about why.
    ///
    /// The exemption is a DIRECTORY, not a pattern: this pin scans `BnHost/` and
    /// simply never walks `BnHostTests/`. It is named here — and asserted to be
    /// real by <see cref="TheTestBundleExemption_IsRealAndStillHoldsNSLog"/> —
    /// so that a reader of the failure message knows why their test file is not
    /// covered without having to infer it from a path.
    ///
    /// IT IS A ROSTER SET, `appleTestBundle`, and the roster entry says in as
    /// many words that this pin consumes it deliberately as its positive-control
    /// anchor. That is why the roster has NAMED SETS rather than one flat list of
    /// roots: "excluded" and "consumed as the fixed point the detector must still
    /// hit" are opposite positions, and a flat list cannot tell them apart.
    /// Dropping it from `consumes` would defuse the control silently, so the
    /// partition makes that a deliberate, reviewed edit.</summary>
    /// <summary>The exempt roots, as one string for a failure message. As above,
    /// there is no shared accessor: each walk asks the loader itself.</summary>
    private static string BnHostTests => string.Join(
        " + ", ShellSourceRoots.SetsFor(nameof(NSLogDriftTests), "appleTestBundle"));

    /// <summary>Matches an `NSLog` CALL. Comments are excluded by
    /// <see cref="CommentStrippedSource.NumberedCodeLines"/> — this phase's own sources discuss `NSLog` at
    /// length (BnLog.swift's header explains for eight lines why it is not one),
    /// and a pattern that cannot tell prose from a call reports the wrong number.
    /// The trailing `\s*\(` is what makes it a call rather than a mention.</summary>
    private const string NSLogCall = @"\bNSLog\s*\(";

    /// <summary>The files that held 66 of the 78 swept sites, plus the seam. Each
    /// must still be found by the walk AND still route through <c>BnLog</c> — see
    /// <see cref="TheSweptFiles_StillRouteThroughTheSeam"/>.</summary>
    private static readonly string[] SweptFiles =
    [
        "BnWidgetMapper.swift",   // 54 — the bulk
        "BnRuntime.swift",        //  6 — the boot narration #155 names
        "BnYogaLayout.mm",        //  4 — Objective-C++, via the @_cdecl BnLogC shim
        "BnSecureStorage.swift",  //  4
        "AppleShellBridge.swift", //  4
        "BnCamera.swift",         //  3
        "HostViewController.swift",
        "BnYogaProbe.swift",
        "BnFrameAdapter.swift",
    ];

    // ── 1. The pin ───────────────────────────────────────────────────────────

    /// <summary>NOT ONE BARE `NSLog` SURVIVES UNDER `BnHost/`.
    ///
    /// The 78 sites Gate C migrated wrote unconditionally to the unified log, at
    /// one severity, with every interpolated value public. Routing them through
    /// <c>BnLog</c> is what gives them a level (so Release is quiet), a category
    /// (so `log stream` can filter), and — through `os.Logger`'s private-by-default
    /// interpolation — redaction of anything the app, the user, the OS or an
    /// `Error` supplied. A new bare `NSLog` opts out of all three at once, and
    /// nothing else in this repository would notice.</summary>
    [Fact]
    public void NoBareNSLog_SurvivesUnderBnHost()
    {
        var offenders = new List<string>();

        foreach (string file in ShellFiles())
        {
            offenders.AddRange(CommentStrippedSource.NumberedCodeLines(file)
                .Where(l => Regex.IsMatch(l.Text, NSLogCall))
                .Select(l => $"  {Relative(file)}:{l.Number}  {l.Text.Trim()}"));
        }

        Assert.True(offenders.Count == 0,
            "BARE NSLog UNDER " + BnHost + " — it must go through BnLog.\n"
            + string.Join("\n", offenders)
            + "\n\nNSLog is unconditional (no level, never compiled out, so it ships in Release), "
            + "and — the half that level gating cannot fix — EVERY VALUE IT INTERPOLATES IS "
            + "PUBLIC in any log collected off the device. os.Logger's interpolation is private "
            + "by default; that is the information-disclosure half of #155.\n"
            + "Use BnLog.error/warn/info/debug/verbose(category, message) from Swift, or BnLogC "
            + "(BnLog.h) from Objective-C++. The category is the bracketed tag the message "
            + "already carried, without the brackets.\n"
            + $"({BnHostTests}'s 24 sites are XCTest diagnostics and are exempt BY DIRECTORY — "
            + "that exemption is about test output and buys no exemption here.)");
    }

    // ── 2. Non-vacuity — the scan must be looking at something ───────────────

    /// <summary>THE SCAN ACTUALLY READS FILES, AND THE RIGHT ONES.
    ///
    /// A scan that walked the wrong tree would report "no offenders" forever. This
    /// is the counterweight: the file set is non-empty, of a plausible size, and
    /// contains every file that held a swept site plus the seam itself.</summary>
    [Fact]
    public void TheScan_IsNotVacuous()
    {
        List<string> files = ShellFiles().ToList();

        Assert.True(files.Count > 15,
            $"the {BnHost} scan found only {files.Count} files — it is reading the wrong tree and "
            + "NoBareNSLog_SurvivesUnderBnHost is passing while blind. Fix the walk, do not delete "
            + "the pin.");

        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BnLog.swift", names);
        foreach (string swept in SweptFiles)
            Assert.Contains(swept, names);

        // THE EXEMPTION IS STRUCTURAL, AND NOW THAT BOTH TREES COME FROM THE ROSTER
        // THAT IS ASSERTED RATHER THAN OBSERVED. `BnHostTests/` being a SIBLING of
        // `BnHost/` was a fact about two directory names; the roots are now two
        // manifest entries, and a widened `appleShell` root would swallow the test
        // bundle — taking this pin's positive control INSIDE its own subject, where
        // its 24 deliberate NSLog sites become 24 offenders. That direction is a
        // loud red rather than a silent green, and this says which edit caused it.
        var bundles = ShellSourceRoots.SetsFor(nameof(NSLogDriftTests), "appleTestBundle")
            .Select(r => CheckoutPath(r) + Path.DirectorySeparatorChar)
            .ToArray();
        var swallowed = files
            .Where(f => bundles.Any(b => f.StartsWith(b, StringComparison.Ordinal)))
            .ToList();

        Assert.True(swallowed.Count == 0,
            "THE EXEMPT TEST BUNDLE IS INSIDE THE SCANNED SHELL:\n"
            + string.Join("\n", swallowed.Select(f => "  " + Relative(f)))
            + "\n\n`appleShell` and `appleTestBundle` are separate sets in "
            + ShellSourceRoots.ManifestPath + " precisely so this pin can scan one and use the "
            + "other as its positive control. A root that nests them makes the control part of "
            + "the subject, so the 24 XCTest NSLog sites that MUST stay would be reported as "
            + "offenders. Re-point the roster, not this assertion.");

        // ── THE ROSTER IS WHAT THE WALK WALKED, ASSERTED RATHER THAN WRITTEN DOWN ──
        //
        // Everything above measures the scan's OUTPUT, and `NoBareNSLog` is an ABSENCE
        // assertion, so an output of nothing is indistinguishable from a walk that
        // opened nothing. `SetsFor` is called HERE rather than through a helper the
        // walk shares, so a walk re-pointed at a hard-coded path disagrees with this
        // list instead of moving with it — which is the one-line revert that reopens
        // #364 F1 while a text guard looking for the name `SetsFor` stays green.
        //
        // WHAT THIS DOES NOT COVER: it demands ONE file per root, not the whole root,
        // and it cannot see a walk over a strict SUPERSET of the declared roots. The
        // superset direction fails SAFE here — an extra tree can only add offenders —
        // except for the one case the assertion directly above rules out, which is a
        // superset that swallows the exempt bundle.
        var unvisited = ShellSourceRoots.SetsFor(nameof(NSLogDriftTests), "appleShell")
            .Where(r => !files.Any(f => f.StartsWith(
                CheckoutPath(r) + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .ToList();

        Assert.True(unvisited.Count == 0,
            "THE SCAN NEVER OPENED A FILE UNDER THESE DECLARED ROOTS:\n"
            + string.Join("\n", unvisited.Select(r => "  " + r))
            + $"\n\n{ShellSourceRoots.ManifestPath} says {nameof(NSLogDriftTests)} consumes "
            + $"them as `appleShell`, and the walk visited {files.Count} files, none of them "
            + "there. Either the walk was re-pointed away from the roster — naming the roster "
            + "is not the same as reading it, and this is the half that checks — or the target "
            + "moved and the roster needs re-pointing deliberately.");
    }

    /// <summary>…AND THE PATTERN STILL MATCHES WHERE A MATCH IS KNOWN TO EXIST.
    ///
    /// A non-empty file set proves the walk works; it does not prove the REGEX
    /// does. The exempt test bundle is the fixed point that proves the detector
    /// detects — it is the one place under `BnHostTests/` that MUST still contain
    /// live `NSLog` calls. Reword the pattern past its subject and this reds,
    /// instead of the pin quietly going green forever.</summary>
    [Fact]
    public void TheTestBundleExemption_IsRealAndStillHoldsNSLog()
    {
        int hits = 0;
        foreach (string relative in ShellSourceRoots.SetsFor(
                     nameof(NSLogDriftTests), "appleTestBundle"))
        {
            string tests = CheckoutPath(relative);
            Assert.True(Directory.Exists(tests),
                $"{relative} is missing, so the exemption this pin names protects nothing. Either "
                + "the test bundle moved — then re-point it in " + ShellSourceRoots.ManifestPath
                + " deliberately — or it is gone, in which case delete the exemption rather than "
                + "keeping it as folklore.");

            hits += Directory.EnumerateFiles(tests, "*.swift", SearchOption.AllDirectories)
                .SelectMany(CommentStrippedSource.NumberedCodeLines)
                .Count(l => Regex.IsMatch(l.Text, NSLogCall));
        }

        Assert.True(hits > 0,
            $"the NSLog pattern matched NOTHING under {BnHostTests}, which is the one tree that "
            + "MUST still contain live NSLog calls (XCTest diagnostics, deliberately not swept). "
            + "Either the test bundle stopped using NSLog — then this fixed point moved and should "
            + "be re-pointed — or the pattern no longer matches a real call, in which case "
            + "NoBareNSLog_SurvivesUnderBnHost is holding nothing.");
    }

    /// <summary>THE SWEEP REWROTE THE DIAGNOSTICS; IT DID NOT DELETE THEM.
    ///
    /// "No bare NSLog" is trivially satisfiable by removing every log line in the
    /// shell, and that would be strictly worse than the state Gate C found — the
    /// mapper's `ignored`/`skipped` lines are the ONLY record that a wire the app
    /// author wrote was silently dropped (design §4.3, which is why they stay at
    /// Warn and ship in Release). So each swept file must still reach the seam:
    /// Swift through <c>BnLog.</c>, Objective-C++ through the <c>@_cdecl</c>
    /// <c>BnLogC</c> shim, which is the only way that language can call it.</summary>
    [Fact]
    public void TheSweptFiles_StillRouteThroughTheSeam()
    {
        var offenders = new List<string>();

        foreach (string name in SweptFiles)
        {
            string file = ShellFiles().Single(f => Path.GetFileName(f) == name);
            bool routed = CommentStrippedSource.NumberedCodeLines(file)
                .Any(l => Regex.IsMatch(l.Text, @"\bBnLog\.\w+\s*\(|\bBnLogC\s*\(|\bbn_log_\w+\s*\("));

            if (!routed) offenders.Add($"  {name}");
        }

        Assert.True(offenders.Count == 0,
            "THE SWEEP WAS UNDONE BY DELETION, NOT BY MIGRATION. These files held NSLog sites "
            + "before Gate C and now reach no logging seam at all:\n" + string.Join("\n", offenders)
            + "\n\n'No bare NSLog' is trivially satisfied by deleting every diagnostic, and that is "
            + "the opposite of what #155 asked for: the mapper's `ignored`/`skipped` lines are the "
            + "only record that a wire the app author wrote was silently dropped (design §4.3). If "
            + "a file genuinely no longer logs, remove it from SweptFiles deliberately.");
    }

    // ── the scanner ──────────────────────────────────────────────────────────

    /// <summary>Every Swift / Objective-C / Objective-C++ / header file under
    /// the roster's `appleShell` roots. Fails loudly if a tree is not there — a
    /// missing tree must break this test, not silently pass it with an empty set.
    ///
    /// THE EXEMPTION IS NO LONGER "STRUCTURAL BECAUSE THEY ARE SIBLINGS", and the
    /// old sentence saying so was left standing for one commit after it stopped
    /// being true. `BnHostTests/` being a sibling of `BnHost/` was a fact about
    /// two directory names written in this file; both are now manifest entries,
    /// so what keeps the exempt bundle out of this walk is that the roster
    /// declares them as two separate sets. That is a stronger guarantee and a
    /// different one, and `TheScan_IsNotVacuous` asserts it directly rather than
    /// leaving it as a property of the paths — see the `swallowed` check
    /// there.</summary>
    private static IEnumerable<string> ShellFiles()
    {
        string[] extensions = [".swift", ".m", ".mm", ".h"];
        var files = new List<string>();

        foreach (string relative in ShellSourceRoots.SetsFor(
                     nameof(NSLogDriftTests), "appleShell"))
        {
            string root = CheckoutPath(relative);
            Assert.True(Directory.Exists(root),
                $"{relative} not found under the repo root: {root}. It is declared in "
                + ShellSourceRoots.ManifestPath + ", so either the target moved and the roster "
                + "needs re-pointing, or the roster is already wrong and every consumer of "
                + "`appleShell` is blind.");

            files.AddRange(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(
                    Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)));
        }

        return files.OrderBy(f => f, StringComparer.Ordinal);
    }

    private static string CheckoutPath(string relativePath)
        => Path.Combine(BnRepo.Root(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Relative(string file)
        => Path.GetRelativePath(BnRepo.Root(), file).Replace(Path.DirectorySeparatorChar, '/');
}
