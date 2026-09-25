---
id: safe-area
title: Safe-area insets
sidebar_label: Safe area
---

# Safe-area insets

On a notched phone, part of the screen belongs to the system. The Dynamic Island on an
iPhone, a display cutout on Android, the status bar, the home indicator — the OS draws
there and **takes the touches there too**. A view laid out at `y=0` under one of those
regions is drawn perfectly and is completely untappable: no highlight, no error, nothing
in the logs. That was [#338](https://github.com/MarcelRoozekrans/BlazorNative/issues/338).

`BnSafeArea` keeps its content clear of all four of those regions, on both shells.

## It is opt-in

Like Flutter's `SafeArea`, React Native's `SafeAreaView` and Compose's
`windowInsetsPadding` — and unlike SwiftUI, which insets by default — `BnSafeArea` does
nothing until you wrap something in it. An automatic root inset was considered and
rejected: it would force every page to be inset whether it wants to be or not, including
the layout and scroll examples on this site, which intentionally start at `(0, 0)` to
show real Yoga coordinates.

Opt-in only works if the correct thing is the thing you copy, so wrap the root of a new
page:

```razor bn-sample=component
<BnSafeArea>
    <BnView BackgroundColor="#FFFFFF" Padding="16">
        <BnText Text="Hello from BlazorNative" FontSize="24" />
        <BnButton Label="Tap me" OnClick="OnTap" />
    </BnView>
</BnSafeArea>

@code {
    private void OnTap() { /* … */ }
}
```

The `dotnet new blazornative` starter page wraps its content the same way — copy it from
there if you want the whole file.

## Per-edge modes

Each edge — `TopEdge`, `RightEdge`, `BottomEdge`, `LeftEdge` — takes a `BnSafeAreaEdge`,
independently of the others:

| Mode | Effect |
|---|---|
| `Off` | The reported inset is ignored on that edge. Your own padding for that edge, if any, still applies. The edge-to-edge case — a full-bleed hero image drawn under the status bar. |
| `Additive` (default) | The reported inset is **added** to your own padding for that edge. |
| `Maximum` | Whichever is larger: the reported inset, or your own padding for that edge — React Native's rule, `Math.max(insets.bottom, 16)` written once here instead of by every author. |

```razor bn-sample=component
<BnSafeArea TopEdge="BnSafeAreaEdge.Off" BottomEdge="BnSafeAreaEdge.Maximum">
    …
</BnSafeArea>
```

`BnSafeArea` derives from the same `BnLayoutContainer` base every other container uses, so
`Padding`, `PaddingTop`/`Right`/`Bottom`/`Left`, `Justify`, `Align`, `Wrap` and `Gap` are
all available on it too — the per-edge padding you set there is exactly "your own padding"
in the table above.

## Nesting is safe

Wrapping a page in `BnSafeArea` and then wrapping a section inside it in another
`BnSafeArea` does not pad twice. Only the outermost instance applies the inset; a nested
one sees it as already consumed — Flutter's rule, followed for the same reason: without
it, the inset would be applied once per ancestor, which is subtle and only visible on a
real notched device.

## Live

Rotation, for one — the reported insets can change while the app is running, and
`BnSafeArea` re-lays-out when they do. There is no need to subscribe to anything yourself.

## What it does not do

- **No `margin` mode.** Padding covers the case #338 needed; a margin mode can be added
  later without breaking anything, so it is left out until something needs it.
- **It does not avoid the keyboard.** The insets are the system bars and the display cutout
  — Android's `systemBars()` and `displayCutout()`, iOS's `safeAreaInsets` — and neither shell
  reports the on-screen keyboard as one.
- **The very first frame renders at zero insets.** Both shells mount before their first
  layout pass, so the first frame cannot yet know the insets — content briefly renders
  unpadded, then re-renders once the real values arrive. React Native has the same gap
  and documents it the same way: "insets are not updated synchronously, so expect a
  slight delay."
