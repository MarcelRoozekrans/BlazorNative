using System.Diagnostics.CodeAnalysis;
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
    // Phase 2.3 Task 2: host-satisfied import via core-shape [DllImport]. See
    // the Native class declaration below for the empirical findings that drove
    // the shape choice (Option B core-shape, NOT Option A's WIT-typed Library
    // path). The .wasm currently does NOT declare this import end-to-end —
    // Task 5 ships the SDK glue + JNA host registration that makes the
    // round-trip real. Until then, calls trap with DllNotFoundException at
    // runtime; the trim-root call in Program.Main wraps the trap in try/catch.

    /// <summary>Synchronous bridge call — fetches the host's platform-info JSON
    /// into a stack buffer + decodes UTF-8. Used by the Phase 2.3 self-test
    /// marker (`[BOOT] bridge-ok platform-info=&lt;json&gt;`) in WasiEntryPoint.cs
    /// and by the async wrapper below.</summary>
    public unsafe string PlatformInfo
    {
        get
        {
            const int BufSize = 1024;
            var buf = stackalloc byte[BufSize];
            var len = Native.ShellPlatformInfo(buf, BufSize);
            if (len <= 0) return string.Empty;
            return Encoding.UTF8.GetString(buf, Math.Min(len, BufSize));
        }
    }

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

    // ─────────────────────────────────────────────────────────────────────────
    // Native extern declarations  (the WASM imports — provided by the host)
    //
    // Phase 2.3 Task 2: ONE [DllImport] — replaces 7 [DllImport]s from Phase 1
    // (all dead code per Phase 2.1.0 trim finding). The other 6 shell-* imports
    // land additively in the downstream phase that needs each per
    // docs/BACKLOG.md.
    //
    // We tried Option A first per the Phase 2.3 design: a WIT-typed shape with
    // [LibraryImport("blazornative:mobile-bridge/bridge", EntryPoint =
    // "shell-platform-info", StringMarshalling = Utf8)] returning string. The
    // C# compiled and the source-generator emitted the marshalling stub, but
    // Mono-AOT's pinvoke scanner doesn't recognize the WIT-style Library path
    // — empirically verified 2026-05-26: no entry in pinvoke-table.h, no WASM
    // import declared. wasi-experimental does NOT consume user .wit files
    // (Task 1 finding — commit 25efdb2), so the WIT-typed import never lowers
    // to a component-import emission.
    //
    // Settled on Option B: core-shape [DllImport("mobile_bridge",
    // EntryPoint="shell_platform_info")] with byte* + int buffer marshaling,
    // matching Phase 1's convention. When the toolchain glue lands (Task 5
    // adds wit-component invocation + the three SDK hacks documented in
    // BlazorNative.WasiHost.csproj), the .wasm will declare a core import
    // ((import "mobile_bridge" "shell_platform_info" (func (param i32 i32)
    // (result i32)))) that the JNA host satisfies by registering against the
    // core module name "mobile_bridge".
    //
    // [DllImport] (not [LibraryImport]) is intentional: Mono-AOT's pinvoke
    // scanner only recognizes DllImport-shape attributes for wasi-wasm. The
    // [LibraryImport] source-generator stub IS invisible to the scanner —
    // empirically verified 2026-05-26 (no shell_platform_info in
    // pinvoke-table.h with LibraryImport, present with DllImport).
    //
    // Trim-root: [DynamicDependency] on Program.Main AND an actual call site
    // in Program.Main itself (see WasiEntryPoint.cs). Mono-AOT requires a
    // reachable IL call site to emit the pinvoke wrapper; [DynamicDependency]
    // alone preserves the IL through ILLink trim but is not sufficient for
    // the AOT scanner.
    // ─────────────────────────────────────────────────────────────────────────

    internal static unsafe class Native
    {
        [DllImport("mobile_bridge", EntryPoint = "shell_platform_info")]
        internal static extern int ShellPlatformInfo(byte* buf, int bufLen);
    }
}
