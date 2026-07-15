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
    /// so the native shell performs the actual HTTP request.
    ///
    /// <code>
    /// // In your DI setup (the runtime composition root — HostSession — or a test harness):
    /// services.AddBlazorNativeHttp();
    ///
    /// // In your services — no changes needed:
    /// public class WeatherService(HttpClient http) { ... }
    /// </code>
    /// </summary>
    public static IServiceCollection AddBlazorNativeHttp(
        this IServiceCollection services)
    {
        // BridgeHttpHandler is registered by the generated AddBlazorNativeHttpServices()
        // via its [Transient] attribute (see Task 6). The HttpClient factory plumbing
        // isn't part of ZA.Inject's surface — keep it here.
        services.AddBlazorNativeHttpServices();

        // AddHttpClient(Options.DefaultName) is the chainable equivalent of the
        // parameterless AddHttpClient(). Both register the default-named factory
        // configuration that IHttpClientFactory.CreateClient() (no args) consumes.
        // The parameterless overload returns IServiceCollection (cannot chain);
        // the named overload returns IHttpClientBuilder (can chain). Don't
        // "simplify" back to AddHttpClient() — the .ConfigurePrimaryHttpMessageHandler
        // call below requires the builder.
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
