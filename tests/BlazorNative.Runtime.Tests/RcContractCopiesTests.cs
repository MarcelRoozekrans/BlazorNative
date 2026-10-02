using System.Text.RegularExpressions;
using Xunit;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// 16.7 (#455): the rc contract is written in three places, and threading.md tells
// readers the copies "can't drift apart". This pin is what makes that true. Each copy
// sits between a BEGIN rc-contract and an END rc-contract line; the text between them
// is compared after comment prefixes, code fences and whitespace are normalised.
//
// DOES NOT COVER:
//   - whether the contract is TRUE, which FaultNoticeTests' inline-completion facts pin;
//   - the return-code lists under the contract, which are written per file;
//   - whether the contract matches the spec: the pin compares the copies with each other, never
//     with the spec, so one identical change to all three stays green.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RcContractCopiesTests
{
    private static readonly string[] Copies =
    [
        Path.Combine("src", "BlazorNative.Runtime", "Exports.cs"),
        Path.Combine("src", "BlazorNative.Apple", "BnHost", "BlazorNativeRuntimeC.h"),
        Path.Combine("website", "docs", "guides", "threading.md"),
    ];

    /// <summary>The normalised text between the markers, or a failure naming the file.</summary>
    private static string ContractIn(string relative)
    {
        string path = Path.Combine(BnRepo.Root(), relative);
        string[] lines = File.ReadAllLines(path);
        int[] begins = [.. lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("BEGIN rc-contract", StringComparison.Ordinal)).Select(x => x.i)];
        int[] ends = [.. lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("END rc-contract", StringComparison.Ordinal)).Select(x => x.i)];
        Assert.True(begins.Length == 1 && ends.Length == 1 && begins[0] < ends[0],
            $"{relative}: expected exactly one BEGIN rc-contract line before exactly one END rc-contract "
            + $"line, found {begins.Length} BEGIN and {ends.Length} END. Without them this pin compares nothing.");
        IEnumerable<string> body = lines[(begins[0] + 1)..ends[0]]
            .Select(l => Regex.Replace(l, @"^\s*(///|//|\*)?\s?", ""))
            .Where(l => !l.TrimStart().StartsWith("```", StringComparison.Ordinal));
        return Regex.Replace(string.Join(" ", body), @"\s+", " ").Trim();
    }

    [Fact]
    public void TheRcContract_IsTheSameInAllThreeCopies()
    {
        string reference = ContractIn(Copies[0]);
        Assert.True(reference.Length > 200,
            $"{Copies[0]}: the contract between the markers is {reference.Length} characters, too short to be it.");
        foreach (string copy in Copies.Skip(1))
        {
            string text = ContractIn(copy);
            Assert.True(text == reference,
                $"{copy} has a different rc contract from {Copies[0]}.\n  {Copies[0]}: {reference}\n  {copy}: {text}");
        }
    }
}
