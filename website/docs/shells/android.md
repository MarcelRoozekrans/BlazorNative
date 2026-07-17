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

The shell boots into a component **by name**, and the name it falls back to is your starter
page's. That name is one of a pair:

- `AppPages.All` in `AppPages.cs` — the manifest that registers the page on the .NET side.
- `MainActivity.kt`'s fallback — the name the shell mounts when no deep link or extra says
  otherwise.

Rename the page and both need the new name. **A mismatch is not a compile error** — the
mount fails at runtime.

### Deep links

The shell ships a custom-scheme intent filter (a custom scheme needs no domain
verification; `https` App Links are more work and are yours to add). Routed pages reach it
through a map in `MainActivity.kt`:

```bash
adb shell am start -a android.intent.action.VIEW -d "blazornative://about"
```

:::caution The one place that does not derive

`DEEP_LINK_COMPONENTS` is a **hand-written mirror** of the routed rows in `AppPages.All`. It
cannot be derived from them: it is read at Intent-parse time, *before* the native library is
loaded. Add a routed page, add its pair here.

Everything else about a page is derived from `AppPages.All` — the mount registry and the
route table are views over that one array, so they cannot drift from it. This map can, and
when it does, nothing fails: the deep link opens the wrong screen, silently.

:::

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
