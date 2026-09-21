using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;

namespace BlazorNative.Runtime.Tests;

// -----------------------------------------------------------------------------
// BnSafeAreaTests -- Phase 14.2 Task 5.
//
// Same harness shape as BnComponentTests (CreateCapturingSession + StyleOn),
// reproduced locally rather than shared because BnComponentTests' helpers are
// private to that file.
//
// The two rules that carry this component -- nesting must not double-pad, and
// Maximum compares against the author's own padding -- are mutation-proven in
// the implementation report, not just asserted here (Step 4 of the task
// brief): the nesting suppression was removed and the nesting test observed
// red, and the Maximum comparison was inverted and BOTH MaximumMode_ tests
// observed red, before either was restored.
// -----------------------------------------------------------------------------

[Collection("host-session")]
public sealed class BnSafeAreaTests : IDisposable
{
    public void Dispose()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
        BnSafeAreaInsets.ResetForTests();
    }

    private static (NativeRenderer Renderer, List<RenderFrame> Frames) CreateCapturingSession()
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        return (renderer, frames);
    }

    private static SetStylePatch StyleOn(RenderFrame frame, int nodeId, string prop)
        => Assert.Single(frame.Patches.OfType<SetStylePatch>(),
            p => p.NodeId == nodeId && p.Property == prop);

    [Fact]
    public void BnSafeArea_PadsEachEdgeByTheReportedInset()
    {
        var (renderer, frames) = CreateCapturingSession();
        BnSafeAreaInsets.SetForTests(new BnSafeAreaInsets(47, 0, 34, 0));

        renderer.Mount<BnSafeArea>(ParameterView.Empty);
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

        Assert.Equal("47", StyleOn(mount, root.NodeId, "paddingTop").Value);
        Assert.Equal("0",  StyleOn(mount, root.NodeId, "paddingRight").Value);
        Assert.Equal("34", StyleOn(mount, root.NodeId, "paddingBottom").Value);
        Assert.Equal("0",  StyleOn(mount, root.NodeId, "paddingLeft").Value);
    }

    [Fact]
    public void ANestedBnSafeArea_PadsNothing_BecauseTheOuterOneAlreadyDid()
    {
        // FLUTTER'S RULE, and the reason this class is not a two-line component.
        // SafeArea rewrites the inset data its children see, so only the outermost
        // instance pads. Without this, a wrapped page containing a wrapped section
        // applies the inset TWICE -- subtle, and visible only on a notched device.
        var (renderer, frames) = CreateCapturingSession();
        BnSafeAreaInsets.SetForTests(new BnSafeAreaInsets(47, 0, 34, 0));

        renderer.Mount<BnSafeArea>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSafeArea.ChildContent)] = (RenderFragment)(b =>
            {
                b.OpenComponent<BnSafeArea>(0);
                b.CloseComponent();
            }),
        }));
        var mount = frames[0];

        var creates = mount.Patches.OfType<CreateNodePatch>().Where(p => p.NodeType == "view").ToList();
        Assert.Equal(2, creates.Count);
        var outer = Assert.Single(creates, p => p.ParentId is null);
        var inner = Assert.Single(creates, p => p.ParentId == outer.NodeId);

        Assert.Equal("47", StyleOn(mount, outer.NodeId, "paddingTop").Value);
        Assert.Equal("0",  StyleOn(mount, inner.NodeId, "paddingTop").Value);
        Assert.Equal("0",  StyleOn(mount, inner.NodeId, "paddingBottom").Value);
    }

    [Fact]
    public void AnEdgeSetToOff_IsNotPadded_EvenWhenTheInsetIsNonZero()
    {
        // The edge-to-edge case: a full-bleed hero image under the status bar.
        var (renderer, frames) = CreateCapturingSession();
        BnSafeAreaInsets.SetForTests(new BnSafeAreaInsets(47, 0, 34, 0));

        renderer.Mount<BnSafeArea>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSafeArea.TopEdge)] = BnSafeAreaEdge.Off,
        }));
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

        Assert.Equal("0",  StyleOn(mount, root.NodeId, "paddingTop").Value);
        Assert.Equal("34", StyleOn(mount, root.NodeId, "paddingBottom").Value);
    }

    [Fact]
    public void AdditiveMode_AddsTheInsetToTheAuthorsOwnPadding()
    {
        // ADDITIVE IS THE DEFAULT, so this pins the behaviour most apps get without
        // choosing it. "Additive" means SUM, not replace: an author who asks for 16
        // and sits under a 34pt home indicator wants clearance from BOTH, not the
        // larger of the two -- that is what Maximum is for.
        var (renderer, frames) = CreateCapturingSession();
        BnSafeAreaInsets.SetForTests(new BnSafeAreaInsets(0, 0, 34, 0));

        renderer.Mount<BnSafeArea>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSafeArea.BottomEdge)] = BnSafeAreaEdge.Additive,
            [nameof(BnSafeArea.PaddingBottom)] = (BnLength)16f,
        }));
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

        Assert.Equal("50", StyleOn(mount, root.NodeId, "paddingBottom").Value);
    }

    [Fact]
    public void MaximumMode_UsesTheAuthorsPadding_WhenItExceedsTheInset()
    {
        // REACT NATIVE'S RULE: "my padding, or the inset, whichever is larger".
        // Author asks for 24, inset is 8 -> 24. This is the case people otherwise
        // hand-write as Math.max(insets.bottom, 16), visible in RN's own docs.
        var (renderer, frames) = CreateCapturingSession();
        BnSafeAreaInsets.SetForTests(new BnSafeAreaInsets(0, 0, 8, 0));

        renderer.Mount<BnSafeArea>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSafeArea.BottomEdge)] = BnSafeAreaEdge.Maximum,
            [nameof(BnSafeArea.PaddingBottom)] = (BnLength)24f,
        }));
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

        Assert.Equal("24", StyleOn(mount, root.NodeId, "paddingBottom").Value);
    }

    [Fact]
    public void MaximumMode_UsesTheInset_WhenItExceedsTheAuthorsPadding()
    {
        // The other half, and the half a naive implementation gets wrong:
        // author asks for 8, inset is 34 -> 34.
        var (renderer, frames) = CreateCapturingSession();
        BnSafeAreaInsets.SetForTests(new BnSafeAreaInsets(0, 0, 34, 0));

        renderer.Mount<BnSafeArea>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSafeArea.BottomEdge)] = BnSafeAreaEdge.Maximum,
            [nameof(BnSafeArea.PaddingBottom)] = (BnLength)8f,
        }));
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

        Assert.Equal("34", StyleOn(mount, root.NodeId, "paddingBottom").Value);
    }

    [Fact]
    public void WhenTheInsetsChange_TheComponentRelaysOut()
    {
        // Rotation, a keyboard, a call banner. Without this the component is correct
        // exactly once per run.
        //
        // NOTE (fix, not an implementation choice): the brief's draft of this test
        // asserted the changed value on an UpdatePropPatch. paddingTop is registered
        // in NativeRenderer.YogaStyleAttributes, so a padding change can only ever
        // arrive as a SetStylePatch (PatchProtocol.cs's SetStylePatch(NodeId,
        // Property, Value) -- consistent with every other paddingTop assertion in
        // this file, all of which read SetStylePatch via StyleOn). The subject under
        // test is unchanged: that BnSafeArea re-lays-out when the insets change.
        var (renderer, frames) = CreateCapturingSession();
        BnSafeAreaInsets.SetForTests(BnSafeAreaInsets.Zero);
        renderer.Mount<BnSafeArea>(ParameterView.Empty);

        int before = frames.Count;
        Assert.Equal(0, Exports.DispatchHostEventCore(
            BnHostEvents.SafeAreaChanged,
            """{"top":"47","right":"0","bottom":"34","left":"0"}"""));

        Assert.True(frames.Count > before,
            "changing the insets produced no new frame — BnSafeArea is subscribed to nothing, "
            + "so it would be correct exactly once per run and wrong after any rotation");
        var latest = frames[^1];
        Assert.Contains(latest.Patches.OfType<SetStylePatch>(),
            p => p.Property == "paddingTop" && p.Value == "47");
    }
}
