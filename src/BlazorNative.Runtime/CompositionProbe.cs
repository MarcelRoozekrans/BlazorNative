using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// CompositionProbe — Phase 3.3 Task 7 (design §6): the composition proof app.
//
// Shape (parent renders an interleaved mix):
//   root div
//     ├─ header div ("CompositionProbe")
//     ├─ ItemComponent "badge"        ← child component INTERLEAVED between
//     ├─ label div ("list:")            elements (slot/parenting, DoD #8)
//     ├─ list div: @foreach of keyed ItemComponents (item-1, item-2, …)
//     └─ buttons: Add (add-at-end) / Insert (insert-at-front) / Remove
//        (remove-first) — each mutates the list + re-renders, producing the
//        keyed component insert/remove diffs Tasks 1-5 exist for.
//
// DELIBERATELY hand-written BuildRenderTree with a plain C# foreach +
// OpenComponent<ItemComponent>: NO RenderFragment parameters and NO
// CascadingValue — their ChildContent renders as a Region frame, which the
// renderer does not walk (documented 3.4 carryover; see NativeWidgetTree's
// carryover block). A plain foreach of OpenComponent emits Component frames
// inline with zero Region frames. builder.SetKey(item) makes Blazor produce
// genuine keyed insert/remove diffs instead of positional rewrites.
//
// Registered as "CompositionProbe" in HostSession's mount registry
// (statically-rooted generic Mount<T> — same trim-safe idiom as
// HelloComponent). HelloComponent stays the untouched demo.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>List/interleave item: own state (tap counter) + own @onclick —
/// the child-component event round-trip probe.</summary>
internal sealed class ItemComponent : ComponentBase
{
    [Parameter] public string Label { get; set; } = "";

    private int _taps;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddAttribute(1, "onclick",
            EventCallback.Factory.Create<MouseEventArgs>(this, () => _taps++));
        b.AddContent(2, $"{Label} (taps: {_taps})");
        b.CloseElement();
    }
}

internal sealed class CompositionProbe : ComponentBase
{
    private readonly List<string> _items = ["item-1", "item-2"];
    private int _nextItem = 3;

    private void AddAtEnd() => _items.Add($"item-{_nextItem++}");
    private void InsertAtFront() => _items.Insert(0, $"item-{_nextItem++}");
    private void RemoveFirst()
    {
        if (_items.Count > 0)
            _items.RemoveAt(0);
    }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");                                 // root container

        b.OpenElement(10, "div");                                // header
        b.AddContent(11, "CompositionProbe");
        b.CloseElement();

        b.OpenComponent<ItemComponent>(20);                      // interleaved child
        b.AddComponentParameter(21, nameof(ItemComponent.Label), "badge");
        b.CloseComponent();

        b.OpenElement(30, "div");                                // label AFTER the component
        b.AddContent(31, "list:");
        b.CloseElement();

        b.OpenElement(40, "div");                                // the mutating list
        foreach (var item in _items)
        {
            b.OpenComponent<ItemComponent>(41);
            b.SetKey(item);
            b.AddComponentParameter(42, nameof(ItemComponent.Label), item);
            b.CloseComponent();
        }
        b.CloseElement();

        b.OpenElement(50, "button");
        b.AddAttribute(51, "onclick",
            EventCallback.Factory.Create<MouseEventArgs>(this, AddAtEnd));
        b.AddContent(52, "Add");
        b.CloseElement();

        b.OpenElement(60, "button");
        b.AddAttribute(61, "onclick",
            EventCallback.Factory.Create<MouseEventArgs>(this, InsertAtFront));
        b.AddContent(62, "Insert");
        b.CloseElement();

        b.OpenElement(70, "button");
        b.AddAttribute(71, "onclick",
            EventCallback.Factory.Create<MouseEventArgs>(this, RemoveFirst));
        b.AddContent(72, "Remove");
        b.CloseElement();

        b.CloseElement();
    }
}
