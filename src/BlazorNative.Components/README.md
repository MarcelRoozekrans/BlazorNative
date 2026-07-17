# BlazorNative.Components

The public `Bn*` component library for
[BlazorNative](https://marcelroozekrans.github.io/BlazorNative/) apps: `BnView`,
`BnText`, `BnButton`, `BnInput`, `BnRow`/`BnColumn`, `BnScroll`, `BnImage`, `BnList`,
form controls, `BnModal`, and `BnTheme` — sealed, AOT/trim-compatible Blazor
components that render to real native widgets through the BlazorNative renderer.

## Where it sits

**Your app → `BlazorNative.Components`** → `BlazorNative.Renderer` →
`BlazorNative.Runtime` → the native shells. This is the package app authors write
against — everything below it is plumbing.

## What you write

```razor
<BnColumn Padding="16">
    <BnText Text="Hello native world" />
    <BnButton Label="Tap" OnClick="@(() => _count++)" />
    <BnText Text="@($"Taps: {_count}")" />
</BnColumn>
```

Plain `.razor` authoring, flexbox layout via Yoga, identical computed frames on the
Android and iOS shells.

## Status

Preview (`1.0.0-preview.N`): BlazorNative is a proof of concept heading toward a
stable release. The ABI and component surface may still move between previews.

License: MIT · Docs: <https://marcelroozekrans.github.io/BlazorNative/>
