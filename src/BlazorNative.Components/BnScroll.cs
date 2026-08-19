using System.Globalization;
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
// Those are correct on a scroll node and mean exactly what they mean on a
// BnView. That is why BnScroll derives from BnLayoutItem rather than
// BnLayoutContainer: it has children, but no container-layout family of its
// own — see BnLayoutContainer's own remarks, which name this type as the
// reason that distinction exists.
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
// ── Sequence numbers (Phase 13.0) ─────────────────────────────────────────────
// The item surface (1–17) is emitted by BnLayoutItem.EmitItemAttributes — this
// component no longer declares or numbers any of those 17 itself. This
// component's own vocabulary (the scroll event and the programmatic-scroll
// command) rides 100+, clear of the base band so a collision cannot silently
// mis-diff. ChildContent is 200.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A scrolling viewport. Renders as a native <c>ScrollView</c> on Android and a
/// <c>UIScrollView</c> on iOS. It <em>is</em> a flex <b>item</b>
/// (<see cref="BnLayoutItem.Grow"/>, <see cref="BnLayoutItem.Shrink"/>,
/// <see cref="BnLayoutItem.Basis"/>, <see cref="BnLayoutItem.AlignSelf"/>, the
/// box, <see cref="BnLayoutItem.Margin"/>, <see cref="BnLayoutItem.Position"/>)
/// that scrolls its content.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a flex container.</b> There is no <c>Direction</c>, <c>Justify</c>,
/// <c>Align</c>, <c>Wrap</c>, <c>Gap</c> or <c>Padding</c> parameter: those
/// would style the viewport itself rather than the content inside it — they
/// would space nothing, or push the content to a negative offset a scroll view
/// can never scroll back to. To lay out the content, put a
/// <see cref="BnColumn"/> inside: <c>&lt;BnScroll Height="200"&gt;&lt;BnColumn
/// Gap="8"&gt;…&lt;/BnColumn&gt;&lt;/BnScroll&gt;</c>. Both platforms ignore
/// those style names on a scroll node, so there is no way around it.
/// </para>
/// <para>
/// <b>Vertical only.</b> Horizontal scrolling is a different widget class on
/// Android, and is not supported rather than half-supported.
/// </para>
/// <para>
/// <b>Give it a definite height.</b> An <c>auto</c>-height <c>BnScroll</c> takes
/// its height <em>from</em> its content, so the viewport equals the content and
/// nothing scrolls; the shells log one warning when that happens. Use an explicit
/// <see cref="BnLayoutItem.Height"/>, or — inside a parent with a definite
/// height — <c>Grow="1" Basis="0"</c> (CSS's <c>flex: 1</c>).
/// </para>
/// <para>
/// <b><see cref="BnLayoutItem.Grow"/> alone is not enough</b>, and it is the
/// mistake everybody makes here. With <see cref="BnLayoutItem.Basis"/> left at
/// <c>auto</c>, the scroll node's flex basis is its <em>content's</em> height,
/// so the free space against a shorter parent is <b>negative</b> — and
/// <see cref="BnLayoutItem.Grow"/> only distributes <em>positive</em> free
/// space. The negative goes to the shrink pass, and Yoga's
/// <see cref="BnLayoutItem.Shrink"/> default is <b>0</b>, so nothing shrinks:
/// the viewport keeps its content's full height, overflows its parent, and does
/// not scroll. <c>Basis="0"</c> (or <c>Shrink="1"</c>) is what fixes it — which
/// is exactly why CSS's <c>flex: 1</c> shorthand sets the basis to <c>0</c>.
/// </para>
/// <para>
/// Your children are re-parented into a content node inside the viewport, whose
/// height is the total content height — that is what the platform scrolls.
/// </para>
/// </remarks>
public sealed class BnScroll : BnLayoutItem
{
    // ── NO container layout ───────────────────────────────────────────────────
    //
    // No Direction, Justify, Align, Wrap, Gap or Padding: a BnScroll is a flex
    // ITEM, and its content's layout belongs to the synthetic content node. Put
    // a BnColumn inside. See the file header — this absence is the design, and
    // it is also why this type derives from BnLayoutItem rather than
    // BnLayoutContainer.
    //
    // The item surface (BackgroundColor, Margin, AlignSelf, Grow/Shrink/Basis,
    // the box, Position and its insets) is inherited from BnLayoutItem and
    // emitted at sequence 1-17 by EmitItemAttributes — see that type for the
    // parameters.

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised with the vertical content offset, in dp on Android and pt
    /// on iOS, as the viewport scrolls. Optional: nothing is attached to the
    /// native control unless you supply a handler.
    /// <para>
    /// <b>Events are conflated, not queued</b> — at most one per rendered frame,
    /// latest offset wins. A slow handler therefore sees fewer, fresher events
    /// rather than a growing backlog, so you cannot use this to count scroll
    /// ticks.
    /// </para>
    /// <para>
    /// <b>The offset arrives raw.</b> iOS rubber-banding can report a negative
    /// offset, or one past the end of the scroll range. Clamp it if your maths
    /// depends on it being inside the content.
    /// </para></summary>
    [Parameter] public EventCallback<BnScrollEventArgs> OnScroll { get; set; }

    /// <summary>Scroll to the end of the content on every render, for
    /// append-driven views — a log tail, a chat transcript, console output —
    /// where new content arriving below the fold should bring itself into view.
    /// Default false.
    /// <para>
    /// <b>Every render, not every append.</b> This component cannot see that
    /// your content grew; it only knows it re-rendered. So a re-render for an
    /// unrelated reason also scrolls to the end. That is usually what a view
    /// with this switched on wants, but it is why it is a parameter you choose
    /// rather than a default — if you need "only when items were added", call
    /// <see cref="ScrollToEndAsync"/> yourself at the point you add them.
    /// </para></summary>
    [Parameter] public bool AutoScrollToEnd { get; set; }

