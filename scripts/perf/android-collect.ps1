<#
.SYNOPSIS
  Phase 19.2 emulator collector: drives a device and writes the logs tools/BlazorNative.PerfReport reads.

.DESCRIPTION
  Layout written, which scripts/perf/README.md defines:
    <Out>/<Side>/boot-<run>.log   one cold boot per file
    <Out>/<Side>/click-<run>.log  PerfMeasurementTest's serial clicks per file
    <Out>/<Side>/meta.json        { "sdk", "commit" }, written for boot and click from -Sdk and -Commit
    <Out>/q1-<Side>-default.txt   q1 evidence, kept out of the side directory: the parser rejects any
    <Out>/q1-<Side>-verbose.txt   .log file there that is not named <kind>-<run>.log

  Run numbers are 0..Runs. Run 0 is warm-up. -Run <k> collects exactly run k instead, so a caller can
  interleave two sides one run at a time; -Runs is then ignored. A file that exists is never overwritten, so a rerun cannot
  produce a duplicate run number; the script stops instead. Any timeout or failed assertion exits 1.

  Kinds:
    boot   am force-stop, logcat -c, am start -W with PerfProbe at Verbose, wait for boot.content and the
           platform's Fully drawn line, dump logcat and append the am start output (it holds TotalTime).
    click  am instrument runs PerfMeasurementTest for -Clicks serial clicks, then dumps logcat.
    q1     one default launch asserting zero BNPERF lines and zero V, D or I lines from BlazorNative* tags,
           with a control that the log was read as logcat lines; then one default-level run of
           PerfMeasurementTest for -Q1Clicks clicks, through the click kind's am instrument and logcat,
           asserting zero BNPERF lines and the same tag check, the androidTest sources' own tags excluded,
           so the .NET dispatch marks and the relayed .NET lines are shown quiet too; then one Verbose
           launch asserting boot.start, boot.content, frame.recv and frame.applied. That the dispatch marks
           are present at Verbose is the click kind's check.

  The parsing and assertion logic is in functions, so the file can be dot-sourced without running. The
  functions both collectors share are in perf-common.ps1, which this file dot-sources from its own directory.

  Requirements: pwsh 7 (the script uses .NET Core APIs, ProcessStartInfo.ArgumentList and
  Process.Kill with a process tree; Windows PowerShell 5.1 can parse the file but is not supported for running it), adb on PATH, and a device that accepts `logcat -G 16M`. The boot and click
  kinds require -Sdk and -Commit, which become meta.json. To rerun after a failed collection, delete
  <Out>/<Side> first, or pass -Force, which overwrites the run logs.
