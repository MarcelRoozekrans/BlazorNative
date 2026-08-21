using System.Text;

namespace BlazorNative.WireGen;

// ─────────────────────────────────────────────────────────────────────────────
// The emitters — one pure function per output file.
//
// PURE ON PURPOSE: each takes the manifest and returns a string, touching no
// disk. That is what lets WireVocabularyCodegenTests re-run them in-process and
// byte-compare against the committed files, which is the pin that makes
// "generated" mean something. A function that wrote its own file could only be
// tested by writing somewhere and reading it back.
//
// LINE ENDINGS: every emitter writes "\n". The repo normalizes with
// `* text=auto`, so the committed files are LF in git and CRLF in a Windows
// checkout — the byte comparison therefore happens against the file as READ
// FROM DISK through a reader that normalizes, never against raw bytes. See the
// test for that decision; getting it wrong makes the pin fail on one OS only.
// ─────────────────────────────────────────────────────────────────────────────

public static class Emitters
{
    public const string ManifestPath = "src/wire-vocabulary.json";

    /// <summary>The deep-link vector manifest — the second table this tool owns.</summary>
    public const string VectorsManifestPath = "src/deeplink-vectors.json";

    /// <summary>A comment block in the target language's line-comment syntax.
    /// Every generated file opens with one; only the prose differs.</summary>
    private static string CommentBlock(string commentPrefix, string[] lines)
    {
        var sb = new StringBuilder();
        foreach (string line in lines)
            sb.Append(commentPrefix).Append(line.Length == 0 ? "" : " ").Append(line).Append('\n');
        return sb.ToString();
    }

    /// <summary>The banner every generated file carries. Named files, not vague
    /// advice: the reader who lands here needs to know where the names live and
    /// which command rewrites this one.</summary>
    private static string Banner(string commentPrefix) => CommentBlock(commentPrefix,
    [
        "GENERATED FILE — DO NOT EDIT (#255).",
        "",
        $"Source of truth: {ManifestPath}",
        "Regenerate:      dotnet run --project tools/BlazorNative.WireGen",
        "",
        "These names used to be hand-maintained in four languages, agreeing only",
        "because a drift test parsed them back out and said so. Editing this file",
        "by hand puts that back: WireVocabularyCodegenTests re-runs the emitter and",
        "byte-compares, so the edit fails the required build-test lane rather than",
        "reaching a device.",
    ]);

    /// <summary>The banner the three vector tables carry — same shape as
    /// <see cref="Banner"/>, naming the vector manifest instead.</summary>
    private static string VectorsBanner(string commentPrefix) => CommentBlock(commentPrefix,
    [
        "GENERATED FILE — DO NOT EDIT (#278).",
        "",
        $"Source of truth: {VectorsManifestPath}",
        "Regenerate:      dotnet run --project tools/BlazorNative.WireGen",
        "",
        "One URL → route case, written ONCE in the manifest and asserted by all",
        "three suites. You cannot generate a deep-link parser; you CAN generate the",
        "table it must satisfy, which is the whole point — the two hand-written",
        "parsers disagreed for months because nothing compared them. Editing this",
        "file by hand puts one suite's expectations out of step with the others';",
        "WireVocabularyCodegenTests re-runs the emitter and byte-compares, so the",
        "edit fails the required build-test lane rather than reaching a device.",
    ]);

    // ── C# — BlazorNative.Renderer ───────────────────────────────────────────

