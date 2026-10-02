using System.Text.RegularExpressions;
using Xunit;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// 16.7 (#455): the rc contract is written in three places, and threading.md tells
// readers the copies "can't drift apart". This pin is what makes that true. Each copy
// sits between a BEGIN rc-contract and an END rc-contract line; the text between them
// is compared after each file's OWN comment prefix is stripped (`///` in Exports.cs, `//` in
// the header, nothing in threading.md), and whitespace is collapsed. threading.md's copy sits
// in a code block, so its FIRST body line is dropped when it is an opening fence, with an
// optional language word, and its LAST body line when it is a bare closing fence. No other
// line is ever dropped: a fence line mid-block, text on a fence line, or a leading `*` is
// compared as contract text, because the page shows it.
//
// DOES NOT COVER:
//   - whether the contract is TRUE, which FaultNoticeTests' inline-completion facts pin;
//   - the return-code lists under the contract, which are written per file;
//   - whether the contract matches the spec: the pin compares the copies with each other, never
//     with the spec, so one identical change to all three stays green.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RcContractCopiesTests
{
    /// <summary>Each copy, with the one comment prefix its contract lines carry, null stripping
    /// nothing, and whether the copy is wrapped in one code block.</summary>
    private static readonly (string Path, string? Prefix, bool Fenced)[] Copies =
    [
        (Path.Combine("src", "BlazorNative.Runtime", "Exports.cs"), "///", false),
        (Path.Combine("src", "BlazorNative.Apple", "BnHost", "BlazorNativeRuntimeC.h"), "//", false),
        (Path.Combine("website", "docs", "guides", "threading.md"), null, true),
    ];

    /// <summary>The block's opening fence, at most with a language word.</summary>
    private static readonly Regex OpeningFence = new(@"^\s*```[A-Za-z]*\s*$");

    /// <summary>The block's closing fence: CommonMark allows nothing after it but spaces.</summary>
    private static readonly Regex ClosingFence = new(@"^\s*```\s*$");

    /// <summary>The normalised text between the markers, or a failure naming the file.</summary>
    private static string ContractIn(string relative, string? prefix, bool fenced)
    {
        string path = Path.Combine(BnRepo.Root(), relative);
        string[] lines = File.ReadAllLines(path);
        int[] begins = [.. lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("BEGIN rc-contract", StringComparison.Ordinal)).Select(x => x.i)];
        int[] ends = [.. lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("END rc-contract", StringComparison.Ordinal)).Select(x => x.i)];
        Assert.True(begins.Length == 1 && ends.Length == 1 && begins[0] < ends[0],
            $"{relative}: expected exactly one BEGIN rc-contract line before exactly one END rc-contract "
            + $"line, found {begins.Length} BEGIN and {ends.Length} END. Without them this pin compares nothing.");
        List<string> block = [.. lines[(begins[0] + 1)..ends[0]]];
        if (fenced && block.Count > 0 && OpeningFence.IsMatch(block[0]))
            block.RemoveAt(0);
        if (fenced && block.Count > 0 && ClosingFence.IsMatch(block[^1]))
            block.RemoveAt(block.Count - 1);
        IEnumerable<string> body = block
            .Select(l => prefix is null ? l : Regex.Replace(l, @"^\s*" + Regex.Escape(prefix) + @"\s?", ""));
        return Regex.Replace(string.Join(" ", body), @"\s+", " ").Trim();
    }

    [Fact]
    public void TheRcContract_IsTheSameInAllThreeCopies()
    {
        var (refPath, refPrefix, refFenced) = Copies[0];
        string reference = ContractIn(refPath, refPrefix, refFenced);
        // Rule 2 floor. The normalised contract measured 509 characters on 2026-10-02, so a
        // floor of 200 leaves 309 of headroom, on purpose: the floor only has to tell an empty
        // or collapsed block from the contract.
        // Shortening the contract identically in all three copies stays green; that is a spec
        // question, not drift.
        Assert.True(reference.Length > 200,
            $"{refPath}: the contract between the markers is {reference.Length} characters, too short to be it. "
            + "It measured 509 when this floor of 200 was set, a margin of 309.");
        foreach (var (copy, prefix, fenced) in Copies.Skip(1))
        {
            string text = ContractIn(copy, prefix, fenced);
            Assert.True(text == reference,
                $"{copy} has a different rc contract from {refPath}.\n  {refPath}: {reference}\n  {copy}: {text}");
        }
    }
}
