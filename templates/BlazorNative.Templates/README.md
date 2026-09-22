# BlazorNative.Templates

`dotnet new` templates for [BlazorNative](https://marcelroozekrans.github.io/BlazorNative/) —
Blazor components compiled with NativeAOT and rendered as **real native widgets**, with no
WebView anywhere.

## Install

```bash
dotnet new install BlazorNative.Templates
```

## Use

```bash
dotnet new blazornative -n MyApp
cd MyApp
```

| Option | Default | What it is |
|---|---|---|
| `-n`, `--name` | `BlazorNativeApp` | The app name — the csproj, the root namespace, the gradle project. |
| `--applicationId` | `com.example.<name>` | The Android `applicationId` **and** the AGP `namespace`. Not the shell's Kotlin package, which stays `io.blazornative.shell` by design. |
| `--BlazorNativeVersion` | the version this pack shipped at | The BlazorNative package version the generated app references. |

## What you get

```
MyApp/
├── MyApp.csproj            the NativeAOT publish head
├── AppPages.cs             your app's page manifest (declare a page ONCE, here)
├── BnStarterPage.razor      the "/" page
├── _Imports.razor
├── global.json             the SDK feature band the ILC host relies on
├── build/                  the vendored NDK cross-compile shim (no bionic publish without it)
└── android/                a runnable Android/Gradle shell
```

> **This template ships an Android shell only — there is no iOS tree in it, and there is no
> iOS template.** `dotnet new blazornative` gives you `android/` and nothing else; the iOS
> shell is a manual procedure against the reference implementation in
> `src/BlazorNative.Apple/`, written up in the docs under **Shells → iOS**. Your .NET app —
> pages, manifest, publish head — is the same on both platforms; it is only the shell tree
> that the template cannot give you.

Then:

```bash
dotnet publish . -c Release -r linux-bionic-x64     # the emulator's ABI
cd android && ./gradlew assembleDebug
```

The generated app's own `README.md` carries the full loop. The
[docs site](https://marcelroozekrans.github.io/BlazorNative/) carries the rest —
getting started, the component reference, the parity contract, and the iOS story
(manual, against the reference shell — there is no iOS template).

## Requirements

- .NET SDK 10.0.3xx (the generated `global.json` pins the band)
- JDK 17, an Android SDK, and NDK `26.3.11579264` for the bionic publishes

## License

MIT
