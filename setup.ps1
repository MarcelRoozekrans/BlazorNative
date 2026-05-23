#Requires -RunAsAdministrator
<#
.SYNOPSIS
    BlazorNative — full prerequisite installer for Windows 11
.DESCRIPTION
    Installs and configures everything needed to build and run BlazorNative:
      • .NET 9 SDK
      • WASI experimental workload
      • Wasmtime CLI
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
# 2. .NET 9 SDK
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "2 · .NET 9 SDK"

$dotnetOk = $false
if (Command-Exists "dotnet") {
    $sdks = dotnet --list-sdks 2>&1
    if ($sdks -match "^9\.") {
        Write-OK ".NET 9 SDK already installed"
        $dotnetOk = $true
    }
}

if (-not $dotnetOk) {
    Invoke-Winget "Microsoft.DotNet.SDK.9" ".NET 9 SDK"
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

# WASI (always required)
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
# 4. Wasmtime
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "4 · Wasmtime"

if (Command-Exists "wasmtime") {
    $wtVer = (wasmtime --version 2>&1).Trim()
    Write-OK "Wasmtime already installed ($wtVer)"
} else {
    Write-Step "Installing Wasmtime via winget..."
    Invoke-Winget "BytecodeAlliance.wasmtime" "Wasmtime"
    Refresh-Path

    # Fallback: PowerShell installer from wasmtime.dev
    if (-not (Command-Exists "wasmtime")) {
        Write-Warn "winget install failed — trying official installer script..."
        $installerUrl = "https://github.com/bytecodealliance/wasmtime/releases/latest/download/wasmtime-dev-x86_64-windows.zip"
        $dest = "$env:USERPROFILE\.wasmtime"
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        Invoke-WebRequest $installerUrl -OutFile "$dest\wasmtime.zip"
        Expand-Archive "$dest\wasmtime.zip" -DestinationPath $dest -Force
        $exePath = (Get-ChildItem "$dest" -Filter "wasmtime.exe" -Recurse | Select-Object -First 1).DirectoryName
        [Environment]::SetEnvironmentVariable("PATH", $env:PATH + ";$exePath", "User")
        Refresh-Path
        if (Command-Exists "wasmtime") { Write-OK "Wasmtime installed via direct download" }
        else { Write-Fail "Wasmtime install failed — install manually from https://wasmtime.dev" }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 5. Java 17 (required for Android)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "5 · Java 17 (Android toolchain)"

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
# 6. Android SDK (via Android command-line tools)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "6 · Android SDK"

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
# 7. Rust + wit-bindgen (optional)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipWitBindgen) {
    Write-Header "7 · Rust + wit-bindgen (WIT binding generation)"

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
}

# ─────────────────────────────────────────────────────────────────────────────
# 8. Restore NuGet packages
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "8 · NuGet restore"

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
# 9. Verify the dev build
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "9 · Smoke test — dev build"

if (Test-Path "src\BlazorNative.Host.Android\BlazorNative.DevHost.csproj") {
    Write-Step "Building DevHost (net9.0)..."
    dotnet build src\BlazorNative.Host.Android\BlazorNative.DevHost.csproj -c Debug --nologo -v q
    if ($LASTEXITCODE -eq 0) { Write-OK "DevHost builds successfully" }
    else { Write-Fail "DevHost build failed — check output above" }
} else {
    Write-Warn "DevHost project not found — skipping smoke test"
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  Setup complete" -ForegroundColor White
Write-Host ""
Write-Host "    ✓ Passed  : $script:passed" -ForegroundColor Green
if ($script:skipped -gt 0) {
Write-Host "    ○ Skipped : $script:skipped" -ForegroundColor DarkGray
}
if ($script:failed -gt 0) {
Write-Host "    ✗ Failed  : $script:failed" -ForegroundColor Red
}
Write-Host ""

if ($script:failed -eq 0) {
    Write-Host "  You're ready to go! Start the dev host:" -ForegroundColor White
    Write-Host ""
    Write-Host "    dotnet watch run --project src\BlazorNative.Host.Android\BlazorNative.DevHost.csproj" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Or if you have make installed:" -ForegroundColor DarkGray
    Write-Host "    make dev" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  DevTools API will be available at https://localhost:5273/dev/storage" -ForegroundColor DarkGray
} else {
    Write-Host "  Some steps failed. Review the ✗ items above and re-run." -ForegroundColor Yellow
    Write-Host "  You can re-run safely — already-installed items are skipped." -ForegroundColor DarkGray
}

Write-Host ""
