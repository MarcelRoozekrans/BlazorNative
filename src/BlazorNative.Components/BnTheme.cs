namespace BlazorNative.Components;

/// <summary>
/// The cascaded demo theme (Phase 3.4 design §4). A record on purpose:
/// toggling produces a NEW instance, so <c>CascadingValue&lt;BnTheme&gt;</c>
/// notifies its consumers and they re-render (DoD #6's mechanism).
/// </summary>
/// <param name="Background">The current background for themed surfaces.</param>
/// <param name="AltBackground">The background the next toggle swaps in.</param>
public sealed record BnTheme(string Background, string AltBackground);
