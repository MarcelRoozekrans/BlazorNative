#Requires -RunAsAdministrator
<#
.SYNOPSIS
    BlazorNative — full prerequisite installer for Windows 11
.DESCRIPTION
    Installs and configures everything needed to build and run BlazorNative:
      • .NET 10 SDK
      • Temurin JDK 21 (Android toolchain + Gradle daemon)
      • Android SDK + NDK 26.3 + AVD
      • Bionic NativeAOT toolchain verification (linux-bionic publishes)

    Run from the repo root:
        powershell -ExecutionPolicy Bypass -File setup.ps1

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
# 3. Java 21 (required for Android)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "3 · Java 21 (Android toolchain + Gradle 8.x daemon)"

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
# 4. Android SDK (via Android command-line tools)
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipAndroid) {
    Write-Header "4 · Android SDK + NDK + AVD"

    $androidHome = $env:ANDROID_HOME ?? "$env:USERPROFILE\AppData\Local\Android\Sdk"
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
#    BlazorNative.NativeHost cross-compiles to linux-bionic-{x64,arm64} .so
#    files directly on Windows via the runtime-pack bypass (the RID-specific
#    ILCompiler packages don't exist for .NET 10 — 3.0b Gate 4 RED).
#    Pinned working combo (Gate 2 GREEN):
#      • .NET SDK 10.0.301 (section 2)
#      • ILCompiler + Microsoft.NETCore.App.Runtime.NativeAOT.linux-bionic-*
#        runtime packs 10.0.9 (pinned via RuntimeFrameworkVersion in
#        BlazorNative.NativeHost.csproj)
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
    Write-Host "  Pinned toolchain combo (Phase 3.0c Gate 2):" -ForegroundColor DarkGray
    Write-Host "    .NET SDK 10.0.3xx band (floor 10.0.301) · ILCompiler/NativeAOT runtime packs 10.0.9 · NDK $bionicNdkPin" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Publish the Android native host (from repo root):" -ForegroundColor DarkGray
    Write-Host "    dotnet publish src\BlazorNative.NativeHost -c Release -r linux-bionic-x64" -ForegroundColor Cyan
    Write-Host "    dotnet publish src\BlazorNative.NativeHost -c Release -r linux-bionic-arm64" -ForegroundColor Cyan
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

Write-Header "7 · Smoke test — Debug build + fast tests"

if (Test-Path "BlazorNative.sln") {
    Write-Step "Building BlazorNative.sln (Debug)..."
    dotnet build BlazorNative.sln -c Debug --nologo -v q
    if ($LASTEXITCODE -eq 0) { Write-OK "BlazorNative.sln builds successfully (Debug)" }
    else { Write-Fail "Debug build failed — check output above" }

    Write-Step "Running fast tests (skipping integration)..."
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
Write-Host "  Setup complete (.NET 10 · JDK 21 · Android SDK/NDK 26.3 · bionic NativeAOT)" -ForegroundColor White
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
    Write-Host "  Fast iteration (plain .NET, no AOT):" -ForegroundColor DarkGray
    Write-Host "    dotnet watch run --project src\BlazorNative.Host.Android\BlazorNative.DevHost.csproj" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Run all .NET tests:" -ForegroundColor DarkGray
    Write-Host "    dotnet test BlazorNative.sln" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Publish the native runtime (pinned combo — see section 5):" -ForegroundColor DarkGray
    Write-Host "    dotnet publish src\BlazorNative.NativeHost -c Release -r win-x64" -ForegroundColor Cyan
    Write-Host "    dotnet publish src\BlazorNative.NativeHost -c Release -r linux-bionic-x64" -ForegroundColor Cyan
    Write-Host "    dotnet publish src\BlazorNative.NativeHost -c Release -r linux-bionic-arm64" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  DevTools API will be available at https://localhost:5273/dev/storage" -ForegroundColor DarkGray
} else {
    Write-Host "  Some steps failed. Review the ✗ items above and re-run." -ForegroundColor Yellow
    Write-Host "  You can re-run safely — already-installed items are skipped." -ForegroundColor DarkGray
}

Write-Host ""
