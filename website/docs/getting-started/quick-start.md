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
the template is published on nuget.org (v0.1.0), and `dotnet new install
BlazorNative.Templates` is the one-line install.

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

## 3. The desktop loop, no emulator

```bash
dotnet publish . -c Release -r win-x64
```

That produces a `BlazorNative.Runtime.dll` a host JVM process can load through JNA — the
same C-ABI, the same patches, the same widget tree, without an emulator in the way. It is
the fastest feedback surface this project has.

## Adding a page

**A page is declared once** — one row in `AppPages.All`. The runtime's mount registry and
route table are *derived views* of that array, so they cannot drift from it:

```csharp
BlazorNativePage.Routed<BnAboutPage>("/about", "BnAboutPage"),   // route + mount name
BlazorNativePage.Named<SomeScreen>("SomeScreen"),                // mount name only
```

:::caution The one place that does not derive

Android's `DEEP_LINK_COMPONENTS` map in `MainActivity.kt` is a **hand-written mirror** of
the routed rows. It cannot be derived — it is read at Intent-parse time, *before* the
native library is loaded. Add a routed page, add its pair there.

A mirror that drifts fails no compile and no test: the deep link just opens the wrong
screen, silently.

:::

## Writing pages — the subset that actually renders

**Only `Bn*` components render.** There is no DOM here: `<div>`, `<span>` and `<p>` are not
widgets and will not appear. The `BlazorNative.Analyzers` package is referenced for you and
catches the common mistakes at compile time — see [Analyzer rules](../analyzers.md).

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
