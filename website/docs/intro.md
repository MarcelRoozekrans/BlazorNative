---
id: intro
title: What BlazorNative is
sidebar_label: Introduction
sidebar_position: 1
---

# What BlazorNative is

BlazorNative runs a **Blazor application as a native mobile app** — no WebView, no
JavaScript, no WebAssembly. Your components render to real `TextView`s and `UILabel`s.

It is not React Native, Flutter, or MAUI, and it does not embed a browser. The whole idea
fits in three steps:

1. **Your UI and logic compile ahead-of-time** into a platform-native shared library — a
   .NET NativeAOT binary, one per platform and ABI.
2. **A headless renderer drives the Blazor render tree** and emits *typed struct patches*
   (create node, set style, replace text, …) through a C-ABI frame callback. There is no
   JSON and no interpreter on the frame path.
3. **A thin native shell** — Kotlin on Android, Swift/UIKit on iOS — loads that library,
   reads the patches, and builds real platform widgets, laid out by
   [Yoga](https://www.yogalayout.dev/), Facebook's C++ flexbox engine.

The result is that the same C# produces **the same frames on both platforms** — a claim
this project asserts with a test rather than prose. See
[the parity contract](./architecture/parity.md).

```razor
<BnColumn Gap="16" Padding="16">
  <BnRow Justify="FlexJustify.SpaceBetween" Align="FlexAlign.Center">
    <BnText Text="Left" />
    <BnText Text="Right" />
  </BnRow>
  <BnButton Label="@($"Tapped {taps} time(s)")" OnClick="OnTap" />
</BnColumn>

@code {
    private int taps;
    private void OnTap() => taps++;
}
```

On Android that is a real `TextView` and a real `Button` inside a layout-suppressed frame
container, each placed at the coordinates Yoga computed. On iOS it is a `UILabel` and a
`UIButton` — placed at *the same* coordinates.

## Status — read this before you build anything on it

:::warning Pre-release proof of concept

The API surface is **unstable and changes without notice**. This is a proof of concept, not
a product. iOS is **simulator-only** — real-device iOS needs code signing and provisioning
that this project does not have yet.

:::

This site does not tell you which milestone is finished, how many tests pass, or which
version is current — **those facts have a home, and it is not here.** The repository's
front door and its CI badge are that home:

- [The repository README](https://github.com/MarcelRoozekrans/BlazorNative#readme) — status,
  the test surface, and the roadmap.
- [The CI workflow](https://github.com/MarcelRoozekrans/BlazorNative/actions/workflows/ci.yml)
  — every count this project claims is asserted there on every pull request. If you want to
  know how many tests pass, that badge is the answer and this page would only be a stale
  copy of it.

## Where to go next

| If you want to… | Go to |
|---|---|
| Build and run an app | [Installation](./getting-started/installation.md) → [Quick start](./getting-started/quick-start.md) |
| Understand how it works | [Architecture overview](./architecture/overview.md) |
| Look up a component's parameters | [Components](./components/overview.md) |
| Add an iOS shell | [Shells → iOS](./shells/ios.md) |
| Know what the analyzer just told you | [Analyzer rules](./analyzers.md) |
