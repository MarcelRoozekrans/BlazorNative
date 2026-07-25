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
    `AddBlazorNativeApp()` found nothing. The script now generates the consumer-facing
    packages, each from its own dependency-complete publish:

        Components  -> website/docs/components/reference   (historical home)
        Device      -> website/docs/reference/device        (the five façades + AddBlazorNativeDevice)
        Core        -> website/docs/reference/core          (IMobileBridge, INavigationManager, wire enums)
        Runtime     -> website/docs/reference/runtime        (BlazorNativeApp, BlazorNativePage — STABLE tier only)
        Http        -> website/docs/reference/http           (BridgeHttpHandler + the AddBlazorNativeHttp extensions)

    A PACKAGE IS GENERATED ONLY WHEN ITS DOCS ALLOW IT (#173's coupling, made
    literal: a page for an undocumented member is a blank stub, so a package is
    added ONLY once `BnEnforceDocCoverage` can be turned on for it with zero CS1591).
    Device was the one consumer-facing package whose public surface was already
    fully `///`-documented; Core, Runtime and Http followed as #173 closed their gaps —
    so EVERY consumer-facing package is now generated. The reasons are recorded at each
    csproj switch:

      · Runtime — DONE (#173). Its STABLE types (BlazorNativeApp, BlazorNativePage) are
        documented; the ~12 NOT-API interop types (PatchProtocolNative, BridgeProtocolNative,
        NativeShellBridge, Exports…, ~98 undocumented public members) each carry
        [EditorBrowsable(Never)] and opt out of CS1591 with a file-level pragma, while the
        manifest's FilterNotApi drops their pages (Remove-NotApiPages) so the reference is
        the browsable STABLE tier only. BnEnforceDocCoverage is ON.
        (src/BlazorNative.Runtime/BlazorNative.Runtime.csproj)
      · Core — DONE (#173). IMobileBridge's 27 members, DevHostBridge and the
        wire-mirrored enums/records were converted from `//` block comments to `///`
        XML (enum values lifted from their block-comment tables, DevHostBridge's
        implementations via <inheritdoc/>); BnEnforceDocCoverage is ON and generation
        + the ReferenceDriftTests fixture cover it.
        (src/BlazorNative.Core/BlazorNative.Core.csproj)
      · Http — DONE (#173), via an UPSTREAM fix. ZeroAlloc.Inject.Generator emitted a
        PUBLIC `AddBlazorNativeHttpServices` with no XML doc and no pragma, and CS1591
        fired on generated code a consumer cannot annotate (no per-file lever exists for
        it). ZeroAlloc.Inject is first-party, so v1.7.2 makes the generator emit
        `#pragma warning disable 1591` in its own output; the hand-written surface is
        `///`-documented and BnEnforceDocCoverage is ON. The generated extension renders
        as a valid signature page, not a stub.
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
    Core = @{
        Csproj  = 'src/BlazorNative.Core/BlazorNative.Core.csproj'
        Dll     = 'BlazorNative.Core.dll'
        Default = { Join-Path $ReferenceRoot 'core' }
    }
    Runtime = @{
        Csproj  = 'src/BlazorNative.Runtime/BlazorNative.Runtime.csproj'
        Dll     = 'BlazorNative.Runtime.dll'
        Default = { Join-Path $ReferenceRoot 'runtime' }
        # Runtime is the one package that mixes tiers: the two STABLE consumer types
        # (BlazorNativeApp, BlazorNativePage) plus twelve [EditorBrowsable(Never)]
        # interop types the C ABI forces public. Filter drops the interop pages so the
        # reference is the browsable surface only (see Remove-NotApiPages above).
        FilterNotApi = $true
    }
    Http = @{
        Csproj  = 'src/BlazorNative.Http/BlazorNative.Http.csproj'
        Dll     = 'BlazorNative.Http.dll'
        Default = { Join-Path $ReferenceRoot 'http' }
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

# ── github-slugger normalization (the #196 cross-PR bug) ────────────────────────
# xmldoc2md writes in-page / cross-page links as `](target#fragment)`, GUESSING the
# heading's anchor id. Docusaurus, however, forms the real heading id with
# github-slugger, and the two disagree the moment a signature carries punctuation
# github-slugger STRIPS but xmldoc2md RETAINS: a nullable `?`, a generic `<T>`, an
# array `[]`, a tuple. When they disagree the link points at an id no heading has,
# and `onBrokenAnchors: 'throw'` (website/docusaurus.config — deliberately a guard)
# fails the site build. It first bit when #196 changed
# ICamera.CapturePhotoAsync(CaptureOptions options) to `CaptureOptions? options`:
# xmldoc2md emitted `#capturephotoasynccaptureoptions?-cancellationtoken`, Docusaurus
# slugged the heading to `…captureoptions-cancellationtoken`, and they missed on the
# lone `?`. Components never tripped it because none of its signatures carried one.
#
# THE FIX IS IN THE GENERATED OUTPUT, NOT THE SITE CONFIG. We re-slug every link
# fragment through github-slugger's own rules rather than special-casing `?`, so the
# next punctuation (generics as coverage widens) is already handled.
function Convert-ToDocusaurusSlug([string]$fragment) {
    # github-slugger semantics as Docusaurus applies them to heading TEXT: lowercase,
    # whitespace -> '-', then drop everything that is not a word char or a hyphen.
    # xmldoc2md already lowercases and hyphenates the parameter separators the same
    # way, so removing the retained punctuation is the whole difference.
    $s = $fragment -replace '\s+', '-'
    $s = $s -replace '[^\w-]', ''
    return $s.ToLowerInvariant()
}

function Repair-DocusaurusAnchors([string]$directory) {
    # Only the FRAGMENT of a markdown link is rewritten (`](url#FRAGMENT)`), never the
    # url or the visible text. Idempotent: a fragment already equal to its slug is
    # unchanged. `pre` stops at the first '#'; `frag` runs to the closing ')'.
    $anchor = [regex]'\]\((?<pre>[^()\s#]*#)(?<frag>[^()\s]+)\)'
    $evaluator = {
        param($m)
        '](' + $m.Groups['pre'].Value + (Convert-ToDocusaurusSlug $m.Groups['frag'].Value) + ')'
    }
    $changed = 0
    foreach ($file in Get-ChildItem -Path $directory -Filter '*.md' -File) {
        $text  = Get-Content -Raw -LiteralPath $file.FullName
        $fixed = $anchor.Replace($text, $evaluator)
        if ($fixed -ne $text) {
            Set-Content -NoNewline -LiteralPath $file.FullName -Value $fixed
            $changed++
        }
    }
    Write-Host "==> generate-reference: normalized heading anchors in $changed file(s) under $directory"
}

# ── the [EditorBrowsable(Never)] filter (#173, Runtime) ─────────────────────────
# xmldoc2md emits ONE page per PUBLIC type, and has no way to exclude any — but a
# package like Runtime is public-by-necessity, not public-as-API: two consumer types
# (BlazorNativeApp, BlazorNativePage) sit beside twelve interop types (Exports, the
# wire structs, NativeShellBridge…) that are public only because the C ABI and AOT
# exports require it. Every one of those twelve carries [EditorBrowsable(Never)] — the
# same mark that hides them from IntelliSense and the same tier line the API baseline
# draws. So the reference documents the BROWSABLE public surface: after generation we
# drop the page (and index link) of every [EditorBrowsable(Never)] type. This is a
# RULE keyed on an attribute, not a hand roster — the failure class the whole site
# refuses — and it is the browsability twin of --member-accessibility-level public.
#
# The types are read via System.Reflection.Metadata (PE metadata, no assembly LOAD):
# the generator runs under pwsh, whose runtime (.NET 9 on CI) CANNOT LoadFrom a net10
# assembly, but CAN read its metadata. `Never` is EditorBrowsableState value 1, encoded
# in the attribute blob right after the 2-byte prolog as a little-endian Int32.
function Get-EditorBrowsableNeverType([string]$assembly) {
    Add-Type -AssemblyName System.Reflection.Metadata -ErrorAction SilentlyContinue
    $stream = [System.IO.File]::OpenRead($assembly)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        $mr = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $result = [System.Collections.Generic.List[string]]::new()
        foreach ($th in $mr.TypeDefinitions) {
            $td = $mr.GetTypeDefinition($th)
            if (($td.Attributes -band [System.Reflection.TypeAttributes]::Public) -eq 0) { continue }
            foreach ($cah in $td.GetCustomAttributes()) {
                $ca = $mr.GetCustomAttribute($cah)
                if ($ca.Constructor.Kind -ne [System.Reflection.Metadata.HandleKind]::MemberReference) { continue }
                $mref   = $mr.GetMemberReference([System.Reflection.Metadata.MemberReferenceHandle]$ca.Constructor)
                if ($mref.Parent.Kind -ne [System.Reflection.Metadata.HandleKind]::TypeReference) { continue }
                $tr     = $mr.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$mref.Parent)
                if ($mr.GetString($tr.Name) -ne 'EditorBrowsableAttribute') { continue }
                $blob = $mr.GetBlobBytes($ca.Value)
                # prolog(2) + Int32 LE; Never == 1
                if ($blob.Length -ge 6 -and $blob[2] -eq 1 -and $blob[3] -eq 0 -and $blob[4] -eq 0 -and $blob[5] -eq 0) {
                    $ns = $mr.GetString($td.Namespace); $nm = $mr.GetString($td.Name)
                    $result.Add($(if ($ns) { "$ns.$nm" } else { $nm }))
                }
            }
        }
        return $result
    } finally { $stream.Dispose() }
}

function Remove-NotApiPages([string]$directory, [string]$assembly) {
    $never = Get-EditorBrowsableNeverType $assembly
    if ($never.Count -eq 0) {
        Write-Host "==> generate-reference: no [EditorBrowsable(Never)] types in $assembly — nothing filtered"
        return
    }
    # xmldoc2md names a page `<fulltypename lowercased>.md`. Delete each NOT-API page…
    $dropped = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($t in $never) {
        $page = ($t.ToLowerInvariant()) + '.md'
        $path = Join-Path $directory $page
        if (Test-Path $path) { Remove-Item -Force $path; [void]$dropped.Add($page) }
    }
    # …and prune its link line from index.md (a `[Name](./<page>)` line). A markdown
    # blank line follows each link; drop it too so no double-gap is left behind.
    $indexPath = Join-Path $directory 'index.md'
    if (Test-Path $indexPath) {
        $lines = Get-Content -LiteralPath $indexPath
        $kept  = [System.Collections.Generic.List[string]]::new()
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $isDroppedLink = $false
            if ($line -match '\]\(\./([^()\s]+\.md)\)') {
                if ($dropped.Contains($Matches[1])) { $isDroppedLink = $true }
            }
            if ($isDroppedLink) {
                # also swallow a single trailing blank line that separated the links
                if ($i + 1 -lt $lines.Count -and $lines[$i + 1] -eq '') { $i++ }
                continue
            }
            $kept.Add($line)
        }
        Set-Content -LiteralPath $indexPath -Value $kept
    }
    Write-Host "==> generate-reference: filtered $($dropped.Count) [EditorBrowsable(Never)] page(s) from $directory"
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

    # Rewrite xmldoc2md's guessed anchors onto the slugs Docusaurus actually emits,
    # before onBrokenAnchors:'throw' sees them (see the functions above / #196).
    Repair-DocusaurusAnchors $outDir

    # Drop [EditorBrowsable(Never)] pages so the reference is the browsable surface
    # (Runtime only — see the manifest's FilterNotApi and Remove-NotApiPages above).
    # `.Contains` first: Set-StrictMode -Version Latest THROWS on `$entry.FilterNotApi`
    # for the entries that do not carry the key, so the presence check cannot be skipped.
    if ($entry.Contains('FilterNotApi') -and $entry.FilterNotApi) { Remove-NotApiPages $outDir $assembly }

    $pages = @(Get-ChildItem -Path $outDir -Filter '*.md' -File)
    Write-Host "==> generate-reference [$name]: $($pages.Count) markdown files in $outDir"
}
