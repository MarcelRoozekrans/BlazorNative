using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ShellFrameTableDriftTests — M6 final audit, finding F2.
//
// **"The same frames on both platforms" is not a nice property of M6. It IS M6.**
// It is DoD #2's entire sentence, DoD #4's and DoD #5's; it is the reason Yoga was
// chosen at all; it is the claim the whole two-shell architecture exists to make.
//
// And nothing checked it.
//
// The demo pages' frame tables were HAND-TRANSCRIBED LITERALS, sitting inside six
// device test files — `BnLayoutDemoAndroidTest.kt` / `BnLayoutDemoTests.swift`,
// `BnScrollDemo*`, `BnImageDemo*`. The audit compared every number and they matched.
// But they matched *by careful transcription*, not by an invariant: one shell's `90`
// becoming `100` while the other's stayed would have left both suites green, both
// platforms internally consistent, and the parity claim quietly false. It was the last
// cross-shell contract in this repo that nothing pinned — standing next to the style
// routing tables, the fixture pixel sizes, the 72-byte bridge struct and the Yoga
// version, every one of which IS pinned, all by a drift test, all in this lane.
//
// ── WHY IT LIVES HERE ────────────────────────────────────────────────────────
// `build-test` is the ONE required lane where Kotlin, Swift and .NET are all
// checkout-visible: the Android lane has no Xcode, the iOS lane has no Gradle, and
// neither can see the other's source. Same reason `ShellStyleTableDriftTests` lives
// here, and the same mechanism — parse the shells' sources as TEXT.
//
// ── WHY THE TESTS WERE RESTRUCTURED RATHER THAN JUST REGEX'D ─────────────────
// The obvious cheap fix is to regex the old `assertFrame("…", view, 0f, 300f, …)` call
// sites where they lay. It was tried on paper and rejected as **too brittle to be
// trustworthy**, which is worse than nothing — a drift test people stop believing is a
// drift test people delete:
//
//   · the frames were not literals at all in two of the three pages. They were
//     EXPRESSIONS over per-class constants — `ROW_H * i` on Kotlin against
//     `rowH * CGFloat(i)` on Swift, `hi + BAND_H` against `hi + bandH` — so a comparison
//     would have needed an identifier-normaliser, a loop unroller and a `CGFloat()`
//     stripper, each one a place to be quietly wrong;
//   · there was no key to pair a Kotlin assertion with its Swift twin except CALL ORDER,
//     because the message strings are multi-line concatenations carrying per-shell
//     interpolation (`${fixture.width}` vs `\(Int(fixture.size.width))`);
//   · and a parse target that is not the assertion can drift away from the assertion —
//     which is the exact class of bug this file exists to prevent, reintroduced by the
//     fix for it.
//
// So the shells now **DECLARE** their tables, once each, in a machine-readable file —
// `BnDemoFrameTables.kt` and `BnDemoFrameTables.swift` — and **the device tests consume
// that declaration** (`assertFrame(bnLayoutDemoFrames, "wrap 3", view)`; a missing key
// fails). The parse target IS the assertion. There is nowhere left to write a frame
// number that this test cannot see.
//
// ── THE GRAMMAR IT READS ─────────────────────────────────────────────────────
// A cell is a sum of terms; a term is a literal number, `wi`, `hi`, or `MEASURED`:
//
//     "[1] band I"           to bnRect(0f, hi, 300f, 20f),      // Kotlin
//     "[1] band I":             bnRect(0,  hi, 300,  20),       // Swift
//
// `wi`/`hi` are the intrinsic image fixture's natural pixel size, which BOTH shells read
// at run time off the DECODED bytes and NEITHER is allowed to write down (that is what
// pins "no downsampling" — see BnImageDemoTests' fixture-server drift pins). They are
// therefore compared AS SYMBOLS: this test proves both shells say `hi + 20` without ever
// learning what `hi` is. `MEASURED` is the same trick for a font metric — a dimension
// that is measured on one platform and pinned on the other is itself a drift, and it is
// caught.
//
// Sums are compared by VALUE, not by text, so `160 + hi` and `hi + 160` are equal. A
// spurious red over term order would be a red nobody trusts.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ShellFrameTableDriftTests
{
    private const string KotlinTables =
        "src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/BnDemoFrameTables.kt";

    private const string AppleTables =
        "src/BlazorNative.Apple/BnHostTests/BnDemoFrameTables.swift";

    private const string KotlinTestDir =
        "src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell";

    private const string AppleTestDir = "src/BlazorNative.Apple/BnHostTests";

    /// <summary>THE BASELINE — asserted, not observed (house style).
    ///
    /// Table name → (the accessor each shell's device tests call, the number of rows).
    /// Both halves bite:
    ///
    ///   - the **accessor** is checked to be REFERENCED from at least one other file in
    ///     that shell's test tree. A table nobody consumes is a table the drift test would
    ///     happily compare while neither device asserted a thing — the "structurally
    ///     unreachable assertion" the AGP 9 incident is named for, in miniature.
    ///   - the **row count** is checked, so a row deleted from BOTH shells (by someone
    ///     "fixing" a red drift test) still reddens something.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Accessor, int Rows)> Expected =
        new Dictionary<string, (string, int)>(StringComparer.Ordinal)
        {
            ["BnLayoutDemo"] = ("bnLayoutDemoFrames", 15),
            ["BnScrollDemo"] = ("bnScrollDemoFrames", 17),
            ["BnScrollDemo/Image"] = ("bnScrollDemoImageFrames", 1),
            ["BnImageDemo/Before"] = ("bnImageDemoBeforeFrames", 10),
            ["BnImageDemo/After"] = ("bnImageDemoAfterFrames", 10),
        };

    [Fact]
    public void TheFrameTables_AreExactlyTheExpectedFive_OnBothShells()
    {
        var kotlin = ParseTables(KotlinTables);
        var apple = ParseTables(AppleTables);

        Assert.True(
            kotlin.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Expected.Keys),
            "BnDemoFrameTables.kt declares the wrong set of tables.\n"
            + $"  expected : {Join(Expected.Keys)}\n"
            + $"  declared : {Join(kotlin.Keys)}\n"
            + "This list is the baseline. A table that quietly disappears takes its whole "
            + "page's parity contract with it, and every other assertion here would still pass.");

        Assert.True(
            apple.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Expected.Keys),
            "BnDemoFrameTables.swift declares the wrong set of tables.\n"
            + $"  expected : {Join(Expected.Keys)}\n"
            + $"  declared : {Join(apple.Keys)}\n"
            + "A page whose table exists on ONE shell is a page whose frames nothing compares.");
    }

    [Fact]
    public void EachFrameTable_NamesTheSameNodes_OnBothShells()
    {
        var kotlin = ParseTables(KotlinTables);
        var apple = ParseTables(AppleTables);

        foreach (var table in Expected.Keys)
        {
            var k = kotlin[table].Keys.ToHashSet(StringComparer.Ordinal);
            var a = apple[table].Keys.ToHashSet(StringComparer.Ordinal);

            Assert.True(
                k.SetEquals(a),
                $"the two shells' `{table}` tables do not describe the same nodes.\n"
                + $"  only in Kotlin : {Join(k.Except(a))}\n"
                + $"  only in Swift  : {Join(a.Except(k))}\n"
                + "A node one shell pins and the other does not is a frame that is asserted on "
                + "ONE platform. Add it to both, in the same commit.");
        }
    }

    /// <summary>**THE DRIFT PIN.** The two shells' declarations must be equal, cell for cell.
    ///
    /// This is the one that catches the failure F2 named: a number moves on one shell and
    /// not the other. Both device suites stay green; both platforms stay internally
    /// consistent; the parity claim — which is the whole architecture — becomes quietly
    /// false, and NO SINGLE-DEVICE TEST IN EITHER SUITE CAN SEE IT.</summary>
    [Fact]
    public void EachFrameTable_IsIdenticalNumberForNumber_OnBothShells()
    {
        var kotlin = ParseTables(KotlinTables);
        var apple = ParseTables(AppleTables);
        var drift = new StringBuilder();

        foreach (var table in Expected.Keys)
        {
            var k = kotlin[table];
            var a = apple[table];

            foreach (var key in k.Keys.Where(a.ContainsKey).OrderBy(s => s, StringComparer.Ordinal))
            {
                if (k[key] != a[key])
                    drift.Append($"\n  {table} → \"{key}\"\n      Kotlin: {k[key]}\n      Swift : {a[key]}");
            }
        }

        Assert.True(
            drift.Length == 0,
            "THE TWO SHELLS DISAGREE ABOUT A FRAME." + drift + "\n\n"
            + "\"The same frames on both platforms\" is M6's entire architectural claim — DoD #2, "
            + "#4 and #5 are all that one sentence — and this is the ONLY test in the repo that "
            + "checks it. Each device suite asserts against its OWN shell's table, so BOTH stay "
            + "green while the two platforms lay out differently.\n"
            + "If the frames genuinely changed, change them in BnDemoFrameTables.kt AND "
            + "BnDemoFrameTables.swift, in the same commit — and expect both device lanes to "
            + "confirm it. (`wi`/`hi` are symbols, deliberately: neither shell may write the "
            + "fixture's pixel size down. `MEASURED` is a font metric, asserted by oracle, "
            + "never as a number.)");
    }

    [Fact]
    public void EachFrameTable_HasItsPinnedRowCount_OnBothShells()
    {
        var kotlin = ParseTables(KotlinTables);
        var apple = ParseTables(AppleTables);

        foreach (var (table, (_, rows)) in Expected)
        {
            Assert.True(
                kotlin[table].Count == rows,
                $"BnDemoFrameTables.kt's `{table}` has {kotlin[table].Count} rows; the baseline is "
                + $"{rows}. Baselines are asserted, not observed: a row deleted from BOTH shells "
                + "satisfies every other test in this file, so this is what stops a red drift test "
                + "from being 'fixed' by deleting the row that reddened it. Move the baseline "
                + "deliberately.");

            Assert.True(
                apple[table].Count == rows,
                $"BnDemoFrameTables.swift's `{table}` has {apple[table].Count} rows; the baseline "
                + $"is {rows}. See the Kotlin message above — the same rule.");
        }
    }

    /// <summary>A DECLARED TABLE THAT NO DEVICE TEST READS IS A CONTRACT NOTHING ASSERTS —
    /// and it would pass every other test in this file.
    ///
    /// That is not a hypothetical failure mode in this repo; it is the one the M6 audit is
    /// *about*. `android-instrumented` asserted 111 passing tests while the entire Android
    /// shell went uncompiled: an assertion can be green and STRUCTURALLY UNREACHABLE. So the
    /// accessor each table is consumed through must actually appear somewhere in that shell's
    /// test tree, outside the declaration itself.
    ///
    /// It is a source-level check and honestly so: it proves the table is READ, not that every
    /// row of it is asserted. What makes the rows bite is the other end — `assertFrame(table,
    /// key, view)` is the only lookup path in either shell, and a key that is not in the table
    /// FAILS on the device rather than silently asserting nothing.</summary>
    [Fact]
    public void EveryFrameTable_IsConsumedByADeviceTest_OnBothShells()
    {
        foreach (var (table, (accessor, _)) in Expected)
        {
            AssertConsumed(table, accessor, KotlinTestDir, "*.kt", KotlinTables);
            AssertConsumed(table, accessor, AppleTestDir, "*.swift", AppleTables);
        }
    }

    private static void AssertConsumed(
        string table, string accessor, string testDir, string pattern, string declaringFile)
    {
        var declaring = Path.GetFullPath(Absolute(declaringFile));
        var consumers = Directory
            .EnumerateFiles(Absolute(testDir), pattern)
            .Where(f => !string.Equals(Path.GetFullPath(f), declaring, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains(accessor, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            consumers.Count > 0,
            $"the `{table}` frame table is DECLARED but no device test in {testDir} reads "
            + $"`{accessor}`. A table nobody consumes is a parity contract nothing asserts — and it "
            + "passes every other test in this file, exactly the way android-instrumented asserted "
            + "111 green tests over an Android shell that was not being compiled at all.");
    }

    // ── The parser ───────────────────────────────────────────────────────────

    /// <summary>One cell of a frame table: a sum of a constant and the two symbols the shells
    /// are not allowed to write down as numbers — plus the MEASURED marker, which is its own
    /// value and equal only to itself.</summary>
    private readonly record struct Cell(double Constant, int Wi, int Hi, bool Measured)
    {
        public override string ToString()
        {
            if (Measured) return "MEASURED";
            var parts = new List<string>();
            if (Constant != 0 || (Wi == 0 && Hi == 0))
                parts.Add(Constant.ToString("0.###", CultureInfo.InvariantCulture));
            if (Wi != 0) parts.Add(Wi == 1 ? "wi" : $"{Wi}*wi");
            if (Hi != 0) parts.Add(Hi == 1 ? "hi" : $"{Hi}*hi");
            return string.Join(" + ", parts);
        }
    }

    private readonly record struct Rect(Cell X, Cell Y, Cell W, Cell H)
    {
        public override string ToString() => $"({X}, {Y}, {W}, {H})";
    }

    /// <summary>`// BN-FRAME-TABLE &lt;name&gt;` … `// BN-FRAME-TABLE-END`.</summary>
    private static readonly Regex BlockPattern = new(
        @"(?m)^[ \t]*//[ \t]*BN-FRAME-TABLE[ \t]+(?<name>[^\s]+)[ \t]*\r?$(?<body>.*?)^[ \t]*//[ \t]*BN-FRAME-TABLE-END[ \t]*\r?$",
        RegexOptions.Singleline);

    /// <summary>One row, in EITHER language: Kotlin's `"k" to bnRect(…)` and Swift's
    /// `"k": bnRect(…)` differ only in the separator, which is the entire point of writing
    /// them that way.</summary>
    private static readonly Regex RowPattern = new(
        @"""(?<key>[^""]+)""\s*(?:to\s+|:\s*)bnRect\(\s*(?<x>[^,]+?)\s*,\s*(?<y>[^,]+?)\s*,\s*(?<w>[^,]+?)\s*,\s*(?<h>[^)]+?)\s*\)");

    private static Dictionary<string, Dictionary<string, Rect>> ParseTables(string relativePath)
    {
        var source = ReadShellSource(relativePath);

        // Fail loudly on a half-written block: an opening marker whose END is missing would
        // otherwise swallow the rest of the file (or vanish), and a table that silently
        // disappears is exactly what this file exists to catch.
        var opens = Regex.Matches(source, @"(?m)^[ \t]*//[ \t]*BN-FRAME-TABLE[ \t]+[^\s]").Count;
        var blocks = BlockPattern.Matches(source);
        Assert.True(
            blocks.Count == opens,
            $"{relativePath}: {opens} `BN-FRAME-TABLE` markers but {blocks.Count} complete blocks — "
            + "one is missing its `// BN-FRAME-TABLE-END`. The markers ARE the contract's boundary; "
            + "an unterminated one is a table this drift test cannot see.");
        Assert.True(blocks.Count > 0, $"{relativePath}: no BN-FRAME-TABLE block found at all.");

        var tables = new Dictionary<string, Dictionary<string, Rect>>(StringComparer.Ordinal);
        foreach (Match block in blocks)
        {
            var name = block.Groups["name"].Value;
            Assert.False(tables.ContainsKey(name), $"{relativePath}: duplicate frame table `{name}`.");

            var rows = new Dictionary<string, Rect>(StringComparer.Ordinal);
            foreach (Match row in RowPattern.Matches(block.Groups["body"].Value))
            {
                var key = row.Groups["key"].Value;
                Assert.False(rows.ContainsKey(key),
                    $"{relativePath}: duplicate key \"{key}\" in `{name}` — one of the two rows is "
                    + "dead, and this test would be comparing a table the device does not assert.");

                rows[key] = new Rect(
                    ParseCell(relativePath, name, key, "x", row.Groups["x"].Value),
                    ParseCell(relativePath, name, key, "y", row.Groups["y"].Value),
                    ParseCell(relativePath, name, key, "w", row.Groups["w"].Value),
                    ParseCell(relativePath, name, key, "h", row.Groups["h"].Value));
            }

            Assert.True(rows.Count > 0,
                $"{relativePath}: frame table `{name}` parsed to ZERO rows. The declaration's shape "
                + "changed and this parser did not — re-point it deliberately rather than letting an "
                + "empty table compare equal to another empty table.");

            tables[name] = rows;
        }

        return tables;
    }

    /// <summary>A cell: `term (+ term)*`, term ∈ { number, `wi`, `hi`, `MEASURED` }. Nothing
    /// else parses — a named constant or an arithmetic expression is REJECTED, loudly, because
    /// a literal is the only thing two languages can be compared on and any indirection is
    /// somewhere a number could hide.</summary>
    private static Cell ParseCell(string file, string table, string key, string axis, string text)
    {
        double constant = 0;
        int wi = 0, hi = 0;
        var measured = false;

        foreach (var raw in text.Split('+'))
        {
            var term = raw.Trim();
            if (term.Length == 0) continue;

            if (term == "MEASURED") { measured = true; continue; }
            if (term is "wi") { wi++; continue; }
            if (term is "hi") { hi++; continue; }

            // Kotlin writes Float literals (`300f`); Swift writes CGFloat ones (`300`).
            var number = term.TrimEnd('f', 'F');
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                constant += value;
                continue;
            }

            Assert.Fail(
                $"{file}: `{table}` → \"{key}\".{axis} contains the term `{term}`, which the frame-table "
                + "grammar does not allow. A cell is a sum of literal numbers, `wi`, `hi` and `MEASURED` "
                + "— and only those. A named constant would let a frame number hide behind an identifier "
                + "on one shell and a different value on the other, which is precisely the drift this "
                + "test exists to catch.");
        }

        Assert.False(measured && (constant != 0 || wi != 0 || hi != 0),
            $"{file}: `{table}` → \"{key}\".{axis} mixes MEASURED with a number. MEASURED means 'this "
            + "dimension is a font metric, asserted by oracle and never as a number' — it is not a zero.");

        return new Cell(constant, wi, hi, measured);
    }

    private static string ReadShellSource(string relativePath)
    {
        var file = Absolute(relativePath);
        Assert.True(File.Exists(file), $"shell source not found: {file}");
        return File.ReadAllText(file);
    }

    private static string Absolute(string relativePath) =>
        Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The repo root — the nearest ancestor of the test binary holding
    /// BlazorNative.sln. The shells' sources are not build inputs of this project, so they are
    /// read from the checkout (which is what makes `build-test` the only lane that can host
    /// this test).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Join(IEnumerable<string> names)
    {
        var list = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }
}
