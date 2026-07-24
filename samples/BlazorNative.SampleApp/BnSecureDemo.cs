using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Device;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// BnSecureDemo — Phase 9.2 (M9 DoD #4): the routed page that proves the biometrics
// + secure-storage surface reaches a mounted component. The THIRD worked example of
// the permission pattern (the SECOND reuse of the 9.0 generic ABI), and the first
// page that [Inject]s TWO device facades — IBiometrics AND ISecureStorage (both from
// the 7th package BlazorNative.Device; no 8th package).
//
// It mounts the SAME component on all three surfaces — .NET (BnSecureDemoTests via
// DispatchEventCore, with a DevHostBridge driving every status + the pairing), and —
// at Gates 2/3 — the AVD (BiometricPrompt + AndroidKeyStore) and the iOS simulator
// (LAContext + Keychain). The .NET/wire half + the demo live here; Gates 2/3 wire
// the shells' real prompt + store.
//
// THE PAIRING, shown: Set stores an auth-bound secret (requireAuth:true); Unlock
// reads it back GATED BY A BIOMETRIC PROMPT (GetWithAuthAsync) and echoes the value
// on Ok; a plain Get of the same auth-bound item (no prompt) fails AuthFailed —
// shown, never thrown. Every action echoes its returned status/value as DATA: a
// failed auth, a cancelled prompt, a no-hardware device, a NotFound, an AuthFailed
// are all SHOWN, never thrown, never hung (the geolocation/notifications discipline).
//
// Sample-only (the 9.0/9.1 template-minimal precedent): BnGeolocationDemo /
// BnNotificationsDemo are sample-only because the template tree is pinned minimal;
// BnSecureDemo joins them sample-only — NOT added to the template.
//
// Shape:
//   root div
//     ├─ BnButton "Authenticate" → IBiometrics.AuthenticateAsync        → echo status
//     ├─ BnButton "Set"          → ISecureStorage.SetAsync(auth:true)   → echo status
//     ├─ BnButton "Unlock"       → ISecureStorage.GetWithAuthAsync      → echo value / status
//     ├─ BnButton "Delete"       → ISecureStorage.DeleteAsync           → echo status
//     ├─ BnText echo (mount-pinned text node — the ClipboardProbe echo contract)
//     └─ BnButton "← Back" → INavigationManager.NavigateToAsync("/")   (#204 — nav
//        parity with the eight pages that already carried one; TRAILING so the device
//        suites' "first TextView/UILabel that is not a Button" echo selectors still
//        resolve to the echo)
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class BnSecureDemo : ComponentBase
{
    /// <summary>The key this demo sets / unlocks / deletes.</summary>
    internal const string Key = "demo-secret";

    /// <summary>The secret value this demo stores (text — binary secrets base64
    /// per §8; the demo uses text).</summary>
    internal const string Secret = "hunter2";

    /// <summary>The biometric prompt message the Unlock read shows.</summary>
    internal const string UnlockReason = "Unlock your secret";

    /// <summary>The route this page is mounted at (the 12th routed page).</summary>
    internal const string Route = "/secure";

    /// <summary>Echo prefixes — distinctive so a stale echo is obvious and a denial
    /// is provably DATA.</summary>
    internal const string StatusPrefix = "status:";
    internal const string ValuePrefix = "value:";

    private string _echo = "ready";

    [Inject] public IBiometrics Biometrics { get; set; } = default!;
    [Inject] public ISecureStorage Secrets { get; set; } = default!;

    /// <summary>#204: the navigation service, for the trailing "← Back" — the same
    /// explicit [Inject] public property every other page uses.</summary>
    [Inject] public INavigationManager Navigation { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        b.OpenComponent<BnButton>(10);
        b.AddComponentParameter(11, nameof(BnButton.Label), "Authenticate");
        b.AddComponentParameter(12, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, AuthenticateAsync));
        b.CloseComponent();

        b.OpenComponent<BnButton>(20);
        b.AddComponentParameter(21, nameof(BnButton.Label), "Set");
        b.AddComponentParameter(22, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, SetAsync));
        b.CloseComponent();

        b.OpenComponent<BnButton>(30);
        b.AddComponentParameter(31, nameof(BnButton.Label), "Unlock");
        b.AddComponentParameter(32, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, UnlockAsync));
        b.CloseComponent();

        b.OpenComponent<BnButton>(40);
        b.AddComponentParameter(41, nameof(BnButton.Label), "Delete");
        b.AddComponentParameter(42, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, DeleteAsync));
        b.CloseComponent();

        b.OpenComponent<BnText>(50);                             // the echo
        b.AddComponentParameter(51, nameof(BnText.Text), _echo);
        b.CloseComponent();

        // "← Back" (#204) — nav parity with the eight pages that already carry one.
        // LAST, after the echo: both device suites select the echo as "the first
        // TextView/UILabel that is not a Button", so a TRAILING button leaves those
        // selectors resolving to exactly what they did before.
        b.OpenComponent<BnButton>(90);
        b.AddComponentParameter(91, nameof(BnButton.Label), "← Back");
        b.AddComponentParameter(92, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, GoBack));
        b.CloseComponent();

        b.CloseElement();
    }

    // Sync-completing (inline dispatcher), like every other page's GoBack.
    private Task GoBack() => Navigation.NavigateToAsync("/").AsTask();

    // Each action echoes its returned status/value as DATA — a denial is SHOWN,
    // never thrown, never left hanging (the BnNotificationsDemo discipline).
    private async Task AuthenticateAsync()
    {
        BiometricStatus status = await Biometrics.AuthenticateAsync("Prove it's you");
        _echo = $"{StatusPrefix}{status}";
    }

    private async Task SetAsync()
    {
        SecureStorageStatus status = await Secrets.SetAsync(Key, Secret, requireAuth: true);
        _echo = $"{StatusPrefix}{status}";
    }

    private async Task UnlockAsync()
    {
        // THE PAIRING: read the auth-bound secret behind the biometric prompt. On Ok
        // echo the value; otherwise echo the status (AuthFailed / NotFound / …) — the
        // failure path is a first-class, visible affordance.
        SecretResult result = await Secrets.GetWithAuthAsync(Key, UnlockReason);
        _echo = result.Status == SecureStorageStatus.Ok
            ? $"{ValuePrefix}{result.Value}"
            : $"{StatusPrefix}{result.Status}";
    }

    private async Task DeleteAsync()
    {
        SecureStorageStatus status = await Secrets.DeleteAsync(Key);
        _echo = $"{StatusPrefix}{status}";
    }
}
