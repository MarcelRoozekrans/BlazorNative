using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// HandlerIdNarrowingTests — Phase 10.2 Gate A (#125.1).
//
// THE ASYMMETRY THIS PINS: Blazor's event-handler id is a ulong; the AttachEvent
// wire and the renderer's handler table are int-indexed. The dispatch side
// (Exports.DispatchEventCore) already rejects a handlerId > int.MaxValue as
// malformed BEFORE its own (int) cast — but attach used to narrow with a bare
// (int), silently truncating an out-of-range id (wrapping negative or aliasing
// onto a LIVE handler). NativeRenderer.NarrowHandlerId makes the two sides agree:
// attach now FAILS LOUD at the exact boundary dispatch rejects.
//
// The condition is unreachable from a real frame (handler ids count from 1 and
// reaching 2^31 needs >2e9 attaches in one session), so this is the guard's only
// witness — a direct unit test, the "mutation proves the guard bites" shape the
// phase asks for.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class HandlerIdNarrowingTests
{
    [Theory]
    [InlineData(0ul, 0)]
    [InlineData(1ul, 1)]
    [InlineData((ulong)int.MaxValue, int.MaxValue)] // the exact upper bound is legal
    public void NarrowHandlerId_InRange_ReturnsTheSameValue(ulong handlerId, int expected)
        => Assert.Equal(expected, NativeRenderer.NarrowHandlerId(handlerId));

    [Theory]
    [InlineData((ulong)int.MaxValue + 1)]           // one past the boundary
    [InlineData(uint.MaxValue)]                      // would truncate to -1
    [InlineData(ulong.MaxValue)]
    public void NarrowHandlerId_PastIntMaxValue_ThrowsLoud_NotSilentlyTruncated(ulong handlerId)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => NativeRenderer.NarrowHandlerId(handlerId));
        Assert.Contains("int.MaxValue", ex.Message);
    }
}
