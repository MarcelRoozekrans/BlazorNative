namespace BlazorNative.Renderer;

// ─────────────────────────────────────────────────────────────────────────────
// Patch Protocol
//
// The in-memory patch model produced by the headless renderer. Each render
// cycle yields a list of RenderPatch commands the native shell applies to its
// widget tree. This is NO LONGER a JSON protocol: the wire format is the
// typed-struct encoding in BlazorNative.Runtime (PatchProtocolNative /
// FrameEncoder), consumed on the Kotlin side by NativeFrameAdapter. The JSON
// layer (RendererJsonContext + polymorphic attributes) was deleted with the
// WASM era in Phase 3.0e.
//
// Design goals:
//   • Minimal payload — only diffs, never full tree snapshots
//   • Zero-alloc friendly — structs for hot-path types
//
// NativeUiEvent stays as the in-memory event model; its (de)serialization for
// blazornative_dispatch_event(argsJsonUtf8) is a Phase 3.2 design decision.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A single atomic UI change produced by the headless renderer.</summary>
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

/// <summary>An event dispatched from the native shell back into the renderer.</summary>
public sealed record NativeUiEvent(
    int    NodeId,
    int    HandlerId,
    string EventName,
    string? Payload = null);   // JSON payload (e.g. input value, scroll position)
