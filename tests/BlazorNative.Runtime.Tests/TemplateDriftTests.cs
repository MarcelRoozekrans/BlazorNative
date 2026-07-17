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
//   3. The shell's Kotlin is BYTE-IDENTICAL to src/BlazorNative.Jni's. This is a
//      real byte comparison, not a normalized one, and the trick that buys that
//      exactness is decision 6's: the template's shell keeps the
//      io.blazornative.shell PACKAGE (AGP's `namespace`/`applicationId` are
//      separate identities, so they can be the user's while the sources are
//      not). A shell change that skips the template reds HERE. Without this the
//      template would be a second copy of the shell THAT NOTHING COMPILES — the
//      AGP 9 incident's exact shape.
//   4. The gradle pins == the repo's. Yoga most of all: one engine, and a
//      template that drifts from it lays out differently from both shells,
//      silently. (ci.yml's parity step owns Yoga's FOURTH copy; this pin owns
//      the rest.)
//   5. MainActivity == the repo's, MODULO the three divergences it is ALLOWED:
//      the map block, the fallback literal, and the template-only
//      `import <namespace>.R` (AGP puts R in the `namespace` package; Kotlin
//      resolves a bare `R` against the FILE's package — the repo's two match by
//      coincidence, the template's differ by design, so only the template needs
//      the import. Gate 2's assembleDebug found that; the design had assumed
//      byte-identity was free here). ⚠ Brittle by construction (an excision),
//      and the design says so; the clean fix (extract the map to its own file)
//      is a shell change and a 184-test re-run, ledgered rather than smuggled in
//      here. The first two excisions target exactly what RouteTableDriftTests
//      already parses, so the parser is not new.
//   6. PIN B: the template's OWN map + fallback vs the template's OWN
//      AppPages.All. The template ships the same contract the repo has — a page
//      is declared once; the Kotlin map is the one pinned mirror — so the user
//      inherits a pin that will insist when they add page two.
//   7. The guard ORDER, in BOTH copies (the sample's and the template's).
//      Stated honestly: a source-order pin proves the ORDER, not the SEMANTICS.
//      Here the semantics IS the order, and the alternative is nothing —
//      EnsureRegistered is a static once-guard over a static array, with no seam
//      to inject a throwing manifest. Direct precedent:
//      AndroidSetStyleDispatch_HasAnArmForEveryYogaStyle pins Kotlin source
//      shape for the same reason. What it DOES do is cover both copies in one
//      test, so reference and template cannot drift apart on this line again.
//   8. global.json + BionicNativeAot.targets == the repo's. A generated app
//      inherits NEITHER from the repo, and without them there is no bionic
//      publish (hence no .so, hence no APK) and no SDK-band pin under the ILC
//      host.
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

    private const string RepoGradle = "src/BlazorNative.Jni/build.gradle.kts";
    private const string TemplateGradle = ContentRoot + "/android/build.gradle.kts";
    private const string RepoMainActivity =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt";
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

        // NON-VACUITY: a pin that found no literals would green forever.
        Assert.True(literals.Count >= 5,
            $"found only {literals.Count} version literals in the template tree — expected at least 5 "
            + "(three BlazorNative PackageReferences + template.json's default + the pack's own "
            + "<Version>). The pin is reading the wrong files or the template changed shape; a pin "
            + "that cannot see its subject must never pass vacuously.");

        var offenders = literals.Where(l => l.Value != expected).ToList();
        Assert.True(offenders.Count == 0,
            $"TEMPLATE VERSION DRIFT. src/Directory.Build.props is the ONE version source "
            + $"(\"{expected}\"); every literal in the template tree is a MIRROR of it and must be "
            + "bumped in the SAME commit. Offenders:\n"
            + string.Join("\n", offenders.Select(o => $"  {o.Where}\n    is \"{o.Value}\", must be \"{expected}\""))
            + $"\n(Checked {literals.Count} literals in total.)");
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

    // ── 2. The shell's Kotlin, byte-identical ────────────────────────────────

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
    /// normalization (`* text=auto`), so they are converted identically.</summary>
    [Fact]
    public void TemplateShellSources_AreByteIdenticalToTheRepos()
    {
        // The VERBATIM set (8.3 decision 6's split table). MainActivity is NOT
        // here — it is the one genuinely divergent file, pinned separately below.
        var verbatim = new[]
        {
            // jni/ — the runtime binding surface
            "src/main/kotlin/io/blazornative/jni/BlazorNativeRuntime.kt",
            "src/main/kotlin/io/blazornative/jni/NativeBindings.kt",
            "src/main/kotlin/io/blazornative/jni/NativeFrameAdapter.kt",
            "src/main/kotlin/io/blazornative/jni/RenderFrame.kt",
            "src/main/kotlin/io/blazornative/jni/ShellBridge.kt",
            "src/main/kotlin/io/blazornative/jni/ItemsJson.kt",
            // shell/ — the image tables
            "src/main/kotlin/io/blazornative/shell/ImageContentModeTable.kt",
            "src/main/kotlin/io/blazornative/shell/ImageErrorDispatch.kt",
            "src/main/kotlin/io/blazornative/shell/ImageRequestGuard.kt",
            // shell/ — the mapper, the layout, the bridge, the spinner
            "src/androidMain/kotlin/io/blazornative/shell/WidgetMapper.kt",
            "src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt",
            "src/androidMain/kotlin/io/blazornative/shell/AndroidShellBridge.kt",
            "src/androidMain/kotlin/io/blazornative/shell/BnSpinner.kt",
            // res
            "src/androidMain/res/layout/main.xml",
            "src/androidMain/res/xml/network_security_config.xml",
        };

        AssertByteIdentical(
            verbatim.Select(f => ($"src/BlazorNative.Jni/{f}", $"{ContentRoot}/android/{f}")),
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

    // ── 3. The gradle pins ───────────────────────────────────────────────────

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

    // ── 4. MainActivity, modulo the map ──────────────────────────────────────

    /// <summary>PIN A — MainActivity ≡ the repo's, MODULO the three divergences
    /// it is allowed. The template's MainActivity is the one genuinely divergent
    /// shell file. Two of the three are things RouteTableDriftTests already
    /// parses; the third is an import the repo cannot have:
    ///
    ///   · DEEP_LINK_COMPONENTS — the template's is EMPTY (a one-page app has no
    ///     non-"/" routes), and its KDoc documents the template's contract
    ///     rather than the repo's phase history;
    ///   · the `?: "…"` fallback literal — the template's is the starter page;
    ///   · `import &lt;namespace&gt;.R` — TEMPLATE-ONLY. AGP generates R into the
    ///     `namespace` package while Kotlin resolves a bare `R` against the file's
    ///     own package; the repo's two happen to be the same string, the
    ///     template's are deliberately different, so only the template needs the
    ///     import. Gate 2's assembleDebug on the generated tree is what found
    ///     that (see ExciseTheAllowedDivergences).
    ///
    /// Everything else is byte-identical, so a shell fix landing in MainActivity
    /// and skipping the template reds here.
    ///
    /// ⚠ BRITTLE, and named as such by the design: this is an excision regex,
    /// and an excision is a parser that can silently stop matching. Every
    /// excision therefore asserts it FIRED — the map and the fallback in BOTH
    /// files, the R import in the template and its ABSENCE in the repo — so a
    /// moved map or a rewritten resolution chain reds this test rather than
    /// quietly comparing the wrong thing. The clean fix (extract the map to its
    /// own file, retiring the excision entirely) is a shell change and a
    /// 184-instrumented-test re-run: ledgered, not smuggled into this phase.</summary>
    [Fact]
    public void TemplateMainActivity_EqualsTheRepos_ModuloTheMapAndTheFallback()
    {
        string repo = ExciseTheAllowedDivergences(
            ReadCheckoutFile(RepoMainActivity), RepoMainActivity, isTemplate: false);
        string template = ExciseTheAllowedDivergences(
            ReadCheckoutFile(TemplateMainActivity), TemplateMainActivity, isTemplate: true);

        if (repo == template)
            return;

        Assert.Fail(
            "MAINACTIVITY DRIFT (8.3 decision 6, Pin A). The template's MainActivity must equal "
            + "src/BlazorNative.Jni's EXCEPT for the three divergences it is allowed — the "
            + "DEEP_LINK_COMPONENTS map block, the `?: \"…\"` fallback literal, and the "
            + "template-only `import <namespace>.R`, all excised "
            + "before this comparison. Something else moved: a shell fix landed in one copy and "
            + "not the other, and a generated app now runs a different Activity from the one this "
            + "repo tests.\n"
            + "First difference:\n" + FirstDifference(repo, template)
            + "\n\nRegenerate the template's copy from the repo's, re-applying ONLY the three "
            + "divergences (empty map + the starter page's fallback + the R import).");
    }

    // ── 5. Pin B — the template's own map tracks the template's own pages ────

    /// <summary>PIN B — the template ships the SAME CONTRACT the repo has: a page
    /// is declared once (AppPages.All), and the Kotlin map is the one pinned
    /// mirror of its routed rows. So the user inherits a pin that will insist
    /// when they add page two — which is the moment this contract stops being
    /// theory for them.
    ///
    /// PAIRS, NOT NAME SETS (RouteTableDriftTests' rule): the drift that matters
    /// is a route mapped to the WRONG page, which set-equality cannot see.
    ///
    /// The template's AppPages.cs is CONTENT, not a compiled reference, so its
    /// manifest is parsed out of the checkout as text — the same handle every
    /// other pin in this file uses on the template.</summary>
    [Fact]
    public void TemplateDeepLinkMap_IsTheTemplateManifestsRoutedRowsMinusDefault_PairForPair()
    {
        Dictionary<string, string> kotlin = ParseKotlinPairTable(
            ReadCheckoutFile(TemplateMainActivity), TemplateMainActivity, allowEmpty: true);
        Dictionary<string, string> expected = TemplateRoutedPairs();

        var onlyInManifest = expected.Where(e => !kotlin.ContainsKey(e.Key)).ToList();
        var onlyInKotlin = kotlin.Where(k => !expected.ContainsKey(k.Key)).ToList();
        var wrongComponent = expected
            .Where(e => kotlin.TryGetValue(e.Key, out string? actual) && actual != e.Value)
            .Select(e => $"\"{e.Key}\" → manifest says \"{e.Value}\", Kotlin says \"{kotlin[e.Key]}\"")
            .ToList();

        Assert.True(
            onlyInManifest.Count == 0 && onlyInKotlin.Count == 0 && wrongComponent.Count == 0,
            "TEMPLATE ROUTE DRIFT (Pin B). The template's MainActivity.DEEP_LINK_COMPONENTS must "
            + "mirror the template's own AppPages.All routed rows (minus \"/\", which rides the ?: "
            + "fallback) PAIR-FOR-PAIR — the same contract the repo holds itself to, shipped to the "
            + "user.\n"
            + $"  only in AppPages.All (route missing from Kotlin): {JoinPairs(onlyInManifest)}\n"
            + $"  only in Kotlin (route AppPages.All does not know): {JoinPairs(onlyInKotlin)}\n"
            + "  route mapped to the WRONG page: "
            + (wrongComponent.Count == 0 ? "(none)" : string.Join(", ", wrongComponent))
            + "\nThe map is consulted at Intent-parse time, before the .so loads — it MUST be a "
            + "hand-written copy, and a copy that drifts opens the WRONG SCREEN from a deep link.");
    }

    /// <summary>PIN B's other half — the one routed row the map deliberately does
    /// not carry. No deep link (or an unknown one) mounts the `?:` literal, which
    /// must be the template manifest's default row's component. Rename the
    /// starter page on one side only and a generated app boots into a name
    /// nothing registers: rc 1 at first mount, the phase's whole nightmare,
    /// shipped.</summary>
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

    // ── 6. The guard order, in BOTH copies ───────────────────────────────────

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

            string text = body.Groups["body"].Value;
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

    /// <summary>The template manifest's routed rows, minus the default route —
    /// exactly what the template's Kotlin map is supposed to mirror. Parsed as
    /// TEXT: AppPages.cs is template content, not a compiled reference, so
    /// (unlike RouteTableDriftTests, which can hold SampleAppPages.All in its
    /// hand) the checkout is the only handle.</summary>
    private static Dictionary<string, string> TemplateRoutedPairs()
        => ParseTemplateManifest()
            .Where(r => r.Route is not null && r.Route != "BlazorNativeApp.DefaultRoute")
            .ToDictionary(r => r.Route!.Trim('"'), r => r.Name, StringComparer.Ordinal);

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
    /// documentation) are stripped before parsing, or Pin B would demand Kotlin
    /// entries for pages that do not exist.</summary>
    private static List<(string? Route, string Name)> ParseTemplateManifest()
    {
        string source = ReadCheckoutFile(TemplateAppPages);

        Match array = Regex.Match(
            source,
            @"(?m)^\s*public static readonly BlazorNativePage\[\] All\s*=\s*\[(?<body>.*?)^\s*\];",
            RegexOptions.Singleline);
        Assert.True(array.Success,
            $"could not find the `public static readonly BlazorNativePage[] All = [ … ];` "
            + $"declaration in {TemplateAppPages}. It moved or was rewritten — Pin B IS the "
            + "contract that the template's route map tracks the template's pages, so re-point it "
            + "deliberately rather than deleting it.");

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
            + "register no pages at all, and Pin B would pass over an empty read. A pin that "
            + "cannot see its subject must never pass vacuously.");
        return rows;
    }

    /// <summary>Every `"route" to "name"` pair inside DEEP_LINK_COMPONENTS.
    /// Accepts the explicit-type form (`mapOf&lt;String, String&gt;()`), which the
    /// template needs because an EMPTY `mapOf()` cannot infer its type — the one
    /// spelling difference the empty map forces.
    ///
    /// <paramref name="allowEmpty"/> is the template's case and it is a
    /// deliberate, narrow exception to the never-vacuous rule: the template's map
    /// is CORRECTLY empty (one page, no non-"/" routes), so emptiness is the
    /// expected content rather than a failed read. The read itself is still
    /// asserted — the DECLARATION must be found, which is the part that could
    /// silently stop matching.</summary>
    private static Dictionary<string, string> ParseKotlinPairTable(string source, string file, bool allowEmpty)
    {
        Match match = Regex.Match(
            source,
            @"(?m)^\s*private val DEEP_LINK_COMPONENTS = mapOf(?:<[^>]*>)?\((?<body>[^)]*)\)",
            RegexOptions.Singleline);
        Assert.True(match.Success,
            $"could not find DEEP_LINK_COMPONENTS in {file}. It moved or was renamed — this drift "
            + "test IS the contract, so re-point it deliberately rather than deleting it.");

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match pair in Regex.Matches(
            match.Groups["body"].Value, @"""(?<route>[^""]+)""\s+to\s+""(?<name>[^""]+)"""))
        {
            pairs[pair.Groups["route"].Value] = pair.Groups["name"].Value;
        }

        if (!allowEmpty)
            Assert.NotEmpty(pairs);
        return pairs;
    }

    /// <summary>The `?: "…"` literal at the end of the map-consuming elvis chain
    /// — RouteTableDriftTests' pattern, on the template's copy.</summary>
    private static string ParseKotlinFallback(string source, string file)
    {
        Match match = Regex.Match(
            source, @"DEEP_LINK_COMPONENTS\[it\] \}\s*\?\:\s*""(?<name>[^""]+)""", RegexOptions.Singleline);
        Assert.True(match.Success,
            $"could not find the deep-link default fallback (?: \"…\") in {file}. The componentName "
            + "resolution chain moved or was rewritten — re-point this pin deliberately rather "
            + "than deleting it.");
        return match.Groups["name"].Value;
    }

    /// <summary>Removes the three divergences Pin A allows, asserting that EVERY
    /// excision actually FIRED. That assertion is the whole safety of a brittle
    /// pin: an excision regex that silently stops matching would compare the
    /// wrong thing and pass.</summary>
    private static string ExciseTheAllowedDivergences(string source, string file, bool isTemplate)
    {
        // Divergence 1: the map block — its KDoc and the declaration together.
        // The KDoc is part of the block deliberately: it DOCUMENTS the map, so a
        // repo KDoc full of phase history ("Phase 7.5 adds /imagepolish …") would
        // otherwise have to ship, verbatim and false, above a template map that
        // is empty. Excising the doc with its declaration lets each copy describe
        // its own contract truthfully. What remains pinned is all of the file's
        // CODE.
        const string mapBlock =
            @"(?s)/\*\*(?:(?!\*/).)*\*/\r?\n\s*private val DEEP_LINK_COMPONENTS = mapOf(?:<[^>]*>)?\([^)]*\)";
        Match map = Regex.Match(source, mapBlock);
        Assert.True(map.Success,
            $"Pin A's map-block excision did not match in {file} — the DEEP_LINK_COMPONENTS "
            + "declaration or its KDoc moved. An excision that silently stops matching would make "
            + "this pin compare the wrong thing and PASS, so it reds instead. Re-point it.");
        source = source.Remove(map.Index, map.Length).Insert(map.Index, "<<DEEP_LINK_COMPONENTS>>");

        // Divergence 2: the fallback literal — exactly what RouteTableDriftTests
        // parses, so the parser is not new.
        const string fallback = @"(DEEP_LINK_COMPONENTS\[it\] \}\s*\?\:\s*"")([^""]+)("")";
        Assert.True(Regex.IsMatch(source, fallback),
            $"Pin A's fallback excision did not match in {file} — the componentName resolution "
            + "chain moved or was rewritten. Same rule: it reds rather than comparing the wrong "
            + "thing. Re-point it.");
        source = Regex.Replace(source, fallback, "${1}<<FALLBACK>>${3}");

        // Divergence 3: the `import <namespace>.R` line (with its comment block).
        // ASYMMETRIC — the only one of the three that is not "same construct,
        // different content": the template HAS this import and the repo MUST NOT.
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

    private static string ParsePin(string source, string pattern, string name, string file)
    {
        Match match = Regex.Match(source, pattern);
        Assert.True(match.Success,
            $"could not find the {name} pin in {file} (pattern: {pattern}). It moved or was "
            + "rewritten — a pin that cannot see its subject must never pass vacuously, so this "
            + "reds. Re-point it deliberately.");
        return match.Groups[1].Value.Trim();
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

    private static string JoinPairs(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var list = pairs
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"\"{p.Key}\" → \"{p.Value}\"")
            .ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

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
