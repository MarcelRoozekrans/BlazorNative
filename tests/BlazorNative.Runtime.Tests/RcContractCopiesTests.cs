using System.Text.RegularExpressions;
using Xunit;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// 16.7 (#455): the rc contract is written in three places, and threading.md tells
// readers the copies "can't drift apart". This pin is what makes that true. Each copy
// sits between a BEGIN rc-contract and an END rc-contract line; the text between them
// is compared after each file's OWN comment prefix is stripped (`///` in Exports.cs, `//` in
// the header, nothing in threading.md, whose copy sits in a code block), lines that are
// exactly a code fence are dropped, and whitespace is collapsed. Every other line is
// compared whole, so text on a fence line, or a leading `*`, counts as contract text.
//
// DOES NOT COVER:
//   - whether the contract is TRUE, which FaultNoticeTests' inline-completion facts pin;
//   - the return-code lists under the contract, which are written per file;
//   - whether the contract matches the spec: the pin compares the copies with each other, never
//     with the spec, so one identical change to all three stays green.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RcContractCopiesTests
{
    /// <summary>Each copy, with the one comment prefix its contract lines carry; null strips nothing.</summary>
    private static readonly (string Path, string? Prefix)[] Copies =
    [
        (Path.Combine("src", "BlazorNative.Runtime", "Exports.cs"), "///"),
        (Path.Combine("src", "BlazorNative.Apple", "BnHost", "BlazorNativeRuntimeC.h"), "//"),
        (Path.Combine("website", "docs", "guides", "threading.md"), null),
    ];

    /// <summary>A line that is exactly a code fence, at most with a language word. Any other
    /// text on the line makes it content, which is compared.</summary>
    private static readonly Regex Fence = new(@"^\s*```[A-Za-z]*\s*$");

    /// <summary>The normalised text between the markers, or a failure naming the file.</summary>
    private static string ContractIn(string relative, string? prefix)
    {
        string path = Path.Combine(BnRepo.Root(), relative);
        string[] lines = File.ReadAllLines(path);
        int[] begins = [.. lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("BEGIN rc-contract", StringComparison.Ordinal)).Select(x => x.i)];
        int[] ends = [.. lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("END rc-contract", StringComparison.Ordinal)).Select(x => x.i)];
        Assert.True(begins.Length == 1 && ends.Length == 1 && begins[0] < ends[0],
            $"{relative}: expected exactly one BEGIN rc-contract line before exactly one END rc-contract "
            + $"line, found {begins.Length} BEGIN and {ends.Length} END. Without them this pin compares nothing.");
        IEnumerable<string> body = lines[(begins[0] + 1)..ends[0]]
            .Where(l => !Fence.IsMatch(l))
            .Select(l => prefix is null ? l : Regex.Replace(l, @"^\s*" + Regex.Escape(prefix) + @"\s?", ""));
        return Regex.Replace(string.Join(" ", body), @"\s+", " ").Trim();
    }

    [Fact]
    public void TheRcContract_IsTheSameInAllThreeCopies()
    {
        var (refPath, refPrefix) = Copies[0];
        string reference = ContractIn(refPath, refPrefix);
        Assert.True(reference.Length > 200,
            $"{refPath}: the contract between the markers is {reference.Length} characters, too short to be it.");
        foreach (var (copy, prefix) in Copies.Skip(1))
        {
            string text = ContractIn(copy, prefix);
            Assert.True(text == reference,
                $"{copy} has a different rc contract from {refPath}.\n  {refPath}: {reference}\n  {copy}: {text}");
        }
    }
}
