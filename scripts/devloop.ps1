#Requires -Version 7
<#
.SYNOPSIS
    BlazorNative — the dev inner loop (Phase 4.3): watch → publish → preview.

.DESCRIPTION
    FAST-RESTART, NOT HOT-RELOAD (by design, not omission): JNA's Native.load
    is process-lifetime — there is no unload API and Windows locks the loaded
    dll — so a warm JVM can never pick up a rebuilt native library, and
    NativeAOT binaries cannot hot-patch. Each cycle therefore restarts a tiny
    host process; the loop makes that restart one keystroke (or zero: save a
    file and watch).

    Default (JVM fast lane):
        dotnet publish win-x64 → gradlew runPreviewHost (console tree dump +
        stage timings). Watches src/BlazorNative.{Core,Renderer,Http,
        Components,Runtime}/**/*.cs (500 ms debounce; obj/bin excluded — the
        publish itself regenerates obj/**/*.cs and must not retrigger) and
        re-runs the cycle on every change. Ctrl+C exits.

    -Android (ADB device lane):
        dotnet publish linux-bionic-x64 → gradlew installDebug (preBuild
        re-stages the fresh .so into jniLibs) → am force-stop + am start
        MainActivity with EXTRA_COMPONENT → poll logcat for the
        "[BOOT] mounted <Component>" marker MainActivity logs on a successful
        mount. Requires a RUNNING emulator/device — this script fails fast
        and points at scripts/test-android.ps1 instead of booting one.
        NOTE: only bionic-x64 is republished (the AVD lane). A physical arm64
        device also needs `dotnet publish ... -r linux-bionic-arm64` — the
        staged arm64 .so goes stale otherwise.

    Gradle daemon note (design risk: "a lingering daemon locks the dll"):
    runPreviewHost is a JavaExec, which always FORKS a fresh java process —
    the daemon itself never loads BlazorNative.Runtime.dll, and PreviewHost
    exits after its dump. The daemon is therefore kept (it is what makes the
    warm cycle fast); the script verifies the exec's exit code instead of
    forcing --no-daemon.

.PARAMETER Component
    Mount-registry component name to preview (default: BnDemo).

.PARAMETER Android
    Use the ADB device lane instead of the JVM fast lane.

.PARAMETER Once
    Run a single cycle and exit with its code (no watcher) — CI-able smoke /
    timing capture.

.EXAMPLE
    .\scripts\devloop.ps1
    # Watch the .NET source; every save → win-x64 publish → BnDemo tree dump.

.EXAMPLE
    .\scripts\devloop.ps1 -Android -Once -Component BnDemo
    # One measured publish → install → launch → mounted round-trip on the AVD.
#>

param(
    [string]$Component = "BnDemo",
    [switch]$Android,
    [switch]$Once
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Environment (test-android.ps1 conventions)
# ─────────────────────────────────────────────────────────────────────────────

$repoRoot  = Resolve-Path (Join-Path $PSScriptRoot "..")
$jniModule = Join-Path $repoRoot "src\BlazorNative.Jni"

# JAVA_HOME — Gradle 8.x daemon needs JDK 8-23. Pin to Temurin JDK 21 if present.
$jdk21 = (Get-ChildItem "C:\Program Files\Eclipse Adoptium\" -Directory -ErrorAction SilentlyContinue |
          Where-Object { $_.Name -match "^jdk-21" } | Select-Object -First 1)
if ($jdk21) {
    $env:JAVA_HOME = $jdk21.FullName
} elseif (-not $env:JAVA_HOME) {
    Write-Host "  ✗  No Temurin JDK 21 found at default Adoptium location and JAVA_HOME not set." -ForegroundColor Red
    Write-Host "     Run setup.ps1 to install JDK 21." -ForegroundColor DarkGray
    exit 1
}

# ANDROID_HOME — defaults to user-local SDK if not set (AGP needs it even for
# the JVM lane's gradle invocation).
if (-not $env:ANDROID_HOME) {
    $env:ANDROID_HOME = Join-Path $env:USERPROFILE "AppData\Local\Android\Sdk"
}
$adb = Join-Path $env:ANDROID_HOME "platform-tools\adb.exe"

# ANDROID_NDK_ROOT — the bionic cross-compile (build/BionicNativeAot.targets)
# reads source.properties from it. The revision mirrors ci.yml's NDK_PIN /
# setup.ps1's install.
if (-not $env:ANDROID_NDK_ROOT) {
    $env:ANDROID_NDK_ROOT = Join-Path $env:ANDROID_HOME "ndk\26.3.11579264"
}

# vswhere on PATH — the ILC win-x64 link step invokes it by bare name.
$env:PATH = "$env:JAVA_HOME\bin;${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"

# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────

function Write-Step([string]$text) { Write-Host "  ⟶  $text" -ForegroundColor Gray }
function Write-OK([string]$text)   { Write-Host "  ✓  $text" -ForegroundColor Green }
function Write-Fail([string]$text) { Write-Host "  ✗  $text" -ForegroundColor Red }

function Get-ConnectedDevice {
    $devices = & $adb devices 2>&1 | Select-Object -Skip 1 |
               Where-Object { $_ -match "^(\S+)\s+device$" } |
               ForEach-Object { ($_ -split "\s+")[0] }
    return $devices | Select-Object -First 1
}

<# Publishes BlazorNative.Runtime for $Rid; returns the elapsed [TimeSpan] or
   $null on failure (with the dll-lock hint when the log smells of one). #>
function Invoke-Publish([string]$Rid) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $log = & dotnet publish (Join-Path $repoRoot "src\BlazorNative.Runtime") -c Release -r $Rid -tl:off -nologo 2>&1
    $sw.Stop()
    if ($LASTEXITCODE -ne 0) {
        $log | Select-Object -Last 15 | Out-Host
        if ("$log" -match 'used by another process|cannot access the file|MSB3026|MSB3027|LNK1104') {
            Write-Fail "publish -r $Rid failed: the output binary looks LOCKED."
            Write-Host "     Likely cause: a process still holds BlazorNative.Runtime.$(($Rid -eq 'win-x64') ? 'dll' : 'so')." -ForegroundColor DarkGray
            Write-Host "     PreviewHost exits after every dump and the gradle daemon never loads the dll" -ForegroundColor DarkGray
            Write-Host "     (JavaExec forks) — look for a stray java.exe or another consumer and retry." -ForegroundColor DarkGray
        } else {
            Write-Fail "dotnet publish -r $Rid failed (exit $LASTEXITCODE)"
        }
        return $null
    }
    return $sw.Elapsed
}

