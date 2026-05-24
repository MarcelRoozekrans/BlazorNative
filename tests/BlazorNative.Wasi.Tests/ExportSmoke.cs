using Xunit;

namespace BlazorNative.Wasi.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ExportSmoke
//
// Verifies the [UnmanagedCallersOnly(EntryPoint = "blazornative_dispatch_event")]
// attribute on WasiBridge.DispatchEvent survives Mono-AOT and is callable.
//
// wasmtime --invoke skips _start/Main and calls the named export directly. If
// the export resolves, the function runs; if not, wasmtime errors with
// "function not found" (non-zero exit).
//
// All-zero args route through the no-subscriber branch — WasiBridge.Current is
// null because Main never ran, so the function returns immediately without
// tripping the D1 async-trap concern (BACKLOG "Mono-WASI async trap will fire
// on first real bridge event").
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Integration")]
public sealed class ExportSmoke : IClassFixture<WasiPublishFixture>
{
    private readonly WasiPublishFixture _fixture;
    public ExportSmoke(WasiPublishFixture f) => _fixture = f;

    [Fact]
    public async Task Export_BlazorNativeDispatchEvent_IsCallable()
    {
        var (exitCode, _, stderr) = await WasmtimeRunner.Run(
            _fixture,
            extraArgsBeforeWasm: new[] { "--invoke", "blazornative_dispatch_event" },
            programArgs: new[] { "0", "0", "0", "0" },   // namePtr, nameLen, payloadPtr, payloadLen
            timeout: TimeSpan.FromSeconds(10));

        Assert.True(exitCode == 0, BuildFailureMessage(exitCode, stderr));
    }

    private static string BuildFailureMessage(int exitCode, string stderr) =>
        $"wasmtime --invoke exited {exitCode}.\nSTDERR:\n{stderr}\n\n" +
        $"If stderr mentions 'function not found' or 'no export', the [UnmanagedCallersOnly] " +
        $"attribute on WasiBridge.DispatchEvent did not survive Mono-AOT trimming. " +
        $"Fix candidates: add WasiBridge to a TrimmerRoots.xml referenced by " +
        $"BlazorNative.WasiHost.csproj, or add [DynamicDependency(...)] to keep " +
        $"DispatchEvent rooted.\n\n" +
        $"If stderr mentions '--invoke is not supported for components' or similar, " +
        $"install wasm-tools (https://github.com/bytecodealliance/wasm-tools/releases " +
        $"or `cargo install wasm-tools`) and rewrite this test to shell out to " +
        $"`wasm-tools dump <path> | grep blazornative_dispatch_event`.";
}
