using BlazorNative.SampleApp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RegistrationTests — Phase 8.0 (design decision 1, M8 DoD #1: the
// registration inversion's VALIDATION + the empty-registry diagnostic).
//
// The public API's contract, pinned: BlazorNativeApp.RegisterPages validates
// LOUDLY at registration time (ArgumentException naming the offending row),
// registers ONCE (a second call throws), and never after the freeze (the
// first materialization of a derived view pins the window shut). If the app
// registers NOTHING, blazornative_mount's path returns the EXISTING rc 1 —
// no new return code, no ABI change — with a distinguished stderr diagnostic
// that names the fix (RegisterPages + [ModuleInitializer] + the sample app).
//
// Every test here tears down the process-wide registration store
// (BlazorNativeApp.ResetRegistrationForTests) and RESTORES the sample app's
// manifest in a finally — the rest of the suite mounts through it. Serialized
// in the "host-session" collection like every registry consumer.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class RegistrationTests
{
    /// <summary>Test-local page components — the validation surface needs
    /// IComponent types that are nobody's production page.</summary>
    private sealed class ProbePageA : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b) { }
    }

    private sealed class ProbePageB : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b) { }
    }

    /// <summary>Runs <paramref name="body"/> against an EMPTY, unfrozen
    /// registration store and restores the sample app's manifest afterwards
    /// (EnsureRegistered's once-guard has already tripped for the process, so
    /// the restore goes through RegisterPages directly).</summary>
    private static void WithCleanRegistry(Action body)
    {
        BlazorNativeApp.ResetRegistrationForTests();
        try
        {
            body();
        }
        finally
        {
            BlazorNativeApp.ResetRegistrationForTests();
            BlazorNativeApp.RegisterPages(SampleAppPages.All);
        }
    }

    // ── Validation: loud, at registration time ──────────────────────────────

    [Fact]
    public void EmptyArray_Throws()
    {
        WithCleanRegistry(() =>
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => BlazorNativeApp.RegisterPages());
            Assert.Contains("at least one page", ex.Message);
        });
    }

    [Fact]
    public void DuplicateName_Throws_NamingTheRow()
    {
        WithCleanRegistry(() =>
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
                BlazorNativeApp.RegisterPages(
                    BlazorNativePage.Routed<ProbePageA>("/", "Duplicated"),
                    BlazorNativePage.Named<ProbePageB>("Duplicated")));
            Assert.Contains("row 1", ex.Message);
            Assert.Contains("'Duplicated'", ex.Message);
            Assert.Contains("duplicate page name", ex.Message);
        });
    }

    [Fact]
    public void DuplicateRoute_Throws_NamingTheRow()
    {
        WithCleanRegistry(() =>
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
                BlazorNativeApp.RegisterPages(
                    BlazorNativePage.Routed<ProbePageA>("/", "PageA"),
                    BlazorNativePage.Routed<ProbePageB>("/", "PageB")));
            Assert.Contains("row 1", ex.Message);
            Assert.Contains("duplicate route '/'", ex.Message);
        });
    }

    [Fact]
    public void RoutedRowsWithoutADefaultRouteRow_Throw()
    {
        WithCleanRegistry(() =>
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
                BlazorNativeApp.RegisterPages(
                    BlazorNativePage.Routed<ProbePageA>("/settings", "PageA")));
            Assert.Contains("\"/\" row", ex.Message);
        });
    }

    /// <summary>An all-probes registry is legal: validation demands "/" only
    /// when a routed row exists (the doc's rule, pinned from the other side —
    /// so the rule cannot quietly become "always").</summary>
    [Fact]
    public void UnroutedOnlyRegistry_IsLegal_AndHasNoDefaultComponent()
    {
        WithCleanRegistry(() =>
        {
            BlazorNativeApp.RegisterPages(BlazorNativePage.Named<ProbePageA>("OnlyProbe"));
            Assert.Null(PageManifest.DefaultComponent);
            Assert.Contains("OnlyProbe", HostSession.RegisteredComponentsForTests);
        });
    }

    /// <summary>A default(BlazorNativePage) — e.g. an under-filled array — is
    /// not factory-built (no name, no thunk) and must be named by index, not
    /// NullReference somewhere downstream.</summary>
    [Fact]
    public void NonFactoryBuiltRow_Throws_NamingTheRow()
    {
        WithCleanRegistry(() =>
        {
            var pages = new BlazorNativePage[2];
            pages[0] = BlazorNativePage.Routed<ProbePageA>("/", "PageA");
            // pages[1] stays default — the under-filled-array mistake.
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => BlazorNativeApp.RegisterPages(pages));
            Assert.Contains("row 1", ex.Message);
            Assert.Contains("factory-built", ex.Message);
        });
    }

    // ── Once, and never after the freeze ─────────────────────────────────────

    [Fact]
    public void SecondRegisterPages_Throws()
    {
        WithCleanRegistry(() =>
        {
            BlazorNativeApp.RegisterPages(BlazorNativePage.Routed<ProbePageA>("/", "PageA"));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                BlazorNativeApp.RegisterPages(BlazorNativePage.Routed<ProbePageB>("/", "PageB")));
            Assert.Contains("registered once, at startup", ex.Message);
        });
    }

    [Fact]
    public void RegisterAfterTheViewsMaterialized_Throws()
    {
        WithCleanRegistry(() =>
        {
            BlazorNativeApp.RegisterPages(BlazorNativePage.Routed<ProbePageA>("/", "PageA"));
            _ = HostSession.RegisteredComponentsForTests; // materialize = freeze

            BlazorNativeApp.ResetRegistrationForTests();  // reopens the store…
            _ = HostSession.RegisteredComponentsForTests; // …and re-freeze EMPTY

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                BlazorNativeApp.RegisterPages(BlazorNativePage.Routed<ProbePageB>("/", "PageB")));
            Assert.Contains("after the page registry was already", ex.Message);
            Assert.Contains("[ModuleInitializer]", ex.Message);
        });
    }

    // ── The empty-registry diagnostic (rc 1, distinguished stderr) ───────────

    /// <summary>The app forgot to register: blazornative_mount's path returns
    /// the EXISTING rc 1 (no ABI change) and stderr names the FIX — not a
    /// typo-hunt "unknown component". The literal is asserted because the
    /// diagnostic IS the contract: it is what an integrator sees first.</summary>
    [Fact]
    public void EmptyRegistry_MountReturnsRc1_AndTheDiagnosticNamesTheFix()
    {
        WithCleanRegistry(() =>
        {
            var stderr = new StringWriter();
            TextWriter original = Console.Error;
            Console.SetError(stderr);
            int rc;
            try
            {
                rc = HostSession.TryMount("BnDemo");
            }
            finally
            {
                Console.SetError(original);
            }

            Assert.Equal(1, rc);
            string diagnostic = stderr.ToString();
            Assert.Contains("no pages are registered", diagnostic);
            Assert.Contains("BlazorNativeApp.RegisterPages", diagnostic);
            Assert.Contains("[ModuleInitializer]", diagnostic);
            Assert.Contains("samples/BlazorNative.SampleApp", diagnostic);
        });
    }

    /// <summary>…and the ordinary unknown-name rc 1 stays QUIET on the
    /// registered path (the diagnostic is distinguished — it must not fire
    /// for a plain typo, or it stops meaning anything).</summary>
    [Fact]
    public void UnknownComponentOnAPopulatedRegistry_IsRc1_WithoutTheDiagnostic()
    {
        var stderr = new StringWriter();
        TextWriter original = Console.Error;
        Console.SetError(stderr);
        int rc;
        try
        {
            rc = HostSession.TryMount("NoSuchComponent");
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Equal(1, rc);
        Assert.DoesNotContain("no pages are registered", stderr.ToString());
    }
}
