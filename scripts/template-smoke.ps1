#Requires -Version 7
<#
.SYNOPSIS
    BlazorNative — template smoke (Phase 8.3 Gate 2, M8 DoD #4: `dotnet new
    blazornative`, "creation → build validated on CI").

.DESCRIPTION
    THE SENTENCE THIS SCRIPT EXISTS FOR: a fresh `dotnet new blazornative` app
    has EXACTLY the shape nativelib ILC trims silently — and unlike Phase 8.0,
    NOBODY WILL BE WATCHING when it happens.

    8.0 met that failure with a human at the console: the publish head moved, the
    IL2072 count went 4 → 0, the binary shrank ~400KB, the page names vanished
    from the native image, and the stop-and-analyze rule caught it before it
    shipped. A template user gets NONE of that. They get a green build, a green
    APK, an app that installs, and rc 1 AT FIRST MOUNT — the first thing they
    ever see this framework do is fail, silently, for a reason nothing in their
    own project names.

    So this lane does not read the template. It GENERATES an app and PUBLISHES
    it, and asserts the 8.0 tripwire trio on GENERATED OUTPUT (8.3 design
    decision 4):

      · exactly 4 IL2072    — the app is rooted and the libraries are still
                              trimmable. A whole-module trim drives this to 0.
      · exactly the 10      — the exports still ride out of the REFERENCED
        blazornative_*        Runtime assembly, and CanonicalizeNativeArtifactName
        exports              produced BlazorNative.Runtime.dll from <AppName>.dll
                              (the frozen shell contract, proven for a name that
                              is not BlazorNative.SampleApp).
      · the page probe      — THE APP SURVIVED. The starter page's name-string is
                              in the native image.

    A TEXT PIN ON THE TEMPLATE CONTENT WAS CONSIDERED AND REFUSED, and the reason
    is the whole phase: a text pin proves the LINE IS IN THE FILE. It cannot see
    ILC silently ignoring an unresolvable root (MSBuild does not validate a root
    name — a bogus one passes evaluation with no error and no warning), a dropped
    UnmanagedEntryPointsAssembly, a broken RID condition, or a missing PublishAot.
    And, decisively: THE LINE'S ABSENCE IN 8.0 WAS FOUND BY A PUBLISH, NOT BY A
    READ. "The line is in the file" and "the app survives ILC" are different
    claims, and only the second one matters.

    THE LANE:

      1. pack      — templates/BlazorNative.Templates → artifacts/templates. Its
                     OWN feed, never artifacts/packages: consumer-smoke.ps1
                     asserts an EXACT 6 nupkg + 5 snupkg there, and a seventh
                     would turn an exact count into "6 or 7, depending".
      2. install   — `dotnet new install <nupkg>`. The REAL shipping path (the
                     pack, not the content directory) — uninstalled in the
                     finally, so nothing is left machine-wide.
      3. create    — `dotnet new blazornative -n <App> -o <temp>` into a TEMP
                     directory outside the repo: no Directory.Build.props, no
                     repo global.json, no solution. A true out-of-repo consumer.
      4. restore   — from the LOCAL feed into a temp package cache, --no-cache,
                     PROVENANCE ASSERTED ×3 (Runtime/Components/Analyzers came
                     from artifacts/packages, not nuget.org) — the ConsumerSmoke
                     pattern verbatim. The template is the SECOND out-of-repo
                     consumer, and a much fuller one.
      5. publish   — `-r win-x64` → THE TRIPWIRE TRIO ABOVE. One RID: it is the
                     cheapest (no NDK, no bionic bypass) and it is the RID the
                     page probe already runs on in ci.yml. The trim law is a fact
                     about the app's SHAPE, not about the RID.
      6. assemble  — `gradlew assembleDebug -PappPubRoot=<the SAMPLE's publish
                     tree>` on the generated Android project. ZERO EXTRA
                     PUBLISHES: build-test already published both bionic RIDs for
                     the sample, and the .so is a BUILD INPUT staged into jniLibs
                     — its contents are irrelevant to a Kotlin compile and an APK
                     assembly.

    WHY STEP 6 IS A REAL COMPILE AND NOT `gradlew tasks`: the AGP 9 incident was
    a `sourceSets` misconfiguration that silently STOPPED COMPILING THE ENTIRE
    SHELL while everything stayed green. `gradlew tasks` would not have caught
    it. The template's build.gradle.kts declares those same `kotlin.srcDirs` —
    the template could ship that exact bug, reborn, and ONLY A REAL COMPILE SEES
    IT. `assembleDebug` compiles src/androidMain/kotlin in the template's own
    tree. That is the assertion.

    HONESTY, STATED RATHER THAN PAPERED OVER (8.3 design decision 8, U2): the
    APK carries the SAMPLE's .so. Step 6 proves the gradle tree is coherent, the
    parameterized files are valid, the shell Kotlin COMPILES IN THE TEMPLATE'S
    OWN TREE, and the APK assembles. It does NOT prove the generated app's own
    .so pairs with the generated APK. The residual is small — step 5 proves the
    generated app's own native image roots, exports and canonicalizes; the .so is
    the same target's other extension — but it is a SEAM, and it is named.

    PRECONDITION: artifacts/packages must hold the six (this script does NOT pack
    them — consumer-smoke.ps1 owns that, and in ci.yml it runs immediately
    before this step). A missing feed fails loudly below, naming the fix.

    Runs locally and as the ci.yml template-smoke step (build-test, windows).
    IT NEEDS THE VS TOOLCHAIN: the generated app's win-x64 AOT publish links
    through it, and the export check reads the image with dumpbin. Both are
    resolved from the VS Installer directory, which the script also PREPENDS TO
    PATH for its own process — ILC's link step invokes `vswhere.exe` by bare name
    (ci.yml does the same job-wide for its own publish gates), so without it the
    failure surfaces as "'vswhere.exe' is not recognized" from inside the linker,
    which names nothing useful and reads like a template defect.

.EXAMPLE
    .\scripts\template-smoke.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot      = Resolve-Path (Join-Path $PSScriptRoot "..")
$templateProj  = Join-Path $repoRoot "templates\BlazorNative.Templates"
$templateFeed  = Join-Path $repoRoot "artifacts\templates"
$packageFeed   = Join-Path $repoRoot "artifacts\packages"
$samplePubRoot = Join-Path $repoRoot "samples\BlazorNative.SampleApp\bin\Release\net10.0"

$templateId  = "BlazorNative.Templates"
$shortName   = "blazornative"

# THE APP NAME IS DELIBERATELY NOT "MyBlazorNativeApp" (the template's
# sourceName) AND NOT "BlazorNative.SampleApp" (the repo's own publish head).
# Both matter: the first proves substitution fired, the second is what makes the
# export/canonicalization assertion mean something (P5 — the frozen artifact name
# is produced from an assembly name that is NOT the sample's).
$appName = "TemplateSmokeApp"

# The starter page's name-string, as AppPages.All registers it. THE PROBE'S
# SUBJECT — see the mutation note at step 5.
$starterPage = "BnStarterPage"

# The template's csproj references exactly these three (Core/Renderer/Http arrive
# transitively via Runtime). TemplateDriftTests pins the referenced ids as a
# subset of the shipped six; this script asserts their PROVENANCE.
$referencedIds = @("Runtime", "Components", "Analyzers")

# The frozen export surface — identical to ci.yml's publish gates, because the
# whole point is that a GENERATED app clears the SAME bar the repo's app clears.
$expectedExports = @(
    'blazornative_dispatch_event', 'blazornative_fetch_complete',
    'blazornative_host_call_complete', 'blazornative_host_event',
    'blazornative_init', 'blazornative_mount',
    'blazornative_register_bridge', 'blazornative_register_frame_callback',
    'blazornative_shutdown', 'blazornative_version'
)

function Write-Step([string]$text) { Write-Host "  ⟶  $text" -ForegroundColor Gray }
function Write-OK([string]$text)   { Write-Host "  ✓  $text" -ForegroundColor Green }
function Write-Fail([string]$text) { Write-Host "  ✗  $text" -ForegroundColor Red }

Write-Host ""
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  BlazorNative template smoke — a GENERATED app clears the publish bar (M8 DoD #4)" -ForegroundColor White
Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

# ── 0. The ONE version, parsed from src/Directory.Build.props ────────────────
# The same read consumer-smoke.ps1 performs, for the same reason: the props is
# the ONE version source, and the template's literals are MIRRORS of it
# (TemplateDriftTests holds them equal). Here it is used to NAME the pack file
# and the restored package versions — so a drift that slipped both pins still
# fails to find its nupkg.
$propsPath = Join-Path $repoRoot "src\Directory.Build.props"
if (-not (Test-Path $propsPath)) {
    Write-Fail "src/Directory.Build.props not found — it is the one version source (8.1 decision 4)"
    exit 1
}
$propsXml = [xml](Get-Content $propsPath -Raw)
$versionNodes = @($propsXml.Project.PropertyGroup | ForEach-Object { $_ } |
    Where-Object { $_.PSObject.Properties.Name -contains "Version" } |
    ForEach-Object { $_.Version })
if ($versionNodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($versionNodes[0])) {
    Write-Fail "expected exactly ONE non-empty <Version> in src/Directory.Build.props, found $($versionNodes.Count) — the single version truth is gone"
    exit 1
}
$version = $versionNodes[0]
Write-OK "version source: src/Directory.Build.props → $version"

# ── The feed precondition (consumer-smoke.ps1 owns the pack) ─────────────────
# This script does NOT pack the six. In ci.yml the consumer smoke runs
# immediately before it and leaves artifacts/packages populated; locally that is
# the documented order. Asserted rather than assumed — a restore that silently
# fell through to nuget.org would prove nothing about THIS commit's packages.
if (-not (Test-Path $packageFeed)) {
    Write-Fail "artifacts/packages not found — this lane restores the generated app from the LOCAL feed. Run .\scripts\consumer-smoke.ps1 first (it packs the six; in ci.yml it is the step immediately before this one)."
    exit 1
}
$feedNupkgs = @(Get-ChildItem $packageFeed -Filter "BlazorNative.*.nupkg" -ErrorAction SilentlyContinue)
if ($feedNupkgs.Count -eq 0) {
    Write-Fail "artifacts/packages holds no BlazorNative.*.nupkg — run .\scripts\consumer-smoke.ps1 first"
    exit 1
}
Write-OK "local feed present: $($feedNupkgs.Count) BlazorNative.* nupkg in artifacts/packages"

$genRoot     = Join-Path ([System.IO.Path]::GetTempPath()) "blazornative-template-smoke-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$cache       = Join-Path ([System.IO.Path]::GetTempPath()) "blazornative-template-cache-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$installed   = $false
$savedNuGetPackages = $env:NUGET_PACKAGES

try {
    # ── 1. Pack the template → artifacts/templates ───────────────────────────
    Write-Step "packing the template → artifacts/templates ..."
    if (Test-Path $templateFeed) { Remove-Item -Recurse -Force $templateFeed }
    $packLog = & dotnet pack $templateProj -c Release -o $templateFeed -tl:off -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $packLog | Select-Object -Last 20 | Out-Host
        Write-Fail "dotnet pack $templateId failed (exit $LASTEXITCODE)"
        exit 1
    }
    # The zero-warning bar applies here exactly as it does to the six.
    $packWarnings = @($packLog | Where-Object { "$_" -match '\bwarning\b' })
    if ($packWarnings.Count -ne 0) {
        $packWarnings | Out-Host
        Write-Fail "template pack emitted $($packWarnings.Count) warning(s) — the zero-warning bar applies"
        exit 1
    }
    $pack = Join-Path $templateFeed "$templateId.$version.nupkg"
    if (-not (Test-Path $pack)) {
        $found = @(Get-ChildItem $templateFeed -Filter "*.nupkg" -ErrorAction SilentlyContinue)
        Write-Fail "expected '$templateId.$version.nupkg' in artifacts/templates — the pack's own <Version> disagrees with src/Directory.Build.props ($version). Feed: $($found.Name -join ', ')"
        exit 1
    }
    Write-OK "template packed at $version, zero warnings: $([System.IO.Path]::GetFileName($pack))"

    # ── 2. Install the template FROM THE PACK ────────────────────────────────
    # From the nupkg, not from the content directory: the pack is what a user
    # installs, so the pack is what this lane proves. A pack whose content
    # excludes swallowed a file (the .gitignore find of Gate 1 — the Gradle
    # `build/` rule was eating build/BionicNativeAot.targets) is invisible to a
    # directory install and fatal to a real one.
    Write-Step "dotnet new install $([System.IO.Path]::GetFileName($pack)) ..."
    # Best-effort pre-uninstall so a rerun after an interrupted run is clean.
    # The template does not publish this phase, so nothing legitimate can be
    # installed from anywhere else.
    & dotnet new uninstall $templateId 2>&1 | Out-Null
    $global:LASTEXITCODE = 0
    $installLog = & dotnet new install $pack 2>&1
    if ($LASTEXITCODE -ne 0) {
        $installLog | Out-Host
        Write-Fail "dotnet new install failed (exit $LASTEXITCODE)"
        exit 1
    }
    $installed = $true
    $installLine = @($installLog | Where-Object { "$_" -match 'Success' }) | Select-Object -First 1
    Write-OK "installed: $(if ($installLine) { "$installLine".Trim() } else { "$templateId@$version" })"

    # ── 3. Create the app (P1) ───────────────────────────────────────────────
    $appDir = Join-Path $genRoot $appName
    Write-Step "dotnet new $shortName -n $appName -o <temp>\$appName ..."
    $newLog = & dotnet new $shortName -n $appName -o $appDir 2>&1
    if ($LASTEXITCODE -ne 0) {
        $newLog | Out-Host
        Write-Fail "dotnet new $shortName failed (exit $LASTEXITCODE)"
        exit 1
    }
    $appCsproj = Join-Path $appDir "$appName.csproj"
    if (-not (Test-Path $appCsproj)) {
        Write-Fail "the generated tree has no '$appName.csproj' — sourceName substitution did not fire. Tree: $((Get-ChildItem $appDir -File).Name -join ', ')"
        exit 1
    }
    Write-OK "created: $appName ($((Get-ChildItem $appDir -Recurse -File).Count) files), $appName.csproj present — substitution fired"

    # ── 4. Restore from the LOCAL feed, provenance asserted ×3 (P2) ──────────
    # The template ships NO nuget.config, deliberately: a real user restores from
    # nuget.org. This one is written by the HARNESS, into the generated tree, so
    # the lane proves THIS commit's packages rather than whatever is published.
    # <clear /> drops every inherited source so nothing leaks in from a user or
    # machine config — the ConsumerSmoke posture verbatim.
    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<!-- Written by scripts/template-smoke.ps1 — NOT template content. The template
     ships no nuget.config (a real user restores from nuget.org); this file
     points the generated app at THIS commit's packed feed so the lane proves
     the packages in this tree. -->
<configuration>
  <packageSources>
    <clear />
    <add key="blazornative-local" value="$packageFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
    Set-Content -Path (Join-Path $appDir "nuget.config") -Value $nugetConfig -Encoding UTF8

    # NUGET_PACKAGES rather than --packages: it binds the restore AND the publish
    # below (publish has no --packages switch), so the whole lane runs out of one
    # temp cache and leaves the machine's cache untouched.
    $env:NUGET_PACKAGES = $cache
    Write-Step "dotnet restore (temp cache, --no-cache; sources = the generated nuget.config only) ..."
    $restoreLog = & dotnet restore $appDir --no-cache -tl:off -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $restoreLog | Select-Object -Last 25 | Out-Host
        Write-Fail "the generated app failed to restore (exit $LASTEXITCODE) — a `dotnet new` app must restore from the packages ALONE"
        exit 1
    }
    # Provenance: NuGet records every extracted package's origin in
    # <cache>/<id>/<version>/.nupkg.metadata. The three referenced ids MUST come
    # from the local feed — a restore that silently reached nuget.org would prove
    # something about a PUBLISHED version, not about this commit.
    foreach ($proj in $referencedIds) {
        $id = "blazornative.$($proj.ToLowerInvariant())"
        $metaPath = Join-Path $cache "$id\$version\.nupkg.metadata"
        if (-not (Test-Path $metaPath)) {
            Write-Fail "$id $version was not restored at all (no $metaPath) — the generated csproj's PackageReference version disagrees with the feed?"
            exit 1
        }
        $meta = Get-Content $metaPath -Raw | ConvertFrom-Json
        if ($meta.source -notlike "*artifacts*packages*") {
            Write-Fail "$id restored from '$($meta.source)' — expected the local artifacts/packages feed"
            exit 1
        }
        Write-Host "     $id $version ← $($meta.source)" -ForegroundColor DarkGray
    }
    Write-OK "restore clean: the generated app's three BlazorNative references came from the local feed"

    # ── 5. THE PHASE'S POINT — publish win-x64, the tripwire trio ────────────
    # The host toolchain is resolved BEFORE the publish, not after: both the
    # ILCompiler's link step and the export check below need it, and a missing
    # toolchain discovered three minutes into an AOT compile reads as a template
    # defect when it is a host fact.
    #
    # TWO DISTINCT NEEDS, one file. dumpbin is found by ABSOLUTE path (below);
    # but ILC's win-x64 link step invokes `vswhere.exe` BY BARE NAME, so the VS
    # Installer directory must be ON PATH — ci.yml does exactly this job-wide
    # ("The ILCompiler win-x64 link step invokes vswhere.exe by bare name") for
    # its own publish gates, so in CI this is already true and the prepend is a
    # no-op. Doing it here as well is what makes the script runnable from a
    # plain local pwsh instead of failing in the linker with
    # "'vswhere.exe' is not recognized", which names nothing useful.
    $vsInstallerDir = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
    $vswhere = Join-Path $vsInstallerDir "vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        Write-Fail "vswhere.exe not found at $vswhere — the generated app's win-x64 AOT publish links through the VS toolchain and the export check below needs dumpbin. This is ci.yml's own posture (windows-latest with VS Build Tools); it is a host fact, not a template defect."
        exit 1
    }
    if (($env:PATH -split ';') -notcontains $vsInstallerDir) {
        $env:PATH = "$vsInstallerDir;$env:PATH"
    }
    $dumpbin = & $vswhere -latest -products * -find "**\Hostx64\x64\dumpbin.exe" | Select-Object -First 1
    if (-not $dumpbin) {
        Write-Fail "dumpbin.exe not found via vswhere — the export check cannot read the generated app's native image"
        exit 1
    }

    Write-Step "dotnet publish -c Release -r win-x64 (the generated app) ..."
    $publishLog = Join-Path $genRoot "publish-win-x64.log"
    & dotnet publish $appDir -c Release -r win-x64 -tl:off -nologo 2>&1 | Tee-Object -FilePath $publishLog | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Get-Content $publishLog | Select-Object -Last 30 | Out-Host
        Write-Fail "the generated app failed to publish win-x64 (exit $LASTEXITCODE)"
        exit 1
    }

    # ── THE TRIPWIRE TRIO ────────────────────────────────────────────────────
    # ALL THREE ARMS ARE EVALUATED BEFORE ANY OF THEM FAILS THE LANE, and that is
    # deliberate rather than tidiness: THE TRIO IS A SIGNATURE, NOT THREE
    # INDEPENDENT CHECKS. 8.0's failure was recognised precisely because its arms
    # moved TOGETHER — 4 → 0 IL2072, ~400KB smaller, the page names gone. A
    # fail-fast at the first arm would print "trim drift: got 0" and leave the
    # reader to guess whether the app survived; printing the whole signature at
    # once is what lets the message SAY which failure this is. (It is also what
    # makes the delete-the-line mutation quotable in both arms, which the gate
    # requires: a script that exited at arm 1 could never show arm 3 red.)
    $tripwires = @()

    # ── ARM 1 — exactly 4 IL2072 ─────────────────────────────────────────────
    # The trim law, on generated output. 4 = the app is rooted AND the libraries
    # are still trimmable; all four come from Blazor's own reflection internals
    # (ComponentProperties / CascadingParameterState / ComponentFactory), none
    # from the app's pages. A WHOLE-MODULE TRIM DRIVES THIS TO 0 — which is why
    # the number is asserted exactly and not as a ceiling: 0 is not "better", it
    # is the 8.0 failure.
    $il2072 = @(Select-String -Path $publishLog -Pattern 'warning IL2072')
    $armIl2072Ok = $il2072.Count -eq 4
    Write-Host "     [arm 1] IL2072 count: $($il2072.Count) $(if ($armIl2072Ok) { '✓' } else { "✗ (expected exactly 4)" })" -ForegroundColor DarkGray
    if (-not $armIl2072Ok) {
        $il2072 | ForEach-Object { Write-Host "               $($_.Line.Trim())" -ForegroundColor DarkGray }
        $tripwires += "ARM 1 — IL2072: expected exactly 4 accepted trim warnings, got $($il2072.Count). " + $(
            if ($il2072.Count -eq 0) {
                "ZERO is the whole-module trim: ILC dropped the generated app entirely."
            } else {
                "More than 4 means new reflection reached the image — read the lines above. A NEW warning shape under NativeAOT is worth seeing (that is what TrimmerSingleWarn=false is for). Do not raise the number to make this green."
            })
    }

    # ── ARM 2 — exactly the 9 exports, off a canonicalized artifact ──────────
    # THE FROZEN NAME, produced from an assembly name that is not the sample's
    # (P5). CanonicalizeNativeArtifactName renamed <AppName>.dll →
    # BlazorNative.Runtime.dll INSIDE the publish folder — that target was built
    # for a directory re-point and it is what lets a generated app reuse the
    # shell's Native.load("BlazorNative.Runtime") with ZERO shell edits.
    $publishDir = Join-Path $appDir "bin\Release\net10.0\win-x64\publish"
    $dll = Join-Path $publishDir "BlazorNative.Runtime.dll"
    $dllPresent = Test-Path $dll
    if (-not $dllPresent) {
        # Fatal to arms 2 and 3 both — there is nothing to interrogate. Reported
        # as an arm rather than thrown, so the IL2072 arm above still prints.
        $tripwires += "ARM 2 — BlazorNative.Runtime.dll ABSENT from the generated app's publish ($publishDir): CanonicalizeNativeArtifactName did not produce the frozen shell artifact name from $appName.dll. Contents: $((Get-ChildItem $publishDir -File -ErrorAction SilentlyContinue).Name -join ', '). Arms 2 and 3 cannot be evaluated."
    }
    else {
        # The canonicalization is asserted through its own message too: it names
        # the SOURCE, so this is the direct evidence that the frozen name came
        # from $appName.dll rather than from a file that happened to be called
        # right.
        $canonLine = @(Select-String -Path $publishLog -Pattern 'Canonicalized native artifact') | Select-Object -First 1
        if (-not $canonLine -or "$($canonLine.Line)" -notmatch [regex]::Escape("$appName.dll")) {
            $tripwires += "ARM 2 — the publish log does not show CanonicalizeNativeArtifactName renaming '$appName.dll': the frozen artifact name may be a coincidence rather than a product of the target. Line: $(if ($canonLine) { $canonLine.Line.Trim() } else { '<absent>' })"
        }
        else {
            Write-Host "     [arm 2] $($canonLine.Line.Trim())" -ForegroundColor DarkGray
        }

        $out = & $dumpbin /exports $dll
        $actual = @($out | Select-String -Pattern '\bblazornative_\w+' | ForEach-Object { $_.Matches.Value } | Sort-Object -Unique)
        $exportDiff = Compare-Object $expectedExports $actual
        Write-Host "     [arm 2] Exports found ($($actual.Count)): $($actual -join ', ') $(if (-not $exportDiff) { '✓' } else { '✗' })" -ForegroundColor DarkGray
        if ($exportDiff) {
            $exportDiff | Format-Table | Out-String | Write-Host
            $tripwires += "ARM 2 — EXPORT DRIFT: expected exactly the 9 blazornative_* exports, found $($actual.Count). They are emitted from the REFERENCED BlazorNative.Runtime assembly via UnmanagedEntryPointsAssembly — is that line still in the template's csproj?"
        }
    }

    # ── ARM 3 — THE APP SURVIVED ─────────────────────────────────────────────
    # The page's name-string in the native image is the direct evidence that the
    # generated app's module reached the binary at all. THIS IS THE ARM THE OTHER
    # TWO CANNOT COVER: 4 IL2072 and 9 exports are facts about the LIBRARIES and
    # the runtime — both survive a whole-module trim of the APP — so only this one
    # is a fact about the USER'S CODE.
    #
    # MUTATION-PROVEN, not assumed (8.3 design decision 4 — a gate requirement):
    # deleting TrimmerRootAssembly from the template must red this arm AND arm 1.
    # It asserts the presence of a string in a binary, which is exactly the shape
    # that passes vacuously if the string happens to appear in framework code — so
    # the name is chosen to be discriminating, and the mutation is what proves it
    # is, rather than a claim that it is.
    $dllKb = if ($dllPresent) { [math]::Round((Get-Item $dll).Length / 1KB) } else { 0 }
    $pagePresent = $false
    if ($dllPresent) {
        $bytes = [System.IO.File]::ReadAllBytes($dll)
        $text  = [System.Text.Encoding]::ASCII.GetString($bytes)
        $pagePresent = $text -match [regex]::Escape($starterPage)
        Write-Host "     [arm 3] Page-presence probe '$starterPage': $(if ($pagePresent) { "present ✓" } else { "ABSENT ✗" }) (image $dllKb KB)" -ForegroundColor DarkGray
        if (-not $pagePresent) {
            $tripwires += "ARM 3 — THE GENERATED APP DID NOT SURVIVE ILC: the starter page's name '$starterPage' is ABSENT from its published native image ($dllKb KB). Its module was trimmed away, so its pages never register."
        }
    }

    if ($tripwires.Count -ne 0) {
        Write-Host ""
        $tripwires | ForEach-Object { Write-Fail "  $_" }
        Write-Host ""
        # THE SIGNATURE, named when it is unambiguous. Arm 1 at zero AND arm 3
        # absent together is not "two failures" — it is ONE failure with a name,
        # and naming it is the difference between a reader fixing the cause and a
        # reader adjusting a number until the lane goes green.
        if ($il2072.Count -eq 0 -and $dllPresent -and -not $pagePresent) {
            Write-Fail @"
══════════════════════════════════════════════════════════════════════
THIS IS THE 8.0 SILENT TRIM, REACHING A TEMPLATE USER.

The signature is complete and it is unmistakable: the publish was GREEN,
the trim warnings went 4 → 0, and the app's own page is GONE from the
native image ($dllKb KB). ILC roots ONLY the input module's
UnmanagedCallersOnly exports in nativelib mode — this app's exports are
emitted from the REFERENCED Runtime assembly, so NOTHING in the image
references the app's code and ILC trimmed the ENTIRE module, its
[ModuleInitializer] included. The pages never register.

A user generating this template would get: a green build, a green APK,
an app that installs, and rc 1 AT FIRST MOUNT — with nothing in their
own project naming the cause. THAT IS THE FAILURE THIS LANE EXISTS FOR.

THE FIX: <TrimmerRootAssembly Include="`$(AssemblyName)" /> in the
template's csproj (templates/BlazorNative.Templates/content/
BlazorNative.App/MyBlazorNativeApp.csproj), in the ItemGroup beside
UnmanagedEntryPointsAssembly. Its comment explains what breaks.

