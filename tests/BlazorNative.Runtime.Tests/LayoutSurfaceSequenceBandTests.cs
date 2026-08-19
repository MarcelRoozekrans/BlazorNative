using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// LayoutSurfaceSequenceBandTests — Phase 13.0 Task 9, PIN 3.
//
// LayoutSurfacePinTests is pure reflection over the exported type list and
// correctly carries no [Collection("host-session")]. This pin has to MOUNT
// each component to see what it actually emits, which touches the shared
// HostSession the same way BnComponentTests/BnModalTests do — hence its own
// file, with the collection attribute those two use.
//
// WHAT "sequence-band uniqueness" MEANS, OPERATIONALLY: within the frames one
// component's own BuildRenderTree emits for one open Element/Component
// region, no two Attribute frames share a Sequence — EXCEPT the one place the
// design DOCUMENTS as sharing one on purpose: the four .razor form controls'
// `@attributes="ItemAttributes"` splat (BnLayoutItem's own remarks, "Sequence-
// number bands are normative"). Blazor's diff for THAT one call site matches
// by name, not position, so a shared sequence there is correct. Anywhere
// else, a shared sequence is exactly the bug #3's mutation table describes:
// AddAttribute(17, "left", …) misfiled onto 100 collides with BnText's own
// AddAttribute(100, "fontSize", …), and the diff silently mismatches them —
// no exception, no red test, just a wrong wire on the next render.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class LayoutSurfaceSequenceBandTests : IDisposable
{
    public void Dispose()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>
    /// The item surface as PAIRS — the C# parameter name and the name that
    /// same parameter carries once it is on the wire. Both spellings are
    /// needed because the surface reaches a frame array under one name or the
    /// other depending on the emission mechanism:
    /// <c>EmitItemAttributes</c> and the <c>ItemAttributes</c> splat write the
    /// camel-case WIRE name (they are element attributes), while
    /// <c>ForwardItemParameters</c> writes the C# PARAMETER name (it is a
    /// component parameter, matched against the target's property by name).
    /// A pin that knew only one spelling would have to exempt the other
    /// mechanism, and an exemption is how the gap this pin closes was opened.
    /// </summary>
    internal static readonly (string Parameter, string Wire)[] ItemNames =
    {
        ("BackgroundColor", "backgroundColor"),
        ("Margin",          "margin"),
        ("AlignSelf",       "alignSelf"),
        ("Grow",            "flexGrow"),
        ("Shrink",          "flexShrink"),
        ("Basis",           "flexBasis"),
        ("Width",           "width"),
        ("Height",          "height"),
        ("MinWidth",        "minWidth"),
        ("MaxWidth",        "maxWidth"),
        ("MinHeight",       "minHeight"),
        ("MaxHeight",       "maxHeight"),
        ("Position",        "position"),
        ("Top",             "top"),
        ("Right",           "right"),
        ("Bottom",          "bottom"),
        ("Left",            "left"),
    };

    /// <summary>The container surface, in the same two spellings and for the
    /// same reason — <c>EmitContainerAttributes</c> writes the wire name,
    /// <c>ForwardContainerParameters</c> the parameter name.</summary>
    internal static readonly (string Parameter, string Wire)[] ContainerNames =
    {
        ("Padding", "padding"),
        ("Justify", "justifyContent"),
        ("Align",   "alignItems"),
        ("Wrap",    "flexWrap"),
        ("Gap",     "gap"),
    };

    /// <summary>The wire names <see cref="BnLayoutItem.EmitItemAttributes"/> and
    /// <see cref="BnLayoutItem.ItemAttributes"/> both write — the CAMEL-CASE
    /// wire form, not the C# parameter names in
    /// <see cref="LayoutSurfacePinTests.ItemParameters"/>. This is what a
    /// legitimate splat collision looks like: every name in the shared-
    /// sequence group is one of these seventeen. (Declared AFTER
    /// <see cref="ItemNames"/> on purpose: static field initialisers run in
    /// textual order, so reading a field declared below would read null.)</summary>
    private static readonly string[] ItemWireNames =
        ItemNames.Select(n => n.Wire).ToArray();

    /// <summary>The four components whose render tree the Razor compiler
    /// generates, so <see cref="BnLayoutItem.EmitItemAttributes"/> is not
    /// reachable from them — see <see cref="LayoutSurfacePinTests.RazorEmitters"/>,
    /// the existing pin that names this same set for a different assertion.
    /// Reused rather than re-declared for the same reason that pin gives:
    /// these are the ONLY components where a shared attribute sequence
    /// number is correct by design.</summary>
    private static readonly Type[] SplatEmitters = ComputeSplatEmitters();

    private static Type[] ComputeSplatEmitters()
    {
        var types = new List<Type>();
        foreach (Type t in LayoutSurfacePinTests.RazorEmitters)
            types.Add(t);
        return types.ToArray();
    }

    /// <summary>Every non-abstract component the package exports that DOES
    /// derive from <see cref="BnLayoutItem"/> — i.e. exactly the set
    /// <see cref="LayoutSurfacePinTests.EveryComponentInThePackage_DerivesFromBnLayoutItem"/>
    /// requires to derive it. <see cref="BnList{TItem}"/> and
    /// <see cref="BnModal"/> are absent here for the same reason they are
    /// absent from that pin's offender scan: they do not derive, so they
    /// never call <see cref="BnLayoutItem.EmitItemAttributes"/> and have no
    /// band to collide.</summary>
    public static TheoryData<Type> LayoutItemComponents()
    {
        var data = new TheoryData<Type>();
        foreach (Type t in typeof(BnLayoutItem).Assembly
            .GetExportedTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t))
            .Where(t => !t.IsAbstract)
            .Where(t => typeof(BnLayoutItem).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            data.Add(t);
        }
        return data;
    }

    private static readonly MethodInfo GetCurrentRenderTreeFramesMethod =
        typeof(NativeRenderer).BaseType!.GetMethod(
            "GetCurrentRenderTreeFrames", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;

    private static ArrayRange<RenderTreeFrame> FramesOf(NativeRenderer renderer, int componentId)
        => (ArrayRange<RenderTreeFrame>)GetCurrentRenderTreeFramesMethod.Invoke(renderer, new object[] { componentId })!;

    /// <summary>
    /// A representative non-null/non-default value for a <c>[Parameter]</c>
    /// property's type, or null to leave the parameter unset.
    ///
    /// <para><b>This is load-bearing, not cosmetic.</b> Both
    /// <see cref="BnLayoutItem.EmitItemAttributes"/> and every component's own
    /// element attributes are ELEMENT attributes: a null value is never
    /// appended to the frame array at all (the un-styled invariant this
    /// package is built on). Mounting with <see cref="ParameterView.Empty"/>
    /// therefore leaves most of the surface — including a mutated, wrongly-
    /// numbered one — entirely ABSENT from the frames this test inspects, so
    /// the collision it exists to catch would never appear. Every
    /// string/float/enum-typed parameter (which is what the whole item and
    /// container surface, and most components' own optional attributes, are
    /// typed as) needs a real value for its frame to exist to collide.</para>
    /// </summary>
    private static object? SampleValue(Type propertyType)
    {
        Type t = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (t == typeof(string)) return "x";
        if (t == typeof(float)) return 1f;
        if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
        return null; // bool/int/EventCallback*/RenderFragment*/IReadOnlyList<> etc. — left at default, not part of the item/container surface or any collision this pin checks for.
    }

    private static ParameterView FullSurfaceParameters(Type component)
    {
        var values = new Dictionary<string, object?>();
        foreach (PropertyInfo p in component.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
        {
            if (p.GetCustomAttribute<ParameterAttribute>() is null)
                continue;
            object? sample = SampleValue(p.PropertyType);
            if (sample is not null)
                values[p.Name] = sample;
        }
        return ParameterView.FromDictionary(values);
    }

    /// <summary>Mounts <paramref name="component"/> with a full non-null
    /// parameter set and returns the frames its own
    /// <c>BuildRenderTree</c> produced. Shared by all three pins in this file
    /// — they differ only in what they read out of the same frame array.</summary>
    private static RenderTreeFrame[] FramesForFullSurfaceMount(Type component)
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();

        MethodInfo mount = typeof(NativeRenderer)
            .GetMethods()
            .Single(m => m.Name == nameof(NativeRenderer.Mount)
                && m.GetParameters() is [{ ParameterType.Name: nameof(ParameterView) }])
            .MakeGenericMethod(component);

        int componentId = (int)mount.Invoke(renderer, new object[] { FullSurfaceParameters(component) })!;

        ArrayRange<RenderTreeFrame> range = FramesOf(renderer, componentId);
        return range.Array.Take(range.Count).ToArray();
    }

    /// <summary>The attribute run belonging to the component's ROOT frame —
    /// the first Element or Component frame it opens, and the contiguous
    /// Attribute frames that follow it. Every component in this package opens
    /// exactly one root and hangs the whole layout surface off it, so a
    /// surface found anywhere else is not the surface the shells read.</summary>
    private static RenderTreeFrame[] RootAttributes(RenderTreeFrame[] frames)
    {
        int root = Array.FindIndex(frames, f =>
            f.FrameType is RenderTreeFrameType.Element or RenderTreeFrameType.Component);
        if (root < 0) return Array.Empty<RenderTreeFrame>();

        int end = root + 1;
        while (end < frames.Length && frames[end].FrameType == RenderTreeFrameType.Attribute)
            end++;

        return frames[(root + 1)..end];
    }

    [Theory]
    [MemberData(nameof(LayoutItemComponents))]
    public void Component_EmitsNoSequenceCollisionOutsideTheDocumentedSplat(Type component)
    {
        RenderTreeFrame[] frames = FramesForFullSurfaceMount(component);

        bool isSplatEmitter = SplatEmitters.Contains(component);

        // Attribute frames for one Element/Component region are CONTIGUOUS,
        // immediately following the frame that opens it — that is the shape
        // every BuildRenderTree in this package produces (EmitItemAttributes /
        // ForwardItemParameters emit densely-numbered runs, and the splat is
        // one call site). Bucket by "which opening frame owns this run" so a
        // collision is checked within the region it could actually mis-diff,
        // not across unrelated regions that happen to share a literal number
        // (BnRow's own 100 and BnView's own 100 are different components'
        // frame arrays entirely, but even within ONE array, a container that
        // forwards into a child component and a splat-owning element in a
        // sibling region must not be conflated).
        var offenders = new List<string>();
        int i = 0;
        while (i < frames.Length)
        {
            if (frames[i].FrameType is not (RenderTreeFrameType.Element or RenderTreeFrameType.Component))
            {
                i++;
                continue;
            }

            int regionStart = i + 1;
            int j = regionStart;
            while (j < frames.Length && frames[j].FrameType == RenderTreeFrameType.Attribute)
                j++;

            var duplicateGroups = frames[regionStart..j]
                .GroupBy(f => f.Sequence)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicateGroups)
            {
                string[] names = group.Select(f => f.AttributeName).ToArray();
                bool isDocumentedSplat = isSplatEmitter && names.All(n => ItemWireNames.Contains(n));

                if (!isDocumentedSplat)
                    offenders.Add($"{component.Name}: sequence {group.Key} shared by [{string.Join(", ", names)}]");
            }

            i = j;
        }

        Assert.Empty(offenders);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PIN 4 — the surface a component DECLARES is the surface it EMITS.
    //
    // Every other pin in this phase checks a TYPE, not a FRAME:
    // EveryComponentInThePackage_DerivesFromBnLayoutItem checks derivation,
    // NoComponent_RedeclaresAnInheritedLayoutParameter checks shadowing, and
    // NewlyGranted_HasTheFullItemSurface checks — reflectively, via
    // FlattenHierarchy — that the PROPERTY exists. None of them can see
    // BuildRenderTree. Delete `EmitItemAttributes(b)` from BnText and all
    // three stay green: BnText still declares Margin, still offers it in
    // IntelliSense, and silently drops it on every frame. That is the
    // accepted-then-silently-dropped defect this whole phase exists to
    // eliminate, reproduced in the phase's own headline deliverable.
    //
    // This pin is deliberately NOT a list of the four newly-granted
    // components. It runs over LayoutItemComponents() — every non-abstract
    // component in the package that derives from BnLayoutItem, discovered by
    // reflection — so component #14 is covered on the day it is written,
    // without anyone remembering to add it here.
    //
    // It judges all THREE emission mechanisms, because it looks for either
    // spelling of each parameter (see ItemNames): the hand-written emitters'
    // camel-case element attributes, the .razor controls' splat (same names,
    // one shared sequence), and the wrappers' component parameters (the C#
    // names). What it deliberately does NOT check is the VALUE — that a
    // parameter reaches the wire under the right name is this pin's claim;
    // that it carries the right value is BnComponentTests' and
    // BnFormControlTests'.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(LayoutItemComponents))]
    public void Component_EmitsEveryItemParameterItDeclares(Type component)
    {
        string[] emitted = RootAttributes(FramesForFullSurfaceMount(component))
            .Select(f => f.AttributeName)
            .ToArray();

        var missing = ItemNames
            .Where(n => !emitted.Contains(n.Wire) && !emitted.Contains(n.Parameter))
            .Select(n =>
                $"{component.Name} declares {n.Parameter} and never emits it: no element " +
                $"attribute '{n.Wire}' and no component parameter '{n.Parameter}' in its root " +
                "frame region, mounted with every parameter set to a non-null value.")
            .ToArray();

        Assert.Empty(missing);
    }

    [Theory]
    [MemberData(nameof(LayoutItemComponents))]
    public void Container_EmitsEveryContainerParameterItDeclares(Type component)
    {
        if (!typeof(BnLayoutContainer).IsAssignableFrom(component))
            return; // Not a container — it has no container surface to emit.

        string[] emitted = RootAttributes(FramesForFullSurfaceMount(component))
            .Select(f => f.AttributeName)
            .ToArray();

        var missing = ContainerNames
            .Where(n => !emitted.Contains(n.Wire) && !emitted.Contains(n.Parameter))
            .Select(n => $"{component.Name} declares {n.Parameter} and never emits it " +
                         $"(looked for '{n.Wire}' and '{n.Parameter}').")
            .ToArray();

        Assert.Empty(missing);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PIN 5 — the sequence BANDS, which until now were only asserted in prose.
    //
    // BnLayoutItem's remarks and the phase conclusion both call the bands
    // NORMATIVE and both name this file as their pin. They were half right:
    // Component_EmitsNoSequenceCollisionOutsideTheDocumentedSplat enforces
    // pairwise UNIQUENESS within a region, which is the substantive property,
    // but says nothing about MEMBERSHIP — an author who moved the item
    // surface to 300-316 and their own attributes to 1-2 would break no test
    // while making the documented layout a lie, and the next author to follow
    // the doc and start at 100 would collide.
    //
    // Scoped to the components whose BuildRenderTree is HAND-WRITTEN. The
    // four .razor controls are exempt for the structural reason the design
    // already records: the Razor compiler assigns every sequence number in a
    // generated BuildRenderTree, so there is no band for their author to keep
    // to and nothing here to enforce.
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(LayoutItemComponents))]
    public void HandWrittenEmitter_KeepsEachAttributeInItsDeclaredBand(Type component)
    {
        if (SplatEmitters.Contains(component))
            return; // Compiler-assigned sequences — see the note above.

        var offenders = new List<string>();

        foreach (RenderTreeFrame f in RootAttributes(FramesForFullSurfaceMount(component)))
        {
            string name = f.AttributeName;
            int seq = f.Sequence;

            (string Band, bool Ok) rule =
                ItemNames.Any(n => n.Wire == name || n.Parameter == name)
                    ? ("item 1-17", seq is >= 1 and <= 17)
                : ContainerNames.Any(n => n.Wire == name || n.Parameter == name)
                    ? ("container 50-99", seq is >= 50 and <= 99)
                : name == "ChildContent"
                    ? ("ChildContent 200", seq == 200)
                    : ("the component's own 100+", seq >= 100);

            if (!rule.Ok)
                offenders.Add($"{component.Name}: '{name}' is at sequence {seq}, outside {rule.Band}.");
        }

        Assert.Empty(offenders);
    }
}
