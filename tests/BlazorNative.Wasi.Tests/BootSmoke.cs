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
        var (exitCode, stdout, stderr) = await RunWasmtime(_fixture, TimeSpan.FromSeconds(10));

        // Boot sequence assertions — order matters; each marker proves the prior step finished.
        // Async Main + Task.Delay was dropped in Phase 1.2 Task 13 — Mono-WASI throws
        // PlatformNotSupportedException from Task.InternalWaitCore. Sync Main is the
        // supported .NET 10 shape and is what `dotnet new wasiconsole` produces.
        Assert.Contains("[BOOT] runtime-start",                                         stdout);
        Assert.Contains("[BOOT] di-ok bridge=WasiBridge renderer=NativeRenderer",       stdout);
        Assert.Contains("[BOOT] done",                                                  stdout);
        Assert.DoesNotContain("[BOOT] FAIL",                                            stdout);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task WasmModule_ExitsWithinFiveSeconds()
    {
        // Mono-AOT cold-start is slower than NativeAOT — ~2-4s on Marcel's dev box
        // for the runtime + DI graph composition. 5s is a regression catch ceiling,
        // not a performance target.
        var sw = Stopwatch.StartNew();
        var (exitCode, _, _) = await RunWasmtime(_fixture, TimeSpan.FromSeconds(5));
        Assert.Equal(0, exitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"WASI boot took {sw.ElapsedMilliseconds}ms — should be under 5s for a no-op Main");
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunWasmtime(
        WasiPublishFixture fixture, TimeSpan timeout)
    {
        // Mono-WASI invocation:
        //   --dir=.                              relative filesystem access (needed
        //                                        for ICU data lookup); WorkingDirectory
        //                                        below makes "." resolve to AppBundleDir.
        //   -Shttp                               enables wasi:http/types — Mono imports
        //                                        it whenever System.Net.Http is in the
        //                                        dep graph (inherited via BlazorNative.Http).
        //   BlazorNative.WasiHost.wasm           the AOT-compiled app-specific module
        //                                        with the IL baked in (no separate
        //                                        runtime + .dll dance).
        var wasmtimeExe = ResolveWasmtime();
        var args = $"run -Shttp --dir=. {fixture.AppAssemblyName}.wasm";
        var psi = new ProcessStartInfo(wasmtimeExe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = fixture.AppBundleDir,
        };

        // Strip DOTNET_* / MONO_* env vars inherited from the test runner — they
        // can flip the WASM-side Mono runtime into incompatible modes (saw exit 3
        // before clearing when invoked from `dotnet test`).
        foreach (var key in psi.Environment.Keys.Where(k =>
                     k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) ||
                     k.StartsWith("MONO_", StringComparison.OrdinalIgnoreCase) ||
                     k.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase))
                 .ToList())
        {
            psi.Environment.Remove(key);
        }
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

    /// <summary>Find wasmtime on PATH or in standard install locations. Tolerant
    /// of the case where the user installed wasmtime after the test runner
    /// process started — the user-scope PATH change may not be in this
    /// process's PATH yet.</summary>
    private static string ResolveWasmtime()
    {
        var fromPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(d => Path.Combine(d.Trim(), OperatingSystem.IsWindows() ? "wasmtime.exe" : "wasmtime"))
            .FirstOrDefault(File.Exists);
        if (fromPath is not null) return fromPath;

        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)?
            .Split(Path.PathSeparator)
            .Select(d => Path.Combine(d.Trim(), OperatingSystem.IsWindows() ? "wasmtime.exe" : "wasmtime"))
            .FirstOrDefault(File.Exists);
        if (userPath is not null) return userPath;

        var common = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wasmtime", "bin", "wasmtime.exe"),
            @"C:\Tools\wasmtime\wasmtime.exe",
        };
        foreach (var d in Directory.Exists(@"C:\Tools") ? Directory.GetDirectories(@"C:\Tools", "wasmtime*") : Array.Empty<string>())
        {
            var c = Path.Combine(d, "wasmtime.exe");
            if (File.Exists(c)) return c;
        }
        var found = common.FirstOrDefault(File.Exists);
        if (found is not null) return found;

        throw new FileNotFoundException(
            "wasmtime executable not found. Install from https://wasmtime.dev (or run " +
            @"`Invoke-WebRequest https://github.com/bytecodealliance/wasmtime/releases/download/v45.0.0/wasmtime-v45.0.0-x86_64-windows.zip -OutFile $env:TEMP\wasmtime.zip; Expand-Archive $env:TEMP\wasmtime.zip C:\Tools -Force`)");
    }
}
