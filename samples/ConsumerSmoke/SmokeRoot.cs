using BlazorNative.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace ConsumerSmoke;

/// <summary>
/// The hand-written consumer component (M4 DoD #7): a BnView containing a
/// BnText and a BnButton with a live click handler — built exactly the way an
/// app author would compose the packaged Bn* components (BnDemo's idiom;
/// Razor syntax awaits .razor compilation, M6).
/// </summary>
/// <summary>A second consumer-owned component (Phase 8.1): the
/// <c>BlazorNativePage.Named&lt;T&gt;</c> row in Program.cs's registration
/// block needs a concrete T that is NOT SmokeRoot — proving the params
/// surface takes heterogeneous rows from consumer code alone.</summary>
public sealed class SmokeProbePage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent<BnText>(0);
        b.AddComponentParameter(1, nameof(BnText.Text), "probe");
        b.CloseComponent();
    }
}

public sealed class SmokeRoot : ComponentBase
{
    /// <summary>Bumped by the BnButton click handler — proves the
    /// EventCallback wires up (the mount frame must carry its AttachEvent).</summary>
    public static int Clicks { get; private set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent<BnView>(0);
        b.AddComponentParameter(1, nameof(BnView.Padding), 16f);
        b.AddComponentParameter(2, nameof(BnView.ChildContent), (RenderFragment)(cb =>
        {
            cb.OpenComponent<BnText>(0);
            cb.AddComponentParameter(1, nameof(BnText.Text), "Hello from packages");
            cb.CloseComponent();

            cb.OpenComponent<BnButton>(10);
            cb.AddComponentParameter(11, nameof(BnButton.Label), "Tap");
            cb.AddComponentParameter(12, nameof(BnButton.OnClick),
                EventCallback.Factory.Create<MouseEventArgs>(this, () => Clicks++));
            cb.CloseComponent();
        }));
        b.CloseComponent();
    }
}
