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

    // ── Phase 11.4 Gate D (#164): parameter-binding fault PROVENANCE ─────────
    //
    // #164's fault — `<BnSwitch @bind-Value=…>` where the property is `Checked`
    // — is an InvalidOperationException raised by Blazor's parameter-property
    // writer. It is categorically an AUTHOR bug: it cannot be transient, it
    // fails identically on every run of every device, and continuing renders a
    // screen that is wrong in a way the user cannot report. NativeRenderer
    // aborts the mount for it (design §6.2) instead of logging and carrying on.
    //
    // WHY THE CLASSIFIER LIVES HERE, and not in NativeRenderer: deciding "this
    // exception came out of Blazor's parameter-supply path" is knowledge of
    // Microsoft.AspNetCore.Components' INTERNAL LAYOUT, which is this file's
    // entire job (see the header). NativeRenderer asks a question; the answer's
    // fragility is quarantined here with the rest of it.
    //
    // WHY CALL SITE AND NOT MESSAGE TEXT (design §11 R3 — the phase's sharpest
    // risk). `ex.Message.Contains("does not have a property matching")` would be
    // LOCALIZABLE (the BCL/ASP.NET resource strings are satellite-assembly
    // material), version-fragile, and — worst of all — its failure mode is
    // SILENT: it stops matching, mount goes back to returning 0, and nothing
    // looks broken. The frames below are METHOD IDENTITIES. They are not
    // localized, they are not reworded for clarity, and when Blazor renames one
    // the pin in ParameterBindingFaultTests reddens on the upgrade PR — which is
    // the moment a human is already reading the diff.
    //
    // FAIL DIRECTION. A stack with no recognizable frame classifies as NOT a
    // binding fault, so the renderer keeps today's log-and-continue posture.
    // That is deliberate: the failure mode of this predicate is "#164 is not
    // fixed for that fault", never "a recoverable fault crashed the app" — the
    // StrictErrors-in-production outcome #164 explicitly rules out.

    /// <summary>The Blazor methods that MAKE a fault a parameter-binding fault:
    /// the property-writing machinery behind <c>SetParametersAsync</c>.
    /// <list type="bullet">
    /// <item><c>ComponentProperties.SetProperties</c> — the writer itself; it is
    /// what throws for an unknown/unsettable incoming parameter name (internal
    /// type, and the only frame guaranteed to survive an optimized build: it is
    /// far too large to inline).</item>
    /// <item><c>ParameterView.SetParameterProperties</c> — its one-line PUBLIC
    /// caller, listed because a debug/JIT stack shows it and an inlining
    /// decision that erases it must not erase the classification.</item>
    /// </list>
    /// Deliberately NOT listed: <c>ComponentState.SupplyCombinedParameters</c>
    /// and <c>ComponentBase.SetParametersAsync</c>. They enclose the whole of
    /// parameter supply, including a component's own <c>SetParametersAsync</c>
    /// override — genuinely app code, and not the "always an author bug, never
    /// transient" class §6.2 scoped this to. Widening to them is a decision,
    /// not a tweak.</summary>
    internal static readonly string[] ParameterBindingFrames =
    [
        "Microsoft.AspNetCore.Components.Reflection.ComponentProperties.SetProperties",
        "Microsoft.AspNetCore.Components.ParameterView.SetParameterProperties",
    ];

    /// <summary>True when <paramref name="exception"/> (or anything in its inner
    /// chain) was raised inside Blazor's parameter-property writer — see
    /// <see cref="ParameterBindingFrames"/>.</summary>
    /// <remarks>Reads <c>Exception.StackTrace</c>, the recorded PROVENANCE of the
    /// throw, not <c>Message</c>. NativeAOT keeps member names in stack traces by
    /// default (the repo sets no <c>StackTraceSupport</c> anywhere, and
    /// <c>Exports.Init</c> already banks on ToString()'s stack surviving the
    /// C-ABI crossing); if a consumer ever trims them away, this returns false
    /// and the renderer falls back to log-and-continue. The chain walk is
    /// depth-capped so a cyclic/pathological InnerException cannot spin an error
    /// path.</remarks>
    internal static bool IsParameterBindingFault(Exception? exception)
    {
        for (int depth = 0; exception is not null && depth < 8; depth++)
        {
            if (exception is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    if (IsParameterBindingFault(inner))
                        return true;
                }
                return false;
            }

            string? stack = exception.StackTrace;
            if (stack is not null)
            {
                foreach (string frame in ParameterBindingFrames)
                {
                    if (stack.Contains(frame, StringComparison.Ordinal))
                        return true;
                }
            }

            exception = exception.InnerException;
        }

        return false;
    }
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
