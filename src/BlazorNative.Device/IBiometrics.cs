using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// IBiometrics — Phase 9.2 (M9 DoD #4): the app-facing, DI-injectable ergonomic
// facade over biometric authentication. App code injects THIS, not the low-level
// IMobileBridge — a thin delegate (the IGeolocation / INotifications sibling) in
// the SAME 7th package BlazorNative.Device. No 8th package.
//
// authenticate "proves who is holding the phone" and returns a BiometricStatus
// VALUE — failure, cancellation, lockout and no-hardware are all DATA, never an
// exception, never a hang (the milestone law). It deliberately returns NO token
// and NO secret: the honest gate for a stored secret is the OS-key-bound
// ISecureStorage.GetWithAuthAsync, where the OS itself refuses to decrypt without
// a fresh auth — not a .NET bool an attacker could skip (§3d/§4c). IsAvailableAsync
// reads whether biometrics are present + enrolled WITHOUT prompting (the read-only
// sibling), so a UI can decide whether to OFFER a biometric action.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>DI-injectable façade over biometric authentication (Face ID / Touch ID /
/// fingerprint). Inject this rather than the low-level <see cref="IMobileBridge"/>;
/// <see cref="AuthenticateAsync"/> proves who is holding the device and returns a
/// <see cref="BiometricStatus"/> value (failure, cancellation, lockout and no-hardware
/// are DATA, never an exception), while <see cref="IsAvailableAsync"/> reports presence
/// and enrolment without prompting. Register it with
/// <see cref="ServiceCollectionExtensions.AddBlazorNativeDevice"/>.</summary>
public interface IBiometrics
{
    /// <summary>Shows an OS biometric prompt (Face ID / Touch ID / fingerprint) with
    /// <paramref name="reason"/> as its message and returns the terminal
    /// <see cref="BiometricStatus"/> — a failure / cancellation / lockout / no-hardware
    /// outcome is DATA, never a throw. Returns no token and no secret: the status is
    /// the whole answer.</summary>
    ValueTask<BiometricStatus> AuthenticateAsync(string reason, CancellationToken ct = default);

    /// <summary>Reads whether biometric authentication is available (hardware present
    /// AND at least one biometric enrolled) WITHOUT prompting — for a UI that wants to
    /// show state before offering a biometric action.</summary>
    ValueTask<bool> IsAvailableAsync(CancellationToken ct = default);
}
