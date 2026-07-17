using BlazorNative.Core;
using BlazorNative.Device;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NotificationFacadeTests — Phase 9.1 Gate 1 (M9 DoD #3): the DevHostBridge
// five-status matrix (the central headless proof of the named risk — denial as
// data, NO device) + the schedule/show/cancel bookkeeping, and the INotifications
// facade (the 7th package's second app-facing surface — no 8th package)
// delegating over IMobileBridge.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NotificationFacadeTests
{
    private static NotificationSpec Spec(int id = 7)
        => new(id, "Hi", "Body", When: null, Route: "/notifications");

    // ── DevHostBridge drives ALL FIVE statuses headless, no hang ──────────────

    [Theory]
    [InlineData(NotificationStatus.Granted)]
    [InlineData(NotificationStatus.Denied)]
    [InlineData(NotificationStatus.DeniedPermanently)]
    [InlineData(NotificationStatus.Restricted)]
    [InlineData(NotificationStatus.Error)]
    public async Task DevHostBridge_ReturnsConfiguredStatus_NoThrow_NoHang(NotificationStatus status)
    {
        using var bridge = new DevHostBridge { NotificationStatus = status };

        NotificationStatus show = await bridge.ShowNotificationAsync(Spec())
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        NotificationStatus schedule = await bridge.ScheduleNotificationAsync(Spec())
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        NotificationStatus cancel = await bridge.CancelNotificationAsync(7)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        NotificationStatus request = await bridge.RequestNotificationPermissionAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        NotificationStatus check = await bridge.CheckNotificationPermissionAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(status, show);
        Assert.Equal(status, schedule);
        Assert.Equal(status, cancel);
        Assert.Equal(status, request);
        Assert.Equal(status, check);
    }

    // ── The in-memory bookkeeping: show records, cancel removes ───────────────

    [Fact]
    public async Task DevHostBridge_Granted_ShowRecords_CancelRemoves()
    {
        using var bridge = new DevHostBridge { NotificationStatus = NotificationStatus.Granted };

        await bridge.ShowNotificationAsync(Spec(id: 7));
        await bridge.ScheduleNotificationAsync(Spec(id: 8));
        Assert.Equal(2, bridge.Notifications.Count);

        await bridge.CancelNotificationAsync(7);
        Assert.Single(bridge.Notifications);
        Assert.Equal(8, bridge.Notifications[0].Id);

        // Cancel is idempotent — an unknown id is a benign no-op that still statuses.
        Assert.Equal(NotificationStatus.Granted, await bridge.CancelNotificationAsync(999));
        Assert.Single(bridge.Notifications);
    }

    [Fact]
    public async Task DevHostBridge_SameId_Replaces()
    {
        using var bridge = new DevHostBridge { NotificationStatus = NotificationStatus.Granted };
        await bridge.ShowNotificationAsync(Spec(id: 7));
        await bridge.ShowNotificationAsync(Spec(id: 7)); // collision replaces (notify semantics)
        Assert.Single(bridge.Notifications);
    }

    [Fact]
    public async Task DevHostBridge_Denied_RecordsNothing()
    {
        using var bridge = new DevHostBridge { NotificationStatus = NotificationStatus.Denied };
        Assert.Equal(NotificationStatus.Denied, await bridge.ShowNotificationAsync(Spec()));
        Assert.Empty(bridge.Notifications); // a denied op posts nothing — the status IS the answer
    }

    // ── The INotifications facade delegates over the bridge (DI-resolved) ─────

    [Fact]
    public async Task Facade_DelegatesToBridge_AllOps()
    {
        var bridge = new DevHostBridge { NotificationStatus = NotificationStatus.Granted };
        var services = new ServiceCollection();
        services.AddSingleton<IMobileBridge>(bridge);
        services.AddBlazorNativeDevice();
        using ServiceProvider provider = services.BuildServiceProvider();

        var notifications = provider.GetRequiredService<INotifications>();

        Assert.Equal(NotificationStatus.Granted, await notifications.ShowAsync(Spec(id: 7)));
        Assert.Equal(NotificationStatus.Granted, await notifications.ScheduleAsync(Spec(id: 8)));
        Assert.Equal(2, bridge.Notifications.Count);
        Assert.Equal(NotificationStatus.Granted, await notifications.CancelAsync(7));
        Assert.Single(bridge.Notifications);
        Assert.Equal(NotificationStatus.Granted, await notifications.RequestPermissionAsync());
        Assert.Equal(NotificationStatus.Granted, await notifications.CheckPermissionAsync());

        bridge.Dispose();
    }

    [Fact]
    public async Task Facade_DelegatesToBridge_Denied_NoThrow()
    {
        var bridge = new DevHostBridge { NotificationStatus = NotificationStatus.DeniedPermanently };
        var services = new ServiceCollection();
        services.AddSingleton<IMobileBridge>(bridge);
        services.AddBlazorNativeDevice();
        using ServiceProvider provider = services.BuildServiceProvider();

        var notifications = provider.GetRequiredService<INotifications>();
        NotificationStatus status = await notifications.ShowAsync(Spec())
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NotificationStatus.DeniedPermanently, status);
        bridge.Dispose();
    }
}
