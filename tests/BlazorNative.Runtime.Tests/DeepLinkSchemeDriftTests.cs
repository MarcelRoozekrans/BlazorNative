using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DeepLinkSchemeDriftTests — the deep-link scheme NAME, one home, six copies (#296).
//
// Phase 13.4 gave the deep-link PARSE one table. The scheme the parse compares
// against was left restated in six places across three languages and two file
// formats, and nothing compared them: Swift's testTheBundleActuallyDeclaresTheScheme
// checks the plist against the Swift constant on an advisory lane, and the template
// MainActivity follows the shell's only through TemplateDriftTests. The two
// AndroidManifest.xml copies were compared with nothing, and they are not
// byte-identical, so no mirror guard reaches them.
//
// The home is src/deeplink-vectors.json's "scheme". Every site must equal it
// ORDINALLY: declarations must be lowercase because Android's intent-filter
// scheme matching is case-sensitive (RFC 3986 says producers SHOULD emit lowercase).
// Case-INsensitivity belongs to the PARSERS, which the vector table pins.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - An app generated from the template that later renames its scheme. This pins
//     the SHIPPED template; the generated app owns its copies, and
//     website/docs/shells/android.md tells its author where they are.
//   - A scheme assembled at runtime from parts. Fails green: no site spells it.
//   - A SECOND <data android:scheme> or CFBundleURLSchemes entry. That reds on the
//     exactly-one floor, which is a red for the right subject but the wrong
//     message. Disclosed rather than modelled.
//   - Any site not in Sites. Adding a seventh copy of the scheme is invisible to
//     this pin until it is added to the list. Fails green.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The deep-link scheme name has one home, and all six declaration sites agree with it.</summary>
public sealed class DeepLinkSchemeDriftTests
{
    private const string AndroidNs = "http://schemas.android.com/apk/res/android";
    private const string TemplateAndroid = "templates/BlazorNative.Templates/content/BlazorNative.App/android/src/androidMain";

    private static readonly Regex KotlinConst =
        new(@"\bconst\s+val\s+DEEP_LINK_SCHEME\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex SwiftConst =
        new(@"\bstatic\s+let\s+scheme\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant);

    internal sealed record SchemeSite(string RelativePath, Func<string, IReadOnlyList<string>> Extract);

    /// <summary>The six sites, each with the extractor for its file kind. The count is the
    /// Rule 2 floor: it is asserted, not assumed.</summary>
    internal static readonly SchemeSite[] Sites =
    [
        new("src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt", Code(KotlinConst)),
        new($"{TemplateAndroid}/kotlin/io/blazornative/shell/MainActivity.kt", Code(KotlinConst)),
        new("src/BlazorNative.Apple/BnHost/BnDeepLink.swift", Code(SwiftConst)),
        new("src/BlazorNative.Jni/src/androidMain/AndroidManifest.xml", ManifestSchemes),
        new($"{TemplateAndroid}/AndroidManifest.xml", ManifestSchemes),
        new("src/BlazorNative.Apple/BnHost/Info.plist", PlistSchemes),
    ];

    private const int ExpectedSiteCount = 6;

    private static Func<string, IReadOnlyList<string>> Code(Regex pattern) =>
        text => pattern.Matches(CommentStrippedSource.Strip(text)).Select(m => m.Groups[1].Value).ToList();

    /// <summary>XML comments are not elements, so a scheme mentioned in the manifest's
    /// own comment block (it has one) is never read.</summary>
    private static IReadOnlyList<string> ManifestSchemes(string text) =>
        ParseXml(text).Descendants("data")
            .Select(d => (string?)d.Attribute(XName.Get("scheme", AndroidNs)))
            .OfType<string>()
            .ToList();

    /// <summary>The plist is key/value siblings: the array after the
    /// <c>CFBundleURLSchemes</c> key holds the schemes.</summary>
    private static IReadOnlyList<string> PlistSchemes(string text) =>
        ParseXml(text).Descendants("key")
            .Where(k => k.Value == "CFBundleURLSchemes")
            .Select(k => k.ElementsAfterSelf().FirstOrDefault())
            .Where(a => a?.Name == "array")
            .SelectMany(a => a!.Elements("string").Select(s => s.Value))
            .ToList();

    private static XDocument ParseXml(string text)
    {
        // Info.plist carries a DOCTYPE; the default reader settings refuse DTDs.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        return XDocument.Load(reader);
    }

    internal static string Home()
    {
        string path = Path.Combine(BnRepo.Root(), "src", "deeplink-vectors.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.TryGetProperty("scheme", out JsonElement scheme),
            $"{path} has no top-level \"scheme\" — the one home of the deep-link scheme name is gone.");
        return scheme.GetString() ?? "";
    }

    private static List<(string Site, IReadOnlyList<string> Values)> ReadAll()
    {
        string root = BnRepo.Root();
        var read = new List<(string, IReadOnlyList<string>)>();
        foreach (SchemeSite site in Sites)
        {
            string full = Path.Combine(root, site.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full),
                $"scheme site moved or was deleted: {site.RelativePath}. Update Sites to its new home; do not drop it.");
            read.Add((site.RelativePath, site.Extract(File.ReadAllText(full))));
        }
        return read;
    }

    /// <summary>THE detector. The pin and its positive control both call this one
    /// implementation, so the control exercises the production path (Rule 3, Rule 8).</summary>
    internal static List<string> Mismatches(string home, IEnumerable<(string Site, IReadOnlyList<string> Values)> sites) =>
        sites.SelectMany(s => s.Values
                .Where(v => !string.Equals(v, home, StringComparison.Ordinal))
                .Select(v => $"{s.Site}: \"{v}\" (home is \"{home}\")"))
            .ToList();

    [Fact]
    public void TheHome_IsANonEmptyLowercaseScheme()
    {
        string home = Home();
        Assert.Matches("^[a-z][a-z0-9+.-]*$", home);
    }

    /// <summary>Rule 2: every site yields exactly one value, and all six are visited. Without
    /// this, an extractor that stopped matching would hand the agreement fact an empty list
    /// and it would pass over nothing.</summary>
    [Fact]
    public void EverySchemeSite_YieldsExactlyOneValue()
    {
        var read = ReadAll();
        Assert.Equal(ExpectedSiteCount, read.Count);
        foreach ((string site, IReadOnlyList<string> values) in read)
            Assert.True(values.Count == 1,
                $"{site}: expected exactly one scheme declaration, found {values.Count} [{string.Join(", ", values)}]. "
                + "Zero means the extractor no longer matches the file (the pin would be scanning nothing); "
                + "more than one means a second scheme was declared, which this pin does not model.");
    }

    [Fact]
    public void EverySchemeSite_AgreesWithTheHome()
    {
        List<string> mismatches = Mismatches(Home(), ReadAll());
        Assert.True(mismatches.Count == 0,
            "deep-link scheme drift — every copy must equal src/deeplink-vectors.json's \"scheme\" ordinally:\n  "
            + string.Join("\n  ", mismatches));
    }

    /// <summary>Rule 3 positive control: the REAL template manifest, with one planted
    /// misspelling, must produce exactly one mismatch naming that site, through the same
    /// extractor and the same detector the pin uses.</summary>
    [Fact]
    public void ThePositiveControl_APlantedMisspellingIsReported()
    {
        SchemeSite template = Sites.Single(s =>
            s.RelativePath.StartsWith("templates/", StringComparison.Ordinal)
            && s.RelativePath.EndsWith("AndroidManifest.xml", StringComparison.Ordinal));
        string real = File.ReadAllText(Path.Combine(BnRepo.Root(),
            template.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string home = Home();
        string planted = real.Replace($"android:scheme=\"{home}\"", "android:scheme=\"blazor-native\"", StringComparison.Ordinal);
        Assert.NotEqual(real, planted);   // the splice landed, or this control proves nothing

        List<string> found = Mismatches(home, [(template.RelativePath, template.Extract(planted))]);

        string only = Assert.Single(found);
        Assert.Contains(template.RelativePath, only);
        Assert.Contains("blazor-native", only);
    }

    /// <summary>Every vector that expects a route is written against the home scheme,
    /// compared case-INsensitively: the #296 row deliberately differs in case. Floor: the
    /// table has six routed rows before 15.3's #296 row and seven after it; losing any reds here.</summary>
    [Fact]
    public void EveryRoutedVector_UsesTheHomeScheme()
    {
        string home = Home();
        var routed = BnDeepLinkVectors.All.Where(v => v.Route is not null).ToList();
        Assert.True(routed.Count >= 6, $"only {routed.Count} routed vectors; the table has lost its cases.");
        foreach ((string url, _) in routed)
            Assert.True(url.StartsWith(home + "://", StringComparison.OrdinalIgnoreCase),
                $"routed vector {url} is not written against the home scheme \"{home}\".");
    }
}
