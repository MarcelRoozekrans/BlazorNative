---
id: overview
title: Components
sidebar_label: Overview
sidebar_position: 1
---

# Components

Everything you put on a page is a `Bn*` component. **There is no DOM** — `<div>`, `<span>`
and `<p>` are not widgets and render nothing. The `BlazorNative.Analyzers` package catches
the common cases at compile time; see [Analyzer rules](../analyzers.md).

## They render natively, not identically

A `BnButton` is a real Android `Button` and a real `UIButton`. It follows that the two do
not look the same, and in a few places they are not even the same *class* of control —
iOS has no native checkbox, so `BnCheckbox` maps to the control iOS actually offers.

This is a deliberate choice, and it is the other half of
[the parity contract](../architecture/parity.md): **geometry is identical, chrome is the
platform's.** A component that rendered identically everywhere would be a component that
looks native nowhere.

## The surface, by what it is for

| Group | Components | For |
|---|---|---|
| **Layout** | `BnView`, `BnRow`, `BnColumn` | The flex surface, and the two presets over it. Start here — see [Layout and Yoga](../architecture/layout-and-yoga.md). |
| **Scrolling** | `BnScroll` | A viewport, and a flex *item*. Give it a definite height and compose the content inside. |
| **Text and media** | `BnText`, `BnImage`, `BnActivityIndicator` | Natively measured leaves. A long label's real wrapped height drives its row. |
| **Input** | `BnButton`, `BnInput`, `BnCheckbox`, `BnSwitch`, `BnSlider`, `BnPicker` | Real platform controls, `@bind`-able. |
| **Collections** | `BnList` | A virtualized, generic list. |
| **Overlay** | `BnModal` | An overlay inside the existing root — not a native dialog window. |
| **Theming** | `BnTheme` | A cascaded record. Toggling produces a new instance, so consumers re-render. |

The flex enums — `FlexDirection`, `FlexJustify`, `FlexAlign`, `FlexWrap`, `FlexPosition` —
and `ImageContentMode` are typed parameter vocabularies rather than components.

## The reference

**The per-component reference is generated from the components' own XML documentation**, at
build time, from the published assembly — it is not written by hand and not committed to
the repository.

That is on purpose. A hand-written page of parameter descriptions would be a second copy of
something that already exists, correct, six inches from the code it describes; its only
relationship to the truth would be that somebody remembered. The `///` next to the property
is the one home for what that property does, and this site prints it rather than restating
it.

Two consequences worth knowing as a reader:

- **What you read in the reference is what the compiler sees.** If a parameter's
  description is wrong, the fix is a source change in the component, not an edit here.
- **The reference cannot quietly go missing.** The generator that builds it once produced a
  reference with *zero components in it* and reported success — so the set of components it
  emits is now counted against the set the assembly actually contains, and a mismatch fails
  the build in either direction.

## The rules that bite

- **Components referenced in markup must be `public`.**
- **`@bind` and `@onclick` need `@using Microsoft.AspNetCore.Components.Web` in scope.**
  `_Imports.razor` has it, and it is load-bearing: without it, the Razor compiler emits them
  as *literal markup* — no diagnostics, a green build, and a control that does nothing.
- **`BnScroll` is not a flex container.** See
  [Layout and Yoga](../architecture/layout-and-yoga.md#bnscroll--the-rule-that-catches-everybody).
