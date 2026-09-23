using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// THE SHELL SOURCE ROSTER (phase 15.2, #364 F1).
//
// #364's own conclusion is what this file is built from: five separate reviews
// defeated AuthSemanticsDriftTests, and EVERY ONE of them was a place the pin
// did not look -- never a thing it looked at and got wrong. So the fix is not
// another directory added to another list. It is to make "it does not look
// there" a RED.
//
// The divergence was between two PINS, not between two shells.
// AuthSemanticsDriftTests scanned `src/BlazorNative.Jni/src/androidMain` alone.
// AndroidLogDriftTests scanned `src/main/kotlin` AND `src/androidMain/kotlin`
// AND both template mirrors, quoting the Gradle `kotlin.srcDirs` line as its
// authority. Two pins, two different answers to one question -- what IS the
// Android shell's source tree -- and nothing in the repository compared them.
//
// src/shell-source-roots.json is the one home for that answer, and this file is
// what makes the roster binding. The contract is a PARTITION: every consumer
// accounts for every set exactly once, across `consumes` / `delegated` /
// `excluded`. Not a subset -- an overlap reds too, because a tree named in two
// lists means the reason a reviewer reads need not be the behaviour that runs.
//
// ── WHAT THIS FILE DOES NOT COVER (pin standard, Rule 5) ────────────────────
//
//  1. THE PARTITION ONLY BINDS TREES SOMEBODY LISTED, and that hole is closed
//     by a SECOND fact rather than by this one. The totality fact makes
//     "unmentioned BY A CONSUMER" impossible; it cannot make "absent from
//     `sets`" impossible, because `all` is built FROM the roster.
//     `EveryShellSourceFile_IsInsideADeclaredRoot` is the other direction: it
//     walks the `scanRoots` declared in the manifest and requires every `.kt`
//     and `.swift` file found there to sit inside some declared root. That is
//     what makes `src/BlazorNative.Jni/src/debug` -- no `.kt` today, only
//     `res/xml` -- safe to leave out of `sets` entirely: the day it gains
//     Kotlin, that fact reds.
//
//     WHAT REMAINS, one level further out: a shell tree outside every
//     `scanRoots` entry. `src/BlazorNative.Apple/vendor` is deliberately
//     ignored there, so a vendored Swift package is invisible to the check,
//     and a new shell living outside the three scanned containers would be
//     too. Direction: FAILS GREEN. It is narrower than the old residual by
//     the whole of `src/BlazorNative.Jni/src` and the template android tree,
//     and it is the honest remainder rather than a claim of completeness.
//
//  2. THE GRADLE PIN IS A REGEX OVER KOTLIN, NOT A PARSER -- and the natural
//     guess about which reformats hurt is WRONG, so read the measured list
//     rather than assuming. Splitting the ARGUMENT LIST across lines costs
//     nothing: the capture is `[^)]*`, which matches newlines, and it was run.
//     What reds is a RENAME of the call, a COMMENT-OUT of it, or the directory
//     list moving into a variable. All three are FALSE REDS -- Rule 5's
//     footnote direction -- and all three assertions say how to re-point.
//     A nested `)` inside the arguments would truncate the capture at the
//     first one; in every plausible spelling the quoted strings still extract
//     correctly, so that is a note rather than a hole.
//
//  3. THE ROSTER SAYS NOTHING ABOUT WHAT A CONSUMER ACTUALLY SCANS. It records
//     what each pin CLAIMS. The claim only becomes binding when that pin reads
//     its roots from `ShellSourceRoots.SetsFor`, which is task 3's job. Until
//     then a consumer entry is documentation. Direction: FAILS GREEN, and it
//     is the reason task 3 is not optional.
//
//  4. A NAMED TEST IS CHECKED ONLY TO THE DEPTH OF "IT EXISTS". Both
//     `EveryDelegation_NamesAGuardThatExists` and
//     `EveryTestNamedInAReason_Exists` resolve a name against the declared
//     test methods under tests/. Neither can tell whether the named test
//     asserts anything useful about the tree it is cited for; a method by that
//     name with an empty body satisfies both. Direction: FAILS GREEN. What
//     bounds it is that a guard is written once, by hand, beside the claim it
//     serves.
//
//     And `EveryTestNamedInAReason_Exists` only sees names in a test-method
//     SHAPE -- PascalCase_PascalCase. A reason that cites a test in prose
//     ("the credential branch pin") makes an unpinned claim this cannot see.
//     Direction: FAILS GREEN, and the fix is to write the identifier.
//
//  5. AN AGGREGATE OVER A PARTITION SAYS NOTHING ABOUT ANY MEMBER. This is
//     written as a limit because this file already got it wrong once, in
//     review: `EveryRoot_ExistsOnDisk` floored the TOTAL root count against
//     the SET count and its message claimed that proved no set had an empty
//     `roots` array. It did not -- 9 roots over 7 sets absorbs one emptied
//     array without the total dropping below 7, and emptying `appleShell`
//     left all five facts green while two pins would have stopped scanning
//     the entire iOS shell. The per-set assertion is what delivers the
//     property; the total is only a total. Do not re-derive this with a
//     different aggregate.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>THE ONE PARSE of src/shell-source-roots.json. Lives here rather than
/// beside any one consumer so the three consuming pins -- AuthSemanticsDriftTests,
/// AndroidLogDriftTests, NSLogDriftTests -- call it instead of growing a second
/// copy of the parse, which is the precondition for exactly the twin divergence
/// this roster exists to close (pin standard, Rule 8).</summary>
internal static class ShellSourceRoots
{
    /// <summary>The manifest path, repo-relative. Named once so a failure message
    /// and the reader are looking at the same string.</summary>
    internal const string ManifestPath = "src/shell-source-roots.json";

    /// <summary>A named shell source tree. <paramref name="DerivedFrom"/> is
    /// present only on sets whose roots are a copy of something the BUILD
    /// declares -- see <see cref="GradleSource"/>.</summary>
    internal sealed record SetDef(
        string Name, string Language, string Purpose, string[] Roots, GradleSource? DerivedFrom);

