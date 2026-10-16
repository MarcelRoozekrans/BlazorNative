# THE #454 SHAPE, IN MINIATURE, for GeneratorRunTests. The 2026-10-15 dumps show
# the generator's pwsh blocked waiting for EOF on the output pipe of a native
# command that had already exited: something that command started still held the
# pipe. This script rebuilds that chain with three roles of itself:
#
#   stub      runs the launcher as a NATIVE command piped to Out-Host, the way
#             generate-reference.ps1 runs `dotnet publish`, so it blocks on EOF;
#   launcher  starts the sleeper, which inherits its stdout, and exits at once;
#   sleeper   holds that stdout open for 600 s.
#
# GeneratorRunTests also runs two roles directly: the sleeper alone, a script
# still running at the bound, and the launcher alone, a script that exits while
# the sleeper holds the script's own stdout.
#
# Each role appends "<role> <$PID>" to -PidFile. $PID is read inside each
# process, so it names the real pwsh, never the dotnet global-tool shim, where
# `pwsh` resolves to one, as on the dev host.
#
# The sleeper is started with Process.Start, UseShellExecute off and no
# redirection, so it is handed the launcher's standard handles. GeneratorRunTests'
# anchor checks that the pipe is really held, on whichever OS it runs.
param(
    [Parameter(Mandatory)][string]$PidFile,
    [ValidateSet('stub', 'launcher', 'sleeper')][string]$Role = 'stub'
)

$ErrorActionPreference = 'Stop'
Add-Content -LiteralPath $PidFile -Value "$Role $PID"

switch ($Role) {
    'stub' {
        Write-Output 'stub started'
        pwsh -NoProfile -File $PSCommandPath -PidFile $PidFile -Role launcher | Out-Host
        exit 0
    }
    'launcher' {
        $psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
        $psi.UseShellExecute = $false
        foreach ($a in @('-NoProfile', '-File', $PSCommandPath, '-PidFile', $PidFile, '-Role', 'sleeper')) {
            $psi.ArgumentList.Add($a)
        }
        [void][System.Diagnostics.Process]::Start($psi)
        exit 0
    }
    'sleeper' {
        Start-Sleep -Seconds 600
    }
}
