using BlazorNative.Core;
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
// ── THE 6.3 LEDGER, RESOLVED (Phase 7.5 — each shipped AS a measurement rule) ─
// 6.3 deliberately refused `Placeholder`, `OnError` and `ContentMode` as
// footnotes because each was a MEASUREMENT question. Phase 7.5 ships all three
// with the same one-sentence answer: ZERO new measurement states. The 6.3
// contract above — definite never measured; intrinsic 0×0 until the bytes,
// natural size after, ONE reflow; failure stays 0×0; `src → null` collapses —
// survives VERBATIM. Each trap 6.3 named is closed by construction:
//
//   • PlaceholderColor — a COLOR, not content and not a second source (both
//     rejected: a child would un-leaf the leaf and MEASURE — the two-reflow
//     trap verbatim; a second source doubles every piece of async
//     bookkeeping). A placeholder NEVER measures and never reflows: it is
//     paint inside whatever box Yoga already gave the node. The consequence,
//     said out loud: an INTRINSIC image's placeholder is invisible — a 0×0
//     box paints nothing, which is correct, not diagnosed (a feature of the
//     declared or flex-grown box, exactly as in RN). Wire name
//     `placeholderColor`, NOT `placeholder` — that has been the input hint
//     since M2, and reusing it would fork one prop's meaning by NodeType
//     inside both hand-written shells (partition-pinned).
//   • OnError — the `scroll` precedent end-to-end: EventName `error` on the
//     existing dispatch_event, attached ONLY when HasDelegate, payload = the
//     wire `src` verbatim (the only fact two loaders share). Failure never
//     changes measurement IN EITHER DIRECTION: a declared box holds (Yoga
//     never called its measure func — the space stays reserved because it was
//     DECLARED, not because it failed), an intrinsic node stays 0×0 (the 6.3
//     failure row, unchanged). Fallback UI is the app's re-render on the
//     event; the shell changes nothing it does not own.
//   • ContentMode — four modes (Contain default / Cover / Stretch / Center),
//     PAINT-ONLY: the measure func never consults the mode, so every frame in
//     every table is mode-invariant (see ImageContentMode.cs for the mode
//     table, the strict wire strings and the recorded Contain-not-cover
//     default). The paint never escapes the layout box — both shells clip.
//
// The three ride the EXISTING wire: two props (`placeholderColor`,
// `contentMode`) on UpdateProp where `src` rides, one event name (`error`) on
// dispatch_event where `scroll` rides. No new NodeType, no ABI change — still
// 9 exports + the 72-byte bridge. The /imagepolish page (BnImagePolishDemo)
// re-runs 6.3's reflow proof WITH the new features present, pinning "one
// reflow, never two" against the exact feature 6.3 refused for fear of it.
//
// Sequence numbers mirror BnView's and BnScroll's exactly, with the same gaps
// (2 `padding`, 4-8 the container family) left EMPTY, so the three
// BuildRenderTrees read side by side and the gaps are the decision, visible.
// `src` is APPENDED at 24, after the style block — the way BnView appended its
// flex block rather than renumbering what was already there — and the 7.5
// additions append after `src` (25-27), same rule, no renumbering.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// An image loaded from a URL. Renders as a native <c>ImageView</c> on Android
/// and a <c>UIImageView</c> on iOS. The platform fetches, decodes and measures
/// the bytes — your code only names the source. The rest of its surface is
/// <see cref="BnView"/>'s flex <b>item</b> surface (<see cref="Grow"/>,
/// <see cref="Shrink"/>, <see cref="Basis"/>, <see cref="AlignSelf"/>, the box,
/// <see cref="Margin"/>, <see cref="Position"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Sizing is the decision this component's shape makes.</b> Set
/// <see cref="Width"/> <em>and</em> <see cref="Height"/> and the frame is exactly
/// those numbers, always — the bytes never move it, so nothing below it ever
/// reflows. Set NEITHER and the image is <em>intrinsic</em>: it measures
/// <c>0 × 0</c> until its bytes arrive and its <b>natural pixel size</b> (in dp
/// or pt) afterwards, at which point the layout re-solves and its siblings below
/// move down. Exactly one reflow, never two.
/// </para>
/// <para>
/// <b>On failure the node stays <c>0 × 0</c></b>: it reserves no space, and it
/// does not retry. Changing <see cref="Src"/>, or removing the image, cancels the
/// in-flight request, so a late arrival can never paint into something you have
/// replaced. Setting <b><see cref="Src"/> to <c>null</c></b> clears the image and
/// re-solves: an intrinsic image collapses back to <c>0 × 0</c> and its siblings
/// move back <em>up</em>. That is the same reflow in the other direction, and it
/// is part of the contract rather than an accident.
/// </para>
/// <para>
/// <b>Not a flex container, and not a container at all.</b> There is no
/// <c>Direction</c>, <c>Justify</c>, <c>Align</c>, <c>Wrap</c>, <c>Gap</c>,
/// <c>Padding</c> — and no <c>ChildContent</c>. An image is a <em>leaf</em>: it has
/// no Yoga children for any of them to arrange. To overlay content on an image,
/// compose (a <see cref="BnView"/> parent with an absolutely positioned child).
/// </para>
/// <para>
/// <b>Nothing else here changes the sizing rules above.</b>
/// <see cref="PlaceholderColor"/> never measures, <see cref="OnError"/> never
/// changes measurement, and <see cref="ContentMode"/> is paint-only. Whatever you
/// set among those three, the frame is the one the two paragraphs above describe.
/// </para>
/// </remarks>
public sealed class BnImage : ComponentBase
{
    /// <summary>The image URL. The platform fetches it — you cannot hand this
    /// component bytes.
    /// <para>Null = no source: nothing is fetched, and the node keeps measuring
    /// <c>0 × 0</c> unless <see cref="Width"/> and <see cref="Height"/> say
    /// otherwise. Changing it cancels any request still in flight for this
    /// image.</para>
    /// <para><b>Setting it back to null is a real change, not a no-op:</b> the
    /// image is cleared, and an intrinsic node collapses to <c>0 × 0</c> so its
    /// siblings move back up.</para>
    /// </summary>
    [Parameter] public string? Src { get; set; }

