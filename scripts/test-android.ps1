#Requires -Version 7
<#
.SYNOPSIS
    BlazorNative — Android instrumented test runner with auto-emulator boot.

.DESCRIPTION
    One-command "boot AVD if needed + run ./gradlew connectedAndroidTest". Use
    this instead of manually opening an emulator window + running gradle.

    Detects a running emulator/device via `adb devices`. If none is connected,
    starts the AVD named by -AvdName (default: blazornative-pixel6-x86_64) in
    a detached process (-no-window by default, +visible window with -ShowEmulator).
    Waits for full boot via sys.boot_completed before invoking gradle.

    Leaves the emulator running on exit by default (faster subsequent runs).
    Pass -ShutdownEmulator to terminate it at the end.

.PARAMETER AvdName
    AVD to start if no device is connected. Default: blazornative-pixel6-x86_64
    (created by setup.ps1 section 7c).

.PARAMETER ShowEmulator
    Launch the emulator with its GUI window. Default: headless (-no-window).

.PARAMETER ShutdownEmulator
    Kill the emulator after the test run completes. Default: leave it running.

.PARAMETER GradleArgs
    Extra args passed to ./gradlew. Default: "connectedAndroidTest". Pass
    e.g. "connectedAndroidTest --info" to debug, or "installDebug" to just
    install the app without running tests.

.EXAMPLE
    .\scripts\test-android.ps1
    # Default: headless emulator if needed, runs connectedAndroidTest, leaves emulator up

.EXAMPLE
    .\scripts\test-android.ps1 -ShowEmulator -GradleArgs "connectedAndroidTest --info"
    # Visible emulator + verbose gradle output (useful for debugging)
#>

param(
    [string]$AvdName          = "blazornative-pixel6-x86_64",
    [switch]$ShowEmulator,
    [switch]$ShutdownEmulator,
    [string]$GradleArgs       = "connectedAndroidTest"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Environment
# ─────────────────────────────────────────────────────────────────────────────

$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot "..")
$jniModule  = Join-Path $repoRoot "src\BlazorNative.Jni"

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

# ANDROID_HOME — defaults to user-local SDK if not set.
if (-not $env:ANDROID_HOME) {
    $env:ANDROID_HOME = Join-Path $env:USERPROFILE "AppData\Local\Android\Sdk"
}
$adb       = Join-Path $env:ANDROID_HOME "platform-tools\adb.exe"
$emulator  = Join-Path $env:ANDROID_HOME "emulator\emulator.exe"

if (-not (Test-Path $adb))      { Write-Host "  ✗  adb not found at $adb. Run setup.ps1 section 7." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $emulator)) { Write-Host "  ✗  emulator not found at $emulator. Run setup.ps1 section 7." -ForegroundColor Red; exit 1 }

$env:PATH = "$env:JAVA_HOME\bin;$(Split-Path $adb);$(Split-Path $emulator);$env:PATH"

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

function Wait-ForBoot([string]$device, [int]$timeoutSeconds = 180) {
    Write-Step "Waiting for $device to finish booting (timeout ${timeoutSeconds}s)..."
    & $adb -s $device wait-for-device
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $booted = & $adb -s $device shell getprop sys.boot_completed 2>&1
        if ($booted -eq "1") {
            # Extra: wait for package manager to be queryable (avoids race installs)
            & $adb -s $device shell "pm list packages > /dev/null" 2>&1 | Out-Null
            Write-OK "$device booted"
            return $true
        }
        Start-Sleep -Seconds 2
    }
    Write-Fail "Timed out waiting for $device to boot"
    return $false
}

function Start-Emulator([string]$avdName, [bool]$visible) {
    Write-Step "Starting emulator '$avdName' (visible=$visible)..."
    $emuArgs = @("-avd", $avdName, "-no-snapshot")
    if (-not $visible) { $emuArgs += "-no-window" }
    # Detached background process — emulator stays alive after this script exits.
    $proc = Start-Process -FilePath $emulator -ArgumentList $emuArgs -PassThru -WindowStyle Hidden
    Write-Step "Emulator PID $($proc.Id) launched"

    # Wait for `adb devices` to show one
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        $dev = Get-ConnectedDevice
        if ($dev) { return $dev }
        Start-Sleep -Seconds 2
    }
    Write-Fail "Emulator started (PID $($proc.Id)) but adb didn't see it within 60s"
    return $null
}

# ─────────────────────────────────────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  BlazorNative — Android instrumented test runner" -ForegroundColor White
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray

# Ensure adb server is alive
& $adb start-server 2>&1 | Out-Null

# 1. Detect or start device
$device = Get-ConnectedDevice
$emulatorStartedByUs = $false
if ($device) {
    Write-OK "Connected device already present: $device"
} else {
    Write-Step "No device connected; starting AVD '$AvdName'"
    $device = Start-Emulator -avdName $AvdName -visible $ShowEmulator
    if (-not $device) { exit 1 }
    $emulatorStartedByUs = $true
}

# 2. Wait for full boot
if (-not (Wait-ForBoot -device $device)) { exit 1 }

# 3. Run gradle test
Write-Host ""
Write-Step "Running ./gradlew $GradleArgs from $jniModule"
Push-Location $jniModule
try {
    $gradleArgsArray = $GradleArgs -split ' '
    & .\gradlew.bat @gradleArgsArray --no-daemon
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

Write-Host ""

# 4. Result + optional logcat tail for diagnosis
if ($exitCode -eq 0) {
    Write-OK "Gradle exited 0 — tests passed"
} else {
    Write-Fail "Gradle exited $exitCode — see test report"
    Write-Host "     Report: $jniModule\build\reports\androidTests\connected\debug\index.html" -ForegroundColor DarkGray
    Write-Host ""
    Write-Step "Last 20 BlazorNative logcat lines for diagnosis:"
    & $adb -s $device logcat -d -s BlazorNative -t 20 2>&1 | Out-Host
}

# 5. Optional emulator shutdown
if ($ShutdownEmulator -or $emulatorStartedByUs -and $ShutdownEmulator) {
    Write-Host ""
    Write-Step "Shutting down emulator $device..."
    & $adb -s $device emu kill 2>&1 | Out-Null
    Write-OK "Emulator $device terminated"
} elseif ($emulatorStartedByUs) {
    Write-Host ""
    Write-Step "Emulator $device left running for faster subsequent runs. Stop with:"
    Write-Host "       adb -s $device emu kill" -ForegroundColor Cyan
    Write-Host "     OR pass -ShutdownEmulator to this script next time." -ForegroundColor DarkGray
}

Write-Host ""
exit $exitCode