    /// <summary>Where a set's roots really come from: a call in a build script
    /// whose quoted arguments, prefixed with <paramref name="PathPrefix"/>, must
    /// equal the set's roots.</summary>
    internal sealed record GradleSource(string File, string Call, string PathPrefix);

    /// <summary>A tree a consumer does not scan itself because another test
    /// already covers it. <paramref name="Guard"/> is the test that asserts the
    /// delegate still does -- without one this is an exclusion wearing a better
    /// word, which is why the field is read as optional and then asserted.</summary>
    internal sealed record Delegation(string Set, string To, string Guard, string Reason);

    /// <summary>A pin that scans shell source, and the three lists that must
    /// partition the roster for it.</summary>
    internal sealed record Consumer(
        string Name,
        string[] Consumes,
        IReadOnlyDictionary<string, Delegation> Delegated,
        IReadOnlyDictionary<string, string> Excluded);

    /// <summary>A container the shells' source lives in, and the extensions that
    /// count as source there. Used ONLY by
    /// EveryShellSourceFile_IsInsideADeclaredRoot, to answer the question the
    /// roster cannot answer from its own contents: is there a source tree here
    /// that nobody declared?</summary>
    internal sealed record ScanRoot(string Path, string[] Extensions, string[] Ignore, string Why);

    private sealed record Roster(
        IReadOnlyDictionary<string, SetDef> Sets,
        IReadOnlyDictionary<string, Consumer> Consumers,
        ScanRoot[] ScanRoots);

    private static readonly Lazy<Roster> Parsed = new(Load);

    /// <summary>Every named source set, keyed by set name.</summary>
    internal static IReadOnlyDictionary<string, SetDef> Sets() => Parsed.Value.Sets;

    /// <summary>Every pin that declares a coverage position, keyed by test class
    /// name.</summary>
    internal static IReadOnlyDictionary<string, Consumer> Consumers() => Parsed.Value.Consumers;

    /// <summary>The containers walked when looking for source trees NOBODY
    /// declared.</summary>
    internal static ScanRoot[] ScanRoots() => Parsed.Value.ScanRoots;

    /// <summary>THE CONSUMER-FACING DOOR. The repo-relative roots a named pin
    /// CONSUMES -- the sets it scans itself, flattened. Delegated and excluded
    /// sets are deliberately absent: they are positions, not subjects.
    ///
    /// Throws rather than returning an empty array for anything it cannot
    /// resolve. A pin handed `[]` scans nothing and passes, which is the vacuity
    /// this whole roster exists to prevent.</summary>
    internal static string[] SetsFor(string consumer)
    {
        if (!Consumers().TryGetValue(consumer, out Consumer? c))
            throw new InvalidOperationException(
                $"'{consumer}' is not a consumer in {ManifestPath} — the pin was renamed, or its "
                + "entry was never written. Add it, with its three lists, rather than reaching "
                + "past the roster: a pin whose coverage position is unrecorded is the #364 F1 "
                + "shape all over again.");

        var roots = new List<string>();
        foreach (string set in c.Consumes)
        {
            if (!Sets().TryGetValue(set, out SetDef? s))
                throw new InvalidOperationException(
                    $"'{consumer}' consumes '{set}', which is not a set in {ManifestPath}. A typo "
                    + "here silently drops a tree from the pin's subject.");
            roots.AddRange(s.Roots);
        }

        if (roots.Count == 0)
            throw new InvalidOperationException(
                $"'{consumer}' resolves to ZERO roots from {ManifestPath}. A pin with no subject "
                + "scans nothing and passes — fix the roster, do not let the caller proceed.");

        return [.. roots];
    }

    private static Roster Load()
    {
        string path = Path.Combine(
            BnRepo.Root(), ManifestPath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"{ManifestPath} is missing. It is the single home for what the shells' source "
                + "trees are; without it every consuming pin is back to its own private answer. "
                + "Restore it, or re-point this constant deliberately.");

        using JsonDocument doc = JsonDocument.Parse(
            File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var sets = new Dictionary<string, SetDef>(StringComparer.Ordinal);
        foreach (JsonProperty p in TopLevel(doc, "sets").EnumerateObject())
        {
            GradleSource? derived = null;
            if (p.Value.TryGetProperty("derivedFrom", out JsonElement d))
                derived = new GradleSource(
                    d.GetProperty("file").GetString()!,
                    d.GetProperty("call").GetString()!,
                    d.GetProperty("pathPrefix").GetString()!);

            sets[p.Name] = new SetDef(
                p.Name,
                p.Value.GetProperty("language").GetString()!,
                p.Value.GetProperty("purpose").GetString()!,
                [.. p.Value.GetProperty("roots").EnumerateArray().Select(r => r.GetString()!)],
                derived);
        }

        var consumers = new Dictionary<string, Consumer>(StringComparer.Ordinal);
        foreach (JsonProperty p in TopLevel(doc, "consumers").EnumerateObject())
        {
            var delegated = new Dictionary<string, Delegation>(StringComparer.Ordinal);
            foreach (JsonProperty dp in p.Value.GetProperty("delegated").EnumerateObject())
                delegated[dp.Name] = new Delegation(
                    dp.Name,
                    dp.Value.GetProperty("to").GetString()!,
                    // Optional ON PURPOSE. A delegation with no `guard` must reach
                    // EveryDelegation_NamesAGuardThatExists and red there with an
                    // explanation, not die in the parser with a KeyNotFoundException.
                    dp.Value.TryGetProperty("guard", out JsonElement g) ? g.GetString() ?? "" : "",
                    dp.Value.GetProperty("reason").GetString()!);

            var excluded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty ep in p.Value.GetProperty("excluded").EnumerateObject())
                excluded[ep.Name] = ep.Value.GetString()!;

            consumers[p.Name] = new Consumer(
                p.Name,
                [.. p.Value.GetProperty("consumes").EnumerateArray().Select(x => x.GetString()!)],
                delegated,
                excluded);
        }

        var scanRoots = new List<ScanRoot>();
        foreach (JsonElement e in TopLevel(doc, "scanRoots").EnumerateArray())
            scanRoots.Add(new ScanRoot(
                e.GetProperty("path").GetString()!,
                [.. e.GetProperty("extensions").EnumerateArray().Select(x => x.GetString()!)],
                e.TryGetProperty("ignore", out JsonElement ig)
                    ? [.. ig.EnumerateArray().Select(x => x.GetString()!)]
                    : [],
                e.GetProperty("why").GetString()!));

        return new Roster(sets, consumers, [.. scanRoots]);
    }

