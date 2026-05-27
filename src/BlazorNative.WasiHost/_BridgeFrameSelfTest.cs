using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.WasiHost;

// ─────────────────────────────────────────────────────────────────────────────
// _BridgeFrameSelfTest
//
// Phase 2.4 sentinel component. Drives the renderer in Main so DispatchFrame
// fires at least once for the [FRAME] line round-trip test. Deterministic
// shape — tests assert against the exact patches this emits:
//   CreateNodePatch("view") + CreateNodePatch("text") + ReplaceTextPatch + CommitFramePatch.
//
// Stays in the codebase after Phase 2.7 lands the real Hello component, as a
// minimal regression fixture for the renderer + transport pipeline.
//
// Leading underscore + sealed + internal: this is not part of the public
// surface; consumers should not subclass or reference it from outside WasiHost.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class _BridgeFrameSelfTest : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddContent(1, "frame-self-test");
        b.CloseElement();
    }
}
