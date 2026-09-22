namespace BlazorNative.Core;

/// <summary>The area of the display obscured by system UI — notches, camera cutouts,
/// status bars, home indicators and rounded corners — reported by the shell in its
/// own layout units.</summary>
/// <remarks>
/// <para>HAND-WRITTEN, like <see cref="BnHostEvents"/>: this is public API with a
/// PublicAPI baseline, and a generator able to rewrite a frozen surface is worse than
/// the drift it prevents.</para>
/// <para>The values change at runtime — rotation, a keyboard appearing, a call banner —
/// so treat them as current rather than constant.</para>
/// </remarks>
public readonly record struct BnSafeAreaInsets(double Top, double Right, double Bottom, double Left)
{
    /// <summary>No obscured area on any edge. Also the value before the first shell
    /// report arrives, which is why the first frame of a run renders un-inset.</summary>
    public static readonly BnSafeAreaInsets Zero = new(0, 0, 0, 0);

    private static BnSafeAreaInsets s_current = Zero;

    /// <summary>The most recently reported insets — <see cref="Zero"/> until the first
    /// <c>safeAreaChanged</c> host event arrives, and reset to <see cref="Zero"/> by
    /// <see cref="ResetForTests"/>.</summary>
    /// <remarks>FRAMEWORK-OWNED: the reserved <c>safeAreaChanged</c> host-event arm
    /// (<c>Exports.DispatchHostSafeArea</c>, <c>BlazorNative.Runtime</c>) is the only
    /// production writer, via <see cref="Report"/>. There is no
    /// <c>InternalsVisibleTo</c> from this package to Runtime — Runtime calls the same
    /// public <see cref="Report"/> method app code could call, the same trade
    /// <see cref="BnLog.Level"/> makes for the same reason (see that type's remarks):
    /// a public, honestly-named seam beats an InternalsVisibleTo web.</remarks>
    public static BnSafeAreaInsets Current => s_current;

    /// <summary>Raised by <see cref="Report"/> every time the shell reports new
    /// insets, synchronously on the calling thread — even when the reported value
    /// equals the previous one. A subscribing component calls its own
    /// <c>StateHasChanged()</c> here (a later phase); this type only republishes,
    /// it never renders anything itself.</summary>
    public static event Action<BnSafeAreaInsets>? Changed;

    /// <summary>Stores <paramref name="insets"/> as <see cref="Current"/> and raises
    /// <see cref="Changed"/> for every subscriber, with per-subscriber isolation — a
    /// throwing subscriber is stderr-logged and the remaining subscribers still run,
    /// the same posture <c>NativeNavigationManager.RaiseRouteChanged</c> uses for
    /// <see cref="INavigationManager.RouteChanged"/>. Called by the runtime's
    /// <c>safeAreaChanged</c> host-event arm on every valid payload — including when
    /// no session is mounted, so a later mount starts with the right values instead
    /// of rendering at zero a second time.</summary>
    public static void Report(BnSafeAreaInsets insets)
    {
        s_current = insets;
        if (Changed is not { } subscribers)
            return;

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action<BnSafeAreaInsets>)subscriber)(insets);
            }
            catch (Exception ex)
            {
                // ex.ToString() (via BnLog.Error's overload): the subscriber is app
                // code — keep its stack, same as RaiseRouteChanged.
                BnLog.Error("BnSafeAreaInsets", "Changed subscriber threw", ex);
            }
        }
    }

    /// <summary>Test-only: resets <see cref="Current"/> to <see cref="Zero"/> and
    /// clears every <see cref="Changed"/> subscriber, so tests don't leak state or
    /// handlers across runs — the same posture as
    /// <c>HostSession.ResetForTests</c>/<c>NativeShellBridge.ResetForTests</c>.
    /// The production ABI never calls this.</summary>
    public static void ResetForTests()
    {
        s_current = Zero;
        Changed = null;
    }

    /// <summary>Test-only: places a known <see cref="Current"/> value WITHOUT
    /// raising <see cref="Changed"/> — for seeding state before a session mounts
    /// (e.g. so a component's first render already sees the right insets) without
    /// fabricating a <c>safeAreaChanged</c> payload. Production code must call
    /// <see cref="Report"/> instead: that is the path that notifies subscribers.</summary>
    public static void SetForTests(BnSafeAreaInsets insets) => s_current = insets;
}
