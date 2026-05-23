// ─────────────────────────────────────────────────────────────────────────────
// BlazorInterop.cs — SINGLE SEAM between BlazorNative.Renderer and the internal
// layout of Microsoft.AspNetCore.Components.
//
// Bound to Microsoft.AspNetCore.Components 10.0.* — bump BlazorCompatVersion
// and re-verify accessor field names against the linked Blazor source revision
// before any major-version package upgrade.
//
// See docs/plans/2026-05-23-renderer-internal-api-design.md for rationale.
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorNative.Renderer;

internal static class BlazorInterop
{
    public static readonly Version BlazorCompatVersion = new(10, 0);

    static BlazorInterop()
    {
        VerifyVersion();
        VerifyAccessors();
    }

    private static void VerifyVersion()
    {
        var actual = typeof(Renderer).Assembly.GetName().Version;
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
        // Each accessor in this file should be touched once against a
        // default-initialised struct. Populated as accessors are added in Tasks 8-11.
        // Each touch wraps in try/catch and aggregates MissingFieldException /
        // MissingMethodException into a single BlazorVersionMismatchException.
        var failures = new List<string>();
        try
        {
            var range = default(ArrayRange<int>);
            _ = RefAccessors.ArrayRangeArray(ref range);  // may legitimately be null on default — ok
            _ = RefAccessors.ArrayRangeCount(ref range);
        }
        catch (Exception ex) when (ex is MissingFieldException or MissingMethodException)
        {
            failures.Add($"BnArrayRange<T>: {ex.Message}");
        }

        try
        {
            var batch = default(RenderBatch);
            _ = RefAccessors.UpdatedComponents(ref batch);
            _ = RefAccessors.DisposedComponentIDs(ref batch);
        }
        catch (Exception ex) when (ex is MissingFieldException or MissingMethodException)
        {
            failures.Add($"BnRenderBatch: {ex.Message}");
        }

        try
        {
            var diff = default(RenderTreeDiff);
            _ = RefAccessors.ComponentId(ref diff);
            _ = RefAccessors.Edits(ref diff);
            _ = RefAccessors.ReferenceFrames(ref diff);
        }
        catch (Exception ex) when (ex is MissingFieldException or MissingMethodException)
        {
            failures.Add($"BnRenderTreeDiff: {ex.Message}");
        }

        try
        {
            var edit = default(RenderTreeEdit);
            _ = RefAccessors.EditType(ref edit);
            _ = RefAccessors.ReferenceFrameIndex(ref edit);
            _ = RefAccessors.SiblingIndex(ref edit);
            _ = RefAccessors.RemovedAttributeName(ref edit);
        }
        catch (Exception ex) when (ex is MissingFieldException or MissingMethodException)
        {
            failures.Add($"BnRenderTreeEdit: {ex.Message}");
        }

        try
        {
            var frame = default(RenderTreeFrame);
            _ = RefAccessors.FrameType(ref frame);
            _ = RefAccessors.ElementName(ref frame);
            _ = RefAccessors.ElementSubtreeLength(ref frame);
            _ = RefAccessors.AttributeName(ref frame);
            _ = RefAccessors.AttributeValue(ref frame);
            _ = RefAccessors.AttributeEventHandlerId(ref frame);
            _ = RefAccessors.TextContent(ref frame);
        }
        catch (Exception ex) when (ex is MissingFieldException or MissingMethodException)
        {
            failures.Add($"BnRenderTreeFrame: {ex.Message}");
        }

        if (failures.Count > 0)
            throw new BlazorVersionMismatchException(
                "Blazor internal-layout drift detected:\n  - " + string.Join("\n  - ", failures));
    }
}

public sealed class BlazorVersionMismatchException : Exception
{
    public BlazorVersionMismatchException(string message) : base(message) { }
}

// ── ArrayRange<T> ────────────────────────────────────────────────────────────

internal ref struct BnArrayRange<T>
{
    private readonly ref ArrayRange<T> _range;

    public BnArrayRange(ref ArrayRange<T> range)
    {
        _range = ref range;
    }

    public int Count => RefAccessors.ArrayRangeCount(ref _range);

    public ref T this[int i] => ref RefAccessors.ArrayRangeArray(ref _range)[i];

    public Enumerator GetEnumerator() => new(ref _range);

    public ref struct Enumerator
    {
        private readonly ref ArrayRange<T> _range;
        private int _index;
        public Enumerator(ref ArrayRange<T> range) { _range = ref range; _index = -1; }
        public bool MoveNext() => ++_index < RefAccessors.ArrayRangeCount(ref _range);
        public ref T Current => ref RefAccessors.ArrayRangeArray(ref _range)[_index];
    }
}

// ── RenderBatch ──────────────────────────────────────────────────────────────

