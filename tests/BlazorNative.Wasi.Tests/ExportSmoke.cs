using System.Text;
using Xunit;

namespace BlazorNative.Wasi.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ExportSmoke
//
// Verifies the [UnmanagedCallersOnly(EntryPoint = "blazornative_dispatch_event")]
// attribute on WasiBridge.DispatchEvent survives Mono-AOT and appears in the
// AOT'd .wasm's export section.
//
// Phase 1.3 findings:
//  - wasmtime --invoke can't reach core-module exports through the component-
//    model layer wasi-experimental emits — exports inside the inner core
//    module aren't surfaced as component-level functions, so invocation via
//    `wasmtime --invoke` isn't viable.
//  - wasm-tools `print`/`dump` work but produce ~10 MB of output for a 13 MB
//    .wasm; piping that through Process.Standard{Output|Error} deadlocks
//    reliably (the pipe buffer fills before we drain it).
//  - The export name is a literal UTF-8 string in the .wasm's export section,
//    so we can verify presence with a direct byte scan — no subprocess.
//
// Additional Phase 1.3 finding (recorded for context):
//  - [UnmanagedCallersOnly] alone wasn't enough of a trim root on Mono-AOT.
//    WasiBridge.DispatchEvent got stripped because nothing in the post-Main
//    IL graph references it statically. Fix: [DynamicDependency] on
//    Program.Main keeps all WasiBridge members alive (see
//    src/BlazorNative.WasiHost/WasiEntryPoint.cs).
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Integration")]
public sealed class ExportSmoke : IClassFixture<WasiPublishFixture>
{
    private const string ExportName = "blazornative_dispatch_event";

    private readonly WasiPublishFixture _fixture;
    public ExportSmoke(WasiPublishFixture f) => _fixture = f;

    [Fact]
    public void Export_BlazorNativeDispatchEvent_IsPresent()
    {
        // Direct byte scan. UTF-8 export names in WASM are inline in the
        // module's export section — no decoding needed. We accept multiple
        // hits (the name appears both as the export-section entry and as a
        // metadata-section name).
        var wasm = File.ReadAllBytes(_fixture.WasmPath);
        var needle = Encoding.UTF8.GetBytes(ExportName);
        var hitCount = CountOccurrences(wasm, needle);

        Assert.True(hitCount > 0,
            $"'{ExportName}' not found in {_fixture.WasmPath} ({wasm.Length:N0} bytes). " +
            $"The [UnmanagedCallersOnly] attribute on WasiBridge.DispatchEvent did not " +
            $"survive Mono-AOT trimming. Verify that " +
            $"src/BlazorNative.WasiHost/WasiEntryPoint.cs has " +
            $"[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasiBridge))] " +
            $"on Program.Main.");
    }

    /// <summary>
    /// Boyer-Moore-Horspool-ish naive scan. Sufficient for our needs — wasm is
    /// ~13 MB, needle is ~27 bytes, the test runs in well under 100 ms.
    /// </summary>
    private static int CountOccurrences(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return 0;
        var count = 0;
        var i = 0;
        while (i <= haystack.Length - needle.Length)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
            {
                count++;
                i += needle.Length;
            }
            else
            {
                i++;
            }
        }
        return count;
    }
}
