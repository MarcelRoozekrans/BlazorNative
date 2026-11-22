<#
.SYNOPSIS
  Phase 19.2 emulator collector: drives a device and writes the logs tools/BlazorNative.PerfReport reads.

.DESCRIPTION
  Layout written, which scripts/perf/README.md defines:
    <Out>/<Side>/boot-<run>.log   one cold boot per file
    <Out>/<Side>/click-<run>.log  PerfMeasurementTest's serial clicks per file
    <Out>/<Side>/meta.json        { "sdk", "commit" }, written when -Sdk and -Commit are given
    <Out>/q1-<Side>-default.txt   q1 evidence, kept out of the side directory: the parser rejects any
    <Out>/q1-<Side>-verbose.txt   .log file there that is not named <kind>-<run>.log

  Run numbers are 0..Runs. Run 0 is warm-up. A file that exists is never overwritten, so a rerun cannot
  produce a duplicate run number; the script stops instead. Any timeout or failed assertion exits 1.

  Kinds:
    boot   am force-stop, logcat -c, am start -W with PerfProbe at Verbose, wait for boot.content and the
           platform's Fully drawn line, dump logcat and append the am start output (it holds TotalTime).
    click  am instrument runs PerfMeasurementTest for -Clicks serial clicks, then dumps logcat.
    q1     one default launch asserting zero BNPERF lines and zero V, D or I lines from BlazorNative* tags,
           then one Verbose launch asserting boot.start, boot.content, frame.recv and frame.applied.

  The parsing and assertion logic is in functions, so the file can be dot-sourced without running.
