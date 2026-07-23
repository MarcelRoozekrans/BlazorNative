using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Device;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// BnGeolocationDemo — Phase 9.0 (M9 DoD #2): the routed page that proves the
// permission-gated geolocation surface reaches a mounted component. The worked
// example of the permission pattern, as ClipboardProbe was for the clipboard
// slots — but here app code injects the ergonomic IGeolocation FACADE (from the
// 7th package, BlazorNative.Device), not the low-level IMobileBridge.
//
// It mounts the SAME component on all three surfaces — .NET
// (BnGeolocationDemoTests via DispatchEventCore, with a DevHostBridge driving all
// six statuses), and — at Gates 2/3 — the AVD (adb emu geo fix, pm revoke) and the
// iOS simulator (simctl location + the auth alert). The .NET/wire half + the demo
// live here; Gates 2/3 wire the shells (AndroidShellBridge LocationManager +
// requestPermissions, AppleShellBridge CLLocationManager) behind the same façade.
//
// Shape:
//   root div
//     ├─ BnButton "Locate" → GetCurrentPositionAsync() → echo the fix OR the status
//     ├─ BnButton "Check"  → CheckPermissionAsync() (no prompt) → echo the status
//     ├─ BnText echo (mount-pinned text node — the FocusProbe/ClipboardProbe echo
//     │  contract: transitions are ReplaceText on a stable nodeId)
//     └─ BnText accuracy (Phase 11.2 / issue #169 — a SEPARATE trailing node that
//        surfaces GeolocationPosition.Accuracy as "acc:<metres>", so the round-tripped
//        accuracy is observable on-device). It is placed AFTER the echo deliberately:
//        each device suite selects the echo as "the first TextView-not-Button" (Android)
//        / "the first UILabel" (iOS), so a trailing node leaves those selectors resolving
//        to the echo, and the echo's "fix:lat,lng" shape is left UNCHANGED (both device
//        suites split it on ',' and require exactly two doubles). Accuracy is its own
//        self-describing, independently-assertable "acc:" line — invariant-formatted like
//        the coords (a locale decimal comma would collide with the split).
//
// DENIAL-AS-DATA, made visible: a denied Locate echoes "Denied" (or the exact
// tri-state) — it never throws and never leaves the echo blank waiting on a hang.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class BnGeolocationDemo : ComponentBase
{
    /// <summary>The echo prefix a Granted fix carries — distinctive so a stale
    /// echo is obvious, and so a test can assert the fix reached the component.</summary>
    internal const string FixPrefix = "fix:";

    /// <summary>The echo prefix a non-Granted outcome carries — the tri-state
    /// name follows it, proving denial arrived as DATA (not a throw, not a hang).</summary>
    internal const string StatusPrefix = "status:";

    /// <summary>The prefix the trailing accuracy line carries on a Granted fix —
    /// "acc:&lt;metres&gt;", invariant-formatted like the coords. Empty on any
    /// non-Granted outcome (denial-as-data: no fix ⇒ no accuracy to show).</summary>
    internal const string AccuracyPrefix = "acc:";

    private string _echo = "";
    private string _accuracy = "";

    [Inject] public IGeolocation Geo { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        b.OpenComponent<BnButton>(10);
        b.AddComponentParameter(11, nameof(BnButton.Label), "Locate");
        b.AddComponentParameter(12, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, LocateAsync));
        b.CloseComponent();

        b.OpenComponent<BnButton>(20);
        b.AddComponentParameter(21, nameof(BnButton.Label), "Check");
        b.AddComponentParameter(22, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, CheckAsync));
        b.CloseComponent();

        b.OpenComponent<BnText>(30);                             // the echo
        b.AddComponentParameter(31, nameof(BnText.Text), _echo);
        b.CloseComponent();

        b.OpenComponent<BnText>(40);                             // the accuracy line (trailing)
        b.AddComponentParameter(41, nameof(BnText.Text), _accuracy);
        b.CloseComponent();

        b.CloseElement();
    }

    // Locate runs the whole permission dance; the terminal outcome — a fix OR a
    // denial/restriction/unavailable/error — is echoed as DATA. Never throws.
    private async Task LocateAsync()
    {
        GeolocationResult result = await Geo.GetCurrentPositionAsync();
        // Coordinates are INVARIANT — a fix is not locale text (a comma decimal
        // separator would also collide with the lat,lng comma).
        if (result is { Status: GeolocationStatus.Granted, Position: { } p })
        {
            _echo = FormattableString.Invariant($"{FixPrefix}{p.Latitude},{p.Longitude}");
            // Accuracy on its own trailing line, invariant-formatted like the coords.
            _accuracy = FormattableString.Invariant($"{AccuracyPrefix}{p.Accuracy}");
        }
        else
        {
            _echo = $"{StatusPrefix}{result.Status}";
            // Denial-as-data: no Granted fix ⇒ no accuracy. Clear it so a stale value
            // never lingers under a fresh denial (the echo's same discipline).
            _accuracy = "";
        }
    }

    // Check reads the current permission WITHOUT prompting — the read-only path a
    // UI uses to show state before offering to locate.
    private async Task CheckAsync()
    {
        GeolocationStatus status = await Geo.CheckPermissionAsync();
        _echo = $"{StatusPrefix}{status}";
        // Check never yields a fix — keep the accuracy line clear.
        _accuracy = "";
    }
}
