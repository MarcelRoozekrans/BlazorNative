#Requires -Version 7
<#
.SYNOPSIS
    BlazorNative — release preflight (Phase 8.2 Gate 1, M8 DoD #3: the release
    pipeline's decisions, in a script the required lane can self-test).

.DESCRIPTION
    THE DESIGN PRINCIPLE THIS FILE EXISTS FOR (8.2 design decision 6): *the
    untestable surface must be as small as possible.* YAML expressions, `if:`
    guards and shell one-liners inside a workflow that fires ONCE PER RELEASE
    are the least testable code in the repository — so every decision the
    release makes lives HERE, where `-SelfTest` runs it on every PR, and
    `release.yml` holds only wiring.

    Three jobs, one per switch:

      -SelfTest    The 8-row classifier table (8.2 design decision 4), NEGATIVE
                   ROWS INCLUDED. This is what makes the tag↔props assertion
                   provable WITHOUT a Release: on a PR there is no tag, so a
                   lane that "tested" the assertion against a synthetic tag
                   derived from the props would compare the props TO ITSELF —
                   green, vacuous, and exactly 8.1's headline sin. The table
                   supplies real negative inputs instead (`pkg/9.9.9` vs props
                   `1.0.0-preview.1` is a REAL mismatch, and its row expects
                   RED).

      -Tag <t>     Classify + assert, then emit the verdict for release.yml's
                   push-job guard. Two disjoint namespaces (8.2 decision 1):
                     v<N>.<M>       -> milestone   -> SKIP, exit 0, ANNOUNCED
                     pkg/<semver>   -> package     -> assert tag == props
                     anything else  -> unrecognized-> RED
                   THE PROPS WINS AND THE TAG IS A CLAIM (8.2 decision 2): this
                   script VERIFIES `pkg/X` against src/Directory.Build.props and
                   NEVER overrides it. No `-p:Version=` exists anywhere in the
                   release path — pinned by ReleaseWorkflowPinTests, because it
                   is the exact drift a contributor imports by copying the
                   reference implementation.

      -Preflight   The nuget.org state check — the ONE thing a real push can
                   reject that no local step can know (8.2 decision 4). Two
                   POSITIVE-CONTROL ARMS run BEFORE the six are consulted; see
                   the vacuity note on Test-NugetState below. It is the reason
                   `--skip-duplicate` is safe in release.yml: this reds on a
                   forgotten props bump BEFORE the push job starts.

    WHY THIS SCRIPT NAMES NO PACKAGES (8.1 normative rule 2, the I-2 finding).
    The shipped set already has four copies and every one of them is pinned. A
    hardcoded list of six IDs here would be a FIFTH — so there isn't one: the
    IDs are ENUMERATED from src/*/*.csproj, the same derivation
    PackageVersionPinTests and PackagePurityTests' third tooth already use
    (PackageId defaults to the assembly name for all six — src/Directory.Build.props
    carries no explicit PackageId lines, by construction). Enumerating removes
    the copy rather than pinning it: there is nothing left to drift. Non-vacuity
    is asserted — an enumeration that finds nothing is RED, never a cheerful
    "all clear" (the house rule at PackagePurityTests.TypeNamesOf).

.EXAMPLE
    .\scripts\release-preflight.ps1 -SelfTest
    .\scripts\release-preflight.ps1 -Tag pkg/1.0.0-preview.1
    .\scripts\release-preflight.ps1 -Preflight
#>

[CmdletBinding()]
param(
    # The Release's tag (release.yml passes github.event.release.tag_name).
    [string]$Tag,

    # The nuget.org state preflight — two control arms, then the shipped six.
    [switch]$Preflight,

    # The classifier + assertion table (the 8 rows, negative rows included).
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

function Write-Step([string]$text) { Write-Host "  ⟶  $text" -ForegroundColor Gray }
function Write-OK([string]$text)   { Write-Host "  ✓  $text" -ForegroundColor Green }
function Write-Fail([string]$text) { Write-Host "  ✗  $text" -ForegroundColor Red }

# ─────────────────────────────────────────────────────────────────────────────
#  The two namespaces (8.2 design decision 1)
#
#  `git tag -l 'v*'` and `git tag -l 'pkg/*'` partition the tag space with an
#  EMPTY intersection — the disjointness is an assertion you can run, not a
#  convention you have to remember. Seven milestone tags exist (v1.0…v7.0), all
#  Release-less; v8.0 is DoD #6's close and does not exist yet.
#
#  THE HAZARD THIS MILESTONE CREATES, stated where the regexes live: the
#  `release` event carries NO tag filter, so EVERY published Release fires
#  release.yml — including one published on `v8.0` at M8's close. An
#  AdoNet.Async-shaped `VERSION="${TAG#v}"` would turn that into six packages
#  pushed at version "8.0", permanently (nuget.org has no hard delete). Two
#  INDEPENDENT guards stop it: MilestoneTagPattern below reads the tag's SHAPE,
#  and the props assertion reads its CONTENT. Neither is load-bearing alone.
# ─────────────────────────────────────────────────────────────────────────────

# Milestone: v<N>.<M> and nothing else. Mutation vehicle (8.2 mutation 1):
# narrow this to match nothing -> `v8.0` falls through to UNRECOGNIZED -> RED,
# which proves the SKIP arm is a decision rather than a default.
$MilestoneTagPattern = '^v(\d+)\.(\d+)$'

# Package release: the `pkg/` namespace. The payload is validated as semver
# SEPARATELY (below), so a `pkg/` tag with a bad payload diagnoses as MALFORMED
# ("you meant a package release and typo'd the version") rather than as
# UNRECOGNIZED ("you used neither namespace"). Different mistakes, different
# sentences.
$PackageTagPattern = '^pkg/(.+)$'

# SemVer 2.0.0, the official expression from semver.org. `1.0.0-preview.1` and
# `9.9.9` match; `not-a-version` does not.
$SemVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$'

# The two-arm positive control's subject (8.2 design decision 4). newtonsoft.json
# is chosen because it is the single most-published id on nuget.org: it will not
# be unlisted, and 13.0.3 will not stop existing. Verified live at design time
# (84 versions, 13.0.3 present) and again at Gate 1.
$ControlId             = "newtonsoft.json"
$ControlPresentVersion = "13.0.3"
$ControlAbsentVersion  = "0.0.0-does-not-exist"

$NugetFlatContainer = "https://api.nuget.org/v3-flatcontainer"

# ── The version truth ────────────────────────────────────────────────────────

<#
.SYNOPSIS
    The ONE version, parsed from src/Directory.Build.props (8.1 decision 4).
.DESCRIPTION
    consumer-smoke.ps1 parses the same element the same way, and
    PackageVersionPinTests reds if it is ever not exactly one. This function is
    deliberately the ONLY reader of the props here: everything downstream takes
    the version as a PARAMETER, which is what lets -SelfTest feed the classifier
    a fixed props value instead of the live one (a table pinned to the live
    props would break on every bump AND would make row 1 a tautology).
#>
function Get-PropsVersion {
    $propsPath = Join-Path $repoRoot "src\Directory.Build.props"
    if (-not (Test-Path $propsPath)) {
        throw "src/Directory.Build.props not found — it is the one version source (8.1 decision 4)."
    }
    $propsXml = [xml](Get-Content $propsPath -Raw)
    $versionNodes = @($propsXml.Project.PropertyGroup | ForEach-Object { $_ } |
        Where-Object { $_.PSObject.Properties.Name -contains "Version" } |
        ForEach-Object { $_.Version })
    if ($versionNodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($versionNodes[0])) {
        throw "expected exactly ONE non-empty <Version> in src/Directory.Build.props, found $($versionNodes.Count) — the single version truth is gone (PackageVersionPinTests reds on this too)."
    }
    return $versionNodes[0]
}

<#
.SYNOPSIS
    The shipped package IDs, ENUMERATED from src/ — never rostered. See the
    file header: a literal list here would be the shipped set's FIFTH copy.
#>
function Get-ShippedPackageIds {
    $srcDir = Join-Path $repoRoot "src"
    $ids = @(Get-ChildItem $srcDir -Filter "*.csproj" -Recurse -File |
        ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_.Name) } |
        Sort-Object)

    if ($ids.Count -eq 0) {
        throw "enumerated ZERO csprojs under $srcDir — the preflight would report 'all clear' over an EMPTY set, which is a scanner that cannot see its subject passing vacuously. Fix the enumeration; do not let it green."
    }
    return $ids
}

# ─────────────────────────────────────────────────────────────────────────────
#  THE CLASSIFIER + THE ASSERTION — one pure function over (tag, props version)
#
#  Pure and parameterised ON PURPOSE. It is the whole reason the self-test table
#  can supply REAL negative inputs (`pkg/9.9.9` against props `1.0.0-preview.1`)
#  rather than deriving a tag from the live props and comparing the props to
#  itself. `-Tag` passes the live props; `-SelfTest` passes the table's.
# ─────────────────────────────────────────────────────────────────────────────
function Get-ReleaseVerdict {
    param(
        [AllowEmptyString()][AllowNull()][string]$Tag,
        [AllowEmptyString()][AllowNull()][string]$PropsVersion
    )

    # THIS BRANCH IS A MESSAGE, NOT A DECISION — recorded because the mutation
    # sweep proved it, and a comment claiming otherwise would be a lie the next
    # reader inherits. An empty tag matches NEITHER regex below, so it already
    # falls through to the `unrecognized` arm with the SAME class and the SAME
    # verdict: deleting this branch entirely leaves the self-test 8/8 GREEN
    # (run, observed). It is kept because "the Release carries an EMPTY tag"
    # names the actual problem where the generic arm would say "tag '' is in
    # NEITHER namespace" — a sentence that reads like a typo report for a tag
    # that does not exist.
    #
    # WHAT ROW 8 OF THE TABLE ACTUALLY PROVES, measured rather than assumed —
    # an empty tag is DOUBLE-COVERED, and the two mutations disagree in a way
    # worth writing down:
    #   · break THIS branch's verdict (RED -> SKIP)  -> row 8 REDS. This branch
    #     is what executes for an empty tag, so row 8 is load-bearing on it.
    #   · delete this branch outright                -> row 8 stays GREEN, via
    #     the fall-through. That is redundancy, not vacuity: the second arm
    #     gives the same class and verdict, only a worse sentence.
    # So the belt and the braces are both real, and the table can only see the
    # belt. Deleting this branch is a safe refactor the table permits; changing
    # what it DECIDES is not.
    if ([string]::IsNullOrWhiteSpace($Tag)) {
        return [pscustomobject]@{
            Class   = "unrecognized"
            Verdict = "RED"
            Reason  = "the Release carries an EMPTY tag. Package releases use 'pkg/<semver>' (e.g. pkg/$PropsVersion); milestones use 'v<N>.<M>'."
        }
    }

    # CASE — and the asymmetry with the package arm below. Deliberate, kept, and
    # written down because it is NOT predictable from the code (Gate 1 review
    # M-1/M-2). `-match` is PowerShell's CASE-INSENSITIVE operator, so `V8.0`
    # matches HERE and SKIPs. The package arm below uses [regex]::Match, which is
    # CASE-SENSITIVE, so `PKG/1.0.0-preview.1` does NOT match, falls through, and
    # lands on unrecognized -> RED.
    #
    # So a wrong-case tag PUBLISHES NOTHING either way — the arms differ only in
    # what they SAY about it. That is why this is a comment and not a
    # normalization: both candidate fixes cost something real.
    #   · Make this arm case-SENSITIVE -> `V8.0` becomes a RED on a tag of
    #     obvious intent. That is decision 1's named hazard exactly: reddening a
    #     legitimate action trains the owner to ignore reds on release runs.
    #   · Make the package arm case-INSENSITIVE -> the DOOR opens to a tag shape
    #     the docs never define. Toward publishing is the one direction this
    #     phase's law does not bend.
    # And neither shape has a self-test row (the table is 8, and the gate pins
    # it at 8), so either change would be untested behaviour change on the one
    # script that decides whether a push happens. Comment, not code.
    if ($Tag -match $MilestoneTagPattern) {
        return [pscustomobject]@{
            Class   = "milestone"
            Verdict = "SKIP"
            Reason  = "milestone Release — nothing was published; package releases use 'pkg/<semver>'."
        }
    }

    # [regex]::Match is CASE-SENSITIVE (unlike `-match` above — see the note on
    # the milestone arm). `pkg/` is the only spelling that opens this door;
    # `PKG/` falls through to unrecognized -> RED. That strictness is the right
    # default for the publishing arm and is left as-is.
    $pkgMatch = [regex]::Match($Tag, $PackageTagPattern)
    if ($pkgMatch.Success) {
        $claimed = $pkgMatch.Groups[1].Value
        if ($claimed -notmatch $SemVerPattern) {
            return [pscustomobject]@{
                Class   = "malformed"
                Verdict = "RED"
                Reason  = "tag '$Tag' is in the package namespace but '$claimed' is not a valid SemVer 2.0.0 version. The props says '$PropsVersion' — the tag must be 'pkg/$PropsVersion'."
            }
        }

        # THE ASSERTION (8.2 decision 2): the props WINS; the tag is a CLAIM.
        # Mutation vehicle (8.2 mutation 2): neuter this comparison to
        # always-true -> the `pkg/9.9.9` row returns PUBLISH -> its row expects
        # RED -> the self-test reds. A positive control ON the positive control.
        #
        # CASE, and this is the ONE non-obvious PUBLISH in the script (Gate 1
        # review M-2), so it is stated rather than left to be discovered: `-ne`
        # is CASE-INSENSITIVE, so `pkg/1.0.0-PREVIEW.1` compares EQUAL to the
        # props' `1.0.0-preview.1` and the verdict is PUBLISH. That is benign,
        # for two independent reasons: NuGet versions ARE case-insensitive by
        # spec, and — decisively — THE TAG NEVER FEEDS THE PACK. What ships is
        # whatever src/Directory.Build.props spells, and the release path never
        # overrides it (pinned by ReleaseWorkflowPinTests
        # .TheReleaseWorkflow_NeverOverridesTheVersion). So an odd-cased tag
        # still publishes correctly-cased packages; the mis-spelling dies here.
        if ($claimed -ne $PropsVersion) {
            return [pscustomobject]@{
                Class   = "package"
                Verdict = "RED"
                Reason  = "VERSION MISMATCH — the tag claims '$claimed', src/Directory.Build.props says '$PropsVersion'. The props is the version and the tag is a claim about it (8.2 decision 2): this flow NEVER overrides the props. Either the tag is on the wrong commit, or the props bump never merged. Fix the tag (or the props, in a PR) and publish a new Release."
            }
        }

        return [pscustomobject]@{
            Class   = "package"
            Verdict = "PUBLISH"
            Reason  = "package release '$Tag' — the tag's claim matches the props version '$PropsVersion'."
        }
    }

    return [pscustomobject]@{
        Class   = "unrecognized"
        Verdict = "RED"
        Reason  = "tag '$Tag' is in NEITHER namespace. Package releases are 'pkg/<semver>' (the only shape that publishes); milestones are 'v<N>.<M>' (which publish nothing). See docs/GITHUB-SETUP.md."
    }
}

# ─────────────────────────────────────────────────────────────────────────────
#  THE NUGET.ORG SCANNER
#
#  ⚠ THE VACUITY TRAP, closed here rather than after a reviewer finds it (8.2
#  design decision 4; 8.1's headline recurring). All six IDs return 404 today —
#  so the preflight is an ABSENCE assertion over a network scanner whose every
#  current answer is "absent", and a typo'd URL, a wrong endpoint, a swallowed
#  exception or an offline runner ALL produce exactly the same green as a
#  genuinely-free ID.
#
#  Two things follow, and both are load-bearing:
#
#   1. NOTHING IS SWALLOWED. 200 and 404 are the only answers this function
#      accepts. Any other status, and any transport exception, is RED — an
#      offline runner must never be indistinguishable from six free IDs.
#   2. THE ARMS RUN FIRST (Invoke-ControlArms). The six are never reported
#      clean by a scanner that has not proven it can SEE (arm 1) and can
#      DISTINGUISH (arm 2).
# ─────────────────────────────────────────────────────────────────────────────
function Test-NugetState {
    param([Parameter(Mandatory)][string]$Id)

    $url = "$NugetFlatContainer/$($Id.ToLowerInvariant())/index.json"
    try {
        $response = Invoke-WebRequest -Uri $url -Method Get -SkipHttpErrorCheck -UseBasicParsing -TimeoutSec 30
    }
    catch {
        # NOT swallowed: a transport failure is the vacuity trap's favourite
        # disguise — it looks exactly like "the id is free".
        throw "nuget.org query FAILED for '$Id' ($url): $($_.Exception.Message). This is NOT 'the id is free' — the scanner could not see, so no absence claim below it is worth anything."
    }

    switch ($response.StatusCode) {
        200 {
            $versions = @(($response.Content | ConvertFrom-Json).versions)
            if ($versions.Count -eq 0) {
                throw "nuget.org returned 200 for '$Id' but an EMPTY versions[] — a registered id with no versions is not a shape this scanner understands; treating it as 'free' would be a guess."
            }
            return [pscustomobject]@{ Id = $Id; Registered = $true; Versions = $versions }
        }
        404 {
            return [pscustomobject]@{ Id = $Id; Registered = $false; Versions = @() }
        }
        default {
            throw "nuget.org returned HTTP $($response.StatusCode) for '$Id' ($url) — expected 200 (registered) or 404 (free). An unexpected status is RED: it is not evidence the id is free."
        }
    }
}

<#
.SYNOPSIS
    Is <Version> already on nuget.org for <Id>? The one unrecoverable mistake
    (8.2 decision 5: nuget.org versions are IMMUTABLE — a published version can
    only be UNLISTED, never replaced) and the most likely real one (forgetting
    the props bump).
#>
function Test-VersionPublished {
    param(
        [Parameter(Mandatory)][pscustomobject]$State,
        [Parameter(Mandatory)][string]$Version
    )
    if (-not $State.Registered) { return $false }
    # NuGet versions are case-insensitive ('1.0.0-Preview.1' == '1.0.0-preview.1').
    return @($State.Versions | ForEach-Object { $_.ToLowerInvariant() }) -contains $Version.ToLowerInvariant()
}

<#
.SYNOPSIS
    THE TWO-ARM POSITIVE CONTROL. Runs BEFORE the six, and a failing arm is RED.
.DESCRIPTION
    Arm 1 — THE SCANNER CAN SEE: a known-published control returns 200 with a
    non-empty versions[]. A typo'd URL or a wrong endpoint fails HERE, instead
    of reporting a cheerful "six clear".

    Arm 2 — THE SCANNER CAN DISTINGUISH: that same control is classified
    PUBLISHED at a known-present version and FREE at a known-absent one. A
    scanner that answers "free" to everything fails here — and "free to
    everything" is precisely what a broken scanner looks like when all six real
    subjects are 404 anyway.
#>
function Invoke-ControlArms {
    Write-Step "positive control arms (the six are all 404 today — an absence scanner must prove it can see BEFORE it reports absence) ..."

    # ── Arm 1: can it see? ───────────────────────────────────────────────────
    $control = Test-NugetState -Id $ControlId
    if (-not $control.Registered) {
        Write-Fail "ARM 1 FAILED: the control id '$ControlId' came back UNREGISTERED. nuget.org's single most-published package cannot be missing — the scanner is looking at the wrong place ($NugetFlatContainer) or the endpoint's shape changed. Every 'free' answer below would be this same failure wearing a green coat."
        return $false
    }
    if ($control.Versions.Count -eq 0) {
        Write-Fail "ARM 1 FAILED: '$ControlId' returned 200 but zero versions — the scanner reached something, but not the version list it claims to read."
        return $false
    }
    Write-Host "     arm 1 — the scanner SEES: $ControlId -> 200, $($control.Versions.Count) versions" -ForegroundColor DarkGray

    # ── Arm 2: can it distinguish? ───────────────────────────────────────────
    if (-not (Test-VersionPublished -State $control -Version $ControlPresentVersion)) {
        Write-Fail "ARM 2 FAILED: '$ControlId' $ControlPresentVersion must classify as PUBLISHED — it is a known-present version. The scanner reads the feed but cannot recognise a version that IS there, so its 'this version is free' verdicts mean nothing."
        return $false
    }
    if (Test-VersionPublished -State $control -Version $ControlAbsentVersion) {
        Write-Fail "ARM 2 FAILED: '$ControlId' $ControlAbsentVersion must classify as FREE — it is a known-absent version. A scanner that calls everything 'published' would red every release for no reason."
        return $false
    }
    Write-Host "     arm 2 — the scanner DISTINGUISHES: $ControlId@$ControlPresentVersion = published, $ControlId@$ControlAbsentVersion = free" -ForegroundColor DarkGray

    Write-OK "positive control PASSED: the scanner can see and can distinguish — the absence assertions below are worth their exit code"
    return $true
}

# ── The three jobs ───────────────────────────────────────────────────────────

<#
.SYNOPSIS
    -SelfTest: the 8-row table (8.2 design decision 4), negative rows included.
.DESCRIPTION
    THE PROPS VALUES ARE THE TABLE'S, NOT THE LIVE ONES. That is the point: a
    row that derived its tag from the live props would compare the props to
    itself and pass forever, including after someone deleted the comparison.
    Row 2 (`pkg/9.9.9` vs `1.0.0-preview.1`) is a real mismatch with a real
    expected RED, and it is what makes the assertion provable with no Release
    in existence.
#>
function Invoke-SelfTest {
    Write-Host ""
    Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host "  release-preflight — classifier self-test (8 rows, negative rows included)" -ForegroundColor White
    Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host ""

    $rows = @(
        @{ Tag = "pkg/1.0.0-preview.1"; Props = "1.0.0-preview.1"; Class = "package";      Verdict = "PUBLISH"; Why = "the happy path — the tag's claim matches the props" }
        @{ Tag = "pkg/9.9.9";           Props = "1.0.0-preview.1"; Class = "package";      Verdict = "RED";     Why = "a REAL mismatch (not props-vs-props) — the assertion must BITE" }
        @{ Tag = "v8.0";                Props = "1.0.0-preview.1"; Class = "milestone";    Verdict = "SKIP";    Why = "M8's own close tag — the hazard this milestone creates; it must publish NOTHING" }
        @{ Tag = "v1.0";                Props = "1.0.0-preview.1"; Class = "milestone";    Verdict = "SKIP";    Why = "an existing milestone tag — the namespace is seven tags deep already" }
        @{ Tag = "1.0.0-preview.1";     Props = "1.0.0-preview.1"; Class = "unrecognized"; Verdict = "RED";     Why = "bare semver — rejected namespace (c); note the props MATCHES and it is still RED" }
        @{ Tag = "release/1.0.0";       Props = "1.0.0-preview.1"; Class = "unrecognized"; Verdict = "RED";     Why = "rejected namespace (b) — 'release/' is spent on BRANCHES by the owner's GitVersion.yml" }
        @{ Tag = "pkg/not-a-version";   Props = "1.0.0-preview.1"; Class = "malformed";    Verdict = "RED";     Why = "the package namespace with a payload that is not semver" }
        @{ Tag = "";                    Props = "1.0.0-preview.1"; Class = "unrecognized"; Verdict = "RED";     Why = "no tag at all" }
    )

    $failures = @()
    foreach ($row in $rows) {
        $actual = Get-ReleaseVerdict -Tag $row.Tag -PropsVersion $row.Props
        $tagLabel = if ([string]::IsNullOrEmpty($row.Tag)) { "(empty)" } else { $row.Tag }

        if ($actual.Verdict -ne $row.Verdict -or $actual.Class -ne $row.Class) {
            $failures += "  '$tagLabel' + props '$($row.Props)': expected $($row.Verdict)/$($row.Class), got $($actual.Verdict)/$($actual.Class) — $($row.Why)"
            Write-Fail "$($tagLabel.PadRight(20)) expected $($row.Verdict)/$($row.Class), GOT $($actual.Verdict)/$($actual.Class)"
        }
        else {
            Write-Host "     ✓ $($tagLabel.PadRight(20)) $($actual.Verdict.PadRight(8)) ($($actual.Class)) — $($row.Why)" -ForegroundColor DarkGray
        }
    }

    # Non-vacuity: a table that ran zero rows is a self-test that proved nothing.
    if ($rows.Count -ne 8) {
        Write-Fail "the self-test table holds $($rows.Count) rows, expected 8 — rows are the proof; losing one silently shrinks what this lane claims (8.2 design decision 4 enumerates all eight)."
        return $false
    }
    if ($failures.Count -ne 0) {
        Write-Host ""
        Write-Fail "$($failures.Count) of $($rows.Count) self-test rows FAILED:"
        $failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        return $false
    }

    Write-OK "classifier self-test: $($rows.Count)/$($rows.Count) rows green (2 PUBLISH/SKIP arms, 5 RED arms, 1 mismatch arm)"
    return $true
}

<#
.SYNOPSIS
    -Tag: classify the Release's tag, assert the claim, emit the verdict.
#>
function Invoke-Classify {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$ReleaseTag)

    $propsVersion = Get-PropsVersion
    $result = Get-ReleaseVerdict -Tag $ReleaseTag -PropsVersion $propsVersion

    Write-Host ""
    Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host "  release-preflight — tag classification" -ForegroundColor White
    Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "     tag:   $ReleaseTag" -ForegroundColor Gray
    Write-Host "     props: $propsVersion  (src/Directory.Build.props — the version truth)" -ForegroundColor Gray
    Write-Host "     class: $($result.Class)" -ForegroundColor Gray
    Write-Host ""

    switch ($result.Verdict) {
        "PUBLISH" {
            Write-OK "PUBLISH — $($result.Reason)"
            Write-Verdict -Verdict "publish"
            Write-Summary @(
                "### Package release — proceeding to validate",
                "",
                "| | |",
                "|---|---|",
                "| tag | ``$ReleaseTag`` |",
                "| props version | ``$propsVersion`` |",
                "| verdict | **publish** |",
                "",
                "The tag's claim matches ``src/Directory.Build.props``. Nothing is pushed until every check in ``validate`` is green."
            )
            return 0
        }
        "SKIP" {
            # SKIP-AND-SAY-SO, NOT RED (8.2 decision 1, the deliberate call). A
            # milestone announcement Release is a LEGITIMATE thing the owner may
            # do at M8's close — reddening a legitimate action trains the owner
            # to ignore reds on release runs, which is the last place that habit
            # should exist. The loudness that matters lives on the UNRECOGNIZED
            # arm; that one IS red.
            Write-Host "  ────────────────────────────────────────────────────────────────" -ForegroundColor Yellow
            Write-Host "   MILESTONE RELEASE — NOTHING WAS PUBLISHED" -ForegroundColor Yellow
            Write-Host "   '$ReleaseTag' is a milestone tag (v<N>.<M>). Milestone Releases" -ForegroundColor Yellow
            Write-Host "   announce; they never publish. Package releases use 'pkg/<semver>'" -ForegroundColor Yellow
            Write-Host "   — e.g. 'pkg/$propsVersion'. This is by design, not a failure." -ForegroundColor Yellow
            Write-Host "  ────────────────────────────────────────────────────────────────" -ForegroundColor Yellow
            Write-Verdict -Verdict "skip"
            Write-Summary @(
                "### ⚠ Milestone Release — nothing was published",
                "",
                "``$ReleaseTag`` is a **milestone** tag (``v<N>.<M>``). Milestone Releases announce a milestone; they **never publish packages**. This run pushed nothing, by design.",
                "",
                "Package releases use the **``pkg/<semver>``** namespace — for the current tree that is ``pkg/$propsVersion``. See ``docs/GITHUB-SETUP.md`` → *Publishing a release (the manual go)*.",
                "",
                "Exit 0: this is not a failure."
            )
            return 0
        }
        default {
            Write-Fail "RED — $($result.Reason)"
            Write-Summary @(
                "### ❌ Release RED — nothing was published",
                "",
                "| | |",
                "|---|---|",
                "| tag | ``$ReleaseTag`` |",
                "| class | ``$($result.Class)`` |",
                "| props version | ``$propsVersion`` |",
                "| commit | ``$($env:GITHUB_SHA)`` |",
                "",
                $result.Reason
            )
            return 1
        }
    }
}

<#
.SYNOPSIS
    -Preflight: the nuget.org state check — arms first, then the shipped six.
#>
function Invoke-Preflight {
    $propsVersion = Get-PropsVersion
    # @() at the CALL SITE, deliberately: PowerShell unwraps a single-element
    # array on `return`, so a src/ holding exactly ONE csproj would hand back a
    # bare string and the `.Count` below would throw under StrictMode instead of
    # scanning. Found by mutation (pointing the enumeration at a single control
    # id), not by reasoning. Same family as the smoke's Get-TypeNames wrapping
    # bug: let the pipeline ENUMERATE, and collect with @() where it lands.
    $ids = @(Get-ShippedPackageIds)

    Write-Host ""
    Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host "  release-preflight — nuget.org state (the one thing no local step can know)" -ForegroundColor White
    Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host ""
    Write-OK "version source: src/Directory.Build.props → $propsVersion"
    Write-OK "shipped set: $($ids.Count) ids ENUMERATED from src/ (never rostered — 8.1 normative rule 2): $($ids -join ', ')"
    Write-Host ""

    if (-not (Invoke-ControlArms)) {
        Write-Fail "the nuget.org scanner FAILED its positive control — the six were NOT consulted. A blind scanner reporting 'all clear' is the exact vacuity this control exists to prevent (8.2 decision 4; 8.1's I-3 recurring)."
        return 1
    }

    Write-Host ""
    Write-Step "querying the shipped six at $propsVersion ..."
    $taken = @()
    foreach ($id in $ids) {
        $state = Test-NugetState -Id $id
        if (Test-VersionPublished -State $state -Version $propsVersion) {
            $taken += $id
            Write-Fail "     $id $propsVersion — ALREADY PUBLISHED"
        }
        elseif ($state.Registered) {
            Write-Host "     $id — registered ($($state.Versions.Count) versions), $propsVersion is free" -ForegroundColor DarkGray
        }
        else {
            Write-Host "     $id — unregistered (404): any version is pushable" -ForegroundColor DarkGray
        }
    }

    if ($taken.Count -ne 0) {
        Write-Host ""
        Write-Fail "ALREADY PUBLISHED on nuget.org at $propsVersion`: $($taken -join ', ')"
        Write-Fail "nuget.org versions are IMMUTABLE — a published version can only be UNLISTED, never replaced or re-pushed. Bump <Version> in src/Directory.Build.props, merge, and tag the merge commit 'pkg/<new version>'."
        $takenMarkdown = ($taken | ForEach-Object { '`' + $_ + '`' }) -join ', '
        Write-Summary @(
            "### ❌ Version already published — nothing was pushed",
            "",
            "``$propsVersion`` is **already on nuget.org** for: $takenMarkdown",
            "",
            "nuget.org versions are **immutable** — a published version can only be *unlisted*, never replaced. Bump ``<Version>`` in ``src/Directory.Build.props``, merge, and tag the merge commit ``pkg/<new version>``.",
            "",
            "This is the failure ``--skip-duplicate`` would otherwise mask by pushing nothing and going green (8.2 decision 5 — the two are designed as a pair)."
        )
        return 1
    }

    Write-Host ""
    Write-OK "nuget.org preflight CLEAR: $propsVersion is free for all $($ids.Count) ids (and the scanner proved it can see — the arms above)"
    return 0
}

# ── Output plumbing (no-ops off CI) ──────────────────────────────────────────

function Write-Verdict([string]$Verdict) {
    if ($env:GITHUB_OUTPUT) { "verdict=$Verdict" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8 }
    Write-Host "     verdict=$Verdict" -ForegroundColor DarkGray
}

function Write-Summary([string[]]$Lines) {
    if ($env:GITHUB_STEP_SUMMARY) { $Lines -join "`n" | Out-File $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8 }
}

# ── Dispatch ─────────────────────────────────────────────────────────────────

$switchCount = @($SelfTest, $Preflight, ($PSBoundParameters.ContainsKey('Tag'))) |
    Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($switchCount -ne 1) {
    Write-Fail "give exactly ONE of -SelfTest, -Tag <tag>, -Preflight (got $switchCount). Each is a separate step in release.yml's validate job."
    exit 2
}

if ($SelfTest)   { exit ([int](-not (Invoke-SelfTest))) }
if ($Preflight)  { exit (Invoke-Preflight) }
exit (Invoke-Classify -ReleaseTag $Tag)
