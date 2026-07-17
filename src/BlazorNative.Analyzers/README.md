# BlazorNative.Analyzers

Roslyn analyzers enforcing [BlazorNative](https://marcelroozekrans.github.io/BlazorNative/)
usage rules (`BN0xxx`): interop-boundary, bridge-async-handler, and mobile-policy
diagnostics for BlazorNative app and library code. A development dependency — it
ships analyzers only, no runtime assembly.

## Where it sits

Your app → `BlazorNative.Components` → `BlazorNative.Renderer` →
`BlazorNative.Runtime` → the native shells. **Analyzers ride your build** alongside
that stack and flag misuse before it reaches a device.

## What it looks like

```
warning BN0011: 'new HttpClient()' uses the default socket handler, bypassing
the host bridge. Prefer the HttpClient injected by 'AddBlazorNativeHttp()';
constructing over an explicit handler (e.g. BridgeHttpHandler) stays legal.
```

The rules' lifecycle is tracked in `AnalyzerReleases.*.md` (RS2xxx release tracking);
IDs are never reused.

## Status

Pre-1.0 (`0.x`): BlazorNative is a proof of concept heading toward a
stable release. The rule set may grow between minor versions.

License: MIT · Docs: <https://marcelroozekrans.github.io/BlazorNative/>
