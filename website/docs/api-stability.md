---
id: api-stability
title: API stability
sidebar_label: API stability
sidebar_position: 7
---

# API stability

**What you may depend on, what you may not, and how you will be told when it changes.**

BlazorNative is **pre-1.0**. That does not mean "anything can happen" — the public surface of
every shipped package is now **recorded member-by-member and gated in CI**. This page tells you
exactly how far that goes.

:::info The one-sentence version
The public API is **marked but not frozen**: a public-API change can no longer land
unacknowledged, but a **minor** version may still break the surface *deliberately*, with a
changelog line. At 1.0 the same files become a freeze.
:::

---

## What "marked but not frozen" means for you

Six of the seven packages carry a `PublicAPI.Shipped.txt` baseline — one line per public member,
including enum members and nullability annotations. The Roslyn public-API analyzers turn three
diagnostics into **build errors** in the required CI lane:

| Diagnostic | Fires when | Reads as |
|---|---|---|
| `RS0016` | A public member exists that no baseline declares | *You added public API without saying so.* |
| `RS0017` | A baseline lists a member the assembly no longer has | *You removed or changed public API.* |
| `RS0037` | A nullability annotation differs from the baseline | *`string` became `string?` (or the reverse).* |

A **rename fires both** `RS0017` and `RS0016` — the old name vanished, the new one was never
declared.

### The practical consequence, in three lines

- **Before:** a parameter could be renamed and nothing in the pull request would say so. You
  found out at `dotnet restore`.
- **Now:** that pull request fails to build until its author writes the change into
  `PublicAPI.Unshipped.txt` — so **the break appears in the diff as reviewable lines**, and the
  changelog can carry it.
- **Still true while we are on 0.x:** breaking is *allowed*. A `feat:` commit produces a new
  **minor** (0.4 → 0.5) and that minor may break the surface. The gate makes a break a
  **decision**, not an accident. It does not make it impossible.

At **1.0** the files do not change — the *rule about who may edit them* does. See
[Compatibility statement](#compatibility-statement).

---

## The three tiers

Every public type sits in exactly one tier. The tiers are a property of the **type**, not of the
package it happens to live in.

| Tier | Count | What it means for your code |
|---|---:|---|
| **STABLE** | 55 | The surface we intend to freeze at 1.0. Build on it. |
| **PROVISIONAL** | 2 | Usable, baselined, *not* advertised as stable. We will tell you when it moves; we are not promising it will not. |
| **NOT-API** | 31 | Public for mechanical reasons. Binding to it is doing something the framework does not support. |
| **Total public types** | **88** | across seven shipped packages |

<details>
<summary>Why the largest non-stable bucket is the healthiest possible finding</summary>

31 types are NOT-API — but **not one of them is "unfinished"**. They are public because a
C-ABI export mechanism, a cross-assembly composition root, or a source generator required them
to be. That is a very different problem from 31 half-built types, and it is why the honest mark
for them is `[EditorBrowsable(Never)]` rather than `[Experimental]`.

</details>

### STABLE — build on this

The surface an app author actually types.

| Package | What is stable |
|---|---|
| `BlazorNative.Components` | All **25** components and their `[Parameter]`s — `BnView`, `BnText`, `BnRow`, `BnColumn`, `BnButton`, `BnInput`, `BnImage`, `BnScroll`, `BnActivityIndicator`, `BnList<TItem>`, `BnModal`, `BnPicker`, `BnSlider`, `BnSwitch`, `BnCheckbox`, the `BnFlexPreset` base, `BnTheme`, and the `Flex*` / `ImageContentMode` enums and constant holders. |
| `BlazorNative.Device` | The **5 `[Inject]`-able façades** — `IGeolocation`, `INotifications`, `IBiometrics`, `ISecureStorage`, `ICamera` — plus `AddBlazorNativeDevice()`. 15 members total. Every implementation is `internal sealed`, so an interface is the *only* thing you can bind to. This is the cleanest surface in the repo. |
| `BlazorNative.Core` | The **11 capability result/status types** (`GeolocationResult`, `GeolocationPosition`, `GeolocationStatus`, `NotificationSpec`, `NotificationStatus`, `BiometricStatus`, `SecureStorageStatus`, `SecretResult`, `CameraStatus`, `CaptureOptions`, `PhotoResult`), `IMobileBridge`, `INavigationManager`, `PlatformInfo` / `PlatformKind`, `NativeEvent`, `BridgeHttpRequest` / `BridgeHttpResponse`, `BnImageErrorEventArgs`, `BnScrollEventArgs`. |
| `BlazorNative.Http` | `BridgeHttpHandler` and `AddBlazorNativeHttp` / the two `AddBlazorNativeHttpClient` overloads. |
| `BlazorNative.Runtime` | `BlazorNativeApp` (`DefaultRoute`, `RegisterPages`, `ConfigureServices`) and `BlazorNativePage` (`Routed<T>`, `Named<T>`). Your whole startup contract, deliberately tiny. |

**Example — every line here is STABLE:**

```csharp
public static class AppPages
{
    public static readonly BlazorNativePage[] All =
    [
        BlazorNativePage.Routed<Home>("/", "home"),
        BlazorNativePage.Routed<Camera>("/camera", "camera"),
    ];
}

// In a component:
@inject ICamera Camera

PhotoResult photo = await Camera.CapturePhotoAsync(new CaptureOptions(Quality: 80));
if (photo.Status == CameraStatus.Denied) { /* denial is DATA, never an exception */ }
```

### PROVISIONAL — usable, movable

| Type | Why it is not STABLE |
|---|---|
| `DevHostBridge` (`BlazorNative.Core`) | The in-process mock bridge. Genuinely useful for your tests — but its members are *mock-shaped*: seeding hooks and canned results, not a contract designed for strangers. It is also the natural home of a future consumer test harness, work that will reshape it. |
| `AddBlazorNativeRenderer` (`BlazorNative.Renderer`) | A composition-root call you need only if you are building a custom host. It is a one-line delegate to generated code, so its shape is not ours to promise. |

Use them. Pin your version if you depend on their exact shape.

### NOT-API — public, but not for you

These are public because a mechanism required it, not because they are a contract:

- **`NativeRenderer`** and the renderer's in-memory patch model (`RenderPatch` and its 9
  derivatives, `RenderFrame`, `NativeUiEvent`, `BlazorVersionMismatchException`) — `NativeRenderer`
  is public *so that an `internal` type in a different assembly can drive it*. There is no
  scenario in which an app author sets `StrictErrors`.
