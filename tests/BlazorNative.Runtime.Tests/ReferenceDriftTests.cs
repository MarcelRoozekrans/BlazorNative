using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using BlazorNative.Core;      // #173: the Core reference drift pin
using BlazorNative.Device;

namespace BlazorNative.Runtime.Tests;

/// <summary>
/// THE #173 SIBLING OF <see cref="ComponentReferenceDriftTests"/>. The generated API
/// reference used to cover ONE of the seven shipped packages; #173 widened it, and
/// this file widens the drift guard with it so the reference cannot silently fall
/// behind the shipped set again.
///
/// One generated package == one fixture here, and each fixture runs the LANE'S OWN
/// pipeline: scripts/generate-reference.ps1 with <c>-Package &lt;name&gt;</c>, the
/// same script website/package.json's `prebuild` runs. A pin that re-implemented
/// publish + generate could pass forever while the lane went blind — the exact
/// defect the Components file documents at length. One home, many callers.
///
/// WHY THIS IS NOT MERELY <see cref="ComponentReferenceDriftTests"/> WITH A DIFFERENT
/// TYPE. That file also holds the two Razor-generator blind spots (type-level
/// summaries and [Parameter] docs live in *_razor.g.cs, where the generator emits
/// <c>#pragma warning disable 1591</c>, so CS1591 cannot see them and a TEST must).
/// The packages guarded here have NO .razor: their public surface is hand-written
/// C#, so <c>BnEnforceDocCoverage</c> (CS1591-as-error, per csproj) already forbids
/// an undocumented member at compile time. What CS1591 CANNOT prove is that the
/// generator emitted a PAGE for every shipped type — that is this file's job.
/// </summary>
public abstract class ReferenceFixtureBase : IDisposable
{
    public string OutputDirectory { get; }
    public string GeneratorLog { get; }

    protected ReferenceFixtureBase(string package)
    {
        OutputDirectory = Path.Combine(
            Path.GetTempPath(), $"bn-docs-reference-{package}-" + Guid.NewGuid().ToString("N"));

        string script = Path.Combine(
            ComponentReferenceFixture.RepoRoot(), "scripts", "generate-reference.ps1");
        Assert.True(File.Exists(script), $"generator script not found: {script}");

        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = ComponentReferenceFixture.RepoRoot(),
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("-Package");
        psi.ArgumentList.Add(package);
        psi.ArgumentList.Add("-OutputPath");
        psi.ArgumentList.Add(OutputDirectory);

        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        GeneratorLog = stdout + stderr;

        Assert.True(p.ExitCode == 0,
            $"generate-reference.ps1 -Package {package} failed (exit {p.ExitCode}):\n{GeneratorLog}");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { if (Directory.Exists(OutputDirectory)) Directory.Delete(OutputDirectory, true); }
        catch (IOException) { /* a temp dir that outlives the run is not a failure */ }
    }
}

/// <summary>Generates the <c>BlazorNative.Device</c> reference once for the whole class.</summary>
public sealed class DeviceReferenceFixture : ReferenceFixtureBase
{
    public DeviceReferenceFixture() : base("Device") { }
}

/// <summary>
/// PIN — the Device reference documents EXACTLY the package's public types, red in
/// both directions. Device is the first non-Components package #173 added, chosen
/// because its whole public surface — the five <c>[Inject]</c>-able façades plus the
/// <c>AddBlazorNativeDevice</c> registration — was already documented, so
/// <c>BnEnforceDocCoverage</c> could go on with zero CS1591.
/// </summary>
/// <summary>
/// THE COLLECTION THAT SERIALISES REFERENCE GENERATION.
///
/// <para>Both this file's <see cref="DeviceReferenceFixture"/> and
/// ComponentReferenceDriftTests' <c>ComponentReferenceFixture</c> shell out to
/// <c>scripts/generate-reference.ps1</c>, and that script begins with
/// <c>dotnet tool restore</c>. xUnit runs distinct test CLASSES in parallel by
/// default, so the two restores ran concurrently and raced on one file in the
/// shared NuGet package cache:</para>
///
/// <code>
/// The process cannot access the file
/// '…\.nuget\packages\xmldoc2markdown\6.0.0\xmldoc2markdown.6.0.0.nupkg'
/// because it is being used by another process.
/// </code>
///
/// <para>It reds as a fixture-constructor throw taking BOTH of this class's tests
/// with it, on a REQUIRED lane, with a message that names neither the racing pair
/// nor the fact that it is a race — so it reads as a real failure of whatever PR
/// happened to be running. Latent since the generator grew its second package;
/// surfaced by #204, which is unrelated to reference generation.</para>
///
/// <para>A shared collection name is the whole fix: xUnit never runs two classes
/// in the same collection concurrently, so the restores serialise. Cheaper and
/// more honest than a retry loop — there is no flakiness to absorb once the two
/// cannot overlap.</para>
/// </summary>
public static class ReferenceGeneration
{
    public const string Name = "reference-generation";
}

