using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorNative.Analyzers;

// ─────────────────────────────────────────────────────────────────────────────
// WasiBclGapsAnalyzer
//
// Flags BCL APIs known to throw PlatformNotSupportedException on WASI.
// These are gaps that don't have automatic fixes — the developer needs to
// be aware and use the BlazorNative alternatives.
//
// Diagnostics:
//   BN0010 — System.Net.Sockets.*         → use IMobileBridge.FetchAsync / BridgeHttpHandler
//   BN0011 — System.Net.Http.HttpClient   → use AddBlazorNativeHttp() DI registration
//   BN0012 — System.IO.File.*             → use IMobileBridge storage APIs
//   BN0013 — Environment.Exit()           → no-op on WASI, use CancellationToken
//   BN0014 — System.Diagnostics.Process   → not available on WASI
// ─────────────────────────────────────────────────────────────────────────────

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WasiBclGapsAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "BlazorNative.WasiCompatibility";

    public static readonly DiagnosticDescriptor BN0010_Sockets = new(
        id:                 "BN0010",
        title:              "Socket APIs not available on WASI",
        messageFormat:      "'{0}' uses sockets which are not available on WASI. Use 'IMobileBridge.FetchAsync' or inject HttpClient via 'AddBlazorNativeHttp()'.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BN0011_HttpClient = new(
        id:                 "BN0011",
        title:              "Direct HttpClient construction bypasses bridge on WASI",
        messageFormat:      "'new HttpClient()' with default handler uses sockets and fails on WASI. Inject HttpClient via DI after calling 'services.AddBlazorNativeHttp()'.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BN0012_FileIO = new(
        id:                 "BN0012",
        title:              "File I/O is sandboxed on WASI",
        messageFormat:      "'{0}' accesses the filesystem. On WASI, only pre-opened directories are accessible. Consider using 'IMobileBridge' storage APIs for key/value persistence.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BN0013_Process = new(
        id:                 "BN0013",
        title:              "System.Diagnostics.Process not available on WASI",
        messageFormat:      "'{0}' is not available on WASI. Process management is handled by the native shell host.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(BN0010_Sockets, BN0011_HttpClient, BN0012_FileIO, BN0013_Process);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation,     SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext ctx)
    {
        var node     = (ObjectCreationExpressionSyntax)ctx.Node;
        var type     = ctx.SemanticModel.GetTypeInfo(node).Type;
        if (type is null) return;

        var fullName = type.ToDisplayString();

        if (fullName.StartsWith("System.Net.Sockets."))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0010_Sockets, node.GetLocation(), type.Name));
            return;
        }

        if (fullName is "System.Net.Http.HttpClient")
        {
            // Only warn if constructed directly (not injected)
            ctx.ReportDiagnostic(Diagnostic.Create(BN0011_HttpClient, node.GetLocation()));
            return;
        }

        if (fullName.StartsWith("System.Diagnostics.Process"))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0013_Process, node.GetLocation(), type.Name));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var node   = (InvocationExpressionSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType?.ToDisplayString();
        var methodName     = symbol.Name;

        if (containingType?.StartsWith("System.Net.Sockets.") == true)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                BN0010_Sockets, node.GetLocation(), $"{containingType}.{methodName}"));
            return;
        }

        if (containingType is "System.IO.File" or "System.IO.Directory" or "System.IO.Path")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                BN0012_FileIO, node.GetLocation(), $"{containingType}.{methodName}"));
            return;
        }

        if (containingType?.StartsWith("System.Diagnostics.Process") == true)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                BN0013_Process, node.GetLocation(), $"{containingType}.{methodName}"));
        }
    }
}
