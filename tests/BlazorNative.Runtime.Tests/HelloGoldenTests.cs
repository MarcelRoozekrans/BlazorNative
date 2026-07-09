using System.Text.Json;
using BlazorNative.Core;
using BlazorNative.NativeHost;
using BlazorNative.Renderer;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d Task 6 — the Hello golden frame fixture.
//
// Mounts NativeHost's HelloComponent, captures the first RenderFrame via the
// Frames event, normalizes the nondeterministic bits (frameId/timestampMs →
// 0, in the envelope AND the CommitFramePatch), serializes through
// RendererJsonContext, and asserts byte-equality with the committed fixture:
//
//   tests/BlazorNative.Runtime.Tests/Fixtures/hello-frame.json   (this side)
//   src/BlazorNative.Jni/src/test/resources/hello-frame.json     (JVM side)
//
// The SAME fixture is what the Kotlin golden test (NativeFrameAdapterTest.kt)
// compares the native-callback path against — together they lock the wire
// shape on both sides of the C ABI.
//
// REGENERATION (only for an intentional Hello/protocol change):
//   1. set BLAZORNATIVE_RECORD_GOLDEN=1
//   2. dotnet test tests/BlazorNative.Runtime.Tests --filter HelloGoldenTests
//      (the test writes BOTH copies into the repo, then deliberately fails so
//      a record run can never pass CI)
//   3. INSPECT the diff — the fixture must show the Phase 2.8 Hello shape
//      (outer view #FFEEAA + padding 16, inner view fontSize 24 + text
//      "Hello, BlazorNative!", button + "Tap", input + "Type here...")
//   4. unset the env var, re-run green, commit BOTH copies + the Kotlin test.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class HelloGoldenTests
{
    private const string FixtureName = "hello-frame.json";

    [Fact]
    public async Task MountHello_NormalizedFrame_MatchesGoldenFixture()
    {
        var services = new ServiceCollection();
        services.AddBlazorNativeCoreServices();
        services.AddBlazorNativeRendererServices();
        services.AddBlazorNativeHttpServices();
        using var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();

        var tcs = new TaskCompletionSource<RenderFrame>();
        ZeroAlloc.AsyncEvents.AsyncEvent<RenderFrame> handler = (f, _) =>
        {
            tcs.TrySetResult(f);
            return ValueTask.CompletedTask;
        };
        renderer.Frames += handler;
        RenderFrame frame;
        try
        {
            await renderer.MountAsync<HelloComponent>();
            frame = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            renderer.Frames -= handler;
        }

        string json = JsonSerializer.Serialize(
            Normalize(frame), RendererJsonContext.Default.RenderFrame);

        if (Environment.GetEnvironmentVariable("BLAZORNATIVE_RECORD_GOLDEN") == "1")
        {
            RecordFixture(json);
            Assert.Fail(
                "Golden fixture RECORDED (both copies). Inspect the diff, unset " +
                "BLAZORNATIVE_RECORD_GOLDEN, and re-run to verify green.");
        }

        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName);
        Assert.True(File.Exists(fixturePath),
            $"Missing golden fixture at {fixturePath} — see header for regeneration steps.");
        string expected = File.ReadAllText(fixturePath).Trim();

        Assert.Equal(expected, json);
    }

    /// <summary>Zeroes the nondeterministic fields: envelope frameId +
    /// timestampMs, and the same pair inside CommitFramePatch.</summary>
    private static RenderFrame Normalize(RenderFrame frame) => new(
        FrameId: 0,
        TimestampMs: 0,
        Patches: frame.Patches
            .Select(p => p is CommitFramePatch ? new CommitFramePatch(0, 0) : p)
            .ToArray());

    /// <summary>The golden comparison alone can't catch a hand-edit that keeps
    /// the Kotlin copy parse-equivalent while drifting its bytes (whitespace,
    /// key order, escaping) — the JVM golden test parses, so it would sail
    /// past. Pin the two committed copies to BYTE equality.</summary>
    [Fact]
    public void CommittedFixtureCopies_AreByteIdentical()
    {
        string root = FindRepoRoot();
        byte[] dotnetCopy = File.ReadAllBytes(DotnetFixturePath(root));
        byte[] kotlinCopy = File.ReadAllBytes(KotlinFixturePath(root));
        Assert.Equal(dotnetCopy, kotlinCopy);
    }

    /// <summary>Walks up from the test output dir to the repo root (the dir
    /// holding .git).</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string DotnetFixturePath(string root) => Path.Combine(
        root, "tests", "BlazorNative.Runtime.Tests", "Fixtures", FixtureName);

    private static string KotlinFixturePath(string root) => Path.Combine(
        root, "src", "BlazorNative.Jni", "src", "test", "resources", FixtureName);

    private static void RecordFixture(string json)
    {
        string root = FindRepoRoot();
        string dotnetCopy = DotnetFixturePath(root);
        string kotlinCopy = KotlinFixturePath(root);

        Directory.CreateDirectory(Path.GetDirectoryName(dotnetCopy)!);
        Directory.CreateDirectory(Path.GetDirectoryName(kotlinCopy)!);
        File.WriteAllText(dotnetCopy, json);
        File.WriteAllText(kotlinCopy, json);
    }
}
