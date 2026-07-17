#Requires -Version 7
<#
.SYNOPSIS
    BlazorNative — consumer smoke (Phase 4.5 Gate 2, M4 DoD #7; extended by
    Phase 8.1 Gate 1, M8 DoD #2: six packages, nupkg-level purity, the first
    out-of-repo RegisterPages consumer).

.DESCRIPTION
    Proves a BLANK consumer project consumes BlazorNative from the SIX NuGet
    packages ALONE — no ProjectReferences, no solution membership:

      1.  pack        — the six packages → artifacts/packages (fresh), at the
                        ONE version parsed from src/Directory.Build.props
                        (the single version truth, 8.1 design decision 4).
                        Zero pack warnings; exactly 6 .nupkg + 5 .snupkg
                        (Analyzers embeds its pdb — no lib/, no snupkg); every
                        nupkg FILENAME carries the props version (the
                        version-drift tooth — a csproj <Version> override
                        reds here AND in PackageVersionPinTests); and symbols
                        are PAIRED, not merely counted (Phase 8.2 decision 4):
                        each of the five library nupkgs has its .snupkg
                        SIBLING and Analyzers has none — the counts alone are
                        blind to WHICH package owns the symbols, and the push
                        matches them by ADJACENCY.
      2.  interrogate — nupkg-level purity (8.1 design decision 6; the xunit
                        purity pin runs BEFORE pack in CI, so only this script
                        ever owns the packed artifacts). Per nupkg, unzipped:
                        type-level purity off the PE — a POSITIVE CONTROL first
                        (non-empty read + the package's sentinel type; Gate 1
                        review I-3: absence assertions over a blind scanner are
                        green for the wrong reason), then zero app-shaped names,
                        zero moved-roster types — nuspec truth (id+version,
                        dependency allow-list — no SampleApp, nothing outside
                        the six + known third parties; MIT expression; readme
                        entry + file; repository@commit = the SourceLink
                        assertion), inventory shape (five libs: exactly one
                        dll + its XML doc under lib/net10.0; Analyzers: NO
                        lib/, dll under analyzers/dotnet/cs).
      3.  restore     — samples/ConsumerSmoke into a TEMP package cache with
                        --no-cache and -p:BlazorNativeVersion=<props version>
                        (the csproj carries NO fallback — this script is the
                        entry point). Provenance ASSERTED from .nupkg.metadata:
                        the SIX from artifacts/packages, a transitive
                        (ZeroAlloc.Inject) from nuget.org.
      4.  trip/clean  — -p:AnalyzerTrip=true MUST surface BN0011 (the packaged
                        analyzers are LIVE); the clean build carries ZERO BN
                        diagnostics.
      5.  run         — ConsumerSmoke asserts the mount patch set AND the
                        RegisterPages laws (duplicate-route ArgumentException
                        naming the row; register-once) and exits 0 on PASS.

    Runs locally and as the ci.yml consumer-smoke step (windows job).

.EXAMPLE
    .\scripts\consumer-smoke.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$smokeDir = Join-Path $repoRoot "samples\ConsumerSmoke"
$feedDir  = Join-Path $repoRoot "artifacts\packages"

# The shipped set — must agree with PackagePurityTests.ShippedAssemblies, the
# src/ csproj enumeration, and ConsumerSmoke.csproj's references (8.1
# normative rule 2). Order: pack respects the dependency arrows.
$packages = @("Core", "Renderer", "Http", "Components", "Runtime", "Analyzers")

# The 16-row moved roster (PackagePurityTests.MovedTypeRoster verbatim) + the
# pattern net — the same sin, asserted at the PACKAGING layer.
$movedRoster = @(
    "BnDemo", "BnSettingsPage", "BnLayoutDemo", "BnScrollDemo", "BnImageDemo",
    "BnListDemo", "BnFormDemo", "BnModalDemo", "BnImagePolishDemo",
    "BnThemedPanel", "SpikeRazor",
    "HelloComponent", "CompositionProbe", "FocusProbe", "HostEventProbe", "ClipboardProbe"
)
$appShapedPattern = '(Demo$)|(Probe$)|(^SpikeRazor)'

