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

    /// <summary>The wire names <see cref="BnLayoutItem.EmitItemAttributes"/> and
    /// <see cref="BnLayoutItem.ItemAttributes"/> both write — the CAMEL-CASE
    /// wire form, not the C# parameter names in
    /// <see cref="LayoutSurfacePinTests.ItemParameters"/>. This is what a
    /// legitimate splat collision looks like: every name in the shared-
    /// sequence group is one of these seventeen.</summary>
    private static readonly string[] ItemWireNames =
    {
        "backgroundColor", "margin", "alignSelf", "flexGrow", "flexShrink", "flexBasis",
        "width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight",
        "position", "top", "right", "bottom", "left",
    };

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

    [Theory]
    [MemberData(nameof(LayoutItemComponents))]
    public void Component_EmitsNoSequenceCollisionOutsideTheDocumentedSplat(Type component)
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
        RenderTreeFrame[] frames = range.Array.Take(range.Count).ToArray();

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
}
