<#
.SYNOPSIS
  Phase 19.2 simulator collector: drives an iOS simulator and writes the logs tools/BlazorNative.PerfReport reads.

.DESCRIPTION
  Layout written, which scripts/perf/README.md defines:
    <Out>/<Side>/boot-<run>.log   one cold boot per file
    <Out>/<Side>/click-<run>.log  BnPerfMeasurementTests' serial clicks per file
    <Out>/<Side>/meta.json        { "sdk", "commit" }, written for boot and click from -Sdk and -Commit
    <Out>/q1-<Side>-default.txt   q1 evidence, kept out of the side directory: the parser rejects any
    <Out>/q1-<Side>-verbose.txt   .log file there that is not named <kind>-<run>.log

  Run numbers are 0..Runs. Run 0 is warm-up. -Run <k> collects exactly run k instead, so a caller can
  interleave two sides one run at a time; -Runs is then ignored. A file that exists is never overwritten, so a rerun cannot
  produce a duplicate run number; the script stops instead. Any timeout or failed assertion exits 1.

  Before the first run the script enables debug persistence for the subsystem, with
  `simctl spawn <udid> log config --subsystem io.blazornative --mode "level:debug,persist:debug"`, and
  every run reads `log show --debug --predicate 'subsystem == "io.blazornative"'` over a window that
  starts just before that run's launch, so runs do not bleed into each other.

  Kinds:
    boot   terminate, then `simctl launch` with BN_LAUNCH_COMPONENT=PerfProbe and BN_LOG_LEVEL=Verbose, passed
           as SIMCTL_CHILD_ variables. Waits for boot.start and boot.content and dumps the window.
    click  `xcodebuild test-without-building -only-testing:BnHostTests/BnPerfMeasurementTests` for -Clicks
           serial clicks, then waits for every dispatch.post and dispatch.handler mark and dumps the window.
    q1     one default launch of PerfProbe asserting zero BNPERF lines and zero debug or info lines from the subsystem,
           with a positive control that the process ran, then one Verbose launch asserting boot.start,
           boot.content, frame.recv and frame.applied.

  Environment passed to the app: BN_LOG_LEVEL must be the NAME Verbose. BnLogLevel.fromName does not read
  digits, so 5 would resolve to the default and the run would hold no marks. `simctl launch` has no
  per-launch flag for it on every Xcode version, so q1 passes it as SIMCTL_CHILD_BN_LOG_LEVEL, and xcodebuild
  gets TEST_RUNNER_BN_LOG_LEVEL and TEST_RUNNER_PERF_CLICKS.

  The parsing and assertion logic is in functions, so the file can be dot-sourced without running.

  Requirements: pwsh 7 (the script uses .NET Core APIs, ProcessStartInfo.ArgumentList and Process.Kill with
  a process tree; Windows PowerShell 5.1 can parse the file but is not supported for running it), macOS with
  Xcode (xcrun, xcodebuild), a booted simulator, and for click a derived data directory that holds a
  build-for-testing result. The boot and click kinds require -Sdk and -Commit, which become meta.json. To
  rerun after a failed collection, delete <Out>/<Side> first, or pass -Force, which overwrites the run logs.
