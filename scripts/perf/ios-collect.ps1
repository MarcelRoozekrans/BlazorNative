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
  starts just before that run's launch, so runs do not bleed into each other. The click kind widens the
  predicate to the app's NSLog records that carry BNPERF, because under XCTest no stderr pump runs and
  that is the only place the .NET marks are; see Get-ClickPredicate.

  Kinds:
    boot   terminate, then `simctl launch` with BN_LAUNCH_COMPONENT=PerfProbe and BN_LOG_LEVEL=Verbose, passed
           as SIMCTL_CHILD_ variables. Waits for boot.start and boot.content and dumps the window.
    click  `xcodebuild test-without-building -only-testing:BnHostTests/BnPerfMeasurementTests` for -Clicks
           serial clicks, then waits for every dispatch.post and dispatch.handler mark and dumps the window.
    q1     one default launch of PerfProbe, read for the whole process, asserting zero BNPERF lines, zero debug or
           info lines from the subsystem and zero .NET lines below Warn ([BN|I|, [BN|D|, [BN|V|), with a positive
           control that the process ran; then one default-level run of BnPerfMeasurementTests for -Q1Clicks
           clicks, read with the click kind's predicate, asserting zero BNPERF lines, and read for the whole test
           host, asserting zero .NET lines below Warn, with a control that the window holds the host's records;
           then one Verbose launch asserting boot.start, boot.content, frame.recv and frame.applied.

  Environment passed to the app: BN_LOG_LEVEL must be the NAME Verbose. BnLogLevel.fromName does not read
  digits, so 5 would resolve to the default and the run would hold no marks. `simctl launch` has no
  per-launch flag for it on every Xcode version, so q1 passes it as SIMCTL_CHILD_BN_LOG_LEVEL, and xcodebuild
  gets TEST_RUNNER_BN_LOG_LEVEL, TEST_RUNNER_PERF_LEVEL and TEST_RUNNER_PERF_CLICKS. At the default level
  the BN_LOG_LEVEL variable is removed from the child's environment; see Get-LaunchEnv and Get-MeasurementEnv.

  The parsing and assertion logic is in functions, so the file can be dot-sourced without running. The
  functions both collectors share are in perf-common.ps1, which this file dot-sources from its own directory.

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
    [ValidateRange(1, [int]::MaxValue)][int]$Q1Clicks = 3,
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
# The app's process name, the executable of BnHost.app on both sides.
$script:Process = 'BnHost'

# The shared pure functions: the mark counts, Q1 Verbose, the side and id check, the run list and paths.
. (Join-Path $PSScriptRoot 'perf-common.ps1')

# ── Pure functions: no simulator, testable by dot-sourcing ──────────────────

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

# A .NET framework line below Warn: `[BN|I|`, `[BN|D|` or `[BN|V|`. The .NET lines are not typed by level in
# the unified log: NSLog records them as the app's own Default records, and the stderr pump relays them as
# subsystem records of category native, also Default. Only the prefix says what level they were written at.
function Test-GatedDotNetLine {
    param([string]$Line)
    return $Line -match '\[BN\|[IDV]\|'
}

# Q1 default level: returns the failures, an empty list when the contract holds. The lines are ndjson, the
# whole launched process: a debug or info record of the subsystem, or a gated .NET line from any source.
function Test-Q1Default {
    param([string[]]$Lines)
    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($l in $Lines) {
        if ($l -match 'BNPERF ') { $failures.Add("BNPERF line at the default level: $l"); continue }
        if (Test-GatedDotNetLine $l) { $failures.Add(".NET line below Warn at the default level: $l"); continue }
        $r = ConvertFrom-OsLogLine $l
        if ($null -ne $r -and $r.Subsystem -eq $script:Subsystem -and 'Debug', 'Info' -contains $r.Type) {
            $failures.Add("$($r.Type) line from subsystem $($r.Subsystem) at the default level: $l")
        }
    }
    return , $failures
}

# Q1 default-level click: zero BNPERF lines in the click predicate's window, and in the test host's whole
# window (ndjson) no BNPERF line and no gated .NET line, with the control that the host window holds a
# record of the process, so an empty click log is quiet, not unread. The host window is read here for
# BNPERF too because the click predicate takes the subsystem's marks from category Perf only, and a mark
# the shell logged in another category would be outside it.
function Test-Q1DefaultClick {
    param([string[]]$ClickLines, [string[]]$HostLines)
    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($l in $ClickLines) { if ($l -match 'BNPERF ') { $failures.Add("BNPERF line in a default-level click run: $l") } }
    $control = $false
    foreach ($l in $HostLines) {
        $r = ConvertFrom-OsLogLine $l
        if ($null -ne $r -and $r.Pid -ge 0) { $control = $true }
        if ($l -match 'BNPERF ') { $failures.Add("BNPERF line in the test host's window of a default-level click run: $l"); continue }
        if (Test-GatedDotNetLine $l) { $failures.Add(".NET line below Warn in a default-level click run: $l") }
    }
    if (-not $control) { $failures.Add("control failed: the default-level click window holds no record of the $($script:Process) process") }
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

# `xcodebuild test` prints "** TEST SUCCEEDED **", `test-without-building` "** TEST EXECUTE SUCCEEDED **", and
# both an "Executed 1 test" summary; the marker and the summary are both required, so a
# run that executed nothing (a class name that matched no test) is not a pass.
function Test-XcodebuildPassed {
    param([string[]]$Lines)
    $text = $Lines -join "`n"
    return ($text -match '\*\* TEST (EXECUTE )?SUCCEEDED \*\*') -and ($text -match 'Executed 1 test, with 0 failures') -and ($text -notmatch '\*\* TEST (EXECUTE )?FAILED \*\*')
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
# BN_LOG_LEVEL takes a name, because BnLogLevel.fromName does not read digits. At the default level the
# entry is $null, which Invoke-BoundedProcess removes, so a level set in the collector's environment
# cannot reach the default launch.
function Get-LaunchEnv {
    param([bool]$VerboseLevel, [string]$Component = 'PerfProbe')
    $envVars = @{ SIMCTL_CHILD_BN_LAUNCH_COMPONENT = $Component; SIMCTL_CHILD_BN_LOG_LEVEL = $null }
    if ($VerboseLevel) { $envVars['SIMCTL_CHILD_BN_LOG_LEVEL'] = 'Verbose' }
    return $envVars
}

# The environment xcodebuild hands BnPerfMeasurementTests, as TEST_RUNNER_ variables. Both levels set
# PERF_LEVEL, the level the test asserts. At the default level BN_LOG_LEVEL is $null, which
# Invoke-BoundedProcess removes, so a level set in the collector's environment cannot reach the run.
function Get-MeasurementEnv {
    param([int]$Clicks, [bool]$DefaultLevel)
    $envVars = @{ TEST_RUNNER_PERF_CLICKS = "$Clicks"; TEST_RUNNER_PERF_LEVEL = 'Verbose'; TEST_RUNNER_BN_LOG_LEVEL = 'Verbose' }
    if ($DefaultLevel) { $envVars['TEST_RUNNER_PERF_LEVEL'] = 'default'; $envVars['TEST_RUNNER_BN_LOG_LEVEL'] = $null }
    return $envVars
}

# The boot wait's test: boot.start and boot.content, both logged by process $ProcessId. The lines are
# `log show --style compact`, which names the process as <name>[<pid>:<tid>].
function Test-BootMarksOfPid {
    param([string[]]$Lines, [int]$ProcessId)
    $mine = @($Lines | Where-Object { $_ -match ('\s\S+\[' + $ProcessId + ':[0-9A-Fa-fx]+\]\s') })
    return (Test-LogHasMark $mine 'boot.start') -and (Test-LogHasMark $mine 'boot.content')
}

# The simctl arguments of a `log show` read from $Start. --debug always, because the marks are debug
# records. With $Info also --info, which q1's default-level reads need: Test-Q1Default flags the
# subsystem's Info records, so a read without them would pass that check unread. The parsed boot and
# click reads keep their arguments, so the parser's input does not change.
function Get-OsLogArgs {
    param([string]$Udid, [string]$Start, [string]$Style, [string]$Predicate, [bool]$Info = $false)
    $a = @('spawn', $Udid, 'log', 'show', '--debug')
    if ($Info) { $a += '--info' }
    return , @($a + @('--style', $Style, '--start', $Start, '--predicate', $Predicate))
}

# The simctl arguments of a launch. --terminate-running-process has simctl end a running instance first,
# so a launch starts a new process even when Stop-App, which is best effort, left the previous one running.
function Get-LaunchArgs {
    param([string]$Udid, [string]$Bundle)
    return , @('launch', '--terminate-running-process', $Udid, $Bundle)
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
    # perf-common.ps1's Invoke-BoundedProcess: one deadline for the exit and for draining both pipes.
    $r = Invoke-BoundedProcess $File $ToolArgs $TimeoutSeconds $EnvVars
    $lines = @(($r.Stdout + "`n" + $r.Stderr) -split "\r?\n" | Where-Object { $_ -ne '' })
    if ($r.ExitCode -ne 0 -and -not $AllowFail) {
        throw "$File $($ToolArgs -join ' ') failed with exit $($r.ExitCode)`n$($lines -join "`n")"
    }
    return [pscustomobject]@{ ExitCode = $r.ExitCode; Lines = $lines }
}

# ── Simulator functions ─────────────────────────────────────────────────────

function Invoke-Simctl {
    [CmdletBinding(PositionalBinding = $false)]
    param([Parameter(Position = 0)][int]$TimeoutSeconds = 30, [switch]$AllowFail, [hashtable]$EnvVars = @{}, [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$SimArgs)
    return Invoke-Tool 'xcrun' (@('simctl') + $SimArgs) $TimeoutSeconds $EnvVars -AllowFail:$AllowFail
}

# `simctl spawn ... log config` hung past its 30 s deadline once on CI (perf.yml run 45153274952), with the
# simulator working in the calls just before it. perf.yml calls the collector once per run, so a one-off hang would fail a whole job; each
# attempt keeps its deadline and is killed on timeout, and a third failure still fails the run.
function Enable-DebugPersistence {
    $lastError = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-Simctl 30 'spawn' $script:Udid 'log' 'config' '--subsystem' $script:Subsystem '--mode' 'level:debug,persist:debug' | Out-Null
            return
        }
        catch {
            $lastError = $_
            [Console]::Error.WriteLine("ios-collect: log config attempt $attempt of 3 failed: $($_.Exception.Message)")
        }
    }
    throw $lastError
}

# Best-effort stop of the app, for the start of a run and for the cleanup after a timeout.
function Stop-App {
    try { Invoke-Simctl 15 -AllowFail 'terminate' $script:Udid $script:Bundle | Out-Null } catch { }
}

# The subsystem's records: the shell's lines and, when the stderr pump runs, the .NET lines it relays.
function Get-SubsystemPredicate { return 'subsystem == "' + $script:Subsystem + '"' }

# The click log's records: each mark exactly once.
#  - Shell marks are os_log records of the subsystem, category Perf, on both sides.
#  - .NET marks are written by .NET's Console, which on iOS is NSLog, so each becomes an os_log record of the
#    app outside the subsystem. Under XCTest no stderr pump runs on either side: both shells install it in
#    HostViewController.viewDidLoad after the `NSClassFromString("XCTestCase") == nil` guard, so nothing
#    relays the line into the subsystem. The app's NSLog record is the only copy, on both sides.
#  - Outside XCTest, as in the boot and q1 launches, the pump does run and relays NSLog's stderr copy as
#    subsystem records of category native. The predicate excludes that category, so a click log could not
#    take a second copy even if a pump ran.
function Get-ClickPredicate {
    return '(subsystem == "' + $script:Subsystem + '" AND category == "Perf") OR (process == "' + $script:Process + '" AND subsystem != "' + $script:Subsystem + '" AND eventMessage CONTAINS "BNPERF ")'
}

# Reads the log from -Start on, the subsystem by default. Style compact for the parser, ndjson for the q1 checks.
# -Info is for q1's default-level reads; see Get-OsLogArgs.
function Get-OsLog {
    param([string]$Start, [string]$Style = 'compact', [string]$Predicate = '', [switch]$Info, [int]$TimeoutSeconds = 90)
    if (-not $Predicate) { $Predicate = Get-SubsystemPredicate }
    $r = Invoke-Simctl $TimeoutSeconds -SimArgs (Get-OsLogArgs $script:Udid $Start $Style $Predicate $Info.IsPresent)
    return , @($r.Lines)
}

# A window start for a run: a second boundary after the previous run's last line, so runs do not bleed.
function Get-WindowStart {
    Start-Sleep -Milliseconds 1100
    return Format-LogStart (Get-Date)
}

# Polls the log until $Test returns true; throws naming $What after $WaitSeconds. perf-common.ps1's
# Wait-ForLines hands each log show read the budget left as its timeout.
function Wait-ForLog {
    param([string]$Start, [scriptblock]$Test, [string]$What, [int]$Seconds = 0, [string]$Style = 'compact', [string]$Predicate = '')
    if ($Seconds -le 0) { $Seconds = $script:WaitSeconds }
    return , (Wait-ForLines { param($budget) Get-OsLog $Start $Style $Predicate -TimeoutSeconds $budget } $Test $What $Seconds)
}

function Invoke-BootRun {
    param([string]$SideDir, [int]$Run)
    $path = Get-RunLogPath $SideDir 'boot' $Run
    try {
        $launch = Start-Launch $true
        $bootPid = $launch.Pid
        $lines = Wait-ForLog $launch.Start { param($l) Test-BootMarksOfPid $l $bootPid } `
            "the boot.start and boot.content marks of process $bootPid"
        Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
        Write-Host "boot run $Run -> $path"
    }
    finally { Stop-App }
}

# Runs BnPerfMeasurementTests for $Clicks clicks, at Verbose or, with -DefaultLevel, at the default level
# (no BN_LOG_LEVEL, and PERF_LEVEL=default so the test checks that Verbose is off). Throws unless it passed.
# Returns the log window's start.
function Invoke-Measurement {
    param([int]$Clicks, [switch]$DefaultLevel)
    Stop-App
    $start = Get-WindowStart
    $xcArgs = @('test-without-building', '-project', $script:Project, '-scheme', $script:Scheme,
        '-destination', "platform=iOS Simulator,id=$($script:Udid)", '-derivedDataPath', $script:DerivedData,
        "-only-testing:$($script:PerfClass)")
    $envVars = Get-MeasurementEnv $Clicks $DefaultLevel.IsPresent
    try {
        $result = Invoke-Tool 'xcodebuild' $xcArgs (Get-XcodebuildTimeout $Clicks) $envVars -AllowFail
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
    return $start
}

function Invoke-ClickRun {
    param([string]$SideDir, [int]$Run)
    $path = Get-RunLogPath $SideDir 'click' $Run
    $start = Invoke-Measurement $script:Clicks
    # The .NET marks reach the log late through the writer thread, so wait for the tail.
    $clicks = $script:Clicks
    try {
        $lines = Wait-ForLog $start { param($l) Test-ClickLogComplete $l $clicks } "every click's dispatch.post, dispatch.enter and dispatch.handler mark, $clicks of each" -Seconds 30 -Predicate (Get-ClickPredicate)
    }
    catch {
        # Evidence for the failure: every BNPERF record of the window, from any source, with its subsystem and category.
        $diag = Join-Path (Split-Path -Parent $SideDir) ("click-diag-{0}-{1}.txt" -f (Split-Path -Leaf $SideDir), $Run)
        try { Set-Content -LiteralPath $diag -Value (Get-OsLog $start 'ndjson' ('process == "' + $script:Process + '" AND eventMessage CONTAINS "BNPERF "')) -Encoding UTF8 } catch { }
        throw
    }
    # A mark seen twice would be paired with the wrong click, so a second transport is refused here.
    foreach ($point in 'dispatch.post', 'dispatch.enter', 'dispatch.handler') {
        $n = Get-MarkCount $lines $point
        if ($n -ne $clicks) { throw "click run ${Run}: $n $point marks for $clicks clicks; each must appear once" }
    }
    Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
    Write-Host "click run $Run -> $path"
}

function Start-Launch {
    param([bool]$VerboseLevel)
    Stop-App
    $envVars = Get-LaunchEnv $VerboseLevel $script:PerfComponent
    $start = Get-WindowStart
    $r = Invoke-Simctl 60 -EnvVars $envVars -SimArgs (Get-LaunchArgs $script:Udid $script:Bundle)
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
        # The whole launched process, not only the subsystem: the .NET lines NSLog records are the app's own
        # records outside the subsystem, and a gated one must be seen wherever it landed.
        $default = Get-OsLog $launch.Start 'ndjson' "processID == $($launch.Pid)" -Info
        Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-default.txt") -Value $default -Encoding UTF8
        # This control proves only that the launched process ran and logged. That debug capture works
        # is proved by the Verbose phase below: its marks are debug records read through the same persistence.
        if (-not (Test-LaunchControl $default $launch.Pid)) { $failures.Add("positive control failed: process $($launch.Pid) logged nothing, so the default launch is not shown to have happened") }
        foreach ($f in (Test-Q1Default $default)) { $failures.Add($f) }

        # The default-level click: the .NET dispatch marks, read from the click kind's predicate and log.
        $clickStart = Invoke-Measurement $script:Q1Clicks -DefaultLevel
        # The click kind's own tail wait, so a late .NET mark would be seen.
        Start-Sleep -Seconds 5
        $clickLog = Get-OsLog $clickStart 'compact' (Get-ClickPredicate)
        Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-default-click.txt") -Value $clickLog -Encoding UTF8
        # The test host's whole window: the gated .NET line check and the control that the window is readable.
        $hostLog = Get-OsLog $clickStart 'ndjson' ('process == "' + $script:Process + '"') -Info
        Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-default-click-host.txt") -Value $hostLog -Encoding UTF8
        foreach ($f in (Test-Q1DefaultClick $clickLog $hostLog)) { $failures.Add($f) }
        Stop-App

        $launch = Start-Launch $true
        $verbose = Wait-ForLog $launch.Start { param($l) (Test-Q1Verbose $l).Count -eq 0 } 'boot.start, boot.content, frame.recv and frame.applied at Verbose' -Style 'ndjson'
        Set-Content -LiteralPath (Join-Path $OutDir "q1-$SideName-verbose.txt") -Value $verbose -Encoding UTF8
        foreach ($f in (Test-Q1Verbose $verbose)) { $failures.Add($f) }
        Stop-App

        if ($failures.Count -gt 0) {
            $failures | Select-Object -First 20 | ForEach-Object { Write-Error $_ -ErrorAction Continue }
            throw "q1 failed with $($failures.Count) violation(s)"
        }
        Write-Host 'q1 passed: quiet at the default level for a launch and a click, all boot marks at Verbose'
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
        $idErr = Test-SideId $Side $Bundle 'Bundle'
        if ($idErr) { throw $idErr }
        if ('click', 'q1' -contains $Kind -and -not $DerivedData) { throw '-DerivedData is required for the click and q1 kinds: the directory of a build-for-testing result' }
        $script:Udid = $Udid; $script:Bundle = $Bundle; $script:Clicks = $Clicks; $script:Q1Clicks = $Q1Clicks; $script:WaitSeconds = $WaitSeconds
        $script:SettleSeconds = $SettleSeconds; $script:Force = [bool]$Force
        $script:Project = $Project; $script:Scheme = $Scheme; $script:DerivedData = $DerivedData
        # Absolute once, from PowerShell's location, so every later path names the same directory.
        $Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
        $sideDir = Join-Path $Out $Side
        if ($Kind -ne 'q1') {
            foreach ($pair in @(@('Sdk', $Sdk), @('Commit', $Commit))) {
                $err = Test-MetaValue $pair[0] $pair[1]
                if ($err) { throw $err }
            }
            # Refuses before anything is written, removes the logs it replaces under -Force, then writes meta.json.
            # Under -Run only that run's log is replaced; the other runs are kept.
            $sideDir = Initialize-SideDir $sideDir $Kind (Get-RunList $Runs $Run) ([bool]$Force) ($Run -lt 0) $Sdk $Commit
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
