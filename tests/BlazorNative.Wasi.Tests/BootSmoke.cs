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
        // Phase 2.3 env-var bridge: pass BLAZOR_PLATFORM_INFO to wasmtime CLI
        // via --env. The .wasm reads via Environment.GetEnvironmentVariable
        // and emits the value in the [BOOT] bridge-ok marker. With env-var
        // bridge (vs the abandoned custom-WIT-import design) wasmtime CLI
        // needs nothing special — standard wasi:cli/environment surface.
        const string CliPlatformInfo = """{"os":"wasmtime-cli","note":".NET-side BootSmoke harness"}""";

        var (exitCode, stdout, _) = await WasmtimeRunner.Run(
            _fixture,
            extraArgsBeforeWasm: new[] { "--env", $"BLAZOR_PLATFORM_INFO={CliPlatformInfo}" },
            programArgs: Array.Empty<string>(),
            timeout: TimeSpan.FromSeconds(10));

        // Boot sequence assertions — order matters; each marker proves the prior step finished.
        Assert.Contains("[BOOT] runtime-start",                                          stdout);
        Assert.Contains("[BOOT] di-ok bridge=WasiBridge renderer=NativeRenderer",        stdout);
        Assert.Contains("[BOOT] event-ok fired=True name=self-test payload=phase-2.0",   stdout);
        // Phase 2.3 — bridge round-trip via env var (wasmtime --env passed to .NET).
        Assert.Contains($"[BOOT] bridge-ok platform-info={CliPlatformInfo}",             stdout);
        Assert.Contains("[BOOT] done",                                                   stdout);
        Assert.DoesNotContain("[BOOT] FAIL",                                             stdout);
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
