#Requires -Version 7.0
<#
.SYNOPSIS
    THE FOOTER CHECK — a commit BODY can declare a breaking change, or pick the
    released version, by accident. This reds when the body says something the PR
    title does not.

.DESCRIPTION
    ─────────────────────────────────────────────────────────────────────────────
    WHY THIS EXISTS — it already happened, on this branch, to the commit that
    installed the commit contract (Phase 8.6, Gate 2).

    004179f's WHY body is hard-wrapped at 80 columns. The wrap pushed a
    breaking-change token onto a new line, and the next word was "footers.":

        is where this repo's prose goes and where release-please reads Release-As: and
        BREAKING CHANGE: footers.

    A real `npx release-please release-pr --dry-run` duly proposed:

        ### ⚠ BREAKING CHANGES
        * **release:** footers.

    THE LINE WRAP DECLARED A BREAKING CHANGE. One word earlier in the paragraph
    and nothing would have happened. There is no breaking change on that branch.

    AND THE WORSE SHAPE, which is the reason this check exists at all: the same
    accident with a RELEASE-AS token does not cost a changelog section — IT PICKS
    THE RELEASED VERSION. The strategy scans for that note FIRST, before any
    breaking or feature counting, and short-circuits the whole prerelease ladder
    (8.6 decision 2, rung 3). A body documenting the ladder by spelling the token
    releases that version. Nothing warns.

    WHY NOTHING CAUGHT IT. 8.6 decision 5 lints the PR TITLE, on the stated
    premise that a squash DISCARDS the branch's commits. THAT IS TRUE OF SUBJECTS
    AND FALSE OF BODIES: `squash_merge_commit_message` is COMMIT_MESSAGES, so
    every branch commit's BODY concatenates into main's body, and release-please
    parses THAT. The one surface that carries footers was the one surface nothing
    read before it landed.

    ─────────────────────────────────────────────────────────────────────────────
    THE RULE, and it is machine-decidable — which "did you mean it?" is not.

        A PR's commit BODIES may declare a release footer only if the PR's TITLE
        declares the same thing.

          · a BREAKING-CHANGE token  -> the title must carry conventional
                                        commits' `!` breaking marker.
          · a RELEASE-AS token       -> the title must contain the exact version
                                        the token names.

    WHAT IT ASSERTS, stated honestly, because a check that overclaims is worse
    than no check: NOT that the author meant the footer. IT CANNOT READ MINDS AND
    IT DOES NOT TRY. It asserts that THE SUBJECT AND THE BODY AGREE ABOUT WHAT
    THIS PR DOES. Those are two independent statements of intent, both written by
    the same author, and this compares them. On disagreement it reds and makes a
    human reconcile the two texts they wrote. Exactly one of them is wrong; the
    check does not need to know which, and it does not guess.

    That is why there is NO label, NO allow-list and NO "confirm you meant it"
    escape hatch. A label would be a human attesting to their own accident — the
    author who wrapped 004179f's body would have labelled it without hesitating,
    because they did not know it had happened. THE TITLE IS EVIDENCE THE AUTHOR
    PRODUCED FOR ANOTHER REASON, and that is what makes it worth comparing.

    ─────────────────────────────────────────────────────────────────────────────
    ⚠ "COLUMN 0" IS THE WRONG BOUNDARY, AND ASSUMING IT WOULD HAVE SHIPPED A
    CHECK WITH FOUR HOLES. Measured against conventional-commits-parser 6.4.0
    with release-please's own parser options — run, not reasoned:

        BREAKING CHANGE: x.         -> NOTE      (the known case)
        ` `BREAKING CHANGE: x.      -> NOTE      leading spaces DO NOT save it
        <tab>BREAKING CHANGE: x.    -> NOTE      nor does a tab
        `        `BREAKING CHANGE:  -> NOTE      nor does 8 of them
        * BREAKING CHANGE: x.       -> NOTE      A LIST BULLET DOES NOT SAVE IT
        breaking change: x.         -> NOTE      IT IS CASE-INSENSITIVE
        BREAKING CHANGE x.          -> NOTE      THE COLON IS NOT REQUIRED

    Any one of those four would have been a hole in a column-0 check, and the
    case-insensitivity is the sharpest: a body that starts a line with the words
    "breaking change:" in ordinary lowercase prose DECLARES ONE.

    THE ESCAPE HATCHES ARE MEASURED TOO — this is the "name the thing, do not
    spell it" rule (445ba67, 61cfd7a) with the receipts, and it is what the RED
    message tells you to reach for:

        `BREAKING CHANGE:` x.       -> (none)    backticks work
        > BREAKING CHANGE: x.       -> (none)    a quote works
        # BREAKING CHANGE: x.       -> (none)    a hash works
        · BREAKING CHANGE: x.       -> (none)    this repo's own bullet works
        foo BREAKING CHANGE: x.     -> (none)    any word before it works

    THE FIX FOR AN ACCIDENT IS TO RE-WRAP OR QUOTE THE LINE — never to add `!` to
    the title. Adding `!` to silence this makes the changelog say the thing the
    wrap said by accident, which is the wrong fix in the house's most persuasive
    voice (the failure 1814fe1 caught in a different pin's message).

    ─────────────────────────────────────────────────────────────────────────────
    WHY BODIES AND NOT THE SQUASH BODY ITSELF: the check scans every commit's
    body. That is EQUIVALENT to scanning the concatenation a squash produces, and
    the reason is structural rather than lucky — concatenation joins bodies with
    newlines, so it PRESERVES every body line's leading content and can never
    create a footer line that was not already one in some commit. Scanning the
    parts is therefore not an approximation of scanning the whole; it is the same
    assertion, available BEFORE the merge that would produce the whole.

    The SUBJECT of each branch commit is deliberately NOT scanned: a squash
    renders it as a `* subject` bullet, and `.commitlintrc.yml`'s type-enum
    already reds a subject that starts with a note keyword.

    ⚠ ADVISORY, and the honest consequence is NOT the same as decision 5's.
    This runs in the `commitlint` lane, which is advisory by design — so a
    poisoned body CAN merge. Decision 5 argued advisory was safe because "the
    blast radius of a mis-typed subject is a changelog section, never a wrong
    version". THAT ARGUMENT IS ABOUT SUBJECTS AND DOES NOT EXTEND HERE: a
    RELEASE-AS token in a body sets the version outright. The mitigation is that
    it fails LOUD and PRE-MERGE (a red check on the PR, before the release PR
    exists) and that the release PR is itself read before merging. Promoting this
    lane to required is the owner's call and a branch-protection change; it is
    ledgered, not taken. See docs/plans/2026-07-17-phase-8.6-design.md.

.PARAMETER Title
    The PR title. Under `squash_merge_commit_title: PR_TITLE` this IS the subject
    that lands on main and the only text release-please reads as a subject.

.PARAMETER CommitMessagesJson
    Path to a JSON file holding an array of full commit messages (subject + body),
    e.g. `gh api .../pulls/N/commits --jq '[.[].commit.message]'`.

.PARAMETER SelfTest
    Run the table. Every row is a decision this script makes; the table is the
    proof, and its row count is asserted so a silently-deleted row reds.
#>
[CmdletBinding(DefaultParameterSetName = 'Check')]
param(
    [Parameter(ParameterSetName = 'Check', Mandatory = $true)]
    [AllowEmptyString()]
    [string] $Title,

    [Parameter(ParameterSetName = 'Check', Mandatory = $true)]
    [string] $CommitMessagesJson,

    [Parameter(ParameterSetName = 'SelfTest', Mandatory = $true)]
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─────────────────────────────────────────────────────────────────────────────
# THE PATTERNS. Every element is MEASURED (see the docstring's transcripts), not
# copied from the conventional-commits spec — the spec and the parser disagree,
# and THE PARSER IS WHAT RUNS.
#
#   ^[ \t]*        leading whitespace does not save it        (measured)
#   (?:[*+\-][ \t]+)?   a list bullet does not save it        (measured)
#   BREAKING[ -]CHANGE  both spellings are note keywords
#   RELEASE[ -]AS       the version-picking one
#   \b             so `BREAKING CHANGEs` (a plural in prose) is not a token
#
# Matched with -imatch: THE PARSER IS CASE-INSENSITIVE (measured), so this must
# be too, or the check is blind to the lowercase-prose shape.
# ─────────────────────────────────────────────────────────────────────────────
$BreakingTokenPattern = '^[ \t]*(?:[*+\-][ \t]+)?(BREAKING[ -]CHANGE)\b'
$ReleaseAsTokenPattern = '^[ \t]*(?:[*+\-][ \t]+)?(RELEASE[ -]AS)\b'

# The conventional-commits `!` breaking marker, before the `:` and after an
# optional (scope). `feat!:` and `feat(core)!:` both declare breaking.
$TitleBreakingPattern = '^[A-Za-z]+(\([^)]*\))?!:'

function Get-CommitBodyLines {
    <#
        The BODY is everything after the subject line. The subject is line 1; a
        squash renders it as a `* subject` bullet, and commitlint already reds a
        subject that starts with a note keyword.
    #>
    param([string] $Message)

    $normalized = $Message -replace "`r`n", "`n" -replace "`r", "`n"
    $lines = $normalized -split "`n"
    if ($lines.Count -le 1) { return @() }
    return $lines[1..($lines.Count - 1)]
}

function Get-FooterFindings {
    <#
        Returns every footer token in the PR's commit bodies, with the commit it
        came from and the line as written — the RED has to QUOTE the line, or the
        author cannot find the wrap that caused it.
    #>
    param([string[]] $Messages)

    $findings = @()
    for ($i = 0; $i -lt $Messages.Count; $i++) {
        $lines = @(Get-CommitBodyLines -Message $Messages[$i])
        for ($n = 0; $n -lt $lines.Count; $n++) {
            $line = $lines[$n]

            if ($line -imatch $BreakingTokenPattern) {
                $findings += [pscustomobject]@{
                    Kind       = 'breaking'
                    CommitIdx  = $i
                    LineNo     = $n + 2   # +1 for 0-based, +1 for the subject line
                    Line       = $line
                    Value      = $null
                    PrevLine   = if ($n -gt 0) { $lines[$n - 1] } else { '' }
                }
            }
            elseif ($line -imatch $ReleaseAsTokenPattern) {
                # The value is the rest of the line after the keyword and its
                # optional colon. release-please hands this to Version.parse.
                $value = ($line -ireplace '^[ \t]*(?:[*+\-][ \t]+)?RELEASE[ -]AS[ \t]*:?[ \t]*', '').Trim()
                $findings += [pscustomobject]@{
                    Kind       = 'release-as'
                    CommitIdx  = $i
                    LineNo     = $n + 2
                    Line       = $line
                    Value      = $value
                    PrevLine   = if ($n -gt 0) { $lines[$n - 1] } else { '' }
                }
            }
        }
    }
    return $findings
}

function Test-FooterAgreement {
    <#
        THE DECISION. Returns an object with Verdict ('pass'/'RED') and a Reason
        that names the line, the commit and the fix.

        This is the one function the self-test table exercises. Everything else
        in this file is plumbing.
    #>
    param(
        [AllowEmptyString()][string] $Title,
        [string[]] $Messages
    )

    $findings = @(Get-FooterFindings -Messages $Messages)
    if ($findings.Count -eq 0) {
        return [pscustomobject]@{ Verdict = 'pass'; Class = 'no-footers'; Reason = 'no footer token in any commit body' }
    }

    $titleDeclaresBreaking = $Title -match $TitleBreakingPattern

    $problems = @()
    foreach ($f in $findings) {
        if ($f.Kind -eq 'breaking') {
            if (-not $titleDeclaresBreaking) {
                $problems += [pscustomobject]@{
                    Class  = 'undeclared-breaking'
                    Detail = "commit #$($f.CommitIdx + 1), body line $($f.LineNo): '$($f.Line.Trim())' is a BREAKING-CHANGE footer, but the PR title does not carry the '!' breaking marker."
                    Finding = $f
                }
            }
        }
        else {
            # RELEASE-AS: the title must contain the exact version the token names.
            # A bare token with no value can never be deliberate — there is no
            # version for a title to agree with.
            if ([string]::IsNullOrWhiteSpace($f.Value)) {
                $problems += [pscustomobject]@{
                    Class  = 'undeclared-release-as'
                    Detail = "commit #$($f.CommitIdx + 1), body line $($f.LineNo): '$($f.Line.Trim())' is a RELEASE-AS footer naming no version."
                    Finding = $f
                }
            }
            elseif ($Title -notlike "*$($f.Value)*") {
                $problems += [pscustomobject]@{
                    Class  = 'undeclared-release-as'
                    Detail = "commit #$($f.CommitIdx + 1), body line $($f.LineNo): '$($f.Line.Trim())' is a RELEASE-AS footer — IT PICKS THE RELEASED VERSION ('$($f.Value)') — but the PR title does not name that version."
                    Finding = $f
                }
            }
        }
    }

    if ($problems.Count -eq 0) {
        return [pscustomobject]@{
            Verdict = 'pass'
            Class   = 'declared'
            Reason  = "$($findings.Count) footer token(s), each declared by the title"
        }
    }

    return [pscustomobject]@{
        Verdict = 'RED'
        Class   = $problems[0].Class
        Reason  = ($problems | ForEach-Object { $_.Detail }) -join ' | '
    }
}

# ─────────────────────────────────────────────────────────────────────────────
#  THE SELF-TEST TABLE — the rows ARE the proof (the house rule: every decision
#  lives in a script with a table, because YAML is the least testable code in
#  the repo).
#
#  Row 1 is 004179f's REAL body and REAL title, reconstructed byte-for-byte from
#  the wrap that shipped. It is the row this whole file exists for.
# ─────────────────────────────────────────────────────────────────────────────
function Invoke-SelfTest {
    # 004179f, verbatim: the wrap that declared a breaking change that does not exist.
    $poisoned004179f = @"
feat(release): the PR title is the commit subject — and one repo setting decides whether that is true

It is named in .commitlintrc.yml and again in commitlint.yml — in the files it
undermines, not only in a design doc, because that is where someone debugging "how
did ``wip`` reach main?" will be standing. squash_merge_commit_message stays
COMMIT_MESSAGES: the branch commits' WHY bodies concatenate into main's body, which
is where this repo's prose goes and where release-please reads Release-As: and
BREAKING CHANGE: footers.

ADVISORY, not a fourth required gate (8.2's own precedent for release.yml).
"@

    $rows = @(
        @{
            Name    = "004179f's real wrap"
            Title   = 'feat(release): the PR title is the commit subject — and one repo setting decides whether that is true'
            Msgs    = @($poisoned004179f)
            Verdict = 'RED'; Class = 'undeclared-breaking'
            Why     = 'THE ROW THIS FILE EXISTS FOR — the 80-col wrap that shipped, caught'
        },
        @{
            Name    = 'deliberate breaking, title has !'
            Title   = 'feat(core)!: drop the legacy registration API'
            Msgs    = @("feat(core)!: drop the legacy registration API`n`nThe old entry point is gone.`n`nBREAKING CHANGE: IBlazorNativeHost.Register is removed; use AddBlazorNative.")
            Verdict = 'pass'; Class = 'declared'
            Why     = 'the subject and the body AGREE — this is what deliberate looks like'
        },
        @{
            Name    = 'ordinary prose'
            Title   = 'fix(runtime): the guard was set before the register call'
            Msgs    = @("fix(runtime): the guard was set before the register call`n`nThe flag was written first, so the second caller saw it set and skipped.")
            Verdict = 'pass'; Class = 'no-footers'
            Why     = 'the ordinary case must not red, or the check trains people to ignore it'
        },
        @{
            Name    = 'prose naming tokens mid-line'
            Title   = 'docs(release): where release-please reads its footers'
            Msgs    = @("docs(release): where release-please reads its footers`n`nrelease-please reads Release-As: and BREAKING CHANGE: footers from the body.")
            Verdict = 'pass'; Class = 'no-footers'
            Why     = '004179f UNWRAPPED — same words, one line, no token at line start. MEASURED: a word before it means no note.'
        },
        @{
            Name    = 'backticked token at line start'
            Title   = 'docs(release): name the thing, do not spell it'
            Msgs    = @("docs(release): name the thing, do not spell it`n`nThe token is:`n``BREAKING CHANGE:`` and it is read from the body.")
            Verdict = 'pass'; Class = 'no-footers'
            Why     = 'the escape hatch the RED tells you to use — MEASURED to produce no note'
        },
        @{
            Name    = 'quoted token at line start'
            Title   = 'docs(release): the footer hazard'
            Msgs    = @("docs(release): the footer hazard`n`nThe dry-run said:`n> BREAKING CHANGE: footers.")
            Verdict = 'pass'; Class = 'no-footers'
            Why     = 'quoting a transcript is how you show the hazard without causing it'
        },
        @{
            Name    = 'INDENTED token (4 spaces)'
            Title   = 'docs(release): the dry-run transcript'
            Msgs    = @("docs(release): the dry-run transcript`n`nIt proposed:`n`n    BREAKING CHANGE: footers.")
            Verdict = 'RED'; Class = 'undeclared-breaking'
            Why     = 'THE COLUMN-0 TRAP — indenting a transcript does NOT save it (measured). A column-0 check would have passed this.'
        },
        @{
            Name    = 'BULLETED token'
            Title   = 'docs(release): what the parser reads'
            Msgs    = @("docs(release): what the parser reads`n`nThe tokens are:`n`n* BREAKING CHANGE: ends up in the changelog")
            Verdict = 'RED'; Class = 'undeclared-breaking'
            Why     = 'a list bullet does NOT save it (measured) — and `* ` is the shape a squash writes'
        },
        @{
            Name    = 'lowercase prose token'
            Title   = 'docs(release): the two hazards'
            Msgs    = @("docs(release): the two hazards`n`nThe first one:`n`nbreaking change: this reads as prose and is not.")
            Verdict = 'RED'; Class = 'undeclared-breaking'
            Why     = 'THE PARSER IS CASE-INSENSITIVE (measured) — lowercase prose declares one'
        },
        @{
            Name    = 'token without a colon'
            Title   = 'docs(release): the token'
            Msgs    = @("docs(release): the token`n`nThe words:`n`nBREAKING CHANGE is what the parser looks for.")
            Verdict = 'RED'; Class = 'undeclared-breaking'
            Why     = 'the colon is NOT required (measured) — the spec says otherwise; the parser is what runs'
        },
        @{
            Name    = 'RELEASE-AS, title silent'
            Title   = 'docs(release): documenting rung 3 of the ladder'
            Msgs    = @("docs(release): documenting rung 3 of the ladder`n`nGraduation is a commit whose body carries`nRelease-As: 1.0.0")
            Verdict = 'RED'; Class = 'undeclared-release-as'
            Why     = 'THE VERSION HAZARD — a body documenting rung 3 by spelling it RELEASES that version'
        },
        @{
            Name    = 'RELEASE-AS, title names it (rung 3)'
            Title   = 'chore(release): graduate to 1.0.0'
            Msgs    = @("chore(release): graduate to 1.0.0`n`nThe preview line ends here.`n`nRelease-As: 1.0.0")
            Verdict = 'pass'; Class = 'declared'
            Why     = 'RUNG 3 MUST STAY EXPRESSIBLE — the ladder needs it; the title is how it is declared'
        },
        @{
            Name    = 'token in the 2nd commit of a PR'
            Title   = 'feat(docs): the guides'
            Msgs    = @(
                "feat(docs): the guides`n`nA clean body.",
                "docs(docs): a fixup`n`nprose that wraps onto`nBREAKING CHANGE: the next line."
            )
            Verdict = 'RED'; Class = 'undeclared-breaking'
            Why     = 'THE CONCATENATION — every commit body lands in main''s body; scanning the parts IS scanning the whole'
        },
        @{
            Name    = 'empty body'
            Title   = 'chore(deps): bump the action'
            Msgs    = @('chore(deps): bump the action')
            Verdict = 'pass'; Class = 'no-footers'
            Why     = 'a subject-only commit has no body to poison'
        }
    )

    # THE ROW COUNT IS ASSERTED, so a silently-deleted row reds (the classifier's
    # own discipline — "rows are the proof").
    $ExpectedRowCount = 14
    if ($rows.Count -ne $ExpectedRowCount) {
        Write-Host "  ✗  self-test table has $($rows.Count) rows, expected $ExpectedRowCount — a row was added or deleted without updating the count." -ForegroundColor Red
        return 1
    }

    Write-Host ''
    Write-Host '  ──────────────────────────────────────────────────────'
    Write-Host "  footer-check — self-test ($ExpectedRowCount rows, negative rows included)"
    Write-Host '  ──────────────────────────────────────────────────────'
    Write-Host ''

    $failed = 0
    foreach ($row in $rows) {
        $result = Test-FooterAgreement -Title $row.Title -Messages $row.Msgs
        $ok = ($result.Verdict -eq $row.Verdict) -and ($result.Class -eq $row.Class)
        if ($ok) {
            $mark = '✓'; $colour = 'Green'
        }
        else {
            $mark = '✗'; $colour = 'Red'; $failed++
        }
        Write-Host ("     {0} {1,-34} {2,-7} ({3}) — {4}" -f $mark, $row.Name, $result.Verdict, $result.Class, $row.Why) -ForegroundColor $colour
        if (-not $ok) {
            Write-Host ("        expected {0}/{1}, got {2}/{3}" -f $row.Verdict, $row.Class, $result.Verdict, $result.Class) -ForegroundColor Red
            Write-Host ("        reason: {0}" -f $result.Reason) -ForegroundColor Red
        }
    }

    Write-Host ''
    if ($failed -eq 0) {
        $reds = @($rows | Where-Object { $_.Verdict -eq 'RED' }).Count
        $passes = $ExpectedRowCount - $reds
        Write-Host "  ✓  footer-check self-test: $ExpectedRowCount/$ExpectedRowCount rows green ($passes pass arms, $reds RED arms)" -ForegroundColor Green
        return 0
    }

    Write-Host "  ✗  footer-check self-test: $failed/$ExpectedRowCount rows FAILED" -ForegroundColor Red
    return 1
}

# ─────────────────────────────────────────────────────────────────────────────
#  MAIN
# ─────────────────────────────────────────────────────────────────────────────
if ($SelfTest) {
    exit (Invoke-SelfTest)
}

if (-not (Test-Path -LiteralPath $CommitMessagesJson)) {
    throw "commit messages file not found: $CommitMessagesJson"
}

$raw = Get-Content -LiteralPath $CommitMessagesJson -Raw
$messages = @($raw | ConvertFrom-Json)

# A PR with no commits is not a thing, and a check that silently passes on an
# empty subject is the vacuous pass this repo keeps catching. Say so instead.
if ($messages.Count -eq 0) {
    throw "no commit messages were read from '$CommitMessagesJson' — refusing to pass vacuously (a PR always has at least one commit; this means the fetch failed)."
}

Write-Host "footer-check: $($messages.Count) commit(s), title: $Title"

$result = Test-FooterAgreement -Title $Title -Messages $messages

if ($result.Verdict -eq 'pass') {
    Write-Host "✓  footer-check: $($result.Reason)" -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host '  ✗  FOOTER CHECK — a commit BODY declares something the PR TITLE does not.' -ForegroundColor Red
Write-Host ''
Write-Host "     $($result.Reason)" -ForegroundColor Red
Write-Host ''
Write-Host '  WHAT THIS MEANS. Under squash, the branch subjects are discarded but the' -ForegroundColor Yellow
Write-Host '  BODIES concatenate into main''s commit body, and release-please parses them.' -ForegroundColor Yellow
Write-Host '  A note keyword at the START of a body line is a FOOTER even when the' -ForegroundColor Yellow
Write-Host '  sentence around it is prose explaining the mechanism. Leading spaces, a tab,' -ForegroundColor Yellow
Write-Host '  a list bullet and lowercase DO NOT save it — that is measured, not assumed.' -ForegroundColor Yellow
Write-Host ''
Write-Host '  IF YOU DID NOT MEAN IT — this is the likely case, and it is what happened' -ForegroundColor Yellow
Write-Host '  to 004179f: an 80-column wrap pushed the token to the start of a line.' -ForegroundColor Yellow
Write-Host '  FIX THE BODY, NOT THE TITLE:' -ForegroundColor Yellow
Write-Host '     · re-wrap so the token is not first on its line, or' -ForegroundColor Yellow
Write-Host '     · put it in `backticks`, or prefix the line with `> ` or `# `' -ForegroundColor Yellow
Write-Host '       (all three measured to produce no note), or' -ForegroundColor Yellow
Write-Host '     · name the thing instead of spelling it — the house rule.' -ForegroundColor Yellow
Write-Host ''
Write-Host '  IF YOU DID MEAN IT — declare it in the PR title, which is the subject that' -ForegroundColor Yellow
Write-Host '  lands on main:' -ForegroundColor Yellow
Write-Host '     · breaking  -> add the `!` marker:  feat(core)!: …' -ForegroundColor Yellow
Write-Host '     · a version -> name it in the title: chore(release): graduate to 1.0.0' -ForegroundColor Yellow
Write-Host ''
Write-Host '  ⚠ DO NOT add `!` to silence an accident. That makes the changelog announce' -ForegroundColor Yellow
Write-Host '  the thing your line wrap said by mistake. See scripts/footer-check.ps1.' -ForegroundColor Yellow
Write-Host ''
exit 1
