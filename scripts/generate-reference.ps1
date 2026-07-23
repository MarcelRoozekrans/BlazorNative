#Requires -Version 7
<#
.SYNOPSIS
    BlazorNative — generate the API reference (Phase 8.4 Gate 2 / M11 #173).

.DESCRIPTION
    THE SENTENCE THIS SCRIPT EXISTS FOR: xmldoc2md reports `Generation: 10
    succeeded, 0 failed` and exit 0 while emitting ZERO components, and the only
    difference between that run and a correct one is WHICH DIRECTORY you point it
    at.

    Run against `src/BlazorNative.Components/bin/Release/net10.0/`:

        Generation: 10 succeeded, 0 failed          <- exit 0. Ten types. No BnView.

    Run against a `dotnet publish` output:

        Generation: 26 succeeded, 0 failed          <- exit 0. Every component.

    Same tool, same arguments, same reassuring green. The cause sits next to the
    assembly: `bin/` holds only BlazorNative.Components.dll + BlazorNative.Core.dll,
    so `Microsoft.AspNetCore.Components.dll` is absent, so ComponentBase does not
    resolve, so EVERY type deriving from it is dropped SILENTLY. A publish output
    carries the dependency, and the components come back. THE SAME TRAP APPLIES TO
    EVERY PACKAGE — Runtime's Exports resolve types out of Core, Device's façades
    out of DI abstractions — so each package is PUBLISHED before it is generated,
    never read from a bare bin/.

    #173 WIDENED THIS BEYOND COMPONENTS. The generated reference used to cover one
    of the seven shipped packages; a consumer wanting the five device façades or
    `AddBlazorNativeDevice()` found nothing. The script now generates TWO packages,
    each from its own dependency-complete publish:

        Components  -> website/docs/components/reference   (historical home)
        Device      -> website/docs/reference/device        (the five façades + AddBlazorNativeDevice)

    A PACKAGE IS GENERATED ONLY WHEN ITS DOCS ALLOW IT (#173's coupling, made
    literal: a page for an undocumented member is a blank stub, so a package is
    added ONLY once `BnEnforceDocCoverage` can be turned on for it with zero CS1591).
    Device was the one consumer-facing package whose public surface was already
    fully `///`-documented (after this change added the five interface + one
    extension type-level summaries the members already had). The other three
    consumer-facing packages are DEFERRED, each for a concrete reason recorded at its
    csproj switch:

      · Runtime — its STABLE types (BlazorNativeApp, BlazorNativePage) are documented,
        but the package also exports ~12 NOT-API interop types (PatchProtocolNative,
        BridgeProtocolNative, NativeShellBridge, Exports…) with ~98 undocumented
        public members. CS1591 is all-or-nothing per package.
        (src/BlazorNative.Runtime/BlazorNative.Runtime.csproj)
      · Core — IMobileBridge (27 members), DevHostBridge and six wire-mirrored
        enums/records are documented only as `//` block comments, not `///` XML.
        (src/BlazorNative.Core/BlazorNative.Core.csproj)
      · Http — its hand-written surface is documentable, but ZeroAlloc.Inject.Generator
        emits a PUBLIC `AddBlazorNativeHttpServices` extension with no XML doc and no
        `#pragma warning disable 1591`, so CS1591 cannot be cleanly enforced.
        (src/BlazorNative.Http/BlazorNative.Http.csproj)

    When a gap is closed the package's `BnEnforceDocCoverage` flips on and it is added
    to the manifest below — generation and enforcement always advance together.

    Renderer and Analyzers are DELIBERATELY EXCLUDED (not merely deferred).
    Renderer is internal render plumbing a consumer never injects or calls;
    Analyzers ships with PrivateAssets=all (no compile-time reference reaches a
    consumer) and its real contract is the seven BN00xx diagnostic IDs, documented
    on docs/analyzers.md. Generating either would emit pages for a surface nobody
    binds to.

    THE HONEST COUPLING (#173): a page for an undocumented member is a blank stub.
    Every generated package therefore has `BnEnforceDocCoverage` ON in its csproj,
    so CS1591 is an ERROR and a missing `///` stops the build long before it can
    reach a reference page. Generation and enforcement advance together, by design.

    THIS SCRIPT IS THE ONE HOME FOR THAT PIPELINE, and that is the whole reason it
    is a script rather than a handful of lines inlined into docs.yml. Two callers
    run it:

      · .github/workflows/docs.yml — generates the reference it deploys
      · the drift pins (build-test) — the counts that prove the reference is
        complete: ComponentReferenceDriftTests (Components) and
        ReferenceDriftTests (Runtime/Core/Device/Http).

    If the lane and the pins ran DIFFERENT pipelines, the pins would be green while
    the lane went blind — which is precisely the defect above, wearing a pin as a
    disguise. One home, many callers, so the pins guard the real thing.

    NOTE WHAT THIS SCRIPT DELIBERATELY DOES NOT DO: it does not assert that any
    output contains types. That assertion is the pins', and it lives there ALONE on
    purpose — a guard here would fire first and the mutation that proves a pin
    (point this script at bin/) would prove this guard instead. The script
    generates; the count is somebody else's job.

.PARAMETER Package
    Which package(s) to generate. Omit for ALL of them (what docs.yml's `prebuild`
    does). A pin passes a single name so it publishes only the assembly it asserts.

.PARAMETER OutputPath
    Override the output directory for a SINGLE selected package (a pin points this
    at a temp dir). Ambiguous — and rejected — with more than one package.

.PARAMETER ReferenceRoot
    Base directory for the non-Components packages. Defaults to
    website/docs/reference (.gitignore'd — the reference is GENERATED and never
    committed; the `///` next to the code is the one home, 8.4 decision 3).

.PARAMETER PublishPath
    Where the packages are published first. Defaults under artifacts/ (gitignored).
#>
[CmdletBinding()]
param(
    [string[]]$Package,
    [string]$OutputPath,
    [string]$ReferenceRoot,
    [string]$PublishPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $ReferenceRoot) { $ReferenceRoot = Join-Path $repoRoot 'website/docs/reference' }
if (-not $PublishPath)   { $PublishPath   = Join-Path $repoRoot 'artifacts/docs-reference/publish' }

# THE MANIFEST — the one place the generated set is declared. Each package is
# PUBLISHED (dependency-complete) then handed to xmldoc2md. `Default` is where the
# page set lands on a full run; the Components home is historical (8.4) and the
# others sit under ReferenceRoot. The drift pins reflect this exact set.
$manifest = [ordered]@{
    Components = @{
        Csproj  = 'src/BlazorNative.Components/BlazorNative.Components.csproj'
        Dll     = 'BlazorNative.Components.dll'
        Default = { Join-Path $repoRoot 'website/docs/components/reference' }
    }
    Device = @{
        Csproj  = 'src/BlazorNative.Device/BlazorNative.Device.csproj'
        Dll     = 'BlazorNative.Device.dll'
        Default = { Join-Path $ReferenceRoot 'device' }
    }
}

if (-not $Package -or $Package.Count -eq 0) { $Package = @($manifest.Keys) }

foreach ($name in $Package) {
    if (-not $manifest.Contains($name)) {
        throw "unknown package '$name' — known packages: $($manifest.Keys -join ', ')"
    }
}
if ($OutputPath -and $Package.Count -ne 1) {
    throw "-OutputPath overrides a single package's directory, but $($Package.Count) packages were selected. Pass one -Package, or drop -OutputPath and use -ReferenceRoot."
}

Write-Host "==> generate-reference: restoring the pinned generator (.config/dotnet-tools.json)"
dotnet tool restore | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (exit $LASTEXITCODE)" }

foreach ($name in $Package) {
    $entry   = $manifest[$name]
    $project = Join-Path $repoRoot $entry.Csproj
    $outDir  = if ($OutputPath) { $OutputPath } else { & $entry.Default }
    $pubDir  = Join-Path $PublishPath $name

    # THE PUBLISH, NOT THE BUILD. This is the entire point of the script; see the
    # .DESCRIPTION above before changing it to something that looks equivalent.
    # Each package gets its OWN publish dir so a transitively-referenced type from a
    # sibling package is present when xmldoc2md resolves the surface.
    Write-Host "==> generate-reference [$name]: publishing (the dependency-complete output)"
    dotnet publish $project -c Release -o $pubDir --nologo -v minimal | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $name (exit $LASTEXITCODE)" }

    $assembly = Join-Path $pubDir $entry.Dll
    if (-not (Test-Path $assembly)) { throw "published assembly not found: $assembly" }

    # A CLEAN OUTPUT DIRECTORY, every run. The reference is a pure function of the
    # XML docs; a leftover page from a type that no longer exists would be a second
    # copy that outlived its source — the one thing this site refuses.
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    Write-Host "==> generate-reference [$name]: xmldoc2md -> $outDir"
    # --platform docusaurus  : front matter + link rewriting for this site's shape
    # --member-accessibility-level public
    #     The tool's DEFAULT is `protected`, which documents the protected surface
    #     ComponentBase hands every component — RendererInfo, Assets,
    #     AssignedRenderMode. Those are ASP.NET Core's web-hosting concepts; this
    #     framework renders to native widgets and has no such thing. `public` is the
    #     consumer's surface, which is what a consumer reference is.
    dotnet xmldoc2md $assembly -o $outDir --platform docusaurus --member-accessibility-level public | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "xmldoc2md failed for $name (exit $LASTEXITCODE)" }

    $pages = @(Get-ChildItem -Path $outDir -Filter '*.md' -File)
    Write-Host "==> generate-reference [$name]: $($pages.Count) markdown files in $outDir"
}
