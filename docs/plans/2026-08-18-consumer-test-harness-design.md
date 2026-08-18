# Consumer test harness — design (#25)

**Status:** design + first slice. Written because the shape is not obvious and one
decision (an 8th package) runs against a four-times-recorded precedent, so it should
be argued in one place rather than discovered in a diff.

---

## 1. Why this is not optional, and not "growth"

`NativeRenderer` is tier **NOT-API**, `[EditorBrowsable(Never)]`, and making it
`internal` is recorded as **1.0 criterion S3**. `AddBlazorNativeRenderer` is
PROVISIONAL. Those two are *the consumer's only way to mount a page and see what it
rendered* — the `samples/ConsumerSmoke` program is the existence proof, and it reaches
straight into both.

So **S3 cannot execute until this exists.** Today the choice is "break every consumer
test" or "never do S3". That makes #25 a precondition for already-planned 1.0 work, not
a feature beyond it. `DevHostBridge` was left tier PROVISIONAL *specifically* so this
work could reshape it (api-tiers §4.2) — the runway was deliberately left open.

## 2. The problem nobody had named

A test harness must let an author assert **what their page rendered**. The only
description of that today is `RenderPatch[]` — and the whole patch hierarchy is
**NOT-API**, explicitly *"the renderer's in-memory model, not the wire format… the
framework re-shapes it freely"*.

So the obvious plan — "make `GoldenAssertions` public and ship it" — is wrong twice
over:

1. It would publish an assertion vocabulary **over types a consumer is told not to
   bind to**, freezing the in-memory model by the back door.
2. It hands the author the wrong noun. Nobody wants to assert *"there is a
   `CreateNodePatch` with `InsertIndex = -1`"*. They want *"the root has three
   children and the second is a text reading 'Hello'"*.

**The harness therefore needs its own supported projection**: a materialized widget
tree, computed from the frames, that survives the patch model changing underneath it.
That projection — not the patch list — is the thing we freeze at 1.0.

## 3. Where it lives — an 8th package, and why the precedent does not apply

**Decision: a new `BlazorNative.Testing` package.**

The "no 8th package" rule is real and recorded four times (phases 9.1/9.2/9.3 each
added a capability to an existing package) and pinned by `PackagePurityTests`. It does
not apply here, and the distinction is not a loophole:

- Those decisions were about **not splitting capabilities** — `IGeolocation` belongs
  beside `ICamera`, and a package per capability would be a taxonomy with no consumer
  benefit. This is not a capability.
- The pin's own words are *"nothing else under src/ may grow a csproj **without joining
  this pin**"* and 8.1's design says *"a new package **joins all five places
  deliberately**"*. That is a **process**, not a prohibition.

The positive case is the deciding one: **this is a dev-time-only dependency**. An app
references it from its *test* project; it must never be in the app's own reference
graph. The alternative — putting the harness beside `DevHostBridge` in
`BlazorNative.Core` — would ship test-double machinery into **every production binary**
and, worse, freeze it as part of Core's public API at 1.0. A test harness is exactly
the surface that should stay free to move for longer than the runtime does.

Tier: **PROVISIONAL** at introduction, for the same reason `DevHostBridge` is — the
second end of the contract (real consumer tests) does not exist yet.

## 4. The surface

```csharp
using var host = BnTestHost.Mount<MyPage>();          // or Mount<T>(parameters)

BnTestNode root = host.Tree.Root;
Assert.Equal("view", root.NodeType);
Assert.Equal(3, root.Children.Count);
Assert.Equal("Hello", root.Children[1].Text);
Assert.Equal("#336699", root.Children[0].Styles["backgroundColor"]);

await host.ClickAsync(root.Children[2]);              // drive an event
Assert.Equal("Tapped", host.Tree.Root.Children[1].Text);
```

Three types, and no patch record among them:

| Type | What it is |
|---|---|
| `BnTestHost` | Owns the renderer and the frame capture. `Mount<T>`, `Tree`, `ClickAsync`, `ChangeAsync`, `Frames` (count only), `Dispose`. |
| `BnTestTree` | The materialized tree after every frame so far. `Root`, `FindAll(nodeType)`, `FindText(string)`. |
| `BnTestNode` | `NodeType`, `Children` (final sibling order), `Text`, `Styles`, `Props`, `Events`. |

**`Children` is the load-bearing member and the reason this cannot be a thin wrapper.**
Producing it *is* the shells' insert algorithm — walk the creates in patch order, append
on `InsertIndex < 0`, insert at the index otherwise. Blazor's FIFO render queue does not
create children in sibling order, so a consumer who re-derives this by hand gets a test
that passes while their app renders in the wrong order. Shipping it is most of the value
here; `GoldenAssertions.ChildrenOf` is the same algorithm, stated once, and the internal
suites should end up on this projection too rather than keeping a second copy.

## 5. What is deliberately NOT in the first slice

- **No shell-side harness.** The Kotlin/Swift `SyntheticHost`s stay in-repo. Shipping
  them means shipping Gradle/XCTest sources, which is a distribution problem, not an
  API one, and the .NET half is where the missing capability actually is.
- **No assertion library.** No `ShouldHaveChild(...)`. The projection is data; the
  author's own xUnit/NUnit/TUnit assertions do the work. An assertion DSL is a taste
  decision that would freeze at 1.0 for no capability gained.
- **No frame-by-frame history.** `Tree` is the state after all frames. Asserting *how
  many frames* a change took is renderer-internal behaviour, and pinning it in consumer
  tests would make every renderer optimisation a breaking change.

## 6. What this unblocks, and the order

1. This package ships (PROVISIONAL).
2. `website/docs/testing.md` documents it, plus `DevHostBridge` as the bridge double.
3. The template gains a test project so `dotnet new` produces something testable.
4. **Then S3 can execute** — `NativeRenderer` goes `internal` with a supported
   replacement in place, which is the whole point.

Steps 2–4 are follow-ups, not preconditions; each is small and independently useful.
