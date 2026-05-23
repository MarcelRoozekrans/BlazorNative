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

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using BlazorRenderer = Microsoft.AspNetCore.Components.RenderTree.Renderer;

namespace BlazorNative.Renderer;

internal static class BlazorInterop
{
    public static readonly Version BlazorCompatVersion = new(10, 0);

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

        // DispatchEventAsync is the only UnsafeAccessor we still depend on.
        try
        {
            // Probe the accessor metadata exists at all. We can't *invoke* it
            // here without a real Renderer instance, but referring to the
            // delegate via reflection forces it to resolve.
            _ = typeof(RefAccessors).GetMethod(
                nameof(RefAccessors.DispatchEventAsync),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch (Exception ex) when (ex is MissingFieldException or MissingMethodException)
        {
            failures.Add($"DispatchEventAsync: {ex.Message}");
        }

        if (failures.Count > 0)
            throw new BlazorVersionMismatchException(
                "Blazor internal-layout drift detected:\n  - " + string.Join("\n  - ", failures));
    }

    public static Task DispatchEventViaAccessor(
        BlazorRenderer renderer,
        ulong eventHandlerId,
        EventArgs eventArgs)
        => RefAccessors.DispatchEventAsync(renderer, eventHandlerId, fieldInfo: null, eventArgs);
}

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

internal struct BnRenderTreeDiff
{
    private RenderTreeDiff _diff;
    public BnRenderTreeDiff(in RenderTreeDiff diff) { _diff = diff; }

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

    public RenderTreeFrameType FrameType            => _frame.FrameType;
    public string?             ElementName          => _frame.ElementName;
    public int                 ElementSubtreeLength => _frame.ElementSubtreeLength;
    public string?             AttributeName        => _frame.AttributeName;
    public object?             AttributeValue       => _frame.AttributeValue;
    public ulong               AttributeEventHandlerId => _frame.AttributeEventHandlerId;
    public string?             TextContent          => _frame.TextContent;
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
}
