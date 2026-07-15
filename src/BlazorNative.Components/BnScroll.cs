using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnScroll — Phase 6.2 Task 1.1 (design §"The component").
//
// A VIEWPORT whose content may exceed it. It emits the `scroll` element, which
// MapElementToNodeType has mapped to NodeType 6 ("scroll") since Phase 2.5 —
// so this component is a pure Components-side addition: NO new export, NO new
// NodeType, NO new patch kind, NO ABI change (9 exports + the 72-byte bridge).
// The stub the shells have carried since 2.5 becomes real in Gates 2/3.
//
// ── THE SYNTHETIC CONTENT NODE (what the shells do with this node) ───────────
// A scroll node's wire children are NOT parented into the scroll node itself.
// The shells interpose a SYNTHETIC content node — created shell-side, never on
// the wire:
//
//     wire tree:            view/Yoga trees:
//     scroll                scroll                 the VIEWPORT (definite height)
//      ├─ child 0            └─ content            height:auto, width:100%,
//      ├─ child 1                 ├─ child 0       flexDirection:column,
//      └─ …                       ├─ child 1       flexShrink NEVER set
//                                 └─ …             ← its COMPUTED HEIGHT *is*
//                                                    the content size
//
// Yoga owns every frame, including the off-screen ones, and `contentSize` is
// read straight out of the content node — never derived by a shell from a union
// of child frames (two shells deriving it independently is exactly where Android
// and iOS drift apart).
//
// ── BnScroll IS A FLEX *ITEM*, NOT A FLEX *CONTAINER* ────────────────────────
// This is the whole shape of the surface below, so it is worth being blunt: a
// BnScroll is a flex ITEM that scrolls its content. How that CONTENT lays out is
// the CONTENT NODE's job — and the content node is the shells' node, not the
// author's. So the container-layout family is ABSENT BY CONSTRUCTION, the way
// BnRow's Direction is:
//
//     Direction · Justify · Align · Wrap · Gap · Padding   ← NOT parameters
//
// Every one of them would land on the SCROLL node, whose only Yoga child is the
// content node — and each fails silently and bafflingly:
//
//   • Direction  — `row` lays the content node out across the cross axis and
//                  stretches it to the viewport height: the page just stops
//                  scrolling. (Scrolling is VERTICAL-ONLY — 6.2 decision 2:
//                  Android's ScrollView is a vertical-only widget class, and
//                  horizontal is ledgered.)
//   • Gap        — spaces the scroll node's ONE child against nothing. The
//                  author meant "8dp between my rows"; the rows are children of
//                  the CONTENT node and never see it.
//   • Justify /  — free space on a scrolling viewport is NEGATIVE (content 800
//     Align        against a 200 viewport → −600), so `Center` offsets the
//                  content node to y = −300 and `FlexEnd` to −600. A scroll view
//                  cannot scroll above offset 0: the top of the content becomes
//                  PERMANENTLY UNREACHABLE.
//   • Padding    — insets the content node, moving every frame in the shells'
//                  parity table and raising "does contentSize include the
//                  padding?" — a question two shells would answer differently.
//
// A parameter that cannot be set is a bug that cannot be written. The author who
// wants to control the content's layout COMPOSES:
//
//     <BnScroll Height="200">
//         <BnColumn Gap="8" Padding="16">   ← the content's layout, in a node
//             …rows…                          the author owns
//         </BnColumn>
//     </BnScroll>
//
// which already works, needs no new concept, and is React Native's
// `contentContainerStyle` escape hatch without a second style surface. A
// first-class `ContentPadding`/`ContentGap` (RN's content-container style, set
// on the synthetic node) is LEDGERED, not stubbed.
//
// The shells enforce the same rule at the raw-element hatch: `scroll` is a
// mappable element name, so `OpenElement("scroll") + AddAttribute("gap", …)`
// still reaches SetStyle. On a node of type `scroll` the names flexDirection,
// justifyContent, alignItems, flexWrap, gap and padding are IGNORED AND LOGGED
// (6.2 design, "Container styles on a scroll node"). One rule, both doors.
//
// What REMAINS is the item surface — how this viewport is placed IN ITS PARENT
// (Grow/Shrink/Basis/AlignSelf, the box, Margin, Position) — plus BackgroundColor.
// Those are correct on a scroll node and mean exactly what they mean on a BnView.
//
// ── A SCROLL NODE NEEDS A DEFINITE HEIGHT ────────────────────────────────────
// With no definite height the scroll node is `auto` — it takes its height FROM
// its content, so viewport == content and there is nothing to scroll. Nothing is
// defaulted here (the wire says exactly what the author said — the un-styled
// invariant); the SHELLS warn once at runtime when it happens (Gates 2/3).
//
// The shapes that give it one:
//
//     <BnScroll Height="200">                  an explicit height (what the demo does)
//     <BnScroll Grow="1" Basis="0">            CSS's `flex: 1`, in a bounded parent
//     <BnScroll Grow="1" Shrink="1">           …the other way round
//
// ── AND *Grow="1"* ALONE IS **NOT** ONE OF THEM (Gate 2 review) ──────────────
// This file used to say it was. It is not, and the reason is THE PHASE'S OWN
// MECHANISM, one level up — nobody looked:
//
//   Grow="1" leaves flexBasis at `auto`, so the scroll node's flex BASIS is its
//   CONTENT's height (800). Against a 200-high parent the free space is
//   200 − 800 = −600 — NEGATIVE. flexGrow only ever distributes POSITIVE free
//   space, so it never gets a say; the negative goes to the SHRINK pass, in
//   proportion to flexShrink — WHICH YOGA DEFAULTS TO 0. Nothing shrinks. The
//   viewport keeps its 800, spills out of its 200-high parent, and equals its
//   own content: nothing scrolls.
//
// That is the same sentence this phase writes in bold about the CONTENT node
// (Yoga's flexShrink default of 0 is why IT keeps its 800) — it is just as true
// of the VIEWPORT. `Basis="0"` is what makes the free space positive again, and
// that is why CSS's `flex: 1` shorthand sets basis to 0 and not to auto.
// Asserted on the device: WidgetMapperScrollTest
// .a_Grow_ONLY_scroll_node_does_NOT_get_a_definite_height_and_is_warned_about.
//
// Sequence numbers mirror BnView's exactly, with the gaps (2 `padding`,
// 4 `flexDirection`, 5-8 the container family) left EMPTY: the two
// BuildRenderTrees are meant to be read side by side, and the gaps are the
// decision, visible.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A scrolling viewport — emits the <c>scroll</c> element (host NodeType
/// "scroll"). It <em>is</em> a flex <b>item</b> (<see cref="BnView"/>'s item
/// parameters: <see cref="Grow"/>, <see cref="Shrink"/>, <see cref="Basis"/>,
/// <see cref="AlignSelf"/>, the box, <see cref="Margin"/>,
/// <see cref="Position"/>) that scrolls its content.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a flex container.</b> There is no <c>Direction</c>, <c>Justify</c>,
/// <c>Align</c>, <c>Wrap</c>, <c>Gap</c> or <c>Padding</c> parameter: those
/// would style the scroll node, whose only child is the shells' synthetic
/// content node — they would space nothing, or push the content to a negative
/// offset a scroll view can never scroll back to. To lay out the content, put a
/// <see cref="BnColumn"/> inside: <c>&lt;BnScroll Height="200"&gt;&lt;BnColumn
/// Gap="8"&gt;…&lt;/BnColumn&gt;&lt;/BnScroll&gt;</c>. The shells IGNORE (and
/// log) those style names on a scroll node, so the raw-element route is closed
/// too.
/// </para>
/// <para>
/// <b>Vertical only</b> (6.2 design decision 2). Horizontal scrolling is a
/// different widget class on Android and is ledgered, not stubbed.
/// </para>
/// <para>
/// <b>Give it a definite height.</b> An <c>auto</c>-height <c>BnScroll</c> takes
/// its height <em>from</em> its content, so the viewport equals the content and
/// nothing scrolls; the shells log one warning when that happens. Use an explicit
/// <see cref="Height"/>, or — inside a parent with a definite height —
/// <c>Grow="1" Basis="0"</c> (CSS's <c>flex: 1</c>).
/// </para>
/// <para>
/// <b><see cref="Grow"/> ALONE IS NOT ENOUGH</b>, and this component's docs used
/// to say it was (corrected at the Phase 6.2 Gate 2 review). With
/// <see cref="Basis"/> left at <c>auto</c>, the scroll node's flex basis is its
/// <em>content's</em> height, so the free space against a shorter parent is
/// <b>negative</b> — and <see cref="Grow"/> only distributes <em>positive</em>
/// free space. The negative goes to the shrink pass, and Yoga's
/// <see cref="Shrink"/> default is <b>0</b>, so nothing shrinks: the viewport
/// keeps its content's full height, overflows its parent, and does not scroll.
/// <c>Basis="0"</c> (or <c>Shrink="1"</c>) is what fixes it — which is exactly why
/// CSS's <c>flex: 1</c> shorthand sets the basis to <c>0</c>.
/// </para>
/// <para>
/// Children ride the wire as children of the scroll node; the shells re-parent
/// them into a synthetic content node whose Yoga-computed height is the content
/// size (see the file header).
/// </para>
/// </remarks>
public sealed class BnScroll : ComponentBase
{
    /// <inheritdoc cref="BnView.BackgroundColor"/>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <inheritdoc cref="BnView.Margin"/>
    [Parameter] public string? Margin { get; set; }

