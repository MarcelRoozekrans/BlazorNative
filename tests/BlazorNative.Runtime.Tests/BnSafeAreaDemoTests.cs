using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnSafeAreaDemoTests — Phase 14.2 Task 6 (#338): the `/safearea` page ties
// together HostSafeAreaTests' wire (Task 2) and BnSafeAreaTests' component
// (Task 5) — mounting the ROUTED PAGE and dispatching the SAME four inset
// values BnSafeAreaAndroidTest.kt / BnSafeAreaTests.swift dispatch on-device,
// so a change to either the page's explicit 300×200 box or the declared table
// numbers reds here BEFORE it reds a device lane.
//
// This is a WIRE-level pin, not a geometry one: .NET emits Yoga STYLE patches
// (paddingTop/Right/Bottom/Left as strings); Yoga itself runs shell-side, so the
// actual computed frame (x=13, y=47, w=266, h=119) is only observable on-device
// — that is exactly what the two device suites' bnSafeAreaDemoFrames assertion
// proves. This test proves the numbers going INTO that computation are right.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnSafeAreaDemoTests
{
    // The SAME four numbers the device suites dispatch — see
    // BnDemoFrameTables.kt/.swift's bnSafeAreaDemoFrames header.
    private const string FixedInsetsPayload =
        """{"top":"47","right":"21","bottom":"34","left":"13"}""";

    [Fact]
    public void SafeAreaChanged_PadsTheDemosContentBox_OnAllFourEdges()
    {
        HostSession.ResetForTests();
        BnSafeAreaInsets.ResetForTests();

        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };

        Assert.Equal(0, HostSession.TryMount("BnSafeAreaDemo"));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        // Two PARENT-LESS nodes exist — the SafeArea's own BnView and the trailing
        // "← Back" BnButton, BnSafeAreaDemo.razor's second root-level node — so the
        // SafeArea is picked by NODE TYPE rather than by "the only parentless
        // node". (Blazor's render queue does not create children in final sibling
        // order; the button is created FIRST here but carries InsertIndex -1 while
        // the view carries InsertIndex 0, so the FINAL order still matches the
        // markup — see the project's "children in final sibling order" note.)
        var safeArea = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId is null && p.NodeType == "view");
        var content = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId == safeArea.NodeId);

        // rc 0: a live session was told to re-render.
        Assert.Equal(0, Exports.DispatchHostEventCore(BnHostEvents.SafeAreaChanged, FixedInsetsPayload));

        var rerender = Assert.Single(frames, f => f != mount && f.Patches.OfType<SetStylePatch>()
            .Any(p => p.NodeId == safeArea.NodeId && p.Property == "paddingTop"));

        Assert.Equal("47", StyleOn(rerender, safeArea.NodeId, "paddingTop").Value);
        Assert.Equal("21", StyleOn(rerender, safeArea.NodeId, "paddingRight").Value);
        Assert.Equal("34", StyleOn(rerender, safeArea.NodeId, "paddingBottom").Value);
        Assert.Equal("13", StyleOn(rerender, safeArea.NodeId, "paddingLeft").Value);

        // The content box itself carries no padding of its own — Grow="1" is the
        // ONLY thing that makes it fill (300 − left − right) × (200 − top − bottom)
        // once Yoga solves the tree shell-side; this test cannot observe that
        // computed frame, only that the box exists and carries none of the four
        // edges itself (a stray padding here would mean the page wraps the box in
        // its OWN padding rather than relying on the SafeArea's).
        Assert.DoesNotContain(rerender.Patches.OfType<SetStylePatch>(),
            p => p.NodeId == content.NodeId
                && (p.Property == "paddingTop" || p.Property == "paddingRight"
                    || p.Property == "paddingBottom" || p.Property == "paddingLeft"));

        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
        BnSafeAreaInsets.ResetForTests();
    }

    private static SetStylePatch StyleOn(RenderFrame frame, int nodeId, string prop)
        => Assert.Single(frame.Patches.OfType<SetStylePatch>(),
            p => p.NodeId == nodeId && p.Property == prop);
}
