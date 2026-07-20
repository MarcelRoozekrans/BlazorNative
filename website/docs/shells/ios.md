---
id: ios
title: The iOS shell
sidebar_label: iOS
sidebar_position: 2
---

# Adding an iOS shell to a BlazorNative app

**There is no `dotnet new` template for iOS.** This is a manual procedure against a
reference implementation, and it is more work than the Android path — where `dotnet new
blazornative` gives you a runnable Gradle tree with the shell already in it. Budget an
afternoon.

**Simulator only.** The reference shell is built and tested for `iossimulator-arm64`. Real
devices need a signing story, a device RID and an `ios-build` lane that has none of those
today; it is not in this milestone.

---

## What this document is, and what it deliberately is not

**It does not transcribe the recipe.** `project.yml` links against `$(SRCROOT)/vendor/…` and
names no publish directory and no app anywhere: the link between "your .NET app" and "the
iOS shell" is a directory of frozen-name files, and the only thing that populates it today
is CI — about ninety lines of bash across two workflow steps.
There is no script and no Makefile target. (That absence is a known gap:
`scripts/stage-ios.ps1` is on the backlog precisely because it would make this recipe
executable and testable instead of prose.)

So this document **points at the live files** and tells you what to change in them. Those
files are compiled by the **`ios-build` job on every pull request, and `ios-build` is a
required check** — which means the material this guide sends you to is kept true by a gate
rather than by someone remembering to update a document. A transcription here would be a
fourth copy of a recipe that already exists twice in YAML, and it would rot the day
`ci.yml` moved.

**The executable truth is `.github/workflows/ci.yml`, job `ios-build`.** When this prose and
that job disagree, the job is right. Read its steps in order — they are commented heavily,
and they are the procedure:

| Step | What it does |
|---|---|
| `Publish iossimulator-arm64 (NativeLib=Static; assert exactly 4 IL2072)` | your app → a static archive |
| `Stage the link inputs (runtime .a + bootstrapperdll.o + support archive)` | the `vendor/` recipe |
| `Yoga: build libyoga.a` | the flexbox engine, from source |
| `Generate BnHost.xcodeproj` / `xcodebuild build-for-testing` | the Xcode side |

> **One honest warning about this guide as a whole.** No lane executes *this procedure*.
> `ios-build` proves the reference shell compiles and links; it does not prove that a reader
> following these steps arrives at a running app. Every file and line referenced below was
> verified against the tree when this was written, but line numbers drift — each citation
> names the code to search for, so use the text, not the number.

---

## 1. Copy the shell

Copy **`src/BlazorNative.Apple/BnHost/`** into your tree — **19 files**. `BnWidgetMapper.swift`
alone is ~3,500 lines.

**The shell is not packaged.** There is no `.framework`, no CocoaPod, no SwiftPM product.
The Android side has the same duplication problem and the same eventual answer (an `.aar` /
a package); until then, copying is the mechanism, and *your copy will not track upstream
fixes*. Unlike the Android template — whose Kotlin is byte-identity-pinned against the
reference in CI, so a shell fix that skips the template turns a required check red — **your
iOS copy is pinned by nothing.** Diff it against the reference when you update.

You also need **`project.yml`** (the XcodeGen spec) and its `vendor/` layout. Read it: it is
the most load-bearing file in the iOS build and it is thoroughly commented.

---

## 2. What to delete

**`BnHostTests/`** — 30 files. It is the reference shell's own XCTest surface (the fixture
image server, the widget-mapper tests, the runtime tests). Delete the directory, its target
in `project.yml`, and its scheme's test action.

**The ATS exemption — and this is the one you would not guess.** `BnHost/Info.plist`
carries:

```xml
<key>NSAppTransportSecurity</key>
<dict>
    <key>NSAllowsLocalNetworking</key>
    <true/>
</dict>
```

**That exists only so the test fixture server works.** `BnHostTests`'s image tests serve
fixtures over cleartext loopback, and `BnImageDemoTests.testCleartextLoopbackIsPermittedByATS`
is what turns the exemption from an assumption into a checked fact. It is narrow — it is
`NSAllowsLocalNetworking`, *not* `NSAllowsArbitraryLoads`, so it permits loopback,
link-local and `.local` destinations and nothing else; public-internet HTTP stays blocked.
But if you deleted `BnHostTests` above, **you have inherited an exemption you never asked
for.** Delete it too, unless you are talking to a local dev server on purpose.

**`BnYogaProbe`** (`.h`/`.mm`/`.swift`) is vestigial — a Phase 6.0 Yoga-spike artifact that
survives because it doubles as a launch smoke test. A new app does not need it. It is on the
backlog to retire on purpose; delete it if you want the smaller surface.

---

## 3. What to edit — and these are source edits, not configuration

The shell hardcodes the reference app's root component in **two places**. There is no
setting for this; you are editing the shell's source, which is the honest word for it.

