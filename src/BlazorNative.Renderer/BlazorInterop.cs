// ─────────────────────────────────────────────────────────────────────────────
// BlazorInterop.cs — SINGLE SEAM between BlazorNative.Renderer and the
// internal layout of Microsoft.AspNetCore.Components.
//
// Bound to Microsoft.AspNetCore.Components 10.0.* — bump BlazorCompatVersion
// and re-verify against the linked Blazor source revision before any major-
// version package upgrade.
//
// Phase 1.1 finding: against Blazor 10.0.8, almost every render-tree member
// the renderer needs is already accessible as a public field or public
// property. Only Renderer.DispatchEventAsync (which takes the internal
// EventFieldInfo type) still requires UnsafeAccessor + UnsafeAccessorType.
//
// The Bn* wrappers remain because they:
//   1. Quarantine all `Microsoft.AspNetCore.Components.RenderTree` names to
//      this one file (NativeRenderer.cs never references those internals);
//   2. Give us a single place to swap in [UnsafeAccessor] field reads later
//      if Blazor ever makes any of the currently-public members internal.
//
// See docs/plans/2026-05-23-renderer-internal-api-design.md for rationale.
// ─────────────────────────────────────────────────────────────────────────────

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using BlazorRenderer = Microsoft.AspNetCore.Components.RenderTree.Renderer;

namespace BlazorNative.Renderer;

internal static class BlazorInterop
{
    public static readonly Version BlazorCompatVersion = new(10, 0);

    private const string EventFieldInfoTypeName =
        "Microsoft.AspNetCore.Components.RenderTree.EventFieldInfo, Microsoft.AspNetCore.Components";

    static BlazorInterop()
    {
        VerifyVersion();
        VerifyAccessors();
    }

    /// <summary>Idempotent trigger for the static constructor (probes layout).</summary>
    public static void EnsureInitialized() { }

    private static void VerifyVersion()
    {
        var actual = typeof(BlazorRenderer).Assembly.GetName().Version;
        if (actual is null
            || actual.Major != BlazorCompatVersion.Major
            || actual.Minor != BlazorCompatVersion.Minor)
        {
            throw new BlazorVersionMismatchException(
                $"BlazorNative.Renderer expects Microsoft.AspNetCore.Components " +
                $"{BlazorCompatVersion.Major}.{BlazorCompatVersion.Minor}.* — " +
                $"found {actual?.ToString() ?? "<unknown>"}. " +
                $"Update BlazorInterop.cs (see file header) or pin the package.");
        }
    }

    private static void VerifyAccessors()
    {
        var failures = new List<string>();

        // Blazor's Renderer.DispatchEventAsync(ulong, EventFieldInfo?, EventArgs) is
        // the one accessor we genuinely depend on. If Blazor renames it or changes
        // its arity, the [UnsafeAccessor(Method)] binding fails at first call —
        // verify the underlying member exists *now* so we fail at load time instead.
        // Suppression scope: IL2057 (Type.GetType with non-recognized string literal)
        // is silenced here because EventFieldInfo is kept alive by [UnsafeAccessorType]
        // on RefAccessors.DispatchEventAsync below — the trimmer roots the type via
        // that reference, this lookup is for drift detection only and never invokes
        // members reflectively. IL2026 (RequiresUnreferencedCode) is NOT suppressed
        // because Type.GetType(string, bool) doesn't carry RUC; if a future Blazor
        // release annotates EventFieldInfo with [RequiresUnreferencedCode], IL2026
        // would surface and we'd add the suppression then with concrete justification.
        [UnconditionalSuppressMessage("Trimming", "IL2057",
            Justification = "Type name is a const referring to a type rooted by [UnsafeAccessorType] " +
                            "on RefAccessors.DispatchEventAsync. Lookup is for drift detection only.")]
        static Type? GetEventFieldInfoType() =>
            Type.GetType(EventFieldInfoTypeName, throwOnError: false);

        var eventFieldInfoType = GetEventFieldInfoType();
        if (eventFieldInfoType is null)
        {
            failures.Add("Microsoft.AspNetCore.Components.RenderTree.EventFieldInfo type not found");
        }
        else
        {
            var dispatchMethod = typeof(BlazorRenderer).GetMethod(
                "DispatchEventAsync",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(ulong), eventFieldInfoType, typeof(EventArgs) },
                modifiers: null);
            if (dispatchMethod is null)
                failures.Add("BlazorRenderer.DispatchEventAsync(ulong, EventFieldInfo?, EventArgs) not found");
        }

