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

/// The wire vocabulary, generated from the manifest. The DATA only — BnFrameAdapter
/// and BnWidgetMapper keep their own decode and routing code.
enum BnWireVocabulary {
    static let yogaStyles = [
        "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap", "alignSelf", "flexGrow", "flexShrink", "flexBasis", "width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight", "padding", "paddingTop", "paddingRight", "paddingBottom", "paddingLeft", "margin", "position", "top", "right", "bottom", "left",
    ]

    static let visualStyles = [
        "backgroundColor", "color", "fontSize",
    ]

    static let scrollIgnoredContainerStyles = [
        "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap", "padding", "paddingTop", "paddingRight", "paddingBottom", "paddingLeft",
    ]

    static let measuredNodeTypes = [
        "text", "button", "input", "image", "checkbox", "switch", "slider", "picker", "activityindicator",
    ]

    /// Index IS the wire id — decoded by indexing, so order is the contract.
    static let nodeTypes = [
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
    ]
}

/// The host-event vocabulary. `dispatchHostEvent` takes THIS, not a String —
/// passing an enum member is how production Swift code dispatches a host
/// event, and that parameter type is what stops a bare literal there. The
/// underlying C ABI (`blazornative_host_event`) is a separate, lower-level
/// door that stays directly callable regardless of this enum — BnNotifications
/// uses it for one reserved event today, deliberately, not as a bypass.
///
/// `.back` is present and unused on this shell: iOS has no system back. The
/// vocabulary is the union of what the WIRE admits, not what one shell sends.
/// Do not prune it to a per-shell subset — that is a divergence by another name.
enum BnHostEvent: String {
    case back = "back" // reserved
    case navigate = "navigate" // reserved
    case safeAreaChanged = "safeAreaChanged" // reserved
    case onResume = "onResume" // passthrough
    case onPause = "onPause" // passthrough
    case onDestroy = "onDestroy" // passthrough
}

/// The op integer of the ONE hostCallBegin slot: which capability a host call is
/// for. The integer IS the wire contract, and each id is frozen once shipped.
/// Hand-mirrored in three languages until Phase 16.1; now generated.
enum BnHostCallOp {
    /// Phase 9.0. The current position (mode request) or the read-only permission check (mode check).
    static let geolocation: Int32 = 0
    /// Phase 9.1. Schedule, show or cancel a local notification, or request or check the permission.
    static let notifications: Int32 = 1
    /// Phase 9.2. An OS biometric prompt, or the read-only availability check.
    static let biometrics: Int32 = 2
    /// Phase 9.2. Set, get, get-with-auth or delete an encrypted-at-rest secret.
    static let secureStorage: Int32 = 3
    /// Phase 9.3. Capture a photo, returned as a file path, or the read-only availability check.
    static let camera: Int32 = 4
    /// Phase 16.1, #8. A handler faulted after its first await, too late to be its dispatch rc 2. Args are flat JSON: handlerId (0 for a reserved host event), event, type and message; never a stack trace or an event payload, because either can carry user data. The shell routes it to onError and completes it OK with a null payload. Sent fire-and-forget: .NET ignores the result.
    static let faultNotice: Int32 = 5
    /// Phase 16.2, #346. Whether .NET can go back. Args are flat JSON: canGoBack, the string true or false. Sent when the value changes and before every mount, even unchanged, since a later mount is a new shell such as a recreated Activity, and for a navigation BEFORE the frames that show the new page, so the shell can apply it in the same main-thread batch as that page. The shell enables or disables its back callback from it and completes it OK with a null payload. Sent fire-and-forget: .NET ignores the result.
    static let backState: Int32 = 6
    /// Phase 16.2, #346. A back reached .NET and could not be handled, because it is at the root or has no session. Args are an empty flat JSON object. The shell finishes, so a back press is never swallowed, and completes it OK with a null payload. Sent fire-and-forget: .NET ignores the result.
    static let backUnhandled: Int32 = 7
}