    public static string EmitCSharp(WireVocabulary v)
    {
        var sb = new StringBuilder();
        sb.Append(Banner("//"));
        sb.Append("""

            namespace BlazorNative.Renderer;

            /// <summary>The wire vocabulary, generated from the manifest. The DATA only:
            /// <see cref="NativeRenderer"/> keeps the sets, the comparer choice and the prose
            /// that explains the partition — this file exists so those names cannot disagree
            /// with the two shells'.</summary>
            internal static class BnWireVocabulary
            {

            """);

        AppendCSharpArray(sb, "YogaStyles", v.YogaStyles);
        sb.Append('\n');
        AppendCSharpArray(sb, "VisualStyles", v.VisualStyles);
        sb.Append('\n');
        AppendCSharpNames(sb, "ScrollIgnoredContainerStyles", v.ScrollIgnoredContainerStyles.Names);
        sb.Append('\n');
        AppendCSharpNames(sb, "MeasuredNodeTypes", v.MeasuredNodeTypes.Names);
        sb.Append('\n');

        sb.Append("    /// <summary>The shells' ordinal node-type array — index IS the wire id.</summary>\n");
        sb.Append("    internal static readonly string[] NodeTypeNames =\n    [\n");
        foreach (NodeType t in v.NodeTypes.Types)
            sb.Append($"        \"{t.WireName ?? v.NodeTypes.FallbackName}\", // {t.Id} = {t.Enum}\n");
        sb.Append("    ];\n");

        sb.Append("}\n");
        return sb.ToString();
    }

    private static void AppendCSharpArray(StringBuilder sb, string name, StyleTable table)
    {
        sb.Append($"    internal static readonly string[] {name} =\n    [\n");
        foreach (StyleGroup g in table.Groups)
        {
            sb.Append($"        // {g.Name}\n");
            sb.Append("        ").Append(string.Join(", ", g.Names.Select(n => $"\"{n}\""))).Append(",\n");
        }
        sb.Append("    ];\n");
    }

    private static void AppendCSharpNames(StringBuilder sb, string name, IEnumerable<string> names)
    {
        sb.Append($"    internal static readonly string[] {name} =\n    [\n");
        sb.Append("        ").Append(string.Join(", ", names.Select(n => $"\"{n}\""))).Append(",\n");
        sb.Append("    ];\n");
    }

    // ── Kotlin — src/main/kotlin (visible to BOTH the JVM host target and the
    //    android variant, which compiles main + androidMain together) ─────────

    public static string EmitKotlin(WireVocabulary v)
    {
        var sb = new StringBuilder();
        sb.Append(Banner("//"));
        sb.Append("""

            package io.blazornative.jni

            /**
             * The wire vocabulary, generated from the manifest. The DATA only — the shells
             * keep their own routing code and the prose that explains it; this object exists
             * so their names cannot disagree with .NET's or with each other's.
             */
            internal object BnWireVocabulary {

            """);

        AppendKotlinList(sb, "YOGA_STYLES", v.YogaStyles);
        sb.Append('\n');
        AppendKotlinList(sb, "VISUAL_STYLES", v.VisualStyles);
        sb.Append('\n');
        AppendKotlinNames(sb, "SCROLL_IGNORED_CONTAINER_STYLES", v.ScrollIgnoredContainerStyles.Names);
        sb.Append('\n');
        AppendKotlinNames(sb, "MEASURED_NODE_TYPES", v.MeasuredNodeTypes.Names);
        sb.Append('\n');

        sb.Append("    /** Index IS the wire id — decoded by indexing, so order is the contract. */\n");
        sb.Append("    internal val NODE_TYPES = arrayOf(\n");
        foreach (NodeType t in v.NodeTypes.Types)
            sb.Append($"        \"{t.WireName ?? v.NodeTypes.FallbackName}\", // {t.Id} = {t.Enum}\n");
        sb.Append("    )\n");

        sb.Append("}\n");
        return sb.ToString();
    }

    private static void AppendKotlinList(StringBuilder sb, string name, StyleTable table)
    {
        sb.Append($"    internal val {name} = setOf(\n");
        foreach (StyleGroup g in table.Groups)
        {
            sb.Append($"        // {g.Name}\n");
            sb.Append("        ").Append(string.Join(", ", g.Names.Select(n => $"\"{n}\""))).Append(",\n");
        }
        sb.Append("    )\n");
    }

    private static void AppendKotlinNames(StringBuilder sb, string name, IEnumerable<string> names)
    {
        sb.Append($"    internal val {name} = setOf(\n");
        sb.Append("        ").Append(string.Join(", ", names.Select(n => $"\"{n}\""))).Append(",\n");
        sb.Append("    )\n");
    }

    // ── Objective-C++ header — #included by BnYogaLayout.mm ──────────────────
    //
    // A HEADER, not a .inc: XcodeGen globs the whole BnHost folder as sources,
    // and an unknown extension lands in the build as a resource rather than
    // being ignored. `.h` is what the project already understands.

