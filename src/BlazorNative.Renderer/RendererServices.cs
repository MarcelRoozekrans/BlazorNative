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

    private sealed class ConsoleLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            Console.Error.WriteLine($"[BlazorNative.Renderer/{category}] [{logLevel}] {formatter(state, exception)}");
            if (exception is not null) Console.Error.WriteLine(exception);
        }
    }
}

