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

#ifndef BN_WIRE_VOCABULARY_G_H
#define BN_WIRE_VOCABULARY_G_H

static const char* const kYogaStyles[] = {
    "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap", "alignSelf", "flexGrow", "flexShrink", "flexBasis", "width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight", "padding", "margin", "position", "top", "right", "bottom", "left",
};

static const char* const kScrollIgnoredContainerStyles[] = {
    "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap", "padding",
};

// Index IS the wire id.
static const char* const kNodeTypes[] = {
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
};

#endif // BN_WIRE_VOCABULARY_G_H
