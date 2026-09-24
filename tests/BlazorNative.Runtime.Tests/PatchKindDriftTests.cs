using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BlazorNative.Renderer;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PatchKindDriftTests — every RenderPatch, one wire kind, three languages (#297).
//
// FrameEncoder.Encode's `default:` arm says what adding a RenderPatch requires:
// "add a case here AND a wire id to BlazorNativePatchKind (+ Kotlin mirror)".
// Until 15.4 nothing enumerated the subclasses against that obligation. RenderPatch
// is an ordinary record hierarchy, so a new subclass compiles, the renderer emits
// it, and the `default:` arm fires AT RUNTIME, ON DEVICE, on the first real frame
// that carries it. #261's ScrollToPatch was added exactly this way.
//
// Everything here is DERIVED, never restated:
//   · the subclasses come from reflection over BlazorNative.Renderer;
//   · each one's kind comes from ENCODING a real instance through the real
//     FrameEncoder, so the switch is measured rather than re-listed;
//   · the Kotlin and Swift arms are read from the one block in each file that
//     switches on the patch kind, anchored so another `when` or `switch` cannot
//     be read by mistake.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - Arm BODIES. That an arm exists for kind 6 says nothing about whether it
//     decodes SetStyle correctly; FrameEncoderTests and the shells' own adapter
//     tests own that.
//   - The reserved id's meaning. AppendChild = 2 is asserted to be produced by no
//     subclass and to have no arm; what reviving it would mean is out of scope.
//   - The template's copy of NativeFrameAdapter.kt. Not read here:
//     TemplateDriftTests.TemplateShellSources_AreByteIdenticalToTheRepos already
//     byte-compares it to the repo copy this pin reads.
//   - A subclass that cannot encode from default arguments and whose override is
//     WRONG. The override table only supplies arguments; the reflection
//     enumeration still decides which types are tested, so a new subclass is
//     never skipped, only possibly mis-constructed, and that reds.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Every <see cref="RenderPatch"/> subclass encodes to exactly one live
/// <see cref="BlazorNativePatchKind"/>, and both shells switch on exactly the live kinds.</summary>
public sealed class PatchKindDriftTests
{
    private const string KotlinAdapter = "src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeFrameAdapter.kt";
    private const string SwiftAdapter = "src/BlazorNative.Apple/BnHost/BnFrameAdapter.swift";

    /// <summary>Measured 2026-09-24: nine live RenderPatch records. The Rule 2 floor for
    /// the reflection scan: a scan that finds fewer has stopped seeing the hierarchy.</summary>
    private const int MinimumSubclassCount = 9;

    private static readonly Regex KotlinOpen = new(@"\bwhen\s*\(\s*kind\s*\)\s*\{", RegexOptions.CultureInvariant);
    private static readonly Regex KotlinClose = new(@"^\s*else\s*->", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex KotlinArm = new(@"^\s*(\d+)\s*->", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex SwiftOpen = new(@"\bswitch\s+kind\s*\{", RegexOptions.CultureInvariant);
    private static readonly Regex SwiftClose = new(@"^\s*default\s*:", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex SwiftArm = new(@"^\s*case\s+(\d+)\s*:", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Only where a default argument cannot encode. CreateNode's NodeType must be a
    /// node type the wire vocabulary knows, or MapNodeType throws before the kind is written.</summary>
    private static readonly Dictionary<(Type Type, string Parameter), object?> ArgumentOverrides = new()
    {
        [(typeof(CreateNodePatch), "NodeType")] = "view",
    };

    internal static Type[] PatchSubclasses() =>
        [.. typeof(RenderPatch).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(RenderPatch)) && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

    internal static BlazorNativePatchKind[] LiveKinds() =>
        [.. Enum.GetValues<BlazorNativePatchKind>().Where(k => k != BlazorNativePatchKind.AppendChild)];

    internal static RenderPatch Instantiate(Type type)
    {
        ConstructorInfo ctor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        object?[] args = [.. ctor.GetParameters().Select(p => ArgumentFor(type, p))];
        return (RenderPatch)ctor.Invoke(args);
    }

    private static object? ArgumentFor(Type type, ParameterInfo p)
    {
        if (ArgumentOverrides.TryGetValue((type, p.Name!), out object? value)) return value;
        if (p.HasDefaultValue) return p.DefaultValue;
        if (p.ParameterType == typeof(string)) return "";
        return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
    }

    /// <summary>The kind the REAL encoder writes for this patch. Throws exactly when
    /// <c>FrameEncoder.Encode</c>'s <c>default:</c> arm does.</summary>
    internal static BlazorNativePatchKind EncodedKind(RenderPatch patch)
    {
        var frame = new RenderFrame(FrameId: 1, TimestampMs: 0L, Patches: [patch]);
        using var arena = FrameArena.Rent();
        BlazorNativeFrame native = FrameEncoder.Encode(frame, arena);
        return Marshal.PtrToStructure<BlazorNativePatch>(native.Patches).Kind;
    }

    internal static SortedSet<int> KotlinArms(string text) => Arms(text, KotlinOpen, KotlinClose, KotlinArm, "when (kind) {");
    internal static SortedSet<int> SwiftArms(string text) => Arms(text, SwiftOpen, SwiftClose, SwiftArm, "switch kind {");

    private static SortedSet<int> Arms(string text, Regex open, Regex close, Regex arm, string anchor)
    {
        string code = CommentStrippedSource.Strip(text);
        MatchCollection opens = open.Matches(code);
        Assert.True(opens.Count == 1,
            $"expected exactly one `{anchor}` block, found {opens.Count} — the anchor this pin reads from has moved or multiplied.");
        int start = opens[0].Index + opens[0].Length;
        Match stop = close.Match(code, start);
        Assert.True(stop.Success, $"the `{anchor}` block has no closing fallback arm — the region this pin reads is unbounded.");
        string block = code[start..stop.Index];
        return new SortedSet<int>(arm.Matches(block).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
    }

    /// <summary>The detector for facts 3 and 4, and for the positive control.</summary>
    internal static List<string> ArmMismatches(SortedSet<int> arms)
    {
        HashSet<int> live = [.. LiveKinds().Select(k => (int)k)];
        var problems = new List<string>();
        foreach (int k in live.Where(k => !arms.Contains(k)).Order())
            problems.Add($"no arm for kind {k} ({(BlazorNativePatchKind)k}) — a frame carrying it is skipped as unknown");
        foreach (int a in arms.Where(a => !live.Contains(a)))
            problems.Add($"arm {a} matches no live kind — stale, or reserved id {(BlazorNativePatchKind)a} revived on one side only");
        return problems;
    }

    private static void AssertArmsMatch(string relativePath, Func<string, SortedSet<int>> extract)
    {
        string text = File.ReadAllText(Path.Combine(BnRepo.Root(), relativePath));
        SortedSet<int> arms = extract(text);
        Assert.True(arms.Count > 0, $"{relativePath}: read zero patch-kind arms — this fact would compare nothing (Rule 2).");
        List<string> problems = ArmMismatches(arms);
        Assert.True(problems.Count == 0, $"{relativePath}: {string.Join("; ", problems)}");
    }

    [Fact]
    public void EveryRenderPatchSubclass_Encodes()
    {
        Type[] types = PatchSubclasses();
        Assert.True(types.Length >= MinimumSubclassCount,
            $"reflection found {types.Length} RenderPatch subclasses, fewer than the measured {MinimumSubclassCount} — the scan has stopped seeing the hierarchy (Rule 2).");

        var failures = new List<string>();
        foreach (Type t in types)
        {
            try { EncodedKind(Instantiate(t)); }
            catch (Exception e) { failures.Add($"{t.Name}: {e.GetType().Name}: {e.Message}"); }
        }
        Assert.True(failures.Count == 0,
            "these RenderPatch subclasses do not encode. FrameEncoder.Encode needs a case, AND "
            + "BlazorNativePatchKind a wire id, AND both shells an arm: " + string.Join(" | ", failures));
    }

    [Fact]
    public void TheEncodedKinds_AreExactlyTheLiveEnum_BothWays()
    {
        Dictionary<Type, BlazorNativePatchKind> kinds = PatchSubclasses().ToDictionary(t => t, t => EncodedKind(Instantiate(t)));
        Assert.True(kinds.Count >= MinimumSubclassCount, $"only {kinds.Count} subclasses encoded — floor {MinimumSubclassCount} (Rule 2).");

        string[] undeclared = [.. kinds.Where(kv => !Enum.IsDefined(kv.Value)).Select(kv => $"{kv.Key.Name}→{(int)kv.Value}")];
        Assert.True(undeclared.Length == 0, $"encoded to an id BlazorNativePatchKind does not declare: {string.Join(", ", undeclared)}");

        string[] shared = [.. kinds.GroupBy(kv => kv.Value).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(" + ", g.Select(kv => kv.Key.Name))}")];
        Assert.True(shared.Length == 0, $"two subclasses encode to one kind, so a shell cannot tell them apart: {string.Join("; ", shared)}");

        Assert.DoesNotContain(BlazorNativePatchKind.AppendChild, kinds.Values);

        BlazorNativePatchKind[] unproduced = [.. LiveKinds().Except(kinds.Values)];
        Assert.True(unproduced.Length == 0,
            $"declared kinds no subclass produces — dead wire ids, or a deleted patch whose id was not reserved like AppendChild: {string.Join(", ", unproduced)}");
    }

    [Fact]
    public void KotlinAdapterArms_AreExactlyTheLiveKinds() => AssertArmsMatch(KotlinAdapter, KotlinArms);

    [Fact]
    public void SwiftAdapterArms_AreExactlyTheLiveKinds() => AssertArmsMatch(SwiftAdapter, SwiftArms);

    /// <summary>Rule 3: both detectors, driven over a planted divergence. A pin that has
    /// never been seen to say no proves nothing.</summary>
    [Fact]
    public void TheDetectors_SeeAPlantedDivergence()
    {
        // The encoder half: a subclass the switch has never heard of reaches `default:`.
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodedKind(new PlantedPatch()));

        // The arm half: remove kind 10's arm from the REAL Kotlin text.
        string real = File.ReadAllText(Path.Combine(BnRepo.Root(), KotlinAdapter));
        string planted = Regex.Replace(real, @"^(\s*)10\s*->", "$1// planted: arm removed ->", RegexOptions.Multiline);
        Assert.NotEqual(real, planted); // the splice landed, or this control proves nothing
        List<string> problems = ArmMismatches(KotlinArms(planted));
        Assert.Contains(problems, p => p.StartsWith("no arm for kind 10", StringComparison.Ordinal));
    }

    private sealed record PlantedPatch : RenderPatch;
}
