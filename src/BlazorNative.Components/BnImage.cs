using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnImage — Phase 6.3 Task 1.1 (design §"The model", decision 3).
//
// A URL image. It emits the `img` element, which MapElementToNodeType has mapped
// to NodeType 5 ("image") since Phase 2.5 — so this component is a pure
// Components-side addition: NO new export, NO new NodeType, NO new patch kind,
// NO ABI change (still 9 exports + the 72-byte bridge). `image` is the LAST
// stubbed leaf: it creates a widget on both platforms today and measures to
// ZERO, because neither shell has a source-loading path. Gates 2/3 give it one.
//
// ── `Src` IS A PROP, NOT A STYLE ─────────────────────────────────────────────
// It rides the EXISTING UpdateProp wire (patch kind 5), where `value`,
// `placeholder` and `enabled` already ride. It must NOT be added to
// NativeRenderer's style allow-list, and the reason is structural, not stylistic:
// after 6.1 that list is a ROUTING TABLE with exactly two destinations —
//
//     YogaStyleAttributes   → the node's YOGA node   (layout)
//     VisualStyleAttributes → the View / UIView      (paint)
//
// — and a URL is NEITHER. Putting `src` on the style wire would force both
// hand-written shell parsers (Kotlin's YogaLayout, iOS's BnYogaLayout.mm) to grow
// a third arm inside a two-arm contract, for a name that is not layout and is not
// paint. Pinned in Renderer.Tests/StyleAttributePartitionTests.Src_IsAProp_NotAStyle
// and, from the component's side, in BnComponentTests (which asserts the patch
// KIND — a `src` that drifted onto the style wire would still carry the right URL,
// so a value-only test would not see it).
//
// THERE IS NO BINARY PATH ON THE WIRE, and there does not need to be: .NET names
// the source, THE SHELL FETCHES THE BYTES. That is React Native's model, and it is
// what lets Coil (Android) and Kingfisher (iOS) do the decoding, downsampling,
// cancellation and caching that neither of us wants to hand-roll.
//
// ── BnImage IS A FLEX *ITEM*, AND A *LEAF* ───────────────────────────────────
// The surface below is BnScroll's, minus ChildContent, plus Src. Same exclusion
// of the container-layout family — for a DIFFERENT reason, which is why it is
// said out loud rather than inherited:
//
//     Direction · Justify · Align · Wrap · Gap · Padding   ← NOT parameters
//
//   • BnScroll excludes them because its only Yoga child is the shells' SYNTHETIC
//     content node, so each of the six would style a node with one child it does
//     not own (see BnScroll's header — gap spaces nothing, justify pushes the
//     content to an offset a scroll view can never scroll back to).
//   • BnImage excludes them because it has NO YOGA CHILDREN AT ALL. It is a leaf.
//     `Gap` between what? `Justify` of what? Every one of the six is a parameter
//     that could only ever do nothing — and a parameter that cannot be set is a
//     bug that cannot be written.
//
// And a leaf has no ChildContent either. `<BnImage>…</BnImage>` does not compile,
// which is the point: an image is not a container, and an author who wants to
// overlay something on one COMPOSES (a BnView with Position="Absolute" children),
// exactly as they compose a BnColumn inside a BnScroll.
//
// What REMAINS is the item surface — how this image is placed IN ITS PARENT
// (Grow/Shrink/Basis/AlignSelf, the box, Margin, Position) — plus BackgroundColor.
// Those are correct on an image node and mean exactly what they mean on a BnView.
//
// ── MEASUREMENT: THE ONE THING THIS COMPONENT'S SHAPE DECIDES ────────────────
// `image` is in the shells' `measuredNodeTypes` (6.1 attaches the measure func BY
// NODETYPE), so an image node is a Yoga leaf with a measure function. Which means
// the presence or absence of Width/Height on THIS component is the whole
// difference between the two paths the parity contract normalises:
//
//   Width AND Height set  → definite. Yoga never calls measure. The frame is those
//                           numbers, ALWAYS — the bytes never move it. No reflow.
//   neither set           → intrinsic. The measure func reports 0 × 0 until the
//                           bytes land, then the image's NATURAL pixel size in
//                           dp/pt — and the shell marks the node dirty, so the tree
//                           re-solves and the SIBLINGS BELOW IT MOVE DOWN. Exactly
//                           ONE reflow, never two.
//   failure               → stays 0 × 0. It reserves NOTHING and does not retry.
//
// Both paths, and the failure, are demonstrated on `/image` (BnImageDemo) with a
// sibling under each — a sibling's `y` is the only honest proof that a reflow did
// or did not happen. See that file's frame table: it is the spec Gates 2/3 are
// held to.
//
// ── `Src` → null: THE OTHER REFLOW DIRECTION, AND IT IS ON THE WIRE TODAY ────
// NORMATIVE — because the renderer ALREADY EMITS IT and no shell has been told what
// it means. `Src` is an ordinary nullable attribute: setting it back to null on a
// re-render is a RemoveAttribute on a non-style name, which the renderer turns into
//
//     UpdatePropPatch(nodeId, "src", null)
//
// exactly as BnButton's `Enabled` false→true does (BnButton_ReEnabled_
// EmitsEnabledNullProp — the established precedent; a value of null on the prop
// wire means "the author took the attribute away", and the shell restores the
// default). Pinned from the component's side in
// BnComponentTests.BnImage_SrcGoesNull_EmitsUpdatePropNullOnThePropWire.
//
// WHAT THE SHELLS OWE (Gates 2/3, and it is not a footnote — it is a SECOND REFLOW
// DIRECTION, upward, which the parity contract otherwise never mentions):
//
//     src → null  → CANCEL the in-flight request for the node
//                 → CLEAR the image (setImageDrawable(null) / .image = nil)
//                 → markDirty the Yoga node
//                 → re-solve
//                 → the node collapses back to 0 × 0 (if it is intrinsic) and its
//                   SIBLINGS BELOW IT MOVE BACK UP.
//
// A definite (Width+Height) image keeps its frame, of course — Yoga still never
// calls its measure func; only the pixels go away. And a shell that instead NPEs on
// a null value, or that keeps painting the old bytes, is wrong in the way two shells
// wrong DIFFERENTLY is worst: silently, on one platform.
//
// SCOPE, said out loud rather than left to be inferred: `/image` has no runtime
// affordance that flips a `Src` to null (adding one would rewrite this phase's frame
// tables), so Gates 2/3 assert this at the MAPPER level — where they already assert
// UpdateProp behaviour — and not on the demo page. The wire emission is pinned in
// .NET, the shell behaviour is pinned by the mapper tests, and the demo pages stay
// as they are. That is the whole of the 6.3 decision on `src → null`.
//
// ── WHAT IS DELIBERATELY ABSENT (6.3 design decision 3) ──────────────────────
// No `Placeholder`. No `OnError`. No `ContentMode`. Not an oversight and not
// laziness: EACH OF THEM CHANGES MEASUREMENT, which is the one thing this phase
// exists to make identical on two platforms.
//
//   • Placeholder — does the placeholder measure like the image, or like itself?
//     If it measures, an intrinsic image reflows TWICE (0×0 → placeholder →
//     natural) and the "one reflow, never two" rule is gone.
//   • OnError     — an error callback invites "reserve the space anyway", which is
//     the opposite of the contract's failure row (stays 0×0, reserves nothing).
//   • ContentMode — `cover`/`contain` change what the measure func REPORTS versus
//     what the view PAINTS, and two libraries will not agree about it for free.
//
// Each deserves its own design rather than a footnote in the fetch phase. Ledgered
// for M7.
//
// Sequence numbers mirror BnView's and BnScroll's exactly, with the same gaps
// (2 `padding`, 4-8 the container family) left EMPTY, so the three
// BuildRenderTrees read side by side and the gaps are the decision, visible.
// `src` is APPENDED at 24, after the style block — the way BnView appended its
// flex block rather than renumbering what was already there.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A URL image — emits the <c>img</c> element (host NodeType "image"). The shell
/// fetches, decodes and measures the bytes; .NET only names the source.
/// <see cref="Src"/> is a <b>prop</b>, and the rest is <see cref="BnView"/>'s flex
/// <b>item</b> surface (<see cref="Grow"/>, <see cref="Shrink"/>,
/// <see cref="Basis"/>, <see cref="AlignSelf"/>, the box, <see cref="Margin"/>,
/// <see cref="Position"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Sizing is the decision this component's shape makes.</b> Set
/// <see cref="Width"/> <em>and</em> <see cref="Height"/> and the frame is exactly
/// those numbers, always — the bytes never move it, so nothing below it ever
/// reflows. Set NEITHER and the image is <em>intrinsic</em>: it measures
/// <c>0 × 0</c> until its bytes arrive and its <b>natural pixel size</b> (in
/// dp/pt) afterwards, at which point the shell marks its Yoga node dirty, the tree
/// re-solves, and its siblings below move down. Exactly one reflow, never two.
/// </para>
/// <para>
/// <b>On failure the node stays <c>0 × 0</c></b>: it reserves no space, and it does
/// not retry. On a <see cref="Src"/> change, or when the node is removed, the shell
/// cancels the in-flight request — a completion must never paint into a removed
/// node (on iOS that would touch a freed Yoga handle). And on
/// <b><see cref="Src"/> → <c>null</c></b> — which the renderer emits as
/// <c>UpdateProp("src", null)</c> — the shell cancels, <em>clears</em> the image,
/// marks the node dirty and re-solves: an intrinsic image collapses back to
/// <c>0 × 0</c> and its siblings move back <em>up</em>. That is the reflow in the
/// other direction, and it is part of the contract.
/// </para>
/// <para>
/// <b>Not a flex container, and not a container at all.</b> There is no
/// <c>Direction</c>, <c>Justify</c>, <c>Align</c>, <c>Wrap</c>, <c>Gap</c>,
/// <c>Padding</c> — and no <c>ChildContent</c>. An image is a <em>leaf</em>: it has
/// no Yoga children for any of them to arrange. To overlay content on an image,
/// compose (a <see cref="BnView"/> parent with an absolutely positioned child).
/// </para>
/// <para>
/// <b>No <c>Placeholder</c>, no <c>OnError</c>, no <c>ContentMode</c></b> (Phase 6.3
/// design decision 3). Each of the three changes <em>measurement</em> — a measuring
/// placeholder would mean two reflows instead of one; an error hook invites
/// reserving space a failure must not reserve; a content mode divorces what is
/// measured from what is painted — so each gets its own design rather than a
/// footnote in the fetch phase. Ledgered for M7.
/// </para>
/// </remarks>
public sealed class BnImage : ComponentBase
{
    /// <summary>The image source — a URL the SHELL fetches (there is no binary
    /// path on the wire). Rides the <c>UpdateProp</c> wire, <b>not</b> the
    /// <c>SetStyle</c> one: it is neither layout nor paint, so it belongs to
    /// neither half of the shells' style routing table (see the file header).
    /// <para>Null = no source: nothing is fetched and the node keeps measuring
    /// <c>0 × 0</c> (unless <see cref="Width"/>/<see cref="Height"/> say
    /// otherwise). Changing it cancels any in-flight request for this node.</para>
    /// <para><b>Setting it BACK to null is a real wire event</b>, not a no-op: it
    /// emits <c>UpdateProp(nodeId, "src", null)</c> (the same shape
    /// <see cref="BnButton.Enabled"/> uses when it leaves the tree), and the shells
    /// must cancel, CLEAR the image, <c>markDirty</c> and re-solve — so an intrinsic
    /// image collapses to <c>0 × 0</c> and its siblings move back UP. That is the
    /// reflow in the other direction; see the file header.</para>
    /// </summary>
    [Parameter] public string? Src { get; set; }

