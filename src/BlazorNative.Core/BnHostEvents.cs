namespace BlazorNative.Core;

/// <summary>
/// The host-event names an app can subscribe to, so app code names an event
/// rather than spelling it.
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
