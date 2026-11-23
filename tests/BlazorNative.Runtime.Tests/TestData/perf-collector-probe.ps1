# THE COLLECTOR PROBE, for PerfCollectorTests (19.2 final review, I3). It dot-sources one of
# scripts/perf's scripts, whose main block is skipped when dot-sourced, and calls the pure
# functions listed in -CallsFile, a JSON array of { "function": <name>, "args": [ ... ] }. An arg
# that is a JSON array is passed as one string[], the shape of the collectors' -Lines parameters,
# and an arg that is an object { "scriptblock": <text> } is passed as that scriptblock.
# It prints one line, PROBE-RESULT: followed by a JSON array of { "value": <return value>, "error":
# <message or null> }, one per call, in order; a call that throws has a null value and its message.
# No device, no simulator: the functions it calls read only their input, except Invoke-BoundedProcess
# and Get-SideWriteError, which a fact points at a command or a directory of its own.
param(
    [Parameter(Mandatory)][ValidateSet('android', 'ios', 'common', 'interleave')][string]$Script,
    [Parameter(Mandatory)][string]$CallsFile
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$perf = Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'scripts/perf'
$file = switch ($Script) { 'android' { 'android-collect.ps1' } 'ios' { 'ios-collect.ps1' } 'common' { 'perf-common.ps1' } 'interleave' { 'interleave.ps1' } }
# interleave.ps1's parameters are mandatory, so it gets placeholders; its functions under test do not read them.
if ($Script -eq 'interleave') { . (Join-Path $perf $file) -Platform android -Phases q1 -Out . -HeadMeta none -BaseMeta none }
else { . (Join-Path $perf $file) }

$results = New-Object System.Collections.Generic.List[object]
foreach ($call in @(Get-Content -LiteralPath $CallsFile -Raw | ConvertFrom-Json)) {
    $argList = New-Object System.Collections.Generic.List[object]
    foreach ($a in @($call.args)) {
        if ($a -is [array]) { $argList.Add([string[]]@($a)) }
        elseif ($a -is [pscustomobject] -and @($a.PSObject.Properties.Name) -contains 'scriptblock') { $argList.Add([scriptblock]::Create($a.scriptblock)) }
        else { $argList.Add($a) }
    }
    $splat = $argList.ToArray()
    try {
        $value = & $call.function @splat
        $results.Add([pscustomobject]@{ value = $value; error = $null })
    }
    catch { $results.Add([pscustomobject]@{ value = $null; error = $_.Exception.Message }) }
}
Write-Output ('PROBE-RESULT: ' + (ConvertTo-Json -InputObject $results.ToArray() -Depth 6 -Compress))
