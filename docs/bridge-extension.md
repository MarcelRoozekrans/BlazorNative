# The Bridge-Extension Pattern — adding a host API to the C-ABI

**Status:** the documented, versioned pattern for growing the shell bridge (M5 DoD #6).
Since **Phase 9.0 (M9 DoD #1)** it also covers **permission-gated async host calls** —
section (f) — and the ABI is at **10 exports / 80 bytes** (the first export grow since
Phase 3.1's `fetch_complete`). **Phase 9.1 (M9 DoD #3) is the pattern's FIRST declared
reuse** — local notifications — and it held the bet: the ABI stayed at **10 exports /
80 bytes**, unchanged. The pattern now has **two** worked capabilities.
**Worked examples:** clipboard + share (Phase 5.4), the first bridge growth with two
shells (Android + iOS) in lockstep — section (e); **geolocation** (Phase 9.0), the
first *permission-gated* capability — section (f.7); and **notifications** (Phase 9.1),
the first *reuse* + the first *inbound-event* capability (tap-through) — section (f.8).

This is the doc a future contributor reads to add a new host capability (camera,
geolocation, biometrics, …). It covers the bridge model, the **size-negotiation** that
makes growth forward/backward safe, the **step list** to add a callback, the **honest
test bar**, clipboard + share as the fully-worked *synchronous* example, and — since 9.0 —
the **permission pattern** with geolocation as its worked example. There are two shapes of
growth now: a **synchronous slot** (clipboard's shape), and a **permission-gated async
call** that rides the ONE generic `HostCallBegin` slot + the `host_call_complete` export
**without any further ABI change** (section (f)).

---

## (a) The C-ABI bridge model

The renderer (`BlazorNative.Runtime`, NativeAOT) calls **into** the host through a
struct of cdecl function pointers the host registers once at boot. It is the reverse
of the frame callback: the host implements the operations; the runtime invokes them.

- **The struct** — `BlazorNativeBridgeCallbacks` (`src/BlazorNative.Runtime/BridgeProtocolNative.cs`).
  Ten `IntPtr` slots since Phase 9.0 (80 bytes on x64):

  | Offset | Slot | Signature | Kind |
  |---|---|---|---|
  | 0  | `Navigate`       | `int(const char* routeUtf8)`                  | one-string |
  | 8  | `CurrentRoute`   | `int(char* buf, int cap)`                     | buffer read |
  | 16 | `StorageRead`    | `int(const char* keyUtf8, char* buf, int cap)`| buffer read (keyed) |
  | 24 | `StorageWrite`   | `int(const char* keyUtf8, const char* valUtf8)`| two-string |
  | 32 | `StorageDelete`  | `int(const char* keyUtf8)`                    | one-string |
  | 40 | `FetchBegin`     | `int(long id, BlazorNativeFetchRequest*)`     | async begin |
  | 48 | `ClipboardRead`  | `int(char* buf, int cap)`                     | buffer read |
  | 56 | `ClipboardWrite` | `int(const char* textUtf8)`                   | one-string |
  | 64 | `Share`          | `int(const char* textUtf8)`                   | one-string |
  | 72 | `HostCallBegin`  | `int(long id, int op, const char* argsJsonUtf8)` | async begin (generic, permission-gated) |

  The **exports** (`src/BlazorNative.Runtime/Exports.cs`) grew to **ten** at Phase 9.0:
  the async-begin slots each have a **push-completion export** twin — `FetchBegin` →
  `blazornative_fetch_complete`, `HostCallBegin` → `blazornative_host_call_complete`
  (the first export grow since Phase 3.1). `host_call_complete` is
  `int(long requestId, int status, const char* payloadJsonUtf8)`.

- **Register before mount.** `blazornative_register_bridge(int structSize, BlazorNativeBridgeCallbacks*)`
  copies the struct into an immutable holder; components resolving `IMobileBridge`
  (`BlazorNative.Core`) then reach a live host. The function pointers must outlive the
  call (each shell keeps a strong ref — the JNA/Swift GC rule).

- **Return-code protocol** (all slots return `int`, cdecl):
  - `>= 0` success. For buffer-writing slots this is the byte count written **including
    the NUL terminator**.
  - `-needed` — buffer too small; `|value|` is the exact bytes required (incl. NUL),
    meaningful only when `|value| > cap`. The .NET side retries **once** at that exact
    size, then throws. (`.NET` `InvokeBufferProtocol`; the host half is each shell's
    `writeUtf8`.)
  - `-1` host error (a thrown Kotlin/Swift handler is caught host-side and mapped to
    -1 — it never crosses the ABI raw).
  - `-2` key absent (`StorageRead` only → null .NET-side).

- **Three mirrors, byte-exact.** The struct is declared on three sides and pinned by
  drift tests:
  - **.NET** — `BridgeProtocolNative.cs` struct + `tests/…/BridgeProtocolNativeTests.cs`
    (size + per-field offsets).
  - **JVM** — `NativeBindings.kt` `@Structure.FieldOrder` mirror + the callback
    interfaces + `src/…/ShellBridgeTest.kt` (`callbacks_struct_is_…_bytes`).
  - **Swift** — `BlazorNativeRuntimeC.h` (`bn_*_cb` typedefs + the `bn_bridge_callbacks`
    struct) + `BnDriftTests.swift`.

  > **Phase 9.0 landing note.** Gate 1 landed the **.NET** half of the 80-byte struct +
  > the 10th export + the required-lane export-count gates (`ci.yml` ×RIDs, `ios.yml`,
  > `android-instrumented.yml`, the JVM export-resolve test). The **shell mirrors** of the
  > new `HostCallBegin` slot (the Kotlin `@Structure.FieldOrder` + `ShellBridgeTest`
  > 72→80, the Swift typedef + `BnDriftTests`) and the platform providers land at
  > **Gates 2 (Android) / 3 (iOS)** — the size negotiation makes the interim (a .NET
  > runtime that knows 10 slots, a shell that still registers 9) safe: the un-supplied
  > `HostCallBegin` reads back `IntPtr.Zero` and surfaces `NotSupportedException`.

---

## (b) Size negotiation — why growth is safe

Growing a fixed-size struct that is copied whole is normally an ABI break. The bridge
avoids that with a **size-as-argument** scheme, added while there were only three struct
sites (Phase 5.4):

- **`register_bridge` takes a leading `structSize`** = the byte size of the *caller's*
  struct. The runtime copies `min(structSize, sizeof(its own struct))` bytes into a
  **zero-initialised** holder and leaves the rest zero:

  ```csharp
  int known  = sizeof(BlazorNativeBridgeCallbacks);      // the runtime's size (72)
  int toCopy = Math.Clamp(structSize, 0, known);          // never over-read, never negative
  BlazorNativeBridgeCallbacks dest = default;             // zero-fills the whole struct
  Buffer.MemoryCopy(source, &dest, known, toCopy);        // copies only toCopy bytes
  ```

  - The **upper** clamp truncates a newer host's extra tail (it can't corrupt the
    runtime).
  - The **lower** clamp makes a stray non-positive size a safe no-copy (everything
    unsupported) instead of an `OverflowException` — a self-contained invariant,
    independent of the export's `structSize > 0` guard (defense in depth).
  - The min-copy **never over-reads** a host buffer shorter than the runtime's struct.

- **A null callback slot = capability unsupported.** A slot the host didn't supply (an
  old shell, or a runtime older than the shell) is `IntPtr.Zero` after the zero-fill.
  The `NativeShellBridge` invoker guards it (`RequireSlot`) and throws
  `NotSupportedException` — a graceful "not supported by this host", never a null-pointer
  call. This is what makes the negotiation *real*: growth is forward- **and**
  backward-compatible.

**Interop table** (runtime knows 10 slots / 80 bytes since Phase 9.0):

| Scenario | structSize passed | Runtime copies | Result |
|---|---|---|---|
| old shell (9 slots) → **new** runtime | 72 | 72 (9 slots); `HostCallBegin` zero-filled | geolocation → `NotSupportedException`; navigate/storage/fetch/clipboard work |
| **new** shell (10 slots) → new runtime | 80 | 80 (all 10) | everything works |
| new shell (10 slots) → **old** runtime (knew 9) | 80 | 72 (old runtime's `min`) | extra tail ignored; old caps work |

**On the export count — the one honest exception to "a slot grow does not touch the
exports".** Adding a **synchronous** slot (clipboard's shape) does *not* grow the exports:
it is a struct grow only, and the export-count gates in `ci.yml` / `ios.yml` /
`android-instrumented.yml` are untouched. But an **async-begin** slot needs a
**push-completion export** twin, and that *is* an export grow. It has happened exactly
twice: `fetch_complete` (Phase 3.1) and `host_call_complete` (Phase 9.0, 9→10). The 9.0
grow was paid **once** and made **generic** (section (f)), so the *next* permission-gated
capability (notifications / biometrics / camera) rides the same `HostCallBegin` +
`host_call_complete` with **zero** further ABI change and **zero** export-gate churn.
When you *do* grow the exports, every hard-coded export-count array moves in lockstep
(the ~6 gate arrays across the three workflows + the template/consumer scripts + the JVM
export-resolve test) — red-first, in the same commit.

---

## (c) How to add a new host API — the step list

**First, pick the shape.** There are now two:

- **A permission-gated async capability** (needs a system prompt, a possible app
  suspension, and a denial that must come back as *data*) — the geolocation shape. **Do
  NOT grow the ABI.** Ride the generic `HostCallBegin` slot + the `host_call_complete`
  export: pick an `op` constant (`NativeShellBridge.HostCallOp`), a status mapping, a
  typed `IMobileBridge` method + a `DevHostBridge` mock, host handlers on both shells, and
  (optionally) a `BlazorNative.Device` façade. No new export, no struct grow, no drift-test
  move, no export-gate edit. See section (f) — this is the whole point of paying the 9.0
  export grow once. Skip steps 1–2 and 6's struct-literal growth; the rest apply.
- **A synchronous capability** (a near-instant, ungated round-trip — clipboard's shape) —
  append a struct slot as below.

To add a **synchronous** capability `X` (e.g. a hypothetical `BatteryLevel`):

1. **Append a callback to the struct** at the next 8-byte offset. Pick the shape:
   *buffer read* (returns data to .NET), *one-string* (a string arg), or *two-string*.
   (An *async-begin* new slot is only for a request whose args cannot be expressed as the
   generic `op`+flat-JSON — otherwise reuse `HostCallBegin`.) Add the field to **all three
   mirrors**:
   - `.NET` `BlazorNativeBridgeCallbacks` (`BridgeProtocolNative.cs`),
   - `NativeBindings.kt` `@Structure.FieldOrder` + a `Bridge…Callback` interface,
   - the Swift header `bn_bridge_callbacks` + a `bn_x_cb` typedef.
2. **Bump the drift tests** — the struct size (72 → 80) + the new offset, on **both**
   `.NET` (`BridgeProtocolNativeTests`) and JVM (`ShellBridgeTest`), plus the Swift
   `BnDriftTests`.
3. **Grow the `IMobileBridge` contract** (`BlazorNative.Core`) with the typed async
   method, and implement it in **`DevHostBridge`** (the in-process mock).
4. **Add the `NativeShellBridge` invoker** — reuse an existing shape:
   - a *read* → `InvokeBufferProtocol` (the `CurrentRoute`/`ClipboardRead` twin),
   - a *write / fire-and-forget* → `InvokeWithOneString` (the `Navigate`/`Share` twin),
   - guard the slot with **`RequireSlot(cb.X, "x")`** so an old host → `NotSupportedException`.
5. **Grow `ShellBridgeHandlers`** (`ShellBridge.kt`) with the method; wire a callback in
   `BridgeRegistrar` (reads via `writeUtf8`, writes via `getString`); the registrar
   already passes `struct.size()` as `structSize`. Implement it in **all 6 handler
   impls** (see the survey list below) — production shells real, test doubles inert.
6. **Grow the Swift side** — add the trampoline in `AppleShellBridge.swift`, the real
   method, and the field in `BnRuntime`'s `bn_bridge_callbacks` literal (it passes
   `MemoryLayout<bn_bridge_callbacks>.size`).
7. **Implement per platform** — Android in `AndroidShellBridge.kt` (app-scoped context
   only — never retain an Activity), iOS in `AppleShellBridge.swift`.
8. **Test at each layer** — .NET in-process (a probe + `DispatchEventCore`), JVM through
   the dll, and an instrumented/XCTest against the real platform API. Bump the recorded
   counts in the CI baselines.
9. **Version bump** (shared code changed) + both Kotlin version assertions.

**The 6 `ShellBridgeHandlers` impls** to update in step 5: `AndroidShellBridge`
(production), `InspectorHostBridge` (jvmHost), and the four test doubles
`RecordingHandlers` (NavigationTest), `InertHost` (HostEventTest), `InertBridge`
(InspectorServerTest), and the `ShellBridgeTest` anonymous host.

---

## (d) The honest test bar

Capabilities split by what is *observable*:

- **Round-trippable caps** (clipboard, storage) — assert the **value read back**. On
  device this exercises the real platform API end-to-end (write → real store → read →
  echo).
- **UI-affordance caps** (share) — the system share sheet / activity controller is **not
  assertable** under instrumentation (and popping it would hang the test). The honest bar
  is **callback-fired-with-content**: a test seam captures what the impl *built* (the
  `ACTION_SEND` Intent / the `UIActivityViewController` items) and asserts the content
  (`EXTRA_TEXT`, MIME type), **not** the system UI. Document the seam.

Each cap is proven at three layers, so the on-device layer only needs to prove the
*platform wiring* (a value round-trip may be unavailable on-device for platform reasons
— e.g. the Android clipboard-read focus gate below — and is then proven at the .NET/JVM
layers instead).

---

## (e) Worked example — clipboard + share (Phase 5.4)

**Slots:** `ClipboardRead@48` (buffer read), `ClipboardWrite@56` (one-string),
`Share@64` (one-string). Struct 48 → 72.

**.NET surface** (`IMobileBridge` / `NativeShellBridge`):
`ClipboardReadAsync()` (via `InvokeBufferProtocol`, the `CurrentRoute` twin),
`ClipboardWriteAsync(text)` + `ShareAsync(text)` (via `InvokeWithOneString`), each
`RequireSlot`-guarded. `DevHostBridge` mocks an in-memory clipboard + logs share. The
**`ClipboardProbe`** scaffolding component (`[Inject] IMobileBridge`, Copy/Paste/Share
`BnButton`s + an echo `BnText`) is what all three shells mount to drive the round-trip.

**Android** (`AndroidShellBridge.kt`): clipboard → the system `ClipboardManager`
(`getSystemService`, appContext) — `setPrimaryClip(ClipData.newPlainText(...))` /
`primaryClip?.getItemAt(0)?.text`. Share → an `ACTION_SEND` `text/plain` Intent built by
the `buildShareIntent` **seam**, wrapped in `createChooser`, launched from **appContext**
with `FLAG_ACTIVITY_NEW_TASK` (appContext is not an Activity — the retention rule forbids
retaining one; NEW_TASK is required to start an Activity from a non-Activity context). A
process-static `shareLaunchHook` lets the instrumented test capture the Intent and skip
the launch.
*Platform caveat:* Android 10+ gates clipboard **reads** on window focus; the
instrumented test waits on `hasWindowFocus()` and the CI lane sets `hide_error_dialogs 1`
so a boot ANR can't steal focus.

**iOS** (`AppleShellBridge.swift`): clipboard → `UIPasteboard.general.string` (safe off
the main thread). Share → a `UIActivityViewController` presented on the **main thread**
from the key window's root VC; a `shareHook` seam captures the `activityItems` and skips
the sheet for XCTest.

**Tests:** `.NET` `ClipboardProbeTests` (Copy→Paste→echo + Share seam) and the
size-negotiation pins in `NativeShellBridgeTests` (round-trip, `-needed` retry, `-1`
host error, the **48-byte old-shell → unsupported** forward-compat, and the
**negative-structSize** no-copy). JVM `ClipboardTest` (through the dll). Android
`ClipboardAndroidTest` (real `ClipboardManager` + the share-Intent seam). iOS
`BnClipboardTests` (real `UIPasteboard` + the activity-controller seam).

**Counts at 5.4 close** *(a historical snapshot — not current; see `README.md`
for today's numbers)*: .NET 230 / JVM 79 / Android 40 / iOS XCTest 13; version
`1.4.0-phase-5.4`; exports unchanged at 9.

---

## (f) The Permission Pattern — permission-gated async host calls (Phase 9.0)

Clipboard proved a host capability behind a struct slot. It never had to do the two
things a *permission-gated* capability must: **suspend the app behind a system prompt**,
and **carry a denial back to .NET**. Geolocation is the first capability that does both,
and it is the **worked example** of this pattern — the shape 9.1 (notifications) / 9.2
(biometrics) / 9.3 (camera) copy.

**The one normative rule: denial is DATA.** "The user said no" — and every other terminal
outcome (restriction, no-fix, host error) — reaches .NET as a **status value**, **never
an exception and never a hang**. The awaiting `ValueTask` *always* resolves (or is
cancelled by the caller's token). This is proven at Gate 1, on the mock, before any device
work: a test drives all six statuses and asserts each RETURNS within a bounded await.

### 1. The request → prompt → result flow

A permission-gated call is an **async-begin** + a **deferred push-completion**, keyed by
`requestId`, resolved off-lane — structurally identical to **fetch** (the async
precedent), with a permission prompt in the middle and a tri-state instead of an HTTP
response:

1. **Begin.** `.NET` `GetCurrentPositionAsync` assigns an `Interlocked` id, parks a
   `TaskCompletionSource` (`RunContinuationsAsynchronously` — the completion arrives on a
   host thread and must not run continuations inline on it) in the pending registry, then
   calls the **generic** slot `HostCallBegin(id, op:Geolocation, argsJson)`. The pointer
   marshalling is a non-async helper (`BeginHostCall` — the `BeginFetch` split; pointers
   cannot live in an async body). `args` is flat JSON — `{"mode":"request"}` for the
   prompt-then-fetch call, `{"mode":"check"}` for the read-only permission check, on the
   **same op** (no second slot).
2. **The host runs the whole permission dance** — check → prompt if undetermined → on
   grant fetch a fix → on deny/restrict note the status — possibly across an app
   suspension, then calls `blazornative_host_call_complete(id, status, payloadJson)`.
3. **Complete.** `CompleteHostCall(id, status, payload)` removes the id and resolves the
   TCS. The awaiting side maps the wire `status` integer to the typed tri-state and, only
   when `Granted`, parses the flat-JSON fix into a typed `GeolocationPosition`.

### 2. The pending-registry rule

`s_pendingHostCalls : ConcurrentDictionary<long, TaskCompletionSource<HostCallResult>>`
on `NativeShellBridge` (the `s_pendingFetches` twin). Keyed by `requestId`; resolved
**off-lane**. Rules, stated once:

- **Unknown id is benign.** A completion for an id no longer in the table (a
  cancellation race, a duplicate) takes the unknown-id path: `return 1`, logged, **never a
  throw**.
- **Cancellation-safe.** A `CancellationToken` threads through; on cancel the id is
  removed and the TCS `TrySetCanceled`d (the `FetchAsync` `ct.Register` pattern). A
  process **killed during the prompt** drops the whole registry with it; a
  never-completing call is the **caller's token to abandon** — never a leaked pending
  entry, never a hang.
- **Process-scoped — it survives Android Activity recreation.** The `.NET` registry is a
  static in the NativeAOT runtime (process-scoped), untouched by an Activity recreation
  behind the system dialog. Only the *host-side* `requestCode → requestId` map must be
  app-scoped — and the retention law already forbids retaining an Activity.

### 3. Denial-as-data — the tri-state (stated once)

The completion's `int status` is a **wire-mirrored enum**, defined byte-identically on all
three sides (a `.NET` `GeolocationStatus`, a Kotlin enum, a Swift enum — pinned like the
struct). The host maps each platform's native outcome into it **host-side**; `.NET` only
ever sees the integer + the payload.

| status | Name | Meaning | Payload |
|---|---|---|---|
| 0 | `Granted` | permission held; a fix was obtained | the fix (flat JSON) |
| 1 | `Denied` | denied **this time**; a later request MAY prompt again | none |
| 2 | `DeniedPermanently` | "don't ask again" / iOS `.denied` — only Settings changes it | none |
| 3 | `Restricted` | parental controls / MDM — the **user cannot** grant it | none |
| 4 | `LocationUnavailable` | permission fine, services OS-disabled / no provider / timeout | none |
| 5 | `Error` | unexpected host error (a caught Kotlin/Swift throw) | optional message |

An out-of-range integer (a host bug) maps to `Error` — still data, never a throw. The fix
crosses as a **flat JSON object of string→string** (numbers string-encoded, reusing the
fetch-headers `WriteFlatJsonObject`/`ParseFlatJsonObject` pair — no new serializer):
`{"lat":"52.3702","lng":"4.8952","accuracy":"12.0","altitude":"3.0","timestamp":"…"}`.

### 4. The both-shells vocabulary (the mapping table)

The host maps each platform's native permission/outcome into the tri-state:

| enum | Android (`LocationManager` + `PackageManager` / `shouldShowRequestPermissionRationale`) | iOS (`CLAuthorizationStatus` / `CLError`) |
|---|---|---|
| `Granted` | `PERMISSION_GRANTED` + a fix from `getLastKnownLocation` / `requestSingleUpdate` | `.authorizedWhenInUse`/`.authorizedAlways` + `didUpdateLocations` |
| `Denied` | not granted ∧ `shouldShowRequestPermissionRationale` true | (n/a — iOS prompts once; a first `.denied` is permanent) |
| `DeniedPermanently` | not granted ∧ rationale false (after a request) | `.denied` |
| `Restricted` | device-policy restriction | `.restricted` |
| `LocationUnavailable` | permission fine but services off / no provider / fix timed out | `CLError.locationUnknown` / `.denied`-adjacent service-off |
| `Error` | a caught Kotlin throw | a caught Swift throw / other `CLError` |

*(Android uses the platform `LocationManager`, not `FusedLocationProviderClient`: the
fused client needs Google Play Services, whereas the platform manager is fed directly by
`adb emu geo fix`, so CI needs no Play-Services dep. iOS uses `CLLocationManager
.requestLocation`, when-in-use only.)* The provider wiring lands at Gates 2/3.

### 5. The manifest / Info.plist declarations

Each permission API needs its platform declaration (geolocation's shown; the shape the
next three follow):

- **Android** — `<uses-permission>` in **both** manifests (sample + template, for parity):
  `ACCESS_FINE_LOCATION` + `ACCESS_COARSE_LOCATION`, next to the existing `INTERNET`
  (`src/BlazorNative.Jni/src/androidMain/AndroidManifest.xml` and the template's).
- **iOS** — a purpose string in `Info.plist`: `NSLocationWhenInUseUsageDescription`
  (when-in-use authorization only — background/geofencing are out of scope)
  (`src/BlazorNative.Apple/BnHost/Info.plist`).

These land with the shell providers at Gates 2/3.

### 6. Adding a permission-gated cap — NOT a new export

Per section (c)'s first bullet, restated as the pattern: to add capability `X` (a system
prompt, a possible suspension, a denial), pick an **`op` constant**
(`NativeShellBridge.HostCallOp`) + a **status mapping** + a typed **`IMobileBridge`**
method + a **`DevHostBridge`** mock + **host handlers on both shells** + (optionally) a
**`BlazorNative.Device`** façade. The generic `HostCallBegin`/`host_call_complete` are
**shared** — **NOT a new export, NOT a struct grow, NOT an export-gate edit, NOT a
drift-test move.** The export grew **once** (9→10) in 9.0 and will not again this
milestone. That is the reuse the 9.0 export event bought — **and Phase 9.1 collected on
it**: notifications added `Notifications = 1` to `HostCallOp` and **nothing else on the
ABI** (bridge still 80 bytes, exports still 10, every drift pin green as-is). The bet
held on its first draw.

### 7. Worked example — geolocation (Phase 9.0)

**Slot / export:** `HostCallBegin@72` (generic async-begin) + `blazornative_host_call_complete`
(the `fetch_complete` twin). Struct 72 → 80; exports 9 → 10.

**.NET surface** (`IMobileBridge` / `NativeShellBridge`, `BlazorNative.Core` types):
`GetCurrentPositionAsync(ct)` → `GeolocationResult(GeolocationStatus, GeolocationPosition?)`;
`CheckGeolocationPermissionAsync(ct)` (the no-prompt read). Both go through
`InvokeHostCallAsync(op:Geolocation, …)` → the pending registry → the parsed result.
`DevHostBridge` mocks a **configurable status + position** (all six statuses headless).

**The façade** — `IGeolocation` in the new **7th package `BlazorNative.Device`**, a thin
delegate over `IMobileBridge.GetCurrentPositionAsync`; app code injects `IGeolocation`, not
the low-level bridge. Registered via `AddBlazorNativeDevice()` from the runtime composition
root (`HostSession`).

**The demo** — `BnGeolocationDemo` (routed `/geolocation`), the worked example that
`[Inject]`s `IGeolocation`, with a "Locate" button that echoes the fix on `Granted` and the
**status** on every denial (denial made visible as data), and a "Check" button (no prompt).

**Android** (Gate 2, `AndroidShellBridge.kt`): the platform `LocationManager` (app-scoped)
+ `ActivityCompat.requestPermissions` / `onRequestPermissionsResult` wiring that **survives
Activity recreation** (the app-scoped bridge forwards `(requestCode, grantResults)` and
emits `host_call_complete`). **iOS** (Gate 3, `AppleShellBridge.swift`): a **app-scoped**
`CLLocationManager` + its delegate carrying the in-flight `requestId`.

**Tests:** `.NET` `GeolocationBridgeTests` (the op+args, the Granted parse, the six-status
denial-as-data matrix within a bounded await, the wire-integer mapping, the requestId
keying + unknown-id + cancellation, the old-shell-unsupported forward-compat),
`GeolocationFacadeTests` (the `DevHostBridge` six-status matrix + the `IGeolocation`
façade), `BnGeolocationDemoTests` (mount + Locate echoes fix/status). JVM: the
`host_call_complete` export resolves + a tri-state round-trip through the dll (Gate 1). AVD
`adb emu geo fix` real fix + `pm revoke` denial (Gate 2). iOS simulator location + the
denial path (Gate 3).

### 8. Worked example — notifications (Phase 9.1), and tap-through as an inbound event

**The reuse, stated first:** notifications add `HostCallOp.Notifications = 1` and **nothing
else on the ABI** — no struct grow (still **80 bytes / 10 slots**), no new export (still
**10**), no drift-pin move, no export-gate arithmetic. schedule / show / cancel + the
permission are four **geolocation-shaped** host calls; the tap-through (a notification tap →
a route) is the one genuinely new shape, and it too fits with **zero ABI change** — see the
inbound-event note below.

**Op / export:** the EXISTING `HostCallBegin@72` + `blazornative_host_call_complete`. The
**action lives inside the flat JSON** (geolocation's `mode` precedent — one op, many
sub-actions, no second slot):

| action | args (flat JSON) | completion status | payload |
|---|---|---|---|
| `schedule` | `{"action":"schedule","id":"7","title":"…","body":"…","when":"<unix-ms>","route":"/notifications"}` | `NotificationStatus` | none |
| `show` | `{"action":"show","id":"7","title":"…","body":"…","route":"/notifications"}` (no `when`) | `NotificationStatus` | none |
| `cancel` | `{"action":"cancel","id":"7"}` | `NotificationStatus` | none |
| `request` | `{"action":"request"}` | `NotificationStatus` | none |
| `check` | `{"action":"check"}` | `NotificationStatus` | none |

Every field crosses as a **string** (numbers string-encoded, `InvariantCulture`; `id`
decimal, `when` Unix epoch **milliseconds**, `route` a `DEEP_LINK_COMPONENTS` key or absent)
— reusing `WriteFlatJsonObject`/`ParseFlatJsonObject`, no new serializer. schedule/show/cancel
carry **no completion payload** (a status is the whole answer — *less* than geolocation asked).

**`NotificationStatus` — the NEW wire-mirrored status** (geolocation's shape minus
`LocationUnavailable`, which has no notification analogue). Defined byte-identically across
.NET / Kotlin / Swift; .NET sees only the integer, and an out-of-range value → `Error`
(still data, never a throw):

| status | Name | Meaning | Android | iOS |
|---|---|---|---|---|
| 0 | `Granted` | permission held; the op ran (posted / scheduled / cancelled) | API<33 implicit, or `POST_NOTIFICATIONS` granted | `.authorized` / `.provisional` |
| 1 | `Denied` | denied **this time**; a later request MAY prompt | not granted ∧ rationale true | (n/a — iOS prompts once) |
| 2 | `DeniedPermanently` | "don't ask again" / iOS `.denied` | not granted ∧ rationale false (after a request) | `.denied` |
| 3 | `Restricted` | policy / MDM — the user **cannot** grant it | device-policy restriction | `.restricted` |
| 4 | `Error` | unexpected host error (a caught throw) | caught Kotlin throw | caught Swift throw |

The already-granted fast path (the geolocation `check` precedent): a **request short-circuits
to `Granted` when permission is already held** — never a redundant prompt. Two platform
folds are recorded so their absence is a decision, not an omission:
- **Android below API 33 (`SDK_INT < 33`): `POST_NOTIFICATIONS` did not exist and is
  IMPLICITLY GRANTED** — the host returns `Granted` with no prompt and no manifest gate (the
  *majority* of devices). On API 33+ it reuses the 9.0 `requestPermissions` /
  `onRequestPermissionsResult` machinery **verbatim, pointed at a different permission
  string** — 9.1 writes no new Android permission plumbing.
- **iOS `.provisional`** (quiet notifications, delivered without a prompt) **folds into
  `Granted`** — they post, which is what the caller asked. Exposed as a distinct status only
  if a consumer needs to distinguish quiet delivery; the POC folds it.

**Manifest / Info.plist:** Android adds `<uses-permission
android:name="android.permission.POST_NOTIFICATIONS" />` in **both** manifests (inert below
API 33) + the `NotificationPublisher` `BroadcastReceiver`; scheduling uses **inexact**
`AlarmManager.set` (a notification needs no exact timing) so **no `SCHEDULE_EXACT_ALARM`**.
**iOS needs no Info.plist key** — authorization is purely runtime (`requestAuthorization`).
These land with the shell providers at Gates 2/3.

**Tap-through — the genuinely new shape, and NOT a `host_call_complete`.** Tapping a
notification is an **unsolicited inbound event**: the OS wakes the app with **no in-flight
.NET `ValueTask`** (the app may have been dead when the notification posted), so there is no
`requestId` to complete. It rides two EXISTING channels, both ABI-free:
- **Cold start (app killed):** the tap's `PendingIntent` reproduces the 5.1 launch-time
  deep-link Intent (`VIEW`, `data = blazornative://<route>`); `MainActivity` resolves the
  mount via `DEEP_LINK_COMPONENTS` **before the `.so` loads** — untouched by 9.1. iOS carries
  the route in `userInfo["route"]` and seeds the initial mount the same way.
- **Warm re-route (app alive):** the tap delivers to `Activity.onNewIntent` (Android) / the
  UNUC delegate (iOS); the shell dispatches **`host_event("navigate", route)`** over the
  EXISTING `blazornative_host_event` export. `DispatchHostEventCore` maps the reserved name
  **`"navigate"`** (the `"back"` precedent) to `NativeNavigationManager.NavigateToAsync` — the
  name→verb mapping lives in .NET so every shell gets identical semantics. **This is wire
  vocabulary + a .NET branch, NOT an ABI change.** (iOS milestone-first: the
  `blazornative_host_event` export EXISTS in the shared runtime but iOS had never called it —
  9.1's iOS shell simply starts calling it, exactly as 9.0's iOS shell first called
  `host_call_complete`. Still zero ABI change.)

**.NET surface** (`IMobileBridge` / `NativeShellBridge`, `BlazorNative.Core` types):
`ScheduleNotificationAsync` / `ShowNotificationAsync(NotificationSpec)`,
`CancelNotificationAsync(int id)`, `Request`/`CheckNotificationPermissionAsync` — all through
the EXISTING `InvokeHostCallAsync(op:Notifications, argsJson, ct)` (the pending registry,
cancellation posture, unknown-id benignness reused, not re-written). `DevHostBridge` mocks a
**configurable status + an in-memory schedule/cancel list** (all five statuses headless).

**The façade** — `INotifications` in the **SAME 7th package `BlazorNative.Device`** (a sibling
of `IGeolocation`, a thin delegate over `IMobileBridge`); **no 8th package**, one added
`AddBlazorNativeDevice()` line. **The demo** — `BnNotificationsDemo` (routed
`/notifications`), the worked example that `[Inject]`s `INotifications`, echoes every
`NotificationStatus` as data (denial visible, never thrown), and is the **tap-through landing
page** (its route is `/notifications`).

**Android** (Gate 2, `AndroidShellBridge.kt`): the `"blazornative_default"` channel; `show`
→ `NotificationManager.notify`; `schedule` → `AlarmManager` + the `NotificationPublisher`
receiver; `cancel` → both; `POST_NOTIFICATIONS` request/check reusing the 9.0 machinery; the
tap `PendingIntent(blazornative://<route>)` + `onNewIntent` + `singleTop` + the
`host_event("navigate", route)` warm re-route. **iOS** (Gate 3, `AppleShellBridge.swift`):
`UNUserNotificationCenter` (`UNMutableNotificationContent` + trigger; cancel via `remove*`);
`requestAuthorization` / `getNotificationSettings`; the `userInfo["route"]` carrier + the
UNUC delegate calling `host_event("navigate", …)`.

**Tests (Gate 1):** `.NET` `NotificationBridgeTests` (op + action-in-JSON, the five-status
denial-as-data matrix within a bounded await, the wire-integer mapping incl. out-of-range →
`Error`, the requestId keying + old-shell-unsupported), `NotificationFacadeTests` (the
`DevHostBridge` five-status matrix + schedule/show/cancel bookkeeping + the `INotifications`
façade), `BnNotificationsDemoTests` (mount + the arrival marker + status echo),
`HostNavigateTests` (the `"navigate"` branch — handled / unknown-route / no-session / fault),
`NotificationsAbiUnchangedTests` (the reuse headline: 80 bytes / offset 72 / op = 1,
unchanged). AVD real post + `POST_NOTIFICATIONS` + tap-route (Gate 2); iOS simulator UNUC +
auth matrix + `didReceive` → navigate (Gate 3).

---

*Version note: the ABI is at **10 exports / 80 bytes** since Phase 9.0 — **unchanged by
Phase 9.1** (notifications added an op-enum value and no ABI). See `README.md` for current
counts.*
