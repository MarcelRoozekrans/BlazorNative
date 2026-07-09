using BlazorNative.Core;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.1 Task 3 — the managed core behind blazornative_run_bridge_probes:
// the six shell-bridge ops exercised INSIDE the NativeAOT-trimmed library
// against whatever host registered its callbacks (Kotlin JVM in Gate 2, the
// AVD in Gate 3). Same pattern + FATE as the trim probes: TEMPORARY
// diagnostic, delete BOTH probe exports at M3 close.
//
// The fetch probe target arrives as an argument so each host points it at
// its own local test server (10 s timeout, expects HTTP 200).
// ─────────────────────────────────────────────────────────────────────────────

internal static class BridgeProbeRunner
{
    /// <returns>(failedCount, semicolon-joined failure details or "")</returns>
    public static (int Failed, string Detail) RunAll(string fetchUrl)
    {
        var failures = new List<string>();
        var bridge = new NativeShellBridge();

        Run(failures, "navigate", () =>
        {
            // Sync-over-async is safe here: real threads under NativeAOT
            // (same rationale as TrimProbeRunner).
            bridge.NavigateAsync("/probe/navigate").GetAwaiter().GetResult();
            string route = bridge.GetCurrentRouteAsync().GetAwaiter().GetResult();
            if (route != "/probe/navigate")
                throw new InvalidOperationException($"round-trip expected '/probe/navigate', got '{route}'");
        });

        Run(failures, "storage", () =>
        {
            bridge.WriteStorageAsync("probe:key", "probe-value").GetAwaiter().GetResult();
            string? read = bridge.ReadStorageAsync("probe:key").GetAwaiter().GetResult();
            if (read != "probe-value")
                throw new InvalidOperationException($"read-after-write expected 'probe-value', got '{read ?? "<null>"}'");

            bridge.DeleteStorageAsync("probe:key").GetAwaiter().GetResult();
            string? afterDelete = bridge.ReadStorageAsync("probe:key").GetAwaiter().GetResult();
            if (afterDelete is not null)
                throw new InvalidOperationException($"read-after-delete expected null, got '{afterDelete}'");

            string? absent = bridge.ReadStorageAsync("probe:never-written").GetAwaiter().GetResult();
            if (absent is not null)
                throw new InvalidOperationException($"absent-key read expected null, got '{absent}'");
        });

        Run(failures, "fetch", () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            BridgeHttpResponse response = bridge
                .FetchAsync(new BridgeHttpRequest(fetchUrl), cts.Token)
                .AsTask().GetAwaiter().GetResult();
            if (response.StatusCode != 200)
                throw new InvalidOperationException(
                    $"expected HTTP 200 from '{fetchUrl}', got {response.StatusCode} (body: '{response.Body}')");
        });

        return (failures.Count, string.Join("; ", failures));
    }

    private static void Run(List<string> failures, string name, Action probe)
    {
        try
        {
            probe();
        }
        catch (Exception ex)
        {
            // ex.ToString() so the InnerException chain + stack survive the
            // C-ABI crossing (same rationale as TrimProbeRunner).
            failures.Add($"{name}: {ex}");
        }
    }
}
