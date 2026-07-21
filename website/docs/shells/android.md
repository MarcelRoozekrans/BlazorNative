---
id: android
title: The Android shell
sidebar_label: Android
sidebar_position: 1
---

# The Android shell

**The Android shell is the template.** There is no separate setup procedure: `dotnet new
blazornative` hands you a runnable Gradle tree with the shell already in it, wired to your
app. This page tells you what is in that tree, what is yours to change, and what is
library code you are not meant to touch.

If you have not generated an app yet, start at [Quick start](../getting-started/quick-start.md).

## What you get

```
MyApp/
├── MyApp.csproj              your app — pages, the manifest, the publish head
├── AppPages.cs               the page manifest: one row per page
├── BnStarterPage.razor       your first screen, and the "/" route
├── global.json               pins the SDK feature band the ILC host relies on
├── build/
│   └── BionicNativeAot.targets   the NDK cross-compile shim
└── android/                  the shell — a normal Gradle project
    ├── build.gradle.kts
    ├── gradlew  gradlew.bat  gradle/wrapper/…
    └── src/
        ├── main/kotlin/io/blazornative/jni/     the JNA bindings + frame adapter
        └── androidMain/kotlin/io/blazornative/shell/   MainActivity, WidgetMapper, YogaLayout
```

## The order is always publish, then gradle

Your .NET app *is* the native library the shell loads. Gradle copies the published `.so`
into `jniLibs` per ABI; it does not build it.

```bash
dotnet publish . -c Release -r linux-bionic-x64      # the emulator's ABI
cd android && ./gradlew installDebug
```

Gradle **fails fast and names the exact publish command** when a `.so` is missing, rather
than assembling an APK around stale native assets. If you publish somewhere other than the
default, point it at the tree:

```bash
./gradlew assembleDebug -PappPubRoot=<path to bin/Release/net10.0>
```

## What is yours to change

### The app identity

`android/build.gradle.kts` carries an example `namespace` and `applicationId`. **Both are
yours.** They are separate identities from the shell's Kotlin package — AGP's `namespace`
(which owns `R` and `BuildConfig`) and your `applicationId` (which owns your listing on the
device) have nothing to do with `io.blazornative.shell`, which is why the shell sources keep
their own package while your app is your own.

The display name lives in `android/src/androidMain/AndroidManifest.xml`.

### The launcher page

The shell boots into the component registered for the `"/"` route — and that comes from your
manifest, not from a shell edit. `AppPages.All` in `AppPages.cs` registers your starter page at
`BlazorNativeApp.DefaultRoute` (`"/"`), and the generated deep-link map (below) carries that `/`
row, so the shell mounts it on a normal launch.

`MainActivity.kt` also holds a hard-coded `?: "BnStarterPage"` **fallback** — the name it mounts
only if the generated resource is missing or malformed. `dotnet new` rewrites that literal to your
starter page's name for you, and a drift test in the reference repo pins it against `AppPages.All`,
so a renamed default page cannot boot a resource-less app into the wrong screen. You do not normally
touch it.

### Deep links

The shell ships a custom-scheme intent filter (a custom scheme needs no domain
verification; `https` App Links are more work and are yours to add). Routed pages reach it
through a map in `res/raw/blazornative_routes.json`:

```bash
adb shell am start -a android.intent.action.VIEW -d "blazornative://about"
```

:::tip The map is generated — nothing to hand-edit

`res/raw/blazornative_routes.json` is **generated from `AppPages.All` at build time**
(`BlazorNative.RouteGen` parses your `Routed<T>(route, name)` rows and emits the resource;
`MainActivity` reads it at Intent-parse time, *before* the native library loads). It cannot drift
from your pages — add a routed row and the deep link resolves. This was the last place a page lived
twice; since v0.3.0 the mount registry, the route table, **and** this map are all views over that
one array.

The URI **scheme** (`blazornative://`) is yours to change for a production app — it lives in two
paired places, `AndroidManifest.xml`'s `<data android:scheme="…"/>` and `MainActivity`'s
`DEEP_LINK_SCHEME`. Two apps that both ship `blazornative://` collide on one device (Android shows a
disambiguation), so pick your own before you publish.

:::

## Capabilities and permissions

The template `AndroidManifest.xml` **pre-declares everything the shell's capabilities need**, with a
comment on each entry. There is nothing to hand-add to *use* a capability — the shell is copied
source in a single app module (no library manifest-merge yet), so the manifest you receive already
carries:

| Capability | Manifest entry (pre-declared) | Runtime prompt |
|---|---|---|
| Networking (`BnImage`, `HttpClient`) | `uses-permission INTERNET` | none |
| Local notifications | `uses-permission POST_NOTIFICATIONS` + the publisher `<receiver>` | Android 13+ runtime, requested by the shell |
| Biometrics + OS-key-bound secure storage | `uses-permission USE_BIOMETRIC` | none (normal permission) |
| Camera capture | the `ACTION_IMAGE_CAPTURE` `<queries>` + the `FileProvider` `<provider>` (`res/xml/file_paths.xml`) | none — the system camera app owns the sensor |

The camera `FileProvider` authority is `${applicationId}.fileprovider` — AGP substitutes **your**
`applicationId` at build and the shell reads `packageName` at runtime, so it needs no editing.

The inverse is the only thing to mind: because everything is pre-declared, an app that never
authenticates still *ships* `USE_BIOMETRIC`. Each entry carries a "Remove this only if your app
never…" comment — **trim the permissions your app does not use before a store submission.**

## What the NDK shim does

.NET ships no `linux-bionic-*` ILCompiler packages, so a stock `dotnet publish` cannot
produce an Android-native binary at all. `build/BionicNativeAot.targets` is the vendored
workaround: it turns on the runtime-pack bypass and hooks the NDK's linker in, which is what
makes the `.so` come out of a Windows or Linux machine without an Android toolchain in the
.NET SDK.

**Do not delete it, and do not delete `global.json`.** The first means no bionic publish at
all; the second unpins the SDK band the ILC host relies on.

## What you are not meant to edit

Everything under `android/src/**/io/blazornative/` is **library code** — byte-identical
copies of the BlazorNative reference shell, pinned against it in the reference repository's
CI. Editing it is how you inherit a fork: your copy stops tracking upstream fixes, and the
pin that would have told you is in a repository you do not run.

If you need a capability the shell does not have, the honest path is the bridge, not a local
patch — see
[`docs/bridge-extension.md`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/bridge-extension.md).

## The ABIs

The shell builds for the 64-bit ABIs: `arm64-v8a` for devices and `x86_64` for the emulator.
Publish the matching runtime identifier — `linux-bionic-arm64` and `linux-bionic-x64`
respectively — or the APK will assemble around an ABI it has no library for.

## And iOS?

There is no iOS template, and the iOS path is genuinely more work. See
[Shells → iOS](./ios.md).
