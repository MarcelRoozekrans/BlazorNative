using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorNative.Analyzers;

// ─────────────────────────────────────────────────────────────────────────────
// InteropBoundaryAnalyzer
//
// Correctness rules for the [UnmanagedCallersOnly] C-ABI boundary — the real
// constraint surface of the NativeAOT runtime (Exports.cs is the reference
// conformer; the JNA/Kotlin host binds to these exports by name).
//
//   BN0020 — exceptions must not escape an export. An exception crossing the
//            C-ABI boundary is undefined behavior under NativeAOT (process
//            abort on Android). Required shape: the entire method body is a
//            single top-level try with a catch-all (bare `catch` or
//            `catch (Exception)` without a filter) converting failure to a
//            return code, and no throw in ANY of the try's catch clauses.
//            Heuristic is deliberately syntactic + strict — expression-bodied
//            exports and unwrapped statements are flagged.
//   BN0021 — explicit C ABI on exports. Both `EntryPoint = "..."` and
//            `CallConvs = new[] { typeof(CallConvCdecl) }` must be present;
//            the JNA side binds by name to cdecl, so an implicit calling
//            convention or unnamed export is a silent ABI drift vector.
//            (Static + blittability are already compiler-enforced — CS8894
//            family — so they need no analyzer.)
//
// Full rule docs: docs/analyzers.md
// ─────────────────────────────────────────────────────────────────────────────

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InteropBoundaryAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "BlazorNative.Interop";
    private const string HelpBase = "https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/analyzers.md";

    public static readonly DiagnosticDescriptor BN0020_ExceptionEscape = new(
        id:                 "BN0020",
        title:              "Exceptions must not escape [UnmanagedCallersOnly] exports",
        messageFormat:      "'{0}' is [UnmanagedCallersOnly] but its body is not fully wrapped in a top-level try with a catch-all — an exception crossing the C-ABI boundary is undefined behavior under NativeAOT (process abort on Android). Wrap the entire body in try/catch (Exception) and convert failures to a return code.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri:        HelpBase + "#bn0020");

    public static readonly DiagnosticDescriptor BN0021_ExplicitCdecl = new(
        id:                 "BN0021",
        title:              "[UnmanagedCallersOnly] exports must pin EntryPoint and CallConvCdecl",
        messageFormat:      "'{0}' must declare an explicit C ABI: [UnmanagedCallersOnly(EntryPoint = \"...\", CallConvs = new[] {{ typeof(CallConvCdecl) }})]. The JNA host binds by name to cdecl, so an implicit calling convention or unnamed export is a silent ABI drift vector.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri:        HelpBase + "#bn0021");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(BN0020_ExceptionEscape, BN0021_ExplicitCdecl);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext ctx)
    {
        var method = (MethodDeclarationSyntax)ctx.Node;
        if (method.AttributeLists.Count == 0) return; // cheap syntactic bail

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(method, ctx.CancellationToken);
        if (symbol is null) return;

        AttributeData? uco = null;
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "UnmanagedCallersOnlyAttribute" } cls &&
                cls.ContainingNamespace?.ToDisplayString() == "System.Runtime.InteropServices")
            {
                uco = attribute;
                break;
            }
        }
        if (uco is null) return;

        // ── BN0021 — explicit EntryPoint + CallConvCdecl ─────────────────────
        var hasEntryPoint = false;
        var hasCdecl      = false;
        foreach (var named in uco.NamedArguments)
        {
            switch (named.Key)
            {
                case "EntryPoint":
                    hasEntryPoint = named.Value.Value is string { Length: > 0 };
                    break;
                case "CallConvs" when named.Value.Kind == TypedConstantKind.Array:
                    foreach (var conv in named.Value.Values)
                    {
                        if (conv.Value is ITypeSymbol { Name: "CallConvCdecl" })
                        {
                            hasCdecl = true;
                            break;
                        }
                    }
                    break;
            }
        }

        if (!hasEntryPoint || !hasCdecl)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                BN0021_ExplicitCdecl, method.Identifier.GetLocation(), symbol.Name));
        }

        // ── BN0020 — no exception may escape the export ──────────────────────
        if (!BodyIsFullyWrapped(method, ctx.SemanticModel, ctx.CancellationToken))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                BN0020_ExceptionEscape, method.Identifier.GetLocation(), symbol.Name));
        }
    }

    /// <summary>
    /// The required BN0020 shape: the method body is a block whose single
    /// top-level statement is a try with (a) at least one catch-all clause
    /// (bare `catch` or unfiltered `catch (Exception)`) and (b) no throw in
    /// ANY of its catch clauses — a rethrow from a specific catch escapes the
    /// boundary; a catch-all sibling does not intercept it. Expression-bodied
    /// exports never conform (deliberately strict heuristic — see
    /// docs/analyzers.md#bn0020).
    /// </summary>
    private static bool BodyIsFullyWrapped(
        MethodDeclarationSyntax method, SemanticModel semanticModel, CancellationToken ct)
    {
        if (method.ExpressionBody is not null) return false;
        if (method.Body is null) return true; // extern / no body — nothing can escape from here

        if (method.Body.Statements.Count != 1 ||
            method.Body.Statements[0] is not TryStatementSyntax tryStatement)
            return false;

        var hasCatchAll = false;
        foreach (var catchClause in tryStatement.Catches)
        {
            if (ContainsThrow(catchClause))
                return false;
            hasCatchAll |= IsCatchAllShape(catchClause, semanticModel, ct);
        }
        return hasCatchAll;
    }

    private static bool IsCatchAllShape(CatchClauseSyntax catchClause, SemanticModel semanticModel, CancellationToken ct)
    {
        // A filtered catch can decline the exception — not a catch-all.
        if (catchClause.Filter is not null) return false;

        // Bare `catch` is a catch-all; a declared type must be System.Exception.
        if (catchClause.Declaration is null) return true;
        var caughtType = semanticModel.GetTypeInfo(catchClause.Declaration.Type, ct).Type;
        return caughtType?.ToDisplayString() == "System.Exception";
    }

    private static bool ContainsThrow(CatchClauseSyntax catchClause)
    {
        // A throw inside any catch clause (rethrow or fresh) still crosses the
        // boundary. Lambdas / local functions inside the catch run in their own
        // frame — do not descend into them.
        foreach (var node in catchClause.Block.DescendantNodes(
                     descendIntoChildren: static n =>
                         n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax))
        {
            if (node is ThrowStatementSyntax or ThrowExpressionSyntax)
                return true;
        }
        return false;
    }
}
