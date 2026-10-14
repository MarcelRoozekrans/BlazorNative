#Requires -RunAsAdministrator
<#
.SYNOPSIS
    BlazorNative — full prerequisite installer for Windows 11
.DESCRIPTION
    Installs and configures everything needed to build and run BlazorNative:
      • the .NET 10 SDK band pinned in global.json
      • PowerShell 7 (pwsh) — the .NET suite's reference-drift fixtures run
        scripts/generate-reference.ps1 through it
      • Temurin JDK 25, the version CI builds on (Android toolchain + Gradle daemon)
      • Android SDK + NDK 26.3 + AVD
      • Bionic NativeAOT toolchain verification (linux-bionic publishes)

    Run from the repo root, in Windows PowerShell 5.1 or PowerShell 7:
        powershell -ExecutionPolicy Bypass -File setup.ps1

    On Linux and macOS, use setup.sh instead.

    Flags:
        -SkipAndroid     Skip Android SDK + NDK installation
        -Verbose         Show detailed output from installers
#>

param(
    [switch]$SkipAndroid,
    [switch]$Verbose
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────

$script:passed  = 0
$script:skipped = 0
$script:failed  = 0
$script:envChanged = $false

# The Temurin JDK installed for Gradle and sdkmanager — the one CI provisions (#404).
$jdkMajor = 25

function Write-Header([string]$text) {
    Write-Host ""
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host "  $("─" * ($text.Length))" -ForegroundColor DarkGray
}

function Write-Step([string]$text) {
    Write-Host "  ⟶  $text" -ForegroundColor Gray
}

function Write-OK([string]$text) {
    Write-Host "  ✓  $text" -ForegroundColor Green
    $script:passed++
}

function Write-Skip([string]$text) {
    Write-Host "  ○  $text" -ForegroundColor DarkGray
    $script:skipped++
}

function Write-Fail([string]$text) {
    Write-Host "  ✗  $text" -ForegroundColor Red
    $script:failed++
}

function Write-Warn([string]$text) {
    Write-Host "  ⚠  $text" -ForegroundColor Yellow
}

function Command-Exists([string]$cmd) {
    return $null -ne (Get-Command $cmd -ErrorAction SilentlyContinue)
}

function Version-AtLeast([string]$cmd, [string]$args, [version]$min) {
    try {
        $raw = & $cmd $args 2>&1 | Select-Object -First 1
        $ver = [version]($raw -replace '[^\d\.]','')
        return $ver -ge $min
    } catch { return $false }
}

function Invoke-Winget([string]$id, [string]$label) {
    Write-Step "Installing $label via winget..."
    $verboseFlag = if ($Verbose) { @() } else { @("--disable-interactivity") }
    winget install --id $id --accept-source-agreements --accept-package-agreements @verboseFlag
    if ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq -1978335189) {
        # -1978335189 = APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED
        Write-OK $label
    } else {
        Write-Fail "$label (winget exit code: $LASTEXITCODE)"
    }
}

function Refresh-Path {
    $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH","Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("PATH","User")
}

# ─────────────────────────────────────────────────────────────────────────────
# Banner
# ─────────────────────────────────────────────────────────────────────────────

