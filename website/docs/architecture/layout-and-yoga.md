---
id: layout-and-yoga
title: Layout and Yoga
sidebar_label: Layout and Yoga
sidebar_position: 3
---

# Layout and Yoga

**Yoga owns all placement.** You write typed flex parameters in C#; both shells hand them to
the same C++ flexbox engine and place every child at the frame it computes. No shell ever
lays out a child itself.

This is the mechanism behind [the parity contract](./parity.md): two shells that both
delegate to one engine can be held to identical numbers. Two shells that each did their own
layout could not.

## Two trees, one of them invisible

Each shell keeps a tree of real platform widgets **and a Yoga node tree beside it**. When a
style patch arrives, its name decides which tree it lands in:

| Names | Go to | Examples |
|---|---|---|
| **Layout** | the Yoga node | `flexDirection`, `justifyContent`, `width`, `margin`, `padding`, … |
| **Visual** | the view | `backgroundColor`, `color`, `fontSize`, … |

The partition is an **allow-list**, not a heuristic — and because it is hand-written in the
renderer's C#, the Android shell's Kotlin, and the iOS shell's Objective-C++, a name present
in one and missing from another would be *silently dropped*. So a drift test in the required
CI lane parses all three and asserts set-equality. **Every accepted name is a name three
parsers must implement**, which is why the accepted set is small and grows deliberately.

Containers are **layout-suppressed frame containers**: they exist to hold children, not to
arrange them. Leaves are **measured natively** through Yoga's measure callback — a long
label wraps, its real measured height comes back from the platform's own text engine, and
that height drives its row.

## The flex surface

`BnView` carries it:

| | Parameters |
|---|---|
| **Container** | `Direction` · `Justify` · `Align` · `Wrap` · `Gap` · `Padding` |
| **Item** | `AlignSelf` · `Grow` · `Shrink` · `Basis` · `Margin` |
| **Size** | `Width` · `Height` · `MinWidth` · `MaxWidth` · `MinHeight` · `MaxHeight` |
| **Position** | `Position` · `Top` · `Right` · `Bottom` · `Left` |
| **Visual** | `BackgroundColor` |

`BnRow` and `BnColumn` are thin presets over it: they forward every parameter *except*
`Direction`, because a `BnRow` **is** a row. Reach for `BnView` when the direction is
dynamic.

**There is deliberately no `BnStack`** — it would be a synonym for `BnColumn`, and two names
for one thing is a library smell on day one.

```razor
<BnColumn Gap="16" Padding="16">

  @* Grow absorbs the free space: the middle box computes the same on both platforms *@
  <BnRow Width="300" Height="100">
    <BnView Width="50" BackgroundColor="#E57373" />
    <BnView Grow="1"   BackgroundColor="#64B5F6" />
    <BnView Width="50" BackgroundColor="#81C784" />
  </BnRow>

  <BnRow Justify="FlexJustify.SpaceBetween" Align="FlexAlign.Center">
    <BnText Text="Left" />
    <BnText Text="Right" />
  </BnRow>

  @* A long label wraps, and its NATIVELY MEASURED height drives its row *@
  <BnRow Width="150">
    <BnText Text="A label long enough to wrap onto several lines." />
  </BnRow>

</BnColumn>
```

## BnScroll — the rule that catches everybody

**`BnScroll` is a flex *item*, not a flex *container*.** It has no `Direction`, `Justify`,
`Align`, `Wrap`, `Gap` or `Padding`, and that is by construction rather than oversight.

Those parameters would style the **viewport**, whose only child is a shell-synthesised
content node. `Justify="Center"` over 800dp of content in a 200dp viewport would offset it
to y = −300 and make the top of the page **permanently unreachable**. To shape the content,
compose *inside* the scroll — React Native's `contentContainerStyle`, without a second
style surface:

```razor
@* BnScroll is a VIEWPORT: give it a definite height, compose the content inside *@
<BnScroll Height="200">
  <BnColumn Gap="8">
    @foreach (var row in Rows)
    {
      <BnRow Height="80"><BnText Text="@row" /></BnRow>
    }
  </BnColumn>
</BnScroll>
```

The shells enforce the same rule at the wire, so the raw-element hatch is closed by the same
sentence.

### Give it a definite height

Use `Height`, or `Grow="1" Basis="0"` in a bounded parent.

**`Grow="1"` alone is not enough, and the failure is silent.** It leaves `flexBasis: auto`,
so the basis becomes the *content's* height, the free space goes negative, and `flexGrow`
distributes only *positive* free space — the viewport hugs its content and never scrolls.
That is exactly why CSS's `flex: 1` shorthand sets basis to `0`. The shells emit a
diagnostic when a viewport is indefinite.

## Honest boundaries

These are real limits, not omissions from this page:

- **No horizontal scroll.** Android's `ScrollView` is vertical-only; horizontal is a
  different widget class, which would have to be chosen at node creation from a
  `flexDirection` that arrives in a *later* style patch.
- **No `onScroll` / `scrollTo`**, and no scroll-offset restore across navigation. `onScroll`
  fires at 60 Hz and would be the first high-frequency producer on a wire designed for
  taps.
- **`alignContent`, `rowGap`, `columnGap`, `display`, `flex`** are accepted by nothing — no
  typed parameter, no producer.
- **`BnPicker` does not flex its children.** `Spinner` and `UIPickerView` are framework
  containers that run their own layout inside themselves. The picker node itself is placed
  correctly by its parent.
- **One file pixel = one dp/pt** for images, so a `@2x` asset renders at twice its intended
  physical size on both platforms.
