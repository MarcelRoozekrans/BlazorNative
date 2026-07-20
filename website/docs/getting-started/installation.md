---
id: installation
title: Installation
sidebar_label: Installation
sidebar_position: 1
---

# Installation

## Install the template

BlazorNative is **published on nuget.org** as a stable release, so nothing here needs
`--prerelease`. Installing the template is one command:

```bash
dotnet new install BlazorNative.Templates
```

There is deliberately **no version on that command** — the current version lives on
nuget.org, which is the only place that knows it. To pin a specific one, append
`::<version>` using a version listed on nuget.org (for example `::x.y.z`).

Then go to [Quick start](./quick-start.md) to create and run an app.

### Consuming the packages directly

If you are wiring BlazorNative into an existing project rather than using the template, a
`dotnet new` app references only **three packages directly**. Everything else arrives
**transitively** through `BlazorNative.Runtime`, so you do not add it yourself:

| Package | What it is | How you get it |
|---|---|---|
| `BlazorNative.Runtime` | NativeAOT composition root + the C-ABI export surface | **direct** |
| `BlazorNative.Components` | the `Bn*` component library | **direct** |
| `BlazorNative.Analyzers` | the compile-time analyzers | **direct** |
| `BlazorNative.Renderer` | the headless renderer + patch model | transitive (via Runtime) |
| `BlazorNative.Core` | the `IMobileBridge` contract + bridge implementations | transitive (via Runtime) |
| `BlazorNative.Http` | `HttpClient` over the host fetch bridge | transitive (via Runtime) |
| `BlazorNative.Device` | device-capability façades (geolocation, notifications, biometrics, secure storage, camera) | transitive (via Runtime) |

So the whole requirement is three commands — no version needed, nuget.org supplies the
current one:

```bash
dotnet add package BlazorNative.Runtime
dotnet add package BlazorNative.Components
dotnet add package BlazorNative.Analyzers
```

`Renderer`, `Core`, `Http` and `Device` then resolve automatically as dependencies of
`Runtime`; you can confirm the full closure with
`dotnet list package --include-transitive`.

### Building from source instead

Prefer to build the template locally — to hack on it, or to try an unreleased change? Clone
and pack it, then install from the local feed:

```bash
git clone https://github.com/MarcelRoozekrans/BlazorNative.git
cd BlazorNative
dotnet pack templates/BlazorNative.Templates -c Release -o ./artifacts/packages
dotnet new install ./artifacts/packages/BlazorNative.Templates.*.nupkg
```

## Prerequisites

| Tool | Purpose | Needed for |
|---|---|---|
| .NET SDK 10 | Build + the NativeAOT publish | Everything |
| Temurin JDK | Gradle / the Kotlin shell | Android |
| Android SDK + NDK | The bionic cross-compile, and the emulator | Android |
| An AVD or a device | Actually running it | Android |
| macOS + Xcode | The Swift/UIKit shell | iOS (simulator only) |

**The exact SDK band and NDK revision are pinned by the repository, not by this page.** A
generated app carries a `global.json` that pins the .NET SDK feature band the ILC host
relies on, and the Android shim names its NDK revision. Those pins are the truth; a version
number typed here would be a copy that rots the day either moves. If you want the numbers,
read [`global.json`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/global.json)
and
[`build/BionicNativeAot.targets`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/build/BionicNativeAot.targets)
in the repository — or just run the setup script below, which installs and verifies all of
them for you.

### Environment setup the tools assume

Installing the SDKs is not quite enough — three environment steps trip up a fresh machine:

- **win-x64 NativeAOT link:** the native link step needs the Visual Studio C++ toolchain,
  which it locates with `vswhere`. Run your builds from a **Developer Command Prompt for
  Visual Studio**, or make sure the VS Installer directory is on `PATH` so `vswhere` is
  reachable.
- **Gradle / Android:** point `JAVA_HOME` at a **JDK 21** (not an old JRE) — Gradle reads
  `JAVA_HOME`, and an older or JRE-only Java fails the shell build.
- **bionic / Android publish:** set `ANDROID_NDK_ROOT` to your installed NDK
  (for example `$ANDROID_HOME/ndk/<version>`) so the bionic cross-compile can find it.

### Windows: one script does the whole thing

The repository ships a prerequisite installer that pins every tool to the version the build
expects:

```powershell
powershell -ExecutionPolicy Bypass -File setup.ps1
```

## Verify

```bash
dotnet new list blazornative
```

If the template is installed, that lists it. If it does not, re-run the install command at
the top of this page before assuming your machine is at fault.

Next: [Quick start](./quick-start.md).