- **The C-ABI interop surface in `BlazorNative.Runtime`** — `Exports` (the 10
  `[UnmanagedCallersOnly]` entry points), `BlazorNativeBridgeCallbacks`,
  `BlazorNativeFetchRequest`/`Response`, `BlazorNativeInitOptions`/`Result`, `BlazorNativePatch`,
  `BlazorNativeFrame`, `BlazorNativePatchKind`, `BlazorNativeNodeType` — plus `NativeShellBridge`
  and `NativeNavigationManager`. These are frozen **far harder** than a managed baseline can
  express (an 80-byte struct pinned by `Marshal.SizeOf`, a fixed field offset, a symbol-count
  gate on every published binary) — but that freeze is a contract with **the two native shells**,
  not with you. Their *managed* shape is an artefact of it.
- **The three analyzers.** You cannot reference them at all — the package ships as an analyzer
  asset. See [the `BN00xx` roster](#the-bn00xx-and-bn1xxx-id-reservations).

:::warning `[EditorBrowsable(Never)]` is a signpost, not a barrier
NOT-API types carry `[EditorBrowsable(EditorBrowsableState.Never)]` plus an xmldoc line naming the
mechanical reason they are public. That hides them from IntelliSense **and restricts nothing** —
`new SetStylePatch(...)` still compiles today. It is the honest ceiling of a *non-breaking*
change. The closing fix (making them `internal`) **is** breaking, and is tracked as a 1.0
criterion rather than done silently. If you have bound to one of these types, you are outside the
supported surface and a minor version may move it.
:::

**Three NOT-API types cannot be marked**, and we would rather say so than let you infer that an
unmarked type is supported: two generated dependency-injection registration classes
(`BlazorNativeRendererServicesServiceCollectionExtensions`,
`BlazorNativeHttpServicesServiceCollectionExtensions`) and Razor's `_Imports`. An attribute
cannot be added to source we do not own.

---

## The `BN00xx` and `BN1xxx` id reservations

The analyzers package's real contract is **not** its class names — it is the diagnostic ids you
type into a `NoWarn`, an `.editorconfig` severity, or a `#pragma`:

`BN0004` · `BN0010` · `BN0011` · `BN0013` · `BN0014` · `BN0020` · `BN0021`

That roster is pinned by a test in **both directions** — adding an id without updating it, or
removing one, is a build failure. Renaming the C# class an analyzer lives in breaks nothing;
renaming `BN0011` breaks your build. So the id set is what we treat as frozen, and the analyzers
package is deliberately **excluded** from the `.txt` baselines. See
[Analyzer rules](./analyzers.md).

**`BN1xxx` is reserved for `[Experimental]` diagnostic ids** and is disjoint from `BN0xxx`,
permanently — both land in the same `NoWarn`, so a stale suppression must never start silencing a
live warning. Ids are **never reused**.

**`[Experimental]` is currently used on nothing.** It produces a compile **error** at every use
site, so it is spent only for informed consent about a genuinely unproven surface — the first
member of a capability shipped ahead of device proof, for instance. It is *not* a synonym for
"internal": marking a NOT-API type experimental would claim "this may change" about something
that has not moved in five milestones, while breaking the build of anyone referencing the package
for unrelated reasons.

---

## Interfaces are consume-only

`IMobileBridge` and `INavigationManager` are **consume-only contracts**.

> **Adding a member to them is a non-breaking change by declaration. Implementing them outside
> BlazorNative is unsupported.**

This asymmetry is deliberate and it favours you:

- Adding an interface member is **invisible to every caller** and **fatal to every implementer**.
- `IMobileBridge` grows *by construction* — once per capability. Five capabilities have added to
  it already, and more are planned. A policy that made each addition a major bump would make the
  framework unable to grow.
- The only two implementations are ours (`NativeShellBridge`, `DevHostBridge`).

**If you need a test double, do not hand-implement 27 members.** Mock a
`BlazorNative.Device` façade instead — they are 2–5 members each and exist precisely for this —
or use `DevHostBridge`.

The policy is written into the interfaces' own xmldoc, so it reaches you through IntelliSense.

---

## Compatibility statement

### Between `0.x` minor versions (today)

| Tier | What you may rely on |
|---|---|
| **STABLE** | The surface is **recorded and gated**. It *may* break in a minor, but only deliberately: the change is visible in the pull request's `PublicAPI.Unshipped.txt` diff and carries a changelog entry. Nothing breaks silently. |
| **PROVISIONAL** | Same visibility, weaker intent — expect movement. Pin your version if the exact shape matters. |
| **NOT-API** | Nothing. It may move in any release. Changes are still recorded in the baseline, so they are visible if you go looking, but they are not announced as breaks. |
| **The `BN00xx` ids** | Stable. Your `NoWarn` will keep working. |
| **The C-ABI** | Not your contract — it is the framework's contract with its own shells. Do not P/Invoke it. |

**Adding a member to an enum** (a new `GeolocationStatus`, say) is source-compatible and
**behaviourally breaking** — it compiles, then falls through your exhaustive `switch`. We treat
it as a minor-version change **with a changelog line**, never as a free edit. Write a `default`
arm.

### At 1.0 and after

| Tier | What you may rely on |
|---|---|
| **STABLE** | **Frozen.** A breaking change requires a **major** version. A removal or signature change to a STABLE member is a 2.0 conversation, not a changelog line. |
| **PROVISIONAL** | Promoted to STABLE, or removed, before the 1.0 tag. The tier does not survive 1.0. |
| **NOT-API** | Expected to become `internal` at or after 1.0. Treat it as already gone. |
| **Package moves** | A type moving to a new package ships a `[TypeForwardedTo]` from the old one, which makes the move **non-breaking at the source level**. |

### How a break will be signalled

1. **The changelog.** Releases are generated by release-please from conventional commits; a
   breaking change carries the `!` / `BREAKING CHANGE` footer and is rendered as its own section.
2. **The version.** `0.x`: a **minor** bump. Post-1.0: a **major** bump. Never a patch.
3. **The baseline diff.** `PublicAPI.Shipped.txt` in the release's own diff is the machine-readable
   list of exactly what moved — the honest answer to *"what actually changed?"* is a file, not
   prose.

### The limit of all of this, stated plainly

**A baseline records a surface; it does not judge it.** These files will freeze a badly-named
parameter as faithfully as a good one. They guarantee that a change is *visible* — they guarantee
nothing about whether the API was worth freezing. Reading the baselines line by line during this
work turned up two real defects in the *design* of shipped types that no test had caught, and
those are being fixed as defects rather than blessed by the file that recorded them.

---

## Roadmap to 1.0

1.0 is defined by an explicit, checkable list — **12 blockers**, covering the API baselines and
marking, real-device Android proof, logging discipline, a surfaced render error, and this
documentation. As of 2026-07-22, **7 are met** and the remaining 5 are owned by named work.

The full list, with evidence for every row, lives in the repository:
[`docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/plans/2026-07-22-phase-11.3-one-point-oh-criteria.md).

**One caveat will survive to 1.0 and is named rather than hidden:** iOS is proven on the
**simulator** — a real ARM64 execution of the real NativeAOT static library through the real
`xcodebuild` toolchain, with frame tables asserted identical to Android's — and has **never run
on physical iOS hardware**. Specifically untested on device: camera capture from a real sensor,
Face ID / Touch ID against the Secure Enclave, real-GPS geolocation, APNs and universal links,
code-signing and provisioning, and thermal/background behaviour. **Android is device-proven.**
Exactly one platform may carry this caveat, and it is the one blocked on an Apple Developer
account rather than on engineering.
