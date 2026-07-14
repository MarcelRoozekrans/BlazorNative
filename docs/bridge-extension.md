# The Bridge-Extension Pattern — adding a host API to the C-ABI

**Status:** the documented, versioned pattern for growing the shell bridge (M5 DoD #6).
**Worked example:** clipboard + share (Phase 5.4), the first bridge growth with two
shells (Android + iOS) in lockstep.

This is the doc a future contributor reads to add a new host capability (camera,
geolocation, biometrics, …). It covers the bridge model, the **size-negotiation** that
makes growth forward/backward safe, the **step list** to add a callback, the **honest
test bar**, and clipboard + share as the fully-worked example.

---

## (a) The C-ABI bridge model

The renderer (`BlazorNative.Runtime`, NativeAOT) calls **into** the host through a
struct of cdecl function pointers the host registers once at boot. It is the reverse
of the frame callback: the host implements the operations; the runtime invokes them.

- **The struct** — `BlazorNativeBridgeCallbacks` (`src/BlazorNative.Runtime/BridgeProtocolNative.cs`).
  Nine `IntPtr` slots since Phase 5.4 (72 bytes on x64):

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
    interfaces + `src/…/ShellBridgeTest.kt` (`callbacks_struct_is_72_bytes`).
  - **Swift** — `BlazorNativeRuntimeC.h` (`bn_*_cb` typedefs + the `bn_bridge_callbacks`
    struct) + `BnDriftTests.swift`.

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

**Interop table** (runtime knows 9 slots / 72 bytes; two clipboard/share callbacks the
worked example):

| Scenario | structSize passed | Runtime copies | Result |
|---|---|---|---|
| old shell (6 slots) → **new** runtime | 48 | 48 (6 slots); clipboard/share zero-filled | clipboard/share → `NotSupportedException`; navigate/storage/fetch work |
| **new** shell (9 slots) → new runtime | 72 | 72 (all 9) | everything works |
| new shell (9 slots) → **old** runtime (knew 6) | 72 | 48 (old runtime's `min`) | extra tail ignored; old caps work |

The **export count is unchanged** (still 9 `blazornative_*` symbols) — this is a struct
grow + a signature change on one symbol, **not** an export grow. The export-count gates
in `ci.yml` / `ios.yml` / `android-instrumented.yml` are *not* touched when adding a
slot.

---

## (c) How to add a new host API — the step list

To add capability `X` (e.g. `CameraCapture`):

1. **Append a callback to the struct** at the next 8-byte offset. Pick the shape:
   *buffer read* (returns data to .NET), *one-string* (a string arg), *two-string*,
   or an *async-begin* (like fetch). Add the field to **all three mirrors**:
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