    public static string EmitObjCHeader(WireVocabulary v)
    {
        var sb = new StringBuilder();
        sb.Append(Banner("//"));
        sb.Append("""

            #ifndef BN_WIRE_VOCABULARY_G_H
            #define BN_WIRE_VOCABULARY_G_H


            """);

        AppendCArray(sb, "kYogaStyles", v.YogaStyles.Names);
        sb.Append('\n');
        AppendCArray(sb, "kScrollIgnoredContainerStyles", v.ScrollIgnoredContainerStyles.Names);
        sb.Append('\n');

        sb.Append("// Index IS the wire id.\n");
        sb.Append("static const char* const kNodeTypes[] = {\n");
        foreach (NodeType t in v.NodeTypes.Types)
            sb.Append($"    \"{t.WireName ?? v.NodeTypes.FallbackName}\", // {t.Id} = {t.Enum}\n");
        sb.Append("};\n\n");

        sb.Append("#endif // BN_WIRE_VOCABULARY_G_H\n");
        return sb.ToString();
    }

    private static void AppendCArray(StringBuilder sb, string name, IEnumerable<string> names)
    {
        sb.Append($"static const char* const {name}[] = {{\n");
        sb.Append("    ").Append(string.Join(", ", names.Select(n => $"\"{n}\""))).Append(",\n");
        sb.Append("};\n");
    }

    // ── Swift — BnHost ───────────────────────────────────────────────────────

    public static string EmitSwift(WireVocabulary v)
    {
        var sb = new StringBuilder();
        sb.Append(Banner("//"));
        sb.Append("""

            /// The wire vocabulary, generated from the manifest. The DATA only — BnFrameAdapter
            /// and BnWidgetMapper keep their own decode and routing code.
            enum BnWireVocabulary {

            """);

        AppendSwiftArray(sb, "yogaStyles", v.YogaStyles.Names);
        sb.Append('\n');
        AppendSwiftArray(sb, "visualStyles", v.VisualStyles.Names);
        sb.Append('\n');
        AppendSwiftArray(sb, "scrollIgnoredContainerStyles", v.ScrollIgnoredContainerStyles.Names);
        sb.Append('\n');
        AppendSwiftArray(sb, "measuredNodeTypes", v.MeasuredNodeTypes.Names);
        sb.Append('\n');

        sb.Append("    /// Index IS the wire id — decoded by indexing, so order is the contract.\n");
        sb.Append("    static let nodeTypes = [\n");
        foreach (NodeType t in v.NodeTypes.Types)
            sb.Append($"        \"{t.WireName ?? v.NodeTypes.FallbackName}\", // {t.Id} = {t.Enum}\n");
        sb.Append("    ]\n");

        sb.Append("}\n");
        return sb.ToString();
    }

    private static void AppendSwiftArray(StringBuilder sb, string name, IEnumerable<string> names)
    {
        sb.Append($"    static let {name} = [\n");
        sb.Append("        ").Append(string.Join(", ", names.Select(n => $"\"{n}\""))).Append(",\n");
        sb.Append("    ]\n");
    }

    // ── The deep-link vector tables — one manifest, three test suites ────────
    //
    // NOT a vocabulary but a TABLE OF CASES. The shells' URL parsers are
    // hand-written and cannot be generated; what CAN be generated is the set of
    // (url, route) pairs each of them must satisfy. A case added to the manifest
    // reaches .NET, the Android instrumented suite and BnHostTests at once —
    // which is the fix for #278, where the two parsers disagreed about a
    // multi-segment URL and no single place asserted otherwise.

