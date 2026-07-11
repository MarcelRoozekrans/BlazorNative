using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorNative.Analyzers;

// ─────────────────────────────────────────────────────────────────────────────
// MobilePolicyAnalyzer
//
// Advisory architecture policy for BlazorNative app code running on the
// NativeAOT runtime inside a mobile shell. These are not "the API will throw"
// rules (that was the retired WASI era) — they flag patterns that degrade or
// bypass the mobile host contract:
//
//   BN0004 — Thread.Sleep         → blocks a runtime thread; on the single
//                                   Kotlin dispatch lane this stalls every
//                                   queued frame/event. Warning.
//   BN0010 — System.Net.Sockets.* → bypasses the host bridge; network I/O
//                                   should ride IMobileBridge.FetchAsync /
//                                   AddBlazorNativeHttp. Warning.
//   BN0011 — new HttpClient()     → parameterless ctor only: the default
//                                   socket handler bypasses BridgeHttpHandler.
//                                   new HttpClient(handler) stays legal. Warning.
//   BN0013 — System.Diagnostics.Process → unsupported in Android app
//                                   sandboxes. Error (correctness).
//
// Full rule docs: docs/analyzers.md
// ─────────────────────────────────────────────────────────────────────────────

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MobilePolicyAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "BlazorNative.MobilePolicy";
    private const string HelpBase = "https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/analyzers.md";

    public static readonly DiagnosticDescriptor BN0004_ThreadSleep = new(
        id:                 "BN0004",
        title:              "Thread.Sleep blocks a runtime thread",
        messageFormat:      "'Thread.Sleep(...)' blocks a runtime thread — on the single Kotlin dispatch lane this stalls every queued frame and event. Prefer 'await Task.Delay(...)'.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri:        HelpBase + "#bn0004");

    public static readonly DiagnosticDescriptor BN0010_Sockets = new(
        id:                 "BN0010",
        title:              "Raw socket APIs bypass the host bridge",
        messageFormat:      "'{0}' uses raw System.Net.Sockets APIs, bypassing the host bridge. Route network I/O through 'IMobileBridge.FetchAsync' or the HttpClient registered by 'AddBlazorNativeHttp()' so the host owns permissions, proxies, and lifecycle.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri:        HelpBase + "#bn0010");

    public static readonly DiagnosticDescriptor BN0011_HttpClient = new(
        id:                 "BN0011",
        title:              "Parameterless HttpClient bypasses the bridge handler",
        messageFormat:      "'new HttpClient()' uses the default socket handler, bypassing the host bridge. Prefer the HttpClient injected by 'AddBlazorNativeHttp()'; constructing over an explicit handler (e.g. BridgeHttpHandler) stays legal.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri:        HelpBase + "#bn0011");

    public static readonly DiagnosticDescriptor BN0013_Process = new(
        id:                 "BN0013",
        title:              "Process APIs are unsupported in the app sandbox",
        messageFormat:      "'{0}' — System.Diagnostics.Process is unsupported inside Android app sandboxes. A BlazorNative app cannot spawn or manage OS processes.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri:        HelpBase + "#bn0013");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(BN0004_ThreadSleep, BN0010_Sockets, BN0011_HttpClient, BN0013_Process);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation,     SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext ctx)
    {
        var node = (ObjectCreationExpressionSyntax)ctx.Node;
        var type = ctx.SemanticModel.GetTypeInfo(node, ctx.CancellationToken).Type;
        if (type is null) return;

        var fullName = type.ToDisplayString();

        if (fullName.StartsWith("System.Net.Sockets.", StringComparison.Ordinal))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0010_Sockets, node.GetLocation(), type.Name));
            return;
        }

        // BN0011 is narrowed to the parameterless ctor only: `new HttpClient()`
        // uses the default socket handler; `new HttpClient(handler)` stays legal
        // (BlazorNative.Http itself constructs over BridgeHttpHandler).
        if (fullName is "System.Net.Http.HttpClient"
            && (node.ArgumentList is null || node.ArgumentList.Arguments.Count == 0))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0011_HttpClient, node.GetLocation()));
            return;
        }

        if (fullName.StartsWith("System.Diagnostics.Process", StringComparison.Ordinal))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0013_Process, node.GetLocation(), type.Name));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var node   = (InvocationExpressionSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetSymbolInfo(node, ctx.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType?.ToDisplayString();
        if (containingType is null) return;

        var methodName = symbol.Name;

        // Thread.Sleep
        if (containingType is "System.Threading.Thread" && methodName is "Sleep")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0004_ThreadSleep, node.GetLocation()));
            return;
        }

        if (containingType.StartsWith("System.Net.Sockets.", StringComparison.Ordinal))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                BN0010_Sockets, node.GetLocation(), $"{containingType}.{methodName}"));
            return;
        }

        if (containingType.StartsWith("System.Diagnostics.Process", StringComparison.Ordinal))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                BN0013_Process, node.GetLocation(), $"{containingType}.{methodName}"));
        }
    }
}