| File | The line | Change it to |
|---|---|---|
| `BnHost/HostViewController.swift:60` | `try runtime.start(component: "BnDemo", os: "ios")` | your root component's registered name |
| `BnHost/BnRuntime.swift:184` | `func start(component: String = "BnDemo", os: String = "ios", apiLevel: Int32 = 0) throws` | the default — same name |

**The name must match a page your app registers.** On the .NET side that is the `name`
argument in your `AppPages.All` manifest — `BlazorNativePage.Routed<BnStarterPage>(BlazorNativeApp.DefaultRoute, "BnStarterPage")`
registers `"BnStarterPage"`. A mismatch is not a compile error: the mount fails at runtime.

> A generated Android app has exactly this seam, and it is templated: `MainActivity`'s
> `?: "BnStarterPage"` fallback is rewritten for you at `dotnet new` time, and a drift test
> pins it against `AppPages.All`. **On iOS you are the drift test.**

Also yours to change: the **bundle id and display name** in `BnHost/Info.plist` and
`project.yml` (`PRODUCT_BUNDLE_IDENTIFIER`, the target/scheme names).

**And the capability usage-description strings — rewrite the copy, keep the keys.** `BnHost/Info.plist`
carries `NSCameraUsageDescription`, `NSLocationWhenInUseUsageDescription` and
`NSFaceIDUsageDescription`. iOS **`SIGABRT`s at the capability call** if a key is absent, so copying
the shell hands you the keys you need for free. But the *strings* are BlazorNative demo copy
("BlazorNative uses your camera to take a photo in the camera demo") — they are the sentence your
user reads under the system prompt, and the App Store rejects empty or boilerplate text. Rewrite each
to your app's real purpose; the key is what matters to the OS, the sentence is app-specific and yours.
Drop a key only if your app never uses that capability.

---

## 4. What is a stub, not a feature

**`BnHost/AppleShellBridge.swift:106` — `fetchBegin` fails every request, synchronously:**

```swift
func fetchBegin(_ requestId: Int64) -> Int32 {
    NSLog("[AppleShellBridge] fetchBegin id=\(requestId) — unsupported (5.3 stub), returning -1")
    return -1
}
```

The reference app does no fetch, so the bridge does not implement one. The stub is
*deliberately* honest — it returns `-1` (host error) immediately rather than accepting the
request and never completing it, so a stray fetch surfaces at once instead of hanging.

**If your app uses `HttpClient` (`BlazorNative.Http`), you must implement this.** Wire a
real `URLSession` and call back through `blazornative_fetch_complete`. A setup guide that
let you discover this at runtime would be a bad guide.

> Images are *not* affected: `BnImage` goes through Kingfisher (`BnImageLoader`), not
> through this bridge. Android's equivalent path uses Coil.

---

## 5. The staging — the shape of it, and the one thing that will bite you

The recipe lives in `ios-build`'s **`Stage the link inputs`** step. Its shape:

1. **Publish** your app for `iossimulator-arm64`. The static archive lands under
   `bin/Release/net10.0/iossimulator-arm64/` — **`publish/` *or* `native/`**, depending on
   the publish shape; the CI step checks both, and so should you.
2. **`bootstrapperdll.o`** comes out of the NuGet **runtime pack**
   (`Microsoft.NETCore.App.Runtime.NativeAOT.iossimulator-arm64`), not out of your publish.
3. **The support archive**: the rest of the runtime pack's `native/*.a`, merged with
   `xcrun libtool -static` into one `libBnRuntimeSupport.a` — **minus exactly three
   members**: `libRuntime.ServerGC.a`, `libeventpipe-enabled.a`, `libstandalonegc-enabled.a`.
   Your app is ILC-compiled for WorkstationGC with eventpipe and standalone-GC disabled;
   the `-enabled` variants define the same symbols and would be ambiguously pulled.

### The load-bearing fix — read this one twice

**`bootstrapperdll.o` must be linked as a DIRECT OBJECT, never `-force_load`'d and never
merged into the support archive.**

In `project.yml`'s `OTHER_LDFLAGS` it is a bare path, and the `-force_load` below it applies
to the *app* archive:

```yaml
OTHER_LDFLAGS:
  - $(SRCROOT)/vendor/bootstrapperdll.o     # DIRECT — always included
  - -force_load
  - $(SRCROOT)/vendor/libBlazorNative.Runtime.a
  - $(SRCROOT)/vendor/libBnRuntimeSupport.a  # stays ON-DEMAND
  - $(SRCROOT)/vendor/libyoga.a
  - -lc++
  - -lz
```

Its `__attribute__((constructor))` must land in the app's `init_array` so the runtime
initializes before `blazornative_init`. **As an archive member it would be on-demand and
never pulled** — a constructor is not a "referenced symbol" — leaving `RuntimeInstance`
null.

