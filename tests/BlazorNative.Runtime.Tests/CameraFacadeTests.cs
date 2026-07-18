using BlazorNative.Core;
using BlazorNative.Device;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// CameraFacadeTests — Phase 9.3 Gate 1 (M9 DoD #5): the DevHostBridge camera matrix
// (the central headless proof of denial/cancel-as-data — NO device, NO camera) + the
// canned test-image path (the Captured result names a REAL file:// URI, so the
// composition is exercisable headless) + the ICamera facade (the 7th package's FIFTH
// app-facing surface — no 8th package) delegating over IMobileBridge.
//
// This is where the design's central claim is asserted headless, before any device
// work: every CameraStatus drives as data within a bounded await, a Captured result
// carries the path + dims, and — the named "no-path-on-cancel" mutation — a
// non-Captured result carries NO path.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CameraFacadeTests
{
    private static ServiceProvider Provide(DevHostBridge bridge)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMobileBridge>(bridge);
        services.AddBlazorNativeDevice();
        return services.BuildServiceProvider();
    }

    // ── DevHostBridge drives every status headless, no hang ──────────────────────

    [Theory]
    [InlineData(CameraStatus.Captured)]
    [InlineData(CameraStatus.Cancelled)]
    [InlineData(CameraStatus.Denied)]
    [InlineData(CameraStatus.Unavailable)]
    [InlineData(CameraStatus.Error)]
    public async Task Capture_ReturnsConfiguredStatus_NoThrow_NoHang(CameraStatus status)
    {
        using var bridge = new DevHostBridge { CameraCaptureResult = status };
        PhotoResult result = await bridge.CapturePhotoAsync(default)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(status, result.Status);
    }

    // ── Captured returns the canned path + known dims (composition is exercisable) ─

    [Fact]
    public async Task Captured_ReturnsTheCannedPathAndDims_FileExistsWithBytes()
    {
        using var bridge = new DevHostBridge { CameraCaptureResult = CameraStatus.Captured };
        PhotoResult result = await bridge.CapturePhotoAsync(default);

        Assert.Equal(CameraStatus.Captured, result.Status);
        Assert.NotNull(result.Path);
        Assert.StartsWith("file:///", result.Path);      // a real file:// URI, a valid BnImage.Src
        Assert.True(result.Width > 0 && result.Height > 0);
        Assert.True(result.SizeBytes > 0);               // the file the path names has bytes

        // The canned path names a file that actually exists on disk.
        Assert.True(File.Exists(new Uri(result.Path!).LocalPath));
    }

    // ── THE NO-PATH-ON-CANCEL CONTRACT (the named mutation's target) ──────────────

    [Theory]
    [InlineData(CameraStatus.Cancelled)]
    [InlineData(CameraStatus.Denied)]
    [InlineData(CameraStatus.Unavailable)]
    [InlineData(CameraStatus.Error)]
    public async Task NonCaptured_CarriesNoPath_ZeroDims(CameraStatus status)
    {
        // A cancel (or any non-Captured outcome) has NO file — so it must carry NO
        // path and zero dims. If the mock returned a path for a Cancelled status (the
        // named mutation), this reds.
        using var bridge = new DevHostBridge { CameraCaptureResult = status };
        PhotoResult result = await bridge.CapturePhotoAsync(default)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(status, result.Status);
        Assert.Null(result.Path);
        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
        Assert.Equal(0L, result.SizeBytes);
    }

    // ── The read-only availability check ─────────────────────────────────────────

    [Fact]
    public async Task CheckAvailability_UsableVsUnavailable()
    {
        using var usable = new DevHostBridge { CameraCaptureResult = CameraStatus.Captured };
        using var absent = new DevHostBridge { CameraCaptureResult = CameraStatus.Unavailable };

        Assert.Equal(CameraStatus.Captured, await usable.CheckCameraAvailabilityAsync());
        Assert.Equal(CameraStatus.Unavailable, await absent.CheckCameraAvailabilityAsync());
    }

    // ── The ICamera facade delegates over the bridge (DI-resolved) ───────────────

    [Fact]
    public async Task Camera_Facade_DelegatesToBridge_CaptureAndCheck()
    {
        using var bridge = new DevHostBridge { CameraCaptureResult = CameraStatus.Captured };
        using ServiceProvider provider = Provide(bridge);
        var camera = provider.GetRequiredService<ICamera>();

        PhotoResult captured = await camera.CapturePhotoAsync();
        Assert.Equal(CameraStatus.Captured, captured.Status);
        Assert.NotNull(captured.Path);

        Assert.Equal(CameraStatus.Captured, await camera.CheckAvailabilityAsync());
    }

    [Fact]
    public async Task Camera_Facade_DenialIsData_NoThrow()
    {
        using var bridge = new DevHostBridge { CameraCaptureResult = CameraStatus.Cancelled };
        using ServiceProvider provider = Provide(bridge);
        var camera = provider.GetRequiredService<ICamera>();

        PhotoResult cancelled = await camera.CapturePhotoAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CameraStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Path);
    }
}