    /// <inheritdoc cref="BnView.BackgroundColor"/>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <inheritdoc cref="BnView.Margin"/>
    [Parameter] public string? Margin { get; set; }

    // ── NO container layout, and NO ChildContent ──────────────────────────────
    //
    // No Direction, Justify, Align, Wrap, Gap or Padding, and no ChildContent: an
    // image is a LEAF — it has no Yoga children for any of them to arrange. See
    // the file header; this absence is the design, not an omission.

    // ── Item layout (how the image behaves INSIDE its parent) ─────────────────

    /// <inheritdoc cref="BnView.AlignSelf"/>
    [Parameter] public FlexAlign? AlignSelf { get; set; }

    /// <inheritdoc cref="BnView.Grow"/>
    [Parameter] public float? Grow { get; set; }

    /// <inheritdoc cref="BnView.Shrink"/>
    [Parameter] public float? Shrink { get; set; }

    /// <inheritdoc cref="BnView.Basis"/>
    [Parameter] public string? Basis { get; set; }

    // ── Box ───────────────────────────────────────────────────────────────────

    /// <summary>Declared width, e.g. <c>"200"</c>. Null = auto. Set it together
    /// <em>with</em> <see cref="Height"/> and the image is sized IMMEDIATELY — its
    /// frame never moves when the bytes land; set NEITHER and the image is
    /// intrinsic: <c>0 × 0</c> until the bytes, its natural size after (see the
    /// class remarks).</summary>
    [Parameter] public string? Width { get; set; }