[Collection(ReferenceGeneration.Name)]
public sealed class ReferenceDriftTests : IClassFixture<DeviceReferenceFixture>
{
    private readonly DeviceReferenceFixture _fixture;

    public ReferenceDriftTests(DeviceReferenceFixture fixture) => _fixture = fixture;

    private static Assembly DeviceAssembly => typeof(IGeolocation).Assembly;

    /// <summary>The public types the assembly HAS — measured by reflection, DERIVED
    /// not declared, so adding/renaming/removing a public type moves it automatically
    /// and it can never become a roster someone shrinks to make a red go away.</summary>
    private static IEnumerable<Type> PublicTypes()
        => DeviceAssembly.GetTypes().Where(t => t.IsPublic);

    /// <summary>xmldoc2md's file naming: the full type name, lowercased, generic-arity
    /// backtick as a dash — the same rule ComponentReferenceDriftTests uses.</summary>
    private static string PageNameFor(Type t)
        => t.FullName!.Replace('`', '-').ToLowerInvariant() + ".md";

    /// <summary>
    /// The generated page set equals the assembly's public type set, RED IN BOTH
    /// DIRECTIONS. MISSING = a shipped type with no page (the reference fell behind).
    /// UNEXPECTED = a page for a type the assembly does not publish.
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

        // NON-VACUITY, BOTH SIDES, FIRST — an expectation of zero types would be
        // satisfied by a generator that wrote nothing, which is the shape of the
        // defect this whole guard exists for.
        Assert.True(expected.Count > 0,
            "reflected ZERO public types out of BlazorNative.Device — the completeness "
            + "pin has no expectation to hold anything against.");
        Assert.True(actual.Count > 0,
            $"the generator wrote NO pages into {_fixture.OutputDirectory}.\n\n{_fixture.GeneratorLog}");

        var missing = expected.Except(actual, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var unexpected = actual.Except(expected, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && unexpected.Count == 0,
            "THE DEVICE REFERENCE DRIFTED FROM THE ASSEMBLY.\n\n"
            + $"  MISSING (the assembly publishes it, the reference does not document it — {missing.Count}):\n"
            + (missing.Count == 0 ? "    (none)\n" : string.Join("\n", missing.Select(f => $"    {f}")) + "\n")
            + $"  UNEXPECTED (the reference documents it, the assembly does not publish it — {unexpected.Count}):\n"
            + (unexpected.Count == 0 ? "    (none)\n" : string.Join("\n", unexpected.Select(f => $"    {f}")) + "\n")
            + $"\n(Assembly: {expected.Count} public types. Generated: {actual.Count} pages.)\n\n"
            + "If a whole package's types are missing, the generator is likely pointed at bin/ "
            + "instead of a publish output — see scripts/generate-reference.ps1.\n\n"
            + "Generator output:\n" + _fixture.GeneratorLog);
    }

    /// <summary>
    /// The five façades and the registration extension each have a page — named, so
    /// the assertion cannot be satisfied by an empty set that technically has no
    /// missing members. These are the types a Device consumer opens first.
    /// </summary>
    [Fact]
    public void GeneratedReference_ContainsTheFacadesAndRegistration()
    {
        foreach (var t in new[]
                 {
                     typeof(IGeolocation), typeof(INotifications), typeof(IBiometrics),
                     typeof(ISecureStorage), typeof(ICamera),
                     typeof(ServiceCollectionExtensions),
                 })
        {
            string page = Path.Combine(_fixture.OutputDirectory, PageNameFor(t));
            Assert.True(File.Exists(page),
                $"the Device reference is missing a page for {t.Name} ({PageNameFor(t)}).\n\n"
                + _fixture.GeneratorLog);
        }
    }
}

