using System.Diagnostics;

namespace BlazorNative.Wasi.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// WasmtimeRunner
//
// Shared helper for both BootSmoke and ExportSmoke. Wraps the wasmtime
// subprocess invocation pattern that took several iterations in Phase 1.2 to
// get right (env stripping, relative --dir, WorkingDirectory, -Shttp for
// wasi:http imports, wasmtime resolution across PATH / user-PATH-env /
// standard install dirs).
//
// Signature:
//   extraArgsBeforeWasm   wasmtime flags between `run` and the .wasm filename
//                         (e.g. ["--invoke", "blazornative_dispatch_event"])
//   programArgs           args passed to the WASM program (e.g. the app name
//                         for Mono-WASI module load, or function args for
//                         --invoke)
// ─────────────────────────────────────────────────────────────────────────────

internal static class WasmtimeRunner
{
    public static async Task<(int exitCode, string stdout, string stderr)> Run(
        WasiPublishFixture fixture,
        string[] extraArgsBeforeWasm,
        string[] programArgs,
        TimeSpan timeout)
    {
        var wasmtimeExe = ResolveWasmtime();

        // Mono-WASI invocation:
        //   -Shttp              wasi:http/types — Mono imports it whenever
        //                       System.Net.Http is in the dep graph
        //                       (inherited via BlazorNative.Http).
        //   --dir=.             relative filesystem access (needed for ICU
        //                       data lookup); WorkingDirectory below makes
        //                       "." resolve to AppBundleDir.
        //
        // Use ArgumentList (per-arg) not Arguments (string-form) — Phase 2.3
        // env-var bridge passes JSON values that contain spaces; string-form
        // would split them across multiple wasmtime args, breaking --env.
        var psi = new ProcessStartInfo(wasmtimeExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = fixture.AppBundleDir,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("-Shttp");
        psi.ArgumentList.Add("--dir=.");
        foreach (var a in extraArgsBeforeWasm) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add($"{fixture.AppAssemblyName}.wasm");
        foreach (var a in programArgs) psi.ArgumentList.Add(a);

        // Strip DOTNET_*/MONO_*/ASPNETCORE_* env vars inherited from the test
        // runner — they flip the WASM-side Mono runtime into incompatible modes
        // (saw exit 3 before clearing in Phase 1.2 Task 13).
        foreach (var key in psi.Environment.Keys.Where(k =>
                     k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) ||
                     k.StartsWith("MONO_",   StringComparison.OrdinalIgnoreCase) ||
                     k.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase))
                 .ToList())
        {
            psi.Environment.Remove(key);
        }

        var proc = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* may already be dead */ }
            throw new TimeoutException($"wasmtime did not exit within {timeout.TotalSeconds}s");
        }
        return (proc.ExitCode,
                await proc.StandardOutput.ReadToEndAsync(),
                await proc.StandardError.ReadToEndAsync());
    }

    /// <summary>Find wasmtime on PATH or in standard install locations. Tolerant
    /// of the case where wasmtime was installed after the test runner process
    /// started — the user-scope PATH change doesn't propagate to existing
    /// processes.</summary>
    public static string ResolveWasmtime()
    {
        var fromPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(d => Path.Combine(d.Trim(), OperatingSystem.IsWindows() ? "wasmtime.exe" : "wasmtime"))
            .FirstOrDefault(File.Exists);
        if (fromPath is not null) return fromPath;

        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)?
            .Split(Path.PathSeparator)
            .Select(d => Path.Combine(d.Trim(), OperatingSystem.IsWindows() ? "wasmtime.exe" : "wasmtime"))
            .FirstOrDefault(File.Exists);
        if (userPath is not null) return userPath;

        var common = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wasmtime", "bin", "wasmtime.exe"),
            @"C:\Tools\wasmtime\wasmtime.exe",
        };
        foreach (var d in Directory.Exists(@"C:\Tools") ? Directory.GetDirectories(@"C:\Tools", "wasmtime*") : Array.Empty<string>())
        {
            var c = Path.Combine(d, "wasmtime.exe");
            if (File.Exists(c)) return c;
        }
        var found = common.FirstOrDefault(File.Exists);
        if (found is not null) return found;

        throw new FileNotFoundException(
            "wasmtime executable not found. Install from https://wasmtime.dev (or run " +
            @"`Invoke-WebRequest https://github.com/bytecodealliance/wasmtime/releases/download/v45.0.0/wasmtime-v45.0.0-x86_64-windows.zip -OutFile $env:TEMP\wasmtime.zip; Expand-Archive $env:TEMP\wasmtime.zip C:\Tools -Force`)");
    }
}
