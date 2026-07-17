using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// ServiceCollectionExtensions — the Device package's registration (the
// AddBlazorNativeHttp twin). Hand-written rather than source-generated: the
// facade is a single delegate over IMobileBridge, and an explicit registration
// says exactly that. Called from the runtime composition root (HostSession) so a
// component doing [Inject] IGeolocation resolves the facade over whichever
// IMobileBridge the host registered (NativeShellBridge on-device, DevHostBridge in
// a harness).
// ─────────────────────────────────────────────────────────────────────────────

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IGeolocation"/> and <see cref="INotifications"/>
    /// (and the other M9 device facades as they land). Requires an
    /// <c>IMobileBridge</c> to already be registered — each facade is a thin delegate
    /// over it.
    ///
    /// <code>
    /// // In your DI setup (the runtime composition root, or a test harness):
    /// services.AddBlazorNativeDevice();
    ///
    /// // In your component — inject the ergonomic facade, not the low-level bridge:
    /// [Inject] public IGeolocation Geo { get; set; } = default!;
    /// [Inject] public INotifications Notifications { get; set; } = default!;
    /// </code>
    /// </summary>
    public static IServiceCollection AddBlazorNativeDevice(this IServiceCollection services)
    {
        services.AddSingleton<IGeolocation, Geolocation>();
        services.AddSingleton<INotifications, Notifications>(); // Phase 9.1 — the notifications facade (no 8th package)
        return services;
    }
}
