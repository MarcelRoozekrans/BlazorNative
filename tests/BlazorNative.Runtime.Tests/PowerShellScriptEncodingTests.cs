using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PowerShellScriptEncodingTests — every .ps1 in the repo starts with a UTF-8 BOM.
//
// Windows PowerShell 5.1 reads a script without a BOM in the ANSI code page,
// Windows-1252 on a default install. Every script here carries an em dash or a
// box-drawing character, and the last byte of the em dash, 0x94, decodes to a
// right curly quote, which PowerShell accepts as a string terminator. Without the
// BOM, setup.ps1 failed to parse with 17 errors, devloop.ps1 with 18 and
// test-android.ps1 with 3, under the `powershell -File …` command the docs give.
// PowerShell 7 assumes UTF-8 and ignores the BOM, so CI, which runs pwsh, never
// saw it. .editorconfig asks editors for utf-8-bom; this is what enforces it.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - Whether a script parses. A BOM'd script with a syntax error passes here.
//   - PowerShell 7-only syntax such as `??` or `?.`, which 5.1 also rejects.
//     setup.ps1 had one `??`; nothing pins its absence.
//   - Scripts under a directory whose name starts with '.', or under bin, obj or
//     node_modules: they are skipped so a worktree under .claude/ or a build
//     output never enters the population.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Every PowerShell script in the repository starts with a UTF-8 byte-order mark.</summary>
public sealed class PowerShellScriptEncodingTests
{
    private static readonly HashSet<string> SkippedDirectories =
        new(StringComparer.Ordinal) { "bin", "obj", "node_modules" };

    private static IEnumerable<string> Scripts(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.ps1"))
            yield return file;
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.') || SkippedDirectories.Contains(name))
                continue;
            foreach (var file in Scripts(sub))
                yield return file;
        }
    }

    /// <summary>Each .ps1 begins with EF BB BF, so Windows PowerShell 5.1 reads it as UTF-8.</summary>
    [Fact]
    public void EveryScript_StartsWithAUtf8Bom()
    {
        var root = BnRepo.Root();
        var scripts = Scripts(root)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        // The floor: the walk must reach the scripts the docs tell people to run.
        Assert.True(scripts.Contains("setup.ps1") && scripts.Contains("scripts/devloop.ps1"),
            $"the walk found {scripts.Count} scripts without setup.ps1 and scripts/devloop.ps1 among them: "
            + $"[{string.Join(", ", scripts)}]. A pin that cannot find its subject must fail, not pass over nothing.");

        var missing = scripts
            .Where(f =>
            {
                var head = new byte[3];
                using var s = File.OpenRead(Path.Combine(root, f));
                return s.Read(head, 0, 3) < 3 || head[0] != 0xEF || head[1] != 0xBB || head[2] != 0xBF;
            })
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} of {scripts.Count} PowerShell scripts have no UTF-8 BOM: [{string.Join(", ", missing)}]. "
            + "Windows PowerShell 5.1 reads them as Windows-1252, where an em dash ends a string. "
            + "Save them as UTF-8 with BOM.");
    }
}
