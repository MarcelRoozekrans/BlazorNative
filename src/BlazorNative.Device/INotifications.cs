using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// INotifications — Phase 9.1 (M9 DoD #3): the app-facing, DI-injectable ergonomic
// facade over the permission-gated notification host calls. App code injects THIS,
// not the low-level IMobileBridge — a thin delegate (the IGeolocation sibling) in
// the SAME 7th package BlazorNative.Device. No 8th package: the device group was
// the whole reason BlazorNative.Device exists.
//
// The permission state machine is HOST-SIDE (denial-as-data): each call resolves
// with a NotificationStatus VALUE — never an exception, never a hang. schedule /
// show / cancel post/remove a local notification (a Route makes the tap open the
// app to that page — the tap-through target). RequestPermissionAsync may prompt;
// CheckPermissionAsync is read-only (no prompt) so a UI can SHOW the current state.
// ─────────────────────────────────────────────────────────────────────────────

public interface INotifications
{
    /// <summary>Schedules a local notification to fire at <see cref="NotificationSpec.When"/>
    /// (Android AlarmManager / iOS UNCalendar-or-interval trigger, host-side). Returns
    /// the terminal <see cref="NotificationStatus"/> — a denial is DATA, never a throw.</summary>
    ValueTask<NotificationStatus> ScheduleAsync(NotificationSpec spec, CancellationToken ct = default);

    /// <summary>Shows a local notification immediately. Returns the terminal
    /// <see cref="NotificationStatus"/> — a denial is DATA.</summary>
    ValueTask<NotificationStatus> ShowAsync(NotificationSpec spec, CancellationToken ct = default);

    /// <summary>Cancels a shown or scheduled notification by its app-chosen id
    /// (idempotent — an unknown id is a benign no-op that still statuses).</summary>
    ValueTask<NotificationStatus> CancelAsync(int id, CancellationToken ct = default);

    /// <summary>Requests notification permission (POST_NOTIFICATIONS on Android 13+,
    /// UNUserNotificationCenter authorization on iOS) — MAY prompt; short-circuits to
    /// <see cref="NotificationStatus.Granted"/> when already held (incl. Android's
    /// below-API-33 implicit grant, host-side).</summary>
    ValueTask<NotificationStatus> RequestPermissionAsync(CancellationToken ct = default);

    /// <summary>Reads the current notification permission WITHOUT prompting — for a
    /// UI that wants to show state before offering a "notify me" action.</summary>
    ValueTask<NotificationStatus> CheckPermissionAsync(CancellationToken ct = default);
}
