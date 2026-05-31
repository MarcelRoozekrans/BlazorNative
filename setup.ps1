#Requires -RunAsAdministrator
<#
.SYNOPSIS
    BlazorNative — full prerequisite installer for Windows 11
.DESCRIPTION
    Installs and configures everything needed to build and run BlazorNative:
      • .NET 10 SDK
      • WSL Ubuntu (blazornative-ubuntu) + .NET 10 SDK + NativeAOT cross-toolchain
      • wasi-sdk 25.0 (pinned — newer SDKs are rejected by the workload)
      • Wasmtime CLI v45
      • Android SDK + NDK
      • Rust + wit-bindgen (optional, for WIT binding regeneration)

    Run from the repo root:
        powershell -ExecutionPolicy Bypass -File setup.ps1

    Flags:
        -SkipAndroid     Skip Android SDK + NDK installation
        -SkipWitBindgen  Skip Rust + wit-bindgen installation
        -Verbose         Show detailed output from installers
#>

param(
    [switch]$SkipAndroid,
    [switch]$SkipWitBindgen,
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
Write-Host "  .NET → WASM → Native mobile" -ForegroundColor DarkGray
Write-Host ""

if ($SkipAndroid)    { Write-Warn "Skipping Android (--SkipAndroid flag set)" }
if ($SkipWitBindgen) { Write-Warn "Skipping wit-bindgen (--SkipWitBindgen flag set)" }

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

Write-Header "2 · .NET 10 SDK"

$dotnetOk = $false
if (Command-Exists "dotnet") {
    $sdks = dotnet --list-sdks 2>&1
    if ($sdks -match "^10\.") {
        Write-OK ".NET 10 SDK already installed"
        $dotnetOk = $true
    }
}

if (-not $dotnetOk) {
    Invoke-Winget "Microsoft.DotNet.SDK.10" ".NET 10 SDK"
    Refresh-Path
}

# Verify
if (Command-Exists "dotnet") {
    $ver = (dotnet --version 2>&1).Trim()
    Write-Step "Active SDK: $ver"
} else {
    Write-Fail "dotnet command not found after install — restart terminal and re-run"
    exit 1
}

# ─────────────────────────────────────────────────────────────────────────────
# 3. WSL Ubuntu (blazornative-ubuntu) + .NET 10 SDK
#
# Replaces the wasi-experimental workload as the Bionic-cross-compile
# host. wasi-experimental sections 4 + 5 + 7c + 8c stay through
# Phase 3.0b; Phase 3.0c's atomic cleanup deletes them.
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "3 · WSL Ubuntu (blazornative-ubuntu)"

$wslDistro    = "blazornative-ubuntu"
$wslInstall   = "C:\WSL\$wslDistro"
$rootfsUrl    = "https://cloud-images.ubuntu.com/wsl/jammy/current/ubuntu-jammy-wsl-amd64-ubuntu22.04lts.rootfs.tar.gz"
$rootfsPath   = Join-Path $env:TEMP "ubuntu-jammy-wsl.tar.gz"

$existing = wsl -l -q 2>&1 | Where-Object { $_ -eq $wslDistro }
if ($existing) {
    Write-OK "$wslDistro WSL distro already imported"
} else {
    Write-Step "Downloading Ubuntu 22.04 WSL rootfs (~600 MB) ..."
    if (-not (Test-Path $rootfsPath)) {
        Invoke-WebRequest -Uri $rootfsUrl -OutFile $rootfsPath
    }
    New-Item -ItemType Directory -Force -Path $wslInstall | Out-Null
    wsl --import $wslDistro $wslInstall $rootfsPath
    if ($LASTEXITCODE -eq 0) { Write-OK "$wslDistro imported" }
    else { Write-Fail "wsl --import failed (exit $LASTEXITCODE)" }
}

# Bootstrap .NET 10 SDK + cross-toolchain inside the distro.
# NOTE: piped via `bash -c "<single-line>"` not a here-string. PowerShell here-
# strings carry CRLF, which bash treats as a literal carriage return in args —
# so `dotnet --version\r` is parsed as `dotnet--version`. The single-line
# `bash -c` form sidesteps this without needing a CRLF→LF translation step.
$bootstrapScript = "set -e; " +
    "if ! command -v dotnet &> /dev/null; then " +
        "apt-get update -qq && " +
        "apt-get install -y wget ca-certificates clang zlib1g-dev libkrb5-dev && " +
        "wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh && " +
        "bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet && " +
        "ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet; " +
    "fi; " +
    "dotnet --version"
Write-Step "Bootstrapping .NET 10 SDK inside $wslDistro ..."
wsl -d $wslDistro -e bash -c $bootstrapScript
if ($LASTEXITCODE -eq 0) { Write-OK ".NET 10 SDK ready in $wslDistro" }
else { Write-Fail "WSL bootstrap failed" }

# ─────────────────────────────────────────────────────────────────────────────
# 4. wasi-sdk 25 (pinned by the wasi-experimental workload)
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "4 · wasi-sdk 25.0"

$wasiSdkRoot   = "C:\Tools\wasi-sdk-25.0-x86_64-windows"
$wasiSdkClang  = Join-Path $wasiSdkRoot "bin\clang.exe"
$wasiSdkUrl    = "https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-25/wasi-sdk-25.0-x86_64-windows.tar.gz"

# Pick up an existing WASI_SDK_PATH if it already points at a valid install
$existingSdk = $env:WASI_SDK_PATH
if (-not $existingSdk) {
    $existingSdk = [System.Environment]::GetEnvironmentVariable('WASI_SDK_PATH', 'User')
}

if ($existingSdk -and (Test-Path (Join-Path $existingSdk "bin\clang.exe"))) {
    Write-OK "wasi-sdk-25 already installed at $existingSdk"
    if (-not $env:WASI_SDK_PATH) {
        $env:WASI_SDK_PATH = $existingSdk
    }
} elseif (Test-Path $wasiSdkClang) {
    Write-OK "wasi-sdk-25 already installed at $wasiSdkRoot"
    [System.Environment]::SetEnvironmentVariable('WASI_SDK_PATH', $wasiSdkRoot, 'User')
    $env:WASI_SDK_PATH = $wasiSdkRoot
    $script:envChanged = $true
} else {
    Write-Step "Downloading wasi-sdk-25 tarball (~100MB) ..."
    $tarPath = Join-Path $env:TEMP "wasi-sdk-25.tar.gz"
    try {
        Invoke-WebRequest $wasiSdkUrl -OutFile $tarPath -UseBasicParsing
        Write-Step "Extracting to C:\Tools\ ..."
        if (-not (Test-Path "C:\Tools")) {
            New-Item -ItemType Directory -Path "C:\Tools" | Out-Null
        }
        # tar ships with Windows 10/11 — capable of .tar.gz
        & tar -xzf $tarPath -C "C:\Tools\"
        if ($LASTEXITCODE -ne 0) {
            throw "tar extraction failed (exit $LASTEXITCODE)"
        }
        if (Test-Path $wasiSdkClang) {
            [System.Environment]::SetEnvironmentVariable('WASI_SDK_PATH', $wasiSdkRoot, 'User')
            $env:WASI_SDK_PATH = $wasiSdkRoot
            $script:envChanged = $true
            Write-OK "wasi-sdk-25 installed at $wasiSdkRoot"
            Write-Warn "WASI_SDK_PATH set for current session + user scope — restart any open shells to pick it up."
        } else {
            Write-Fail "wasi-sdk-25 extraction did not produce $wasiSdkClang"
        }
    } catch {
        Write-Fail "wasi-sdk-25 install failed: $($_.Exception.Message)"
    } finally {
        if (Test-Path $tarPath) { Remove-Item $tarPath -Force -ErrorAction SilentlyContinue }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 5. Wasmtime v45 (pinned — the workload's WASI proposals match this release)
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "5 · Wasmtime v45"

$wasmtimeDir = "C:\Tools\wasmtime-v45.0.0-x86_64-windows"
$wasmtimeUrl = "https://github.com/bytecodealliance/wasmtime/releases/download/v45.0.0/wasmtime-v45.0.0-x86_64-windows.zip"

if (Command-Exists "wasmtime") {
    $wtVer = (wasmtime --version 2>&1).Trim()
    Write-OK "Wasmtime already installed ($wtVer)"
} elseif (Test-Path (Join-Path $wasmtimeDir "wasmtime.exe")) {
    Write-OK "Wasmtime v45 already extracted at $wasmtimeDir"
    $userPath = [System.Environment]::GetEnvironmentVariable("PATH", "User")
    if ($userPath -notlike "*$wasmtimeDir*") {
        [System.Environment]::SetEnvironmentVariable("PATH", "$wasmtimeDir;$userPath", "User")
        $script:envChanged = $true
        Write-Warn "Prepended $wasmtimeDir to user PATH — restart shells to pick it up."
    }
    Refresh-Path
} else {
    Write-Step "Downloading wasmtime v45 zip..."
    $zipPath = Join-Path $env:TEMP "wasmtime-v45.zip"
    try {
        Invoke-WebRequest $wasmtimeUrl -OutFile $zipPath -UseBasicParsing
        Write-Step "Extracting to C:\Tools\ ..."
        if (-not (Test-Path "C:\Tools")) {
            New-Item -ItemType Directory -Path "C:\Tools" | Out-Null
        }
        Expand-Archive $zipPath -DestinationPath "C:\Tools\" -Force
        if (Test-Path (Join-Path $wasmtimeDir "wasmtime.exe")) {
            $userPath = [System.Environment]::GetEnvironmentVariable("PATH", "User")
            [System.Environment]::SetEnvironmentVariable("PATH", "$wasmtimeDir;$userPath", "User")
            $env:PATH = "$wasmtimeDir;$env:PATH"
            $script:envChanged = $true
            Write-OK "Wasmtime v45 installed at $wasmtimeDir"
            Write-Warn "PATH updated for current session + user scope — restart shells for new processes."
        } else {
            Write-Fail "Wasmtime extraction did not produce wasmtime.exe at $wasmtimeDir"
        }
    } catch {
        Write-Fail "Wasmtime install failed: $($_.Exception.Message)"
    } finally {
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force -ErrorAction SilentlyContinue }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 6. Java 17 (required for Android)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "6 · Java 21 (Android toolchain + Gradle 8.x daemon)"

    # Phase 2.1 found: Gradle 8.11.1 daemon supports JDK 8-23. JDK 25 is too new
    # (Gradle 9.0+ required). JDK 21 is the LTS sweet spot — works for Gradle
    # 8.x AND for Android (which needs 17+). Marcel's box may have JDK 25 from
    # other tooling; we additionally ensure JDK 21 is available for Gradle.
    $jdk21Found = (Get-ChildItem -Path "C:\Program Files\Eclipse Adoptium\" -Directory -ErrorAction SilentlyContinue |
                   Where-Object { $_.Name -match "^jdk-21" } | Select-Object -First 1)
    if ($jdk21Found) {
        Write-OK "Temurin JDK 21 already installed at $($jdk21Found.FullName)"
    } else {
        Invoke-Winget "EclipseAdoptium.Temurin.21.JDK" "Temurin JDK 21"
        Refresh-Path
    }

    # Verify SOME compatible JDK is on PATH
    if (Command-Exists "java") {
        $jver = java -version 2>&1 | Select-Object -First 1
        if ($jver -match "1[7-9]\.|2[0-3]\.") {
            Write-OK "Gradle-compatible JDK on PATH ($($jver.ToString().Trim()))"
        } elseif ($jver -match "2[5-9]\.|[3-9]\d\.") {
            Write-Warn "Active JDK on PATH is $($jver.ToString().Trim()) — too new for Gradle 8.x. Gradle daemon may need JAVA_HOME pointing at JDK 21."
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 7. Android SDK (via Android command-line tools)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "7 · Android SDK + NDK + AVD"

    $androidHome = $env:ANDROID_HOME ?? "$env:USERPROFILE\AppData\Local\Android\Sdk"
    $cmdToolsDest = "$androidHome\cmdline-tools"
    $sdkmanager = "$cmdToolsDest\latest\bin\sdkmanager.bat"
    $avdmanager = "$cmdToolsDest\latest\bin\avdmanager.bat"

    # 7a. Command-line tools — install if sdkmanager isn't already present
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

    # 7b. Install SDK packages (idempotent — sdkmanager skips already-installed)
    if (Test-Path $sdkmanager) {
        # sdkmanager + avdmanager need Java 17+ (class file 61.0). Marcel's PATH
        # may put Oracle's java8path first; force JAVA_HOME for these invocations.
        $jdk21Found = (Get-ChildItem -Path "C:\Program Files\Eclipse Adoptium\" -Directory -ErrorAction SilentlyContinue |
                       Where-Object { $_.Name -match "^jdk-21" } | Select-Object -First 1)
        if ($jdk21Found) {
            $env:JAVA_HOME = $jdk21Found.FullName
            Write-Step "Using JAVA_HOME=$($jdk21Found.FullName) for sdkmanager (needs Java 17+)"
        } else {
            Write-Warn "JDK 21 not found at Adoptium default location — sdkmanager may fail with Java version mismatch"
        }

        Write-Step "Installing Android SDK packages (platform-tools, build-tools, platforms-34, NDK 26.3, x86_64 system image)..."
        # Auto-accept all license prompts
        & cmd /c "echo y| `"$sdkmanager`" --licenses" 2>&1 | Out-Null
        echo "y" | & $sdkmanager `
            "platform-tools" `
            "build-tools;34.0.0" `
            "platforms;android-34" `
            "ndk;26.3.11579264" `
            "system-images;android-34;google_apis;x86_64" `
            "emulator"
        if ($LASTEXITCODE -eq 0) {
            Write-OK "Android SDK packages installed at $androidHome"
        } else {
            Write-Fail "sdkmanager install failed (exit $LASTEXITCODE)"
        }

        # Set ANDROID_NDK_HOME so cargo-ndk can find the NDK toolchain
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

    # 7c. Create AVD for Phase 2.2 emulator (idempotent)
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
# 8. Rust + wit-bindgen (optional)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipWitBindgen) {
    Write-Header "8 · Rust + wit-bindgen (WIT binding generation)"

    if (Command-Exists "cargo") {
        Write-OK "Rust/Cargo already installed"
    } else {
        Write-Step "Installing Rust via winget..."
        Invoke-Winget "Rustlang.Rustup" "Rust (rustup)"
        Refresh-Path

        if (Command-Exists "rustup") {
            rustup default stable
            Write-OK "Rust stable toolchain set"
        }
    }

    if (Command-Exists "wit-bindgen") {
        Write-OK "wit-bindgen already installed"
    } elseif (Command-Exists "cargo") {
        Write-Step "Installing wit-bindgen-cli via cargo (may take a few minutes)..."
        cargo install wit-bindgen-cli
        if ($LASTEXITCODE -eq 0) { Write-OK "wit-bindgen installed" }
        else { Write-Fail "wit-bindgen install failed" }
    } else {
        Write-Fail "Cargo not available — skipping wit-bindgen"
    }

    # wasm-tools — Phase 2.1+ uses it to inspect .wasm component WIT shape
    # (validates the format-pivot spike). Same cargo install path as wit-bindgen.
    if (Command-Exists "wasm-tools") {
        Write-OK "wasm-tools already installed"
    } elseif (Command-Exists "cargo") {
        Write-Step "Installing wasm-tools via cargo (~3-5 min)..."
        cargo install wasm-tools
        if ($LASTEXITCODE -eq 0) { Write-OK "wasm-tools installed" }
        else { Write-Fail "wasm-tools install failed" }
    } else {
        Write-Fail "Cargo not available — skipping wasm-tools"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 8b. libwasmtime — C API DLL cargo-built from source
#     Phase 2.1 needs wasmtime.dll in vendor/wasmtime/ for the BlazorNative.Jni
#     Kotlin module to load via JNA. Phase 2.2 will add an Android NDK target
#     on top of this same cargo install; building from source now front-loads
#     the toolchain rather than mixing prebuilt + source-built versions.
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "8b · libwasmtime (cargo-built from source, ~15 min first run)"

$wasmtimeSrcDir   = Join-Path $PSScriptRoot "vendor\wasmtime-src"
$wasmtimeDllPath  = Join-Path $PSScriptRoot "vendor\wasmtime\wasmtime.dll"
$wasmtimeVersion  = "v45.0.0"   # matches setup.ps1 section 5's CLI version

# CMake is required by wasmtime-c-api's build.rs to copy headers around.
# Without it, the build fails late with: "failed to spawn cmake: program not found".
if (-not (Command-Exists "cmake")) {
    Write-Step "Installing CMake (wasmtime-c-api build.rs prereq)..."
    Invoke-Winget "Kitware.CMake" "CMake"
    Refresh-Path
}

if (Test-Path $wasmtimeDllPath) {
    Write-OK "wasmtime.dll already present at vendor/wasmtime/"
} elseif (-not (Command-Exists "cargo")) {
    Write-Fail "cargo not available — run setup.ps1 without -SkipWitBindgen first to install Rust"
} elseif (-not (Command-Exists "cmake")) {
    Write-Fail "cmake not available even after winget install attempt — install manually and re-run"
} else {
    # Clone if missing
    if (-not (Test-Path $wasmtimeSrcDir)) {
        Write-Step "Cloning bytecodealliance/wasmtime $wasmtimeVersion (depth=1)..."
        $parent = Split-Path $wasmtimeSrcDir
        if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
        git clone --depth 1 --branch $wasmtimeVersion https://github.com/bytecodealliance/wasmtime $wasmtimeSrcDir
        if ($LASTEXITCODE -ne 0) {
            Write-Fail "git clone wasmtime failed (exit $LASTEXITCODE)"
        }
    }

    # Submodule init (Cranelift, etc.)
    if (Test-Path $wasmtimeSrcDir) {
        Push-Location $wasmtimeSrcDir
        Write-Step "Initializing wasmtime submodules..."
        git submodule update --init --recursive --depth 1
        Pop-Location
    }

    # cargo build -p wasmtime-c-api --release
    if (Test-Path $wasmtimeSrcDir) {
        Write-Step "Building wasmtime-c-api (release) — first run is ~15 min..."
        Push-Location $wasmtimeSrcDir
        cargo build -p wasmtime-c-api --release
        $buildExit = $LASTEXITCODE
        Pop-Location

        if ($buildExit -eq 0) {
            $built = Join-Path $wasmtimeSrcDir "target\release\wasmtime.dll"
            if (Test-Path $built) {
                $dllDir = Split-Path $wasmtimeDllPath
                if (-not (Test-Path $dllDir)) { New-Item -ItemType Directory -Force -Path $dllDir | Out-Null }
                Copy-Item $built $wasmtimeDllPath -Force
                Write-OK "wasmtime.dll built and copied to vendor/wasmtime/"
            } else {
                Write-Fail "cargo build reported success but wasmtime.dll not found at $built"
            }
        } else {
            Write-Fail "cargo build wasmtime-c-api failed (exit $buildExit)"
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 8c. libwasmtime for Android ABIs (cargo-ndk + NDK 26.3)
#     Phase 2.2 cross-compiles wasmtime-c-api for arm64-v8a + x86_64 using
#     cargo-ndk (handles per-ABI CC/AR/linker env vars). Reuses the
#     vendor/wasmtime-src/ clone from section 8b — no second checkout needed.
#     Output: jniLibs/<abi>/libwasmtime.so consumed by the Android Gradle
#     plugin via sourceSets.jniLibs.srcDirs.
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "8c · libwasmtime for Android ABIs (cargo-ndk + NDK 26.3, ~10-15 min first run)"

    $jniLibsDir = Join-Path $PSScriptRoot "vendor\wasmtime\jniLibs"
    $arm64So    = Join-Path $jniLibsDir "arm64-v8a\libwasmtime.so"
    $x86_64So   = Join-Path $jniLibsDir "x86_64\libwasmtime.so"
    $wasmtimeSrcDir = Join-Path $PSScriptRoot "vendor\wasmtime-src"

    if ((Test-Path $arm64So) -and (Test-Path $x86_64So)) {
        Write-OK "Android libwasmtime.so already built (both ABIs)"
    } elseif (-not (Command-Exists "cargo")) {
        Write-Fail "cargo not available — section 8 (Rust install) needs to succeed first"
    } elseif (-not $env:ANDROID_NDK_HOME -or -not (Test-Path $env:ANDROID_NDK_HOME)) {
        Write-Fail "ANDROID_NDK_HOME not set or invalid — section 7 needs to install NDK 26.3 first"
    } elseif (-not (Test-Path $wasmtimeSrcDir)) {
        Write-Fail "vendor/wasmtime-src/ missing — section 8b (Windows libwasmtime build) needs to clone it first"
    } else {
        # cargo-ndk wrapper
        if (-not (Command-Exists "cargo-ndk")) {
            Write-Step "Installing cargo-ndk..."
            cargo install cargo-ndk
            if ($LASTEXITCODE -ne 0) {
                Write-Fail "cargo install cargo-ndk failed"
            }
        }

        if (Command-Exists "cargo-ndk") {
            Write-Step "Adding Rust targets aarch64-linux-android + x86_64-linux-android..."
            rustup target add aarch64-linux-android x86_64-linux-android | Out-Null

            Push-Location $wasmtimeSrcDir
            Write-Step "Cross-compiling libwasmtime for arm64-v8a + x86_64 (this is the slow part)..."
            cargo ndk -t arm64-v8a -t x86_64 -o $jniLibsDir build -p wasmtime-c-api --release
            $buildExit = $LASTEXITCODE
            Pop-Location

            if ($buildExit -eq 0 -and (Test-Path $arm64So) -and (Test-Path $x86_64So)) {
                $arm64Mb = [math]::Round((Get-Item $arm64So).Length / 1MB, 1)
                $x86Mb   = [math]::Round((Get-Item $x86_64So).Length / 1MB, 1)
                Write-OK "Android libwasmtime.so built (arm64-v8a: ${arm64Mb}MB, x86_64: ${x86Mb}MB)"
            } else {
                Write-Fail "cargo ndk build failed (exit $buildExit)"
            }
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 9. Restore NuGet packages
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "9 · NuGet restore"

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
# 10. Verify the dev build (fast — no WASI publish)
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "10 · Smoke test — Debug build + fast tests"

if (Test-Path "BlazorNative.sln") {
    Write-Step "Building BlazorNative.sln (Debug)..."
    dotnet build BlazorNative.sln -c Debug --nologo -v q
    if ($LASTEXITCODE -eq 0) { Write-OK "BlazorNative.sln builds successfully (Debug)" }
    else { Write-Fail "Debug build failed — check output above" }

    Write-Step "Running fast tests (skipping WASI integration)..."
    dotnet test BlazorNative.sln --no-build -c Debug --filter "Category!=Integration" --nologo -v q
    if ($LASTEXITCODE -eq 0) { Write-OK "Fast tests passed" }
    else { Write-Warn "Fast tests reported failures — review above" }
} else {
    Write-Warn "BlazorNative.sln not found — skipping smoke test"
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  Setup complete (.NET 10 · wasi-sdk-25 · wasmtime v45)" -ForegroundColor White
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
    Write-Warn "Environment variables (WASI_SDK_PATH and/or PATH) were updated."
    Write-Warn "Open a fresh PowerShell/terminal so new processes inherit the changes."
    Write-Host ""
}

if ($script:failed -eq 0) {
    Write-Host "  You're ready to go!" -ForegroundColor White
    Write-Host ""
    Write-Host "  Fast iteration (no WASM compile):" -ForegroundColor DarkGray
    Write-Host "    dotnet watch run --project src\BlazorNative.Host.Android\BlazorNative.DevHost.csproj" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Run the full WASI boot test (does a Mono-AOT publish, ~3 min):" -ForegroundColor DarkGray
    Write-Host "    dotnet test BlazorNative.sln" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Or via make:" -ForegroundColor DarkGray
    Write-Host "    make wasi       # publish WasiHost → bin\Release\net10.0\wasi-wasm\AppBundle\" -ForegroundColor Cyan
    Write-Host "    make wasi-run   # publish + execute via wasmtime" -ForegroundColor Cyan
    Write-Host "    make wasi-test  # publish + boot smoke test" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  DevTools API will be available at https://localhost:5273/dev/storage" -ForegroundColor DarkGray
} else {
    Write-Host "  Some steps failed. Review the ✗ items above and re-run." -ForegroundColor Yellow
    Write-Host "  You can re-run safely — already-installed items are skipped." -ForegroundColor DarkGray
}

Write-Host ""
