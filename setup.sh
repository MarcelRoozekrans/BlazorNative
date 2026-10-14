#!/usr/bin/env bash
# BlazorNative — prerequisite installer for Linux and macOS.
#
# The counterpart of setup.ps1 (Windows). Installs and verifies:
#   • the .NET SDK band pinned in global.json
#   • PowerShell 7 (pwsh) — the .NET suite's reference-drift fixtures run
#     scripts/generate-reference.ps1 through it
#   • Temurin JDK 25, the version CI builds on, unless JAVA_HOME already holds it
#   • Android SDK + NDK 26.3 + AVD, and ANDROID_NDK_ROOT for the bionic publish
# then restores, builds and runs the .NET suite.
#
# Nothing here needs root. Tools go under $HOME; your shell profile is never
# edited — the summary prints the exports to add yourself.
#
# Not covered on Linux/macOS: the JVM dev loop (testDebugUnitTest) loads the
# win-x64 NativeAOT library, so it runs on Windows only. The iOS lane needs
# Xcode, which this script does not install.
#
# Run from the repo root:
#     ./setup.sh [--skip-android] [--skip-tests]

set -euo pipefail

skip_android=false
skip_tests=false
for arg in "$@"; do
    case "$arg" in
        --skip-android) skip_android=true ;;
        --skip-tests)   skip_tests=true ;;
        -h|--help)      sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "unknown option: $arg (try --help)" >&2; exit 2 ;;
    esac
done

# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────

passed=0; skipped=0; failed=0
exports=()

if [ -t 1 ]; then
    c_cyan=$'\e[36m'; c_gray=$'\e[90m'; c_green=$'\e[32m'; c_red=$'\e[31m'; c_yellow=$'\e[33m'; c_off=$'\e[0m'
else
    c_cyan=''; c_gray=''; c_green=''; c_red=''; c_yellow=''; c_off=''
fi

header() { printf '\n  %s%s%s\n' "$c_cyan" "$1" "$c_off"; }
step()   { printf '  ⟶  %s\n' "$1"; }
ok()     { printf '  %s✓  %s%s\n' "$c_green" "$1" "$c_off"; passed=$((passed + 1)); }
skip()   { printf '  %s○  %s%s\n' "$c_gray" "$1" "$c_off"; skipped=$((skipped + 1)); }
fail()   { printf '  %s✗  %s%s\n' "$c_red" "$1" "$c_off"; failed=$((failed + 1)); }
warn()   { printf '  %s⚠  %s%s\n' "$c_yellow" "$1" "$c_off"; }
have()   { command -v "$1" >/dev/null 2>&1; }

# Records an export for the summary and applies it to this run.
remember_export() {
    export "$1=$2"
    exports+=("export $1=\"$2\"")
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

tools_dir="${BN_TOOLS_DIR:-$HOME/.local/share/blazornative}"

# The Temurin JDK installed for Gradle and sdkmanager — the one CI provisions (#404).
jdk_major=25

case "$(uname -s)" in
    Linux)  os=linux ;;
    Darwin) os=mac ;;
    *) echo "unsupported OS: $(uname -s) — on Windows use setup.ps1" >&2; exit 1 ;;
esac
case "$(uname -m)" in
    x86_64|amd64)  arch=x64 ;;
    arm64|aarch64) arch=arm64 ;;
    *) echo "unsupported CPU: $(uname -m)" >&2; exit 1 ;;
esac

printf '\n  BlazorNative — Prerequisite Installer (%s-%s)\n' "$os" "$arch"
printf '  %s.NET → NativeAOT → Native mobile%s\n' "$c_gray" "$c_off"
$skip_android && warn "Skipping Android (--skip-android)"

# ─────────────────────────────────────────────────────────────────────────────
# 1 · Base tools
# ─────────────────────────────────────────────────────────────────────────────

header "1 · Base tools"

