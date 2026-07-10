using System.Runtime.InteropServices;
using BlazorNative.Runtime;
using BlazorNative.Renderer;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d Gate 1 — FrameEncoder mapping tests.
//
// The RenderPatch → BlazorNativePatch field mapping asserted here is
// CONTRACTUAL: the Kotlin NativeFrameAdapter decodes these exact fields at
// hardcoded offsets. The mapping table lives in the Gate 1 plan and in
// FrameEncoder.cs — if a mapping changes, Kotlin + both sides' tests change
// with it.
//
// Decoding here goes through Marshal (PtrToStructure / PtrToStringUTF8) on
// purpose: it exercises the same byte layout an out-of-process reader sees,
// not just C# field access.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class FrameEncoderTests
{
    [Fact]
    public void Encode_AllEightKinds_RoundTrips()
    {
        // AppendChildPatch is DELETED (Phase 3.3, DoD #10) — 8 live kinds; its
        // wire id stays reserved-dormant (see BlazorNativePatchKind).
        var frame = new RenderFrame(
            FrameId: 7,
            TimestampMs: 123456789L,
            Patches:
            [
                new CreateNodePatch(10, "button", 5, InsertIndex: 2),
                new RemoveNodePatch(12),
                new UpdatePropPatch(13, "placeholder", "typé here…"),
                new ReplaceTextPatch(14, "héllo→世界"),
                new SetStylePatch(15, "backgroundColor", "#336699"),
                new AttachEventPatch(16, "click", HandlerId: 99),
                new DetachEventPatch(17, HandlerId: 98),
                new CommitFramePatch(FrameId: 7, TimestampMs: 123456789L),
            ]);

        using var arena = FrameArena.Rent();
        BlazorNativeFrame native = FrameEncoder.Encode(frame, arena);

        // Envelope
        Assert.NotEqual(IntPtr.Zero, native.Patches);
        Assert.Equal(8, native.PatchCount);
        Assert.Equal(7, native.FrameId);
        Assert.Equal(123456789L, native.TimestampMs);

        // 0: CreateNode — AuxInt carries InsertIndex (Phase 3.3, DoD #10).
        var p = Decode(native, 0);
        Assert.Equal(BlazorNativePatchKind.CreateNode, p.Kind);
        Assert.Equal(10, p.NodeId);
        Assert.Equal(5, p.ParentNodeId);
        Assert.Equal(BlazorNativeNodeType.Button, p.NodeType);
        Assert.Equal(2, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.Text);
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);

        // 1: RemoveNode — ParentNodeId unused ⇒ 0 (only CreateNode-with-null-parent
        //    uses -1).
        p = Decode(native, 1);
        Assert.Equal(BlazorNativePatchKind.RemoveNode, p.Kind);
        Assert.Equal(12, p.NodeId);
        Assert.Equal(0, p.ParentNodeId);
        Assert.Equal(0, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.Text);
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);

        // 2: UpdateProp
        p = Decode(native, 2);
        Assert.Equal(BlazorNativePatchKind.UpdateProp, p.Kind);
        Assert.Equal(13, p.NodeId);
        Assert.Equal("placeholder", Marshal.PtrToStringUTF8(p.PropName));
        Assert.Equal("typé here…", Marshal.PtrToStringUTF8(p.PropValue));
        Assert.Equal(IntPtr.Zero, p.Text);

        // 3: ReplaceText
        p = Decode(native, 3);
        Assert.Equal(BlazorNativePatchKind.ReplaceText, p.Kind);
        Assert.Equal(14, p.NodeId);
        Assert.Equal("héllo→世界", Marshal.PtrToStringUTF8(p.Text));
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);

        // 4: SetStyle
        p = Decode(native, 4);
        Assert.Equal(BlazorNativePatchKind.SetStyle, p.Kind);
        Assert.Equal(15, p.NodeId);
        Assert.Equal("backgroundColor", Marshal.PtrToStringUTF8(p.PropName));
        Assert.Equal("#336699", Marshal.PtrToStringUTF8(p.PropValue));
        Assert.Equal(IntPtr.Zero, p.Text);

        // 5: AttachEvent — Text carries the event name, AuxInt the handler id.
        p = Decode(native, 5);
        Assert.Equal(BlazorNativePatchKind.AttachEvent, p.Kind);
        Assert.Equal(16, p.NodeId);
        Assert.Equal("click", Marshal.PtrToStringUTF8(p.Text));
        Assert.Equal(99, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);

        // 6: DetachEvent
        p = Decode(native, 6);
        Assert.Equal(BlazorNativePatchKind.DetachEvent, p.Kind);
        Assert.Equal(17, p.NodeId);
        Assert.Equal(98, p.AuxInt);

        // 7: CommitFrame — NodeId carries FrameId; the timestamp rides the
        //    envelope, not the patch.
        p = Decode(native, 7);
        Assert.Equal(BlazorNativePatchKind.CommitFrame, p.Kind);
        Assert.Equal(7, p.NodeId);
        Assert.Equal(0, p.ParentNodeId);
        Assert.Equal(0, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.Text);
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);
    }

    [Fact]
    public void Encode_CreateNode_InsertIndexExplicitlyEncoded()
    {
        // −1 (append) is EXPLICITLY encoded — 0 is a valid front index, so the
        // wire must never rely on "0 means unset" (Phase 3.3 design §3).
        var frame = new RenderFrame(1, 0L,
        [
            new CreateNodePatch(1, "view", null),                 // default −1
            new CreateNodePatch(2, "view", 1, InsertIndex: 0),    // front
        ]);

        using var arena = FrameArena.Rent();
        BlazorNativeFrame native = FrameEncoder.Encode(frame, arena);

        Assert.Equal(-1, Decode(native, 0).AuxInt);
        Assert.Equal(0, Decode(native, 1).AuxInt);
    }

    /// <summary>Stand-in for a RenderPatch the encoder has no arm for — the
    /// shape AppendChildPatch left behind when Phase 3.3 deleted it.</summary>
    private sealed record RetiredPatch : RenderPatch;

    [Fact]
    public void Encode_UnknownPatchType_Throws()
    {
        // The AppendChild encoder arm is deleted with its record — any patch
        // type without an arm (including a resurrected AppendChild) must throw,
        // not silently emit garbage on the wire.
        var frame = new RenderFrame(1, 0L, [new RetiredPatch()]);

        using var arena = FrameArena.Rent();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => FrameEncoder.Encode(frame, arena));
        Assert.Contains("RetiredPatch", ex.Message);
    }

    [Theory]
    [InlineData("view",   BlazorNativeNodeType.View)]
    [InlineData("text",   BlazorNativeNodeType.Text)]
    [InlineData("button", BlazorNativeNodeType.Button)]
    [InlineData("input",  BlazorNativeNodeType.Input)]
    [InlineData("image",  BlazorNativeNodeType.Image)]
    [InlineData("scroll", BlazorNativeNodeType.Scroll)]
    [InlineData("picker", BlazorNativeNodeType.Picker)]
    public void Encode_AllSevenNodeTypes_MapCorrectly(string nodeType, BlazorNativeNodeType expected)
    {
        var frame = new RenderFrame(1, 0L, [new CreateNodePatch(1, nodeType, null)]);

        using var arena = FrameArena.Rent();
        BlazorNativeFrame native = FrameEncoder.Encode(frame, arena);

        Assert.Equal(expected, Decode(native, 0).NodeType);
    }

    [Fact]
    public void Encode_NullOptionals()
    {
        var frame = new RenderFrame(1, 0L,
        [
            new UpdatePropPatch(1, "enabled", null),
            new CreateNodePatch(1, "view", null),
        ]);

        using var arena = FrameArena.Rent();
        BlazorNativeFrame native = FrameEncoder.Encode(frame, arena);

        var updateProp = Decode(native, 0);
        Assert.Equal("enabled", Marshal.PtrToStringUTF8(updateProp.PropName));
        Assert.Equal(IntPtr.Zero, updateProp.PropValue); // null value ⇒ NULL

        var createNode = Decode(native, 1);
        Assert.Equal(-1, createNode.ParentNodeId); // null parent ⇒ -1
    }

    [Fact]
    public void Encode_UnknownNodeType_Throws()
    {
        var frame = new RenderFrame(1, 0L, [new CreateNodePatch(1, "blink")]);

        using var arena = FrameArena.Rent();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => FrameEncoder.Encode(frame, arena));
        Assert.Contains("blink", ex.Message);
    }

    [Fact]
    public unsafe void Encode_SteadyState_900Frames_StaysWithinManagedAllocBound()
    {
        // FrameArenaTests pins the arena's zero-alloc contract in isolation;
        // this pins the PRODUCTION path — FrameEncoder.Encode over a realistic
        // prebuilt frame — so a future encoder change that starts allocating
        // per patch (string concat, boxing, LINQ) fails here, not in profiling.
        var frame = new RenderFrame(
            FrameId: 1,
            TimestampMs: 42L,
            Patches:
            [
                new CreateNodePatch(1, "view", null),
                new SetStylePatch(1, "backgroundColor", "#FFEEAA"),
                new SetStylePatch(1, "padding", "16"),
                new CreateNodePatch(2, "text", 1),
                new ReplaceTextPatch(2, "Hello, BlazorNative!"),
                new UpdatePropPatch(3, "placeholder", "Type here..."),
                new CreateNodePatch(4, "button", 1, InsertIndex: 0),
                new CommitFramePatch(FrameId: 1, TimestampMs: 42L),
            ]);

        static void EncodeOnce(RenderFrame f)
        {
            using var arena = FrameArena.Rent();
            BlazorNativeFrame native = FrameEncoder.Encode(f, arena);
            // Consume the result so the JIT can't dead-code the encode.
            if (native.PatchCount != f.Patches.Length)
                throw new InvalidOperationException("encode dropped patches");
        }

        // Warmup: JIT + arena steady-state capacity.
        for (int i = 0; i < 100; i++)
            EncodeOnce(frame);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 900; i++)
            EncodeOnce(frame);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // Same bound as FrameArenaTests.RentDispose_1000Frames: < 100KB slack
        // across 900 frames for runtime incidentals; per-frame allocation
        // would blow well past this.
        Assert.True(delta < 100_000,
            $"Expected < 100000 managed bytes allocated across 900 Encode cycles, got {delta}");
    }

    /// <summary>Reads patch i back through Marshal — the same byte-level view
    /// an out-of-process (Kotlin) reader has.</summary>
    private static BlazorNativePatch Decode(BlazorNativeFrame native, int i) =>
        Marshal.PtrToStructure<BlazorNativePatch>(
            native.Patches + i * Marshal.SizeOf<BlazorNativePatch>());
}
