---
id: rest-backends
title: Firebase and other REST backends
sidebar_label: Firebase & REST backends
---

# Firebase and other REST backends

**"Does it support Firebase?" is three questions, and the first one is already yes.**

Anything reachable over HTTP works today from .NET, through the `HttpClient` the framework
registers. No native code, no new package, no waiting.

```csharp
BlazorNativeApp.ConfigureServices(services => services.AddBlazorNativeHttp());
```

```razor
@code {
    [Inject] public HttpClient Http { get; set; } = default!;

    private async Task LoadConfig()
    {
        var json = await Http.GetStringAsync(
            "https://firebaseremoteconfig.googleapis.com/v1/projects/PROJECT/remoteConfig");
        // …
    }
}
```

That `HttpClient` routes through the host bridge, so the shell owns permissions, proxies and
lifecycle. Use it rather than `new HttpClient()` — an analyzer (BN0011) will tell you if you forget.

## What works, what doesn't, and why

| Firebase surface | Today | Why |
|---|---|---|
| **Remote Config** | ✅ works | Plain REST, JSON |
| **Cloud Functions** (callable over HTTPS) | ✅ works | Plain REST, JSON |
| **Firestore — one-shot reads/writes** | ✅ works | The REST API is request/response |
| **Auth — REST and token flows** | ✅ works | Plain REST, JSON |
| **Cloud Storage — metadata, signed URLs, delete** | ✅ works | JSON control plane |
| **Cloud Storage — uploading or downloading the bytes** | ❌ not yet | **Bodies cross the bridge as UTF-8 text.** Binary payloads are unsupported in either direction |
| **Firestore / RTDB live listeners** | ❌ not yet | Needs streaming — see below |
| **FCM push** | ❌ not yet | Needs a native bridge capability |
| **Analytics, Crashlytics, native-UI Auth** | ❌ not yet | Native SDK surfaces, not REST |

### The two caveats, stated precisely

**Responses are one-shot and fully buffered.** The shell buffers the whole body and hands it over
once, so the handler never sees a partial body and cannot expose one. `ResponseHeadersRead`,
`ReadAsStreamAsync` and friends still *work* — they just yield the already-complete body. Server-Sent
Events, incrementally-read chunked responses and long-polling therefore degrade to "wait for the
whole response", and there is no backpressure because nothing is incremental. That is what rules out
Firestore's live listeners. Streaming is an ABI-shaped change rather than a configuration switch —
tracked in [#285](https://github.com/MarcelRoozekrans/BlazorNative/issues/285).

**Bodies are text (UTF-8), in both directions.** Request bodies are read as strings before dispatch;
response bodies arrive as `StringContent`. Binary or non-UTF-8 payloads are unsupported. For files
and images, use a shell capability that returns a **path** — the pattern the camera already uses —
rather than moving bytes through this handler.

:::note This is why the Storage row is split
Storage's JSON control plane (metadata, signed URLs, delete) is ordinary REST and works. Moving the
actual object bytes does not, because of the text-only rule above. A signed URL handed to a native
downloader is the workable path today.
:::

## None of this is Firebase-specific

Everything here applies to any REST backend — Supabase's REST surface, an API of your own, a
third-party service. The rules are the same three: **request/response only, text only, through the
registered client.**

## What's on the roadmap

| Gap | Issue |
|---|---|
| Streaming HTTP / WebSocket — unblocks SSE, chunked, long-poll, and realtime listeners | [#285](https://github.com/MarcelRoozekrans/BlazorNative/issues/285) |
| Remote push (FCM / APNs) as a provider-agnostic bridge capability | [#284](https://github.com/MarcelRoozekrans/BlazorNative/issues/284) |

Neither is scheduled. They are listed so the boundary is visible, not to imply a date.
