using System.Reflection;
using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ParameterBindingFaultTests — Phase 11.4 Gate D (M11 DoD #6, #164).
//
// #164 in one sentence: a page wrote `<BnSwitch @bind-Value=…>` where the
// property is `Checked`; Blazor threw during render; the renderer logged it and
// RETURNED, and `mount` still reported rc 0 — a silently half-broken screen with
// (on Android, pre-Gate-B) no diagnostic anywhere.
//
// Gates A–C fixed the VISIBILITY half: the log now reaches logcat / the unified
// log. This file pins the SIGNALLING half — design §6.2's option (c):
//
//   * a PARAMETER-BINDING fault aborts the mount → the ALREADY-DOCUMENTED rc 2;
//   * every OTHER render fault keeps log-and-continue → rc 0, unchanged;
//   * the fault is visible through the BnLog seam, asserted against an INJECTED
//     sink rather than by scraping stderr.
//
// WHY rc 2 AND NOT A NEW rc. BlazorNativeRuntime.kt's mount `when` ends in
// `else -> throw`, so an unknown rc is FATAL. The shells are copied source: a
// "non-fatal rc 4" would hard-crash every consumer still on an older shell copy
// the moment they upgraded the runtime package — the StrictErrors-in-production
// outcome #164 explicitly rules out, arriving through the back door. rc 2 is
// handled correctly by both shells today, and the only apps whose behaviour
// changes are the ones that are ALREADY BROKEN.
//
// WHY [Collection("host-session")]: every test here drives the process-wide
// HostSession singleton AND flips StrictErrorsForTests, which
// StrictModeTestDefaults sets true for this whole assembly — production's posture
// is NON-strict, and non-strict is the only posture in which rc 0 vs rc 2 is a
// real question. Each test restores it in a finally.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>#164's EXACT shape, reduced: a child component handed a parameter
/// name it does not declare. `@bind-Value` on a component whose property is
/// `Checked` compiles to precisely this — an attribute named "Value" — so this
/// is the razor bug, expressed without a .razor file.</summary>
public sealed class BadParameterBindingPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<BnSwitch>(0);
        builder.AddAttribute(1, "Value", true);   // the property is `Checked`
        builder.CloseComponent();
    }
}

/// <summary>THE NEAR MISS, and the reason R3 calls this classification the hard
/// part of Gate D. This fault travels the SAME
/// `ComponentState.SupplyCombinedParameters` path as the one above — it is
/// raised from a lifecycle method Blazor invokes out of `SetParametersAsync` —
/// but it is NOT the parameter-property writer. It is ordinary app code that
/// could fail for any reason (a null service, a bad response, a transient), so
/// it must keep log-and-continue. A predicate keyed on the enclosing supply
/// frame instead of the binding frames would classify this as fatal and turn a
/// recoverable fault into a boot failure — which IS the StrictErrors flip.</summary>
public sealed class ThrowingLifecyclePage : ComponentBase
{
    protected override void OnParametersSet()
        => throw new InvalidOperationException("test: a lifecycle method threw");

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<BnSwitch>(0);
        builder.AddAttribute(1, "Checked", true);
        builder.CloseComponent();
    }
}

[Collection("host-session")]
public sealed class ParameterBindingFaultTests
{
    // ── 1. The fix: a bad binding now FAILS the mount ────────────────────────

    /// <summary>#164, ANSWERED. Before Gate D this returned 0 with a
    /// partly-rendered frame; it now returns 2 — the value
    /// `NativeBindings.kt`'s KDoc already documents as "mount threw" and both
    /// shells already turn into a clear `IllegalStateException` / Swift throw.
    ///
    /// The assertion is on the RETURN CODE, not on a log line, because rc is the
    /// only thing the HOST can act on: log-only was already true before this gate
    /// and #164 says so in as many words.</summary>
    [Fact]
    public void ABadParameterBinding_AbortsTheMount_WithTheExistingRc2()
    {
        Assert.Equal(2, MountNonStrict<BadParameterBindingPage>());
    }

    // ── 2. What deliberately did NOT change ──────────────────────────────────

    /// <summary>THE POSTURE IS SCOPED, NOT REVERSED. A recoverable render fault
    /// — here a lifecycle method throwing, reached through the very same
    /// parameter-supply call — still logs and continues, and the mount still
    /// reports rc 0.
    ///
    /// This is the "too broad" half of R3 and the more valuable of the two
    /// assertions: making it green is trivial (classify nothing), making BOTH
    /// green is the actual design constraint. Crashing a running app over one
    /// bad handler would be worse than a half-rendered screen, and #164 says
    /// that too.</summary>
    [Fact]
    public void ARecoverableRenderFault_StillLogsAndContinues_Rc0()
    {
        Assert.Equal(0, MountNonStrict<ThrowingLifecyclePage>());
    }

