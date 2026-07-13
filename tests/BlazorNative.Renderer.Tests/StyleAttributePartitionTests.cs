using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// StyleAttributePartitionTests — Phase 6.1 Gate 1 review (finding I3 + N1/N2).
//
// The SetStyle allow-list is not one list; it is a ROUTING TABLE. After 6.1 a
// shell that receives SetStyle(name, value) must send the name to EXACTLY ONE
// of two destinations:
//
//   • the node's YOGA node   — layout (flexDirection, width, padding, top, …)
//   • the View / UIView      — paint  (backgroundColor, color, fontSize, …)
//
// Two hand-written parsers (Kotlin's YogaLayout, iOS's BnYogaLayout.mm) are
// written against that table, so it must be MECHANICAL, not a judgement call:
// hence YogaStyleAttributes / VisualStyleAttributes, their union pinned equal to
// StyleAttributes and their intersection pinned empty, right here.
//
// The one that bites if you get it wrong: `padding` is LAYOUT. Yoga places a
// container's children inside its padding box, so padding belongs to the Yoga
// node — a shell that ALSO calls view.setPadding(...) (Android does today)
// double-applies it. Gate 2/3 delete those view-level calls; this test is what
// says who owns the name.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StyleAttributePartitionTests
{
    /// <summary>The allow-list IS the two halves — nothing more, nothing less.</summary>
    [Fact]
    public void StyleAttributes_AreExactlyTheUnionOfTheYogaAndVisualHalves()
    {
        var union = new HashSet<string>(
            NativeRenderer.YogaStyleAttributes.Concat(NativeRenderer.VisualStyleAttributes),
            StringComparer.Ordinal);

        Assert.True(union.SetEquals(NativeRenderer.StyleAttributes),
            "StyleAttributes must be exactly YogaStyleAttributes ∪ VisualStyleAttributes — "
            + "a name in neither half is a name no shell knows where to route.");
    }

    /// <summary>...and the halves are DISJOINT: every style has exactly one
    /// destination. A name in both is a double-apply waiting to happen.</summary>
    [Fact]
    public void YogaAndVisualStyleAttributes_AreDisjoint()
    {
        var overlap = NativeRenderer.YogaStyleAttributes
            .Intersect(NativeRenderer.VisualStyleAttributes, StringComparer.Ordinal)
            .ToList();

        Assert.True(overlap.Count == 0,
            $"a style name must route to Yoga OR the view, never both: {string.Join(", ", overlap)}");
    }

    /// <summary>The box names are LAYOUT — Yoga's, not the view's. `padding` in
    /// particular: Yoga lays children out inside the padding box, so a shell that
    /// also calls setPadding()/layoutMargins double-applies it (a Gate 2/3
    /// instruction, recorded in the implementation plan).</summary>
    [Theory]
    [InlineData("padding")]
    [InlineData("margin")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("gap")]
    [InlineData("flexGrow")]
    [InlineData("position")]
    [InlineData("top")]
    public void BoxAndFlexNames_BelongToYoga(string name)
    {
        Assert.Contains(name, NativeRenderer.YogaStyleAttributes);
        Assert.DoesNotContain(name, NativeRenderer.VisualStyleAttributes);
    }

    /// <summary>...and paint is the view's, not Yoga's.</summary>
    [Theory]
    [InlineData("backgroundColor")]
    [InlineData("color")]
    [InlineData("fontSize")]
    [InlineData("fontWeight")]
    public void PaintNames_BelongToTheView(string name)
    {
        Assert.Contains(name, NativeRenderer.VisualStyleAttributes);
        Assert.DoesNotContain(name, NativeRenderer.YogaStyleAttributes);
    }

    /// <summary>Names the allow-list deliberately does NOT accept. Each one is a
    /// name two hand-written shell parsers would otherwise have to implement for
    /// a producer that does not exist:
    /// <list type="bullet">
    /// <item><c>alignContent</c>, <c>rowGap</c>, <c>columnGap</c> — no typed BnView
    /// param, nothing emits them. (BnLayoutDemo's wrap row RELIES on Yoga's
    /// alignContent default of <c>flex-start</c> — not setting it is precisely how
    /// it gets that.) Ledgered for a later phase, with the typed params.</item>
    /// <item><c>display</c>, <c>flex</c> — inherited from the pre-6.1 list; no
    /// typed param, and neither shell ever implemented them.</item>
    /// </list>
    /// They fall onto the PROP wire, where both shells already log "unknown prop"
    /// — logged and ignored, never silently guessed.</summary>
    [Theory]
    [InlineData("alignContent")]
    [InlineData("rowGap")]
    [InlineData("columnGap")]
    [InlineData("display")]
    [InlineData("flex")]
    public async Task LedgeredNames_AreNotStyles_AndFallOntoThePropWire(string name)
    {
        Assert.DoesNotContain(name, NativeRenderer.StyleAttributes);

        var (renderer, frames) = BuildRenderer();
        await renderer.MountAsync<OneAttribute>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(OneAttribute.Name)] = name,
                [nameof(OneAttribute.Value)] = "1",
            }));

        Assert.Empty(frames[0].Patches.OfType<SetStylePatch>());
        Assert.Equal(name, Assert.Single(frames[0].Patches.OfType<UpdatePropPatch>()).Name);
    }

    /// <summary>THE CASE RULE (N1). Both shells match style names
    /// case-SENSITIVELY, so .NET must too: an OrdinalIgnoreCase allow-list would
    /// classify "FlexGrow" as a style that the shells then silently DROP — .NET
    /// promising routing it cannot deliver. Ordinal means a mis-cased name lands
    /// on the prop wire instead, where the shells already log "unknown prop".
    /// Visible, not silent.</summary>
    [Theory]
    [InlineData("FlexGrow")]
    [InlineData("flexgrow")]
    [InlineData("WIDTH")]
    [InlineData("BackgroundColor")]
    public async Task MisCasedStyleName_IsNotAStyle_AndFallsOntoThePropWire(string name)
    {
        Assert.DoesNotContain(name, NativeRenderer.StyleAttributes);

        var (renderer, frames) = BuildRenderer();
        await renderer.MountAsync<OneAttribute>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(OneAttribute.Name)] = name,
                [nameof(OneAttribute.Value)] = "1",
            }));

        Assert.Empty(frames[0].Patches.OfType<SetStylePatch>());
        Assert.Equal(name, Assert.Single(frames[0].Patches.OfType<UpdatePropPatch>()).Name);
    }

    // ── Harness (mirrors StyleResetTests) ─────────────────────────────────────

    private static (NativeRenderer Renderer, List<RenderFrame> Frames) BuildRenderer()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true;
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        return (renderer, frames);
    }

    /// <summary>A div carrying ONE attribute whose name is a test parameter.</summary>
    private sealed class OneAttribute : ComponentBase
    {
        [Parameter] public string Name { get; set; } = "";
        [Parameter] public string Value { get; set; } = "";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, Name, Value);
            b.CloseElement();
        }
    }
}
