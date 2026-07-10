using BlazorNative.Core;
using BlazorNative.Http;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.1 Task 3 — BridgeHttpHandler end-to-end on the host CLR against the
// HostSession-SHAPED DI graph: the same three registrations in the same order
// as HostSession.EnsureSession (Renderer → AddBlazorNativeHttp →
// NativeShellBridge — since Phase 3.2 deleted WasiBridge, Core registers
// nothing and NativeShellBridge is the sole IMobileBridge). A plain
// injected HttpClient GET must round-trip through the registered fake
// callbacks: BridgeHttpHandler → NativeShellBridge.FetchAsync →
// FakeShellHost.FetchBegin → CompleteFetch → HttpResponseMessage.
//
// If HostSession.EnsureSession's registrations change, update BuildHostGraph
// below to match — that mirroring IS the test's value.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BridgeHttpEndToEndTests
{
    /// <summary>The HostSession.EnsureSession registrations, verbatim —
    /// same calls, same order.</summary>
    private static ServiceProvider BuildHostGraph()
    {
        var services = new ServiceCollection();
        services.AddBlazorNativeRendererServices();
        services.AddBlazorNativeHttp(); // BridgeHttpHandler as primary handler + HttpClient factory
        services.AddSingleton<IMobileBridge, NativeShellBridge>(); // sole IMobileBridge since 3.2 (WasiBridge deleted)
        services.AddSingleton<INavigationManager, NativeNavigationManager>(); // Phase 3.5: the navigation service (DoD #7)
        return services.BuildServiceProvider();
    }

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
            await using ServiceProvider provider = BuildHostGraph();

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

    /// <summary>Phase 3.1 final-review follow-up: HostSession must register
    /// the FULL HttpClient-factory plumbing (AddBlazorNativeHttp), not just
    /// the generated handler registration — a 3.3+ component doing
    /// [Inject] HttpClient via blazornative_mount has to resolve, and its
    /// handler chain has to route through BridgeHttpHandler (proven by the
    /// fake-callback GET reaching FakeShellHost).</summary>
    [Fact]
    public async Task HostSessionShapedGraph_ResolvesInjectableHttpClient_ThroughBridgeHttpHandler()
    {
        FakeShellHost.Reset();
        FakeShellHost.AutoCompleteFetch = true;
        FakeShellHost.AutoCompleteStatus = 200;
        FakeShellHost.AutoCompleteBody = "routed";
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        try
        {
            await using ServiceProvider provider = BuildHostGraph();

            // Resolves at all — this is what AddBlazorNativeHttpServices()
            // alone did NOT provide (no IHttpClientFactory, no HttpClient).
            HttpClient client = provider.GetRequiredService<HttpClient>();

            // And its handler chain is BridgeHttpHandler: the GET can only
            // succeed by crossing the fake shell callbacks.
            HttpResponseMessage response = await client.GetAsync("http://hostsession.test/inject");

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("routed", await response.Content.ReadAsStringAsync());
            Assert.Equal("http://hostsession.test/inject", FakeShellHost.LastFetchUrl);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }
}