    /// <summary>…and the recoverable fault is still REPORTED while it continues —
    /// through the ordinary "render fault" line, not the Gate D one. A silent
    /// rc 0 would trade #164's failure mode for a worse one.</summary>
    [Fact]
    public void ARecoverableRenderFault_IsStillLogged_AsAnOrdinaryRenderFault()
    {
        var seen = new List<(BnLogLevel Level, string Category, string Message)>();
        Assert.Equal(0, MountNonStrict<ThrowingLifecyclePage>(seen));

        (BnLogLevel level, _, string message) = Assert.Single(
            seen, e => e.Category == "BlazorNative.Renderer");
        Assert.Equal(BnLogLevel.Error, level);
        Assert.Contains("render fault", message);
        Assert.DoesNotContain("parameter-binding fault", message);
    }

    // ── 3. Visible through the SEAM, not through stderr ──────────────────────

    /// <summary>The fault reaches the Gate A seam — asserted against an INJECTED
    /// `BnLog.Sink`, which is what makes this a pin rather than an observation: a
    /// consumer's sink, Gate B's logcat pump and Gate C's `os_log` all hang off
    /// this one delegate, so a line that reaches it reaches all three. Scraping
    /// `Console.Error` would prove only that the DEFAULT sink still works.
    ///
    /// EXACTLY ONE line: the rethrow unwinds back through Blazor's render
    /// machinery, which routes it to HandleException again (and again). Three
    /// identical `ToString()` blobs for one author bug is the noise #155 exists
    /// to remove, so the report dedupes by exception identity.</summary>
    [Fact]
    public void TheBindingFault_IsVisibleThroughTheBnLogSeam_Once()
    {
        var seen = new List<(BnLogLevel Level, string Category, string Message)>();
        Assert.Equal(2, MountNonStrict<BadParameterBindingPage>(seen));

        (BnLogLevel level, _, string message) = Assert.Single(
            seen, e => e.Category == "BlazorNative.Renderer");

        Assert.Equal(BnLogLevel.Error, level);
        Assert.Contains("parameter-binding fault", message);
        Assert.Contains("#164", message);
        // …carrying the detail an author needs: which component, which name.
        Assert.Contains(nameof(BnSwitch), message);
        Assert.Contains("Value", message);
    }

    // ── 4. R3: the predicate keys on PROVENANCE, never on message text ───────

    /// <summary>THE ANTI-TEXT-MATCHING PIN. An exception carrying #164's message
    /// VERBATIM, but raised anywhere else, is NOT a binding fault.
    ///
    /// `ex.Message.Contains("does not have a property matching")` would pass the
    /// first assertion and fail this one. That predicate would also be
    /// localizable and version-fragile, and its failure mode is SILENT — it
    /// simply stops matching, mount quietly returns to rc 0, and nothing looks
    /// broken. Provenance is a method identity: not translated, not reworded.</summary>
    [Fact]
    public void TheClassifier_IgnoresMessageText_AndKeysOnTheThrowSite()
    {
        Exception impostor = Caught(new InvalidOperationException(
            "Object of type 'BnSwitch' does not have a property matching the name 'Value'."));

        Assert.False(BlazorInterop.IsParameterBindingFault(impostor));
        Assert.True(BlazorInterop.IsParameterBindingFault(RealBindingFault()));
    }

    /// <summary>A binding fault WRAPPED — the shape `Mount&lt;T&gt;` produces when
    /// the render task faults rather than throwing inline — still classifies.
    /// The chain walk is what makes the gate hold on both paths, and it is
    /// depth-capped so a pathological chain cannot spin an error path.</summary>
    [Fact]
    public void TheClassifier_WalksTheInnerChain()
    {
        var wrapped = new InvalidOperationException("mount failed", RealBindingFault());
        Assert.True(BlazorInterop.IsParameterBindingFault(wrapped));
        Assert.True(BlazorInterop.IsParameterBindingFault(
            new AggregateException(wrapped)));

        Assert.False(BlazorInterop.IsParameterBindingFault(null));
        Assert.False(BlazorInterop.IsParameterBindingFault(
            new InvalidOperationException("no stack at all")));
    }

