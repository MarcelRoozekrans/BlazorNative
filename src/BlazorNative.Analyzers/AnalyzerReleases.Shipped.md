; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; Release headers must be System.Version-parseable (RS2007 rejects prerelease
; suffixes), so releases are named after the phase that shipped them:
;   2.0.0 = Phase 2.0 (the WASI-era rule set)  ·  4.1.0 = Phase 4.1 (NativeAOT rescope)

## Release 2.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
BN0001 | BlazorNative.WasiCompatibility | Error | WasiThreadingAnalyzer: new Thread(...) forbidden on WASI
BN0002 | BlazorNative.WasiCompatibility | Error | WasiThreadingAnalyzer: Task.Run(...) forbidden on WASI
BN0003 | BlazorNative.WasiCompatibility | Error | WasiThreadingAnalyzer: Parallel.For/ForEach/Invoke forbidden on WASI
BN0004 | BlazorNative.WasiCompatibility | Error | WasiThreadingAnalyzer: Thread.Sleep blocks the cooperative scheduler
BN0005 | BlazorNative.WasiCompatibility | Warning | WasiThreadingAnalyzer: sync primitives unnecessary on WASI
BN0006 | BlazorNative.WasiCompatibility | Warning | WasiThreadingAnalyzer: [ThreadStatic] has no effect on WASI
BN0010 | BlazorNative.WasiCompatibility | Error | WasiBclGapsAnalyzer: System.Net.Sockets.* unavailable on WASI
BN0011 | BlazorNative.WasiCompatibility | Warning | WasiBclGapsAnalyzer: direct HttpClient construction bypasses bridge
BN0012 | BlazorNative.WasiCompatibility | Warning | WasiBclGapsAnalyzer: file I/O sandboxed on WASI
BN0013 | BlazorNative.WasiCompatibility | Error | WasiBclGapsAnalyzer: System.Diagnostics.Process unavailable on WASI
BN0014 | BlazorNative.WasiCompatibility | Error | BridgeAsyncHandlerAnalyzer: async NativeEvents handler on Mono-WASI

## Release 4.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
BN0020 | BlazorNative.Interop | Error | InteropBoundaryAnalyzer: exceptions must not escape [UnmanagedCallersOnly] exports
BN0021 | BlazorNative.Interop | Error | InteropBoundaryAnalyzer: exports must pin EntryPoint + CallConvCdecl

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
BN0001 | BlazorNative.WasiCompatibility | Error | WASI premise dead — NativeAOT has real threads; ID retired, never reused
BN0002 | BlazorNative.WasiCompatibility | Error | WASI premise dead — thread pool exists; ID retired, never reused
BN0003 | BlazorNative.WasiCompatibility | Error | WASI premise dead — parallelism works; ID retired, never reused
BN0005 | BlazorNative.WasiCompatibility | Warning | WASI premise dead — locks meaningful again; ID retired, never reused
BN0006 | BlazorNative.WasiCompatibility | Warning | Now actively false — FrameArena uses [ThreadStatic] deliberately; ID retired, never reused
BN0012 | BlazorNative.WasiCompatibility | Warning | WASI premise dead — file I/O works in the app sandbox; ID retired, never reused

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
BN0004 | BlazorNative.MobilePolicy | Warning | BlazorNative.WasiCompatibility | Error | MobilePolicyAnalyzer: Thread.Sleep stalls the dispatch lane (advisory)
BN0010 | BlazorNative.MobilePolicy | Warning | BlazorNative.WasiCompatibility | Error | MobilePolicyAnalyzer: raw sockets bypass the host bridge (advisory)
BN0011 | BlazorNative.MobilePolicy | Warning | BlazorNative.WasiCompatibility | Warning | MobilePolicyAnalyzer: narrowed to the parameterless HttpClient ctor
BN0013 | BlazorNative.MobilePolicy | Error | BlazorNative.WasiCompatibility | Error | MobilePolicyAnalyzer: Process unsupported in Android app sandboxes
BN0014 | BlazorNative.Interop | Error | BlazorNative.WasiCompatibility | Error | BridgeAsyncHandlerAnalyzer: reworded off the Mono-WASI premise
