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

    // ── Biometrics + secure storage (Phase 9.2 — the headless lane) ───────────
    //
    // The CENTRAL headless proof of denial-as-data for BOTH capabilities, NO device:
    // a settable biometric auth-result drives authenticate (all six statuses) and
    // GATES the pairing (getWithAuth returns the value only when the auth-result is
    // Authenticated, else AuthFailed), and an in-memory secret dict honours
    // set/get/delete AND the per-key requireAuth flag. So the headless lane drives
    // every status — Authenticated/Failed/Cancelled/Unavailable/LockedOut and
    // Ok/NotFound/AuthFailed/Unavailable/Error — and the pairing, within a bounded
    // await, before any device work. This is a SEPARATE store from the plain
    // unencrypted _storage above (the M5-deferred secure variant, closed here) — the
    // plain slots are untouched.

    /// <summary>The status the next <see cref="AuthenticateAsync"/> returns AND the
    /// gate the pairing (<see cref="GetSecretWithAuthAsync"/>) checks: an auth-bound
    /// secret is released only when this is <see cref="Core.BiometricStatus.Authenticated"/>,
    /// else the read is <see cref="SecureStorageStatus.AuthFailed"/>. Default
    /// Authenticated. Set it to drive a failed / cancelled / locked-out / no-hardware
    /// path headless.</summary>
    public BiometricStatus BiometricAuthResult { get; set; } = BiometricStatus.Authenticated;

    private readonly Dictionary<string, (string Value, bool RequireAuth)> _secrets = new();

    /// <summary>Snapshot of the in-memory secret store — useful in tests.</summary>
    public IReadOnlyDictionary<string, (string Value, bool RequireAuth)> SecretSnapshot => _secrets;

    public ValueTask<BiometricStatus> AuthenticateAsync(string reason, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Denial-as-data: a failed / cancelled / locked-out / no-hardware auth
        // RETURNS its status — it does NOT throw and does NOT hang.
        Console.WriteLine($"[DevBridge] Biometric.Authenticate → {BiometricAuthResult}");
        return ValueTask.FromResult(BiometricAuthResult);
    }

    public ValueTask<BiometricStatus> IsBiometricAvailableAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // The read-only availability check: Unavailable when the mock says biometrics
        // are absent, otherwise Authenticated ("present + enrolled + ready").
        BiometricStatus available = BiometricAuthResult == BiometricStatus.Unavailable
            ? BiometricStatus.Unavailable
            : BiometricStatus.Authenticated;
        return ValueTask.FromResult(available);
    }

    public ValueTask<SecureStorageStatus> SetSecretAsync(string key, string value, bool requireAuth, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // The soft 8 KB cap enforced at the .NET boundary — an oversize value RETURNS
        // a status, never crashes (the on-device NativeShellBridge enforces the same).
        if (System.Text.Encoding.UTF8.GetByteCount(value) > SecretResult.MaxValueBytes)
        {
            Console.WriteLine($"[DevBridge] SecureStorage.Set {key} → Error (oversize)");
            return ValueTask.FromResult(SecureStorageStatus.Error);
        }
        _secrets[key] = (value, requireAuth);
        Console.WriteLine($"[DevBridge] SecureStorage.Set {key} (requireAuth={requireAuth}) → Ok");
        return ValueTask.FromResult(SecureStorageStatus.Ok);
    }

    public ValueTask<SecretResult> GetSecretAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Plain get: an auth-bound item read WITHOUT the prompt correctly fails
        // AuthFailed (the OS-key would refuse the plaintext); an absent key is NotFound.
        if (!_secrets.TryGetValue(key, out var entry))
            return ValueTask.FromResult(new SecretResult(SecureStorageStatus.NotFound, null));
        if (entry.RequireAuth)
            return ValueTask.FromResult(new SecretResult(SecureStorageStatus.AuthFailed, null));
        return ValueTask.FromResult(new SecretResult(SecureStorageStatus.Ok, entry.Value));
    }

    public ValueTask<SecretResult> GetSecretWithAuthAsync(string key, string reason, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // THE PAIRING, honoured headless: getWithAuth returns the value only when the
        // mocked biometric gate is Authenticated; a denied gate returns AuthFailed
        // (no value) — the "getWithAuth must honour requireAuth" contract the bypass
        // mutation breaks. An absent key is NotFound regardless of the gate.
        if (!_secrets.TryGetValue(key, out var entry))
            return ValueTask.FromResult(new SecretResult(SecureStorageStatus.NotFound, null));
        if (BiometricAuthResult != BiometricStatus.Authenticated)
            return ValueTask.FromResult(new SecretResult(SecureStorageStatus.AuthFailed, null));
        return ValueTask.FromResult(new SecretResult(SecureStorageStatus.Ok, entry.Value));
    }

    public ValueTask<SecureStorageStatus> DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _secrets.Remove(key); // idempotent — deleting an absent key still statuses Ok
        Console.WriteLine($"[DevBridge] SecureStorage.Delete {key} → Ok");
        return ValueTask.FromResult(SecureStorageStatus.Ok);
    }

    // ── Camera (Phase 9.3 — the headless capture lane) ────────────────────────
    //
    // A settable capture RESULT + a canned test-image path so tests drive every
    // status — Captured (returns the canned path + known dims), Cancelled, Denied,
    // Unavailable, Error — with NO device and NO camera, the central proof of
    // denial/cancel-as-data. The canned path is a REAL file:// URI (a tiny fixture
    // JPEG the harness writes to a temp path), so the headless lane can also exercise
    // the BnImage-consumes-the-capture composition (a captured path becomes a valid
    // BnImage.Src — §6). A non-Captured result carries NO path and zero dims — a
    // cancel that carried a path would be the named mutation (a cancel has no file).

    /// <summary>A tiny 1×1 JPEG the harness writes ONCE to a temp path, so the
    /// Captured result names a real file:// URI a BnImage can take as its Src. The
    /// bytes are a valid minimal JPEG; the path (not the bytes) is the handoff.</summary>
    private const string CannedImageBase64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRof" +
        "Hh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAAB" +
        "AAAAAAAAAAAAAAAAAAAAAP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==";

    private static readonly Lazy<(string Uri, long Bytes)> s_cannedImage = new(WriteCannedImage);

    private static (string Uri, long Bytes) WriteCannedImage()
    {
        byte[] bytes = Convert.FromBase64String(CannedImageBase64);
        string path = Path.Combine(Path.GetTempPath(), "blazornative-devhost-capture.jpg");
        File.WriteAllBytes(path, bytes);
        return (new Uri(path).AbsoluteUri, bytes.LongLength); // file:///…
    }

    /// <summary>The status the next <see cref="CapturePhotoAsync"/> returns (default
    /// <see cref="Core.CameraStatus.Captured"/>) AND what
    /// <see cref="CheckCameraAvailabilityAsync"/> reports (Unavailable → Unavailable,
    /// else Captured = "present + usable"). Set it to drive a cancel / denied /
    /// unavailable / error path headless.</summary>
    public CameraStatus CameraCaptureResult { get; set; } = CameraStatus.Captured;

    /// <summary>The file:// path a Captured result returns (default the canned
    /// fixture). Settable so a test can point the composition at another path.</summary>
    public string? CameraCapturedPath { get; set; } = s_cannedImage.Value.Uri;

    /// <summary>The final dimensions / size a Captured result reports (default the
    /// fixture's). Ignored for every non-Captured status.</summary>
    public int CameraCapturedWidth { get; set; } = 1;
    public int CameraCapturedHeight { get; set; } = 1;
    public long CameraCapturedSizeBytes { get; set; } = s_cannedImage.Value.Bytes;

    public ValueTask<PhotoResult> CapturePhotoAsync(CaptureOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Denial-as-data: a cancelled / denied / unavailable / error capture RETURNS
        // its status with NO path and zero dims — it does NOT throw and does NOT hang.
        // Only a Captured result carries the path + dims (a cancel carries NO path).
        if (CameraCaptureResult != CameraStatus.Captured)
        {
            Console.WriteLine($"[DevBridge] Camera.Capture → {CameraCaptureResult}");
            return ValueTask.FromResult(new PhotoResult(CameraCaptureResult, null, 0, 0, 0));
        }
        Console.WriteLine($"[DevBridge] Camera.Capture → Captured {CameraCapturedPath}");
        return ValueTask.FromResult(new PhotoResult(
            CameraStatus.Captured, CameraCapturedPath,
            CameraCapturedWidth, CameraCapturedHeight, CameraCapturedSizeBytes));
    }

    public ValueTask<CameraStatus> CheckCameraAvailabilityAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // The read-only availability check: Unavailable when the mock says there is no
        // camera, otherwise Captured ("present + usable" — no capture ran).
        CameraStatus available = CameraCaptureResult == CameraStatus.Unavailable
            ? CameraStatus.Unavailable
            : CameraStatus.Captured;
        return ValueTask.FromResult(available);
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
                // Phase 11.4: this ONE site migrates even though DevHostBridge's 18
                // Console.Write* calls do not. Those are the dev host's UI — its
                // stdout IS the product. This is a DIAGNOSTIC on a fault path, the
                // exact twin of NativeShellBridge's and NativeNavigationManager's
                // subscriber-threw lines, and leaving it behind would make the drift
                // pin exempt a whole file to protect one line that belongs in the seam.
                BnLog.Error("NativeEvents", "subscriber threw", ex);
            }
        }
    }
}
