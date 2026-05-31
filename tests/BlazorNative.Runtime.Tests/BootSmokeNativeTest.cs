using BlazorNative.Renderer;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0b Gate 2 — managed-side bootsmoke.
//
// Confirms BlazorInterop.VerifyAccessors works under managed-side test
// conditions, as a precursor to the JNA test in BlazorNative.Jni. If this
// fails on host CLR, the probe shapes are likely fine but Phase 3.0a's
// Type.GetType refactor has regressed at the managed layer.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BootSmokeNativeManagedTest
{
    private readonly ITestOutputHelper _log;
    public BootSmokeNativeManagedTest(ITestOutputHelper log) => _log = log;

    [Fact]
    public void BlazorInterop_VerifyAccessors_RunsClean()
    {
        // Init's first managed-side action is BlazorInterop.EnsureInitialized,
        // which triggers VerifyAccessors. If Phase 3.0a's Type.GetType refactor
        // broke under the new project's reachability, this would throw.
        // Pure managed-side smoke before we hit the JNA layer.
        BlazorInterop.EnsureInitialized();
        _log.WriteLine("BlazorInterop.EnsureInitialized() OK");
    }
}
