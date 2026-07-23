using System.Diagnostics;
using System.Reflection;
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
