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
    // ── THIS PIN HOLDS NO ROOT LIST AND NO MATCHING LOOP ─────────────────────
    //
    // Both were deleted across two review rounds. A private accessor returning the
    // roots is ONE line to re-point, and re-pointing it moves the walk and the
    // assertion written to catch the walk together. A matching loop in the fact is
    // ONE line to filter, which defeats a coverage assertion while the offender
    // assertion stays green over an unscanned shell.
    //
    // So this pin names a CONSUMER and a SET, and `ShellSourceScan` resolves the
    // roots from src/shell-source-roots.json, reads each file, and records coverage
    // in the same expression that hands the bytes to the matcher. Every fact below
    // calls it ITSELF and asserts coverage on the result of ITS OWN call.
    //
    // IT NAMES ITS SETS ONE AT A TIME, which is why the scan takes a set at all:
    // this pin scans `appleShell` for offenders and uses `appleTestBundle` as the
    // positive control that must still hold live NSLog calls. Flattening the two
    // loses exactly that distinction, and a set named here is still checked against
    // this pin's own `consumes` list in the roster.
    //
    // THE EXEMPT BUNDLE, NAMED SO THE EXEMPTION IS VISIBLE RATHER THAN IMPLIED
    // (design §4.2 step 3, §12). `appleTestBundle` holds 24 NSLog sites and they
    // STAY: XCTest output is not shipped output, it is read by a human watching a
    // simulator run, it never reaches an end user's device, and the process it runs
    // in is a test host. Routing it through a level-gated seam would only mean a
    // failing test could print nothing about why. It is ALSO this pin's positive
    // control -- see TheTestBundleExemption_IsRealAndStillHoldsNSLog -- which is
    // why the roster has NAMED SETS rather than one flat list: "excluded" and
    // "consumed as the fixed point the detector must still hit" are opposite
    // positions, and a flat list cannot tell them apart.

    /// <summary>The exempt roots, as one string for a failure message.</summary>
    private static string BnHostTests => string.Join(
        " + ", ShellSourceRoots.SetsFor(nameof(NSLogDriftTests), "appleTestBundle"));

    /// <summary>The scanned roots, as one string for a failure message.</summary>
    private static string BnHost => string.Join(
        " + ", ShellSourceRoots.SetsFor(nameof(NSLogDriftTests), "appleShell"));

    /// <summary>The seam each swept file must still reach. Swift through
    /// <c>BnLog.</c>, Objective-C++ through the <c>@_cdecl</c> <c>BnLogC</c> shim,
    /// which is the only way that language can call it.</summary>
    private const string SeamCall = @"\bBnLog\.\w+\s*\(|\bBnLogC\s*\(|\bbn_log_\w+\s*\(";

    /// <summary>The file kinds under an Apple target that are source.</summary>
    private static readonly string[] AppleSource = [".swift", ".m", ".mm", ".h"];

    /// <summary>The exempt bundle is Swift only.</summary>
    private static readonly string[] SwiftSource = [".swift"];

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
        // ITS OWN SCAN, AND ITS OWN COVERAGE ASSERTION ON THAT SCAN'S RECORD.
        ShellSourceScan.Hit[] hits = ShellSourceScan
            .ForPattern(nameof(NSLogDriftTests), "appleShell", AppleSource, NSLogCall)
            .HitsCoveringEveryDeclaredRoot();

        Assert.True(hits.Length == 0,
            "BARE NSLog UNDER " + BnHost + " — it must go through BnLog.\n"
            + string.Join("\n", hits.Select(h => $"  {h.File}:{h.Line}  {h.Text}"))
            + "\n\nNSLog is unconditional (no level, never compiled out, so it ships in Release), "
            + "and — the half that level gating cannot fix — EVERY VALUE IT INTERPOLATES IS "
            + "PUBLIC in any log collected off the device. os.Logger's interpolation is private "
            + "by default; that is the information-disclosure half of #155.\n"
            + "Use BnLog.error/warn/info/debug/verbose(category, message) from Swift, or BnLogC "
            + "(BnLog.h) from Objective-C++. The category is the bracketed tag the message "
            + "already carried, without the brackets.\n"
            + "(The exempt XCTest bundle's sites stay and are exempt BY SET — that exemption is "
            + "about test output and buys no exemption here.)");
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
        var (hits, read) = ShellSourceScan
            .ForPattern(nameof(NSLogDriftTests), "appleShell", AppleSource, NSLogCall)
            .CoveringEveryDeclaredRoot();

        Assert.True(read.Length > 15,
            $"the shell scan READ only {read.Length} files — it is reading the wrong tree "
            + "and NoBareNSLog_SurvivesUnderBnHost is passing while blind. Fix the walk, do not "
            + "delete the pin.");

        var names = read.Select(f => f.Split('/')[^1]).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BnLog.swift", names);
        foreach (string swept in SweptFiles)
            Assert.Contains(swept, names);

        // THE EXEMPT BUNDLE MUST NOT BE INSIDE THE SCANNED SHELL. `appleShell` and
        // `appleTestBundle` are separate sets precisely so this pin can scan one and
        // use the other as its positive control; a root that nests them makes the
        // control part of the subject, and the bundle's deliberate NSLog sites become
        // offenders. The trailing separator is load-bearing — without it `BnHost`
        // prefix-matches `BnHostTests`.
        var bundles = ShellSourceRoots.SetsFor(nameof(NSLogDriftTests), "appleTestBundle")
            .Select(r => r.TrimEnd('/') + "/")
            .ToArray();
        var swallowed = read
            .Where(f => bundles.Any(b => f.StartsWith(b, StringComparison.Ordinal)))
            .ToList();

        Assert.True(swallowed.Count == 0,
            "THE EXEMPT TEST BUNDLE IS INSIDE THE SCANNED SHELL:\n"
            + string.Join("\n", swallowed.Select(f => "  " + f))
            + "\n\nRe-point the roster, not this assertion.");
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
        // THROUGH THE SAME EXTRACTOR the offender fact uses, so this exercises the
        // production matcher rather than restating it — and with its own coverage
        // assertion, so "the bundle held no NSLog" cannot mean "the bundle was never
        // read" (pin standard, Rules 3 and 8).
        var (hits, read) = ShellSourceScan
            .ForPattern(nameof(NSLogDriftTests), "appleTestBundle", SwiftSource, NSLogCall)
            .CoveringEveryDeclaredRoot();

        Assert.True(hits.Length > 0,
            $"the NSLog pattern matched NOTHING under {BnHostTests}, which is the one tree that "
            + "MUST still contain live NSLog calls (XCTest diagnostics, deliberately not swept). "
            + $"The scan READ {read.Length} files there, so the walk is fine and the "
            + "DETECTOR is what stopped working — either the bundle stopped using NSLog, in which "
            + "case this fixed point moved and should be re-pointed, or the pattern no longer "
            + "matches a real call, in which case NoBareNSLog_SurvivesUnderBnHost is holding "
            + "nothing.");
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
        var (hits, read) = ShellSourceScan
            .ForPattern(nameof(NSLogDriftTests), "appleShell", AppleSource, SeamCall)
            .CoveringEveryDeclaredRoot();

        var routed = hits.Select(h => h.File.Split('/')[^1]).ToHashSet(StringComparer.Ordinal);
        var everRead = read.Select(f => f.Split('/')[^1]).ToHashSet(StringComparer.Ordinal);

        // A NAME THE WALK CANNOT PRODUCE IS ITS OWN RED. The previous shape called
        // `.Single()` on a filtered walk, which threw rather than explaining; a
        // filtered-to-empty variant of the same shape would have iterated zero times.
        var missing = SweptFiles.Where(n => !everRead.Contains(n)).ToList();
        Assert.True(missing.Count == 0,
            "THESE SWEPT FILES WERE NEVER READ BY THE SCAN:\n"
            + string.Join("\n", missing.Select(n => "  " + n))
            + "\n\nThey are named in SweptFiles because they held 66 of Gate C's 78 sites. If one "
            + "was renamed or removed, edit SweptFiles deliberately.");

        var offenders = SweptFiles.Where(n => !routed.Contains(n)).ToList();
        Assert.True(offenders.Count == 0,
            "THE SWEEP WAS UNDONE BY DELETION, NOT BY MIGRATION. These files held NSLog sites "
            + "before Gate C and now reach no logging seam at all:\n"
            + string.Join("\n", offenders.Select(n => "  " + n))
            + "\n\n\'No bare NSLog\' is trivially satisfied by deleting every diagnostic, and that "
            + "is the opposite of what #155 asked for: the mapper's `ignored`/`skipped` lines are "
            + "the only record that a wire the app author wrote was silently dropped (design "
            + "§4.3). If a file genuinely no longer logs, remove it from SweptFiles "
            + "deliberately.");
    }

    // ── the scanner is `ShellSourceScan`; this pin has none of its own ───────
}
