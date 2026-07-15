using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RazorPageTwinTests — Phase 7.1 (design §"The method"): the TRANSITIONAL
// golden-vs-twin proofs for the page conversions. One test per page under
// conversion; each RETIRES WITH ITS TWIN when the page swaps to its canonical
// name (the pre-existing goldens take over as the parity proof), so this file
// exists only while a conversion is in flight. Gate 1's BnSettingsPage twin,
// Gate 2's BnDemo and BnLayoutDemo twins, and Gate 3's BnScrollDemo twin have
// already come and gone through here — each mutation-verified, then retired
// at its swap. BnImageDemo's is the LAST: this file retires for good with it.
//
// The method is SpikeRazorTests' (7.0), lifted to the REAL host path: both
// versions mount through HostSession.TryMount via a test-only registry
// override (ReplaceRegistryEntryForTests — the registry maps a NAME to a
// type, which is exactly why the shells never notice the swap), are driven
// through the SAME dispatch sequence, and their patch streams must be
// IDENTICAL — every frame, every patch, in order (record equality; only the
// wall-clock CommitFramePatch excluded). NodeIds/handlerIds/frameIds restart
// with the fresh session per mount, so identity is byte-for-byte, not modulo
// renaming. The .razor whitespace Markup frames must vanish wire-invisibly
// (the renderer's 7.0 Markup arm).
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class RazorPageTwinTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    /// <summary>Mounts <paramref name="registryName"/> through the REAL host
    /// path with the registry entry overridden to <paramref name="mount"/>,
    /// runs <paramref name="interactions"/> against the mount frame, and
    /// returns every frame's patches (CommitFramePatch excluded — the one
    /// nondeterministic patch, it carries a wall-clock timestamp).</summary>
    private static List<RenderPatch[]> Drive(
        string registryName,
        Func<NativeRenderer, int> mount,
        Action<RenderFrame> interactions)
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        Func<NativeRenderer, int> original =
            HostSession.ReplaceRegistryEntryForTests(registryName, mount);
        try
        {
            NativeRenderer renderer = HostSession.EnsureSession();
            var frames = new List<RenderFrame>();
            renderer.Frames += (f, _) =>
            {
                frames.Add(f);
                return ValueTask.CompletedTask;
            };
            Assert.Equal(0, HostSession.TryMount(registryName));
            Assert.NotEmpty(frames);
            interactions(frames[0]);
            return frames
                .Select(f => f.Patches.Where(p => p is not CommitFramePatch).ToArray())
                .ToList();
        }
        finally
        {
            HostSession.ReplaceRegistryEntryForTests(registryName, original);
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }

    private static void AssertIdenticalStreams(
        List<RenderPatch[]> golden, List<RenderPatch[]> razor)
    {
        Assert.Equal(golden.Count, razor.Count);
        for (var i = 0; i < golden.Count; i++)
            Assert.Equal(golden[i], razor[i]); // sealed-record equality, order included
    }

    private static int ClickHandlerOnText(RenderFrame mount, string text)
    {
        var t = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == text);
        int container = Assert.IsType<int>(Assert.Single(
            mount.Patches.OfType<CreateNodePatch>(), p => p.NodeId == t.NodeId).ParentId);
        return Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == container && p.EventName == "click").HandlerId;
    }

    // ── BnImageDemo (Gate 3 — the last conversion; declarative, prop-carrying) ─

    /// <summary>Every patch of every frame — mount (the 13-create tree whose
    /// styles, EMPTY style tables on the two measured images, and three `src`
    /// UpdateProps BnImageDemoTests pins) AND the "← Back" navigation (this
    /// page's one-remove subtree purge + BnDemo's creates) — byte-identical
    /// between the hand-written BnImageDemo and BnImageDemoRazor. Covers the
    /// intrinsic/failing cases' ATTRIBUTE ABSENCE (no Width/Height in markup
    /// must put NOTHING on the style wire — the empty table IS the case) and
    /// the enum-typed Align="FlexAlign.FlexStart" the .cs passed as a typed
    /// parameter. Mutation-verified (Gate 1 discipline) — see the phase
    /// conclusion's table.</summary>
    [Fact]
    public void BnImageDemo_GoldenVsTwin_MountAndBack_Identical()
    {
        List<RenderPatch[]> Run(Func<NativeRenderer, int> mount)
            => Drive("BnImageDemo", mount, m =>
                Assert.Equal(0, Exports.DispatchEventCore(
                    (ulong)ClickHandlerOnText(m, "← Back"), ClickArgs)));

        List<RenderPatch[]> golden = Run(r => r.Mount<BnImageDemo>());
        List<RenderPatch[]> razor = Run(r => r.Mount<BnImageDemoRazor>());

        AssertIdenticalStreams(golden, razor);
    }
}
