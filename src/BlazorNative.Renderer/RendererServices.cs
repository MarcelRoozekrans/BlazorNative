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
    /// Call this in both DevHost and the WASI entry point.
    /// </summary>
    public static IServiceCollection AddBlazorNativeRenderer(
        this IServiceCollection services)
    {
        services.AddScoped<NativeRenderer>();
        services.AddScoped<IComponentActivator, DefaultComponentActivator>();
        return services;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// NativeRendererLoggerFactory
// Minimal ILoggerFactory that works under WASI (no MEL host required).
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

// ─────────────────────────────────────────────────────────────────────────────
// WebEventData shim
// Wraps native UI event dispatch back into Blazor's event system.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed record WebEventData(ulong EventHandlerId, EventArgs EventArgs);
