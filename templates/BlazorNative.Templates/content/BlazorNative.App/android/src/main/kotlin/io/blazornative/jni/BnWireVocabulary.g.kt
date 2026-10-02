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
        "width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight", "padding", "paddingTop", "paddingRight", "paddingBottom", "paddingLeft", "margin",
        // Positioning
        "position", "top", "right", "bottom", "left",
    )

    internal val VISUAL_STYLES = setOf(
        // Visual
        "backgroundColor", "color", "fontSize",
    )

    internal val SCROLL_IGNORED_CONTAINER_STYLES = setOf(
        "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap", "padding", "paddingTop", "paddingRight", "paddingBottom", "paddingLeft",
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

/**
 * The host-event vocabulary. `dispatchHostEvent` takes THIS, not a String —
 * the enum member is how production code dispatches a host event, and a
 * source-scan test (NoProductionShellSource_CallsTheHostEventSeamsDirectly,
 * BlazorNative.Runtime.Tests) enforces that production code goes through it
 * rather than the raw-String test seams, so the Kotlin and Swift spellings
 * cannot drift the way they did before #300.
 */
internal enum class BnHostEvent(val wireName: String) {
    Back("back"), // reserved
    Navigate("navigate"), // reserved
    SafeAreaChanged("safeAreaChanged"), // reserved
    OnResume("onResume"), // passthrough
    OnPause("onPause"), // passthrough
    OnDestroy("onDestroy"), // passthrough
}

/**
 * The op integer of the ONE hostCallBegin slot: which capability a host call is
 * for. The integer IS the wire contract, and each id is frozen once shipped.
 * Hand-mirrored in three languages until Phase 16.1; now generated.
 */
object HostCallOp {
    /** Phase 9.0. The current position (mode request) or the read-only permission check (mode check). */
    const val GEOLOCATION = 0
    /** Phase 9.1. Schedule, show or cancel a local notification, or request or check the permission. */
    const val NOTIFICATIONS = 1
    /** Phase 9.2. An OS biometric prompt, or the read-only availability check. */
    const val BIOMETRICS = 2
    /** Phase 9.2. Set, get, get-with-auth or delete an encrypted-at-rest secret. */
    const val SECURE_STORAGE = 3
    /** Phase 9.3. Capture a photo, returned as a file path, or the read-only availability check. */
    const val CAMERA = 4
    /** Phase 16.1, #8. A handler faulted after it began a host call or a fetch, or after it yielded: never an rc. Args are flat JSON: handlerId (0 for a reserved host event), event, type and message; never a stack trace or an event payload, because either can carry user data. The shell routes it to onError and completes it OK with a null payload. Sent fire-and-forget: .NET ignores the result. */
    const val FAULT_NOTICE = 5
    /** Phase 16.2, #346. Whether .NET can go back. Args are flat JSON: canGoBack, the string true or false. Sent when the value changes and before every mount, even unchanged, since a later mount is a new shell such as a recreated Activity, and for a navigation BEFORE the frames that show the new page, so the shell can apply it in the same main-thread batch as that page. The shell enables or disables its back callback from it and completes it OK with a null payload. Sent fire-and-forget: .NET ignores the result. */
    const val BACK_STATE = 6
    /** Phase 16.2, #346. A back reached .NET and could not be handled, because it is at the root or has no session. Args are an empty flat JSON object. On Android the shell hands the press to the platform's default back, so it is never swallowed; iOS has no system back and ignores it. Either way the shell completes it OK with a null payload. Sent fire-and-forget: .NET ignores the result. */
    const val BACK_UNHANDLED = 7
}
