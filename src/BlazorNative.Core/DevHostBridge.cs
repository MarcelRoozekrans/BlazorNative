namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// DevHostBridge
// A full in-process mock of IMobileBridge for local development.
// Runs as a normal .NET app — no WASM compilation needed during the inner loop.
//
// Features:
//   • In-memory key/value store (hot-reload safe)
//   • Route history tracking
//   • HTTP passthrough via HttpClient (real network)
//   • Console-logged native event injection
//   • DevTools endpoint: POST http://localhost:5273/dev/event  { name, payload }
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DevHostBridge : IMobileBridge, IDisposable
{
    private readonly Dictionary<string, string> _storage = new();
    private Action<NativeEvent>? _events;
    private readonly HttpClient _http = new();
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

    // ── Platform info ─────────────────────────────────────────────────────────

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