#>
[CmdletBinding()]
param(
    [string]$Serial,
    [string]$Package = 'io.blazornative.shell',
    [ValidateSet('head', 'v015')][string]$Side,
    [ValidateSet('boot', 'click', 'q1')][string]$Kind,
    [ValidateRange(1, [int]::MaxValue)][int]$Runs = 20,
    [ValidateRange(-1, [int]::MaxValue)][int]$Run = -1,
    [ValidateRange(1, [int]::MaxValue)][int]$Clicks = 25,
    [ValidateRange(1, [int]::MaxValue)][int]$Q1Clicks = 3,
    [string]$Out,
    [string]$Sdk,
    [string]$Commit,
    [int]$WaitSeconds = 60,
    [switch]$Force
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Activity = 'io.blazornative.shell.MainActivity'
$script:ExtraComponent = 'io.blazornative.shell.EXTRA_COMPONENT'
$script:ExtraLogLevel = 'io.blazornative.shell.EXTRA_LOG_LEVEL'
$script:PerfAnnotation = 'io.blazornative.shell.PerfMeasurement'
$script:TestRunner = 'io.blazornative.shell.BlazorNativeTestRunner'
# The tags the androidTest sources log under. They are the instrumentation's own lines, not the
# framework's, and run in the app's process; PerfMeasurementTest writes none of them today.
$script:InstrumentationTags = @('BlazorNative.Test', 'BlazorNativeTest')

# The shared pure functions: the mark counts, Q1 Verbose, the side and id check, the run list and paths.
. (Join-Path $PSScriptRoot 'perf-common.ps1')

# ── Pure functions: no device, testable by dot-sourcing ─────────────────────

# Splits one `logcat -v threadtime` line into priority, tag and message. Null when it is not a log line.
function ConvertFrom-LogcatLine {
    param([string]$Line)
    $m = [regex]::Match($Line, '^\S+\s+\S+\s+\d+\s+\d+\s+([VDIWEF])\s+(.+?)\s*:\s(.*)$')
    if (-not $m.Success) { return $null }
    return [pscustomobject]@{ Priority = $m.Groups[1].Value; Tag = $m.Groups[2].Value; Message = $m.Groups[3].Value }
}

# A framework line below Warn: V, D or I from a BlazorNative* tag that is not one of the instrumentation's.
# Returns the failure text, or $null. -Parsed is a ConvertFrom-LogcatLine result.
function Get-GatedTagFailure {
    param([object]$Parsed, [string]$Line, [string]$Where)
    if ($null -eq $Parsed) { return $null }
    if ($Parsed.Tag -notlike 'BlazorNative*' -or 'V', 'D', 'I' -notcontains $Parsed.Priority) { return $null }
    if ($script:InstrumentationTags -contains $Parsed.Tag) { return $null }
    return "$($Parsed.Priority) line from tag $($Parsed.Tag) $Where`: $Line"
}

# Q1 default level: returns the failures, an empty list when the contract holds. The tag check holds
# only for lines that parse as logcat lines one by one, so the control is that the launch's own
# `Fully drawn` line parses as a record of its own: a read that joined the lines into one string, as
# one did in 19.2, parses as one record whose message runs on past that line, and fails here.
function Test-Q1Default {
    param([string[]]$Lines, [string]$Package, [string]$Activity)
    $failures = New-Object System.Collections.Generic.List[string]
    $fullyDrawn = $false
    $pattern = '^' + (Get-FullyDrawnPattern $Package $Activity)
    foreach ($l in $Lines) {
        if ($l -match 'BNPERF ') { $failures.Add("BNPERF line at the default level: $l"); continue }
        $p = ConvertFrom-LogcatLine $l
        if ($null -ne $p -and $p.Message -match $pattern) { $fullyDrawn = $true }
        $gated = Get-GatedTagFailure $p $l 'at the default level'
        if ($gated) { $failures.Add($gated) }
    }
    if (-not $fullyDrawn) { $failures.Add("control failed: no Fully drawn line for $Package parsed as a logcat line of its own among $(@($Lines).Count) lines, so the tag check is not shown to have read the log line by line") }
    return , $failures
}

# Q1 default-level click: zero BNPERF lines, and the default launch's tag check: no V, D or I line from a
# BlazorNative* tag. The click kind's log source is the whole logcat, so the instrumentation's own lines
# are in it: its runner logs from tags that are not BlazorNative*, and the androidTest sources' own
# BlazorNative* tags are excluded by name. The control, as on iOS, is that the window holds the app's own
# records: the test's launch of $Package must appear as a `Displayed <package>/<activity>` line that
# parses as a logcat line of its own, so an unread or joined log cannot pass as a quiet one.
function Test-Q1DefaultClick {
    param([string[]]$Lines, [string]$Package)
    $failures = New-Object System.Collections.Generic.List[string]
    $displayed = '^Displayed ' + [regex]::Escape($Package) + '/\S+ for user \d+: \+[0-9ms]+$'
    $control = $false
    foreach ($l in $Lines) {
        if ($l -match 'BNPERF ') { $failures.Add("BNPERF line in a default-level click run: $l"); continue }
        $p = ConvertFrom-LogcatLine $l
        if ($null -ne $p -and $p.Message -match $displayed) { $control = $true }
        $gated = Get-GatedTagFailure $p $l 'in a default-level click run'
        if ($gated) { $failures.Add($gated) }
    }
    if (-not $control) { $failures.Add("control failed: no 'Displayed $Package/...' line parsed as a logcat line of its own among $(@($Lines).Count) lines, so the default-level click window is not shown to hold the app's launch") }
    return , $failures
}

# The message of the platform's `Fully drawn <component>: +<time>` record for $Package's $Activity, as a
# regex without a start anchor. The component is either form ActivityTaskManager prints: the full
# <package>/<class>, or the short <package>/.<rest> when the class starts with the package, as head's does.
# The backport's package carries the .v015 suffix and its class does not, so it prints the full form.
# Another app's record, a launcher's or the other side's, does not match.
function Get-FullyDrawnPattern {
    param([string]$Package, [string]$Activity)
    $forms = @([regex]::Escape("$Package/$Activity"))
    if ($Activity.StartsWith("$Package.", [System.StringComparison]::Ordinal)) {
        $forms += [regex]::Escape("$Package/" + $Activity.Substring($Package.Length))
    }
    return 'Fully drawn (?:' + ($forms -join '|') + '): \+[0-9ms]+$'
}

function Test-LogHasFullyDrawn {
    param([string[]]$Lines, [string]$Package, [string]$Activity)
    $pattern = Get-FullyDrawnPattern $Package $Activity
    foreach ($l in $Lines) { if ($l -match $pattern) { return $true } }
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

# `am instrument` ends a passing run with "OK (<n> test...)". The annotation selects PerfMeasurementTest
# alone, so the summary must report exactly one test: "OK (0 tests)" is a filter that matched nothing, and
# a larger count a filter that ran more than the measurement.
function Test-InstrumentPassed {
    param([string[]]$Lines)
    $text = $Lines -join "`n"
    return ($text -match '(?m)^OK \(1 test\)\s*$') -and ($text -notmatch 'FAILURES!!!') -and ($text -notmatch 'INSTRUMENTATION_FAILED')
}

# ── Device functions ────────────────────────────────────────────────────────

# Runs adb with one deadline for its exit and its output, through perf-common.ps1's Invoke-BoundedProcess.
# On timeout this throws. Returns stdout lines; a non-zero exit throws with stderr.
function Invoke-Adb {
    param([int]$TimeoutSeconds = 30, [Parameter(ValueFromRemainingArguments = $true)][string[]]$AdbArgs)
    $r = Invoke-BoundedProcess 'adb' (@('-s', $script:Serial) + $AdbArgs) $TimeoutSeconds
    if ($r.ExitCode -ne 0) { throw "adb $($AdbArgs -join ' ') failed with exit $($r.ExitCode)`n$($r.Stdout)`n$($r.Stderr)" }
    return , @($r.Stdout -split "\r?\n" | Where-Object { $_ -ne '' })
}

# Invoke-Adb already returns the lines as one array. Wrapping its call in @() again would nest that array in a
# one-element array, which a [string[]] parameter then joins into a single string, so every per-line check
# would see one line.
function Get-Logcat {
    $lines = Invoke-Adb 60 'logcat' '-d' '-v' 'threadtime'
    return , $lines
}

function Reset-Logcat {
    # A 16M buffer keeps the whole run: the default one can wrap and lose the first marks.
    Invoke-Adb 30 'logcat' '-G' '16M' | Out-Null
    Invoke-Adb 30 'logcat' '-c' | Out-Null
}

# Polls the log until $Test returns true; throws naming $What after $WaitSeconds.
function Wait-ForLog {
    param([scriptblock]$Test, [string]$What, [int]$Seconds = 0)
    if ($Seconds -le 0) { $Seconds = $script:WaitSeconds }
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $lines = Get-Logcat
        if (& $Test $lines) { return , $lines }
        Start-Sleep -Milliseconds 500
    }
    throw "timed out after ${Seconds}s waiting for $What"
}

# Cold-starts the activity. Returns the `am start -W` output.
function Start-Cold {
    param([bool]$VerboseLevel)
    Invoke-Adb 30 'shell' 'am' 'force-stop' $script:Package | Out-Null
    Reset-Logcat
    $cmd = @('shell', 'am', 'start', '-W', '-n', "$($script:Package)/$($script:Activity)",
        '-e', $script:ExtraComponent, 'PerfProbe')
    if ($VerboseLevel) { $cmd += @('-e', $script:ExtraLogLevel, 'Verbose') }
    $out = Invoke-Adb 90 @cmd
    return , $out
}

function Invoke-BootRun {
    param([string]$SideDir, [int]$Run)
    $path = Get-RunLogPath $SideDir 'boot' $Run
    $start = Start-Cold $true
    $lines = Wait-ForLog { param($l) Test-LogHasMark $l 'boot.content' } 'the boot.content mark'
    $lines = Wait-ForLog { param($l) Test-LogHasFullyDrawn $l $script:Package $script:Activity } 'the Fully drawn line'
    if ($null -eq (Get-TotalTime $start)) { throw "am start -W printed no TotalTime:`n$($start -join "`n")" }
    Set-Content -LiteralPath $path -Value (@($lines) + @($start)) -Encoding UTF8
    Write-Host "boot run $Run -> $path"
}

# Runs PerfMeasurementTest for $Clicks clicks at $Level (Verbose, or default for no level extra), after a
# force-stop and a cleared logcat. Throws unless it passed.
function Invoke-Measurement {
    param([int]$Clicks, [string]$Level = 'Verbose')
    Invoke-Adb 30 'shell' 'am' 'force-stop' $script:Package | Out-Null
    Reset-Logcat
    $runner = "$($script:Package).test/$($script:TestRunner)"
    # Bound: a few seconds per click plus a margin for the boot.
    $instrumentTimeout = 120 + 5 * $Clicks
    try {
        $result = Invoke-Adb $instrumentTimeout 'shell' 'am' 'instrument' '-w' '-e' 'annotation' $script:PerfAnnotation '-e' 'perfClicks' "$Clicks" '-e' 'perfLevel' $Level $runner
    }
    catch {
        # Killing the host adb tree does not stop the device-side test. Best effort; the original error stays.
        foreach ($pkg in "$($script:Package).test", $script:Package) {
            try { Invoke-Adb 10 'shell' 'am' 'force-stop' $pkg | Out-Null } catch { }
        }
        throw
    }
    if (-not (Test-InstrumentPassed $result)) { throw "PerfMeasurementTest did not pass:`n$($result -join "`n")" }
}

function Invoke-ClickRun {
    param([string]$SideDir, [int]$Run)
    $path = Get-RunLogPath $SideDir 'click' $Run
    Invoke-Measurement $script:Clicks 'Verbose'
    # The .NET marks reach the log late through the writer thread and the stderr pump, so wait for the tail.
    $clicks = $script:Clicks
    $lines = Wait-ForLog { param($l) Test-ClickLogComplete $l $clicks } "every click's dispatch.post, dispatch.enter and dispatch.handler mark, $clicks of each" -Seconds 15
    Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
    Write-Host "click run $Run -> $path"
}

function Invoke-Q1 {
    param([string]$OutDir, [string]$SideName)
    $failures = New-Object System.Collections.Generic.List[string]
    try {
        Start-Cold $false | Out-Null
        $default = Wait-ForLog { param($l) Test-LogHasFullyDrawn $l $script:Package $script:Activity } 'the Fully drawn line at the default level'
        # Let late marks and shell logs reach the log before it is read.
        Start-Sleep -Seconds 3
        $default = Get-Logcat
        Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-default.txt") -Value $default -Encoding UTF8
        foreach ($f in (Test-Q1Default $default $script:Package $script:Activity)) { $failures.Add($f) }

        Invoke-Measurement $script:Q1Clicks 'default'
        # The window the click kind waits for its tail marks in, so a late .NET mark would be seen.
        Start-Sleep -Seconds 5
        $clickLog = Get-Logcat
        Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-default-click.txt") -Value $clickLog -Encoding UTF8
        foreach ($f in (Test-Q1DefaultClick $clickLog $script:Package)) { $failures.Add($f) }
        Invoke-Adb 30 'shell' 'am' 'force-stop' $script:Package | Out-Null

        Start-Cold $true | Out-Null
        $verbose = Wait-ForLog { param($l) (Test-Q1Verbose $l).Count -eq 0 } 'boot.start, boot.content, frame.recv and frame.applied at Verbose'
        Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-verbose.txt") -Value $verbose -Encoding UTF8
        foreach ($f in (Test-Q1Verbose $verbose)) { $failures.Add($f) }

        if ($failures.Count -gt 0) {
            $failures | Select-Object -First 20 | ForEach-Object { Write-Error $_ -ErrorAction Continue }
            throw "q1 failed with $($failures.Count) violation(s)"
        }
        Write-Host 'q1 passed: quiet at the default level for a launch and a click, all boot marks at Verbose'
    }
    finally {
        # A timeout or failed assertion must not leave the app or its test package running, as on iOS.
        foreach ($pkg in "$($script:Package).test", $script:Package) {
            try { Invoke-Adb 10 'shell' 'am' 'force-stop' $pkg | Out-Null } catch { }
        }
    }
}

# ── Main: skipped when dot-sourced ──────────────────────────────────────────

if ($MyInvocation.InvocationName -ne '.') {
    try {
        foreach ($required in 'Serial', 'Side', 'Kind', 'Out') {
            if (-not (Get-Variable $required -ValueOnly)) { throw "-$required is required" }
        }
        $idErr = Test-SideId $Side $Package 'Package'
        if ($idErr) { throw $idErr }
        $script:Serial = $Serial; $script:Package = $Package; $script:Clicks = $Clicks; $script:Q1Clicks = $Q1Clicks; $script:WaitSeconds = $WaitSeconds; $script:Force = [bool]$Force
        $sideDir = Join-Path $Out $Side
        if ($Kind -ne 'q1') {
            foreach ($pair in @(@('Sdk', $Sdk), @('Commit', $Commit))) {
                $err = Test-MetaValue $pair[0] $pair[1]
                if ($err) { throw $err }
            }
            # Refuse before anything is written: an existing run log without -Force, or logs that would
            # remain under another build's meta.json.
            $writeErr = Get-SideWriteError $sideDir $Kind (Get-RunList $Runs $Run) ([bool]$Force) ($Run -lt 0) $Sdk $Commit
            if ($writeErr) { throw $writeErr }
            New-Item -ItemType Directory -Force -Path $sideDir | Out-Null
            # Under -Run only that run's file is replaced, by Get-RunLogPath; the other runs are kept.
            if ($Force -and $Run -lt 0) { Remove-KindLogs $sideDir $Kind }
            $meta = [ordered]@{ sdk = $Sdk; commit = $Commit } | ConvertTo-Json
            [System.IO.File]::WriteAllText((Join-Path $sideDir 'meta.json'), $meta, (New-Object System.Text.UTF8Encoding($false)))
        }
        else { New-Item -ItemType Directory -Force -Path $Out | Out-Null }

        $runList = Get-RunList $Runs $Run
        switch ($Kind) {
            'boot'  { foreach ($r in $runList) { Invoke-BootRun $sideDir $r } }
            'click' { foreach ($r in $runList) { Invoke-ClickRun $sideDir $r } }
            'q1'    { Invoke-Q1 $Out $Side }
        }
    }
    catch {
        [Console]::Error.WriteLine("android-collect: $($_.Exception.Message)")
        exit 1
    }
}
