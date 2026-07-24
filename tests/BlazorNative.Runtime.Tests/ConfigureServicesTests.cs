using BlazorNative.Core;          // INavigationManager — the consume-only contract (#210)
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
    private const string NavProbeName = "ConfigureServicesNavProbe";

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

    // ── #210: the one limit on "last-wins" ───────────────────────────────────

    /// <summary>An app-authored navigator — the exact shape the ConfigureServices
    /// summary used to invite, and the one that broke composition.</summary>
    private sealed class AppNavigationManager : INavigationManager
    {
        public string CurrentRoute => "/";
        public ValueTask NavigateToAsync(string route) => default;
        public ValueTask<bool> NavigateBackAsync() => new(false);
        public event Action<string>? RouteChanged { add { } remove { } }
    }

    /// <summary>A page that [Inject]s the navigation CONTRACT — the component's-eye
    /// view, which is the half that must agree with the shell's.</summary>
    private sealed class NavigationInjectingPage : ComponentBase
    {
        internal static INavigationManager? Captured;

        [Inject] internal INavigationManager Navigation { get; set; } = default!;

        protected override void OnInitialized() => Captured = Navigation;

        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    /// <summary>
    /// What a component injects is the same object the shell's route-aware mount and
    /// root swap drive.
    ///
    /// **This does NOT reproduce #210** — say so plainly, because a test whose comment
    /// overstates its reach is worse than no comment. On the pre-fix single
    /// registration this passed: there was one registration and therefore one instance.
    /// It guards the risk the FIX introduces. Splitting into two registrations
    /// (concrete + contract) is exactly how a second SINGLETON gets created by
    /// accident, and that would leave components on one navigator and the shell on
    /// another — navigation half-working, with no stack trace to explain it, which is
    /// the same silent split #210's fail-fast exists to prevent.
    ///
    /// Asserted through the REAL injection path rather than by resolving twice from a
    /// provider, because that is the property with consequences:
    /// `CurrentNavigationManager` is what the session stored for its own plumbing,
    /// `Captured` is what Blazor injected, and they must be one object.
    /// The pin that DOES reproduce #210 is the fail-fast test below.
    /// </summary>
    [Fact]
    public void WhatAComponentInjects_IsTheSameNavigatorTheShellDrives()
    {
        NavigationInjectingPage.Captured = null;
        BlazorNativeApp.ResetRegistrationForTests();
        try
        {
            BlazorNativeApp.RegisterPages(
                BlazorNativePage.Named<NavigationInjectingPage>(NavProbeName));
            HostSession.ResetForTests();

            Assert.Equal(0, HostSession.TryMount(NavProbeName));

            Assert.NotNull(NavigationInjectingPage.Captured);
            Assert.NotNull(HostSession.CurrentNavigationManager);
            Assert.Same(HostSession.CurrentNavigationManager, NavigationInjectingPage.Captured);
        }
        finally
        {
            NavigationInjectingPage.Captured = null;
            HostSession.ResetForTests();
            BlazorNativeApp.ResetRegistrationForTests();
            BlazorNativeApp.RegisterPages(SampleAppPages.All);
        }
    }

    /// <summary>
    /// #210 — re-registering the consume-only navigation contract fails FAST, and
    /// the message names the cause.
    ///
    /// Before the fix this threw <c>InvalidCastException</c> from a downcast inside
    /// EnsureSession, which TryMount mapped to rc 2: every mount failed and nothing
    /// in the log said "DI". The test asserts the exception TYPE changed and that the
    /// text names both the contract and the seam — a fail-fast whose message does not
    /// identify the cause would be no better than the cast it replaced.
    /// </summary>
    [Fact]
    public void ReRegisteringTheNavigationContract_FailsFast_AndTheMessageNamesTheCause()
    {
        BlazorNativeApp.ResetRegistrationForTests();
        try
        {
            BlazorNativeApp.RegisterPages(BlazorNativePage.Named<InjectingProbePage>(ProbeName));
            HostSession.ResetForTests();

            // The exact line the seam's summary used to invite.
            BlazorNativeApp.ConfigureServices(
                s => s.AddSingleton<INavigationManager, AppNavigationManager>());

            var ex = Assert.Throws<InvalidOperationException>(() => HostSession.EnsureSession());

            Assert.Contains("INavigationManager", ex.Message, StringComparison.Ordinal);
            Assert.Contains("CONSUME-ONLY", ex.Message, StringComparison.Ordinal);
            Assert.Contains("ConfigureServices", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            HostSession.ResetForTests();
            BlazorNativeApp.ResetRegistrationForTests();
            BlazorNativeApp.RegisterPages(SampleAppPages.All);
        }
    }
}