internal ref struct BnRenderBatch
{
    private readonly ref RenderBatch _batch;
    public BnRenderBatch(ref RenderBatch batch) { _batch = ref batch; }

    public BnArrayRange<RenderTreeDiff> UpdatedComponents
        => new(ref RefAccessors.UpdatedComponents(ref _batch));

    public BnArrayRange<int> DisposedComponentIDs
        => new(ref RefAccessors.DisposedComponentIDs(ref _batch));
}

// ── RenderTreeDiff ───────────────────────────────────────────────────────────

internal ref struct BnRenderTreeDiff
{
    private readonly ref RenderTreeDiff _diff;
    public BnRenderTreeDiff(ref RenderTreeDiff diff) { _diff = ref diff; }

    public int ComponentId => RefAccessors.ComponentId(ref _diff);
    public BnArrayRange<RenderTreeEdit>  Edits           => new(ref RefAccessors.Edits(ref _diff));
    public BnArrayRange<RenderTreeFrame> ReferenceFrames => new(ref RefAccessors.ReferenceFrames(ref _diff));
}

// ── RenderTreeEdit ───────────────────────────────────────────────────────────

internal ref struct BnRenderTreeEdit
{
    private readonly ref RenderTreeEdit _edit;
    public BnRenderTreeEdit(ref RenderTreeEdit edit) { _edit = ref edit; }

    public int     Type                  => (int)RefAccessors.EditType(ref _edit);
    public int     ReferenceFrameIndex   => RefAccessors.ReferenceFrameIndex(ref _edit);
    public int     SiblingIndex          => RefAccessors.SiblingIndex(ref _edit);
    public string? RemovedAttributeName  => RefAccessors.RemovedAttributeName(ref _edit);
}

// ── RenderTreeFrame ──────────────────────────────────────────────────────────

internal ref struct BnRenderTreeFrame
{
    private readonly ref RenderTreeFrame _frame;
    public BnRenderTreeFrame(ref RenderTreeFrame frame) { _frame = ref frame; }

    public RenderTreeFrameType FrameType            => RefAccessors.FrameType(ref _frame);
    public string?             ElementName          => RefAccessors.ElementName(ref _frame);
    public int                 ElementSubtreeLength => RefAccessors.ElementSubtreeLength(ref _frame);
    public string?             AttributeName        => RefAccessors.AttributeName(ref _frame);
    public object?             AttributeValue       => RefAccessors.AttributeValue(ref _frame);
    public ulong               AttributeEventHandlerId => RefAccessors.AttributeEventHandlerId(ref _frame);
    public string?             TextContent          => RefAccessors.TextContent(ref _frame);
}

file static class RefAccessors
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Array")]
    public static extern ref T[] ArrayRangeArray<T>(ref ArrayRange<T> range);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Count")]
    public static extern ref int ArrayRangeCount<T>(ref ArrayRange<T> range);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "UpdatedComponentsBuffer")]
    public static extern ref ArrayRange<RenderTreeDiff> UpdatedComponents(ref RenderBatch batch);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "DisposedComponentIndices")]
    public static extern ref ArrayRange<int> DisposedComponentIDs(ref RenderBatch batch);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ComponentId")]
    public static extern ref int ComponentId(ref RenderTreeDiff diff);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Edits")]
    public static extern ref ArrayRange<RenderTreeEdit> Edits(ref RenderTreeDiff diff);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ReferenceFrames")]
    public static extern ref ArrayRange<RenderTreeFrame> ReferenceFrames(ref RenderTreeDiff diff);

    // RenderTreeEdit — underlying field names may differ; verify on first probe.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "TypeField")]
    public static extern ref int EditType(ref RenderTreeEdit edit);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ReferenceFrameIndexField")]
    public static extern ref int ReferenceFrameIndex(ref RenderTreeEdit edit);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "SiblingIndexField")]
    public static extern ref int SiblingIndex(ref RenderTreeEdit edit);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "RemovedAttributeNameField")]
    public static extern ref string? RemovedAttributeName(ref RenderTreeEdit edit);

    // RenderTreeFrame
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "FrameTypeField")]
    public static extern ref RenderTreeFrameType FrameType(ref RenderTreeFrame frame);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ElementNameField")]
    public static extern ref string? ElementName(ref RenderTreeFrame frame);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ElementSubtreeLengthField")]
    public static extern ref int ElementSubtreeLength(ref RenderTreeFrame frame);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "AttributeNameField")]
    public static extern ref string? AttributeName(ref RenderTreeFrame frame);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "AttributeValueField")]
    public static extern ref object? AttributeValue(ref RenderTreeFrame frame);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "AttributeEventHandlerIdField")]
    public static extern ref ulong AttributeEventHandlerId(ref RenderTreeFrame frame);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "TextContentField")]
    public static extern ref string? TextContent(ref RenderTreeFrame frame);
}
