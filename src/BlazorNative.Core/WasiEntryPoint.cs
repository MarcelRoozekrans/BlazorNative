namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// WasiEntryPoint
//
// Minimal Main stub so the wasi-wasm RID build (OutputType=Exe) produces a
// runnable .wasm binary. The Mono-AOT pipeline requires a static entry point.
//
// Phase 1.1 (this commit): no-op. Confirms the toolchain wires through and
// the build produces a .wasm artifact.
//
// Phase 1.2 (next): bootstrap the .NET 10 cooperative async scheduler, wire
// up DI, register WasiBridge + AddBlazorNativeRenderer(), and mount the root
// Blazor component. The body of this Main becomes the WASI bootstrap.
//
// On non-WASI builds (net10.0 library) this Main is harmless dead weight —
// libraries can contain Main methods that are never called.
// ─────────────────────────────────────────────────────────────────────────────

public static class Program
{
    public static int Main()
    {
        Console.WriteLine("[BlazorNative.Core] WASI entry point reached. Phase 1.2 will replace this with the cooperative scheduler bootstrap.");
        return 0;
    }
}