#>
[CmdletBinding()]
param(
    [string]$Udid,
    [string]$Bundle = 'io.blazornative.bnhost',
    [ValidateSet('head', 'v015')][string]$Side,
    [ValidateSet('boot', 'click', 'q1')][string]$Kind,
    [ValidateRange(1, [int]::MaxValue)][int]$Runs = 20,
    [ValidateRange(-1, [int]::MaxValue)][int]$Run = -1,
    [ValidateRange(1, [int]::MaxValue)][int]$Clicks = 25,
    [string]$Out,
    [string]$Sdk,
    [string]$Commit,
    [string]$Project = 'src/BlazorNative.Apple/BnHost.xcodeproj',
    [string]$Scheme = 'BnHost',
    [string]$DerivedData,
    [int]$WaitSeconds = 60,
    [int]$SettleSeconds = 10,
    [switch]$Force
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Subsystem = 'io.blazornative'
$script:PerfClass = 'BnHostTests/BnPerfMeasurementTests'
$script:PerfComponent = 'PerfProbe'

# ── Pure functions: no simulator, testable by dot-sourcing ──────────────────

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

# One `log show --style ndjson` line as an object with Type, Subsystem, Pid and Message. Null when it is
# not a log record (the header lines, blank lines).
function ConvertFrom-OsLogLine {
    param([string]$Line)
    if ([string]::IsNullOrWhiteSpace($Line) -or -not $Line.TrimStart().StartsWith('{')) { return $null }
    try { $o = $Line | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
    $props = $o.PSObject.Properties.Name
    if ($props -notcontains 'messageType') { return $null }
    return [pscustomobject]@{
        Type      = [string]$o.messageType
        Subsystem = $(if ($props -contains 'subsystem') { [string]$o.subsystem } else { '' })
        Pid       = $(if ($props -contains 'processID') { [int]$o.processID } else { -1 })
        Message   = $(if ($props -contains 'eventMessage') { [string]$o.eventMessage } else { '' })
    }
}

# Q1 default level: returns the failures, an empty list when the contract holds. The lines are ndjson.
function Test-Q1Default {
    param([string[]]$Lines)
    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($l in $Lines) {
        if ($l -match 'BNPERF ') { $failures.Add("BNPERF line at the default level: $l"); continue }
        $r = ConvertFrom-OsLogLine $l
        if ($null -ne $r -and $r.Subsystem -eq $script:Subsystem -and 'Debug', 'Info' -contains $r.Type) {
            $failures.Add("$($r.Type) line from subsystem $($r.Subsystem) at the default level: $l")
        }
    }
    return , $failures
}

# Positive control for the default launch: the process the launch reported must have logged something.
function Test-LaunchControl {
    param([string[]]$Lines, [int]$ProcessId)
    foreach ($l in $Lines) {
        $r = ConvertFrom-OsLogLine $l
        if ($null -ne $r -and $r.Pid -eq $ProcessId) { return $true }
    }
    return $false
}

# `simctl launch` prints "<bundle>: <pid>"; null when there is no pid.
function Get-LaunchPid {
    param([string[]]$Lines)
    foreach ($l in $Lines) {
        $m = [regex]::Match($l, ':\s*(\d+)\s*$')
        if ($m.Success) { return [int]$m.Groups[1].Value }
    }
    return $null
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

# `xcodebuild test` prints "** TEST SUCCEEDED **" and an "Executed 1 test" summary; both are required, so a
# run that executed nothing (a class name that matched no test) is not a pass.
function Test-XcodebuildPassed {
    param([string[]]$Lines)
    $text = $Lines -join "`n"
    return ($text -match '\*\* TEST SUCCEEDED \*\*') -and ($text -match 'Executed 1 test, with 0 failures') -and ($text -notmatch '\*\* TEST FAILED \*\*')
}

# The parser's meta.json rule: a non-empty string without commas, pipes or newlines. Returns an error or $null.
function Test-MetaValue {
    param([string]$Name, [string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "-$Name is required for the boot and click kinds" }
    if ($Value.IndexOfAny([char[]]@(',', '|', "`n", "`r")) -ge 0) { return "-$Name must not contain commas, pipes or newlines" }
    return $null
}

# The side and the app id must agree, so a baseline run can never measure HEAD's app: v015 needs an id
# ending in .v015 (the backport's suffix), head one that does not. Returns the error, or null.
function Test-SideId {
    param([string]$Side, [string]$Id)
    if ([string]::IsNullOrWhiteSpace($Id)) { return "-Bundle must not be empty" }
    $suffixed = $Id.EndsWith('.v015', [System.StringComparison]::Ordinal)
    if ($Side -eq 'v015' -and -not $suffixed) { return "-Side v015 needs the backport's bundle id ending in .v015, got '$Id'" }
    if ($Side -eq 'head' -and $suffixed) { return "-Side head needs HEAD's bundle id, not the backport's '$Id'" }
    return $null
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

# The xcodebuild deadline: a margin for the build products and the boot, plus time per click.
function Get-XcodebuildTimeout {
    param([int]$Clicks)
    return 300 + 5 * $Clicks
}

# `log show --start` takes local time to the second.
function Format-LogStart {
    param([datetime]$When)
    return $When.ToString('yyyy-MM-dd HH:mm:ss', [System.Globalization.CultureInfo]::InvariantCulture)
}

# The environment simctl launch hands the app: SIMCTL_CHILD_ variables, PerfProbe always, Verbose on request.
# BN_LOG_LEVEL takes a name, because BnLogLevel.fromName does not read digits.
function Get-LaunchEnv {
    param([bool]$VerboseLevel, [string]$Component = 'PerfProbe')
    $envVars = @{ SIMCTL_CHILD_BN_LAUNCH_COMPONENT = $Component }
    if ($VerboseLevel) { $envVars['SIMCTL_CHILD_BN_LOG_LEVEL'] = 'Verbose' }
    return $envVars
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

# ── Process functions ───────────────────────────────────────────────────────

# Runs a tool with a deadline. On timeout the process tree is killed and this throws. Returns an object with
# ExitCode and Lines (stdout, then stderr); throws on a non-zero exit unless -AllowFail.
function Invoke-Tool {
    param(
        [string]$File,
        [string[]]$ToolArgs,
        [int]$TimeoutSeconds = 30,
        [hashtable]$EnvVars = @{},
        [switch]$AllowFail
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    foreach ($a in $ToolArgs) { [void]$psi.ArgumentList.Add($a) }
    foreach ($k in $EnvVars.Keys) { $psi.Environment[$k] = [string]$EnvVars[$k] }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $proc = [System.Diagnostics.Process]::Start($psi)
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
        try { $proc.Kill($true) } catch { }
        throw "$File $($ToolArgs -join ' ') timed out after ${TimeoutSeconds}s"
    }
    $proc.WaitForExit()
    $lines = @(($outTask.Result + "`n" + $errTask.Result) -split "\r?\n" | Where-Object { $_ -ne '' })
    if ($proc.ExitCode -ne 0 -and -not $AllowFail) {
        throw "$File $($ToolArgs -join ' ') failed with exit $($proc.ExitCode)`n$($lines -join "`n")"
    }
    return [pscustomobject]@{ ExitCode = $proc.ExitCode; Lines = $lines }
}

# ── Simulator functions ─────────────────────────────────────────────────────

function Invoke-Simctl {
    [CmdletBinding(PositionalBinding = $false)]
    param([Parameter(Position = 0)][int]$TimeoutSeconds = 30, [switch]$AllowFail, [hashtable]$EnvVars = @{}, [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$SimArgs)
    return Invoke-Tool 'xcrun' (@('simctl') + $SimArgs) $TimeoutSeconds $EnvVars -AllowFail:$AllowFail
}

function Enable-DebugPersistence {
    Invoke-Simctl 30 'spawn' $script:Udid 'log' 'config' '--subsystem' $script:Subsystem '--mode' 'level:debug,persist:debug' | Out-Null
}

# Best-effort stop of the app, for the start of a run and for the cleanup after a timeout.
function Stop-App {
    try { Invoke-Simctl 15 -AllowFail 'terminate' $script:Udid $script:Bundle | Out-Null } catch { }
}

# Reads the subsystem's log from -Start on. Style compact for the parser, ndjson for the q1 checks.
function Get-OsLog {
    param([string]$Start, [string]$Style = 'compact')
    $r = Invoke-Simctl 90 'spawn' $script:Udid 'log' 'show' '--debug' '--style' $Style '--start' $Start '--predicate' ('subsystem == "' + $script:Subsystem + '"')
    return , @($r.Lines)
}

# A window start for a run: a second boundary after the previous run's last line, so runs do not bleed.
function Get-WindowStart {
    Start-Sleep -Milliseconds 1100
    return Format-LogStart (Get-Date)
}

# Polls the log until $Test returns true; throws naming $What after $WaitSeconds.
function Wait-ForLog {
    param([string]$Start, [scriptblock]$Test, [string]$What, [int]$Seconds = 0, [string]$Style = 'compact')
    if ($Seconds -le 0) { $Seconds = $script:WaitSeconds }
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $lines = Get-OsLog $Start $Style
        if (& $Test $lines) { return , $lines }
        Start-Sleep -Milliseconds 500
    }
    throw "timed out after ${Seconds}s waiting for $What"
}

function Invoke-BootRun {
    param([string]$SideDir, [int]$Run)
    $path = Get-RunLogPath $SideDir 'boot' $Run
    try {
        $launch = Start-Launch $true
        $lines = Wait-ForLog $launch.Start { param($l) (Test-LogHasMark $l 'boot.start') -and (Test-LogHasMark $l 'boot.content') } `
            'the boot.start and boot.content marks'
        Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
        Write-Host "boot run $Run -> $path"
    }
    finally { Stop-App }
}

function Invoke-ClickRun {
    param([string]$SideDir, [int]$Run)
    $path = Get-RunLogPath $SideDir 'click' $Run
    Stop-App
    $start = Get-WindowStart
    $xcArgs = @('test-without-building', '-project', $script:Project, '-scheme', $script:Scheme,
        '-destination', "platform=iOS Simulator,id=$($script:Udid)", '-derivedDataPath', $script:DerivedData,
        "-only-testing:$($script:PerfClass)")
    $envVars = @{ TEST_RUNNER_BN_LOG_LEVEL = 'Verbose'; TEST_RUNNER_PERF_CLICKS = "$($script:Clicks)" }
    try {
        $result = Invoke-Tool 'xcodebuild' $xcArgs (Get-XcodebuildTimeout $script:Clicks) $envVars -AllowFail
    }
    catch {
        # Killing the host xcodebuild tree does not stop the app on the simulator. Best effort; the original error stays.
        Stop-App
        throw
    }
    if ($result.ExitCode -ne 0 -or -not (Test-XcodebuildPassed $result.Lines)) {
        Stop-App
        throw "BnPerfMeasurementTests did not pass (exit $($result.ExitCode)):`n$((@($result.Lines) | Select-Object -Last 40) -join "`n")"
    }
    # The .NET marks reach the log late through the writer thread and the stderr pump, so wait for the tail.
    $clicks = $script:Clicks
    $lines = Wait-ForLog $start { param($l) Test-ClickLogComplete $l $clicks } "every click's dispatch.post ($clicks) and dispatch.handler marks" -Seconds 30
    Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
    Write-Host "click run $Run -> $path"
}

function Start-Launch {
    param([bool]$VerboseLevel)
    Stop-App
    $envVars = Get-LaunchEnv $VerboseLevel $script:PerfComponent
    $start = Get-WindowStart
    $r = Invoke-Simctl 60 -EnvVars $envVars 'launch' $script:Udid $script:Bundle
    $procId = Get-LaunchPid $r.Lines
    if ($null -eq $procId) { throw "simctl launch printed no pid:`n$($r.Lines -join "`n")" }
    return [pscustomobject]@{ Start = $start; Pid = $procId }
}

function Invoke-Q1 {
    param([string]$OutDir, [string]$SideName)
    $failures = New-Object System.Collections.Generic.List[string]
    try {
        $launch = Start-Launch $false
        # Nothing marks the end of a boot at the default level, so give it a bounded settle before reading.
        Start-Sleep -Seconds $script:SettleSeconds
        $default = Get-OsLog $launch.Start 'ndjson'
        Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-default.txt") -Value $default -Encoding UTF8
        if (-not (Test-LaunchControl $default $launch.Pid)) {
            # This control proves only that the launched process ran and logged. That debug capture works
            # is proved by the Verbose phase below: its marks are debug records read through the same persistence.
            # The control needs a record from the process itself; the subsystem filter may hide it, so ask unfiltered.
            $any = (Invoke-Simctl 90 'spawn' $script:Udid 'log' 'show' '--style' 'ndjson' '--start' $launch.Start '--predicate' "processID == $($launch.Pid)").Lines
            if (-not (Test-LaunchControl $any $launch.Pid)) { $failures.Add("positive control failed: process $($launch.Pid) logged nothing, so the default launch is not shown to have happened") }
        }
        foreach ($f in (Test-Q1Default $default)) { $failures.Add($f) }

        $launch = Start-Launch $true
        $verbose = Wait-ForLog $launch.Start { param($l) (Test-Q1Verbose $l).Count -eq 0 } 'boot.start, boot.content, frame.recv and frame.applied at Verbose' -Style 'ndjson'
        Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-verbose.txt") -Value $verbose -Encoding UTF8
        foreach ($f in (Test-Q1Verbose $verbose)) { $failures.Add($f) }
        Stop-App

        if ($failures.Count -gt 0) {
            $failures | Select-Object -First 20 | ForEach-Object { Write-Error $_ -ErrorAction Continue }
            throw "q1 failed with $($failures.Count) violation(s)"
        }
        Write-Host 'q1 passed: quiet at the default level, all marks at Verbose'
    }
    finally {
        # A timeout or failed assertion must not leave the app running.
        Stop-App
    }
}

# ── Main: skipped when dot-sourced ──────────────────────────────────────────

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'pwsh 7 is required: the script uses ProcessStartInfo.ArgumentList and Process.Kill with a process tree' }
        foreach ($required in 'Udid', 'Side', 'Kind', 'Out') {
            if (-not (Get-Variable $required -ValueOnly)) { throw "-$required is required" }
        }
        $idErr = Test-SideId $Side $Bundle
        if ($idErr) { throw $idErr }
        if ($Kind -eq 'click' -and -not $DerivedData) { throw '-DerivedData is required for the click kind: the directory of a build-for-testing result' }
        $script:Udid = $Udid; $script:Bundle = $Bundle; $script:Clicks = $Clicks; $script:WaitSeconds = $WaitSeconds
        $script:SettleSeconds = $SettleSeconds; $script:Force = [bool]$Force
        $script:Project = $Project; $script:Scheme = $Scheme; $script:DerivedData = $DerivedData
        $sideDir = Join-Path $Out $Side
        if ($Kind -ne 'q1') {
            foreach ($pair in @(@('Sdk', $Sdk), @('Commit', $Commit))) {
                $err = Test-MetaValue $pair[0] $pair[1]
                if ($err) { throw $err }
            }
            # Refuse before anything is written: a second run into an existing side directory would
            # otherwise rewrite meta.json and fail at run 0 only after that.
            $firstRun = (Get-RunList $Runs $Run)[0]
            if ((Test-Path -LiteralPath (Join-Path $sideDir "$Kind-$firstRun.log")) -and -not $Force) {
                [void](Get-RunLogPath $sideDir $Kind $firstRun)
            }
            New-Item -ItemType Directory -Force -Path $sideDir | Out-Null
            # Under -Run only that run's file is replaced, by Get-RunLogPath; the other runs are kept.
            if ($Force -and $Run -lt 0) { Remove-KindLogs $sideDir $Kind }
            $meta = [ordered]@{ sdk = $Sdk; commit = $Commit } | ConvertTo-Json
            [System.IO.File]::WriteAllText((Join-Path $sideDir 'meta.json'), $meta, (New-Object System.Text.UTF8Encoding($false)))
        }
        else { New-Item -ItemType Directory -Force -Path $Out | Out-Null }

        Enable-DebugPersistence
        $runList = Get-RunList $Runs $Run
        switch ($Kind) {
            'boot'  { foreach ($r in $runList) { Invoke-BootRun $sideDir $r } }
            'click' { foreach ($r in $runList) { Invoke-ClickRun $sideDir $r } }
            'q1'    { Invoke-Q1 $Out $Side }
        }
    }
    catch {
        [Console]::Error.WriteLine("ios-collect: $($_.Exception.Message)")
        exit 1
    }
}
