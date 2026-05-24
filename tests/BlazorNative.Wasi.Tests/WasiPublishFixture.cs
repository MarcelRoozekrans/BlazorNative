using System.Diagnostics;

namespace BlazorNative.Wasi.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// WasiPublishFixture
//
// Per-class fixture that publishes BlazorNative.WasiHost for the wasi-wasm RID
// once before the boot tests run, exposes the produced .wasm path, and fails
// fast with an actionable error if wasi-sdk isn't installed.
//
// Note: publishes WasiHost (not Core) because the WASI entry point lives in
// WasiHost — see src/BlazorNative.WasiHost/BlazorNative.WasiHost.csproj
// header for the circular-dep rationale.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class WasiPublishFixture : IDisposable
{
    /// <summary>Path to the Mono runtime .wasm. Pass this to wasmtime; it loads
    /// the managed app assembly (BlazorNative.WasiHost.dll) from the same dir
    /// at runtime via runtimeconfig.json.</summary>
    public string WasmPath { get; }

    /// <summary>The publish output dir. wasmtime must be invoked with
    /// <c>--dir=&lt;this&gt;</c> for WASI filesystem access to the managed dlls.</summary>
    public string AppBundleDir { get; }

    /// <summary>The assembly name (without .dll) to pass as wasmtime's program arg.</summary>
    public string AppAssemblyName => "BlazorNative.WasiHost";

    public WasiPublishFixture()
    {
        var repoRoot = FindRepoRoot();

        // The wasi-experimental workload puts the AOT'd app-specific .wasm in
        // bin/Release/net10.0/wasi-wasm/AppBundle/ — NOT in the --output dir
        // (which only gets the IL .dlls + the generic dotnet.wasm runtime).
        // Publish without --output so the AppBundle lands at its canonical path.
        AppBundleDir = Path.Combine(repoRoot,
            "src", "BlazorNative.WasiHost", "bin", "Release", "net10.0", "wasi-wasm", "AppBundle");

        var psi = new ProcessStartInfo("dotnet",
            "publish src/BlazorNative.WasiHost/BlazorNative.WasiHost.csproj " +
            "-r wasi-wasm -c Release")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Ensure WASI_SDK_PATH is visible to the publish subprocess. If the
        // user-scope env var was set in this Windows session but the parent
        // process started before that change, propagate it explicitly.
        var sdkPath = Environment.GetEnvironmentVariable("WASI_SDK_PATH")
            ?? Environment.GetEnvironmentVariable("WASI_SDK_PATH", EnvironmentVariableTarget.User);
        if (!string.IsNullOrEmpty(sdkPath))
            psi.Environment["WASI_SDK_PATH"] = sdkPath;

        var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds);

        if (proc.ExitCode != 0)
        {
            var wasiSdkHint = stderr.Contains("wasi-sdk", StringComparison.OrdinalIgnoreCase)
                || stdout.Contains("wasi-sdk", StringComparison.OrdinalIgnoreCase)
                ? "\n\nNOTE: dotnet publish requires wasi-sdk. Install from https://github.com/WebAssembly/wasi-sdk/releases and set WASI_SDK_PATH (user env var) — see docs/plans/2026-05-23-phase-1.2-implementation-plan.md Task 0."
                : "";
            throw new InvalidOperationException(
                $"dotnet publish failed (exit={proc.ExitCode}).{wasiSdkHint}\n\n" +
                $"STDOUT:\n{stdout}\n\nSTDERR:\n{stderr}");
        }

        // Mono-AOT for wasi-wasm produces an app-specific .wasm at
        // bin/.../AppBundle/<AppName>.wasm with the IL baked in. This is the
        // single-file artifact wasmtime needs to invoke directly (no separate
        // dotnet.wasm runtime + .dll dance).
        WasmPath = Path.Combine(AppBundleDir, "BlazorNative.WasiHost.wasm");
        if (!File.Exists(WasmPath))
            throw new FileNotFoundException(
                "Expected app-specific .wasm not produced at " + WasmPath +
                "\n\nDirectory contents:\n  " +
                (Directory.Exists(AppBundleDir)
                    ? string.Join("\n  ", Directory.GetFiles(AppBundleDir, "*", SearchOption.AllDirectories))
                    : "(AppBundle dir does not exist)"));
    }

    public void Dispose() { /* leave artifacts/wasi-publish-test in place for inspection */ }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException(
                "Could not locate BlazorNative.sln walking up from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
