using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// TemplateDriftTests — Phase 8.3 (design decisions 3/5/6/7, M8 DoD #4:
// `dotnet new blazornative`).
//
// THE PROBLEM THIS FILE EXISTS FOR: templates/BlazorNative.Templates ships
// COPIES — of the version literal, of the shell's Kotlin, of the gradle pins, of
// the build machinery, of the registration pattern. Every copy is a thing that
// can drift, and a template's drift is INVISIBLE: it rots inside a nupkg, on a
// schedule nobody watches, and surfaces on a stranger's laptop as an app that
// does not work for a reason nothing in their project names.
//
// THE HOUSE ANSWER, applied: a copy that cannot be a derived view is a PINNED
// MIRROR — held equal by a test that reds in the commit that causes the drift.
// This is the RouteTableDriftTests / ShellStyleTableDriftTests / PackagePurityTests
// pattern, verbatim, with the template as the subject; and like all of them it
// lives in `build-test`, the ONE required lane where every file is
// checkout-visible (the template is not a build input of any project, so the
// checkout is the only handle).
//
// WHAT IS PINNED HERE, and why each one is not paranoia:
//
//   1. EVERY BlazorNative version literal in the template tree == the props.
//      Including the PACK'S OWN <Version> — that one specifically, because
//      PackageVersionPinTests.ShippedCsprojs() enumerates src/ ONLY and the
//      pack lives in templates/. That is the 8.1 "Seven" trap exactly (a
//      packable project whose version is guarded by nothing), named and closed.
//      A WILDCARD was rejected for this: the CI check restores from a local
//      feed holding exactly one version, so a wildcard always resolves and the
//      drift is invisible — a pass that is green because it cannot see.
//   2. The referenced ids are a SUBSET of the shipped six, ENUMERATED from src/
//      (never rostered — the 8.1 I-2 rule), so a typo'd or foreign id reds.
//   3. THE FILE SET ITSELF — WHICH files the template ships, before any pin
//      says what is IN them. Gate 1's review found the original file blind
//      here and PROVED it: 7 template files deleted (gradlew, the wrapper jar,
//      settings.gradle.kts, AndroidManifest.xml, BnStarterPage.razor,
//      _Imports.razor, gradle.properties) and every pin stayed GREEN, because
//      the byte-identity pin iterated a HARDCODED 15-path roster and asserted
//      only `compared > 0`. It did not know how many files it SHOULD pin. That
//      is this file's own cited rule broken in this file — "never a roster"
//      (8.1 I-2), the rule ShippedPackageIds() below exists to honour — and it
//      is this phase's discovered class made live: the .gitignore find proved a
//      template file can silently never exist, and the pin was blind to 7 of
//      the 32 that DO. So the set is now pinned three ways, and the split is
//      deliberate — a set pin says WHICH, a byte pin says WHAT:
//        · the template's content tree, enumerated FROM DISK and held equal to
//          an expected manifest — reds on a REMOVED file and on an ADDED one
//          (the pack ships `content/**` minus bin/obj, so the disk IS the
//          shipped set: this pin is the nupkg's inventory);
//        · the byte-identity set is now DERIVED from the template's own
//          android/src tree rather than rostered — the roster cannot be shrunk
//          because there is no roster;
//        · and the REPO's shell tree is enumerated too (pin 9 below), because
//          neither of the above can see a file src/ grew that the template
//          never got.
//   4. The shell's Kotlin is BYTE-IDENTICAL to src/BlazorNative.Jni's. This is a
//      real byte comparison, not a normalized one, and the trick that buys that
//      exactness is decision 6's: the template's shell keeps the
//      io.blazornative.shell PACKAGE (AGP's `namespace`/`applicationId` are
//      separate identities, so they can be the user's while the sources are
//      not). A shell change that skips the template reds HERE. Without this the
//      template would be a second copy of the shell THAT NOTHING COMPILES — the
//      AGP 9 incident's exact shape.
//   5. The gradle pins == the repo's. Yoga most of all: one engine, and a
//      template that drifts from it lays out differently from both shells,
//      silently. (ci.yml's parity step owns Yoga's FOURTH copy; this pin owns
//      the rest.)
//   6. MainActivity == the repo's, MODULO the TWO divergences it is ALLOWED:
//      the ultimate fallback literal and the template-only `import <namespace>.R`
//      (AGP puts R in the `namespace` package; Kotlin resolves a bare `R` against
//      the FILE's package — the repo's two match by coincidence, the template's
//      differ by design, so only the template needs the import. Gate 2's
//      assembleDebug found that; the design had assumed byte-identity was free
//      here). ⚠ Brittle by construction (an excision), and the design says so.
//      Phase 11.0 RETIRED the third divergence (the DEEP_LINK_COMPONENTS map
//      block): both copies now read the build-time-generated resource through the
//      identical loadDeepLinkRoutes(), so the loader is byte-identical pinned code
//      and the excision that used to remove the map is gone. The fallback excision
//      targets exactly what RouteTableDriftTests parses, so the parser is not new.
//   7. PIN B (the template's OWN hand-written map vs its OWN AppPages.All) —
//      RETIRED at 11.0: the template has no hand-written map any more, it reads
//      the same generated resource the repo does, so the pairs cannot drift from
//      the pages by construction (the footgun this milestone closes, closed for
//      the consumer too — Gate B proves the template's generated resource in the
//      template-smoke lane). What survives is the half the resource does not
//      subsume: the ultimate `?: "…"` fallback literal must name the template's
//      default page, so a resource-less generated app still boots into something
//      that registers.
//   8. The guard ORDER, in BOTH copies (the sample's and the template's).
//      Stated honestly: a source-order pin proves the ORDER, not the SEMANTICS.
//      Here the semantics IS the order, and the alternative is nothing —
//      EnsureRegistered is a static once-guard over a static array, with no seam
//      to inject a throwing manifest. Direct precedent:
//      AndroidSetStyleDispatch_HasAnArmForEveryYogaStyle pins Kotlin source
//      shape for the same reason. What it DOES do is cover both copies in one
//      test, so reference and template cannot drift apart on this line again.
//   9. global.json + BionicNativeAot.targets == the repo's. A generated app
//      inherits NEITHER from the repo, and without them there is no bionic
//      publish (hence no .so, hence no APK) and no SDK-band pin under the ILC
//      host.
//  10. THE REPO'S SIDE OF THE SET — every shell source under the three
//      subtrees the template MIRRORS is either in the template or named
//      repo-only, ENUMERATED from src/. Pins 3 and 4 both read the TEMPLATE's
//      tree, so neither can see the drift that runs the other way: src/ grows a
//      shell file, the template never gets it, and the generated app compiles
//      against a shell missing a piece the repo's tests all exercise. That is
//      the exact failure this file's own docs claim to catch ("a shell change
//      that skips the template REDS HERE") — and Gate 1's reviewer proved it
//      did not: a new file in src/BlazorNative.Jni, all 9 tests green. The
//      repo-only set is NOT a roster of subjects; it is a justified EXCLUSION
//      list, the same shape as the pack csproj's `Exclude=`, and a new shell
//      file matches neither arm and reds until someone decides which it is.
//
// EVERY PIN ASSERTS NON-VACUITY. A parse that finds nothing REDS rather than
// passing over an empty read — PackagePurityTests.TypeNamesOf's rule, verbatim:
// "a pin that cannot see its subject must never pass vacuously." That rule is
// 8.1's headline and this file is where it would be easiest to forget.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TemplateDriftTests
{
    private const string TemplateRoot = "templates/BlazorNative.Templates";
    private const string ContentRoot = TemplateRoot + "/content/BlazorNative.App";
    private const string PackCsproj = TemplateRoot + "/BlazorNative.Templates.csproj";
    private const string AppCsproj = ContentRoot + "/MyBlazorNativeApp.csproj";
    private const string TemplateJson = ContentRoot + "/.template.config/template.json";
    private const string TemplateAppPages = ContentRoot + "/AppPages.cs";
    private const string SampleAppPagesSource = "samples/BlazorNative.SampleApp/SampleAppPages.cs";

    private const string JniRoot = "src/BlazorNative.Jni";
    private const string TemplateAndroidRoot = ContentRoot + "/android";

    private const string RepoGradle = JniRoot + "/build.gradle.kts";
    private const string TemplateGradle = ContentRoot + "/android/build.gradle.kts";
    private const string RepoMainActivity =
        JniRoot + "/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt";
    private const string TemplateMainActivity =
        ContentRoot + "/android/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt";

    // ── 1. The version mirror ────────────────────────────────────────────────

    /// <summary>THE MIRROR PIN (8.3 normative rule 2). src/Directory.Build.props
    /// remains the ONE version; every BlazorNative version literal in the
    /// template tree is a MIRROR of it — the same relationship
    /// DEEP_LINK_COMPONENTS has to the page manifest: a copy that exists because
    /// it CANNOT be a derived view, held equal by a pin that reds. A bump stays a
    /// one-line edit in the props PLUS a red test that names the second place,
    /// which is the entire point of a mirror over a silent copy.
    ///
    /// The pack is built FROM this content, so pinning the content pins the
    /// nupkg — there is no third place for the literal to hide.</summary>
    [Fact]
    public void EveryTemplateVersionLiteral_EqualsTheSharedProps()
    {
        string expected = PropsVersion();
        var literals = new List<(string Where, string Value)>();

        // (a) The generated csproj's BlazorNative.* PackageReference versions.
        foreach (XElement reference in PackageReferences(CheckoutPath(AppCsproj)))
        {
            string? id = reference.Attribute("Include")?.Value;
            if (id is null || !id.StartsWith("BlazorNative.", StringComparison.Ordinal))
                continue;
            literals.Add(($"{AppCsproj} → <PackageReference Include=\"{id}\">", reference.Attribute("Version")?.Value ?? "(no Version attribute)"));
        }

        // (b) template.json's BlazorNativeVersion default — the symbol that lets a
        //     user override the version is a SECOND literal inside the template,
        //     and it is covered here by construction (8.3 decision 3's flag).
        using (JsonDocument doc = JsonDocument.Parse(
            ReadCheckoutFile(TemplateJson),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
        {
            bool found = doc.RootElement.TryGetProperty("symbols", out JsonElement symbols)
                && symbols.TryGetProperty("BlazorNativeVersion", out JsonElement symbol)
                && symbol.TryGetProperty("defaultValue", out JsonElement value);
            Assert.True(found,
                $"could not read symbols.BlazorNativeVersion.defaultValue from {TemplateJson} — the "
                + "version symbol moved or was renamed. It is a version literal like any other; "
                + "re-point this pin deliberately rather than letting the literal go unguarded.");
            literals.Add(($"{TemplateJson} → symbols.BlazorNativeVersion.defaultValue",
                doc.RootElement.GetProperty("symbols").GetProperty("BlazorNativeVersion")
                    .GetProperty("defaultValue").GetString() ?? "(null)"));

            // The symbol REPLACES a literal in the content; if `replaces` ever
            // stopped naming the version, --BlazorNativeVersion would silently do
            // nothing and every generated app would carry the default forever.
            JsonElement sym = doc.RootElement.GetProperty("symbols").GetProperty("BlazorNativeVersion");
            Assert.True(sym.TryGetProperty("replaces", out JsonElement replaces),
                $"{TemplateJson}'s BlazorNativeVersion symbol has no `replaces` — the --BlazorNativeVersion "
                + "option would silently do nothing.");
            literals.Add(($"{TemplateJson} → symbols.BlazorNativeVersion.replaces", replaces.GetString() ?? "(null)"));
        }

        // (c) THE PACK'S OWN <Version> — the 8.1 "Seven" trap: it lives outside
        //     src/, which PackageVersionPinTests enumerates, so nothing else in
        //     this repo guards it.
        var packVersions = XDocument.Load(CheckoutPath(PackCsproj)).Root!
            .Elements("PropertyGroup").Elements("Version")
            .Select(v => v.Value)
            .ToList();
        Assert.True(packVersions.Count == 1,
            $"{PackCsproj} must carry exactly ONE <Version> (it is outside src/, so "
            + $"PackageVersionPinTests cannot see it at all), found {packVersions.Count}.");
        literals.Add(($"{PackCsproj} → <Version>", packVersions[0]));

        // NON-VACUITY, AND THE HONEST SHAPE (Gate 1 review M-2). This used to be
        // `literals.Count >= 5` while the pin collects SIX — so a dropped
        // PackageReference cleared the floor and the pin went on comparing five
        // literals as if that were the whole set. A floor below the real count
        // is a floor that is never reached.
        //
        // A NAMED SET rather than `== 6`: both red on the drop, but a count
        // names a number and a set names the FILE. If a fourth BlazorNative
        // PackageReference is ever added legitimately (Http, say — the subset
        // pin above already permits any of the shipped six), this reds, and
        // that is correct: a new version literal is a new mirror, and it gets
        // added here deliberately rather than sliding in unguarded. That is the
        // rule this whole file is built on.
        string[] expectedSources =
        [
            $"{AppCsproj} → <PackageReference Include=\"BlazorNative.Analyzers\">",
            $"{AppCsproj} → <PackageReference Include=\"BlazorNative.Components\">",
            $"{AppCsproj} → <PackageReference Include=\"BlazorNative.Runtime\">",
            $"{TemplateJson} → symbols.BlazorNativeVersion.defaultValue",
            $"{TemplateJson} → symbols.BlazorNativeVersion.replaces",
            $"{PackCsproj} → <Version>",
        ];

        var sources = literals.Select(l => l.Where).ToList();
        var unseen = expectedSources.Except(sources, StringComparer.Ordinal).ToList();
        var extra = sources.Except(expectedSources, StringComparer.Ordinal).ToList();

        Assert.True(unseen.Count == 0 && extra.Count == 0,
            $"the version mirror pin collected {literals.Count} literals, not the {expectedSources.Length} "
            + "it expects — so it is no longer reading the set it claims to.\n"
            + $"  NOT FOUND (expected, unseen — {unseen.Count}): "
            + (unseen.Count == 0 ? "(none)" : string.Join(", ", unseen)) + "\n"
            + $"  UNEXPECTED (found, undeclared — {extra.Count}): "
            + (extra.Count == 0 ? "(none)" : string.Join(", ", extra)) + "\n\n"
            + "NOT FOUND means a version literal LEFT the template — or, worse, that it is still "
            + "there and this pin stopped seeing it, which is a mirror that has quietly stopped "
            + "being held. UNEXPECTED means a new literal appeared: add it above once you have "
            + "confirmed it is a mirror and not a second source. A pin that cannot see its subject "
            + "must never pass vacuously.");

        var offenders = literals.Where(l => l.Value != expected).ToList();
        Assert.True(offenders.Count == 0,
            $"TEMPLATE VERSION DRIFT. src/Directory.Build.props is the ONE version source "
            + $"(\"{expected}\"); every literal in the template tree is a MIRROR of it and must be "
            + "bumped in the SAME commit. Offenders:\n"
            + string.Join("\n", offenders.Select(o => $"  {o.Where}\n    is \"{o.Value}\", must be \"{expected}\""))
            + $"\n(Checked {literals.Count} literals in total.)");
    }

    /// <summary>THE SUBSTITUTION-COLLISION PIN (Phase 8.7), and it exists because
    /// 8.7 ARMED THE TRAP IT GUARDS. `dotnet new` substitutes the BlazorNativeVersion
    /// symbol by EXACT STRING MATCH: every occurrence of `replaces` anywhere in the
    /// generated content becomes the user's `--BlazorNativeVersion` value. That was
    /// harmless while the version was `1.0.0-preview.1` — a string nothing else could
    /// plausibly contain. PHASE 8.7 MOVED IT TO PLAIN `0.x` SEMVER, which is short,
    /// generic, and shaped exactly like the third-party version pins this template is
    /// full of.
    ///
    /// THE LIVE COLLISION, measured at 8.7 and reachable: the android gradle file pins
    /// `com.facebook.soloader:soloader:0.12.1`. The day this repo releases `0.12.1`,
    /// `replaces` becomes `"0.12.1"` — and `dotnet new blazornative --BlazorNativeVersion
    /// 0.13.0` rewrites SOLOADER'S pin to 0.13.0 and generates an app that cannot build.
    ///
    /// ⚠ AND EVERY OTHER LANE IS BLIND TO IT, which is the whole reason this pin is
    /// worth its lines. template-smoke runs `dotnet new blazornative` WITHOUT
    /// `--BlazorNativeVersion`, so the substitution replaces the default with ITSELF —
    /// a no-op. The collision is invisible until a real user overrides the version,
    /// and then it is their build that breaks, not ours.</summary>
    [Fact]
    public void TheVersionLiteral_CollidesWithNoOtherStringInTheTemplate()
    {
        string replaces;
        using (JsonDocument doc = JsonDocument.Parse(
            ReadCheckoutFile(TemplateJson),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
        {
            replaces = doc.RootElement.GetProperty("symbols").GetProperty("BlazorNativeVersion")
                .GetProperty("replaces").GetString() ?? "";
        }

        Assert.False(string.IsNullOrWhiteSpace(replaces),
            $"{TemplateJson}'s BlazorNativeVersion.replaces is empty — this pin searches for that "
            + "string, and searching for an empty string proves nothing. A pin that cannot see its "
            + "subject must never pass vacuously.");

        // The ONLY legitimate homes for the literal, and each is a MIRROR the pin
        // above already holds equal to the props. template.json is excluded because
        // `dotnet new` CONSUMES it — it declares the substitution, it is not subject
        // to it, and it does not ship into the generated app.
        var hits = new List<(string File, int Line, string Text)>();
        foreach (string relative in TrackedFilesUnder(ContentRoot))
        {
            if (relative.Replace('\\', '/') == ".template.config/template.json")
                continue;

            string path = CheckoutPath($"{ContentRoot}/{relative}");
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (IOException) { continue; }   // a binary the wrapper ships; no substitution risk

            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(replaces, StringComparison.Ordinal))
                    continue;
                // The three annotated PackageReference lines ARE the version, by design.
                if (lines[i].Contains("x-release-please-version", StringComparison.Ordinal))
                    continue;
                hits.Add((relative, i + 1, lines[i].Trim()));
            }
        }

        Assert.True(hits.Count == 0,
            $"VERSION-SUBSTITUTION COLLISION. The template's version literal is \"{replaces}\", and that "
            + "exact string also appears on the line(s) below. `dotnet new` substitutes BY STRING MATCH, "
            + $"so `dotnet new blazornative --BlazorNativeVersion <other>` would rewrite these too:\n"
            + string.Join("\n", hits.Select(h => $"  {ContentRoot}/{h.File}:{h.Line}\n    {h.Text}"))
            + "\n\n⚠ DO NOT SILENCE THIS BY ADDING AN EXCEPTION ABOVE. The line is not a mirror and it "
            + "is not supposed to hold the version — that is precisely the bug. It means this repo's "
            + "version has collided with a THIRD-PARTY pin that happens to share its shape, and a "
            + "generated app built by anyone who overrides the version will not compile.\n"
            + "The fix is one of:\n"
            + "  · release a different version (the collision is with one specific value, and the next "
            + "one along is free) — cheapest, and the version carries no meaning worth defending;\n"
            + "  · change the colliding pin, if it is ours to change;\n"
            + "  · give the symbol a distinctive `replaces` token and teach release-please to write "
            + "THAT line — a real design change, not a test edit.\n"
            + "This pin exists because template-smoke CANNOT see this: it generates with the default "
            + "version, so the substitution is a no-op there and the collision stays invisible until a "
            + "user hits it.");
    }

    /// <summary>THE SUBSET PIN. The template references Runtime + Components +
    /// Analyzers (Core/Renderer/Http arrive transitively via Runtime — the
    /// sample proves the shape). The shipped set is ENUMERATED from src/, never
    /// rostered (the 8.1 I-2 rule: a roster is a copy, and copies drift), so a
    /// typo'd or foreign BlazorNative.* id — one that will never resolve from
    /// the feed, and whose failure a user meets as a restore error in a fresh
    /// project — reds here first.</summary>
    [Fact]
    public void TemplateCsprojReferences_AreASubsetOfTheShippedSix()
    {
        List<string> shipped = ShippedPackageIds();

        var referenced = PackageReferences(CheckoutPath(AppCsproj))
            .Select(r => r.Attribute("Include")?.Value)
            .Where(id => id is not null && id.StartsWith("BlazorNative.", StringComparison.Ordinal))
            .Select(id => id!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(referenced.Count > 0,
            $"parsed ZERO BlazorNative.* PackageReferences out of {AppCsproj} — the generated app "
            + "would reference no BlazorNative packages at all, and this pin would pass over an "
            + "empty read. A pin that cannot see its subject must never pass vacuously.");

        var foreign = referenced.Where(id => !shipped.Contains(id, StringComparer.Ordinal)).ToList();
        Assert.True(foreign.Count == 0,
            $"{AppCsproj} references BlazorNative package id(s) that src/ does not ship: "
            + $"{string.Join(", ", foreign)}. The shipped set (enumerated from src/, not rostered) "
            + $"is: {string.Join(", ", shipped)}. A generated app cannot restore an id nobody packs.");
    }

    // ── 2. The file SET — which files, before what is in them ────────────────

    /// <summary>THE COMPLETENESS PIN (Gate 1 review I-1). Every other pin in
    /// this file asserts something about the CONTENT of files it already knows
    /// the names of. This one asserts the NAMES — that the template ships
    /// exactly these 35 files and no others.
    ///
    /// IT EXISTS BECAUSE THE FILE WAS PROVEN BLIND WITHOUT IT. Gate 1's
    /// reviewer deleted SEVEN template files — gradlew, gradle-wrapper.jar,
    /// settings.gradle.kts, AndroidManifest.xml, BnStarterPage.razor,
    /// _Imports.razor, gradle.properties — and every pin here passed, on every
    /// one. Three of those seven (gradlew, the wrapper jar,
    /// settings.gradle.kts) are pinned by NOTHING else even now: they are
    /// byte-identical copies of the repo's that no comparison reads, because
    /// the pins that could read them iterate names rather than the disk. A
    /// template missing its wrapper jar is a `dotnet new` that produces a tree
    /// whose FIRST command fails, and it would have shipped green.
    ///
    /// GIT IS THE SUBJECT, deliberately (the 8.1 I-2 rule, which the original
    /// roster broke while citing it four lines above itself) — and it is git
    /// rather than the disk since Phase 8.6 Gate 2. THE DISK WAS THE WRONG
    /// SUBJECT: this pin used to enumerate it and call that "the nupkg's
    /// inventory, read the same way the pack reads it". Both halves were
    /// wrong. The disk is the inventory PLUS whatever the last tool to run in
    /// this tree left behind, and the reading was a COPY — `bin`/`obj`
    /// hardcoded in C# beside a docstring claiming to read the csproj.
    ///
    /// It cost exactly what a wrong subject costs: eight gitignored `.gradle`
    /// cache files made this pin and the byte-identity pin RED on every
    /// developer machine that had ever run gradle in the template tree, for a
    /// drift that had not happened — while CI, a clean checkout, stayed green
    /// and saw nothing. A pin that reds only where nobody is watching, for a
    /// reason that is not true, is worse than no pin: it teaches its own
    /// reader to make it green, and its message told them how — "add it to the
    /// manifest… the edit IS the review". Following that literally pins
    /// gradle's lock files as required template content.
    ///
    /// So the pin now holds THREE things apart, because they have three
    /// different fixes: what git TRACKS (the subject — the manifest's
    /// business), what the pack's own globs would SHIP (read off the csproj's
    /// Include/Exclude, never copied), and the gap between them (untracked
    /// artifacts — a PACK bug, never a manifest edit).
    ///
    /// THE MANIFEST IS AN EXPECTED VALUE, NOT A ROSTER OF SUBJECTS. That
    /// distinction is the whole of I-2: enumerate what you MEASURE, declare
    /// what you EXPECT. The old code inverted it — it declared the subjects and
    /// measured nothing, so shrinking the declaration shrank the test. Here the
    /// subjects come off the disk and the declaration is the thing they are
    /// held against, which is why this reds in BOTH directions: a file removed
    /// (the seven) and a file added (a copy that silently joined the pack
    /// without a pin — the AGP 9 shape again, an artifact nothing compares).
    ///
    /// A CHANGE TO THE TEMPLATE'S SHAPE IS SUPPOSED TO RED THIS. It is a
    /// one-line edit to make it green again, and making it deliberately is the
    /// point: the reader is asked whether the new file needs a pin of its own.</summary>
    [Fact]
    public void TemplateContentTree_IsExactlyTheExpectedManifest()
    {
        // THE EXPECTED MANIFEST — 35 files, the pack's whole inventory.
        string[] expected =
        [
            // The .NET app the user gets
            ".template.config/template.json",
            "AppPages.cs",
            "BnStarterPage.razor",
            "MyBlazorNativeApp.csproj",
            "README.md",
            "_Imports.razor",
            "global.json",
            // The build machinery no `dotnet new` app inherits (decision 7)
            "build/BionicNativeAot.targets",
            // The gradle project — wrapper included. `gradlew` and the wrapper
            // JAR are the app's FIRST command; without them `dotnet new`
            // produces a tree that cannot build at all.
            "android/build.gradle.kts",
            "android/gradle.properties",
            "android/gradle/wrapper/gradle-wrapper.jar",
            "android/gradle/wrapper/gradle-wrapper.properties",
            "android/gradlew",
            "android/gradlew.bat",
            "android/settings.gradle.kts",
            // The shell — androidMain
            "android/src/androidMain/AndroidManifest.xml",
            // Font parity Gate A (#126): the OFL license text for the bundled Inter
            // font (res/font/inter_regular.ttf below). It travels WITH the font so a
            // generated app ships the font under its license (OFL §requires the
            // license accompany the Font Software). Not under a mirrored subtree, so
            // no repo-shell-in-template obligation — mirrored here deliberately.
            "android/src/androidMain/OFL.txt",
            "android/src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt",
            "android/src/androidMain/kotlin/io/blazornative/shell/BnSpinner.kt",
            "android/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt",
            "android/src/androidMain/kotlin/io/blazornative/shell/WidgetMapper.kt",
            "android/src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt",
            "android/src/androidMain/res/layout/main.xml",
            // Font parity Gate A (#126): the bundled Inter font (OFL, static
            // Regular), byte-identical to src/BlazorNative.Jni's copy — a generated
            // app renders the same font both shells will (Gate B), so text metrics
            // match. res/font naming forces lowercase, no hyphens: inter_regular.
            "android/src/androidMain/res/font/inter_regular.ttf",
            "android/src/androidMain/res/xml/network_security_config.xml",
            // Phase 9.3 (M9 DoD #5): the FileProvider path config for ACTION_IMAGE_CAPTURE.
            // A NEW resource-file class this milestone — a generated app's camera capture
            // needs it (the shell references @xml/file_paths from the manifest <provider>).
            "android/src/androidMain/res/xml/file_paths.xml",
            // The shell — the runtime binding surface
            "android/src/main/kotlin/io/blazornative/jni/BlazorNativeRuntime.kt",
            "android/src/main/kotlin/io/blazornative/jni/ItemsJson.kt",
            "android/src/main/kotlin/io/blazornative/jni/NativeBindings.kt",
            "android/src/main/kotlin/io/blazornative/jni/NativeFrameAdapter.kt",
            "android/src/main/kotlin/io/blazornative/jni/RenderFrame.kt",
            "android/src/main/kotlin/io/blazornative/jni/ShellBridge.kt",
            // The shell — the image tables
            "android/src/main/kotlin/io/blazornative/shell/ImageContentModeTable.kt",
            "android/src/main/kotlin/io/blazornative/shell/ImageErrorDispatch.kt",
            "android/src/main/kotlin/io/blazornative/shell/ImageRequestGuard.kt",
        ];

        // THE SUBJECT IS WHAT GIT TRACKS. The pack ships tracked content; the
        // disk is that plus whatever the last tool to run here left behind.
        List<string> tracked = TrackedFilesUnder(ContentRoot);

        // …AND WHAT THE PACK WOULD ACTUALLY SHIP — the disk tree minus the
        // csproj's own Exclude, read off that attribute. The gaps between these
        // lists are the interesting part, and they do not all mean the same
        // thing, so they are not all reported the same way.
        List<string> disk = DiskFilesUnder(ContentRoot);
        List<string> packed = PackedContentFiles();

        var missing = expected.Except(tracked, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var undeclared = tracked.Except(expected, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var wouldShipUntracked = packed.Except(tracked, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var trackedNotOnDisk = tracked.Except(disk, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var trackedButExcluded = tracked.Intersect(disk, StringComparer.Ordinal)
            .Except(packed, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0 && undeclared.Count == 0 && wouldShipUntracked.Count == 0
                && trackedNotOnDisk.Count == 0 && trackedButExcluded.Count == 0,
            "THE TEMPLATE'S SHIPPED FILE SET IS NOT THE MANIFEST (Gate 1 review I-1; subject fixed "
            + "in Phase 8.6 Gate 2). This pin is the one that knows how many files the template "
            + "SHOULD ship — every other pin here reads the CONTENT of files it is told the names "
            + "of, and Gate 1 proved that leaves the set itself unguarded (seven files deleted, "
            + "nine tests green).\n\n"
            + "FIVE DIFFERENT FAULTS, AND THEY HAVE FIVE DIFFERENT FIXES — read the one that "
            + "fired, because the wrong fix here is worse than the fault:\n\n"

            + $"  1. MISSING — in the manifest, TRACKED BY NOBODY ({missing.Count}):\n"
            + (missing.Count == 0 ? "     (none)\n" : string.Join("\n", missing.Select(f => $"     {f}")) + "\n")
            + "     A file left the template for good (git does not track it). A generated app is "
            + "short a file, and that failure lands on a stranger's laptop unless this pin says so "
            + "first. If the removal was deliberate, the manifest above is where you say so.\n\n"

            + $"  2. UNDECLARED — TRACKED, not in the manifest ({undeclared.Count}):\n"
            + (undeclared.Count == 0 ? "     (none)\n" : string.Join("\n", undeclared.Select(f => $"     {f}")) + "\n")
            + "     The pack grew a file no pin in this file compares to anything. Decide whether "
            + "it needs one, then add it to the manifest above. THIS is the arm where the edit IS "
            + "the review — do not make it green without answering that question.\n\n"

            + $"  3. THE PACK WOULD SHIP AN UNTRACKED FILE ({wouldShipUntracked.Count}):\n"
            + (wouldShipUntracked.Count == 0 ? "     (none)\n" : string.Join("\n", wouldShipUntracked.Select(f => $"     {f}")) + "\n")
            + "     ⚠ DO NOT ADD THESE TO THE MANIFEST. They are not template content — they are "
            + "local artifacts some tool left in your working tree (gradle's .gradle cache is the "
            + "usual one), and git does not track them. The manifest describes what the template "
            + "SHIPS; an untracked artifact is not a manifest violation, it is a PACK bug: "
            + "`<Content Include=\"content\\**\\*\">` is a DISK glob, so anything sitting in the "
            + "tree at pack time rides into the nupkg and out to a stranger's `dotnet new`.\n"
            + "     TWO HONEST FIXES: clean it (it is regenerable — that is why it is gitignored), "
            + "or, if its kind will keep coming back, add its pattern to that item's `Exclude` in "
            + "BlazorNative.Templates.csproj — which this pin reads, so the two cannot disagree. "
            + "⚠ NOT a blanket dotfile exclude: the gradle wrapper's tree is dotted and shipping it "
            + "is why NoDefaultExcludes is set. Name the artifact.\n\n"

            + $"  4. TRACKED, BUT NOT IN YOUR WORKING TREE ({trackedNotOnDisk.Count}):\n"
            + (trackedNotOnDisk.Count == 0 ? "     (none)\n" : string.Join("\n", trackedNotOnDisk.Select(f => $"     {f}")) + "\n")
            + "     Git tracks it and it is not on disk, so THE PACK BUILT FROM THIS TREE RIGHT NOW "
            + "WOULD SHIP WITHOUT IT — the Include is a disk glob and cannot ship what is not there. "
            + "Usually an unstaged delete: `git checkout -- <path>` restores it. If you meant to "
            + "remove it, stage the removal and update the manifest above — that is arm 1, and it "
            + "is the arm that makes the removal a decision.\n\n"

            + $"  5. TRACKED AND ON DISK, BUT THE EXCLUDE DROPS IT ({trackedButExcluded.Count}):\n"
            + (trackedButExcluded.Count == 0 ? "     (none)\n" : string.Join("\n", trackedButExcluded.Select(f => $"     {f}")) + "\n")
            + "     Committed template content that no generated app will ever receive, because the "
            + "pack's own Exclude throws it away. THE EXCLUDE IS TOO BROAD — and the shape to "
            + "suspect first is the `build/` trap: content/BlazorNative.App/build/"
            + "BionicNativeAot.targets is REAL shipped content (without it a generated app has no "
            + "bionic publish, no .so, no APK), .gitignore carries a negation to keep it in the "
            + "checkout, and an Exclude of `content\\**\\build\\**` would silently delete it from "
            + "the pack. Exclude gradle's output by its full path, not by the name `build`.\n"

            + $"\n(Manifest: {expected.Length}. Tracked: {tracked.Count}. On disk: {disk.Count}. "
            + $"The pack would ship: {packed.Count}.)");

        // NON-VACUITY. TrackedFilesUnder already reds on an empty read, but the
        // pack's side has its own way to see nothing: an Exclude broad enough to
        // drop everything would leave `packed` empty and arms 1/2 still green.
        Assert.True(packed.Count > 0,
            $"the pack's own globs select ZERO files under {ContentRoot} — it would ship an EMPTY "
            + "template. This pin would have compared an empty inventory and passed over it. A pin "
            + "that cannot see its subject must never pass vacuously.");
    }

    // ── 3. The shell's Kotlin, byte-identical ────────────────────────────────

    /// <summary>THE BYTE-IDENTITY PIN (8.3 normative rule 3). The template's
    /// shell sources are a copy of src/BlazorNative.Jni's, and the copy is EXACT
    /// — which is only possible because decision 6 keeps the Kotlin package
    /// io.blazornative.shell in both (AGP's `namespace` and `applicationId` are
    /// separate identities from a source package, so the user's app id does not
    /// force a source edit). That choice is what makes this a file comparison
    /// rather than a normalized one, and it makes the eventual .aar migration a
    /// deletion.
    ///
    /// A shell change that skips the template REDS here, in build-test, in the
    /// commit that causes it. Without this pin the template is a second copy of
    /// the shell THAT NOTHING COMPILES — the AGP 9 incident's exact shape: an
    /// assertion green and structurally unreachable, rotting silently.
    ///
    /// Byte comparison is safe across platforms despite git's autocrlf: BOTH
    /// files live in the same checkout under the same .gitattributes
    /// normalization (`* text=auto`), so they are converted identically.
    ///
    /// THE SET IS DERIVED, NOT ROSTERED (Gate 1 review I-1). It used to be a
    /// hardcoded 15-path list, and the reviewer shrank it to ONE path and got a
    /// green — the pin iterated its own roster, so cutting the roster cut the
    /// test, and `compared > 0` was happy with one. Now the subjects come off
    /// the disk: EVERY file in the template's android/ tree is byte-compared to
    /// src/BlazorNative.Jni's copy at the same relative path, except the five
    /// that genuinely diverge — each named below with the pin that owns it
    /// instead. A roster that cannot be shrunk is a roster that does not exist.
    ///
    /// Deriving also WIDENED the set from 15 files to 19: `gradlew`,
    /// `gradlew.bat`, `gradle-wrapper.jar` and `gradle-wrapper.properties` are
    /// byte-identical copies of the repo's that the roster never listed, so
    /// nothing compared them at all. They are the app's FIRST command.</summary>
    [Fact]
    public void TemplateShellSources_AreByteIdenticalToTheRepos()
    {
        List<string> verbatim = TemplateVerbatimAndroidFiles();

        AssertByteIdentical(
            verbatim.Select(f => ($"{JniRoot}/{f}", $"{TemplateAndroidRoot}/{f}")),
            "THE TEMPLATE'S SHELL KOTLIN IS A PINNED MIRROR of src/BlazorNative.Jni's (8.3 "
            + "normative rule 3) — byte-identical, deliberately: the package stays "
            + "io.blazornative.shell in both precisely so this can be a file comparison. A shell "
            + "change must be copied into the template in the SAME commit, or a generated app "
            + "silently runs a different shell from the one this repo tests.");
    }

    /// <summary>THE BUILD-MACHINERY PIN (8.3 decision 7). Two files the sample
    /// depends on that NO `dotnet new` app inherits, so the template ships both:
    ///
    ///   · build/BionicNativeAot.targets — the vendored NDK cross-compile shim.
    ///     Without it there is no bionic publish, hence no .so, hence no APK.
    ///   · global.json — the SDK band (10.0.3xx) the host ILCompiler relies on.
    ///     Without it a generated app publishes against whatever band the user
    ///     has, and the runtime-pack bypass is band-sensitive.
    ///
    /// (The better architecture — BionicNativeAot.targets INSIDE the Runtime
    /// package as build/BlazorNative.Runtime.targets, which NuGet auto-imports
    /// for every consumer — is real, is ledgered, and is not taken here: it
    /// changes a shipped package's contents and reds the smoke's inventory-shape
    /// assertion, which is DoD #2's closed pack shape re-opened inside the
    /// template phase.)</summary>
    [Fact]
    public void TemplateBuildMachinery_IsByteIdenticalToTheRepos()
    {
        AssertByteIdentical(
            [
                ("build/BionicNativeAot.targets", $"{ContentRoot}/build/BionicNativeAot.targets"),
                ("global.json", $"{ContentRoot}/global.json"),
            ],
            "The template ships the repo's build machinery VERBATIM (8.3 decision 7) — a "
            + "`dotnet new` app inherits neither the NDK cross-compile shim nor the SDK band, and "
            + "both failures are silent-ish: no bionic publish at all, or an ILC host on the wrong "
            + "feature band. Copy the change into the template in the same commit.");
    }

    /// <summary>THE OTHER SIDE OF THE SET (Gate 1 review I-1). Both pins above
    /// read the TEMPLATE's tree, so neither can see the drift that runs the
    /// other way: src/BlazorNative.Jni grows a shell file and the template
    /// never gets it. The reviewer added exactly that file and watched all nine
    /// tests pass — while this file's own header claimed, four lines from the
    /// mutation, that "a shell change that skips the template REDS HERE".
    ///
    /// So the REPO's tree is enumerated too, over the three subtrees the
    /// template mirrors wholesale (src/main/kotlin, src/androidMain/kotlin,
    /// src/androidMain/res), and every file must be one of two things:
    ///
    ///   · in the template — the normal case, and the byte pin above then owns
    ///     its contents;
    ///   · or NAMED repo-only, below, with a reason.
    ///
    /// The repo-only set is an EXCLUSION list, not a roster of subjects — the
    /// same shape as the pack csproj's `Exclude=`, and the distinction I-2
    /// turns on. Nothing is derived FROM it; it only forgives. A new shell file
    /// matches neither arm, so it reds, and the author answers the question the
    /// old pin never asked: does the template need this, or is it repo-only?
    /// Both answers are one line. Neither is silent.
    ///
    /// WHY IT MATTERS BEYOND TIDINESS: the template's build.gradle.kts compiles
    /// `src/main/kotlin` and `src/androidMain/kotlin` in the generated tree. A
    /// shell file that lands in src/ and is referenced by a file the template
    /// DOES ship makes every generated app fail to compile — and build-test,
    /// the lane that owns this contract, would say nothing. template-smoke's
    /// assembleDebug would eventually catch it as an unresolved reference, on a
    /// slower lane, naming a symbol instead of the cause.</summary>
    [Fact]
    public void EveryRepoShellSource_IsInTheTemplate_OrIsDeliberatelyRepoOnly()
    {
        // REPO-ONLY, each with its reason. Not a roster of subjects: nothing is
        // derived from this list, it only forgives what the enumeration finds.
        var repoOnly = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The inspector/preview surface — a dev-tooling stack the reference
            // shell hosts (InspectorServer/InspectorHost live in src/jvmHost,
            // which the template does not mirror at all). Nothing the template
            // ships references these; Gate 2's assembleDebug on the generated
            // tree is the standing proof that the six jni files are
            // self-sufficient without them.
            ["src/main/kotlin/io/blazornative/jni/InspectorJson.kt"] = "inspector tooling — repo-only",
            ["src/main/kotlin/io/blazornative/jni/InspectorState.kt"] = "inspector tooling — repo-only",
            ["src/main/kotlin/io/blazornative/jni/TreeSnapshot.kt"] = "inspector tooling — repo-only",
            ["src/main/kotlin/io/blazornative/jni/PreviewHost.kt"] = "preview tooling — repo-only",
            // Git placeholder, not source.
            ["src/main/kotlin/io/blazornative/jni/.gitkeep"] = "a git placeholder, not a source file",
        };

        // The three subtrees the template mirrors wholesale. src/androidTest,
        // src/test, src/jvmHost and src/debug are NOT here: they are the repo's
        // test and tooling trees, and a template shipping them would be
        // shipping this repo's test suite to a stranger.
        string[] mirroredSubtrees =
        [
            "src/main/kotlin",
            "src/androidMain/kotlin",
            "src/androidMain/res",
        ];

        var missing = new List<string>();
        int inspected = 0;

        foreach (string subtree in mirroredSubtrees)
        {
            foreach (string file in TrackedFilesUnder($"{JniRoot}/{subtree}"))
            {
                string relative = $"{subtree}/{file}";
                inspected++;

                if (repoOnly.ContainsKey(relative))
                    continue;
                if (File.Exists(CheckoutPath($"{TemplateAndroidRoot}/{relative}")))
                    continue;

                missing.Add($"  {JniRoot}/{relative}\n    has no counterpart at {TemplateAndroidRoot}/{relative}");
            }
        }

        // NON-VACUITY: an enumeration that read nothing would forgive everything.
        Assert.True(inspected > 0,
            $"enumerated ZERO shell sources under {JniRoot} — every repo file is trivially "
            + "'in the template' when nothing is read. A pin that cannot see its subject must "
            + "never pass vacuously.");

        Assert.True(missing.Count == 0,
            "A SHELL FILE EXISTS IN THE REPO AND NOT IN THE TEMPLATE (Gate 1 review I-1). This is "
            + "the drift this file's header has always claimed to catch — 'a shell change that "
            + "skips the template REDS HERE' — and until this pin existed it did not: the byte-"
            + "identity pin walks the TEMPLATE's tree, so a file the template never got was a file "
            + "it never looked for.\n\n"
            + string.Join("\n", missing)
            + $"\n\n({inspected} repo shell sources checked across "
            + $"{string.Join(", ", mirroredSubtrees)}.)\n\n"
            + "The template's build.gradle.kts compiles src/main/kotlin and src/androidMain/kotlin "
            + "in the GENERATED tree. If anything the template ships references this file, every "
            + "`dotnet new blazornative` app fails to compile — on a stranger's laptop, naming an "
            + "unresolved symbol rather than the missing copy.\n\n"
            + "TWO HONEST FIXES, and the choice is the point: copy the file into the template "
            + "(then the byte pin owns it, and it is pinned forever), or add it to this test's "
            + "`repoOnly` map with the reason it does not ship. Do not delete this pin.");
    }

    // ── 4. The gradle pins ───────────────────────────────────────────────────

    /// <summary>THE GRADLE PIN. Every pinned literal in the template's
    /// build.gradle.kts is a COPY of the repo's, so every one is drift. A
    /// floating pin would break a generated app unpredictably; a drifted pin
    /// breaks it in a way the user cannot diagnose, because their build.gradle
    /// looks fine.
    ///
    /// Yoga is the sharpest of these and it is NOT pinned here alone: ci.yml's
    /// parity step owns it across FOUR files now (Android's gradle, ios.yml,
    /// ci.yml's own YOGA_VERSION, and the template's gradle), because one engine
    /// laying out two shells differently is exactly what that step exists to
    /// prevent. This pin is the second lock on the same door, plus the rest of
    /// the toolchain the parity step does not read.</summary>
    [Fact]
    public void TemplateGradlePins_EqualTheRepos()
    {
        string repo = ReadCheckoutFile(RepoGradle);
        string template = ReadCheckoutFile(TemplateGradle);

        // name → the pattern that lifts the pinned literal out of either file.
        var pins = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AGP (com.android.application)"] = @"id\(""com\.android\.application""\) version ""([^""]+)""",
            ["KGP (kotlinVersion)"] = @"(?m)^val kotlinVersion = ""([^""]+)""",
            ["JNA (jna:@aar)"] = @"implementation\(""net\.java\.dev\.jna:jna:([^""@]+)@aar""\)",
            ["JNA (jna-platform)"] = @"implementation\(""net\.java\.dev\.jna:jna-platform:([^""]+)""\)",
            ["Yoga"] = @"implementation\(""com\.facebook\.yoga:yoga:([^""]+)""\)",
            ["soloader"] = @"implementation\(""com\.facebook\.soloader:soloader:([^""]+)""\)",
            ["Coil"] = @"implementation\(""io\.coil-kt:coil:([^""]+)""\)",
            // androidx.biometric — the first new gradle dep of M9 (Phase 9.2's
            // biometrics + OS-key-bound secure storage). The dep lives in BOTH the
            // repo's build.gradle.kts and the template's, and until this entry existed
            // the mirror was SYNCED but not ENFORCED: ParsePin reds if the pattern is
            // absent from either file, so dropping the dep from the template gradle now
            // reds this pin (it did not before — the gap Gate 2 flagged).
            ["androidx.biometric"] = @"implementation\(""androidx\.biometric:biometric:([^""]+)""\)",
            ["compileSdk"] = @"(?m)^\s*compileSdk = (\d+)",
            ["minSdk"] = @"(?m)^\s*minSdk = (\d+)",
            ["targetSdk"] = @"(?m)^\s*targetSdk = (\d+)",
            ["Java sourceCompatibility"] = @"sourceCompatibility = JavaVersion\.(\S+)",
            ["Kotlin jvmTarget"] = @"jvmTarget\.set\(org\.jetbrains\.kotlin\.gradle\.dsl\.JvmTarget\.(\w+)\)",
        };

        var offenders = new List<string>();
        foreach ((string name, string pattern) in pins)
        {
            string repoValue = ParsePin(repo, pattern, name, RepoGradle);
            string templateValue = ParsePin(template, pattern, name, TemplateGradle);
            if (repoValue != templateValue)
                offenders.Add($"  {name}: repo pins \"{repoValue}\", the template pins \"{templateValue}\"");
        }

        // The gradle wrapper — a different distribution is a different Gradle,
        // and AGP 9 is version-sensitive about it.
        string repoWrapper = ParsePin(
            ReadCheckoutFile("src/BlazorNative.Jni/gradle/wrapper/gradle-wrapper.properties"),
            @"distributionUrl=.*?gradle-([0-9][^-]*)-bin\.zip", "gradle distribution",
            "src/BlazorNative.Jni/gradle/wrapper/gradle-wrapper.properties");
        string templateWrapper = ParsePin(
            ReadCheckoutFile($"{ContentRoot}/android/gradle/wrapper/gradle-wrapper.properties"),
            @"distributionUrl=.*?gradle-([0-9][^-]*)-bin\.zip", "gradle distribution",
            $"{ContentRoot}/android/gradle/wrapper/gradle-wrapper.properties");
        if (repoWrapper != templateWrapper)
            offenders.Add($"  gradle wrapper: repo pins \"{repoWrapper}\", the template pins \"{templateWrapper}\"");

        Assert.True(offenders.Count == 0,
            "TEMPLATE GRADLE PIN DRIFT. Every pinned literal in the template's build.gradle.kts is "
            + "a COPY of src/BlazorNative.Jni's, so every one of them is drift — and a generated "
            + "app that builds against a different toolchain than the one this repo tests fails in "
            + "ways its owner cannot diagnose. Yoga most of all: ONE ENGINE lays out both shells, "
            + "and a template pinning a different version lays out differently from both, "
            + "silently.\n" + string.Join("\n", offenders));
    }

    // ── 5. MainActivity, modulo the map ──────────────────────────────────────

    /// <summary>PIN A — MainActivity ≡ the repo's, MODULO the TWO divergences it is
    /// allowed. The template's MainActivity is the one genuinely divergent shell
    /// file. One is a literal RouteTableDriftTests already parses; the other is an
    /// import the repo cannot have:
    ///
    ///   · the ultimate `?: "…"` fallback literal — the template's is the starter
    ///     page (BnStarterPage), the repo's is BnDemo;
    ///   · `import &lt;namespace&gt;.R` — TEMPLATE-ONLY. AGP generates R into the
    ///     `namespace` package while Kotlin resolves a bare `R` against the file's
    ///     own package; the repo's two happen to be the same string, the
    ///     template's are deliberately different, so only the template needs the
    ///     import. Gate 2's assembleDebug on the generated tree is what found
    ///     that (see ExciseTheAllowedDivergences).
    ///
    /// PHASE 11.0 removed the THIRD divergence (the DEEP_LINK_COMPONENTS map
    /// block): both copies now read the build-time-generated resource through the
    /// identical loadDeepLinkRoutes(), so the loader — KDoc and all — is
    /// byte-identical pinned code, and there is no map to excise. Everything else
    /// is byte-identical, so a shell fix landing in MainActivity and skipping the
    /// template reds here.
    ///
    /// ⚠ BRITTLE, and named as such by the design: this is an excision regex, and
    /// an excision is a parser that can silently stop matching. Every excision
    /// therefore asserts it FIRED — the fallback in BOTH files, the R import in the
    /// template and its ABSENCE in the repo — so a rewritten resolution chain reds
    /// this test rather than quietly comparing the wrong thing.</summary>
    [Fact]
    public void TemplateMainActivity_EqualsTheRepos_ModuloTheFallbackAndTheImport()
    {
        string repo = ExciseTheAllowedDivergences(
            ReadCheckoutFile(RepoMainActivity), RepoMainActivity, isTemplate: false);
        string template = ExciseTheAllowedDivergences(
            ReadCheckoutFile(TemplateMainActivity), TemplateMainActivity, isTemplate: true);

        if (repo == template)
            return;

        Assert.Fail(
            "MAINACTIVITY DRIFT (8.3 decision 6, Pin A; 11.0 dropped the map divergence). The "
            + "template's MainActivity must equal src/BlazorNative.Jni's EXCEPT for the two "
            + "divergences it is allowed — the ultimate `?: \"…\"` fallback literal and the "
            + "template-only `import <namespace>.R`, both excised before this comparison. Something "
            + "else moved: a shell fix landed in one copy and not the other, and a generated app "
            + "now runs a different Activity from the one this repo tests.\n"
            + "First difference:\n" + FirstDifference(repo, template)
            + "\n\nRegenerate the template's copy from the repo's, re-applying ONLY the two "
            + "divergences (the starter page's fallback + the R import).");
    }

    // ── 6. Pin B — the template's default fallback tracks its default page ───

    // PIN B (the hand-written map half) — RETIRED at Phase 11.0. It used to hold
    // the template's MainActivity.DEEP_LINK_COMPONENTS pair-for-pair against the
    // template's own AppPages.All routed rows, so a user who added page two and
    // forgot the map got a red. There is no hand-written map any more: the template
    // reads the build-time-generated res/raw/blazornative_routes.json (the same
    // codegen the repo uses), so the pairs cannot drift from the pages by
    // construction — the exact footgun this milestone closes, now closed for the
    // consumer too. The template's OWN generated resource is proven by Gate B (the
    // template-smoke lane: a generated app compiles and its routes.json contains the
    // added page); wiring the template's codegen target + a generated-routes pin on
    // it is Gate B's business, not this repo-side gate's. What survives here is the
    // half the generated resource does NOT subsume: the ultimate fallback literal.

    /// <summary>The one row the resource-backed resolution still hard-codes: the
    /// template's MainActivity keeps a `?: "…"` ULTIMATE fallback for a missing or
    /// malformed generated resource, and it must be the template manifest's default
    /// row's component. Rename the starter page on one side only and a resource-less
    /// generated app boots into a name nothing registers: rc 1 at first mount, the
    /// phase's whole nightmare, shipped. The template's AppPages.cs is CONTENT, so
    /// its manifest is parsed out of the checkout as text.</summary>
    [Fact]
    public void TemplateDefaultFallbackLiteral_IsTheTemplateManifestsDefaultComponent()
    {
        string fallback = ParseKotlinFallback(ReadCheckoutFile(TemplateMainActivity), TemplateMainActivity);
        string expected = TemplateDefaultComponent();

        Assert.True(fallback == expected,
            $"the template's MainActivity fallback literal (?: \"{fallback}\") must be the template "
            + $"manifest's default component (\"{expected}\" — the AppPages.All row registered at "
            + "BlazorNativeApp.DefaultRoute). It is the one pair DEEP_LINK_COMPONENTS deliberately "
            + "omits, so Pin B cannot see it drift; this pin can. A generated app whose fallback "
            + "names an unregistered page fails with rc 1 at first mount.");
    }

    // ── 7. The guard order, in BOTH copies ───────────────────────────────────

    /// <summary>THE GUARD-ORDER PIN (8.3 decision 5; 8.0 Gate 1 review M-1).
    ///
    /// THE RULE: `s_registered = true` must appear AFTER the `RegisterPages(`
    /// call, in both copies. Set before, a throwing registration leaves the guard
    /// SET and every retry silently no-ops into an empty registry — surfacing as
    /// rc 1 at first mount with nothing naming the cause. Set after, every path
    /// is loud.
    ///
    /// STATED HONESTLY: this proves the ORDER, not the SEMANTICS. The flip is not
    /// testable in place — EnsureRegistered is a static once-guard over a static
    /// array and there is no seam to inject a throwing manifest; a behavioural
    /// test would need a manifest that throws, which neither copy has. Here the
    /// semantics IS the order, and the alternative is nothing. Precedent:
    /// AndroidSetStyleDispatch_HasAnArmForEveryYogaStyle pins Kotlin source shape
    /// for exactly this reason.
    ///
    /// AND IT DOES THE THING THIS DECISION IS ACTUALLY ABOUT: it covers BOTH
    /// copies in one test, so the reference and the template — which 8.4 is about
    /// to publish as each other's worked example — cannot drift apart on this
    /// line again.</summary>
    [Fact]
    public void EnsureRegistered_SetsTheGuardAfterTheRegisterCall_InBothCopies()
    {
        foreach (string file in new[] { SampleAppPagesSource, TemplateAppPages })
        {
            string source = ReadCheckoutFile(file);

            // The method BODY, anchored on the signature so a comment that
            // discusses the order (both files have one) cannot match.
            Match body = Regex.Match(
                source,
                @"(?m)^\s*public static void EnsureRegistered\(\)\s*\r?\n\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
                RegexOptions.Singleline);
            Assert.True(body.Success,
                $"could not find EnsureRegistered()'s body in {file}. It moved, was renamed, or "
                + "changed shape — this pin IS the contract that the guard order is right in both "
                + "copies, so re-point it deliberately rather than deleting it. (Non-vacuity: a "
                + "pin that cannot see its subject must never pass vacuously.)");

            // COMMENTS OUT FIRST (Gate 1 review M-1). The IndexOf below reads
            // source POSITIONS, and a comment is source. Both copies carry a
            // comment that discusses this very ordering and names
            // `BlazorNativeApp.RegisterPages(` while doing it — so the pin was
            // measuring against prose. The reviewer proved it live: a body with
            // the guard set BEFORE the call (the actual bug) plus a comment
            // mentioning the call ABOVE it, and the pin PASSED, because the
            // comment's `RegisterPages(` was the first hit and it preceded the
            // guard. The pin found its own documentation and called it code.
            //
            // Anchoring on the signature was not enough: that keeps the file's
            // header out, not the comments INSIDE the body. Strip them.
            string text = StripLineComments(body.Groups["body"].Value);
            int call = text.IndexOf("BlazorNativeApp.RegisterPages(", StringComparison.Ordinal);
            int guard = text.IndexOf("s_registered = true", StringComparison.Ordinal);

            Assert.True(call >= 0,
                $"EnsureRegistered() in {file} does not call BlazorNativeApp.RegisterPages( at all — "
                + "the registration is gone, and this pin would have compared two positions in an "
                + "empty read.");
            Assert.True(guard >= 0,
                $"EnsureRegistered() in {file} has no `s_registered = true` assignment — the "
                + "once-guard is gone, and this pin would have passed vacuously.");

            Assert.True(guard > call,
                $"GUARD ORDER INVERTED in {file}.\n"
                + "`s_registered = true` must come AFTER the BlazorNativeApp.RegisterPages(All) "
                + "call, not before it (8.0 Gate 1 review M-1; 8.3 design decision 5).\n\n"
                + "Set BEFORE the call, a throwing registration leaves the guard SET — so every "
                + "retry silently no-ops into an EMPTY registry, and the app fails with rc 1 at "
                + "first mount with nothing naming the cause. Set AFTER, a throw leaves the guard "
                + "CLEAR and the retry re-throws: loud and repeatable. Every path is loud, which is "
                + "the whole improvement.\n\n"
                + "Both copies are pinned together deliberately: the sample is the reference the "
                + "template distills, and divergence between them on the exact line a review "
                + "flagged is a trap that outlives the phase.\n\n"
                + $"The body as parsed:\n{text.Trim()}");
        }
    }

    // ── The parsers ──────────────────────────────────────────────────────────

    private static string TemplateDefaultComponent()
    {
        var defaults = ParseTemplateManifest()
            .Where(r => r.Route == "BlazorNativeApp.DefaultRoute")
            .ToList();
        Assert.True(defaults.Count == 1,
            $"the template's AppPages.All must carry exactly ONE row routed at "
            + $"BlazorNativeApp.DefaultRoute (the page the app boots into), found {defaults.Count}. "
            + "Zero means a generated app boots into nothing; two is an ambiguity nobody should "
            + "have to resolve.");
        return defaults[0].Name;
    }

    /// <summary>The template's AppPages.All rows. The declaration is anchored so
    /// the file's header comment — which discusses the array and shows example
    /// rows in prose — cannot be mistaken for the array itself; and commented-out
    /// example rows INSIDE the array (the template has two, deliberately, as
    /// documentation) are stripped before parsing, or the default-fallback pin
    /// would read a page that does not exist.</summary>
    private static List<(string? Route, string Name)> ParseTemplateManifest()
    {
        string source = ReadCheckoutFile(TemplateAppPages);

        Match array = Regex.Match(
            source,
            @"(?m)^\s*public static readonly BlazorNativePage\[\] All\s*=\s*\[(?<body>.*?)^\s*\];",
            RegexOptions.Singleline);
        Assert.True(array.Success,
            $"could not find the `public static readonly BlazorNativePage[] All = [ … ];` "
            + $"declaration in {TemplateAppPages}. It moved or was rewritten — the template's "
            + "default-fallback pin reads this to find the boot page, so re-point it deliberately "
            + "rather than deleting it.");

        // Drop comment lines: the template ships commented-out example rows on
        // purpose (they are how a user learns the two factories).
        string body = string.Join("\n", array.Groups["body"].Value
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var rows = new List<(string?, string)>();
        foreach (Match row in Regex.Matches(
            body,
            @"BlazorNativePage\.(?<factory>Routed|Named)<\w+>\(\s*(?:(?<route>""[^""]+""|BlazorNativeApp\.DefaultRoute)\s*,\s*)?""(?<name>[^""]+)""\s*\)"))
        {
            string factory = row.Groups["factory"].Value;
            string? route = row.Groups["route"].Success ? row.Groups["route"].Value : null;
            rows.Add((factory == "Routed" ? route : null, row.Groups["name"].Value));
        }

        Assert.True(rows.Count > 0,
            $"parsed ZERO page rows out of {TemplateAppPages}'s All array — a generated app would "
            + "register no pages at all, and the default-fallback pin would pass over an empty read. "
            + "A pin that cannot see its subject must never pass vacuously.");
        return rows;
    }

    /// <summary>The ultimate `?: "…"` literal at the end of the map-consuming elvis
    /// chain (`?: routes["/"]` then `?: "…"`) — RouteTableDriftTests' 11.0 pattern,
    /// on the template's copy.</summary>
    private static string ParseKotlinFallback(string source, string file)
    {
        Match match = Regex.Match(
            source, @"\?\:\s*routes\[""/""\]\s*\r?\n\s*\?\:\s*""(?<name>[^""]+)""", RegexOptions.Singleline);
        Assert.True(match.Success,
            $"could not find the ultimate deep-link fallback (?: routes[\"/\"] ?: \"…\") in {file}. "
            + "The componentName resolution chain moved or was rewritten — re-point this pin "
            + "deliberately rather than deleting it.");
        return match.Groups["name"].Value;
    }

    /// <summary>Removes the TWO divergences Pin A allows, asserting that EVERY
    /// excision actually FIRED. That assertion is the whole safety of a brittle
    /// pin: an excision regex that silently stops matching would compare the
    /// wrong thing and pass.
    ///
    /// PHASE 11.0 retired the THIRD divergence — the DEEP_LINK_COMPONENTS map
    /// block. There is no hand-written map any more (both copies read the
    /// build-time-generated res/raw/blazornative_routes.json through the identical
    /// loadDeepLinkRoutes()), so the block that used to diverge — a repo KDoc full
    /// of phase history above a template's empty map — is GONE from both, and its
    /// excision with it. The loader is now byte-identical pinned code. What remains
    /// divergent is the ultimate `?: "…"` fallback literal and the template-only
    /// `import &lt;namespace&gt;.R`.</summary>
    private static string ExciseTheAllowedDivergences(string source, string file, bool isTemplate)
    {
        // Divergence 1: the ultimate fallback literal — the last `?: "…"` in the
        // componentName chain (`?: routes["/"]` then `?: "<default>"`), which the
        // repo names "BnDemo" and the template the starter page. Same anchor
        // RouteTableDriftTests uses, so the parser is not new.
        const string fallback = @"(\?\:\s*routes\[""/""\]\s*\r?\n\s*\?\:\s*"")([^""]+)("")";
        Assert.True(Regex.IsMatch(source, fallback),
            $"Pin A's fallback excision did not match in {file} — the componentName resolution "
            + "chain moved or was rewritten. An excision that silently stops matching would make "
            + "this pin compare the wrong thing and PASS, so it reds instead. Re-point it.");
        source = Regex.Replace(source, fallback, "${1}<<FALLBACK>>${3}");

        // Divergence 2: the `import <namespace>.R` line (with its comment block).
        // ASYMMETRIC — the one of the two that is not "same construct, different
        // content": the template HAS this import and the repo MUST NOT.
        //
        // WHY IT EXISTS AT ALL, because it looks like a stray edit and is not:
        // AGP generates `R` into the `namespace` package, while Kotlin resolves a
        // bare `R` against the FILE's package. The repo's namespace happens to
        // equal its source package (io.blazornative.shell), so its bare `R`
        // resolves and it needs no import. The template deliberately holds the two
        // apart — shell Kotlin stays io.blazornative.shell (that is what makes the
        // byte-identity pin a file comparison at all) while `namespace` is the
        // user's app id — so `R.layout.main` there resolves ONLY through this
        // import, which generation rewrites to the user's namespace.
        //
        // This was NOT a design assumption: 8.3 decision 6 asserted that AGP's
        // namespace "does not have to match a source package" and concluded the
        // sources could be byte-identical. That is true for every shell file that
        // never names `R` — and MainActivity names it three times. Gate 2's
        // assembleDebug on the GENERATED tree is what found it ("Unresolved
        // reference 'R'", MainActivity.kt:133), which is precisely why the design
        // chose a real compile over `gradlew tasks`.
        //
        // Asserted in BOTH directions: the template must carry exactly this
        // import (deleting it breaks the generated build — the pin says so before
        // a user does), and the repo must NOT (if it ever grows one, its namespace
        // and package have drifted apart and this excision would be hiding it).
        const string rImport = @"(?m)^(?://[^\n]*\r?\n)*import [\w.]+\.R\r?\n";
        Match r = Regex.Match(source, rImport);
        if (isTemplate)
        {
            Assert.True(r.Success,
                $"Pin A: the template's MainActivity ({file}) has NO `import <namespace>.R`. Its "
                + "shell Kotlin is in io.blazornative.shell while AGP generates R into the app's "
                + "`namespace`, so without that import the GENERATED app does not compile: "
                + "\"Unresolved reference 'R'\" at R.layout.main. Do not delete it to make this "
                + "pin green — it is the fix, not the drift (found by Gate 2's assembleDebug).");
            source = source.Remove(r.Index, r.Length);
        }
        else
        {
            Assert.False(r.Success,
                $"Pin A: the repo's MainActivity ({file}) has grown an `import <namespace>.R`. It "
                + "must not need one — its AGP namespace equals its source package "
                + "(io.blazornative.shell), which is why its bare `R` resolves. An import here "
                + "means those two have drifted apart, and this excision would silently hide the "
                + "difference from the template. Stop and analyze.");
        }

        return source;
    }

    /// <summary>Drops `//` line comments (Gate 1 review M-1). A source-ORDER
    /// pin must read code positions, and a comment is not one — the guard-order
    /// pin above was proven to accept the real bug when a comment above it
    /// happened to name `BlazorNativeApp.RegisterPages(`.
    ///
    /// Line comments only, and that is enough BY INSPECTION rather than by
    /// luck: it is applied to EnsureRegistered()'s body, which in both copies
    /// is four statements with no string literal at all (so no "http://" to
    /// mangle) and no block comment. If that body ever grows either, this
    /// stripper is the thing to revisit — it would truncate a line at a `//`
    /// inside a string, which fails SAFE here (the pin loses text and reds on a
    /// missing call) but is not a property to lean on.</summary>
    private static string StripLineComments(string body)
        => string.Join("\n", body.Split('\n').Select(line =>
        {
            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes < 0 ? line : line[..slashes];
        }));

    private static string ParsePin(string source, string pattern, string name, string file)
    {
        Match match = Regex.Match(source, pattern);
        Assert.True(match.Success,
            $"could not find the {name} pin in {file} (pattern: {pattern}). It moved or was "
            + "rewritten — a pin that cannot see its subject must never pass vacuously, so this "
            + "reds. Re-point it deliberately.");
        return match.Groups[1].Value.Trim();
    }

    // ── The disk enumerations (Gate 1 review I-1) ────────────────────────────

    /// <summary>Every GIT-TRACKED file under a checkout subtree, as
    /// forward-slash paths relative to it, sorted.
    ///
    /// GIT IS THE SUBJECT, NOT THE DISK (Phase 8.6 Gate 2). This used to
    /// enumerate the disk and call that "the nupkg's inventory". It is not:
    /// the disk is the inventory PLUS whatever the last tool to run in this
    /// tree left behind. The two are equal in a clean checkout — which is why
    /// CI never noticed — and unequal on any machine that has run gradle in the
    /// template tree, which is what a maintainer does to test the template.
    /// Eight `.gradle` cache files made two pins in this file RED, locally,
    /// forever, for a drift that had not happened.
    ///
    /// THE PACK SHIPS TRACKED CONTENT. An untracked local artifact is not a
    /// manifest violation and must never be reported as one — the old message
    /// told the reader to add it to the manifest, and following that
    /// instruction literally would have pinned gradle's lock files as required
    /// template content. What an untracked artifact IS, is a PACK bug (the
    /// glob would ship it), and the pin below says so in those words.
    ///
    /// STILL DERIVED, STILL NON-VACUOUS (the 8.1 I-2 rule): `git ls-files`
    /// enumerates, it does not roster. A git that will not run, or that returns
    /// nothing, REDS — a pin that cannot see its subject must never pass
    /// vacuously, and "git is missing" must not silently become "no files
    /// drifted".
    ///
    /// It reads the INDEX, not the disk, so a tracked file DELETED locally is
    /// still listed — correctly: it is still what a clean checkout ships, and
    /// the byte pins then red on the missing file rather than quietly comparing
    /// a shorter set.</summary>
    private static List<string> TrackedFilesUnder(string relativeRoot)
    {
        string root = CheckoutPath(relativeRoot);
        Assert.True(Directory.Exists(root),
            $"checkout directory not found: {root} — a set pin cannot enumerate a tree that is not "
            + "there, and must not pass over it.");

        string prefix = relativeRoot.TrimEnd('/') + "/";
        var files = Git($"ls-files --cached -z -- \"{relativeRoot}\"")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace('\\', '/'))
            .Where(p => p.StartsWith(prefix, StringComparison.Ordinal))
            .Select(p => p[prefix.Length..])
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(files.Count > 0,
            $"`git ls-files` returned ZERO tracked files under {relativeRoot} — the set pins would "
            + "compare an empty set and pass over it. A pin that cannot see its subject must never "
            + "pass vacuously.");
        return files;
    }

    /// <summary>Runs git from the repo root and REDS on a non-zero exit.
    ///
    /// The failure is deliberately loud rather than a fallback to the disk: a
    /// silent fallback is how "git is not available here" becomes "nothing
    /// drifted", which is the vacuous pass this whole file exists to
    /// forbid.</summary>
    private static string Git(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = RepoRoot(),
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
            Assert.Fail(
                $"could not run `git {arguments}`: {ex.Message}\n\nThe template's set pins take "
                + "their subjects from git, because the pack ships TRACKED content and the disk is "
                + "that plus whatever the last tool to run here left behind. Without git these pins "
                + "cannot see their subject, and they must red rather than pass. (build-test runs "
                + "on an actions/checkout, so git is always present there.)");
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0,
            $"`git {arguments}` exited {process.ExitCode} — the set pins cannot enumerate their "
            + $"subject and must not pass over the failure.\n{stderr}");
        return stdout;
    }

    /// <summary>Every file on DISK under a checkout subtree — used ONLY to ask
    /// what the pack's glob would sweep up, never as a pin's subject. The
    /// difference between this and <see cref="TrackedFilesUnder"/> is exactly
    /// the set of local artifacts, and naming that difference is the pack pin's
    /// whole job.</summary>
    private static List<string> DiskFilesUnder(string relativeRoot)
    {
        string root = CheckoutPath(relativeRoot);
        Assert.True(Directory.Exists(root), $"checkout directory not found: {root}");

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>What the pack would actually ship: the disk tree under
    /// content/, minus everything BlazorNative.Templates.csproj's `Exclude`
    /// drops — READ FROM THAT ATTRIBUTE, never copied into this file.
    ///
    /// The old code hardcoded `bin`/`obj` in C# while its docstring claimed to
    /// read "the same way the pack reads it". That was a copy, and the copy was
    /// already stale: the pack excluded two directory names and shipped
    /// everything else, gradle's cache included. Parsing the real attribute is
    /// what makes the claim true — change the Exclude and this pin follows in
    /// the same commit.</summary>
    private static List<string> PackedContentFiles()
    {
        (string include, var excludes) = PackContentGlobs();

        Assert.True(include == @"content\**\*",
            $"BlazorNative.Templates.csproj's <Content> Include is \"{include}\", not "
            + @"""content\**\*"". This pin derives the pack's inventory by enumerating the content "
            + "tree and subtracting the Exclude — an assumption that only holds while the Include "
            + "is that glob. Re-point it deliberately rather than letting the inventory pin measure "
            + "a set the pack no longer ships.");

        // The Exclude patterns are relative to the PROJECT directory, so the
        // paths they are matched against must be too.
        const string projectPrefix = "content/BlazorNative.App/";

        return DiskFilesUnder(ContentRoot)
            .Where(f => !excludes.Any(rx => rx.IsMatch(projectPrefix + f)))
            .ToList();
    }

    /// <summary>The pack's own Include + Exclude globs, off the csproj.</summary>
    private static (string Include, List<Regex> Excludes) PackContentGlobs()
    {
        var content = XDocument.Load(CheckoutPath(PackCsproj)).Root!
            .Elements("ItemGroup").Elements("Content")
            .ToList();

        Assert.True(content.Count == 1,
            $"{PackCsproj} must carry exactly ONE <Content> item (it is the whole of what the "
            + $"template pack ships), found {content.Count}. This pin reads that item's Include and "
            + "Exclude to know the pack's inventory; more than one, and it is reading a fraction of "
            + "the pack while claiming to read all of it.");

        string include = content[0].Attribute("Include")?.Value ?? "(none)";
        var excludes = (content[0].Attribute("Exclude")?.Value ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MsBuildGlobToRegex)
            .ToList();

        Assert.True(excludes.Count > 0,
            $"{PackCsproj}'s <Content> has no Exclude — bin/, obj/ and every local build artifact "
            + "in the template tree would ride into the nupkg. This pin would also then be asserting "
            + "nothing about the Exclude. A pin that cannot see its subject must never pass "
            + "vacuously.");
        return (include, excludes);
    }

    /// <summary>An MSBuild item glob as a regex over forward-slash paths.
    /// Handles the three constructs the pack's Exclude actually uses — `**` for
    /// any depth, `*` within a segment, and literal text — and nothing else,
    /// deliberately: a translator that quietly mis-handles a construct would
    /// under-exclude (the pin reds — safe, and it names the file) or
    /// over-exclude (the pin goes blind — NOT safe). So the parser is small
    /// enough to read, and the Exclude it is pointed at is six named
    /// patterns.</summary>
    private static Regex MsBuildGlobToRegex(string glob)
    {
        // The two multi-character constructs are punched out to sentinels
        // BEFORE the per-character pass, so their `*`s cannot be mistaken for
        // the single-segment wildcard.
        const char anyDirs = '\u0001';   // "/**/"          -> zero or more dirs
        const char tail = '\u0002';      // a trailing "/**" -> everything below

        string p = glob.Replace('\\', '/');

        if (p.EndsWith("/**", StringComparison.Ordinal))
            p = p[..^3] + tail;
        p = p.Replace("/**/", anyDirs.ToString(), StringComparison.Ordinal);

        // Any `**` still standing is a construct this translator does not
        // model. It REDS rather than guessing: a mistranslation that
        // over-excludes would make the inventory pin go quietly blind.
        Assert.True(!p.Contains("**", StringComparison.Ordinal),
            $"the pack's Exclude pattern \"{glob}\" uses a `**` shape this translator does not "
            + "model (it handles a trailing `/**` and an interior `/**/`, plus `*` and `?` within "
            + "a segment). Teach it the shape deliberately — an Exclude the pin mistranslates is "
            + "an inventory pin measuring a set the pack does not ship.");

        var sb = new StringBuilder("^");
        foreach (char c in p)
        {
            sb.Append(c switch
            {
                anyDirs => "/(?:[^/]+/)*",
                tail => "(?:/.*)?",
                '*' => "[^/]*",
                '?' => "[^/]",
                _ => Regex.Escape(c.ToString()),
            });
        }
        sb.Append('$');

        // MSBuild matches item globs case-insensitively, as does Windows.
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
    }

    /// <summary>The template's android/ files that must be BYTE-IDENTICAL to
    /// src/BlazorNative.Jni's — the whole tree MINUS the five that genuinely
    /// diverge. Derived, so it cannot be shrunk (Gate 1 review I-1).
    ///
    /// Each exclusion names the pin that owns it instead; none of the five is
    /// unpinned, which is what makes excluding them honest rather than
    /// convenient.</summary>
    private static List<string> TemplateVerbatimAndroidFiles()
    {
        var divergent = new HashSet<string>(StringComparer.Ordinal)
        {
            // AGP `namespace`/`applicationId` are the user's; every pinned
            // literal in it is held equal by TemplateGradlePins_EqualTheRepos.
            "build.gradle.kts",
            // `rootProject.name` is the user's app, not BlazorNative.Jni.
            "settings.gradle.kts",
            // Prose only (the repo's comment cites Phase 2.2 history); the
            // template's says what AGP 9 needs. No pinned literal lives here.
            "gradle.properties",
            // android:label + the FULLY-QUALIFIED activity name — the template's
            // must be fully qualified precisely because its `namespace` is the
            // user's while the shell's Kotlin package is not (the same split
            // that forces MainActivity's R import).
            "src/androidMain/AndroidManifest.xml",
            // Pin A owns this one, modulo its three allowed divergences.
            "src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt",
        };

        var files = TrackedFilesUnder($"{TemplateAndroidRoot}")
            .Where(f => !divergent.Contains(f))
            .ToList();

        Assert.True(files.Count > 0,
            $"derived ZERO verbatim files from {TemplateAndroidRoot} — the byte-identity pin would "
            + "compare nothing. A pin that cannot see its subject must never pass vacuously.");
        return files;
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    private static void AssertByteIdentical(IEnumerable<(string Repo, string Template)> pairs, string why)
    {
        var offenders = new List<string>();
        int compared = 0;

        foreach ((string repoPath, string templatePath) in pairs)
        {
            string repoFile = CheckoutPath(repoPath);
            string templateFile = CheckoutPath(templatePath);
            Assert.True(File.Exists(repoFile), $"repo file not found: {repoFile}");
            Assert.True(File.Exists(templateFile),
                $"THE TEMPLATE IS MISSING A FILE: {templateFile}\nIt must be a copy of {repoPath}. "
                + why);

            byte[] repoBytes = File.ReadAllBytes(repoFile);
            byte[] templateBytes = File.ReadAllBytes(templateFile);
            compared++;

            if (!repoBytes.AsSpan().SequenceEqual(templateBytes))
            {
                offenders.Add(
                    $"  {templatePath}\n    differs from {repoPath} "
                    + $"({repoBytes.Length} bytes vs {templateBytes.Length})\n"
                    + Indent(FirstDifference(File.ReadAllText(repoFile), File.ReadAllText(templateFile)), "    "));
            }
        }

        // NON-VACUITY: a list that enumerated nothing would green forever.
        Assert.True(compared > 0,
            "compared ZERO files — the byte-identity pin read an empty set and would have passed "
            + "over it. A pin that cannot see its subject must never pass vacuously.");

        Assert.True(offenders.Count == 0,
            why + $"\n\n{offenders.Count} file(s) drifted (of {compared} pinned):\n"
            + string.Join("\n", offenders));
    }

    /// <summary>The first differing line, with its neighbours — a byte pin whose
    /// failure said only "they differ" would send the reader to a diff tool for
    /// a 2,400-line file.</summary>
    private static string FirstDifference(string left, string right)
    {
        string[] a = left.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string[] b = right.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string? l = i < a.Length ? a[i] : null;
            string? r = i < b.Length ? b[i] : null;
            if (l == r)
                continue;
            return $"line {i + 1}:\n  repo:     {l ?? "(end of file)"}\n  template: {r ?? "(end of file)"}";
        }

        return "(no line differs — the files differ only in line endings or a trailing byte; "
             + "check .gitattributes normalization)";
    }

    private static string Indent(string text, string prefix)
        => string.Join("\n", text.Split('\n').Select(l => prefix + l));

    /// <summary>The ONE version source (8.1 design decision 4).
    /// PackageVersionPinTests owns the "exactly one, non-empty" teeth; this reads
    /// it as the value every template literal is measured against.</summary>
    private static string PropsVersion()
    {
        string props = CheckoutPath("src/Directory.Build.props");
        Assert.True(File.Exists(props),
            "src/Directory.Build.props is the ONE version source — it must exist.");

        var versions = XDocument.Load(props).Root!
            .Elements("PropertyGroup").Elements("Version")
            .Select(v => v.Value)
            .ToList();
        Assert.True(versions.Count == 1,
            $"src/Directory.Build.props must carry exactly ONE <Version>, found {versions.Count} — "
            + "the template's mirror pin has nothing unambiguous to mirror.");
        Assert.False(string.IsNullOrWhiteSpace(versions[0]),
            "the <Version> literal must not be empty — the template's mirror would be pinned to "
            + "nothing.");
        return versions[0];
    }

    /// <summary>The shipped package ids, ENUMERATED from src/'s csprojs — the
    /// 8.1 I-2 rule: never a roster. PackageId defaults to the assembly name for
    /// all six (no explicit PackageId lines anywhere), so the csproj name IS the
    /// package id.</summary>
    private static List<string> ShippedPackageIds()
    {
        string src = CheckoutPath("src");
        var names = Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(names.Count > 0,
            $"enumerated ZERO csprojs under {src} — the subset pin would pass over an empty set "
            + "(every id is trivially 'not foreign' when nothing is shipped). Fix the enumeration; "
            + "do not let it green vacuously.");
        return names!;
    }

    private static List<XElement> PackageReferences(string csproj)
        => XDocument.Load(csproj).Root!
            .Elements("ItemGroup").Elements("PackageReference")
            .ToList();

    private static string CheckoutPath(string relativePath)
        => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ReadCheckoutFile(string relativePath)
    {
        string file = CheckoutPath(relativePath);
        Assert.True(File.Exists(file), $"checkout file not found: {file}");
        return File.ReadAllText(file);
    }

    /// <summary>The repo root — the nearest ancestor holding BlazorNative.sln
    /// (RouteTableDriftTests' rule: build-test is the one required lane where the
    /// whole checkout is visible).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
