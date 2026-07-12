#Requires -Version 7
<#
.SYNOPSIS
    BlazorNative — consumer smoke (Phase 4.5 Gate 2, M4 DoD #7 proof).

.DESCRIPTION
    Proves a BLANK consumer project mounts a Bn* component from the five NuGet
    packages ALONE — no ProjectReferences, no solution membership, no Runtime:

      1. pack   — the five packages → artifacts/packages (fresh).
      2. restore— samples/ConsumerSmoke into a TEMP package cache with
                  --no-cache; its nuget.config <clear/>s every inherited
                  source, leaving ONLY the local feed + nuget.org. Provenance
                  is then ASSERTED from the cache's .nupkg.metadata files:
                  BlazorNative.* must come from artifacts/packages, a
                  transitive (ZeroAlloc.Inject) from nuget.org.
      3. trip   — build with -p:AnalyzerTrip=true: the deliberate
                  parameterless HttpClient in Program.cs MUST surface BN0011,
                  proving the packaged analyzers are LIVE in a consumer build.
      4. clean  — build without the trip: MUST carry ZERO BN diagnostics.
      5. run    — execute the clean build; ConsumerSmoke asserts the patch set
                  (view/text/button creates, text content, exactly one click
                  AttachEvent) and exits 0 on PASS.

    Runs locally and as the ci.yml consumer-smoke step (windows job).

.EXAMPLE
    .\scripts\consumer-smoke.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$smokeDir = Join-Path $repoRoot "samples\ConsumerSmoke"
$feedDir  = Join-Path $repoRoot "artifacts\packages"
$version  = "1.2.0-phase-4.5"

function Write-Step([string]$text) { Write-Host "  ⟶  $text" -ForegroundColor Gray }
function Write-OK([string]$text)   { Write-Host "  ✓  $text" -ForegroundColor Green }
function Write-Fail([string]$text) { Write-Host "  ✗  $text" -ForegroundColor Red }

Write-Host ""
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  BlazorNative consumer smoke — packages-only mount (DoD #7)" -ForegroundColor White
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

# ── 1. Pack the five packages (fresh feed) ───────────────────────────────────
Write-Step "packing the five packages → artifacts/packages ..."
if (Test-Path $feedDir) { Remove-Item -Recurse -Force $feedDir }
$packLog = @()
foreach ($proj in @("Core", "Renderer", "Http", "Components", "Analyzers")) {
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
$nupkgs = @(Get-ChildItem $feedDir -Filter "*.nupkg")
if ($nupkgs.Count -ne 5) {
    Write-Fail "expected 5 .nupkg in artifacts/packages, found $($nupkgs.Count)"
    exit 1
}
Write-OK "five packages packed at $version, zero warnings"

# ── 2. Clean restore into a temp package cache ───────────────────────────────
$cache = Join-Path ([System.IO.Path]::GetTempPath()) "blazornative-smoke-cache-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
try {
    Write-Step "dotnet restore (temp cache: $cache, --no-cache; sources = nuget.config only) ..."
    $restoreLog = & dotnet restore $smokeDir --packages $cache --no-cache -tl:off -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $restoreLog | Select-Object -Last 25 | Out-Host
        Write-Fail "dotnet restore failed (exit $LASTEXITCODE)"
        exit 1
    }

    # Provenance proof: NuGet records the origin of every extracted package in
    # <cache>/<id>/<version>/.nupkg.metadata ("source"). The five must come
    # from the LOCAL feed; a transitive dep must come from nuget.org.
    foreach ($id in @("blazornative.core", "blazornative.renderer", "blazornative.http", "blazornative.components", "blazornative.analyzers")) {
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
    Write-OK "restore clean: BlazorNative.* from the local feed, transitives from nuget.org"

    # ── 3. Analyzer trip build (BN0011 MUST fire) ────────────────────────────
    Write-Step "trip build (-p:AnalyzerTrip=true) — expecting BN0011 ..."
    $tripLog = & dotnet build $smokeDir -c Release --no-restore -p:AnalyzerTrip=true -tl:off -nologo 2>&1
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
    $cleanLog = & dotnet build $smokeDir -c Release --no-restore -tl:off -nologo 2>&1
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
    Write-Step "running ConsumerSmoke (mount + patch-set assertions) ..."
    $dll = Join-Path $smokeDir "bin\Release\net10.0\ConsumerSmoke.dll"
    & dotnet $dll 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "ConsumerSmoke exited $LASTEXITCODE"
        exit 1
    }
    Write-OK "consumer smoke PASS — blank project mounts Bn* from packages alone (M4 DoD #7)"
    exit 0
}
finally {
    if (Test-Path $cache) { Remove-Item -Recurse -Force $cache -ErrorAction SilentlyContinue }
}
