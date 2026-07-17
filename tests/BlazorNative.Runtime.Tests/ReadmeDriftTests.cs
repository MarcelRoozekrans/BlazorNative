using System.Text.RegularExpressions;

namespace BlazorNative.Runtime.Tests;

/// <summary>
/// PHASE 8.5 (M8 DoD #6) — the README's copies of numbers a GATE owns.
///
/// THE SENTENCE THIS PIN EXISTS FOR, and the README wrote it about itself:
///
///     "The gate is the truth; this table is a copy of it. When the two disagree,
///      the workflow is right — and they have disagreed before: for four
///      milestones this table read 333 / 83 / 111 / 72 while the gates asserted
///      otherwise, and not one of the four was within 50% of reality."
///
/// Four numbers, wrong by up to 5x, on the front page of a public repo, for four
/// milestones — because **nothing re-runs a number on a page**. The README named
/// its own remedy ("a single cheap CI read away from being held by a gate instead
/// of by someone remembering") and ledgered it 8.3 → 8.4 → here. This is that read.
///
/// THE RULE (8.4's one-home rule, applied to the README): every fact is GENERATED,
/// LINKED, or OWNED — *"kept in sync" is not a home*. These copies cannot be
/// deleted (a reader wants the counts on the front page) and cannot be generated
/// (the README is hand-written prose), so they become **LINKED by a gate**: the
/// number stays in the file, and this test is what keeps it true.
///
/// BOTH SIDES ARE DERIVED. The expectation is parsed from the workflow's actual
/// `if` condition — **the line that DECIDES**, not the step's `name:` prose, which
/// is itself a copy that has drifted before (ci.yml's own header "read 92 for four
/// milestones"). The subjects are parsed from the README. Not one number below is
/// transcribed into this file.
/// </summary>
public sealed class ReadmeDriftTests
{
    /// <summary>The count bars, and the gate that decides each. The regex targets
    /// the workflow's `if` condition — the code, not the step name. Each is
    /// verified to match EXACTLY ONCE in its file, so a second assertion appearing
    /// cannot be silently ignored (an ambiguous parse is a blind parse).
    ///
    /// EVERY PATTERN IS ANCHORED AT `^\s*if [(\[]` — the STATEMENT, not the text.
    /// It has to be: this test's own provenance block in ci.yml quotes the string
    /// `$passed -ne 577` while explaining the mutation, and an unanchored pattern
    /// matched the COMMENT as a second gate and reported "found 2". The pin was
    /// right — two matches means it cannot know which one decides — so the fix is
    /// to read only lines that are code. A YAML comment cannot satisfy `^\s*if`.
    ///
    /// ⚠ THIS IS A ROSTER, and 8.3's I-1 is the reason it does not stop there: a
    /// roster that does not know how many rows it SHOULD have is a pin that passes
    /// while blind. <see cref="ReadmeCountTable_PinsEveryRowItLists"/> derives the
    /// README's rows and compares the two SETS both directions, so a fifth bar
    /// added to the table must join this roster on purpose.</summary>
    private static readonly (string Surface, string Workflow, string GateRegex)[] CountBars =
    [
        (".NET", "ci.yml", @"(?m)^\s*if \(.*\$passed\s+-ne\s+(?<n>\d+)"),
        ("JVM", "ci.yml", @"(?m)^\s*if \(.*\$tests\s+-ne\s+(?<n>\d+)"),
        ("Android (instrumented, AVD)", "android-instrumented.yml", @"(?m)^\s*if \[.*""\$tests""\s+-ne\s+(?<n>\d+)"),
        ("iOS (XCTest, simulator)", "ios.yml", @"(?m)^\s*if \[.*""\$passed""\s+-ne\s+(?<n>\d+)"),
    ];

    // ── 1. The four counts == the four gates ─────────────────────────────────

    /// <summary>Every count the README's table states equals the number the
    /// workflow actually asserts. The README's row is matched by its Surface cell's
    /// PREFIX (the cell carries parenthetical detail: "JVM (JNA + win-x64 .dll)"),
    /// and the count cell's FIRST integer is the bar (".NET" reads "577 passed /
    /// 0 skipped" — the 0 is not a bar).</summary>
    [Fact]
    public void ReadmeCounts_MatchTheGatesThatAssertThem()
    {
        Dictionary<string, int> readme = ParseReadmeCountTable();
        var drift = new List<string>();

        foreach ((string surface, string workflow, string gateRegex) in CountBars)
        {
            int gate = ParseGateAssertion(workflow, gateRegex);
            string key = MatchReadmeRow(readme.Keys, surface);
            if (readme[key] != gate)
                drift.Add($"  {surface}: README says {readme[key]}, {workflow}'s gate asserts {gate}");
        }

        Assert.True(drift.Count == 0,
            "README COUNT DRIFT — the front page states a number no longer true.\n"
            + string.Join("\n", drift)
            + "\n\nThe GATE is the truth; the README's table is a copy of it. This table read "
            + "333 / 83 / 111 / 72 for four milestones because nothing re-ran a number on a page. "
            + "Update README.md's count table to match the workflows — or, if the baseline moved "
            + "deliberately, update both together.");
    }