#>
[CmdletBinding()]
param(
    [string]$Serial,
    [string]$Package = 'io.blazornative.shell',
    [ValidateSet('head', 'v015')][string]$Side,
    [ValidateSet('boot', 'click', 'q1')][string]$Kind,
    [int]$Runs = 20,
    [int]$Clicks = 25,
    [string]$Out,
    [string]$Sdk,
    [string]$Commit,
    [int]$WaitSeconds = 60
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Activity = 'io.blazornative.shell.MainActivity'
$script:ExtraComponent = 'io.blazornative.shell.EXTRA_COMPONENT'
$script:ExtraLogLevel = 'io.blazornative.shell.EXTRA_LOG_LEVEL'
$script:PerfAnnotation = 'io.blazornative.shell.PerfMeasurement'
$script:TestRunner = 'io.blazornative.shell.BlazorNativeTestRunner'

# ── Pure functions: no device, testable by dot-sourcing ─────────────────────

# Splits one `logcat -v threadtime` line into priority, tag and message. Null when it is not a log line.
function ConvertFrom-LogcatLine {
    param([string]$Line)
    $m = [regex]::Match($Line, '^\S+\s+\S+\s+\d+\s+\d+\s+([VDIWEF])\s+(.+?)\s*:\s(.*)$')
    if (-not $m.Success) { return $null }
    return [pscustomobject]@{ Priority = $m.Groups[1].Value; Tag = $m.Groups[2].Value; Message = $m.Groups[3].Value }
}

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

# Q1 default level: returns the failures, an empty list when the contract holds.
function Test-Q1Default {
    param([string[]]$Lines)
    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($l in $Lines) {
        if ($l -match 'BNPERF ') { $failures.Add("BNPERF line at the default level: $l"); continue }
        $p = ConvertFrom-LogcatLine $l
        if ($null -ne $p -and $p.Tag -like 'BlazorNative*' -and 'V', 'D', 'I' -contains $p.Priority) {
            $failures.Add("$($p.Priority) line from tag $($p.Tag) at the default level: $l")
        }
    }
    return , $failures
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

function Test-LogHasFullyDrawn {
    param([string[]]$Lines)
    foreach ($l in $Lines) { if ($l -match 'Fully drawn \S+: \+') { return $true } }
    return $false
}

# `am start -W` prints "TotalTime: <n>"; null when the line is missing.
function Get-TotalTime {
    param([string[]]$Lines)
    foreach ($l in $Lines) {
        $m = [regex]::Match($l, '^\s*TotalTime:\s*(\d+)\s*$')
        if ($m.Success) { return [int]$m.Groups[1].Value }
    }
    return $null
}

# `am instrument` ends a passing run with "OK (<n> test...)".
function Test-InstrumentPassed {
    param([string[]]$Lines)
    $text = $Lines -join "`n"
    return ($text -match '(?m)^OK \(\d+ tests?\)') -and ($text -notmatch 'FAILURES!!!') -and ($text -notmatch 'INSTRUMENTATION_FAILED')
}

# The path for a run's log; throws when it exists, which is how a duplicate run number is refused.
function Get-RunLogPath {
    param([string]$SideDir, [string]$Kind, [int]$Run)
    $path = Join-Path $SideDir ('{0}-{1}.log' -f $Kind, $Run)
    if (Test-Path -LiteralPath $path) { throw "refusing to overwrite ${path}: the parser rejects duplicate run numbers" }
    return $path
}

# ── Device functions ────────────────────────────────────────────────────────

function Invoke-Adb {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$AdbArgs)
    $output = & adb -s $script:Serial @AdbArgs 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "adb $($AdbArgs -join ' ') failed with exit $LASTEXITCODE`n$($output -join "`n")" }
    return , @($output)
}

function Get-Logcat { return , @(Invoke-Adb 'logcat' '-d' '-v' 'threadtime') }

function Reset-Logcat {
    # A 16M buffer keeps the whole run: the default one can wrap and lose the first marks.
    Invoke-Adb 'logcat' '-G' '16M' | Out-Null
    Invoke-Adb 'logcat' '-c' | Out-Null
}

# Polls the log until $Test returns true; throws naming $What after $WaitSeconds.
function Wait-ForLog {
    param([scriptblock]$Test, [string]$What)
    $deadline = (Get-Date).AddSeconds($script:WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        $lines = Get-Logcat
        if (& $Test $lines) { return , $lines }
        Start-Sleep -Milliseconds 500
    }
    throw "timed out after $($script:WaitSeconds)s waiting for $What"
}

# Cold-starts the activity. Returns the `am start -W` output.
function Start-Cold {
    param([bool]$VerboseLevel)
    Invoke-Adb 'shell' 'am' 'force-stop' $script:Package | Out-Null
    Reset-Logcat
    $cmd = @('shell', 'am', 'start', '-W', '-n', "$($script:Package)/$($script:Activity)",
        '-e', $script:ExtraComponent, 'PerfProbe')
    if ($VerboseLevel) { $cmd += @('-e', $script:ExtraLogLevel, 'Verbose') }
    return , @(Invoke-Adb @cmd)
}

function Invoke-BootRun {
    param([string]$SideDir, [int]$Run)
    $path = Get-RunLogPath $SideDir 'boot' $Run
    $start = Start-Cold $true
    $lines = Wait-ForLog { param($l) Test-LogHasMark $l 'boot.content' } 'the boot.content mark'
    $lines = Wait-ForLog { param($l) Test-LogHasFullyDrawn $l } 'the Fully drawn line'
    if ($null -eq (Get-TotalTime $start)) { throw "am start -W printed no TotalTime:`n$($start -join "`n")" }
    Set-Content -LiteralPath $path -Value (@($lines) + @($start)) -Encoding UTF8
    Write-Host "boot run $Run -> $path"
}

function Invoke-ClickRun {
    param([string]$SideDir, [int]$Run)
    $path = Get-RunLogPath $SideDir 'click' $Run
    Invoke-Adb 'shell' 'am' 'force-stop' $script:Package | Out-Null
    Reset-Logcat
    $runner = "$($script:Package).test/$($script:TestRunner)"
    $result = Invoke-Adb 'shell' 'am' 'instrument' '-w' '-e' 'annotation' $script:PerfAnnotation '-e' 'perfClicks' "$($script:Clicks)" $runner
    if (-not (Test-InstrumentPassed $result)) { throw "PerfMeasurementTest did not pass:`n$($result -join "`n")" }
    $lines = Get-Logcat
    if (-not (Test-LogHasMark $lines 'dispatch.post')) { throw 'the click run logged no dispatch.post mark' }
    Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
    Write-Host "click run $Run -> $path"
}

function Invoke-Q1 {
    param([string]$OutDir, [string]$SideName)
    $failures = New-Object System.Collections.Generic.List[string]

    Start-Cold $false | Out-Null
    $default = Wait-ForLog { param($l) Test-LogHasFullyDrawn $l } 'the Fully drawn line at the default level'
    # Let late marks and shell logs reach the log before it is read.
    Start-Sleep -Seconds 3
    $default = Get-Logcat
    Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-default.txt") -Value $default -Encoding UTF8
    foreach ($f in (Test-Q1Default $default)) { $failures.Add($f) }

    Start-Cold $true | Out-Null
    Wait-ForLog { param($l) Test-LogHasMark $l 'boot.content' } 'boot.content at Verbose' | Out-Null
    $verbose = Get-Logcat
    Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-verbose.txt") -Value $verbose -Encoding UTF8
    foreach ($f in (Test-Q1Verbose $verbose)) { $failures.Add($f) }

    if ($failures.Count -gt 0) {
        $failures | Select-Object -First 20 | ForEach-Object { Write-Error $_ -ErrorAction Continue }
        throw "q1 failed with $($failures.Count) violation(s)"
    }
    Write-Host 'q1 passed: quiet at the default level, all marks at Verbose'
}

# ── Main: skipped when dot-sourced ──────────────────────────────────────────

if ($MyInvocation.InvocationName -ne '.') {
    try {
        foreach ($required in 'Serial', 'Side', 'Kind', 'Out') {
            if (-not (Get-Variable $required -ValueOnly)) { throw "-$required is required" }
        }
        $script:Serial = $Serial; $script:Package = $Package; $script:Clicks = $Clicks; $script:WaitSeconds = $WaitSeconds
        $sideDir = Join-Path $Out $Side
        New-Item -ItemType Directory -Force -Path $sideDir | Out-Null

        if ($Sdk -and $Commit) {
            $meta = [ordered]@{ sdk = $Sdk; commit = $Commit } | ConvertTo-Json
            [System.IO.File]::WriteAllText((Join-Path $sideDir 'meta.json'), $meta, (New-Object System.Text.UTF8Encoding($false)))
        }

        switch ($Kind) {
            'boot'  { foreach ($r in 0..$Runs) { Invoke-BootRun $sideDir $r } }
            'click' { foreach ($r in 0..$Runs) { Invoke-ClickRun $sideDir $r } }
            'q1'    { Invoke-Q1 $Out $Side }
        }
    }
    catch {
        Write-Host "android-collect: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}
