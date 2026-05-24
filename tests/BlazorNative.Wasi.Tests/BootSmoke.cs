using System.Diagnostics;
using Xunit;

namespace BlazorNative.Wasi.Tests;

[Trait("Category", "Integration")]
[Collection("Wasi")]
public sealed class BootSmoke
{
    private readonly WasiPublishFixture _fixture;
    public BootSmoke(WasiPublishFixture f) => _fixture = f;

    [Fact]
    public async Task WasmModule_BootsCleanly_UnderWasmtime()
    {
        var (exitCode, stdout, _) = await WasmtimeRunner.Run(
            _fixture,
            extraArgsBeforeWasm: Array.Empty<string>(),
            programArgs: Array.Empty<string>(),
            timeout: TimeSpan.FromSeconds(10));

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
        var (exitCode, _, _) = await WasmtimeRunner.Run(
            _fixture,
            extraArgsBeforeWasm: Array.Empty<string>(),
            programArgs: Array.Empty<string>(),
            timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(0, exitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"WASI boot took {sw.ElapsedMilliseconds}ms — should be under 5s for a no-op Main");
    }
}
