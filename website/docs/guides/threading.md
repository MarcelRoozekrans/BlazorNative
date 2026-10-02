---
id: threading
title: Threading
sidebar_label: Threading
---

# Threading

BlazorNative renders on one thread it owns. This page is the contract for that thread: what
runs on it, what it means for the code a return code reports, how to get back onto it safely,
and what happens if you don't.

## The render thread

Every mounted session gets one dedicated thread, named `BlazorNative-Render`. Blazor's own
`Dispatcher` sits on it, and `Dispatcher.CheckAccess()` answers honestly — it is `true` only
when you are actually on that thread.

The shell's own dispatch lane hands each UI event and host event to the render thread and
waits, but only for the handler's **synchronous part** — up to its first `await`, if it has
one. A handler that yields frees the lane at once; the render thread keeps running the
continuation on its own. When that continuation resumes, it resumes on the render thread too,
same as it started — nothing needs to marshal back manually just because an `await` happened.

Source:
[`RenderThreadDispatcher.cs`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/src/BlazorNative.Renderer/RenderThreadDispatcher.cs)
and
[`NativeRenderer.cs`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/src/BlazorNative.Renderer/NativeRenderer.cs).

## Return codes: the rc contract

`blazornative_dispatch_event`'s return code is written in
[`Exports.cs`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/src/BlazorNative.Runtime/Exports.cs),
in
[`BlazorNativeRuntimeC.h`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/src/BlazorNative.Apple/BnHost/BlazorNativeRuntimeC.h)
and here, and `RcContractCopiesTests` fails if the three copies differ:

<!-- BEGIN rc-contract -->
```text
rc reports the SYNCHRONOUS part of the handler: 0 = it did not fault before it first yielded (it may
still be running); 2 = it faulted before yielding. An await on a host call or a fetch always yields,
even when the shell completes the call inside `hostCallBegin` or `fetchBegin`, so a fault after the
first such await arrives later as a FaultNotice host-call op, never as an rc. An await on a task the
app already completed itself, such as `Task.CompletedTask` or a cached result, does not yield: a
fault after it is still in the synchronous part, rc 2. Frames from the synchronous part are
delivered before the export returns; frames from a continuation are delivered later, from the render
thread.
```
<!-- END rc-contract -->

In plain terms: **rc only ever describes the synchronous half of a handler, and an await on a
host call or a fetch always ends that half.** If your handler awaits a host call or a fetch and
then throws, that fault does not come back as an rc. The rc was fixed at 0 when the handler first
yielded, even when the shell answered inside `hostCallBegin` or `fetchBegin`. The continuation
may even start before the export has handed that 0 back to the shell, but it can no longer change
it. Instead the fault is delivered later as a `FaultNotice` host call,
which reaches the shell's `onError` on a thread-pool thread, not the render thread. This is true
in production mode as well as strict/debug mode — a late fault is never silently dropped just
because the app isn't running under a debugger. The one exception is an await on a task your own
code already completed, such as `Task.CompletedTask` or a cached result: that does not yield, so
a throw after it is still part of the synchronous half and comes back as rc 2.

### Don't await a shell call in `OnInitializedAsync`

A page's first render must finish synchronously for the shell to mount it. Because an await on
a host call or a fetch always yields, a page whose `OnInitializedAsync` awaits one cannot finish
its first render in time, and the mount fails with "requires RenderRootComponentAsync to
complete synchronously". This holds on both platforms, including when the shell answers the call
inside `hostCallBegin` or `fetchBegin`. Await the call in `OnAfterRenderAsync` instead, or
start it in `OnInitialized` without awaiting it and render its result when it arrives.

## Re-rendering from another thread

An off-thread call to `StateHasChanged` throws — exactly the rule Blazor Server enforces, for
exactly the same reason: the render tree is not safe to touch from two threads at once. If your
own code (a timer callback, a background thread, a native SDK callback that isn't already
routed through the bridge) needs to update the UI, marshal back with `InvokeAsync`:

```razor bn-sample=component
@implements IDisposable

@code {
    private int _ticks;
    private System.Threading.Timer? _timer;

    protected override void OnInitialized()
        => _timer = new System.Threading.Timer(_ => OnTick(), null, 0, 1000);

    public void Dispose() => _timer?.Dispose();

    private void OnTick()
    {
        _ticks++;
        // The timer callback runs on a thread-pool thread, not the render thread.
        // InvokeAsync marshals the call onto the render thread before StateHasChanged runs.
        _ = InvokeAsync(StateHasChanged);
    }
}
```

