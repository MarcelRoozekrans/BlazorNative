using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorNative.Renderer;
using Xunit;

namespace BlazorNative.Wasi.Tests;

[Trait("Category", "Integration")]
[Collection("Wasi")]
public sealed class FrameSelfTestParsesAsRenderFrame
{
    private readonly WasiPublishFixture _fixture;
    public FrameSelfTestParsesAsRenderFrame(WasiPublishFixture f) => _fixture = f;

    [Fact]
    public async Task Sentinel_frame_deserializes_to_RenderFrame_with_expected_patches()
    {
        const string CliPlatformInfo = """{"os":"wasmtime-cli","note":"frame-parse-test"}""";

        var (exitCode, stdout, _) = await WasmtimeRunner.Run(
            _fixture,
            extraArgsBeforeWasm: new[] { "--env", $"BLAZOR_PLATFORM_INFO={CliPlatformInfo}" },
            programArgs: Array.Empty<string>(),
            timeout: TimeSpan.FromSeconds(10));

        Assert.Equal(0, exitCode);

        // Extract the first [FRAME] line.
        var match = Regex.Match(stdout, @"^\[FRAME\] (\{.+\})$", RegexOptions.Multiline);
        Assert.True(match.Success, $"No [FRAME] line in stdout. Captured:\n{stdout}");

        var frame = JsonSerializer.Deserialize(match.Groups[1].Value, RendererJsonContext.Default.RenderFrame);
        Assert.NotNull(frame);
        Assert.True(frame.Patches.Length >= 3,
            $"Expected >= 3 patches, got {frame.Patches.Length}");
        Assert.Single(frame.Patches.OfType<CommitFramePatch>());
        Assert.Contains(frame.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "view");

        // Phase 2.5: text-node create patches must carry ParentId so the host
        // mapper can attach text widgets inside their container (not as siblings).
        var textCreatePatches = frame.Patches.OfType<CreateNodePatch>()
            .Where(p => p.NodeType == "text").ToList();
        Assert.NotEmpty(textCreatePatches);
        Assert.All(textCreatePatches, p => Assert.NotNull(p.ParentId));
    }
}