# THE POSITIVE CONTROL (Gate 1 review, I-3). The type scan below is an ABSENCE
# assertion — "no app-shaped names in the packed dll" — and an absence assertion
# over a blind scanner is green for the wrong reason. That is not hypothetical
# here: Get-TypeNames' own comment records a wrapping bug that already made the
# whole scan report "1 type", and the reviewer proved the hole live by neutering
# the return to @() — all six packages reported "0 types clean" and the smoke
# exited 0. So each package names ONE type it MUST contain: a load-bearing type,
# read off the real packed surface, whose disappearance is either a scanner that
# stopped seeing or a package that stopped being itself. Both must be loud.
# (PackagePurityTests.TypeNamesOf states the house rule this enforces: "a pin
# that cannot see its subject must never pass vacuously.")
$sentinels = @{
    "Core"       = "IMobileBridge"          # the bridge abstraction Core exists for
    "Renderer"   = "NativeRenderer"         # the renderer itself
    "Http"       = "BridgeHttpHandler"      # the handler Http exists for
    "Components" = "BnView"                 # the component surface's base view
    "Runtime"    = "BlazorNativeApp"        # the 8.0 registration API the smoke consumes
    "Analyzers"  = "MobilePolicyAnalyzer"   # the analyzer that owns BN0011 (the trip tooth)
}

function Write-Step([string]$text) { Write-Host "  ⟶  $text" -ForegroundColor Gray }
function Write-OK([string]$text)   { Write-Host "  ✓  $text" -ForegroundColor Green }
function Write-Fail([string]$text) { Write-Host "  ✗  $text" -ForegroundColor Red }

Write-Host ""
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  BlazorNative consumer smoke — six packages, purity + mount (DoD #7 / M8 DoD #2)" -ForegroundColor White
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

# ── 0. The ONE version, parsed from src/Directory.Build.props ────────────────
$propsPath = Join-Path $repoRoot "src\Directory.Build.props"
if (-not (Test-Path $propsPath)) {
    Write-Fail "src/Directory.Build.props not found — it is the one version source (8.1 decision 4)"
    exit 1
}
$propsXml = [xml](Get-Content $propsPath -Raw)
$versionNodes = @($propsXml.Project.PropertyGroup | ForEach-Object { $_ } |
    Where-Object { $_.PSObject.Properties.Name -contains "Version" } |
    ForEach-Object { $_.Version })
if ($versionNodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($versionNodes[0])) {
    Write-Fail "expected exactly ONE non-empty <Version> in src/Directory.Build.props, found $($versionNodes.Count) — the single version truth is gone"
    exit 1
}
$version = $versionNodes[0]
Write-OK "version source: src/Directory.Build.props → $version"

# ── 1. Pack the six packages (fresh feed) ────────────────────────────────────
Write-Step "packing the six packages → artifacts/packages ..."
if (Test-Path $feedDir) { Remove-Item -Recurse -Force $feedDir }
$packLog = @()
foreach ($proj in $packages) {
    $packLog += & dotnet pack (Join-Path $repoRoot "src\BlazorNative.$proj") -c Release -o $feedDir -tl:off -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $packLog | Select-Object -Last 20 | Out-Host
        Write-Fail "dotnet pack BlazorNative.$proj failed (exit $LASTEXITCODE)"
        exit 1
    }
}
$packWarnings = @($packLog | Where-Object { "$_" -match '\bwarning\b' })
if ($packWarnings.Count -ne 0) {
    $packWarnings | Out-Host
    Write-Fail "pack emitted $($packWarnings.Count) warning(s) — the zero-warning bar applies"
    exit 1
}
$nupkgs  = @(Get-ChildItem $feedDir -Filter "*.nupkg")
$snupkgs = @(Get-ChildItem $feedDir -Filter "*.snupkg")
if ($nupkgs.Count -ne 6) {
    Write-Fail "expected 6 .nupkg in artifacts/packages, found $($nupkgs.Count)"
    exit 1
}
if ($snupkgs.Count -ne 5) {
    Write-Fail "expected 5 .snupkg (Analyzers embeds its pdb — no snupkg), found $($snupkgs.Count): $($snupkgs.Name -join ', ')"
    exit 1
}
# The version-drift tooth: every nupkg filename carries exactly the props
# version — a csproj that grows a <Version> override reds HERE (and in
# PackageVersionPinTests, earlier).
foreach ($proj in $packages) {
    $expected = "BlazorNative.$proj.$version.nupkg"
    if (-not (Test-Path (Join-Path $feedDir $expected))) {
        Write-Fail "expected '$expected' in the feed — a package was packed at a DIFFERENT version than src/Directory.Build.props ($version). Feed: $($nupkgs.Name -join ', ')"
        exit 1
    }
}

