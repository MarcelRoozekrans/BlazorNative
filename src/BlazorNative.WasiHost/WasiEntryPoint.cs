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
// Five structured [BOOT] markers on stdout let the xUnit subprocess test
// disambiguate failure stages by exit code:
//   0  = clean exit (all 5 markers emitted; including Phase 2.0 event-ok
//        self-test + Phase 2.3 bridge-ok env-var round-trip)
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
    // Keep WasiBridge (including the [UnmanagedCallersOnly] DispatchEventNative
    // export) rooted through Mono-AOT trimming. Without this, the trimmer
    // removes WasiBridge.DispatchEventNative because nothing in the post-Main
    // IL graph references it statically — verified via wasm-tools: the string
    // 'blazornative_dispatch_event' was absent from the AOT'd .wasm before
    // this attribute was added. The IMobileBridge resolution via DI below
    // keeps the type alive, but the trimmer still strips per-method dead code;
    // [DynamicDependency] on Main forces all WasiBridge members to be preserved.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WasiBridge))]
    // Phase 2.4 Task 4 / Phase 2.8 Task 1: NativeRenderer.Mount<T> calls
    // InstantiateComponent which uses Activator.CreateInstance — Mono-AOT
    // trimmer drops the parameterless ctor of any component-type mounted
    // via Mount<T> otherwise (error surfaces as "CtorNotLocated,
    // BlazorNative.WasiHost.<TypeName>" at mount time). M3 may generalize
    // this pattern via a `[BlazorNativeMountable]` marker analyzer that
    // emits the [DynamicDependency] automatically.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(HelloComponent))]
    // The dormant BLAZOR_STREAMING_SPIKE=1 code path (Phase 2.4 Task 15) still
    // mounts _BridgeFrameSelfTest. Keep its ctor rooted for the spike test
    // (StreamingSpike_Rung1Test) — otherwise CtorNotLocated fires when the
    // env var is set. Phase 2.8 Task 8 GREEN-sweep finding.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(_BridgeFrameSelfTest))]
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
            Console.WriteLine($"[BOOT] event-ok fired={selfTestFired} name={receivedEvent.Name} payload={receivedEvent.Payload}");

            // Phase 2.3 self-test: prove .NET ← host round-trip via env-var
            // bridge (the revised C-1 path; see docs/plans/2026-05-26-phase-2.3-
            // design-revision.md). Host passes BLAZOR_PLATFORM_INFO before
            // wasmtime_context_set_wasi; WasiBridge.PlatformInfo reads via
            // Environment.GetEnvironmentVariable through wasi:cli/environment.
            // The captured marker payload varies per host (proves real round-
            // trip vs constant baked into .wasm). Three-way validation: JVM
            // stub vs Android Build.* vs .NET CLI --env-passed value.
            var platformInfo = bridge.PlatformInfo;
            Console.WriteLine($"[BOOT] bridge-ok platform-info={platformInfo}");

            // Phase 2.4: mount the sentinel component. The renderer's sync
            // Mount<T> asserts the first render completes synchronously — true
            // for _BridgeFrameSelfTest (no async lifecycle, sync UpdateDisplayAsync).
            // UpdateDisplayAsync calls DispatchFrame which emits one [FRAME] line
            // to stdout; the host parses it via FrameStreamParser. End-to-end
            // proof of the .NET → host runtime transport for a single snapshot.
            Console.WriteLine("[BOOT] mounting hello");
            renderer.Mount<HelloComponent>();
            Console.WriteLine("[BOOT] hello-rendered");

            // Phase 2.4 Task 15 streaming spike: when BLAZOR_STREAMING_SPIKE=1
            // is set, sleep + emit a second frame so a host-side poller has
            // a measurable observation window to detect line-by-line flush.
            // Production Main does not sleep; this code path is dormant unless
            // the env var is set by the spike test.
            if (Environment.GetEnvironmentVariable("BLAZOR_STREAMING_SPIKE") == "1")
            {
                Console.WriteLine("[BOOT] spike-sleeping");
                Console.Out.Flush();
                // Phase 2.4 Task 15 spike: Thread.Sleep is normally banned on
                // WASI (BN0004) — Task.Delay throws PlatformNotSupportedException
                // on Mono-WASI (see Phase 1.2 notes above). For this spike we
                // WANT a synchronous block to extend the observation window;
                // the cooperative-scheduler concern doesn't apply because Main
                // has no async continuations.
#pragma warning disable BN0004
                Thread.Sleep(200);
#pragma warning restore BN0004
                renderer.Mount<_BridgeFrameSelfTest>();
                Console.WriteLine("[BOOT] spike-second-frame-emitted");
                Console.Out.Flush();
            }

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