/// <summary>Generates the <c>BlazorNative.Core</c> reference once for the whole class (#173).</summary>
public sealed class CoreReferenceFixture : ReferenceFixtureBase
{
    public CoreReferenceFixture() : base("Core") { }
}

/// <summary>
/// PIN — the Core reference documents EXACTLY the package's public types, red in both
/// directions (#173). Core was the deferred one: its contract surface —
/// <c>IMobileBridge</c>'s 27 members, <c>DevHostBridge</c>, and the wire-mirrored
/// enums/records — carried rich <c>//</c> block comments but no <c>///</c> XML, so
/// <c>BnEnforceDocCoverage</c> could not go on and it could not be generated (a page
/// for an undocumented member is a blank stub). Those were converted to XML; this pin
/// holds the generated set equal to the shipped set from here.
///
/// <para>In the <see cref="ReferenceGeneration"/> collection deliberately: like the
/// Device and Components fixtures it shells out to <c>generate-reference.ps1</c>'s
/// <c>dotnet tool restore</c>, and the shared collection serialises them so they cannot
/// race on the NuGet cache.</para>
/// </summary>
[Collection(ReferenceGeneration.Name)]
public sealed class CoreReferenceDriftTests : IClassFixture<CoreReferenceFixture>
{
    private readonly CoreReferenceFixture _fixture;

    public CoreReferenceDriftTests(CoreReferenceFixture fixture) => _fixture = fixture;

    private static Assembly CoreAssembly => typeof(IMobileBridge).Assembly;

    private static IEnumerable<Type> PublicTypes()
        => CoreAssembly.GetTypes().Where(t => t.IsPublic);

    private static string PageNameFor(Type t)
        => t.FullName!.Replace('`', '-').ToLowerInvariant() + ".md";

