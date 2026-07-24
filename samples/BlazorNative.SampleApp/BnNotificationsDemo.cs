using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Device;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// BnNotificationsDemo — Phase 9.1 (M9 DoD #3): the routed page that proves the
// permission-gated notifications surface reaches a mounted component AND is the
// tap-through LANDING page. The worked example of the permission pattern's FIRST
// reuse — app code injects the ergonomic INotifications FACADE (from the 7th
// package, BlazorNative.Device), not the low-level IMobileBridge — exactly as
// BnGeolocationDemo did for geolocation.
//
// It mounts the SAME component on all three surfaces — .NET
// (BnNotificationsDemoTests via DispatchEventCore, with a DevHostBridge / FakeShell
// driving all five statuses), and — at Gates 2/3 — the AVD (real NotificationManager
// post + POST_NOTIFICATIONS) and the iOS simulator (UNUserNotificationCenter). The
// .NET/wire half + the demo live here; Gates 2/3 wire the shells' real post.
//
// TAP-THROUGH LANDS HERE: the notification's route is "/notifications", so tapping
// it opens the app ON this page (cold: cold launch to the route; warm:
// host_event("navigate","/notifications") → NavigateToAsync re-route). The arrival
// IS the proof — this page mounting via the tap is the observable. On mount the
// echo shows an ARRIVAL marker carrying the current route, so a tap that lands here
// is visible, and denial-as-data is made visible too: a denied Show/Schedule/Cancel
// echoes the tri-state — never thrown, never left hanging.
//
// Shape:
//   root div
//     ├─ BnButton "Show"     → ShowAsync (Route="/notifications")      → echo status
//     ├─ BnButton "Schedule" → ScheduleAsync (When: now+5s, Route=…)   → echo status
//     ├─ BnButton "Cancel"   → CancelAsync(id)                          → echo status
//     ├─ BnText echo (mount-pinned text node — the ClipboardProbe echo contract)
//     └─ BnButton "← Back" → INavigationManager.NavigateToAsync("/")   (#204 — nav
//        parity with the eight pages that already carried one; TRAILING so the device
//        suites' "first TextView/UILabel that is not a Button" echo selectors still
//        resolve to the echo)
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class BnNotificationsDemo : ComponentBase
{
    /// <summary>The app-chosen notification id this demo schedules/shows/cancels —
    /// a stable int so cancel targets the same identity across all three ops.</summary>
    internal const int DemoId = 7;

    /// <summary>The tap-through route — this page. A notification carrying it opens
    /// the app HERE, which is the whole proof of tap-through.</summary>
    internal const string Route = "/notifications";

    /// <summary>The echo prefix a returned <see cref="NotificationStatus"/> carries —
    /// distinctive so a stale echo is obvious and a denial is provably DATA.</summary>
    internal const string StatusPrefix = "status:";

    /// <summary>The echo prefix the mount-time ARRIVAL marker carries, followed by
    /// the current route — a tap that lands here shows "arrived:/notifications".</summary>
    internal const string ArrivedPrefix = "arrived:";

    // The arrival marker is the mount-time echo: this IS the "/notifications" page,
    // so its mere mounting — via a tap (cold launch to the route, or a warm
    // "navigate" re-route) — is the tap-through proof, and the marker names the
    // route it landed on. Reading the page's own Route constant (not the nav
    // manager) keeps it deterministic: CurrentRoute is only synced AFTER the mount
    // render for a by-name mount, so a live read here would be stale.
    private string _echo = $"{ArrivedPrefix}{Route}";

    [Inject] public INotifications Notifications { get; set; } = default!;

    /// <summary>#204: the navigation service, for the trailing "← Back" — the same
    /// explicit [Inject] public property every other page uses.</summary>
    [Inject] public INavigationManager Navigation { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        b.OpenComponent<BnButton>(10);
        b.AddComponentParameter(11, nameof(BnButton.Label), "Show");
        b.AddComponentParameter(12, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, ShowAsync));
        b.CloseComponent();

        b.OpenComponent<BnButton>(20);
        b.AddComponentParameter(21, nameof(BnButton.Label), "Schedule");
        b.AddComponentParameter(22, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, ScheduleAsync));
        b.CloseComponent();

        b.OpenComponent<BnButton>(30);
        b.AddComponentParameter(31, nameof(BnButton.Label), "Cancel");
        b.AddComponentParameter(32, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, CancelAsync));
        b.CloseComponent();

        b.OpenComponent<BnText>(40);                             // the echo
        b.AddComponentParameter(41, nameof(BnText.Text), _echo);
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

    // Each op echoes its returned NotificationStatus as DATA — a denial is SHOWN,
    // never thrown, never left hanging (the BnGeolocationDemo discipline).
    private async Task ShowAsync()
    {
        NotificationStatus status = await Notifications.ShowAsync(
            new NotificationSpec(DemoId, "Hello", "A local notification", When: null, Route: Route));
        _echo = $"{StatusPrefix}{status}";
    }

    private async Task ScheduleAsync()
    {
        NotificationStatus status = await Notifications.ScheduleAsync(
            new NotificationSpec(DemoId, "Hello (soon)", "A scheduled notification",
                When: DateTimeOffset.UtcNow.AddSeconds(5), Route: Route));
        _echo = $"{StatusPrefix}{status}";
    }

    private async Task CancelAsync()
    {
        NotificationStatus status = await Notifications.CancelAsync(DemoId);
        _echo = $"{StatusPrefix}{status}";
    }
}
