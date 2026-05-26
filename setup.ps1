#Requires -RunAsAdministrator
<#
.SYNOPSIS
    BlazorNative — full prerequisite installer for Windows 11
.DESCRIPTION
    Installs and configures everything needed to build and run BlazorNative:
      • .NET 10 SDK
      • WASI experimental workload (manifest 10.0.108)
      • wasi-sdk 25.0 (pinned — newer SDKs are rejected by the workload)
      • Wasmtime CLI v45
      • MAUI Android workload + Android SDK
      • Rust + wit-bindgen (optional, for WIT binding regeneration)

    Run from the repo root:
        powershell -ExecutionPolicy Bypass -File setup.ps1

    Flags:
        -SkipAndroid     Skip Android SDK/MAUI installation
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
# 3. .NET workloads
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "3 · .NET workloads"

$installedWorkloads = dotnet workload list 2>&1 | Out-String

# WASI (always required) — net10 manifest 10.0.108
if ($installedWorkloads -match "wasi-experimental") {
    Write-OK "wasi-experimental workload already installed"
} else {
    Write-Step "Installing wasi-experimental workload..."
    dotnet workload install wasi-experimental
    if ($LASTEXITCODE -eq 0) { Write-OK "wasi-experimental installed" }
    else { Write-Fail "wasi-experimental install failed (exit $LASTEXITCODE)" }
}

# MAUI Android (optional)
if (-not $SkipAndroid) {
    if ($installedWorkloads -match "maui-android") {
        Write-OK "maui-android workload already installed"
    } else {
        Write-Step "Installing maui-android workload (this may take a few minutes)..."
        dotnet workload install maui-android
        if ($LASTEXITCODE -eq 0) { Write-OK "maui-android installed" }
        else { Write-Fail "maui-android install failed (exit $LASTEXITCODE)" }
    }
}

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
    Write-Header "6 · Java 17 (Android toolchain)"

    $javaOk = $false
    if (Command-Exists "java") {
        $jver = java -version 2>&1 | Select-Object -First 1
        if ($jver -match "17\.|21\.") {
            Write-OK "Java already installed ($($jver.ToString().Trim()))"
            $javaOk = $true
        }
    }

    if (-not $javaOk) {
        Invoke-Winget "Microsoft.OpenJDK.17" "Microsoft OpenJDK 17"
        Refresh-Path
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 7. Android SDK (via Android command-line tools)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "7 · Android SDK"

    $androidHome = $env:ANDROID_HOME ?? "$env:USERPROFILE\AppData\Local\Android\Sdk"

    if (Test-Path "$androidHome\platform-tools\adb.exe") {
        Write-OK "Android SDK found at $androidHome"
    } else {
        Write-Step "Downloading Android command-line tools..."
        $cmdToolsUrl  = "https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip"
        $cmdToolsZip  = "$env:TEMP\android-cmdtools.zip"
        $cmdToolsDest = "$androidHome\cmdline-tools"

        New-Item -ItemType Directory -Force -Path $cmdToolsDest | Out-Null
        Invoke-WebRequest $cmdToolsUrl -OutFile $cmdToolsZip
        Expand-Archive $cmdToolsZip -DestinationPath $cmdToolsDest -Force

        # Rename extracted folder to 'latest' as required by sdkmanager
        $extracted = Get-ChildItem $cmdToolsDest | Where-Object { $_.Name -ne "latest" } | Select-Object -First 1
        if ($extracted) { Rename-Item $extracted.FullName "latest" }

        $sdkmanager = "$cmdToolsDest\latest\bin\sdkmanager.bat"
        if (Test-Path $sdkmanager) {
            Write-Step "Installing Android platform-tools and build-tools..."
            echo "y" | & $sdkmanager "platform-tools" "build-tools;34.0.0" "platforms;android-34"

            [Environment]::SetEnvironmentVariable("ANDROID_HOME", $androidHome, "User")
            [Environment]::SetEnvironmentVariable("PATH", $env:PATH + ";$androidHome\platform-tools", "User")
            Refresh-Path
            Write-OK "Android SDK installed at $androidHome"
        } else {
            Write-Fail "sdkmanager not found after extraction"
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
