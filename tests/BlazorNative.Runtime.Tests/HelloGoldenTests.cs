using BlazorNative.Core;
using BlazorNative.NativeHost;
using BlazorNative.Renderer;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0e — the Hello golden frame, typed.
//
// Typed expected list transcribed from the retired hello-frame.json fixture
// (3.0e); the wire shape lock lives here now. Mounts HelloComponent, captures
// the first RenderFrame via the Frames event, and asserts the 13 Hello patches
// field-by-field against in-code record literals (records give value
// equality). The CommitFramePatch carries nondeterministic FrameId/TimestampMs
// — it is asserted type-only, matching the old fixture normalization (which
// zeroed both).
//
// The Kotlin twin (NativeFrameAdapterTest.golden_mountHello_viaNativeDll_
// matchesExpectedShape) asserts the same 13 patches against the native
// struct-callback path — together they lock the wire shape on both sides of
// the C ABI.
//
// An intentional Hello/protocol change updates BOTH typed lists by hand —
// there is no record mode anymore.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class HelloGoldenTests
{
    /// <summary>The Phase 2.8 Hello shape: outer view #FFEEAA + padding 16,
    /// inner view fontSize 24 + text "Hello, BlazorNative!", button + "Tap",
    /// input + "Type here...". Transcribed from the retired fixture.</summary>
    private static readonly IReadOnlyList<RenderPatch> ExpectedPatches =
    [
        new CreateNodePatch(1, "view"),
        new SetStylePatch(1, "backgroundColor", "#FFEEAA"),
        new SetStylePatch(1, "padding", "16"),
        new CreateNodePatch(2, "view", 1),
        new SetStylePatch(2, "fontSize", "24"),
        new CreateNodePatch(3, "text", 2),
        new ReplaceTextPatch(3, "Hello, BlazorNative!"),
        new CreateNodePatch(4, "button", 1),
        new CreateNodePatch(5, "text", 4),
        new ReplaceTextPatch(5, "Tap"),
        new CreateNodePatch(6, "input", 1),
        new UpdatePropPatch(6, "placeholder", "Type here..."),
        // 13th patch: CommitFramePatch — asserted type-only in the test body
        // (FrameId/TimestampMs are nondeterministic).
    ];

    [Fact]
    public async Task MountHello_Frame_MatchesTypedGoldenPatches()
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

        // 12 shape patches + the commit terminator.
        Assert.Equal(ExpectedPatches.Count + 1, frame.Patches.Length);

        for (var i = 0; i < ExpectedPatches.Count; i++)
            Assert.Equal(ExpectedPatches[i], frame.Patches[i]);

        // Terminator: type-only (FrameId/TimestampMs are nondeterministic —
        // the old fixture normalized both to 0 before comparing).
        Assert.IsType<CommitFramePatch>(frame.Patches[^1]);
    }
}