    /// <summary>The generated page set equals the assembly's public type set, RED IN BOTH
    /// DIRECTIONS — the Device pin's twin over Core.</summary>
    [Fact]
    public void GeneratedReference_DocumentsExactlyThePublicTypes()
    {
        var expected = PublicTypes().Select(PageNameFor).ToList();

        var actual = Directory.GetFiles(_fixture.OutputDirectory, "*.md")
            .Select(Path.GetFileName)
            .Where(f => !string.Equals(f, "index.md", StringComparison.Ordinal))
            .Select(f => f!.ToLowerInvariant())
            .ToList();

        Assert.True(expected.Count > 0,
            "reflected ZERO public types out of BlazorNative.Core — the completeness pin "
            + "has no expectation to hold anything against.");
        Assert.True(actual.Count > 0,
            $"the generator wrote NO pages into {_fixture.OutputDirectory}.\n\n{_fixture.GeneratorLog}");

        var missing = expected.Except(actual, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var unexpected = actual.Except(expected, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && unexpected.Count == 0,
            "THE CORE REFERENCE DRIFTED FROM THE ASSEMBLY.\n\n"
            + $"  MISSING (the assembly publishes it, the reference does not document it — {missing.Count}):\n"
            + (missing.Count == 0 ? "    (none)\n" : string.Join("\n", missing.Select(f => $"    {f}")) + "\n")
            + $"  UNEXPECTED (the reference documents it, the assembly does not publish it — {unexpected.Count}):\n"
            + (unexpected.Count == 0 ? "    (none)\n" : string.Join("\n", unexpected.Select(f => $"    {f}")) + "\n")
            + $"\n(Assembly: {expected.Count} public types. Generated: {actual.Count} pages.)\n\n"
            + "Generator output:\n" + _fixture.GeneratorLog);
    }

    /// <summary>
    /// The contract types a Core consumer opens first each have a page — named, so the
    /// completeness pin above cannot be satisfied by an empty set with no missing members.
    /// </summary>
    [Fact]
    public void GeneratedReference_ContainsTheBridgeContractAndNavigation()
    {
        foreach (var t in new[]
                 {
                     typeof(IMobileBridge), typeof(INavigationManager), typeof(DevHostBridge),
                     typeof(GeolocationStatus), typeof(CameraStatus), typeof(PlatformKind),
                 })
        {
            string page = Path.Combine(_fixture.OutputDirectory, PageNameFor(t));
            Assert.True(File.Exists(page),
                $"the Core reference is missing a page for {t.Name} ({PageNameFor(t)}).\n\n"
                + _fixture.GeneratorLog);
        }
    }
}

/// <summary>Generates the <c>BlazorNative.Runtime</c> reference once for the whole class (#173).</summary>
public sealed class RuntimeReferenceFixture : ReferenceFixtureBase
{
    public RuntimeReferenceFixture() : base("Runtime") { }
}

/// <summary>
/// PIN — the Runtime reference documents EXACTLY the BROWSABLE public surface (#173).
/// Runtime is the one package that mixes tiers: two STABLE consumer types
/// (<c>BlazorNativeApp</c>, <c>BlazorNativePage</c>) beside twelve
/// <c>[EditorBrowsable(Never)]</c> interop types the C ABI / AOT exports force public
/// (<c>Exports</c>, <c>NativeShellBridge</c>, the wire structs…). The generator drops the
/// NOT-API pages (scripts/generate-reference.ps1 → Remove-NotApiPages, keyed on that same
/// attribute), so the reference is the browsable tier only.
///
/// <para>This pin therefore expects the page set to equal the public types MINUS the
/// <c>[EditorBrowsable(Never)]</c> ones, RED IN BOTH DIRECTIONS: a STABLE type without a
/// page means the reference fell behind; a NOT-API type WITH a page means the filter
/// stopped working and interop plumbing leaked into the consumer reference. Both the doc
/// pin and the generator read the one attribute, so this proves they agree.</para>
///
/// <para>In the <see cref="ReferenceGeneration"/> collection with the other reference
/// fixtures so their <c>dotnet tool restore</c>s cannot race on the NuGet cache.</para>
/// </summary>
[Collection(ReferenceGeneration.Name)]
public sealed class RuntimeReferenceDriftTests : IClassFixture<RuntimeReferenceFixture>
{
    private readonly RuntimeReferenceFixture _fixture;

    public RuntimeReferenceDriftTests(RuntimeReferenceFixture fixture) => _fixture = fixture;

    private static Assembly RuntimeAssembly => typeof(BlazorNativeApp).Assembly;

    private static bool IsNotApi(Type t)
        => t.GetCustomAttribute<EditorBrowsableAttribute>()?.State == EditorBrowsableState.Never;

    /// <summary>The BROWSABLE public types — the reference's expected set. Derived from the
    /// same <c>[EditorBrowsable(Never)]</c> mark the generator filters on, never a roster.</summary>
    private static IEnumerable<Type> BrowsablePublicTypes()
        => RuntimeAssembly.GetTypes().Where(t => t.IsPublic && !IsNotApi(t));

    private static IEnumerable<Type> NotApiPublicTypes()
        => RuntimeAssembly.GetTypes().Where(t => t.IsPublic && IsNotApi(t));

    private static string PageNameFor(Type t)
        => t.FullName!.Replace('`', '-').ToLowerInvariant() + ".md";

