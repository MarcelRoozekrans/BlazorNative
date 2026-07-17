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

      -SelfTest    The 9-row classifier table (8.6 design decision 4), NEGATIVE
                   ROWS INCLUDED. This is what makes the tag↔props assertion
                   provable WITHOUT a Release: on a PR there is no tag, so a
                   lane that "tested" the assertion against a synthetic tag
                   derived from the props would compare the props TO ITSELF —
                   green, vacuous, and exactly 8.1's headline sin. The table
                   supplies real negative inputs instead (`v9.9.9` vs props
                   `1.0.0-preview.2` is a REAL mismatch, and its row expects
                   RED).

      -Tag <t>     Classify + assert, then emit the verdict for release.yml's
                   push-job guard. ONE namespace publishes (8.6 decision 4):
                     v<semver>      -> package         -> assert tag == props
                     v<N>.<M>       -> legacy-milestone-> RED
                     pkg/<semver>   -> legacy-package  -> RED
                     v<not-semver>  -> malformed       -> RED
                     anything else  -> unrecognized    -> RED
                   THE PROPS WINS AND THE TAG IS A CLAIM (8.2 decision 2,
                   unchanged and RE-POINTED): this script VERIFIES `v<X>`
                   against src/Directory.Build.props and NEVER overrides it. Its
                   old subject was "the owner forgot to bump the props"; its new
                   subject is "release-please's config is wrong". No
                   `-p:Version=` exists anywhere in the release path — pinned by
                   ReleaseWorkflowPinTests, because it is the exact drift a
                   contributor imports by copying the reference implementation.

      -Preflight   The nuget.org state check — the ONE thing a real push can
                   reject that no local step can know (8.2 decision 4). Two
                   POSITIVE-CONTROL ARMS run BEFORE the six are consulted; see
                   the vacuity note on Test-NugetState below. It is the reason
                   `--skip-duplicate` is safe in release.yml: this reds on a
                   forgotten props bump BEFORE the push job starts.

    THE VERDICT VOCABULARY IS `publish` AND RED. THERE IS NO `skip` (8.6
    decision 4). 8.2's milestone arm skipped-and-said-so on ONE premise, stated
    in its own words: "a milestone Release is a LEGITIMATE thing the owner may
    do at M8's close — reddening a legitimate action trains the owner to ignore
    reds on release runs." PHASE 8.6 DELETES THAT PREMISE: the milestone-tag
    namespace is RETIRED, v1.0…v7.0 are TO BE DELETED (decided and authorized;
    as of 2026-07-17 all seven still exist — the deletion is the owner's step and
    the last of 8.6's close), no v<N>.<M> will ever be cut again, and `v8.0` is
    CANCELLED (M8 DoD #6's tag was a ritual, not a result — see
    docs/plans/2026-07-17-milestone-8-audit-addendum.md). A Release on `v8.0` is
    no longer a legitimate action; it is a MISTAKE. The loudness argument inverts
    with the premise: a green "milestone Release — nothing published" would tell
    an owner who just did something meaningless that all is well.

    THE RED DOES NOT WAIT FOR THE DELETION, and the distinction is why this arm
    is sound today: the namespace was retired by DECISION, and this arm reds on
    the tag's SHAPE. RETIRING A NAMESPACE AND EMPTYING IT ARE TWO DIFFERENT ACTS
    and only the first has to have happened. `v8.0` reds with all seven tags live
    exactly as it will once they are gone — so nothing here is load-bearing on a
    step that has not been taken.

    AND RED IS NOT THE UNSAFE DIRECTION HERE, which is the part worth checking
    rather than trusting. 8.2's headline hazard was `v8.0` -> six packages at
    version "8.0", permanently (nuget.org has no hard delete). Count the guards
    between `v8.0` and that outcome:
      1. the legacy-milestone arm (SHAPE)            -> RED
      2. SemVer validation of the `v` payload        -> "8.0" is not SemVer -> RED
      3. the tag↔props assertion (CONTENT)           -> "8.0" != props -> RED
      4. STRUCTURAL — the tag NEVER feeds the pack; what ships is whatever the
         props spells (pinned by ReleaseWorkflowPinTests
         .TheReleaseWorkflow_NeverOverridesTheVersion)
    8.2 had two (shape + content). This has FOUR, and the fourth is structural.
    Converting arm 1 from SKIP to RED does not remove a guard — it makes the
    first one louder. The hazard's defence did not weaken.

    ALL THREE OF THAT COUNT'S CLAIMS WERE MUTATION-PROVEN AT 8.6 GATE 1, and the
    third is the one worth having: M3′ neutered guard 2 AND deleted guard 1, and
    `v8.0` STILL came back RED — from guard 3, alone, with class `package`. The
    last line of defence for 8.2's headline hazard holds after the two in front
    of it are both gone.

    THE NAMESPACES STAY DISJOINT, BY A DIFFERENT MECHANISM THAN 8.2's. 8.2:
    disjoint by PREFIX (`pkg/` vs `v`). 8.6: disjoint by COMPONENT COUNT —
    `v<N>.<M>` is not valid SemVer (two components), so `v8.0` can never be a
    valid `v<semver>` tag, and `v8.0.0` can, and SHOULD publish 8.0.0 if this
    repo ever gets there. No collision exists, now or ever.

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
    .\scripts\release-preflight.ps1 -Tag v1.0.0-preview.2
    .\scripts\release-preflight.ps1 -Preflight
#>

[CmdletBinding()]
param(
    # The Release's tag (release.yml passes github.event.release.tag_name).
    [string]$Tag,

    # The nuget.org state preflight — two control arms, then the shipped six.
    [switch]$Preflight,

    # The classifier + assertion table (the 9 rows, negative rows included).
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

function Write-Step([string]$text) { Write-Host "  ⟶  $text" -ForegroundColor Gray }
function Write-OK([string]$text)   { Write-Host "  ✓  $text" -ForegroundColor Green }
function Write-Fail([string]$text) { Write-Host "  ✗  $text" -ForegroundColor Red }

# ─────────────────────────────────────────────────────────────────────────────
#  The ONE publishing namespace, and the two retired ones (8.6 design decision 4)
#
#  `v<semver>` is release-please's default tag shape (`include-v-in-tag`
#  defaults to true) and, after 8.6, the ONLY shape that publishes. The owner
#  chose it to match the reference and the ecosystem norm; it is also the
#  default, and fewer knobs turned away from default is fewer ways to be wrong.
#
#  THE HAZARD, stated where the regexes live: the `release` event carries NO tag
#  filter, so EVERY published Release fires release.yml — including one
#  published on `v8.0`. An AdoNet.Async-shaped `VERSION="${TAG#v}"` would turn
#  that into six packages pushed at version "8.0", permanently (nuget.org has no
#  hard delete). FOUR independent guards stop it now; see the file header. The
#  two that live in this section are the SHAPE (the legacy-milestone arm below)
#  and the CONTENT (the props assertion). Neither is load-bearing alone, and
#  neither is the last line — guard 4 is structural.
#
#  WHY THE RETIRED NAMESPACES GET NAMED ARMS RATHER THAN FALLING TO THE GENERIC
#  ONE — the house rule already in this script: different mistakes, different
#  sentences. A `pkg/1.0.0-preview.2` Release means someone followed
#  docs/GITHUB-SETUP.md's OLD ritual, and that reader deserves "the `pkg/`
#  namespace was retired in Phase 8.6; release-please owns `v<semver>` now" —
#  not "tag is in neither namespace", which reads like a typo report.
# ─────────────────────────────────────────────────────────────────────────────

# LEGACY MILESTONE: v<N>.<M> and nothing else — RETIRED by 8.6 (the seven tags
# v1.0…v7.0 are to be deleted — authorized, not yet taken as of 2026-07-17;
# v8.0 was cancelled, never cut). The RETIREMENT is what this arm rests on, and
# it is a decision, not a tag state — so the arm is correct whether or not the
# seven still exist. This arm exists to SAY SO, not to decide: `v8.0` is RED with
# or without it (`"8.0"` is not SemVer, so the `v<semver>` arm below would reject
# it as malformed anyway).
#
# Mutation vehicle M1′ (8.6): DELETE this arm -> `v8.0` falls through to
# `^v(.+)$` -> `"8.0"` is not SemVer -> malformed/RED -> row 3 expects
# `legacy-milestone` -> the CLASS differs -> row 3 REDS. That proves the arm is
# a DECISION and not a default, *and* that the verdict stays RED without it.
# Belt and braces, both real; the table can only see the belt.
#
# It must be consulted BEFORE the `v<semver>` arm — `^v(.+)$` would otherwise
# swallow `v8.0` and diagnose it as a typo'd version rather than as the retired
# ritual it actually is.
$LegacyMilestoneTagPattern = '^v(\d+)\.(\d+)$'

# LEGACY PACKAGE: 8.2's `pkg/` namespace — RETIRED by 8.6. Note that row 5's
# props MATCHES and the row is still RED: this arm is about the NAMESPACE, not
# the version. The payload is never even parsed.
$LegacyPackageTagPattern = '^pkg/(.+)$'

# THE ONE PUBLISHING NAMESPACE: `v<semver>`. The payload is validated as semver
# SEPARATELY (below), so a `v` tag with a bad payload diagnoses as MALFORMED
# ("you meant a package release and typo'd the version") rather than as
# UNRECOGNIZED ("you used no namespace at all").
$PackageTagPattern = '^v(.+)$'

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
    # reader inherits. An empty tag matches NO regex below, so it already falls
    # through to the `unrecognized` arm with the SAME class and the SAME
    # verdict: deleting this branch entirely leaves the self-test 9/9 GREEN
    # (run, observed — re-run at 8.6 Gate 1 against the new table). It is kept
    # because "the Release carries an EMPTY tag" names the actual problem where
    # the generic arm would say "tag '' is not in the publishing namespace" — a
    # sentence that reads like a typo report for a tag that does not exist.
    #
    # WHAT ROW 9 OF THE TABLE ACTUALLY PROVES, measured rather than assumed —
    # an empty tag is DOUBLE-COVERED, and the two mutations disagree in a way
    # worth writing down:
    #   · break THIS branch's verdict (RED -> PUBLISH) -> row 9 REDS. This
    #     branch is what executes for an empty tag, so row 9 is load-bearing
    #     on it.
    #   · delete this branch outright                  -> row 9 stays GREEN, via
    #     the fall-through. That is redundancy, not vacuity: the second arm
    #     gives the same class and verdict, only a worse sentence.
    # So the belt and the braces are both real, and the table can only see the
    # belt. Deleting this branch is a safe refactor the table permits; changing
    # what it DECIDES is not.
    if ([string]::IsNullOrWhiteSpace($Tag)) {
        return [pscustomobject]@{
            Class   = "unrecognized"
            Verdict = "RED"
            Reason  = "the Release carries an EMPTY tag. Package releases use 'v<semver>' (e.g. v$PropsVersion) and are cut by release-please, not by hand."
        }
    }

    # ── ARM 1: the retired milestone namespace (SHAPE) ───────────────────────
    # Consulted FIRST, before `^v(.+)$` below, so `v8.0` is diagnosed as the
    # retired ritual it is rather than as a typo'd version.
    #
    # CASE: `-match` is PowerShell's CASE-INSENSITIVE operator, so `V8.0`
    # matches here too. That is harmless now and was not before — under 8.2 this
    # arm returned SKIP, so its case-sensitivity decided between "announce" and
    # "red". After 8.6 both spellings are RED; only the SENTENCE differs, and
    # the case-insensitive one is the better sentence. The asymmetry with the
    # publishing arm below is therefore no longer a judgment call the reader has
    # to be warned about — it is just correct.
    if ($Tag -match $LegacyMilestoneTagPattern) {
        return [pscustomobject]@{
            Class   = "legacy-milestone"
            Verdict = "RED"
            Reason  = "tag '$Tag' is a MILESTONE tag (v<N>.<M>), and that namespace was RETIRED in Phase 8.6. No v<N>.<M> will ever be cut again: the milestone tags v1.0-v7.0 are being deleted, and 'v8.0' was cancelled — M8 DoD #6's tag was a ritual, not a result (see docs/plans/2026-07-17-milestone-8-audit-addendum.md). Under 8.2 this was an announce-and-skip; the premise it rested on ('a milestone Release is a legitimate thing the owner may do') no longer holds, so it is now a mistake and says so. Package releases are 'v<semver>' — e.g. 'v$PropsVersion' — and release-please cuts them. Nothing was published."
        }
    }

    # ── ARM 2: the retired `pkg/` namespace (NAMESPACE, not version) ─────────
    # Note that this arm never parses the payload: row 5 of the table feeds it a
    # tag whose version MATCHES the props, and it is still RED. The namespace is
    # the whole subject.
    if ($Tag -match $LegacyPackageTagPattern) {
        return [pscustomobject]@{
            Class   = "legacy-package"
            Verdict = "RED"
            Reason  = "tag '$Tag' uses the 'pkg/' namespace, which was RETIRED in Phase 8.6 — release-please owns 'v<semver>' now. This Release was cut by hand following docs/GITHUB-SETUP.md's OLD ritual; that ritual is gone. Merge release-please's release PR instead: it cuts the tag and a DRAFT Release, and publishing the draft is the go. Nothing was published."
        }
    }

    # ── ARM 3: the ONE publishing namespace ──────────────────────────────────
    # [regex]::Match is CASE-SENSITIVE (unlike `-match` above). `v` is the only
    # spelling that opens this door; `V1.0.0-preview.2` falls through to
    # unrecognized -> RED. That strictness is the right default for the arm that
    # can publish, and it is deliberate: toward publishing is the one direction
    # this phase's law does not bend.
    $pkgMatch = [regex]::Match($Tag, $PackageTagPattern)
    if ($pkgMatch.Success) {
        $claimed = $pkgMatch.Groups[1].Value

        # GUARD 2 (see the file header's four-guard count). `v8.0` reaches here
        # only if arm 1 is gone — and `"8.0"` is not SemVer, so it still cannot
        # publish. Mutation vehicle M3′ (8.6) neuters THIS check *and* deletes
        # arm 1, to prove guard 3 below is a real last line of defence.
        if ($claimed -notmatch $SemVerPattern) {
            return [pscustomobject]@{
                Class   = "malformed"
                Verdict = "RED"
                Reason  = "tag '$Tag' is in the publishing namespace but '$claimed' is not a valid SemVer 2.0.0 version. The props says '$PropsVersion' — the tag must be 'v$PropsVersion'. Nothing was published."
            }
        }

        # GUARD 3 — THE ASSERTION (8.2 decision 2, unchanged and RE-POINTED):
        # the props WINS; the tag is a CLAIM. Its old subject was "the owner
        # forgot to bump the props". Its new subject is "release-please's config
        # is wrong" — the manifest bumped and the props did not, because
        # `extra-files` missed it. The subject is better, not gone. The
        # PRE-MERGE half of that guard is
        # PackageVersionPinTests.TheManifest_AgreesWithTheProps, which reds
        # release-please's own PR in the required lane before it can merge; this
        # is the Release-time backstop for when that pin is removed or the PR is
        # force-merged.
        #
        # Mutation vehicle M2′ (8.6): neuter this comparison to always-true ->
        # the `v9.9.9` row returns PUBLISH -> its row expects RED -> the
        # self-test reds. A positive control ON the positive control.
        #
        # CASE, and this is the ONE non-obvious PUBLISH in the script, so it is
        # stated rather than left to be discovered: `-ne` is CASE-INSENSITIVE,
        # so `v1.0.0-PREVIEW.2` compares EQUAL to the props' `1.0.0-preview.2`
        # and the verdict is PUBLISH. That is benign, for two independent
        # reasons: NuGet versions ARE case-insensitive by spec, and —
        # decisively — THE TAG NEVER FEEDS THE PACK (guard 4). What ships is
        # whatever src/Directory.Build.props spells, and the release path never
        # overrides it (pinned by ReleaseWorkflowPinTests
        # .TheReleaseWorkflow_NeverOverridesTheVersion). So an odd-cased tag
        # still publishes correctly-cased packages; the mis-spelling dies here.
        if ($claimed -ne $PropsVersion) {
            return [pscustomobject]@{
                Class   = "package"
                Verdict = "RED"
                Reason  = "VERSION MISMATCH — the tag claims '$claimed', src/Directory.Build.props says '$PropsVersion'. The props is the version and the tag is a claim about it (8.2 decision 2): this flow NEVER overrides the props. release-please writes BOTH the manifest and the props in one release PR, so on main they agree at every commit — a mismatch here means the release PR's `extra-files` did not reach the props, or the tag is on the wrong commit. Nothing was published."
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
        Reason  = "tag '$Tag' is not in the publishing namespace. Package releases are 'v<semver>' (e.g. 'v$PropsVersion') and are cut by release-please's release PR, never by hand. See docs/GITHUB-SETUP.md. Nothing was published."
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
    -SelfTest: the 9-row table (8.6 design decision 4), negative rows included.
.DESCRIPTION
    THE PROPS VALUES ARE THE TABLE'S, NOT THE LIVE ONES. That is the point: a
    row that derived its tag from the live props would compare the props to
    itself and pass forever, including after someone deleted the comparison.
    Row 2 (`v9.9.9` vs `1.0.0-preview.2`) is a real mismatch with a real
    expected RED, and it is what makes the assertion provable with no Release
    in existence.

    THE TABLE'S PROPS IS `1.0.0-preview.2` AND THE LIVE PROPS IS SOMETHING ELSE
    — deliberately, and it is worth one sentence. `1.0.0-preview.2` is the
    version release-please proposes NEXT, so it is a value the live file does
    not hold; a row that happened to agree with the live props would be a row
    that could pass by reading the wrong file.

    8 ROWS BECAME 9 (8.6 decision 4), and the count assertion moves with the
    decision: rows are the proof. 1 PUBLISH arm, 8 RED arms, 0 SKIP arms —
    `skip` no longer exists in the vocabulary. Row 3 is the phase in one line.
#>
function Invoke-SelfTest {
    Write-Host ""
    Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host "  release-preflight — classifier self-test (9 rows, negative rows included)" -ForegroundColor White
    Write-Host "  ──────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host ""

    $rows = @(
        @{ Tag = "v1.0.0-preview.2";    Props = "1.0.0-preview.2"; Class = "package";          Verdict = "PUBLISH"; Why = "the happy path — release-please's own tag shape, matching the props" }
        @{ Tag = "v9.9.9";              Props = "1.0.0-preview.2"; Class = "package";          Verdict = "RED";     Why = "a REAL mismatch (not props-vs-props) — the assertion must BITE; its new subject is release-please's config" }
        @{ Tag = "v8.0";                Props = "1.0.0-preview.2"; Class = "legacy-milestone"; Verdict = "RED";     Why = "THE ARM 8.6 INVERTS — was SKIP in 8.2; v8.0 is cancelled, not pending" }
        @{ Tag = "v1.0";                Props = "1.0.0-preview.2"; Class = "legacy-milestone"; Verdict = "RED";     Why = "a retired tag's shape — the arm reds on the SHAPE, so this row is true whether or not v1.0 still exists (it does, as of 2026-07-17)" }
        @{ Tag = "pkg/1.0.0-preview.2"; Props = "1.0.0-preview.2"; Class = "legacy-package";   Verdict = "RED";     Why = "8.2's namespace, retired — note the props MATCHES and it is still RED" }
        @{ Tag = "1.0.0-preview.2";     Props = "1.0.0-preview.2"; Class = "unrecognized";     Verdict = "RED";     Why = "bare semver, no 'v' — the props matches, still RED" }
        @{ Tag = "release/1.0.0";       Props = "1.0.0-preview.2"; Class = "unrecognized";     Verdict = "RED";     Why = "rejected namespace (b) — 'release/' is spent on BRANCHES by the owner's GitVersion.yml" }
        @{ Tag = "vnot-a-version";      Props = "1.0.0-preview.2"; Class = "malformed";        Verdict = "RED";     Why = "the 'v' namespace with a payload that is not semver" }
        @{ Tag = "";                    Props = "1.0.0-preview.2"; Class = "unrecognized";     Verdict = "RED";     Why = "no tag at all" }
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
    if ($rows.Count -ne 9) {
        Write-Fail "the self-test table holds $($rows.Count) rows, expected 9 — rows are the proof; losing one silently shrinks what this lane claims (8.6 design decision 4 enumerates all nine). The count moved 8 -> 9 with the decision that added row 3's inversion; it is not a number to relax."
        return $false
    }
    if ($failures.Count -ne 0) {
        Write-Host ""
        Write-Fail "$($failures.Count) of $($rows.Count) self-test rows FAILED:"
        $failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        return $false
    }

    Write-OK "classifier self-test: $($rows.Count)/$($rows.Count) rows green (1 PUBLISH arm, 8 RED arms, 0 SKIP arms)"
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
        # THERE IS NO `SKIP` ARM (8.6 decision 4). 8.2 had one, and it rested on
        # a single premise — "a milestone Release is a LEGITIMATE thing the
        # owner may do at M8's close" — which this phase deleted along with the
        # milestone-tag namespace. Every non-publishing tag now falls to
        # `default` below and REDS. The verdict vocabulary is `publish` and RED.
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
        # ONE SENTENCE, TWO CONSUMERS — and the reason is this sentence's own
        # history (8.6 Gate 1 review, S1-2 + Q2-2). The console copy and the
        # step-summary copy said the same thing in two dialects, so they went
        # STALE TOGETHER: both survived 8.6's sweep of ~20 other `pkg/` lines in
        # this very file and kept instructing the reader to "bump <Version> ...
        # and tag the merge commit 'pkg/<new version>'" — a hand-bump that
        # normative rule 2 abolished, in a namespace THIS FILE REDS AT ARM 2.
        # A RED that instructs the ritual it reds is worse than silence.
        # Two copies of a sentence are two chances to be wrong and one chance in
        # two of fixing it; it is now ONE string, deliberately dialect-neutral
        # (no markdown emphasis) so the console and the summary can both read it
        # verbatim. The next drift here is a one-line fix.
        $remedy = "nuget.org versions are IMMUTABLE — a published version can only be UNLISTED, never replaced or re-pushed. Nobody bumps a version by hand any more (8.6 normative rule 2), and the 'pkg/' namespace is retired — this script reds it. THE RITUAL: land a conventional commit on main; release-please opens a release PR that writes the next version for you; APPROVE ITS WORKFLOWS so its checks start (they are held approval-required because release-please opens the PR with GITHUB_TOKEN); merge it, which cuts the 'v<semver>' tag and a DRAFT Release; then publish the draft. That click is the go. See docs/GITHUB-SETUP.md."

        Write-Host ""
        Write-Fail "ALREADY PUBLISHED on nuget.org at $propsVersion`: $($taken -join ', ')"
        Write-Fail $remedy
        $takenMarkdown = ($taken | ForEach-Object { '`' + $_ + '`' }) -join ', '
        Write-Summary @(
            "### ❌ Version already published — nothing was pushed",
            "",
            "``$propsVersion`` is **already on nuget.org** for: $takenMarkdown",
            "",
            $remedy,
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
