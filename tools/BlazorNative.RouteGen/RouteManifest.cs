using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorNative.RouteGen;

// ─────────────────────────────────────────────────────────────────────────────
// RouteManifest — Phase 11.0 (M11 DoD #1), PIVOTED at Gate A to Roslyn SOURCE
// analysis. The extraction + serialization behind the deep-link route codegen.
//
// WHY SOURCE, NOT THE ASSEMBLY (the Gate-A fix). The first cut LOADED the built
// app assembly, ran its [ModuleInitializer], and reflected the framework's
// PageManifest. That could not survive CI: the app is published per-RID, and a
// linux-bionic-arm64 managed dll cannot be loaded into the x64 build host —
// "The assembly architecture is not compatible with the current process
// architecture". Real Android devices are arm64, so this MUST work for arm64.
// The design's named fallback (docs/plans/2026-07-20-phase-11.0-design.md,
// "Considered + rejected" / "Risks") is source analysis: parse the app's own
// SOURCE for the `BlazorNativePage.Routed<T>(route, name)` registrations. It
// loads NOTHING — no assembly, no RID, no arch — so it produces byte-identical
// output for win-x64, linux-bionic-x64 AND linux-bionic-arm64. That
// arch-independence is the whole point of the pivot.
//
// GENERAL BY CONSTRUCTION: it matches the framework's `Routed<T>` registration
// SHAPE, not a convention-named array — it works for any consumer app, not just
// SampleAppPages.
//
// LITERALS ONLY (the documented constraint — the design ledger). Both string
// arguments of a `Routed<T>(...)` call must be compile-time string literals so
// the codegen can read them from source without binding a semantic model. There
// is ONE framework-owned exception: `BlazorNativeApp.DefaultRoute` (the const
// "/") is resolved to "/", because the framework's own convention writes the
// default row as `Routed<T>(BlazorNativeApp.DefaultRoute, "…")` (SampleAppPages
// AND the template's AppPages both do). Any OTHER non-literal route/name — a
// variable, a user const, an interpolated or computed string — is something the
// generator cannot see through, so it ERRORS with the file+line rather than
// silently dropping the row. The drift test guards the "/" resolution:
// SampleAppPages.All reads the real BlazorNativeApp.DefaultRoute at runtime, so
// if that constant ever stopped being "/", the pair-for-pair pin would redden.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One routed page: the deep-link route and the mount-registry
/// component name it resolves to.</summary>
public readonly record struct RoutedPage(string Route, string Name);

/// <summary>Raised when a <c>Routed&lt;T&gt;(...)</c> call has a route or name
/// argument the source-analysis codegen cannot read (a non-literal that is not
/// the framework's <c>DefaultRoute</c> constant). Carries the file+line so the
/// build can point the author at the exact call to fix.</summary>
public sealed class NonLiteralRouteArgumentException(string message) : Exception(message);

public static class RouteManifest
{
    // The framework's default-route constant (BlazorNativeApp.DefaultRoute = "/").
    // The one non-literal the codegen resolves — see the file header.
    private const string DefaultRouteConstName = "DefaultRoute";
    private const string DefaultRouteValue = "/";

    /// <summary>Parses the app's SOURCE files, finds every
    /// <c>BlazorNativePage.Routed&lt;T&gt;(route, name)</c> registration, and
    /// returns the routed rows in declaration order (the "/" default row
    /// included). Loads no assembly — arch/RID-independent. Files are visited in
    /// ordinal path order, and each file's calls in source-position order, so a
    /// single manifest file (the framework convention) yields exactly the array's
    /// order. Skips <c>Named&lt;T&gt;(...)</c> (unrouted). Throws
    /// <see cref="NonLiteralRouteArgumentException"/> on a non-literal argument,
    /// and <see cref="InvalidOperationException"/> if no routed row is found.</summary>
    public static IReadOnlyList<RoutedPage> Extract(IEnumerable<string> sourceFiles)
    {
        var routed = new List<RoutedPage>();

        foreach (string file in sourceFiles.OrderBy(f => f, StringComparer.Ordinal))
        {
            if (!File.Exists(file)) continue;

            SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
            SyntaxNode root = tree.GetRoot();

            foreach (InvocationExpressionSyntax call in
                     root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!IsRoutedCall(call)) continue;

                SeparatedSyntaxList<ArgumentSyntax> args = call.ArgumentList.Arguments;
                string route = ResolveStringArgument(args[0], file, "route");
                string name = ResolveStringArgument(args[1], file, "name");
                routed.Add(new RoutedPage(route, name));
            }
        }