    /// <summary>Reads one required top-level object out of the manifest, failing
    /// with a re-point message rather than a bare KeyNotFoundException. A parse
    /// that cannot find its own structure must say which key moved, because the
    /// reader of that stack trace is deciding whether to re-point this loader or
    /// to delete a test (pin standard, Rule 4).</summary>
    private static JsonElement TopLevel(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty(key, out JsonElement value))
            throw new InvalidOperationException(
                $"{ManifestPath} has no top-level `{key}` object. The manifest was restructured, "
                + "or the key was renamed — re-point this loader deliberately. Every fact in "
                + "ShellSourceRootsDriftTests reads through here, so the alternative to a loud "
                + "failure is five pins that cannot see their subject.");
        return value;
    }
}

public sealed class ShellSourceRootsDriftTests
{
    /// <summary>THE FLOOR, and it is the REAL count rather than a round number
    /// below it. Consolidating two sets is then a deliberate edit to this line
    /// instead of something that slides past. 15.1's review called a floor of 20
    /// against 131 files "theatre"; this is the opposite end of the same rule.</summary>
    private const int DeclaredSetCount = 7;

    /// <summary>The roster's total root count: 9 over those 7 sets —
    /// <c>androidShell</c> and <c>androidTemplateMirror</c> carry two each, the
    /// other five one apiece. It is a floor on a TOTAL and nothing more. Read the
    /// note at its assertion before reaching for it as a per-set guarantee; that
    /// mistake is limit 5 in this file's header and it shipped once.</summary>
    private const int DeclaredRootCount = 9;

    /// <summary>THE ANTI-VACUITY FLOOR, ON THE ITERATED SET. Three facts read the
    /// roster through here rather than through <c>ShellSourceRoots.Sets()</c>
    /// directly, so there is one floor and not three (pin standard, Rule 8).
    ///
    /// The floor guards the set that is ITERATED, not the one that is subtracted.
    /// That distinction is the census's own headline defect and it is the exact
    /// shape this file could have reproduced: `all.Except(named)` is empty when
    /// `all` is small, so a SHRUNK roster whose consumers were trimmed to match
    /// makes every consumer trivially total — a partition pin passing over
    /// almost no partition.
    ///
    /// SHRUNK, not emptied, and the difference was MEASURED rather than assumed.
    /// Emptying `sets` outright does not reach this floor's failure mode: the
    /// totality fact's `unknown` arm reds first, because every name the consumers
    /// still carry is then absent from the roster. Removing the floor and
    /// emptying `sets` was run, and it went RED on `unknown` — so that mutation
    /// proves nothing about this assertion. The honest contrast is one set left
    /// standing with all three consumers trimmed to it: floor removed, the fact
    /// passes; floor restored, it reds on the count. Both halves were run.</summary>
    private static IReadOnlyDictionary<string, ShellSourceRoots.SetDef> TheWholeRoster()
    {
        IReadOnlyDictionary<string, ShellSourceRoots.SetDef> sets = ShellSourceRoots.Sets();

        Assert.True(sets.Count >= DeclaredSetCount,
            $"the roster names only {sets.Count} sets, and {ShellSourceRoots.ManifestPath} "
            + $"declares {DeclaredSetCount} — this assertion is a floor on the ITERATED set, not "
            + "on the walk. A roster that shrank, with the consumers trimmed to match, would "
            + "otherwise make every consumer trivially total over the handful of sets left. "
            + "If a set was deliberately consolidated away, edit DeclaredSetCount in the same "
            + "commit and say why; do not lower it to make a red go away.");

        return sets;
    }

    /// <summary>THE CONTRACT, MECHANICALLY. Every consumer must account for every
    /// set exactly once across consumes / delegated / excluded. A set a consumer
    /// never mentions is the failure five separate reviews of the auth pin kept
    /// finding: not a wrong assertion, a place nobody looked. Partition, not
    /// subset — an overlap is also a red, because a tree both consumed and
    /// excluded means the reason a reviewer reads is not the behaviour that runs.
    ///
    /// ITS FIXED POINT IS THE `missing` ARM, and naming the wrong one is how a
    /// fact gets scored as controlled when it is not — the standard's own closing
    /// trap, "read the assertion, never the comment above it", which this comment
    /// fell into on its first draft. `unknown` is an ABSENCE assertion: it must
    /// hit nothing, so it proves nothing about the parse. `missing` comes back
    /// empty only because the consumer-side parse produced all seven roster
    /// names, so a parse that silently returned empty consumes/delegated/excluded
    /// lists reds there against real data. That plus the floor — a structural
    /// assertion about the sets half — is what Rule 3 asks for.</summary>
    [Fact]
    public void EveryConsumer_AccountsForEverySet_ExactlyOnce()
    {
        var all = TheWholeRoster().Keys.ToHashSet(StringComparer.Ordinal);

        IReadOnlyDictionary<string, ShellSourceRoots.Consumer> consumers = ShellSourceRoots.Consumers();
        Assert.True(consumers.Count > 0,
            $"{ShellSourceRoots.ManifestPath} declares no consumers, so this fact iterates "
            + "nothing and the partition contract binds nobody. Either a consumer was lost, or "
            + "the `consumers` object moved — re-point this deliberately.");

        foreach ((string consumer, ShellSourceRoots.Consumer c) in consumers)
        {
            var named = new List<string>();
            named.AddRange(c.Consumes);
            named.AddRange(c.Delegated.Keys);
            named.AddRange(c.Excluded.Keys);

            var dupes = named.GroupBy(n => n, StringComparer.Ordinal)
                             .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dupes.Count == 0,
                $"{consumer} names {string.Join(", ", dupes)} in more than one of "
                + "consumes/delegated/excluded, in " + ShellSourceRoots.ManifestPath + ". One "
                + "tree, one state — otherwise the reason a reviewer reads need not be the "
                + "behaviour that runs.");

            var missing = all.Except(named, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0,
                $"{consumer} says nothing about {string.Join(", ", missing)} in "
                + ShellSourceRoots.ManifestPath + ". `unmentioned` is not a state: decide whether "
                + "the pin consumes that tree, delegates it to a named guard, or excludes it with "
                + "a reason. This assertion is #364's actual fix — five reviews defeated the auth "
                + "pin by finding places it did not look.");

            var unknown = named.Except(all, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.True(unknown.Count == 0,
                $"{consumer} names {string.Join(", ", unknown)}, which is not a set in this "
                + "roster. A typo here silently drops a tree from the pin's subject — and, worse, "
                + "makes the consumer LOOK total while the real tree goes unmentioned.");
        }
    }

