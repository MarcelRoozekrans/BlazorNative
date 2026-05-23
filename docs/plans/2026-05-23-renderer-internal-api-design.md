# Design — Renderer Internal-API Strategy (Phase 1.1)

*Brainstormed: 2026-05-23 — Milestone 1, Phase 1.1*

## Problem

`BlazorNative.Renderer.NativeRenderer` needs to walk `RenderBatch` / `RenderTreeDiff` / `RenderTreeFrame` to translate Blazor renders into the `RenderPatch` JSON protocol. Almost every member it touches is `internal` in `Microsoft.AspNetCore.Components`. The current code references them directly and will not compile against a stock NuGet package. BACKLOG.md P0 lists this as a blocker: every later renderer work depends on the strategy chosen here.

## Decisions

| # | Decision | Chosen | Rejected alternatives |
|---|---|---|---|
| 1 | Coupling to Blazor internals | **Minimise (hybrid)** — `UnsafeAccessor` only where the public `Renderer` surface can't reach; floating minor pin (`10.0.*`); runtime safety net | A: embrace (UnsafeAccessor everywhere, exact-patch pin); C: avoid (HtmlRenderer pivot — loses high-fidelity diff, adds CPU) |
| 2 | Where `[UnsafeAccessor]` declarations live | **Isolation file** `BlazorInterop.cs` with `Bn*` `ref struct` wrappers; rest of renderer only ever touches wrappers | Sprinkled throughout `NativeRenderer.cs` (harder to audit / version-bump) |
| 3 | Version-compatibility check | **Runtime probe at startup** — static ctor on `BlazorInterop` verifies assembly version range AND exercises every accessor once; throws `BlazorVersionMismatchException` with actionable message | Compile-time analyzer (duplicates NuGet pin; doesn't catch member-rename); Both (overengineered for M1) |
| 4 | Verification strategy | **Minimal smoke test** in `tests/BlazorNative.Renderer.Tests/` (xUnit, net10) — mount a tiny component, assert first frame's patches | No tests (no proof spike works); Full per-patch coverage (belongs in Milestone 4 / BACKLOG P3) |
| 5 | Fallback for un-nameable internal types | **`[UnsafeAccessorType]` (.NET 9+)** — references internal types like `EventFieldInfo` by qualified-name string; pass `null` for our use of `DispatchEventAsync`'s field-info parameter | Hard-fail and pivot to plan C; build-time IVT injection (brittle, nuclear) |
| 6 | Target framework | **net10** across all consumer projects | Stay on net9 (would block `ZeroAlloc.TestHelpers`, force netstandard2.1 fallback for `ZeroAlloc.AsyncEvents`) |
| 7 | Adopt ZeroAlloc-Net packages in M1 | **`ZeroAlloc.AsyncEvents`** (replaces `System.Reactive.Subject<T>`), **`ZeroAlloc.Collections.PooledList<T>`** (replaces per-frame `List<RenderPatch>`), **`ZeroAlloc.TestHelpers.AllocationGate`** (proves zero-alloc on hot path) | Keep `System.Reactive` (drags trim warnings into WASI AOT); defer ZA adoption to M2 (would require rewriting the renderer twice) |

## Architecture

```
NativeRenderer.cs          ─┐
NativeWidgetTree.cs         │  (touches only BnRenderBatch / BnRenderTreeDiff /
RendererServices.cs         │   BnRenderTreeFrame / BnRenderTreeEdit + public
PatchProtocol.cs            │   Renderer, Dispatcher, ParameterView, etc.)
                            │
                            ▼
                     BlazorInterop.cs                          ← SINGLE SEAM
                     ├── [UnsafeAccessor] declarations for every internal field/method
                     ├── [UnsafeAccessorType] for EventFieldInfo (fully-internal type)
                     ├── Bn* ref struct wrappers (zero-copy: hold ref T to original)
                     ├── static ctor: version + accessor probe
                     └── header: "Bound to Microsoft.AspNetCore.Components 10.0.* — bump on update"
                            │
                            ▼
                Microsoft.AspNetCore.Components 10.0.*  (pinned in .csproj)
```

## The wrapper layer

`BlazorInterop.cs` defines four `ref struct` wrappers. Each holds a `ref T` to the underlying Blazor struct and exposes typed properties via `[UnsafeAccessor]` externs. Zero copy, zero allocation on the hot path.

```csharp
internal ref struct BnRenderBatch
{
    private ref RenderBatch _batch;
    public BnRenderBatch(ref RenderBatch b) => _batch = ref b;

    public BnArrayRange<RenderTreeDiff> UpdatedComponents
        => new(ref RefAccessors.UpdatedComponentsField(ref _batch));
    public BnArrayRange<int> DisposedComponentIDs => /* ... */;
}

internal ref struct BnArrayRange<T>
{
    private ref ArrayRange<T> _range;
    public int Count => RefAccessors.ArrayRangeCount(ref _range);
    public ref T this[int i] => ref RefAccessors.ArrayRangeArray(ref _range)[i];
    public RefEnumerator<T> GetEnumerator() => new(ref _range);   // ref-returning enumerator
}

internal ref struct BnRenderTreeDiff   { /* ComponentId, Edits, ReferenceFrames */ }
internal ref struct BnRenderTreeFrame  { /* FrameType, ElementName, ElementSubtreeLength,
                                            AttributeName, AttributeValue,
                                            AttributeEventHandlerId, TextContent */ }
internal ref struct BnRenderTreeEdit   { /* Type (as int), ReferenceFrameIndex,
                                            SiblingIndex, RemovedAttributeName */ }

file static class RefAccessors
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Array")]
    public static extern ref T[] ArrayRangeArray<T>(ref ArrayRange<T> range);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "FrameTypeField")]
    public static extern ref RenderTreeFrameType FrameType(ref RenderTreeFrame frame);
    // ... one per internal member; exact names validated by spike against Blazor 10 source
}
```

The protected `Renderer.DispatchEventAsync(ulong, EventFieldInfo?, EventArgs)` uses `[UnsafeAccessorType]` because `EventFieldInfo` is a fully-internal class:

```csharp
[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "DispatchEventAsync")]
extern static Task DispatchEventAsyncImpl(
    Renderer renderer,
    ulong eventHandlerId,
    [UnsafeAccessorType("Microsoft.AspNetCore.Components.RenderTree.EventFieldInfo, Microsoft.AspNetCore.Components")]
    object? fieldInfo,
    EventArgs eventArgs);
```

We always pass `null` for `fieldInfo` in M1. The patch protocol's `AttachEventPatch` doesn't carry field-binding info, which matches that null.

`RenderTreeEditType` is treated as `int` at the wrapper boundary (its underlying type) — saves us aliasing the enum.

## Data flow (single render cycle)

```
[1] Renderer.RenderRootComponentAsync (or StateHasChanged)
         │  builds RenderBatch internally
         ▼
[2] NativeRenderer.UpdateDisplayAsync(in RenderBatch renderBatch)
         │  ← sole entry-point that touches Blazor internals
         │
         │  using var patches = _patchBuffer.Lease();   // ZA.Collections.PooledList
         │  var batch = new BnRenderBatch(ref Unsafe.AsRef(in renderBatch));
         ▼
[3] foreach (ref BnRenderTreeDiff diff in batch.UpdatedComponents)
         │  ProcessRenderTreeDiff(ref diff, patches);
         ▼
[4]    foreach (ref BnRenderTreeEdit edit in diff.Edits)
         │  switch (edit.Type) {
         │     case RenderTreeEditType.PrependFrame: ProcessFrame(...);
         │     case RenderTreeEditType.RemoveFrame:  ...;
         │     case RenderTreeEditType.SetAttribute: ...;
         │     ...
         │  }
         ▼
[5]    ref BnRenderTreeFrame frame = ref diff.ReferenceFrames[frameIndex];
         │  switch (frame.FrameType) {
         │     Element → CreateNodePatch + walk attributes;
         │     Text    → CreateNodePatch + ReplaceTextPatch;
         │  }
         ▼
[6] _frames.Invoke(new RenderFrame(frameId, ts, patches.AsSpan().ToArray()))  // ZA.AsyncEvents
         → DispatchFrameAsync via IMobileBridge.FetchAsync

──────── Reverse direction (native → WASM event) ────────

[A] Native shell → blazornative_dispatch_event → WasiBridge.DispatchEvent
[B] NativeRenderer.DispatchUiEventAsync(NativeUiEvent e)
         │  Dispatcher.InvokeAsync(() =>
         │     BlazorInterop.DispatchEventViaAccessor(this, e.HandlerId, null, args))
         ▼
[C] Blazor's internal DispatchEventAsync → registered handler → component re-renders → [1]
```

Lifetime notes:
- `Unsafe.AsRef(in renderBatch)` converts `in` to `ref`; safe because the wrapper's lifetime is bounded by `UpdateDisplayAsync`'s stack frame.
- `Bn*` are `ref struct`s — cannot be boxed, captured in closures, or escape to the heap.
- `_patchBuffer.Lease()` returns the `PooledList<RenderPatch>` to the pool on `Dispose`.

## Error handling

| Failure | Trigger | Behaviour |
|---|---|---|
| **Blazor layout/version mismatch** | `BlazorInterop` static ctor: assembly version outside supported range OR an `[UnsafeAccessor]` probe throws `MissingFieldException` / `MissingMethodException` | Throws `BlazorVersionMismatchException` with actionable message including found-vs-expected version and the offending member name. Module fails to load. Caught on every CI smoke test. |
| **Per-frame failure** | Anything thrown inside `UpdateDisplayAsync` | Caught at the boundary; logged at `Error` with frame ID + edit index; **frame is dropped** (no patch dispatched); `Renderer.HandleException` still called so component error boundaries get a chance. Next `StateHasChanged` triggers a fresh batch. |
| **Stale native event** | `DispatchUiEventAsync` receives unknown `HandlerId` (race between shell and WASM lifecycle) | Catch only `ArgumentException`; log at `Warning` with handler ID; swallow. Other exception types re-throw (genuine bugs). |

Deliberately deferred to Milestone 7 (BACKLOG P6 hardening):
- Retry / backoff / circuit breaker on bridge calls
- Structured `shell_report_crash` payload back to native shell
- Validation of incoming `NativeUiEvent.NodeId` against the widget tree

## Testing

New project: `tests/BlazorNative.Renderer.Tests/BlazorNative.Renderer.Tests.csproj` (xUnit, net10), added to `BlazorNative.sln`.

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
<PackageReference Include="ZeroAlloc.TestHelpers" Version="*" />  <!-- source-includes AllocationGate.cs -->
```

Two tests in `RendererSpike.cs`:

| Test | Proves |
|---|---|
| `FirstFrame_HasExpectedPatches` | Mounts a `HelloComponent` (`<div>Hello @Name</div>`), captures the first `RenderFrame`, asserts the patch sequence is `[CreateNodePatch, CreateNodePatch, ReplaceTextPatch("Hello BlazorNative"), CommitFramePatch]`. End-to-end proof that the wrapper layer + accessor names work against the actually-installed Blazor 10 assembly. |
| `RenderWalk_IsAllocationFree_OnSteadyState` | Warms the pool with an initial render, then uses `AllocationGate.AssertNoAllocations(...)` around a second render. Catches regressions where someone accidentally boxes a `ref struct` or replaces `PooledList` with `List<>`. |

Picks up automatically via `make test`.

Deferred to Milestone 4 (BACKLOG P3 "BlazorNative.Renderer.Tests"):
- Per-`RenderTreeEditType` case coverage
- Disposed-component cleanup
- Event-dispatch round-trip with handler invocation
- Cascading parameters
- Style attribute handling
- Concurrent component trees

## .NET 10 retarget — file-by-file

| File | Change |
|---|---|
| `src/BlazorNative.Core/BlazorNative.Core.csproj` | `TargetFrameworks` → `net10.0;net10.0-browser;wasi-wasm`; `Microsoft.DotNet.ILCompiler.LLVM` → `10.*` |
| `src/BlazorNative.Blazor/BlazorNative.Blazor.csproj` | TF → `net10.0` |
| `src/BlazorNative.Renderer/BlazorNative.Renderer.csproj` | TFs → `net10.0;net10.0-browser;wasi-wasm`; pin `Microsoft.AspNetCore.Components` → `10.0.*`; pin `Microsoft.AspNetCore.Components.Web` → `10.0.*`; `System.Text.Json` → `10.*`; **drop** `System.Reactive`; **add** `ZeroAlloc.AsyncEvents`, `ZeroAlloc.Collections` |
| `src/BlazorNative.Http/BlazorNative.Http.csproj` | TF → `net10.0` |
| `src/BlazorNative.Host.Android/BlazorNative.DevHost.csproj` | TF → `net10.0`; `Microsoft.AspNetCore.Components.WebAssembly.Server` → `10.*`; **drop** `System.Reactive` |
| `src/BlazorNative.Core/BlazorNative.Core.csproj` | **drop** `System.Reactive` reference; **add** `ZeroAlloc.AsyncEvents` (DevHostBridge/WasiBridge use `Subject<NativeEvent>` today) |
| `src/BlazorNative.Analyzers/BlazorNative.Analyzers.csproj` | Stays `netstandard2.0` (Roslyn analyzers must) |
| `setup.ps1` | Workload IDs unchanged (`wasi-experimental` still exists in .NET 10, just targets net10 now); verify `Java 17+` / Android SDK versions still apply |
| `Makefile` | No change |
| `tests/BlazorNative.Renderer.Tests/BlazorNative.Renderer.Tests.csproj` | New file, net10 |
| `BlazorNative.sln` | Add new test project |

**Public interface change in `BlazorNative.Core`:** `IMobileBridge.NativeEvents` signature changes from `IObservable<NativeEvent>` to `IAsyncEventSource<NativeEvent>` (ZA.AsyncEvents). Ripples through `DevHostBridge`, `WasiBridge`, and any future bridge implementations. Documented here as a breaking change made now, before any external consumer exists.

## Risks

1. **Blazor 10 internal layout vs Blazor 9.** Accessor field names may differ. The runtime probe catches this on first build; expect to chase 1–3 renames during the spike. Mitigation: probe message names the offending member.
2. **`wasi-wasm` workload on .NET 10.** Available (`dotnet workload search wasi` confirmed `wasi-experimental` targets net10) but not yet installed on Marcel's machine. Phase 1.1 must run `dotnet workload install wasi-experimental` as a prerequisite.
3. **`Microsoft.DotNet.ILCompiler.LLVM` package may have been renamed in .NET 10.** First `make wasi` build will tell us. Mitigation: if renamed, update the package reference; if removed, escalate as a milestone-blocking issue.
4. **ZA packages on `wasi-wasm`.** `IsAotCompatible=true` in their csprojs is validated against ILC desktop, not necessarily ILC-LLVM. Mitigation: the `make wasi` smoke build at the end of Phase 1.1 catches incompatibility before we proceed.
5. **`[UnsafeAccessorType]` behaviour for the specific `DispatchEventAsync` case.** Documented as .NET 9+ feature; first compile in the spike confirms whether it works for protected methods on a base class. Fallback: skip the field-info parameter dispatch path in M1 and accept that field-bound events won't round-trip — `AttachEventPatch` doesn't need them.

## Out of scope

- The Android Kotlin shell, native widget rendering, MAUI integration — Milestone 2+
- Component library, Bn* components — Milestone 3
- iOS shell — Milestone 4+
- Hot reload of the WASI binary — Future / exploratory
- Adoption of other ZeroAlloc-Net packages (Notify, StateMachine, Validation, Serialisation, etc.) — Milestones 2–7 (queued in BACKLOG)

## Definition of done for Phase 1.1

1. `src/BlazorNative.Renderer/BlazorInterop.cs` exists with `[UnsafeAccessor]` declarations, `Bn*` ref struct wrappers, and the `BlazorVersionMismatchException` startup probe.
2. `src/BlazorNative.Renderer/NativeRenderer.cs` rewritten to walk the render batch via `Bn*` wrappers only — no direct reference to `RenderTreeDiff` / `RenderTreeEdit` / `RenderTreeFrame` / `ArrayRange<T>.Array`.
3. All consumer projects retargeted to net10; package pins updated; `System.Reactive` removed.
4. `IMobileBridge.NativeEvents` migrated to `IAsyncEventSource<NativeEvent>`; `DevHostBridge` and `WasiBridge` updated.
5. `tests/BlazorNative.Renderer.Tests/` project exists; both `FirstFrame_HasExpectedPatches` and `RenderWalk_IsAllocationFree_OnSteadyState` pass.
6. `make wasi` produces a `.wasm` binary (the binary doesn't need to run usefully yet — that's Phases 1.2 / 1.3 — but the build must succeed end-to-end).
7. Design doc (this file) committed; brief follow-up note added to BACKLOG.md mapping deferred items to their target milestones.

## Implementation plan

Produced separately via the `writing-plans` skill — this file is the design, not the step-by-step.
