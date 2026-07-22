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
    public ILogger CreateLogger(string categoryName) => new ConsoleLogger(categoryName);
    public void Dispose() { }

    /// <summary>Phase 11.4 Gate A (#155): this used to BE the framework's entire
    /// level concept — nine hard-coded characters, <c>logLevel &gt;= LogLevel.Warning</c>,
    /// not configurable and not build-gated. It now DELEGATES to <see cref="BnLog"/>,
    /// which is what makes Blazor's own <c>ILogger</c> calls and the framework's own
    /// diagnostics share ONE throttle — the "one seam" DoD #6 asks for.</summary>
    private sealed class ConsoleLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => BnLog.IsEnabled(ToBnLevel(logLevel));

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
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

