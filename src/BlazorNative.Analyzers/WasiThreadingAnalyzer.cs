using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorNative.Analyzers;

// ─────────────────────────────────────────────────────────────────────────────
// WasiThreadingAnalyzer
//
// Detects threading APIs that are incompatible with the WASI runtime and
// flags them with actionable error messages at compile time.
//
// WASI Preview 1 (targeted by .NET 9 wasi-wasm) has NO thread support.
// These APIs will either throw PlatformNotSupportedException at runtime
// or silently corrupt state. Catching them at compile time is far better.
//
// Diagnostics:
//   BN0001 — new Thread(...)           → use async/await
//   BN0002 — Task.Run(...)             → use await directly
//   BN0003 — Parallel.For/ForEach      → use sequential or chunked async
//   BN0004 — Thread.Sleep(...)         → use await Task.Delay(...)
//   BN0005 — Monitor/Mutex/Semaphore   → not needed; WASI is single-threaded
//   BN0006 — [ThreadStatic]            → use instance fields instead
// ─────────────────────────────────────────────────────────────────────────────

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WasiThreadingAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "BlazorNative.WasiCompatibility";

    // ── Diagnostic descriptors ────────────────────────────────────────────────

    public static readonly DiagnosticDescriptor BN0001_NewThread = new(
        id:                 "BN0001",
        title:              "Thread creation not supported on WASI",
        messageFormat:      "'new Thread(...)' will fail on WASI. Use 'async/await' and cooperative scheduling instead.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri:        "https://github.com/your-org/BlazorNative/docs/wasi-threading.md");

    public static readonly DiagnosticDescriptor BN0002_TaskRun = new(
        id:                 "BN0002",
        title:              "Task.Run not supported on WASI",
        messageFormat:      "'Task.Run(...)' spawns a thread pool thread which does not exist on WASI. Await the work directly or restructure as async.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri:        "https://github.com/your-org/BlazorNative/docs/wasi-threading.md");

    public static readonly DiagnosticDescriptor BN0003_Parallel = new(
        id:                 "BN0003",
        title:              "Parallel APIs not supported on WASI",
        messageFormat:      "'Parallel.{0}' uses threads and will fail on WASI. Use sequential iteration or chunked async processing.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri:        "https://github.com/your-org/BlazorNative/docs/wasi-threading.md");

    public static readonly DiagnosticDescriptor BN0004_ThreadSleep = new(
        id:                 "BN0004",
        title:              "Thread.Sleep not supported on WASI",
        messageFormat:      "'Thread.Sleep(...)' blocks the cooperative scheduler on WASI. Use 'await Task.Delay(...)' instead.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri:        "https://github.com/your-org/BlazorNative/docs/wasi-threading.md");

    public static readonly DiagnosticDescriptor BN0005_SyncPrimitive = new(
        id:                 "BN0005",
        title:              "Synchronisation primitive unnecessary on WASI",
        messageFormat:      "'{0}' is a multi-threading primitive. WASI is single-threaded — remove it, or replace with SemaphoreSlim for async guards.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri:        "https://github.com/your-org/BlazorNative/docs/wasi-threading.md");

    public static readonly DiagnosticDescriptor BN0006_ThreadStatic = new(
        id:                 "BN0006",
        title:              "[ThreadStatic] not supported on WASI",
        messageFormat:      "[ThreadStatic] has no effect on WASI (single thread). Use an instance field or scoped DI service instead.",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri:        "https://github.com/your-org/BlazorNative/docs/wasi-threading.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(BN0001_NewThread, BN0002_TaskRun, BN0003_Parallel,
                              BN0004_ThreadSleep, BN0005_SyncPrimitive, BN0006_ThreadStatic);

    // ── Analysis ──────────────────────────────────────────────────────────────

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Invocation expressions: new Thread(), Task.Run(), Parallel.For(), Thread.Sleep()
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation,   SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation,       SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAttribute,        SyntaxKind.Attribute);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext ctx)
    {
        var node = (ObjectCreationExpressionSyntax)ctx.Node;
        var type = ctx.SemanticModel.GetTypeInfo(node).Type;
        if (type is null) return;

        var fullName = type.ToDisplayString();

        if (fullName is "System.Threading.Thread")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0001_NewThread, node.GetLocation()));
            return;
        }

        if (fullName is "System.Threading.Mutex"
                     or "System.Threading.Monitor"
                     or "System.Threading.ReaderWriterLock"
                     or "System.Threading.ReaderWriterLockSlim")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                BN0005_SyncPrimitive, node.GetLocation(),
                type.Name));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var node = (InvocationExpressionSyntax)ctx.Node;
        if (node.Expression is not MemberAccessExpressionSyntax member) return;

        var symbol = ctx.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType?.ToDisplayString();
        var methodName     = symbol.Name;

        // Task.Run
        if (containingType is "System.Threading.Tasks.Task" && methodName is "Run")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0002_TaskRun, node.GetLocation()));
            return;
        }

        // Thread.Sleep
        if (containingType is "System.Threading.Thread" && methodName is "Sleep")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0004_ThreadSleep, node.GetLocation()));
            return;
        }

        // Parallel.For / ForEach / Invoke
        if (containingType is "System.Threading.Tasks.Parallel"
            && methodName is "For" or "ForEach" or "ForEachAsync" or "Invoke")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(BN0003_Parallel, node.GetLocation(), methodName));
        }
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext ctx)
    {
        var node   = (AttributeSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetTypeInfo(node).Type;
        if (symbol?.ToDisplayString() is "System.ThreadStaticAttribute")
            ctx.ReportDiagnostic(Diagnostic.Create(BN0006_ThreadStatic, node.GetLocation()));
    }
}
