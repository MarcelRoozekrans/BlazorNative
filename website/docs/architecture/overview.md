---
id: overview
title: Architecture overview
sidebar_label: Overview
sidebar_position: 1
---

# Architecture overview

One runtime, one transport, one layout engine. The same NativeAOT library, the same typed
struct protocol and the same Yoga tree run everywhere — which is *why* both platforms can
be made to agree on the numbers.

```
[Your Blazor components]      plain Razor/C# — BnView / BnRow / BnColumn / BnScroll …
        ↓
[BlazorNative.Renderer]       headless NativeRenderer + the RenderPatch model
        ↓
[BlazorNative.Runtime]        NativeAOT composition root + the C-ABI exports
                              typed-struct frames — no JSON, no interpreter
        ↓   one native library per platform
        ↓
   Android: a .so, cross-compiled     iOS: a static archive, linked into the app
        ↓                                   ↓
   JNA (cdecl callback)               direct static link (C-ABI, no JNA)
        ↓                                   ↓
[BlazorNative.Jni]                   [BlazorNative.Apple]
   Kotlin shell                          Swift/UIKit shell
        ↓                                   ↓
        └────── each shell builds TWO MIRRORED TREES ──────┘
                              ↓
        [view tree]                      [Yoga node tree]
   real platform widgets             Facebook's C++ flexbox engine
   TextView / UILabel                  Android: the Maven JNI artifact
   Button, EditText / UITextField      iOS: source-built, reached through
   ScrollView / UIScrollView                Objective-C++ behind a plain-C
   containers = layout-suppressed           surface (Yoga's C++ headers can
   frame containers                         never be visible to Swift)
                              ↓
   Yoga computes → every child is placed at its COMPUTED FRAME
```

## The four ideas

### 1. Your app is a native library, not a bundle

The Blazor UI and your business logic compile **ahead-of-time** into a platform-native
shared library. There is no IL interpreter, no JIT, and nothing to download at boot. On
Android that is a `.so` cross-compiled against the NDK; on iOS it is a static archive
linked directly into the app binary.

### 2. The renderer is headless, and it speaks structs

A `NativeRenderer` drives the real Blazor render tree — nested components, keyed lists,
real disposal — and turns diffs into **typed struct patches**: create node, set style,
replace text. They cross into the shell through a C-ABI frame callback. Nothing is
serialized. See [The wire](./the-wire.md).

### 3. The shell builds two trees, not one

This is the design's load-bearing move. Every shell keeps a tree of **real platform
widgets** and, beside it, a **Yoga node tree**. Style names are partitioned by an
allow-list — layout names go to the Yoga node, visual names go to the view — and the
containers are *layout-suppressed*: they never place a child themselves. Yoga computes; the
shell assigns computed frames. See [Layout and Yoga](./layout-and-yoga.md).

### 4. The two shells are held to the same numbers by a test

Two hand-written shells in two languages will drift. The answer is not discipline, it is a
gate: the frames are asserted equal across platforms in CI. See
[The parity contract](./parity.md).

## The packages

| Package | What it is |
|---|---|
| `BlazorNative.Core` | The `IMobileBridge` contract and the bridge implementations. A pure library. |
| `BlazorNative.Renderer` | The headless `NativeRenderer` and the `RenderPatch` model. A pure library. |
| `BlazorNative.Http` | `BridgeHttpHandler` + DI — plain `HttpClient` over the shell's fetch. |
| `BlazorNative.Components` | The `Bn*` component library. [Reference](../components/overview.md). |
| `BlazorNative.Analyzers` | Compile-time guards for the native runtime. [Rules](../analyzers.md). |
| `BlazorNative.Runtime` | The publishable composition root: DI wiring + the `[UnmanagedCallersOnly]` export surface. |

## Where the drift is caught

The style routing table is hand-written in **three places** — the renderer's C#, the
Android shell's Kotlin, and the iOS shell's Objective-C++. A name present in one and
missing from another is *silently dropped*, not a build error, so a drift test in the
required CI lane parses all three and asserts set-equality.

That pattern — *if it can drift silently, a gate reads it* — is the one this project reaches
for repeatedly, and it is why the architecture pages here point at the code and the
workflows instead of restating what they say.

## Extending the bridge

Adding a capability to the host contract (navigate, storage, fetch, …) touches the C-ABI in
several places at once. That procedure lives in the repository, next to the code it
changes:
[`docs/bridge-extension.md`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/bridge-extension.md).
