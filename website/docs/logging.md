---
id: logging
title: Logging
sidebar_label: Logging
---

# Logging

BlazorNative routes its diagnostics through **one level-gated seam** that is **quiet by
default** and consistent across the .NET runtime and both native shells. In a Release
build you get **warnings and errors only**; the boot narration, per-frame tracing and
developer detail are suppressed unless you turn the level up.

You never see any of this on screen — there is no on-device console. Logging is for you,
at a terminal, with the device or simulator attached.

## The levels

The managed enum is [`BlazorNative.Core.BnLogLevel`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/src/BlazorNative.Core/BnLog.cs);
the Android (`io.blazornative.jni.BnLogLevel`) and iOS (`BnLogLevel`) shells mirror the
same ordinals byte-for-byte, because the level crosses the ABI at boot.

| Level | Ships in Release? | What it carries |
|-------|-------------------|-----------------|
| `Error` | yes | A fault. |
| `Warn` | yes — **the default** | Something the author asked for was dropped, or a host contract was bent. |
| `Info` | no | Success narration (boot lines such as "native init ok", "mounted …"). |
| `Debug` | no | Developer detail, including full exception stacks. |
| `Verbose` | no | Per-frame / per-patch tracing. |

Setting the threshold to a level emits that level **and everything more severe**. The
default is `Warn` — deliberately a *runtime* default, not a `#if DEBUG` switch, so a
Release build can be asked for one verbose session without a rebuild and without the two
configurations' code paths diverging.

A name that is unrecognised or absent resolves to the default (`Warn`) — a typo never
silently turns logging **off**.

## Raising the level

You can raise verbosity three ways; they do not conflict, and the **last writer at boot
wins** (a per-launch override beats the app's declared default beats the runtime
default).

### From managed code — `BnLog.Level`

`BnLog.Level` is a public setter on `BlazorNative.Core`. Set it anywhere — most naturally
from your app's `BlazorNativeApp.ConfigureServices` callback — when a consumer wants
verbosity for a session without touching the shell:

```csharp
using BlazorNative.Core;

// e.g. in your BlazorNativeApp.ConfigureServices override:
BnLog.Level = BnLogLevel.Debug;
```

Assigning `BnLogLevel.Unset` or an out-of-range value resolves back to the default
(`Warn`). This is the same threshold the runtime's own renderer logging flows through, so
raising it turns up the framework's diagnostics as a whole.

### On Android — manifest meta-data or an adb Intent extra

**App-wide default (ships with the APK).** Add a `<meta-data>` to your
`AndroidManifest.xml`, on the `<application>` (or on the `<activity>`, which takes
precedence):

```xml
<meta-data android:name="io.blazornative.logLevel" android:value="Debug" />
```

The value is a `BnLogLevel` **name** — `Error`, `Warn`, `Info`, `Debug` or `Verbose`
(case-insensitive). An unparseable value falls back to the quiet default.

**One launch only (no rebuild).** Pass the `EXTRA_LOG_LEVEL` Intent extra, which has the
highest precedence:

```bash
adb shell am start -e io.blazornative.shell.EXTRA_LOG_LEVEL Debug \
  -n com.example.myapp/io.blazornative.shell.MainActivity
```

Framework lines reach **logcat** through the shell's stderr pump, under the tag
`BlazorNative/<category>`. The **shell's own** narration — the `[BOOT]` lines, the
`[deep-link]` routes and the bridge's `navigate` — is written directly by the Kotlin
shell under the plain `BlazorNative` tag, and since
[#200](https://github.com/MarcelRoozekrans/BlazorNative/issues/200) it obeys the **same
threshold**: it is `Info`, so it is silent at the default `Warn` and returns as soon as you
raise the level with either knob above. Filter for both:

```bash
adb logcat | grep BlazorNative
```

### On iOS — Info.plist or the `BN_LOG_LEVEL` environment variable

**App-wide default (ships with the build).** Add a string to your `Info.plist`:

```xml
<key>io.blazornative.logLevel</key>
<string>Debug</string>
```

**One launch only (no rebuild).** Set the `BN_LOG_LEVEL` environment variable — from your
Xcode scheme's *Run → Arguments → Environment Variables*, or when launching a simulator:

```bash
xcrun simctl launch --console --env BN_LOG_LEVEL=Debug booted com.example.myapp
```

`BN_LOG_LEVEL` takes precedence over the Info.plist value. The iOS shell logs through
`os_log` / `Logger` under the subsystem `io.blazornative`, so you can stream and filter
the whole framework from a terminal:

```bash
log stream --predicate 'subsystem == "io.blazornative"'
```

Note that iOS **redacts message payloads by default** in logs collected off-device
(`<private>`); only compile-time-constant text (and the framework version) is written in
the clear. This is deliberate — it keeps app, user and exception data out of the unified
log unless a call site explicitly marks it safe.

## What a line looks like

The default sink tags the level into the text so the shells' stderr pumps can map a line
back onto `android.util.Log` / `os_log`:

```
[BN|E|renderer] <message>
```

The single character after `BN|` is the level (`E`/`W`/`I`/`D`/`V`); the token after it is
the category.

## Custom sinks

For structured logging (Serilog, OpenTelemetry, JSON-per-line), assign
`BnLog.Sink` — an `Action<BnLogLevel, string, string>` (level, category, message). It is
the narrowest extension point that works; the framework does not host an
`ILoggerProvider` ecosystem. A sink that throws is swallowed, because a logger that faults
its caller is worse than a quiet one.

```csharp
BnLog.Sink = (level, category, message) =>
    MyLogger.Write(level.ToString(), category, message);
```
