<#
.SYNOPSIS
  Phase 19.2: the pure functions both collectors share, dot-sourced by android-collect.ps1, ios-collect.ps1
  and interleave.ps1 from their own directory. No device and no simulator. The side effects are the file
  system calls Get-RunLogPath, Get-SideWriteError and Remove-KindLogs make, and the process
  Invoke-BoundedProcess starts for the collectors' tool calls.

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

# A click log is complete when it holds each click's dispatch.post, dispatch.enter and dispatch.handler
# exactly once: a short count is a log not yet drained, and a long one a second transport or a stray
# dispatch, which the parser would pair with the wrong click.
function Test-ClickLogComplete {
    param([string[]]$Lines, [int]$Clicks)
    if ($Clicks -lt 1) { return $false }
    foreach ($point in 'dispatch.post', 'dispatch.enter', 'dispatch.handler') {
        if ((Get-MarkCount $Lines $point) -ne $Clicks) { return $false }
    }
    return $true
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

# Checks, before a boot or click collection writes anything, that it may write to the side directory.
# Without -Force, every requested run's log must be absent, so the call fails before meta.json is touched.
# meta.json labels every log in the directory, of every kind, so when any log will remain after this call,
# the meta.json already there must name the same -Sdk and -Commit: otherwise a -Force -Run call would
# relabel the samples it never touched. A log remains unless it is a requested run under -Force, or one of
# the kind's logs that -Force without -Run removes. Returns the error, or $null.
function Get-SideWriteError {
    param([string]$SideDir, [string]$Kind, [int[]]$RunList, [bool]$Force, [bool]$ReplaceKind, [string]$Sdk, [string]$Commit)
    $requested = @($RunList | ForEach-Object { '{0}-{1}.log' -f $Kind, $_ })
    if (-not $Force) {
        foreach ($name in $requested) {
            $path = Join-Path $SideDir $name
            if (Test-Path -LiteralPath $path) {
                return "refusing to overwrite ${path}: the parser rejects duplicate run numbers. Delete the side directory $SideDir, or pass -Force."
            }
        }
    }
    if (-not (Test-Path -LiteralPath $SideDir)) { return $null }
    $remaining = @(Get-ChildItem -LiteralPath $SideDir -File | Where-Object {
            $m = [regex]::Match($_.Name, '^([a-z]+)-\d+\.log$')
            $m.Success -and
            -not ($Force -and ($requested -contains $_.Name)) -and
            -not ($Force -and $ReplaceKind -and $m.Groups[1].Value -eq $Kind)
        })
    if ($remaining.Count -eq 0) { return $null }
    $metaPath = Join-Path $SideDir 'meta.json'
    if (-not (Test-Path -LiteralPath $metaPath)) {
        return "$SideDir holds $($remaining.Count) log(s) but no meta.json, so they cannot be shown to match -Sdk '$Sdk' -Commit '$Commit'. Delete the side directory."
    }
    $meta = Get-Content -LiteralPath $metaPath -Raw | ConvertFrom-Json
    $names = @($meta.PSObject.Properties.Name)
    $oldSdk = $(if ($names -contains 'sdk') { [string]$meta.sdk } else { '' })
    $oldCommit = $(if ($names -contains 'commit') { [string]$meta.commit } else { '' })
    if (-not [string]::Equals($oldSdk, $Sdk, [System.StringComparison]::Ordinal) -or
        -not [string]::Equals($oldCommit, $Commit, [System.StringComparison]::Ordinal)) {
        return "$metaPath records sdk '$oldSdk' and commit '$oldCommit', and $($remaining.Count) log(s) such as $($remaining[0].Name) would keep that label under -Sdk '$Sdk' -Commit '$Commit'. Delete the side directory to collect another build."
    }
    return $null
}

# Runs a process with one deadline for its exit and for draining its stdout and stderr. A child that
# outlives the process can hold a redirected pipe open after the exit, and an unbounded wait on that pipe
# would hang the collector before its device cleanup. At the deadline the process tree is killed, best
# effort, and this throws. Returns ExitCode, Stdout and Stderr; judging the exit code is the caller's job.
function Invoke-BoundedProcess {
    param([string]$File, [string[]]$ArgumentList, [int]$TimeoutSeconds = 30, [hashtable]$EnvVars = @{})
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    foreach ($a in $ArgumentList) { [void]$psi.ArgumentList.Add($a) }
    foreach ($k in $EnvVars.Keys) { $psi.Environment[$k] = [string]$EnvVars[$k] }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $proc = [System.Diagnostics.Process]::Start($psi)
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    $what = "$File $($ArgumentList -join ' ')"
    if (-not $proc.WaitForExit([int][math]::Max(0, ($deadline - [DateTime]::UtcNow).TotalMilliseconds))) {
        try { $proc.Kill($true) } catch { }
        throw "$what timed out after ${TimeoutSeconds}s"
    }
    $left = [int][math]::Max(0, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
    if (-not [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($outTask, $errTask), $left)) {
        try { $proc.Kill($true) } catch { }
        throw "$what timed out after ${TimeoutSeconds}s: it exited with $($proc.ExitCode), but a process it left running held its output open"
    }
    return [pscustomobject]@{ ExitCode = $proc.ExitCode; Stdout = $outTask.Result; Stderr = $errTask.Result }
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