Do NOT make this green by relaxing the counts. 0 is not "fewer
warnings" — it is the app being gone.
══════════════════════════════════════════════════════════════════════
"@
        }
        else {
            Write-Fail "THE GENERATED APP DOES NOT CLEAR THE PUBLISH BAR ($($tripwires.Count) of the 3 tripwire arms red). The bar is the repo's own: exactly 4 IL2072 + exactly the 9 blazornative_* exports + the starter page present in the native image. A template that produces an app the gates would reject is a broken template."
        }
        exit 1
    }
    Write-OK "THE GENERATED APP CLEARS THE PUBLISH BAR: 4 IL2072, the 9 exports off BlazorNative.Runtime.dll (canonicalized from $appName.dll), '$starterPage' in the image ($dllKb KB)"

    # ── 6. The generated Android project assembles (P6) ──────────────────────
    # -PappPubRoot points the generated project at the SAMPLE's publish tree:
    # build-test already published both bionic RIDs (ci.yml's win-x64/bionic-x64/
    # bionic-arm64 gates run before this step), and the .so is a build INPUT
    # staged into jniLibs — its contents are irrelevant to a Kotlin compile and an
    # APK assembly. So a real APK assembles for the price of a gradle run: ZERO
    # extra publishes. (The honest residual is U2 — the APK carries the sample's
    # .so, so this proves the tree compiles and assembles, not that the generated
    # app's own .so pairs with it. Step 5 proves the generated native image
    # itself.)
    foreach ($rid in @("linux-bionic-x64", "linux-bionic-arm64")) {
        $so = Join-Path $samplePubRoot "$rid\publish\BlazorNative.Runtime.so"
        if (-not (Test-Path $so)) {
            Write-Fail "the sample's $rid publish is missing ($so) — this lane reuses it as the generated project's jniLibs input (zero extra publishes). In ci.yml the bionic publish gates run before this step; locally: dotnet publish samples/BlazorNative.SampleApp -c Release -r $rid"
            exit 1
        }
    }
    $appPubRoot = (Resolve-Path $samplePubRoot).Path
    $androidDir = Join-Path $appDir "android"
    Write-Step "gradlew assembleDebug -PappPubRoot=<the sample's publish tree> (the generated Android project) ..."
    Push-Location $androidDir
    try {
        & .\gradlew.bat --no-daemon assembleDebug "-PappPubRoot=$appPubRoot" 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Fail @"
THE GENERATED ANDROID PROJECT DOES NOT ASSEMBLE (exit $LASTEXITCODE).

This step is a REAL COMPILE rather than a `gradlew tasks` configuration check for
one reason: the AGP 9 incident was a `sourceSets` misconfiguration that silently
stopped compiling the ENTIRE shell while everything stayed green. The template's
build.gradle.kts declares those same kotlin.srcDirs — it could ship that exact
bug, reborn, and only a real compile sees it.
"@
            exit 1
        }
    }
    finally { Pop-Location }
    $apks = @(Get-ChildItem (Join-Path $androidDir "build\outputs\apk\debug") -Filter "*.apk" -ErrorAction SilentlyContinue)
    if ($apks.Count -eq 0) {
        Write-Fail "gradlew assembleDebug reported success but produced no APK under android/build/outputs/apk/debug — an assembly that assembles nothing is the AGP-9 shape (green and structurally unreachable)"
        exit 1
    }
    Write-OK "the generated Android project assembles: $($apks[0].Name) ($([math]::Round($apks[0].Length / 1KB)) KB) — the shell Kotlin compiles in the TEMPLATE'S OWN tree"

    Write-Host ""
    Write-OK "template smoke PASS — `dotnet new $shortName` creates, restores from packages alone, PUBLISHES with 4 IL2072 + the 9 exports + its page in the image, and assembles an APK (M8 DoD #4)"
    exit 0
}
finally {
    # Nothing is left behind: not the installed template (it is machine-wide
    # state), not the generated tree, not the temp cache, not the env var.
    if ($installed) {
        & dotnet new uninstall $templateId 2>&1 | Out-Null
        $global:LASTEXITCODE = 0
    }
    if ($null -eq $savedNuGetPackages) { Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue }
    else { $env:NUGET_PACKAGES = $savedNuGetPackages }
    if (Test-Path $genRoot) { Remove-Item -Recurse -Force $genRoot -ErrorAction SilentlyContinue }
    if (Test-Path $cache)   { Remove-Item -Recurse -Force $cache   -ErrorAction SilentlyContinue }
}
