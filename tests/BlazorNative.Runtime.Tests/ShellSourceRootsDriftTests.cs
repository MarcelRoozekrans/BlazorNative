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
//  3. THE ROSTER NOW BINDS, AND WHAT IS LEFT OF THE OLD LIMIT IS NARROWER AND
//     WORTH READING RATHER THAN SKIMMING. This entry used to say the roster
//     recorded what a pin CLAIMS and nothing more, because no pin read it.
//     All three now take their roots from `ShellSourceRoots.SetsFor` and
//     `EveryConsumer_ReadsItsRootsFromTheRoster` reds if one stops, so the
//     claim and the behaviour are one object — that is #364 F1 and F2 closed
//     as a consequence of the roster rather than as two patches.
//
//     THE TEXT GUARD IS NOT WHAT CLOSES IT, AND THE FIRST DRAFT OF THIS ENTRY
//     SAID THE OPPOSITE THREE TIMES. It said the residual — a pin that calls
//     `SetsFor` and then walks a hard-coded path anyway — needed dataflow
//     analysis and could not be closed by a test. That was FALSE, and the
//     counterexample was in the same commit: `AndroidLogDriftTests` already
//     carried a two-line per-root coverage assertion doing exactly this. The
//     claim was measured false, not argued false — reverting one wrapper method
//     in the auth pin while leaving the name `SetsFor` elsewhere in the file
//     reopened #364 F1 at 15 passed / 0 failed, and the coverage assertion is
//     what reds on it.
//
//     SO WHAT BINDS THE ROSTER IS BEHAVIOURAL AND LIVES IN `ShellSourceScan`:
//     every file the roster puts in a pin's scope must have been READ by the walk
//     that pin's assertion consumes, and the code that records the read is the
//     code that performs it. A pin cannot hold a root list at all — the doors
//     take a consumer name and an optional set name, so the round-1 and round-2
//     defeats are a COMPILE ERROR rather than a red.
//     `EveryConsumer_ReadsItsRootsFromTheRoster` is the cheap first line that
//     catches the blunt revert; the coverage proof inside the scan is what makes
//     the claim true.
//
//     THIS PARAGRAPH HAS BEEN WRONG IN EVERY ROUND IT WAS WRITTEN, which is the
//     most useful thing about it. It described naming the door, then calling it
//     per site, then "at least one file per root" — each true when written and
//     each superseded by a measurement a round later. The residuals that survive
//     measurement are at `ShellSourceScan`, beside the code, and are the only
//     ones to trust.
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
//     AND THE INDEX IS BUILT FROM RAW SOURCE, so a COMMENTED-OUT test still
//     resolves a citation. That is disclosed at `DeclaredTestMethods` and was
//     missing from this list, which is the decay Rule 5's subtle half warns
//     about: a limit stated in one place and omitted from the summary a reader
//     trusts. The choice is deliberate -- a name is being resolved, not a
//     behaviour asserted, and a citation this cannot SEE at all is the worse
//     failure -- but it belongs in both places. Direction: FAILS GREEN.
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
//     to misclassify.
//
//     AND THE LIST BELOW IS ITSELF CHECKED, because the round that wrote the
//     first one omitted the floor that same commit added, and the round after
//     it omitted three more. An enumeration nobody enforces decays exactly as
//     fast as a comment. `EveryFloorInThisFile_IsNamedInTheInventory` scans
//     this file for every `Assert.True` whose condition contains `>=` or
//     `> 0` and requires the condition text to appear between the markers
//     below. Add a floor without naming it here and the suite reds.
//
//     THE INVENTORY. Each entry is the condition verbatim, and what makes the
//     property hold. An `== 0` assertion is a SUBJECT check, not a floor, and
//     is deliberately out of scope.
//
// ── FLOOR INVENTORY ─────────────────────────────────────────────────────────
//
//       CARDINALITY OVER THE PARTITION, exact by necessity -- there is no
//       second record of how many there ought to be, so the number IS the
//       record, which is why none of these may be `> 0`:
//         `sets.Count >= DeclaredSetCount`
//         `consumers.Count >= DeclaredConsumerCount`   -- twice, two facts
//         `scanRoots.Length >= ScanRootCount`
//         `floors.Count >= FloorCount`                 -- this inventory's own
//
//       DERIVED, so not classifiable and not arguable:
//         `checkedRoots >= declaredRoots`   -- the sum of the per-set root counts
//         `scanned >= expected`             -- the sum of the per-container floors
//
//       A RELATION between two numbers the manifest already holds:
//         `sr.MinFiles >= 1 && sr.MinFiles <= sr.Measured`
//
//       PER MEMBER, inside the loop that finds the member:
//         `s.Roots.Length > 0`
//         `contributed >= sr.MinFiles`
//         `hits.Count > 0`              -- twice: the derivation pattern, and
//                                          RegionAt's anchor search
//         `declaredByBuild.Count > 0`
//
//       ONE WALK, NO PARTITION BELOW. Both fail LOUD: a narrowed index makes a
//       real citation unresolvable rather than forgiving an unreal one:
//         `sources.Length >= 100`
//         `names.Count >= 500`
//
//       TOTALS OVER A POPULATION WHOSE MEMBERS ARE EACH ASSERTED AS THE LOOP
//       FINDS THEM. Single-member today except `derived.Count`, which is six --
//       and that one is backed by `EveryRootList_IsExternallyDerived`, which
//       reds if any set loses its derivation, so the total is not carrying it:
//         `delegations >= 1`
//         `cited.Count >= 1`
//         `derived.Count >= 1`
//         `mirrors.Count >= 1`
//         `authFiles.Count >= 1`
//
//       NOT A VACUITY FLOOR AT ALL, and listed anyway because the scan is
//       mechanical and the concept is not. This is a Rule 4 subject-moved
//       guard that happens to spell itself with `>=`; the inventory names
//       what the scan finds, never what a reader thinks ought to count:
//         `from >= 0 && to > from`  -- the inventory markers were found
//
//       THE ONE WITH SOMETHING FAILS-GREEN RESTING ON IT:
//         `entries.Count > 0`  -- NOT per-member and NOT in a loop; an earlier
//         taxonomy put it in the per-member bucket and that was wrong. Limit 6
//         has the measurement and the assigned repair.
//
// ── END FLOOR INVENTORY ─────────────────────────────────────────────────────
//
//     WHAT IS STILL AN INDEPENDENT LITERAL, after all of that: the four
//     cardinalities, the six manifest observations, and the two walk floors.
//     Nothing in the repository can derive "how many sets there ought to be",
//     so that residual is real and is where it stops.
//  6. THE DELEGATION GUARD READS ANOTHER TEST'S SOURCE WITH A REGEX, AND
//     THAT REGEX SEES ONLY THE COLLECTION INITIALISER. `QuotedEntries`
//     captures the text between `{` and `};`, so entries added to
//     TemplateDriftTests' `divergent` set by any other route -- an
//     `Add(...)` call after the initialiser, a loop, a second collection
//     unioned in -- are invisible. MEASURED in review: an `Add` of the
//     auth-bearing file immediately below the initialiser removed it from
//     the byte comparison and EVERY fact across both pins passed, with a
//     real byte divergence in place. Adding the same file INSIDE
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
    /// declares -- see <see cref="ExternalRecord"/>.</summary>
    internal sealed record SetDef(
        string Name, string Language, string Purpose, string[] Roots,
        ExternalRecord? DerivedFrom, MirrorSource? MirrorOf);

    /// <summary>A set whose roots are another set's roots with the path prefix
    /// swapped. It is how a root list stops being a free literal: the template
    /// mirror's two source sets answer to the same Gradle call the repo's do,
    /// transitively, instead of being a pair of strings nothing compares.</summary>
    internal sealed record MirrorSource(string Set, string PathPrefix);

    /// <summary>Where a set's roots really come from: a build file, a nest of
    /// anchors locating the block inside it, and a pattern whose group 1 captures
    /// the region holding the paths. Prefixed with <paramref name="PathPrefix"/>,
    /// those paths must equal the set's roots.
    ///
    /// ONE SHAPE FOR TWO LANGUAGES. Gradle spells a source set
    /// <c>kotlin.srcDirs("a", "b")</c> and XcodeGen spells one <c>- path: BnHost</c>,
    /// so the extractor takes group 1 as a REGION: quoted strings inside it if
    /// there are any, otherwise the region itself. That covers both without a
    /// per-language branch, and it is why <c>pattern</c> lives in the manifest
    /// rather than in the test.</summary>
    internal sealed record ExternalRecord(
        string File, string[] Within, string Describes, string Pattern, string PathPrefix);

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
        Consumer c = ConsumerOrThrow(consumer);

        var roots = new List<string>();
        foreach (string set in c.Consumes)
            roots.AddRange(RootsOfConsumedSet(consumer, set));

        if (roots.Count == 0)
            throw new InvalidOperationException(
                $"'{consumer}' resolves to ZERO roots from {ManifestPath}. A pin with no subject "
                + "scans nothing and passes — fix the roster, do not let the caller proceed.");

        return [.. roots];
    }

    /// <summary>THE SAME DOOR, ONE SET AT A TIME — for a consumer that treats its
    /// consumed sets DIFFERENTLY and therefore cannot use the flattened list.
    ///
    /// <c>NSLogDriftTests</c> is the case and the reason this overload exists:
    /// it scans <c>appleShell</c> for offenders and uses <c>appleTestBundle</c>
    /// as its positive control, which must still hold live <c>NSLog</c> calls.
    /// Flattening the two is exactly what loses that distinction, so the pin
    /// names the set — and naming a set the roster says this consumer does NOT
    /// consume throws, rather than quietly scanning a tree the consumer's own
    /// entry disclaims. That check is what keeps this from being a back door
    /// around the partition: the set still has to be declared, by this consumer,
    /// as consumed.
    ///
    /// Throws, never returns an empty array, for the same reason the flattened
    /// overload does.</summary>
    internal static string[] SetsFor(string consumer, string set)
    {
        Consumer c = ConsumerOrThrow(consumer);

        if (!c.Consumes.Contains(set, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"'{consumer}' asked for the roots of '{set}', which is not in its `consumes` "
                + $"list in {ManifestPath} — it is delegated, excluded, or absent. A pin may only "
                + "scan a tree it has declared it consumes; scanning one it disclaims makes the "
                + "roster's reason and the pin's behaviour two different things, which is the "
                + "#364 F1 shape in the other direction.");

        return RootsOfConsumedSet(consumer, set);
    }

    /// <summary>One set's roots, resolved for a named consumer. The one place
    /// either overload turns a set name into paths, so there is no second copy of
    /// the resolution to diverge from the first (pin standard, Rule 8).</summary>
    private static string[] RootsOfConsumedSet(string consumer, string set)
    {
        if (!Sets().TryGetValue(set, out SetDef? s))
            throw new InvalidOperationException(
                $"'{consumer}' consumes '{set}', which is not a set in {ManifestPath}. A typo "
                + "here silently drops a tree from the pin's subject.");

        if (s.Roots.Length == 0)
            throw new InvalidOperationException(
                $"'{set}' declares no roots in {ManifestPath}, so '{consumer}' would scan nothing "
                + "for a tree it says it covers. Fix the roster, do not let the caller proceed.");

        return s.Roots;
    }

    /// <summary>The consumer entry, or a throw naming what to do about it.</summary>
    private static Consumer ConsumerOrThrow(string consumer)
    {
        if (!Consumers().TryGetValue(consumer, out Consumer? c))
            throw new InvalidOperationException(
                $"'{consumer}' is not a consumer in {ManifestPath} — the pin was renamed, or its "
                + "entry was never written. Add it, with its three lists, rather than reaching "
                + "past the roster: a pin whose coverage position is unrecorded is the #364 F1 "
                + "shape all over again.");
        return c;
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
            ExternalRecord? derived = null;
            if (p.Value.TryGetProperty("derivedFrom", out JsonElement d))
                derived = new ExternalRecord(
                    d.GetProperty("file").GetString()!,
                    [.. d.GetProperty("within").EnumerateArray().Select(x => x.GetString()!)],
                    d.GetProperty("describes").GetString()!,
                    d.GetProperty("pattern").GetString()!,
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

// THE ONE WALK over a consumer's declared shell source, and the thing
// that makes the roster BIND rather than merely be named.
//
// ── WHY THIS TYPE EXISTS AT ALL ─────────────────────────────────────────────
//
// Three rounds of review each bound one link further along a chain, and each
// time the assertion that mattered consumed something a line or two downstream
// of what the guard watched:
//
//   round 1 — the pin NAMES `SetsFor`  → defeated by a private wrapper method
//   round 2 — the pin CALLS `SetsFor` per site → defeated because the walk and
//             the coverage assertion called it through the same wrapper
//   round 3 — the walk RECORDS what it enumerated → defeated by a one-line
//             content filter between the listing and the read, and by a second
//             walk inside the fact that asserts the absence
//   round 4 — the record was written in the ARGUMENT carrying the bytes →
//             defeated by a one-line filter INSIDE that argument, because a
//             record handed a value cannot say where the value came from
//
// The common cause was always the same: the guard was bound to a FILE-LIST
// PRODUCER while the absence assertion consumed something else. So this type
// stops moving along the chain and binds to its end. Three properties, and each
// one closed a demonstrated one-line defeat:
//
//  1. THE COVERAGE RECORD IS WRITTEN BY THE CODE THAT PERFORMS THE READ.
//     `Recorded` takes a PATH and opens the file itself. A guard that RECEIVES a
//     value cannot verify how that value was obtained; it can only verify what it
//     obtains itself.
//
//     THE PREVIOUS VERSION LOOKED IDENTICAL AND WAS NOT. It took the content as
//     an argument, and its comment claimed there was "no statement between the
//     read and the record into which a filter can be inserted". True, and
//     irrelevant: the filter does not need a statement, it needs an expression.
//     `Recorded(read, file, file.Contains("/main/kotlin/") ? "" :
//     File.ReadAllText(path))` is one line, sits INSIDE the fused argument, and
//     was MEASURED at 24 passed / 0 failed with two planted defects live in the
//     exact tree #364 F1 is about. That sentence is deleted rather than softened.
//
//  2. THE FACT THAT ASSERTS AN ABSENCE OBSERVES THE RECORD ITS OWN CALL
//     PRODUCED. Every fact calls this itself, and the call that proves coverage
//     RETURNS the value the assertion consumes, so proving one thing and
//     asserting on another needs new code rather than a different variable.
//
//  3. NO PIN HOLDS A ROOT LIST. Callers name a CONSUMER, and the roots are
//     resolved here from the roster. There is no array in a pin to re-point, so
//     the round-1 and round-2 defeats are not expressible rather than merely
//     detected.
//
// And one hardening that closes the obvious next move PARTLY, which is all it
// does: THE EXTRACTOR IS NEVER TOLD WHICH FILE IT IS LOOKING AT. It receives
// content and returns RawHits carrying a line number; this type stamps the path
// on afterwards. That removes the PATH-keyed filter from a matcher. It does NOT
// remove a CONTENT-keyed one, because content is a proxy for path — see limit A,
// which is measured. An earlier draft said a filter inside a matcher was "not
// expressible either"; it is expressible, and the word was wrong.
//
// ── WHAT THIS STILL DOES NOT COVER (pin standard, Rule 5) ───────────────────
//
// EVERY LINE BELOW WAS MEASURED BY MUTATION, and two of them contradict what an
// earlier draft of this header asserted. That draft claimed the shared matcher
// was protected by the positive controls running through it. IT IS NOT, and the
// claim is deleted rather than softened: an unenforced safety sentence is the
// bug class this whole phase exists to close, and writing one INTO the machinery
// built to close it is the worst available place for it.
//
//  A. A CONTENT-KEYED FILTER INSIDE A MATCHER. MEASURED GREEN. One line —
//     `text.Contains("package io.blazornative.jni") ? [] : …` inside
//     `ForPattern` — hid a planted bare `Log.i` from all 24 facts. The extractor
//     cannot see a PATH, which is why every earlier defeat's path filter is gone,
//     but CONTENT IS A PROXY FOR PATH: a Kotlin package declaration or a Swift
//     import names the tree just as well.
//
//     AND THE POSITIVE CONTROL DOES NOT SAVE IT, AND THERE IS ONLY ONE.
//     `TheTestBundleExemption_IsRealAndStillHoldsNSLog` runs through this
//     matcher, so a filter broad enough to blind it reds — but a filter keyed
//     on one package is not broad enough, and that control lives in another
//     package. `TheInstrumentedTestExemption_IsRealAndStillHoldsBareLogI` does
//     NOT run through here at all: its tree is roster-EXCLUDED, so it walks
//     itself and only shares the PATTERN. An earlier draft named both, which
//     overstated the cover by exactly one control — measured: a total blind of
//     `ForPattern` reds 5 facts and that one is not among them.
//     Direction: FAILS GREEN.
//
//  B. REWRITING A FACT'S BODY. MEASURED GREEN, twice: reassigning the hit array
//     from a freshly written second walk, and `.Where(…)`-ing the array before
//     the assertion reads it. Both are 1–9 lines INSIDE the fact.
//
//     This one is not a defect that a guard can close, and saying so is not a
//     shrug. No assertion in any repository survives its own body being
//     rewritten; what a pin can do is make the rewrite VISIBLE and make the
//     cheap version unavailable, and that part is done — there is no leftover
//     walk to filter, no root list to re-point, and the coverage proof is fused
//     into the call that yields the value the assertion consumes, so the shapes
//     below are all that is left and every one of them shows up in a diff as new
//     code rather than as a changed constant.
//
//  C. WHAT IS CLOSED, each verified by a mutation that now REDS where it used to
//     pass:
//       - re-pointing a pin at a hard-coded root list — NOT EXPRESSIBLE. No door
//         a pin can call takes roots; they take a consumer name and an optional
//         set name, both checked against the roster.
//       - a content filter between the listing and the read — REDS, 7 facts.
//       - a filter INSIDE the fused record argument — REDS, and it is what
//         forced `Recorded` to perform the read rather than be handed bytes.
//       - a filter narrowed to exactly ONE file — REDS, 7 facts. This is what
//         forced coverage from "at least one file per root" to SET EQUALITY: the
//         loose form was measured green on a package-wide filter, with a planted
//         offender escaping its own fact and only a sibling's count floor
//         noticing.
//       - an early return part-way through the walk — REDS, 7 facts.
//       - a second walk SHARING one coverage assertion — no longer possible:
//         every fact calls the scan itself and the coverage proof is fused to the
//         value it consumes.
internal static class ShellSourceScan
{
    /// <summary>A finding as the EXTRACTOR reports it: a line number against the
    /// original file, the text, and whatever the matcher wants to call it. NO
    /// PATH — see the type header. <see cref="Over"/> stamps the file on.</summary>
    internal sealed record RawHit(int Line, string Text, string Token);

    /// <summary>A finding with its file attached.</summary>
    internal sealed record Hit(string File, int Line, string Text, string Token);

    /// <summary>What one scan found AND what it actually read. The second half is
    /// the point: an empty findings list cannot distinguish "this tree is clean"
    /// from "this tree was never opened", and every pin using this asserts an
    /// absence.</summary>
    internal sealed record Result(
        string Consumer, string? Set, string[] Roots, string[] Extensions,
        Hit[] Hits, string[] Read)
    {
        /// <summary>THE COVERAGE ASSERTION, over the record THIS result carries.
        /// Every fact that asserts an absence must call this on its own result —
        /// that is property 2, and the reason it is a method on the result rather
        /// than a free function taking a root list is so that it cannot be handed
        /// one walk's roots and another walk's files.</summary>
        /// <summary>The findings, but only after proving the scan read everything the
        /// roster puts in its scope. THE TWO ARE ONE CALL ON PURPOSE: a fact that
        /// holds a <c>Result</c> can assert coverage on it and then hand its
        /// assertion a different collection, which is the "guard watches producer A,
        /// assertion consumes producer B" shape three review rounds kept finding. A
        /// fact that only ever holds the ARRAY this returns has nothing to swap.
        /// </summary>
        internal Hit[] HitsCoveringEveryDeclaredRoot() => CoveringEveryDeclaredRoot().Hits;

        /// <summary>Both halves, for a fact that needs the findings AND the file
        /// list. One call, one scan, one coverage proof.</summary>
        internal (Hit[] Hits, string[] Read) CoveringEveryDeclaredRoot()
        {
            AssertItReadEveryDeclaredRoot();
            return (Hits, Read);
        }

        /// <summary>The files read, after the same proof. Same reasoning.</summary>
        internal string[] ReadCoveringEveryDeclaredRoot() => CoveringEveryDeclaredRoot().Read;

        private void AssertItReadEveryDeclaredRoot()
        {
            string repo = BnRepo.Root();
            var shortfall = new List<string>();

            foreach (string root in Roots)
            {
                string prefix = root.TrimEnd('/') + "/";
                string dir = Path.Combine(repo, root.Replace('/', Path.DirectorySeparatorChar));

                // THE CANDIDATE SET IS ENUMERATED HERE, INDEPENDENTLY OF THE WALK.
                // That independence is the whole mechanism: whatever the walk did or
                // skipped, this recomputes what it was supposed to read straight from
                // the roster's root and the scan's own declared extensions.
                var candidates = Directory.Exists(dir)
                    ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                        .Where(f => Extensions.Contains(
                            Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                        .Select(f => Path.GetRelativePath(repo, f).Replace('\\', '/'))
                        .ToHashSet(StringComparer.Ordinal)
                    : [];

                // A ROOT HOLDING NO FILE OF THE SCANNED KINDS IS NOT A DEFECT. This pin
                // family runs language-specific scans over a two-language roster: the
                // auth pin's Kotlin-only caller count legitimately reads nothing under
                // an iOS target, and demanding otherwise is a permanent false red. An
                // empty root is what EveryRoot_ExistsOnDisk and
                // EveryShellSourceFile_IsInsideADeclaredRoot are already looking at.
                if (candidates.Count == 0) continue;

                // EVERY CANDIDATE, NOT MERELY ONE — and this is the strengthening that
                // a mutation forced rather than a tidiness. "At least one file per
                // root" was MEASURED defeated: a one-line content filter dropped every
                // file of one Kotlin package before the read, the root still had other
                // files under it so coverage stayed green, and a planted bare `Log.i`
                // escaped its own offender fact. Only a sibling's file-count floor
                // caught it, and a filter one file narrower would have cleared that
                // too. Set equality has no such gap: any file the roster says is in
                // scope and the scan did not read is named here.
                var missed = candidates
                    .Where(c => !Read.Contains(c, StringComparer.Ordinal))
                    .OrderBy(c => c, StringComparer.Ordinal)
                    .ToList();

                if (missed.Count > 0)
                    shortfall.Add($"  {root} — READ {candidates.Count - missed.Count} of "
                        + $"{candidates.Count}, missing:\n"
                        + string.Join("\n", missed.Select(m => "      " + m)));
            }

            Assert.True(shortfall.Count == 0,
                "THE SCAN DID NOT READ EVERY FILE THE ROSTER PUTS IN ITS SCOPE:\n"
                + string.Join("\n", shortfall)
                + $"\n\n{ShellSourceRoots.ManifestPath} says {Consumer} consumes "
                + (Set is null ? "these roots" : $"these roots as `{Set}`")
                + $", and this scan's own extensions ({string.Join(", ", Extensions)}) match the "
                + "files listed above, yet their content never reached the matcher. Coverage is "
                + "recorded in the same expression that hands a file's bytes to the matcher, so "
                + "this is not 'a path was listed' — it is 'the content never arrived'. Either "
                + "the scan was narrowed away from the roster, which is #364 F1, or a tree moved "
                + "and the roster needs re-pointing deliberately.");
        }
    }

    /// <summary>Walk one consumer's declared roots, READ every file with a listed
    /// extension, and hand each file's content to <paramref name="extract"/>.
    ///
    /// <paramref name="set"/> names ONE consumed set for a pin that treats its
    /// sets differently — `NSLogDriftTests` scans `appleShell` and uses
    /// `appleTestBundle` as its positive control — and is null for a pin that
    /// takes everything it consumes.
    ///
    /// The caller passes a CONSUMER NAME, never roots: there is deliberately no
    /// overload taking a root list, because a pin holding a root list is the
    /// defect this type was built after.</summary>
    internal static Result Over(
        string consumer, string? set, string[] extensions,
        Func<string, IEnumerable<RawHit>> extract)
    {
        string[] roots = set is null
            ? ShellSourceRoots.SetsFor(consumer)
            : ShellSourceRoots.SetsFor(consumer, set);

        string repo = BnRepo.Root();
        var hits = new List<Hit>();
        var read = new List<string>();

        foreach (string rel in roots)
        {
            string dir = Path.Combine(repo, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(dir),
                $"declared root '{rel}' does not exist, so {consumer} would scan nothing there "
                + "and report it as clean. It is declared in " + ShellSourceRoots.ManifestPath
                + " — re-point the roster deliberately rather than narrowing the walk.");

            foreach (string path in Directory
                         .EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                         .Where(f => extensions.Contains(
                             Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                string file = Path.GetRelativePath(repo, path).Replace('\\', '/');

                // ── PROPERTY 1, AND IT IS THIS LINE ──────────────────────────
                // `Recorded` PERFORMS THE READ. It is handed a PATH, never bytes,
                // and that distinction is the whole of the property rather than a
                // detail of it: A GUARD THAT RECEIVES A VALUE CANNOT VERIFY HOW
                // THAT VALUE WAS OBTAINED -- it can only verify what it obtains
                // itself.
                //
                // The previous version took the content as an argument, which
                // looked identical and was not. MEASURED: one line, inside the
                // fused expression rather than before it --
                // `Recorded(read, file, file.Contains("/main/kotlin/") ? "" :
                // File.ReadAllText(path))` -- left all 24 facts green with two
                // planted defects live in the exact tree #364 F1 is about. The
                // filter did not need to sit between the read and the record; it
                // sat inside the argument, where the record could not see it.
                foreach (RawHit h in extract(Recorded(read, repo, path)))
                    hits.Add(new Hit(file, h.Line, h.Text, h.Token));
            }
        }

        return new Result(consumer, set, roots, extensions, [.. hits], [.. read]);
    }

    /// <summary>The regex-over-code-lines shape four pin facts want, with the
    /// matching loop living HERE rather than in each pin. That is deliberate:
    /// a loop in a pin is a place to insert a one-line filter, and the three
    /// demonstrated defeats were all exactly that. Comments are stripped through
    /// the shared helper, from content already read, so the file is opened
    /// once.</summary>
    internal static Result ForPattern(
        string consumer, string? set, string[] extensions, string pattern)
        => Over(consumer, set, extensions, text =>
            CommentStrippedSource.NumberedCodeLinesOf(text)
                .SelectMany(l => Regex.Matches(l.Text, pattern)
                    .Select(m => new RawHit(l.Number, l.Text.Trim(), m.Value))));

    /// <summary>READS <paramref name="path"/>, and records the file it read —
    /// naming it from THAT SAME PATH.
    ///
    /// Two things are deliberate and both were forced by a measurement rather
    /// than chosen. It takes a PATH rather than CONTENT, because a version handed
    /// the bytes could be handed any bytes at all: one line inside the argument,
    /// `… ? "" : File.ReadAllText(path)`, left all 24 facts green with two live
    /// planted defects. And it derives the recorded NAME from the path it opened
    /// rather than taking the name as a second argument, because two arguments
    /// can be desynchronised: substitute the path and keep the name and the
    /// record says a file was covered while different bytes were scanned.
    ///
    /// Both collapse to one rule. THE RECORD MUST BE A STATEMENT ABOUT WHAT THIS
    /// METHOD DID, derived from the single value it was given, never a report
    /// about what its caller says it did.</summary>
    private static string Recorded(List<string> read, string repo, string path)
    {
        string text = File.ReadAllText(path);
        read.Add(Path.GetRelativePath(repo, path).Replace('\\', '/'));
        return text;
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

    /// <summary>The number of distinct floor conditions limit 5's inventory was
    /// written against. A cardinality, for the reason the inventory gives: there
    /// is no second record of how many floors this file ought to have.</summary>
    private const int FloorCount = 20;

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

    /// <summary>THE INVENTORY OF FLOORS IS ITSELF ENFORCED. Limit 5 lists every
    /// floor in this file and what makes its property hold. Two successive rounds
    /// shipped that list incomplete — the first omitted the floor its own commit
    /// added, the second omitted three more — which is the same failure as an
    /// unclassified floor, one level up. A list nobody checks decays exactly as
    /// fast as any other comment.
    ///
    /// So a floor must name itself. This scans this file for every
    /// <c>Assert.True</c> whose condition contains <c>&gt;=</c> or <c>&gt; 0</c>,
    /// and requires that condition, verbatim, between the inventory markers.
    ///
    /// AN `== 0` ASSERTION IS OUT OF SCOPE ON PURPOSE. Those are SUBJECT checks —
    /// "no consumer is unaccounted for", "no file is an orphan" — and they are
    /// what the floors exist to protect. Pulling them in would double the list
    /// with entries that have nothing to say about vacuity.
    ///
    /// THE EXTRACTOR IS A SCANNER, NOT A LINE REGEX, and that was a live hole
    /// rather than a preference. The first cut matched a condition only when the
    /// line ENDED after it, so a floor written
    /// <c>Assert.True(x &gt;= 1, "msg");</c> — condition and message on one line,
    /// the ordinary way to write a short assertion — was invisible. Adding
    /// exactly that was MEASURED green against this very fact. It now reads to
    /// the first comma at paren depth one, through strings and char literals,
    /// wherever the line breaks fall.
    ///
    /// LIMITS, both FALSE RED. A condition WRAPPED across lines reds asking to be
    /// unwrapped, because the inventory quotes conditions verbatim and a
    /// re-indented one would never match its entry. And it matches TEXT, so
    /// renaming a local reds until the inventory is updated — which is the
    /// point.</summary>
    [Fact]
    public void EveryFloorInThisFile_IsNamedInTheInventory()
    {
        string source = File.ReadAllText(Path.Combine(
            BnRepo.Root(), "tests", "BlazorNative.Runtime.Tests",
            "ShellSourceRootsDriftTests.cs"));

        const string open = "── FLOOR INVENTORY ";
        const string close = "── END FLOOR INVENTORY ";
        int from = source.IndexOf(open, StringComparison.Ordinal);
        int to = source.IndexOf(close, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from,
            $"could not find the inventory markers `{open}` and `{close}` in this file. They "
            + "bound the list limit 5 tells the next reader to trust, so without them this fact "
            + "would approve every floor by finding them all in the whole file. Restore the "
            + "markers; do not delete this fact.");

        string inventory = source[from..to];

        // Comment-stripped, so a commented-out assertion is not counted and the
        // inventory's own backticked copies cannot satisfy the scan. The
        // lookbehind keeps this fact's own `"Assert.True("` search token, which
        // survives stripping as a string literal, out of its own population.
        string code = CommentStrippedSource.Strip(source);

        var floors = Regex.Matches(code, @"(?<![\w""])Assert\.True\(")
            .Select(m => FirstArgument(code, m.Index + m.Length))
            .Where(c => c.Contains(">=", StringComparison.Ordinal)
                        || c.Contains("> 0", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var wrapped = floors.Where(c => c.Contains('\n')).ToList();
        Assert.True(wrapped.Count == 0,
            "FLOOR CONDITIONS WRAPPED ACROSS LINES:\n"
            + string.Join("\n", wrapped.Select(c => "  " + c.Replace("\n", " / ")))
            + "\n\nThe inventory quotes conditions verbatim, so a wrapped one can never match "
            + "its entry and would be reported as unnamed forever. Put the condition on one "
            + "line; if it is too long for that, it is too complicated to be a floor.");

        Assert.True(floors.Count >= FloorCount,
            $"found only {floors.Count} distinct floor conditions in this file and the inventory "
            + $"is written against {FloorCount}. Either a floor was deleted — then delete its "
            + "inventory entry and edit FloorCount in the same commit — or the scan has stopped "
            + "seeing its subject, in which case every remaining floor is being approved without "
            + "being read.");

        var unnamed = floors
            .Where(c => !inventory.Contains("`" + c + "`", StringComparison.Ordinal))
            .ToList();

        Assert.True(unnamed.Count == 0,
            "FLOORS THAT NAME THEMSELVES NOWHERE IN THE INVENTORY:\n"
            + string.Join("\n", unnamed.Select(c => "  " + c))
            + "\n\nEvery floor in this file must appear in limit 5's inventory, in backticks, "
            + "with what makes its property hold — a cardinality over the partition, a derivation, "
            + "a relation, a per-member assertion, or a total whose members are each asserted as "
            + "the loop finds them. This fact exists because two rounds shipped that list "
            + "incomplete, one of them omitting the floor the same commit introduced. Write the "
            + "entry; do not widen the markers.");
    }

    /// <summary>The first argument of a call whose open paren is at
    /// <paramref name="start"/>: everything up to the first comma at depth one,
    /// skipping over nested parens, string literals and char literals. A regex
    /// cannot do this — the first cut used one, anchored to end-of-line, and a
    /// floor with its message on the same line was invisible to it.</summary>
    private static string FirstArgument(string code, int start)
    {
        int depth = 1;
        for (int i = start; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '"' || c == '\'')
            {
                char quote = c;
                i++;
                while (i < code.Length && code[i] != quote)
                    i += code[i] == '\\' ? 2 : 1;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return code[start..i].Trim();
            else if (c == ',' && depth == 1) return code[start..i].Trim();
        }
        return code[start..].Trim();
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

        // THE DUAL OF THE WALK BELOW, and it needs no number at all. The orphan
        // half asks whether every FILE under a container sits inside a declared
        // root. This asks the other direction: whether every declared ROOT sits
        // inside a container. Without it a container can be re-pointed one level
        // deeper and the roots it used to hold simply stop being looked at —
        // measured green with `minFiles: 1` and `measured: 1`, which is the edit
        // the per-container floor cannot refuse because both of its numbers are
        // free. This refuses it on structure instead.
        var uncontained = TheWholeRoster().Values
            .SelectMany(x => x.Roots.Select(r => (Set: x.Name, Root: r)))
            .Where(x => !scanRoots.Any(sr =>
                x.Root.Equals(sr.Path, StringComparison.OrdinalIgnoreCase)
                || x.Root.StartsWith(sr.Path + "/", StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"  {x.Set} declares {x.Root}")
            .ToList();

        Assert.True(uncontained.Count == 0,
            "DECLARED ROOTS LIE OUTSIDE EVERY scanRoot:\n"
            + string.Join("\n", uncontained)
            + "\n\nThe orphan check below only sees files UNDER a container, so a root outside "
            + "every container is a tree this fact never reads — it can neither confirm that "
            + "root is covered nor report anything living beside it. Either widen a `scanRoots` "
            + "entry to contain the root, or add a container for it with a `minFiles` from a real "
            + "count. Do not narrow the root to fit.");

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
            + $"a summed floor of {expected}; {scanRoots.Sum(x => x.Measured)} were counted "
            + "when those floors were set. This is a floor on a TOTAL, and the per-container "
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

    /// <summary>THE ROSTER BINDS, AND THIS IS WHAT MAKES IT BIND (phase 15.2
    /// task 3, #364 F1 and F2).
    ///
    /// Every entry under `consumers` is a claim about where a pin looks. A claim
    /// nothing enforces is the bug class this repository has paid for most often,
    /// and for one phase this file carried it openly: the manifest disclosed, in
    /// its own $doc, that a consumer could declare it consumes `appleShell` while
    /// scanning somewhere else entirely with everything green. The three pins now
    /// read their roots from <see cref="ShellSourceRoots.SetsFor"/>, so the claim
    /// and the behaviour are the same object — and this fact is what keeps them
    /// that way when someone re-grows a private array, which is precisely the
    /// state #364 F1 was reported from.
    ///
    /// IT REPLACED A TEMPORARY FACT RATHER THAN OUTLIVING ONE.
    /// `TheClaimsNotScansDisclaimer_MatchesWhetherTheConsumersAreRepointed` held
    /// the $doc disclaimer present while any consumer was unrepointed and absent
    /// once none was; it deleted itself with the paragraph, by its own
    /// instruction. What that fact was PROVING along the way — that each consumer
    /// names the door — is a standing property, so it stays here without the
    /// disclaimer half. Deleting the whole thing would have handed the next
    /// author a roster that reads as binding and is not.
    ///
    /// THIS IS THE CHEAP FIRST LINE, NOT THE GUARANTEE, AND THE DIFFERENCE WAS A
    /// REVIEW FINDING. It is a TEXT test: it sees the consumer's source name
    /// `SetsFor`; it cannot see whether the result reaches the walk. A pin that
    /// calls it and then enumerates a hard-coded path anyway satisfies this, and
    /// that is not hypothetical — reverting ONE wrapper method in
    /// `AuthSemanticsDriftTests` while leaving a `SetsFor` call eleven lines away
    /// reopened #364 F1 with the repository at 15 passed / 0 failed.
    ///
    /// AN EARLIER VERSION OF THIS COMMENT SAID CLOSING THAT NEEDED DATAFLOW
    /// ANALYSIS. It does not, and the counterexample was already in the same
    /// commit: each consuming pin now asserts that every root the roster declares
    /// contributed at least one file to what its walk ACTUALLY OPENED, reading
    /// the roster inside the scan, where the code that records a read performs it.
    /// That is two lines per pin. What this fact still adds is a legible red for
    /// the blunt revert — a private array and no roster call at all — naming the
    /// consumer rather than reporting an unvisited root.
    ///
    /// IT READS COMMENT-STRIPPED SOURCE, for a reason found by mutation rather
    /// than by design: every one of the three consumers now carries a doc comment
    /// explaining why it calls the roster, and against RAW text those comments
    /// satisfied this fact all by themselves.</summary>
    [Fact]
    public void EveryConsumer_ReadsItsRootsFromTheRoster()
    {
        // TWO DOORS, BOTH NAMED BY `nameof` so a rename breaks this at compile time
        // rather than turning every consumer permanently "unrepointed".
        //
        // `ShellSourceScan` is the one a pin should reach for: it resolves the roots
        // from the roster AND reads the files AND records coverage, so the pin holds
        // no root list at all. `ShellSourceRoots.SetsFor` is the raw loader, still
        // legitimate for a fact that needs the root strings themselves — NSLog's
        // swallowed-bundle check compares roots rather than scanning them.
        string[] doors =
        [
            $"{nameof(ShellSourceScan)}.",
            $"{nameof(ShellSourceRoots)}.{nameof(ShellSourceRoots.SetsFor)}",
        ];
        string SetsForMarker = string.Join(" or ", doors);

        string[] testSources = Directory.EnumerateFiles(
            Path.Combine(BnRepo.Root(), "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(BinSegment, StringComparison.Ordinal)
                     && !f.Contains(ObjSegment, StringComparison.Ordinal))
            .ToArray();

        var consumers = ShellSourceRoots.Consumers().Keys
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(consumers.Count >= DeclaredConsumerCount,
            $"only {consumers.Count} consumers to check — the roster shrank, and this fact would "
            + "otherwise approve the binding from a short list.");

        var unrepointed = new List<string>();
        foreach (string consumer in consumers)
        {
            string? file = testSources.FirstOrDefault(
                f => Path.GetFileNameWithoutExtension(f).Equals(consumer, StringComparison.Ordinal));

            Assert.True(file is not null,
                $"'{consumer}' is a consumer in {ShellSourceRoots.ManifestPath} and no file named "
                + $"{consumer}.cs exists under tests/. The pin was renamed or removed, so neither "
                + "this fact nor a reader can tell whether it still reads the roster — fix the "
                + "roster key deliberately.");

            // COMMENT-STRIPPED, AND THIS WAS A LIVE HOLE RATHER THAN A PRECAUTION.
            // The first cut read the raw file, so ANY mention of the marker satisfied
            // it — including the doc comments each of these three pins now carries
            // EXPLAINING why it calls the roster. MEASURED: replacing every real call
            // in AuthSemanticsDriftTests with a hard-coded array left this fact GREEN,
            // satisfied by its own header comment, with #364 F1 fully reopened at 15
            // passed / 0 failed. A guard defeated by the prose written to describe it
            // is the worst shape available, because the prose arrives with the fix.
            string code = CommentStrippedSource.Strip(File.ReadAllText(file!));
            if (!doors.Any(d => code.Contains(d, StringComparison.Ordinal)))
                unrepointed.Add(consumer);
        }

        Assert.True(unrepointed.Count == 0,
            "THESE PINS DECLARE A COVERAGE POSITION IN THE ROSTER AND DO NOT READ IT:\n"
            + string.Join("\n", unrepointed.Select(c => "  " + c))
            + $"\n\nEach must take its roots from {SetsForMarker} rather than from a private "
            + "array. A private array is how #364 F1 happened: AuthSemanticsDriftTests' own list "
            + "omitted src/BlazorNative.Jni/src/main/kotlin — half the Android shell — while "
            + "AndroidLogDriftTests' list had it, and nothing compared the two. The roster is the "
            + "one home for that answer; a pin that keeps its own copy is back to being right by "
            + "luck, and its entry here becomes a claim about behaviour that nothing checks.");
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
            $"found only {sources.Length} hand-written .cs files under tests/ — the walk has "
            + "stopped seeing most of its subject, so every cited test name would resolve against "
            + "a short index and red for the wrong reason. No total is quoted here for the same "
            + "reason none is quoted at the name floor below: a denominator in a message is a "
            + "number the next commit invalidates, and this one was kept for a round after its "
            + "sibling was deleted for exactly that. bin/ and obj/ are excluded so the count "
            + "cannot move with build state.");

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
    /// ones with no underscore at all.
    ///
    /// THAT IS A CLAIM ABOUT THIS SUITE, NOT ABOUT C#, and the first draft called
    /// it structural. Five legal test-name shapes carry an underscore and are
    /// still invisible: a lower-case head `mount_returns_x`, a double underscore
    /// `Foo__Bar`, a trailing one `Foo_`, a leading one `_Leading`, and anything
    /// with no lower-case letter at all — `ABI_SIZE_80` — which is the
    /// deliberate ALL_CAPS refusal. Zero occur today; all five fail GREEN.
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

    /// <summary>NO ROOT LIST IN THE ROSTER IS A FREE LITERAL. Every set's roots
    /// are read out of a file the BUILD reads — `src/BlazorNative.Jni/build.gradle.kts`
    /// for the five Kotlin source sets, `src/BlazorNative.Apple/project.yml` for
    /// the two Swift targets — and the seventh, `androidTemplateMirror`, inherits
    /// `androidShell`'s through `mirrorOf`.
    ///
    /// WHY IT REACHES ALL SEVEN AND NOT ONE. It derived only `androidShell` for
    /// three rounds, and the file claimed that covered the roster because a root
    /// leaving a single-root set takes it to zero. That is true of REMOVAL and
    /// false of REARRANGEMENT, and three edits to the manifest alone proved it,
    /// every one nine facts green: swap `appleShell` and `appleTestBundle`, and
    /// the shipped iOS shell lands in the set the auth pin EXCLUDES; swap
    /// `appleShell` and `androidJvmHost`, and it lands in the set all three
    /// consumers exclude, with `language` asserted nowhere to stop a Swift tree
    /// posing as a Kotlin one; narrow `appleShell` to `BnHost/Fonts` and copy
    /// `BnHost` into `androidJvmHost`, and the consumed iOS set is a font folder.
    /// None hits zero. Deriving every list retires the class instead of naming it.
    ///
    /// THE GRAMMAR IS ONE RULE FOR BOTH LANGUAGES, deliberately. `within` is a
    /// nest of anchors: at each level the OUTERMOST line whose trimmed text
    /// matches, unique at that depth, and its region runs to the next non-blank
    /// line indented no deeper. Kotlin braces and YAML blocks both obey that, so
    /// there is no per-language branch to get wrong. `pattern`'s group 1 is a
    /// region: quoted strings inside it if there are any, otherwise the region
    /// itself — which is `srcDirs("a", "b")` and `- path: BnHost` under one rule.
    ///
    /// LIMITS, and the direction of each. It is REGEX AND INDENTATION over build
    /// files, not a parser: a reformat that changes indentation, renames a block,
    /// or moves a path list into a variable reds. FALSE RED, Rule 5's footnote
    /// direction, and every message says how to re-point. The YAML side is not
    /// comment-stripped and does not need to be — its pattern is line-anchored,
    /// so a commented `#  - path: x` cannot match; the Kotlin side goes through
    /// the shared stripper, so a commented-out call cannot either. An anchor that
    /// matches twice at the same depth reds rather than picking one.
    ///
    /// WHAT IT STILL DOES NOT CHECK: that a set's `language` matches the tree it
    /// names. Nothing does. With every list derived, a Swift tree can no longer
    /// arrive under a Kotlin set by rearrangement — but if the two build files
    /// ever agree on a path, this would not notice. FAILS GREEN, disclosed.</summary>
    [Fact]
    public void EveryDerivedRootList_MatchesItsExternalRecord()
    {
        var derived = TheWholeRoster().Values.Where(x => x.DerivedFrom is not null).ToList();

        Assert.True(derived.Count >= 1,
            $"no set in {ShellSourceRoots.ManifestPath} carries a `derivedFrom` block, so every "
            + "root list in the roster is a free literal again and the rearrangement class is "
            + "back. Restore the blocks, or re-point this fact deliberately.");

        foreach (ShellSourceRoots.SetDef set in derived)
        {
            ShellSourceRoots.ExternalRecord g = set.DerivedFrom!;
            string file = Path.Combine(
                BnRepo.Root(), g.File.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(file),
                $"set '{set.Name}' derives its roots from {g.File}, which does not exist. The "
                + "build file moved — re-point `derivedFrom.file` deliberately rather than "
                + "deleting the block, or that root list goes back to answering only to itself.");

            // Kotlin goes through the shared stripper so a commented-out call
            // cannot satisfy the pattern. YAML does not need it: its patterns are
            // line-anchored, and a `#` comment cannot start with `- path:`.
            string text = File.ReadAllText(file);
            string code = g.File.EndsWith(".yml", StringComparison.Ordinal)
                ? text
                : CommentStrippedSource.Strip(text);

            string region = RegionAt(code, g.Within, set.Name, g.File);

            MatchCollection hits = Regex.Matches(region, g.Pattern, RegexOptions.Multiline);

            // THE PATTERN MUST HIT SOMETHING FIRST. Without this, a renamed call
            // yields an empty extracted set, and an empty set compared to an empty
            // roster would be "equal" — two nothings agreeing.
            Assert.True(hits.Count > 0,
                $"set '{set.Name}': found {g.Describes} block in {g.File} but the pattern "
                + $"{g.Pattern} matched nothing inside it. The call was renamed, or the paths are "
                + "written some other way now — a pin that cannot see its subject must never pass "
                + "vacuously, so this reds. Re-point `derivedFrom.pattern`.");

            var declaredByBuild = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match hit in hits)
            {
                string captured = hit.Groups[1].Value;
                MatchCollection quoted = Regex.Matches(captured, "\"([^\"]*)\"");
                if (quoted.Count > 0)
                    foreach (Match qm in quoted)
                        declaredByBuild.Add(g.PathPrefix + qm.Groups[1].Value);
                else if (captured.Trim().Length > 0)
                    declaredByBuild.Add(g.PathPrefix + captured.Trim());
            }

            Assert.True(declaredByBuild.Count > 0,
                $"set '{set.Name}': {g.Describes} matched in {g.File} but yielded no path. The "
                + "arguments are built some other way now — a variable, a list, a spread — so "
                + "this cannot derive the source set and must be re-pointed rather than believed.");

            var rostered = new SortedSet<string>(set.Roots, StringComparer.Ordinal);
            Assert.True(declaredByBuild.SetEquals(rostered),
                $"set '{set.Name}' in {ShellSourceRoots.ManifestPath} and {g.Describes} in "
                + $"{g.File} disagree about what that source tree IS.\n"
                + $"  the build says: {string.Join(", ", declaredByBuild)}\n"
                + $"  the roster says: {string.Join(", ", rostered)}\n"
                + "The build wins. A directory the build compiles and the roster omits is a tree "
                + "every consuming pin is blind to — #364 F1 exactly. A directory the roster names "
                + "and the build does not is the AGP 9 incident: source in the tree that nothing "
                + "builds, with pins reporting green over it. And a root that has MOVED from one "
                + "set to another is why this reaches every set rather than one: no count can see "
                + "a move, and a move can put a consumed tree inside an excluded set.");
        }
    }

    /// <summary>THE EVERY-LIST GUARD. `derivedFrom` and `mirrorOf` only close the
    /// rearrangement class while EVERY set has one. An eighth set arriving with a
    /// hand-written `roots` array would reopen it in one commit and nothing would
    /// say so — which is the exact shape of every defect this file has shipped.
    ///
    /// So the requirement is mechanical rather than remembered. A new set must
    /// name the build file that already knows its source tree, or mirror a set
    /// that does.</summary>
    [Fact]
    public void EveryRootList_IsExternallyDerived()
    {
        var free = TheWholeRoster().Values
            .Where(x => x.DerivedFrom is null && x.MirrorOf is null)
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(free.Count == 0,
            $"{string.Join(", ", free)} declare `roots` with no `derivedFrom` and no `mirrorOf`, "
            + "so those lists answer only to themselves. A free root list can be rearranged — a "
            + "root moved from a consumed set into an excluded one, a set narrowed to a "
            + "subdirectory — with every other fact here green, because no count and no existence "
            + "check can see a move. Three such edits were measured green before every list was "
            + "derived.\n\nGive the set the build file that already knows its source tree: "
            + "src/BlazorNative.Jni/build.gradle.kts for a Gradle source set, "
            + "src/BlazorNative.Apple/project.yml for an XcodeGen target, or `mirrorOf` a set "
            + "that has one.");
    }

    /// <summary>The text of one nested, indentation-delimited block. At each level
    /// the anchor is matched against the trimmed line, the OUTERMOST depth holding
    /// a match wins, that depth must hold exactly one, and the block runs to the
    /// next non-blank line indented no deeper.
    ///
    /// Blank lines are skipped rather than ending the block, which matters because
    /// the Kotlin side arrives comment-stripped and a stripped comment line is
    /// blank.</summary>
    private static string RegionAt(string code, string[] within, string setName, string file)
    {
        string[] lines = code.Replace("\r\n", "\n").Split('\n');
        int from = 0, to = lines.Length;

        foreach (string anchor in within)
        {
            var hits = Enumerable.Range(from, to - from)
                .Where(i => lines[i].Trim() == anchor)
                .ToList();

            Assert.True(hits.Count > 0,
                $"set '{setName}': could not find the line `{anchor}` in {file}"
                + (from == 0 ? "" : $" inside the block found for the previous anchor")
                + ". The block was renamed or reformatted — a derivation that cannot find its "
                + "subject must never fall back to a wider region, so this reds. Re-point "
                + "`derivedFrom.within`.");

            int depth = hits.Min(i => Indent(lines[i]));
            var outermost = hits.Where(i => Indent(lines[i]) == depth).ToList();

            Assert.True(outermost.Count == 1,
                $"set '{setName}': the line `{anchor}` appears {outermost.Count} times at the "
                + $"same depth in {file}, so which block this derivation reads is arbitrary. "
                + "Ambiguity here silently re-points a root list at a sibling block, so it reds "
                + "instead of picking one. Add an enclosing anchor to `derivedFrom.within`.");

            int start = outermost[0];
            int end = start + 1;
            while (end < to && (lines[end].Trim().Length == 0 || Indent(lines[end]) > depth))
                end++;

            from = start + 1;
            to = end;
        }

        return string.Join("\n", lines[from..to]);
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;


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
    /// WHAT COVERS THE REST, now that this no longer has to carry it. The
    /// sentence that stood here said the cover was complete because a root
    /// leaving a single-root set takes it to zero. That is true of REMOVAL and
    /// false of REARRANGEMENT, and it was measured false three ways.
    /// `EveryDerivedRootList_MatchesItsExternalRecord` now derives every other
    /// root list from the build file that already knows it, and
    /// `EveryRootList_IsExternallyDerived` stops an eighth set arriving with a
    /// free one. This fact covers `androidTemplateMirror`, whose source tree no
    /// build file in this repository declares independently of the shell's.
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
