---
id: state
title: State in BlazorNative
sidebar_label: State
---

# State in BlazorNative

**There is no "BlazorNative.State" package, and there is not going to be one.** Blazor's own
state mechanisms work here unchanged. The cascading theme and the `[Inject]`-ed services on this
page are the sample app's own, exercised on a real device; registering your own singleton through
`ConfigureServices` is covered by the framework's unit tests rather than by the sample. None of it
is asserted from how Blazor behaves on the web.

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

```razor bn-sample=component
<BnColumn Gap="8">
    <BnText Text="@($"Count: {_count}")" />
    <BnButton Label="Increment" OnClick="Increment" />
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
toggle is exactly this — the consumer below is the sample app's `BnThemedPanel`, abridged; the
full version, with its own history, lives at
`samples/BlazorNative.SampleApp/BnThemedPanel.razor`:

```razor bn-sample=component
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

```razor bn-sample=component:BnThemedPanel
@* consumer — abridged from samples/BlazorNative.SampleApp/BnThemedPanel.razor *@
<BnView BackgroundColor="@(Theme?.Background)" Padding="@Padding" ChildContent="@ChildContent" />

@code {
    [CascadingParameter] public BnTheme? Theme { get; set; }
    [Parameter] public float? Padding { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
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

```csharp bn-sample=statements
BlazorNativeApp.ConfigureServices(services =>
{
    services.AddSingleton<CartState>();
});
```

```razor bn-sample=component
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
  them. Re-registering `INavigationManager` is rejected at startup with an exception rather than
  half-honoured. Re-registering `IMobileBridge` is not checked, but it is just as unsupported: do
  not do it.

### Notifying components from a singleton

A singleton has no `StateHasChanged` of its own. The ordinary Blazor pattern applies — expose an
event, subscribe in `OnInitialized`, and **unsubscribe in `Dispose`**:

```csharp bn-sample=file
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

```razor bn-sample=component
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

Calling `StateHasChanged` off the render thread throws — exactly the rule Blazor Server enforces,
for the same reason: the render tree is not safe to touch from two threads at once. Marshal back
first, with `InvokeAsync(StateHasChanged)`. Awaiting inside a handler is fine on its own; the
concern is work that starts on a thread-pool thread or arrives from a native callback of its own.

The full contract — what runs where, why an `await` doesn't move you off the render thread, and
what a slow handler costs the app — is its own page: [Threading](./threading.md).

## What about a store, Flux, or Redux?

Nothing stops you using one — they are ordinary .NET libraries, and an AOT-friendly one works here
like any other dependency. The framework does not ship one because:

- **DI singletons and cascading values already cover the demonstrated cases**, both proven on device.
- A shipped store would be a **mandatory transitive dependency** for every consumer, including those
  who want none of it.
- It would be one more shipped package. This project adds packages only on purpose — capabilities
  join an existing package — and `PackagePurityTests` makes any new one join the pinned shipped set
  deliberately rather than drift in.

If your app grows past what this page describes, reach for a library you choose — not one the
framework chose for you.
