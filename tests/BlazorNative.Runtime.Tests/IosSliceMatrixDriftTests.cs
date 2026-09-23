using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

/// <summary>
/// PHASE 14.4 — the roster `ci.yml`'s required `ios-build` check cannot hold for itself.
///
/// THE HOLE THIS CLOSES. `ios-build` is an AGGREGATOR over the `ios-build-slice` matrix:
/// it reads <c>needs.ios-build-slice.result</c> and fails on anything that is not
/// <c>success</c>, including <c>skipped</c>. That is fail-closed against the matrix job as
/// a WHOLE — but a job-level <c>if:</c> on a matrix is evaluated PER LEG, and GitHub's
/// aggregate result is <c>success</c> for a mix of success and skipped. So
/// <c>if: matrix.leg != 'device'</c>, or simply deleting an entry from <c>include:</c>,
/// leaves the required check GREEN over exactly one compiled slice.
///
/// The aggregator cannot close that itself without counting legs at runtime, and the one
/// binding constraint on that job is that its VERDICT never depends on the Actions API —
/// it reads `needs` and nothing else, so an API hiccup can never make the gate pass. The
/// roster therefore lives OUTSIDE the job, here, in the required .NET suite that already
/// parses `ci.yml` for exactly this kind of copy.
///
/// WHAT THAT BUYS. With both RIDs pinned and the matrix job pinned unconditional, "every
/// leg the matrix declared succeeded" — which is all the aggregator can honestly claim —
/// becomes equivalent to "both slices compiled", TRUE BY CONSTRUCTION rather than by a
/// sentence in an echo. Dropping the device slice stops being a silent green and becomes
/// a red test that names the missing RID.
///
/// WHY THE RIDS ARE A LITERAL ROSTER AND NOT DERIVED. Every other copy of a RID in this
/// repo is downstream of this matrix — `ios.yml` declares the same two in its own
/// advisory matrix, which makes it a twin worth pinning against one day, but a purely
/// differential pin passes when BOTH copies lose the device leg. A floor has to be
/// absolute to be a floor. (`ReadmeDriftTests` derives both sides because both sides
/// exist; here one side is the decision itself.)
/// </summary>
public sealed class IosSliceMatrixDriftTests
{
    /// <summary>The job whose matrix the required `ios-build` check aggregates.</summary>
    private const string MatrixJob = "ios-build-slice";

    /// <summary>The required check itself — the aggregator over <see cref="MatrixJob"/>'s
    /// legs. It is not this pin's subject; it is this pin's FIXED POINT. See
    /// <see cref="TheConditionedLegDetector_StillMatchesTheAggregatorsOwnJobLevelIf"/>.</summary>
    private const string AggregatorJob = "ios-build";

    /// <summary>THE CONDITIONED-LEG DETECTOR, hoisted so the pin and its positive control
    /// share ONE copy of it (pin standard Rule 8 — a control over a second copy of the
    /// pattern controls the copy, not the pin).
    ///
    /// <para>Four spaces exactly, because that is what makes a key JOB-level in this
    /// workflow: a two-space job id, its keys at four, its steps' keys at eight or more.
    /// A step-level <c>if:</c> is ordinary and must not be flagged — and one exists
    /// inside <see cref="MatrixJob"/> itself, which is why the negative half of the
    /// control is a real line in the tree rather than a fixture.</para></summary>
    private const string JobLevelConditionPattern =
        @"(?m)^\s{4}(?<key>if|continue-on-error):\s*(?<value>.*)$";

    private static Match JobLevelCondition(string jobBody) =>
        Regex.Match(jobBody, JobLevelConditionPattern);

    /// <summary>Every slice the per-PR gate must build. Adding one here without adding it
    /// to `ci.yml` reds this test, and vice versa — which is the point.</summary>
    private static readonly string[] RequiredRids = ["iossimulator-arm64", "ios-arm64"];