        // ComponentBase.StateHasChanged (protected, parameterless) backs the
        // Phase 4.2 test-only re-render seam (NativeRenderer
        // .TriggerRootRenderForTests). Same rationale as above: verify the
        // member NOW so an [UnsafeAccessor] binding failure surfaces at load
        // time, not at first test-seam call.
        var stateHasChanged = typeof(ComponentBase).GetMethod(
            "StateHasChanged",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (stateHasChanged is null)
            failures.Add("ComponentBase.StateHasChanged() not found");

        if (failures.Count > 0)
            throw new BlazorVersionMismatchException(
                "Blazor internal-layout drift detected:\n  - " + string.Join("\n  - ", failures));
    }

    public static Task DispatchEventViaAccessor(
        BlazorRenderer renderer,
        ulong eventHandlerId,
        EventArgs eventArgs)
        => RefAccessors.DispatchEventAsync(renderer, eventHandlerId, fieldInfo: null, eventArgs);

    /// <summary>Phase 4.2: <c>ComponentBase.StateHasChanged()</c> — protected,
    /// so it needs an accessor. Only consumer: NativeRenderer's test-only
    /// re-render seam (<c>TriggerRootRenderForTests</c>).</summary>
    public static void StateHasChangedViaAccessor(ComponentBase component)
        => RefAccessors.StateHasChanged(component);
}

/// <summary>Thrown when the linked Blazor assembly's internal layout no longer matches the shape
/// this renderer reflects over.</summary>
/// <remarks>Not part of the supported public API: it escapes only from the renderer's
/// reflection-over-Blazor-internals seam (this file), which runs behind
/// <see cref="NativeRenderer"/>. A consumer would catch it only if it were driving the renderer
/// directly, which is unsupported. Public because the seam that throws it is reached from types
/// outside <c>BlazorInterop</c>'s accessibility. Tier NOT-API.</remarks>
// Fully qualified — see the note on NativeRenderer: System.ComponentModel.IComponent would
// collide with Microsoft.AspNetCore.Components.IComponent in this file.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class BlazorVersionMismatchException : Exception
{
    public BlazorVersionMismatchException(string message) : base(message) { }
}

// ── ArrayRange<T> ────────────────────────────────────────────────────────────
// Array and Count are public readonly fields of ArrayRange<T>.

internal struct BnArrayRange<T>
{
    private ArrayRange<T> _range;

    public BnArrayRange(in ArrayRange<T> range) { _range = range; }

    public int Count => _range.Count;
    public ref T this[int i] => ref _range.Array[i];

    public Enumerator GetEnumerator() => new(in _range);

    public struct Enumerator
    {
        private ArrayRange<T> _range;
        private int _index;
        public Enumerator(in ArrayRange<T> range) { _range = range; _index = -1; }
        public bool MoveNext() => ++_index < _range.Count;
        public ref T Current => ref _range.Array[_index];
    }
}

// ── ArrayBuilderSegment<T> ───────────────────────────────────────────────────
// Array / Offset / Count are public on ArrayBuilderSegment<T>.

internal struct BnArrayBuilderSegment<T>
{
    private ArrayBuilderSegment<T> _seg;

    public BnArrayBuilderSegment(in ArrayBuilderSegment<T> seg) { _seg = seg; }

    public int Count => _seg.Count;
    public int Offset => _seg.Offset;
    public ref T this[int i] => ref _seg.Array[_seg.Offset + i];

    public Enumerator GetEnumerator() => new(in _seg);

    public struct Enumerator
    {
        private ArrayBuilderSegment<T> _seg;
        private int _index;
        public Enumerator(in ArrayBuilderSegment<T> seg) { _seg = seg; _index = -1; }
        public bool MoveNext() => ++_index < _seg.Count;
        public ref T Current => ref _seg.Array[_seg.Offset + _index];
    }
}

