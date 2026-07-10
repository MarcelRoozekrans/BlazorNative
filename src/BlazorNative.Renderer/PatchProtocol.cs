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
/// <remarks>Phase 3.3 (DoD #10): <paramref name="InsertIndex"/> is the HOST
/// child position the new view takes inside <paramref name="ParentId"/> —
/// −1 = append at end (the mount-walk common case), ≥0 = insert at that
/// index (mid-list keyed inserts; 0 = front is a VALID index, which is why
/// −1 is encoded explicitly on the wire). Counts real views only — the
/// renderer translates Blazor sibling slots to view indices, skipping
/// component slots (NativeWidgetTree.TranslateToHostInsertIndex). This
/// retires AppendChildPatch: creation carries its own placement, and moves
/// are remove+insert at POC fidelity (a dedicated move patch is YAGNI).</remarks>
public sealed record CreateNodePatch(
    int     NodeId,
    string  NodeType,       // "view" | "text" | "button" | "input" | "scroll" | "image"
    int?    ParentId = null,
    int     InsertIndex = -1
) : RenderPatch;

// AppendChildPatch DELETED (Phase 3.3, DoD #10) — CreateNodePatch.InsertIndex
// carries placement. Its wire kind (BlazorNativePatchKind.AppendChild = 2)
// stays reserved-dormant so wire ids never renumber.

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
/// <remarks>Re-attach for the same (node, event) REPLACES the prior handler —
/// last wins; no DetachEventPatch precedes it. Blazor emits this shape when a
/// re-render swaps a handler delegate in place (a SetAttribute edit with a
/// fresh handlerId, no RemoveAttribute); the renderer's detach registry
/// follows suit, so a later detach carries the NEWEST handlerId. Hosts must
/// swap their watcher, never stack a second one.</remarks>
public sealed record AttachEventPatch(
    int    NodeId,
    string EventName,       // "click" | "change" | "focus" | "blur" | "scroll"
    int    HandlerId        // opaque ID the .NET runtime side registered
) : RenderPatch;

/// <summary>Stop routing an event for a node. Phase 3.3 (carryover e):
/// emitted when a re-render removes an on* attribute — the renderer resolves
/// the ORIGINAL handlerId through its (nodeId, eventName) registry, and
/// <paramref name="EventName"/> tells the host exactly which watcher to drop
/// (no map-membership guessing; rides the wire's free Text field).</summary>
public sealed record DetachEventPatch(
    int    NodeId,
    int    HandlerId,
    string EventName        // "click" | "change" | "focus" | "blur" | "scroll"
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