    // ── NO container layout ───────────────────────────────────────────────────
    //
    // No Direction, Justify, Align, Wrap, Gap or Padding: a BnScroll is a flex
    // ITEM, and its content's layout belongs to the synthetic content node. Put
    // a BnColumn inside. See the file header — this absence is the design.

    // ── Item layout (how the viewport behaves INSIDE its parent) ──────────────

    /// <inheritdoc cref="BnView.AlignSelf"/>
    [Parameter] public FlexAlign? AlignSelf { get; set; }

    /// <inheritdoc cref="BnView.Grow"/>
    [Parameter] public float? Grow { get; set; }

    /// <inheritdoc cref="BnView.Shrink"/>
    [Parameter] public float? Shrink { get; set; }

    /// <inheritdoc cref="BnView.Basis"/>
    [Parameter] public string? Basis { get; set; }

    // ── Box ───────────────────────────────────────────────────────────────────

    /// <inheritdoc cref="BnView.Width"/>
    [Parameter] public string? Width { get; set; }

    /// <summary>Height of the VIEWPORT, e.g. <c>"200"</c>. Null = auto — and an
    /// auto-height scroll takes its height from its content, so nothing scrolls
    /// (see the class remarks). Either this, or <see cref="Grow"/> <b>plus</b>
    /// <see cref="Basis"/><c>="0"</c> inside a bounded parent — <see cref="Grow"/>
    /// on its own does NOT bound a viewport whose content is taller than its
    /// parent.</summary>
    [Parameter] public string? Height { get; set; }

