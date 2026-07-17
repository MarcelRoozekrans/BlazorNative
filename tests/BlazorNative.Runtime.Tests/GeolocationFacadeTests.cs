using BlazorNative.Core;
using BlazorNative.Device;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// GeolocationFacadeTests — Phase 9.0 Gate 1 (M9 DoD #2): the DevHostBridge
// six-status matrix (the central headless proof of the named risk — denial as
// data, NO device) and the IGeolocation facade (the 7th package's app-facing
// surface) delegating over IMobileBridge.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class GeolocationFacadeTests
{
    // ── DevHostBridge drives ALL SIX statuses headless, no hang ───────────────

    [Theory]
    [InlineData(GeolocationStatus.Granted)]
    [InlineData(GeolocationStatus.Denied)]
    [InlineData(GeolocationStatus.DeniedPermanently)]
    [InlineData(GeolocationStatus.Restricted)]
    [InlineData(GeolocationStatus.LocationUnavailable)]
    [InlineData(GeolocationStatus.Error)]
    public async Task DevHostBridge_ReturnsConfiguredStatus_NoThrow_NoHang(GeolocationStatus status)
    {
        using var bridge = new DevHostBridge { GeolocationStatus = status };

        GeolocationResult result = await bridge.GetCurrentPositionAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(status, result.Status);
        if (status == GeolocationStatus.Granted)
            Assert.NotNull(result.Position);
        else
            Assert.Null(result.Position); // denial/etc. is a status ALONE
    }

    [Fact]
    public async Task DevHostBridge_Granted_ReturnsConfiguredPosition()
    {
        var fix = new GeolocationPosition(1.5, 2.5, 3.5, 4.5, 12345);
        using var bridge = new DevHostBridge
        {
            GeolocationStatus = GeolocationStatus.Granted,
            GeolocationPosition = fix,
        };

        GeolocationResult result = await bridge.GetCurrentPositionAsync();
        Assert.Equal(fix, result.Position);
    }

    [Fact]
    public async Task DevHostBridge_CheckPermission_DoesNotPrompt_ReturnsStatus()
    {
        using var bridge = new DevHostBridge { GeolocationStatus = GeolocationStatus.DeniedPermanently };
        Assert.Equal(GeolocationStatus.DeniedPermanently, await bridge.CheckGeolocationPermissionAsync());
    }

    // ── The IGeolocation facade delegates over the bridge (DI-resolved) ───────

    [Fact]
    public async Task Facade_DelegatesToBridge_Granted()
    {
        var fix = new GeolocationPosition(52.0, 4.0, 10.0, null, 999);
        var bridge = new DevHostBridge
        {
            GeolocationStatus = GeolocationStatus.Granted,
            GeolocationPosition = fix,
        };
        var services = new ServiceCollection();
        services.AddSingleton<IMobileBridge>(bridge);
        services.AddBlazorNativeDevice();
        using ServiceProvider provider = services.BuildServiceProvider();

        var geo = provider.GetRequiredService<IGeolocation>();
        GeolocationResult result = await geo.GetCurrentPositionAsync();

        Assert.Equal(GeolocationStatus.Granted, result.Status);
        Assert.Equal(fix, result.Position);
        bridge.Dispose();
    }

    [Fact]
    public async Task Facade_DelegatesToBridge_Denied_NoThrow()
    {
        var bridge = new DevHostBridge { GeolocationStatus = GeolocationStatus.Denied };
        var services = new ServiceCollection();
        services.AddSingleton<IMobileBridge>(bridge);
        services.AddBlazorNativeDevice();
        using ServiceProvider provider = services.BuildServiceProvider();

        var geo = provider.GetRequiredService<IGeolocation>();
        GeolocationResult result = await geo.GetCurrentPositionAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(GeolocationStatus.Denied, result.Status);
        Assert.Null(result.Position);
        Assert.Equal(GeolocationStatus.Denied, await geo.CheckPermissionAsync());
        bridge.Dispose();
    }
}
