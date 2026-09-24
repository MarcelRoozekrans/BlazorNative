using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ReleaseParserVersionPinTests — the parse guard runs the parser that writes the notes (#302).
//
// scripts/commit-parse-check is only worth running if its release-please, and its
// release-please's @conventional-commits/parser, are the pair release-please-action
// bundles: the two commits it exists to catch parse or fail depending on the
// parser's version. "Same version as the action" would otherwise be a comment.
// action-version.json records the pair pinned — release-please plus its parser —
// measured from the action's lockfile, and this pin holds both ends of it.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - Whether the recorded pair is TRUE. A bump that copies the new actionSha in
//     without re-measuring passes. This pin guarantees a bump is NOTICED, not
//     that it is measured correctly; action-version.json's $doc says how.
//   - A second release-please-action use in release-please.yml. That reds on the
//     exactly-one floor as unmodelled rather than being compared.
//   - Whether the squash reconstruction check.js approximates matches what GitHub
//     actually builds. It assumes the repo's COMMIT_MESSAGES squash setting, which
//     nothing here pins, and a message edited in the merge dialog is never seen.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The parse guard's release-please version is the one the release action runs.</summary>
public sealed class ReleaseParserVersionPinTests
{
    private const string GuardDir = "scripts/commit-parse-check";

    private static readonly Regex ActionUse = new(
        @"^\s*-?\s*uses:\s*googleapis/release-please-action@([0-9a-f]{40})\b",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static JsonElement Json(string relativePath) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(BnRepo.Root(), relativePath))).RootElement;

    private static string Recorded(string property) =>
        Json($"{GuardDir}/action-version.json").GetProperty(property).GetString() ?? "";

    [Fact]
    public void TheRecordedActionSha_IsTheOneTheReleaseWorkflowRuns()
    {
        string workflow = File.ReadAllText(Path.Combine(BnRepo.Root(), ".github/workflows/release-please.yml"));
        MatchCollection uses = ActionUse.Matches(workflow);
        Assert.True(uses.Count == 1,
            $"expected exactly one SHA-pinned `uses: googleapis/release-please-action@…` step, found {uses.Count} — the pin's anchor moved or multiplied.");
        Assert.True(uses[0].Groups[1].Value == Recorded("actionSha"),
            $"release-please.yml now runs release-please-action@{uses[0].Groups[1].Value}, but {GuardDir}/action-version.json "
            + $"records {Recorded("actionSha")}. Re-measure which release-please that SHA bundles, as the file's $doc says, "
            + "then update actionSha, releasePlease and the guard's package.json together.");
    }

    [Fact]
    public void TheGuardsLockedParser_IsTheRecordedVersion()
    {
        string recorded = Recorded("releasePlease");
        Assert.Matches(@"^\d+\.\d+\.\d+$", recorded);

        string declared = Json($"{GuardDir}/package.json").GetProperty("dependencies").GetProperty("release-please").GetString() ?? "";
        Assert.True(declared == recorded,
            $"{GuardDir}/package.json declares release-please \"{declared}\"; it must be exactly \"{recorded}\", with no range, or the lockfile can drift from the action.");

        string locked = Json($"{GuardDir}/package-lock.json").GetProperty("packages")
            .GetProperty("node_modules/release-please").GetProperty("version").GetString() ?? "";
        Assert.True(locked == recorded, $"{GuardDir}/package-lock.json locks release-please {locked}; the action bundles {recorded}.");

        string recordedParser = Recorded("conventionalCommitsParser");
        Assert.Matches(@"^\d+\.\d+\.\d+$", recordedParser);

        string lockedParser = Json($"{GuardDir}/package-lock.json").GetProperty("packages")
            .GetProperty("node_modules/@conventional-commits/parser").GetProperty("version").GetString() ?? "";
        Assert.True(lockedParser == recordedParser,
            $"{GuardDir}/package-lock.json locks @conventional-commits/parser {lockedParser}; action-version.json records {recordedParser}.");
    }
}
