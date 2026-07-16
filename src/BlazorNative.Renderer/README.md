# BlazorNative.Renderer

The [BlazorNative](https://github.com/MarcelRoozekrans/BlazorNative) renderer: it
drives Blazor's RenderTree diffing into a native widget tree and emits binary UI
patch frames that platform shells (Kotlin/Android, Swift/iOS) apply to real native
views. NativeAOT/trim-compatible.

## Where it sits

Your app → `BlazorNative.Components` → **`BlazorNative.Renderer`** →
`BlazorNative.Runtime` → the native shells. The renderer is the diff engine in the
middle: components render into it, shells consume the patch frames it emits.

## What you use from it

```csharp
var services = new ServiceCollection().AddBlazorNativeRenderer();
await using var provider = services.BuildServiceProvider();
var renderer = provider.GetRequiredService<NativeRenderer>();
renderer.Frames += (frame, _) => { /* apply patches */ return ValueTask.CompletedTask; };
await renderer.MountAsync<MyRootComponent>(ParameterView.Empty);
```

In a real app the runtime hosts the renderer for you — direct use is a harness/test
concern.

## Status

Preview (`1.0.0-preview.N`): BlazorNative is a proof of concept heading toward a
stable release. The ABI and component surface may still move between previews.

License: MIT · Source: <https://github.com/MarcelRoozekrans/BlazorNative>