        if (routed.Count == 0)
            throw new InvalidOperationException(
                "the app's source declared no ROUTED pages — there is no deep-link map to "
                + "generate. A routed app declares at least the \"/\" row "
                + "(BlazorNativePage.Routed<T>(BlazorNativeApp.DefaultRoute, \"…\")). "
                + "Confirm the source list passed to RouteGen includes the app's page manifest.");

        return routed;
    }

    /// <summary>True for a <c>BlazorNativePage.Routed&lt;T&gt;(a, b)</c> call —
    /// the method name is <c>Routed</c>, with exactly one generic type argument
    /// and exactly two arguments, invoked on <c>BlazorNativePage</c> (or bare,
    /// via <c>using static</c>). Deliberately excludes <c>Named&lt;T&gt;(...)</c>
    /// (one arg, unrouted) and any unrelated <c>Routed</c>.</summary>
    private static bool IsRoutedCall(InvocationExpressionSyntax call)
    {
        GenericNameSyntax? generic;
        bool receiverOk;

        switch (call.Expression)
        {
            // BlazorNativePage.Routed<T>(…) — the normal form.
            case MemberAccessExpressionSyntax { Name: GenericNameSyntax g } mae:
                generic = g;
                receiverOk = ReceiverIsBlazorNativePage(mae.Expression);
                break;
            // Routed<T>(…) — a `using static …BlazorNativePage;` form.
            case GenericNameSyntax g:
                generic = g;
                receiverOk = true;
                break;
            default:
                return false;
        }

        return receiverOk
            && generic.Identifier.Text == "Routed"
            && generic.TypeArgumentList.Arguments.Count == 1
            && call.ArgumentList.Arguments.Count == 2;
    }

    /// <summary>The receiver of a member-access <c>Routed</c> call must name
    /// <c>BlazorNativePage</c> — accepts the bare identifier and any qualified
    /// form (<c>BlazorNative.Runtime.BlazorNativePage</c>).</summary>
    private static bool ReceiverIsBlazorNativePage(ExpressionSyntax receiver) => receiver switch
    {
        IdentifierNameSyntax id => id.Identifier.Text == "BlazorNativePage",
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text == "BlazorNativePage",
        _ => false,
    };

    /// <summary>Reads a compile-time string from a <c>Routed</c> argument: a
    /// string literal, or the framework's <c>DefaultRoute</c> constant ("/").
    /// Anything else is a non-literal the codegen cannot see through — it throws
    /// <see cref="NonLiteralRouteArgumentException"/> naming the file+line so the
    /// build fails loudly instead of dropping the row.</summary>
    private static string ResolveStringArgument(ArgumentSyntax arg, string file, string kind)
    {
        ExpressionSyntax expr = arg.Expression;

        // 1) a plain string literal — the common case for every route and name.
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
            return lit.Token.ValueText;

        // 2) the framework's DefaultRoute constant (BlazorNativeApp.DefaultRoute
        //    or a bare DefaultRoute via `using static`) — resolves to "/".
        if (expr is MemberAccessExpressionSyntax { Name.Identifier.Text: DefaultRouteConstName }
            || expr is IdentifierNameSyntax { Identifier.Text: DefaultRouteConstName })
            return DefaultRouteValue;

        // 3) anything else — a variable, a user const, an interpolated/computed
        //    string. The source-analysis codegen cannot read it. Fail LOUD.
        FileLinePositionSpan span = arg.GetLocation().GetLineSpan();
        throw new NonLiteralRouteArgumentException(
            $"{file}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): "
            + $"the {kind} argument of a BlazorNativePage.Routed<T>(...) call is not a string "
            + $"literal (found '{expr}'). The deep-link route codegen reads the app's SOURCE, so "
            + "route/name arguments must be string literals — the only accepted non-literal is the "
            + "framework's BlazorNativeApp.DefaultRoute (\"/\") for the default row. Replace the "
            + $"{kind} with a literal, or move the value to a literal at the registration site.");
    }

    /// <summary>Serializes the routed rows to the flat JSON the shells read at
    /// Intent-parse time: <c>{ "/route": "ComponentName", ... }</c>, the "/"
    /// default row included. Stable, 2-space indented, keys in declaration order.</summary>
    public static string ToJson(IReadOnlyList<RoutedPage> routed)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        for (int i = 0; i < routed.Count; i++)
        {
            sb.Append("  ").Append(JsonEncode(routed[i].Route))
              .Append(": ").Append(JsonEncode(routed[i].Name));
            sb.Append(i == routed.Count - 1 ? "\n" : ",\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string JsonEncode(string s) => JsonSerializer.Serialize(s);
}
