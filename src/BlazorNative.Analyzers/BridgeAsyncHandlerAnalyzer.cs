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
// IMobileBridge.NativeEvents. Bridge event handlers must complete synchronously
// on Mono-WASI — any path that suspends (await on an incomplete Task) trips
// Task.InternalWaitCore PlatformNotSupportedException.
//
// Action<NativeEvent> alone doesn't enforce this — async lambdas compile as
// `async void` and silently reintroduce the trap. This analyzer is the
// compile-time gate.
//
// See docs/plans/2026-05-25-phase-2.0-design.md.
// ─────────────────────────────────────────────────────────────────────────────

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BridgeAsyncHandlerAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "BlazorNative.WasiCompatibility";

    public static readonly DiagnosticDescriptor BN0014_AsyncBridgeHandler = new(
        id:                 "BN0014",
        title:              "Bridge event handlers must complete synchronously on Mono-WASI",
        messageFormat:      "Async handlers cannot be registered against IMobileBridge.NativeEvents — Mono-WASI single-threaded scheduler cannot resume await continuations from unmanaged callbacks. Use a synchronous lambda and fire-and-forget async work via Dispatcher.InvokeAsync.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri:        "https://github.com/your-org/BlazorNative/docs/wasi-async.md");

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