    /// <summary>THE DRIFT PIN, and the answer to "keying on a frame name will
    /// silently stop matching one day". Every allow-listed frame is resolved
    /// REFLECTIVELY against the linked Blazor assembly here: if a version bump
    /// renames or removes one, this test reddens on the upgrade PR — while a
    /// human is already reading the diff — instead of the gate quietly
    /// evaporating in the field.
    ///
    /// It lives in the test assembly rather than in `BlazorInterop`'s load-time
    /// `VerifyAccessors` on purpose: `ComponentProperties` is an INTERNAL type,
    /// and a `Type.GetType` lookup that the trimmer had stripped the metadata for
    /// would turn a diagnostic into a false BOOT FAILURE on device. A red test is
    /// the right blast radius for this.</summary>
    [Fact]
    public void TheAllowListedFrames_StillNameRealBlazorMethods()
    {
        Assembly components = typeof(ParameterView).Assembly;

        foreach (string frame in BlazorInterop.ParameterBindingFrames)
        {
            int split = frame.LastIndexOf('.');
            string typeName = frame[..split];
            string methodName = frame[(split + 1)..];

            Type? type = components.GetType(typeName, throwOnError: false);
            Assert.True(type is not null, $"Blazor type '{typeName}' no longer exists");

            MethodInfo[] overloads = type!
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                            | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.Name == methodName)
                .ToArray();
            Assert.True(overloads.Length > 0, $"Blazor method '{frame}' no longer exists");
        }
    }

    /// <summary>The frames that are deliberately NOT on the list, stated as an
    /// assertion so widening the gate is a decision rather than a drive-by edit.
    /// `SupplyCombinedParameters` and `SetParametersAsync` ENCLOSE parameter
    /// supply, including a component's own override — app code, and exactly the
    /// recoverable class the test above pins at rc 0.</summary>
    [Fact]
    public void TheAllowList_IsTheBindingWriter_NotTheEnclosingSupplyPath()
    {
        Assert.Equal(
            new[]
            {
                "Microsoft.AspNetCore.Components.Reflection.ComponentProperties.SetProperties",
                "Microsoft.AspNetCore.Components.ParameterView.SetParameterProperties",
            },
            BlazorInterop.ParameterBindingFrames);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Mounts <typeparamref name="TPage"/> through the REAL host path
    /// (<see cref="HostSession.TryMount"/> — the body of `blazornative_mount`),
    /// in the NON-STRICT production posture this assembly otherwise overrides,
    /// and returns the rc a shell would see.</summary>
    private static int MountNonStrict<TPage>(
        List<(BnLogLevel, string, string)>? seen = null)
        where TPage : IComponent, new()
    {
        HostSession.ResetForTests();
        HostSession.StrictErrorsForTests = false;

        Func<NativeRenderer, int> originalEntry =
            HostSession.ReplaceRegistryEntryForTests(MountProbeName, r => r.Mount<TPage>());
        Action<BnLogLevel, string, string>? originalSink = BnLog.Sink;
        BnLogLevel originalLevel = BnLog.Level;
        try
        {
            // A sink is installed even when the caller does not want the lines:
            // otherwise the default writer puts a full render fault on the test
            // run's stderr, which is noise the other suites have to read past.
            BnLog.Level = BnLogLevel.Warn;
            BnLog.Sink = (l, c, m) => seen?.Add((l, c, m));

            return HostSession.TryMount(MountProbeName);
        }
        finally
        {
            BnLog.Sink = originalSink;
            BnLog.Level = originalLevel;
            HostSession.ReplaceRegistryEntryForTests(MountProbeName, originalEntry);
            HostSession.StrictErrorsForTests = true;
            HostSession.ResetForTests();
        }
    }

    /// <summary>Any registered name works — the registry entry is swapped for the
    /// duration; "HelloComponent" is the assembly's canonical mount probe.</summary>
    private const string MountProbeName = "HelloComponent";

    /// <summary>A REAL Blazor parameter-binding exception, produced by the real
    /// writer through its public entry point — not a hand-built stand-in. The
    /// classifier is only worth what its input is faithful to.</summary>
    private static Exception RealBindingFault()
    {
        try
        {
            ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Value"] = true })
                .SetParameterProperties(new BnSwitch());
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException(
            "Blazor no longer throws for an unknown incoming parameter name — "
            + "#164's premise changed; re-read design §6.2 before touching Gate D.");
    }

    private static Exception Caught(Exception toThrow)
    {
        try { throw toThrow; }
        catch (Exception ex) { return ex; }
    }
}
