using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BlazorNative.Core;

namespace BlazorNative.Renderer;

// ─────────────────────────────────────────────────────────────────────────────
// ServiceCollectionExtensions
// ─────────────────────────────────────────────────────────────────────────────

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BlazorNative headless renderer and all required services.
    /// Call this in every host: the NativeAOT runtime's HostSession and any
    /// test/dev harness composition root.
    /// </summary>
    public static IServiceCollection AddBlazorNativeRenderer(
        this IServiceCollection services)
        => services.AddBlazorNativeRendererServices();
}

// ─────────────────────────────────────────────────────────────────────────────
// NativeRendererLoggerFactory
// Minimal ILoggerFactory that needs no MEL host — works in the trimmed
// NativeAOT runtime and plain-CLR test harnesses alike.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class NativeRendererLoggerFactory : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider) { }
    public ILogger CreateLogger(string categoryName) => new ConsoleLogger(categoryName, this);

    /// <summary>18.2 (#500): the renderer this factory belongs to, assigned in its constructor.
    /// Loggers read it at log time.</summary>
    internal NativeRenderer? Renderer { get; set; }

    private const string BlazorRendererCategory = "Microsoft.AspNetCore.Components.RenderTree.Renderer";
    public void Dispose() { }

    /// <summary>Phase 11.4 Gate A (#155): this used to BE the framework's entire
    /// level concept — nine hard-coded characters, <c>logLevel &gt;= LogLevel.Warning</c>,
    /// not configurable and not build-gated. It now DELEGATES to <see cref="BnLog"/>,
    /// which is what makes Blazor's own <c>ILogger</c> calls and the framework's own
    /// diagnostics share ONE throttle — the "one seam" DoD #6 asks for.</summary>
    private sealed class ConsoleLogger(string category, NativeRendererLoggerFactory factory) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            // 18.2 (#500): Blazor only calls Log for an enabled level, so the renderer category
            // reports Debug as enabled while the renderer wants the RenderingComponent event.
            // Log still filters every event through BnLog before writing.
            if (logLevel == LogLevel.Debug && category == BlazorRendererCategory
                && factory.Renderer?.WantsRenderLog == true)
                return true;
            return BnLog.IsEnabled(ToBnLevel(logLevel));
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // 18.2 (#500): capture the id first, then fall through to the unchanged BnLog path.
            // Never throws: a malformed state is simply not a signal.
            if (eventId.Name == "RenderingComponent" && category == BlazorRendererCategory
                && factory.Renderer is { } renderer
                && state is IReadOnlyList<KeyValuePair<string, object?>> entries)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Key == "ComponentId" && entries[i].Value is int id)
                    {
                        renderer.NoteRenderingComponent(id);
                        break;
                    }
                }
            }

            BnLogLevel level = ToBnLevel(logLevel);
            if (!BnLog.IsEnabled(level)) return;

            string message = $"[{logLevel}] {formatter(state, exception)}";
            if (exception is not null)
                message += $" — {BnLog.FormatException(exception, BnLog.Level)}";

            BnLog.Write(level, $"BlazorNative.Renderer/{category}", message);
        }

        /// <summary>The MEL → BnLogLevel map of the design's §3.1 table. <c>None</c>
        /// is the one level that must never be emitted, so it maps to
        /// <see cref="BnLogLevel.Unset"/>, which <see cref="BnLog.IsEnabled"/>
        /// rejects unconditionally.</summary>
        private static BnLogLevel ToBnLevel(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Critical => BnLogLevel.Error,
            LogLevel.Error => BnLogLevel.Error,
            LogLevel.Warning => BnLogLevel.Warn,
            LogLevel.Information => BnLogLevel.Info,
            LogLevel.Debug => BnLogLevel.Debug,
            LogLevel.Trace => BnLogLevel.Verbose,
            _ => BnLogLevel.Unset,   // LogLevel.None — never enabled
        };
    }
}

