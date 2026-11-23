<#
.SYNOPSIS
  Phase 19.2 interleaver: drives android-collect.ps1 or ios-collect.ps1 for HEAD and the v0.15.0 baseline,
  alternating the two sides one run at a time, on one device. perf.yml calls it.

.DESCRIPTION
  Phases, in the order given by -Phases (comma-separated):
    q1     the collector's q1 kind for head, then v015.
    boot   runs 0..Runs, one collector call per side per run, with -Run <k>. Run 0 is each side's warm-up.
           The side that goes first alternates by run, so neither side always follows the other.
    click  blocks 0..Blocks of -Clicks serial clicks each, one collector call per side per block, alternating the
           same way. Block 0 is the warm-up. Blocks = ceil(Runs / Clicks) + 1: enough for Runs samples per side
           after the warm-up, plus one spare block so that a single dropped sample (the parser allows up to 5%)
           does not take a side below --min-runs.

  Each side's run numbers are unique per kind, so the collectors' refusal to overwrite never fires, and the parser
  sees boot-0..boot-Runs and click-0..click-Blocks per side.

  Isolation: logcat is not filtered by pid, and the simulator log window is not filtered by process, so before every
  collector call the OTHER side's app (and on Android its test package) is stopped. The two apps never run at
  Verbose together.

  Each side is described by a meta.json from the build job ({ "sdk", "commit" }), passed as -HeadMeta and
  -BaseMeta; the collector writes it into <Out>/<side>/meta.json for the parser.

  Any collector failure stops the script with exit 1, naming the side, kind and run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('android', 'ios')][string]$Platform,
    # Comma-separated, because `pwsh -File` passes an array argument as one string: q1,boot,click.
    [Parameter(Mandatory)][string]$Phases,
    [Parameter(Mandatory)][string]$Out,
    [Parameter(Mandatory)][string]$HeadMeta,
    [Parameter(Mandatory)][string]$BaseMeta,
    [ValidateRange(1, [int]::MaxValue)][int]$Runs = 20,
    [ValidateRange(1, [int]::MaxValue)][int]$Clicks = 10,
    # Android
    [string]$Serial,
    # iOS
    [string]$Udid,
    [string]$HeadProject,
    [string]$BaseProject,
    [string]$HeadDerivedData,
    [string]$BaseDerivedData
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Read-Meta {
    param([string]$Path)
    $m = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$m.sdk) -or [string]::IsNullOrWhiteSpace([string]$m.commit)) {
        throw "${Path}: sdk and commit must both be set"
    }
    return $m
}

# The order of the two sides for run or block $Index: head first on even, v015 first on odd.
function Get-SideOrder {
    param([object[]]$Sides, [int]$Index)
    if ($Index % 2 -eq 0) { return , @($Sides[0], $Sides[1]) }
    return , @($Sides[1], $Sides[0])
}

function Get-ClickBlocks {
    param([int]$Runs, [int]$Clicks)
    return [int][math]::Ceiling($Runs / $Clicks) + 1
}

function Stop-Side {
    param([object]$Side)
    if ($Platform -eq 'android') {
        foreach ($pkg in $Side.Id, "$($Side.Id).test") {
            & adb -s $Serial shell am force-stop $pkg
            if ($LASTEXITCODE -ne 0) { throw "adb am force-stop $pkg failed with exit $LASTEXITCODE" }
        }
    }
    else {
        # terminate fails when the app is not running, which is the state wanted.
        & xcrun simctl terminate $Udid $Side.Id 2>$null
    }
}

function Invoke-Collector {
    param([object]$Side, [object]$Other, [string]$Kind, [int]$Run = -1)
    Stop-Side $Other
    $a = @('-Side', $Side.Name, '-Kind', $Kind, '-Out', $Out)
    if ($Platform -eq 'android') {
        $collector = Join-Path $PSScriptRoot 'android-collect.ps1'
        $a += @('-Serial', $Serial, '-Package', $Side.Id)
    }
    else {
        $collector = Join-Path $PSScriptRoot 'ios-collect.ps1'
        $a += @('-Udid', $Udid, '-Bundle', $Side.Id, '-Project', $Side.Project, '-DerivedData', $Side.DerivedData)
    }
    if ($Kind -ne 'q1') { $a += @('-Sdk', $Side.Meta.sdk, '-Commit', $Side.Meta.commit, '-Run', "$Run") }
    if ($Kind -eq 'click') { $a += @('-Clicks', "$Clicks") }
    $label = "$($Side.Name) $Kind$(if ($Run -ge 0) { " run $Run" })"
    Write-Host "::group::$label"
    $started = Get-Date
    & pwsh -NoProfile -File $collector @a
    $code = $LASTEXITCODE
    Write-Host ("{0} finished in {1:n1}s with exit {2}" -f $label, ((Get-Date) - $started).TotalSeconds, $code)
    Write-Host '::endgroup::'
    if ($code -ne 0) { throw "$label failed with exit $code" }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ($Platform -eq 'android' -and -not $Serial) { throw '-Serial is required on android' }
        if ($Platform -eq 'ios') {
            foreach ($n in 'Udid', 'HeadProject', 'BaseProject', 'HeadDerivedData', 'BaseDerivedData') {
                if (-not (Get-Variable $n -ValueOnly)) { throw "-$n is required on ios" }
            }
        }
        $ids = if ($Platform -eq 'android') { @('io.blazornative.shell', 'io.blazornative.shell.v015') } else { @('io.blazornative.bnhost', 'io.blazornative.bnhost.v015') }
        $head = [pscustomobject]@{ Name = 'head'; Id = $ids[0]; Meta = (Read-Meta $HeadMeta); Project = $HeadProject; DerivedData = $HeadDerivedData }
        $base = [pscustomobject]@{ Name = 'v015'; Id = $ids[1]; Meta = (Read-Meta $BaseMeta); Project = $BaseProject; DerivedData = $BaseDerivedData }
        $sides = @($head, $base)
        New-Item -ItemType Directory -Force -Path $Out | Out-Null

        foreach ($phase in @($Phases -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
            switch ($phase) {
                'q1' {
                    Invoke-Collector $head $base 'q1'
                    Invoke-Collector $base $head 'q1'
                }
                'boot' {
                    foreach ($r in 0..$Runs) {
                        $order = Get-SideOrder $sides $r
                        Invoke-Collector $order[0] $order[1] 'boot' $r
                        Invoke-Collector $order[1] $order[0] 'boot' $r
                    }
                }
                'click' {
                    $blocks = Get-ClickBlocks $Runs $Clicks
                    Write-Host "click: $blocks blocks of $Clicks clicks per side after the warm-up block 0"
                    foreach ($b in 0..$blocks) {
                        $order = Get-SideOrder $sides $b
                        Invoke-Collector $order[0] $order[1] 'click' $b
                        Invoke-Collector $order[1] $order[0] 'click' $b
                    }
                }
                default { throw "unknown phase '$phase': expected q1, boot or click" }
            }
        }
        foreach ($s in $sides) { Stop-Side $s }
    }
    catch {
        [Console]::Error.WriteLine("interleave: $($_.Exception.Message)")
        exit 1
    }
}