    /// <summary>A DECLARED ROOT THAT IS NOT THERE IS A PIN SCANNING NOTHING.
    /// Every consuming pin walks these directories; a path that has been renamed
    /// or moved yields an empty file set, and an absence assertion over an empty
    /// file set passes.
    ///
    /// This is a PRESENCE pin by construction — it asserts existence rather than
    /// absence — so its detector runs against real data on every execution and
    /// needs no separate fixed point (pin standard, Rule 3; the
    /// DeepLinkSeedDriftTests shape).
    ///
    /// THE PER-SET ASSERTION IS THE ONE THAT DELIVERS THE PROPERTY, and this is
    /// the file's own scar. The first cut carried only an aggregate — total roots
    /// floored against the SET count — with a message claiming that proved no set
    /// had an empty `roots` array. It proved nothing of the kind: the roster has
    /// 9 roots over 7 sets, so emptying one array leaves the total at 7 or 8 and
    /// the floor never fires. It was MEASURED in review: `appleShell.roots: []`
    /// and all five facts went green, with `SetsFor` still returning roots for
    /// both of its consumers because each has a second consumed set. After task 3
    /// that is `AuthSemanticsDriftTests` silently ceasing to scan the entire iOS
    /// shell and `NSLogDriftTests`' absence half scanning nothing while its own
    /// positive control keeps passing — #364 F1, reproduced by the pin written to
    /// close it. An aggregate over a partition says nothing about any member.
    ///
    /// LIMIT: it checks one direction only. A declared root must exist and must
    /// be non-empty; an existing shell tree need NOT be declared. That other
    /// direction is `EveryShellSourceFile_IsInsideADeclaredRoot`, not this fact —
    /// see limit 1 in this file's header for what is left after it.</summary>
    [Fact]
    public void EveryRoot_ExistsOnDisk()
    {
        int checkedRoots = 0;

        foreach ((string name, ShellSourceRoots.SetDef s) in TheWholeRoster())
        {
            // PER SET, not in aggregate. An empty `roots` array is invisible to
            // any total, and it is the one edit that removes a whole tree from
            // every pin that consumes the set while the roster still reads total.
            Assert.True(s.Roots.Length > 0,
                $"set '{name}' declares an EMPTY `roots` array. Every pin that consumes it then "
                + "walks nothing there and passes, while the totality fact still reports the set "
                + "as accounted for by all three consumers — a tree that has left the repository's "
                + "attention with the roster still reading complete. If the tree is genuinely "
                + "gone, delete the set and answer the totality question for every consumer; if it "
                + "moved, re-point the roots deliberately.");

            foreach (string rel in s.Roots)
            {
                checkedRoots++;
                Assert.True(
                    Directory.Exists(Path.Combine(
                        BnRepo.Root(), rel.Replace('/', Path.DirectorySeparatorChar))),
                    $"set '{name}' names '{rel}', which is not a directory. A pin walking a path "
                    + "that is not there scans nothing and passes — the exact vacuity this roster "
                    + "exists to prevent. Either the tree moved, in which case re-point the set "
                    + "deliberately, or it is gone, in which case delete the set and answer the "
                    + "totality fact's question for every consumer.");
            }
        }

        // A TOTAL, AND IT IS ONLY A TOTAL. It cannot see an emptied `roots`
        // array — the per-set assertion above does that, and this comment is
        // here because the message that used to sit on this line claimed
        // otherwise. What it does catch is the loop not running: a roster whose
        // sets all parsed with no roots at all, which the per-set arm would
        // report one set at a time and this reports as a whole.
        Assert.True(checkedRoots >= DeclaredRootCount,
            $"walked only {checkedRoots} roots, and {ShellSourceRoots.ManifestPath} declares "
            + $"{DeclaredRootCount} — the parse or the walk has stopped seeing the roster. This is "
            + "a floor on the total, NOT a per-set guarantee; the assertion inside the loop is "
            + "what rules out an empty array. If roots were deliberately consolidated, edit "
            + "DeclaredRootCount in the same commit and say why.");
    }

    /// <summary>DELEGATED IS NOT A SYNONYM FOR EXCLUDED, and this fact is what
    /// keeps the difference real. A delegation is a claim about ANOTHER test's
    /// behaviour, and an unpinned claim of that kind is the bug class this repo
    /// has paid for most often — three times in one week at its worst, every one
    /// a comment asserting a safety property nothing enforced.
    ///
    /// So every delegation names a guard, and the guard must exist. A delegation
    /// whose guard does not is an exclusion wearing a better word.
    ///
    /// LIMIT: existence only. This cannot tell whether the named guard asserts
    /// anything useful about the delegated tree — a method by that name with an
    /// empty body satisfies it. Direction: FAILS GREEN. What bounds it is that a
    /// guard is written once, by hand, next to the delegation it serves.</summary>
    [Fact]
    public void EveryDelegation_NamesAGuardThatExists()
    {
        HashSet<string> declared = DeclaredTestMethods();
        int delegations = 0;

        foreach ((string consumer, ShellSourceRoots.Consumer c) in ShellSourceRoots.Consumers())
            foreach ((string set, ShellSourceRoots.Delegation d) in c.Delegated)
            {
                delegations++;

                Assert.False(string.IsNullOrWhiteSpace(d.Guard),
                    $"{consumer} delegates '{set}' to {d.To} with no `guard`. A delegation is a "
                    + "claim about another test's behaviour; without a guard it is an exclusion "
                    + "wearing a better word. Name the test that asserts the delegate still "
                    + "covers this tree, or move the set to `excluded` and say so plainly.");

                Assert.True(declared.Contains(d.Guard),
                    $"{consumer} delegates '{set}' to guard '{d.Guard}', and no test by that name "
                    + $"exists under tests/. The delegation currently covers nothing: {d.To} may "
                    + "well still scan the tree, but nothing in this repository says so, which is "
                    + "precisely the unpinned claim the `guard` field exists to forbid.");
            }

        // NON-VACUITY. The roster has exactly one delegation today. If the last
        // one is ever genuinely removed, delete this fact in the same commit —
        // do not leave it standing as a guard over nothing.
        Assert.True(delegations >= 1,
            "no consumer in " + ShellSourceRoots.ManifestPath + " declares a delegation, so this "
            + "fact checked nothing. Either the `delegated` objects were emptied — in which case "
            + "every tree is now consumed or excluded outright and this fact should go — or the "
            + "parse stopped seeing them.");
    }

