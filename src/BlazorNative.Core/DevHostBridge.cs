namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// DevHostBridge
// A full in-process mock of IMobileBridge for local development.
// Runs as a normal .NET app — no NativeAOT publish needed during the inner loop.
//
// Features:
//   • In-memory key/value store (hot-reload safe)
//   • Route history tracking
//   • HTTP passthrough via HttpClient (real network)
//   • Console-logged native event injection via InjectEvent (in the WASM era
//     the browser DevHost exposed this over a /dev/event REST endpoint; that
//     host is gone — tests and harnesses call InjectEvent directly)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DevHostBridge : IMobileBridge, IDisposable
{
    private readonly Dictionary<string, string> _storage = new();
    private Action<NativeEvent>? _events;
    // BN0011 pragma justification: DevHostBridge IS the bridge — its FetchAsync
    // passthrough is the dev-host implementation of IMobileBridge.FetchAsync,
    // so a real socket-backed HttpClient is exactly right here; there is no
    // host underneath it to ride.
#pragma warning disable BN0011
    private readonly HttpClient _http = new();
#pragma warning restore BN0011
    private readonly List<string> _routeHistory = new();
    private string _currentRoute = "/";

    public event Action<NativeEvent>? NativeEvents
    {
        add    => _events += value;
        remove => _events -= value;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    public ValueTask NavigateAsync(string route, CancellationToken ct = default)
    {
        _routeHistory.Add(_currentRoute);
        _currentRoute = route;
        Console.WriteLine($"[DevBridge] Navigate → {route}");
        RaiseNativeEvent(new NativeEvent("navigation", route));
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetCurrentRouteAsync(CancellationToken ct = default)
        => ValueTask.FromResult(_currentRoute);

    // ── Storage ───────────────────────────────────────────────────────────────

    public ValueTask<string?> ReadStorageAsync(string key, CancellationToken ct = default)
    {
        _storage.TryGetValue(key, out var val);
        Console.WriteLine($"[DevBridge] Storage.Read  {key} → {val ?? "<null>"}");
        return ValueTask.FromResult(val);
    }

    public ValueTask WriteStorageAsync(string key, string value, CancellationToken ct = default)
    {
        _storage[key] = value;
        Console.WriteLine($"[DevBridge] Storage.Write {key} = {value}");
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteStorageAsync(string key, CancellationToken ct = default)
    {
        _storage.Remove(key);
        Console.WriteLine($"[DevBridge] Storage.Delete {key}");
        return ValueTask.CompletedTask;
    }

    // ── Network ───────────────────────────────────────────────────────────────

    public async ValueTask<BridgeHttpResponse> FetchAsync(BridgeHttpRequest request, CancellationToken ct = default)
    {
        Console.WriteLine($"[DevBridge] Fetch {request.Method} {request.Url}");

        var req = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        if (request.Body is not null)
            req.Content = new StringContent(request.Body);
        if (request.Headers is not null)
            foreach (var (k, v) in request.Headers)
                req.Headers.TryAddWithoutValidation(k, v);

        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        return new BridgeHttpResponse(
            (int)res.StatusCode,
            body,
            res.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)));
    }

    // ── Clipboard + Share (Phase 5.4 — in-memory mock) ────────────────────────

    private string _clipboard = "";

    public ValueTask<string> ClipboardReadAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"[DevBridge] Clipboard.Read → {_clipboard}");
        return ValueTask.FromResult(_clipboard);
    }

    public ValueTask ClipboardWriteAsync(string text, CancellationToken ct = default)
    {
        _clipboard = text;
        Console.WriteLine($"[DevBridge] Clipboard.Write = {text}");
        return ValueTask.CompletedTask;
    }

    public ValueTask ShareAsync(string text, CancellationToken ct = default)
    {
        Console.WriteLine($"[DevBridge] Share → {text}");
        return ValueTask.CompletedTask;
    }

    /// <summary>Snapshot of the in-memory clipboard — useful in tests.</summary>
    public string ClipboardSnapshot => _clipboard;

    // ── Geolocation (Phase 9.0 — the headless tri-state lane) ─────────────────
    //
    // A configurable status + position so tests drive ALL SIX statuses (granted /
    // denied / permanent / restricted / unavailable / error) with NO device — the
    // central proof of the named risk: denial RETURNS a status, never throws, never
    // hangs. Mirrors how the on-device NativeShellBridge resolves the tri-state, but
    // in-process and instant (no prompt, no suspension).

    /// <summary>The status the next <see cref="GetCurrentPositionAsync"/> returns
    /// (default <see cref="GeolocationStatus.Granted"/>). Set it to drive a denial /
    /// restriction / unavailable / error path headless.</summary>
    public GeolocationStatus GeolocationStatus { get; set; } = GeolocationStatus.Granted;

    /// <summary>The fix returned when <see cref="GeolocationStatus"/> is Granted
    /// (Amsterdam by default). Ignored for every non-Granted status.</summary>
    public GeolocationPosition GeolocationPosition { get; set; } =
        new(Latitude: 52.3702, Longitude: 4.8952, Accuracy: 12.0, Altitude: 3.0, TimestampUnixMs: 0);

    public ValueTask<GeolocationResult> GetCurrentPositionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Denial-as-data: a non-Granted status resolves the task with a status and a
        // null position — it does NOT throw and does NOT hang.
        GeolocationResult result = GeolocationStatus == GeolocationStatus.Granted
            ? new GeolocationResult(GeolocationStatus.Granted, GeolocationPosition)
            : new GeolocationResult(GeolocationStatus, null);
        Console.WriteLine($"[DevBridge] Geolocation → {result.Status}");
        return ValueTask.FromResult(result);
    }

    public ValueTask<GeolocationStatus> CheckGeolocationPermissionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GeolocationStatus);
    }

    // ── Notifications (Phase 9.1 — the headless five-status lane) ─────────────
    //
    // A configurable status so tests drive ALL FIVE statuses (granted / denied /
    // permanent / restricted / error) with NO device — the central proof of the
    // named risk: denial RETURNS a status, never throws, never hangs. Scheduled /
    // shown notifications are recorded in an in-memory list, and cancel removes by
    // id (idempotent — cancelling an unknown id is a no-op that still statuses),
    // so the headless lane mirrors the on-device schedule/show/cancel bookkeeping
    // without a NotificationManager.

    /// <summary>The status the next notification op returns (default
    /// <see cref="Core.NotificationStatus.Granted"/>). Set it to drive a denial /
    /// restriction / error path headless.</summary>
    public NotificationStatus NotificationStatus { get; set; } = NotificationStatus.Granted;

    /// <summary>The notifications recorded by <see cref="ShowNotificationAsync"/> /
    /// <see cref="ScheduleNotificationAsync"/> and not yet cancelled — useful in
    /// tests to assert schedule/show/cancel bookkeeping headless.</summary>
    public IReadOnlyList<NotificationSpec> Notifications => _notifications;
    private readonly List<NotificationSpec> _notifications = new();

    public ValueTask<NotificationStatus> ScheduleNotificationAsync(NotificationSpec spec, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return RecordNotification(spec, scheduled: true);
    }

    public ValueTask<NotificationStatus> ShowNotificationAsync(NotificationSpec spec, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return RecordNotification(spec, scheduled: false);
    }

    private ValueTask<NotificationStatus> RecordNotification(NotificationSpec spec, bool scheduled)
    {
        // Only a Granted op actually posts — every non-Granted status is the
        // status ALONE (denial-as-data), and it records nothing, exactly as the
        // on-device host would decline to post without permission.
        if (NotificationStatus == NotificationStatus.Granted)
        {
            _notifications.RemoveAll(n => n.Id == spec.Id); // collisions replace (notify semantics)
            _notifications.Add(spec);
        }
        Console.WriteLine($"[DevBridge] Notification.{(scheduled ? "Schedule" : "Show")} id={spec.Id} → {NotificationStatus}");
        return ValueTask.FromResult(NotificationStatus);
    }

    public ValueTask<NotificationStatus> CancelNotificationAsync(int id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _notifications.RemoveAll(n => n.Id == id); // idempotent — an unknown id is a benign no-op
        Console.WriteLine($"[DevBridge] Notification.Cancel id={id} → {NotificationStatus}");
        return ValueTask.FromResult(NotificationStatus);
    }

    public ValueTask<NotificationStatus> RequestNotificationPermissionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(NotificationStatus);
    }

    public ValueTask<NotificationStatus> CheckNotificationPermissionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(NotificationStatus);
    }

    // ── Platform info ─────────────────────────────────────────────────────────

    public string PlatformInfo =>
        $$"""{"kind":"DevHost","os":"{{Environment.OSVersion}}","version":"0.1.0-dev","isDebug":true}""";

    public ValueTask<PlatformInfo> GetPlatformInfoAsync(CancellationToken ct = default)
        => ValueTask.FromResult(new PlatformInfo(
            PlatformKind.DevHost,
            Environment.OSVersion.ToString(),
            "0.1.0-dev",
            IsDebug: true));

    // ── Dev tools ─────────────────────────────────────────────────────────────

    /// <summary>Inject a native event programmatically — use from tests or DevTools UI.</summary>
    public void InjectEvent(string name, string? payload = null)
    {
        Console.WriteLine($"[DevBridge] InjectEvent  {name}  payload={payload ?? "<none>"}");
        RaiseNativeEvent(new NativeEvent(name, payload));
    }

    /// <summary>Snapshot of current storage — useful in tests.</summary>
    public IReadOnlyDictionary<string, string> StorageSnapshot => _storage;

    /// <summary>Full navigation history.</summary>
    public IReadOnlyList<string> RouteHistory => _routeHistory;

    public void Dispose() { _http.Dispose(); }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RaiseNativeEvent(NativeEvent evt)
    {
        if (_events is null) return;
        foreach (var handler in _events.GetInvocationList())
        {
            try { ((Action<NativeEvent>)handler)(evt); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[NativeEvents] subscriber threw: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
