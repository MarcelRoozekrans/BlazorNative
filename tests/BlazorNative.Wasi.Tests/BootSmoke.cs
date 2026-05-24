using System.Diagnostics;
using Xunit;

namespace BlazorNative.Wasi.Tests;

[Trait("Category", "Integration")]
public sealed class BootSmoke : IClassFixture<WasiPublishFixture>
{
    private readonly WasiPublishFixture _fixture;
    public BootSmoke(WasiPublishFixture f) => _fixture = f;

    [Fact]
    public async Task WasmModule_BootsCleanly_UnderWasmtime()
    {
        var (exitCode, stdout, stderr) = await RunWasmtime(_fixture.WasmPath, TimeSpan.FromSeconds(10));

        // Boot sequence assertions — order matters; each marker proves the prior step finished.
        Assert.Contains("[BOOT] scheduler-start",                                       stdout);
        Assert.Contains("[BOOT] di-ok bridge=WasiBridge renderer=NativeRenderer",       stdout);
        Assert.Matches(@"\[BOOT\] delay-ok Δ=\d+ms",                                    stdout);
        Assert.Contains("[BOOT] done",                                                  stdout);
        Assert.DoesNotContain("[BOOT] FAIL",                                            stdout);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task WasmModule_ExitsWithinTwoSeconds()
    {
        var sw = Stopwatch.StartNew();
        var (exitCode, _, _) = await RunWasmtime(_fixture.WasmPath, TimeSpan.FromSeconds(2));
        Assert.Equal(0, exitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
            $"WASI boot took {sw.ElapsedMilliseconds}ms — should be sub-second for a no-op Main");
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunWasmtime(
        string wasmPath, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo("wasmtime", $"run \"{wasmPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var proc = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* may already be dead */ }
            throw new TimeoutException($"wasmtime did not exit within {timeout.TotalSeconds}s");
        }
        return (proc.ExitCode,
                await proc.StandardOutput.ReadToEndAsync(),
                await proc.StandardError.ReadToEndAsync());
    }
}
