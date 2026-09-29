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
// hence YogaStyleAttributes / VisualStyleAttributes, and their intersection
// pinned empty, right here. Their union IS StyleAttributes by definition in
// NativeRenderer, so the fact that pinned it was retired in 15.7; see below.
//
// The one that bites if you get it wrong: `padding` is LAYOUT. Yoga places a
// container's children inside its padding box, so padding belongs to the Yoga
// node — a shell that ALSO calls view.setPadding(...) (Android does today)
// double-applies it. Gate 2/3 delete those view-level calls; this test is what
// says who owns the name.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StyleAttributePartitionTests
{
    // ── RETIRED (Phase 15.7): StyleAttributes_AreExactlyTheUnionOfTheYogaAndVisualHalves ──
    //
    // It asserted StyleAttributes == YogaStyleAttributes ∪ VisualStyleAttributes, and
    // NativeRenderer DEFINES StyleAttributes as exactly that union, so it compared a value with
    // its own definition and could not go red. The house rule, set when NavigationTests retired
    // EveryRoute_ResolvesToAComponentTheMountRegistryKnows in Phase 7.6, is that a green
    // tautology is retired, not kept.
    //
    // It was not re-pointed, because no independent second copy exists. Provenance of each
    // candidate side:
    //   - StyleAttributes: the union of the two halves, built in NativeRenderer.
    //   - YogaStyleAttributes and VisualStyleAttributes: new HashSets over
    //     BnWireVocabulary.YogaStyles and .VisualStyles, which WireGen emits from
    //     src/wire-vocabulary.json.
    //   - The manifest's style lists: the source every set above is generated from.
    // Every pairing is therefore a set against its own definition, or a generated table against
    // its own source. The second is already pinned twice, in BlazorNative.Runtime.Tests:
    // WireVocabularyCodegenTests byte-compares BnWireVocabulary.g.cs with what the manifest
    // produces, and its TheRenderersStyleSets_AreTheManifests holds both halves equal to the
    // manifest's lists. A third comparison here would restate those pins, not add a pair.
    //
    // What the retired fact's message warned about, a style name that belongs to neither half,
    // cannot be written: a name enters StyleAttributes only through one of the halves.

    /// <summary>The measured sizes of the two halves, at zero headroom. The disjointness fact
    /// below reads "the overlap is empty", and an empty half makes the overlap empty for free.
    /// </summary>
    private const int MeasuredYogaStyles = 26;
    private const int MeasuredVisualStyles = 3;

    /// <summary>...and the halves are DISJOINT: every style has exactly one
    /// destination. A name in both is a double-apply waiting to happen.</summary>
    [Fact]
    public void YogaAndVisualStyleAttributes_AreDisjoint()
    {
        // Rule 2, on this fact alone (15.7): the theories below anchor named rows in each half,
        // but this fact must not lean on a sibling to notice that a half came back empty.
        Assert.True(NativeRenderer.YogaStyleAttributes.Count >= MeasuredYogaStyles,
            $"YogaStyleAttributes has {NativeRenderer.YogaStyleAttributes.Count} names, and "
            + $"{MeasuredYogaStyles} were measured. An empty half makes the overlap below empty and "
            + "this fact pass while checking nothing; if a name was deliberately removed, lower the "
            + "floor in the same commit.");
        Assert.True(NativeRenderer.VisualStyleAttributes.Count >= MeasuredVisualStyles,
            $"VisualStyleAttributes has {NativeRenderer.VisualStyleAttributes.Count} names, and "
            + $"{MeasuredVisualStyles} were measured. An empty half makes the overlap below empty and "
            + "this fact pass while checking nothing; if a name was deliberately removed, lower the "
            + "floor in the same commit.");

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
    // Removed from the VISUAL half for the same reason the five above were never in
    // the layout half — the table accepted them and no shell applied them, so every
    // use was silently dropped. `fontWeight` is the interesting one: it is not a
    // missing arm but a design question (only Inter Regular is bundled, and synthetic
    // bold changes text METRICS, which would put the font-parity contract at risk to
    // add a paint property). `background`/`style` are pre-6.1 HTML leftovers nothing
    // ever emitted.
    [InlineData("fontWeight")]
    [InlineData("background")]
    [InlineData("style")]
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

    /// <summary>PHASE 6.3 NON-NEGOTIABLE #1: <c>src</c> is a PROP, not a style.
    /// <para>It is the one name a 6.3 implementer is most likely to add to the
    /// partition by reflex — it arrives on an element, it looks declarative, and
    /// <see cref="NativeRenderer.StyleAttributes"/> is right there. But the
    /// partition is a ROUTING TABLE with exactly two destinations (the Yoga node,
    /// or the View/UIView), and a URL belongs to NEITHER: a shell would have to
    /// grow a third arm inside a two-arm contract, in two hand-written parsers, for
    /// a name that is not layout and is not paint. It rides the existing UpdateProp
    /// wire instead, which is where <c>value</c> / <c>placeholder</c> / <c>enabled</c>
    /// already ride — no ABI change, no new patch kind (design §"No ABI change").
    /// </para>
    /// If this ever goes red, an image source has become a style and both shells'
    /// SetStyle dispatch has quietly acquired a case that routes nowhere.</summary>
    [Fact]
    public async Task Src_IsAProp_NotAStyle()
    {
        Assert.DoesNotContain("src", NativeRenderer.StyleAttributes);
        Assert.DoesNotContain("src", NativeRenderer.YogaStyleAttributes);
        Assert.DoesNotContain("src", NativeRenderer.VisualStyleAttributes);

        var (renderer, frames) = BuildRenderer();
        await renderer.MountAsync<OneAttribute>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(OneAttribute.Name)] = "src",
                [nameof(OneAttribute.Value)] = "http://127.0.0.1:8099/a.png",
            }));

        Assert.Empty(frames[0].Patches.OfType<SetStylePatch>());
        UpdatePropPatch src = Assert.Single(frames[0].Patches.OfType<UpdatePropPatch>());
        Assert.Equal("src", src.Name);
        Assert.Equal("http://127.0.0.1:8099/a.png", src.Value);
    }

    /// <summary>PHASE 7.5 (design decisions 1 and 3): <c>placeholderColor</c> and
    /// <c>contentMode</c> are PROPS, not styles — <c>src</c>'s rule, for <c>src</c>'s
    /// reason, applied to the two names most likely to be added to the partition by
    /// reflex ("a color, so paint?"; "a mode, so… paint?"). Neither is a name ANY
    /// node can carry (the partition's admission bar): both are image-only
    /// vocabulary, and <c>contentMode</c> in particular must never be layout — the
    /// mode is PAINT-ONLY, normatively (the measure func never consults it), so a
    /// shell routing it to a Yoga node would be wrong twice. They ride the
    /// UpdateProp wire where <c>value</c>/<c>placeholder</c>/<c>enabled</c>/<c>src</c>
    /// ride. If this goes red, the design's mutation table's third row happened:
    /// a 7.5 prop became a style and both shells' two-arm SetStyle dispatch
    /// quietly acquired a case that routes nowhere.</summary>
    [Theory]
    [InlineData("placeholderColor", "#FFCA28")]
    [InlineData("contentMode", "cover")]
    public async Task PlaceholderColorAndContentMode_AreProps_NotStyles(string name, string value)
    {
        Assert.DoesNotContain(name, NativeRenderer.StyleAttributes);
        Assert.DoesNotContain(name, NativeRenderer.YogaStyleAttributes);
        Assert.DoesNotContain(name, NativeRenderer.VisualStyleAttributes);

        var (renderer, frames) = BuildRenderer();
        await renderer.MountAsync<OneAttribute>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(OneAttribute.Name)] = name,
                [nameof(OneAttribute.Value)] = value,
            }));

        Assert.Empty(frames[0].Patches.OfType<SetStylePatch>());
        UpdatePropPatch prop = Assert.Single(frames[0].Patches.OfType<UpdatePropPatch>());
        Assert.Equal(name, prop.Name);
        Assert.Equal(value, prop.Value);
    }

    /// <summary>THE NAME-COLLISION PIN (Phase 7.5 design decision 1):
    /// <c>placeholder</c> has been the INPUT prop (the EditText hint /
    /// <c>UITextField.placeholder</c>) since M2 — Android's UpdateProp arm routes it
    /// to EditText and warn-drops everything else — so BnImage's wire name is
    /// <c>placeholderColor</c>, a DISTINCT prop. This pins the two names as two
    /// separate props on the prop wire, byte-for-byte as authored: a "helpful"
    /// alias or normalization anywhere in the renderer (either direction) would
    /// fork one prop's meaning by NodeType inside both hand-written shells.</summary>
    [Fact]
    public async Task Placeholder_And_PlaceholderColor_StayDistinctPropsOnThePropWire()
    {
        // Neither name is a style…
        Assert.DoesNotContain("placeholder", NativeRenderer.StyleAttributes);
        Assert.DoesNotContain("placeholderColor", NativeRenderer.StyleAttributes);

        var (renderer, frames) = BuildRenderer();
        await renderer.MountAsync<TwoAttributes>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TwoAttributes.NameA)] = "placeholder",
                [nameof(TwoAttributes.ValueA)] = "type here…",
                [nameof(TwoAttributes.NameB)] = "placeholderColor",
                [nameof(TwoAttributes.ValueB)] = "#FFCA28",
            }));

        // …and each reaches the prop wire under its OWN name, values intact.
        Assert.Empty(frames[0].Patches.OfType<SetStylePatch>());
        List<UpdatePropPatch> props = frames[0].Patches.OfType<UpdatePropPatch>().ToList();
        Assert.Equal(2, props.Count);
        Assert.Equal("type here…", Assert.Single(props, p => p.Name == "placeholder").Value);
        Assert.Equal("#FFCA28", Assert.Single(props, p => p.Name == "placeholderColor").Value);
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

    /// <summary>A div carrying TWO attributes — the distinct-names pin's
    /// harness (both on ONE node, so a renderer-side alias/normalization of
    /// either name would collapse them into one patch).</summary>
    private sealed class TwoAttributes : ComponentBase
    {
        [Parameter] public string NameA { get; set; } = "";
        [Parameter] public string ValueA { get; set; } = "";
        [Parameter] public string NameB { get; set; } = "";
        [Parameter] public string ValueB { get; set; } = "";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, NameA, ValueA);
            b.AddAttribute(2, NameB, ValueB);
            b.CloseElement();
        }
    }
}
