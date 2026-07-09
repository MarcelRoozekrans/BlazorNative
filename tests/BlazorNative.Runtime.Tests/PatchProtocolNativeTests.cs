using System.Runtime.InteropServices;
using BlazorNative.Runtime;

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
// src/BlazorNative.Runtime/PatchProtocolNative.cs — NEVER the expected
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

    // Size checks alone don't catch same-width field REORDERS (e.g. swapping
    // NodeId and ParentNodeId still totals 48 bytes) — but Kotlin's
    // NativeFrameAdapter reads hardcoded byte offsets, so a reorder silently
    // scrambles every decoded patch. Pin each field's offset explicitly.
    [Fact]
    public void BlazorNativePatch_FieldOffsets_MatchKotlinAdapter()
    {
        Assert.Equal(0,  (int)Marshal.OffsetOf<BlazorNativePatch>(nameof(BlazorNativePatch.Kind)));
        Assert.Equal(4,  (int)Marshal.OffsetOf<BlazorNativePatch>(nameof(BlazorNativePatch.NodeId)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BlazorNativePatch>(nameof(BlazorNativePatch.ParentNodeId)));
        Assert.Equal(12, (int)Marshal.OffsetOf<BlazorNativePatch>(nameof(BlazorNativePatch.NodeType)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BlazorNativePatch>(nameof(BlazorNativePatch.AuxInt)));
        Assert.Equal(20, (int)Marshal.OffsetOf<BlazorNativePatch>(nameof(BlazorNativePatch.Reserved0)));
        Assert.Equal(24, (int)Marshal.OffsetOf<BlazorNativePatch>(nameof(BlazorNativePatch.Text)));
        Assert.Equal(32, (int)Marshal.OffsetOf<BlazorNativePatch>(nameof(BlazorNativePatch.PropName)));
        Assert.Equal(40, (int)Marshal.OffsetOf<BlazorNativePatch>(nameof(BlazorNativePatch.PropValue)));
    }

    [Fact]
    public void BlazorNativeFrame_FieldOffsets_MatchKotlinAdapter()
    {
        Assert.Equal(0,  (int)Marshal.OffsetOf<BlazorNativeFrame>(nameof(BlazorNativeFrame.Patches)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BlazorNativeFrame>(nameof(BlazorNativeFrame.PatchCount)));
        Assert.Equal(12, (int)Marshal.OffsetOf<BlazorNativeFrame>(nameof(BlazorNativeFrame.FrameId)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BlazorNativeFrame>(nameof(BlazorNativeFrame.TimestampMs)));
    }
}
