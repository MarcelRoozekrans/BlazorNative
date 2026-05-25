using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Http;

namespace BlazorNative.WasiHost;

// ─────────────────────────────────────────────────────────────────────────────
// WasiEntryPoint
//
// Real WASI entry point (Phase 1.2). Builds the DI graph via ZeroAlloc.Inject's
// generated extension methods, resolves IMobileBridge + NativeRenderer (does
// NOT invoke any bridge method — would trap on unresolved mobile_bridge extern
// imports until M2 ships the native shell), and exits 0.
//
// .NET 10 Mono-WASI limitation (discovered Phase 1.2 Task 13): async Main
// and `await Task.Delay(N)` throw PlatformNotSupportedException because
// Mono's WASI runtime resolves the await-continuation through Task.Wait,
// which traps on the single-threaded WASI scheduler. Synchronous Main is
// the supported shape (matches the `dotnet new wasiconsole` template).
//
// The "scheduler round-trip" DoD criterion (MILESTONE.md DoD #5) is deferred
// to a later phase: once cooperative async is supported on Mono-WASI (.NET
// 11 candidate) or once we run inside the Android shell where threads exist.
// For Phase 1.2 the load-bearing proof is "Mono runtime loads + DI graph
// composes + Blazor drift probe runs + clean exit", which the three remaining
// [BOOT] markers cover.
//
// Four structured [BOOT] markers on stdout let the xUnit subprocess test
// disambiguate failure stages by exit code:
//   0  = clean exit (all 4 markers emitted; including Phase 2.0 self-test)
//   1  = DI failure  ([BOOT] FAIL stage=di)
//   2  = Blazor drift ([BOOT] FAIL stage=blazor-drift)
//   99 = Unknown     ([BOOT] FAIL stage=unknown)
//
// Lives in BlazorNative.WasiHost (not Core) because Renderer + Http already
// reference Core; having Core also reference Renderer/Http to satisfy the
// using directives above would create a circular project dependency.
//
// See docs/plans/2026-05-23-phase-1.2-design.md for full design.
// ─────────────────────────────────────────────────────────────────────────────

public static class Program
{
    // Keep WasiBridge (including the [UnmanagedCallersOnly] DispatchEvent export)
    // rooted through Mono-AOT trimming. Without this, the trimmer removes
    // WasiBridge.DispatchEvent because nothing in the post-Main IL graph
    // references it statically — verified via wasm-tools: the string
    // 'blazornative_dispatch_event' was absent from the AOT'd .wasm before
    // this attribute was added. The IMobileBridge resolution via DI below
    // keeps the type alive, but the trimmer still strips per-method dead code;
    // [DynamicDependency] on Main forces all WasiBridge members to be preserved.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasiBridge))]
    public static int Main()
    {
        try
        {
            Console.WriteLine("[BOOT] runtime-start");

            IServiceProvider provider;
            IMobileBridge bridge;
            NativeRenderer renderer;
            try
            {
                var services = new ServiceCollection();
                services.AddBlazorNativeCoreServices();
                services.AddBlazorNativeRendererServices();
                services.AddBlazorNativeHttpServices();
                provider = services.BuildServiceProvider();
                bridge = provider.GetRequiredService<IMobileBridge>();
                renderer = provider.GetRequiredService<NativeRenderer>();
            }
            catch (BlazorVersionMismatchException ex)
            {
                Console.WriteLine($"[BOOT] FAIL stage=blazor-drift msg={ex.Message}");
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BOOT] FAIL stage=di msg={ex.GetType().Name}: {ex.Message}");
                return 1;
            }

            Console.WriteLine($"[BOOT] di-ok bridge={bridge.GetType().Name} renderer={renderer.GetType().Name}");

            // Phase 2.0 self-test: prove bridge round-trip works without trapping.
            // Registers a sync subscriber, invokes DispatchEventCore from managed
            // code (the same path the unmanaged blazornative_dispatch_event export
            // calls into), confirms the subscriber fired. This is the Mono-WASI
            // end-to-end check that the Task.InternalWaitCore PNSE trap from M1 is
            // genuinely closed.
            var selfTestFired = false;
            NativeEvent receivedEvent = default;
            Action<NativeEvent> probe = e => { selfTestFired = true; receivedEvent = e; };
            bridge.NativeEvents += probe;
            WasiBridge.DispatchEventCore("self-test", "phase-2.0");
            bridge.NativeEvents -= probe;
            Console.WriteLine($"[BOOT] event-ok fired={selfTestFired} name={receivedEvent.Name}");

            Console.WriteLine("[BOOT] done");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOOT] FAIL stage=unknown msg={ex.GetType().Name}: {ex.Message}");
            return 99;
        }
    }
}