Clear-Host
Write-Host ""
Write-Host "  ██████╗ ██╗      █████╗ ███████╗ ██████╗ ██████╗ ███╗   ██╗ █████╗ ████████╗██╗██╗   ██╗███████╗" -ForegroundColor Blue
Write-Host "  ██╔══██╗██║     ██╔══██╗╚══███╔╝██╔═══██╗██╔══██╗████╗  ██║██╔══██╗╚══██╔══╝██║██║   ██║██╔════╝" -ForegroundColor Blue
Write-Host "  ██████╔╝██║     ███████║  ███╔╝ ██║   ██║██████╔╝██╔██╗ ██║███████║   ██║   ██║██║   ██║█████╗  " -ForegroundColor Blue
Write-Host "  ██╔══██╗██║     ██╔══██║ ███╔╝  ██║   ██║██╔══██╗██║╚██╗██║██╔══██║   ██║   ██║╚██╗ ██╔╝██╔══╝  " -ForegroundColor Blue
Write-Host "  ██████╔╝███████╗██║  ██║███████╗╚██████╔╝██║  ██║██║ ╚████║██║  ██║   ██║   ██║ ╚████╔╝ ███████╗" -ForegroundColor Blue
Write-Host "  ╚═════╝ ╚══════╝╚═╝  ╚═╝╚══════╝ ╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═══╝╚═╝  ╚═╝   ╚═╝   ╚═╝  ╚═══╝  ╚══════╝" -ForegroundColor Blue
Write-Host ""
Write-Host "  BlazorNative — Prerequisite Installer" -ForegroundColor White
Write-Host "  .NET → NativeAOT → Native mobile" -ForegroundColor DarkGray
Write-Host ""

if ($SkipAndroid)    { Write-Warn "Skipping Android (--SkipAndroid flag set)" }

# ─────────────────────────────────────────────────────────────────────────────
# 1. Winget check
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "1 · Package manager"

if (Command-Exists "winget") {
    Write-OK "winget is available"
} else {
    Write-Fail "winget not found — install App Installer from the Microsoft Store first"
    Write-Host "     https://apps.microsoft.com/store/detail/app-installer/9NBLGGH4NNS1" -ForegroundColor DarkGray
    exit 1
}

# ─────────────────────────────────────────────────────────────────────────────
# 2. .NET 10 SDK
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "2 · .NET SDK (the band global.json pins)"

# global.json pins an exact version with rollForward latestPatch, so any SDK in
# the same feature band at or above it satisfies it: 10.0.401 → 10.0.4xx >= 401.
# Any other 10.x SDK installs fine and then fails every build on global.json.
$sdkPin = [version](Get-Content (Join-Path $PSScriptRoot "global.json") -Raw | ConvertFrom-Json).sdk.version
function Find-PinnedSdk {
    if (-not (Command-Exists "dotnet")) { return $null }
    foreach ($line in (dotnet --list-sdks 2>$null)) {
        if ($line -notmatch '^(\d+)\.(\d+)\.(\d+)\s') { continue }
        $v = [version]"$($Matches[1]).$($Matches[2]).$($Matches[3])"
        if ($v.Major -eq $sdkPin.Major -and $v.Minor -eq $sdkPin.Minor -and
            [math]::Floor($v.Build / 100) -eq [math]::Floor($sdkPin.Build / 100) -and
            $v.Build -ge $sdkPin.Build) { return $v }
    }
    return $null
}

