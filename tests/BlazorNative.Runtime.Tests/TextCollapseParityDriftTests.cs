using System.Text.RegularExpressions;
using BlazorNative.Renderer;
using BlazorNative.Testing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// TextCollapseParityDriftTests — Phase 13.4 (R7). THE HARNESS'S MODEL OF EACH
// SHELL, HELD AGAINST THAT SHELL'S OWN SOURCE.
//
// WHY THIS EXISTS, AND WHY IT EXISTS IMMEDIATELY. Phase 13.4 fixed a bug in the
// [[safety-claims-without-pins]] class: BnTestTree hard-coded {text, button, input}
// — iOS's collapse predicate — and presented it to consumers as THE contract, so
// `node.Text` on a BnCheckbox answered something true of iOS and silently wrong for
// Android. The fix made the set per-shell and named the shell. But it did so by
// introducing a SECOND hard-coded list, {text, button, input, checkbox, switch},
// with nothing holding it to the Android shell's real behaviour — creating a fresh
// instance of the same bug class while fixing one. This pin is what makes the new
// list a measurement instead of a claim.
//
// ── WHAT THE ANDROID PIN COVERS ──────────────────────────────────────────────
//
// The Kotlin predicate is STRUCTURAL, not a list. WidgetMapper.handleCreate:
//
//     if (rawParent is TextView && rawParent !is android.view.ViewGroup) { … }
//
// so there is no name-by-name list in the shell to compare against. What IS in the
// shell, and what actually decides the answer, is the widget-class factory directly
// below it — the `when (p.nodeType)` that says which Android View class each node
// type becomes. Compose the two and the shell's text-bearing NODE TYPES fall out.
// That is what this derives:
//
//   1. node type → Android widget class, PARSED from the shell's own `when` arms
//      (Kotlin `const val` node-type spellings resolved from the same file);
//   2. widget class → is-TextView-and-not-ViewGroup, from the declared SDK table
//      below — the one fact this pin cannot read out of the checkout;
//   3. the harness's own Android set, derived BEHAVIOURALLY by mounting a probe per
//      node type and observing whether the text child was absorbed. Nothing is read
//      out of BnTestTree by reflection: the pin asks the harness the same question a
//      consumer's assertion asks it.
//
// So a Material widget swap, a new node type without a `when` arm, a widget class
// changing, or the harness's set drifting all turn this red.
//
// ── WHAT IT CANNOT COVER, STATED RATHER THAN OVERCLAIMED ─────────────────────
//
//   · THE SDK HIERARCHY IS DECLARED, NOT MEASURED. `CheckBox : CompoundButton :
//     Button : TextView` is an android.jar fact and no android.jar is on this lane's
//     path. It is written out per entry with its chain, and an UNKNOWN class fails
//     the test rather than defaulting — so the table cannot go stale silently, only
//     loudly. It is a far weaker claim than the one it replaces: Android's own class
//     hierarchy is frozen public API, while our node→widget mapping is edited.
//   · IT IS SOURCE, NOT RUNTIME. That a real MaterialCheckBox on a real device
//     absorbs the child is the instrumented lane's job, not a text scan's.
//   · THE iOS HALF IS PARTIAL — see the Swift test's own note.
//
// A pin that honestly covers part of the invariant beats a comment covering none.
//
// ⚠ WHY THIS LIVES IN THE .NET SUITE. Same three reasons AndroidLogDriftTests gives:
// the BnRepo.Root scan mechanism already lives here, it covers the template mirror in
// the same pass, and it needs no Android toolchain (the JVM lane cannot start
// without two NativeAOT bionic publishes). The subject is a .NET type's fidelity to
// a Kotlin file, so the .NET side is also where the failure belongs.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TextCollapseParityDriftTests
{
    private const string ShellWidgetMapper =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/WidgetMapper.kt";

    /// <summary>The template's mirror of the same shell — what a `dotnet new
    /// blazornative` app actually compiles. TemplateDriftTests holds the two
    /// byte-equal; parsing both is a second lock on the same door for one line.</summary>
    private const string TemplateWidgetMapper =
        "templates/BlazorNative.Templates/content/BlazorNative.App/android/src/androidMain/kotlin/"
        + "io/blazornative/shell/WidgetMapper.kt";

    private const string SwiftWidgetMapper = "src/BlazorNative.Apple/BnHost/BnWidgetMapper.swift";

    /// <summary>The text child every probe renders. Any non-empty string; named so a
    /// failure message shows why the assertion is about this particular content.</summary>
    private const string Marker = "collapse-me";

    /// <summary>ANDROID SDK INHERITANCE — the one fact this pin cannot derive from the
    /// checkout, so it is written out with the chain that justifies each verdict. The
    /// question each answers is the shell's own: `is TextView && !is ViewGroup`.</summary>
    /// <remarks>A widget class the shell names and this table does not know FAILS the
    /// test. That is the point: swapping `CheckBox` for a Material or composite widget
    /// must force a human to decide what it inherits from, not silently keep the old
    /// answer.</remarks>
    private static readonly Dictionary<string, bool> IsTextViewNotViewGroup =
        new(StringComparer.Ordinal)
        {
            // TextView and its subclasses — none of them are ViewGroups.
            ["TextView"] = true,                    // the class the predicate names
            ["Button"] = true,                      // Button : TextView
            ["EditText"] = true,                    // EditText : TextView
            ["CheckBox"] = true,                    // CheckBox : CompoundButton : Button : TextView
            ["Switch"] = true,                      // Switch : CompoundButton : Button : TextView

            // Not TextViews at all.
            ["View"] = false,                       // the base class
            ["ImageView"] = false,                  // ImageView : View
            ["ProgressBar"] = false,                // ProgressBar : View
            ["SeekBar"] = false,                    // SeekBar : AbsSeekBar : ProgressBar : View

            // ViewGroups — containers, excluded by the predicate's second clause even
            // if the first ever held.
            ["ScrollView"] = false,                 // ScrollView : FrameLayout : ViewGroup
            ["BnYogaFrameLayout"] = false,          // repo type : YogaLayout : ViewGroup
            ["BnSpinner"] = false,                  // repo type : Spinner : AbsSpinner :
                                                    //   AdapterView : ViewGroup
        };

    /// <summary>UIKIT, the same shape as the table above and the same caveat. Used only
    /// by the iOS test, whose header states what it does and does not prove.</summary>
    private static readonly Dictionary<string, string> SwiftTextBearingClassToNodeType =
        new(StringComparer.Ordinal)
        {
            ["UILabel"] = "text",
            ["UIButton"] = "button",
            ["UITextField"] = "input",
        };

    // ── The probe ────────────────────────────────────────────────────────────

    /// <summary>Renders one element with a lone text child — the wire shape the two
    /// shells project differently. Parameterised on the element name so ONE component
    /// covers every node type: the names are derived by inverting the renderer's own
    /// element→node-type switch, never listed here.</summary>
    private sealed class TextChildProbe : ComponentBase
    {
        [Parameter] public string Element { get; set; } = "div";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, Element);
            b.AddContent(1, Marker);
            b.CloseElement();
        }
    }

    // ── The Android pin ──────────────────────────────────────────────────────

    [Fact]
    public void TheHarnessAndroidSet_IsExactlyWhatTheKotlinShellsWidgetClassesImply()
    {
        IReadOnlyDictionary<string, string> widgetClasses = NodeTypeToWidgetClass(ShellWidgetMapper);

        // VACUITY GUARD 1 — the parse saw the factory.
        Assert.True(widgetClasses.Count >= 12,
            $"parsed only {widgetClasses.Count} arms out of WidgetMapper.handleCreate's "
            + "`when (p.nodeType)`. The parse stopped seeing its subject — fix the scan rather "
            + "than letting this pin compare empty sets.");

        // COVERAGE — every node type the wire knows about must have an arm. One that
        // does not falls through the shell's `else ->`, which builds a TextView, and is
        // therefore SILENTLY text-bearing on Android. That is exactly the class of
        // surprise this pin exists to stop, so it is an error here, not a gap.
        var uncovered = BnWireVocabulary.NodeTypeNames
            .Where(n => n != "?" && !widgetClasses.ContainsKey(n))
            .ToList();
        Assert.True(uncovered.Count == 0,
            $"the Android shell has no `when` arm for node type(s): {string.Join(", ", uncovered)}. "
            + "They fall through to `else ->`, which builds a TextView — so on Android they are "
            + "text-bearing and absorb a text child, and BnTestTree does not model that. Give "
            + "them an arm in WidgetMapper, or add them to the harness's Android set.");

        // Classify. An unknown widget class is a HARD failure: it means someone changed
        // what a node type becomes, and the whole derivation below depends on knowing
        // what that class inherits from.
        var unclassified = widgetClasses.Values
            .Where(c => !IsTextViewNotViewGroup.ContainsKey(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(unclassified.Count == 0,
            $"the Android shell now builds widget class(es) this pin cannot classify: "
            + $"{string.Join(", ", unclassified)}. Add each to IsTextViewNotViewGroup with its "
            + "inheritance chain, and check whether BnTestTree's Android set must change: the "
            + "shell collapses a text child into any parent that `is TextView && !is ViewGroup`.");

        var fromTheShell = new SortedSet<string>(
            widgetClasses.Where(kv => IsTextViewNotViewGroup[kv.Value]).Select(kv => kv.Key),
            StringComparer.Ordinal);

        SortedSet<string> fromTheHarness = HarnessTextBearing(BnShell.Android);

        // VACUITY GUARD 2 — neither side may be empty. Two empty sets are equal forever.
        Assert.True(fromTheShell.Count > 0,
            "derived an EMPTY text-bearing set from the Kotlin. Nothing the shell builds "
            + "classified as TextView-and-not-ViewGroup, which cannot be right — TextView "
            + "itself is in the factory.");
        Assert.True(fromTheHarness.Count > 0,
            "the harness absorbed a text child for NO node type under BnShell.Android. Either "
            + "the collapse broke or the probe stopped producing a text child.");

        Assert.True(fromTheShell.SetEquals(fromTheHarness),
            "BnTestTree's Android text-bearing set has drifted from the Android shell.\n"
            + $"  the Kotlin implies : {string.Join(", ", fromTheShell)}\n"
            + $"  the harness answers: {string.Join(", ", fromTheHarness)}\n"
            + "The shell collapses a text child into any parent that `is TextView && !is "
            + "ViewGroup` (WidgetMapper.handleCreate), and the widget class for each node type "
            + "is the `when` directly below it. What a mismatch costs: a consumer asserting "
            + "node.Text on one of these gets an answer that is true of the harness and wrong "
            + "on the device — which is the defect Phase 13.4 exists to remove, not to relocate. "
            + "Fix BnTestTree.TextBearingFor(BnShell.Android).");
    }

    /// <summary>THE ANCHOR. Everything above derives the shell's set from the widget
    /// FACTORY, which is only valid while the predicate above the factory is still the
    /// structural `is TextView && !is ViewGroup` this pin models. Rewrite it as a node-type
    /// allowlist and the derivation silently starts answering the wrong question.</summary>
    [Fact]
    public void TheKotlinCollapsePredicate_IsStillTheOneThisPinModels()
    {
        string kotlin = ReadCheckoutFile(ShellWidgetMapper);

        Assert.Matches(
            new Regex(@"rawParent is TextView\s*&&\s*rawParent !is (android\.view\.)?ViewGroup"),
            kotlin);

        // …and it still guards the text arm specifically, not something else.
        Assert.Matches(new Regex(@"if \(p\.nodeType == ""text""\)"), kotlin);
    }

    /// <summary>The generated app compiles the template's copy, so the pin must hold
    /// there too. TemplateDriftTests already pins byte-equality; this asserts the thing
    /// this test actually depends on, which is cheaper to keep true.</summary>
    [Fact]
    public void TheTemplateMirror_ImpliesTheSameWidgetClasses()
    {
        IReadOnlyDictionary<string, string> shell = NodeTypeToWidgetClass(ShellWidgetMapper);
        IReadOnlyDictionary<string, string> template = NodeTypeToWidgetClass(TemplateWidgetMapper);

        Assert.True(template.Count > 0, "parsed ZERO `when` arms out of the template's WidgetMapper.");
        Assert.Equal(shell.OrderBy(kv => kv.Key, StringComparer.Ordinal),
                     template.OrderBy(kv => kv.Key, StringComparer.Ordinal));
    }

    // ── #298: the component docs' own widget claims, held to the same derivation ─
    //
    // FIX ROUND 1 REWRITE. The first version matched a claimed class name anywhere in the
    // file with a word-boundary regex — which is exactly what let #298's OWN pre-fix text
    // through: "Android: SwitchMaterial/Switch." contains a SECOND, standalone "Switch"
    // after the slash, and `\bSwitch\b` finds that occurrence and reports a match even
    // though the claim right after "Android:" is "SwitchMaterial". Confirmed by restoring
    // that exact text and re-running the old fact: it passed. This version instead
    // EXTRACTS the token immediately after "Android:" (or immediately before "on Android"
    // in a published `///` doc) and compares that extracted token, never a file-wide
    // search — see the extractor mutation below for the corrected pin actually going red.

    /// <summary>File name → node type, for every component the two scans below might
    /// discover. A file the scan finds with no row here REDS rather than being silently
    /// skipped — a new "Android: X" claim, or a new published "&lt;c&gt;X&lt;/c&gt; on
    /// Android", must be wired to a node type deliberately. The published-doc scan turned
    /// up six components the original #298 sweep never looked at (`BnButton`, `BnImage`,
    /// `BnInput`, `BnScroll`, `BnText`, `BnView`) — two of which, `BnImage` and `BnView`,
    /// had the identical class of false claim (`UIImageView` for the `BnImageView`
    /// subclass; `FrameLayout` for the `BnYogaFrameLayout` subclass), corrected in the
    /// same fix round this table was widened in.</summary>
    private static readonly Dictionary<string, string> ComponentNodeTypes =
        new(StringComparer.Ordinal)
        {
            ["BnActivityIndicator.cs"] = "activityindicator",
            ["BnButton.cs"] = "button",
            ["BnCheckbox.razor"] = "checkbox",
            ["BnCheckbox.razor.cs"] = "checkbox",
            ["BnImage.cs"] = "image",
            ["BnInput.cs"] = "input",
            ["BnPicker.razor"] = "picker",
            ["BnPicker.razor.cs"] = "picker",
            ["BnScroll.cs"] = "scroll",
            ["BnSlider.razor"] = "slider",
            ["BnSlider.razor.cs"] = "slider",
            ["BnSwitch.razor"] = "switch",
            ["BnSwitch.razor.cs"] = "switch",
            ["BnText.cs"] = "text",
            ["BnView.cs"] = "view",
        };

    private const string ComponentsDir = "src/BlazorNative.Components";

    /// <summary>The internal-comment claim shape: `Android: ClassName` — captures only the
    /// FIRST token, so "SwitchMaterial/Switch" yields "SwitchMaterial", not "Switch".</summary>
    private static readonly Regex InternalAndroidClaim =
        new(@"Android:\s*(?<cls>[A-Za-z_][A-Za-z0-9_]*)");

    /// <summary>The published `///` doc claim shape: `&lt;c&gt;ClassName&lt;/c&gt; on
    /// Android` — the class immediately adjacent to "on Android", not any `&lt;c&gt;` span
    /// in the remarks. Matched against the NORMALIZED text (see
    /// <see cref="StripDocCommentContinuations"/>) because a wrapped `///` summary breaks
    /// the phrase across lines with a `///` marker in between — `BnActivityIndicator.cs`
    /// does exactly this ("...&lt;c&gt;ProgressBar&lt;/c&gt; on\n/// Android...") and a
    /// plain `\s+` does not bridge a `///` marker, which is not whitespace.</summary>
    private static readonly Regex PublishedAndroidClaim =
        new(@"<c>(?<cls>[A-Za-z_][A-Za-z0-9_]*)</c>\s+on Android");

    /// <summary>Joins a wrapped `///` XML doc comment back into one line, so a phrase split
    /// across the wrap (`&lt;c&gt;X&lt;/c&gt; on\n/// Android`) reads as one run of text
    /// instead of being invisible to a `\s+`-only regex.</summary>
    private static string StripDocCommentContinuations(string text) =>
        Regex.Replace(text, @"\r?\n\s*///\s?", " ");

    /// <summary>Scans every `.razor`/`.cs` file directly under <see cref="ComponentsDir"/>
    /// for the first match of <paramref name="pattern"/>, returning (file name, captured
    /// class) pairs — a MEASUREMENT of what the sources currently claim, never a
    /// hand-maintained list of which files to look at.</summary>
    private static List<(string File, string ClaimedClass)> ScanAndroidClaims(Regex pattern, bool normalizeDocComments)
    {
        string dir = Path.Combine(BnRepo.Root(), ComponentsDir.Replace('/', Path.DirectorySeparatorChar));
        var found = new List<(string, string)>();
        foreach (string path in Directory.EnumerateFiles(dir)
            .Where(p => p.EndsWith(".razor", StringComparison.Ordinal) || p.EndsWith(".cs", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path);
            if (normalizeDocComments) text = StripDocCommentContinuations(text);
            Match m = pattern.Match(text);
            if (m.Success) found.Add((Path.GetFileName(path), m.Groups["cls"].Value));
        }
        return found;
    }

    /// <summary>#298's own fix, held so it cannot regress: a component doc that names an
    /// Android widget class must name the class WidgetMapper.kt's `when (p.nodeType)`
    /// actually builds for that node type — the SAME derivation
    /// <see cref="TheHarnessAndroidSet_IsExactlyWhatTheKotlinShellsWidgetClassesImply"/>
    /// already reads out of the shell, reused rather than re-parsed. Covers BOTH claim
    /// shapes: the internal design comments (`Android: X`, never published) AND the
    /// published `///` summaries that ship in the generated component reference
    /// (`&lt;c&gt;X&lt;/c&gt; on Android`) — #298's own regression was in the former, but
    /// `BnPicker.razor.cs` carried the identical mistake in the latter, so both are worth
    /// holding.
    ///
    /// RULE 5 — WHAT THIS DOES NOT CHECK. iOS claims (`iOS: X` / `&lt;c&gt;X&lt;/c&gt; on
    /// iOS`): `BnWidgetMapper.swift`'s factory is a `switch` whose arms are prose-heavy
    /// Swift, not a parseable one-line-per-arm table the way `WidgetMapper.kt`'s `when` is,
    /// so the #298 sweep checked the iOS side BY HAND (recorded in the audit record) rather
    /// than parsed here. And components that make NO widget claim at all — nothing forces
    /// one to be written in the first place.</summary>
    [Fact]
    public void ComponentDocs_NameTheAndroidWidgetClassTheShellActuallyBuilds()
    {
        IReadOnlyDictionary<string, string> widgetClasses = NodeTypeToWidgetClass(ShellWidgetMapper);

        // POSITIVE CONTROL — run the SAME extractor on the exact planted pre-#298 string,
        // and assert it yields the WRONG class, distinct from what the shell derives for
        // `switch`. This is what proves the detector can actually tell a bad claim from a
        // good one, rather than merely existing.
        Match control = InternalAndroidClaim.Match("Android: SwitchMaterial/Switch.");
        Assert.True(control.Success, "the extractor itself failed to match the planted string.");
        Assert.Equal("SwitchMaterial", control.Groups["cls"].Value);
        Assert.NotEqual(control.Groups["cls"].Value, widgetClasses["switch"]);

        // Internal design comments — five components (BnActivityIndicator, BnCheckbox,
        // BnPicker, BnSlider, BnSwitch).
        CheckClaims(InternalAndroidClaim, isPublished: false, normalizeDocComments: false, floor: 5);
        // Published `///` summaries — eleven components. The published-doc scan reaches
        // six the internal-comment scan never covered (BnButton, BnImage, BnInput,
        // BnScroll, BnText, BnView), because a hand-written component with no `.razor`
        // header still carries a type-level `///` summary naming its widget.
        CheckClaims(PublishedAndroidClaim, isPublished: true, normalizeDocComments: true, floor: 11);

        void CheckClaims(Regex pattern, bool isPublished, bool normalizeDocComments, int floor)
        {
            List<(string File, string ClaimedClass)> discovered = ScanAndroidClaims(pattern, normalizeDocComments);

            // VACUITY GUARD — the scan did not stop seeing its subject, and did not shrink
            // below what is measured on the checkout today.
            Assert.True(discovered.Count >= floor,
                $"discovered only {discovered.Count} {(isPublished ? "published `///`" : "internal comment")} "
                + $"Android claims under {ComponentsDir} (floor {floor}) — the scan stopped seeing its "
                + "subject, or claims were removed without this floor being lowered deliberately.");

            foreach ((string file, string claimed) in discovered)
            {
                Assert.True(ComponentNodeTypes.TryGetValue(file, out string? nodeType),
                    $"{file} names an Android widget (\"{claimed}\") with no row in "
                    + $"{nameof(ComponentNodeTypes)} — a new claim must be wired to a node type "
                    + "deliberately, not silently skipped.");

                Assert.True(widgetClasses.TryGetValue(nodeType!, out string? actual),
                    $"{file}: node type '{nodeType}' has no `when` arm in {ShellWidgetMapper} — "
                    + "the claim cannot be checked.");

                string kind = isPublished
                    ? "published `///` summary — ships in the generated component reference"
                    : "internal design comment — never reaches the assembly or the reference";
                Assert.True(actual == claimed,
                    $"{file}'s {kind} claims Android builds `{claimed}` for a `{nodeType}` node, "
                    + $"but WidgetMapper.kt's `when (p.nodeType)` now builds `{actual}`. Fix the "
                    + "doc, or update this fact if the new class is correct — never both silently.");
            }
        }
    }

    // ── The iOS half, and what it is worth ───────────────────────────────────

    /// <summary>PARTIAL BY CONSTRUCTION, and said so rather than dressed up. The Swift
    /// predicate IS a class list — `view is UILabel || view is UIButton || view is
    /// UITextField` — so this pin holds the harness's iOS set to that list's SIZE and
    /// membership through the declared UIKit correspondence. What it does NOT do is
    /// parse `makeView` to prove that `text` really builds a UILabel: the Swift factory's
    /// arms span several lines each with the class buried in the body, and a fragile
    /// parse asserting the wrong thing would be worse than an honest partial one. So the
    /// UILabel↔text correspondence is declared, and what is MEASURED is that the Swift
    /// predicate has not grown, shrunk, or changed classes — which is the way this set
    /// would actually drift.</summary>
    [Fact]
    public void TheHarnessIosSet_MatchesTheClassesTheSwiftPredicateNames()
    {
        string swift = ReadCheckoutFile(SwiftWidgetMapper);

        Match predicate = Regex.Match(
            swift,
            @"func isTextBearingNonContainer\([^)]*\)\s*->\s*Bool\s*\{(?<body>.*?)\n\s*\}",
            RegexOptions.Singleline);

        Assert.True(predicate.Success,
            "could not find isTextBearingNonContainer in BnWidgetMapper.swift. It moved or was "
            + "renamed — re-point this pin deliberately; it is the only thing holding "
            + "BnTestTree's iOS set to the Apple shell.");

        var named = new SortedSet<string>(
            Regex.Matches(predicate.Groups["body"].Value, @"view is (?<cls>[A-Za-z_][A-Za-z0-9_]*)")
                 .Select(m => m.Groups["cls"].Value),
            StringComparer.Ordinal);

        Assert.True(named.Count > 0,
            "parsed ZERO classes out of the Swift collapse predicate — the pin cannot see its "
            + "subject and must not pass vacuously.");

        Assert.True(named.SetEquals(SwiftTextBearingClassToNodeType.Keys),
            $"the Apple shell's collapse predicate now names {{{string.Join(", ", named)}}}, not "
            + $"{{{string.Join(", ", SwiftTextBearingClassToNodeType.Keys)}}}. Update "
            + "SwiftTextBearingClassToNodeType AND BnTestTree.TextBearingFor(BnShell.Ios) "
            + "together — a class added there is a node type the harness must start collapsing.");

        var expected = new SortedSet<string>(
            SwiftTextBearingClassToNodeType.Values, StringComparer.Ordinal);
        SortedSet<string> fromTheHarness = HarnessTextBearing(BnShell.Ios);

        Assert.True(fromTheHarness.Count > 0,
            "the harness absorbed a text child for NO node type under BnShell.Ios.");
        Assert.True(expected.SetEquals(fromTheHarness),
            "BnTestTree's iOS text-bearing set has drifted from the Apple shell.\n"
            + $"  the Swift implies  : {string.Join(", ", expected)}\n"
            + $"  the harness answers: {string.Join(", ", fromTheHarness)}");
    }

    // ── Deriving the harness's own answer, behaviourally ─────────────────────

    /// <summary>Which node types the harness collapses a lone text child into, on the
    /// given shell — measured by MOUNTING, not by reading BnTestTree's private set.</summary>
    /// <remarks>This is the question a consumer's assertion asks, so asking it the same
    /// way keeps the pin honest: a refactor that keeps the field and breaks the
    /// behaviour still reds. The element for each node type is derived by inverting the
    /// renderer's own switch, so this method names no element and no node type.</remarks>
    private static SortedSet<string> HarnessTextBearing(BnShell shell)
    {
        var bearing = new SortedSet<string>(StringComparer.Ordinal);

        foreach ((string nodeType, string element) in RendererNodeTypeMap.RepresentativeElements())
        {
            if (!RendererNodeTypeMap.Accepts(nodeType)) continue;   // not a wire node type

            using BnTestHost host = BnTestHost.Mount<TextChildProbe>(
                new Dictionary<string, object?> { ["Element"] = element },
                shell: shell);

            BnTestNode root = host.Tree.Root;
            Assert.Equal(nodeType, root.NodeType);   // the inversion really produced it

            if (root.Children.Count == 0 && root.Text == Marker) bearing.Add(nodeType);
        }

        return bearing;
    }

    // ── Reading the shells ───────────────────────────────────────────────────

    /// <summary>node type → Android widget class, parsed from `handleCreate`'s
    /// `when (p.nodeType)` arms. Kotlin `const val` spellings (SCROLL, PICKER, MODAL)
    /// are resolved from the same file, so the shell can keep naming them by constant.
    /// The `else ->` arm is deliberately NOT collected: it is the fallback for node
    /// types that have no arm, and the coverage assertion above is what handles those.</summary>
    private static IReadOnlyDictionary<string, string> NodeTypeToWidgetClass(string relativePath)
    {
        string kotlin = ReadCheckoutFile(relativePath);

        var consts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(kotlin, @"const val (?<name>[A-Z][A-Z0-9_]*)\s*=\s*""(?<value>[^""]+)"""))
            consts[m.Groups["name"].Value] = m.Groups["value"].Value;

        Assert.True(consts.Count > 0,
            $"resolved ZERO `const val` node-type spellings from {relativePath} — the arms that "
            + "name their node type by constant would be silently dropped.");

        string[] lines = kotlin.Split('\n');
        int start = Array.FindIndex(lines, l => l.Contains("val view: View = when (p.nodeType) {", StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"could not find the `val view: View = when (p.nodeType) {{` factory in {relativePath}. "
            + "It moved or was rewritten — re-point this scan deliberately.");

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var arm = new Regex(
            @"^\s*(?:""(?<lit>[a-z]+)""|(?<const>[A-Z][A-Z0-9_]*))\s*->\s*(?<cls>[A-Za-z_][A-Za-z0-9_.]*)\s*\(");

        for (int i = start + 1; i < lines.Length; i++)
        {
            string raw = lines[i].TrimEnd('\r');
            if (raw == "        }") break;              // the `when`'s own closing brace

            string line = Regex.Replace(raw, @"//.*$", string.Empty);
            Match m = arm.Match(line);
            if (!m.Success) continue;

            string nodeType = m.Groups["lit"].Success
                ? m.Groups["lit"].Value
                : consts.TryGetValue(m.Groups["const"].Value, out string? resolved)
                    ? resolved
                    : throw new InvalidOperationException(
                        $"{relativePath}: the `when` arm names constant "
                        + $"'{m.Groups["const"].Value}', which no `const val` in the file defines. "
                        + "The constant moved to another file — resolve it there, do not guess.");

            // Only the class NAME matters; `android.widget.CheckBox` and `CheckBox` are
            // the same verdict, and the shell writes both spellings in places.
            string cls = m.Groups["cls"].Value;
            map[nodeType] = cls[(cls.LastIndexOf('.') + 1)..];
        }

        return map;
    }

    private static string ReadCheckoutFile(string relativePath)
    {
        string full = Path.Combine(BnRepo.Root(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"checkout file not found: {full}");
        return File.ReadAllText(full);
    }
}