# ─────────────────────────────────────────────────────────────────────────────
# The two lanes (one cycle each; return 0/1)
# ─────────────────────────────────────────────────────────────────────────────

function Invoke-JvmCycle {
    $total = [System.Diagnostics.Stopwatch]::StartNew()

    Write-Step "dotnet publish win-x64 ..."
    $publish = Invoke-Publish -Rid "win-x64"
    if ($null -eq $publish) { return 1 }

    Write-Step "gradlew runPreviewHost -Pcomponent=$Component ..."
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Push-Location $jniModule
    try {
        & .\gradlew.bat runPreviewHost "-Pcomponent=$Component" -q --console=plain 2>&1 | Out-Host
        $rc = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    $sw.Stop(); $total.Stop()

    if ($rc -ne 0) {
        Write-Fail "PreviewHost cycle failed (gradle exit $rc)"
        return 1
    }
    Write-Host ("  [CYCLE] publish {0:n1} s | preview {1:n1} s | total {2:n1} s" -f
        $publish.TotalSeconds, $sw.Elapsed.TotalSeconds, $total.Elapsed.TotalSeconds) -ForegroundColor Cyan
    return 0
}

function Invoke-AndroidCycle {
    if (-not (Test-Path $adb)) {
        Write-Fail "adb not found at $adb. Run setup.ps1 section 7."
        return 1
    }
    $device = Get-ConnectedDevice
    if (-not $device) {
        Write-Fail "No device/emulator connected — the ADB lane needs a running one. Boot the AVD first:"
        Write-Host "     powershell -ExecutionPolicy Bypass -File scripts/test-android.ps1 -GradleArgs installDebug" -ForegroundColor Cyan
        return 1
    }

    $total = [System.Diagnostics.Stopwatch]::StartNew()

    Write-Step "dotnet publish linux-bionic-x64 ..."
    $publish = Invoke-Publish -Rid "linux-bionic-x64"
    if ($null -eq $publish) { return 1 }

    Write-Step "gradlew installDebug (preBuild re-stages the fresh .so) ..."
    $swInstall = [System.Diagnostics.Stopwatch]::StartNew()
    Push-Location $jniModule
    try {
        & .\gradlew.bat installDebug -q --console=plain 2>&1 | Out-Host
        $rc = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    $swInstall.Stop()
    if ($rc -ne 0) {
        Write-Fail "installDebug failed (gradle exit $rc)"
        return 1
    }

    Write-Step "launching MainActivity with EXTRA_COMPONENT=$Component ..."
    $swLaunch = [System.Diagnostics.Stopwatch]::StartNew()
    & $adb -s $device shell am force-stop io.blazornative.shell 2>&1 | Out-Null
    & $adb -s $device logcat -c 2>&1 | Out-Null
    # am start reports failures in its OUTPUT (e.g. "Error: Activity class
    # does not exist"), often with exit 0 — check both instead of letting the
    # 90 s marker timeout discover it.
    $amOut = & $adb -s $device shell am start -n io.blazornative.shell/.MainActivity `
        -e io.blazornative.shell.EXTRA_COMPONENT $Component 2>&1
    if ($LASTEXITCODE -ne 0 -or "$amOut" -match 'Error|Exception') {
        Write-Fail "am start failed (exit $LASTEXITCODE):"
        $amOut | Out-Host
        return 1
    }

    # The stopwatch end: MainActivity logs the runtime's "[BOOT] mounted X"
    # line (tag BlazorNative) once the mount frame has been delivered.
    $marker  = "[BOOT] mounted $Component"
    $pattern = [regex]::Escape($marker)
    $deadline = (Get-Date).AddSeconds(90)
    $mounted = $false
    while ((Get-Date) -lt $deadline) {
        $lines = & $adb -s $device logcat -d -s BlazorNative 2>&1
        if ($lines -match $pattern) { $mounted = $true; break }
        if ($lines -match 'Boot failed|FAIL:') { break }
        Start-Sleep -Milliseconds 250
    }
    $swLaunch.Stop(); $total.Stop()

    if (-not $mounted) {
        Write-Fail "boot marker '$marker' not seen within 90 s — last BlazorNative logcat lines:"
        & $adb -s $device logcat -d -s BlazorNative -t 20 2>&1 | Out-Host
        return 1
    }

    & $adb -s $device logcat -d -s BlazorNative 2>&1 |
        Where-Object { $_ -match '\[BOOT\]' } | Out-Host
    Write-Host ("  [CYCLE] publish {0:n1} s | install {1:n1} s | launch→mounted {2:n1} s | total {3:n1} s" -f
        $publish.TotalSeconds, $swInstall.Elapsed.TotalSeconds, $swLaunch.Elapsed.TotalSeconds, $total.Elapsed.TotalSeconds) -ForegroundColor Cyan
    return 0
}

function Invoke-Cycle {
    if ($Android) { return Invoke-AndroidCycle }
    return Invoke-JvmCycle
}

# ─────────────────────────────────────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────────────────────────────────────

$lane = $Android ? "ADB device lane (linux-bionic-x64 → AVD)" : "JVM fast lane (win-x64 → PreviewHost)"
Write-Host ""
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  BlazorNative devloop — $lane" -ForegroundColor White
Write-Host "  component: $Component | fast-restart, not hot-reload" -ForegroundColor DarkGray
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

$rc = Invoke-Cycle
if ($Once) { exit $rc }

# ── Watcher (the loop proper) ────────────────────────────────────────────────
$watchProjects = @("Core", "Renderer", "Http", "Components", "Runtime")
$queue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$watchers = @()
$subscriptions = @()
foreach ($proj in $watchProjects) {
    $w = [System.IO.FileSystemWatcher]::new((Join-Path $repoRoot "src\BlazorNative.$proj"), "*.cs")
    $w.IncludeSubdirectories = $true
    $w.EnableRaisingEvents = $true
    foreach ($evtName in @("Changed", "Created", "Renamed", "Deleted")) {
        $subscriptions += Register-ObjectEvent -InputObject $w -EventName $evtName -MessageData $queue -Action {
            $path = $Event.SourceEventArgs.FullPath
            # obj/bin are BUILD OUTPUTS — the publish regenerates obj/**/*.cs,
            # which must not retrigger the cycle (infinite loop otherwise).
            if ($path -notmatch '\\(obj|bin)\\') { $Event.MessageData.Enqueue($path) }
        }
    }
    $watchers += $w
}

Write-Host ""
Write-OK "Watching src/BlazorNative.{$($watchProjects -join ',')}/**/*.cs — save a file to re-run; Ctrl+C exits"

try {
    while ($true) {
        # Block until the first change...
        $changed = $null
        while (-not $queue.TryDequeue([ref]$changed)) { Start-Sleep -Milliseconds 200 }
        # ...then debounce: drain until 500 ms of quiet (editors fire bursts).
        while ($true) {
            Start-Sleep -Milliseconds 500
            $drained = $false
            $sink = $null
            while ($queue.TryDequeue([ref]$sink)) { $drained = $true }
            if (-not $drained) { break }
        }
        Write-Host ""
        Write-Step "change detected: $changed"
        Invoke-Cycle | Out-Null
        Write-OK "watching (Ctrl+C exits)"
    }
} finally {
    foreach ($sub in $subscriptions) { Unregister-Event -SourceIdentifier $sub.Name -ErrorAction SilentlyContinue }
    $watchers | ForEach-Object { $_.Dispose() }
}
