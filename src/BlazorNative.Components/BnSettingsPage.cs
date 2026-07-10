using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnSettingsPage — Phase 3.5 (design §2): the demo's SECOND page, closing
// M3 DoD #7's on-device proof (tap "Settings →" → whole screen swaps → tap
// "← Back"). Registered as "BnSettingsPage" in HostSession's mount registry;
// route "/settings" in NativeNavigationManager's table.
//
// Shape (the pinned mount contract — NavigationTests + Gate 2's
// NavigationTest.kt):
//   BnView settings div                            backgroundColor+padding 16
//    ├─ BnText title "Settings"                    span, fontSize 24
//    └─ BnButton "← Back"                          button + click → NavigateToAsync("/")
//
// The first real DI consumer in Components (together with BnDemo's Settings
// button): [Inject] INavigationManager — the Core contract, resolved from
// the session's service provider through Blazor's property injection.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The demo's settings page — see the file header for the pinned
/// shape. A fresh ROOT component (no cascaded theme crosses a root swap):
/// it carries BnDemo's default palette itself, keeping the two pages
/// visually consistent.</summary>
public sealed class BnSettingsPage : ComponentBase
{
    // BnDemo's DefaultBackground — the CascadingValue<BnTheme>-consistent look.
    private const string DefaultBackground = "#FFEEAA";

    [Inject] public INavigationManager Navigation { get; set; } = default!;

    // Sync-completing (inline dispatcher): the swap has fully happened —
    // removes + creates delivered — when this Task is observed.
    private Task GoBack() => Navigation.NavigateToAsync("/").AsTask();

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent<BnView>(0);
        b.AddComponentParameter(1, nameof(BnView.BackgroundColor), DefaultBackground);
        b.AddComponentParameter(2, nameof(BnView.Padding), "16");
        b.AddComponentParameter(3, nameof(BnView.ChildContent), (RenderFragment)BuildChildren);
        b.CloseComponent();
    }

    private void BuildChildren(RenderTreeBuilder b)
    {
        b.OpenComponent<BnText>(0);                              // title
        b.AddComponentParameter(1, nameof(BnText.Text), "Settings");
        b.AddComponentParameter(2, nameof(BnText.FontSize), "24");
        b.CloseComponent();

        b.OpenComponent<BnButton>(10);                           // back → "/"
        b.AddComponentParameter(11, nameof(BnButton.Label), "← Back");
        b.AddComponentParameter(12, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, GoBack));
        b.CloseComponent();
    }
}