    /// <summary>`ci.yml`'s `ios-build-slice` matrix declares EVERY required slice, and
    /// declares them unconditionally.
    ///
    /// Two facts, one test, because neither is sufficient alone: a matrix that names both
    /// RIDs but carries <c>if: matrix.leg != 'device'</c> compiles one slice and reports
    /// `success`, and an unconditional matrix that lost an `include:` entry does the same.
    /// Both are the green-on-nothing class the aggregator exists to close.
    ///
    /// NON-VACUITY, at four points, per issue #357: the job header must match exactly once,
    /// the job body must be non-empty, the `include:` block must match exactly once inside
    /// it, and at least one `rid:` must parse. If any regex here stops matching — the job
    /// is renamed, the matrix moves, the YAML is reindented — this test FAILS rather than
    /// looping zero times over nothing and going green.
    ///
    /// <para>Clause 3 is the one those four points do NOT reach: it is an absence claim,
    /// and no count assertion can tell a clean job from a blind pattern. Its fixed point
    /// is a separate fact,
    /// <see cref="TheConditionedLegDetector_StillMatchesTheAggregatorsOwnJobLevelIf"/>
    /// (phase 15.1, census item 6).</para></summary>
    [Fact]
    public void IosBuildSliceMatrix_DeclaresEveryRequiredSlice_Unconditionally()
    {
        string job = ReadJobBody(MatrixJob);

        // ── 1. the include: block, matched exactly once ─────────────────────────
        MatchCollection includes = Regex.Matches(job, @"(?m)^\s{6,}include:\s*$");
        Assert.True(includes.Count == 1,
            $"Expected exactly ONE `include:` under {MatrixJob}'s strategy/matrix in ci.yml, found "
            + $"{includes.Count}. Zero means the matrix moved or was rewritten as a bare "
            + "`rid: [...]` list and this pin is reading nothing; more than one means the parse is "
            + "ambiguous and could be reading the wrong block. Either way nothing below is "
            + "evidence — fix the pattern, do not delete the test.");

        string include = job[includes[0].Index..];

        var declared = Regex.Matches(include, @"(?m)^\s+-?\s*rid:\s*(?<rid>[A-Za-z0-9._-]+)\s*$")
            .Select(m => m.Groups["rid"].Value)
            .ToList();

        Assert.True(declared.Count > 0,
            $"{MatrixJob}'s `include:` block declares NO `rid:` at all, so this pin holds NOTHING "
            + "and would pass forever. The matrix was rewritten past the pattern, or emptied. "
            + "A pin whose subject vanished must say so, not go quietly green.");

        // ── 2. the roster, compared BOTH directions ─────────────────────────────
        var missing = RequiredRids.Except(declared, StringComparer.Ordinal).ToList();
        var extra = declared.Except(RequiredRids, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            "iOS SLICE MATRIX DRIFT — the required `ios-build` check would report success over "
            + "the wrong set of slices.\n"
            + (missing.Count > 0
                ? $"  Required but NOT declared in ci.yml's {MatrixJob} matrix: {string.Join(", ", missing)}\n"
                  + "  `ios-build` aggregates whatever legs the matrix declares, so a dropped leg is a "
                  + "SILENT pass: the check stays green and nothing else in the repo would notice. "
                  + "`ios-arm64` is the slice that actually ships and had never been built by CI at "
                  + "all before Phase 14.4.\n"
                : "")
            + (extra.Count > 0
                ? $"  Declared in ci.yml but not in this roster: {string.Join(", ", extra)}\n"
                  + "  A new slice is welcome, but it has to join the roster ON PURPOSE so this pin "
                  + "keeps knowing how many rows it should have.\n"
                : ""));

        // ── 3. nothing may condition a leg out ──────────────────────────────────
        // A job-level `if:` is evaluated PER LEG and a skipped leg does NOT make the
        // aggregate result anything other than `success`, so this is the one skip the
        // aggregator genuinely cannot see. `continue-on-error` is the same hole by
        // another spelling: it turns a red leg into a successful one.
        Match conditioned = JobLevelCondition(job);
        Assert.False(conditioned.Success,
            $"ci.yml's {MatrixJob} job declares a job-level "
            + $"`{conditioned.Groups["key"].Value}: {conditioned.Groups["value"].Value.Trim()}`.\n"
            + "  A job-level `if:` on a MATRIX is evaluated per leg, and GitHub reports the "
            + "aggregate `result` as `success` when some legs succeed and the rest are SKIPPED. So "
            + "`ios-build` — which reads only that aggregate, deliberately, to keep the Actions API "
            + "off its verdict path — would stay GREEN over a single compiled slice. "
            + "`continue-on-error` is the same hole spelled differently: it launders a red leg into "
            + "a successful one.\n"
            + "  If a leg really must be conditional, the aggregator has to learn to count legs "
            + "first, and that trade — an API call on the verdict path — needs deciding out loud, "
            + "not by editing this line.");
    }