    /// <summary>The C# vector table, for BlazorNative.Runtime.Tests.</summary>
    public static string EmitCSharpVectors(DeepLinkVectors v)
    {
        var sb = new StringBuilder();
        sb.Append(VectorsBanner("//"));
        sb.Append("""

            namespace BlazorNative.Runtime.Tests;

            /// <summary>The deep-link URL → route cases, generated from the manifest. The DATA
            /// only: each suite keeps its own parser and its own assertion loop — this table
            /// exists so the three cannot disagree about what a URL means.</summary>
            public static class BnDeepLinkVectors
            {
                /// <summary>Every case. <c>Route</c> is null when the URL must produce NO
                /// route — rejected, never coerced into some other route.</summary>
                public static readonly (string Url, string? Route)[] All =
                [

            """);

        foreach (DeepLinkVector c in v.Vectors)
        {
            sb.Append($"        // {c.Why}\n");
            sb.Append($"        (\"{c.Url}\", {(c.Route is null ? "null" : $"\"{c.Route}\"")}),\n");
        }

        sb.Append("    ];\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>The Kotlin vector table, for the Android instrumented suite.</summary>
    public static string EmitKotlinVectors(DeepLinkVectors v)
    {
        var sb = new StringBuilder();
        sb.Append(VectorsBanner("//"));
        sb.Append("""

            package io.blazornative.shell

            /**
             * The deep-link URL → route cases, generated from the manifest. The DATA only —
             * the shell keeps its own parser and this suite its own assertion loop.
             */
            object BnDeepLinkVectors {
                /** Every case. The second element is null when the URL must produce NO route. */
                val ALL: List<Pair<String, String?>> = listOf(

            """);

        foreach (DeepLinkVector c in v.Vectors)
        {
            sb.Append($"        // {c.Why}\n");
            sb.Append($"        Pair(\"{c.Url}\", {(c.Route is null ? "null" : $"\"{c.Route}\"")}),\n");
        }

        sb.Append("    )\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>The Swift vector table, for BnHostTests.</summary>
    public static string EmitSwiftVectors(DeepLinkVectors v)
    {
        var sb = new StringBuilder();
        sb.Append(VectorsBanner("//"));
        sb.Append("""

            /// The deep-link URL → route cases, generated from the manifest. The DATA only —
            /// BnDeepLink keeps its own parser and this suite its own assertion loop.
            enum BnDeepLinkVectors {
                /// Every case. `route` is nil when the URL must produce NO route.
                static let all: [(url: String, route: String?)] = [

            """);

        foreach (DeepLinkVector c in v.Vectors)
        {
            sb.Append($"        // {c.Why}\n");
            sb.Append($"        (\"{c.Url}\", {(c.Route is null ? "nil" : $"\"{c.Route}\"")}),\n");
        }

        sb.Append("    ]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>Every output this tool owns: repo-relative path → emitted content.
    /// The single list both the CLI and the byte-identity test drive, so a new
    /// output cannot be added to one and forgotten in the other.</summary>
    public static IReadOnlyDictionary<string, string> EmitAll(WireVocabulary v) => new Dictionary<string, string>
    {
        ["src/BlazorNative.Renderer/BnWireVocabulary.g.cs"] = EmitCSharp(v),
        ["src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/BnWireVocabulary.g.kt"] = EmitKotlin(v),
        ["templates/BlazorNative.Templates/content/BlazorNative.App/android/src/main/kotlin/io/blazornative/jni/BnWireVocabulary.g.kt"] = EmitKotlin(v),
        ["src/BlazorNative.Apple/BnHost/BnWireVocabulary.g.h"] = EmitObjCHeader(v),
        ["src/BlazorNative.Apple/BnHost/BnWireVocabulary.g.swift"] = EmitSwift(v),
    };

    /// <summary>Every vector-table output, repo-relative path → emitted content —
    /// the twin of <see cref="EmitAll"/>, and driven by the same two callers so a
    /// new suite cannot be wired into the CLI and forgotten in the pin.</summary>
    public static IReadOnlyDictionary<string, string> EmitAllVectors(DeepLinkVectors v) => new Dictionary<string, string>
    {
        ["tests/BlazorNative.Runtime.Tests/BnDeepLinkVectors.g.cs"] = EmitCSharpVectors(v),
        ["src/BlazorNative.Jni/src/androidTest/kotlin/io/blazornative/shell/BnDeepLinkVectors.g.kt"] = EmitKotlinVectors(v),
        ["src/BlazorNative.Apple/BnHostTests/BnDeepLinkVectors.g.swift"] = EmitSwiftVectors(v),
    };
}
