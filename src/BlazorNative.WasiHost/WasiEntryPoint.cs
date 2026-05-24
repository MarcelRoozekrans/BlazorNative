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
// imports until M2 ships the native shell), performs an await Task.Delay(1)
// round-trip to prove the cooperative scheduler works, and exits 0.
//
// Four structured [BOOT] markers on stdout let the xUnit subprocess test in
// tests/BlazorNative.Wasi.Tests/ disambiguate failure stages by exit code:
//   0  = clean exit (all 4 markers emitted)
//   1  = DI failure  ([BOOT] FAIL stage=di)
//   2  = Blazor drift ([BOOT] FAIL stage=blazor-drift)
//   3  = Scheduler   ([BOOT] FAIL stage=scheduler)
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
    public static async Task<int> Main()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("[BOOT] scheduler-start");

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

            var t0 = sw.ElapsedMilliseconds;
            try
            {
                await Task.Delay(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BOOT] FAIL stage=scheduler msg={ex.GetType().Name}: {ex.Message}");
                return 3;
            }
            var dt = sw.ElapsedMilliseconds - t0;
            Console.WriteLine($"[BOOT] delay-ok Δ={dt}ms");

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
