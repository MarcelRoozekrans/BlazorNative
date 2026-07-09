using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.1 Task 3 — the managed core behind blazornative_run_bridge_probes,
// exercised against FakeShellHost. The real-host runs (Kotlin JVM + AVD) are
// Gates 2/3; this pins the runner's probe logic and its failed-count/detail
// contract on the host CLR.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("native-shell-bridge")]
public sealed class BridgeProbeRunnerTests
{
    private const string ProbeUrl = "http://fake.test/probe";

    [Fact]
    public void BridgeProbes_AllPass_AgainstFakeHost()
    {
        FakeShellHost.Reset();
        FakeShellHost.AutoCompleteFetch = true;
        FakeShellHost.AutoCompleteStatus = 200;
        FakeShellHost.AutoCompleteBody = "probe-ok";
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        try
        {
            var (failed, detail) = BridgeProbeRunner.RunAll(ProbeUrl);

            Assert.True(failed == 0, $"expected 0 failed probes, got {failed}: {detail}");
            Assert.Equal("", detail);
            // The fetch probe must have hit the URL the export was given.
            Assert.Equal(ProbeUrl, FakeShellHost.LastFetchUrl);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public void BridgeProbes_FetchTransportError_ReportsFetchFailure()
    {
        FakeShellHost.Reset();
        FakeShellHost.AutoCompleteFetch = true;
        FakeShellHost.AutoCompleteOk = false; // Ok = 0 → transport error
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        try
        {
            var (failed, detail) = BridgeProbeRunner.RunAll(ProbeUrl);

            Assert.Equal(1, failed); // navigate + storage still pass
            Assert.Contains("fetch:", detail);
            Assert.Contains("fake transport error", detail);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public void BridgeProbes_UnregisteredHost_FailsAllProbes_InsteadOfCrashing()
    {
        NativeShellBridge.ResetForTests();

        var (failed, detail) = BridgeProbeRunner.RunAll(ProbeUrl);

        Assert.Equal(3, failed);
        Assert.Contains(NativeShellBridge.NotRegisteredMessage, detail);
    }
}
