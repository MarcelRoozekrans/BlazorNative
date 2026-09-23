using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BlazorNative.Components;
using Microsoft.AspNetCore.Components;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

/// <summary>
/// PINS 1 AND 3 — Phase 8.4 Gate 2 (M8 DoD #5): the docs site's component
/// reference is complete, and it is written for strangers.
///
/// THE SENTENCE THESE PINS EXIST FOR. xmldoc2md, run against
/// src/BlazorNative.Components/bin/Release/net10.0/, prints:
///
///     Generation: 10 succeeded, 0 failed          exit 0
///
/// Ten types, ZERO components, and a reassuring green. Microsoft.AspNetCore.Components.dll
/// is not next to the assembly there, so ComponentBase does not resolve, so every
/// type deriving from it is dropped SILENTLY. Against a publish output the same
/// command prints `26 succeeded` and every component is there.
///
/// Nothing in that failure is visible to anything that does not COUNT: the tool
/// says succeeded, the exit code says success, the site builds, the sidebar
/// renders, and the reference simply has no components in it. A count is the only
/// witness.
/// </summary>
public sealed class ComponentReferenceFixture : IDisposable
{
    public string OutputDirectory { get; }
    public string GeneratorLog { get; }

    public ComponentReferenceFixture()
    {
        OutputDirectory = Path.Combine(
            Path.GetTempPath(), "bn-docs-reference-" + Guid.NewGuid().ToString("N"));

        // THE PIN RUNS THE LANE'S OWN PIPELINE, and that is the whole reason
        // generation is a script. scripts/generate-reference.ps1 is what
        // .github/workflows/docs.yml runs (via the site's `prebuild`), so what is
        // asserted below is what deploys. A pin that re-implemented the publish +
        // generate steps here would be measuring ITSELF: it could pass forever
        // while the lane pointed at bin/ and shipped a reference with no
        // components in it. One home, two callers.
        string script = Path.Combine(BnRepo.Root(), "scripts", "generate-reference.ps1");
        Assert.True(File.Exists(script), $"generator script not found: {script}");

        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = BnRepo.Root(),
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        // #173: the generator now covers more than one package, so a bare
        // -OutputPath is ambiguous — name the package. This fixture asserts the
        // Components pipeline specifically (ComponentBase, the .razor pins), so it
        // publishes and generates ONLY Components into its temp dir. The sibling
        // ReferenceDriftTests does the same for each other generated package.
        psi.ArgumentList.Add("-Package");
        psi.ArgumentList.Add("Components");
        psi.ArgumentList.Add("-OutputPath");
        psi.ArgumentList.Add(OutputDirectory);

        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        GeneratorLog = stdout + stderr;

        Assert.True(p.ExitCode == 0,
            $"generate-reference.ps1 failed (exit {p.ExitCode}):\n{GeneratorLog}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(OutputDirectory)) Directory.Delete(OutputDirectory, true); }
        catch (IOException) { /* a temp dir that outlives the run is not a failure */ }
    }
}

// SERIALISED AGAINST ReferenceDriftTests — see that file's collection note. Both
// fixtures shell out to generate-reference.ps1, which runs `dotnet tool restore`;
// run in parallel they race on the same file in the NuGet package cache.
[Collection(ReferenceGeneration.Name)]
public sealed class ComponentReferenceDriftTests : IClassFixture<ComponentReferenceFixture>
{
    private readonly ComponentReferenceFixture _fixture;

    public ComponentReferenceDriftTests(ComponentReferenceFixture fixture) => _fixture = fixture;

    private static Assembly ComponentsAssembly => typeof(BnView).Assembly;

    /// <summary>The public types this assembly HAS — measured by reflection, in a
    /// process where ComponentBase resolves for real. This is the truth the
    /// generator is held against, and it is DERIVED rather than declared: adding,
    /// renaming or removing a public type moves it automatically, so it can never
    /// become a roster that someone shrinks to make a red go away.</summary>
    private static IEnumerable<Type> PublicTypes()
        => ComponentsAssembly.GetTypes().Where(t => t.IsPublic);

    /// <summary>The components proper: public, concrete, ComponentBase-derived.
    /// This is the set the 10-vs-26 defect emptied.</summary>
    private static List<Type> PublicComponents()
        => PublicTypes()
            .Where(t => typeof(ComponentBase).IsAssignableFrom(t) && !t.IsAbstract)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>xmldoc2md's file naming: the full type name, lowercased, with the
    /// generic-arity backtick as a dash — BnList`1 → blazornative.components.bnlist-1.md.</summary>
    private static string PageNameFor(Type t)
        => t.FullName!.Replace('`', '-').ToLowerInvariant() + ".md";

