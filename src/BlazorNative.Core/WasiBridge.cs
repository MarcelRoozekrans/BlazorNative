using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ZeroAlloc.Inject;

namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// WasiBridge
// Implements IMobileBridge using WASI extern imports.
// The native host (Android/iOS) exports these symbols into the WASM module.
//
// In AOT/WASI mode these become real extern calls via P/Invoke.
// In browser-WASM mode they call through JS interop shims.
// ─────────────────────────────────────────────────────────────────────────────

[Singleton(As = typeof(IMobileBridge))]
public sealed class WasiBridge : IMobileBridge, IDisposable
{
    private Action<NativeEvent>? _events;

    public event Action<NativeEvent>? NativeEvents
    {
        add    => _events += value;
        remove => _events -= value;
    }

    // ── Navigation ────────────────────────────────────────────────────────────
    //
    // Phase 2.3 scope-cut (per docs/plans/2026-05-26-phase-2.3-design.md Q5):
    // only shell-platform-info is WIT-typed + host-satisfied in this phase. The
    // 6 other shell-* imports were Phase 1 [DllImport] dead code (Mono-AOT
    // trimmed them — Phase 2.1.0 spike) and are deferred per docs/BACKLOG.md.
    // Each lands additively in the phase that needs it (navigate/storage =
    // Phase 2.5, fetch = M4 unless an earlier demo needs it).
    //
    // Until then these methods throw — calling them would have trapped at
    // runtime anyway since the .wasm no longer declares those imports.

    public ValueTask NavigateAsync(string route, CancellationToken ct = default)
        => throw new NotImplementedException("shell-navigate is deferred to Phase 2.5 — see docs/BACKLOG.md.");

    public ValueTask<string> GetCurrentRouteAsync(CancellationToken ct = default)
        => throw new NotImplementedException("shell-current-route is deferred to Phase 2.5 — see docs/BACKLOG.md.");

    // ── Storage ───────────────────────────────────────────────────────────────

    public ValueTask<string?> ReadStorageAsync(string key, CancellationToken ct = default)
        => throw new NotImplementedException("shell-storage-read is deferred to Phase 2.5+ — see docs/BACKLOG.md.");

    public ValueTask WriteStorageAsync(string key, string value, CancellationToken ct = default)
        => throw new NotImplementedException("shell-storage-write is deferred to Phase 2.5+ — see docs/BACKLOG.md.");

    public ValueTask DeleteStorageAsync(string key, CancellationToken ct = default)
        => throw new NotImplementedException("shell-storage-delete is deferred to Phase 2.5+ — see docs/BACKLOG.md.");

    // ── Network ───────────────────────────────────────────────────────────────

    public ValueTask<BridgeHttpResponse> FetchAsync(BridgeHttpRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("shell-fetch is deferred to M4 (or earlier if a demo needs it) — see docs/BACKLOG.md.");

    // ── Platform info ─────────────────────────────────────────────────────────
    //
    // Phase 2.3 (revised — see docs/plans/2026-05-26-phase-2.3-design-revision.md):
    // host passes platform-info JSON via the BLAZOR_PLATFORM_INFO env var. We
    // read it via standard System.Environment.GetEnvironmentVariable (which
    // Mono-WASI implements via the wasi:cli/environment.get-environment
    // interface — pre-included by wasi-experimental's component-adapter).
    //
    // Why env-var instead of the original Phase 2.3 WIT-typed import design:
    // Task 2's spike (commit 3aa83c9) found three wasi-experimental SDK gaps
    // that block custom WIT imports from materializing in the .wasm. Env vars
    // ride on the STANDARD wasi:cli/environment surface that wasi-experimental
    // already supports — zero SDK gaps. Trade-off: one-way (host → .NET) and
    // initialization-time only, which is correct semantics for platform-info
    // anyway (the OS doesn't change at runtime).
    //
    // The 6 other Phase 1 mobile_bridge imports stay deferred per the revised
    // design (and per docs/BACKLOG.md). Future dynamic bridges (e.g., button-
    // tap event callbacks in Phase 2.5+) will use a different mechanism —
    // most likely the export-based request/response pattern.

    /// <summary>Host-provided platform-info JSON. Set by the WasiHost facade
    /// via wasi_config_set_env before instantiation. Used by the Phase 2.3
    /// self-test marker (`[BOOT] bridge-ok platform-info=&lt;json&gt;`) in
    /// WasiEntryPoint.cs and by the async wrapper below.</summary>
    public string PlatformInfo =>
        Environment.GetEnvironmentVariable("BLAZOR_PLATFORM_INFO") ?? "{}";

    public ValueTask<PlatformInfo> GetPlatformInfoAsync(CancellationToken ct = default)
    {
        var json = PlatformInfo;
        return ValueTask.FromResult(
            JsonSerializer.Deserialize(json, BridgeJsonContext.Default.PlatformInfo));
    }

    // ── Called by native host to push events into WASM ────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "blazornative_dispatch_event")]
    public static unsafe void DispatchEventNative(byte* namePtr, int nameLen, byte* payloadPtr, int payloadLen)
    {
        var name = Encoding.UTF8.GetString(namePtr, nameLen);
        var payload = payloadLen > 0 ? Encoding.UTF8.GetString(payloadPtr, payloadLen) : null;
        DispatchEventCore(name, payload);
    }

    /// <summary>Managed-callable entry into the bridge event multicast. Invoked by
    /// DispatchEventNative from the native shell, and by WasiEntryPoint.cs's
    /// self-test during boot.
    ///
    /// Trim-root: kept alive via [DynamicDependency(All, typeof(WasiBridge))] on
    /// Program.Main (see src/BlazorNative.WasiHost/WasiEntryPoint.cs). If Main's
    /// attribute is ever removed, this method's IL may be stripped — the Phase 1.3
    /// ExportSmoke test will catch the regression (the blazornative_dispatch_event
    /// export disappears too, since it's static-reachable only from this assembly).
    ///
    /// Subscribers MUST complete synchronously per the Phase 2.0 sync-contract
    /// decision — see docs/plans/2026-05-25-phase-2.0-design.md.</summary>
    internal static void DispatchEventCore(string name, string? payload)
    {
        if (Current is not { } c || c._events is null) return;

        var evt = new NativeEvent(name, payload);
        // Multicast manually so one subscriber's exception doesn't strand later
        // subscribers in the invocation list. Log + continue.
        foreach (var handler in c._events.GetInvocationList())
        {
            try { ((Action<NativeEvent>)handler)(evt); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[NativeEvents] subscriber threw: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // Singleton access for the unmanaged callback
    internal static WasiBridge? Current { get; private set; }

    public WasiBridge() => Current = this;
    public void Dispose() { Current = null; }
}
