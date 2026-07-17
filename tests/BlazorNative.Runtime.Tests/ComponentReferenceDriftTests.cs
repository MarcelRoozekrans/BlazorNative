using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BlazorNative.Components;
using Microsoft.AspNetCore.Components;

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
        string script = Path.Combine(RepoRoot(), "scripts", "generate-reference.ps1");
        Assert.True(File.Exists(script), $"generator script not found: {script}");

        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot(),
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
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

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}

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
        Assert.True(parameters.Count > 100,
            $"reflected only {parameters.Count} [Parameter] properties out of BlazorNative.Components "
            + "— there were 196 when this pin was written. A pin that cannot see its subject must "
            + "never pass vacuously.");

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
            m.Attribute("name")?.Value == "P:BlazorNative.Components.BnView.BackgroundColor");

        var published = PublicSurfaceDocs(xml);
        Assert.True(published.Count > 50,
            $"only {published.Count} PUBLIC documented members — the filter ate the subject.");

        // The vocabulary of a repo talking to itself. Each of these was in the
        // shipped XML when this phase opened.
        (string Pattern, string Why)[] banned =
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
            // mutation caught 3 of 4 patterns and this was the miss.
            (@"\bgolden", "a golden file the reader cannot run"),
            (@"\bHelloComponent\b", "an internal fixture"),
            (@"\bfile header\b", "the reader is looking at a web page, not your source file"),
            (@"awaits \.razor compilation", "it does not — Razor components compile today"),
        ];

        var violations = new List<string>();
        foreach (var (name, text) in published)
            foreach (var (pattern, why) in banned)
            {
                Match m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (m.Success)
                    violations.Add($"    {name}\n        matched /{pattern}/ ({why})\n"
                        + $"        ...{Excerpt(text, m.Index)}...");
            }

        Assert.True(violations.Count == 0,
            $"{violations.Count} PUBLISHED doc comment(s) speak to this repo rather than to a "
            + "stranger.\n\nThis XML is what the docs site's component reference is generated "
            + "from AND what a consumer's IDE shows in a tooltip. Rewrite the comment; do not "
            + "add an exception here.\n\n" + string.Join("\n", violations));
    }

    /// <summary>The documented members the generator PUBLISHES: public members of
    /// public types, plus the public types themselves — the same surface
    /// `--member-accessibility-level public` emits.</summary>
    private static List<(string Name, string Text)> PublicSurfaceDocs(XDocument xml)
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

        var result = new List<(string, string)>();
        foreach (var member in xml.Descendants("member"))
        {
            string id = member.Attribute("name")?.Value ?? "";
            string idNoArgs = id.Contains('(', StringComparison.Ordinal)
                ? id[..id.IndexOf('(', StringComparison.Ordinal)]
                : id;

            if (!publicMemberIds.Contains(id) && !publicMemberIds.Contains(idNoArgs))
                continue;

            // A member of a public type is only published if its own declaring
            // type is public — GetProperties(Public) already guarantees that.
            result.Add((id, member.Value));
        }

        Assert.All(result, r => Assert.True(
            publicTypeNames.Count > 0, "public type set went empty"));
        return result;
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
