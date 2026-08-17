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

package io.blazornative.jni

/**
 * The wire vocabulary, generated from the manifest. The DATA only — the shells
 * keep their own routing code and the prose that explains it; this object exists
 * so their names cannot disagree with .NET's or with each other's.
 */
internal object BnWireVocabulary {
    internal val YOGA_STYLES = setOf(
        // Container
        "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap",
        // Item
        "alignSelf", "flexGrow", "flexShrink", "flexBasis",
        // Box
        "width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight", "padding", "margin",
        // Positioning
        "position", "top", "right", "bottom", "left",
    )

    internal val VISUAL_STYLES = setOf(
        // Visual
        "backgroundColor", "color", "fontSize",
    )

    internal val SCROLL_IGNORED_CONTAINER_STYLES = setOf(
        "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap", "padding",
    )

    internal val MEASURED_NODE_TYPES = setOf(
        "text", "button", "input", "image", "checkbox", "switch", "slider", "picker", "activityindicator",
    )

    /** Index IS the wire id — decoded by indexing, so order is the contract. */
    internal val NODE_TYPES = arrayOf(
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
    )
}
