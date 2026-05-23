using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// WasiBridge
// Implements IMobileBridge using WASI extern imports.
// The native host (Android/iOS) exports these symbols into the WASM module.
//
// In AOT/WASI mode these become real extern calls via P/Invoke.
// In browser-WASM mode they call through JS interop shims.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class WasiBridge : IMobileBridge, IDisposable
{
    private readonly Subject<NativeEvent> _events = new();

    public IObservable<NativeEvent> NativeEvents => _events;

    // ── Navigation ────────────────────────────────────────────────────────────

    public ValueTask NavigateAsync(string route, CancellationToken ct = default)
    {
        var routeBytes = Encoding.UTF8.GetBytes(route);
        unsafe
        {
            fixed (byte* ptr = routeBytes)
                Native.shell_navigate(ptr, routeBytes.Length);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetCurrentRouteAsync(CancellationToken ct = default)
    {
        var buf = new byte[512];
        int len;
        unsafe { fixed (byte* ptr = buf) len = Native.shell_current_route(ptr, buf.Length); }
        return ValueTask.FromResult(Encoding.UTF8.GetString(buf, 0, len));
    }

    // ── Storage ───────────────────────────────────────────────────────────────

    public ValueTask<string?> ReadStorageAsync(string key, CancellationToken ct = default)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var buf = new byte[4096];
        int len;
        unsafe
        {
            fixed (byte* kPtr = keyBytes, vPtr = buf)
                len = Native.shell_storage_read(kPtr, keyBytes.Length, vPtr, buf.Length);
        }
        return ValueTask.FromResult(len < 0 ? null : (string?)Encoding.UTF8.GetString(buf, 0, len));
    }

    public ValueTask WriteStorageAsync(string key, string value, CancellationToken ct = default)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var valBytes = Encoding.UTF8.GetBytes(value);
        unsafe
        {
            fixed (byte* kPtr = keyBytes, vPtr = valBytes)
                Native.shell_storage_write(kPtr, keyBytes.Length, vPtr, valBytes.Length);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteStorageAsync(string key, CancellationToken ct = default)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        unsafe { fixed (byte* ptr = keyBytes) Native.shell_storage_delete(ptr, keyBytes.Length); }
        return ValueTask.CompletedTask;
    }

    // ── Network ───────────────────────────────────────────────────────────────

    public async ValueTask<BridgeHttpResponse> FetchAsync(BridgeHttpRequest request, CancellationToken ct = default)
    {
        var reqJson = JsonSerializer.Serialize(request);
        var reqBytes = Encoding.UTF8.GetBytes(reqJson);
        var resBuf = new byte[1024 * 64]; // 64KB response buffer
        int resLen;

        unsafe
        {
            fixed (byte* reqPtr = reqBytes, resPtr = resBuf)
                resLen = Native.shell_fetch(reqPtr, reqBytes.Length, resPtr, resBuf.Length);
        }

        if (resLen < 0)
            return new BridgeHttpResponse(0, "fetch failed", new Dictionary<string, string>());

        var json = Encoding.UTF8.GetString(resBuf, 0, resLen);
        return JsonSerializer.Deserialize<BridgeHttpResponse>(json);
    }

    // ── Platform info ─────────────────────────────────────────────────────────

    public ValueTask<PlatformInfo> GetPlatformInfoAsync(CancellationToken ct = default)
    {
        var buf = new byte[256];
        int len;
        unsafe { fixed (byte* ptr = buf) len = Native.shell_platform_info(ptr, buf.Length); }
        var json = Encoding.UTF8.GetString(buf, 0, len);
        return ValueTask.FromResult(JsonSerializer.Deserialize<PlatformInfo>(json));
    }

    // ── Called by native host to push events into WASM ────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "blazornative_dispatch_event")]
    public static unsafe void DispatchEvent(byte* namePtr, int nameLen, byte* payloadPtr, int payloadLen)
    {
        // Note: this is the WASM export the native host calls
        var name = Encoding.UTF8.GetString(namePtr, nameLen);
        var payload = payloadLen > 0 ? Encoding.UTF8.GetString(payloadPtr, payloadLen) : null;
        Current?._events.OnNext(new NativeEvent(name, payload));
    }

    // Singleton access for the unmanaged callback
    internal static WasiBridge? Current { get; private set; }

    public WasiBridge() => Current = this;
    public void Dispose() { Current = null; _events.Dispose(); }
}

// ─────────────────────────────────────────────────────────────────────────────
// Native extern declarations  (the WASM imports — provided by the host)
// ─────────────────────────────────────────────────────────────────────────────

internal static unsafe class Native
{
    private const string Lib = "mobile_bridge"; // WASM import module name

    [DllImport(Lib)] internal static extern void   shell_navigate(byte* routePtr, int routeLen);
    [DllImport(Lib)] internal static extern int    shell_current_route(byte* buf, int bufLen);
    [DllImport(Lib)] internal static extern int    shell_storage_read(byte* keyPtr, int keyLen, byte* valBuf, int valBufLen);
    [DllImport(Lib)] internal static extern void   shell_storage_write(byte* keyPtr, int keyLen, byte* valPtr, int valLen);
    [DllImport(Lib)] internal static extern void   shell_storage_delete(byte* keyPtr, int keyLen);
    [DllImport(Lib)] internal static extern int    shell_fetch(byte* reqPtr, int reqLen, byte* resBuf, int resBufLen);
    [DllImport(Lib)] internal static extern int    shell_platform_info(byte* buf, int bufLen);
}
