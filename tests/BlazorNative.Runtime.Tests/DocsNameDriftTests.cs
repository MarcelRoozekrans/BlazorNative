using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using BlazorNative.DocSamples;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DocsNameDriftTests — every inline `Bn…`/`BlazorNative…` name on a hand-written
// docs page resolves to something that exists (Task 3, phase 15.5).
//
// DocsSamplesDriftTests holds a docs page's CODE FENCES to compiling.
// EveryFenceOnAHandWrittenPage_DeclaresAKnownKind cannot see the PROSE around
// those fences — a sentence like "call BnFooBar.Baz()" is never compiled, so a
// renamed or deleted member drifts silently. This pin is the prose half: every
// backtick-quoted span on a hand-written page that starts with `Bn` or
// `BlazorNative` must resolve to something that is real TODAY.
//
// Everything here is DERIVED, never restated, from four resolution sources —
// Step 3's triage widened three of them once real, resolvable names turned up
// unresolved; none of the three widenings is an allow-list, each is recorded
// here and in docs/plans/2026-09-25-phase-15.5-docs-audit.md's `## Names`:
//   1. the public .NET surface of the seven shipped packages — reached the
//      same way DocsSamplesDriftTests.ShippedAssemblyAnchors reaches it, one
//      verified `typeof` per package. Widened to also accept
//      `<AssemblyName>.dll` for any shipped assembly name — a real build
//      output (`getting-started/quick-start.md` names it), just never a
//      tracked file, so source 3 alone could not see it. The pattern is
//      .NET's own naming convention, not a special case;
//   2. shell declarations — every `class`/`struct`/`enum`/`object`/`protocol`/
//      `interface`/`func`/`fun Bn…` in a Kotlin/Swift/Objective-C++ source
//      under src/, main AND test alike (the Swift XCTest suites live under
//      src/BlazorNative.Apple/BnHostTests/, not under tests/). Widened with a
//      FILE-SCOPED member map (ShellDeclarationsAndMembers): every
//      `func`/`fun`/`let`/`var`/`case` name anywhere in a file that also
//      declares a `Bn…` container counts as that container's member — needed
//      for `BnImageDemoTests.testCleartextLoopbackIsPermittedByATS`, a real
//      XCTest method with no `Bn` prefix of its own, so no other source ever
//      sees it;
//   3. repo file AND directory names, read via `git ls-files` (never a raw
//      directory walk — bin/obj/artifacts sit physically under these same
//      trees, and a locally-built DLL must never make a doc span resolve for
//      a reason CI cannot reproduce). A directory name counts alongside a
//      file name: `BnHostTests` (the XCTest folder) and `BlazorNative.RouteGen`
//      (the tool project's folder) are both named in prose without an
//      extension, and both are real, tracked paths, not files. Widened to
//      also index each file's STEM (name without extension) — prose names a
//      component by its bare type name far more often than by its file name
//      (`guides/state.md`'s "the sample app's `BnThemedPanel`", never "…its
//      `BnThemedPanel.razor`"), and that bare name is real precisely because
//      the file is;
//   4. `BlazorNative.*` diagnostic-category string literals in
//      src/BlazorNative.Analyzers/**/*.cs — `BlazorNative.MobilePolicy` and
//      `BlazorNative.Interop` are real names a consumer configures against in
//      .editorconfig, but they are C# constants, not types, so no other
//      source ever sees them.
//
// The resolution rule for a dotted span `A.B[.C…]` (the span pattern strips a
// trailing `<...>` generic-argument list before this rule ever runs):
//   - the WHOLE string is a namespace, an assembly, an `<Assembly>.dll`, or a
//     file/directory name (or file stem);
//   - `A` is a shipped type's simple name, or a `Bn…` shell declaration, and
//     `B` is one of its members (source 1: public members or nested types;
//     source 2: any func/fun/let/var/case declared in the same file — the
//     only two sources with member data at all);
//   - the full dotted string is a namespace-qualified shipped type.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - The MEANING of a sentence. "BnView carries the flex surface" names a
//     real type and can still expire the day BnView stops carrying it; only
//     the docs audit (docs/plans/2026-09-25-phase-15.5-docs-audit.md) catches
//     that. This pin only checks that the NAME still exists.
//   - A name without the `Bn`/`BlazorNative` prefix. `NativeRenderer` and
//     `ICamera` are real shipped types; a docs page is free to name them with
//     no backticks or with backticks, and neither is scanned here.
//   - A member's SIGNATURE. `BnLog.Sink` is checked to be a public member of
//     BnLog, never to be a property of the type or arity the prose implies.
//     A shell member is checked even more loosely: FILE-scoped, not
//     brace-scoped, so a method declared elsewhere in the same file as an
//     unrelated second `Bn…` type would be misattributed to it too.
//   - A member or nested type on source 3 or 4. Those two sources supply
//     NAMES only, never members — a Swift class's dotted member reference
//     with trailing punctuation (`BnStderrPump.install()`) never reaches the
//     resolver at all, because the span pattern's closing backtick cannot
//     follow the trailing `()` a real call always carries.
//   - Fenced code blocks. `DocSampleParser.Fences` finds every fence with a
//     non-empty language, 0–3 leading spaces included; a span inside an
//     UNLABELED fence (no language at all) is invisible to `Fences` and is
//     therefore scanned as if it were prose — the same blind spot
//     DocsSamplesDriftTests carries, inherited rather than duplicated.
//   - `~~~`-fenced blocks, four-or-more-backtick fences, and double-backtick
//     spans (`` `` `Foo` `` ``) — none exist on a hand-written page today.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DocsNameDriftTests
{
    private const string AnalyzersDir = "src/BlazorNative.Analyzers";

    /// <summary>Re-measured 2026-09-25 (Task 2 had already edited pages since the
    /// brief's own 225/86 measurement, so this is the real, current count): 230
    /// inline Bn/BlazorNative spans across the 20 hand-written pages, all
    /// resolved once Step 3's triage landed. A floor, not a count — Rule 2. Fewer
    /// means the scan stopped seeing pages, or a real name silently stopped
    /// resolving and someone raised the floor to match instead of fixing it.</summary>
    private const int MinimumResolvedSpans = 230;

    private static readonly Regex Span = new(
        @"`(?<name>(?:Bn|BlazorNative)[A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*)(?:<[^`]*>)?`",
        RegexOptions.CultureInvariant);

    private static readonly Regex ShellDeclaration = new(
        @"\b(?:class|struct|enum|object|protocol|interface|func|fun)\s+(?<name>Bn[A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);

    private static readonly Regex AnalyzerStringLiteral = new(
        @"""(?<name>BlazorNative\.[A-Za-z0-9_.]*)""",
        RegexOptions.CultureInvariant);

    /// <summary>One verified-public anchor type per shipped package — the same
    /// seven DocsSamplesDriftTests.ShippedAssemblyAnchors anchors, kept as its
    /// own copy here because that array is private to that class and this
    /// pin's resolver needs more off each assembly (members, namespaces) than
    /// that one's ShippedTypeNames() exposes.</summary>
    private static readonly Type[] ShippedAssemblyAnchors =
    [
        typeof(BlazorNative.Components.BnView),
        typeof(BlazorNative.Core.BnLog),
        typeof(BlazorNative.Device.ICamera),
        typeof(BlazorNative.Http.BridgeHttpHandler),
        typeof(BlazorNative.Renderer.NativeRenderer),
        typeof(BlazorNative.Runtime.BlazorNativeApp),
        typeof(BlazorNative.Testing.BnTestHost),
    ];

    /// <summary>Everything the resolver needs, built once from the four
    /// sources.</summary>
    internal sealed record Resolver(
        HashSet<string> Namespaces,
        HashSet<string> AssemblyNames,
        HashSet<string> RepoNames,
        HashSet<string> NamespaceQualifiedTypes,
        Dictionary<string, HashSet<string>> TypeMembers,
        HashSet<string> ShellDeclarations,
        Dictionary<string, HashSet<string>> ShellMembers,
        HashSet<string> AnalyzerLiterals)
    {
        public bool Resolves(string name)
        {
            if (Namespaces.Contains(name) || AssemblyNames.Contains(name)
                || RepoNames.Contains(name) || NamespaceQualifiedTypes.Contains(name)
                || ShellDeclarations.Contains(name) || AnalyzerLiterals.Contains(name))
                return true;

            // A shipped assembly's build output: <AssemblyName>.dll is what `dotnet build`
            // actually produces, even though the dll itself is never a tracked file — the
            // pattern is fixed by .NET's own naming convention, so this is exactly as
            // derived as the namespace/assembly checks above, not a special case.
            if (name.EndsWith(".dll", StringComparison.Ordinal) && AssemblyNames.Contains(name[..^4]))
                return true;

            if (TypeMembers.ContainsKey(name)) return true; // bare shipped type name

            int lastDot = name.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == name.Length - 1) return false;

            string head = name[..lastDot];
            string tail = name[(lastDot + 1)..];
            if (TypeMembers.TryGetValue(head, out HashSet<string>? members) && members.Contains(tail))
                return true;
            return ShellMembers.TryGetValue(head, out HashSet<string>? shellMembers) && shellMembers.Contains(tail);
        }
    }

    /// <summary>Strips a generic type's reflection arity suffix (`` `1 ``) —
    /// <c>BnList&lt;TItem&gt;</c> is named <c>BnList</c> in prose, never
    /// <c>BnList`1</c>.</summary>
    private static string SimpleName(Type t)
    {
        int backtick = t.Name.IndexOf('`');
        return backtick < 0 ? t.Name : t.Name[..backtick];
    }

    internal static Resolver BuildResolver(string root)
    {
        Assembly[] assemblies = [.. ShippedAssemblyAnchors.Select(t => t.Assembly).Distinct()];
        Type[] types = [.. assemblies.SelectMany(a => a.GetTypes()).Where(t => t.IsPublic)];

        var namespaces = new HashSet<string>(types.Select(t => t.Namespace).Where(n => n is not null)!, StringComparer.Ordinal);
        // Every PREFIX of a real namespace counts as a namespace too — every shipped
        // namespace here is "BlazorNative.<Package>", so this adds exactly one name,
        // the bare "BlazorNative" root, which no type lives in directly but which is
        // genuinely the enclosing namespace of the whole shipped surface (logging.md
        // names it as the Android shell's literal logcat tag, a real Kotlin `const val`
        // this pin has no other source for — narrower than adding a whole language's
        // string-literal scan for one span).
        foreach (string ns in namespaces.ToList())
        {
            string[] parts = ns.Split('.');
            for (int i = 1; i < parts.Length; i++)
                namespaces.Add(string.Join('.', parts.Take(i)));
        }
        var assemblyNames = new HashSet<string>(assemblies.Select(a => a.GetName().Name!), StringComparer.Ordinal);
        var namespaceQualified = new HashSet<string>(types.Select(t => $"{t.Namespace}.{SimpleName(t)}"), StringComparer.Ordinal);

        var members = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Type t in types)
        {
            string name = SimpleName(t);
            if (!members.TryGetValue(name, out HashSet<string>? set))
                members[name] = set = new HashSet<string>(StringComparer.Ordinal);
            foreach (MemberInfo m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                set.Add(m.Name);
            foreach (Type nested in t.GetNestedTypes(BindingFlags.Public))
                set.Add(SimpleName(nested));
        }

        HashSet<string> repoNames = RepoNames(root);
        (HashSet<string> shellDeclarations, Dictionary<string, HashSet<string>> shellMembers) = ShellDeclarationsAndMembers(root);
        HashSet<string> analyzerLiterals = AnalyzerLiterals(root);

        return new Resolver(namespaces, assemblyNames, repoNames, namespaceQualified, members, shellDeclarations, shellMembers, analyzerLiterals);
    }

    /// <summary>Kotlin/Swift/Objective-C++ extensions — a bare shell type name
    /// is deliberately NOT re-derived here as a file stem (see the STEM
    /// paragraph below): it stays exclusively source 2's job, so the two
    /// sources stay independently necessary rather than silently
    /// overlapping.</summary>
    private static readonly HashSet<string> ShellExtensions = new(StringComparer.OrdinalIgnoreCase) { ".kt", ".swift", ".mm" };

    /// <summary>Source 3: every TRACKED file and directory name under the six
    /// source trees, via `git ls-files` — never a raw directory walk. bin/,
    /// obj/ and artifacts/ sit physically inside src/, tests/, samples/ and
    /// tools/, and a locally-built DLL must never be why a doc span resolves;
    /// only a checked-in path can be, so only git's index is asked. Every path
    /// SEGMENT counts on its own — a directory name (`BnHostTests`,
    /// `BlazorNative.RouteGen`) resolves exactly like a file name.
    ///
    /// The final segment of a tracked path — the actual file, never a
    /// directory — ALSO contributes its STEM (name without extension), unless
    /// the extension is Kotlin/Swift/Objective-C++: prose names a C#/Razor
    /// component by its bare type name far more often than by its file name
    /// — `guides/state.md` says "the sample app's `BnThemedPanel`", not
    /// "…its `BnThemedPanel.razor`" — and that bare name is real precisely
    /// because the file exists. Computing a stem from every INTERMEDIATE
    /// directory segment too would be wrong in a different way:
    /// `Path.GetFileNameWithoutExtension` on a directory that merely
    /// CONTAINS a dot, like `BlazorNative.Apple`, treats `.Apple` as an
    /// extension and silently manufactures a bare `BlazorNative` resolution
    /// nothing in the doc corpus actually earns — this method never calls it
    /// on anything but the true last segment.</summary>
    internal static HashSet<string> RepoNames(string root)
    {
        string[] roots = ["src", "tests", "samples", "tools", "templates", "scripts"];
        string output = Git(root, $"ls-files -- {string.Join(' ', roots)}");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] segments = line.Split('/');
            foreach (string segment in segments)
                if (segment.Length > 0) names.Add(segment);

            string fileName = segments[^1];
            string extension = Path.GetExtension(fileName);
            if (!ShellExtensions.Contains(extension))
                names.Add(Path.GetFileNameWithoutExtension(fileName));
        }
        return names;
    }

    /// <summary>Runs git from the repo root and reds loudly on a non-zero exit
    /// or a failure to start — a pin whose subject it cannot enumerate must
    /// never pass over the gap (the TemplateDriftTests.Git precedent).</summary>
    private static string Git(string root, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            Assert.Fail($"could not run `git {arguments}`: {ex.Message} — the repo-name resolution source cannot see its subject and must not pass vacuously.");
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"`git {arguments}` exited {process.ExitCode}: {stderr}");
        return stdout;
    }

    /// <summary>Source 2: every `Bn…` name declared by a `class`/`struct`/
    /// `enum`/`object`/`protocol`/`interface`/`func`/`fun` in a Kotlin, Swift
    /// or Objective-C++ source under src/ — main and test sources alike, since
    /// the XCTest suites live under src/BlazorNative.Apple/BnHostTests/, not
    /// under tests/.
    ///
    /// Also returns a FILE-SCOPED member map: for every `Bn…`-named container
    /// this regex finds in a file, every `func`/`fun`/`let`/`var`/`case` name
    /// anywhere in that SAME file counts as one of its members — for example
    /// `BnImageDemoTests.testCleartextLoopbackIsPermittedByATS` names a real
    /// XCTest method the doc turns "an exemption from an assumption into a
    /// checked fact"; the method name itself carries no `Bn` prefix, so
    /// nothing else here ever sees it. File-scoped, not brace-scoped: a
    /// method declared elsewhere in the same file as an unrelated second `Bn…`
    /// type would be misattributed to it too, which this pin accepts as the
    /// same looseness source 1's member check already has (a member is
    /// checked to exist, never to belong to the exact type the prose
    /// implies).</summary>
    private static readonly Regex ShellMember = new(
        @"\b(?:func|fun|let|var|case)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);

    internal static (HashSet<string> Declarations, Dictionary<string, HashSet<string>> Members) ShellDeclarationsAndMembers(string root)
    {
        string srcRoot = Path.Combine(root, "src");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var members = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string ext in new[] { "*.kt", "*.swift", "*.mm" })
        {
            foreach (string file in Directory.EnumerateFiles(srcRoot, ext, SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                List<string> declared = [.. ShellDeclaration.Matches(text).Select(m => m.Groups["name"].Value)];
                if (declared.Count == 0) continue;

                names.UnionWith(declared);
                HashSet<string> fileMembers = [.. ShellMember.Matches(text).Select(m => m.Groups["name"].Value)];
                foreach (string declaredName in declared)
                {
                    if (!members.TryGetValue(declaredName, out HashSet<string>? set))
                        members[declaredName] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.UnionWith(fileMembers);
                }
            }
        }
        return (names, members);
    }

    /// <summary>Source 4: every `BlazorNative.*` string literal in
    /// src/BlazorNative.Analyzers/**/*.cs — the diagnostic categories
    /// (`BlazorNative.MobilePolicy`, `BlazorNative.Interop`) a consumer
    /// configures against in .editorconfig. These are C# constants, not
    /// types, so no other source ever sees them; without this source, both
    /// names named in analyzers.md are false positives for a real thing.</summary>
    internal static HashSet<string> AnalyzerLiterals(string root)
    {
        string dir = Path.Combine(root, AnalyzersDir);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal)) continue;
            foreach (Match m in AnalyzerStringLiteral.Matches(File.ReadAllText(file)))
                names.Add(m.Groups["name"].Value);
        }
        return names;
    }

    /// <summary>Every fence's [firstLine, lastLine] (1-based, inclusive) —
    /// derived from <see cref="DocSampleParser.Fences"/>'s own Line and Body,
    /// never a second fence parser. A span inside these lines is code, not
    /// prose, and is excluded before the name scan ever runs.</summary>
    internal static List<(int First, int Last)> FenceLineRanges(string markdown)
    {
        var ranges = new List<(int, int)>();
        foreach (Fence f in DocSampleParser.Fences(markdown))
        {
            int bodyLines = f.Body.Length == 0 ? 0 : f.Body.Split('\n').Length;
            ranges.Add((f.Line, f.Line + bodyLines + 1)); // opening line .. closing fence line
        }
        return ranges;
    }

    /// <summary>Every `Bn…`/`BlazorNative…` inline span in <paramref name="markdown"/>,
    /// outside a fenced code block, as (line, name) 1-based.</summary>
    internal static List<(int Line, string Name)> Spans(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        List<(int First, int Last)> fences = FenceLineRanges(markdown);
        var spans = new List<(int, string)>();
        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            if (fences.Any(f => lineNo >= f.First && lineNo <= f.Last)) continue;
            foreach (Match m in Span.Matches(lines[i]))
                spans.Add((lineNo, m.Groups["name"].Value));
        }
        return spans;
    }

    internal static List<string> Unresolved(string root, Resolver resolver)
    {
        var bad = new List<string>();
        foreach (string page in DocSampleParser.HandWrittenPages(root))
        {
            string markdown = File.ReadAllText(Path.Combine(root, page));
            foreach ((int line, string name) in Spans(markdown))
                if (!resolver.Resolves(name))
                    bad.Add($"{page}:{line} `{name}`");
        }
        return bad;
    }

    [Fact]
    public void EveryInlineName_OnAHandWrittenPage_Resolves()
    {
        string root = BnRepo.Root();
        Resolver resolver = BuildResolver(root);

        int total = DocSampleParser.HandWrittenPages(root)
            .SelectMany(p => Spans(File.ReadAllText(Path.Combine(root, p))))
            .Count();
        Assert.True(total >= MinimumResolvedSpans,
            $"found {total} inline Bn/BlazorNative spans across the hand-written pages, fewer than the measured {MinimumResolvedSpans} — the scan has stopped seeing the docs (Rule 2).");

        List<string> bad = Unresolved(root, resolver);
        Assert.True(bad.Count == 0,
            "these inline names resolve to nothing — renamed, removed, or never real: " + string.Join(" | ", bad));
    }

    [Fact]
    public void TheResolver_SeesAPlantedName()
    {
        Resolver resolver = BuildResolver(BnRepo.Root());
        Assert.False(resolver.Resolves("BnDoesNotExist"));
        Assert.False(resolver.Resolves("BnView.NoSuchMember"));
        Assert.True(resolver.Resolves("BnView"));
    }
}
