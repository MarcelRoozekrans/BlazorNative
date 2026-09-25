using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using BlazorNative.DocSamples;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DocsNameDriftTests — every inline `Bn…`/`BlazorNative…` name on a hand-written
// docs page resolves to something that exists (Task 3, phase 15.5; fix round 1
// widened the span pattern and narrowed the shell-member map — see below).
//
// DocsSamplesDriftTests holds a docs page's CODE FENCES to compiling.
// EveryFenceOnAHandWrittenPage_DeclaresAKnownKind cannot see the PROSE around
// those fences — a sentence like "call BnFooBar.Baz()" is never compiled, so a
// renamed or deleted member drifts silently. This pin is the prose half: every
// backtick-quoted span on a hand-written page that starts with `Bn` or
// `BlazorNative` (optionally behind an `@`) must resolve to something real.
//
// THE SPAN ITSELF is not just a plain dotted name any more (fix round 1). Prose
// wraps a real identifier in decorations a bare `Bn[A-Za-z0-9_.]*` pattern
// cannot see through: a leading `@` for a Razor directive (`@BnLength.Percent(50)`),
// a generic `<...>` group BEFORE a `.Member` (`BnList<TItem>.Width`), a trailing
// `?` (`BnLength?`), or a trailing call `(...)` (`BnListWindow.Compute(...)`).
// `ExtractName` captures the WHOLE backtick content once it starts with `@`/`Bn`/
// `BlazorNative`, then strips exactly those four decorations, in that order, and
// validates what is left against a strict dotted-identifier pattern. What does
// NOT reduce cleanly is never counted as a span at all — see Rule 5.
//
// Everything here is DERIVED, never restated, from four resolution sources —
// each has been widened at least once since Task 3 first measured it, always to
// cover a real name a source could not yet see, never an allow-list; every
// widening is recorded here and in docs/plans/2026-09-25-phase-15.5-docs-audit.md's
// `## Names`:
//   1. The seven shipped packages' FULL reflected surface — one verified
//      `typeof` per package anchors the assembly, then EVERY type reflection
//      finds (public AND internal alike — reflection reads the assembly's
//      actual current members either way, so dropping the public-only filter
//      costs no drift protection, it just extends the same live check to
//      non-public surface too) and each type's members (public and
//      non-public). Needed for `BnListWindow.Compute`
//      (`migrating/typed-lengths.md:223`), a real `internal static` method a
//      migration note deliberately names to explain a design constraint.
//      Also widened twice for other real names: every PREFIX of a real
//      namespace counts as a namespace too (adds exactly the bare
//      `BlazorNative` root — `logging.md:86` names it as the Android shell's
//      literal Kotlin `const val TAG = "BlazorNative"`, and no source scans
//      Kotlin string-literal constants); and `<AssemblyName>.dll` resolves
//      for any shipped assembly name (`quick-start.md:52` names a real but
//      untracked `dotnet publish` output — .NET's own naming convention, not
//      a special case).
//   2. Shell declarations — every `class`/`struct`/`enum`/`object`/`protocol`/
//      `interface`/`func`/`fun Bn…` in a Kotlin/Swift/Objective-C++ source
//      under src/, main AND test alike (the Swift XCTest suites live under
//      src/BlazorNative.Apple/BnHostTests/, not under tests/), enumerated
//      through the SAME `git ls-files` call source 3 uses — never a second,
//      independent directory walk — and with both comment forms stripped via
//      `CommentStrippedSource.Strip` first, so a declaration mentioned only
//      inside a comment is not mistaken for a live one. FILE-SCOPED, TYPE-
//      NARROWED member map: a `func`/`fun` name counts as a member ONLY of
//      the ONE declared container whose name equals the file's own stem
//      (`BnImageDemoTests.swift` → `BnImageDemoTests`) — needed for
//      `BnImageDemoTests.testCleartextLoopbackIsPermittedByATS`, a real
//      XCTest method with no `Bn` prefix of its own. Fix round 1 narrowed
//      this from "every func/fun/let/var/case in the file, attributed to
//      EVERY `Bn…` container the file declares" after review found it too
//      wide: `BnWidgetMapper.swift` alone declares 14 containers, and every
//      local `let`/`var` in the file was resolving as a member of all 14.
//   3. Repo file AND directory names, read via `git ls-files` (never a raw
//      directory walk — bin/obj/artifacts sit physically under these same
//      trees, and a locally-built DLL must never make a doc span resolve for
//      a reason CI cannot reproduce; ONLY a tracked path counts). A
//      directory name counts alongside a file name: `BnHostTests` (the
//      XCTest folder) and `BlazorNative.RouteGen` (the tool project's
//      folder) are both named in prose without an extension, and both are
//      real, tracked paths, not files. The final (file) segment of a tracked
//      path ALSO contributes its full STEM — every extension segment
//      dropped, not just the last one, so `BnDeepLinkVectors.g.cs` gives
//      `BnDeepLinkVectors` — unless the extension is Kotlin/Swift/
//      Objective-C++, which stays exclusively source 2's job so the two
//      sources remain independently necessary. Needed for `BnThemedPanel`,
//      `BnDemo` and three xUnit class names, all named bare without their
//      file extension.
//   4. `BlazorNative.*` diagnostic-category string literals in
//      src/BlazorNative.Analyzers/**/*.cs — `BlazorNative.MobilePolicy` and
//      `BlazorNative.Interop` are real names a consumer configures against in
//      .editorconfig, but they are C# constants, not types, so no other
//      source ever sees them.
//
// The resolution rule for a dotted name `A.B[.C…]` (already decoration-free —
// `ExtractName` ran first):
//   - the WHOLE string is a namespace (or any PREFIX of one), an assembly, an
//     `<Assembly>.dll`, or a file/directory name (or file stem);
//   - `A` is a shipped type's simple name, or a `Bn…` shell declaration, and
//     `B` is one of its members (source 1: any member, public or not, or a
//     nested type; source 2: a `func`/`fun` declared in the file matching
//     `A`'s own name — the only two sources with member data at all);
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
//   - A member's SIGNATURE. `BnLog.Sink` is checked to be a member of `BnLog`
//     that exists, never to have the type or arity the prose implies. Same
//     looseness for a shell member: attributed to the ONE container matching
//     the file's stem, but never brace-scoped inside that container, so a
//     `func` anywhere in the file — including inside a closure or a nested
//     type — still counts.
//   - A member or nested type on source 3 or 4. Those two sources supply
//     NAMES only, never members.
//   - A span whose `Bn`/`BlazorNative` is not the FIRST thing inside the
//     backticks. `AddBlazorNativeHttp()`, `libBnRuntimeSupport.a`,
//     `default(BnLength)`, a full statement like `BnShell shell =
//     BnShell.Ios`, and a markup tag (`<BnButton MaxWidth="200" />`) all
//     mention a real name somewhere in the span, but none of them starts
//     with `@`/`Bn`/`BlazorNative` right at the opening backtick, so the
//     span pattern never even attempts them.
//   - A span that DOES start there but does not reduce cleanly once
//     decorated. A path:line span (`BnHost/BnRuntime.swift:184`) keeps its
//     `/` and `:` after every decoration is stripped, so it fails the final
//     identifier check and is silently skipped rather than counted.
//     Measured 2026-09-25 (fix round 1), both shapes together: 59 skipped
//     spans across the 20 pages, outside fenced code (a page:line inventory
//     is in the audit record's `## Names`).
//   - A bare `BlazorNative` ALWAYS resolves once matched — the namespace-
//     prefix widening above holds regardless of what a given span's prose
//     actually means by it (a Kotlin log tag, an assembly root, a product
//     name); this pin checks only that the literal text is real somewhere.
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

    /// <summary>Re-measured 2026-09-25 (fix round 1, after the span pattern was
    /// widened to see decorated identifiers — leading `@`, a mid-name generic, a
    /// trailing `?`, a trailing call): 250 inline Bn/BlazorNative spans across the
    /// 20 hand-written pages, all resolved (up from 230; the widened pattern now
    /// sees real spans, like `BnLength?` and `@BnAutoLength.Auto`, that used to
    /// be invisible). A floor, not a count — Rule 2. Fewer means the scan
    /// stopped seeing pages, or a real name silently stopped resolving and
    /// someone raised the floor to match instead of fixing it.</summary>
    private const int MinimumResolvedSpans = 250;

    /// <summary>Captures the WHOLE backtick content once it starts with an
    /// optional `@` then `Bn`/`BlazorNative` — permissive on purpose. What this
    /// captures is not yet a name; <see cref="ExtractName"/> decides that.</summary>
    private static readonly Regex Span = new(
        @"`(?<raw>@?(?:Bn|BlazorNative)[^`]*)`",
        RegexOptions.CultureInvariant);

    private static readonly Regex GenericGroup = new(@"<[^<>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex CleanName = new(@"^(?:Bn|BlazorNative)[A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*$", RegexOptions.CultureInvariant);

    private static readonly Regex ShellDeclaration = new(
        @"\b(?:class|struct|enum|object|protocol|interface|func|fun)\s+(?<name>Bn[A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);

    /// <summary>Fix round 1: narrowed from `func|fun|let|var|case` — a `let`/`var`
    /// matched every LOCAL variable in a file too, not just its declared members,
    /// and `BnWidgetMapper.swift` alone declares 14 `Bn…` containers, so a local
    /// in any one of them was resolving as a member of all 14. Only one span in
    /// the corpus needs a shell member at all, and it names a `func`.</summary>
    private static readonly Regex ShellMember = new(
        @"\b(?:func|fun)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);

    private static readonly Regex AnalyzerStringLiteral = new(
        @"""(?<name>BlazorNative\.[A-Za-z0-9_.]*)""",
        RegexOptions.CultureInvariant);

    /// <summary>Kotlin/Swift/Objective-C++ extensions — a bare shell type name is
    /// deliberately NOT re-derived here as a file stem: it stays exclusively
    /// source 2's job, so source 2 and source 3 stay independently necessary
    /// rather than silently overlapping (proven by the N3 mutation).</summary>
    private static readonly HashSet<string> ShellExtensions = new(StringComparer.OrdinalIgnoreCase) { ".kt", ".swift", ".mm" };

    /// <summary>One verified-public anchor type per shipped package, to reach
    /// each assembly — the same seven DocsSamplesDriftTests.ShippedAssemblyAnchors
    /// anchors, kept as its own copy here because that array is private to that
    /// class. The TYPES this pin reflects off each assembly are not limited to
    /// public ones (see the header comment); only the ANCHOR itself needs to be
    /// a type known to be real.</summary>
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

    /// <summary>A tracked path's file name with EVERY extension segment dropped,
    /// not just the last one — <c>Path.GetFileNameWithoutExtension</c> applied
    /// once turns <c>BnDeepLinkVectors.g.cs</c> into <c>BnDeepLinkVectors.g</c>;
    /// applied until no extension remains, it reaches the real stem a consumer
    /// would actually call the file.</summary>
    private static string FullStem(string fileName)
    {
        string stem = fileName;
        while (Path.GetExtension(stem).Length > 0)
            stem = Path.GetFileNameWithoutExtension(stem);
        return stem;
    }

    internal static Resolver BuildResolver(string root)
    {
        Assembly[] assemblies = [.. ShippedAssemblyAnchors.Select(t => t.Assembly).Distinct()];
        // Every type reflection finds, public or not — see the header comment's
        // source-1 paragraph. Reflection reads the assembly's ACTUAL current
        // members either way, so this costs no drift protection.
        Type[] types = [.. assemblies.SelectMany(a => a.GetTypes())];

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
            // Public members keep the ORIGINAL shape (inherited included, e.g.
            // ToString/Equals) so nothing that already resolved stops resolving.
            // Non-public is DECLARED-ONLY: without that bound, a type deriving from
            // a framework base class (ComponentBase, …) would pull in that base
            // class's entire internal/private surface too, which is never this
            // pin's subject.
            foreach (MemberInfo m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                set.Add(m.Name);
            foreach (MemberInfo m in t.GetMembers(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                set.Add(m.Name);
            foreach (Type nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                set.Add(SimpleName(nested));
        }

        List<string> trackedFiles = TrackedFiles(root);
        HashSet<string> repoNames = RepoNames(trackedFiles);
        (HashSet<string> shellDeclarations, Dictionary<string, HashSet<string>> shellMembers) = ShellDeclarationsAndMembers(root, trackedFiles);
        HashSet<string> analyzerLiterals = AnalyzerLiterals(root);

        return new Resolver(namespaces, assemblyNames, repoNames, namespaceQualified, members, shellDeclarations, shellMembers, analyzerLiterals);
    }

    /// <summary>The ONE `git ls-files` call both source 2 and source 3 read from
    /// — fix round 1: source 2 used to walk src/ directly with
    /// <c>Directory.EnumerateFiles</c>, a second, independent way of finding the
    /// same files source 3 already finds through git, and the two could in
    /// principle disagree about what is tracked.</summary>
    internal static List<string> TrackedFiles(string root)
    {
        string[] roots = ["src", "tests", "samples", "tools", "templates", "scripts"];
        string output = Git(root, $"ls-files -- {string.Join(' ', roots)}");
        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>Source 3: every TRACKED file and directory name under the six
    /// source trees — never a raw directory walk. bin/, obj/ and artifacts/ sit
    /// physically inside src/, tests/, samples/ and tools/, and a locally-built
    /// DLL must never make a doc span resolve for a reason CI cannot reproduce;
    /// only a checked-in path can be, so only git's index is asked. Every path
    /// SEGMENT counts on its own — a directory name (`BnHostTests`,
    /// `BlazorNative.RouteGen`) resolves exactly like a file name.
    ///
    /// The final segment of a tracked path — the actual file, never a
    /// directory — ALSO contributes its full STEM (<see cref="FullStem"/>,
    /// every extension segment dropped), unless the extension is Kotlin/Swift/
    /// Objective-C++: prose names a C#/Razor component by its bare type name far
    /// more often than by its file name — `guides/state.md` says "the sample
    /// app's `BnThemedPanel`", not "…its `BnThemedPanel.razor`" — and that bare
    /// name is real precisely because the file exists. Computing a stem from
    /// every INTERMEDIATE directory segment too would be wrong in a different
    /// way: <c>Path.GetFileNameWithoutExtension</c> on a directory that merely
    /// CONTAINS a dot, like `BlazorNative.Apple`, treats `.Apple` as an
    /// extension and silently manufactures a bare `BlazorNative` resolution
    /// nothing in the doc corpus actually earns — this method never stems
    /// anything but the true last segment.</summary>
    internal static HashSet<string> RepoNames(List<string> trackedFiles)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in trackedFiles)
        {
            string[] segments = line.Split('/');
            foreach (string segment in segments)
                if (segment.Length > 0) names.Add(segment);

            string fileName = segments[^1];
            string extension = Path.GetExtension(fileName);
            if (!ShellExtensions.Contains(extension))
                names.Add(FullStem(fileName));
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
    /// `enum`/`object`/`protocol`/`interface`/`func`/`fun` in a Kotlin, Swift or
    /// Objective-C++ source under src/ — main and test sources alike, since the
    /// Swift XCTest suites live under src/BlazorNative.Apple/BnHostTests/, not
    /// under tests/. Enumerated from <paramref name="trackedFiles"/>, the same
    /// `git ls-files` result source 3 reads, never a second directory walk. Both
    /// comment forms are stripped first via <see cref="CommentStrippedSource.Strip"/>
    /// — the pin standard's one shared stripper — so a declaration named only
    /// inside a `//` or `/* */` comment is not mistaken for a live one.
    ///
    /// Also returns a FILE-SCOPED, TYPE-NARROWED member map: a `func`/`fun` name
    /// found anywhere in a file counts as a member of exactly ONE container —
    /// the `Bn…` declaration in that same file whose name equals the file's own
    /// <see cref="FullStem"/>. Needed for
    /// `BnImageDemoTests.testCleartextLoopbackIsPermittedByATS`
    /// (`BnImageDemoTests.swift` → container `BnImageDemoTests`); the method
    /// name itself carries no `Bn` prefix, so nothing else here ever sees it.
    /// Narrowed in fix round 1 from "every func/fun/let/var/case in the file,
    /// attributed to EVERY `Bn…` container the file declares" — that shape let
    /// a local variable in any one of `BnWidgetMapper.swift`'s 14 declared
    /// containers resolve as a member of all 14, which was never the claim
    /// being checked. Still file-scoped rather than brace-scoped inside the ONE
    /// matched container (Rule 5): a `func` anywhere in the file, including one
    /// nested inside a different type, still counts.</summary>
    internal static (HashSet<string> Declarations, Dictionary<string, HashSet<string>> Members) ShellDeclarationsAndMembers(string root, List<string> trackedFiles)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var members = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string relativePath in trackedFiles)
        {
            if (!relativePath.StartsWith("src/", StringComparison.Ordinal)) continue;
            string fileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];
            if (!ShellExtensions.Contains(Path.GetExtension(fileName))) continue;

            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string text = CommentStrippedSource.Strip(File.ReadAllText(path));
            List<string> declared = [.. ShellDeclaration.Matches(text).Select(m => m.Groups["name"].Value)];
            if (declared.Count == 0) continue;
            names.UnionWith(declared);

            string stem = FullStem(fileName);
            if (!declared.Contains(stem, StringComparer.Ordinal)) continue; // no container named after this file

            HashSet<string> fileMembers = [.. ShellMember.Matches(text).Select(m => m.Groups["name"].Value)];
            if (!members.TryGetValue(stem, out HashSet<string>? set))
                members[stem] = set = new HashSet<string>(StringComparer.Ordinal);
            set.UnionWith(fileMembers);
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

    /// <summary>Reduces a captured RAW span (the backtick content, already known
    /// to start with an optional `@` then `Bn`/`BlazorNative`) to a bare dotted
    /// name by stripping, in order: the leading `@` if present; every `<...>`
    /// generic-argument group wherever it appears, not only trailing
    /// (`BnList<TItem>.Width` → `BnList.Width`); a single trailing `?`
    /// (`BnLength?` → `BnLength`); a single trailing BALANCED call `(...)`
    /// (`BnListWindow.Compute(...)` → `BnListWindow.Compute`). Returns null when
    /// what is left does not fully reduce to a clean dotted identifier — a
    /// path:line span (`BnHost/BnRuntime.swift:184`) keeps its `/` and `:`
    /// after every strip and is never counted as a span at all (Rule 5).</summary>
    internal static string? ExtractName(string raw)
    {
        string s = raw.StartsWith('@') ? raw[1..] : raw;
        s = GenericGroup.Replace(s, "");
        if (s.EndsWith('?')) s = s[..^1];
        if (s.EndsWith(')'))
        {
            int depth = 0;
            int openIndex = -1;
            for (int i = s.Length - 1; i >= 0; i--)
            {
                if (s[i] == ')') depth++;
                else if (s[i] == '(')
                {
                    depth--;
                    if (depth == 0) { openIndex = i; break; }
                }
            }
            if (openIndex > 0) s = s[..openIndex];
        }
        return CleanName.IsMatch(s) ? s : null;
    }

    /// <summary>Every `Bn…`/`BlazorNative…` inline span in <paramref name="markdown"/>,
    /// outside a fenced code block, as (line, name) 1-based. A raw match that
    /// <see cref="ExtractName"/> cannot reduce to a clean identifier is silently
    /// skipped — not a span at all (Rule 5).</summary>
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
            {
                string? name = ExtractName(m.Groups["raw"].Value);
                if (name is not null) spans.Add((lineNo, name));
            }
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
