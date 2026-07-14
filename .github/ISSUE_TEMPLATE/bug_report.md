---
name: Bug report
about: Something broken in BlazorNative
title: ''
labels: ''
assignees: ''
---

## Describe the bug

A clear, concise description of what is broken.

## To reproduce

1. Component / markup involved (minimal repro preferred):
2. Steps:
3. Command(s) run:

## Expected behavior

What you expected to happen.

## Actual behavior

What actually happened. Include the relevant output — patch stream, logcat lines
(`adb logcat -s BlazorNative`), test failure text, or publish errors.

## Environment

- OS (host): e.g. Windows 11
- .NET SDK: output of `dotnet --version` (pinned by `global.json`)
- JDK: e.g. Temurin 21
- Android NDK (if publishing for Android): e.g. 26.3.11579264
- Xcode + simulator (if reproducing on iOS): e.g. Xcode 16, iPhone 16 sim
- Device/emulator (if applicable): e.g. AVD Pixel 6, API 34, x86_64

## Surface where the bug reproduces

- [ ] JVM dev loop (`gradlew testDebugUnitTest` against the win-x64 dll)
- [ ] Android emulator/device (`gradlew connectedAndroidTest` / demo app)
- [ ] iOS simulator (`xcodebuild test` — the Swift/UIKit shell; simulator-only)
- [ ] .NET test suite (`dotnet test`)
- [ ] NativeAOT publish step

If a layout bug reproduces on **one** platform and not the other, say so
explicitly — both shells compute frames from the same Yoga tree, so a divergence
between them is a much more serious bug than a wrong frame on both.