    /// <summary>THE `excluded` REASONS MAKE THE SAME CLAIM `delegated` DOES, and
    /// until this fact existed nothing checked them. AuthSemanticsDriftTests
    /// excludes `androidInstrumentedTests` on the written ground that the seam it
    /// drives "is pinned in the shell by TheTestOnlyCredentialBranch_IsStillGuarded".
    /// That is a claim about another test's behaviour — word for word the shape
    /// the manifest's own `$doc` forbids — and the only thing separating it from a
    /// delegation was which JSON key it sat under. Rename or delete the cited test
    /// and the exclusion becomes folklore, green.
    ///
    /// So every reason string in the roster, delegated and excluded alike, is read
    /// for test-method identifiers and each must resolve. The point is not this
    /// one sentence; it is that citing a test anywhere in the roster now costs the
    /// same as citing one in a `guard`.
    ///
    /// THE SHAPE IS DELIBERATELY NARROW: PascalCase_PascalCase, each half
    /// beginning upper-then-lower. That admits
    /// `TheTestOnlyCredentialBranch_IsStillGuarded` and refuses `BIOMETRIC_STRONG`
    /// and `AUTH_DEVICE_CREDENTIAL`, which are constants this repo's reasons
    /// legitimately name. A ban that reds on unrelated prose is one the next
    /// author weakens rather than obeys.</summary>
    [Fact]
    public void EveryTestNamedInAReason_Exists()
    {
        HashSet<string> declared = DeclaredTestMethods();
        var cited = new List<(string Where, string Name)>();

        foreach ((string consumer, ShellSourceRoots.Consumer c) in ShellSourceRoots.Consumers())
        {
            foreach ((string set, ShellSourceRoots.Delegation d) in c.Delegated)
                foreach (string n in TestIdentifiersIn(d.Reason))
                    cited.Add(($"{consumer}.delegated.{set}.reason", n));

            foreach ((string set, string reason) in c.Excluded)
                foreach (string n in TestIdentifiersIn(reason))
                    cited.Add(($"{consumer}.excluded.{set}", n));
        }

        // NON-VACUITY, and it is a real fixed point rather than a formality: the
        // roster cites exactly one test today, in an `excluded` reason, and that
        // citation is the whole reason this fact was written. If the extraction
        // stops matching it, this reds instead of quietly approving every reason
        // in the file.
        Assert.True(cited.Count >= 1,
            $"no reason in {ShellSourceRoots.ManifestPath} yielded a test-method identifier, so "
            + "this fact approved every reason without reading one. Either the identifier shape "
            + "stopped matching — re-point TestIdentifiersIn — or the last citation was rewritten "
            + "into prose, which is itself an unpinned claim this fact can no longer see.");

        var unresolved = cited.Where(x => !declared.Contains(x.Name)).ToList();
        Assert.True(unresolved.Count == 0,
            string.Join("\n", unresolved.Select(x => $"  {x.Where} cites '{x.Name}'"))
            + "\n\n— and no test by that name is declared under tests/. A reason that names a "
            + "test is a claim about that test's behaviour, and this repo has paid for that class "
            + "more often than any other. Either the test was renamed, in which case update the "
            + "reason, or it is gone, in which case the tree that reason excuses is now unguarded "
            + "and the exclusion needs re-deciding rather than re-wording.");
    }

