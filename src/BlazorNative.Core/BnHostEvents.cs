namespace BlazorNative.Core;

/// <summary>
/// The host-event names the shells send, so app code names an event rather than
/// spelling it. Two tiers: RESERVED names (<see cref="Back"/>, <see cref="Navigate"/>,
/// <see cref="SafeAreaChanged"/>) are intercepted by the runtime and never reach
/// subscribers; the rest are the names an app can actually subscribe to. See each
/// constant's remarks for which tier it is in.
/// </summary>
/// <remarks>
/// <para>HAND-WRITTEN AND PINNED, NOT GENERATED. This is public API with a
/// PublicAPI baseline; a generator able to rewrite it is a generator able to move
/// a frozen surface. <c>TheHostEventConstants_MatchTheManifest_BothWays</c> asserts
/// it against <c>src/wire-vocabulary.json</c> in both directions instead — the same
/// trade <c>BlazorNativeNodeType</c> makes.</para>
/// <para>Adding a name here alone does nothing: the shells send what the manifest
/// says. Add it to the manifest, regenerate, then add the constant.</para>
/// </remarks>
public static class BnHostEvents
{
    /// <summary>The user pressed system back. RESERVED — the runtime intercepts this
    /// and routes it to the navigation manager; it does not reach subscribers.</summary>
    public const string Back = "back";

    /// <summary>A deep link or notification tap asks for a route, carried as the
    /// payload. RESERVED — the runtime intercepts this and navigates.</summary>
    public const string Navigate = "navigate";

    /// <summary>The area of the display obscured by system UI changed — rotation, a
    /// keyboard, a call banner. RESERVED — the runtime intercepts this, stores it as
    /// <see cref="BnSafeAreaInsets.Current"/>, and re-renders a live session; it does
    /// not reach subscribers. The payload is flat JSON with string-valued numbers:
    /// <c>{"top":"47","right":"0","bottom":"34","left":"0"}</c> — all four edge keys
    /// are required.</summary>
    public const string SafeAreaChanged = "safeAreaChanged";

    /// <summary>The app returned to the foreground. Android <c>onResume</c>,
    /// iOS <c>didBecomeActive</c>.</summary>
    public const string OnResume = "onResume";

    /// <summary>The app left the foreground. Android <c>onPause</c>, iOS
    /// <c>willResignActive</c> — which fires on partial obscuring too, matching
    /// Android's semantics rather than <c>didEnterBackground</c>'s.
    /// <para>PERSISTENCE BELONGS HERE. <see cref="OnDestroy"/> is best-effort on both
    /// platforms and weaker on iOS, which kills suspended apps without it.</para></summary>
    public const string OnPause = "onPause";

    /// <summary>The app is terminating. BEST-EFFORT and NOT GUARANTEED — iOS kills
    /// suspended apps without firing it. Save on <see cref="OnPause"/> instead.</summary>
    public const string OnDestroy = "onDestroy";
}
