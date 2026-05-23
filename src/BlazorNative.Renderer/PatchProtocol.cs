using System.Text.Json.Serialization;

namespace BlazorNative.Renderer;

// ─────────────────────────────────────────────────────────────────────────────
// Patch Protocol
//
// BlazorNative communicates UI changes from the .NET renderer to the native
// shell via a lightweight JSON patch protocol. Each render cycle produces a
// list of RenderPatch commands the native shell applies to its widget tree.
//
// Design goals:
//   • Minimal payload — only diffs, never full tree snapshots
//   • AOT-safe — all types registered via JsonSerializerContext
//   • Zero-alloc friendly — structs for hot-path types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A single atomic UI change produced by the headless renderer.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(CreateNodePatch),   "create")]
[JsonDerivedType(typeof(UpdatePropPatch),   "prop")]
[JsonDerivedType(typeof(AppendChildPatch),  "append")]
[JsonDerivedType(typeof(RemoveNodePatch),   "remove")]
[JsonDerivedType(typeof(ReplaceTextPatch),  "text")]
[JsonDerivedType(typeof(SetStylePatch),     "style")]
[JsonDerivedType(typeof(AttachEventPatch),  "event")]
[JsonDerivedType(typeof(DetachEventPatch),  "detach")]
[JsonDerivedType(typeof(CommitFramePatch),  "commit")]
public abstract record RenderPatch;

// ── Node lifecycle ────────────────────────────────────────────────────────────

/// <summary>Create a new native widget node.</summary>
public sealed record CreateNodePatch(
    int     NodeId,
    string  NodeType,       // "view" | "text" | "button" | "input" | "scroll" | "image"
    int?    ParentId = null
) : RenderPatch;

/// <summary>Append an existing node as a child of another.</summary>
public sealed record AppendChildPatch(
    int ParentId,
    int ChildId,
    int AtIndex = -1        // -1 = append at end
) : RenderPatch;

/// <summary>Remove a node and its subtree from the widget tree.</summary>
public sealed record RemoveNodePatch(
    int NodeId
) : RenderPatch;

// ── Property updates ──────────────────────────────────────────────────────────

/// <summary>Set or update a property on a node (e.g. placeholder, enabled, value).</summary>
public sealed record UpdatePropPatch(
    int    NodeId,
    string Name,
    string? Value           // null = remove property
) : RenderPatch;

/// <summary>Update the text content of a text node.</summary>
public sealed record ReplaceTextPatch(
    int    NodeId,
    string Text
) : RenderPatch;

/// <summary>Apply a style property to a node.</summary>
public sealed record SetStylePatch(
    int    NodeId,
    string Property,        // camelCase CSS-like: "backgroundColor", "fontSize"
    string? Value           // null = reset to default
) : RenderPatch;

// ── Events ────────────────────────────────────────────────────────────────────

/// <summary>Tell the native shell to start routing events of this type for a node.</summary>
public sealed record AttachEventPatch(
    int    NodeId,
    string EventName,       // "click" | "change" | "focus" | "blur" | "scroll"
    int    HandlerId        // opaque ID the WASM side registered
) : RenderPatch;

/// <summary>Stop routing an event for a node.</summary>
public sealed record DetachEventPatch(
    int NodeId,
    int HandlerId
) : RenderPatch;

// ── Frame boundary ────────────────────────────────────────────────────────────

/// <summary>
/// Signals the native shell that a batch of patches is complete and ready to apply.
/// The shell must apply all preceding patches atomically before this commit.
/// </summary>
public sealed record CommitFramePatch(
    int FrameId,
    long TimestampMs
) : RenderPatch;

// ── Frame envelope ────────────────────────────────────────────────────────────

/// <summary>A complete render frame — the unit sent over the bridge per render cycle.</summary>
public sealed record RenderFrame(
    int              FrameId,
    long             TimestampMs,
    RenderPatch[]    Patches);

/// <summary>An event dispatched from the native shell back into the WASM renderer.</summary>
public sealed record NativeUiEvent(
    int    NodeId,
    int    HandlerId,
    string EventName,
    string? Payload = null);   // JSON payload (e.g. input value, scroll position)

// ── AOT-safe JSON context ─────────────────────────────────────────────────────

[JsonSerializable(typeof(RenderFrame))]
[JsonSerializable(typeof(RenderPatch))]
[JsonSerializable(typeof(CreateNodePatch))]
[JsonSerializable(typeof(UpdatePropPatch))]
[JsonSerializable(typeof(AppendChildPatch))]
[JsonSerializable(typeof(RemoveNodePatch))]
[JsonSerializable(typeof(ReplaceTextPatch))]
[JsonSerializable(typeof(SetStylePatch))]
[JsonSerializable(typeof(AttachEventPatch))]
[JsonSerializable(typeof(DetachEventPatch))]
[JsonSerializable(typeof(CommitFramePatch))]
[JsonSerializable(typeof(NativeUiEvent))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class RendererJsonContext : JsonSerializerContext { }
