---
id: installation
title: Installation
sidebar_label: Installation
sidebar_position: 1
---

# Installation

## Before you start — the template is not on nuget.org yet

:::warning The one thing that will not work yet

`dotnet new install BlazorNative.Templates` **will fail today**, and it is not your setup.
Nothing in this project is published to nuget.org until the maintainer cuts a GitHub
Release; the packages and the template are built and verified on every pull request, but
they are *inert* until then.

Until that Release exists, the way to get the template is to build it from the repository:

```bash
git clone https://github.com/MarcelRoozekrans/BlazorNative.git
cd BlazorNative
dotnet pack templates/BlazorNative.Templates -c Release -o ./artifacts/packages
dotnet new install ./artifacts/packages/BlazorNative.Templates.*.nupkg
```

:::

Once the template is published, installing it is one command — and note there is **no
version on it**, deliberately. The current version lives on nuget.org, which is the only
place that knows it:

```bash
dotnet new install BlazorNative.Templates
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

If the template is installed, that lists it. If it does not, the install did not take —
see the warning at the top of this page before assuming your machine is at fault.

Next: [Quick start](./quick-start.md).
