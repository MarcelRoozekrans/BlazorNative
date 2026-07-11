using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    BlazorNative.Analyzers.BridgeAsyncHandlerAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace BlazorNative.Analyzers.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BridgeAsyncHandlerAnalyzerTests
//
// Phase 2.0 Layer 3 tests; the harness patterns established here are reused by
// the Phase 4.1 suites (MobilePolicyAnalyzerTests, InteropBoundaryAnalyzerTests).
// BN0014 was reworded off the Mono-WASI premise in Phase 4.1 — these tests
// assert diagnostic IDs via markup (not message text), so they pin the
// surviving detection contract unchanged.
//
// Notes on harness configuration:
//
// 1) ReferenceAssemblies.Net.Net100 doesn't exist yet (constant lags releases);
//    Net90 is used as the reference-assembly baseline. The harness uses these
//    to provide the BCL surface — the analyzer's pattern detection is
//    BCL-version-agnostic so this is fine.
//
// 2) The harness compiles a *fresh* in-memory project that does NOT inherit
//    our ProjectReferences, so we can't just `using BlazorNative.Core;`.
//    Two options: (a) MetadataReference to the real BlazorNative.Core.dll, or
//    (b) inline-stub the IMobileBridge / NativeEvent shape the analyzer matches
//    on (by namespace + type name + event name) inside the test source.
//
//    We pick (b): the real BlazorNative.Core targets net10.0 and pulls in
//    System.Runtime 10.0.0.0, but Net90 reference assemblies cap at 9.0.0.0 —
//    direct CS1705 conflict. Inline stubs are also more hermetic — the test
//    documents *exactly* what shape the analyzer keys off (namespace name
//    `BlazorNative.Core`, type name `IMobileBridge`, event name `NativeEvents`).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BridgeAsyncHandlerAnalyzerTests
{
    // Inline stub of the BlazorNative.Core surface the analyzer keys off.
    // Same namespace + type + event names = same analyzer recognition.
    private const string BridgeStub = """

        namespace BlazorNative.Core
        {
            public readonly record struct NativeEvent(string Name, string? Payload);

            public interface IMobileBridge
            {
                event System.Action<NativeEvent> NativeEvents;
            }
        }
        """;

    [Fact]
    public async Task BN0014_FiresOnAsyncLambdaSubscription()
    {
        var source = """
            using System.Threading.Tasks;
            using BlazorNative.Core;
            public class C
            {
                public void M(IMobileBridge b)
                {
                    b.NativeEvents += {|BN0014:async e => { await Task.Yield(); }|};
                }
            }
            """ + BridgeStub;

        var test = new VerifyCS
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task BN0014_FiresOnAsyncMethodReference()
    {
        // The realistic async-method-reference footgun: an `async void` method
        // that matches `Action<NativeEvent>` by signature and assigns cleanly
        // (an `async Task` method wouldn't compile against Action<T>). The
        // analyzer's IMethodSymbol.IsAsync branch picks this up — exactly the
        // path that's untested by BN0014_FiresOnAsyncLambdaSubscription.
        var source = """
            using System.Threading.Tasks;
            using BlazorNative.Core;
            public class C
            {
                public void M(IMobileBridge b)
                {
                    b.NativeEvents += {|BN0014:OnEvent|};
                }
                private async void OnEvent(NativeEvent e) { await Task.Yield(); }
            }
            """ + BridgeStub;

        var test = new VerifyCS
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task BN0014_SilentOnSyncLambda()
    {
        var source = """
            using BlazorNative.Core;
            public class C
            {
                public void M(IMobileBridge b)
                {
                    b.NativeEvents += e => { var _ = e.Name; };
                }
            }
            """ + BridgeStub;

        var test = new VerifyCS
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        // No diagnostics expected — empty ExpectedDiagnostics is the default.
        await test.RunAsync();
    }
}