    // ── PIN 1 — the reference is complete ────────────────────────────────────

    /// <summary>
    /// The generated page set equals the assembly's public type set, RED IN BOTH
    /// DIRECTIONS.
    ///
    /// Both sides are measured. The subjects are the files the generator actually
    /// wrote; the expectation is reflected out of the assembly. Neither is a list
    /// a human maintains, which is what makes this survive a rename: the type
    /// moves and the expectation moves with it.
    ///
    /// MISSING means the reference does not document a type that exists — the
    /// 10-vs-26 failure, and the reason this file exists. UNEXPECTED means the
    /// generator emitted a page for something the assembly does not publish.
    /// </summary>
    [Fact]
    public void GeneratedReference_DocumentsExactlyThePublicTypes()
    {
        var expected = PublicTypes().Select(PageNameFor).ToList();

        var actual = Directory.GetFiles(_fixture.OutputDirectory, "*.md")
            .Select(Path.GetFileName)
            .Where(f => !string.Equals(f, "index.md", StringComparison.Ordinal))
            .Select(f => f!.ToLowerInvariant())
            .ToList();

        // NON-VACUITY, BOTH SIDES, FIRST. An expectation of zero types would be
        // satisfied by a generator that wrote nothing — green, and exactly the
        // shape of the defect. So the pin proves it can SEE its subject before it
        // compares anything.
        Assert.True(expected.Count > 0,
            "reflected ZERO public types out of BlazorNative.Components — the completeness "
            + "pin has no expectation to hold anything against. A pin that cannot see its "
            + "subject must never pass vacuously.");
        Assert.True(actual.Count > 0,
            $"the generator wrote NO pages into {_fixture.OutputDirectory}.\n\n{_fixture.GeneratorLog}");

        var missing = expected.Except(actual, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var unexpected = actual.Except(expected, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && unexpected.Count == 0,
            "THE COMPONENT REFERENCE DRIFTED FROM THE ASSEMBLY.\n\n"
            + $"  MISSING (the assembly publishes it, the reference does not document it — {missing.Count}):\n"
            + (missing.Count == 0 ? "    (none)\n" : string.Join("\n", missing.Select(f => $"    {f}")) + "\n")
            + $"  UNEXPECTED (the reference documents it, the assembly does not publish it — {unexpected.Count}):\n"
            + (unexpected.Count == 0 ? "    (none)\n" : string.Join("\n", unexpected.Select(f => $"    {f}")) + "\n")
            + $"\n(Assembly: {expected.Count} public types. Generated: {actual.Count} pages.)\n\n"
            + "IF EVERY COMPONENT IS MISSING AND THE ENUMS ARE NOT, THE GENERATOR IS POINTED AT "
            + "bin/ INSTEAD OF A PUBLISH OUTPUT. That is not a hypothesis — it is what happened "
            + "the first time this was run. Microsoft.AspNetCore.Components.dll is absent from "
            + "bin/, so ComponentBase does not resolve and every type deriving from it is dropped "
            + "SILENTLY, while xmldoc2md reports '10 succeeded, 0 failed' and exits 0. See "
            + "scripts/generate-reference.ps1.\n\n"
            + "Generator output:\n" + _fixture.GeneratorLog);
    }

    /// <summary>
    /// The anti-vacuity heart, stated as its own assertion: the reference contains
    /// COMPONENTS, not merely files.
    ///
    /// The failure that motivates this whole file produced ten perfectly valid
    /// pages — the enums, the two static helpers, the Razor imports class — and
    /// not one component. A page count alone would have called that a healthy 10.
    /// The named three are the ones a reader opens first; if BnView is not in the
    /// reference, the reference is not a reference.
    /// </summary>
    [Fact]
    public void GeneratedReference_ContainsTheComponents_NotJustTheEnums()
    {
        var components = PublicComponents();

        Assert.True(components.Count > 0,
            "reflected ZERO ComponentBase-derived types — the pin cannot see its subject.");

        var missing = components
            .Where(t => !File.Exists(Path.Combine(_fixture.OutputDirectory, PageNameFor(t))))
            .Select(t => t.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            $"THE REFERENCE IS MISSING {missing.Count} OF {components.Count} COMPONENTS: "
            + string.Join(", ", missing)
            + "\n\nA reference with no components in it is the failure this pin was written for, "
            + "and the generator calls it success. Check that scripts/generate-reference.ps1 is "
            + "generating from a PUBLISH output.\n\n" + _fixture.GeneratorLog);

        // The three a reader opens first — named, so the assertion cannot be
        // satisfied by an empty set that technically has no missing members.
        foreach (var name in new[] { "BnView", "BnText", "BnButton" })
            Assert.Contains(name, components.Select(t => t.Name));
    }

    // ── PIN 2's BLIND SPOT — the six .razor summaries ────────────────────────

    /// <summary>
    /// Every public component carries a type-level &lt;summary&gt;.
    ///
    /// THIS EXISTS BECAUSE PIN 2 STRUCTURALLY CANNOT SEE IT. CS1591-as-an-error
    /// catches an undocumented member in hand-written C#, but the Razor source
    /// generator emits `#pragma warning disable 1591` into every generated file —
    /// so a .razor component with no type summary compiles clean forever. That is
    /// exactly what happened: the measured coverage gap said 8 while the reference
    /// had SIX headless pages in it, because a `@* ... *@` header is a Razor
    /// comment and never reaches the assembly.
    ///
    /// The compiler cannot pin it, so a test does. Subjects derived from the
    /// assembly; the shipped XML is the evidence.
    /// </summary>
    [Fact]
    public void EveryPublicComponent_CarriesATypeLevelSummary()
    {
        XDocument xml = ShippedXml();
        var documented = xml.Descendants("member")
            .Where(m => (m.Attribute("name")?.Value ?? "").StartsWith("T:", StringComparison.Ordinal))
            .Where(m => !string.IsNullOrWhiteSpace(m.Element("summary")?.Value))
            .Select(m => m.Attribute("name")!.Value["T:".Length..])
            .ToHashSet(StringComparer.Ordinal);

        var components = PublicComponents();
        Assert.True(components.Count > 0, "reflected ZERO components — vacuous.");

        var undocumented = components
            .Select(t => t.FullName!)
            .Where(n => !documented.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(undocumented.Count == 0,
            $"{undocumented.Count} public component(s) have NO type-level <summary>, so the "
            + "reference renders them as a headless signature dump:\n"
            + string.Join("\n", undocumented.Select(n => "    " + n))
            + "\n\nFor a .razor component the summary goes in a `/// ` on a `partial class` in a "
            + ".razor.cs — a `@* ... *@` header is a RAZOR comment and never reaches the "
            + "assembly. CS1591 cannot help you here: the Razor generator disables it.");
    }

    /// <summary>
    /// Every <c>[Parameter]</c> a consumer can bind carries a doc comment — a
    /// non-empty &lt;summary&gt; or an &lt;inheritdoc&gt;.
    ///
    /// PIN 2 GUARANTEES ROUGHLY HALF OF WHAT IT READS AS, AND THIS IS THE OTHER
    /// HALF. CS1591-as-an-error is the stated mechanism for "every public
    /// component member is documented", but it is structurally blind to .razor:
    /// the Razor generator emits `#pragma warning disable 1591` at line 3 of
    /// every *_razor.g.cs, and an @code-block [Parameter] is declared INSIDE
    /// that generated file. The compiler is not lenient there — it is switched
    /// off.
    ///
    /// Measured, this repo, when the pin was written: of 196 [Parameter]
    /// properties, 98 live in .cs (Pin 2 holds them) and 98 live in .razor
    /// (Pin 2 cannot see them) — EXACTLY HALF. The mutation that proves it: delete
    /// BnSlider.Value's summary and the build is `0 Warning(s), 0 Error(s)` —
    /// while P:BlazorNative.Components.BnSlider.Value vanishes from the shipped
    /// XML. That is the property `@bind-Value` targets. Its reference row goes
    /// blank, its IDE tooltip goes empty, and nothing anywhere turns red.
    ///
    /// THE COUNT MOVES, ON PURPOSE, DURING A REFACTOR THAT SHARES DECLARATIONS.
    /// This pin is DeclaredOnly by design (an inherited parameter is attributed
    /// to the type that declares it, where its XML id actually lives) — so
    /// consolidating a parameter that used to be copy-pasted across N
    /// components into one shared base LOWERS the reflected count by (N-1),
    /// even though the number of parameters an author can bind is unchanged.
    /// That is a feature of the count, not drift to chase back up: the
    /// threshold below exists only to catch reflection returning an empty set,
    /// not to pin an exact figure.
    ///
    /// EveryPublicComponent_CarriesATypeLevelSummary above does not cover this:
    /// it asserts TYPE-level summaries. A component can carry a perfect class
    /// summary and document not one of its parameters.
    ///
    /// THE COUNT IS DERIVED, AND THAT IS NOT PEDANTRY — IT IS HOW THE NUMBER GOT
    /// FIXED. Every hand-written source in this repo said "192", from the design
    /// down. 192 came from `grep '\[Parameter\]'`, which cannot see
    /// `[Parameter, EditorRequired]` — and BnList.razor declares four of them.
    /// This pin reflects, so the first time it ran it said 196; ilspycmd over the
    /// DLL agrees. A pin that trusts a number a human typed inherits that human's
    /// blind spot.
    ///
    /// AN <inheritdoc> COUNTS, and must — it is how 124 of the 196 are
    /// documented (BnFlexPreset 23; BnCheckbox/BnPicker/BnSlider/BnSwitch 17
    /// each; BnScroll 16; BnImage 15; BnModal 2). Those are overwhelmingly
    /// BnView's flex and box vocabulary re-exposed, where
    /// `<inheritdoc cref="BnView.Shrink"/>` is the RIGHT answer: one home for the
    /// sentence, and xmldoc2md resolves it on the page. Demanding a hand-written
    /// summary per property would be demanding the 124 copies this repo exists to
    /// refuse.
    ///
    /// Subjects reflected, expectation declared, never a roster.
    /// </summary>
    [Fact]
    public void EveryParameter_CarriesADocComment()
    {
        XDocument xml = ShippedXml();

        // id -> the member element, so <inheritdoc/> is visible AS an element.
        // PublicSurfaceDocs() cannot serve here and the reason is worth stating:
        // it returns member.Value, and an inheritdoc-only member's Value is the
        // EMPTY STRING — indistinguishable from an undocumented one. Reading the
        // elements is the difference between this pin and a pin that reds on 124
        // correctly-documented properties.
        var documented = xml.Descendants("member")
            .Where(m => m.Attribute("name") is not null)
            .Where(m => !string.IsNullOrWhiteSpace(m.Element("summary")?.Value)
                        || m.Element("inheritdoc") is not null)
            .Select(m => m.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);

        // The subjects: every [Parameter]-decorated public property on a public
        // type, DeclaredOnly so an inherited parameter is attributed to the type
        // that declares it — which is where its XML id lives.
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var parameters = PublicTypes()
            .SelectMany(t => t.GetProperties(flags)
                .Where(p => p.IsDefined(typeof(ParameterAttribute), inherit: false))
                .Select(p => (Id: $"P:{t.FullName}.{p.Name}", Type: t.Name, Property: p.Name)))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        // NON-VACUITY, and it is not ceremony: this pin's whole subject is a set
        // reflection could silently return empty for (wrong assembly, wrong
        // attribute type, a BindingFlags typo). An empty subject set makes every
        // assertion below green while proving nothing at all.
        Assert.True(parameters.Count > 60,
            $"reflected only {parameters.Count} [Parameter] properties out of BlazorNative.Components "
            + "— there were 196 when this pin was written; phase 13.0 moved the 17 item and 5 container "
            + "parameters onto BnLayoutItem/BnLayoutContainer, which lowers this DeclaredOnly count on "
            + "purpose (see the doc comment above), and the count MEASURED at the close of 13.0 was 74. "
            + "The floor sits just under that measurement rather than far below it, so a collapse — a "
            + "wrong assembly, a BindingFlags typo, or a whole component's surface silently going "
            + "missing — still reds instead of passing vacuously. If a later phase legitimately moves "
            + "more declarations onto a base, re-measure and lower this by the amount it actually moved, "
            + "not by a round number.");

        var undocumented = parameters
            .Where(p => !documented.Contains(p.Id))
            .ToList();

        Assert.True(undocumented.Count == 0,
            $"{undocumented.Count} of {parameters.Count} [Parameter] properties have NO <summary> "
            + "and no <inheritdoc> in the shipped XML:\n"
            + string.Join("\n", undocumented.Select(p => $"    {p.Type}.{p.Property}"))
            + "\n\nEach one is a bindable parameter whose reference row renders blank and whose IDE "
            + "tooltip is empty. IF THE PROPERTY IS DECLARED IN A .razor @code BLOCK, THE COMPILER "
            + "WILL NOT HELP YOU: the Razor generator disables CS1591 in the file it generates, so "
            + "the build is green and the doc is simply gone. Write the `///` above the [Parameter], "
            + "or `<inheritdoc cref=\"...\"/>` if BnView already says it.");
    }

    // ── PIN 3 — the reference is written for strangers ───────────────────────

    /// <summary>
    /// The published docs carry no repo history.
    ///
    /// The XML this scans is BOTH consumer surfaces at once: the docs site's
    /// reference is generated from it, and it packs into the nupkg beside the DLL,
    /// which is what a consumer's IDE shows in a tooltip. A stranger cannot read
    /// "since Phase 3.4 Gate 1", cannot run the BnDemo goldens, and does not have
    /// a file header to be referred to.
    ///
    /// IT SCANS THE PUBLIC SURFACE ONLY, and that is deliberate rather than lazy.
    /// The XML also documents internal and private members — BnItemsJson's wire
    /// grammar, BnListWindow's arithmetic, BnPicker's clamp guard. Those are
    /// MAINTAINER documentation for types no consumer can reach and the generator
    /// correctly drops them (it runs at --member-accessibility-level public). A
    /// regex over the whole file would red on prose that is doing its job, and
    /// would teach the next author to delete engineering truth to make a test go
    /// green. The filter mirrors the generator's setting, so this pin reads
    /// exactly what ships.
    /// </summary>
    [Fact]
    public void PublishedDocs_SpeakToConsumers_NotToTheRepo()
    {
        XDocument xml = ShippedXml();

        // NON-VACUITY: an absence assertion over a file that failed to load, or
        // whose member set is empty, is green for the WRONG reason. Prove the XML
        // is real, and prove a known member is in it, before believing any absence.
        var members = xml.Descendants("member").ToList();
        Assert.True(members.Count > 50,
            $"the shipped XML has only {members.Count} members — this pin's silence would "
            + "mean nothing. A pin that cannot see its subject must never pass vacuously.");
        Assert.Contains(members, m =>
            m.Attribute("name")?.Value == "P:BlazorNative.Components.BnLayoutItem.BackgroundColor");

        var published = PublicSurfaceDocs(xml);
        Assert.True(published.Count > 50,
            $"only {published.Count} PUBLIC documented members — the filter ate the subject.");

        List<string> violations = Violations(published);

        Assert.True(violations.Count == 0,
            $"{violations.Count} PUBLISHED doc comment(s) speak to this repo rather than to a "
            + "stranger.\n\nThis XML is what the docs site's component reference is generated "
            + "from AND what a consumer's IDE shows in a tooltip. Rewrite the comment; do not "
            + "add an exception here.\n\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// THE POSITIVE CONTROL FOR THE NINE BANNED PROSE PATTERNS, HALF ONE — a TREE
    /// ANCHOR (pin standard Rule 3; census item 8).
    ///
    /// <see cref="PublishedDocs_SpeakToConsumers_NotToTheRepo"/> is an ABSENCE
    /// assertion, and its subject tree is required to be empty by construction: the
    /// published surface must contain none of these phrases, so nothing there can ever
    /// be a fixed point. Reword a pattern past its subject and it reports no violations
    /// forever.
    ///
    /// THE ANCHOR IS THE EXCLUSION. That fact filters to the PUBLIC surface on purpose,
    /// and says so: the XML also documents internal and private members — BnItemsJson's
    /// wire grammar, BnListWindow's arithmetic, BnPicker's clamp guard — which are
    /// MAINTAINER documentation the generator correctly drops. Anything deliberately
    /// excluded from a scan is something the detector must still be able to SEE, and
    /// that half of the file is full of exactly the repo-speak the published half must
    /// not carry. Three of the nine patterns have a live subject there right now:
    ///
    ///   · `\bfile header\b`         — BnItemsJson's "normative grammar in the file
    ///                                 header", BnPicker.Clamp's "The normative clamp
    ///                                 (file header)"
    ///   · `\bdesign decision \d`    — BnListWindow.Compute's "(design decision 3)"
    ///   · `\bGate \d`               — BnPicker.OnParametersSetAsync's "(Gate 1 review…"
    ///
    /// THE OTHER SIX have no instance anywhere in this XML, which is the pin working:
    /// they were reworded out of the published surface when this file was written and
    /// out of the internal one since. They are controlled by the fixture in
    /// <see cref="TheBannedProsePatterns_MatchThePhrasesTheyWereWrittenFor"/> instead,
    /// and the split is stated rather than blurred — a tree anchor and a fixture are
    /// not the same strength of evidence.
    ///
    /// IF THIS REDS: an internal doc comment was rewritten, which is legitimate and
    /// free — maintainer prose is not governed by this pin. Move the pattern down to
    /// the fixture-only list and say so, rather than restoring prose to satisfy a test
    /// or deleting the assertion.
    /// </summary>
    [Fact]
    public void TheBannedProsePatterns_StillHitTheUnpublishedHalfOfTheXml()
    {
        XDocument xml = ShippedXml();
        List<(string Name, string Text)> unpublished = NonPublishedDocs(xml);

        Assert.True(unpublished.Count > 10,
            $"only {unpublished.Count} NON-published documented members — this control's subject "
            + "is the internal/private half of the shipped XML, and an empty half would let every "
            + "assertion below pass over nothing. Either the filter inverted or "
            + "GenerateDocumentationFile stopped emitting internals.");

        string[] anchored = [@"\bfile header\b", @"\bdesign decision \d", @"\bGate \d"];

        foreach (string pattern in anchored)
        {
            // Rule 4: this control names three patterns by their literal text. If one
            // is reworded in BannedProse and not here, the loop below would be
            // exercising a pattern the pin no longer uses.
            Assert.True(BannedProse.Any(b => b.Pattern == pattern),
                $"/{pattern}/ is named here as a tree-anchored pattern but is no longer in "
                + "BannedProse. It was reworded or removed and this control was not moved with "
                + "it — so this loop is about to test a pattern the pin does not run. Re-point "
                + "deliberately.");

            var hits = unpublished
                .Where(m => Regex.IsMatch(m.Text, pattern, RegexOptions.IgnoreCase))
                .Select(m => m.Name)
                .ToList();

            Assert.True(hits.Count > 0,
                $"/{pattern}/ no longer matches ANY member of the unpublished half of "
                + "BlazorNative.Components.xml, and that half is this control's fixed point — the "
                + "maintainer documentation the publication filter deliberately drops, which is "
                + "where the repo is still allowed to talk to itself.\n\n"
                + "TWO THINGS THIS CAN MEAN, and they need different answers. Either the PATTERN "
                + "was reworded past its subject — in which case PublishedDocs_SpeakToConsumers_"
                + "NotToTheRepo is now green because it cannot see, not because the docs are "
                + "clean, and the pattern is the thing to fix. Or the internal PROSE was rewritten "
                + "and this phrase simply no longer occurs anywhere — which is fine and free, and "
                + "the answer is to move this pattern into the fixture-only list in "
                + "TheBannedProsePatterns_MatchThePhrasesTheyWereWrittenFor and record that it "
                + "lost its tree anchor.");
        }
    }

    /// <summary>
    /// THE POSITIVE CONTROL, HALF TWO — a FIXTURE for all nine patterns, run through
    /// the pin's OWN detector (<see cref="Violations"/>), not a copy of it.
    ///
    /// Each row is a phrase in the shape the shipped XML actually carried when this
    /// phase opened; four are verbatim lines still in the file. The pin's failure mode
    /// is a pattern that stops matching the prose people really write, and the scar is
    /// already recorded three lines above <c>\bgolden</c>: `/\bgolden\b/` does NOT match
    /// "goldens", and the plural is what the repo writes. That correction was found by
    /// a mutation and would have been lost again by the next person to "tidy" the
    /// pattern with a trailing boundary — SO THE PLURAL IS IN THE FIXTURE. Add the
    /// boundary back and this reds.
    ///
    /// THE NEGATIVE HALF is ordinary English that contains each pattern's words in a
    /// shape it must refuse. Without it, "make every pattern match" is satisfiable by
    /// widening them all to `.`, and the pin would red on prose doing its job — which
    /// the file's own header warns about for a different reason: a pin that reds on
    /// correct writing teaches the next author to delete engineering truth.
    ///
    /// WHAT A FIXTURE CANNOT BUY, stated because the strengths differ: a hand-written
    /// phrase proves the regex still matches a string in this file. It cannot prove the
    /// regex matches the prose a future author will write. That is the unguardable
    /// WIDTH of the list — nine phrasings someone thought of — and no control over a
    /// fixed pattern set reaches it.
    /// </summary>
    [Fact]
    public void TheBannedProsePatterns_MatchThePhrasesTheyWereWrittenFor()
    {
        (string Pattern, string Phrase)[] fixtures =
        [
            (@"\bPhase \d", "Introduced in Phase 3.4 when the renderer learned about text."),
            (@"\bGate \d", "Tightened at Gate 1 review, see the clamp note."),
            (@"\bDoD #\d", "Closes M8 DoD #5."),
            (@"\bdesign decision \d", "Fixed row height in dp/pt (design decision 3)."),
            (@"\bBnDemo\b", "Exercised by BnDemo; the BnDemo goldens stay byte-identical."),
            (@"\bBnSettingsPage\b", "See BnSettingsPage for the switch wiring."),
            // THE PLURAL, deliberately. /\bgolden\b/ does not match it.
            (@"\bgolden", "the BnDemo goldens stay byte-identical"),
            (@"\bHelloComponent\b", "Mirrors HelloComponent in the test project."),
            (@"\bfile header\b", "Normative grammar in the file header."),
            (@"awaits \.razor compilation", "The typed overload awaits .razor compilation."),
        ];

        Assert.Equal(
            BannedProse.Select(b => b.Pattern).OrderBy(p => p, StringComparer.Ordinal),
            fixtures.Select(f => f.Pattern).Distinct().OrderBy(p => p, StringComparer.Ordinal));

        foreach (var (pattern, phrase) in fixtures)
        {
            List<string> hits = Violations([($"fixture:{pattern}", phrase)]);
            Assert.True(hits.Any(h => h.Contains($"/{pattern}/", StringComparison.Ordinal)),
                $"/{pattern}/ no longer matches the phrase it was written for:\n\n    \"{phrase}\"\n\n"
                + "That phrase is the shape the shipped XML carried when this pin was written, and "
                + "several are verbatim lines still in the file. A pattern that has stopped "
                + "matching its own subject makes PublishedDocs_SpeakToConsumers_NotToTheRepo "
                + "report zero violations forever while the published docs fill up with repo-speak. "
                + "Fix the pattern; do not rewrite the fixture to suit it.");
        }

        // THE NEGATIVE: ordinary prose that shares the words but not the shape. A net
        // widened until everything matches is not a stricter pin, it is a broken one.
        string[] nearMisses =
        [
            "We phase the rollout in gradually; the gateway stays open.",
            "See BnDemonstrator and BnSettingsPageModel for the wiring.",
            "The golfer left a note in the header file about the grammar.",
            "This overload awaits razor compilation and a DoD number 5 sign-off.",
            "HelloComponentBase is internal, and design decisions were revisited.",
        ];

        foreach (string phrase in nearMisses)
        {
            List<string> hits = Violations([("near-miss", phrase)]);
            Assert.True(hits.Count == 0,
                $"a banned pattern now matches ordinary prose:\n\n    \"{phrase}\"\n\n"
                + string.Join("\n", hits)
                + "\n\nEvery one of these shares words with a banned pattern in a shape the "
                + "pattern must refuse — no digit after 'Phase', no word boundary after 'BnDemo', "
                + "'header file' rather than 'file header', no literal dot before 'razor'. A "
                + "pattern widened past those distinctions reds on doc comments that are doing "
                + "their job, and the next author will delete the sentence rather than the "
                + "pattern.");
        }
    }

    /// <summary>The vocabulary of a repo talking to itself. Each of these was in the
    /// shipped XML when phase 8.4 opened. Declared once and shared by the pin and both
    /// of its controls, so a control can never be testing a stale copy of the list —
    /// which is the whole reason the array moved out of the fact body in 15.1.</summary>
    private static readonly (string Pattern, string Why)[] BannedProse =
    [
        (@"\bPhase \d", "a phase number — the reader has no access to the phase history"),
        (@"\bGate \d", "a gate number — same"),
        (@"\bDoD #\d", "a Definition-of-Done reference"),
        (@"\bdesign decision \d", "a design-doc decision number"),
        (@"\bBnDemo\b", "a demo page the reader does not have"),
        (@"\bBnSettingsPage\b", "a demo page the reader does not have"),
        // NO trailing \b, and that is not sloppiness — it is a correction. The
        // phrase in the shipped XML was "the BnDemo goldens stay
        // byte-identical", and /\bgolden\b/ does NOT match "goldens": the
        // boundary fails against the plural the repo actually writes. The
        // mutation caught 3 of 4 patterns and this was the miss. The plural is
        // pinned as a fixture in
        // TheBannedProsePatterns_MatchThePhrasesTheyWereWrittenFor so the
        // correction cannot be tidied away a second time.
        (@"\bgolden", "a golden file the reader cannot run"),
        (@"\bHelloComponent\b", "an internal fixture"),
        (@"\bfile header\b", "the reader is looking at a web page, not your source file"),
        (@"awaits \.razor compilation", "it does not — Razor components compile today"),
    ];

    /// <summary>THE DETECTOR, factored out so the pin and both of its positive
    /// controls run the same code. A control that re-implemented this loop would
    /// prove that the control's copy works.</summary>
    private static List<string> Violations(IEnumerable<(string Name, string Text)> docs)
    {
        var violations = new List<string>();
        foreach (var (name, text) in docs)
            foreach (var (pattern, why) in BannedProse)
            {
                Match m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (m.Success)
                    violations.Add($"    {name}\n        matched /{pattern}/ ({why})\n"
                        + $"        ...{Excerpt(text, m.Index)}...");
            }
        return violations;
    }

    /// <summary>The documented members the generator PUBLISHES: public members of
    /// public types, plus the public types themselves — the same surface
    /// `--member-accessibility-level public` emits.</summary>
    private static List<(string Name, string Text)> PublicSurfaceDocs(XDocument xml)
        => PartitionDocs(xml).Published;

    /// <summary>The complement: everything in the shipped XML the generator DROPS —
    /// internal types, private fields, protected overrides. Maintainer documentation,
    /// correctly unpublished, and therefore the one place the banned vocabulary is
    /// still allowed to live. That is what makes it a fixed point.</summary>
    private static List<(string Name, string Text)> NonPublishedDocs(XDocument xml)
        => PartitionDocs(xml).Unpublished;

    private static (List<(string Name, string Text)> Published,
                    List<(string Name, string Text)> Unpublished) PartitionDocs(XDocument xml)
    {
        var publicTypeNames = PublicTypes().Select(t => t.FullName!).ToHashSet(StringComparer.Ordinal);

        var publicMemberIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in PublicTypes())
        {
            publicMemberIds.Add("T:" + t.FullName);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var p in t.GetProperties(flags)) publicMemberIds.Add($"P:{t.FullName}.{p.Name}");
            foreach (var f in t.GetFields(flags)) publicMemberIds.Add($"F:{t.FullName}.{f.Name}");
            foreach (var e in t.GetEvents(flags)) publicMemberIds.Add($"E:{t.FullName}.{e.Name}");
            // Methods carry an argument list in their XML id; match on the prefix.
            foreach (var m in t.GetMethods(flags)) publicMemberIds.Add($"M:{t.FullName}.{m.Name}");
        }

        var published = new List<(string, string)>();
        var unpublished = new List<(string, string)>();
        foreach (var member in xml.Descendants("member"))
        {
            string id = member.Attribute("name")?.Value ?? "";
            string idNoArgs = id.Contains('(', StringComparison.Ordinal)
                ? id[..id.IndexOf('(', StringComparison.Ordinal)]
                : id;

            // A member of a public type is only published if its own declaring
            // type is public — GetProperties(Public) already guarantees that.
            if (publicMemberIds.Contains(id) || publicMemberIds.Contains(idNoArgs))
                published.Add((id, member.Value));
            else
                unpublished.Add((id, member.Value));
        }

        Assert.All(published, r => Assert.True(
            publicTypeNames.Count > 0, "public type set went empty"));
        return (published, unpublished);
    }

    private static XDocument ShippedXml()
    {
        // The XML that ships INSIDE the nupkg, beside the DLL — the same file the
        // reference is generated from. Read it next to the assembly under test.
        string path = Path.Combine(
            Path.GetDirectoryName(ComponentsAssembly.Location)!,
            "BlazorNative.Components.xml");

        Assert.True(File.Exists(path),
            $"BlazorNative.Components.xml not found at {path} — GenerateDocumentationFile is the "
            + "reference's raw material; without it there is nothing to publish and nothing to "
            + "pin.");
        return XDocument.Load(path);
    }

    private static string Excerpt(string text, int at)
    {
        string flat = Regex.Replace(text, @"\s+", " ");
        int start = Math.Max(0, Math.Min(at, flat.Length) - 60);
        int len = Math.Min(140, flat.Length - start);
        return flat.Substring(start, len).Trim();
    }
}