# ── THE PAIRING TOOTH (Phase 8.2, design decision 4) ─────────────────────────
# PAIRING, NOT COUNTING — and the difference is not academic. The two counts
# above (6 nupkg, 5 snupkg) are blind to WHICH package the symbols belong to:
# a feed holding Core's nupkg with no sibling, while Analyzers wrongly carries
# one, is 6 and 5 and passes both counts GREEN while BlazorNative.Core ships
# with no symbols at all. Proven by mutation, quoted in the commit.
#
# It matters to the RELEASE, which is why 8.2 found it: `dotnet nuget push`
# carries symbols BY ADJACENCY (an adjacent .snupkg rides its .nupkg — the
# CLI's own evidence is that `-n|--no-symbols` is an opt-OUT). The push
# enumerates *.nupkg and never names a symbol file, so "every library package
# has its snupkg sibling" is the assumption the whole symbol story rests on.
# Its home is HERE rather than in the release lane: it is a PACK-TRUTH claim, it
# belongs beside the counts it strengthens, and here it rides the REQUIRED lane
# on every PR instead of only on release-machinery PRs.
#
# ONE HONEST NOTE ON ITS REACH, so nobody over-claims it (8.2 Gate 1): with the
# six shaped as they are TODAY, the csproj route into that state is blocked —
# flipping Analyzers to IncludeSymbols=true trips NU5017 ("Cannot create a
# package that has no dependencies nor content") and pack FAILS before any
# count is read, exactly as the Analyzers csproj comment predicts. So today the
# counts and the shapes conspire to make broken pairing hard to reach, and this
# tooth is mostly a guard on the FUTURE: the day a seventh library package
# joins, the counts move to 7/6 and a Core whose symbols quietly stopped
# packing is invisible to them and visible only here.
$symbolOffenders = @()
foreach ($proj in $packages) {
    $hasSnupkg = Test-Path (Join-Path $feedDir "BlazorNative.$proj.$version.snupkg")
    # Analyzers is the ONE deliberate exception: no lib/, so a snupkg would be
    # empty (NU5017 territory) — its pdb travels embedded in the analyzer dll.
    if ($proj -eq "Analyzers") {
        if ($hasSnupkg) {
            $symbolOffenders += "BlazorNative.Analyzers has a .snupkg — it must NOT (no lib/ means an empty symbols package; its pdb is embedded via DebugType=embedded)"
        }
    }
    elseif (-not $hasSnupkg) {
        $symbolOffenders += "BlazorNative.$proj has NO .snupkg sibling — it would publish to nuget.org with no symbols at all"
    }
}
if ($symbolOffenders.Count -ne 0) {
    $symbolOffenders | ForEach-Object { Write-Fail "     $_" }
    Write-Fail "SYMBOL PAIRING broken. The 6/5 counts above are blind to this: symbols are matched to packages by ADJACENCY at push time, so a library package without its .snupkg sibling ships unsymbolicated and nothing else in this repo would notice. Feed: $(($nupkgs.Name + $snupkgs.Name | Sort-Object) -join ', ')"
    exit 1
}
Write-OK "six packages packed at $version, zero warnings, filenames agree with the props; symbols PAIRED (5 libs each with its .snupkg sibling; Analyzers embedded, no snupkg)"

# ── 1.5 Interrogate the nupkgs (8.1 decision 6 — packaging-layer purity) ─────
Write-Step "interrogating the six nupkgs (types off the PE, nuspec truth, inventory shape) ..."

function Get-TypeNames([string]$dllPath) {
    $stream = [System.IO.File]::OpenRead($dllPath)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            $names = [System.Collections.Generic.List[string]]::new()
            foreach ($h in $md.TypeDefinitions) {
                $names.Add($md.GetString($md.GetTypeDefinition($h).Name)) | Out-Null
            }
            # No leading comma: let the pipeline ENUMERATE the list so the
            # caller's @() collects one element per type name — a wrapped
            # single-List return would make every -match run against the
            # whole collection and report "1 type".
            return $names.ToArray()
        }
        finally { $pe.Dispose() }
    }
    finally { $stream.Dispose() }
}

