---
id: installation
title: Installation
sidebar_label: Installation
sidebar_position: 1
---

# Installation

## Install the template

BlazorNative is **published on nuget.org** — **v0.1.0**, a stable release, so nothing here
needs `--prerelease`. Installing the template is one command:

```bash
dotnet new install BlazorNative.Templates
```

There is deliberately **no version on that command** — the current version lives on
nuget.org, which is the only place that knows it. To pin one, append `::0.1.0`.

Then go to [Quick start](./quick-start.md) to create and run an app.

### Consuming the packages directly

If you are wiring BlazorNative into an existing project rather than using the template, the
**seven packages** are on nuget.org, all at the same version (no `--prerelease`):

| Package | What it is |
|---|---|
| `BlazorNative.Runtime` | NativeAOT composition root + the C-ABI export surface |
| `BlazorNative.Components` | the `Bn*` component library |
| `BlazorNative.Renderer` | the headless renderer + patch model |
| `BlazorNative.Core` | the `IMobileBridge` contract + bridge implementations |
| `BlazorNative.Http` | `HttpClient` over the host fetch bridge |
| `BlazorNative.Device` | device-capability façades (geolocation, notifications, biometrics, secure storage, camera) |
| `BlazorNative.Analyzers` | the compile-time analyzers |

```bash
dotnet add package BlazorNative.Components
dotnet add package BlazorNative.Runtime
# …and the others as you need them
```

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
