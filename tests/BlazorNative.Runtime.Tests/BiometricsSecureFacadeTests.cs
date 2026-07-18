using BlazorNative.Core;
using BlazorNative.Device;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BiometricsSecureFacadeTests — Phase 9.2 Gate 1 (M9 DoD #4): the DevHostBridge
// biometric + secure-storage matrices (the central headless proof of the named
// risk — denial as data, NO device) + THE PAIRING (a mocked-auth getWithAuth
// returns the value, a mocked-deny returns AuthFailed) + the IBiometrics /
// ISecureStorage facades (the 7th package's third + fourth app-facing surfaces —
// no 8th package) delegating over IMobileBridge.
//
// This is where the design's central claim is asserted headless, before any device
// work: every BiometricStatus / SecureStorageStatus drives as data within a bounded
// await, the pairing honours requireAuth (the named bypass mutation — "getWithAuth
// returns the secret without auth" — reds the gated-read test), and the 8 KB cap
// statuses rather than crashes.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BiometricsSecureFacadeTests
{
    private static ServiceProvider Provide(DevHostBridge bridge)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMobileBridge>(bridge);
        services.AddBlazorNativeDevice();
        return services.BuildServiceProvider();
    }

    // ── Biometrics: DevHostBridge drives all six statuses headless, no hang ───

    [Theory]
    [InlineData(BiometricStatus.Authenticated)]
    [InlineData(BiometricStatus.Failed)]
    [InlineData(BiometricStatus.Cancelled)]
    [InlineData(BiometricStatus.Unavailable)]
    [InlineData(BiometricStatus.LockedOut)]
    [InlineData(BiometricStatus.Error)]
    public async Task Biometric_Authenticate_ReturnsConfiguredStatus_NoThrow_NoHang(BiometricStatus status)
    {
        using var bridge = new DevHostBridge { BiometricAuthResult = status };
        BiometricStatus result = await bridge.AuthenticateAsync("reason")
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(status, result);
    }

    [Fact]
    public async Task Biometric_IsAvailable_TrueWhenReady_FalseWhenUnavailable()
    {
        using var ready = new DevHostBridge { BiometricAuthResult = BiometricStatus.Authenticated };
        using var absent = new DevHostBridge { BiometricAuthResult = BiometricStatus.Unavailable };

        using ServiceProvider readyP = Provide(ready);
        using ServiceProvider absentP = Provide(absent);

        Assert.True(await readyP.GetRequiredService<IBiometrics>().IsAvailableAsync());
        Assert.False(await absentP.GetRequiredService<IBiometrics>().IsAvailableAsync());
    }

    [Fact]
    public async Task Biometric_Facade_DelegatesToBridge()
    {
        using var bridge = new DevHostBridge { BiometricAuthResult = BiometricStatus.LockedOut };
        using ServiceProvider provider = Provide(bridge);
        var biometrics = provider.GetRequiredService<IBiometrics>();

        Assert.Equal(BiometricStatus.LockedOut, await biometrics.AuthenticateAsync("reason"));
    }

    // ── Secure storage: the round-trip + every status, headless ───────────────

    [Fact]
    public async Task Secure_PlainSetGetDelete_RoundTrips()
    {
        using var bridge = new DevHostBridge();

        Assert.Equal(SecureStorageStatus.Ok, await bridge.SetSecretAsync("k", "v", requireAuth: false));

        SecretResult got = await bridge.GetSecretAsync("k");
        Assert.Equal(SecureStorageStatus.Ok, got.Status);
        Assert.Equal("v", got.Value);

        Assert.Equal(SecureStorageStatus.Ok, await bridge.DeleteSecretAsync("k"));

        SecretResult after = await bridge.GetSecretAsync("k");
        Assert.Equal(SecureStorageStatus.NotFound, after.Status);
        Assert.Null(after.Value);
    }

    [Fact]
    public async Task Secure_Delete_IsIdempotent()
    {
        using var bridge = new DevHostBridge();
        Assert.Equal(SecureStorageStatus.Ok, await bridge.DeleteSecretAsync("absent"));
    }

    [Fact]
    public async Task Secure_GetAbsentKey_IsNotFound()
    {
        using var bridge = new DevHostBridge();
        SecretResult r = await bridge.GetSecretAsync("absent")
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SecureStorageStatus.NotFound, r.Status);
        Assert.Null(r.Value);
    }

    // ── THE PAIRING: an auth-bound write pairs with an auth-bound read ────────

    [Fact]
    public async Task Pairing_AuthBoundSet_UnlockWithAuth_ReturnsTheValue()
    {
        using var bridge = new DevHostBridge { BiometricAuthResult = BiometricStatus.Authenticated };

        Assert.Equal(SecureStorageStatus.Ok, await bridge.SetSecretAsync("k", "s3cret", requireAuth: true));

        SecretResult unlocked = await bridge.GetSecretWithAuthAsync("k", "Unlock")
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SecureStorageStatus.Ok, unlocked.Status);
        Assert.Equal("s3cret", unlocked.Value);
    }

    [Fact]
    public async Task Pairing_AuthBoundSet_UnlockWhenAuthDenied_ReturnsAuthFailed_NoValue()
    {
        // THE GATED-READ CONTRACT (the named bypass mutation's target): a
        // getWithAuth whose mocked biometric gate is NOT Authenticated must return
        // AuthFailed with NO value. If the mock returned the secret regardless of the
        // gate (the bypass mutant), this reds.
        using var bridge = new DevHostBridge { BiometricAuthResult = BiometricStatus.Failed };
        Assert.Equal(SecureStorageStatus.Ok, await bridge.SetSecretAsync("k", "s3cret", requireAuth: true));

        SecretResult denied = await bridge.GetSecretWithAuthAsync("k", "Unlock")
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SecureStorageStatus.AuthFailed, denied.Status);
        Assert.Null(denied.Value);
    }

    [Fact]
    public async Task Pairing_PlainGetOfAuthBoundItem_FailsAuthFailed()
    {
        // A plain get (no prompt) of an auth-bound item correctly fails AuthFailed —
        // the OS-key would refuse the plaintext without the prompt (§4c), and the
        // headless mock mirrors that.
        using var bridge = new DevHostBridge { BiometricAuthResult = BiometricStatus.Authenticated };
        await bridge.SetSecretAsync("k", "s3cret", requireAuth: true);

        SecretResult plain = await bridge.GetSecretAsync("k");
        Assert.Equal(SecureStorageStatus.AuthFailed, plain.Status);
        Assert.Null(plain.Value);
    }

    // ── The 8 KB cap statuses rather than crashes (headless) ──────────────────

    [Fact]
    public async Task Secure_OversizeValue_ReturnsError_NoThrow()
    {
        using var bridge = new DevHostBridge();
        string oversize = new('x', SecretResult.MaxValueBytes + 1);
        SecureStorageStatus status = await bridge.SetSecretAsync("k", oversize, requireAuth: false)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SecureStorageStatus.Error, status);
        Assert.False(bridge.SecretSnapshot.ContainsKey("k")); // nothing stored
    }

    // ── The ISecureStorage facade delegates over the bridge (DI-resolved) ─────

    [Fact]
    public async Task Secure_Facade_DelegatesToBridge_AllOps_IncludingPairing()
    {
        using var bridge = new DevHostBridge { BiometricAuthResult = BiometricStatus.Authenticated };
        using ServiceProvider provider = Provide(bridge);
        var secrets = provider.GetRequiredService<ISecureStorage>();

        Assert.Equal(SecureStorageStatus.Ok, await secrets.SetAsync("k", "v", requireAuth: true));

        SecretResult unlocked = await secrets.GetWithAuthAsync("k", "Unlock");
        Assert.Equal(SecureStorageStatus.Ok, unlocked.Status);
        Assert.Equal("v", unlocked.Value);

        // A plain get of the auth-bound item fails through the facade too.
        Assert.Equal(SecureStorageStatus.AuthFailed, (await secrets.GetAsync("k")).Status);

        Assert.Equal(SecureStorageStatus.Ok, await secrets.DeleteAsync("k"));
        Assert.Equal(SecureStorageStatus.NotFound, (await secrets.GetAsync("k")).Status);
    }

    [Fact]
    public async Task Secure_Facade_DenialIsData_NoThrow()
    {
        using var bridge = new DevHostBridge { BiometricAuthResult = BiometricStatus.Cancelled };
        using ServiceProvider provider = Provide(bridge);
        var secrets = provider.GetRequiredService<ISecureStorage>();
        await secrets.SetAsync("k", "v", requireAuth: true);

        SecretResult denied = await secrets.GetWithAuthAsync("k", "Unlock")
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SecureStorageStatus.AuthFailed, denied.Status);
    }
}
