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
           does not take a side below --min-runs. -Clicks defaults to 5, so runs=20 gives 5 measured processes
           per side, not 3: each block is one test process, and a slow mode confined to some processes needs
           several of them to show in the parser's per-process medians.

  Load: before every collector call one line goes to <Out>/load.txt: the UTC time, the side, the kind, the run
  or block, and the host's load average, `/proc/loadavg` on the Android runner and `sysctl -n vm.loadavg` on
  macOS. The parser ignores files at the root of <Out>; the file is evidence for reading the per-process medians.

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
    [ValidateRange(1, [int]::MaxValue)][int]$Clicks = 5,
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

# perf-common.ps1 holds Select-RunningAppJobs, the pure half of Stop-Side on iOS, where PerfCollectorTests reach it.
. (Join-Path $PSScriptRoot 'perf-common.ps1')

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

# The host's load average as one line: /proc/loadavg on Linux, vm.loadavg on macOS. Never throws: a
# missing sample is written as `unavailable` with the reason, and does not stop a collection.
function Get-LoadSample {
    try {
        if (Test-Path -LiteralPath '/proc/loadavg') { return (Get-Content -LiteralPath '/proc/loadavg' -TotalCount 1).Trim() }
        $out = & sysctl -n vm.loadavg 2>&1
        if ($LASTEXITCODE -eq 0) { return ((@($out) -join ' ').Trim()) }
        return "unavailable: sysctl exit $LASTEXITCODE"
    }
    catch { return "unavailable: $($_.Exception.Message)" }
}

function Write-LoadSample {
    param([string]$Label)
    $line = '{0} {1} load={2}' -f (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [System.Globalization.CultureInfo]::InvariantCulture), $Label, (Get-LoadSample)
    Add-Content -LiteralPath (Join-Path $Out 'load.txt') -Value $line -Encoding UTF8
}

# Every tool call goes through perf-common.ps1's Invoke-BoundedProcess, so a hung adb or simctl cannot
# hang the cleanup.
function Stop-Side {
    param([object]$Side, [int]$GraceSeconds = 10)
    if ($Platform -eq 'android') {
        foreach ($pkg in $Side.Id, "$($Side.Id).test") {
            $r = Invoke-BoundedProcess 'adb' @('-s', $Serial, 'shell', 'am', 'force-stop', $pkg) 30
            if ($r.ExitCode -ne 0) { throw "adb am force-stop $pkg failed with exit $($r.ExitCode): $($r.Stderr.Trim())" }
        }
    }
    else {
        # terminate exits non-zero when the app is not running, which is the state wanted, and a terminate
        # that timed out may still have ended it, so neither outcome decides. What is checked is the state:
        # no running launchd job for the app afterwards.
        try {
            $t = Invoke-BoundedProcess 'xcrun' @('simctl', 'terminate', $Udid, $Side.Id) 15
            $said = "exit $($t.ExitCode) $($t.Stderr.Trim())"
        }
        catch { $said = $_.Exception.Message }
        # A short grace for launchd to reap the job.
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        do {
            $running = Get-RunningAppJobs $Side.Id
            if ($running.Count -eq 0) { break }
            Start-Sleep -Milliseconds 250
        } while ($clock.Elapsed.TotalSeconds -lt $GraceSeconds)
        if ($running.Count -gt 0) {
            throw "$($Side.Id) is still running after simctl terminate ($said): $($running -join ' | ')"
        }
    }
}

# The simulator's launchd jobs for the app that have a pid; perf-common.ps1's Select-RunningAppJobs picks them.
function Get-RunningAppJobs {
    param([string]$BundleId)
    $r = Invoke-BoundedProcess 'xcrun' @('simctl', 'spawn', $Udid, 'launchctl', 'list') 30
    if ($r.ExitCode -ne 0) { throw "simctl spawn launchctl list failed with exit $($r.ExitCode): $($r.Stderr.Trim())" }
    return , @(Select-RunningAppJobs @($r.Stdout -split "\r?\n") $BundleId)
}

# Runs $Body, then $Stop for every side, also when $Body failed, so a failed collection does not leave
# either app running. A failure stopping one side does not skip the other. $Body's error is rethrown
# first, so a cleanup failure cannot replace it; with no $Body error, the first stop error is thrown.
function Invoke-WithSideCleanup {
    param([object[]]$Sides, [scriptblock]$Body, [scriptblock]$Stop)
    $bodyError = $null
    try { & $Body } catch { $bodyError = $_ }
    $stopError = $null
    foreach ($s in $Sides) {
        try { & $Stop $s }
        catch {
            [Console]::Error.WriteLine("interleave: stopping a side failed: $($_.Exception.Message)")
            if ($null -eq $stopError) { $stopError = $_ }
        }
    }
    if ($null -ne $bodyError) { throw $bodyError }
    if ($null -ne $stopError) { throw $stopError }
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
    Write-LoadSample $label
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

        Invoke-WithSideCleanup $sides {
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
        } { param($s) Stop-Side $s }
    }
    catch {
        [Console]::Error.WriteLine("interleave: $($_.Exception.Message)")
        exit 1
    }
}
