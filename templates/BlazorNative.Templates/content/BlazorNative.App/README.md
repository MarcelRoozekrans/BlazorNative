# MyBlazorNativeApp

A [BlazorNative](https://github.com/MarcelRoozekrans/BlazorNative) app: Blazor components
compiled with NativeAOT and rendered as **real native widgets**. No WebView.

## The loop

Your .NET app compiles to a native library; the Android shell in `android/` loads it and
renders its frames. So the order is always **publish, then gradle**:

```bash
# 1. the native library, per ABI
dotnet publish . -c Release -r linux-bionic-x64      # the emulator's ABI
dotnet publish . -c Release -r linux-bionic-arm64    # real devices

# 2. the APK
cd android
./gradlew assembleDebug
./gradlew installDebug        # onto a running emulator/device
```

`gradlew` fails fast and names the exact publish command if a `.so` is missing — it will
not build you an APK with stale native assets.

Publishing somewhere else? Point gradle at the tree:
`./gradlew assembleDebug -PappPubRoot=<path to bin/Release/net10.0>`.

### The desktop dev loop

`dotnet publish . -c Release -r win-x64` produces `BlazorNative.Runtime.dll`, which a
host-JVM process can load through JNA without an emulator in the way.

## Adding a page

**A page is declared ONCE** — one row in `AppPages.All` (`AppPages.cs`). The runtime's
mount registry and route table are *derived views* of that array, so they cannot drift
from it.

```csharp
BlazorNativePage.Routed<BnAboutPage>("/about", "BnAboutPage"),   // route + mount name
BlazorNativePage.Named<SomeScreen>("SomeScreen"),                // mount name only
```

**The one exception, and it matters:** Android's `DEEP_LINK_COMPONENTS`
(`android/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt`) is a **hand-written
mirror** of the routed rows. It cannot be derived — it is read at Intent-parse time, before
the native library loads. Add a routed page, add its pair there. A mirror that drifts fails
no compile and no test: the deep link just opens the wrong screen, silently.

Test a deep link with:
`adb shell am start -a android.intent.action.VIEW -d "blazornative://about"`

## Writing pages

Only `Bn*` components render — there is no DOM, so `<div>`/`<span>` are not widgets. The
`BlazorNative.Analyzers` package (referenced already) catches the common mistakes at
compile time. Keep `@using Microsoft.AspNetCore.Components.Web` in `_Imports.razor`:
without it, `@bind`/`@onclick` compile to literal markup with **no diagnostics** and are
silently dead.

## Two things not to delete

- **`<TrimmerRootAssembly Include="$(AssemblyName)" />`** in the csproj. Without it ILC
  trims your entire app — green build, zero trim warnings, and `rc 1` at first mount. Its
  comment explains the whole failure signature.
- **`build/BionicNativeAot.targets`** and **`global.json`**. The first is the NDK
  cross-compile shim (no bionic publish without it); the second pins the SDK feature band
  the ILC host relies on.

## The shell sources

Everything under `android/src/**/io/blazornative/` is **library code** — byte-identical
copies of the BlazorNative reference shell, which is why it stays in the
`io.blazornative.shell` package while your app id is your own (AGP's `namespace` and
`applicationId` are separate identities from a source package). You are not meant to edit
it.

## iOS

**There is no iOS template.** iOS is a manual procedure against the reference shell, and it
is more work than the Android path — simulator only for now. See
[Adding an iOS shell](https://marcelroozekrans.github.io/BlazorNative/docs/shells/ios)
in the BlazorNative docs.

## Requirements

- .NET SDK 10.0.3xx (`global.json` pins the band)
- JDK 17, an Android SDK, and NDK `26.3.11579264`
