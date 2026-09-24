using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// THE PIN POPULATION (phase 15.0).
//
// A pin is not defined by its NAME. `Drift`, `Pin`, `Sweep` and `Roster` are all
// in use as suffixes, so a convention test keyed on one would miss a quarter of
// the population -- this milestone's own bug class, inside the mechanism meant
// to close it.
//
// A pin is defined by what it DOES: it reads the repository tree at runtime and
// asserts on the contents. Every such test must reach the tree through
// BnRepo.Root, because that is what makes the population enumerable. A test that
// walks to the solution file itself is invisible to enforcement, and this test
// is what notices.
//
// WHY THE SCAN IS OVER CODE, NOT FILE TEXT. The property being asserted is a
// property of the CODE a test runs, and this repo documents its pins at length:
// three drift pins describe their own mechanism as "walking up from the test
// binary to the directory holding BlazorNative.sln", which is an accurate
// sentence about what BnRepo.Root does on their behalf. Matching raw file text
// would red all three for writing that sentence down -- a pin that fires on
// prose teaches its readers to stop writing prose, or to weaken the pin. So the
// scan runs over `CommentStrippedSource`, the shared stripper two sibling pins
// (GeneratedSymbolShadowTests, DispatchSurfaceDriftTests) already scan through
// for the same reason. No new infrastructure, and no marker is exempted.
//
// ── WHAT THIS PIN DOES NOT COVER (pin standard, Rule 5) ─────────────────────
//
// Phase 15.1. These limits were disclosed in docs/pin-standard.md and in a task
// report, and nowhere near the guard itself. Rule 5 is specifically about a
// reader finding the limit where they find the pin, so they live here now.
//
// READ THE DIRECTION FIRST, because it is the part that matters: ALL FOUR OF
// THESE FAIL GREEN. By the standard's own test -- "a limit that can only cost
// you a red is a footnote; a limit that can hand you a green is a defect,
// whether or not it is written down" -- every one of them is a defect this pin
// cannot close, not a footnote. They are written down because the alternative
// is a guard that implies completeness it does not have, which the standard
// rates as worse than an admitted gap.
//
//  1. A MARKER BUILT BY STRING CONCATENATION EVADES THE SCAN. The detector is
//     `code.Contains(marker)` over one file's stripped text. A test that spells
//     the sentinel as two halves joined at runtime contains neither marker as a
//     substring and is invisible. This is the accidental/determined boundary:
//     what this pin catches is the COPY-PASTED walk, which is how all 24 copies
//     phase 15.0 consolidated actually arose.
//
//  2. A BYPASS CAN REACH THE TREE WITH NEITHER MARKER PRESENT, and this is
//     DEMONSTRATED rather than theoretical. `Assembly.GetExecutingAssembly()
//     .Location` plus a fixed `Path.GetFullPath` climb lands in the checkout
//     without naming the solution file and without touching AppContext. The
//     marker list is a list of the two EASY doors, not of every door -- see
//     limit 4 on why widening it is not a fix on its own.
//
//  3. THE TWO BY-NAME EXCLUSIONS ARE BLIND SPOTS, not merely exemptions. A
//     bypass written INSIDE BnRepo.cs or inside this file is never read by the
//     scan. The census calls the plausible half of that: BnRepo.cs could grow a
//     THIRD walk beside Root and TestBinaryDirectory, with a slightly different
//     sentinel or climb -- two copies of one truth inside the file whose whole
//     purpose is that there be one, in the one location nothing scans. Both
//     exclusions are correct and neither should be removed; what is missing is
//     a guard, and BnRepo.cs is 51 lines reviewed by eye.
//
//  4. `BypassMarkers` IS A MANUAL LIST AND NOTHING GUARDS ITS WIDTH. The
//     positive control below proves each DECLARED marker still matches real
//     code. It cannot prove the list is still COMPLETE: delete an entry and the
//     control simply stops asking about it. Completeness is a fact about the
//     .NET API surface rather than about this repo, so no assertion in this
//     repository can establish it -- which is exactly why the list is described
//     below as the one manual seam that cannot be removed.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PinPopulationTests
{
    /// <summary>Every way a test could reach the tree without the shared helper.
    /// This list is the one manual seam and cannot be removed: it is a fact about
    /// the .NET API surface, not about this repo.</summary>
    private static readonly string[] BypassMarkers =
    [
        "BlazorNative.sln",       // the sentinel — walking to it by hand
        "AppContext.BaseDirectory",
    ];

    /// <summary>THE DETECTOR — one implementation, driven by the pin AND by its
    /// positive control (pin standard Rule 8). A control that reran its own copy
    /// of `Contains` would control the copy; the point is that both facts push the
    /// same marker list through the same stripper.</summary>
    private static IReadOnlyList<string> MarkersIn(string path)
    {
        string code = string.Join('\n', CommentStrippedSource.Lines(path));
        return [.. BypassMarkers.Where(m => code.Contains(m, StringComparison.Ordinal))];
    }

    [Fact]
    public void NoTest_ReachesTheRepoTree_WithoutTheSharedHelper()
    {
        string root = BnRepo.Root();
        string testsDir = Path.Combine(root, "tests");
        Assert.True(Directory.Exists(testsDir), $"tests/ not found under {root}");

        var offenders = new List<string>();
        int scanned = 0;

        foreach (string path in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            // Build output is not a test. `obj/` also makes the count depend on
            // whether the tree has been built, and a pin whose anti-vacuity floor
            // moves with build state is a pin that can be argued with.
            if (relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal)) continue;

            // Two files, named individually and never as a pattern. BnRepo.cs is
            // allowed to do exactly this — it is the one implementation everything
            // else routes through. This file is allowed to SPELL the markers,
            // because it is where they are declared; nothing can scan for a string
            // it is forbidden to contain.
            //
            // STILL TWO, deliberately, after 15.1 moved CommentStrippedSource.cs into
            // tests/Shared beside BnRepo.cs. Living in that directory is not the
            // qualification — doing the walk is. The stripper never reaches the tree:
            // it is handed a path by its caller and reads it. It carries no bypass
            // marker in live code, so it is SCANNED like any other test file, and it
            // must be. Every by-name exclusion is a blind spot the scan can never read
            // again, so an exclusion added for tidiness costs coverage and buys
            // nothing. Do not grow this list because a file moved next door.
            string name = Path.GetFileName(path);
            if (name == "BnRepo.cs" || name == "PinPopulationTests.cs") continue;

            scanned++;
            foreach (string marker in MarkersIn(path))
                offenders.Add($"{relative} uses '{marker}'");
        }

        // ANTI-VACUITY: the standard this milestone is writing, applied to the
        // test writing it. If the walk finds no files, the loop above asserts
        // nothing and this test passes while checking nothing.
        //
        // THE FLOOR IS NOT ARBITRARY, and a low one is nearly useless. This scan
        // sees 131 files today: 133 hand-written .cs files under tests/, less the
        // two named above. It was 129 of 131 until 15.2 added two test files. The "~151" the plan quoted counted bin/ and obj/, which
        // this scan skips -- the same counting-the-wrong-thing mistake the
        // population itself has suffered three times. A floor of 20 would pass
        // while the walk saw 15% of its subject -- a guard weak enough to be
        // theatre, which is precisely what this milestone exists to remove. 100
        // leaves 31 files of headroom so ordinary deletion does not red it, and
        // sits far enough above zero that a broken walk cannot slip through.
        //
        // AND IT PROVES ONLY THE WALK. Whether the DETECTOR still detects is a
        // separate property, asserted separately below (pin standard Rule 3).
        Assert.True(scanned >= 100,
            $"scanned only {scanned} test files, and there are roughly 131 — the walk has stopped "
            + "seeing its subject, so the assertion below is checking almost nothing");

        Assert.True(offenders.Count == 0,
            "these tests reach the repo tree without BnRepo.Root, so they are invisible to pin "
            + "enforcement — route them through the shared helper:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>THE POSITIVE CONTROL for <see cref="BypassMarkers"/> (pin standard
    /// Rule 3; phase 15.0 census item 1 and §5.6).
    ///
    /// <para>WHY THIS PIN NEEDED ONE MOST, and why being the milestone's own
    /// enforcement mechanism makes that worse rather than exempt. The fact above is
    /// an ABSENCE claim: it asserts an empty offender list. Its floor of 100 proves
    /// the WALK found files; nothing proved the DETECTOR still detects. Reword either
    /// marker — or let the shared stripper start removing too much — and it reports
    /// no offenders, forever, while its name tells every reader that the population
    /// is enumerable. A guard that cannot demonstrate its own detector works is
    /// precisely what the standard objects to, and this one is the guard the standard
    /// is enforced BY.</para>
    ///
    /// <para>WHY `BnRepo.cs` IS THE FIXED POINT, and the relationship worth
    /// understanding rather than an arbitrary file to assert about. It is the ONE
    /// PERMITTED HOME of the walk, so by construction it must contain both markers in
    /// live code: <c>Root()</c> climbs to the solution-file sentinel, and
    /// <c>TestBinaryDirectory()</c> exists specifically to give the base-directory
    /// marker a sanctioned home rather than an exemption. If either marker stopped
    /// being found THERE, it would stop being found in an offender too.</para>
    ///
    /// <para>That is <c>NSLogDriftTests</c>' design, read in reverse: the file the
    /// offender scan EXCLUDES is the file the detector is required to hit. There, the
    /// exempt test bundle must still hold the `NSLog` calls the shipped tree must
    /// not; here, the exempt implementation must still hold the markers every other
    /// test must not. The exemption and the control are two different jobs, and until
    /// phase 15.1 only the first had been done.</para>
    ///
    /// <para>Read through <see cref="MarkersIn"/> — the pin's own detector, over the
    /// same comment-stripped source — so this controls the production path and not a
    /// copy of it. What it does NOT establish is the list's WIDTH; see limit 4 in the
    /// header.</para></summary>
    [Fact]
    public void TheBypassMarkers_AreStillFoundInBnRepo_TheOnePermittedHomeOfTheWalk()
    {
        string bnRepo = Path.Combine(BnRepo.Root(), "tests", "Shared", "BnRepo.cs");

        Assert.True(File.Exists(bnRepo),
            $"tests/Shared/BnRepo.cs is missing (looked in {bnRepo}) — it is both the one permitted "
            + "implementation of the walk and the fixed point this control depends on. Either it "
            + "moved, in which case re-point this control AND the by-name exclusion in the pin "
            + "above deliberately, or the shared helper is gone, in which case the whole population "
            + "has stopped being enumerable and that is the thing to fix.");

        var found = MarkersIn(bnRepo);
        string[] missing = [.. BypassMarkers.Except(found, StringComparer.Ordinal)];

        Assert.True(missing.Length == 0,
            $"the bypass marker(s) [{string.Join(", ", missing)}] matched NOTHING in "
            + "tests/Shared/BnRepo.cs, which is the one file that MUST contain every one of them: "
            + "it is the only sanctioned home of the walk, so Root() spells the solution-file "
            + "sentinel and TestBinaryDirectory() spells the base-directory marker, both in live "
            + "code. Either a marker was reworded past its subject, or the shared comment stripper "
            + "now removes code it should keep — and in EITHER case the pin above is reporting an "
            + "empty offender list because it can no longer see an offender, not because none "
            + "exists. Fix the detector; do not green this by editing the list.");
    }
}
