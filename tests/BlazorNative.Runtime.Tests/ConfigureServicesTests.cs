using BlazorNative.SampleApp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ConfigureServicesTests — Phase 0.4.0-prep Gate A (design §1: the
// ConfigureServices app-service DI seam).
//
// The contract, pinned end-to-end: an app captures its own service
// registrations via BlazorNativeApp.ConfigureServices (at [ModuleInitializer]
// on-device; called directly here), EnsureSession() invokes that delegate on
// the SAME ServiceCollection it hands to BuildServiceProvider — AFTER the
// framework's registrations, immediately BEFORE the build — and a page that
// [Inject]s the app service resolves the app-registered instance through the
// framework's one provider, identically to how it reaches framework services.
//
// Red-first: without the call-site in EnsureSession the app service is never
// registered, the [Inject] property injection throws during mount, and
// TryMount returns rc 2 (mount threw). With it, the mount is rc 0 and the
// injected instance is the app's.
//
// Isolation: ConfigureServices writes a process-wide captured delegate, so
// this class serializes via the "host-session" collection (like every session
// consumer) and, in a finally, clears the captured delegate + tears down the
// session (HostSession.ResetForTests) AND restores the sample app's manifest —
// the rest of the suite mounts through it. Same reset discipline as
// RegistrationTests' WithCleanRegistry.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class ConfigureServicesTests
{
    /// <summary>An app-authored contract — nobody's framework service, so it
    /// resolves ONLY if the app registered it through the seam.</summary>
    private interface IAppMarkerService
    {
        string Marker { get; }
    }

    private sealed class AppMarkerService : IAppMarkerService
    {
        public string Marker => "app-registered";
    }

    /// <summary>A page that [Inject]s the app service and records the resolved
    /// instance at init — the observation point for "an app [Inject] reaches
    /// app services through the framework's provider".</summary>
    private sealed class InjectingProbePage : ComponentBase
    {
        internal static IAppMarkerService? Captured;

        [Inject] internal IAppMarkerService Service { get; set; } = default!;

        protected override void OnInitialized() => Captured = Service;

        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    private const string ProbeName = "ConfigureServicesProbe";

    [Fact]
    public void AppServiceRegisteredViaConfigureServices_ResolvesThroughAPageInject()
    {
        InjectingProbePage.Captured = null;
        BlazorNativeApp.ResetRegistrationForTests();
        try
        {
            // The app names itself: one probe page that [Inject]s the app service.
            BlazorNativeApp.RegisterPages(BlazorNativePage.Named<InjectingProbePage>(ProbeName));

            // Force a fresh session so EnsureSession composes the provider WITH
            // the captured delegate (a cached session would predate this test).
            // ResetForTests also clears any captured delegate, so the seam is set
            // AFTER the reset — mirroring the on-device order (module-init capture
            // precedes the first EnsureSession).
            HostSession.ResetForTests();

            // Register the app service through the seam under test.
            BlazorNativeApp.ConfigureServices(s => s.AddSingleton<IAppMarkerService, AppMarkerService>());

            int rc = HostSession.TryMount(ProbeName);

            // Red without the EnsureSession call-site: the [Inject] finds no
            // IAppMarkerService, injection throws, mount → rc 2.
            Assert.Equal(0, rc);
            Assert.NotNull(InjectingProbePage.Captured);
            Assert.Equal("app-registered", InjectingProbePage.Captured!.Marker);
        }
        finally
        {
            InjectingProbePage.Captured = null;
            // Tears down the session AND clears the captured configure delegate
            // (ResetForTests) so neither leaks into another host-session test.
            HostSession.ResetForTests();
            BlazorNativeApp.ResetRegistrationForTests();
            BlazorNativeApp.RegisterPages(SampleAppPages.All);
        }
    }
}
