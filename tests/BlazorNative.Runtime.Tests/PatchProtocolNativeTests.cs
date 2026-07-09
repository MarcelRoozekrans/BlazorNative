using System.Runtime.InteropServices;
using BlazorNative.NativeHost;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d Gate 1 — .NET half of the dual struct-layout drift-catcher.
//
// The Kotlin side (src/BlazorNative.Jni/.../NativeFrameAdapter.kt) reads
// BlazorNativePatch / BlazorNativeFrame at HARDCODED byte offsets, and asserts
// the same sizes in NativeFrameAdapterTest.kt. These two facts pin the .NET
// side of that contract.
//
// If one of these tests fails: the struct layout drifted. Fix the layout in
// src/BlazorNative.NativeHost/PatchProtocolNative.cs — NEVER the expected
// sizes here — or, for an intentional ABI change, update the Kotlin offsets
// AND both drift tests together.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PatchProtocolNativeTests
{
    [Fact]
    public void BlazorNativePatch_Is48Bytes()
    {
        Assert.Equal(48, Marshal.SizeOf<BlazorNativePatch>());
    }

    [Fact]
    public void BlazorNativeFrame_Is24Bytes()
    {
        Assert.Equal(24, Marshal.SizeOf<BlazorNativeFrame>());
    }
}
