using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    BlazorNative.Analyzers.MobilePolicyAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace BlazorNative.Analyzers.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// MobilePolicyAnalyzerTests — Phase 4.1 Gate 1
//
// The four MobilePolicy survivors (BN0004/BN0010/BN0011/BN0013), rescoped for
// the NativeAOT era. Per rule: fires-on-bad + silent-on-good; BN0011 gets the
// handler-ctor negative edge (design §4).
//
// Harness notes are in BridgeAsyncHandlerAnalyzerTests: Net90 reference
// assemblies + inline stubs for BlazorNative.Core shapes; {|BNxxxx:...|}
// markup asserts diagnostic IDs + spans (not message text).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MobilePolicyAnalyzerTests
{
    // Inline stub of the IMobileBridge fetch surface — the "good" network shape
    // BN0010 points users at. Same namespace/type/member names as the real
    // BlazorNative.Core (which can't be referenced: net10 Core.dll vs Net90
    // reference assemblies → CS1705).
    private const string BridgeStub = """

        namespace BlazorNative.Core
        {
            public readonly record struct BridgeHttpRequest(string Url, string Method = "GET", string? Body = null);
            public readonly record struct BridgeHttpResponse(int StatusCode, string Body);

            public interface IMobileBridge
            {
                System.Threading.Tasks.ValueTask<BridgeHttpResponse> FetchAsync(
                    BridgeHttpRequest request,
                    System.Threading.CancellationToken ct = default);
            }
        }
        """;

    private static VerifyCS Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
    };

    // ── BN0004 — Thread.Sleep ────────────────────────────────────────────────

    [Fact]
    public async Task BN0004_FiresOnThreadSleep()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    {|BN0004:System.Threading.Thread.Sleep(10)|};
                }
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0004_SilentOnTaskDelay()
    {
        var source = """
            using System.Threading.Tasks;
            public class C
            {
                public async Task M() => await Task.Delay(10);
            }
            """;
        await Test(source).RunAsync();
    }

    // ── BN0010 — raw sockets ─────────────────────────────────────────────────

    [Fact]
    public async Task BN0010_FiresOnSocketCreation()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    using var client = {|BN0010:new System.Net.Sockets.TcpClient()|};
                }
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0010_SilentOnBridgeFetch()
    {
        var source = """
            using System.Threading.Tasks;
            using BlazorNative.Core;
            public class C
            {
                public async Task M(IMobileBridge bridge)
                {
                    var response = await bridge.FetchAsync(new BridgeHttpRequest("https://example.com"));
                    _ = response.StatusCode;
                }
            }
            """ + BridgeStub;
        await Test(source).RunAsync();
    }

    // ── BN0011 — parameterless HttpClient only ───────────────────────────────

    [Fact]
    public async Task BN0011_FiresOnParameterlessHttpClient()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    using var client = {|BN0011:new System.Net.Http.HttpClient()|};
                }
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0011_SilentOnHttpClientWithHandler()
    {
        // The handler-ctor negative (design §4): BlazorNative.Http itself
        // constructs HttpClient over BridgeHttpHandler — that shape stays legal.
        var source = """
            public class C
            {
                public void M()
                {
                    using var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler());
                }
            }
            """;
        await Test(source).RunAsync();
    }

    // ── BN0013 — Process ─────────────────────────────────────────────────────

    [Fact]
    public async Task BN0013_FiresOnProcessStart()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    {|BN0013:System.Diagnostics.Process.Start("ls")|};
                }
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0013_FiresOnProcessCreation()
    {
        var source = """
            public class C
            {
                public void M()
                {
                    using var p = {|BN0013:new System.Diagnostics.Process()|};
                }
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0013_SilentOnStopwatch()
    {
        // Other System.Diagnostics types stay legal — only Process* is flagged.
        var source = """
            public class C
            {
                public long M()
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    return sw.ElapsedMilliseconds;
                }
            }
            """;
        await Test(source).RunAsync();
    }
}
