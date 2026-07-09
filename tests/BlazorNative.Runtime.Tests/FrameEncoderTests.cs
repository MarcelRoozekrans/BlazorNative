using System.Runtime.InteropServices;
using BlazorNative.NativeHost;
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
    public void Encode_AllNineKinds_RoundTrips()
    {
        var frame = new RenderFrame(
            FrameId: 7,
            TimestampMs: 123456789L,
            Patches:
            [
                new CreateNodePatch(10, "button", 5),
                new AppendChildPatch(ParentId: 3, ChildId: 11, AtIndex: 2),
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
        Assert.Equal(9, native.PatchCount);
        Assert.Equal(7, native.FrameId);
        Assert.Equal(123456789L, native.TimestampMs);

        // 0: CreateNode
        var p = Decode(native, 0);
        Assert.Equal(BlazorNativePatchKind.CreateNode, p.Kind);
        Assert.Equal(10, p.NodeId);
        Assert.Equal(5, p.ParentNodeId);
        Assert.Equal(BlazorNativeNodeType.Button, p.NodeType);
        Assert.Equal(0, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.Text);
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);

        // 1: AppendChild — NodeId carries ChildId, ParentNodeId carries ParentId,
        //    AuxInt carries AtIndex.
        p = Decode(native, 1);
        Assert.Equal(BlazorNativePatchKind.AppendChild, p.Kind);
        Assert.Equal(11, p.NodeId);
        Assert.Equal(3, p.ParentNodeId);
        Assert.Equal(BlazorNativeNodeType.None, p.NodeType);
        Assert.Equal(2, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.Text);

        // 2: RemoveNode — ParentNodeId unused ⇒ 0 (only CreateNode-with-null-parent
        //    uses -1).
        p = Decode(native, 2);
        Assert.Equal(BlazorNativePatchKind.RemoveNode, p.Kind);
        Assert.Equal(12, p.NodeId);
        Assert.Equal(0, p.ParentNodeId);
        Assert.Equal(0, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.Text);
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);

        // 3: UpdateProp
        p = Decode(native, 3);
        Assert.Equal(BlazorNativePatchKind.UpdateProp, p.Kind);
        Assert.Equal(13, p.NodeId);
        Assert.Equal("placeholder", Marshal.PtrToStringUTF8(p.PropName));
        Assert.Equal("typé here…", Marshal.PtrToStringUTF8(p.PropValue));
        Assert.Equal(IntPtr.Zero, p.Text);

        // 4: ReplaceText
        p = Decode(native, 4);
        Assert.Equal(BlazorNativePatchKind.ReplaceText, p.Kind);
        Assert.Equal(14, p.NodeId);
        Assert.Equal("héllo→世界", Marshal.PtrToStringUTF8(p.Text));
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);

        // 5: SetStyle
        p = Decode(native, 5);
        Assert.Equal(BlazorNativePatchKind.SetStyle, p.Kind);
        Assert.Equal(15, p.NodeId);
        Assert.Equal("backgroundColor", Marshal.PtrToStringUTF8(p.PropName));
        Assert.Equal("#336699", Marshal.PtrToStringUTF8(p.PropValue));
        Assert.Equal(IntPtr.Zero, p.Text);

        // 6: AttachEvent — Text carries the event name, AuxInt the handler id.
        p = Decode(native, 6);
        Assert.Equal(BlazorNativePatchKind.AttachEvent, p.Kind);
        Assert.Equal(16, p.NodeId);
        Assert.Equal("click", Marshal.PtrToStringUTF8(p.Text));
        Assert.Equal(99, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);

        // 7: DetachEvent
        p = Decode(native, 7);
        Assert.Equal(BlazorNativePatchKind.DetachEvent, p.Kind);
        Assert.Equal(17, p.NodeId);
        Assert.Equal(98, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.Text);

        // 8: CommitFrame — NodeId carries FrameId; the timestamp rides the
        //    envelope, not the patch.
        p = Decode(native, 8);
        Assert.Equal(BlazorNativePatchKind.CommitFrame, p.Kind);
        Assert.Equal(7, p.NodeId);
        Assert.Equal(0, p.ParentNodeId);
        Assert.Equal(0, p.AuxInt);
        Assert.Equal(IntPtr.Zero, p.Text);
        Assert.Equal(IntPtr.Zero, p.PropName);
        Assert.Equal(IntPtr.Zero, p.PropValue);
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

    /// <summary>Reads patch i back through Marshal — the same byte-level view
    /// an out-of-process (Kotlin) reader has.</summary>
    private static BlazorNativePatch Decode(BlazorNativeFrame native, int i) =>
        Marshal.PtrToStructure<BlazorNativePatch>(
            native.Patches + i * Marshal.SizeOf<BlazorNativePatch>());
}
