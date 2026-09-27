using BlazorNative.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// EmptyFirstRenderProbe — Phase 16.2 (#346): a page whose FIRST render creates no
// node at all, the shape of `@if (_loaded) { … }` behind an async load.
// SCAFFOLDING, like HostEventProbe/BackHoldProbe: a Named mount-registry entry, not
// a routed page, so it has no menu row and no deep link.
//
// Why it exists: the Android shell applies .NET's pushed back state with the batch
// that shows the new page. "Shows a page" was first read as "creates a parentless
// node", which a page like this never does on its first render, so its back state
// would never be applied and back would exit from a sub-page. BackNoticeTest mounts
// it through the dll's real frames to pin the rule that replaced it.
//
// Shape:
//   first render:            nothing
//   ~50 ms after that render: BnText "loaded"
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class EmptyFirstRenderProbe : ComponentBase
{
    /// <summary>The text the page shows once its "load" completes.</summary>
    internal const string Loaded = "loaded";

    private bool _loaded;

    // The "load" starts AFTER the first render. A mount must render synchronously
    // (NativeRenderer.Mount rejects an OnInitializedAsync that yields before its first
    // render completes), so an async page defers its load exactly like this.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;
        await Task.Delay(50);
        await InvokeAsync(() =>
        {
            _loaded = true;
            StateHasChanged();
        });
    }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        if (!_loaded)
            return;

        b.OpenComponent<BnText>(0);
        b.AddComponentParameter(1, nameof(BnText.Text), Loaded);
        b.CloseComponent();
    }
}
