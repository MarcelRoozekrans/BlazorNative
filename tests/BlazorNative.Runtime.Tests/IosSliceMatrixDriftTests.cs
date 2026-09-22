using System.Text.RegularExpressions;

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
    /// looping zero times over nothing and going green.</summary>
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
        Match conditioned = Regex.Match(job, @"(?m)^\s{4}(?<key>if|continue-on-error):\s*(?<value>.*)$");
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
        string file = Path.Combine(RepoRoot(), relativePath);
        Assert.True(File.Exists(file), $"checkout file not found: {file}");
        return File.ReadAllText(file);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
