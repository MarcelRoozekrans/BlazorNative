using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    BlazorNative.Analyzers.InteropBoundaryAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace BlazorNative.Analyzers.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// InteropBoundaryAnalyzerTests — Phase 4.1 Gate 1
//
// The two new [UnmanagedCallersOnly] C-ABI boundary rules:
//
//   BN0020 — exceptions must not escape an export: the method body must be a
//            single top-level try whose catch-all (bare `catch` or unfiltered
//            `catch (Exception)`) does not rethrow. Exports.cs is the
//            reference conformer (rc-code + capture-slot contract).
//   BN0021 — explicit C ABI: both EntryPoint = "..." and
//            CallConvs = new[] { typeof(CallConvCdecl) } required.
//
// Both diagnostics anchor on the method identifier. Test sources are crafted
// so only the rule under test fires (BN0021 tests use wrapped bodies; BN0020
// tests use fully attributed exports).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class InteropBoundaryAnalyzerTests
{
    private static VerifyCS Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
    };

    // ── BN0020 — exception escape ────────────────────────────────────────────

    [Fact]
    public async Task BN0020_FiresOnUnwrappedBody()
    {
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class NativeExports
            {
                [UnmanagedCallersOnly(EntryPoint = "bn_bad", CallConvs = new[] { typeof(CallConvCdecl) })]
                public static int {|BN0020:Bad|}(int x)
                {
                    return Compute(x);
                }

                private static int Compute(int x) => x + 1;
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0020_SilentOnTopLevelTryCatchAll()
    {
        // The wrapped-body negative (design §4): unfiltered catch (Exception)
        // and bare catch both count as catch-all.
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class NativeExports
            {
                [UnmanagedCallersOnly(EntryPoint = "bn_good", CallConvs = new[] { typeof(CallConvCdecl) })]
                public static int Good(int x)
                {
                    try
                    {
                        return Compute(x);
                    }
                    catch (Exception)
                    {
                        return 2;
                    }
                }

                [UnmanagedCallersOnly(EntryPoint = "bn_good_bare", CallConvs = new[] { typeof(CallConvCdecl) })]
                public static int GoodBareCatch(int x)
                {
                    try
                    {
                        return Compute(x);
                    }
                    catch
                    {
                        return 2;
                    }
                }

                private static int Compute(int x) => x + 1;
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0020_FiresOnRethrowInsideCatch()
    {
        // A catch-all that rethrows still lets the exception cross the C-ABI.
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class NativeExports
            {
                [UnmanagedCallersOnly(EntryPoint = "bn_rethrow", CallConvs = new[] { typeof(CallConvCdecl) })]
                public static int {|BN0020:Rethrows|}(int x)
                {
                    try
                    {
                        return Compute(x);
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }

                private static int Compute(int x) => x + 1;
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0020_FiresOnRethrowInSpecificCatchBesideCatchAll()
    {
        // A throw in ANY catch clause of the top-level try escapes the
        // boundary — a catch-all sibling does not intercept a rethrow from a
        // more specific catch.
        var source = """
            using System;
            using System.IO;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class NativeExports
            {
                [UnmanagedCallersOnly(EntryPoint = "bn_specific_rethrow", CallConvs = new[] { typeof(CallConvCdecl) })]
                public static int {|BN0020:SpecificRethrow|}(int x)
                {
                    try
                    {
                        return Compute(x);
                    }
                    catch (IOException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        return 2;
                    }
                }

                private static int Compute(int x) => x + 1;
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0020_FiresOnFilteredCatch()
    {
        // `catch (Exception) when (...)` is not a catch-all — the filter can
        // decline and the exception escapes.
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class NativeExports
            {
                [UnmanagedCallersOnly(EntryPoint = "bn_filtered", CallConvs = new[] { typeof(CallConvCdecl) })]
                public static int {|BN0020:Filtered|}(int x)
                {
                    try
                    {
                        return Compute(x);
                    }
                    catch (Exception) when (x > 0)
                    {
                        return 2;
                    }
                }

                private static int Compute(int x) => x + 1;
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0020_FiresOnExpressionBodiedExport()
    {
        // The heuristic is deliberately strict: expression-bodied exports have
        // no top-level try/catch shape, so they are flagged.
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class NativeExports
            {
                private static readonly IntPtr s_version = IntPtr.Zero;

                [UnmanagedCallersOnly(EntryPoint = "bn_version", CallConvs = new[] { typeof(CallConvCdecl) })]
                public static IntPtr {|BN0020:Version|}() => s_version;
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0020_SilentOnNonExportMethod()
    {
        // Ordinary managed methods are out of scope — no attribute, no rule.
        var source = """
            public static class Helpers
            {
                public static int Plain(int x)
                {
                    return x + 1;
                }
            }
            """;
        await Test(source).RunAsync();
    }

    // ── BN0021 — explicit EntryPoint + CallConvCdecl ─────────────────────────

    [Fact]
    public async Task BN0021_FiresOnMissingCallConvs()
    {
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class NativeExports
            {
                [UnmanagedCallersOnly(EntryPoint = "bn_no_callconv")]
                public static int {|BN0021:NoCallConv|}(int x)
                {
                    try
                    {
                        return x;
                    }
                    catch (Exception)
                    {
                        return 2;
                    }
                }
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0021_FiresOnMissingEntryPoint()
    {
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class NativeExports
            {
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
                public static int {|BN0021:NoEntryPoint|}(int x)
                {
                    try
                    {
                        return x;
                    }
                    catch (Exception)
                    {
                        return 2;
                    }
                }
            }
            """;
        await Test(source).RunAsync();
    }

    [Fact]
    public async Task BN0021_SilentOnFullyAttributed()
    {
        // The fully-attributed negative (design §4): EntryPoint + CallConvCdecl
        // both pinned — the Exports.cs target shape.
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class NativeExports
            {
                [UnmanagedCallersOnly(EntryPoint = "bn_full", CallConvs = new[] { typeof(CallConvCdecl) })]
                public static int Full(int x)
                {
                    try
                    {
                        return x;
                    }
                    catch (Exception)
                    {
                        return 2;
                    }
                }
            }
            """;
        await Test(source).RunAsync();
    }
}