$foundSdk = Find-PinnedSdk
if ($foundSdk) {
    Write-OK ".NET SDK $foundSdk satisfies global.json ($sdkPin, latestPatch)"
} else {
    Invoke-Winget "Microsoft.DotNet.SDK.$($sdkPin.Major)" ".NET $($sdkPin.Major) SDK"
    Refresh-Path
    $foundSdk = Find-PinnedSdk
    if ($foundSdk) {
        Write-OK ".NET SDK $foundSdk satisfies global.json ($sdkPin, latestPatch)"
    } elseif (Command-Exists "dotnet") {
        Write-Fail "no SDK in the $($sdkPin.Major).$($sdkPin.Minor).$([math]::Floor($sdkPin.Build / 100))xx band >= $sdkPin after install — install it from https://dotnet.microsoft.com/download/dotnet/$($sdkPin.Major).$($sdkPin.Minor)"
        exit 1
    } else {
        Write-Fail "dotnet command not found after install — restart terminal and re-run"
        exit 1
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 2b. PowerShell 7 — tests/BlazorNative.Runtime.Tests' reference-drift fixtures
#     start `pwsh` by name; Windows PowerShell 5.1 is `powershell` and does not count.
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "2b · PowerShell 7 (pwsh)"

if (Command-Exists "pwsh") {
    Write-OK "pwsh is available"
} else {
    Invoke-Winget "Microsoft.PowerShell" "PowerShell 7"
    Refresh-Path
    if (-not (Command-Exists "pwsh")) {
        Write-Warn "pwsh not on PATH yet — open a fresh terminal before running the .NET suite"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 3. Java (required for Android)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "3 · Java $jdkMajor (Android toolchain + Gradle daemon)"

    # The same JDK CI provisions (#404): ci.yml and android-instrumented.yml set
    # java-version to it, and JdkVersionDriftTests holds them, this and setup.sh
    # together. The wrapper's Gradle 9.8 runs on 17-25; Android needs 17+.
    $jdkFound = (Get-ChildItem -Path "C:\Program Files\Eclipse Adoptium\" -Directory -ErrorAction SilentlyContinue |
                 Where-Object { $_.Name -match "^jdk-$jdkMajor\." } | Select-Object -First 1)
    if ($jdkFound) {
        Write-OK "Temurin JDK $jdkMajor already installed at $($jdkFound.FullName)"
    } else {
        Invoke-Winget "EclipseAdoptium.Temurin.$jdkMajor.JDK" "Temurin JDK $jdkMajor"
        Refresh-Path
    }

    # Verify SOME compatible JDK is on PATH
    if (Command-Exists "java") {
        $jver = java -version 2>&1 | Select-Object -First 1
        $jmajor = if ($jver -match 'version "(?:1\.)?(\d+)') { [int]$Matches[1] } else { 0 }
        if ($jmajor -ge 17 -and $jmajor -le 25) {
            Write-OK "Gradle-compatible JDK on PATH ($($jver.ToString().Trim()))"
        } else {
            Write-Warn "Active JDK on PATH is $($jver.ToString().Trim()) — Gradle 9.8 needs 17-25. Point JAVA_HOME at the Temurin JDK $jdkMajor install."
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 4. Android SDK (via Android command-line tools)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "4 · Android SDK + NDK + AVD"

    # No `??` here: it is PowerShell 7 syntax, and Windows PowerShell 5.1 then
    # fails to parse the whole script.
    $androidHome = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "$env:USERPROFILE\AppData\Local\Android\Sdk" }
    $cmdToolsDest = "$androidHome\cmdline-tools"
    $sdkmanager = "$cmdToolsDest\latest\bin\sdkmanager.bat"
    $avdmanager = "$cmdToolsDest\latest\bin\avdmanager.bat"

    # 4a. Command-line tools — install if sdkmanager isn't already present
    # (Guard on $sdkmanager not adb.exe, so a partial previous run that extracted
    # cmdline-tools but didn't install platform-tools doesn't re-download.)
    if (-not (Test-Path $sdkmanager)) {
        Write-Step "Downloading Android command-line tools..."
        $cmdToolsUrl  = "https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip"
        $cmdToolsZip  = "$env:TEMP\android-cmdtools.zip"

        New-Item -ItemType Directory -Force -Path $cmdToolsDest | Out-Null

        # Clean up any stale 'latest' from a previous failed run that left
        # the dir but without a working sdkmanager.
        if (Test-Path "$cmdToolsDest\latest") {
            Remove-Item -Recurse -Force "$cmdToolsDest\latest"
        }

        Invoke-WebRequest $cmdToolsUrl -OutFile $cmdToolsZip
        Expand-Archive $cmdToolsZip -DestinationPath $cmdToolsDest -Force

        # Extracted folder is named 'cmdline-tools' — rename to 'latest' as sdkmanager requires.
        $extracted = Get-ChildItem $cmdToolsDest | Where-Object { $_.Name -ne "latest" } | Select-Object -First 1
        if ($extracted) { Rename-Item $extracted.FullName "latest" }

        [Environment]::SetEnvironmentVariable("ANDROID_HOME", $androidHome, "User")
        [Environment]::SetEnvironmentVariable("PATH", $env:PATH + ";$androidHome\platform-tools", "User")
        Refresh-Path
    }

    # 4b. Install SDK packages (idempotent — sdkmanager skips already-installed)
    if (Test-Path $sdkmanager) {
        # sdkmanager + avdmanager need Java 17+ (class file 61.0). Marcel's PATH
        # may put Oracle's java8path first; force JAVA_HOME for these invocations.
        $jdkFound = (Get-ChildItem -Path "C:\Program Files\Eclipse Adoptium\" -Directory -ErrorAction SilentlyContinue |
                     Where-Object { $_.Name -match "^jdk-$jdkMajor\." } | Select-Object -First 1)
        if ($jdkFound) {
            $env:JAVA_HOME = $jdkFound.FullName
            Write-Step "Using JAVA_HOME=$($jdkFound.FullName) for sdkmanager (needs Java 17+)"
        } else {
            Write-Warn "JDK $jdkMajor not found at Adoptium default location — sdkmanager may fail with Java version mismatch"
        }

        Write-Step "Installing Android SDK packages (platform-tools, build-tools, platforms-37, NDK 26.3, x86_64 system image)..."
        # Auto-accept all license prompts
        & cmd /c "echo y| `"$sdkmanager`" --licenses" 2>&1 | Out-Null
        echo "y" | & $sdkmanager `
            "platform-tools" `
            "build-tools;34.0.0" `
            "platforms;android-37.0" `
            "ndk;26.3.11579264" `
            "system-images;android-34;google_apis;x86_64" `
            "emulator"
        if ($LASTEXITCODE -eq 0) {
            Write-OK "Android SDK packages installed at $androidHome"
        } else {
            Write-Fail "sdkmanager install failed (exit $LASTEXITCODE)"
        }

        # Set ANDROID_NDK_HOME so NDK-consuming tools can find the toolchain
        $ndkRoot = "$androidHome\ndk\26.3.11579264"
        if (Test-Path $ndkRoot) {
            [Environment]::SetEnvironmentVariable("ANDROID_NDK_HOME", $ndkRoot, "User")
            $env:ANDROID_NDK_HOME = $ndkRoot
            Write-OK "ANDROID_NDK_HOME set to $ndkRoot"
        } else {
            Write-Fail "NDK 26.3 not found at $ndkRoot after install"
        }
    } else {
        Write-Fail "sdkmanager not found at $sdkmanager"
    }

    # 4c. Create AVD for Phase 2.2 emulator (idempotent)
    if (Test-Path $avdmanager) {
        $avdName = "blazornative-pixel6-x86_64"
        $existingAvds = & $avdmanager list avd -c 2>&1
        if ($LASTEXITCODE -eq 0 -and ($existingAvds | Out-String) -match "(?m)^$([regex]::Escape($avdName))$") {
            Write-OK "AVD $avdName already exists"
        } else {
            Write-Step "Creating AVD $avdName..."
            echo "no" | & $avdmanager create avd `
                -n $avdName `
                -k "system-images;android-34;google_apis;x86_64" `
                -d "pixel_6"
            if ($LASTEXITCODE -eq 0) {
                Write-OK "AVD $avdName created"
            } else {
                Write-Fail "avdmanager create avd failed (exit $LASTEXITCODE)"
            }
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 5. Bionic NativeAOT toolchain (Phase 3.0c) — verify + document, no installs
#    samples/BlazorNative.SampleApp, the publish head since Phase 8.0,
#    cross-compiles to linux-bionic-{x64,arm64} .so files directly on Windows
#    via the runtime-pack bypass (the RID-specific ILCompiler packages don't
#    exist for .NET 10 — 3.0b Gate 4 RED).
#    Pinned working combo:
#      • the .NET SDK band in global.json (section 2)
#      • ILCompiler + Microsoft.NETCore.App.Runtime.NativeAOT.linux-bionic-*
#        runtime packs 10.0.9 (pinned via RuntimeFrameworkVersion in
#        BlazorNative.SampleApp.csproj)
#      • Android NDK 26.3.11579264 (installed by section 4)
#      • vendored build/BionicNativeAot.targets (NDK shim + linker args)
#    The targets read ANDROID_NDK_ROOT (not ANDROID_NDK_HOME) — this section
#    mirrors section 4's NDK path into it.
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "5 · Bionic NativeAOT toolchain (verify env for linux-bionic publishes)"

    $bionicNdkPin = "26.3.11579264"
    # Anchored so a longer revision (e.g. "26.3.115792640") can't sneak past;
    # (?m) because source.properties is a multi-line key=value file.
    $bionicNdkRevisionPattern = '(?m)^\s*Pkg\.Revision\s*=\s*' + [regex]::Escape($bionicNdkPin) + '\s*$'

    $bionicNdkRoot = "$env:LOCALAPPDATA\Android\Sdk\ndk\$bionicNdkPin"
    $bionicNdkPathSource = "the default SDK location (`$env:LOCALAPPDATA\Android\Sdk)"
    if ($env:ANDROID_NDK_HOME -and (Test-Path $env:ANDROID_NDK_HOME)) {
        $bionicNdkRoot = $env:ANDROID_NDK_HOME
        $bionicNdkPathSource = "ANDROID_NDK_HOME"
    }

    # The path existing isn't enough — a stray ANDROID_NDK_HOME can point at a
    # different NDK. Verify the pinned revision from the NDK's own source.properties.
    $bionicNdkProps = Join-Path $bionicNdkRoot "source.properties"
    $bionicNdkRevisionOk = (Test-Path $bionicNdkProps) -and
        ((Get-Content $bionicNdkProps -Raw) -match $bionicNdkRevisionPattern)

    if (-not (Test-Path $bionicNdkRoot)) {
        Write-Fail "NDK $bionicNdkPin not found at $bionicNdkRoot — section 4 needs to install it first"
    } elseif (-not $bionicNdkRevisionOk) {
        Write-Fail "NDK at $bionicNdkRoot (resolved from $bionicNdkPathSource) is not revision $bionicNdkPin — source.properties is missing or reports a different Pkg.Revision. Point ANDROID_NDK_HOME at NDK $bionicNdkPin, or unset it so the default SDK path is used."
    } elseif ($env:ANDROID_NDK_ROOT -and (Test-Path $env:ANDROID_NDK_ROOT)) {
        # A pre-existing ANDROID_NDK_ROOT is what the bionic publish actually
        # reads — verify ITS revision too, not just that the path exists.
        $ndkRootProps = Join-Path $env:ANDROID_NDK_ROOT "source.properties"
        $ndkRootRevisionOk = (Test-Path $ndkRootProps) -and
            ((Get-Content $ndkRootProps -Raw) -match $bionicNdkRevisionPattern)
        if ($ndkRootRevisionOk) {
            Write-OK "ANDROID_NDK_ROOT already set to $env:ANDROID_NDK_ROOT (revision $bionicNdkPin verified)"
        } else {
            Write-Fail "NDK at $env:ANDROID_NDK_ROOT (resolved from ANDROID_NDK_ROOT) is not revision $bionicNdkPin — source.properties is missing or reports a different Pkg.Revision. Point ANDROID_NDK_ROOT at NDK $bionicNdkPin, or unset it so setup can set it to the pinned default."
        }
    } else {
        [Environment]::SetEnvironmentVariable("ANDROID_NDK_ROOT", $bionicNdkRoot, "User")
        $env:ANDROID_NDK_ROOT = $bionicNdkRoot
        $script:envChanged = $true
        Write-OK "ANDROID_NDK_ROOT set to $bionicNdkRoot"
    }

    Write-Host ""
    Write-Host "  Pinned toolchain combo:" -ForegroundColor DarkGray
    Write-Host "    .NET SDK $sdkPin band (global.json) · ILCompiler/NativeAOT runtime packs 10.0.9 · NDK $bionicNdkPin" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Publish the Android native host (from repo root):" -ForegroundColor DarkGray
    Write-Host "    dotnet publish samples\BlazorNative.SampleApp -c Release -r linux-bionic-x64" -ForegroundColor Cyan
    Write-Host "    dotnet publish samples\BlazorNative.SampleApp -c Release -r linux-bionic-arm64" -ForegroundColor Cyan
    Write-Host ""
}

# ─────────────────────────────────────────────────────────────────────────────
# 6. Restore NuGet packages
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "6 · NuGet restore"

if (Test-Path "BlazorNative.sln") {
    Write-Step "Restoring NuGet packages..."
    dotnet restore BlazorNative.sln
    if ($LASTEXITCODE -eq 0) { Write-OK "NuGet packages restored" }
    else { Write-Fail "NuGet restore failed" }
} else {
    Write-Warn "BlazorNative.sln not found in current directory — skipping restore"
    Write-Warn "Run 'dotnet restore BlazorNative.sln' from the repo root"
}

# ─────────────────────────────────────────────────────────────────────────────
# 7. Verify the dev build (fast — no AOT publish)
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "7 · Smoke test — Debug build + the .NET suite"

if (Test-Path "BlazorNative.sln") {
    Write-Step "Building BlazorNative.sln (Debug)..."
    dotnet build BlazorNative.sln -c Debug --nologo -v q
    if ($LASTEXITCODE -eq 0) { Write-OK "BlazorNative.sln builds successfully (Debug)" }
    else { Write-Fail "Debug build failed — check output above" }

    # --blame-hang-timeout turns #454, a local hang in the reference-drift
    # fixture, into a named failure with dumps instead of a silent wait.
    Write-Step "Running the .NET suite (about a minute)..."
    dotnet test BlazorNative.sln --no-build -c Debug --nologo -v q --blame-hang --blame-hang-timeout 10m
    if ($LASTEXITCODE -eq 0) { Write-OK ".NET suite passed" }
    else { Write-Fail ".NET suite reported failures — review above" }
} else {
    Write-Warn "BlazorNative.sln not found — skipping smoke test"
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  Setup complete (.NET 10 · JDK $jdkMajor · Android SDK/NDK 26.3 · bionic NativeAOT)" -ForegroundColor White
Write-Host ""
Write-Host "    ✓ Passed  : $script:passed" -ForegroundColor Green
if ($script:skipped -gt 0) {
Write-Host "    ○ Skipped : $script:skipped" -ForegroundColor DarkGray
}
if ($script:failed -gt 0) {
Write-Host "    ✗ Failed  : $script:failed" -ForegroundColor Red
}
Write-Host ""

if ($script:envChanged) {
    Write-Warn "Environment variables (ANDROID_NDK_ROOT/ANDROID_NDK_HOME and/or PATH) were updated."
    Write-Warn "Open a fresh PowerShell/terminal so new processes inherit the changes."
    Write-Host ""
}

if ($script:failed -eq 0) {
    Write-Host "  You're ready to go!" -ForegroundColor White
    Write-Host ""
    Write-Host "  Native fast lane (watch .NET src -> publish -> PreviewHost tree):" -ForegroundColor DarkGray
    Write-Host "    powershell -ExecutionPolicy Bypass -File scripts\devloop.ps1" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Run all .NET tests:" -ForegroundColor DarkGray
    Write-Host "    dotnet test BlazorNative.sln" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Publish the native runtime (pinned combo — see section 5):" -ForegroundColor DarkGray
    Write-Host "    dotnet publish samples\BlazorNative.SampleApp -c Release -r win-x64" -ForegroundColor Cyan
    Write-Host "    dotnet publish samples\BlazorNative.SampleApp -c Release -r linux-bionic-x64" -ForegroundColor Cyan
    Write-Host "    dotnet publish samples\BlazorNative.SampleApp -c Release -r linux-bionic-arm64" -ForegroundColor Cyan
} else {
    Write-Host "  Some steps failed. Review the ✗ items above and re-run." -ForegroundColor Yellow
    Write-Host "  You can re-run safely — already-installed items are skipped." -ForegroundColor DarkGray
}

Write-Host ""
