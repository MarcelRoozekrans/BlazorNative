using System.Runtime.InteropServices;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d native wire protocol — typed-struct replacement for the [FRAME]
// JSON-over-stdout transport (retired with the WASM era in Phase 3.0e; this is
// now the only frame transport).
//
// Layout contract: mirrored by offset constants in
// src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/NativeFrameAdapter.kt.
// Sizes asserted on both sides (PatchProtocolNativeTests.cs /
// NativeFrameAdapterTest.kt). If you change ANY field, update the Kotlin
// offsets + both drift tests.
//
// String/array ownership: native-owned, valid ONLY for the duration of the
// frame callback. The Kotlin side copies synchronously before returning.
//
// Endianness/width: this layout assumes little-endian byte order and 8-byte
// pointers (all supported targets: win-x64, linux-bionic-x64/arm64). A
// ByteBuffer-based reader must call order(ByteOrder.LITTLE_ENDIAN); JNA
// Pointer.getInt/getLong/getPointer read in native order and are safe as-is.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Wire ids for the RenderPatch types. AppendChild = 2 is
/// RESERVED-DORMANT since Phase 3.3 deleted AppendChildPatch (DoD #10 —
/// CreateNode.AuxInt carries InsertIndex instead): the id is never emitted
/// and never reused, so wire ids stay stable across hosts (no ABI break).</summary>
public enum BlazorNativePatchKind : int
{
    CreateNode = 1, AppendChild = 2 /* reserved-dormant */, RemoveNode = 3, UpdateProp = 4,
    ReplaceText = 5, SetStyle = 6, AttachEvent = 7, DetachEvent = 8, CommitFrame = 9,
}

/// <summary>Wire ids for CreateNode's widget class. Phase 7.3 extends the
/// vocabulary with Checkbox/Switch/Slider (8-10) — a wire-VOCABULARY extension,
/// not an ABI change: the id rides the existing int32 field of the 48-byte
/// patch (exports stay 9, the bridge stays 72 bytes). The vocabulary lives in
/// THREE mirrors that must move together: FrameEncoder.MapNodeType (throws on
/// unknown), Kotlin NativeFrameAdapter.nodeTypes and Swift
/// BnFrameAdapter.nodeTypes (both log-and-fallback to "?" on an index past
/// their array — which is exactly what a shell that missed the extension does
/// with 8/9/10 until its Gate lands).</summary>
public enum BlazorNativeNodeType : int
{
    None = 0, View = 1, Text = 2, Button = 3, Input = 4, Image = 5, Scroll = 6, Picker = 7,
    Checkbox = 8, Switch = 9, Slider = 10,
}

[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativePatch
{
    public BlazorNativePatchKind Kind;    // offset 0
    public int    NodeId;                 // offset 4  (CommitFrame: FrameId)
    public int    ParentNodeId;           // offset 8  (-1 = none)
    public BlazorNativeNodeType NodeType; // offset 12 (CreateNode only)
    public int    AuxInt;                 // offset 16 (CreateNode: InsertIndex, -1 = append — explicitly encoded, 0 is a valid front index; Attach/DetachEvent: HandlerId)
    public int    Reserved0;              // offset 20 — explicit pad so the pointers are 8-aligned
    public IntPtr Text;                   // offset 24 (ReplaceText: text; Attach/DetachEvent: eventName; NULL if unused)
    public IntPtr PropName;               // offset 32 (UpdateProp/SetStyle: name)
    public IntPtr PropValue;              // offset 40 (UpdateProp/SetStyle: value; NULL = null)
}                                         // total 48 bytes

[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativeFrame
{
    public IntPtr Patches;                // offset 0 — BlazorNativePatch*
    public int    PatchCount;             // offset 8
    public int    FrameId;                // offset 12
    public long   TimestampMs;            // offset 16
}                                         // total 24 bytes
