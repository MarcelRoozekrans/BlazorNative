using System.Runtime.InteropServices;
using BlazorNative.NativeHost;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d Gate 1 — FrameArena behavior tests.
//
// FrameArena is the pooled native scratch block backing FrameEncoder: one
// native allocation per thread (process-lifetime cache), bump-pointer allocs
// inside it, Reset() on Rent(). The steady-state contract that matters for
// the 60fps frame path: after warmup, a rent/alloc/dispose cycle performs
// ZERO managed allocation — pinned by the 1000-frame test below.
// ─────────────────────────────────────────────────────────────────────────────

public sealed unsafe class FrameArenaTests
{
    [Fact]
    public void AllocPatches_ReturnsWritableZeroedBlock()
    {
        using var arena = FrameArena.Rent();
        BlazorNativePatch* patches = arena.AllocPatches(3);

        Assert.True(patches != null);

        // Initial contents must be zeroed (FrameEncoder relies on this for
        // "unused fields are 0" semantics).
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal((BlazorNativePatchKind)0, patches[i].Kind);
            Assert.Equal(0, patches[i].NodeId);
            Assert.Equal(0, patches[i].ParentNodeId);
            Assert.Equal(BlazorNativeNodeType.None, patches[i].NodeType);
            Assert.Equal(0, patches[i].AuxInt);
            Assert.Equal(IntPtr.Zero, patches[i].Text);
            Assert.Equal(IntPtr.Zero, patches[i].PropName);
            Assert.Equal(IntPtr.Zero, patches[i].PropValue);
        }

        // Block must be writable and hold values.
        patches[1].Kind = BlazorNativePatchKind.ReplaceText;
        patches[1].NodeId = 42;
        Assert.Equal(BlazorNativePatchKind.ReplaceText, patches[1].Kind);
        Assert.Equal(42, patches[1].NodeId);
    }

    [Fact]
    public void AllocUtf8_RoundTripsIncludingNonAscii()
    {
        using var arena = FrameArena.Rent();

        const string s = "héllo→世界";
        IntPtr p = arena.AllocUtf8(s);
        Assert.NotEqual(IntPtr.Zero, p);
        Assert.Equal(s, Marshal.PtrToStringUTF8(p));

        Assert.Equal(IntPtr.Zero, arena.AllocUtf8(null));

        IntPtr empty = arena.AllocUtf8("");
        Assert.NotEqual(IntPtr.Zero, empty);
        Assert.Equal("", Marshal.PtrToStringUTF8(empty));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var arena = FrameArena.Rent();
        arena.AllocPatches(1);
        arena.Dispose();
        arena.Dispose(); // must not throw
    }

    [Fact]
    public void RentDispose_1000Frames_DoesNotGrowUnbounded()
    {
        // Warm-up: 100 cycles let the thread-cached arena grow to its
        // steady-state capacity (and JIT the code paths).
        for (int i = 0; i < 100; i++)
            RunEncodeIshCycle();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 900; i++)
            RunEncodeIshCycle();

        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // The arena must not allocate managed memory per frame after warmup.
        // Small slack (< 100KB over 900 cycles) for runtime incidentals.
        Assert.True(delta < 100_000,
            $"Expected < 100000 managed bytes allocated across 900 rent/dispose cycles, got {delta}");
    }

    private static void RunEncodeIshCycle()
    {
        using var arena = FrameArena.Rent();
        BlazorNativePatch* patches = arena.AllocPatches(8);
        for (int i = 0; i < 8; i++)
        {
            patches[i].Kind = BlazorNativePatchKind.SetStyle;
            patches[i].NodeId = i;
            patches[i].PropName = arena.AllocUtf8("backgroundColor");
            patches[i].PropValue = arena.AllocUtf8("#336699");
        }
    }
}
