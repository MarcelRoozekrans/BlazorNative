using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnDemo — Phase 3.4 Task 4 (design §4): the Bn*-built demo form closing
// M3 DoD #5 (two-way bound input + live echo) and DoD #6 (cascading theme
// toggle → themed children re-render). Registered as "BnDemo" in
// HostSession's mount registry; becomes MainActivity's default at Gate 4.
//
// Shape (the pinned mount contract — BnDemoTests + Gate 3's BnDemoTest.kt):
//   CascadingValue<BnTheme>                       (region — no node)
//   └─ BnThemedPanel #1 → BnView form div          backgroundColor+padding 16
//       ├─ BnText  title "BnDemo"                  span, fontSize 24
//       ├─ BnInput bound (Value/ValueChanged)      input: value+placeholder+change
//       ├─ BnThemedPanel #2 → BnView echo panel    div, backgroundColor+padding 8
//       │   └─ BnText echo (= the bound text)      span
//       ├─ BnButton "Clear"                        button + click
//       └─ BnButton "Theme"                        button + click
//
// The bind loop (DoD #5): change dispatch → BnInput.ValueChanged → _text
// mutates → re-render → echo ReplaceText + input value UpdateProp.
// The theme toggle (DoD #6): _theme swaps to a NEW BnTheme record →
// CascadingValue notifies → BOTH BnThemedPanels re-render → SetStyle
// backgroundColor on both divs.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Demo-internal cascading consumer: reads the cascaded
/// <see cref="BnTheme"/> and renders a <see cref="BnView"/> with the theme's
/// current background — the "themed child" whose re-render on theme change
/// is DoD #6's proof surface.</summary>
internal sealed class BnThemedPanel : ComponentBase
{
    [CascadingParameter] public BnTheme? Theme { get; set; }

    [Parameter] public string? Padding { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent<BnView>(0);
        b.AddComponentParameter(1, nameof(BnView.BackgroundColor), Theme?.Background);
        b.AddComponentParameter(2, nameof(BnView.Padding), Padding);
        b.AddComponentParameter(3, nameof(BnView.ChildContent), ChildContent);
        b.CloseComponent();
    }
}

/// <summary>The Bn* demo form — see the file header for the pinned shape.</summary>
public sealed class BnDemo : ComponentBase
{
    private const string DefaultBackground = "#FFEEAA";
    private const string AltBackground = "#334455";

    private string _text = "";
    private BnTheme _theme = new(DefaultBackground, AltBackground);

    private void Clear() => _text = "";

    // Swap: a NEW record instance each toggle (see BnTheme doc).
    private void ToggleTheme()
        => _theme = new BnTheme(_theme.AltBackground, _theme.Background);

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent<CascadingValue<BnTheme>>(0);
        b.AddComponentParameter(1, "Value", _theme);
        b.AddComponentParameter(2, "ChildContent", (RenderFragment)BuildForm);
        b.CloseComponent();
    }

    private void BuildForm(RenderTreeBuilder b)
    {
        b.OpenComponent<BnThemedPanel>(0);
        b.AddComponentParameter(1, nameof(BnThemedPanel.Padding), "16");
        b.AddComponentParameter(2, nameof(BnThemedPanel.ChildContent), (RenderFragment)BuildFormChildren);
        b.CloseComponent();
    }

    private void BuildFormChildren(RenderTreeBuilder b)
    {
        b.OpenComponent<BnText>(0);                              // title
        b.AddComponentParameter(1, nameof(BnText.Text), "BnDemo");
        b.AddComponentParameter(2, nameof(BnText.FontSize), "24");
        b.CloseComponent();

        b.OpenComponent<BnInput>(10);                            // the bound input
        b.AddComponentParameter(11, nameof(BnInput.Value), _text);
        b.AddComponentParameter(12, nameof(BnInput.ValueChanged),
            EventCallback.Factory.Create<string>(this, v => _text = v));
        b.AddComponentParameter(13, nameof(BnInput.Placeholder), "Type here...");
        b.CloseComponent();

        b.OpenComponent<BnThemedPanel>(20);                      // echo panel (themed #2)
        b.AddComponentParameter(21, nameof(BnThemedPanel.Padding), "8");
        b.AddComponentParameter(22, nameof(BnThemedPanel.ChildContent), (RenderFragment)(eb =>
        {
            eb.OpenComponent<BnText>(0);                         // the live echo
            eb.AddComponentParameter(1, nameof(BnText.Text), _text);
            eb.CloseComponent();
        }));
        b.CloseComponent();

        b.OpenComponent<BnButton>(30);
        b.AddComponentParameter(31, nameof(BnButton.Label), "Clear");
        b.AddComponentParameter(32, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, Clear));
        b.CloseComponent();

        b.OpenComponent<BnButton>(40);
        b.AddComponentParameter(41, nameof(BnButton.Label), "Theme");
        b.AddComponentParameter(42, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, ToggleTheme));
        b.CloseComponent();
    }
}
