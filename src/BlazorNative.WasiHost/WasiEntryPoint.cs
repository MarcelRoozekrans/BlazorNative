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
// Three structured [BOOT] markers on stdout let the xUnit subprocess test
// disambiguate failure stages by exit code:
//   0  = clean exit (all 3 markers emitted)
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
