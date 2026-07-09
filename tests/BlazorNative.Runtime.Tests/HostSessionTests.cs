using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BlazorNative.NativeHost;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d Task 5 — HostSession behavior on the host CLR.
//
// Exercises the REAL function-pointer path: SetFrameCallback receives a
// [UnmanagedCallersOnly] cdecl method's address (exactly what JNA hands the
// export), TryMount builds the DI session, mounts HelloComponent, and the
// FrameSink marshaller must fire the callback with an encoded frame.
//
// The full cross-language path (JNA callback object → dll → Kotlin adapter)
// is covered by the JVM golden test
// (src/BlazorNative.Jni/.../NativeFrameAdapterTest.kt, Task 7).
//
// State note: HostSession is a process-wide singleton (static renderer +
// callback slot), so these tests live in ONE class — xUnit runs same-class
// tests sequentially — and each test restores the callback slot to Zero.
// ─────────────────────────────────────────────────────────────────────────────

public sealed unsafe class HostSessionTests
{
    private static volatile bool s_callbackFired;
    private static int s_capturedPatchCount;
    private static int s_capturedCreateNodeCount;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFrame(BlazorNativeFrame* frame)
    {
        // Copy synchronously — the frame + arena memory die when we return.
        s_capturedPatchCount = frame->PatchCount;
        var patches = (BlazorNativePatch*)frame->Patches;
        int creates = 0;
        for (int i = 0; i < frame->PatchCount; i++)
        {
            if (patches[i].Kind == BlazorNativePatchKind.CreateNode)
                creates++;
        }
        s_capturedCreateNodeCount = creates;
        s_callbackFired = true;
    }

    [Fact]
    public void TryMount_HelloComponent_ReturnsZero_AndFiresFrameCallback()
    {
        s_callbackFired = false;
        s_capturedPatchCount = 0;

        delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void> fn = &OnFrame;
        HostSession.SetFrameCallback((IntPtr)fn);
        try
        {
            int status = HostSession.TryMount("HelloComponent");

            Assert.Equal(0, status);
            Assert.True(s_callbackFired, "frame callback did not fire during mount");
            Assert.True(s_capturedPatchCount > 0,
                $"expected encoded patches, got {s_capturedPatchCount}");
            // Hello has 4 elements (outer div, inner div, button, input).
            Assert.True(s_capturedCreateNodeCount >= 4,
                $"expected >= 4 CreateNode patches, got {s_capturedCreateNodeCount}");
        }
        finally
        {
            HostSession.SetFrameCallback(IntPtr.Zero);
        }
    }

    [Fact]
    public void TryMount_UnknownComponent_ReturnsOne()
    {
        Assert.Equal(1, HostSession.TryMount("NoSuchComponent"));
    }
}
