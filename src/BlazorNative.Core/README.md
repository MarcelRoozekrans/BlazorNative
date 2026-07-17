# BlazorNative.Core

Core contracts for [BlazorNative](https://marcelroozekrans.github.io/BlazorNative/):
the shell bridge and interop abstractions, the `INavigationManager` contract, and the
DI-facing primitives shared by every other BlazorNative package.

## Where it sits

Your app → `BlazorNative.Components` → `BlazorNative.Renderer` → `BlazorNative.Runtime`
→ the native shells. **Core is the contracts layer underneath all of them** — it names
no renderer, no runtime, no shell.

## What you use from it

```csharp
// Navigate from any component through the Core contract:
[Inject] public INavigationManager Navigation { get; set; } = default!;
Navigation.NavigateTo("/settings");
```

You rarely reference Core directly — it arrives transitively with the packages above.

## Status

Preview (`1.0.0-preview.N`): BlazorNative is a proof of concept heading toward a
stable release. The ABI and component surface may still move between previews.

License: MIT · Docs: <https://marcelroozekrans.github.io/BlazorNative/>