    /// <summary>THE ROSTER KNOWS ITS SIZE (8.3 I-1). The README's rows and this
    /// file's roster are compared as SETS, both directions: a bar added to the
    /// table without a gate here reds, and a roster entry whose row was deleted
    /// reds too. Without this, <see cref="ReadmeCounts_MatchTheGatesThatAssertThem"/>
    /// would silently ignore a fifth, unpinned number.</summary>
    [Fact]
    public void ReadmeCountTable_PinsEveryRowItLists()
    {
        Dictionary<string, int> readme = ParseReadmeCountTable();

        var unpinned = readme.Keys
            .Where(row => !CountBars.Any(b => row.StartsWith(b.Surface, StringComparison.Ordinal)))
            .ToList();
        var missing = CountBars
            .Where(b => !readme.Keys.Any(row => row.StartsWith(b.Surface, StringComparison.Ordinal)))
            .Select(b => b.Surface)
            .ToList();

        Assert.True(unpinned.Count == 0 && missing.Count == 0,
            "README COUNT-TABLE ROSTER DRIFT.\n"
            + (unpinned.Count > 0
                ? $"  Rows in the table that NO gate here pins: {string.Join(", ", unpinned)}\n"
                  + "  A number on the front page that no gate holds is exactly what this pin exists "
                  + "to prevent. Add it to CountBars with the workflow line that decides it.\n"
                : "")
            + (missing.Count > 0
                ? $"  Roster entries with no row in the table: {string.Join(", ", missing)}\n"
                  + "  The table moved or a row was deleted; the pin is now reading nothing for it.\n"
                : ""));
    }

    // ── 2. The Yoga literal == the Gradle pin ────────────────────────────────