// ── RenderBatch ──────────────────────────────────────────────────────────────
// UpdatedComponents / ReferenceFrames / DisposedComponentIDs are public auto-
// properties on RenderBatch (which is a readonly struct).

internal ref struct BnRenderBatch
{
    private readonly ref readonly RenderBatch _batch;
    public BnRenderBatch(in RenderBatch batch) { _batch = ref batch; }

    public BnArrayRange<RenderTreeDiff>  UpdatedComponents    => new(_batch.UpdatedComponents);
    public BnArrayRange<RenderTreeFrame> ReferenceFrames      => new(_batch.ReferenceFrames);
    public BnArrayRange<int>             DisposedComponentIDs => new(_batch.DisposedComponentIDs);
}

// ── RenderTreeDiff ───────────────────────────────────────────────────────────
// ComponentId / Edits are public readonly fields.

internal ref struct BnRenderTreeDiff
{
    private readonly ref readonly RenderTreeDiff _diff;
    public BnRenderTreeDiff(in RenderTreeDiff diff) { _diff = ref diff; }

    public int ComponentId => _diff.ComponentId;
    public BnArrayBuilderSegment<RenderTreeEdit> Edits => new(_diff.Edits);
}

// ── RenderTreeEdit ───────────────────────────────────────────────────────────
// Type / SiblingIndex / ReferenceFrameIndex / RemovedAttributeName are public
// readonly fields of the StructLayout-Explicit RenderTreeEdit.

internal struct BnRenderTreeEdit
{
    private RenderTreeEdit _edit;
    public BnRenderTreeEdit(in RenderTreeEdit edit) { _edit = edit; }

    public int     Type                  => (int)_edit.Type;
    public int     ReferenceFrameIndex   => _edit.ReferenceFrameIndex;
    public int     SiblingIndex          => _edit.SiblingIndex;
    public string? RemovedAttributeName  => _edit.RemovedAttributeName;
}

// ── RenderTreeFrame ──────────────────────────────────────────────────────────
// The *Field fields are internal but the public property wrappers cover every
// piece of data the renderer reads. RenderTreeFrame is a non-readonly struct
// with StructLayout(Explicit); we keep ref semantics so we don't copy
// 40-plus-byte structs in the hot loop.

internal ref struct BnRenderTreeFrame
{
    private readonly ref RenderTreeFrame _frame;
    public BnRenderTreeFrame(ref RenderTreeFrame frame) { _frame = ref frame; }

    public RenderTreeFrameType FrameType                => _frame.FrameType;
    public string?             ElementName              => _frame.ElementName;
    public int                 ElementSubtreeLength     => _frame.ElementSubtreeLength;
    public int                 ComponentSubtreeLength   => _frame.ComponentSubtreeLength;
    public int                 ComponentId              => _frame.ComponentId;
    public int                 RegionSubtreeLength      => _frame.RegionSubtreeLength;
    public string?             AttributeName            => _frame.AttributeName;
    public object?             AttributeValue           => _frame.AttributeValue;
    public ulong               AttributeEventHandlerId  => _frame.AttributeEventHandlerId;
    public string?             TextContent              => _frame.TextContent;
    // Phase 7.0: the Razor compiler emits Markup frames (inter-element
    // whitespace at minimum) — the walk's Markup arm reads the content to
    // split whitespace (slot, no patch) from raw HTML (contract violation).
    public string?             MarkupContent            => _frame.MarkupContent;
}

internal static class RefAccessors
{
    // The one true UnsafeAccessor: Renderer.DispatchEventAsync takes an
    // internal EventFieldInfo type that we cannot reference directly.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "DispatchEventAsync")]
    public static extern Task DispatchEventAsync(
        BlazorRenderer renderer,
        ulong eventHandlerId,
        [UnsafeAccessorType("Microsoft.AspNetCore.Components.RenderTree.EventFieldInfo, Microsoft.AspNetCore.Components")]
        object? fieldInfo,
        EventArgs eventArgs);

    // Phase 4.2: StateHasChanged is protected on ComponentBase — the
    // test-only re-render seam calls it through this accessor (verified in
    // VerifyAccessors like DispatchEventAsync above).
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "StateHasChanged")]
    public static extern void StateHasChanged(ComponentBase component);
}