    /// <summary>The generated page set equals the BROWSABLE public type set, RED IN BOTH
    /// DIRECTIONS. MISSING = a STABLE type with no page. UNEXPECTED = a page the browsable
    /// set does not name — which, for Runtime, is exactly how a leaked NOT-API page reds.</summary>
    [Fact]
    public void GeneratedReference_DocumentsExactlyTheBrowsableSurface()
    {
        var expected = BrowsablePublicTypes().Select(PageNameFor).ToList();
        var notApi   = NotApiPublicTypes().Select(PageNameFor).ToList();

        var actual = Directory.GetFiles(_fixture.OutputDirectory, "*.md")
            .Select(Path.GetFileName)
            .Where(f => !string.Equals(f, "index.md", StringComparison.Ordinal))
            .Select(f => f!.ToLowerInvariant())
            .ToList();

        // NON-VACUITY on every set the assertions lean on — an empty browsable set (all
        // types accidentally NOT-API) or an empty NOT-API set (the filter guarding nothing)
        // would each let a broken generator pass.
        Assert.True(expected.Count > 0,
            "reflected ZERO browsable public types out of BlazorNative.Runtime — the STABLE "
            + "tier vanished, so the completeness pin holds nothing.");
        Assert.True(notApi.Count > 0,
            "reflected ZERO [EditorBrowsable(Never)] public types out of BlazorNative.Runtime — "
            + "the filter this pin exists to guard has nothing to drop, so the test is vacuous.");
        Assert.True(actual.Count > 0,
            $"the generator wrote NO pages into {_fixture.OutputDirectory}.\n\n{_fixture.GeneratorLog}");

        var missing = expected.Except(actual, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var unexpected = actual.Except(expected, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        // Of the unexpected pages, the ones that are NOT-API are the filter failing.
        var leakedNotApi = unexpected.Intersect(notApi, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && unexpected.Count == 0,
            "THE RUNTIME REFERENCE DRIFTED FROM THE BROWSABLE SURFACE.\n\n"
            + $"  MISSING (a STABLE type with no page — {missing.Count}):\n"
            + (missing.Count == 0 ? "    (none)\n" : string.Join("\n", missing.Select(f => $"    {f}")) + "\n")
            + $"  UNEXPECTED (a page the browsable set does not name — {unexpected.Count}):\n"
            + (unexpected.Count == 0 ? "    (none)\n" : string.Join("\n", unexpected.Select(f => $"    {f}")) + "\n")
            + $"    …of which LEAKED NOT-API (the [EditorBrowsable(Never)] filter failed — {leakedNotApi.Count}):\n"
            + (leakedNotApi.Count == 0 ? "      (none)\n" : string.Join("\n", leakedNotApi.Select(f => $"      {f}")) + "\n")
            + $"\n(Browsable public types: {expected.Count}. NOT-API filtered: {notApi.Count}. Generated pages: {actual.Count}.)\n\n"
            + "Generator output:\n" + _fixture.GeneratorLog);
    }

    /// <summary>
    /// The two STABLE types a Runtime consumer opens first each have a page, AND the interop
    /// types are ABSENT — the filter's positive and negative halves, named so neither can be
    /// satisfied vacuously.
    /// </summary>
    [Fact]
    public void GeneratedReference_KeepsTheStableTierAndDropsTheInterop()
    {
        foreach (var t in new[] { typeof(BlazorNativeApp), typeof(BlazorNativePage) })
        {
            string page = Path.Combine(_fixture.OutputDirectory, PageNameFor(t));
            Assert.True(File.Exists(page),
                $"the Runtime reference is missing a STABLE page for {t.Name} ({PageNameFor(t)}).\n\n"
                + _fixture.GeneratorLog);
        }

        foreach (var name in new[] { "Exports", "NativeShellBridge", "BlazorNativePatch" })
        {
            var t = RuntimeAssembly.GetType($"BlazorNative.Runtime.{name}")!;
            Assert.True(IsNotApi(t), $"{name} is expected to be [EditorBrowsable(Never)] NOT-API.");
            string page = Path.Combine(_fixture.OutputDirectory, PageNameFor(t));
            Assert.False(File.Exists(page),
                $"the Runtime reference LEAKED an interop page for {name} — the "
                + $"[EditorBrowsable(Never)] filter did not drop it.\n\n" + _fixture.GeneratorLog);
        }
    }
}

/// <summary>Generates the <c>BlazorNative.Http</c> reference once for the whole class (#173).</summary>
public sealed class HttpReferenceFixture : ReferenceFixtureBase
{
    public HttpReferenceFixture() : base("Http") { }
}

/// <summary>
/// PIN — the Http reference documents EXACTLY the package's public types, red in both
/// directions (#173). Http was the LAST consumer package added, and the only one blocked
/// UPSTREAM: <c>ZeroAlloc.Inject.Generator</c> emits a PUBLIC
/// <c>BlazorNativeHttpServicesServiceCollectionExtensions</c> with no XML doc and — before
/// v1.7.2 — no <c>#pragma warning disable 1591</c>, so CS1591 fired on generated code a
/// consumer cannot annotate (no per-file lever exists for it). v1.7.2 makes the generator
/// pragma-suppress 1591 in its own output, which is what let <c>BnEnforceDocCoverage</c> go
/// on here; the generated extension is a valid signature page, not a stub.
///
/// <para>In the <see cref="ReferenceGeneration"/> collection with the other reference
/// fixtures so their <c>dotnet tool restore</c>s cannot race on the NuGet cache.</para>
/// </summary>
[Collection(ReferenceGeneration.Name)]
public sealed class HttpReferenceDriftTests : IClassFixture<HttpReferenceFixture>
{
    private readonly HttpReferenceFixture _fixture;

    public HttpReferenceDriftTests(HttpReferenceFixture fixture) => _fixture = fixture;

    private static Assembly HttpAssembly => typeof(BlazorNative.Http.BridgeHttpHandler).Assembly;

    private static IEnumerable<Type> PublicTypes()
        => HttpAssembly.GetTypes().Where(t => t.IsPublic);

    private static string PageNameFor(Type t)
        => t.FullName!.Replace('`', '-').ToLowerInvariant() + ".md";

    /// <summary>The generated page set equals the assembly's public type set, RED IN BOTH
    /// DIRECTIONS — the Device/Core pin's twin over Http. Note the public set INCLUDES the
    /// ZeroAlloc-generated <c>BlazorNativeHttpServicesServiceCollectionExtensions</c> (in the
    /// <c>Microsoft.Extensions.DependencyInjection</c> namespace), so this also proves that
    /// generated public type gets a page rather than tripping the build on CS1591.</summary>
    [Fact]
    public void GeneratedReference_DocumentsExactlyThePublicTypes()
    {
        var expected = PublicTypes().Select(PageNameFor).ToList();

        var actual = Directory.GetFiles(_fixture.OutputDirectory, "*.md")
            .Select(Path.GetFileName)
            .Where(f => !string.Equals(f, "index.md", StringComparison.Ordinal))
            .Select(f => f!.ToLowerInvariant())
            .ToList();

        Assert.True(expected.Count > 0,
            "reflected ZERO public types out of BlazorNative.Http — the completeness pin "
            + "has no expectation to hold anything against.");
        Assert.True(actual.Count > 0,
            $"the generator wrote NO pages into {_fixture.OutputDirectory}.\n\n{_fixture.GeneratorLog}");

        var missing = expected.Except(actual, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var unexpected = actual.Except(expected, StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && unexpected.Count == 0,
            "THE HTTP REFERENCE DRIFTED FROM THE ASSEMBLY.\n\n"
            + $"  MISSING (the assembly publishes it, the reference does not document it — {missing.Count}):\n"
            + (missing.Count == 0 ? "    (none)\n" : string.Join("\n", missing.Select(f => $"    {f}")) + "\n")
            + $"  UNEXPECTED (the reference documents it, the assembly does not publish it — {unexpected.Count}):\n"
            + (unexpected.Count == 0 ? "    (none)\n" : string.Join("\n", unexpected.Select(f => $"    {f}")) + "\n")
            + $"\n(Assembly: {expected.Count} public types. Generated: {actual.Count} pages.)\n\n"
            + "Generator output:\n" + _fixture.GeneratorLog);
    }

    /// <summary>
    /// The types an Http consumer opens first each have a page — the handler, the hand-written
    /// registration surface, and the ZeroAlloc-generated primitive it wraps — named so the
    /// completeness pin cannot be satisfied by an empty set with no missing members.
    /// </summary>
    [Fact]
    public void GeneratedReference_ContainsTheHandlerAndRegistration()
    {
        var types = new[]
        {
            typeof(BlazorNative.Http.BridgeHttpHandler),
            typeof(BlazorNative.Http.ServiceCollectionExtensions),
            HttpAssembly.GetType(
                "Microsoft.Extensions.DependencyInjection.BlazorNativeHttpServicesServiceCollectionExtensions")!,
        };
        foreach (var t in types)
        {
            string page = Path.Combine(_fixture.OutputDirectory, PageNameFor(t));
            Assert.True(File.Exists(page),
                $"the Http reference is missing a page for {t.Name} ({PageNameFor(t)}).\n\n"
                + _fixture.GeneratorLog);
        }
    }
}