    /// <inheritdoc cref="BnView.MinWidth"/>
    [Parameter] public string? MinWidth { get; set; }

    /// <inheritdoc cref="BnView.MaxWidth"/>
    [Parameter] public string? MaxWidth { get; set; }

    /// <inheritdoc cref="BnView.MinHeight"/>
    [Parameter] public string? MinHeight { get; set; }

    /// <inheritdoc cref="BnView.MaxHeight"/>
    [Parameter] public string? MaxHeight { get; set; }

    // ── Positioning ───────────────────────────────────────────────────────────

    /// <inheritdoc cref="BnView.Position"/>
    [Parameter] public FlexPosition? Position { get; set; }

    /// <inheritdoc cref="BnView.Top"/>
    [Parameter] public string? Top { get; set; }

    /// <inheritdoc cref="BnView.Right"/>
    [Parameter] public string? Right { get; set; }

    /// <inheritdoc cref="BnView.Bottom"/>
    [Parameter] public string? Bottom { get; set; }

    /// <inheritdoc cref="BnView.Left"/>
    [Parameter] public string? Left { get; set; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised with the shell-conflated vertical content offset (dp/pt)
    /// when the viewport scrolls (Phase 7.2 — the <c>onScroll</c> wire).
    /// OPTIONAL: the <c>scroll</c> attach is emitted only when a delegate is
    /// set (<c>HasDelegate</c> — the <see cref="BnInput.OnFocus"/> pattern from
    /// 4.2), so an unwired BnScroll's patch shape is byte-identical to the
    /// pre-7.2 one and BnScrollDemo's golden (1 attach: the back click) does
    /// not churn. The shell conflates: at most ONE dispatch per committed
    /// frame, latest offset wins — a slow consumer sees fewer, fresher events,
    /// never a queue (the wire contract, mirrored in both shells in Gates
    /// 2/3). The offset arrives raw: iOS rubber-banding can make it negative
    /// or push it past the scroll range — consumers clamp
    /// (<see cref="BnList{TItem}"/>'s window function does).</summary>
    [Parameter] public EventCallback<BnScrollEventArgs> OnScroll { get; set; }

    /// <summary>The scrolled content. Rides the wire as children of the scroll
    /// node; the shells parent it into the synthetic content node. Wrap it in a
    /// <see cref="BnColumn"/> to give it a gap, a padding or an alignment.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "scroll");

        // Null attributes are not appended to the frame array at all — that is
        // how "unset" reaches the wire as "absent" (BnView's un-styled invariant,
        // and the reason this component needs no null guards).
        b.AddAttribute(1, "backgroundColor", BackgroundColor);
        // 2 ("padding") is deliberately UNUSED — see the file header.
        b.AddAttribute(3, "margin", Margin);

        // 4 ("flexDirection") and 5-8 (justifyContent / alignItems / flexWrap /
        // gap) are deliberately UNUSED: the container-layout family styles the
        // SCROLL node, whose only child is the synthetic content node. The gaps
        // are the decision — see the file header.

        b.AddAttribute(9, "alignSelf", AlignSelf.ToStyleValue());
        b.AddAttribute(10, "flexGrow", Grow.ToStyleValue());
        b.AddAttribute(11, "flexShrink", Shrink.ToStyleValue());
        b.AddAttribute(12, "flexBasis", Basis);

        b.AddAttribute(13, "width", Width);
        b.AddAttribute(14, "height", Height);
        b.AddAttribute(15, "minWidth", MinWidth);
        b.AddAttribute(16, "maxWidth", MaxWidth);
        b.AddAttribute(17, "minHeight", MinHeight);
        b.AddAttribute(18, "maxHeight", MaxHeight);

        b.AddAttribute(19, "position", Position.ToStyleValue());
        b.AddAttribute(20, "top", Top);
        b.AddAttribute(21, "right", Right);
        b.AddAttribute(22, "bottom", Bottom);
        b.AddAttribute(23, "left", Left);

        // Attach-only-when-subscribed (the BnInput.OnFocus pattern): an
        // unwired BnScroll emits no `scroll` attach, so its wire shape is
        // byte-identical to pre-7.2 — the un-styled invariant, for events.
        if (OnScroll.HasDelegate)
            b.AddAttribute(24, "onscroll", OnScroll);

        b.AddContent(100, ChildContent);

        b.CloseElement();
    }
}
