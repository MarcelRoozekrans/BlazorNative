---
id: quick-start
title: Quick start
sidebar_label: Quick start
sidebar_position: 2
---

# Quick start

This is the Android path, because **the Android path is the template**. iOS is a manual
procedure against a reference shell — see [Shells → iOS](../shells/ios.md).

If `dotnet new blazornative` is not found, read [Installation](./installation.md) first —
the template is published on nuget.org, and `dotnet new install BlazorNative.Templates` is
the one-line install.

## 1. Create the app

```bash
dotnet new blazornative -n MyApp
cd MyApp
```

You get a .NET app (your pages, the page manifest, the NativeAOT publish head) **and a
runnable Android/Gradle tree with the shell already in it**.

## 2. Publish, then gradle — always in that order

Your .NET app compiles to a native library; the Android shell loads it and renders its
frames. The APK is built from the output of the publish, so the publish comes first:

```bash
# 1. the native library, for the ABI you are targeting
dotnet publish . -c Release -r linux-bionic-x64      # the emulator's ABI
dotnet publish . -c Release -r linux-bionic-arm64    # real devices

# 2. the APK
cd android
./gradlew installDebug        # onto a running emulator/device
```

`gradlew` **fails fast and names the exact publish command** if a `.so` is missing. It will
not hand you an APK with stale native assets — the failure you would otherwise get is an
app that launches and shows nothing.

## 3. The win-x64 publish

```bash
dotnet publish . -c Release -r win-x64
```

That produces the loadable native `BlazorNative.Runtime.dll` — the same C-ABI, the same
patches, the same widget tree as on device. It exposes the exact export surface a JNA host
would bind to. Note the template ships **no runnable desktop host today**: this publish
gives you the loadable `.dll`, not an app you can launch without writing a host of your own.

## Adding a page

**A page is declared once** — one row in `AppPages.All`. The runtime's mount registry and
route table are *derived views* of that array, so they cannot drift from it:

```csharp bn-sample=file
using BlazorNative.Runtime;

// MyBlazorNativeApp is dotnet new blazornative's default namespace for your app —
// AppPages.cs there carries the same namespace.
namespace MyBlazorNativeApp;

// BnAboutPage and SomeScreen stand for your own page components.
internal sealed class BnAboutPage : Microsoft.AspNetCore.Components.ComponentBase { }
internal sealed class SomeScreen : Microsoft.AspNetCore.Components.ComponentBase { }

public static class AppPages
{
    public static readonly BlazorNativePage[] All =
    [
        BlazorNativePage.Routed<BnAboutPage>("/about", "BnAboutPage"),   // route + mount name
        BlazorNativePage.Named<SomeScreen>("SomeScreen"),                // mount name only
    ];
}
```

:::tip Everything derives — including the deep-link map

Adding a **routed** page is that one row and nothing else. Android's deep-link map
(`res/raw/blazornative_routes.json`, read at Intent-parse time *before* the native library
loads) used to be a hand-written mirror in `MainActivity.kt` you had to keep in sync — the last
place a page lived twice. It is now **generated from `AppPages.All` at build time**
(`BlazorNative.RouteGen` reads your routed rows and emits the resource), so it cannot drift from
your pages and there is no shell file to edit. Add the row; the deep link works.

:::

## Testing a page

Add `BlazorNative.Testing` to your **test** project — never to the app — and a unit test can
mount a page and assert the widget tree it rendered, with no device and no emulator.
`BnTestHost.Mount<T>()` runs the real renderer and the real components; `host.Tree` is the
result, with children in the order the shells place them. `ClickAsync` and `ChangeAsync` drive
events, and when the page injects `IMobileBridge`, register a `DevHostBridge` through `Mount`'s
`configureServices`. It does not run Yoga, so it answers "what did my page render?", never
"how big is it?".

```csharp bn-sample=statements
using BlazorNative.Testing;

// Inside a test method in your test project, with your test framework's Assert:
using BnTestHost host = BnTestHost.Mount<BnButton>(
    new Dictionary<string, object?> { ["Label"] = "Save" });

BnTestNode button = host.Tree.FindAll("button")[0];
Assert.Equal("Save", button.Text);
```

## Writing pages — the subset that actually renders

**Write `Bn*` components.** There is no DOM here: `<div>`, `<span>` and `<p>` are not HTML.
The renderer maps a few element names onto native widgets and turns any other name into a
plain view, so a raw element does appear — but with none of a component's typed parameters,
and no analyzer rule flags it. The `BlazorNative.Analyzers` package is referenced for you and
catches other mistakes at compile time — see [Analyzer rules](../analyzers.md).

Two rules worth knowing before they bite:

- **Components referenced in markup must be `public`.**
- **Keep `@using Microsoft.AspNetCore.Components.Web` in `_Imports.razor`.** Without it,
  `@bind` and `@onclick` compile to *literal markup* — no diagnostics, a green build, and
  a page that is silently dead on a native shell.

## Two things not to delete

- **`<TrimmerRootAssembly Include="$(AssemblyName)" />`** in the csproj. Without it, ILC
  trims your entire app: green build, zero trim warnings, and a failed mount at runtime.
  The line carries a comment explaining the whole failure signature.
- **`build/BionicNativeAot.targets`** and **`global.json`**. The first is the NDK
  cross-compile shim — there is no bionic publish without it. The second pins the SDK
  feature band the ILC host relies on.

## Next

- [Components](../components/overview.md) — what you can put on a page.
- [Layout and Yoga](../architecture/layout-and-yoga.md) — how things get placed, and the
  one `BnScroll` rule that catches everybody.
