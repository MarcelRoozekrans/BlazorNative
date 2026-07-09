using System.Runtime.InteropServices;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.1 Gate 1 — .NET half of the dual struct-layout drift-catcher for the
// shell-bridge ABI.
//
// The Kotlin side (src/BlazorNative.Jni/.../ShellBridge.kt, Gate 2) mirrors
// BlazorNativeBridgeCallbacks / BlazorNativeFetchRequest /
// BlazorNativeFetchResponse as JNA Structures and asserts the same sizes in
// ShellBridgeTest.kt. These two facts pin the .NET side of that contract.
//
// If one of these tests fails: the struct layout drifted. Fix the layout in
// src/BlazorNative.Runtime/BridgeProtocolNative.cs — NEVER the expected
// sizes here — or, for an intentional ABI change, update the Kotlin mirror
// AND both drift tests together.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BridgeProtocolNativeTests
{
    [Fact]
    public void BlazorNativeBridgeCallbacks_Is48Bytes()
    {
        Assert.Equal(48, Marshal.SizeOf<BlazorNativeBridgeCallbacks>());
    }

    [Fact]
    public void BlazorNativeFetchRequest_Is32Bytes()
    {
        Assert.Equal(32, Marshal.SizeOf<BlazorNativeFetchRequest>());
    }

    [Fact]
    public void BlazorNativeFetchResponse_Is32Bytes()
    {
        Assert.Equal(32, Marshal.SizeOf<BlazorNativeFetchResponse>());
    }

    // Size checks alone don't catch same-width field REORDERS (swapping
    // Navigate and FetchBegin still totals 48 bytes) — but the Kotlin
    // registrar populates the struct by field position, so a reorder silently
    // routes storage calls to navigation. Pin each field's offset explicitly.
    [Fact]
    public void BlazorNativeBridgeCallbacks_FieldOffsets_MatchKotlinMirror()
    {
        Assert.Equal(0,  (int)Marshal.OffsetOf<BlazorNativeBridgeCallbacks>(nameof(BlazorNativeBridgeCallbacks.Navigate)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BlazorNativeBridgeCallbacks>(nameof(BlazorNativeBridgeCallbacks.CurrentRoute)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BlazorNativeBridgeCallbacks>(nameof(BlazorNativeBridgeCallbacks.StorageRead)));
        Assert.Equal(24, (int)Marshal.OffsetOf<BlazorNativeBridgeCallbacks>(nameof(BlazorNativeBridgeCallbacks.StorageWrite)));
        Assert.Equal(32, (int)Marshal.OffsetOf<BlazorNativeBridgeCallbacks>(nameof(BlazorNativeBridgeCallbacks.StorageDelete)));
        Assert.Equal(40, (int)Marshal.OffsetOf<BlazorNativeBridgeCallbacks>(nameof(BlazorNativeBridgeCallbacks.FetchBegin)));
    }

    [Fact]
    public void BlazorNativeFetchRequest_FieldOffsets_MatchKotlinMirror()
    {
        Assert.Equal(0,  (int)Marshal.OffsetOf<BlazorNativeFetchRequest>(nameof(BlazorNativeFetchRequest.Url)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BlazorNativeFetchRequest>(nameof(BlazorNativeFetchRequest.Method)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BlazorNativeFetchRequest>(nameof(BlazorNativeFetchRequest.Body)));
        Assert.Equal(24, (int)Marshal.OffsetOf<BlazorNativeFetchRequest>(nameof(BlazorNativeFetchRequest.HeadersJson)));
    }

    [Fact]
    public void BlazorNativeFetchResponse_FieldOffsets_MatchKotlinMirror()
    {
        Assert.Equal(0,  (int)Marshal.OffsetOf<BlazorNativeFetchResponse>(nameof(BlazorNativeFetchResponse.StatusCode)));
        Assert.Equal(4,  (int)Marshal.OffsetOf<BlazorNativeFetchResponse>(nameof(BlazorNativeFetchResponse.Ok)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BlazorNativeFetchResponse>(nameof(BlazorNativeFetchResponse.BodyUtf8)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BlazorNativeFetchResponse>(nameof(BlazorNativeFetchResponse.ErrorMessage)));
        Assert.Equal(24, (int)Marshal.OffsetOf<BlazorNativeFetchResponse>(nameof(BlazorNativeFetchResponse.HeadersJson)));
    }
}