missing=()
for t in curl unzip tar; do have "$t" || missing+=("$t"); done
if [ ${#missing[@]} -eq 0 ]; then
    ok "curl, unzip and tar are available"
else
    fail "missing: ${missing[*]} — install them with your package manager and re-run"
    exit 1
fi

# ─────────────────────────────────────────────────────────────────────────────
# 2 · .NET SDK (the band global.json pins)
# ─────────────────────────────────────────────────────────────────────────────

header "2 · .NET SDK"

# global.json pins an exact version with rollForward latestPatch, so any SDK in
# the same feature band at or above it satisfies it: 10.0.401 → 10.0.4xx >= 401.
sdk_pin="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([0-9.]*\)".*/\1/p' global.json | head -1)"
if [ -z "$sdk_pin" ]; then
    fail "could not read the SDK version from global.json"
    exit 1
fi
pin_mm="${sdk_pin%.*}"            # 10.0
pin_patch="${sdk_pin##*.}"        # 401
pin_band="${pin_patch:0:1}"       # 4

sdk_satisfies() {
    local v mm patch
    while read -r v _; do
        mm="${v%.*}"; patch="${v##*.}"
        patch="${patch%%-*}"
        [ "$mm" = "$pin_mm" ] || continue
        [ "${patch:0:1}" = "$pin_band" ] || continue
        [ "$patch" -ge "$pin_patch" ] && { echo "$v"; return 0; }
    done < <(dotnet --list-sdks 2>/dev/null)
    return 1
}

# A previous run's ~/.dotnet is not on PATH until the profile says so.
if [ -x "$HOME/.dotnet/dotnet" ] && ! have dotnet; then
    export PATH="$HOME/.dotnet:$PATH"
    dotnet_path_added=true
fi

if have dotnet && found_sdk="$(sdk_satisfies)"; then
    ok ".NET SDK $found_sdk satisfies global.json ($sdk_pin, latestPatch)"
    if ${dotnet_path_added:-false}; then
        remember_export DOTNET_ROOT "$HOME/.dotnet"
        exports+=('export PATH="$HOME/.dotnet:$PATH"')
    fi
else
    step "Installing the .NET SDK for global.json ($sdk_pin) into ~/.dotnet..."
    installer="$(mktemp)"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
    bash "$installer" --jsonfile "$repo_root/global.json" --install-dir "$HOME/.dotnet" >/dev/null
    rm -f "$installer"
    export PATH="$HOME/.dotnet:$PATH"
    if found_sdk="$(sdk_satisfies)"; then
        ok ".NET SDK $found_sdk installed"
        remember_export DOTNET_ROOT "$HOME/.dotnet"
        exports+=('export PATH="$HOME/.dotnet:$PATH"')
    else
        fail ".NET SDK install did not produce a $pin_mm.${pin_band}xx SDK >= $sdk_pin"
        exit 1
    fi
fi

# ─────────────────────────────────────────────────────────────────────────────
# 3 · PowerShell 7 (pwsh)
# ─────────────────────────────────────────────────────────────────────────────

header "3 · PowerShell 7 (the .NET suite's reference-drift fixtures need pwsh)"

if [ -x "$HOME/.dotnet/tools/pwsh" ] && ! have pwsh; then
    export PATH="$PATH:$HOME/.dotnet/tools"
    exports+=('export PATH="$PATH:$HOME/.dotnet/tools"')
fi

if have pwsh; then
    ok "pwsh $(pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()') is available"
else
    step "Installing PowerShell as a .NET global tool..."
    if dotnet tool install --global PowerShell >/dev/null; then
        export PATH="$PATH:$HOME/.dotnet/tools"
        exports+=('export PATH="$PATH:$HOME/.dotnet/tools"')
        ok "pwsh installed to ~/.dotnet/tools"
    else
        fail "dotnet tool install --global PowerShell failed"
    fi
fi

# ─────────────────────────────────────────────────────────────────────────────
# 4 · JDK (the version CI builds on; Gradle 9.8 runs on 17–25)
# ─────────────────────────────────────────────────────────────────────────────

java_major() {  # prints the major version of the java binary given
    "$1" -version 2>&1 | sed -n 's/.*version "\([0-9]*\)[."].*/\1/p' | head -1
}

if ! $skip_android; then
    header "4 · JDK $jdk_major (Gradle 9.8 + sdkmanager)"

    # Only the CI version counts as found, so a local build runs what CI runs.
    jdk_home=""
    if [ -n "${JAVA_HOME:-}" ] && [ -x "$JAVA_HOME/bin/java" ] &&
            [ "$(java_major "$JAVA_HOME/bin/java")" = "$jdk_major" ]; then
        jdk_home="$JAVA_HOME"
    fi
    for candidate in "$tools_dir/jdk-$jdk_major/Contents/Home" "$tools_dir/jdk-$jdk_major"; do
        if [ -z "$jdk_home" ] && [ -x "$candidate/bin/java" ]; then jdk_home="$candidate"; fi
    done
    if [ -z "$jdk_home" ] && [ "$os" = mac ] && [ -x /usr/libexec/java_home ]; then
        jdk_home="$(/usr/libexec/java_home -v "$jdk_major" 2>/dev/null || true)"
        [ -n "$jdk_home" ] && [ "$(java_major "$jdk_home/bin/java")" = "$jdk_major" ] || jdk_home=""
    fi
    if [ -z "$jdk_home" ] && have java && [ "$(java_major "$(command -v java)")" = "$jdk_major" ]; then
        jdk_home="$(cd "$(dirname "$(readlink -f "$(command -v java)" 2>/dev/null || command -v java)")/.." && pwd)"
    fi

    if [ -n "$jdk_home" ]; then
        ok "JDK $(java_major "$jdk_home/bin/java") at $jdk_home"
    else
        step "Installing Temurin JDK $jdk_major into $tools_dir/jdk-$jdk_major..."
        adoptium_os=$([ "$os" = mac ] && echo mac || echo linux)
        adoptium_arch=$([ "$arch" = arm64 ] && echo aarch64 || echo x64)
        mkdir -p "$tools_dir/jdk-$jdk_major"
        curl -fsSL "https://api.adoptium.net/v3/binary/latest/$jdk_major/ga/$adoptium_os/$adoptium_arch/jdk/hotspot/normal/eclipse" \
            | tar -xz -C "$tools_dir/jdk-$jdk_major" --strip-components=1
        # macOS tarballs nest the JDK under Contents/Home.
        if [ -x "$tools_dir/jdk-$jdk_major/Contents/Home/bin/java" ]; then
            jdk_home="$tools_dir/jdk-$jdk_major/Contents/Home"
        else
            jdk_home="$tools_dir/jdk-$jdk_major"
        fi
        if [ -x "$jdk_home/bin/java" ]; then
            ok "Temurin JDK $(java_major "$jdk_home/bin/java") installed"
        else
            fail "Temurin JDK $jdk_major download did not produce $jdk_home/bin/java"
            jdk_home=""
        fi
    fi
    [ -n "$jdk_home" ] && remember_export JAVA_HOME "$jdk_home"
fi

# ─────────────────────────────────────────────────────────────────────────────
# 5 · Android SDK + NDK + AVD
# ─────────────────────────────────────────────────────────────────────────────

ndk_pin="26.3.11579264"   # mirrors ci.yml's NDK_PIN and setup.ps1

if ! $skip_android; then
    header "5 · Android SDK + NDK $ndk_pin + AVD"

    if [ -z "${ANDROID_HOME:-}" ]; then
        if [ "$os" = mac ]; then ANDROID_HOME="$HOME/Library/Android/sdk"; else ANDROID_HOME="$HOME/Android/Sdk"; fi
    fi
    remember_export ANDROID_HOME "$ANDROID_HOME"
    sdkmanager="$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager"
    avdmanager="$ANDROID_HOME/cmdline-tools/latest/bin/avdmanager"

    if [ -z "${JAVA_HOME:-}" ]; then
        fail "no JDK — sdkmanager cannot run (see section 4)"
    else
        # 5a. Command-line tools, guarded on sdkmanager so a partial previous
        # run that extracted them but installed nothing else does not re-download.
        if [ ! -x "$sdkmanager" ]; then
            step "Downloading Android command-line tools..."
            zip="$(mktemp)"
            curl -fsSL "https://dl.google.com/android/repository/commandlinetools-$os-11076708_latest.zip" -o "$zip"
            mkdir -p "$ANDROID_HOME/cmdline-tools"
            rm -rf "$ANDROID_HOME/cmdline-tools/latest" "$ANDROID_HOME/cmdline-tools/cmdline-tools"
            unzip -q "$zip" -d "$ANDROID_HOME/cmdline-tools"
            mv "$ANDROID_HOME/cmdline-tools/cmdline-tools" "$ANDROID_HOME/cmdline-tools/latest"
            rm -f "$zip"
        fi

        # 5b. Packages. sdkmanager skips what is already installed. The system
        # image follows the host CPU: an arm64 host cannot run an x86_64 AVD.
        image_abi=$([ "$arch" = arm64 ] && echo arm64-v8a || echo x86_64)
        system_image="system-images;android-34;google_apis;$image_abi"
        step "Installing platform-tools, build-tools, platforms;android-37.0, NDK $ndk_pin, $system_image, emulator..."
        yes 2>/dev/null | "$sdkmanager" --licenses >/dev/null 2>&1 || true
        if "$sdkmanager" "platform-tools" "build-tools;34.0.0" "platforms;android-37.0" \
                "ndk;$ndk_pin" "$system_image" "emulator" >/dev/null; then
            ok "Android SDK packages installed at $ANDROID_HOME"
        else
            fail "sdkmanager install failed"
        fi

        # 5c. AVD (idempotent).
        avd_name="blazornative-pixel6-$image_abi"
        if "$avdmanager" list avd -c 2>/dev/null | grep -qx "$avd_name"; then
            ok "AVD $avd_name already exists"
        elif echo no | "$avdmanager" create avd -n "$avd_name" -k "$system_image" -d pixel_6 >/dev/null; then
            ok "AVD $avd_name created"
        else
            fail "avdmanager create avd failed"
        fi
    fi
fi

# ─────────────────────────────────────────────────────────────────────────────
# 6 · Bionic NativeAOT toolchain — verify ANDROID_NDK_ROOT for linux-bionic publishes
# ─────────────────────────────────────────────────────────────────────────────

if ! $skip_android; then
    header "6 · Bionic NativeAOT toolchain (ANDROID_NDK_ROOT)"

    # build/BionicNativeAot.targets reads ANDROID_NDK_ROOT, and the path existing is
    # not enough: a stray value can point at a different NDK, so the revision is
    # read from the NDK's own source.properties.
    ndk_revision_ok() {
        [ -f "$1/source.properties" ] &&
            grep -Eq "^[[:space:]]*Pkg\.Revision[[:space:]]*=[[:space:]]*${ndk_pin//./\\.}[[:space:]]*$" "$1/source.properties"
    }

    ndk_root="${ANDROID_NDK_ROOT:-$ANDROID_HOME/ndk/$ndk_pin}"
    source_of="${ANDROID_NDK_ROOT:+ANDROID_NDK_ROOT}"
    source_of="${source_of:-the default SDK location}"
    if [ ! -d "$ndk_root" ]; then
        fail "NDK $ndk_pin not found at $ndk_root — section 5 needs to install it first"
    elif ! ndk_revision_ok "$ndk_root"; then
        fail "NDK at $ndk_root (from $source_of) is not revision $ndk_pin. Point ANDROID_NDK_ROOT at NDK $ndk_pin, or unset it."
    else
        remember_export ANDROID_NDK_ROOT "$ndk_root"
        ok "ANDROID_NDK_ROOT is $ndk_root (revision $ndk_pin verified)"
    fi

    # The NDK ships no linux-aarch64 host toolchain, so the bionic cross-compile
    # cannot run on an arm64 Linux host. macOS uses darwin-x86_64, which runs on
    # Apple silicon.
    if [ "$os" = linux ] && [ "$arch" = arm64 ]; then
        warn "linux-bionic publishes need an x86_64 Linux host — NDK $ndk_pin has no linux-aarch64 toolchain"
    fi
fi

# ─────────────────────────────────────────────────────────────────────────────
# 7 · Restore, build, test
# ─────────────────────────────────────────────────────────────────────────────

header "7 · Restore, Debug build and the .NET suite"

if dotnet restore BlazorNative.sln --nologo -v q; then ok "NuGet packages restored"; else fail "NuGet restore failed"; fi

if dotnet build BlazorNative.sln -c Debug --no-restore --nologo -v q; then
    ok "BlazorNative.sln builds (Debug)"
    if $skip_tests; then
        skip ".NET suite (--skip-tests)"
    else
        # --blame-hang-timeout turns #454, a local hang in the reference-drift
        # fixture, into a named failure with dumps instead of a silent wait.
        step "Running the .NET suite (about a minute)..."
        if dotnet test BlazorNative.sln -c Debug --no-build --nologo -v q \
                --blame-hang --blame-hang-timeout 10m; then
            ok ".NET suite passed"
        else
            fail ".NET suite reported failures — review above"
        fi
    fi
else
    fail "Debug build failed — check the output above"
fi

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

printf '\n  ────────────────────────────────────\n'
printf '    %s✓ Passed  : %d%s\n' "$c_green" "$passed" "$c_off"
[ "$skipped" -gt 0 ] && printf '    %s○ Skipped : %d%s\n' "$c_gray" "$skipped" "$c_off"
[ "$failed" -gt 0 ] && printf '    %s✗ Failed  : %d%s\n' "$c_red" "$failed" "$c_off"

if [ ${#exports[@]} -gt 0 ]; then
    printf '\n  Add these to your shell profile (this script does not edit it):\n\n'
    for e in "${exports[@]}"; do printf '    %s\n' "$e"; done
fi

if [ "$failed" -eq 0 ]; then
    printf '\n  You'\''re ready to go.\n\n'
    if ! $skip_android; then
        printf '  Publish the Android native host:\n'
        printf '    dotnet publish samples/BlazorNative.SampleApp -c Release -r linux-bionic-x64\n'
        printf '    dotnet publish samples/BlazorNative.SampleApp -c Release -r linux-bionic-arm64\n\n'
    fi
    printf '  The JVM dev loop (testDebugUnitTest) needs the win-x64 library, so it runs on Windows only.\n\n'
else
    printf '\n  Some steps failed. Review the ✗ items above and re-run — finished steps are skipped.\n\n'
    exit 1
fi