    /// <summary>THE OTHER DIRECTION, and the one that makes "unmentioned" mean
    /// something for a tree NOBODY LISTED. Every other fact here reasons from the
    /// roster outwards, so a Kotlin source set that was never added to `sets` is a
    /// tree the partition is never asked about — the residual this file disclosed
    /// on its first draft, one level further out than #364 F1 but the same shape.
    ///
    /// This walks the manifest's `scanRoots` — the containers the shells' source
    /// actually lives in — and requires every file with a declared extension to
    /// sit inside some declared root. `src/BlazorNative.Jni/src/debug` is exactly
    /// why: it holds `res/xml` and no Kotlin, so leaving it out of `sets` is
    /// correct today and reds here the day it gains a `.kt`.
    ///
    /// WHAT IT STILL CANNOT SEE, stated because the residual MOVED rather than
    /// vanished: a shell tree outside every `scanRoots` entry, and anything under
    /// a declared `ignore`. `src/BlazorNative.Apple/vendor` is the one ignore —
    /// third-party checkouts, and scanning it would red on a vendored Swift
    /// package that is nobody's shell. Direction: FAILS GREEN, narrower than
    /// before by the whole of the Jni and template android trees.
    ///
    /// EXTENSION MATCHING IS EXPLICIT, not a glob. `Directory.EnumerateFiles` with
    /// `*.kt` also returns `.kts` on Windows — the legacy short-name rule — which
    /// would drag every `build.gradle.kts` into the subject and red immediately.
    /// The walk takes `*` and compares the extension itself.</summary>
    [Fact]
    public void EveryShellSourceFile_IsInsideADeclaredRoot()
    {
        var roots = TheWholeRoster().Values.SelectMany(s => s.Roots)
            .Select(r => r.Replace('/', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToList();

        ShellSourceRoots.ScanRoot[] scanRoots = ShellSourceRoots.ScanRoots();
        Assert.True(scanRoots.Length >= 1,
            $"{ShellSourceRoots.ManifestPath} declares no `scanRoots`, so this fact walks nothing "
            + "and every undeclared tree is invisible again. Restore the block, or delete this "
            + "fact and put the residual back in the header where a reader will find it.");

        string repo = BnRepo.Root();
        var orphans = new List<string>();
        int scanned = 0;

        foreach (ShellSourceRoots.ScanRoot sr in scanRoots)
        {
            string abs = Path.Combine(repo, sr.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(abs),
                $"`scanRoots` names '{sr.Path}', which is not a directory. A container that is "
                + "not there contributes no files and forgives everything under it — re-point it "
                + "deliberately rather than letting the walk shrink in silence.");

            var ignored = sr.Ignore.Select(
                i => Path.Combine(repo, i.Replace('/', Path.DirectorySeparatorChar))
                     + Path.DirectorySeparatorChar).ToList();

            foreach (string file in Directory.EnumerateFiles(abs, "*", SearchOption.AllDirectories))
            {
                // Explicit extension comparison — see the note about *.kt also
                // matching .kts on Windows.
                if (!sr.Extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (ignored.Any(i => file.StartsWith(i, StringComparison.OrdinalIgnoreCase)))
                    continue;

                scanned++;
                string rel = Path.GetRelativePath(repo, file);
                if (!roots.Any(r => rel.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                    orphans.Add("  " + rel.Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        // ANTI-VACUITY ON THE ITERATED SET. 118 Kotlin and 66 Swift files as
        // measured; the floor sits under both so ordinary growth and ordinary
        // deletion never touch it, while a walk that has stopped seeing its
        // subject cannot hide behind an empty orphan list.
        Assert.True(scanned >= 150,
            $"scanned only {scanned} shell source files across {scanRoots.Length} scanRoots, and "
            + "there are roughly 184 — 118 Kotlin and 66 Swift, measured. The walk or the "
            + "extension list has stopped seeing its subject, so the orphan list below is empty "
            + "for the wrong reason.");

        Assert.True(orphans.Count == 0,
            "SHELL SOURCE LIVES OUTSIDE EVERY DECLARED ROOT:\n"
            + string.Join("\n", orphans)
            + "\n\nA tree in no set is a tree the partition never asks about, so every consuming "
            + "pin is blind to it while " + ShellSourceRoots.ManifestPath + " still reads total. "
            + "That is #364 F1 one level out. Add a set for it and answer the totality question "
            + "for all three consumers, or widen an existing set's roots — both are a few lines, "
            + "and neither is silent.");
    }

    /// <summary>Every test-method name declared under tests/, read once. Two facts
    /// resolve names against it, so there is one walk and one detector rather than
    /// two copies of the same search (pin standard, Rule 8).
    ///
    /// The population is `public void Name(` in the raw file text, which is the
    /// detector the first draft ran per-delegation. Comments are NOT stripped,
    /// deliberately: a name is being resolved, not a behaviour asserted, and a
    /// commented-out test that still satisfies a citation is a far smaller problem
    /// than a citation this cannot see at all.</summary>
    private static HashSet<string> DeclaredTestMethods()
    {
        string[] sources = Directory.EnumerateFiles(
            Path.Combine(BnRepo.Root(), "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(BinSegment, StringComparison.Ordinal)
                     && !f.Contains(ObjSegment, StringComparison.Ordinal))
            .ToArray();

        Assert.True(sources.Length >= 100,
            $"found only {sources.Length} hand-written .cs files under tests/, and there were "
            + "133 when this floor was measured — the walk has stopped seeing its subject, so "
            + "every cited test name would resolve against a short index and red for the wrong "
            + "reason. bin/ and obj/ are excluded so the count cannot move with build state.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string f in sources)
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"public\s+void\s+(\w+)\s*\("))
                names.Add(m.Groups[1].Value);

        Assert.True(names.Count >= 500,
            $"extracted only {names.Count} distinct test-method names from {sources.Length} "
            + "files, and 591 were measured — the `public void Name(` pattern has stopped "
            + "matching. Re-point it; do not let citations resolve against a short index.");

        return names;
    }

    private static readonly string BinSegment =
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
    private static readonly string ObjSegment =
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

    /// <summary>Test-method identifiers inside a reason string:
    /// PascalCase_PascalCase, each half beginning upper-then-lower. Narrow on
    /// purpose — see EveryTestNamedInAReason_Exists for why ALL_CAPS constants
    /// must not match.</summary>
    private static IEnumerable<string> TestIdentifiersIn(string reason) =>
        Regex.Matches(reason, @"\b[A-Z][a-z][A-Za-z0-9]*(?:_[A-Z][a-z][A-Za-z0-9]*)+\b")
             .Select(m => m.Value);

    /// <summary>THE ROSTER ANSWERS TO THE BUILD, NOT TO ITSELF. `androidShell`
    /// must name exactly the directories the Gradle `main` source set feeds the
    /// Kotlin compiler. This repo has already paid for that silence once: the
    /// AGP 9 migration left `src/androidMain/kotlin` UNCOMPILED while the
    /// instrumented suite reported 111 passing tests, and it merged and sat
    /// there. A roster that only agreed with itself would have been just as
    /// quiet.
    ///
    /// The call and the file are read from the set's own `derivedFrom` block, so
    /// nothing here restates them as a second literal.
    ///
    /// LIMIT, stated — AND MEASURED, because the obvious guess about it is wrong.
    /// This reads `kotlin.srcDirs(...)` with a REGEX, not a Kotlin parser, so the
    /// first draft of this paragraph said a multi-line reformat reds. It does
    /// not: the capture is `[^)]*`, which matches newlines, and splitting the
    /// argument list over four lines was run and PASSED. That is the likeliest
    /// reformat by far and it costs nothing.
    ///
    /// The three shapes that do red, each verified: the call RENAMED — to
    /// `setSrcDirs(listOf(…))`, say — reds on `calls.Count > 0`; the call
    /// COMMENTED OUT reds on the same arm, because the comment-stripping pass
    /// runs first; the directory list moved into a VARIABLE reds on
    /// `declared.Count > 0`. All three are FALSE REDS — Rule 5's footnote
    /// direction, not its defect direction — and each message says how to
    /// re-point. A nested `)` inside the arguments truncates the capture at the
    /// first one; in every plausible spelling the quoted strings still come out
    /// right, so that is a note rather than a hole.</summary>
    [Fact]
    public void TheAndroidRoster_MatchesTheGradleMainSourceSet()
    {
        var derived = TheWholeRoster().Values.Where(s => s.DerivedFrom is not null).ToList();

        Assert.True(derived.Count >= 1,
            $"no set in {ShellSourceRoots.ManifestPath} carries a `derivedFrom` block, so this "
            + "fact compares nothing and the roster answers only to itself. The one build-derived "
            + "set is `androidShell`; if it was renamed, re-point this deliberately.");

        foreach (ShellSourceRoots.SetDef s in derived)
        {
            ShellSourceRoots.GradleSource g = s.DerivedFrom!;
            string file = Path.Combine(
                BnRepo.Root(), g.File.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(file),
                $"set '{s.Name}' derives its roots from {g.File}, which does not exist. The build "
                + "script moved — re-point `derivedFrom.file` deliberately rather than deleting "
                + "the block, or the roster stops answering to the build.");

            string code = CommentStrippedSource.Strip(File.ReadAllText(file));
            string pattern = Regex.Escape(g.Call) + @"\s*\(([^)]*)\)";
            MatchCollection calls = Regex.Matches(code, pattern);

            // THE REGEX MUST HIT SOMETHING FIRST. Without this, a call that was
            // reformatted or renamed yields an empty extracted set, and an empty
            // set compared to an empty roster would be "equal" — two nothings
            // agreeing. Assert the subject was found before comparing it.
            Assert.True(calls.Count > 0,
                $"could not find a `{g.Call}(` call in {g.File} (pattern: {pattern}). It was "
                + "renamed, reformatted onto several lines, or the directory list moved into a "
                + "variable — a pin that cannot see its subject must never pass vacuously, so "
                + "this reds. Re-point the regex, or change `derivedFrom.call` to whatever the "
                + "build now spells.");

            var declared = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match call in calls)
                foreach (Match quoted in Regex.Matches(call.Groups[1].Value, "\"([^\"]*)\""))
                    declared.Add(g.PathPrefix + quoted.Groups[1].Value);

            Assert.True(declared.Count > 0,
                $"`{g.Call}(` matched in {g.File} but carried no quoted directory. The arguments "
                + "are built some other way now — a variable, a list, a spread — so this pin can "
                + "no longer derive the source set and must be re-pointed rather than believed.");

            var rostered = new SortedSet<string>(s.Roots, StringComparer.Ordinal);
            Assert.True(declared.SetEquals(rostered),
                $"set '{s.Name}' in {ShellSourceRoots.ManifestPath} and the `{g.Call}` call in "
                + $"{g.File} disagree about what the shell's source tree IS.\n"
                + $"  gradle says: {string.Join(", ", declared)}\n"
                + $"  roster says: {string.Join(", ", rostered)}\n"
                + "The build wins. A directory Gradle compiles and the roster omits is a tree "
                + "every consuming pin is blind to — #364 F1 exactly — and a directory the roster "
                + "names and Gradle does not is the AGP 9 incident: source in the tree that "
                + "nothing builds, with pins reporting green over it.");
        }
    }

    /// <summary>THE DELEGATION'S OWN GUARD. AuthSemanticsDriftTests does not scan
    /// `androidTemplateMirror`; it delegates it to TemplateDriftTests, whose
    /// byte-identity pin holds the template's copy of the auth-bearing shell file
    /// equal to the repo's. An auth change in one and not the other already reds
    /// there. Scanning it here as well would double every site and every ignored
    /// count in auth-semantics.json for a tree that cannot differ.
    ///
    /// That is a claim about ANOTHER test's behaviour, so it gets a pin. Two
    /// things could quietly falsify it and both are checked: the template's copy
    /// could disappear from the mirrored tree, and the file could be added to
    /// TemplateDriftTests' `divergent` set — the one list that REMOVES a file
    /// from the byte comparison while leaving everything green.
    ///
    /// NOTHING IS RESTATED HERE. The auth-bearing file comes off
    /// src/auth-semantics.json; the repo→template path mapping comes off the
    /// roster's own `androidShell` and `androidTemplateMirror` roots.
    ///
    /// LIMIT: it reads TemplateDriftTests' two lists with anchored regexes. A
    /// rename of either collection reds with a re-point instruction. FALSE RED
    /// direction. It does not verify that the byte comparison itself still
    /// asserts anything — that is TemplateDriftTests' own business.</summary>
    private const string WhyThisMatters =
        "AuthSemanticsDriftTests delegates the WHOLE template mirror on the strength of that "
        + "byte comparison. The moment it stops, `templates/` is an unscanned auth surface no "
        + "pin in this repository looks at, and the delegation in " + ShellSourceRoots.ManifestPath
        + " becomes an exclusion wearing a better word. Either restore the comparison, or change "
        + "the delegation to `consumes` and let the auth pin scan the mirror itself.";

    [Fact]
    public void TheAuthBearingShellFile_IsStillInTheTemplateMirrorList()
    {
        IReadOnlyDictionary<string, ShellSourceRoots.SetDef> sets = TheWholeRoster();
        ShellSourceRoots.SetDef shell = RequireSet(sets, "androidShell");
        ShellSourceRoots.SetDef mirror = RequireSet(sets, "androidTemplateMirror");

        Assert.True(shell.DerivedFrom is not null,
            "set 'androidShell' no longer carries a `derivedFrom` block, and this guard needs its "
            + "`pathPrefix` to turn a repo path into a template path. Restore the block, or "
            + "re-point this guard deliberately — do not delete it, because the delegation it "
            + "protects has no other guard.");
        string prefix = shell.DerivedFrom!.PathPrefix;

        // ── The auth-bearing shell files, derived from the auth manifest ──────
        string authManifest = Path.Combine(BnRepo.Root(), "src", "auth-semantics.json");
        Assert.True(File.Exists(authManifest),
            "src/auth-semantics.json is missing, so this guard cannot tell which shell file the "
            + "delegation is about. Re-point it deliberately.");

        using JsonDocument auth = JsonDocument.Parse(File.ReadAllText(authManifest),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var authFiles = auth.RootElement.GetProperty("sites").EnumerateArray()
            .Select(s => s.GetProperty("file").GetString()!)
            .Where(f => shell.Roots.Any(r => f.StartsWith(r + "/", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(authFiles.Count >= 1,
            "no site in src/auth-semantics.json lives under any of androidShell's roots, so this "
            + "guard has no subject and the delegation of androidTemplateMirror is unguarded. "
            + "Either the auth sites moved, or the roster's roots did — re-point, do not delete.");

        // ── TemplateDriftTests' two lists, read rather than restated ──────────
        string templatePin = Path.Combine(
            BnRepo.Root(), "tests", "BlazorNative.Runtime.Tests", "TemplateDriftTests.cs");
        Assert.True(File.Exists(templatePin),
            "tests/BlazorNative.Runtime.Tests/TemplateDriftTests.cs is missing — the delegate this "
            + "delegation names is gone. Move androidTemplateMirror to `consumes` or re-point the "
            + "delegation at whatever replaced it.");

        string pinCode = CommentStrippedSource.Strip(File.ReadAllText(templatePin));

        var manifest = QuotedEntries(pinCode,
            @"string\[\]\s+expected\s*=\s*\[(.*?)\];",
            "TemplateDriftTests' expected content manifest",
            "string[] expected = [ … ];");

        var divergent = QuotedEntries(pinCode,
            @"var\s+divergent\s*=\s*new\s+HashSet<string>\s*\([^)]*\)\s*\{(.*?)\};",
            "TemplateDriftTests' `divergent` exclusion set",
            "var divergent = new HashSet<string>(…) { … };");

        foreach (string authFile in authFiles)
        {
            // repo path → the tail below the source set, → the mirror's copy.
            string shellRoot = shell.Roots.First(r => authFile.StartsWith(r + "/", StringComparison.Ordinal));
            string tail = authFile[(shellRoot.Length + 1)..];
            string sourceSet = shellRoot[prefix.Length..];                 // src/androidMain/kotlin

            string mirrorRoot = mirror.Roots.FirstOrDefault(
                r => r.EndsWith("/" + sourceSet, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"androidTemplateMirror names no root ending in '{sourceSet}', so the repo "
                    + $"source set holding {authFile} has no counterpart in the mirror roster. "
                    + "The template's layout changed, or the roster's did — re-point both "
                    + "deliberately rather than dropping the delegation.");

            string templateRoot = mirrorRoot[..^(sourceSet.Length + 1)];   // templates/…/android
            string androidRelative = $"{sourceSet}/{tail}";                // as `divergent` spells it
            string contentRelative =                                       // as the manifest spells it
                $"{templateRoot[(templateRoot.LastIndexOf('/') + 1)..]}/{androidRelative}";

            Assert.True(
                File.Exists(Path.Combine(
                    BnRepo.Root(), $"{mirrorRoot}/{tail}".Replace('/', Path.DirectorySeparatorChar))),
                $"the template has no copy of the auth-bearing shell file {authFile} at "
                + $"{mirrorRoot}/{tail}. AuthSemanticsDriftTests DELEGATES the template mirror on "
                + "the strength of that copy being byte-identical; with no copy there is nothing "
                + "to be identical to, and the delegation covers nothing.");

            // Plain `Assert.Contains` prints the collection and not the reason,
            // and the reason is the whole value of a delegation guard.
            Assert.True(manifest.Contains(contentRelative),
                $"TemplateDriftTests' expected content manifest no longer lists "
                + $"{contentRelative}, so the template's copy of the auth-bearing shell file is "
                + "not part of the pack inventory that pin holds. " + WhyThisMatters);

            Assert.False(divergent.Contains(androidRelative),
                $"TemplateDriftTests now names {androidRelative} in its `divergent` set — the one "
                + "list that REMOVES a file from the byte comparison while everything stays "
                + "green. " + WhyThisMatters);
        }
    }

    /// <summary>Looks a set up by name and reds with a re-point message instead of
    /// throwing KeyNotFoundException. Every other assertion in this file tells the
    /// reader what probably happened and what to do; a bare dictionary indexer
    /// tells them to open a stack trace (pin standard, Rule 4).</summary>
    private static ShellSourceRoots.SetDef RequireSet(
        IReadOnlyDictionary<string, ShellSourceRoots.SetDef> sets, string name)
    {
        Assert.True(sets.TryGetValue(name, out ShellSourceRoots.SetDef? s),
            $"{ShellSourceRoots.ManifestPath} has no set named '{name}', which this fact is "
            + "written about by name. The set was renamed or removed — re-point this fact "
            + $"deliberately. Sets present: {string.Join(", ", sets.Keys.OrderBy(k => k, StringComparer.Ordinal))}.");
        return s!;
    }

    /// <summary>Pulls the quoted entries out of one named collection initialiser
    /// in already-comment-stripped C#. Reds — naming the pattern and the shape it
    /// expects — when the collection cannot be found or comes back empty, because
    /// a guard that cannot see its subject must not compare two empty sets and
    /// call them equal (pin standard, Rule 4).</summary>
    private static HashSet<string> QuotedEntries(
        string code, string pattern, string what, string shape)
    {
        Match region = Regex.Match(code, pattern, RegexOptions.Singleline);
        Assert.True(region.Success,
            $"could not find {what} (pattern: {pattern}). It was renamed, reshaped or moved — "
            + $"the shape this guard expects is `{shape}`. Re-point it deliberately; do not "
            + "delete the assertion, because the delegation it protects has no other guard.");

        var entries = Regex.Matches(region.Groups[1].Value, "\"([^\"]*)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(entries.Count > 0,
            $"{what} matched but parsed to ZERO entries. Comparing against an empty set would "
            + "make every membership question answer the convenient way — re-point the parse.");

        return entries;
    }
}
