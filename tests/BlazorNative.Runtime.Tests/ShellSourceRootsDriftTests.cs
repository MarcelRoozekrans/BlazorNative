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
//     And `EveryTestNamedInAReason_Exists` only sees names carrying an
//     UNDERSCORE -- all but a handful of the suite, and the exceptions are
//     exactly the underscore-free names:
//     `AnEmptyComponentFailsLoudlyRatherThanVacuously`, `EventsAreProjected`,
//     `TheImageCannotMoveTheFrameTable`. Nothing can match those without also
//     matching every capitalised word in prose, so the boundary is where it
//     is on purpose. A reason citing one of them, or citing a test in prose
//     at all, makes an unpinned claim this cannot see. Direction: FAILS
//     GREEN, and the fix is to cite a test whose name has an underscore.
//
//     The other direction is latent and LOUD. Anything shaped `Word_Word`
//     with a lower-case letter matches -- `Phase_2`, `BnHost_Tests` -- so a
//     reason that happens to contain one would red demanding a test that was
//     never meant. Zero occur in today's reasons, checked, and the direction
//     is a red rather than a green, which is why the shape is not narrowed
//     further: narrowing it is what produced the false green above.
//
//  5. AN AGGREGATE OVER A PARTITION SAYS NOTHING ABOUT ANY MEMBER -- and the
//     honest thing to record here is that WRITING THIS DOWN DID NOT WORK.
//
//     First instance: `EveryRoot_ExistsOnDisk` floored the TOTAL root count
//     against the SET count and its message claimed that proved no set had
//     an empty `roots` array. It did not -- 9 roots over 7 sets absorbs one
//     emptied array without the total dropping below 7 -- and emptying
//     `appleShell` left all five facts green while two pins would have
//     stopped scanning the entire iOS shell. The fix was a per-set
//     assertion, and this paragraph was written beside it, with the incident
//     attached, so that nobody re-derived it.
//
//     Second instance: TWELVE LINES BELOW, IN THE SAME COMMIT.
//     `EveryShellSourceFile_IsInsideADeclaredRoot` shipped `scanned >= 150`
//     over three containers of 100 / 18 / 66 files. Deleting the whole
//     template container -- the tree that ships with every `dotnet new
//     blazornative` -- left all seven facts green, because 18 disappears
//     inside 34 of headroom. Third instance, found by the sweep that finally
//     followed: `consumers.Count > 0` against three consumers.
//
//     Fourth instance: `checkedRoots >= DeclaredRootCount`, a literal 9
//     summing a SECOND-LEVEL partition -- roots within sets. Worse, the fix
//     round CLASSIFIED it as a cardinality and promised "a member leaving
//     drops it", while its own assertion site twelve hundred lines away said
//     "A TOTAL, AND IT IS ONLY A TOTAL". Right at the red, wrong in the
//     summary -- which is precisely the defect that same round had just
//     corrected elsewhere. Moving one of androidTemplateMirror's two roots
//     into androidJvmHost left all eight facts green.
//
//     KNOWING THE RULE, HAVING JUST WRITTEN THE RULE, AND ATTACHING A WORKED
//     INCIDENT TO IT DID NOT PREVENT THE NEXT INSTANCE. Neither did writing
//     a taxonomy: the taxonomy acquired a wrong row on its first outing.
//     Prose does not generalise itself, and NEITHER DOES CLASSIFICATION --
//     a bucket is just more prose, and it can be wrong in the same way.
//
//     SO THE RULE IS NOT "CLASSIFY EVERY FLOOR". IT IS: ASK WHETHER THE
//     NUMBER CAN BE DERIVED FROM THE MANIFEST, AND IF IT CAN, DERIVE IT. A
//     derived number needs no classification, because there is nothing left
//     to misclassify. Applied here, that emptied two of the four buckets:
//
//       DERIVED, so not classifiable and not arguable --
//       `scanned >= expected`, the sum of the per-container `minFiles`; and
//       `checkedRoots >= declaredRoots`, the sum of the per-set root counts,
//       which used to be the literal 9.
//
//       CONSTRAINED BY A RELATION between numbers the manifest already holds
//       -- `sr.MinFiles >= 1 && sr.MinFiles <= sr.Measured`. Without it,
//       `minFiles: 0` plus a re-point defused the whole fact in two JSON
//       tokens, measured.
//
//       PER MEMBER, inside the loop -- `s.Roots.Length > 0` and
//       `contributed >= sr.MinFiles`. These deliver a property about every
//       member; no aggregate can.
//
//       EXTERNALLY DERIVED ROOT LISTS, which is what stops a root MOVING
//       between sets where no count can see it -- the Gradle derivation for
//       `androidShell`, `mirrorOf` for `androidTemplateMirror`. Those are the
//       roster's only two multi-root sets, and a root leaving a single-root
//       set hits zero and reds per set, so the cover is complete TODAY. A
//       third multi-root set would arrive uncovered: give it a derivation,
//       not a count.
//
//     WHAT IS STILL AN INDEPENDENT LITERAL, which is the honest residual and
//     is now a short list rather than four buckets:
//
//       `DeclaredSetCount` 7, `DeclaredConsumerCount` 3, `ScanRootCount` 3.
//       These CANNOT be derived: there is no second record of how many sets,
//       consumers or containers there ought to be -- the number IS the
//       record, which is exactly why it has to be exact rather than `> 0`.
//
//       The six manifest numbers -- three `minFiles`, three `measured`. Not
//       derivable either, because they are observations about the disk. They
//       are mutually constrained by the relation above, which is the most a
//       recorded observation can be given.
//
//       `sources.Length >= 100` and `names.Count >= 500` in
//       DeclaredTestMethods. One walk over tests/, no manifest behind it.
//       Both fail LOUD: a narrowed index makes a real citation unresolvable.
//
//       `delegations >= 1`, `cited.Count >= 1`, `derived.Count >= 1`,
//       `authFiles.Count >= 1`. Each population is single-member today, so
//       `>= 1` IS its exact cardinality, and every member the loop finds is
//       asserted as it is found. Each carries its own note saying what a
//       second member would cost.
//
//       `entries.Count > 0` in QuotedEntries. NOT per-member and NOT in a
//       loop -- the earlier taxonomy put it in the per-member bucket and
//       that was wrong. See limit 6.
//  6. THE DELEGATION GUARD READS ANOTHER TEST'S SOURCE WITH A REGEX, AND
//     THAT REGEX SEES ONLY THE COLLECTION INITIALISER. `QuotedEntries`
//     captures the text between `{` and `};`, so entries added to
//     TemplateDriftTests' `divergent` set by any other route -- an
//     `Add(...)` call after the initialiser, a loop, a second collection
//     unioned in -- are invisible. MEASURED in review: an `Add` of the
//     auth-bearing file immediately below the initialiser removed it from
//     the byte comparison and all nineteen facts across both pins passed,
//     with this guard's entire job bypassed. Adding the same file INSIDE
//     the initialiser reds, so the detector works; its subject is too
//     narrow.
//
//     Direction: FAILS GREEN, on a negative membership test, which is the
//     worst combination in this file. `entries.Count > 0` does not help --
//     it is a total over five initialiser entries and says nothing about
//     any of them, which is why it is NOT in the per-member list above.
//
//     THE REPAIR IS NAMED AND ASSIGNED, not deferred vaguely: make
//     `TemplateVerbatimAndroidFiles()` internal and assert membership of
//     the byte-identity set itself instead of regexing its source. That
//     deletes both regexes here, turns a rename into a compile error, and
//     closes this because it reads the SET the comparison uses rather than
//     the text one of its inputs is written in. Phase 15.2 task 5 owns it;
//     that file is already open there.
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
        string Name, string Language, string Purpose, string[] Roots,
        GradleSource? DerivedFrom, MirrorSource? MirrorOf);

    /// <summary>A set whose roots are another set's roots with the path prefix
    /// swapped. It is how a root list stops being a free literal: the template
    /// mirror's two source sets answer to the same Gradle call the repo's do,
    /// transitively, instead of being a pair of strings nothing compares.</summary>
    internal sealed record MirrorSource(string Set, string PathPrefix);

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
    internal sealed record ScanRoot(
        string Path, string[] Extensions, string[] Ignore, int MinFiles, int Measured, string Why);

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
                derived,
                p.Value.TryGetProperty("mirrorOf", out JsonElement mo)
                    ? new MirrorSource(
                        mo.GetProperty("set").GetString()!,
                        mo.GetProperty("pathPrefix").GetString()!)
                    : null);
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
                e.GetProperty("minFiles").GetInt32(),
                e.GetProperty("measured").GetInt32(),
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

    /// <summary>The three pins that declare a coverage position. A cardinality
    /// floor rather than <c>&gt; 0</c>, for the reason limit 5 gives.</summary>
    private const int DeclaredConsumerCount = 3;

    /// <summary>The three containers walked when looking for undeclared source
    /// trees. Same shape, same reason — and this is the one the re-review caught:
    /// deleting the template container left all seven facts green.</summary>
    private const int ScanRootCount = 3;

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
        // CARDINALITY, NOT `> 0` — the second instance of limit 5's shape, found
        // by the sweep limit 5 should have made reflexive. `> 0` is an aggregate
        // over a three-member partition: delete one consumer's whole entry and
        // 2 > 0 still passes, while that pin's coverage position stops being
        // recorded anywhere. Measured.
        Assert.True(consumers.Count >= DeclaredConsumerCount,
            $"{ShellSourceRoots.ManifestPath} declares {consumers.Count} consumers and "
            + $"{DeclaredConsumerCount} are expected. A consumer whose entry is deleted is a pin "
            + "with NO declared coverage position — the partition contract stops binding it, "
            + "silently, which is this fact's own failure one level up. If a pin was genuinely "
            + "retired, edit DeclaredConsumerCount in the same commit and say why.");

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

        // DERIVED, NOT DECLARED — and this line is the file's fourth encounter
        // with the aggregate shape. It used to read `>= DeclaredRootCount`, a
        // literal 9 summing a SECOND-LEVEL partition: roots within sets. The
        // header called that a cardinality and promised "a member leaving drops
        // it". It does not. Moving one of androidTemplateMirror's two roots into
        // androidJvmHost keeps the total at 9, every array non-empty, and left
        // all eight facts green while the tree that ships with every
        // `dotnet new blazornative` declared one of its two source directories.
        //
        // So the number is computed from the roster rather than written beside
        // it. There is nothing left to misclassify: this asserts the loop ran
        // once per declared root and claims nothing else. What stops a root
        // MOVING between sets is elsewhere, and it is complete — see the note
        // at TheTemplateMirrorRoots_MirrorTheShellRoots.
        int declaredRoots = TheWholeRoster().Values.Sum(x => x.Roots.Length);
        Assert.True(checkedRoots >= declaredRoots,
            $"walked {checkedRoots} roots against {declaredRoots} declared in "
            + $"{ShellSourceRoots.ManifestPath} — the walk did not run once per root, so this "
            + "fact has skipped part of the roster. This is arithmetic over the per-set "
            + "assertion above, not a guarantee of its own.");
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

        // NON-VACUITY, and a real fixed point rather than a formality — for a
        // non-obvious reason worth writing down. The roster's only citation sits
        // in an `excluded` reason, so deleting the `foreach (c.Excluded)` loop —
        // the exact regression this fact exists to prevent — takes `cited` to 0
        // and reds here.
        //
        // THAT IS LUCK, AND IT EXPIRES. The moment any `delegated` reason gains an
        // identifier, this floor is satisfied by the delegated half alone and the
        // excluded half can be dropped green. It is limit 5's shape one mutation
        // away rather than present: an aggregate over two sources of citations.
        // If a delegated reason ever cites a test, split this into one floor per
        // source rather than raising the number.
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
    /// EXTENSION MATCHING IS EXPLICIT, not a glob, because
    /// `Directory.EnumerateFiles` with `*.kt` also returns `.kts` on Windows —
    /// the legacy short-name rule. NOTHING WOULD BE DRAGGED IN TODAY: there are
    /// zero `.kts` files under any scanRoot, and `build.gradle.kts` sits one level
    /// ABOVE the `src/BlazorNative.Jni/src` container. That sentence used to claim
    /// the opposite and is corrected rather than softened — a checkable claim that
    /// is wrong reads as a measurement and makes the code look load-bearing for a
    /// reason it is not. The explicit compare stays because it costs nothing and
    /// keeps the property true if a `.kts` ever lands inside a container.</summary>
    [Fact]
    public void EveryShellSourceFile_IsInsideADeclaredRoot()
    {
        var roots = TheWholeRoster().Values.SelectMany(s => s.Roots)
            .Select(r => r.Replace('/', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToList();

        ShellSourceRoots.ScanRoot[] scanRoots = ShellSourceRoots.ScanRoots();
        // CARDINALITY FIRST. `>= 1` was the defect: deleting the template
        // container — the tree that ships with every `dotnet new blazornative` —
        // left all seven facts green, because 2 >= 1 and because 18 files vanish
        // inside a global floor's headroom. Measured.
        Assert.True(scanRoots.Length >= ScanRootCount,
            $"{ShellSourceRoots.ManifestPath} declares {scanRoots.Length} `scanRoots` and "
            + $"{ScanRootCount} are expected. A container that is DELETED rather than "
            + "mis-pointed is invisible to every other assertion here: Directory.Exists never "
            + "runs for it, and no count over the containers that remain can miss what is no "
            + "longer iterated. If a container was genuinely consolidated, edit ScanRootCount "
            + "in the same commit and say why.");

        string repo = BnRepo.Root();
        var orphans = new List<string>();
        int scanned = 0;
        int expected = 0;

        foreach (ShellSourceRoots.ScanRoot sr in scanRoots)
        {
            int contributed = 0;

            // THE FLOOR'S OWN DATA IS FLOORED. Moving the magic number out of
            // the C# and into the manifest moved it somewhere nothing
            // constrained it: `minFiles: 0` plus a re-point one level deeper was
            // MEASURED green, two JSON tokens defusing this fact entirely. The
            // relation is between two numbers the manifest already records, so
            // it costs two comparisons and needs no new surface.
            Assert.True(sr.MinFiles >= 1 && sr.MinFiles <= sr.Measured,
                $"`scanRoots` entry '{sr.Path}' declares minFiles={sr.MinFiles} against "
                + $"measured={sr.Measured}. A floor must be at least 1 — zero forgives a "
                + "container that has stopped contributing anything — and never above what was "
                + "actually counted, which would red on a tree that is fine. Lowering a floor "
                + "below its observation is the edit this fact exists to make loud, so it is "
                + "the one edit that cannot be made quietly.");

            expected += sr.MinFiles;
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
                contributed++;
                string rel = Path.GetRelativePath(repo, file);
                if (!roots.Any(r => rel.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                    orphans.Add("  " + rel.Replace(Path.DirectorySeparatorChar, '/'));
            }

            // PER CONTAINER. This is the thing the total below cannot do, and the
            // reason the manifest carries a floor per entry rather than one number
            // for all three: a container re-pointed at a deeper directory
            // contributes a handful of files instead of a hundred, and any global
            // floor with useful headroom absorbs that without a word.
            Assert.True(contributed >= sr.MinFiles,
                $"`scanRoots` entry '{sr.Path}' contributed {contributed} files against its "
                + $"declared floor of {sr.MinFiles}; {sr.Measured} were counted when that floor "
                + "was set. The container has narrowed — it was re-pointed deeper, its "
                + "extensions no longer match what lives there, or the tree genuinely shrank. "
                + "The first two leave this fact blind to everything under it while the orphan "
                + "list stays empty. If the tree really shrank, edit `minFiles` and `measured` "
                + "together in the same commit and say why.");
        }

        // THE TOTAL IS DERIVED, NOT DECLARED. It is the sum of the per-container
        // floors asserted above, so there is no second number free to be looser
        // than the members imply, and nothing here can be read as a guarantee the
        // members do not already give. It survives only because it catches the
        // loop not running at all, which the per-container arm cannot report.
        Assert.True(scanned >= expected,
            $"scanned {scanned} shell source files across {scanRoots.Length} scanRoots against "
            + $"a summed floor of {expected}; 184 were counted when those floors were set, 118 "
            + "Kotlin and 66 Swift. This is a floor on a TOTAL, and the per-container "
            + "assertions above are what rule out a single container going quiet — read it as "
            + "arithmetic over them, not as a guarantee of its own.");

        Assert.True(orphans.Count == 0,
            "SHELL SOURCE LIVES OUTSIDE EVERY DECLARED ROOT:\n"
            + string.Join("\n", orphans)
            + "\n\nA tree in no set is a tree the partition never asks about, so every consuming "
            + "pin is blind to it while " + ShellSourceRoots.ManifestPath + " still reads total. "
            + "That is #364 F1 one level out. Add a set for it and answer the totality question "
            + "for all three consumers, or widen an existing set's roots — both are a few lines, "
            + "and neither is silent.");
    }

    /// <summary>THE DISCLAIMER DELETES ITSELF, OR THIS REDS. The manifest carries a
    /// paragraph saying these entries record what a pin CLAIMS rather than what it
    /// scans, true only until task 3 points each consumer at
    /// <c>ShellSourceRoots.SetsFor</c>. It ends with an instruction to delete it in
    /// that commit.
    ///
    /// A NOTE INSTRUCTING A FUTURE DELETION, WITH NOTHING ENFORCING IT, IS THE
    /// CLASS THIS WHOLE PHASE IS ABOUT — one more time, in the artefact a reader
    /// opens first. If the paragraph survives the repointing, the roster's primary
    /// document carries a false disclaimer saying the roster does not bind, in a
    /// repository where it does. If it is deleted EARLY, the opposite: the roster
    /// claims to bind while three pins still read their own private lists.
    ///
    /// So the paragraph's presence is pinned to the tree rather than to anyone's
    /// diligence. Present while any consumer is unrepointed; gone once none is.
    ///
    /// LIMIT: "repointed" is `the consumer's source names SetsFor`, which is a
    /// text test, not a proof the pin USES the result. A consumer that calls it
    /// and ignores the answer satisfies this. Direction: FAILS GREEN, and it is
    /// bounded by task 3 being one reviewed commit rather than a drift path.</summary>
    [Fact]
    public void TheClaimsNotScansDisclaimer_MatchesWhetherTheConsumersAreRepointed()
    {
        const string marker = "WILL, NOT DOES";

        // nameof, not a literal: `SetsFor` has NO callers yet, so a rename or a
        // typo in a bare string would be noticed by nothing. It would make every
        // consumer permanently "unrepointed" and point this fact at demanding the
        // false disclaimer stay forever — the exact direction it exists to stop.
        string SetsForMarker =
            $"{nameof(ShellSourceRoots)}.{nameof(ShellSourceRoots.SetsFor)}";

        string[] testSources = Directory.EnumerateFiles(
            Path.Combine(BnRepo.Root(), "tests"), "*.cs", SearchOption.AllDirectories).ToArray();

        var consumers = ShellSourceRoots.Consumers().Keys
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(consumers.Count >= DeclaredConsumerCount,
            $"only {consumers.Count} consumers to check — the roster shrank, and this fact would "
            + "otherwise decide the disclaimer's fate from a short list.");

        var unrepointed = new List<string>();
        foreach (string consumer in consumers)
        {
            string? file = testSources.FirstOrDefault(
                f => Path.GetFileNameWithoutExtension(f).Equals(consumer, StringComparison.Ordinal));

            Assert.True(file is not null,
                $"'{consumer}' is a consumer in {ShellSourceRoots.ManifestPath} and no file named "
                + $"{consumer}.cs exists under tests/. The pin was renamed or removed, so neither "
                + "this fact nor a reader can tell whether it has been repointed — fix the roster "
                + "key deliberately.");

            if (!File.ReadAllText(file!).Contains(SetsForMarker, StringComparison.Ordinal))
                unrepointed.Add(consumer);
        }

        bool disclaimerPresent = File.ReadAllText(Path.Combine(
            BnRepo.Root(), ShellSourceRoots.ManifestPath.Replace('/', Path.DirectorySeparatorChar)))
            .Contains(marker, StringComparison.Ordinal);

        if (unrepointed.Count > 0)
            Assert.True(disclaimerPresent,
                $"{string.Join(", ", unrepointed)} still {(unrepointed.Count == 1 ? "reads" : "read")} "
                + $"{(unrepointed.Count == 1 ? "its" : "their")} own roots rather than calling "
                + $"ShellSourceRoots.SetsFor, and the `{marker}` paragraph is gone from "
                + $"{ShellSourceRoots.ManifestPath}. The roster now reads as if it binds those "
                + "pins. It does not: a consumer can declare that it consumes a set while scanning "
                + "somewhere else entirely and everything stays green. Restore the paragraph, or "
                + "repoint the pins.");
        else
            Assert.False(disclaimerPresent,
                $"every consumer now calls ShellSourceRoots.SetsFor, so the `{marker}` paragraph "
                + $"in {ShellSourceRoots.ManifestPath} is false: it tells the reader these entries "
                + "record what a pin claims rather than what it scans, and they now record both. "
                + "Delete the paragraph — that is the instruction it ends with — and delete this "
                + "fact with it, since it exists only to make the deletion happen.");
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

        // NO TOTAL IS QUOTED HERE ON PURPOSE. The first version said "591 were
        // measured" and the commit that wrote it added a `public void`, so it
        // shipped stale in the round whose subject was three corrected
        // measurements. A lower bound needs no denominator to do its job: this
        // exists to catch the pattern failing wholesale, not to track the suite.
        Assert.True(names.Count >= 500,
            $"extracted only {names.Count} distinct test-method names from {sources.Length} "
            + "files — the `public void Name(` pattern has stopped matching most of the "
            + "suite. Re-point it; do not let citations resolve against a short index.");

        return names;
    }

    private static readonly string BinSegment =
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
    private static readonly string ObjSegment =
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

    /// <summary>Test-method identifiers inside a reason string: an upper-case
    /// head, at least one underscore, and at least one lower-case letter
    /// somewhere in the whole name.
    ///
    /// THE SHAPE WAS WIDENED AFTER MEASURING IT. The first cut required each
    /// underscore-separated half to begin upper-then-lower, and that missed every
    /// one-letter-word head (`ACommentedOutWrap_…`, `AThrowingSink_…`), every
    /// ALL-CAPS segment (`…_STILL_BlocksTheDispatchLane`) and every snake_case
    /// name (`Mount_returns_component_id_for_sync_component`) — dozens of real
    /// tests. Two NONEXISTENT names in those shapes were put in a reason and the
    /// fact passed, so the hole was a false green rather than a theoretical one.
    ///
    /// WHAT IT REACHES IS STATED STRUCTURALLY, not as a ratio, because a ratio
    /// decays the moment anyone adds a test — the first version quoted one and
    /// the same commit invalidated it. This shape matches EVERY declared test
    /// name that contains an underscore. The ones it cannot see are exactly the
    /// ones with no underscore at all, and that sentence stays true.
    ///
    /// THE ALL_CAPS REFUSAL MOVED, it did not go. It is now carried by the
    /// lower-case requirement over the whole identifier rather than by the head
    /// pattern, so `BIOMETRIC_STRONG` and `AUTH_DEVICE_CREDENTIAL` — constants
    /// this repo's reasons legitimately name — still do not match. Verified over
    /// every reason in the roster and ten adversarial spellings: zero false
    /// positives.</summary>
    private static IEnumerable<string> TestIdentifiersIn(string reason) =>
        Regex.Matches(reason, @"\b[A-Z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+\b")
             .Select(m => m.Value)
             .Where(v => v.Any(char.IsLower));

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
            // renamed or commented out yields an empty extracted set, and an empty
            // set compared to an empty roster would be "equal" — two nothings
            // agreeing. Assert the subject was found before comparing it.
            //
            // NOT "reformatted", which is what this comment and the message below
            // used to say. Splitting the argument list across lines PASSES — the
            // capture is `[^)]*` and that matches newlines. The claim was corrected
            // in the doc comment and left standing here, which is the worse half:
            // this text is read at the failure, by someone deciding what broke.
            Assert.True(calls.Count > 0,
                $"could not find a `{g.Call}(` call in {g.File} (pattern: {pattern}). Three "
                + "things do this, all verified: the call was RENAMED, it was COMMENTED OUT — "
                + "the comment-stripping pass runs first — or the directory list moved into a "
                + "VARIABLE, which reds on the next assertion instead. Splitting the argument "
                + "list across lines does NOT do it and is not worth checking; the capture "
                + "matches newlines, measured. A pin that cannot see its subject must never "
                + "pass vacuously, so this reds. Re-point the regex, or change "
                + "`derivedFrom.call` to whatever the build now spells.");

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

    /// <summary>THE TEMPLATE'S ROOT LIST IS NOT A FREE LITERAL EITHER. It used to
    /// be two strings nothing compared. `androidShell` answers to the Gradle
    /// `kotlin.srcDirs` call; this makes `androidTemplateMirror` answer to
    /// `androidShell`, so both multi-root sets in the roster are externally
    /// derived and the generated app's source sets are the repo's with a prefix
    /// swapped — which is what they have to be, because the template's own
    /// build.gradle.kts compiles the same two names.
    ///
    /// WHY IT EXISTS: a root can MOVE between sets without any count noticing.
    /// Taking `…/android/src/main/kotlin` out of `androidTemplateMirror` and
    /// putting it in `androidJvmHost` leaves the total at 9, every array
    /// non-empty, every root on disk, and every file still inside some root —
    /// eight facts green, with the tree that ships to every `dotnet new
    /// blazornative` consumer declaring one of its two source directories.
    ///
    /// THE COVER IS COMPLETE, and that is worth stating because it is why no
    /// aggregate is needed here. A root leaving a SINGLE-root set takes it to
    /// zero, which `EveryRoot_ExistsOnDisk` reds on per set. The roster has
    /// exactly two multi-root sets: `androidShell`, held by the Gradle
    /// derivation, and `androidTemplateMirror`, held here. A third would arrive
    /// uncovered — so if one does, give it a derivation rather than a count.
    ///
    /// LIMIT: it compares root LISTS, not the trees behind them. The template
    /// genuinely having those directories is `EveryRoot_ExistsOnDisk`; their
    /// contents being a byte mirror is TemplateDriftTests'.</summary>
    [Fact]
    public void TheTemplateMirrorRoots_MirrorTheShellRoots()
    {
        IReadOnlyDictionary<string, ShellSourceRoots.SetDef> sets = TheWholeRoster();

        var mirrors = sets.Values.Where(x => x.MirrorOf is not null).ToList();
        Assert.True(mirrors.Count >= 1,
            $"no set in {ShellSourceRoots.ManifestPath} carries a `mirrorOf` block, so every "
            + "multi-root set but `androidShell` is back to being a free list of strings. The one "
            + "that needs it is `androidTemplateMirror`; if it was renamed, re-point this "
            + "deliberately.");

        foreach (ShellSourceRoots.SetDef mirror in mirrors)
        {
            ShellSourceRoots.MirrorSource m = mirror.MirrorOf!;
            ShellSourceRoots.SetDef source = RequireSet(sets, m.Set);

            Assert.True(source.DerivedFrom is not null,
                $"set '{mirror.Name}' mirrors '{m.Set}', and '{m.Set}' has no `derivedFrom` block. "
                + "The whole value of a mirror is that it inherits an EXTERNAL derivation; "
                + "mirroring a set that answers only to itself makes two free lists out of one.");

            var expected = new SortedSet<string>(
                source.Roots.Select(r => m.PathPrefix + r[source.DerivedFrom!.PathPrefix.Length..]),
                StringComparer.Ordinal);
            var actual = new SortedSet<string>(mirror.Roots, StringComparer.Ordinal);

            Assert.True(actual.SetEquals(expected),
                $"set '{mirror.Name}' and the set it mirrors, '{m.Set}', disagree about which "
                + "source sets the Android shell has.\n"
                + $"  mirrored from {m.Set}: {string.Join(", ", expected)}\n"
                + $"  declared on {mirror.Name}: {string.Join(", ", actual)}\n"
                + "The generated app compiles the same source-set names the repo does — its own "
                + "build.gradle.kts says so — so a root here that the shell does not have, or a "
                + "shell root missing here, means the template ships a tree no pin walks. A root "
                + "MOVED into another set is the case no count can see: the total is unchanged and "
                + "every array is still non-empty.");
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

        Assert.True(auth.RootElement.TryGetProperty("sites", out JsonElement authSites),
            "src/auth-semantics.json has no top-level `sites` array, so this guard cannot tell "
            + "which shell file the delegation is about. The manifest was restructured — re-point "
            + "this deliberately rather than deleting the guard.");

        var authFiles = authSites.EnumerateArray()
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

            // A NEGATIVE MEMBERSHIP TEST OVER A REGEX-PARSED LIST, which is the
            // fails-green combination limit 6 is about: `divergent` holds only
            // what the collection INITIALISER spells, so a file added to that set
            // any other way is invisible here and this passes. Task 5 replaces the
            // parse with the byte-identity set itself.
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

        // A TOTAL, AND ONE OF THIS FILE'S TWO CALLERS LEANS ON IT THE WRONG WAY.
        // For the content manifest the consumer is a POSITIVE membership test, so
        // a narrowed parse reds. For `divergent` it is a NEGATIVE one, and `> 0`
        // says nothing about any of the five entries. Do not read this as a
        // per-member assertion; it is not, and an earlier version of limit 5 said
        // it was. Limit 6 has the measurement and the assigned repair.
        Assert.True(entries.Count > 0,
            $"{what} matched but parsed to ZERO entries. Comparing against an empty set would "
            + "make every membership question answer the convenient way — re-point the parse.");

        return entries;
    }
}
