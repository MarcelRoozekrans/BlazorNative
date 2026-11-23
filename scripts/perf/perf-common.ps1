<#
.SYNOPSIS
  Phase 19.2: the pure functions both collectors share, dot-sourced by android-collect.ps1, ios-collect.ps1
  and interleave.ps1 from their own directory. No device, no simulator, no side effects beyond the file
  system calls Get-RunLogPath and Remove-KindLogs make.

.DESCRIPTION
  One copy, so the two collectors cannot drift. tests/BlazorNative.Runtime.Tests/PerfCollectorTests.cs feeds
  each detector a passing input and a planted violation through BoundedScript.

  Get-RunLogPath reads $script:Force. A dot-sourced function's $script: scope is the dot-sourcing script's,
  so it reads the collector's -Force.
#>

Set-StrictMode -Version 2.0

# The mark points present in a log: every `BNPERF <point> ` token.
function Get-MarkPoints {
    param([string[]]$Lines)
    $points = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($l in $Lines) {
        $m = [regex]::Match($l, 'BNPERF (\S+) ')
        if ($m.Success) { [void]$points.Add($m.Groups[1].Value) }
    }
    return , $points
}

# Q1 Verbose: every required point must be present.
function Test-Q1Verbose {
    param([string[]]$Lines)
    $failures = New-Object System.Collections.Generic.List[string]
    $points = Get-MarkPoints $Lines
    foreach ($need in 'boot.start', 'boot.content', 'frame.recv', 'frame.applied') {
        if (-not $points.Contains($need)) { $failures.Add("no BNPERF $need at Verbose") }
    }
    return , $failures
}

function Test-LogHasMark {
    param([string[]]$Lines, [string]$Point)
    foreach ($l in $Lines) { if ($l -match ('BNPERF ' + [regex]::Escape($Point) + ' ')) { return $true } }
    return $false
}

function Get-MarkCount {
    param([string[]]$Lines, [string]$Point)
    $n = 0
    foreach ($l in $Lines) { if ($l -match ('BNPERF ' + [regex]::Escape($Point) + ' ')) { $n++ } }
    return $n
}

# A click log is complete when every click's post and handler are in, and post equals the click count.
function Test-ClickLogComplete {
    param([string[]]$Lines, [int]$Clicks)
    if ($Clicks -lt 1) { return $false }
    $post = Get-MarkCount $Lines 'dispatch.post'
    $handler = Get-MarkCount $Lines 'dispatch.handler'
    return ($post -eq $Clicks) -and ($handler -ge $post)
}

# The parser's meta.json rule: a non-empty string without commas, pipes or newlines. Returns an error or $null.
function Test-MetaValue {
    param([string]$Name, [string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "-$Name is required for the boot and click kinds" }
    if ($Value.IndexOfAny([char[]]@(',', '|', "`n", "`r")) -ge 0) { return "-$Name must not contain commas, pipes or newlines" }
    return $null
}

# The side and the app id must agree, so a baseline run can never measure HEAD's app: v015 needs an id
# ending in .v015 (the backport's suffix), head one that does not. -IdName names the collector's id
# parameter, Package on Android and Bundle on iOS. Returns the error, or null.
function Test-SideId {
    param([string]$Side, [string]$Id, [string]$IdName = 'Package')
    $kind = $IdName.ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($Id)) { return "-$IdName must not be empty" }
    $suffixed = $Id.EndsWith('.v015', [System.StringComparison]::Ordinal)
    if ($Side -eq 'v015' -and -not $suffixed) { return "-Side v015 needs the backport's $kind id ending in .v015, got '$Id'" }
    if ($Side -eq 'head' -and $suffixed) { return "-Side head needs HEAD's $kind id, not the backport's '$Id'" }
    return $null
}

# The run numbers one call collects: 0..Runs, or exactly -Run when it is 0 or more.
function Get-RunList {
    param([int]$Runs, [int]$Run = -1)
    if ($Run -ge 0) { return , @($Run) }
    return , @(0..$Runs)
}

# The path for a run's log; throws when it exists, which is how a duplicate run number is refused.
function Get-RunLogPath {
    param([string]$SideDir, [string]$Kind, [int]$Run)
    $path = Join-Path $SideDir ('{0}-{1}.log' -f $Kind, $Run)
    if ((Test-Path -LiteralPath $path) -and -not $script:Force) {
        throw "refusing to overwrite ${path}: the parser rejects duplicate run numbers. Delete the side directory $SideDir, or pass -Force."
    }
    return $path
}

# Under -Force: removes the side directory's <kind>-<n>.log files for the kind being collected, and nothing
# else, so a run above the new -Runs cannot survive from an earlier collection.
function Remove-KindLogs {
    param([string]$SideDir, [string]$Kind)
    if (-not (Test-Path -LiteralPath $SideDir)) { return }
    foreach ($f in Get-ChildItem -LiteralPath $SideDir -File) {
        if ($f.Name -match ('^' + [regex]::Escape($Kind) + '-\d+\.log$')) { Remove-Item -LiteralPath $f.FullName }
    }
}

# Pure: the simulator's `launchctl list` lines (PID, status, label) of the app's jobs whose PID column is a
# number. A UIKit app's label is UIKitApplication:<bundle id>[<hex>][...], so the bracket after the id keeps
# io.blazornative.bnhost from matching io.blazornative.bnhost.v015.
function Select-RunningAppJobs {
    param([string[]]$Lines, [string]$BundleId)
    $label = 'UIKitApplication:' + [regex]::Escape($BundleId) + '\['
    return @($Lines | Where-Object { $_ -match ('^\s*\d+\s+\S+\s+' + $label) })
}
