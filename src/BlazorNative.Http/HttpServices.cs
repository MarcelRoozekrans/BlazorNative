using Microsoft.Extensions.DependencyInjection;
using BlazorNative.Core;

namespace BlazorNative.Http;

// ─────────────────────────────────────────────────────────────────────────────
// ServiceCollectionExtensions
// ─────────────────────────────────────────────────────────────────────────────

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers HttpClient with BridgeHttpHandler as the primary handler.
    /// All HttpClient injections will route through IMobileBridge.FetchAsync,
    /// bypassing WASI's lack of socket support transparently.
    ///
    /// <code>
    /// // In your DI setup (DevHost Program.cs or WASI entry point):
    /// services.AddBlazorNativeHttp();
    ///
    /// // In your services — no changes needed:
    /// public class WeatherService(HttpClient http) { ... }
    /// </code>
    /// </summary>
    public static IServiceCollection AddBlazorNativeHttp(
        this IServiceCollection services)
    {
        // Register the handler itself
        services.AddScoped<BridgeHttpHandler>();

        // Default HttpClient — uses bridge handler.
        // AddHttpClient(name) returns IHttpClientBuilder (the parameterless
        // AddHttpClient() returns IServiceCollection and can't be chained).
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler<BridgeHttpHandler>();

        return services;
    }

    /// <summary>
    /// Register a typed HttpClient with BridgeHttpHandler and optional base address.
    ///
    /// <code>
    /// services.AddBlazorNativeHttpClient&lt;WeatherService&gt;("https://api.weather.com");
    /// </code>
    /// </summary>
    public static IHttpClientBuilder AddBlazorNativeHttpClient<TClient>(
        this IServiceCollection services,
        string? baseAddress = null)
        where TClient : class
    {
        var builder = services
            .AddHttpClient<TClient>()
            .ConfigurePrimaryHttpMessageHandler<BridgeHttpHandler>();

        if (baseAddress is not null)
            builder.ConfigureHttpClient(c => c.BaseAddress = new Uri(baseAddress));

        return builder;
    }

    /// <summary>
    /// Register a named HttpClient with BridgeHttpHandler.
    ///
    /// <code>
    /// services.AddBlazorNativeHttpClient("weather", "https://api.weather.com");
    /// // Inject via IHttpClientFactory and call CreateClient("weather")
    /// </code>
    /// </summary>
    public static IHttpClientBuilder AddBlazorNativeHttpClient(
        this IServiceCollection services,
        string name,
        string? baseAddress = null)
    {
        var builder = services
            .AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler<BridgeHttpHandler>();

        if (baseAddress is not null)
            builder.ConfigureHttpClient(c => c.BaseAddress = new Uri(baseAddress));

        return builder;
    }
}
