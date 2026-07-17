using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// Notifications — the thin delegate that IS the facade (the Geolocation twin). It
// holds NO permission logic: the whole state machine is host-side
// (NativeShellBridge on-device, DevHostBridge headless), and this type only
// forwards to the IMobileBridge notification primitives. Those primitives stay in
// Core precisely so DevHostBridge can mock the five statuses headless — this
// facade rides whatever bridge DI hands it.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class Notifications(IMobileBridge bridge) : INotifications
{
    public ValueTask<NotificationStatus> ScheduleAsync(NotificationSpec spec, CancellationToken ct = default)
        => bridge.ScheduleNotificationAsync(spec, ct);

    public ValueTask<NotificationStatus> ShowAsync(NotificationSpec spec, CancellationToken ct = default)
        => bridge.ShowNotificationAsync(spec, ct);

    public ValueTask<NotificationStatus> CancelAsync(int id, CancellationToken ct = default)
        => bridge.CancelNotificationAsync(id, ct);

    public ValueTask<NotificationStatus> RequestPermissionAsync(CancellationToken ct = default)
        => bridge.RequestNotificationPermissionAsync(ct);

    public ValueTask<NotificationStatus> CheckPermissionAsync(CancellationToken ct = default)
        => bridge.CheckNotificationPermissionAsync(ct);
}
