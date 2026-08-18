# BlazorNative.Testing

**Mount a BlazorNative page in a unit test and assert what it rendered.**

Add it to your **test** project — it is a dev-time dependency and should never be
referenced by the app itself.

```sh
dotnet add package BlazorNative.Testing
```

## The shape of a test

```csharp
using BlazorNative.Testing;

[Fact]
public async Task TheCounterIncrements()
{
    using var host = BnTestHost.Mount<CounterPage>();

    Assert.Equal("count 0", host.Tree.FindText("count 0").Text);

    await host.ClickAsync(host.Tree.FindAll("button")[0]);

    Assert.Equal("count 1", host.Tree.FindAll("text")[0].Text);
}
```

`host.Tree` is the rendered widget tree: `Root`, `Children`, `Text`, `Styles`,
`Props`, `Events`, plus `FindAll(nodeType)` and `FindText(text)`. Drive interaction
with `ClickAsync`, `ChangeAsync`, or `DispatchAsync` for anything else.

## What it gives you that hand-rolling does not

**Children come back in real sibling order.** Blazor's render queue does not create
children in the order they appear — a chained child component renders after its
later siblings — so the framework carries each node's own placement and the shells
replay it. This package replays the same algorithm. A test that walked the render
output naively would pass while your app rendered in a different order than the
test asserts.

**Text reads off the node you wrote.** `<BnText Text="Hello" />` renders a node with
a text child, and the shells fold that child into its parent rather than giving it a
widget. The tree does the same, so `node.Text` is `"Hello"` and not `null`.

**It does not expose the framework's internals.** The renderer and its patch model
are explicitly not public API and are free to change; this package absorbs them so
your tests do not depend on them.

## What it does not do

- **No layout.** It runs the real renderer and the real components, not Yoga — so it
  answers *"what did my page render?"*, never *"how big is it?"*.
- **No device.** Register a `DevHostBridge` (from `BlazorNative.Core`) through
  `Mount`'s `configureServices` when your page injects `IMobileBridge`.
- **No assertions of its own.** The tree is data; your own xUnit/NUnit/TUnit
  assertions do the work.

## Stability

**Provisional.** Usable and supported, but not yet frozen — the shape may move in a
minor version while real consumer suites shake it out. See the
[API stability page](https://marcelroozekrans.github.io/BlazorNative/docs/api-stability).

---

Part of [BlazorNative](https://github.com/MarcelRoozekrans/BlazorNative) — write your
mobile app in Blazor, ship real native widgets.
