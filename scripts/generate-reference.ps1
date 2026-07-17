#Requires -Version 7
<#
.SYNOPSIS
    BlazorNative — generate the component reference (Phase 8.4 Gate 2, M8 DoD #5).

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
    carries the dependency, and the components come back.

    THIS SCRIPT IS THE ONE HOME FOR THAT PIPELINE, and that is the whole reason it
    is a script rather than four lines inlined into docs.yml. Two callers run it:

      · .github/workflows/docs.yml — generates the reference it deploys
      · ComponentReferenceDriftTests (Pin 1, build-test) — the count that proves
        the reference is complete

    If the lane and the pin ran DIFFERENT pipelines, the pin would be green while
    the lane went blind — which is precisely the defect above, wearing a pin as a
    disguise. One home, two callers, so the pin guards the real thing.

    NOTE WHAT THIS SCRIPT DELIBERATELY DOES NOT DO: it does not assert that the
    output contains any components. That assertion is Pin 1's, and it lives there
    ALONE on purpose — a guard here would fire first and the mutation that proves
    Pin 1 (point this script at bin/) would prove this guard instead. The script
    generates; the count is somebody else's job. See
    tests/BlazorNative.Runtime.Tests/ComponentReferenceDriftTests.cs.

.PARAMETER OutputPath
    Where the markdown lands. Defaults to website/docs/components/reference —
    which is .gitignore'd, because the reference is GENERATED and never committed
    (8.4 design decision 3: the `///` next to the code is the one home).

.PARAMETER PublishPath
    Where Components is published first. Defaults under artifacts/ (gitignored).
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$PublishPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $OutputPath)  { $OutputPath  = Join-Path $repoRoot 'website/docs/components/reference' }
if (-not $PublishPath) { $PublishPath = Join-Path $repoRoot 'artifacts/docs-reference/publish' }

$project = Join-Path $repoRoot 'src/BlazorNative.Components/BlazorNative.Components.csproj'

Write-Host "==> generate-reference: restoring the pinned generator (.config/dotnet-tools.json)"
dotnet tool restore | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (exit $LASTEXITCODE)" }

# THE PUBLISH, NOT THE BUILD. This line is the entire point of the script; see
# the .DESCRIPTION above before changing it to something that looks equivalent.
Write-Host "==> generate-reference: publishing BlazorNative.Components (the dependency-complete output)"
dotnet publish $project -c Release -o $PublishPath --nologo -v minimal | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$assembly = Join-Path $PublishPath 'BlazorNative.Components.dll'
if (-not (Test-Path $assembly)) { throw "published assembly not found: $assembly" }

# A CLEAN OUTPUT DIRECTORY, every run. The reference is a pure function of the
# XML docs; a leftover page from a component that no longer exists would be a
# second copy that outlived its source — the one thing this site refuses.
if (Test-Path $OutputPath) { Remove-Item -Recurse -Force $OutputPath }
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

Write-Host "==> generate-reference: xmldoc2md -> $OutputPath"
# --platform docusaurus  : front matter + link rewriting for this site's shape
# --member-accessibility-level public
#     The tool's DEFAULT is `protected`, which documents the protected surface
#     ComponentBase hands every component — RendererInfo, Assets,
#     AssignedRenderMode. Those are ASP.NET Core's web-hosting concepts; this
#     framework renders to native widgets and has no such thing. Publishing them
#     on all 15 component pages would tell a stranger the opposite of the truth.
#     (15 = the concrete ComponentBase-derived types, measured; "21" was carried
#     here from an older count and was never true of this assembly — 8.4 review,
#     S1-3. Pin 1 holds the real number.)
#     `public` is the consumer's surface, which is what a consumer reference is.
dotnet xmldoc2md $assembly -o $OutputPath --platform docusaurus --member-accessibility-level public | Out-Host
if ($LASTEXITCODE -ne 0) { throw "xmldoc2md failed (exit $LASTEXITCODE)" }

$pages = @(Get-ChildItem -Path $OutputPath -Filter '*.md' -File)
Write-Host "==> generate-reference: $($pages.Count) markdown files in $OutputPath"
