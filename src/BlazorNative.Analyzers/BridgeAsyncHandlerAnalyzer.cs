using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorNative.Analyzers;

// ─────────────────────────────────────────────────────────────────────────────
// BridgeAsyncHandlerAnalyzer
//
// Fires BN0014 when an async lambda / async method is registered against
// IMobileBridge.NativeEvents. Handlers run synchronously inside a native
// callback window (DevHost multicast is sync; the production lane is the
// single-threaded dispatch lane) — an async handler compiles to `async void`,
// becoming fire-and-forget: its continuation escapes the callback window and
// its exceptions vanish. This analyzer is the compile-time gate.
//
// Note: NativeEvents' own redesign is a ledgered open item (NativeShellBridge
// currently stubs it no-op) — the rule guards the surviving contract.
//
// Originally Phase 2.0 (docs/plans/2026-05-25-phase-2.0-design.md); reworded
// off the WASI premise in Phase 4.1. Full rule docs: docs/analyzers.md
// ─────────────────────────────────────────────────────────────────────────────

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BridgeAsyncHandlerAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "BlazorNative.Interop";

    public static readonly DiagnosticDescriptor BN0014_AsyncBridgeHandler = new(
        id:                 "BN0014",
        title:              "Bridge event handlers must complete synchronously",
        messageFormat:      "Async handlers cannot be registered against IMobileBridge.NativeEvents — handlers run synchronously inside a native callback window, so an async handler becomes fire-and-forget: its continuation escapes the window and its exceptions vanish. Use a synchronous handler and dispatch async work explicitly (e.g. Dispatcher.InvokeAsync).",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri:        "https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/analyzers.md#bn0014");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(BN0014_AsyncBridgeHandler);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // `+=` event subscription expressions
        context.RegisterSyntaxNodeAction(AnalyzeAddAssignment, SyntaxKind.AddAssignmentExpression);
    }

    private static void AnalyzeAddAssignment(SyntaxNodeAnalysisContext ctx)
    {
        var node = (AssignmentExpressionSyntax)ctx.Node;

        // LHS must be a member access targeting IMobileBridge.NativeEvents.
        if (!IsBridgeNativeEventsAccess(ctx.SemanticModel, node.Left, ctx.CancellationToken))
            return;

        // RHS must be async (lambda with async keyword, OR method symbol marked async).
        var isAsync = IsAsyncHandler(ctx.SemanticModel, node.Right, ctx.CancellationToken);
        if (!isAsync) return;

        ctx.ReportDiagnostic(Diagnostic.Create(BN0014_AsyncBridgeHandler, node.Right.GetLocation()));
    }

    private static bool IsBridgeNativeEventsAccess(SemanticModel sm, SyntaxNode lhs, System.Threading.CancellationToken ct)
    {
        var symbol = sm.GetSymbolInfo(lhs, ct).Symbol;
        if (symbol is not IEventSymbol evt) return false;
        if (evt.Name != "NativeEvents") return false;

        // The event must be declared on IMobileBridge (or a type that implements it
        // and forwards via interface implementation). Walk up the containing type
        // chain and look for IMobileBridge.
        var containingType = evt.ContainingType;
        if (containingType is null) return false;
        if (containingType.Name == "IMobileBridge" &&
            containingType.ContainingNamespace?.ToDisplayString() == "BlazorNative.Core")
            return true;

        // Check if containingType IS or IMPLEMENTS BlazorNative.Core.IMobileBridge.
        foreach (var iface in containingType.AllInterfaces)
        {
            if (iface.Name == "IMobileBridge" &&
                iface.ContainingNamespace?.ToDisplayString() == "BlazorNative.Core")
                return true;
        }
        return false;
    }

    private static bool IsAsyncHandler(SemanticModel sm, SyntaxNode rhs, System.Threading.CancellationToken ct)
    {
        // Case 1: anonymous async lambda — async e => ... / async (e) => ...
        if (rhs is LambdaExpressionSyntax lambda &&
            lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
            return true;

        // Case 2: method reference — Bridge.NativeEvents += SomeMethod;
        // where SomeMethod is declared async (IMethodSymbol.IsAsync).
        var symbol = sm.GetSymbolInfo(rhs, ct).Symbol;
        if (symbol is IMethodSymbol method && method.IsAsync)
            return true;

        return false;
    }
}
