// Phase 5.0 iOS spike (rung 2) — throwaway scaffold.
// A hard reference edge to BlazorNative.Runtime so the assembly is unambiguously
// in the ILC compilation closure. The eight blazornative_* [UnmanagedCallersOnly]
// exports live in BlazorNative.Runtime.Exports and are exported from the produced
// NativeLib as native entry points; this type only guarantees the edge exists.
using System.Runtime.CompilerServices;

namespace BlazorNative.Spikes.IosAotProbe;

internal static class Probe
{
    // Referencing a Runtime type keeps the ProjectReference edge live even if a
    // future trimmer heuristic would otherwise drop an "unused" reference.
    internal static readonly RuntimeTypeHandle s_anchor =
        typeof(BlazorNative.Runtime.Exports).TypeHandle;
}
