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
    /// <summary>Registers <see cref="IGeolocation"/>, <see cref="INotifications"/>,
    /// <see cref="IBiometrics"/>, <see cref="ISecureStorage"/> and
    /// <see cref="ICamera"/> (the full M9 device roster — the 7th package is the last,
    /// no 8th). Requires an <c>IMobileBridge</c> to already be registered — each facade
    /// is a thin delegate over it.
    ///
    /// <code>
    /// // In your DI setup (the runtime composition root, or a test harness):
    /// services.AddBlazorNativeDevice();
    ///
    /// // In your component — inject the ergonomic facade, not the low-level bridge:
    /// [Inject] public IGeolocation Geo { get; set; } = default!;
    /// [Inject] public INotifications Notifications { get; set; } = default!;
    /// [Inject] public IBiometrics Biometrics { get; set; } = default!;
    /// [Inject] public ISecureStorage Secrets { get; set; } = default!;
    /// [Inject] public ICamera Camera { get; set; } = default!;
    /// </code>
    /// </summary>
    public static IServiceCollection AddBlazorNativeDevice(this IServiceCollection services)
    {
        services.AddSingleton<IGeolocation, Geolocation>();
        services.AddSingleton<INotifications, Notifications>(); // Phase 9.1 — the notifications facade (no 8th package)
        services.AddSingleton<IBiometrics, Biometrics>();       // Phase 9.2 — biometrics (no 8th package)
        services.AddSingleton<ISecureStorage, SecureStorage>(); // Phase 9.2 — secure storage (M5 deferral closed)
        services.AddSingleton<ICamera, Camera>();               // Phase 9.3 — camera (no 8th package; M9 roster closed)
        return services;
    }
}