    /// <summary>Declared height, e.g. <c>"120"</c>. Null = auto. The sizing rule is
    /// <see cref="Width"/>'s and is stated there: BOTH set → definite, never
    /// measured; NEITHER set → intrinsic. (Its own summary rather than an
    /// <c>inheritdoc</c> of <see cref="Width"/>, which would document this property
    /// as "declared width" — <see cref="BnScroll"/>'s precedent.)</summary>
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

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "img");

        // Null attributes are not appended to the frame array at all — that is how
        // "unset" reaches the wire as "absent" (BnView's un-styled invariant, and
        // the reason this component needs no null guards). It holds for `src` too:
        // a null Src is an image with no source, and the wire says exactly what the
        // author said.
        b.AddAttribute(1, "backgroundColor", BackgroundColor);
        // 2 ("padding") is deliberately UNUSED — a leaf has no children to inset.
        b.AddAttribute(3, "margin", Margin);

        // 4 ("flexDirection") and 5-8 (justifyContent / alignItems / flexWrap /
        // gap) are deliberately UNUSED: the container-layout family arranges
        // CHILDREN, and an image has none. The gaps are the decision — see the
        // file header.

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

        // THE PROP. `src` is NOT in NativeRenderer's style allow-list, so
        // ProcessAttribute routes it to UpdatePropPatch — the same wire `value` and
        // `placeholder` ride. Appended at 24, after the style block, so the
        // style sequence numbers stay identical to BnView's and BnScroll's.
        b.AddAttribute(24, "src", Src);

        // No AddContent: a leaf has no ChildContent (BnView/BnScroll use 100).

        b.CloseElement();
    }
}
