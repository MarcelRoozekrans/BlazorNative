using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
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
//
// ItemNames and ContainerNames below are a THIRD hand copy of the layout
// surface, after BnLayoutItem/BnLayoutContainer themselves and
// LayoutSurfacePinTests' ItemParameters/ContainerParameters. The emission pins
// read this copy, not the declaration, so TheEmissionRosters_AreExactlyTheDeclaredSurface
// holds its parameter half to the declared surface in both directions.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//
// - THE WIRE HALF OF THE ROSTERS IS NOT PINNED HERE. The roster fact compares
//   parameter names only. A wrong wire spelling in ItemNames or ContainerNames
//   fails SAFE in the emission and band pins: the emission pins would miss the
//   frame and red, and the band pin would file the attribute under the
//   component's own 100+ band and red. It is not a false-green channel, but the
//   wire names' agreement with the shells is src/wire-vocabulary.json's job.
//
// - ONE MOUNT, SAMPLE VALUES ONLY. Each component is mounted once with
//   SampleValue's representative values. A parameter SampleValue leaves unset,
//   such as a bool, int, EventCallback or RenderFragment, emits no element
//   attribute, so a collision or band error on THAT attribute is never seen.
//   Nothing re-renders, so a collision that appears only on a second render is
//   out of reach too.
//
// - ROOT-CONTIGUOUS RUNS ONLY. The emission and band pins read RootAttributes:
//   the attribute frames that immediately follow the FIRST Element or
//   Component frame. An item attribute emitted on a nested element, or after a
//   non-attribute frame, is invisible to them. The collision pin walks every
//   region, but only the contiguous attribute run after each opening frame.
//
// - BnList<> AND BnModal ARE OUTSIDE. The rows are LayoutItemComponents, the
//   exported components deriving from BnLayoutItem, so the two argued
//   exceptions in LayoutSurfacePinTests.AllowedNonLayoutComponents are never
//   mounted here.
//
// - PRESENCE, NOT VALUE, AND EITHER SPELLING. PIN 4 accepts a parameter under
//   its wire name OR its parameter name, and never checks the value.
//
// - THE EXEMPTIONS. The collision pin excuses a shared sequence in the four
//   RazorEmitters when every name in the group is an item wire name, and the
//   band pin skips those four entirely. The container emission pin skips every
//   non-container row, and TheContainerRows_AreThereToBeChecked floors the rows
//   it does check.
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
        ("Padding",       "padding"),
        ("Justify",       "justifyContent"),
        ("Align",         "alignItems"),
        ("Wrap",          "flexWrap"),
        ("Gap",           "gap"),
        ("PaddingTop",    "paddingTop"),
        ("PaddingRight",  "paddingRight"),
        ("PaddingBottom", "paddingBottom"),
        ("PaddingLeft",   "paddingLeft"),
    };

    /// <summary>ItemNames/ContainerNames are this file's hand copy of the layout
    /// surface. The emission facts read THEM, not the declaration, so a parameter
    /// added to BnLayoutItem and LayoutSurfacePinTests.ItemParameters but not here
    /// would never be checked for emission. This holds the copy to the declared
    /// surface in both directions. ItemParameters and ContainerParameters are
    /// themselves held to the declaration by LayoutSurfacePinTests, so this chains
    /// the third copy to the source.</summary>
    [Fact]
    public void TheEmissionRosters_AreExactlyTheDeclaredSurface()
    {
        Assert.Equal(
            LayoutSurfacePinTests.ItemParameters.OrderBy(n => n, StringComparer.Ordinal),
            ItemNames.Select(p => p.Parameter).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(
            LayoutSurfacePinTests.ContainerParameters.OrderBy(n => n, StringComparer.Ordinal),
            ContainerNames.Select(p => p.Parameter).OrderBy(n => n, StringComparer.Ordinal));
    }

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
        foreach (Type t in LayoutItemTypes())
            data.Add(t);
        return data;
    }

    /// <summary>The rows of <see cref="LayoutItemComponents"/> as types, so a
    /// floor can count the same population the theories run over.</summary>
    private static Type[] LayoutItemTypes()
        => typeof(BnLayoutItem).Assembly
            .GetExportedTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t))
            .Where(t => !t.IsAbstract)
            .Where(t => typeof(BnLayoutItem).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

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
    /// string/float/enum/length-typed parameter (which is what the whole item and
    /// container surface, and most components' own optional attributes, are
    /// typed as) needs a real value for its frame to exist to collide.</para>
    /// </summary>
    private static object? SampleValue(Type propertyType)
    {
        Type t = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (t == typeof(string)) return "x";
        if (t == typeof(float)) return 1f;
        // Phase 13.1: twelve of the twenty-two shared parameters are these two
        // types now. Without these two arms they would fall through to `null`
        // below and be left UNSET — and an unset element attribute is never
        // appended to the frame array at all, so more than half the surface this
        // pin exists to check would silently stop being checked, with the pin
        // still green. That is the failure mode the paragraph above describes.
        if (t == typeof(BnLength)) return (BnLength)1f;
        if (t == typeof(BnAutoLength)) return (BnAutoLength)1f;
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
        (List<string> offenders, int attributeRegions) = SequenceCollisions(
            component.Name, FramesForFullSurfaceMount(component), SplatEmitters.Contains(component));

        // Rule 2, per row: every layout component carries the item surface on
        // its root, so a mount with every parameter set yields at least one
        // region with attributes. Zero regions means the walk saw nothing, and
        // the Assert.Empty below would pass over it.
        Assert.True(attributeRegions >= 1,
            $"{component.Name}: the collision walk found no attribute region in its frames, " +
            "so it checked nothing. Either the mount stopped emitting or the walk stopped seeing it.");

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The positive control for <see cref="Component_EmitsNoSequenceCollisionOutsideTheDocumentedSplat"/>.
    /// Synthetic frames, built with Blazor's own <see cref="RenderTreeBuilder"/>, are
    /// fed through the SAME <see cref="SequenceCollisions"/> the pin calls. The
    /// planted collision is the 13.0 mutation table's shape: <c>left</c> misfiled
    /// onto 100, where it collides with a component's own <c>fontSize</c>.
    /// </summary>
    [Fact]
    public void TheCollisionDetector_ReportsAPlantedCollision_AndExcusesOnlyTheDocumentedSplat()
    {
        RenderTreeFrame[] planted = Synthetic(b =>
        {
            b.OpenElement(0, "text");
            b.AddAttribute(100, "fontSize", "x");
            b.AddAttribute(100, "left", "x");
            b.CloseElement();
        });

        (List<string> offenders, int regions) = SequenceCollisions("Planted", planted, isSplatEmitter: false);
        Assert.Equal(1, regions);
        Assert.Equal(new[] { "Planted: sequence 100 shared by [fontSize, left]" }, offenders);

        // A splat emitter does not excuse it either: fontSize is not an item wire name.
        Assert.Single(SequenceCollisions("Planted", planted, isSplatEmitter: true).Offenders);

        // The documented splat: only item wire names share the sequence. Excused
        // in a splat emitter, and reported anywhere else.
        RenderTreeFrame[] splat = Synthetic(b =>
        {
            b.OpenElement(0, "checkbox");
            b.AddAttribute(1, "margin", "x");
            b.AddAttribute(1, "left", "x");
            b.CloseElement();
        });
        Assert.Empty(SequenceCollisions("Splat", splat, isSplatEmitter: true).Offenders);
        Assert.Single(SequenceCollisions("Splat", splat, isSplatEmitter: false).Offenders);

        // The bucketing: the same sequence in two DIFFERENT regions is not a collision.
        RenderTreeFrame[] twoRegions = Synthetic(b =>
        {
            b.OpenElement(0, "view");
            b.AddAttribute(100, "a", "x");
            b.OpenElement(1, "text");
            b.AddAttribute(100, "b", "x");
            b.CloseElement();
            b.CloseElement();
        });
        (List<string> bucketed, int bucketedRegions) = SequenceCollisions("TwoRegions", twoRegions, isSplatEmitter: false);
        Assert.Equal(2, bucketedRegions);
        Assert.Empty(bucketed);
    }

    /// <summary>Frames built by Blazor's own <see cref="RenderTreeBuilder"/>, for the
    /// controls: the same frame shape a component's BuildRenderTree produces.</summary>
    private static RenderTreeFrame[] Synthetic(Action<RenderTreeBuilder> build)
    {
        using var builder = new RenderTreeBuilder();
        build(builder);
        ArrayRange<RenderTreeFrame> range = builder.GetFrames();
        return range.Array.Take(range.Count).ToArray();
    }

    /// <summary>The collision detector, shared by the pin and its control. Returns
    /// every shared sequence outside the documented splat, and how many regions
    /// carried at least one attribute, which is what the pin floors.</summary>
    internal static (List<string> Offenders, int AttributeRegions) SequenceCollisions(
        string componentName, RenderTreeFrame[] frames, bool isSplatEmitter)
    {
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
        int attributeRegions = 0;
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

            if (j > regionStart)
                attributeRegions++;

            var duplicateGroups = frames[regionStart..j]
                .GroupBy(f => f.Sequence)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicateGroups)
            {
                string[] names = group.Select(f => f.AttributeName).ToArray();
                bool isDocumentedSplat = isSplatEmitter && names.All(n => ItemWireNames.Contains(n));

                if (!isDocumentedSplat)
                    offenders.Add($"{componentName}: sequence {group.Key} shared by [{string.Join(", ", names)}]");
            }

            i = j;
        }

        return (offenders, attributeRegions);
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
        if (!IsContainerRow(component))
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

    /// <summary>Which rows <see cref="Container_EmitsEveryContainerParameterItDeclares"/>
    /// actually checks. Shared with its floor, so the floor counts the rows the
    /// fact really checks rather than a restatement of the filter.</summary>
    private static bool IsContainerRow(Type component)
        => typeof(BnLayoutContainer).IsAssignableFrom(component);

    /// <summary>
    /// Rule 2 for <see cref="Container_EmitsEveryContainerParameterItDeclares"/>. That
    /// fact returns early for every non-container row, so a filter that matched
    /// nothing would pass every row while checking none. Measured at 4 container
    /// rows on 2026-09-26 (BnColumn, BnRow, BnSafeArea and BnView), floored at
    /// exactly that with no headroom, with BnView as the named anchor.
    /// </summary>
    [Fact]
    public void TheContainerRows_AreThereToBeChecked()
    {
        Type[] containerRows = LayoutItemTypes()
            .Where(IsContainerRow)
            .ToArray();

        Assert.True(containerRows.Length >= 4,
            $"only {containerRows.Length} of the layout rows are containers, and there are 4. " +
            "The container emission pin returns early for the rest, so it is checking too little.");
        Assert.Contains(typeof(BnView), containerRows);
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

        RenderTreeFrame[] rootAttributes = RootAttributes(FramesForFullSurfaceMount(component));

        // Rule 2, per row: a component that declares any parameter, mounted with
        // every parameter set, has a non-empty root attribute run. An empty run
        // would make the band check below pass while checking nothing.
        bool declaresAnyParameter = component
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
            .Any(p => p.GetCustomAttribute<ParameterAttribute>() is not null);
        if (declaresAnyParameter)
            Assert.True(rootAttributes.Length > 0,
                $"{component.Name} declares parameters, but its root attribute run is empty, so " +
                "the band check saw nothing. The mount or RootAttributes has stopped seeing the surface.");

        Assert.Empty(BandOffenders(component.Name, rootAttributes));
    }

    /// <summary>
    /// The positive control for <see cref="HandWrittenEmitter_KeepsEachAttributeInItsDeclaredBand"/>.
    /// Synthetic frames go through the SAME <see cref="RootAttributes"/> and
    /// <see cref="BandOffenders"/> the pin calls. The planted errors are the 13.0
    /// conclusion's recorded mutation, <c>fontSize</c> moved from 100 to 18, and
    /// the collision table's <c>left</c> misfiled onto 100. The in-band
    /// <c>margin</c> and <c>padding</c> must not be reported.
    /// </summary>
    [Fact]
    public void TheBandDetector_ReportsOutOfBandAttributes_AndOnlyThose()
    {
        RenderTreeFrame[] planted = Synthetic(b =>
        {
            b.OpenElement(0, "text");
            b.AddAttribute(1, "margin", "x");
            b.AddAttribute(18, "fontSize", "x");
            b.AddAttribute(50, "padding", "x");
            b.AddAttribute(100, "left", "x");
            b.CloseElement();
        });

        Assert.Equal(
            new[]
            {
                "Planted: 'fontSize' is at sequence 18, outside the component's own 100+.",
                "Planted: 'left' is at sequence 100, outside item 1-17.",
            },
            BandOffenders("Planted", RootAttributes(planted)));
    }

    /// <summary>The band detector, shared by the pin and its control.</summary>
    internal static List<string> BandOffenders(string componentName, RenderTreeFrame[] rootAttributes)
    {
        var offenders = new List<string>();

        foreach (RenderTreeFrame f in rootAttributes)
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
                offenders.Add($"{componentName}: '{name}' is at sequence {seq}, outside {rule.Band}.");
        }

        return offenders;
    }
}
