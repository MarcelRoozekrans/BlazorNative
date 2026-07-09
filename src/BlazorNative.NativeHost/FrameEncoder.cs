using BlazorNative.Renderer;

namespace BlazorNative.NativeHost;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d RenderFrame → BlazorNativePatch[] encoder.
//
// Field mapping (CONTRACTUAL — Kotlin NativeFrameAdapter decodes exactly this;
// asserted by FrameEncoderTests.cs on this side):
//
//   CreateNodePatch(NodeId, NodeType, ParentId) → Kind=CreateNode, NodeId,
//       ParentNodeId = ParentId ?? -1, NodeType = enum (throws on unknown)
//   AppendChildPatch(ParentId, ChildId, AtIndex) → Kind=AppendChild,
//       NodeId = ChildId, ParentNodeId = ParentId, AuxInt = AtIndex
//   RemoveNodePatch(NodeId)                     → Kind=RemoveNode, NodeId
//   UpdatePropPatch(NodeId, Name, Value)        → Kind=UpdateProp,
//       PropName = Name, PropValue = Value (NULL if null)
//   ReplaceTextPatch(NodeId, Text)              → Kind=ReplaceText, Text
//   SetStylePatch(NodeId, Property, Value)      → Kind=SetStyle,
//       PropName = Property, PropValue = Value (NULL if null)
//   AttachEventPatch(NodeId, EventName, HandlerId) → Kind=AttachEvent,
//       Text = EventName, AuxInt = HandlerId
//   DetachEventPatch(NodeId, HandlerId)         → Kind=DetachEvent,
//       NodeId, AuxInt = HandlerId
//   CommitFramePatch(FrameId, TimestampMs)      → Kind=CommitFrame,
//       NodeId = FrameId (the timestamp rides the envelope, not the patch)
//
// Unused fields: AllocPatches hands back zeroed structs and the switch below
// only writes the fields each kind uses, so every unused field is 0/NULL.
// ParentNodeId = -1 is written ONLY for CreateNode with a null parent; kinds
// that don't use ParentNodeId leave it 0.
//
// Lifetime: everything the returned BlazorNativeFrame points into lives in
// the passed arena — valid until the arena's next Rent() on this thread. The
// consumer (frame callback) must copy synchronously.
// ─────────────────────────────────────────────────────────────────────────────

internal static unsafe class FrameEncoder
{
    public static BlazorNativeFrame Encode(RenderFrame frame, FrameArena arena)
    {
        RenderPatch[] patches = frame.Patches;
        BlazorNativePatch* native = arena.AllocPatches(patches.Length);

        for (int i = 0; i < patches.Length; i++)
        {
            ref BlazorNativePatch dst = ref native[i];
            switch (patches[i])
            {
                case CreateNodePatch p:
                    dst.Kind = BlazorNativePatchKind.CreateNode;
                    dst.NodeId = p.NodeId;
                    dst.ParentNodeId = p.ParentId ?? -1;
                    dst.NodeType = MapNodeType(p.NodeType);
                    break;

                case AppendChildPatch p:
                    dst.Kind = BlazorNativePatchKind.AppendChild;
                    dst.NodeId = p.ChildId;
                    dst.ParentNodeId = p.ParentId;
                    dst.AuxInt = p.AtIndex;
                    break;

                case RemoveNodePatch p:
                    dst.Kind = BlazorNativePatchKind.RemoveNode;
                    dst.NodeId = p.NodeId;
                    break;

                case UpdatePropPatch p:
                    dst.Kind = BlazorNativePatchKind.UpdateProp;
                    dst.NodeId = p.NodeId;
                    dst.PropName = arena.AllocUtf8(p.Name);
                    dst.PropValue = arena.AllocUtf8(p.Value); // null → IntPtr.Zero
                    break;

                case ReplaceTextPatch p:
                    dst.Kind = BlazorNativePatchKind.ReplaceText;
                    dst.NodeId = p.NodeId;
                    dst.Text = arena.AllocUtf8(p.Text);
                    break;

                case SetStylePatch p:
                    dst.Kind = BlazorNativePatchKind.SetStyle;
                    dst.NodeId = p.NodeId;
                    dst.PropName = arena.AllocUtf8(p.Property);
                    dst.PropValue = arena.AllocUtf8(p.Value); // null → IntPtr.Zero
                    break;

                case AttachEventPatch p:
                    dst.Kind = BlazorNativePatchKind.AttachEvent;
                    dst.NodeId = p.NodeId;
                    dst.Text = arena.AllocUtf8(p.EventName);
                    dst.AuxInt = p.HandlerId;
                    break;

                case DetachEventPatch p:
                    dst.Kind = BlazorNativePatchKind.DetachEvent;
                    dst.NodeId = p.NodeId;
                    dst.AuxInt = p.HandlerId;
                    break;

                case CommitFramePatch p:
                    dst.Kind = BlazorNativePatchKind.CommitFrame;
                    dst.NodeId = p.FrameId; // timestamp rides the envelope
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(frame),
                        $"Unknown RenderPatch type '{patches[i].GetType().Name}' — " +
                        "add a case here AND a wire id to BlazorNativePatchKind (+ Kotlin mirror).");
            }
        }

        return new BlazorNativeFrame
        {
            Patches = (IntPtr)native,
            PatchCount = patches.Length,
            FrameId = frame.FrameId,
            TimestampMs = frame.TimestampMs,
        };
    }

    /// <summary>Maps PatchProtocol's string node types to wire enum values.
    /// Throws (message includes the offending string) on unknown types — an
    /// unknown type here means the Renderer and the wire protocol drifted.</summary>
    private static BlazorNativeNodeType MapNodeType(string nodeType) => nodeType switch
    {
        "view"   => BlazorNativeNodeType.View,
        "text"   => BlazorNativeNodeType.Text,
        "button" => BlazorNativeNodeType.Button,
        "input"  => BlazorNativeNodeType.Input,
        "image"  => BlazorNativeNodeType.Image,
        "scroll" => BlazorNativeNodeType.Scroll,
        "picker" => BlazorNativeNodeType.Picker,
        _ => throw new ArgumentOutOfRangeException(
            nameof(nodeType), nodeType,
            $"Unknown node type '{nodeType}' — not representable in BlazorNativeNodeType."),
    };
}
