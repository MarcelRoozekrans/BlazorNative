---
id: state
title: State in BlazorNative
sidebar_label: State
---

# State in BlazorNative

**There is no `BlazorNative.State` package, and there is not going to be one.** Blazor's own
state mechanisms work here unchanged, and everything on this page is exercised on a real device by
the sample app in this repository — not asserted from how Blazor behaves on the web.

That decision is [issue #22](https://github.com/MarcelRoozekrans/BlazorNative/issues/22)'s real
answer. The short version: a state package would be a mandatory transitive dependency that adds a
vocabulary you would have to learn, in place of one you already know.

:::tip The whole page in four lines
- **One component's state** → a private field + `StateHasChanged`.
- **Shared across a subtree** → `CascadingValue` — and pass a **new instance** on change.
- **Shared app-wide** → a DI singleton via `ConfigureServices` + `[Inject]`.
- **Changed from a native callback or a background thread** → get back to the render thread first. See [Threading](#threading).
:::

## Component-local state

A private field and `StateHasChanged`. Nothing framework-specific:

```razor
<BnColumn Gap="8">
    <BnText Text="@($"Count: {_count}")" />
    <BnButton Text="Increment" OnClick="Increment" />
</BnColumn>

@code {
    private int _count;

    private void Increment()
    {
        _count++;
        StateHasChanged();
    }
}
```

An event handler raised by a native widget already runs on the render thread, so this is safe as
written.

## Shared across a subtree — `CascadingValue`

Wrap the subtree, cascade the value, and read it with `[CascadingParameter]`. The sample's theme
toggle is exactly this, and both halves ship in the repo:

```razor
@* provider *@
<CascadingValue Value="_theme">
    <BnThemedPanel Padding="16">…</BnThemedPanel>
    <BnThemedPanel Padding="16">…</BnThemedPanel>
</CascadingValue>

@code {
    private BnTheme _theme = new("#FFEEAA", "#DDEEFF");

    private void ToggleTheme()
    {
        // A NEW record instance — see the warning below.
        _theme = new BnTheme(_theme.AltBackground, _theme.Background);
        StateHasChanged();
    }
}
```

```razor
@* consumer *@
@code {
    [CascadingParameter] public BnTheme? Theme { get; set; }
}
```

:::warning Pass a new instance, not a mutated one
`CascadingValue` notifies consumers when the **value** changes. If you mutate the object in place
and cascade the same reference, consumers are not notified and nothing re-renders.

`BnTheme` is a `record` for exactly this reason — it makes "a new value" the natural thing to write.
Prefer immutable types for anything you cascade.
:::

## Shared app-wide — a DI singleton

Register in `ConfigureServices` and take it with `[Inject]`:

```csharp
BlazorNativeApp.ConfigureServices(services =>
{
    services.AddSingleton<CartState>();
});
```

```razor
@code {
    [Inject] public CartState Cart { get; set; } = default!;
}
```

This is the same container the framework registers its own services into — `IGeolocation`,
`INotifications`, `IBiometrics`, `ISecureStorage`, `ICamera` all arrive this way, so your state
object sits beside them rather than in a parallel system.

Two things worth knowing:

- **`[Inject]` on a public property, not `@inject`.** `@inject` generates a *private* property. Use
  the explicit attribute when the injected service is part of a component's own surface — the
  sample does, deliberately.
- **Adding services is always safe; replacing a framework contract is not.** `INavigationManager`
  and `IMobileBridge` are documented **consume-only** — the framework both implements and consumes
  them, and a replacement is rejected rather than half-honoured.

### Notifying components from a singleton

A singleton has no `StateHasChanged` of its own. The ordinary Blazor pattern applies — expose an
event, subscribe in `OnInitialized`, and **unsubscribe in `Dispose`**:

```csharp
public sealed class CartState
{
    private int _count;
    public event Action? Changed;

    public int Count => _count;

    public void Add()
    {
        _count++;
        Changed?.Invoke();
    }
}
```

```razor
@implements IDisposable

@code {
    [Inject] public CartState Cart { get; set; } = default!;

    protected override void OnInitialized() => Cart.Changed += OnCartChanged;

    public void Dispose() => Cart.Changed -= OnCartChanged;

    private void OnCartChanged() => StateHasChanged();
}
```

Forgetting the unsubscribe leaks the component: the singleton outlives every page, so it keeps a
reference to a component that has been disposed.

## Threading — the one rule that is not Blazor's {#threading}

**Everything above assumes you are on the render thread.** Handlers raised by native widgets are;
work you start yourself may not be.

The renderer's dispatcher runs work on the **calling** thread — it does not marshal — so nothing
moves you back automatically. Mutating state and calling `StateHasChanged` from a background thread
drives a render batch from that thread, and the render tree is not safe to touch concurrently.

The renderer reports this rather than letting it pass silently. A batch driven from a thread other
than the one that drove the first batch is logged — as a warning when
`StrictErrors` is on, at `Debug` level otherwise — naming both threads:

```
render batch driven from thread 14, but this renderer's batches are owned by
thread 1 — the render tree is not safe to drive concurrently.
```

It is a **report, not an exception**: it tells you, it does not stop you. If you see it, the fix is
to get the state change back onto the render thread — raise it from a native event handler, or
route it through whatever your app already uses to reach the UI thread.

Awaiting inside a handler is fine. The concern is work that starts on a pool thread or arrives from
a native callback on a thread of its own.

## What about a store, Flux, or Redux?

Nothing stops you using one — they are ordinary .NET libraries, and an AOT-friendly one works here
like any other dependency. The framework does not ship one because:

- **DI singletons and cascading values already cover the demonstrated cases**, both proven on device.
- A shipped store would be a **mandatory transitive dependency** for every consumer, including those
  who want none of it.
- It would be an eighth package, against a rule this project has recorded four times and pins with
  `PackagePurityTests`.

If your app grows past what this page describes, reach for a library you choose — not one the
framework chose for you.
