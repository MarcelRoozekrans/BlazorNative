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
    public string WasmPath { get; }
    public string AppBundleDir { get; }

    public WasiPublishFixture()
    {
        var repoRoot = FindRepoRoot();
        AppBundleDir = Path.Combine(repoRoot, "artifacts", "wasi-publish-test");
        if (Directory.Exists(AppBundleDir))
            Directory.Delete(AppBundleDir, recursive: true);

        var psi = new ProcessStartInfo("dotnet",
            $"publish src/BlazorNative.WasiHost/BlazorNative.WasiHost.csproj " +
            $"-r wasi-wasm -c Release --output \"{AppBundleDir}\"")
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

        // WasmSingleFileBundle=true → single bundled .wasm; probe both candidate paths.
        var candidates = new[]
        {
            Path.Combine(AppBundleDir, "AppBundle", "BlazorNative.WasiHost.wasm"),
            Path.Combine(AppBundleDir, "BlazorNative.WasiHost.wasm"),
        };
        WasmPath = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                "Expected app-specific .wasm not produced. Searched:\n  " +
                string.Join("\n  ", candidates) +
                "\n\nDirectory contents under " + AppBundleDir + ":\n  " +
                string.Join("\n  ", Directory.GetFiles(AppBundleDir, "*", SearchOption.AllDirectories)));
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
