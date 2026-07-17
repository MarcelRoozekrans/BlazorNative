# BlazorNative.Http

The [BlazorNative](https://marcelroozekrans.github.io/BlazorNative/) HTTP bridge:
`BridgeHttpHandler` routes `HttpClient` traffic through the hosting native shell's
fetch bridge, so Blazor components use plain `HttpClient`/`IHttpClientFactory` on
NativeAOT while the shell owns the platform networking stack, permissions, and
proxy rules.

## Where it sits

Your app → `BlazorNative.Components` → `BlazorNative.Renderer` →
`BlazorNative.Runtime` → the native shells. **Http is the bridge handler alongside
that stack**: the shell owns the sockets, your components own the requests.

## What you use from it

```csharp
// Components just take HttpClient — the bridge handler is wired underneath:
[Inject] public HttpClient Http { get; set; } = default!;
var todos = await Http.GetFromJsonAsync<Todo[]>("https://example.com/api/todos");
```

## Status

Pre-1.0 (`0.x`): BlazorNative is a proof of concept heading toward a
stable release. The ABI and component surface may still move between minor versions.

License: MIT · Docs: <https://marcelroozekrans.github.io/BlazorNative/>
