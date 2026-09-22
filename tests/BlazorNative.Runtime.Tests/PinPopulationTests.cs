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
            string name = Path.GetFileName(path);
            if (name == "BnRepo.cs" || name == "PinPopulationTests.cs") continue;

            scanned++;
            string code = string.Join('\n', CommentStrippedSource.Lines(path));
            foreach (string marker in BypassMarkers)
                if (code.Contains(marker, StringComparison.Ordinal))
                    offenders.Add($"{relative} uses '{marker}'");
        }

        // ANTI-VACUITY: the standard this milestone is writing, applied to the
        // test writing it. If the walk finds no files, the loop above asserts
        // nothing and this test passes while checking nothing.
        //
        // THE FLOOR IS NOT ARBITRARY, and a low one is nearly useless. This scan
        // sees 129 files today: 131 hand-written .cs files under tests/, less the
        // two named above. The "~151" the plan quoted counted bin/ and obj/, which
        // this scan skips -- the same counting-the-wrong-thing mistake the
        // population itself has suffered three times. A floor of 20 would pass
        // while the walk saw 15% of its subject -- a guard weak enough to be
        // theatre, which is precisely what this milestone exists to remove. 100
        // leaves 29 files of headroom so ordinary deletion does not red it, and
        // sits far enough above zero that a broken walk cannot slip through.
        Assert.True(scanned >= 100,
            $"scanned only {scanned} test files, and there are roughly 129 — the walk has stopped "
            + "seeing its subject, so the assertion below is checking almost nothing");

        Assert.True(offenders.Count == 0,
            "these tests reach the repo tree without BnRepo.Root, so they are invisible to pin "
            + "enforcement — route them through the shared helper:\n  " + string.Join("\n  ", offenders));
    }
}