A handler a native widget raises is already on the render thread, so calling
`StateHasChanged()` directly there is fine — see [State in BlazorNative](./state.md#threading) for
that half of the picture. `InvokeAsync` only matters once you start work of your own that
doesn't begin on that thread.

One more trap worth naming: don't put `ConfigureAwait(false)` before a render inside a
component. It is the standard, correct habit in library code, and it is wrong here — it can
resume your continuation off the render thread, which is exactly the condition above. There is
no compiled counterexample on this page for it (the resulting failure is intermittent, not a
compile error), but there is an open issue for a compile-time guard:
[#427](https://github.com/MarcelRoozekrans/BlazorNative/issues/427).

## Don't block the render thread

Never call `.Result`, `.Wait()` or `.GetAwaiter().GetResult()` inside a handler. The render
thread is shared: the shell's dispatch lane waits for your handler's synchronous part, and
every event or navigation queued behind it waits for that lane, one for one. A handler that
blocks for 200 ms holds the lane for 200 ms — measured, not estimated: a slow handler's
synchronous part and the delay felt by the very next event track each other almost exactly —
see the measurement in
[`docs/plans/2026-09-27-phase-16.3-record.md`](https://github.com/MarcelRoozekrans/BlazorNative/blob/main/docs/plans/2026-09-27-phase-16.3-record.md).
There is no back-pressure and no timeout; the next event simply waits as long as you do.

Blocking on a host call or a fetch on the render thread is worse than slow: it never returns.
A call started on the render thread continues on the render thread after its yield, so a
render thread blocked on it waits for work only it can run, even after the shell has answered.

```csharp bn-sample=statements
// Anti-pattern — never do this inside a handler. It blocks the render thread until the
// work finishes, and the dispatch lane — and every event queued behind it — wait with it.
static Task SlowWork() => Task.Delay(200);

SlowWork().GetAwaiter().GetResult();
```

`await` the work instead. The analyzer **BN0004** catches the most literal form of this,
`Thread.Sleep`, which stalls the same way a blocking wait does; it does not (and cannot) catch
`.Result` or `.GetAwaiter().GetResult()` in general, so treat this page as the rule and BN0004
as one thing it happens to be able to flag mechanically. See [Analyzer rules](../analyzers.md) for
the full set.

## The slow-handler warning

Since a slow synchronous handler is diagnosable but not preventable by the framework, the
renderer times every dispatch's synchronous part and warns when it runs long. The budget is
100 ms (`NativeRenderer.SlowHandlerBudget`) — chosen, not guessed; the phase record above has
the reasoning.

A few things worth knowing about the warning before you see one:

- **It fires once per call site per session**, then stays quiet for that call site. The key is
  the handler's own method — the declaring type and method name — so the same handler slow
  twice warns once, and a different slow handler warns on its own. A host-event arm (`back`,
  `navigate`, the lifecycle multicast) is keyed by event name instead, for the same reason.
- **It is capped at 32 distinct warnings per session.** The next one after that logs one line —
  `further slow-handler warnings suppressed` — and then the warning goes silent until the session
  ends. Together with the once-per-call-site rule, this is what keeps a Release build quiet:
  `BnLog`'s default level is `Warn` in every build (see [Logging](../logging.md)), so without these
  two limits a genuinely slow handler could spam every run.
- **It never includes the payload.** The message names the handler, the event and the elapsed
  time — never the arguments the handler was called with, which can carry a user's own input.

**Known limit.** A framework-forwarded handler — `BnInput`'s change handler is the example —
runs its own method for every instance, so the warning ends up naming `BnInput` itself rather
than the app code that is actually slow. Tracked as
[#435](https://github.com/MarcelRoozekrans/BlazorNative/issues/435). If you see the warning name
a BlazorNative component rather than your own code, this is why — the slow work is still yours,
just wrapped by that control.

## Back on Android, briefly

Pressing back never blocks Android's main thread. .NET decides whether there is somewhere to go
back to and **pushes** that answer to the shell as it changes — the shell never asks .NET on the
main thread and waits for a reply. At the root of the app, with nothing left to go back to, the
press falls through to the platform's own default back behavior; on Android 12 and later that
moves the task to the background rather than closing it. iOS has no system back gesture or
button to route this way, so this section is Android-specific.

## Shutdown

`blazornative_shutdown` quiesces the render pipeline before it returns: no frame reaches the
shell after that call comes back, even from a handler that never yielded. That guarantee covers
frames only. A late fault that races shutdown — a handler resuming and throwing while shutdown
runs — has two possible outcomes:

- it is dropped, because its pending task was cancelled first; or
- its fault notice is delivered **after** `blazornative_shutdown` has returned.

The bridge callbacks live for the whole process, so the second outcome is safe, but it means your
shell's `onError` **can fire after shutdown**. Don't tear down what `onError` touches on the
assumption that shutdown silenced it. Either way the fault still reaches stderr.