    /// <summary>A colour to paint while the image is loading — a hex string, not
    /// content and not a second image. It stays as the visible state if the load
    /// fails, and is cleared once the real bytes paint. Null = no placeholder.
    /// <para><b>It never measures and never reflows:</b> it paints inside
    /// whatever box the layout already gave the node. That means an
    /// <em>intrinsic</em> image's placeholder is invisible — a <c>0 × 0</c> box
    /// paints nothing — so give the image a size if you want a placeholder to
    /// show. Letterbox bars show <see cref="BackgroundColor"/>, never
    /// this.</para></summary>
    [Parameter] public string? PlaceholderColor { get; set; }

    /// <summary>How the pixels are painted inside the box the layout gave this
    /// image. <b>Paint-only:</b> the mode never changes measurement, so every
    /// frame is identical under all four modes and the paint never escapes the
    /// box — both platforms clip it.
    /// <para>Null leaves the platform default, which is <c>Contain</c>
    /// (aspect-fit). Clearing the parameter restores that default. See
    /// <see cref="ImageContentMode"/> for the four modes.</para></summary>
    [Parameter] public ImageContentMode? ContentMode { get; set; }

    /// <summary>Raised when the image fails to load. The arguments carry the
    /// failed URL and nothing else. Optional: nothing is attached to the native
    /// control unless you supply a handler.
    /// <para><b>Failure never changes measurement:</b> a declared box keeps its
    /// size, an intrinsic node stays <c>0 × 0</c>. The platform will not
    /// substitute anything of its own — a fallback is whatever you render from
    /// this handler (swap the <see cref="Src"/>, unmount the image, show a
    /// message).</para>
    /// <para>It fires at most once per request, and only for the request that is
    /// still current: cancelling — changing <see cref="Src"/>, setting it to
    /// null, or removing the image — is not a failure and raises nothing. There
    /// is no matching success event.</para></summary>
    [Parameter] public EventCallback<BnImageErrorEventArgs> OnError { get; set; }

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

    /// <inheritdoc />
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

        // THE 7.5 PROPS — appended after `src` (no renumbering), same wire,
        // same null-is-absent rule. Image-only vocabulary: neither joins the
        // style partition (the partition is the routing table for names ANY
        // node can carry; StyleAttributePartitionTests pins both as props).
        b.AddAttribute(25, "placeholderColor", PlaceholderColor);
        b.AddAttribute(26, "contentMode", ContentMode.ToWireValue());

        // Attach-only-when-subscribed (the BnScroll.OnScroll pattern): an
        // unwired BnImage emits no `error` attach, so its wire shape is
        // byte-identical to pre-7.5 — the un-styled invariant, for events.
        if (OnError.HasDelegate)
            b.AddAttribute(27, "onerror", OnError);

        // No AddContent: a leaf has no ChildContent (BnView/BnScroll use 100).

        b.CloseElement();
    }
}
