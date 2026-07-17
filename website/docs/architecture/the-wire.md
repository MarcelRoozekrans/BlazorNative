---
id: the-wire
title: The wire
sidebar_label: The wire
sidebar_position: 2
---

# The wire

Between your C# and the native widget on screen there is exactly one boundary: a **C-ABI**,
carrying **fixed-size typed structs**. Understanding it is optional for writing pages and
essential for understanding why this project behaves the way it does.

## It is not a JSON protocol

It used to be, in the WebAssembly era, and that layer was deleted. Today the renderer
produces an in-memory patch model and the runtime encodes it as a **typed-struct frame**:
a header followed by an array of fixed-size patch records. Nothing on the frame path is
serialized, parsed, or allocated per-frame.

The design goals are stated in the protocol source itself, and they explain most of the
shape you will see:

- **Minimal payload** — only diffs, never full tree snapshots.
- **Zero-alloc friendly** — structs for hot-path types.

## The patches

A render cycle yields a list of atomic commands the shell applies to its widget tree:

| Patch | What it says |
|---|---|
| `CreateNodePatch` | Create a native widget of a given type, under a parent, at a host child index |
| `RemoveNodePatch` | Remove a node and its subtree |
| `SetStylePatch` | Apply one style property to a node |
| `UpdatePropPatch` | Set or update a property (a `null` value removes it) |
| `ReplaceTextPatch` | Replace a text node's content |

Two details in there are load-bearing and worth pulling out:

**Creation carries its own placement.** `CreateNodePatch` has an `InsertIndex`: `-1` means
append (the mount-walk common case), and `≥ 0` inserts at that position for mid-list keyed
inserts. Zero is a *valid* index, which is precisely why `-1` is encoded explicitly rather
than using zero as a sentinel. There is no separate append patch — it was retired once
creation carried placement, and moves are remove-plus-insert.

**The index counts real views only.** Blazor's sibling slots include component slots that
own no view; the renderer translates its own sibling positions into *host* child indices,
skipping them. A shell never has to know that a component existed.

**Retired wire ids stay reserved.** When a patch kind is deleted, its number is not reused
and the remaining kinds do not renumber. A wire whose ids shift under a shell that was
compiled against the old ones fails in the least debuggable way possible, so it is not
allowed to happen.

## The exports

The runtime's export surface is a small, deliberately boring set of `cdecl` entry points —
initialize, shut down, report a version, register the frame callback, mount a component,
dispatch an event, register the host bridge, complete a fetch, deliver a host event.

This site does not list their count or their signatures, on purpose: they are declared in
one place, and that place is checked by the build.
[`Exports.cs`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/src/BlazorNative.Runtime/Exports.cs)
is the truth, and the analyzers enforce the rules the boundary lives by — see
[BN0020 and BN0021](../analyzers.md), which exist because **an exception escaping a
`[UnmanagedCallersOnly]` frame does not throw into your caller; it aborts the process.**

## Events come back the same way

A tap on a real `Button` becomes a `blazornative_dispatch_event` call carrying a handler id
and a JSON argument payload, which the renderer routes to your `EventCallback`; your
handler runs, the tree re-renders, and the resulting patches ride the frame callback back
out. The round trip is synchronous on the dispatch lane — by the time a sync-completing
handler returns, the re-render has already been delivered to the shell.

The argument payload is JSON, and that is not an inconsistency with the rest of this page:
event args are low-frequency and shaped by Blazor's own event model, while frames are the
hot path. The hot path got the structs.

## The bridge

Host capabilities — navigate, storage, fetch — are **callbacks the shell registers into the
runtime**, not P/Invokes the runtime makes outward. That inversion is what lets one .NET
binary serve two shells that share no code.

`BlazorNative.Http` rides that bridge: with it referenced, a plain `HttpClient` works
inside your components on Android. On iOS, the reference shell's fetch is a deliberate
stub — see [Shells → iOS](../shells/ios.md), which tells you exactly what to implement and
why the stub fails loudly instead of hanging.

Adding a capability to this contract touches the ABI in several places at once. The
procedure is in the repository, beside the code it changes:
[`docs/bridge-extension.md`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/bridge-extension.md).
