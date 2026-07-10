using BlazorNative.Core;
using BlazorNative.Runtime;
using BlazorNative.Renderer;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0e — the Hello golden frame, typed. (Phase 3.2: +AttachEventPatch,
// counter text.)
//
// Typed expected list transcribed from the retired hello-frame.json fixture
// (3.0e); the wire shape lock lives here now. Mounts HelloComponent, captures
// the first RenderFrame via the Frames event, and asserts the 14-patch Hello
// frame: 13 patches field-by-field against in-code record literals (records
// give value equality) + the commit terminator type-only. The CommitFramePatch
// carries nondeterministic FrameId/TimestampMs — hence type-only, matching the
// old fixture normalization (which zeroed both).
//
// The Kotlin twin (NativeFrameAdapterTest.golden_mountHello_viaNativeDll_
// matchesExpectedShape) asserts the same 14 patches against the native
// struct-callback path — together they lock the wire shape on both sides of
// the C ABI.
//
// An intentional Hello/protocol change updates BOTH typed lists by hand —
// there is no record mode anymore.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class HelloGoldenTests
{
    /// <summary>Sentinel for the one runtime-assigned field in the golden
    /// list: AttachEventPatch.HandlerId comes from Blazor's event-handler
    /// table (a process-global counter), so its value depends on how many
    /// handlers earlier tests registered. Pinning it would make the golden
    /// order-dependent; the test asserts HandlerId &gt; 0 instead — the same
    /// relaxation the Kotlin twin applies via normalize(). Every OTHER field
    /// of the patch (NodeId, EventName) stays pinned.</summary>
    private const int AnyHandlerId = -1;

    /// <summary>The Phase 2.8 Hello shape, Phase 3.2 interactive: outer view
    /// #FFEEAA + padding 16, inner view fontSize 24 + text
    /// "Hello, BlazorNative! (taps: 0)", button with @onclick + "Tap",
    /// input + "Type here...". Transcribed from the retired fixture,
    /// hand-updated per the procedure above.</summary>
    private static readonly IReadOnlyList<RenderPatch> ExpectedPatches =
    [
        // Phase 3.3 (DoD #10): every CreateNode carries InsertIndex — all −1
        // here because mount-walk creates are genuine appends. The ONLY 3.3
        // delta in this golden; everything else is the regression net.
        new CreateNodePatch(1, "view", InsertIndex: -1),
        new SetStylePatch(1, "backgroundColor", "#FFEEAA"),
        new SetStylePatch(1, "padding", "16"),
        new CreateNodePatch(2, "view", 1, InsertIndex: -1),
        new SetStylePatch(2, "fontSize", "24"),
        new CreateNodePatch(3, "text", 2, InsertIndex: -1),
        new ReplaceTextPatch(3, "Hello, BlazorNative! (taps: 0)"),
        new CreateNodePatch(4, "button", 1, InsertIndex: -1),
        new AttachEventPatch(4, "click", AnyHandlerId),
        new CreateNodePatch(5, "text", 4, InsertIndex: -1),
        new ReplaceTextPatch(5, "Tap"),
        new CreateNodePatch(6, "input", 1, InsertIndex: -1),
        new UpdatePropPatch(6, "placeholder", "Type here..."),
        // 14th patch: CommitFramePatch — asserted type-only in the test body
        // (FrameId/TimestampMs are nondeterministic).
    ];

    [Fact]
    public async Task MountHello_Frame_MatchesTypedGoldenPatches()
    {
        var services = new ServiceCollection();
        services.AddBlazorNativeRendererServices();
        services.AddBlazorNativeHttpServices();
        using var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)

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

        // 13 shape patches + the commit terminator.
        Assert.Equal(ExpectedPatches.Count + 1, frame.Patches.Length);

        for (var i = 0; i < ExpectedPatches.Count; i++)
        {
            if (ExpectedPatches[i] is AttachEventPatch expectedAttach)
            {
                // HandlerId is runtime-assigned (see AnyHandlerId doc) —
                // normalize it to the sentinel, then compare by record
                // equality so every OTHER field (including any future
                // additions to the record) stays load-bearing.
                var actualAttach = Assert.IsType<AttachEventPatch>(frame.Patches[i]);
                Assert.True(actualAttach.HandlerId > 0,
                    $"AttachEventPatch.HandlerId must be a positive runtime-assigned id, got {actualAttach.HandlerId}");
                Assert.Equal(expectedAttach, actualAttach with { HandlerId = AnyHandlerId });
                continue;
            }
            Assert.Equal(ExpectedPatches[i], frame.Patches[i]);
        }

        // Terminator: type-only (FrameId/TimestampMs are nondeterministic —
        // the old fixture normalized both to 0 before comparing).
        Assert.IsType<CommitFramePatch>(frame.Patches[^1]);
    }
}