    /// <summary>THE POSITIVE CONTROL FOR THE CONDITIONED-LEG DETECTOR (pin standard
    /// Rule 3; census item 6 — <i>"one clause of an otherwise strongly controlled
    /// fact"</i>).
    ///
    /// <para>The fact above is controlled everywhere except here. Its roster is compared
    /// BOTH directions, so a reworded `rid:` pattern yields an empty `declared` and reds
    /// naming both missing RIDs; its `include:` match is an exact-count assertion; its
    /// job header likewise. But clause 3 is an ABSENCE claim over
    /// <see cref="JobLevelConditionPattern"/>, and nothing required that pattern to still
    /// match anything. Reword it — four spaces to five, a `(?m)` dropped, the alternation
    /// misspelled — and a genuinely conditioned matrix leg sails through while the rest
    /// of the fact keeps reporting green.</para>
    ///
    /// <para>THE FIXED POINT IS THE AGGREGATOR'S OWN `if:`, and it is load-bearing rather
    /// than incidental. <c>ios-build</c> exists to fail when a leg did not succeed,
    /// including when it was SKIPPED — and a `needs:` job with no `if:` is itself skipped
    /// the moment its dependency fails, so without <c>if: ${{ always() }}</c> the
    /// aggregator would never run in exactly the case it was written for. It cannot be
    /// deleted while the job still does its job. That makes it the strongest anchor
    /// available in this workflow, and if it ever does move, this test reds and asks for
    /// the control to be re-pointed deliberately — which is the right outcome, because a
    /// detector whose fixed point vanished is a detector nobody is checking.</para>
    ///
    /// <para>THE NEGATIVE HALF IS ALSO A REAL LINE IN THE TREE. <see cref="MatrixJob"/>
    /// carries a STEP-level <c>if: always()</c>, indented eight spaces, inside the body
    /// this pin scans. It must NOT be flagged: the pin's claim is about job-level
    /// conditioning, and a detector widened to match any `if:` anywhere would red on
    /// ordinary step logic until someone deleted it. So the control asserts both
    /// directions — the four-space key matches, the eight-space one does not.</para>
    ///
    /// <para>WHAT IS NOT ANCHORED IN THE TREE: the <c>continue-on-error</c> alternative.
    /// No job and no step in <c>ci.yml</c> uses it, which is a fact about today and not a
    /// guarantee. Manufacturing one — adding a conditioned job to the workflow so a test
    /// could find it — would be changing the subject to suit the pin, so that clause gets
    /// a FIXTURE instead: a synthetic job-level line the pattern must still match. That
    /// is weaker than a tree anchor (it proves the regex, not that the regex is pointed at
    /// anything real) and it is labelled as such, but it is the difference between the
    /// alternation being checked and not being checked at all.</para></summary>
    [Fact]
    public void TheConditionedLegDetector_StillMatchesTheAggregatorsOwnJobLevelIf()
    {
        // ── positive, from the tree: the aggregator's job-level `if:` ────────────
        Match aggregator = JobLevelCondition(ReadJobBody(AggregatorJob));

        Assert.True(aggregator.Success,
            $"the conditioned-leg detector ({JobLevelConditionPattern}) no longer matches "
            + $"ci.yml's `{AggregatorJob}` job, which carries a job-level `if: ${{{{ always() }}}}` "
            + "and must, or it would be skipped on the very failure it exists to report.\n"
            + "  This is the FIXED POINT for clause 3 of "
            + nameof(IosBuildSliceMatrix_DeclaresEveryRequiredSlice_Unconditionally) + ", which is "
            + "an absence claim: with the pattern reworded past its subject, a job-level `if:` or "
            + "`continue-on-error:` on the MATRIX job would no longer be seen, and a single "
            + "compiled slice would report success through a green required check.\n"
            + "  Either the pattern was reworded — fix it — or the aggregator genuinely stopped "
            + "being conditional, in which case re-point this control at another job-level key on "
            + "purpose rather than deleting it.");

        Assert.True(aggregator.Groups["key"].Value == "if",
            $"the detector matched `{AggregatorJob}` on key '{aggregator.Groups["key"].Value}', "
            + "expected 'if'. The alternation's `if` branch is the one the aggregator anchors; a "
            + "match on the other branch means the pattern is reading a different line than this "
            + "control believes it is.");

        // ── negative, also from the tree: a STEP-level `if:` must not be flagged ──
        string matrixJob = ReadJobBody(MatrixJob);

        Assert.Contains("\n        if: always()", matrixJob, StringComparison.Ordinal);
        Assert.False(JobLevelCondition(matrixJob).Success,
            $"the detector flagged a key inside {MatrixJob} — but that job's only `if:` is a "
            + "STEP-level one, indented eight spaces, which is ordinary and must not be flagged. "
            + "A detector widened to match any `if:` at any depth reds on normal step logic, and a "
            + "pin that reds at everything gets deleted rather than fixed. (If the matrix job "
            + "genuinely grew a job-level condition, the sibling fact above is the one reporting "
            + "it, and this message is the wrong place to look.)");

        // ── fixture, NOT a tree anchor: the `continue-on-error` alternative ──────
        Match fixture = JobLevelCondition("  some-job:\n    continue-on-error: true\n    steps:\n");
        Assert.True(fixture.Success && fixture.Groups["key"].Value == "continue-on-error",
            "the detector no longer matches a job-level `continue-on-error:` — the second half of "
            + "its alternation, and the same hole spelled differently (it launders a red leg into "
            + "a successful one). Nothing in ci.yml uses it today, so this clause has no anchor in "
            + "the tree and is held by this fixture instead. A fixture proves the REGEX still "
            + "recognises the shape; it cannot prove the regex is pointed at anything real. That "
            + "second property is what the aggregator assertion above buys.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Returns the YAML of one `ci.yml` job: everything from its two-space-indented
    /// key to the next one, or to end of file. Asserts the header matched EXACTLY ONCE and
    /// that the body is non-empty — a blind parse is worse than no parse.</summary>
    private static string ReadJobBody(string jobId)
    {
        string yaml = ReadCheckoutFile(Path.Combine(".github", "workflows", "ci.yml"));

        MatchCollection headers = Regex.Matches(yaml, $@"(?m)^\s{{2}}{Regex.Escape(jobId)}:\s*$");
        Assert.True(headers.Count == 1,
            $"Expected exactly ONE `{jobId}:` job in ci.yml, found {headers.Count}. Zero means the "
            + "job was renamed or removed — and if it was renamed, the required `ios-build` "
            + "aggregator's `needs:` is pointing at a job that no longer exists, which is a far "
            + "bigger problem than this test. Fix that first.");

        int start = headers[0].Index;
        Match next = Regex.Match(yaml[(start + headers[0].Length)..], @"(?m)^\s{2}[A-Za-z0-9_-]+:\s*$");
        string body = next.Success
            ? yaml.Substring(start, headers[0].Length + next.Index)
            : yaml[start..];

        Assert.True(body.Trim().Length > jobId.Length + 1,
            $"ci.yml's `{jobId}:` job parsed to an EMPTY body. The next-job boundary matched "
            + "immediately, so everything below would be reading nothing.");
        return body;
    }

    /// <summary>The workflows are not build inputs of this project, so they are read from the
    /// checkout — which is what makes `build-test` the only lane that can host this test
    /// (ShellStyleTableDriftTests' rule, and ReadmeDriftTests' too).</summary>
    private static string ReadCheckoutFile(string relativePath)
    {
        string file = Path.Combine(BnRepo.Root(), relativePath);
        Assert.True(File.Exists(file), $"checkout file not found: {file}");
        return File.ReadAllText(file);
    }
}
