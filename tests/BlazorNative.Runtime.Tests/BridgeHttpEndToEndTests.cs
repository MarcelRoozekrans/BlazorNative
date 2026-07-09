using BlazorNative.Core;
using BlazorNative.Http;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.1 Task 3 — BridgeHttpHandler end-to-end on the host CLR: the
// production-shaped DI graph (Core + Renderer + Http, NativeShellBridge
// registered LAST so it overrides WasiBridge's [Singleton(As=IMobileBridge)]
// registration — same order HostSession uses) plus the HttpClient-factory
// plumbing. A plain injected HttpClient GET must round-trip through the
// registered fake callbacks: BridgeHttpHandler → NativeShellBridge.FetchAsync
// → FakeShellHost.FetchBegin → CompleteFetch → HttpResponseMessage.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("native-shell-bridge")]
public sealed class BridgeHttpEndToEndTests
{
    [Fact]
    public async Task InjectedHttpClient_Get_RoundTripsThroughFakeCallbacks()
    {
        FakeShellHost.Reset();
        FakeShellHost.AutoCompleteFetch = true;
        FakeShellHost.AutoCompleteStatus = 200;
        FakeShellHost.AutoCompleteBody = """{"pong":true}""";
        FakeShellHost.AutoCompleteHeadersJson = """{"Content-Type":"application/json","X-Shell":"fake"}""";
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        try
        {
            var services = new ServiceCollection();
            services.AddBlazorNativeCoreServices();
            services.AddBlazorNativeRendererServices();
            services.AddBlazorNativeHttp(); // BridgeHttpHandler as primary handler + HttpClient factory
            services.AddSingleton<IMobileBridge, NativeShellBridge>(); // last wins over WasiBridge
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.IsType<NativeShellBridge>(provider.GetRequiredService<IMobileBridge>());

            HttpClient client = provider.GetRequiredService<HttpClient>();
            HttpResponseMessage response = await client.GetAsync("http://bridge.test/ping");

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("""{"pong":true}""", await response.Content.ReadAsStringAsync());
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("fake", Assert.Single(response.Headers.GetValues("X-Shell")));

            // The fake host saw the real request.
            Assert.Equal("http://bridge.test/ping", FakeShellHost.LastFetchUrl);
            Assert.Equal("GET", FakeShellHost.LastFetchMethod);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }
}
