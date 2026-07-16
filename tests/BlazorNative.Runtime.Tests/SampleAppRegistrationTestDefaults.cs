using System.Runtime.CompilerServices;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 8.0 (design decision 4): the app manifest registers BEFORE any test
// runs — the StrictModeTestDefaults pattern, second telling.
//
// On NativeAOT, SampleAppPages' own [ModuleInitializer] runs eagerly inside
// blazornative_init. On the CoreCLR test host, module initializers run LAZILY
// — on first touch of the assembly — and a mount-by-NAME test
// (HostSession.TryMount("BnDemo")) never touches a SampleApp type, so lazy
// initialization would leave the registry EMPTY exactly where it matters.
// This explicit call is the deterministic hook; EnsureRegistered is
// idempotent, so the two paths meet safely.
// ─────────────────────────────────────────────────────────────────────────────

internal static class SampleAppRegistrationTestDefaults
{
    [ModuleInitializer]
    internal static void RegisterTheSampleAppManifest()
        => SampleAppPages.EnsureRegistered();
}
