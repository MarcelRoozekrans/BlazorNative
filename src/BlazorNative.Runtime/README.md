# BlazorNative.Runtime

The [BlazorNative](https://marcelroozekrans.github.io/BlazorNative/) runtime: the 9
`blazornative_*` native exports the shells call, the host session, the binary
frame/bridge wire protocol, and the registration API through which your app names
its pages. NativeAOT/trim-compatible — your app project owns the publish head
(`PublishAot`), this library ships the exports it emits.

## Where it sits

Your app → `BlazorNative.Components` → `BlazorNative.Renderer` →
**`BlazorNative.Runtime`** → the native shells. The runtime is the boundary layer:
everything above it is managed Blazor, everything below it is Kotlin/Swift.

## What you use from it

```csharp
// At startup (a [ModuleInitializer] in a NativeAOT app), once:
BlazorNativeApp.RegisterPages(
    BlazorNativePage.Routed<HomePage>("/", nameof(HomePage)),
    BlazorNativePage.Named<DiagnosticsPage>(nameof(DiagnosticsPage)));
```

Routed pages are reachable by route (navigation/deep links) and by name; named pages
by name only. Registration is validated loudly and happens exactly once.

## Status

Preview (`1.0.0-preview.N`): BlazorNative is a proof of concept heading toward a
stable release. The ABI and component surface may still move between previews.

License: MIT · Docs: <https://marcelroozekrans.github.io/BlazorNative/>