    /// <summary>The content to scroll. Wrap it in a <see cref="BnColumn"/> to
    /// give it a gap, a padding or an alignment — this component will not do
    /// that for you.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    // ── Programmatic scrolling (#256) ─────────────────────────────────────────
    //
    // The command reaches the shell as an ATTRIBUTE on this component's own
    // element, which the renderer routes to a ScrollTo patch. Two consequences
    // are worth stating where the code is, because both are the reason the API
    // looks like this:
    //
    //  1. ORDERING IS FREE AND IS THE WHOLE POINT. The attribute rides the same
    //     render as whatever else changed, so "append rows, then scroll to the
    //     end" is ONE frame the shell applies in order. The rows exist before
    //     the scroll lands. Nothing here has to wait for anything.
    //
    //  2. THE NONCE IS WHY A REPEAT WORKS. Blazor's diff emits an attribute
    //     edit only when the value CHANGES, which is right for state and wrong
    //     for a command — two ScrollToEndAsync() calls in a row would collapse
    //     into one. The counter makes each call a distinct value. It is
    //     stripped by the renderer and never crosses the wire.
    //
    // Both methods are async-shaped and complete as soon as the command is
    // QUEUED, not when the view has moved: the shell applies the scroll after
    // its next layout pass, and there is no completion coming back. Observing
    // the result is what OnScroll is for. Returning ValueTask anyway keeps the
    // signatures honest for a future in which a completion does exist — the
    // alternative, a void method that looks synchronous while being anything
    // but, would be the lie.

    private int _commandNonce;
    private string? _pendingCommand;

    /// <summary>Scrolls to <paramref name="offset"/> — dp on Android, pt on iOS,
    /// the same units <see cref="OnScroll"/> reports. An offset outside the
    /// content is clamped by the platform, not rejected here.</summary>
    /// <param name="offset">The vertical content offset to scroll to.</param>
    /// <returns>A task that completes when the command has been QUEUED for the
    /// next frame — <b>not</b> when the view has finished moving. Use
    /// <see cref="OnScroll"/> to observe where it actually landed.</returns>
    public ValueTask ScrollToAsync(float offset)
        => IssueCommand(offset.ToString(CultureInfo.InvariantCulture));

    /// <summary>Scrolls to the end of the content.</summary>
    /// <remarks>The end is computed by the shell, not here: content height is a
    /// layout result the platform holds, so an offset computed on this side
    /// would be one frame stale — exactly wrong for the append-driven case this
    /// exists to serve.</remarks>
    /// <returns>A task that completes when the command has been QUEUED for the
    /// next frame — <b>not</b> when the view has finished moving.</returns>
    public ValueTask ScrollToEndAsync() => IssueCommand(ScrollToEndTarget);

    private ValueTask IssueCommand(string target)
    {
        _pendingCommand = $"{target}#{++_commandNonce}";
        StateHasChanged();
        return ValueTask.CompletedTask;
    }

    /// <summary>The command target meaning "the end of the content". Mirrors
    /// <c>NativeRenderer.ScrollToEndTarget</c>; the two are pinned equal by
    /// BnScrollCommandTests, because a silent disagreement here would be a
    /// command the renderer logs and drops.</summary>
    internal const string ScrollToEndTarget = "end";

    /// <summary>The attribute name the command rides. Mirrors
    /// <c>NativeRenderer.ScrollToAttributeName</c>, pinned equal by the same test.</summary>
    internal const string ScrollToAttributeName = "scrollTo";

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // AutoScrollToEnd re-arms on every parameter set — see the parameter's
        // own doc for why "every render" is the honest semantic and not a bug.
        // Deliberately AFTER any explicit ScrollToAsync of this same render:
        // last writer wins, and an author who asked for a specific offset while
        // AutoScrollToEnd is on has asked for two contradictory things.
        if (AutoScrollToEnd)
            _pendingCommand = $"{ScrollToEndTarget}#{++_commandNonce}";
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "scroll");

        // Sequence 1-17: the shared item surface (BackgroundColor, Margin,
        // AlignSelf, Grow/Shrink/Basis, the box, Position and its insets) — see
        // BnLayoutItem.EmitItemAttributes. A null value is not appended to the
        // frame array at all, which is how "unset" reaches the wire as "absent".
        EmitItemAttributes(b);

        // Sequence 100+: this component's own surface, clear of the base's
        // 1-17 so a collision (which would produce a wrong diff, silently)
        // cannot happen.

        // Attach-only-when-subscribed (the BnInput.OnFocus pattern): an
        // unwired BnScroll emits no `scroll` attach, so its wire shape is
        // byte-identical to pre-7.2 — the un-styled invariant, for events.
        if (OnScroll.HasDelegate)
            b.AddAttribute(100, "onscroll", OnScroll);

        // #256 — the command, when one is pending. Null on a render with no
        // command, so the un-styled invariant holds for commands too: a
        // BnScroll nobody has scrolled emits the same wire shape it always did.
        // The value is NOT cleared after emission: clearing it would make the
        // NEXT render remove the attribute and the one after re-add it, and a
        // stale-but-unchanged value is diffed away for free (that is exactly
        // what the nonce makes a repeat immune to).
        b.AddAttribute(101, ScrollToAttributeName, _pendingCommand);

        b.AddContent(200, ChildContent);

        b.CloseElement();
    }
}