$interrogateRoot = Join-Path ([System.IO.Path]::GetTempPath()) "blazornative-smoke-unzip-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
try {
    foreach ($proj in $packages) {
        $id = "BlazorNative.$proj"
        $pkgFile = Join-Path $feedDir "$id.$version.nupkg"
        $dest = Join-Path $interrogateRoot $id
        [System.IO.Compression.ZipFile]::ExtractToDirectory($pkgFile, $dest)

        # ── nuspec truth ─────────────────────────────────────────────────────
        $nuspecPath = Join-Path $dest "$id.nuspec"
        if (-not (Test-Path $nuspecPath)) {
            Write-Fail "$id`: nuspec '$id.nuspec' missing from the package"
            exit 1
        }
        $meta = ([xml](Get-Content $nuspecPath -Raw)).package.metadata

        if ($meta.id -cne $id) {
            Write-Fail "$id`: nuspec id is '$($meta.id)' — expected '$id'"
            exit 1
        }
        if ($meta.version -ne $version) {
            Write-Fail "$id`: nuspec version is '$($meta.version)' — expected the props version '$version'"
            exit 1
        }
        $license = $meta.SelectSingleNode("*[local-name()='license']")
        if ($null -eq $license -or $license.type -ne "expression" -or $license.InnerText -ne "MIT") {
            Write-Fail "$id`: nuspec must carry <license type=""expression"">MIT</license>, got '$(if ($license) { $license.OuterXml } else { '<absent>' })'"
            exit 1
        }
        if ($meta.readme -ne "README.md") {
            Write-Fail "$id`: nuspec readme entry is '$($meta.readme)' — expected 'README.md'"
            exit 1
        }
        if (-not (Test-Path (Join-Path $dest "README.md"))) {
            Write-Fail "$id`: README.md missing from the package ROOT (the readme entry points at nothing)"
            exit 1
        }
        # THE ICON (8.4 decision 6). Unlike the per-project README this is ONE
        # file shared by all six — the identity is the framework's, not a
        # package's — and it packs to the ROOT, which is exactly why it costs
        # the counts above and the lib/net10.0 inventory tooth below NOTHING.
        # Pack is already the pin (PackageIcon naming an unpacked file is
        # NU5046, and step 1's zero-pack-warning bar catches it); these two
        # lines mirror the readme's pair because the loop is already holding
        # the unzipped package open and the marginal cost is two lines.
        if ($meta.icon -ne "icon.png") {
            Write-Fail "$id`: nuspec icon entry is '$($meta.icon)' — expected 'icon.png'"
            exit 1
        }
        if (-not (Test-Path (Join-Path $dest "icon.png"))) {
            Write-Fail "$id`: icon.png missing from the package ROOT (the icon entry points at nothing)"
            exit 1
        }
        # SourceLink verification (8.1 decision 2): the SDK's implicit
        # Microsoft.SourceLink.GitHub must have stamped repository@commit.
        $repository = $meta.SelectSingleNode("*[local-name()='repository']")
        if ($null -eq $repository -or [string]::IsNullOrWhiteSpace($repository.GetAttribute("commit"))) {
            Write-Fail "$id`: nuspec <repository> lacks a commit attribute — SourceLink did not stamp the pack (the named fallback: add the explicit Microsoft.SourceLink.GitHub reference to src/Directory.Build.props)"
            exit 1
        }
        # Dependency allow-list: nothing outside the six + known third parties;
        # BlazorNative.SampleApp (or ANY unknown BlazorNative.*) is the sin.
        $depNodes = @($meta.SelectNodes(".//*[local-name()='dependency']"))
        foreach ($dep in $depNodes) {
            $depId = $dep.GetAttribute("id")
            $isShipped = $packages | Where-Object { "BlazorNative.$_" -eq $depId }
            if ($depId -like "BlazorNative.*" -and -not $isShipped) {
                Write-Fail "$id`: nuspec dependency '$depId' is outside the shipped six — app/sample types must never ride a package's dependency group"
                exit 1
            }
            if ($depId -notlike "BlazorNative.*" -and $depId -notlike "Microsoft.*" -and $depId -notlike "ZeroAlloc.*") {
                Write-Fail "$id`: nuspec dependency '$depId' is not a known third party (Microsoft.*/ZeroAlloc.*) — the allow-list is deliberate; extend it deliberately"
                exit 1
            }
        }

        # ── inventory shape + the shipped dll's path ─────────────────────────
        if ($proj -eq "Analyzers") {
            if (Test-Path (Join-Path $dest "lib")) {
                Write-Fail "$id`: an analyzer package must carry NO lib/ at all (IncludeBuildOutput=false) — found one"
                exit 1
            }
            $dll = Join-Path $dest "analyzers\dotnet\cs\$id.dll"
            if (-not (Test-Path $dll)) {
                Write-Fail "$id`: analyzer dll missing at analyzers/dotnet/cs/$id.dll"
                exit 1
            }
        }
        else {
            $libDir = Join-Path $dest "lib\net10.0"
            if (-not (Test-Path $libDir)) {
                Write-Fail "$id`: lib/net10.0/ missing from the package"
                exit 1
            }
            $libFiles = @(Get-ChildItem $libDir -File | Sort-Object Name)
            $expectedLib = @("$id.dll", "$id.xml")
            if (Compare-Object @($libFiles.Name) $expectedLib) {
                Write-Fail "$id`: lib/net10.0 must carry exactly one assembly + its XML doc ($($expectedLib -join ', ')), found: $($libFiles.Name -join ', ')"
                exit 1
            }
            $dll = Join-Path $libDir "$id.dll"
        }

        # ── type-level purity, off the PE (no loading) ───────────────────────
        $typeNames = @(Get-TypeNames $dll)

        # The positive control FIRST — the absence assertions below are only
        # worth their exit code if the scanner can see the surface at all.
        if ($typeNames.Count -eq 0) {
            Write-Fail "$id`: the type scan read ZERO types out of $([System.IO.Path]::GetFileName($dll)) — every purity assertion below would pass VACUOUSLY over a blind scanner. This is Get-TypeNames failing to see its subject, not a clean package (Gate 1 review, I-3)."
            exit 1
        }
        $sentinel = $sentinels[$proj]
        if ($typeNames -cnotcontains $sentinel) {
            Write-Fail "$id`: the sentinel type '$sentinel' is NOT among the $($typeNames.Count) types the scan read from $([System.IO.Path]::GetFileName($dll)) — either the scanner is misreading the surface (the purity assertions below are then vacuous) or the package genuinely lost a load-bearing type. Both are stop-and-analyze; do not 'fix' this by changing the sentinel without reading the dll (Gate 1 review, I-3)."
            exit 1
        }

        $offenders = @($typeNames | Where-Object { $_ -match $appShapedPattern })
        $offenders += @($typeNames | Where-Object { $movedRoster -ccontains $_ })
        if ($offenders.Count -ne 0) {
            Write-Fail "$id`: app-shaped/moved types INSIDE the packed dll ($([System.IO.Path]::GetFileName($dll))): $((@($offenders | Select-Object -Unique)) -join ', ') — shipped packages carry no app types (8.0 rule, 8.1 packaging tooth)"
            exit 1
        }

        Write-Host "     $id $version — nuspec ✓ (MIT, readme, repository@commit $($repository.GetAttribute('commit').Substring(0,8))…), inventory ✓, $($typeNames.Count) types clean (sentinel $sentinel ✓)" -ForegroundColor DarkGray
    }
    Write-OK "nupkg interrogation clean: purity, nuspec truth, and inventory shape on all six"
}
finally {
    if (Test-Path $interrogateRoot) { Remove-Item -Recurse -Force $interrogateRoot -ErrorAction SilentlyContinue }
}