    /// <summary>Every "Yoga &lt;semver&gt;" the README states equals the version
    /// the Android shell actually builds. ONE ENGINE, and by Phase 8.3 it had FOUR
    /// pinned homes (build.gradle.kts, ios.yml, ci.yml's own env, the template's
    /// build.gradle.kts) — all four held by ci.yml's Yoga-parity step. The README
    /// carried a fifth copy that nothing read.
    ///
    /// SUBJECTS DERIVED, NOT ROSTERED: this matches the PATTERN, so a sixth copy
    /// pasted into the README tomorrow is held the day it lands — no list to
    /// remember. (Phase 8.4's Gate 3 author wrote a fresh copy of this very number
    /// while removing another one; the pull toward a new copy is not theoretical.)
    ///
    /// WHY THE HISTORY ROW IS NOT A SUBJECT: the "Milestone 6" checklist row read
    /// "Yoga 3.2.1 linked into both shells — Phase 6.0". Holding it would force a
    /// FALSEHOOD on the next bump — Phase 6.0 linked 3.2.1 and always will have.
    /// Its version literal was deleted at 8.5 instead: the row's subject is the
    /// linking, and the version's home is the pin. *A pin that can only be
    /// satisfied by writing something untrue is the wrong pin.*</summary>
    [Fact]
    public void ReadmeYogaLiterals_MatchTheGradlePin()
    {
        string gradle = ReadCheckoutFile(Path.Combine("src", "BlazorNative.Jni", "build.gradle.kts"));
        Match pin = Regex.Match(gradle, @"^\s*implementation\(""com\.facebook\.yoga:yoga:(?<v>[^""]+)""\)",
            RegexOptions.Multiline);
        Assert.True(pin.Success,
            "Could not find the com.facebook.yoga:yoga pin in build.gradle.kts — did the "
            + "declaration move? A pin that cannot see the version it compares against is blind. "
            + "(ci.yml's Yoga-parity step reads this same line and would red for the same reason.)");
        string expected = pin.Groups["v"].Value.Trim();

        string readme = ReadCheckoutFile("README.md");
        var found = Regex.Matches(readme, @"Yoga\s+(?<v>\d+\.\d+\.\d+)")
            .Select(m => m.Groups["v"].Value)
            .ToList();

        // NON-VACUITY (8.1's `return ,$names`, 8.4's "10 succeeded"): a pattern that
        // matches nothing passes this test forever while holding nothing at all.
        Assert.True(found.Count > 0,
            "The README states NO 'Yoga <version>' literal — so this pin now holds NOTHING and "
            + "would pass forever. Either the architecture diagram's version was removed (then "
            + "delete this pin, deliberately) or the prose was reworded past the pattern (then "
            + "fix the pattern). A pin whose subject vanished must say so, not go quietly green.");

        var wrong = found.Where(v => v != expected).ToList();
        Assert.True(wrong.Count == 0,
            $"README YOGA DRIFT: the README states Yoga {string.Join(" / ", wrong.Distinct())}, "
            + $"but src/BlazorNative.Jni/build.gradle.kts pins {expected} — which is what both "
            + "shells and a generated app actually build. One engine; the pin is the home. "
            + "(This copy is why the item was ledgered 8.3 → 8.4 → 8.5: it is the only Yoga "
            + "literal in the repo that ci.yml's four-way parity step does not read.)");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Parses the README's count table, anchored on its header row so no
    /// other table in the file can be read by accident. Returns Surface → the
    /// first integer in the Count cell.</summary>
    private static Dictionary<string, int> ParseReadmeCountTable()
    {
        string readme = ReadCheckoutFile("README.md");

        Match header = Regex.Match(readme,
            @"^\|\s*Surface\s*\|\s*Command\s*\|\s*Count\s*\|\s*Asserted by\s*\|\s*$",
            RegexOptions.Multiline);
        Assert.True(header.Success,
            "Could not find the README's count table header "
            + "(| Surface | Command | Count | Asserted by |) — did the table move or get "
            + "reworded? This pin is now reading nothing; fix the anchor rather than deleting "
            + "the test.");

        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string line in readme[header.Index..].Split('\n').Skip(1))
        {
            string t = line.Trim();
            if (t.Length == 0 || !t.StartsWith('|')) break;      // the table ended
            if (Regex.IsMatch(t, @"^\|[\s\-|:]+\|$")) continue;  // the |---|---| separator

            Match row = Regex.Match(t, @"^\|(?<surface>[^|]+)\|(?<cmd>[^|]+)\|(?<count>[^|]+)\|");
            if (!row.Success) break;

            Match n = Regex.Match(row.Groups["count"].Value, @"\d+");
            if (!n.Success) continue;   // a Count cell with no number is not a bar
            rows[row.Groups["surface"].Value.Trim()] = int.Parse(n.Value);
        }

        Assert.True(rows.Count > 0,
            "The README's count table parsed to ZERO rows — the header matched but the rows did "
            + "not. A pin that reads nothing passes while blind (8.4's '10 succeeded, 0 failed').");
        return rows;
    }

    /// <summary>Reads the number a workflow's `if` condition actually compares
    /// against, and asserts the parse matched EXACTLY ONCE — an ambiguous match is
    /// a blind one.</summary>
    private static int ParseGateAssertion(string workflow, string gateRegex)
    {
        string yaml = ReadCheckoutFile(Path.Combine(".github", "workflows", workflow));
        MatchCollection m = Regex.Matches(yaml, gateRegex);

        Assert.True(m.Count == 1,
            $"Expected exactly ONE gate assertion matching /{gateRegex}/ in {workflow}, found "
            + $"{m.Count}. Zero means the assertion moved or was reworded and this pin is reading "
            + "nothing; more than one means the parse is ambiguous and could be reading the wrong "
            + "gate. Either way the number below is not evidence — fix the pattern.");

        return int.Parse(m[0].Groups["n"].Value);
    }

    private static string MatchReadmeRow(IEnumerable<string> rows, string surfacePrefix)
    {
        string? key = rows.FirstOrDefault(r => r.StartsWith(surfacePrefix, StringComparison.Ordinal));
        Assert.True(key is not null,
            $"No README count-table row starts with '{surfacePrefix}'. "
            + $"{nameof(ReadmeCountTable_PinsEveryRowItLists)} explains which side moved.");
        return key!;
    }

    /// <summary>The README and the workflows are not build inputs of this project,
    /// so they are read from the checkout — which is what makes `build-test` the
    /// only lane that can host this test (ShellStyleTableDriftTests' rule).</summary>
    private static string ReadCheckoutFile(string relativePath)
    {
        string file = Path.Combine(RepoRoot(), relativePath);
        Assert.True(File.Exists(file), $"checkout file not found: {file}");
        return File.ReadAllText(file);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
