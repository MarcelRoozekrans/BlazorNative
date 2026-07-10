using System.Runtime.CompilerServices;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.3 Task 6 (DoD #9): every renderer this test assembly touches runs
// STRICT. Fixtures that build their own renderer set StrictErrors inline;
// the HostSession singleton (DispatchEventTests, HostSessionTests, …) is
// born inside EnsureSession/TryMount, so its strictness is flipped once here,
// process-wide, before any test runs. Production keeps the default (false —
// log-to-stderr POC posture; see NativeRenderer.StrictErrors).
// BootSmokeNativeTest drives the PUBLISHED native dll over the C ABI in a
// separate load context — this toggle can't (and shouldn't) reach it.
// ─────────────────────────────────────────────────────────────────────────────

internal static class StrictModeTestDefaults
{
    [ModuleInitializer]
    internal static void EnableStrictHostSession()
        => HostSession.StrictErrorsForTests = true;
}
