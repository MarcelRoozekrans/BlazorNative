// GENERATED FILE — DO NOT EDIT (#255).
//
// Source of truth: src/wire-vocabulary.json
// Regenerate:      dotnet run --project tools/BlazorNative.WireGen
//
// These names used to be hand-maintained in four languages, agreeing only
// because a drift test parsed them back out and said so. Editing this file
// by hand puts that back: WireVocabularyCodegenTests re-runs the emitter and
// byte-compares, so the edit fails the required build-test lane rather than
// reaching a device.

namespace BlazorNative.Renderer;

/// <summary>The wire vocabulary, generated from the manifest. The DATA only:
/// <see cref="NativeRenderer"/> keeps the sets, the comparer choice and the prose
/// that explains the partition — this file exists so those names cannot disagree
/// with the two shells'.</summary>
internal static class BnWireVocabulary
{
    internal static readonly string[] YogaStyles =
    [
        // Container
        "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap",
        // Item
        "alignSelf", "flexGrow", "flexShrink", "flexBasis",
        // Box
        "width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight", "padding", "margin",
        // Positioning
        "position", "top", "right", "bottom", "left",
    ];

    internal static readonly string[] VisualStyles =
    [
        // Visual
        "backgroundColor", "color", "fontSize",
    ];

    internal static readonly string[] ScrollIgnoredContainerStyles =
    [
        "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap", "padding",
    ];

    internal static readonly string[] MeasuredNodeTypes =
    [
        "text", "button", "input", "image", "checkbox", "switch", "slider", "picker", "activityindicator",
    ];

    /// <summary>The shells' ordinal node-type array — index IS the wire id.</summary>
    internal static readonly string[] NodeTypeNames =
    [
        "?", // 0 = None
        "view", // 1 = View
        "text", // 2 = Text
        "button", // 3 = Button
        "input", // 4 = Input
        "image", // 5 = Image
        "scroll", // 6 = Scroll
        "picker", // 7 = Picker
        "checkbox", // 8 = Checkbox
        "switch", // 9 = Switch
        "slider", // 10 = Slider
        "modal", // 11 = Modal
        "activityindicator", // 12 = ActivityIndicator
    ];
}