**And here is why this is the paragraph that matters: you do not get a link error. You get a
`SIGSEGV` inside `ThreadStore::AttachCurrentThread`,** at runtime, pointing at nothing you
wrote. Do not "tidy" that bare `.o` into the merge.

The support archive **stays on-demand** on purpose: the linker pulls the library-appropriate
runtime objects without dragging in the exe bootstrapper or the Network-framework transport.

---

## 6. Yoga, from source

The Android shell gets Yoga from Maven (`com.facebook.yoga:yoga`). **iOS builds it from
source** — C++20, against the simulator SDK, merged with `libtool -static` into
`libyoga.a`, headers copied to `vendor/yoga-include/`. See `ios-build`'s
**`Yoga: build libyoga.a`** step.

**Then delete `vendor/yoga-include/yoga/module.modulemap`:**

```bash
rm -f src/BlazorNative.Apple/vendor/yoga-include/yoga/module.modulemap
```

With it present, Clang's explicit-module scanner tries to build a `yoga` *module* and cannot
resolve the module's own headers. Removing it forces plain textual includes via
`HEADER_SEARCH_PATHS`. This is not optional and it is not cosmetic.

**Use the same Yoga version both shells use.** It is one engine, and identical frames on
Android and iOS is the entire reason this framework chose Yoga — two versions lay out
differently, silently. The repo pins it in four files and asserts them equal in CI's very
first step; your copy is a fifth that nothing checks.

---

## 7. The csproj — and the asymmetry you are entitled to know about

Your app's csproj needs an iOS `PropertyGroup`. The reference is
`samples/BlazorNative.SampleApp/BlazorNative.SampleApp.csproj`:

```xml
<PropertyGroup Condition="$(RuntimeIdentifier.StartsWith('iossimulator')) Or $(RuntimeIdentifier.StartsWith('ios-'))">
  <NativeLib>Static</NativeLib>
  <RuntimeFrameworkVersion>10.0.9</RuntimeFrameworkVersion>
  <!-- … -->
</PropertyGroup>
```

You also need `UnmanagedEntryPointsAssembly`, `TrimmerRootAssembly` and the
`CanonicalizeNativeArtifactName` target — **a `dotnet new blazornative` app already has all
three**, so if you started from the template, you are copying only the iOS `PropertyGroup`.

> **`TrimmerRootAssembly` is not optional and its failure is silent.** Without it ILC trims
> your entire app module — green build, trim warnings drop from 4 to 0, your page names
> vanish from the binary, and the first thing you see is a failed mount. The template's
> csproj carries the line with a comment explaining it; do not drop it on the way to iOS.

**The asymmetry, stated plainly:** the Android side ships
`build/BionicNativeAot.targets` — a vendored, reusable MSBuild shim that a generated app
gets in its tree. **iOS has no equivalent.** Every property above is hand-copied, and
nothing tells you when the reference changes them. Moving that recipe into the
`BlazorNative.Runtime` package (where NuGet would auto-import it for every consumer, on both
platforms) is the right architecture and is on the backlog; it costs a shipped package's
inventory shape, which is why it has not happened yet.

---

## Reference: the files that matter

| Path | What it is |
|---|---|
| `src/BlazorNative.Apple/BnHost/` | the shell — 19 files, copy them |
| `src/BlazorNative.Apple/project.yml` | XcodeGen spec: targets, link flags, SwiftPM deps. Heavily commented; read it |
| `src/BlazorNative.Apple/BnHostTests/` | the reference's tests — 30 files, delete them |
| `src/BlazorNative.Apple/vendor/` | the frozen-name link inputs, produced by the staging step: `bootstrapperdll.o`, `libBlazorNative.Runtime.a`, `libBnRuntimeSupport.a`, `libyoga.a` + `yoga-include/`. The `.a`/`.o` are git-ignored — they are build outputs, never committed |
| `.github/workflows/ci.yml` → `ios-build` | **the executable truth.** Required on every PR |
| `.github/workflows/ios.yml` | the advisory simulator-execution lane — runs the XCTests on a booted simulator |

**Third-party dependencies** (from `project.yml`): **Kingfisher** (`from: 8.10.0`) via
SwiftPM — the iOS twin of Android's Coil, driving `BnImage`. Exactly one file in the shell
imports it.

---

## When it does not work

- **`SIGSEGV` in `ThreadStore::AttachCurrentThread`** at launch → `bootstrapperdll.o` is not
  a direct object. See §5.
- **Undefined `blazornative_*` symbols at link** → the app archive is not `-force_load`'d,
  or the publish did not produce it.
- **The app launches and the screen is empty / the mount fails** → the component name in
  `HostViewController.swift` does not match a page your `AppPages.All` registers (§3) — or
  `TrimmerRootAssembly` is missing and your app was trimmed away (§7).
- **A fetch hangs or fails with -1** → the bridge's fetch is a stub. See §4.
- **Clang cannot build a `yoga` module** → delete `module.modulemap`. See §6.