# ── 2. Clean restore into a temp package cache ───────────────────────────────
$cache = Join-Path ([System.IO.Path]::GetTempPath()) "blazornative-smoke-cache-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
try {
    Write-Step "dotnet restore (temp cache: $cache, --no-cache; sources = nuget.config only) ..."
    $restoreLog = & dotnet restore $smokeDir --packages $cache --no-cache -p:BlazorNativeVersion=$version -tl:off -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $restoreLog | Select-Object -Last 25 | Out-Host
        Write-Fail "dotnet restore failed (exit $LASTEXITCODE)"
        exit 1
    }

    # Provenance proof: NuGet records the origin of every extracted package in
    # <cache>/<id>/<version>/.nupkg.metadata ("source"). The six must come
    # from the LOCAL feed; a transitive dep must come from nuget.org.
    foreach ($proj in $packages) {
        $id = "blazornative.$($proj.ToLowerInvariant())"
        $meta = Get-Content (Join-Path $cache "$id\$version\.nupkg.metadata") -Raw | ConvertFrom-Json
        if ($meta.source -notlike "*artifacts*packages*") {
            Write-Fail "$id restored from '$($meta.source)' — expected the local artifacts/packages feed"
            exit 1
        }
        Write-Host "     $id $version ← $($meta.source)" -ForegroundColor DarkGray
    }
    $transitiveDir = Join-Path $cache "zeroalloc.inject"
    $transitiveVer = (Get-ChildItem $transitiveDir -Directory | Select-Object -First 1).Name
    $transitiveMeta = Get-Content (Join-Path $transitiveDir "$transitiveVer\.nupkg.metadata") -Raw | ConvertFrom-Json
    if ($transitiveMeta.source -notlike "*api.nuget.org*") {
        Write-Fail "zeroalloc.inject restored from '$($transitiveMeta.source)' — expected nuget.org"
        exit 1
    }
    Write-Host "     zeroalloc.inject $transitiveVer ← $($transitiveMeta.source)" -ForegroundColor DarkGray
    Write-OK "restore clean: BlazorNative.* ×6 from the local feed, transitives from nuget.org"

    # ── 3. Analyzer trip build (BN0011 MUST fire) ────────────────────────────
    Write-Step "trip build (-p:AnalyzerTrip=true) — expecting BN0011 ..."
    $tripLog = & dotnet build $smokeDir -c Release --no-restore -p:AnalyzerTrip=true -p:BlazorNativeVersion=$version -tl:off -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $tripLog | Select-Object -Last 25 | Out-Host
        Write-Fail "trip build failed (exit $LASTEXITCODE) — it must BUILD (warning, not error)"
        exit 1
    }
    $bn0011 = @($tripLog | Where-Object { "$_" -match 'warning BN0011' })
    if ($bn0011.Count -eq 0) {
        $tripLog | Select-Object -Last 25 | Out-Host
        Write-Fail "BN0011 did NOT appear in the trip build — the analyzers package is not live"
        exit 1
    }
    Write-Host "     $($bn0011[0].ToString().Trim())" -ForegroundColor DarkGray
    Write-OK "analyzers package LIVE: BN0011 surfaced on the deliberate parameterless HttpClient"

    # ── 4. Clean build (ZERO BN diagnostics) ─────────────────────────────────
    Write-Step "clean build (no trip) — expecting zero BN diagnostics ..."
    $cleanLog = & dotnet build $smokeDir -c Release --no-restore -p:BlazorNativeVersion=$version -tl:off -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $cleanLog | Select-Object -Last 25 | Out-Host
        Write-Fail "clean build failed (exit $LASTEXITCODE)"
        exit 1
    }
    $bnDiags = @($cleanLog | Where-Object { "$_" -match '\bBN\d{4}\b' })
    if ($bnDiags.Count -ne 0) {
        $bnDiags | Out-Host
        Write-Fail "clean build carries $($bnDiags.Count) BN diagnostic(s) — expected zero"
        exit 1
    }
    Write-OK "clean build: zero BN diagnostics"

    # ── 5. Run the smoke ─────────────────────────────────────────────────────
    Write-Step "running ConsumerSmoke (mount + patch-set + RegisterPages assertions) ..."
    $dll = Join-Path $smokeDir "bin\Release\net10.0\ConsumerSmoke.dll"
    & dotnet $dll 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "ConsumerSmoke exited $LASTEXITCODE"
        exit 1
    }
    Write-OK "consumer smoke PASS — six packages: purity interrogated, mount + RegisterPages from packages alone (M4 DoD #7 / M8 DoD #2)"
    exit 0
}
finally {
    if (Test-Path $cache) { Remove-Item -Recurse -Force $cache -ErrorAction SilentlyContinue }
}
